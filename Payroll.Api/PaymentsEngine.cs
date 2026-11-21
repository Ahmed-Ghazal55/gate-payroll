using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>WP1: all payment sources use one document, atomic claim, recorded dual approval, and posting transaction.</summary>
public static partial class PaymentsEngine
{
    public static readonly string[] DocumentTypes=["quick","expense","purchase","rent","installment","hr_fee","traffic_violation","government","fuel_topup","prepaid_topup","mada_topup","advance","hs_topup"];
    internal static string Q(DbConnection c,string table)=>SchemaSql.Q(c,"pay",table);
    internal static string Me(ClaimsPrincipal actor)=>actor.Identity?.Name??throw new ApiError("هوية المستخدم مطلوبة.",401);
    internal static string Id(string value){J.Need(Guid.TryParse(value,out _),"المعرف غير صالح.");return Guid.Parse(value).ToString("D");}
    internal static object NullId(string value)=>value==""?DBNull.Value:Id(value);
    internal static string Day(string value){J.Need(DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"التاريخ غير صالح.");return value;}
    internal static decimal Money(JsonObject input,string key){decimal n=input.N(key);J.Need(n>0&&n<=999_999_999_999m&&n==J.Money(n),"المبلغ موجب وبدقة منزلتين عشريتين.");return n;}
    internal static bool Visible(ClaimsPrincipal actor,JsonObject doc)=>FinancialScopes.Sponsor(actor,doc.S("companyId"))&&(doc.S("platform")==""||FinancialScopes.Platform(actor,doc.S("platform")));
    internal static void Scope(ClaimsPrincipal actor,JsonObject doc){J.Need(Visible(actor,doc),"المستند غير موجود.",404);}
    internal static string Permission(string type)=>type switch{"hr_fee"=>"payments.hr_request.create","advance" or "hs_topup"=>"advances.request","fuel_topup" or "prepaid_topup" or "mada_topup"=>"payments.fuel.manage",_=>"payments.disburse"};
    internal static Guid Account(DbConnection c,DbTransaction? tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@code AND is_active=1 AND is_postable=1",("@code",code));
        J.Need(id!=null,"الحساب غير موجود أو غير نشط أو غير قابل للترحيل.",409);return Guid.Parse(id!.ToString()!);
    }
    internal static List<JsonObject> Rows(DbConnection c,DbTransaction? tx,string sql,params (string Name,object Value)[] args)
    {
        var rows=new List<JsonObject>();using var cmd=Store.Command(c,tx,sql,args);using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            var row=new JsonObject();
            for(int i=0;i<r.FieldCount;i++)
            {
                string name=r.GetName(i);object value=r.GetValue(i);
                row[name]=value switch{DBNull=>null,Guid g=>JsonValue.Create(g.ToString("D")),DateTime d=>JsonValue.Create(d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),_=>System.Text.Json.JsonSerializer.SerializeToNode(value)};
                if(name=="payoutBlocked")row[name]=Convert.ToInt32(value)==1;
            }
            rows.Add(row);
        }
        return rows;
    }

    internal static string Hash(JsonObject input)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Canonical(input))));
    static string Canonical(JsonNode? n)=>n switch{JsonObject o=>"{"+string.Join(",",o.Where(p=>p.Key!="allowedActions").OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>System.Text.Json.JsonSerializer.Serialize(p.Key)+":"+Canonical(p.Value)))+"}",JsonArray a=>"["+string.Join(",",a.Select(Canonical))+"]",_=>n?.ToJsonString()??"null"};
    internal static string Text(JsonObject input,string field,int max=100){string value=input.Required(field).Trim();J.Need(value.Length<=max,"النص أطول من الحد المسموح.");return value;}
    internal static JsonArray Array(IEnumerable<JsonObject> rows)=>new(rows.Select(x=>(JsonNode?)x).ToArray());
    internal static JsonObject Raw(DbConnection c,DbTransaction? tx,string id)
    {
        var doc=Rows(c,tx,$"""
            SELECT id,doc_no AS docNo,type,company_id AS companyId,supplier_id AS supplierId,reference,platform,
            occurred_on AS occurredOn,due_on AS dueOn,period_month AS periodMonth,amount,vat_amount AS vatAmount,
            status,run_mode AS runMode,payout_blocked AS payoutBlocked,created_by AS createdBy,approved_by AS approvedBy,metadata,
            content_hash AS contentHash
            FROM {Q(c,"document")} WHERE id=@id
            """,("@id",Id(id))).SingleOrDefault()??throw new ApiError("المستند غير موجود.",404);
        doc["metadata"]=JsonNode.Parse(doc.S("metadata"));
        doc["lines"]=Array(Rows(c,tx,$"SELECT id,account_id AS accountId,cost_centre_id AS costCentreId,erp_rider_id AS erpRiderId,erp_platform_account_id AS erpPlatformAccountId,app_code AS appCode,amount FROM {Q(c,"document_line")} WHERE document_id=@id AND active=1 ORDER BY line_no",("@id",Id(id))));
        var payable=Rows(c,tx,$"SELECT id AS payableId,amount_due AS amountDue,amount_reserved AS amountReserved,amount_paid AS amountPaid,available_balance AS availableBalance FROM {Q(c,"payable")} WHERE source_table='pay.document' AND source_id=@id",("@id",Id(id))).SingleOrDefault();
        if(payable!=null)foreach(var p in payable)doc[p.Key]=p.Value?.DeepClone();return doc;
    }
    static JsonObject Actions(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject d)
    {
        var actions=new JsonArray();int status=(int)d.N("status");
        if(status==1&&AccessPolicy.Has(actor,Permission(d.S("type")))){actions.Add("edit_draft");actions.Add("submit");actions.Add("cancel_draft");}
        if(status==2&&AccessPolicy.Has(actor,"approvals.decide")&&AccessPolicy.WithinApprovalLimit(actor,d,c,tx))actions.Add("approve");
        if(status==3&&d.N("availableBalance")>0&&AccessPolicy.Has(actor,"payments.disburse")&&d.N("runMode")==0&&!d.B("payoutBlocked"))actions.Add("create_payment_request");
        d["allowedActions"]=actions;return d;
    }
    public static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {AccessPolicy.Demand(actor,"payments.view");var d=Raw(c,tx,id);Scope(actor,d);return Actions(c,tx,actor,d);}
    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,string? type,string? companyId)
    {
        AccessPolicy.Demand(actor,"payments.view");
        var ids=Rows(c,null,$"SELECT id,company_id AS companyId,platform FROM {Q(c,"document")} WHERE (@type='' OR type=@type){(companyId==null?"":" AND company_id=@company")} ORDER BY due_on,id",("@type",type??""),("@company",companyId==null?DBNull.Value:Id(companyId)));
        // Avoid implicit SQL uniqueidentifier conversion of the empty optional company filter.
        return new(){["items"]=Array(ids.Where(d=>Visible(actor,d)).Select(d=>Actions(c,null,actor,Raw(c,null,d.S("id"))))),["allowedActions"]=new JsonArray()};
    }
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        string type=input.Required("type");J.Need(DocumentTypes.Contains(type),"نوع المستند غير صالح.");AccessPolicy.Demand(actor,Permission(type));
        string company=Id(input.Required("companyId")),key=Text(input,"idempotencyKey"),hash=Hash(input);
        var scope=new JsonObject{["companyId"]=company,["platform"]=input.S("platform")};Scope(actor,scope);
        object? old=SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"document")} WHERE company_id=@c AND idempotency_key=@k",("@c",company),("@k",key));
        if(old!=null){var prior=Raw(c,tx,old.ToString()!);J.Need(prior.S("contentHash")==hash,"مفتاح العملية مستخدم بمحتوى مختلف.",409);return Actions(c,tx,actor,prior);}
        string month=J.Period(input.Required("period"))+"-01",occurred=Day(input.Required("occurredOn")),due=Day(input.Required("dueOn")),reference=Text(input,"reference");
        J.Need(occurred[..7]==month[..7],"تاريخ المستند خارج شهر الفترة.");
        J.Need(input.N("runMode") is 0 or 1,"وضع التشغيل غير صالح.");
        var lines=input["lines"] as JsonArray??throw new ApiError("بنود المستند مطلوبة.");J.Need(lines.Count is >0 and <=100,"عدد البنود من واحد إلى مائة.");
        decimal amount=lines.OfType<JsonObject>().Sum(l=>Money(l,"amount"));J.Need(lines.All(l=>l is JsonObject),"بند مستند غير صالح.");
        decimal vat=input.N("vatAmount");J.Need(vat>=0&&vat==J.Money(vat)&&vat<amount,"ضريبة المستند غير صالحة.");
        J.Need(vat==0||input.B("validTaxInvoice"),"خصم ضريبة المدخلات يتطلب فاتورة ضريبية صالحة.");
        decimal taxLines=lines.OfType<JsonObject>().Where(l=>l.S("accountCode")=="112601").Sum(l=>l.N("amount"));
        J.Need(taxLines==vat,"بنود ضريبة المدخلات لا تطابق ضريبة المستند.");
        var apps=lines.OfType<JsonObject>().Select(l=>l.S("appCode","none")).Where(a=>a!="none").Select(a=>PayPlatforms.For(a).Platform).Distinct().ToArray();
        J.Need(apps.Length<=1,"مستند الدفع يخص منصة واحدة؛ افصل المستندات حسب المنصة.");
        if(apps.Length==1){J.Need(input.S("platform")==""||input.S("platform")==apps[0],"منصة المستند لا تطابق منصة البنود.");input=input.DeepClone().AsObject();input["platform"]=apps[0];Scope(actor,new(){["companyId"]=company,["platform"]=apps[0]});}
        string supplier=input.S("supplierId");
        if(supplier!="")J.Need(SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"supplier")} WHERE id=@id AND company_id=@c AND status=1",("@id",Id(supplier)),("@c",company))!=null,"المورد غير موجود في نطاق الشركة.",404);
        J.Need(type!="purchase"||supplier!="","المورد مطلوب لفاتورة المشتريات.");
        string id=MoneyId();
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {Q(c,"document")}(id,doc_no,type,company_id,supplier_id,reference,idempotency_key,content_hash,platform,occurred_on,due_on,period_month,amount,vat_amount,erp_receipt_id,metadata,run_mode,payout_blocked,created_by)
            VALUES(@id,@no,@t,@c,@sup,@ref,@key,@hash,@plat,@on,@due,@pm,@amount,@vat,@erp,@meta,@mode,@blocked,@by)
            """,("@id",id),("@no","PD-"+id.Replace("-","")[..30]),("@t",type),("@c",company),("@sup",NullId(supplier)),("@ref",reference),("@key",key),("@hash",hash),("@plat",input.S("platform")),("@on",occurred),("@due",due),("@pm",month),("@amount",amount),("@vat",vat),("@erp",NullId(input.S("erpReceiptId"))),("@meta",input.ToJsonString()),("@mode",(int)input.N("runMode")),("@blocked",input.B("payoutBlocked")?1:0),("@by",Me(actor)));
        int index=0;foreach(var l in lines.OfType<JsonObject>())
        {
            Guid account=Account(c,tx,l.Required("accountCode"));string centre=l.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D"));
            ValidateCentre(c,tx,company,Id(centre));
            J.Need((l.S("erpRiderId")=="")==(l.S("erpPlatformAccountId")==""),"بند المندوب يحتاج معرف المندوب وحساب المنصة معًا.");
            string app=l.S("appCode","none");if(app!="none")J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(app).Platform),"المنصة خارج نطاقك.",403);
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"document_line")}(id,document_id,company_id,line_no,period_month,account_id,cost_centre_id,erp_rider_id,erp_platform_account_id,app_code,amount,created_by) VALUES(@id,@doc,@c,@ln,@pm,@a,@cc,@r,@p,@app,@amount,@by)",("@id",MoneyId()),("@doc",id),("@c",company),("@ln",++index),("@pm",month),("@a",account.ToString("D")),("@cc",centre),("@r",NullId(l.S("erpRiderId"))),("@p",NullId(l.S("erpPlatformAccountId"))),("@app",app),("@amount",l.N("amount")),("@by",Me(actor)));
        }
        Store.Audit(c,tx,"payment.document.created",id,Me(actor));return Actions(c,tx,actor,Raw(c,tx,id));
    }
    internal static string MoneyId()=>SchemaSql.GuidText(SchemaSql.NewId());
    internal static void ValidateCentre(DbConnection c,DbTransaction? tx,string company,string centre)=>J.Need(SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"org","cost_centre")} WHERE id=@id AND status=1 AND (company_id IS NULL OR company_id=@c)",("@id",centre),("@c",company))!=null,"مركز التكلفة خارج نطاق الشركة أو غير نشط.",409);
    public static JsonObject Transition(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,string action,JsonObject input)
    {
        var doc=Raw(c,tx,id);Scope(actor,doc);int status=(int)doc.N("status");
        AccessPolicy.Demand(actor,action=="approve"?"approvals.decide":Permission(doc.S("type")));
        int next=action switch{"submit" when status==1=>2,"cancel" when status==1=>5,"approve" when status==2=>3,_=>throw new ApiError("حالة المستند لا تسمح بهذا الإجراء.",409)};
        object approved=DBNull.Value;
        if(next==3)
        {
            MoneyWriteGuards.Demand((int)doc.N("runMode"),doc.B("payoutBlocked"),MoneyEffectKind.Accounting);
            PaymentAdvances.DemandApproval(c,tx,actor,doc,input);
            PaymentAdvances.DemandApproval(c,tx,actor,doc,input);
            AccessPolicy.DemandApprovalLimit(actor,doc,c,tx);approved=SchemaApprovals.EnsureAuthUserId(c,tx,Me(actor));
        }
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"document")} SET status=@s,approved_by=@by,approved_at=@at WHERE id=@id AND status=@old",("@s",next),("@by",approved),("@at",next==3?DateTimeOffset.UtcNow.ToString("O"):(object)DBNull.Value),("@id",Id(id)),("@old",status));
        if(next==3)
        {
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"approval")}(id,document_id,user_id,decided_at,note,created_by) VALUES(@id,@doc,@by,{SchemaSql.NowLiteral(c)},@note,@actor)",("@id",MoneyId()),("@doc",Id(id)),("@by",approved),("@note",input.S("note")),("@actor",Me(actor)));
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payable")}(id,kind,source_table,source_id,company_id,amount_due,period_month,due_on,created_by) VALUES(@id,@kind,'pay.document',@doc,@c,@amount,@pm,@due,@by)",("@id",MoneyId()),("@kind",doc.S("type")),("@doc",Id(id)),("@c",doc.S("companyId")),("@amount",doc.N("amount")),("@pm",doc.S("periodMonth")[..10]),("@due",doc.S("dueOn")[..10]),("@by",Me(actor)));
            if(doc.S("type")=="purchase")PostDocument(c,tx,actor,doc,"purchase_accrual",Id(id),"211101",doc.N("amount"),doc.S("occurredOn")[..10]);
        }
        Store.Audit(c,tx,"payment.document."+action,Id(id),Me(actor));return Actions(c,tx,actor,Raw(c,tx,id));
    }
    internal static Guid Period(DbConnection c,DbTransaction tx,string company,string day)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m AND state=1",("@c",company),("@m",day[..7]+"-01"));
        J.Need(id!=null,"الفترة المحاسبية مغلقة أو غير موجودة.",409);return Guid.Parse(id!.ToString()!);
    }
    internal static Guid PostDocument(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,string purpose,string source,string creditCode,decimal amount,string day,bool supplierPayment=false)
    {
        var lines=new List<JournalPosting.Line>();Guid centre=SchemaOrgCostCentre.NoneId;decimal allocated=0;var sourceLines=doc["lines"]!.AsArray().OfType<JsonObject>().ToList();
        if(supplierPayment)lines.Add(new(Account(c,tx,"211101"),centre,"none",amount,0,"سداد مورد"));
        else for(int i=0;i<sourceLines.Count;i++)
        {
            var l=sourceLines[i];decimal share=i==sourceLines.Count-1?amount-allocated:J.Money(l.N("amount")/doc.N("amount")*amount);allocated+=share;
            if(share==0)continue;
            lines.Add(new(Guid.Parse(l.S("accountId")),Guid.Parse(l.S("costCentreId")),l.S("appCode"),share,0,null,Guid.TryParse(l.S("erpPlatformAccountId"),out var p)?p:null,Guid.TryParse(l.S("erpRiderId"),out var r)?r:null));
        }
        lines.Add(new(Account(c,tx,creditCode),centre,"none",0,amount,"سند صرف"));
        if(Guid.TryParse(doc.S("supplierId"),out var supplier))lines=lines.Select(l=>l with {SupplierId=supplier}).ToList();
        return JournalPosting.Post(c,tx,new(Guid.Parse(doc.S("companyId")),Period(c,tx,doc.S("companyId"),day),DateOnly.ParseExact(day[..7]+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture),DateOnly.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture),"payment_document",Guid.Parse(source),purpose,(byte)doc.N("runMode"),doc.B("payoutBlocked"),Me(actor),lines)).JournalId;
    }
}
