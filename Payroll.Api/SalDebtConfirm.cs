using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-15c: old-debt instalment confirmation BEFORE final approval (M-0670).
/// 0 = deferred, no movement; else movement + balance + journal in one TX; net may hit 0.
/// Allocations list per rider (Codex RWC4): multiple debts, aggregate cap, statement freeze.</summary>
public static class SalDebtConfirm
{
    public const string ManagePermission="pay.manage";

    public static JsonObject Confirm(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        var run=LoadRun(c,tx,actor,payRunId);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Debt);
        var rows=(input["rows"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(rows.Count>0,"أدخل صفوف تأكيد الدين.",400);
        var outRows=new JsonArray();
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        foreach(var row in rows)
        {
            string riderId=row.Required("payRunRiderId");
            var rider=LoadRider(c,tx,payRunId,riderId);
            J.Need(rider.S("debtConfirmStatus")=="pending","تم تأكيد دين هذا المندوب مسبقاً.",409);
            J.Need(string.IsNullOrEmpty(rider.S("salaryApprovedAt")),"لا تأكيد دين بعد اعتماد الراتب.",409);
            decimal net=rider.N("salaryNet");

            var allocations=(row["allocations"] as JsonArray??[]).OfType<JsonObject>().ToList();
            if(allocations.Count==0&&row.ContainsKey("amount"))
            {
                decimal single=J.Money(row.N("amount"));
                if(single==0)allocations=[];
                else allocations=[new JsonObject{["debtId"]=row.S("debtId"),["amount"]=single}];
            }

            decimal total=J.Money(allocations.Sum(a=>J.Money(a.N("amount"))));
            J.Need(total>=0,"مبلغ القسط لا يكون سالباً.",400);
            J.Need(total==0||total<=Math.Max(0,net),"القسط لا يتجاوز الصافي الموجب.",400);

            if(total==0)
            {
                Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                    SET debt_confirm_status='deferred',old_debt_instalment=0,updated_by=@by,updated_at=@at
                    WHERE id=@id
                    """,("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));
                ReplaceOldDebtItems(c,tx,actor,run,riderId,[],now);
                outRows.Add(new JsonObject{["payRunRiderId"]=riderId,["debtConfirmStatus"]="deferred",["amount"]=0,["salaryNet"]=net});
                continue;
            }

            // WC4D H12: validate every allocation (balance minus linked reservations) before any write.
            var perDebt=new Dictionary<string,decimal>(StringComparer.Ordinal);
            foreach(var alloc in allocations)
            {
                string debtId=NullIfEmpty(alloc.S("debtId"))??"";
                J.Need(debtId!="","معرّف الدين مطلوب عند تأكيد قسط أكبر من صفر.",400);
                perDebt[debtId]=J.Money(perDebt.GetValueOrDefault(debtId)+J.Money(alloc.N("amount")));
            }
            foreach(var (debtId,sum) in perDebt)
            {
                LoadDebt(c,tx,debtId,run.S("companyId"),rider.S("erpRiderId"));
                decimal available=J.Money(SchemaDebt.LockDebt(c,tx,debtId)-SalPayCalc.ReservedByLinks(c,tx,debtId));
                J.Need(sum<=available,"هذا الدين مخصوم كبند مربوط في المسير أو رصيده أقل؛ القسط يتجاوز المتاح.",409);
            }

            // Confirm before movements so SchemaDebt TR sees confirmed + instalment cap on original net.
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET debt_confirm_status='confirmed',old_debt_instalment=@amt,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@amt",total),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));

            var movementIds=new JsonArray();
            string? lastJournal=null;
            foreach(var alloc in allocations)
            {
                decimal amount=J.Money(alloc.N("amount"));
                J.Need(amount>0,"كل بند توزيع يجب أن يكون أكبر من صفر.",400);
                string debtId=NullIfEmpty(alloc.S("debtId"))??"";
                J.Need(debtId!="","معرّف الدين مطلوب عند تأكيد قسط أكبر من صفر.",400);
                var debt=LoadDebt(c,tx,debtId,run.S("companyId"),rider.S("erpRiderId"));
                decimal locked=SchemaDebt.LockDebt(c,tx,debtId);
                J.Need(amount<=locked,"القسط أكبر من رصيد الدين.",409);

                Guid company=Guid.Parse(run.S("companyId"));
                Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
                Guid payableAcc=AccountId(c,tx,"212103");
                string recvCode=ReceivableCode(debt.S("category"));
                Guid debtAcc=AccountId(c,tx,recvCode);
                // Allocation-specific source: unique per (pay-run rider, debt) so next month does not collide (RWC4b new HIGH).
                Guid sourceId=SchemaSql.NewId();
                var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                    company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                    DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                    "sal_debt_confirm",sourceId,"debt_confirm",(byte)run.N("runMode"),run.B("payoutBlocked"),
                    actor.Identity!.Name!,
                    [
                        new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),amount,0m,"قسط مديونية قديمة"),
                        new(debtAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,amount,"قسط مديونية قديمة")
                    ]));
                lastJournal=SchemaSql.GuidText(journal.JournalId);

                string movId=SchemaSql.GuidText(SchemaSql.NewId());
                Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
                    (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month,journal_id,run_mode,payout_blocked,created_by,created_at)
                    VALUES(@id,@pm,@debt,@c,@on,@amt,'salary_deduction',@src,@rider,@pm,@j,0,@pb,@by,@at)
                    """,("@id",movId),("@pm",run.S("periodMonth")),("@debt",debtId),("@c",run.S("companyId")),
                    ("@on",run.S("periodMonth")),("@amt",-amount),("@src",movId),("@rider",riderId),
                    ("@j",lastJournal),("@pb",run.B("payoutBlocked")?1:0),
                    ("@by",actor.Identity!.Name!),("@at",now));
                movementIds.Add(movId);
            }

            decimal newNet=J.Money(net-total);
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET salary_net=@net,updated_by=@by,updated_at=@at WHERE id=@id
                """,("@net",newNet),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));
            ReplaceOldDebtItems(c,tx,actor,run,riderId,allocations,now);
            // RWC4a3 H11 / §40.5+§40.6: after consolidate, old-debt confirm must shrink combined_net too.
            if(total>0)ReduceSharedCombinedNet(c,tx,rider.S("erpRiderId"),run.S("periodMonth"),total,now);

            outRows.Add(new JsonObject
            {
                ["payRunRiderId"]=riderId,["debtConfirmStatus"]="confirmed",["amount"]=total,
                ["salaryNet"]=newNet,["journalId"]=lastJournal??"",["movementIds"]=movementIds
            });
        }
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run")} SET stage=CASE WHEN stage<8 THEN 8 ELSE stage END,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@by",actor.Identity!.Name!),("@at",now),("@id",payRunId));
        Store.Audit(c,tx,"sal.debt_confirmed",payRunId,actor.Identity!.Name!);
        return new(){["payRunId"]=payRunId,["rows"]=outRows,["allowedActions"]=new JsonArray("approve_salary")};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/pay-runs/{id}/debt-confirm",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Confirm(c,tx,ctx.User,id,input);
        })).WithTags("Payroll").WithSummary("Confirm old-debt instalments before final salary approval");
    }

    /// <summary>RWC4a3 H11: cleared multi-app hold — reduce combined_net by the confirmed instalment so payables follow §40.5 after §40.6.</summary>
    static void ReduceSharedCombinedNet(DbConnection c,DbTransaction tx,string erpRider,string periodMonth,decimal amount,string now)
    {
        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","shared_rider_hold")}
            SET combined_net=combined_net-@amt,updated_at=@at
            WHERE erp_rider_id=@erp AND period_month=@pm AND status='cleared'
            """,("@amt",amount),("@at",now),("@erp",erpRider),("@pm",periodMonth));
    }

    /// <summary>RWC4b M1: freeze each confirmed allocation with its receivable account (not hardcoded 112507).</summary>
    static void ReplaceOldDebtItems(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject run,string riderId,List<JsonObject> allocations,string now)
    {
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run_item")} SET amount=0,updated_by=@by,updated_at=@at WHERE pay_run_rider_id=@r AND item_code='old_debt_instalment'",
            ("@by",actor.Identity!.Name!),("@at",now),("@r",riderId));
        int nextLine=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(MAX(line_no),0)+1 FROM {SchemaSql.Q(c,"sal","pay_run_item")} WHERE pay_run_rider_id=@r",("@r",riderId))!);
        foreach(var alloc in allocations)
        {
            decimal amount=J.Money(alloc.N("amount"));
            if(amount<=0)continue;
            string debtId=alloc.S("debtId");
            string account="112507";
            if(debtId!="")
            {
                object? cat=SchemaSql.Scalar(c,tx,$"SELECT category FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",debtId));
                if(cat is not null and not DBNull)account=ReceivableCode(cat.ToString()!);
            }
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","pay_run_item")}
                (id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code,debt_id,created_by,created_at)
                VALUES(@id,@pm,@r,@line,'old_debt','old_debt_instalment',@amt,1,@acc,@debt,@by,@at)
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",run.S("periodMonth")),("@r",riderId),("@line",nextLine++),
                ("@amt",amount),("@acc",account),("@debt",(object?)NullIfEmpty(debtId)??DBNull.Value),
                ("@by",actor.Identity!.Name!),("@at",now));
        }
    }

    static string ReceivableCode(string category)
    {
        var hit=ConfigLists.DefaultDeductions.FirstOrDefault(d=>string.Equals(d.Category,category,StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(hit.Account)?"112505":hit.Account;
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);
        SchemaFinPeriod.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);SchemaDebt.Ensure(c,tx);SchemaOperationCapture.Ensure(c,tx);
        EnsureItemDebtColumn(c,tx);
    }

    static void EnsureItemDebtColumn(DbConnection c,DbTransaction? tx)
    {
        string t=SchemaSql.Q(c,"sal","pay_run_item");
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'sal.pay_run_item',N'debt_id') IS NULL ALTER TABLE {t} ADD debt_id {SchemaSql.GuidType(c)} NULL;");
        else
        {
            object? has=SchemaSql.Scalar(c,tx,"SELECT 1 FROM pragma_table_info('sal_pay_run_item') WHERE name='debt_id'");
            if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {t} ADD COLUMN debt_id TEXT NULL;");
        }
    }

    static JsonObject LoadRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT id,company_id,app_code,period_month,run_mode,payout_blocked FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id";
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"مسير الرواتب غير موجود.",404);
        var run=new JsonObject
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,["app"]=r["app_code"].ToString()!,
            ["periodMonth"]=ReadMonth(r["period_month"]),["runMode"]=Convert.ToInt32(r["run_mode"]),
            ["payoutBlocked"]=Convert.ToInt32(r["payout_blocked"])==1
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
            SELECT id,erp_rider_id,salary_net,old_debt_instalment,debt_confirm_status,salary_approved_at
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} WHERE id=@id AND pay_run_id=@run
            """;
        Add(cmd,"@id",riderId);Add(cmd,"@run",payRunId);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"المندوب غير موجود في المسير.",404);
        return new()
        {
            ["id"]=r["id"].ToString()!,["erpRiderId"]=r["erp_rider_id"].ToString()!,
            ["salaryNet"]=Convert.ToDecimal(r["salary_net"]),["oldDebtInstalment"]=Convert.ToDecimal(r["old_debt_instalment"]),
            ["debtConfirmStatus"]=r["debt_confirm_status"].ToString()!,
            ["salaryApprovedAt"]=r["salary_approved_at"] is DBNull?"":r["salary_approved_at"].ToString()!
        };
    }

    static JsonObject LoadDebt(DbConnection c,DbTransaction tx,string debtId,string companyId,string erpRiderId)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,erp_rider_id,company_id,category,status FROM {SchemaSql.Q(c,"debt","debt")}
            WHERE id=@id AND company_id=@c
            """;
        Add(cmd,"@id",debtId);Add(cmd,"@c",companyId);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"الدين غير موجود.",404);
        J.Need(r["erp_rider_id"].ToString()==erpRiderId,"الدين لا يخص هذا المندوب.",409);
        J.Need(r["status"].ToString()=="open","الدين غير مفتوح.",409);
        return new(){["id"]=r["id"].ToString()!,["category"]=r["category"].ToString()!};
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
    static string? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (n,v) in values)Add(cmd,n,v);
        return cmd.ExecuteNonQuery();
    }
    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}
}
