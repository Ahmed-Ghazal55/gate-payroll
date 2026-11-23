using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-11: money-schema platform invoice upload/approve (Keeta first).
/// Account lines feed sal.distribution; partner-total deductions (VAT, partner penalty) stay on the company.</summary>
public static class SalPlatformInvoice
{
    public const string ManagePermission="pay.manage",ApprovePermission="pay.approve";
    public const byte StatusDraft=1,StatusApproved=2,StatusPosted=3,StatusCancelled=4,StatusReversed=5;
    public const string PartnerVat="partner_vat",PartnerPenalty="partner_penalty";

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaSalInvoice.Ensure(c,tx);SchemaSalDistribution.Ensure(c,tx);SchemaOpsPlatformAccount.Ensure(c,tx);
    }

    public static JsonObject Upload(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        string app=PayPlatforms.App(input.S("app",input.S("appCode","keeta")));
        // U-W2-11 Keeta first; Ninja October ops read Omar live (M-0692) — no our Ninja upload on this path. HS uses HungerStationInvoices.
        J.Need(app=="keeta","رفع فاتورة مسار المال متاح لكيتا أولًا؛ نينجا من قراءة عمر المباشرة.",400);
        string company=input.Required("companyId");
        FinancialScopes.DemandSponsor(actor,company);
        string city=input.Required("cityCode");
        string from=Day(input.Required("periodFrom")),to=Day(input.Required("periodTo"));
        string month=Month(from);
        J.Need(string.CompareOrdinal(from,to)<=0,"فترة الفاتورة غير صالحة.",400);
        string cr=input.S("erpCrId");if(cr=="")cr=SchemaSql.GuidText(SchemaSql.NewId());
        string postingOwner=input.S("postingOwner","invoice");
        J.Need(postingOwner is "invoice" or "agent_settlement","مالك الترحيل غير صالح.",400);
        var lines=(input["lines"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(lines.Count>0,"أدخل بنود الحسابات.",400);
        var partner=(input["partnerTotals"] as JsonObject)??new JsonObject();
        decimal partnerVat=J.Money(partner.N("vat")),partnerPenalty=J.Money(partner.N("partnerPenalty",partner.N("belowTargetPenalty")));
        J.Need(partnerVat>=0&&partnerPenalty>=0,"إجماليات الشريك لا تكون سالبة.",400);

        decimal lineGross=0,lineDed=0,lineNet=0;
        var accountKeys=new List<(string AccountId,string AccountKey)>();
        int row=0;
        foreach(var line in lines)
        {
            row++;
            string account=line.Required("erpPlatformAccountId");
            string key=line.S("accountKey");if(key=="")key=account;
            PlatformAccountOwnership.DemandOwnerMatches(c,tx,account,month,company);
            accountKeys.Add((account,key));
            decimal gLine=J.Money(line.N("gross")),dLine=J.Money(line.N("deductions")),nLine=J.Money(line.ContainsKey("net")?line.N("net"):gLine-dLine);
            J.Need(gLine>=0&&dLine>=0&&nLine>=0&&Math.Abs(nLine-(gLine-dLine))<0.005m,"صافي بند الفاتورة لا يطابق الإجمالي ناقص الخصم.",400);
            lineGross+=gLine;lineDed+=dLine;lineNet+=nLine;
            _=line.N("orders");
        }
        // Partner VAT / partner penalty are company totals — never rider deductions (X6).
        decimal gross=J.Money(lineGross+partnerVat+partnerPenalty);
        decimal deductions=J.Money(lineDed+partnerVat+partnerPenalty);
        decimal net=J.Money(gross-deductions);
        J.Need(Math.Abs(net-lineNet)<0.005m,"صافي البنود لا يطابق صافي الفاتورة بعد بنود الشركة.",400);

        byte[] hash=input["fileHash"] is JsonValue jv&&jv.TryGetValue<string>(out var hex)&&hex.Length==64
            ?Convert.FromHexString(hex):SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(company+":"+app+":"+from+":"+to+":"+gross+":"+deductions));
        J.Need(hash.Length==32,"بصمة الملف يجب أن تكون 32 بايت.",400);

        string invoiceId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string by=actor.Identity!.Name!;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","platform_invoice")}
            (id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,status,uploaded_by,uploaded_at,posting_owner,created_by,created_at)
            VALUES(@id,@app,@c,@cr,@city,@from,@to,@pm,@hash,@g,@d,@n,@st,@by,@at,@owner,@by,@at)
            """,("@id",invoiceId),("@app",app),("@c",company),("@cr",cr),("@city",city),
            ("@from",from),("@to",to),("@pm",month),("@hash",hash),
            ("@g",gross),("@d",deductions),("@n",net),("@st",StatusDraft),
            ("@by",by),("@at",now),("@owner",postingOwner));

        var ownerSnap=PlatformAccountOwnership.SnapshotOwners(c,tx,month,accountKeys,company);
        var savedLines=new JsonArray();
        int sourceRow=1;
        foreach(var line in lines)
        {
            string lineId=SchemaSql.GuidText(SchemaSql.NewId());
            string account=line.Required("erpPlatformAccountId");
            string key=line.S("accountKey");if(key=="")key=account;
            decimal grossL=J.Money(line.N("gross")),dedL=J.Money(line.N("deductions")),netL=J.Money(line.ContainsKey("net")?line.N("net"):grossL-dedL);
            int orders=(int)line.N("orders");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","platform_invoice_line")}
                (id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders,created_by,created_at)
                VALUES(@id,@inv,@pm,@row,@key,@acc,@g,@d,@n,@o,@by,@at)
                """,("@id",lineId),("@inv",invoiceId),("@pm",month),("@row",sourceRow),("@key",key),
                ("@acc",account),("@g",grossL),("@d",dedL),("@n",netL),("@o",orders),("@by",by),("@at",now));
            foreach(var item in (line["items"] as JsonArray??[]).OfType<JsonObject>())
                InsertItem(c,tx,lineId,month,item.Required("itemCode"),J.Money(item.N("amount")),by,now,companyBorne:false);
            savedLines.Add(new JsonObject{["id"]=lineId,["erpPlatformAccountId"]=account,["accountKey"]=key,["gross"]=grossL,["deductions"]=dedL,["net"]=netL,["orders"]=orders,["sourceRow"]=sourceRow});
            sourceRow++;
        }
        // Company-only partner totals (never rider).
        if(partnerVat>0||partnerPenalty>0)
        {
            string companyLine=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","platform_invoice_line")}
                (id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders,created_by,created_at)
                VALUES(@id,@inv,@pm,@row,'partner_total',@acc,@g,@d,0,0,@by,@at)
                """,("@id",companyLine),("@inv",invoiceId),("@pm",month),("@row",sourceRow),
                ("@acc",SchemaSql.GuidText(SchemaSql.NewId())),("@g",J.Money(partnerVat+partnerPenalty)),
                ("@d",J.Money(partnerVat+partnerPenalty)),("@by",by),("@at",now));
            if(partnerVat>0)InsertItem(c,tx,companyLine,month,PartnerVat,partnerVat,by,now,companyBorne:true);
            if(partnerPenalty>0)InsertItem(c,tx,companyLine,month,PartnerPenalty,partnerPenalty,by,now,companyBorne:true);
        }

        Store.Audit(c,tx,"sal.platform_invoice_uploaded",invoiceId,by);
        return new()
        {
            ["id"]=invoiceId,["app"]=app,["companyId"]=company,["cityCode"]=city,
            ["periodFrom"]=from,["periodTo"]=to,["periodMonth"]=month,
            ["gross"]=gross,["deductions"]=deductions,["net"]=net,["status"]=StatusDraft,
            ["ownerSnapshot"]=ownerSnap,["lines"]=savedLines,
            ["partnerVat"]=partnerVat,["partnerPenalty"]=partnerPenalty,
            ["allowedActions"]=new JsonArray("approve_invoice")
        };
    }

    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string invoiceId,JsonObject? input=null)
    {
        AccessPolicy.Demand(actor,ApprovePermission);
        Ensure(c,tx);
        var invoice=Load(c,tx,invoiceId);
        FinancialScopes.DemandSponsor(actor,invoice.S("companyId"));
        J.Need((byte)invoice.N("status")==StatusDraft,"الفاتورة ليست مسودة للاعتماد.",409);
        string by=actor.Identity!.Name!;
        string uploadedBy=invoice.S("uploadedBy");
        if(uploadedBy!="")J.Need(AccessPolicy.Independent(actor,uploadedBy,"platform.invoice_approve",c,tx,invoice,scope:AccessPolicy.ScopeInvoiceApprovals),"يلزم مراجع مستقل.",403);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        string month=invoice.S("periodMonth");

        // Re-check ownership at approve (period map).
        var lines=LoadLines(c,tx,invoiceId);
        foreach(var line in lines.Where(l=>l.S("accountKey")!="partner_total"))
            PlatformAccountOwnership.DemandOwnerMatches(c,tx,line.S("erpPlatformAccountId"),month,invoice.S("companyId"));

        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","platform_invoice")}
            SET status=@st,approved_by=@by,approved_at=@at,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@st",StatusApproved),("@by",by),("@at",now),("@id",invoiceId));

        // Feed sal.distribution from account lines (fixes frozen evidence for Keeta runs).
        var workers=(input?["workers"] as JsonArray??[]).OfType<JsonObject>()
            .GroupBy(w=>w.S("erpPlatformAccountId"),StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.Ordinal);
        SchemaSql.Exec(c,tx,$"DELETE FROM {SchemaSql.Q(c,"sal","distribution")} WHERE invoice_id=@id",("@id",invoiceId));
        var dist=new JsonArray();
        foreach(var line in lines.Where(l=>l.S("accountKey")!="partner_total"))
        {
            string account=line.S("erpPlatformAccountId");
            decimal share=J.Money(line.N("net"));
            if(workers.TryGetValue(account,out var list)&&list.Count>0)
            {
                decimal kept=0;
                for(int i=0;i<list.Count;i++)
                {
                    decimal part=i==list.Count-1?J.Money(share-kept):J.Money(share/list.Count);
                    kept+=part;
                    dist.Add(InsertDistribution(c,tx,invoiceId,month,line.S("id"),account,list[i].Required("erpRiderId"),(int)list[i].N("orders"),part,list[i].S("fromDate"),list[i].S("toDate"),by,now,list.Count==1?"actual_rider":"explicit"));
                }
            }
            else
            {
                // Sole provisional row keyed by account — capture/calc bind via account when one rider worked it.
                string placeholderRider=line.S("erpRiderId");
                if(placeholderRider=="")placeholderRider=account; // account-scoped until distribution assigns riders
                dist.Add(InsertDistribution(c,tx,invoiceId,month,line.S("id"),account,placeholderRider,(int)line.N("orders"),share,"","",by,now,"actual_rider"));
            }
        }
        // Partner items stay company-borne (already platform_deducted + ready bearer).
        Store.Audit(c,tx,"sal.platform_invoice_approved",invoiceId,by);
        return new()
        {
            ["id"]=invoiceId,["status"]=StatusApproved,["approvedBy"]=by,["approvedAt"]=now,
            ["distribution"]=dist,["allowedActions"]=new JsonArray("propose_distribution","allocate_tga")
        };
    }

    /// <summary>Approved Keeta invoice lines as capture kind keeta_invoice_row (used-field allowlist).</summary>
    public static IEnumerable<JsonObject> ApprovedKeetaRows(DbConnection c,DbTransaction? tx,string companyId,string periodMonth)
    {
        Ensure(c,tx);
        var rows=new List<JsonObject>();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT l.id,l.erp_platform_account_id,l.gross,l.deductions,l.orders
            FROM {SchemaSql.Q(c,"sal","platform_invoice_line")} l
            INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice")} i ON i.id=l.invoice_id
            WHERE i.company_id=@c AND i.app_code='keeta' AND i.period_month=@pm AND i.status IN (2,3)
              AND i.approved_at IS NOT NULL AND l.account_key<>'partner_total'
            """;
        Add(cmd,"@c",companyId);Add(cmd,"@pm",Month(periodMonth));
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            rows.Add(new JsonObject
            {
                ["id"]=r["id"].ToString(),
                ["erpPlatformAccountId"]=r["erp_platform_account_id"].ToString(),
                ["gross"]=Convert.ToDecimal(r["gross"]),
                ["deductions"]=Convert.ToDecimal(r["deductions"]),
                ["orders"]=Convert.ToInt32(r["orders"])
            });
        }
        return rows;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/platform-invoices",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Upload(c,tx,ctx.User,input)))
            .WithTags("Payroll").WithSummary("Upload a money-schema platform invoice (Keeta first); partner totals on company");
        api.MapPost("/sal/platform-invoices/{id}/approve",(string id,JsonObject? input,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id,input)))
            .WithTags("Payroll").WithSummary("Approve invoice totals and feed sal.distribution from account lines");
    }

    static void InsertItem(DbConnection c,DbTransaction tx,string lineId,string month,string code,decimal amount,string by,string now,bool companyBorne)
    {
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","platform_invoice_item")}
            (id,line_id,period_month,item_code,amount,bearer_status,platform_deducted,created_by,created_at)
            VALUES(@id,@line,@pm,@code,@amt,@bearer,@ded,@by,@at)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@line",lineId),("@pm",month),("@code",code),
            ("@amt",amount),("@bearer",companyBorne?SalDistribution.BearerReady:SalDistribution.BearerPending),
            ("@ded",companyBorne?1:0),("@by",by),("@at",now));
        if(companyBorne)
        {
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","item_bearer")}
                (id,period_month,item_id,line_no,bearer,amount,created_by,created_at)
                SELECT @id,@pm,i.id,1,'company',i.amount,@by,@at FROM {SchemaSql.Q(c,"sal","platform_invoice_item")} i
                WHERE i.line_id=@line AND i.item_code=@code
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",month),("@line",lineId),("@code",code),("@by",by),("@at",now));
        }
    }

    static JsonObject InsertDistribution(DbConnection c,DbTransaction tx,string invoiceId,string month,string lineId,string account,string rider,int orders,decimal share,string from,string to,string by,string now,string method)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","distribution")}
            (id,period_month,invoice_id,line_id,erp_platform_account_id,erp_rider_id,orders,days,from_date,to_date,share_amount,method,status,created_by,created_at)
            VALUES(@id,@pm,@inv,@line,@acc,@rider,@orders,0,@from,@to,@share,@method,'ready',@by,@at)
            """,("@id",id),("@pm",month),("@inv",invoiceId),("@line",NullIfEmpty(lineId)??(object)DBNull.Value),
            ("@acc",account),("@rider",rider),("@orders",orders),
            ("@from",NullDate(from)),("@to",NullDate(to)),("@share",share),("@method",method),("@by",by),("@at",now));
        return new(){["id"]=id,["erpPlatformAccountId"]=account,["erpRiderId"]=rider,["shareAmount"]=share,["method"]=method};
    }

    static JsonObject Load(DbConnection c,DbTransaction? tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,app_code,company_id,period_month,status,uploaded_by,gross,deductions,net
            FROM {SchemaSql.Q(c,"sal","platform_invoice")} WHERE id=@id
            """;
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"الفاتورة غير موجودة.",404);
        return new()
        {
            ["id"]=r["id"].ToString(),["app"]=r["app_code"].ToString(),["companyId"]=r["company_id"].ToString(),
            ["periodMonth"]=DateVal(r["period_month"]),["status"]=Convert.ToInt32(r["status"]),
            ["uploadedBy"]=r["uploaded_by"] is DBNull?"":r["uploaded_by"].ToString(),
            ["gross"]=Convert.ToDecimal(r["gross"]),["deductions"]=Convert.ToDecimal(r["deductions"]),["net"]=Convert.ToDecimal(r["net"])
        };
    }

    static List<JsonObject> LoadLines(DbConnection c,DbTransaction? tx,string invoiceId)
    {
        var rows=new List<JsonObject>();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,account_key,erp_platform_account_id,gross,deductions,net,orders
            FROM {SchemaSql.Q(c,"sal","platform_invoice_line")} WHERE invoice_id=@id ORDER BY source_row
            """;
        Add(cmd,"@id",invoiceId);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            rows.Add(new JsonObject
            {
                ["id"]=r["id"].ToString(),["accountKey"]=r["account_key"].ToString(),
                ["erpPlatformAccountId"]=r["erp_platform_account_id"].ToString(),
                ["gross"]=Convert.ToDecimal(r["gross"]),["deductions"]=Convert.ToDecimal(r["deductions"]),
                ["net"]=Convert.ToDecimal(r["net"]),["orders"]=Convert.ToInt32(r["orders"])
            });
        }
        return rows;
    }

    static string Day(string v)=>v.Length>=10?v[..10]:v;
    static string Month(string v){string d=Day(v);return d.Length>=7?d[..7]+"-01":d;}
    static string DateVal(object v)=>v is DateTime dt?dt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):v.ToString()![..10];
    static object NullDate(string v)=>string.IsNullOrWhiteSpace(v)?DBNull.Value:Day(v);
    static string? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s;
    static void Add(DbCommand cmd,string name,object value){var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}
}
