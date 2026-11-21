using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-15c: old-debt instalment confirmation BEFORE final approval (M-0670).
/// 0 = deferred, no movement; else movement + balance + journal in one TX; net may hit 0.</summary>
public static class SalDebtConfirm
{
    public const string ManagePermission="pay.manage";

    public static JsonObject Confirm(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        var run=LoadRun(c,tx,payRunId);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Debt);
        var rows=(input["rows"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(rows.Count>0,"أدخل صفوف تأكيد الدين.",400);
        var outRows=new JsonArray();
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        foreach(var row in rows)
        {
            string riderId=row.Required("payRunRiderId");
            decimal amount=J.Money(row.N("amount"));
            J.Need(amount>=0,"مبلغ القسط لا يكون سالباً.",400);
            var rider=LoadRider(c,tx,payRunId,riderId);
            J.Need(rider.S("debtConfirmStatus")=="pending","تم تأكيد دين هذا المندوب مسبقاً.",409);
            J.Need(string.IsNullOrEmpty(rider.S("salaryApprovedAt")),"لا تأكيد دين بعد اعتماد الراتب.",409);
            decimal net=rider.N("salaryNet");
            J.Need(amount==0||amount<=Math.Max(0,net),"القسط لا يتجاوز الصافي الموجب.",400);

            if(amount==0)
            {
                Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                    SET debt_confirm_status='deferred',old_debt_instalment=0,updated_by=@by,updated_at=@at
                    WHERE id=@id
                    """,("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));
                outRows.Add(new JsonObject{["payRunRiderId"]=riderId,["debtConfirmStatus"]="deferred",["amount"]=0,["salaryNet"]=net});
                continue;
            }

            string debtId=NullIfEmpty(row.S("debtId"))??"";
            J.Need(debtId!="","معرّف الدين مطلوب عند تأكيد قسط أكبر من صفر.",400);
            var debt=LoadDebt(c,tx,debtId,run.S("companyId"),rider.S("erpRiderId"));
            decimal locked=SchemaDebt.LockDebt(c,tx,debtId);
            J.Need(amount<=locked,"القسط أكبر من رصيد الدين.",409);

            Guid company=Guid.Parse(run.S("companyId"));
            Guid periodId=ResolvePeriod(c,tx,run.S("companyId"),run.S("periodMonth"));
            Guid payableAcc=AccountId(c,tx,"212103");
            Guid debtAcc=AccountId(c,tx,ReceivableCode(debt.S("category")));
            Guid sourceId=Guid.Parse(riderId);
            var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                company,periodId,DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                DateOnly.Parse(run.S("periodMonth"),CultureInfo.InvariantCulture),
                "sal_pay_run_rider",sourceId,"debt_confirm",(byte)run.N("runMode"),run.B("payoutBlocked"),
                actor.Identity!.Name!,
                [
                    new(payableAcc,SchemaOrgCostCentre.NoneId,run.S("app"),amount,0m,"قسط مديونية قديمة"),
                    new(debtAcc,SchemaOrgCostCentre.NoneId,run.S("app"),0m,amount,"قسط مديونية قديمة")
                ]));

            // Confirm + instalment before movement (SchemaDebt TR); keep original net until after insert
            // so -amount<=salary_net and overCap still hold at insert time.
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET debt_confirm_status='confirmed',old_debt_instalment=@amt,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@amt",amount),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));

            string movId=SchemaSql.GuidText(SchemaSql.NewId());
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
                (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,pay_run_rider_id,pay_run_period_month,journal_id,run_mode,payout_blocked,created_by,created_at)
                VALUES(@id,@pm,@debt,@c,@on,@amt,'salary_deduction',@src,@rider,@pm,@j,0,@pb,@by,@at)
                """,("@id",movId),("@pm",run.S("periodMonth")),("@debt",debtId),("@c",run.S("companyId")),
                ("@on",run.S("periodMonth")),("@amt",-amount),("@src",movId),("@rider",riderId),
                ("@j",SchemaSql.GuidText(journal.JournalId)),("@pb",run.B("payoutBlocked")?1:0),
                ("@by",actor.Identity!.Name!),("@at",now));

            decimal newNet=J.Money(net-amount);
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                SET salary_net=@net,updated_by=@by,updated_at=@at WHERE id=@id
                """,("@net",newNet),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));

            outRows.Add(new JsonObject
            {
                ["payRunRiderId"]=riderId,["debtConfirmStatus"]="confirmed",["amount"]=amount,
                ["salaryNet"]=newNet,["journalId"]=SchemaSql.GuidText(journal.JournalId),["movementId"]=movId
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
    }

    static JsonObject LoadRun(DbConnection c,DbTransaction tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT id,company_id,app_code,period_month,run_mode,payout_blocked FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id";
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"مسير الرواتب غير موجود.",404);
        return new()
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,["app"]=r["app_code"].ToString()!,
            ["periodMonth"]=ReadMonth(r["period_month"]),["runMode"]=Convert.ToInt32(r["run_mode"]),
            ["payoutBlocked"]=Convert.ToInt32(r["payout_blocked"])==1
        };
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
