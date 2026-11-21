using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-36: bank statement upload / auto-match / adjust.
/// Adjust posts a journal through JournalPosting after approval. One audit row per file (M-0606).</summary>
public static class TreasuryBankStatementsApi
{
    public static JsonObject Upload(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        string bankAccountId=input.Required("bankAccountId");
        string companyId,registrationId,chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT company_id,registration_id,account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
        using(var r=cmd.ExecuteReader())
        {
            J.Need(r.Read(),"الحساب البنكي غير موجود.",404);
            companyId=r.GetValue(0).ToString()!;registrationId=r.GetValue(1).ToString()!;chartAccountId=r.GetValue(2).ToString()!;
        }
        FinancialScopes.DemandSponsor(actor,companyId);
        DateOnly periodMonth=ParseMonth(input.Required("periodMonth"));
        decimal opening=J.Money(PayValues.Number(input,"openingBalance"));
        decimal closing=J.Money(PayValues.Number(input,"closingBalance"));
        var lines=PayValues.Array(input["lines"]??new JsonArray(),5000).OfType<JsonObject>().ToList();
        J.Need(lines.Count>0,"كشف البنك يحتاج سطرًا واحدًا على الأقل.");
        decimal sum=J.Money(lines.Sum(l=>PayValues.Number(l,"amount",-1000000000)));
        J.Need(J.Money(closing-opening)==sum,"مجموع أسطر الكشف يجب أن يساوي فرق الرصيد الختامي عن الافتتاحي.");
        string fileContent=input.S("fileContent",string.Join("\n",lines.Select(l=>$"{l.S("occurredOn")}|{l.S("reference")}|{l.N("amount")}")));
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes(fileContent));
        // Duplicate active upload of the same file hash is refused (filtered unique).
        object? dup=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"treasury","bank_statement")}
            WHERE bank_account_id=@b AND file_hash=@h AND status<>5
            """,("@b",bankAccountId),("@h",hash));
        J.Need(dup is null or DBNull,"نفس ملف الكشف مرفوع مسبقًا لهذا الحساب.",409);

        string statementId=SchemaSql.GuidText(SchemaSql.NewId());
        string actorName=actor.Identity!.Name!;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_statement")}
            (id,company_id,registration_id,bank_account_id,period_month,file_hash,opening_balance,closing_balance,status,created_by,created_at)
            VALUES(@id,@c,@r,@b,@m,@h,@o,@cl,1,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",statementId),("@c",companyId),("@r",registrationId),("@b",bankAccountId),
            ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@h",hash),("@o",opening),("@cl",closing),("@by",actorName));

        int lineNo=0;
        foreach(var line in lines)
        {
            lineNo++;
            DateOnly occurred=ParseDay(line.Required("occurredOn"));
            J.Need(occurred.Year==periodMonth.Year&&occurred.Month==periodMonth.Month,"تاريخ السطر خارج شهر الكشف.");
            decimal amount=J.Money(PayValues.Number(line,"amount",-1000000000));
            J.Need(amount!=0,"مبلغ سطر الكشف لا يكون صفرًا.");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"treasury","bank_statement_line")}
                (id,statement_id,company_id,period_month,line_no,occurred_on,amount,reference)
                VALUES(@id,@s,@c,@m,@n,@d,@a,@ref)
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",statementId),("@c",companyId),
                ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@n",lineNo),("@d",occurred.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@a",amount),("@ref",(object?)line.S("reference")??DBNull.Value));
        }

        // One audit row per file (count + hash) — M-0606.
        Store.Save(c,"audit",new()
        {
            ["id"]=Guid.NewGuid().ToString("N"),["action"]="treasury.bank_statement_uploaded",
            ["entity"]=statementId,["recordId"]=statementId,["actor"]=actorName,
            ["at"]=DateTimeOffset.UtcNow.ToString("O"),["lineCount"]=lineNo,
            ["fileHash"]=Convert.ToHexString(hash),["bankAccountId"]=bankAccountId
        },tx);
        return View(c,tx,actor,statementId);
    }

    public static JsonObject AutoMatch(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);
        var statement=View(c,tx,actor,id);
        J.Need(statement.N("statusCode") is 1 or 2,"الكشف مغلق للمطابقة.");
        string companyId=statement.S("companyId");
        string bankAccountId=statement.S("bankAccountId");
        string chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
            chartAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);

        int matched=0;
        var unmatched=new JsonArray();
        foreach(var line in statement["lines"]!.AsArray().OfType<JsonObject>())
        {
            if(line.B("matched"))continue;
            decimal amount=line.N("amount");
            // Match open journal lines on the same bank account with equal absolute amount and correct side.
            string sql=amount>0
                ?$"""
                    SELECT TOP 1 jl.id FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                    JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                    WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
                      AND jl.debit=@amt
                      AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"treasury","bank_match")} m WHERE m.journal_line_id=jl.id AND m.status=1)
                    ORDER BY jl.line_no
                    """
                :$"""
                    SELECT TOP 1 jl.id FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                    JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                    WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
                      AND jl.credit=@amt
                      AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"treasury","bank_match")} m WHERE m.journal_line_id=jl.id AND m.status=1)
                    ORDER BY jl.line_no
                    """;
            if(!SchemaSql.IsSql(c))
                sql=sql.Replace("SELECT TOP 1","SELECT",StringComparison.Ordinal)+" LIMIT 1";
            // absolute amount for credit/debit compare
            decimal abs=J.Money(Math.Abs(amount));
            object? jl=SchemaSql.Scalar(c,tx,sql,("@c",companyId),("@a",chartAccountId),("@amt",abs));
            if(jl is null or DBNull){unmatched.Add(line.S("id"));continue;}
            string journalLineId=jl switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(jl,CultureInfo.InvariantCulture)!};
            SchemaTreasury.LockMatchSources(c,tx,line.S("id"),journalLineId);
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"treasury","bank_match")}
                (id,statement_line_id,journal_line_id,company_id,period_month,amount,status,created_by,created_at)
                VALUES(@id,@s,@j,@c,@m,@a,1,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",line.S("id")),("@j",journalLineId),
                ("@c",companyId),("@m",statement.S("periodMonth").Length>=10?statement.S("periodMonth")[..10]:statement.S("periodMonth")+"-01"),
                ("@a",abs),("@by",actor.Identity!.Name!));
            matched++;
        }
        Store.Audit(c,tx,"treasury.bank_statement_auto_matched",id,actor.Identity!.Name!);
        var view=View(c,tx,actor,id);
        view["matchedCount"]=matched;view["unmatchedLineIds"]=unmatched;
        return view;
    }

    public static JsonObject RequestAdjust(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        var statement=View(c,tx,actor,id);
        decimal amount=J.Money(PayValues.Number(input,"amount",-1000000000));
        J.Need(amount!=0,"مبلغ التسوية لا يكون صفرًا.");
        string reason=input.S("reason").Trim();J.Need(reason.Length is >=3 and <=500,"سبب التسوية من 3 إلى 500 حرف.");
        string adjustId=SchemaSql.GuidText(SchemaSql.NewId());
        var draft=new JsonObject
        {
            ["id"]=adjustId,["kind"]="bank_statement_adjust",["statementId"]=id,
            ["companyId"]=statement.S("companyId"),["amount"]=amount,["reason"]=reason,
            ["status"]="pending_approval",["createdBy"]=actor.Identity!.Name,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["approvedBy"]="",["journalId"]=""
        };
        Store.Save(c,"bank_statement_adjust",draft,tx);
        Store.Audit(c,tx,"treasury.bank_adjust_requested",adjustId,actor.Identity!.Name!);
        draft["allowedActions"]=new JsonArray("approve");
        draft["noteAr"]="التسوية البنكية تحتاج اعتمادًا قبل ترحيل القيد.";
        return draft;
    }

    public static JsonObject ApproveAdjust(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string adjustId)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"approvals.decide")||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية اعتماد التسوية مطلوبة.",403);
        SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);SchemaTreasury.Ensure(c,tx);
        var draft=Store.Get(c,adjustId,"bank_statement_adjust",tx)??throw new ApiError("طلب التسوية غير موجود.",404);
        J.Need(draft.S("status")=="pending_approval","التسوية ليست بانتظار الاعتماد.");
        J.Need(AccessPolicy.Independent(actor,draft.S("createdBy"),"treasury.bank_adjust_approve",c,tx,draft,scope:AccessPolicy.ScopePayApprovals),
            "لا يجوز اعتماد تسوية أنشأتها؛ يلزم مراجع مستقل.",403);
        var statement=View(c,tx,actor,draft.S("statementId"));
        string companyId=statement.S("companyId");
        string bankAccountId=statement.S("bankAccountId");
        string chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
            chartAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);
        // Timing / recon adjust: over → 422202 income, short → 542102 expense (same CoA pair as vault over/short).
        string clearingId=AccountIdByCode(c,tx,draft.N("amount")>0?"422202":"542102");

        string periodRaw=statement.S("periodMonth");
        if(periodRaw.Length==7)periodRaw+="-01";
        DateOnly month=DateOnly.ParseExact(periodRaw.Length>=10?periodRaw[..10]:periodRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        month=new DateOnly(month.Year,month.Month,1);
        Guid periodId=EnsureOpenPeriod(c,tx,companyId,month);
        decimal amount=J.Money(Math.Abs(draft.N("amount")));
        var lines=new List<JournalPosting.Line>();
        if(draft.N("amount")>0)
        {
            lines.Add(new(Guid.Parse(chartAccountId),SchemaOrgCostCentre.NoneId,"none",amount,0m,draft.S("reason")));
            lines.Add(new(Guid.Parse(clearingId),SchemaOrgCostCentre.NoneId,"none",0m,amount,draft.S("reason")));
        }
        else
        {
            lines.Add(new(Guid.Parse(clearingId),SchemaOrgCostCentre.NoneId,"none",amount,0m,draft.S("reason")));
            lines.Add(new(Guid.Parse(chartAccountId),SchemaOrgCostCentre.NoneId,"none",0m,amount,draft.S("reason")));
        }
        var posted=JournalPosting.Post(c,tx,new(
            Guid.Parse(companyId),periodId,month,month.AddDays(14),
            "bank_statement_adjust",Guid.Parse(adjustId),"bank_recon_adjust",0,false,actor.Identity!.Name!,lines,
            NarrationAr:draft.S("reason")));
        draft["status"]="approved";draft["approvedBy"]=actor.Identity!.Name;draft["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");
        draft["journalId"]=SchemaSql.GuidText(posted.JournalId);
        Store.Save(c,"bank_statement_adjust",draft,tx);
        Store.Audit(c,tx,"treasury.bank_adjust_approved",adjustId,actor.Identity!.Name!);
        draft["allowedActions"]=new JsonArray();
        return draft;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/treasury/bank-statements/upload",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Upload(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Upload bank statement file lines");
        api.MapGet("/treasury/bank-statements/{id}",(string id,HttpContext ctx)=>store.Read(c=>View(c,null,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Read one bank statement");
        api.MapPost("/treasury/bank-statements/{id}/auto-match",(string id,HttpContext ctx)=>store.Write((c,tx)=>AutoMatch(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Auto-match statement lines to journal lines");
        api.MapPost("/treasury/bank-statements/{id}/adjust",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>RequestAdjust(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Request bank recon adjustment (needs approval)");
        api.MapPost("/treasury/bank-adjustments/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>ApproveAdjust(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Approve bank recon adjustment and post journal");
    }

    static JsonObject View(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,company_id,registration_id,bank_account_id,period_month,opening_balance,closing_balance,status,created_by
            FROM {SchemaSql.Q(c,"treasury","bank_statement")} WHERE id=@id
            """,("@id",id));
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"كشف البنك غير موجود.",404);
        string companyId=r["company_id"].ToString()!;
        FinancialScopes.DemandSponsor(actor,companyId);
        var doc=new JsonObject
        {
            ["id"]=r["id"].ToString(),["companyId"]=companyId,["registrationId"]=r["registration_id"].ToString(),
            ["bankAccountId"]=r["bank_account_id"].ToString(),
            ["periodMonth"]=DateOnlyText(r["period_month"]),
            ["openingBalance"]=J.Money(Convert.ToDecimal(r["opening_balance"],CultureInfo.InvariantCulture)),
            ["closingBalance"]=J.Money(Convert.ToDecimal(r["closing_balance"],CultureInfo.InvariantCulture)),
            ["statusCode"]=Convert.ToInt32(r["status"],CultureInfo.InvariantCulture),
            ["status"]=StatusName(Convert.ToInt32(r["status"],CultureInfo.InvariantCulture)),
            ["createdBy"]=r["created_by"] is DBNull?"":r["created_by"].ToString()
        };
        r.Close();
        var lines=new JsonArray();
        using var lineCmd=Store.Command(c,tx,$"""
            SELECT l.id,l.line_no,l.occurred_on,l.amount,l.reference,
                   CASE WHEN EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"treasury","bank_match")} m WHERE m.statement_line_id=l.id AND m.status=1) THEN 1 ELSE 0 END AS matched
            FROM {SchemaSql.Q(c,"treasury","bank_statement_line")} l WHERE l.statement_id=@id ORDER BY l.line_no
            """,("@id",id));
        using var lr=lineCmd.ExecuteReader();
        while(lr.Read())
            lines.Add(new JsonObject
            {
                ["id"]=lr["id"].ToString(),["lineNo"]=Convert.ToInt32(lr["line_no"],CultureInfo.InvariantCulture),
                ["occurredOn"]=DateOnlyText(lr["occurred_on"]),
                ["amount"]=J.Money(Convert.ToDecimal(lr["amount"],CultureInfo.InvariantCulture)),
                ["reference"]=lr["reference"] is DBNull?"":lr["reference"].ToString(),
                ["matched"]=Convert.ToInt32(lr["matched"],CultureInfo.InvariantCulture)==1
            });
        doc["lines"]=lines;
        var actions=new JsonArray();
        if(AccessPolicy.Has(actor,"treasury.bank_reconcile")||AccessPolicy.Administrator(actor))
        {
            if(doc.N("statusCode") is 1 or 2){actions.Add("auto_match");actions.Add("adjust");}
        }
        doc["allowedActions"]=actions;
        return doc;
    }

    static string AccountIdByCode(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        J.Need(id is not null and not DBNull,$"حساب {code} غير مهيأ.",409);
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
    static Guid EnsureOpenPeriod(DbConnection c,DbTransaction tx,string companyId,DateOnly month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",
            ("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})",
            ("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        return id;
    }
    static DateOnly ParseMonth(string raw)
    {
        if(raw.Length==7)raw+="-01";
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"شهر الكشف غير صالح.");
        return new DateOnly(d.Year,d.Month,1);
    }
    static DateOnly ParseDay(string raw)
    {
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"تاريخ السطر غير صالح.");
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
