using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-15a: one calc engine, per-app rule modules. Order L1 → NET → L2 (M-0669).
/// Debt bulk-preview + mudad apply. Capture must be frozen (U-W2-12).</summary>
public static class SalPayCalc
{
    public const string ManagePermission="pay.manage";

    public static JsonObject Calculate(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        var run=LoadRun(c,tx,payRunId);
        J.Need(run.N("captureState")>=OpsSnapshotCapture.CaptureFrozen,"جمّد التقاط المدخلات قبل الاحتساب.",409);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);
        var rules=AppPayRules.For(run.S("app"));
        var riders=(input["riders"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(riders.Count>0,"أدخل مناديب الاحتساب.",400);

        var outRiders=new JsonArray();
        foreach(var src in riders)
        {
            rules.ValidateRider(src);
            string erpRider=src.Required("erpRiderId");
            decimal gross=J.Money(src.N("gross"));
            decimal commission=J.Money(src.N("commission"));
            bool keetaIneligible=src.B("keetaIneligible");
            string scheme=src.S("schemeType","commission");

            decimal level1Personal=0,companyOperating=0;
            var l1Items=new List<JsonObject>();
            foreach(var item in (src["level1"] as JsonArray??[]).OfType<JsonObject>())
            {
                decimal amount=J.Money(item.N("amount"));
                J.Need(amount>=0,"مبلغ المستوى 1 لا يكون سالباً.",400);
                bool operating=rules.OperatingCostOnCompany(src,item);
                string code=item.S("itemCode","l1");
                string account=item.S("accountCode","112505");
                if(operating){companyOperating+=amount;l1Items.Add(Item("level1",code,amount,false,account,null));}
                else{level1Personal+=amount;l1Items.Add(Item("level1",code,amount,true,account,item.S("debtId")));}
            }

            decimal netAfterL1=J.Money(gross-level1Personal);
            decimal level2Total=0;
            var l2Items=new List<JsonObject>();
            foreach(var item in (src["level2"] as JsonArray??[]).OfType<JsonObject>())
            {
                decimal amount=J.Money(item.N("amount"));
                J.Need(amount>=0,"مبلغ المستوى 2 لا يكون سالباً.",400);
                // L2 capped by positive net remaining (BR §40.6).
                decimal room=Math.Max(0,netAfterL1-level2Total);
                decimal take=J.Money(Math.Min(amount,room));
                level2Total+=take;
                l2Items.Add(Item("level2",item.S("itemCode","l2"),take,true,item.S("accountCode","112502"),item.S("debtId")));
            }

            decimal salaryNet=J.Money(netAfterL1-level2Total);
            decimal proposedDebt=J.Money(Math.Max(0,src.N("oldDebtInstalment")));
            if(proposedDebt>0&&salaryNet>0)
                proposedDebt=J.Money(Math.Min(proposedDebt,salaryNet));
            else if(salaryNet<=0)proposedDebt=0;

            bool createsNegativeDebt=salaryNet<0&&!keetaIneligible;
            string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            string? existingId=FindPendingRider(c,tx,payRunId,erpRider);
            string riderId;
            if(existingId!=null)
            {
                riderId=existingId;
                J.Need(CanRecalc(c,tx,riderId),"لا إعادة احتساب بعد تأكيد الدين أو الاعتماد.",409);
                // Zero prior item amounts (retain forbids delete); new lines continue after max line_no.
                Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run_item")} SET amount=0,updated_by=@by,updated_at=@at WHERE pay_run_rider_id=@r",
                    ("@by",actor.Identity!.Name!),("@at",now),("@r",riderId));
                Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")}
                    SET gross=@gross,commission=@comm,salary_net=@net,old_debt_instalment=@debt,keeta_ineligible=@keeta,updated_by=@by,updated_at=@at
                    WHERE id=@id
                    """,("@gross",gross),("@comm",commission),("@net",salaryNet),("@debt",proposedDebt),
                    ("@keeta",keetaIneligible?1:0),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));
            }
            else
            {
                riderId=SchemaSql.GuidText(SchemaSql.NewId());
                Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"sal","pay_run_rider")}
                    (id,pay_run_id,company_id,period_month,erp_rider_id,run_mode,gross,commission,salary_net,old_debt_instalment,keeta_ineligible,created_by,created_at)
                    VALUES(@id,@run,@c,@pm,@erp,@rm,@gross,@comm,@net,@debt,@keeta,@by,@at)
                    """,("@id",riderId),("@run",payRunId),("@c",run.S("companyId")),("@pm",run.S("periodMonth")),
                    ("@erp",erpRider),("@rm",(byte)run.N("runMode")),("@gross",gross),("@comm",commission),
                    ("@net",salaryNet),("@debt",proposedDebt),("@keeta",keetaIneligible?1:0),
                    ("@by",actor.Identity!.Name!),("@at",now));
            }

            int lineNo=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(MAX(line_no),0) FROM {SchemaSql.Q(c,"sal","pay_run_item")} WHERE pay_run_rider_id=@r",("@r",riderId))!);
            void Persist(JsonObject it,string stage)
            {
                lineNo++;
                string id=SchemaSql.GuidText(SchemaSql.NewId());
                Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"sal","pay_run_item")}
                    (id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code,created_by,created_at)
                    VALUES(@id,@pm,@r,@line,@stage,@code,@amt,@aff,@acc,@by,@at)
                    """,("@id",id),("@pm",run.S("periodMonth")),("@r",riderId),("@line",lineNo),
                    ("@stage",stage),("@code",it.S("itemCode")),("@amt",it.N("amount")),
                    ("@aff",it.B("affectsNet")?1:0),("@acc",it.S("accountCode")),
                    ("@by",actor.Identity!.Name!),("@at",now));
            }
            Persist(Item("invoice","gross",gross,true,"212103",null),"invoice");
            if(commission!=0)Persist(Item("commission","commission",commission,true,"411104",null),"commission");
            foreach(var it in l1Items)Persist(it,"level1");
            Persist(Item("net","net_after_l1",netAfterL1,true,"212103",null),"net");
            foreach(var it in l2Items)Persist(it,"level2");
            if(proposedDebt>0)Persist(Item("old_debt","old_debt_instalment",proposedDebt,true,"112507",null),"old_debt");

            outRiders.Add(new JsonObject
            {
                ["id"]=riderId,["erpRiderId"]=erpRider,["gross"]=gross,["commission"]=commission,
                ["schemeType"]=scheme,["level1Total"]=level1Personal,["companyOperatingTotal"]=companyOperating,
                ["netAfterLevel1"]=netAfterL1,["level2Total"]=level2Total,["salaryNet"]=salaryNet,
                ["oldDebtInstalment"]=proposedDebt,["keetaIneligible"]=keetaIneligible,
                ["createsNegativeDebt"]=createsNegativeDebt
            });
        }

        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run")} SET stage=CASE WHEN stage<5 THEN 5 ELSE stage END,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)),("@id",payRunId));
        Store.Audit(c,tx,"sal.pay_run_calculated",payRunId,actor.Identity!.Name!);
        return new()
        {
            ["payRunId"]=payRunId,["app"]=run.S("app"),["riderCount"]=outRiders.Count,["riders"]=outRiders,
            ["allowedActions"]=new JsonArray("preview_debt_deductions","apply_mudad_deduction","confirm_debt")
        };
    }

    public static JsonObject BulkPreviewDebts(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string payRunId,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        var run=LoadRun(c,tx,payRunId);
        var rows=new JsonArray();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT r.id,r.erp_rider_id,r.salary_net,r.old_debt_instalment,r.debt_confirm_status
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r WHERE r.pay_run_id=@run
            """;
        Add(cmd,"@run",payRunId);
        using var reader=cmd.ExecuteReader();
        var riders=new List<(string Id,string Erp,decimal Net,decimal Instalment,string Status)>();
        while(reader.Read())
            riders.Add((reader["id"].ToString()!,reader["erp_rider_id"].ToString()!,
                Convert.ToDecimal(reader["salary_net"]),Convert.ToDecimal(reader["old_debt_instalment"]),
                reader["debt_confirm_status"].ToString()!));
        reader.Close();

        foreach(var r in riders)
        {
            var debts=new JsonArray();
            using var dcmd=c.CreateCommand();dcmd.Transaction=tx;
            dcmd.CommandText=$"""
                SELECT id,category,balance,instalment FROM {SchemaSql.Q(c,"debt","debt")}
                WHERE erp_rider_id=@erp AND company_id=@c AND status='open' AND balance>0
                ORDER BY category,id
                """;
            Add(dcmd,"@erp",r.Erp);Add(dcmd,"@c",run.S("companyId"));
            using var dr=dcmd.ExecuteReader();
            decimal room=Math.Max(0,r.Net);
            while(dr.Read())
            {
                decimal balance=Convert.ToDecimal(dr["balance"]);
                decimal instalment=dr["instalment"] is DBNull?balance:Convert.ToDecimal(dr["instalment"]);
                decimal due=J.Money(Math.Min(instalment,Math.Min(balance,room)));
                debts.Add(new JsonObject
                {
                    ["debtId"]=dr["id"].ToString(),["category"]=dr["category"].ToString(),
                    ["balance"]=balance,["due"]=due,["max"]=due
                });
                room=J.Money(room-due);
            }
            rows.Add(new JsonObject
            {
                ["payRunRiderId"]=r.Id,["erpRiderId"]=r.Erp,["salaryNet"]=r.Net,
                ["oldDebtInstalment"]=r.Instalment,["debtConfirmStatus"]=r.Status,["items"]=debts
            });
        }
        return new(){["payRunId"]=payRunId,["rows"]=rows,["allowedActions"]=new JsonArray("preview_debt_deductions")};
    }

    public static JsonObject ApplyMudad(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,ManagePermission);
        Ensure(c,tx);
        string payRunId=input.Required("payRunId"),erpRider=input.Required("erpRiderId"),lineId=input.Required("mudadLineId");
        decimal amount=J.Money(input.N("amount"));
        J.Need(amount>=0,"مبلغ مدد لا يكون سالباً.",400);
        var run=LoadRun(c,tx,payRunId);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);

        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,salary_net,debt_confirm_status,salary_approved_at,period_month
            FROM {SchemaSql.Q(c,"sal","pay_run_rider")}
            WHERE pay_run_id=@run AND erp_rider_id=@erp
            """;
        Add(cmd,"@run",payRunId);Add(cmd,"@erp",erpRider);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"المندوب غير موجود في المسير.",404);
        string riderId=r["id"].ToString()!;
        decimal net=Convert.ToDecimal(r["salary_net"]);
        J.Need(r["salary_approved_at"] is DBNull,"لا تطبيق مدد بعد اعتماد الراتب.",409);
        J.Need(r["debt_confirm_status"].ToString()=="pending","لا تطبيق مدد بعد تأكيد الدين.",409);
        string period=ReadMonth(r["period_month"]);
        r.Close();

        object? lineOk=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"hr","mudad_line")} WHERE id=@id AND match_status=2",("@id",lineId));
        J.Need(lineOk is not null,"صف مدد غير مطابق.",404);

        string dedId=SchemaSql.GuidText(SchemaSql.NewId());
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        try
        {
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","mudad_deduction")}
                (id,period_month,pay_run_rider_id,mudad_line_id,amount,created_by,created_at)
                VALUES(@id,@pm,@r,@line,@amt,@by,@at)
                """,("@id",dedId),("@pm",period),("@r",riderId),("@line",lineId),("@amt",amount),
                ("@by",actor.Identity!.Name!),("@at",now));
        }
        catch(Exception ex) when(ex.GetBaseException().Message.Contains("UNIQUE",StringComparison.OrdinalIgnoreCase)
            ||ex.GetBaseException().Message.Contains("Money row replacement",StringComparison.OrdinalIgnoreCase))
        {throw new ApiError("خصم مدد لهذا الصف مسجل مسبقاً.",409);}

        decimal newNet=J.Money(net-amount);
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")} SET salary_net=@net,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@net",newNet),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));

        int nextLine=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(MAX(line_no),0)+1 FROM {SchemaSql.Q(c,"sal","pay_run_item")} WHERE pay_run_rider_id=@r",("@r",riderId))!);
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","pay_run_item")}
            (id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code,created_by,created_at)
            VALUES(@id,@pm,@r,@line,'level1','mudad_salary',@amt,1,'112505',@by,@at)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",period),("@r",riderId),("@line",nextLine),
            ("@amt",amount),("@by",actor.Identity!.Name!),("@at",now));

        Store.Audit(c,tx,"sal.mudad_applied",dedId,actor.Identity!.Name!);
        return new(){["id"]=dedId,["payRunRiderId"]=riderId,["mudadLineId"]=lineId,["amount"]=amount,["salaryNet"]=newNet};
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/sal/pay-runs/{id}/calculate",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return Calculate(c,tx,ctx.User,id,input);
        })).WithTags("Payroll").WithSummary("Calculate pay run L1→NET→L2 per app rules");
        api.MapPost("/sal/pay-runs/{id}/debt-deductions/bulk-preview",(string id,JsonObject? input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return BulkPreviewDebts(c,tx,ctx.User,id,input??new JsonObject());
        })).WithTags("Payroll").WithSummary("Preview old-debt instalments before confirmation");
        api.MapPost("/sal/mudad-deductions/apply",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            Ensure(c,tx);return ApplyMudad(c,tx,ctx.User,input);
        })).WithTags("Payroll").WithSummary("Apply matched Mudad basic salary into L1");
    }

    static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);SchemaPayPayable.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);SchemaHrMudad.Ensure(c,tx);SchemaSalMudad.Ensure(c,tx);
        SchemaDebt.Ensure(c,tx);SchemaOperationCapture.Ensure(c,tx);
    }

    static JsonObject LoadRun(DbConnection c,DbTransaction tx,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,company_id,app_code,period_month,stage,status,run_mode,payout_blocked,capture_state
            FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """;
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"مسير الرواتب غير موجود.",404);
        return new()
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,
            ["app"]=r["app_code"].ToString()!,["periodMonth"]=ReadMonth(r["period_month"]),
            ["stage"]=Convert.ToInt32(r["stage"]),["status"]=Convert.ToInt32(r["status"]),
            ["runMode"]=Convert.ToInt32(r["run_mode"]),["payoutBlocked"]=Convert.ToInt32(r["payout_blocked"])==1,
            ["captureState"]=Convert.ToInt32(r["capture_state"])
        };
    }

    static string? FindPendingRider(DbConnection c,DbTransaction tx,string payRunId,string erpRider)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"sal","pay_run_rider")} WHERE pay_run_id=@run AND erp_rider_id=@erp",
            ("@run",payRunId),("@erp",erpRider));
        return id is null or DBNull?null:id.ToString();
    }

    static bool CanRecalc(DbConnection c,DbTransaction tx,string riderId)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT debt_confirm_status,salary_approved_at FROM {SchemaSql.Q(c,"sal","pay_run_rider")} WHERE id=@id";
        Add(cmd,"@id",riderId);
        using var r=cmd.ExecuteReader();
        if(!r.Read())return false;
        return r["salary_approved_at"] is DBNull&&r["debt_confirm_status"].ToString()=="pending";
    }

    static JsonObject Item(string stage,string code,decimal amount,bool affects,string account,string? debtId)=>new()
    {
        ["stageCode"]=stage,["itemCode"]=code,["amount"]=amount,["affectsNet"]=affects,
        ["accountCode"]=account,["debtId"]=debtId??""
    };

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
