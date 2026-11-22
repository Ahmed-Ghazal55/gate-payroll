using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-15b: final salary approval after debt confirm (M-0670); bypass M-0677; statement snapshot X10;
/// negative net → new debt 112507 here only. Positive net → accrual journal + payable (Codex RWC4).
/// Ordinary salary self-approval allowed by permission (§35 / answer 4); shared-hold release stays two-person.</summary>
public static class SalSalaryApprove
{
    public const string ApprovePermission="pay.approve";

    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ApprovePermission);
        Ensure(c,tx);
        var run=LoadRun(c,tx,actor,payRunId);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);
        var ids=(input["payRunRiderIds"] as JsonArray??[]).Select(x=>x!.ToString()!).Where(s=>s!="").ToList();
        J.Need(ids.Count>0,"حدد مناديب الاعتماد.",400);
        var outRows=new JsonArray();
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        // WC4D H11: re-detect every app each rider worked this month before any approval.
        var worked=SalSharedRiders.WorkedApps(c,tx,run.S("periodMonth"));
        foreach(string riderId in ids)
        {
            var rider=LoadRider(c,tx,payRunId,riderId);
            J.Need(rider.S("debtConfirmStatus") is "confirmed" or "deferred","أكمل تأكيد الدين قبل اعتماد الراتب.",409);
            J.Need(string.IsNullOrEmpty(rider.S("salaryApprovedAt")),"الراتب معتمد مسبقاً.",409);
            SalSharedRiders.DemandApprovable(c,tx,worked,rider.S("erpRiderId"),run.S("periodMonth"),run.S("app"));

            string calcBy=rider.S("createdBy",run.S("createdBy"));
            bool self=AccessPolicy.SamePerson(actor,calcBy);
            // Answer 4: permission-based salary approval; audit self-approval. Two-person retained for money-out / shared-hold release elsewhere.
            if(self)Store.Audit(c,tx,"sal.salary_self_approved",riderId,actor.Identity!.Name!);

            bool negDebt=false;
            decimal net=rider.N("salaryNet");
            bool keetaIneligible=rider.B("keetaIneligible");
            string? payableId=null;

            // Settle linked L1/L2 debt_id rows before the approval stamp (SchemaDebt source guard).
            decimal linkedSettled=SettleLinkedDebts(c,tx,actor,run,rider,now);

            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET salary_approved_at=@at,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@at",now),("@by",actor.Identity!.Name!),("@id",riderId));

            if(net<0&&!keetaIneligible)
                negDebt=CreateNegativeCarryover(c,tx,actor,run,rider,now);
            // WC4D H3: accrue even when the final net is zero (500 − confirmed debt 500 → accrual 500, payable none).
            payableId=CreatePayableAndAccrual(c,tx,actor,run,rider,now,linkedSettled,negDebt?J.Money(-net):0m);

            FreezeStatement(c,tx,actor,run,rider,now,payableId);
            outRows.Add(new JsonObject
            {
                ["payRunRiderId"]=riderId,["erpRiderId"]=rider.S("erpRiderId"),
                ["salaryApprovedAt"]=now,["salaryNet"]=net,["negativeDebtCreated"]=negDebt,
                ["payableId"]=payableId??"",["selfApproved"]=self
            });
        }
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run")} SET stage=CASE WHEN stage<9 THEN 9 ELSE stage END,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@by",actor.Identity!.Name!),("@at",now),("@id",payRunId));
        Store.Audit(c,tx,"sal.salary_approved",payRunId,actor.Identity!.Name!);
        return new(){["payRunId"]=payRunId,["rows"]=outRows,["allowedActions"]=new JsonArray("export_bank_file")};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/pay-runs/{id}/approve-salary",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Approve(c,tx,ctx.User,id,input);
        })).WithTags("Payroll").WithSummary("Final salary approval after debt confirmation");
    }

    /// <summary>212103 must end at the payable: accrual − confirmed debt − linked settlements + negative carry-over = max(0,net).
    /// So accrual = payable + confirmed + linked − carry-over; posted whenever positive (net may be zero). Payable only when net&gt;0.</summary>
    static string? CreatePayableAndAccrual(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now,decimal linkedDebt,decimal carryover)
    {
        decimal netPay=J.Money(Math.Max(0,rider.N("salaryNet")));
        decimal confirmedDebt=J.Money(rider.N("oldDebtInstalment"));
        decimal entitlement=J.Money(netPay+confirmedDebt+linkedDebt-carryover);
        if(entitlement>0)PostAccrual(c,tx,actor,run,rider,entitlement);
        if(netPay<=0)return null;
        string payableId=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"pay","payable")}
            (id,kind,source_table,source_id,company_id,erp_rider_id,amount_due,currency_code,status,period_month,created_by,created_at)
            VALUES(@id,'pay_run_rider','sal.pay_run_rider',@src,@c,@erp,@amt,'SAR',1,@pm,@by,@at)
            """,("@id",payableId),("@src",rider.S("id")),("@c",run.S("companyId")),("@erp",rider.S("erpRiderId")),
            ("@amt",netPay),("@pm",run.S("periodMonth")),("@by",actor.Identity!.Name!),("@at",now));

        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
            SET payable_id=@p,bank_eligible=CASE WHEN run_mode=0 AND @pb=0 THEN 1 ELSE 0 END,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@p",payableId),("@pb",run.B("payoutBlocked")?1:0),("@by",actor.Identity!.Name!),("@at",now),("@id",rider.S("id")));
        return payableId;
    }

    static void PostAccrual(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,decimal entitlement)
    {
        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid expenseAcc=AccountId(c,tx,"512101");
        Guid payableAcc=AccountId(c,tx,"212103");
        JournalPosting.Post(c,tx,new JournalPosting.Draft(
            company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            "sal_pay_run_rider",Guid.Parse(rider.S("id")),"salary_accrual",(byte)run.N("runMode"),run.B("payoutBlocked"),
            actor.Identity!.Name!,
            [
                new(expenseAcc,SchemaOrgCostCentre.NoneId,run.S("app"),entitlement,0m,"استحقاق راتب"),
                new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,entitlement,"استحقاق راتب")
            ]));
    }

    /// <summary>RWC4b H12: settle each pay_run_item.debt_id for the actual deducted amount at approval. Returns the settled total.</summary>
    static decimal SettleLinkedDebts(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now)
    {
        decimal settled=0;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT debt_id,amount,account_code FROM {SchemaSql.Q(c,"sal","pay_run_item")}
            WHERE pay_run_rider_id=@r AND amount>0 AND debt_id IS NOT NULL AND item_code<>'old_debt_instalment'
            """;
        Add(cmd,"@r",rider.S("id"));
        using var r=cmd.ExecuteReader();
        var rows=new List<(string DebtId,decimal Amount,string Account)>();
        while(r.Read())
            rows.Add((r["debt_id"].ToString()!,Convert.ToDecimal(r["amount"]),r["account_code"].ToString()!));
        r.Close();
        foreach(var row in rows)
        {
            decimal locked=SchemaDebt.LockDebt(c,tx,row.DebtId);
            // WC4D H12: a stale/exhausted link would leave accrual ≠ payable; reject so the run is recalculated.
            J.Need(J.Money(row.Amount)<=locked,"رصيد الدين المربوط تغيّر بعد الاحتساب؛ ألغِ الدورة وأعد الاحتساب.",409);
            decimal take=J.Money(row.Amount);
            if(take<=0)continue;
            Guid company=Guid.Parse(run.S("companyId"));
            Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
            Guid payableAcc=AccountId(c,tx,"212103");
            Guid debtAcc=AccountId(c,tx,row.Account is {Length:>0}?row.Account:"112507");
            var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",SchemaSql.NewId(),"salary_debt_settle",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,
                [
                    new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),take,0m,"تسوية دين مربوط"),
                    new(debtAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,take,"تسوية دين مربوط")
                ]));
            string movId=SchemaSql.GuidText(SchemaSql.NewId());
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
                (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month,journal_id,run_mode,payout_blocked,created_by,created_at)
                VALUES(@id,@pm,@debt,@c,@on,@amt,'pay_item_debt_settle',@src,@rider,@pm,@j,0,@pb,@by,@at)
                """,("@id",movId),("@pm",run.S("periodMonth")),("@debt",row.DebtId),("@c",run.S("companyId")),
                ("@on",run.S("periodMonth")),("@amt",-take),("@src",movId),("@rider",rider.S("id")),
                ("@j",SchemaSql.GuidText(journal.JournalId)),("@pb",run.B("payoutBlocked")?1:0),
                ("@by",actor.Identity!.Name!),("@at",now));
            settled=J.Money(settled+take);
        }
        return settled;
    }

    static bool CreateNegativeCarryover(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now)
    {
        decimal amount=J.Money(-rider.N("salaryNet"));
        J.Need(amount>0,"لا مديونية عجز بدون صافٍ سالب.",400);
        string debtId=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","debt")}
            (id,erp_rider_id,company_id,category,principal,balance,instalment,status,created_by,created_at)
            VALUES(@id,@erp,@c,'NegativeSalaryCarryover',@amt,0,0,'open',@by,@at)
            """,("@id",debtId),("@erp",rider.S("erpRiderId")),("@c",run.S("companyId")),
            ("@amt",amount),("@by",actor.Identity!.Name!),("@at",now));

        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid debtAcc=AccountId(c,tx,"112507");
        Guid payableAcc=AccountId(c,tx,"212103");
        var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            "sal_pay_run_rider",Guid.Parse(rider.S("id")),"salary_approval",(byte)run.N("runMode"),run.B("payoutBlocked"),
            actor.Identity!.Name!,
            [
                new(debtAcc,SchemaOrgCostCentre.NoneId,run.S("app"),amount,0m,"عجز راتب مرحل"),
                new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,amount,"عجز راتب مرحل")
            ]));

        string movId=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
            (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month,journal_id,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@pm,@debt,@c,@on,@amt,'carryover_112507',@src,@rider,@pm,@j,0,@pb,@by,@at)
            """,("@id",movId),("@pm",run.S("periodMonth")),("@debt",debtId),("@c",run.S("companyId")),
            ("@on",run.S("periodMonth")),("@amt",amount),("@src",movId),("@rider",rider.S("id")),
            ("@j",SchemaSql.GuidText(journal.JournalId)),("@pb",run.B("payoutBlocked")?1:0),
            ("@by",actor.Identity!.Name!),("@at",now));
        return true;
    }

    static void FreezeStatement(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now,string? payableId)
    {
        var items=new JsonArray();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT line_no,stage_code,item_code,amount,affects_net,account_code
            FROM {SchemaSql.Q(c,"sal","pay_run_item")} WHERE pay_run_rider_id=@r AND amount<>0 ORDER BY line_no
            """;
        Add(cmd,"@r",rider.S("id"));
        using var r=cmd.ExecuteReader();
        while(r.Read())
            items.Add(new JsonObject
            {
                ["lineNo"]=Convert.ToInt32(r["line_no"]),["stage"]=r["stage_code"].ToString(),
                ["itemCode"]=r["item_code"].ToString(),["amount"]=Convert.ToDecimal(r["amount"]),
                ["affectsNet"]=Convert.ToInt32(r["affects_net"])==1,["accountCode"]=r["account_code"].ToString()
            });
        r.Close();
        var payload=new JsonObject
        {
            ["payRunId"]=run.S("id"),["app"]=run.S("app"),["periodMonth"]=run.S("periodMonth"),
            ["erpRiderId"]=rider.S("erpRiderId"),["gross"]=rider.N("gross"),["commission"]=rider.N("commission"),
            ["salaryNet"]=rider.N("salaryNet"),["debtConfirmStatus"]=rider.S("debtConfirmStatus"),
            ["payableId"]=payableId??"",["items"]=items,["frozenAt"]=now
        };
        string snapId=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","rider_statement_snapshot")}
            (id,period_month,pay_run_rider_id,erp_rider_id,company_id,app_code,payload,frozen_at,frozen_by,created_by,created_at)
            VALUES(@id,@pm,@r,@erp,@c,@app,@payload,@at,@by,@by,@at)
            """,("@id",snapId),("@pm",run.S("periodMonth")),("@r",rider.S("id")),("@erp",rider.S("erpRiderId")),
            ("@c",run.S("companyId")),("@app",run.S("app")),
            ("@payload",payload.ToJsonString(new JsonSerializerOptions{WriteIndented=false})),
            ("@at",now),("@by",actor.Identity!.Name!));
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);
        SchemaFinPeriod.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);SchemaDebt.Ensure(c,tx);SchemaOperationCapture.Ensure(c,tx);
        SchemaSalStatement.Ensure(c,tx);
    }

    static JsonObject LoadRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT id,company_id,app_code,period_month,run_mode,payout_blocked,created_by FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id";
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"مسير الرواتب غير موجود.",404);
        var run=new JsonObject
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,["app"]=r["app_code"].ToString()!,
            ["periodMonth"]=ReadMonth(r["period_month"]),["runMode"]=Convert.ToInt32(r["run_mode"]),
            ["payoutBlocked"]=Convert.ToInt32(r["payout_blocked"])==1,
            ["createdBy"]=r["created_by"] is DBNull?"":r["created_by"].ToString()!
        };
        r.Close();
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        var platform=PayPlatforms.For(run.S("app"));
        J.Need(FinancialScopes.Platform(actor,platform.Platform),"المنصة خارج نطاق المستخدم.",403);
        return run;
    }

    static JsonObject LoadRider(DbConnection c,DbTransaction tx,string payRunId,string riderId)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,erp_rider_id,gross,commission,salary_net,old_debt_instalment,debt_confirm_status,salary_approved_at,keeta_ineligible,created_by
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} WHERE id=@id AND pay_run_id=@run
            """;
        Add(cmd,"@id",riderId);Add(cmd,"@run",payRunId);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"المندوب غير موجود في المسير.",404);
        return new()
        {
            ["id"]=r["id"].ToString()!,["erpRiderId"]=r["erp_rider_id"].ToString()!,
            ["gross"]=Convert.ToDecimal(r["gross"]),["commission"]=Convert.ToDecimal(r["commission"]),
            ["salaryNet"]=Convert.ToDecimal(r["salary_net"]),
            ["oldDebtInstalment"]=Convert.ToDecimal(r["old_debt_instalment"]),
            ["debtConfirmStatus"]=r["debt_confirm_status"].ToString()!,
            ["salaryApprovedAt"]=r["salary_approved_at"] is DBNull?"":r["salary_approved_at"].ToString()!,
            ["keetaIneligible"]=Convert.ToInt32(r["keeta_ineligible"])==1,
            ["createdBy"]=r["created_by"] is DBNull?"":r["created_by"].ToString()!
        };
    }

    static Guid ResolvePeriod(DbConnection c,DbTransaction tx,string companyId,string month)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@pm",
            ("@c",companyId),("@pm",month));
        J.Need(id is not null && id is not DBNull,"الفترة المحاسبية غير مفتوحة لهذا الشهر.",409);
        return Guid.Parse(id!.ToString()!);
    }

    static Guid AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@code AND is_postable=1",("@code",code));
        J.Need(id is not null && id is not DBNull,"حساب الترحيل غير موجود: "+code,409);
        return Guid.Parse(id!.ToString()!);
    }

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>s.Length==7?s+"-01":s,
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };
    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (n,v) in values)Add(cmd,n,v);
        return cmd.ExecuteNonQuery();
    }
    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}
}
