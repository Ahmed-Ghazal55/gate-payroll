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
            SettleLinkedDebts(c,tx,actor,run,rider,now);

            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET salary_approved_at=@at,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@at",now),("@by",actor.Identity!.Name!),("@id",riderId));

            // RWC4a1 New HIGH: post entitlement and recoveries separately so −30 does not leave orphan 212103 credit.
            PostEntitlementAccrual(c,tx,actor,run,rider);
            // RWC4a3F H8 §22.9.2: protected salary operating legs (full D/fuel credit + unrecovered 512121).
            PostKeetaProtected(c,tx,actor,run,rider);
            PostPersonalRecoveries(c,tx,actor,run,rider);
            // RWC4m2: reverse shared L1 duplicates once per cleared hold even when payable boost is 0.
            PostSharedDuplicatesOnce(c,tx,actor,run,rider);
            if(net<0&&!rider.B("protectedSalary"))
                negDebt=CreateNegativeCarryover(c,tx,actor,run,rider,now);
            payableId=CreatePayable(c,tx,actor,run,rider,now);

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
        // RWC4a3F LOW: no W2 bank-file writer yet (U-W2-17) — never advertise export_bank_file.
        return new(){["payRunId"]=payRunId,["rows"]=outRows,["allowedActions"]=new JsonArray()};
    }

    /// <summary>RWC4a3F H8 / §22.9.2: for each operating item post Dr recovered(212103)+Dr unrecovered(512121) / Cr control account for the full amount.
    /// Vehicle → 421101 car / 421102 bike; fuel → 112511; housing → 421401. Excluded from PostPersonalRecoveries.</summary>
    static void PostKeetaProtected(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider)
    {
        if(!string.Equals(run.S("app"),"keeta",StringComparison.OrdinalIgnoreCase))return;
        bool protectedSalary=rider.B("protectedSalary")
            ||(rider.B("keetaIneligible")&&string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase));
        if(!protectedSalary)return;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT item_code,amount,account_code,affects_net FROM {SchemaSql.Q(c,"sal","pay_run_item")}
            WHERE pay_run_rider_id=@r AND amount>0 AND stage_code='level1'
              AND (item_code IN ('fuel','vehicle','housing') OR item_code LIKE 'fuel_%' OR item_code LIKE 'vehicle_%' OR item_code LIKE 'housing_%')
            ORDER BY line_no
            """;
        Add(cmd,"@r",rider.S("id"));
        using var r=cmd.ExecuteReader();
        var rows=new List<(string Code,decimal Amount,string Account,bool Affects)>();
        while(r.Read())
            rows.Add((r["item_code"].ToString()!,Convert.ToDecimal(r["amount"]),r["account_code"].ToString()!,Convert.ToInt32(r["affects_net"])==1));
        r.Close();
        if(rows.Count==0)return;
        // Group recovered + unrecovered by base code so one journal credits the full control amount.
        var groups=new Dictionary<string,(decimal Recovered,decimal Unrecovered,string Account)>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            string baseCode=row.Code.EndsWith("_unrecovered",StringComparison.Ordinal)?row.Code[..^"_unrecovered".Length]:row.Code;
            groups.TryGetValue(baseCode,out var g);
            if(row.Affects)g.Recovered=J.Money(g.Recovered+row.Amount);
            else g.Unrecovered=J.Money(g.Unrecovered+row.Amount);
            if(row.Account is {Length:>0}&&!string.Equals(row.Account,"512121",StringComparison.Ordinal))
                g.Account=row.Account;
            else if(g.Account is not {Length:>0})
                g.Account=ProtectedControlAccount(baseCode,rider);
            groups[baseCode]=g;
        }
        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid payableAcc=AccountId(c,tx,"212103");
        Guid unrecoveredAcc=AccountId(c,tx,"512121");
        foreach(var (code,g) in groups)
        {
            decimal full=J.Money(g.Recovered+g.Unrecovered);
            if(full<=0)continue;
            string ctrl=g.Account is {Length:>0}?g.Account:ProtectedControlAccount(code,rider);
            Guid creditAcc=AccountId(c,tx,ctrl);
            var lines=new List<JournalPosting.Line>();
            // Pair rule: until row-12 fills rider+account together, leave both null here.
            if(g.Recovered>0)lines.Add(new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),g.Recovered,0m,"استرداد محمي كيتا"));
            if(g.Unrecovered>0)lines.Add(new(unrecoveredAcc,SchemaOrgCostCentre.NoneId,run.S("app"),g.Unrecovered,0m,"غير مسترد §22.9.2"));
            lines.Add(new(creditAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,full,"استقطاع محمي كيتا"));
            JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",SchemaSql.NewId(),"salary_keeta_protected",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,lines));
        }
    }

    static string ProtectedControlAccount(string baseCode,JsonObject rider)
    {
        if(baseCode.StartsWith("fuel",StringComparison.OrdinalIgnoreCase))return "112511";
        if(baseCode.StartsWith("housing",StringComparison.OrdinalIgnoreCase))return "421401";
        string vehicleType=rider.S("vehicleType","car").ToLowerInvariant();
        return vehicleType is "bike" or "motorcycle" or "scooter"?"421102":"421101";
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/pay-runs/{id}/approve-salary",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Approve(c,tx,ctx.User,id,input);
        })).WithTags("Payroll").WithSummary("Final salary approval after debt confirmation");
    }

    /// <summary>Platform entitlement = gross − commission (Chefz: RiderAmount already net). Posted whenever positive.
    /// COA1 B2: salary scheme → Dr 512101 / Cr 212103; commission (agent) → Dr receivable / Cr 212103 + Cr agent revenue.</summary>
    static void PostEntitlementAccrual(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider)
    {
        decimal gross=J.Money(rider.N("gross"));
        decimal commission=J.Money(rider.N("commission"));
        decimal entitlement=J.Money(gross-commission);
        if(entitlement<=0&&commission<=0)return;
        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid payableAcc=AccountId(c,tx,"212103");
        string scheme=rider.S("schemeType","commission");
        string app=PayPlatforms.App(run.S("app"));
        bool commissionScheme=!string.Equals(scheme,"salary",StringComparison.OrdinalIgnoreCase)
            &&PayPlatforms.For(app).Agent is {Length:>0};
        if(commissionScheme)
        {
            // BR §23.5 / §24.8: Dr platform receivable (gross) / Cr 212103 (entitlement) + Cr agent revenue (commission).
            if(gross<=0)return;
            Guid receivable=AccountId(c,tx,PayPlatforms.Active(PayPlatforms.For(app).Receivable));
            string agentCode=PayPlatforms.Revenue(app,"agent");
            Guid revenueAcc=AccountId(c,tx,agentCode);
            var lines=new List<JournalPosting.Line>
            {
                new(receivable,SchemaOrgCostCentre.NoneId,app,gross,0m,"استحقاق عمولة — مدين المنصة"),
                new(payableAcc,SchemaOrgCostCentre.NoneId,app,0m,entitlement,"استحقاق عمولة — دائن المندوب")
            };
            if(commission>0)lines.Add(new(revenueAcc,SchemaOrgCostCentre.NoneId,app,0m,commission,"إيراد عمولة الوكيل"));
            JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",Guid.Parse(rider.S("id")),"salary_accrual",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,lines));
            return;
        }
        if(entitlement<=0)return;
        Guid expenseAcc=AccountId(c,tx,"512101");
        JournalPosting.Post(c,tx,new JournalPosting.Draft(
            company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
            "sal_pay_run_rider",Guid.Parse(rider.S("id")),"salary_accrual",(byte)run.N("runMode"),run.B("payoutBlocked"),
            actor.Identity!.Name!,
            [
                new(expenseAcc,SchemaOrgCostCentre.NoneId,app,entitlement,0m,"استحقاق راتب"),
                new(payableAcc,SchemaOrgCostCentre.NoneId,app,0m,entitlement,"استحقاق راتب")
            ]));
    }

    /// <summary>Recover personal L1/L2 that still sit on 212103 (skip linked debt_id — settled earlier; skip old_debt — settled at confirm).
    /// RWC4a3F H8: Keeta protected operating items are posted by PostKeetaProtected.</summary>
    static void PostPersonalRecoveries(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider)
    {
        bool skipProtectedOps=rider.B("protectedSalary");
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT item_code,amount,account_code FROM {SchemaSql.Q(c,"sal","pay_run_item")}
            WHERE pay_run_rider_id=@r AND amount>0 AND affects_net=1
              AND stage_code IN ('level1','level2')
              AND item_code<>'old_debt_instalment'
              AND debt_id IS NULL
            """;
        Add(cmd,"@r",rider.S("id"));
        using var r=cmd.ExecuteReader();
        var rows=new List<(string Code,decimal Amount,string Account)>();
        while(r.Read())
        {
            string code=r["item_code"].ToString()!;
            if(skipProtectedOps&&(code is "fuel" or "vehicle" or "housing"
                ||code.StartsWith("fuel_",StringComparison.Ordinal)
                ||code.StartsWith("vehicle_",StringComparison.Ordinal)
                ||code.StartsWith("housing_",StringComparison.Ordinal)))
                continue;
            rows.Add((code,Convert.ToDecimal(r["amount"]),r["account_code"].ToString()!));
        }
        r.Close();
        if(rows.Count==0)return;
        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid payableAcc=AccountId(c,tx,"212103");
        foreach(var row in rows)
        {
            Guid recoveryAcc=AccountId(c,tx,row.Account is {Length:>0}?row.Account:"112505");
            JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",SchemaSql.NewId(),"salary_recovery",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,
                [
                    new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),row.Amount,0m,"استرداد من الراتب"),
                    new(recoveryAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,row.Amount,"استرداد من الراتب")
                ]));
        }
    }

    /// <summary>Payable only when final net &gt; 0. Accrual and recoveries are posted separately.
    /// RWC4a2 H11 / §40.5: multi-app cleared hold — payables across apps consume combined_net (shared L1 once).</summary>
    static string? CreatePayable(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now)
    {
        decimal appNet=J.Money(Math.Max(0,rider.N("salaryNet")));
        decimal netPay=CombinedPayableAmount(c,tx,run,rider,appNet);
        if(netPay<=0)return null;
        string payableId=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"pay","payable")}
            (id,kind,source_table,source_id,company_id,erp_rider_id,amount_due,currency_code,status,period_month,created_by,created_at)
            VALUES(@id,'pay_run_rider','sal.pay_run_rider',@src,@c,@erp,@amt,'SAR',1,@pm,@by,@at)
            """,("@id",payableId),("@src",rider.S("id")),("@c",run.S("companyId")),("@erp",rider.S("erpRiderId")),
            ("@amt",netPay),("@pm",run.S("periodMonth")),("@by",actor.Identity!.Name!),("@at",now));

        bool blockedByHold=SalRiderRequests.HasActiveBankBlockingHold(c,tx,rider.S("erpRiderId"),run.S("periodMonth"),run.S("app"));
        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
            SET payable_id=@p,bank_eligible=CASE WHEN run_mode=0 AND @pb=0 AND @held=0 THEN 1 ELSE 0 END,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@p",payableId),("@pb",run.B("payoutBlocked")?1:0),("@held",blockedByHold?1:0),("@by",actor.Identity!.Name!),("@at",now),("@id",rider.S("id")));
        return payableId;
    }

    /// <summary>§40.5: last approved app takes the remaining combined_net; earlier apps take their own net capped by remaining.
    /// RWC4a3: a consolidated zero/negative combined_net pays 0 (exception release with pending hold apps still uses appNet).</summary>
    static decimal CombinedPayableAmount(DbConnection c,DbTransaction tx,JsonObject run,JsonObject rider,decimal appNet)
    {
        object? holdIdObj=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'",
            ("@erp",rider.S("erpRiderId")),("@pm",run.S("periodMonth")));
        if(holdIdObj is null or DBNull)return appNet;
        string holdId=holdIdObj.ToString()!;
        object? combinedObj=SchemaSql.Scalar(c,tx,$"SELECT combined_net FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE id=@id",("@id",holdId));
        decimal combined=J.Money(Convert.ToDecimal(combinedObj??0m));
        // Exception release clears before every app is received — fall back to each app's own net.
        long pendingHoldApps=Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(*) FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND received=0",("@h",holdId))??0L);
        if(pendingHoldApps>0)return appNet;
        if(combined<=0)return 0m;
        decimal already=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(p.amount_due),0) FROM {SchemaSql.Q(c,"pay","payable")} p
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run_rider")} r ON r.id=p.source_id
            WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND r.salary_approved_at IS NOT NULL AND r.id<>@id
            """,("@erp",rider.S("erpRiderId")),("@pm",run.S("periodMonth")),("@id",rider.S("id")))??0m);
        decimal remaining=J.Money(combined-already);
        long pendingOthers=Convert.ToInt64(SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(*) FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
            WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4 AND r.salary_approved_at IS NULL AND r.id<>@id
            """,("@erp",rider.S("erpRiderId")),("@pm",run.S("periodMonth")),("@id",rider.S("id")))??0L);
        if(pendingOthers==0)return J.Money(Math.Max(0,remaining));
        return J.Money(Math.Min(appNet,Math.Max(0,remaining)));
    }

    /// <summary>RWC4m2 / §40.5: reverse each duplicate shared L1 against its own account once per cleared hold,
    /// independent of payable boost (combined 0 still needs the add-back on 212103).</summary>
    static void PostSharedDuplicatesOnce(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider)
    {
        object? holdIdObj=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
            WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'
            """,("@erp",rider.S("erpRiderId")),("@pm",run.S("periodMonth")));
        if(holdIdObj is null or DBNull)return;
        string holdId=holdIdObj.ToString()!;
        var parts=SharedDuplicateParts(c,tx,rider.S("erpRiderId"),run.S("periodMonth"));
        if(parts.Count==0)return;
        Guid company=Guid.Parse(run.S("companyId"));
        Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
        Guid payableAcc=AccountId(c,tx,"212103");
        int i=0;
        foreach(var part in parts)
        {
            decimal take=J.Money(part.Amount);
            if(take<=0)continue;
            string accCode=part.Account is {Length:>0}?part.Account:"112511";
            Guid sourceId=StableSharedOnceSource(holdId,part.Code,accCode,i++);
            // Idempotent across apps: first approval posts; later apps must not rewrite lines with a different app_code.
            object? already=SchemaSql.Scalar(c,tx,$"""
                SELECT 1 FROM {SchemaSql.Q(c,"fin","journal")}
                WHERE source_type='sal_pay_run_rider' AND source_id=@sid AND company_id=@c AND purpose='salary_shared_once' AND status=2
                """,("@sid",SchemaSql.GuidText(sourceId)),("@c",run.S("companyId")));
            if(already is not null and not DBNull)continue;
            Guid recoveryAcc=AccountId(c,tx,accCode);
            JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",sourceId,"salary_shared_once",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,
                [
                    new(recoveryAcc,SchemaOrgCostCentre.NoneId,run.S("app"),take,0m,"خصم مشترك مرة واحدة"),
                    new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,take,"خصم مشترك مرة واحدة")
                ]));
        }
    }

    /// <summary>Stable source id so a second app approval is idempotent (UQ fin_journal source).</summary>
    static Guid StableSharedOnceSource(string holdId,string code,string account,int index)
    {
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"salary_shared_once|{holdId}|{code}|{account}|{index}"));
        Span<byte> g=stackalloc byte[16];
        hash.AsSpan(0,16).CopyTo(g);
        g[6]=(byte)((g[6]&0x0F)|0x40);
        g[8]=(byte)((g[8]&0x3F)|0x80);
        return new Guid(g);
    }

    /// <summary>Same duplicate detection as SalSharedRiders.ConsolidateIfComplete: second+ occurrence of each shared code, with its account.</summary>
    static List<(string Code,decimal Amount,string Account)> SharedDuplicateParts(DbConnection c,DbTransaction tx,string erpRider,string periodMonth)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var parts=new List<(string Code,decimal Amount,string Account)>();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT r.id FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
            WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.status<>4
            ORDER BY pr.app_code,r.id
            """;
        Add(cmd,"@erp",erpRider);Add(cmd,"@pm",periodMonth);
        using var r=cmd.ExecuteReader();
        var riderIds=new List<string>();
        while(r.Read())riderIds.Add(r["id"].ToString()!);
        r.Close();
        foreach(string riderId in riderIds)
        {
            using var items=c.CreateCommand();items.Transaction=tx;
            items.CommandText=$"""
                SELECT item_code,amount,account_code FROM {SchemaSql.Q(c,"sal","pay_run_item")}
                WHERE pay_run_rider_id=@r AND stage_code='level1' AND affects_net=1 AND amount>0
                  AND item_code IN ('fuel','vehicle','housing')
                ORDER BY line_no
                """;
            Add(items,"@r",riderId);
            using var ir=items.ExecuteReader();
            while(ir.Read())
            {
                string code=ir["item_code"].ToString()!;
                decimal amt=Convert.ToDecimal(ir["amount"]);
                string account=ir["account_code"].ToString() is {Length:>0} a?a:"112511";
                if(!seen.Add(code))parts.Add((code,amt,account));
            }
        }
        return parts;
    }

    /// <summary>RWC4b H12: settle each pay_run_item.debt_id for the actual deducted amount at approval. Returns the settled total.</summary>
    static decimal SettleLinkedDebts(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now)
    {
        decimal settled=0;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        // RWC4m2 H12: company-cost lines (affects_net=0) must not settle against salary payable.
        cmd.CommandText=$"""
            SELECT debt_id,amount,account_code FROM {SchemaSql.Q(c,"sal","pay_run_item")}
            WHERE pay_run_rider_id=@r AND amount>0 AND affects_net=1 AND debt_id IS NOT NULL AND item_code<>'old_debt_instalment'
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
            // RWC4a3F H12: always credit the debt control account from category (ignore caller item account).
            string ctrl=DebtControlAccount(c,tx,row.DebtId);
            Guid debtAcc=AccountId(c,tx,ctrl);
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

    /// <summary>RWC4m H11 / §40.5+§40.6: single-app carryover = −salary_net; cleared multi-app hold uses combined shortfall
    /// (max(0,−combined_net) minus carryovers already posted this period), capped by this app's −net.</summary>
    static bool CreateNegativeCarryover(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,JsonObject rider,string now)
    {
        decimal appShort=J.Money(-rider.N("salaryNet"));
        J.Need(appShort>0,"لا مديونية عجز بدون صافٍ سالب.",400);
        decimal amount=CombinedCarryoverAmount(c,tx,run,rider,appShort);
        if(amount<=0)return false;
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

    /// <summary>Cleared shared hold: debt only for the combined shortfall remaining this period; otherwise the app shortfall.
    /// RWC4m2 H11: exception release leaves apps pending — keep standalone −salary_net carryover (combined_net may still be 0).</summary>
    static decimal CombinedCarryoverAmount(DbConnection c,DbTransaction tx,JsonObject run,JsonObject rider,decimal appShort)
    {
        object? holdIdObj=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
            WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'
            """,("@erp",rider.S("erpRiderId")),("@pm",run.S("periodMonth")));
        if(holdIdObj is null or DBNull)return appShort;
        string holdId=holdIdObj.ToString()!;
        long pendingHoldApps=Convert.ToInt64(SchemaSql.Scalar(c,tx,$"""
            SELECT COUNT(*) FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND received=0
            """,("@h",holdId))??0L);
        if(pendingHoldApps>0)return appShort;
        decimal combined=J.Money(Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"""
            SELECT combined_net FROM {SchemaSql.Q(c,"sal","shared_rider_hold")} WHERE id=@id
            """,("@id",holdId))??0m));
        decimal need=J.Money(Math.Max(0m,-combined));
        if(need<=0)return 0m;
        decimal already=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(m.amount),0) FROM {SchemaSql.Q(c,"debt","debt_movement")} m
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run_rider")} r ON r.id=m.pay_run_rider_id
            WHERE m.source_type='carryover_112507' AND m.pay_run_period_month=@pm AND r.erp_rider_id=@erp
            """,("@pm",run.S("periodMonth")),("@erp",rider.S("erpRiderId")))??0m);
        decimal remaining=J.Money(Math.Max(0m,need-already));
        return J.Money(Math.Min(appShort,remaining));
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
            SELECT id,erp_rider_id,gross,commission,salary_net,old_debt_instalment,debt_confirm_status,salary_approved_at,keeta_ineligible,protected_salary,created_by
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
            ["protectedSalary"]=Convert.ToInt32(r["protected_salary"])==1,
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

    static string DebtControlAccount(DbConnection c,DbTransaction tx,string debtId)
    {
        object? cat=SchemaSql.Scalar(c,tx,$"SELECT category FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",debtId));
        string category=cat is null or DBNull?"":cat.ToString()!;
        var hit=ConfigLists.DefaultDeductions.FirstOrDefault(d=>string.Equals(d.Category,category,StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(hit.Account)?"112505":hit.Account;
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
