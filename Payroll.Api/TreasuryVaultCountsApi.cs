using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-37: vault counts + approve-post → 422202 (over) / 542102 (short). Maker ≠ checker. DB trigger checks net account totals.</summary>
public static class TreasuryVaultCountsApi
{
    public const string OverAccount="422202",ShortAccount="542102";
    public static JsonObject List(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? companyId)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        SchemaTreasury.Ensure(c,tx);
        var rows=new JsonArray();
        using var cmd=Store.Command(c,tx,$"""
            SELECT v.id,v.cash_box_id,v.company_id,v.registration_id,v.period_month,v.counted_on,
                   v.book_balance,v.counted_amount,v.journal_id,v.status,v.created_by,v.approved_by,b.code,b.name_ar
            FROM {SchemaSql.Q(c,"treasury","vault_count")} v
            JOIN {SchemaSql.Q(c,"treasury","cash_box")} b ON b.id=v.cash_box_id
            ORDER BY v.counted_on DESC
            """);
        using var r=cmd.ExecuteReader();
        var pending=new List<(JsonObject Row,string AccountId)>();
        while(r.Read())
        {
            string company=r["company_id"].ToString()!;
            if(!FinancialScopes.Sponsor(actor,company))continue;
            if(!string.IsNullOrWhiteSpace(companyId)&&!string.Equals(company,companyId,StringComparison.OrdinalIgnoreCase))continue;
            decimal book=Convert.ToDecimal(r["book_balance"],CultureInfo.InvariantCulture);
            decimal counted=Convert.ToDecimal(r["counted_amount"],CultureInfo.InvariantCulture);
            decimal variance=J.Money(counted-book);
            int status=Convert.ToInt32(r["status"],CultureInfo.InvariantCulture);
            var row=new JsonObject
            {
                ["id"]=r["id"].ToString(),["cashBoxId"]=r["cash_box_id"].ToString(),["companyId"]=company,
                ["registrationId"]=r["registration_id"].ToString(),
                ["periodMonth"]=DateOnlyText(r["period_month"]),["countedOn"]=DateOnlyText(r["counted_on"]),
                ["bookBalance"]=J.Money(book),["countedAmount"]=J.Money(counted),["variance"]=variance,
                ["varianceAccount"]=variance>0?OverAccount:variance<0?ShortAccount:"",
                ["journalId"]=r["journal_id"] is DBNull or null?"":r["journal_id"].ToString(),
                ["status"]=StatusName(status),["statusCode"]=status,
                ["cashBoxCode"]=r["code"].ToString(),["cashBoxNameAr"]=r["name_ar"].ToString(),
                ["createdBy"]=r["created_by"] is DBNull?"":r["created_by"].ToString(),
                ["approvedBy"]=r["approved_by"] is DBNull?"":r["approved_by"].ToString()
            };
            pending.Add((row,r["cash_box_id"].ToString()!));
        }
        r.Close();
        foreach(var (row,boxId) in pending)
        {
            object? accountObj=SchemaSql.Scalar(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","cash_box")} WHERE id=@id",("@id",boxId));
            string accountId=accountObj?.ToString()??"";
            if(accountId=="")continue;
            object? codeObj=SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",accountId));
            string code=codeObj?.ToString()??"";
            if(!FinancialScopes.TargetAccount(actor,row.S("companyId"),code))continue;
            row["allowedActions"]=Allowed(actor,row);
            rows.Add(row);
        }
        var cashBoxes=new JsonArray();
        using(var boxCmd=Store.Command(c,tx,$"""
            SELECT b.id,b.code,b.name_ar,b.company_id,a.code AS account_code
            FROM {SchemaSql.Q(c,"treasury","cash_box")} b
            JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=b.account_id
            WHERE b.status=1
            ORDER BY b.code
            """))
        using(var br=boxCmd.ExecuteReader())
        {
            while(br.Read())
            {
                string comp=br["company_id"].ToString()!;
                if(!FinancialScopes.Sponsor(actor,comp))continue;
                if(!string.IsNullOrWhiteSpace(companyId)&&!string.Equals(comp,companyId,StringComparison.OrdinalIgnoreCase))continue;
                string accCode=br["account_code"].ToString()!;
                if(!FinancialScopes.TargetAccount(actor,comp,accCode))continue;
                cashBoxes.Add(new JsonObject
                {
                    ["id"]=br["id"].ToString(),
                    ["code"]=br["code"].ToString(),
                    ["nameAr"]=br["name_ar"].ToString(),
                    ["companyId"]=comp,
                    ["accountCode"]=accCode
                });
            }
        }
        var listActions=new JsonArray();
        if(AccessPolicy.Has(actor,"treasury.vault_reconcile")||AccessPolicy.Administrator(actor))listActions.Add("create");
        return new(){["items"]=rows,["allowedActions"]=listActions,["cashBoxes"]=cashBoxes};
    }

    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.vault_reconcile"),"صلاحية جرد الخزن مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        string cashBoxId=input.Required("cashBoxId");
        string companyId,registrationId,boxAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT company_id,registration_id,account_id FROM {SchemaSql.Q(c,"treasury","cash_box")} WHERE id=@id",("@id",cashBoxId)))
        using(var r=cmd.ExecuteReader())
        {
            J.Need(r.Read(),"الخزنة غير موجودة.",404);
            companyId=r.GetValue(0).ToString()!;registrationId=r.GetValue(1).ToString()!;boxAccountId=r.GetValue(2).ToString()!;
        }
        FinancialScopes.DemandSponsor(actor,companyId);
        DemandTreasuryAccount(actor,companyId,boxAccountId,c,tx);
        DateOnly countedOn=ParseDay(input.Required("countedOn"));
        DateOnly periodMonth=new(countedOn.Year,countedOn.Month,1);
        decimal counted=J.Money(PayValues.Number(input,"countedAmount",0));
        J.Need(counted>=0,"مبلغ الجرد لا يكون سالبًا.");
        // Book balance is server-authored through the count date; callers never supply it.
        // Later posted lines after the count date block the count (cutoff must stay the count date).
        J.Need(!HasPostedLinesAfter(c,tx,companyId,boxAccountId,countedOn),
            "توجد قيود مرحّلة بعد تاريخ الجرد؛ لا يُنشأ الجرد حتى يُزال الفرق أو يُختَم الحد.",409);
        decimal book=BookBalanceAsOf(c,tx,companyId,boxAccountId,periodMonth,countedOn);
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        string actorName=actor.Identity!.Name!;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","vault_count")}
            (id,cash_box_id,company_id,registration_id,period_month,counted_on,book_balance,counted_amount,status,created_by,created_at)
            VALUES(@id,@b,@c,@r,@m,@d,@book,@cnt,1,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",id),("@b",cashBoxId),("@c",companyId),("@r",registrationId),
            ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@d",countedOn.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@book",book),("@cnt",counted),("@by",actorName));
        Store.Audit(c,tx,"treasury.vault_count_created",id,actorName);
        return Get(c,tx,actor,id);
    }

    public static JsonObject Submit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.vault_reconcile"),"صلاحية جرد الخزن مطلوبة.",403);
        var row=Get(c,tx,actor,id);J.Need(row.N("statusCode")==1,"الجرد ليس مسودة.");
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"treasury","vault_count")} SET status=2,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
        Store.Audit(c,tx,"treasury.vault_count_submitted",id,actor.Identity!.Name!);
        return Get(c,tx,actor,id);
    }

    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"approvals.decide")||AccessPolicy.Has(actor,"treasury.vault_reconcile"),"صلاحية اعتماد الجرد مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        var row=Get(c,tx,actor,id);
        J.Need(row.N("statusCode")==2,"الجرد بانتظار الاعتماد.");
        string maker=row.S("createdBy");
        J.Need(AccessPolicy.Independent(actor,maker,"treasury.vault_count_approve",c,tx,row,scope:AccessPolicy.ScopeTreasuryVaultCount),
            "لا يجوز اعتماد جرد أنشأته؛ يلزم مراجع مستقل (صانع ≠ معتمد).",403);
        string companyId=row.S("companyId");
        DateOnly countedOn=ParseDay(row.S("countedOn"));
        DateOnly periodMonth=new(countedOn.Year,countedOn.Month,1);
        string boxAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","cash_box")} WHERE id=@id",("@id",row.S("cashBoxId"))))
            boxAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الخزنة غير موجود.",409);
        // Later posted lines after the count date also block approve (not only create).
        J.Need(!HasPostedLinesAfter(c,tx,companyId,boxAccountId,countedOn),
            "توجد قيود مرحّلة بعد تاريخ الجرد؛ لا يُعتمد الجرد حتى يُزال الفرق أو يُختَم الحد.",409);
        // Recompute book/variance at approve — a line dated on/before count posted after create must change the journal.
        decimal book=BookBalanceAsOf(c,tx,companyId,boxAccountId,periodMonth,countedOn);
        decimal counted=J.Money(row.N("countedAmount"));
        decimal variance=J.Money(counted-book);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","vault_count")}
            SET book_balance=@book,updated_at={SchemaSql.NowLiteral(c)}
            WHERE id=@id
            """,("@id",id),("@book",book));
        row["bookBalance"]=book;row["variance"]=variance;
        string? journalId=null;
        // Workflow column approved_by is a GUID (no FK); keep a stable id for the approver.
        string approverId=SchemaSql.GuidText(SchemaSql.NewId());
        try{approverId=SchemaApprovals.EnsureAuthUserId(c,tx,actor.Identity!.Name!);}catch(ApiError){/* document-only users keep synthetic id */}
        if(variance!=0)
        {
            journalId=PostVarianceJournal(c,tx,actor,row);
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"treasury","vault_count")}
                SET status=3,journal_id=@j,approved_by=@by,approved_at={SchemaSql.NowLiteral(c)},updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id
                """,("@id",id),("@j",journalId),("@by",approverId));
        }
        else
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"treasury","vault_count")}
                SET status=3,approved_by=@by,approved_at={SchemaSql.NowLiteral(c)},updated_at={SchemaSql.NowLiteral(c)}
                WHERE id=@id
                """,("@id",id),("@by",approverId));
        Store.Audit(c,tx,"treasury.vault_count_approved",id,actor.Identity!.Name!);
        var result=Get(c,tx,actor,id);
        if(journalId!=null)result["journalId"]=journalId;
        return result;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/treasury/vault-counts",(string? companyId,HttpContext ctx)=>store.Read(c=>List(c,null,ctx.User,companyId)))
            .WithTags("Banks and treasury").WithSummary("List vault counts");
        api.MapPost("/treasury/vault-counts",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Create(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Create vault count");
        api.MapPost("/treasury/vault-counts/{id}/submit",(string id,HttpContext ctx)=>store.Write((c,tx)=>Submit(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Submit vault count for approval");
        api.MapPost("/treasury/vault-counts/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Approve vault count and post over/short journal");
    }

    static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var list=List(c,tx,actor,null);var row=list["items"]!.AsArray().OfType<JsonObject>().FirstOrDefault(x=>x.S("id")==id)
            ??throw new ApiError("جرد الخزنة غير موجود.",404);
        return row;
    }
    static JsonArray Allowed(ClaimsPrincipal actor,JsonObject row)
    {
        var a=new JsonArray();
        int status=(int)row.N("statusCode");
        bool write=AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.vault_reconcile");
        if(status==1&&write)a.Add("submit");
        if(status==2&&(AccessPolicy.Has(actor,"approvals.decide")||write)&&!AccessPolicy.SamePerson(actor,row.S("createdBy")))a.Add("approve");
        return a;
    }
    static string PostVarianceJournal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject row)
    {
        decimal variance=row.N("variance"); // counted - book; >0 over, <0 short
        string companyId=row.S("companyId");
        string boxAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","cash_box")} WHERE id=@id",("@id",row.S("cashBoxId"))))
            boxAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الخزنة غير موجود.",409);
        string varianceCode=variance>0?OverAccount:ShortAccount;
        string varianceAccountId=AccountIdByCode(c,tx,varianceCode);
        string periodRaw=row.S("periodMonth");
        if(periodRaw.Length==7)periodRaw+="-01";
        if(periodRaw.Length>10)periodRaw=periodRaw[..10];
        if(!DateOnly.TryParseExact(periodRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var month))
            month=ParseDay(row.S("countedOn"));
        month=new DateOnly(month.Year,month.Month,1);
        Guid periodId=EnsureOpenPeriod(c,tx,companyId,month);
        decimal amount=J.Money(Math.Abs(variance));
        var lines=new List<JournalPosting.Line>();
        if(variance>0)
        {
            // Over: Dr cash box, Cr 422202
            lines.Add(new(Guid.Parse(boxAccountId),SchemaOrgCostCentre.NoneId,"none",amount,0m,"زيادة جرد خزنة"));
            lines.Add(new(Guid.Parse(varianceAccountId),SchemaOrgCostCentre.NoneId,"none",0m,amount,"زيادة جرد خزنة"));
        }
        else
        {
            // Short: Dr 542102, Cr cash box
            lines.Add(new(Guid.Parse(varianceAccountId),SchemaOrgCostCentre.NoneId,"none",amount,0m,"عجز جرد خزنة"));
            lines.Add(new(Guid.Parse(boxAccountId),SchemaOrgCostCentre.NoneId,"none",0m,amount,"عجز جرد خزنة"));
        }
        var result=JournalPosting.Post(c,tx,new(
            Guid.Parse(companyId),periodId,month,DateOnly.ParseExact(row.S("countedOn")[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture),
            "vault_count",Guid.Parse(row.S("id")),"vault_over_short",0,false,actor.Identity!.Name!,lines,
            NarrationAr:variance>0?"ترحيل زيادة جرد خزنة":"ترحيل عجز جرد خزنة"));
        return SchemaSql.GuidText(result.JournalId);
    }
    static decimal BookBalanceAsOf(DbConnection c,DbTransaction tx,string companyId,string accountId,DateOnly periodMonth,DateOnly asOf)
    {
        object? openingObj=SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(opening),0) FROM {SchemaSql.Q(c,"fin","period_balance")}
            WHERE company_id=@c AND account_id=@a AND period_month=@m
            """,("@c",companyId),("@a",accountId),("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        decimal opening=J.Money(Convert.ToDecimal(openingObj??0m,CultureInfo.InvariantCulture));
        object? moveObj=SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(jl.debit-jl.credit),0)
            FROM {SchemaSql.Q(c,"fin","journal_line")} jl
            JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
            WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
              AND j.journal_date>=@from AND j.journal_date<=@to
            """,
            ("@c",companyId),("@a",accountId),
            ("@from",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@to",asOf.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        return J.Money(opening+Convert.ToDecimal(moveObj??0m,CultureInfo.InvariantCulture));
    }
    static bool HasPostedLinesAfter(DbConnection c,DbTransaction tx,string companyId,string accountId,DateOnly asOf)
    {
        object? v=SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(1)
            FROM {SchemaSql.Q(c,"fin","journal_line")} jl
            JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
            WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
              AND j.journal_date>@d
            """,("@c",companyId),("@a",accountId),("@d",asOf.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        return Convert.ToInt32(v??0,CultureInfo.InvariantCulture)>0;
    }
    static string AccountIdByCode(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        J.Need(id is not null and not DBNull,$"حساب {code} غير مهيأ في الدليل.",409);
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
    static Guid EnsureOpenPeriod(DbConnection c,DbTransaction tx,string companyId,DateOnly month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",
            ("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at)
            VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        return id;
    }
    static void DemandTreasuryAccount(ClaimsPrincipal actor,string companyId,string accountId,DbConnection c,DbTransaction tx)
    {
        object? codeObj=SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",accountId));
        string code=codeObj?.ToString()??"";
        J.Need(code!=""&&FinancialScopes.TargetAccount(actor,companyId,code),"الحساب المالي خارج النطاق المسند للمستخدم.",403);
    }
    static DateOnly ParseDay(string raw)
    {
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"تاريخ الجرد غير صالح.");
        return d;
    }
    static string DateOnlyText(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateTimeOffset dto=>DateOnly.FromDateTime(dto.UtcDateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        _=>(Convert.ToString(value,CultureInfo.InvariantCulture)??"").Length>=10?(Convert.ToString(value,CultureInfo.InvariantCulture)??"")[..10]:Convert.ToString(value,CultureInfo.InvariantCulture)??""
    };
    static string StatusName(int status)=>status switch{1=>"draft",2=>"submitted",3=>"approved",4=>"rejected",5=>"cancelled",_=>"unknown"};
}
