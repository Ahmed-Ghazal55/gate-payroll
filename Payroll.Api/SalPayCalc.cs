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
        var run=LoadRun(c,tx,actor,payRunId);
        J.Need(run.N("captureState")>=OpsSnapshotCapture.CaptureFrozen,"جمّد التقاط المدخلات قبل الاحتساب.",409);
        MoneyWriteGuards.Demand((byte)run.N("runMode"),run.B("payoutBlocked"),MoneyEffectKind.Accounting);
        var rules=AppPayRules.For(run.S("app"));
        var riders=(input["riders"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(riders.Count>0,"أدخل مناديب الاحتساب.",400);
        var assignments=LoadFrozenKind(c,tx,payRunId,"assignment");
        var frozenScheme=LoadFrozenKind(c,tx,payRunId,"scheme");
        var frozenEligibility=LoadFrozenKind(c,tx,payRunId,"eligibility");
        var frozenChefz=LoadFrozenKind(c,tx,payRunId,"chefz_settlement");
        var frozenKeeta=LoadFrozenKind(c,tx,payRunId,"keeta_invoice_row");
        var frozenWallet=LoadFrozenKind(c,tx,payRunId,"hs_wallet");

        var outRiders=new JsonArray();
        foreach(var src in riders)
        {
            string erpRider=src.Required("erpRiderId");
            DemandAssigned(assignments,run.S("app"),erpRider);
            BindFromFrozen(src,run.S("app"),erpRider,frozenScheme,frozenEligibility,frozenChefz,frozenKeeta,frozenWallet);
            BindGrossFromEvidence(c,tx,run,src,erpRider,assignments,frozenKeeta);
            DetectAndOpenSharedHold(c,tx,run,erpRider);
            rules.ValidateRider(src);
            decimal gross=J.Money(src.N("gross"));
            decimal commission=J.Money(src.N("commission"));
            J.Need(commission>=0&&commission<=gross,"العمولة بين صفر والإجمالي المجمّد.",400);
            // Chefz: RiderAmount is the entitlement; commission is Omar's already-netted figure — do not subtract again.
            bool chefz=string.Equals(run.S("app"),"chefz",StringComparison.OrdinalIgnoreCase);
            decimal entitlement=chefz?gross:J.Money(gross-commission);
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
                var row=Item("level1",code,amount,!operating,account,item.S("debtId"));
                // Preserve app-specific evidence fields for AdjustLevel1 (HS funding/eventType).
                if(item.S("fundingType")!="")row["fundingType"]=item.S("fundingType");
                if(item.S("eventType")!="")row["eventType"]=item.S("eventType");
                if(item.B("offsetsTopup"))row["offsetsTopup"]=true;
                if(item.ContainsKey("operating"))row["operating"]=item.B("operating");
                if(operating){companyOperating+=amount;l1Items.Add(row);}
                else{level1Personal+=amount;l1Items.Add(row);}
            }

            decimal wagesRoom=entitlement;
            decimal residualPersonal=rules.AdjustLevel1(src,l1Items,ref level1Personal,ref companyOperating,ref wagesRoom);

            // Mudad is L1 before NET/L2 (Codex RWC4); preserve through recalculation via request fields.
            decimal mudadAmount=0;
            string mudadLineId=src.S("mudadLineId");
            if(mudadLineId!="")
            {
                mudadAmount=J.Money(src.N("mudadAmount",src.N("amount")));
                J.Need(mudadAmount>=0,"مبلغ مدد لا يكون سالباً.",400);
                DemandMudadLine(c,tx,mudadLineId,run.S("companyId"),run.S("periodMonth"),erpRider,mudadAmount);
                level1Personal=J.Money(level1Personal+mudadAmount);
                l1Items.Add(Item("level1","mudad_salary",mudadAmount,true,"112505",null));
            }
            else
            {
                // Re-apply previously linked mudad on recalc so NET/L2 stay correct.
                mudadAmount=LoadExistingMudad(c,tx,payRunId,erpRider);
                if(mudadAmount>0)
                {
                    level1Personal=J.Money(level1Personal+mudadAmount);
                    l1Items.Add(Item("level1","mudad_salary",mudadAmount,true,"112505",null));
                }
            }

            decimal netAfterL1=J.Money(entitlement-level1Personal);
            // Keeta protected salary: net floor zero (§22.9.2).
            if(keetaIneligible&&string.Equals(scheme,"salary",StringComparison.OrdinalIgnoreCase)&&netAfterL1<0)
                netAfterL1=0;

            decimal level2Total=0;
            var l2Items=new List<JsonObject>();
            foreach(var item in (src["level2"] as JsonArray??[]).OfType<JsonObject>())
            {
                decimal amount=J.Money(item.N("amount"));
                J.Need(amount>=0,"مبلغ المستوى 2 لا يكون سالباً.",400);
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
                string? debtId=NullIfEmpty(it.S("debtId"));
                Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"sal","pay_run_item")}
                    (id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code,debt_id,created_by,created_at)
                    VALUES(@id,@pm,@r,@line,@stage,@code,@amt,@aff,@acc,@debt,@by,@at)
                    """,("@id",id),("@pm",run.S("periodMonth")),("@r",riderId),("@line",lineNo),
                    ("@stage",stage),("@code",it.S("itemCode")),("@amt",it.N("amount")),
                    ("@aff",it.B("affectsNet")?1:0),("@acc",it.S("accountCode")),
                    ("@debt",(object?)debtId??DBNull.Value),
                    ("@by",actor.Identity!.Name!),("@at",now));
                // Persist debt_id only here; settle balances at debt-confirm / salary-approve (SchemaDebt source guards).
            }
            Persist(Item("invoice","gross",gross,true,"212103",null),"invoice");
            if(commission!=0&&!chefz)Persist(Item("commission","commission",commission,true,"411104",null),"commission");
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
                ["createsNegativeDebt"]=createsNegativeDebt,["personalResidualDebt"]=residualPersonal,
                ["mudadAmount"]=mudadAmount
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
        var run=LoadRun(c,tx,actor,payRunId);
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
        var run=LoadRun(c,tx,actor,payRunId);
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

        DemandMudadLine(c,tx,lineId,run.S("companyId"),period,erpRider,amount);

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

        // Atomic reorder: mudad is L1 before NET/L2 (M-0669); never net−amount after L2 (RWC4b H13).
        decimal newNet=ReorderAfterMudad(c,tx,actor,riderId,period,amount,now);

        Store.Audit(c,tx,"sal.mudad_applied",dedId,actor.Identity!.Name!);
        return new(){["id"]=dedId,["payRunRiderId"]=riderId,["mudadLineId"]=lineId,["amount"]=amount,["salaryNet"]=newNet,["noteAr"]="مدد ضمن المستوى 1 قبل الصافي والمستوى 2."};
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
        SchemaFinPeriod.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);SchemaHrMudad.Ensure(c,tx);SchemaSalMudad.Ensure(c,tx);
        SchemaDebt.Ensure(c,tx);SchemaOperationCapture.Ensure(c,tx);
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
        cmd.CommandText=$"""
            SELECT id,company_id,app_code,period_month,stage,status,run_mode,payout_blocked,capture_state
            FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """;
        Add(cmd,"@id",id);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"مسير الرواتب غير موجود.",404);
        var run=new JsonObject
        {
            ["id"]=r["id"].ToString()!,["companyId"]=r["company_id"].ToString()!,
            ["app"]=r["app_code"].ToString()!,["periodMonth"]=ReadMonth(r["period_month"]),
            ["stage"]=Convert.ToInt32(r["stage"]),["status"]=Convert.ToInt32(r["status"]),
            ["runMode"]=Convert.ToInt32(r["run_mode"]),["payoutBlocked"]=Convert.ToInt32(r["payout_blocked"])==1,
            ["captureState"]=Convert.ToInt32(r["capture_state"])
        };
        r.Close();
        FinancialScopes.DemandSponsor(actor,run.S("companyId"));
        var platform=PayPlatforms.For(run.S("app"));
        J.Need(FinancialScopes.Platform(actor,platform.Platform),"المنصة خارج نطاق المستخدم.",403);
        return run;
    }

    static void DemandAssigned(List<JsonObject> assignments,string app,string erpRider)
    {
        if(assignments.Count==0)return;
        bool ok=AppAssignments(assignments,app).Any(a=>AssignmentRider(a)==erpRider);
        J.Need(ok,"المندوب غير معيّن في التقاط التجميد.",409);
    }

    /// <summary>Frozen timeline rows may cover every app the rider worked (H11); rows without appCode belong to the run's app.</summary>
    internal static IEnumerable<JsonObject> AppAssignments(IEnumerable<JsonObject> assignments,string app)
    {
        string own=PayPlatforms.For(app).App;
        return assignments.Where(a=>{string code=AssignmentApp(a);return code==""||code==own;});
    }
    internal static string AssignmentApp(JsonObject a)
    {
        string raw=a.S("appCode",a.S("platformCode"));
        return raw==""?"":PayPlatforms.For(raw).App;
    }
    internal static string AssignmentRider(JsonObject a)=>a.S("actualRiderProfileId",a.S("riderProfileId",a.S("erpRiderId",a.S("riderId"))));

    /// <summary>WC4D H1 (§40.1–40.2): the rider's gross comes only from HIS evidence — Chefz frozen settlement (bound in BindFromFrozen),
    /// else the distribution of his account's invoice line (sal.distribution), else (Keeta) the frozen invoice row of an account
    /// that only he worked. A caller gross is never money; a conflicting one is rejected.</summary>
    static void BindGrossFromEvidence(DbConnection c,DbTransaction tx,JsonObject run,JsonObject src,string erpRider,List<JsonObject> assignments,List<JsonObject> keeta)
    {
        string app=PayPlatforms.For(run.S("app")).App;
        if(app=="chefz")
        {
            J.Need(J.Money(src.N("commission"))==0,"شيفز: RiderAmount صافٍ من عمولة عمر؛ لا عمولة من الطلب.",409);
            src["commission"]=0;
            return;
        }
        decimal? bound=null;
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT d.share_amount,d.status FROM {SchemaSql.Q(c,"sal","distribution")} d
                INNER JOIN {SchemaSql.Q(c,"sal","platform_invoice")} i ON i.id=d.invoice_id
                WHERE i.company_id=@c AND i.app_code=@app AND i.period_month=@pm AND i.status<>4 AND d.erp_rider_id=@erp
                """;
            Add(cmd,"@c",run.S("companyId"));Add(cmd,"@app",app);Add(cmd,"@pm",run.S("periodMonth"));Add(cmd,"@erp",erpRider);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                J.Need(r["status"].ToString()!="needs_decision","توزيع فاتورة المندوب ينتظر قرار المراجع.",409);
                bound=J.Money((bound??0)+Convert.ToDecimal(r["share_amount"]));
            }
        }
        if(bound==null&&app=="keeta")
        {
            var own=AppAssignments(assignments,app).ToList();
            var accounts=own.Where(a=>AssignmentRider(a)==erpRider).Select(a=>a.S("platformRiderAccountId")).Where(a=>a!="").ToHashSet(StringComparer.Ordinal);
            var rows=keeta.Where(k=>accounts.Contains(k.S("erpPlatformAccountId"))).ToList();
            foreach(string account in rows.Select(k=>k.S("erpPlatformAccountId")).Distinct(StringComparer.Ordinal))
            {
                int riders=own.Where(a=>a.S("platformRiderAccountId")==account).Select(AssignmentRider).Distinct(StringComparer.Ordinal).Count();
                J.Need(riders==1,"صف فاتورة كيتا لحساب عليه أكثر من مندوب؛ وزّع الحصة أولاً (§40.2).",409);
            }
            if(rows.Count>0)bound=J.Money(rows.Sum(k=>J.Money(k.N("gross"))));
        }
        J.Need(bound!=null,"لا دليل مجمّد لإجمالي هذا المندوب (بند فاتورة حسابه أو توزيعه).",409);
        if(src.ContainsKey("gross")&&src["gross"] is not null&&J.Money(src.N("gross"))!=bound)
            throw new ApiError("الإجمالي في الطلب يتعارض مع دليل المندوب المجمّد.",409);
        src["gross"]=bound;
    }

    static List<JsonObject> LoadFrozenKind(DbConnection c,DbTransaction tx,string payRunId,string kind)
    {
        var rows=new List<JsonObject>();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT s.payload FROM {SchemaSql.Q(c,"fin","operation_snapshot_link")} l
            INNER JOIN {SchemaSql.Q(c,"sal","ops_snapshot")} s ON s.id=l.snapshot_id
            WHERE l.operation_type='pay_run' AND l.operation_id=@run AND l.snapshot_kind=@kind
            """;
        Add(cmd,"@run",payRunId);Add(cmd,"@kind",kind);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            var payload=System.Text.Json.Nodes.JsonNode.Parse(reader.GetString(0)) as JsonObject??new JsonObject();
            rows.Add(payload);
        }
        return rows;
    }

    static void DemandMudadLine(DbConnection c,DbTransaction tx,string lineId,string companyId,string periodMonth,string erpRider,decimal amount)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT company_id,period_month,erp_employee_id,basic_salary,match_status
            FROM {SchemaSql.Q(c,"hr","mudad_line")} WHERE id=@id
            """;
        Add(cmd,"@id",lineId);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"صف مدد غير موجود.",404);
        J.Need(Convert.ToInt32(r["match_status"])==2,"صف مدد غير مطابق.",404);
        J.Need(string.Equals(r["company_id"].ToString(),companyId,StringComparison.Ordinal),"صف مدد لشركة أخرى.",404);
        J.Need(string.Equals(ReadMonth(r["period_month"]),periodMonth.Length>=10?periodMonth[..10]:periodMonth,StringComparison.Ordinal)
            ||string.Equals(ReadMonth(r["period_month"]),periodMonth,StringComparison.Ordinal),"صف مدد لشهر آخر.",404);
        J.Need(r["erp_employee_id"] is not DBNull&&string.Equals(r["erp_employee_id"].ToString(),erpRider,StringComparison.Ordinal),"صف مدد لموظف آخر.",404);
        if(r["basic_salary"] is not DBNull)
            J.Need(J.Money(Convert.ToDecimal(r["basic_salary"]))==amount||amount<=J.Money(Convert.ToDecimal(r["basic_salary"])),"مبلغ مدد لا يطابق المصدر.",409);
    }

    static decimal LoadExistingMudad(DbConnection c,DbTransaction tx,string payRunId,string erpRider)
    {
        object? amt=SchemaSql.Scalar(c,tx,$"""
            SELECT COALESCE(SUM(d.amount),0) FROM {SchemaSql.Q(c,"sal","mudad_deduction")} d
            INNER JOIN {SchemaSql.Q(c,"sal","pay_run_rider")} r ON r.id=d.pay_run_rider_id
            WHERE r.pay_run_id=@run AND r.erp_rider_id=@erp
            """,("@run",payRunId),("@erp",erpRider));
        return amt is null or DBNull?0m:J.Money(Convert.ToDecimal(amt));
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

    static string? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();

    /// <summary>RWC4b H1: scheme/eligibility/gross/wallet from frozen capture; reject conflicting caller values.</summary>
    static void BindFromFrozen(JsonObject src,string app,string erpRider,
        List<JsonObject> schemes,List<JsonObject> eligibility,List<JsonObject> chefz,List<JsonObject> keeta,List<JsonObject> wallet)
    {
        var schemeRow=schemes.FirstOrDefault(s=>RiderMatch(s,erpRider));
        if(schemeRow!=null)
        {
            string frozenScheme=schemeRow.S("schemeCode","commission");
            if(frozenScheme is "salary" or "commission")
            {
                if(src.ContainsKey("schemeType")&&src.S("schemeType")!=""&&!string.Equals(src.S("schemeType"),frozenScheme,StringComparison.OrdinalIgnoreCase))
                    throw new ApiError("نظام الأجر في الطلب يتعارض مع التجميد.",409);
                src["schemeType"]=frozenScheme;
            }
        }
        var elig=eligibility.FirstOrDefault(s=>RiderMatch(s,erpRider));
        if(elig!=null)
        {
            bool frozenIneligible=!elig.B("eligible");
            if(src.ContainsKey("keetaIneligible")&&src.B("keetaIneligible")!=frozenIneligible)
                throw new ApiError("حالة أهلية كيتا في الطلب تتعارض مع التجميد.",409);
            src["keetaIneligible"]=frozenIneligible;
        }
        if(string.Equals(app,"chefz",StringComparison.OrdinalIgnoreCase))
        {
            var settle=chefz.FirstOrDefault(s=>RiderMatch(s,erpRider));
            J.Need(settle!=null,"شيفز: لا تسوية مجمّدة لهذا المندوب.",409);
            decimal frozenAmount=J.Money(settle!.N("RiderAmount",settle.N("riderAmount")));
            if(src.ContainsKey("riderAmount")&&src["riderAmount"] is not null&&J.Money(src.N("riderAmount"))!=frozenAmount)
                throw new ApiError("شيفز: RiderAmount لا يطابق التسوية المجمّدة.",409);
            if(src.ContainsKey("gross")&&J.Money(src.N("gross"))!=0&&J.Money(src.N("gross"))!=frozenAmount)
                throw new ApiError("شيفز: الإجمالي لا يطابق التسوية المجمّدة.",409);
            src["riderAmount"]=frozenAmount;
            src["gross"]=frozenAmount;
            src["frozenSettlement"]=true;
            src["frozenRiderAmount"]=frozenAmount;
        }
        // Keeta gross binds per account in BindGrossFromEvidence (WC4D H1) — never a sole-row fallback.
        _=keeta;
        if(string.Equals(app,"hs",StringComparison.OrdinalIgnoreCase))
        {
            var rows=wallet.Where(s=>RiderMatch(s,erpRider)).Select(s=>
            {
                var copy=s.DeepClone()!.AsObject();
                if(!copy.ContainsKey("amount"))copy["amount"]=copy.N("balance");
                return (JsonNode)copy;
            }).ToArray();
            src["frozenWallet"]=new JsonArray(rows);
        }
    }

    static bool RiderMatch(JsonObject row,string erpRider)
        =>string.Equals(row.S("erpRiderId",row.S("riderProfileId",row.S("actualRiderProfileId"))),erpRider,StringComparison.Ordinal);

    /// <summary>§40.5 / RWC4b H11: open shared hold when the rider already has another app run this month.</summary>
    static void DetectAndOpenSharedHold(DbConnection c,DbTransaction tx,JsonObject run,string erpRider)
    {
        var otherApps=new List<string>();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT DISTINCT pr.app_code FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.erp_rider_id=@erp AND r.period_month=@pm AND pr.app_code<>@app AND pr.status<>4
                """;
            Add(cmd,"@erp",erpRider);Add(cmd,"@pm",run.S("periodMonth"));Add(cmd,"@app",run.S("app"));
            using var reader=cmd.ExecuteReader();
            while(reader.Read())otherApps.Add(reader.GetString(0));
        }
        if(otherApps.Count==0)return;
        object? existing=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
            WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'
            """,("@erp",erpRider),("@pm",run.S("periodMonth")));
        string holdId;
        if(existing is null or DBNull)
        {
            holdId=SchemaSql.GuidText(SchemaSql.NewId());
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold")}
                (id,erp_rider_id,period_month,status,created_at)
                VALUES(@id,@erp,@pm,'open',@at)
                """,("@id",holdId),("@erp",erpRider),("@pm",run.S("periodMonth")),
                ("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        }
        else holdId=existing.ToString()!;
        foreach(string app in otherApps.Append(run.S("app")).Distinct(StringComparer.Ordinal))
        {
            object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold_app")} WHERE hold_id=@h AND app_code=@a",
                ("@h",holdId),("@a",app));
            if(has is null or DBNull)
                Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"sal","shared_rider_hold_app")}(id,hold_id,app_code,received) VALUES(@id,@h,@a,0)",
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@h",holdId),("@a",app));
        }
    }

    /// <summary>Rebuild L1 (incl. mudad) then cap L2 by remaining positive net — exact: 100−50 L1, L2 min(80,50)=50 → 0.</summary>
    static decimal ReorderAfterMudad(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string riderId,string period,decimal mudadAmount,string now)
    {
        decimal gross=0,commission=0,l1Other=0;
        var l2=new List<(string Code,decimal Amount,string Account,string? DebtId)>();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT stage_code,item_code,amount,account_code,debt_id FROM {SchemaSql.Q(c,"sal","pay_run_item")}
                WHERE pay_run_rider_id=@r AND amount<>0 ORDER BY line_no
                """;
            Add(cmd,"@r",riderId);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                string stage=r["stage_code"].ToString()!;
                string code=r["item_code"].ToString()!;
                decimal amt=Convert.ToDecimal(r["amount"]);
                string acc=r["account_code"].ToString()!;
                string? debt=r["debt_id"] is DBNull?null:r["debt_id"].ToString();
                if(stage=="invoice"&&code=="gross")gross=amt;
                else if(stage=="commission")commission=amt;
                else if(stage=="level1"&&code!="mudad_salary")l1Other=J.Money(l1Other+amt);
                else if(stage=="level2")l2.Add((code,amt,acc,debt));
            }
        }
        decimal entitlement=J.Money(gross-commission);
        decimal level1=J.Money(l1Other+mudadAmount);
        decimal netAfterL1=J.Money(Math.Max(0,entitlement-level1));
        decimal level2Total=0;
        var l2Takes=new List<(string Code,decimal Take,string Account,string? DebtId)>();
        foreach(var item in l2)
        {
            decimal room=Math.Max(0,netAfterL1-level2Total);
            decimal take=J.Money(Math.Min(item.Amount,room));
            level2Total=J.Money(level2Total+take);
            l2Takes.Add((item.Code,take,item.Account,item.DebtId));
        }
        decimal salaryNet=J.Money(netAfterL1-level2Total);
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run_item")} SET amount=0,updated_by=@by,updated_at=@at WHERE pay_run_rider_id=@r AND (item_code='mudad_salary' OR stage_code='level2' OR item_code='net_after_l1')",
            ("@by",actor.Identity!.Name!),("@at",now),("@r",riderId));
        int nextLine=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(MAX(line_no),0)+1 FROM {SchemaSql.Q(c,"sal","pay_run_item")} WHERE pay_run_rider_id=@r",("@r",riderId))!);
        void Ins(string stage,string code,decimal amt,string acc,string? debt)
        {
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","pay_run_item")}
                (id,period_month,pay_run_rider_id,line_no,stage_code,item_code,amount,affects_net,account_code,debt_id,created_by,created_at)
                VALUES(@id,@pm,@r,@line,@stage,@code,@amt,1,@acc,@debt,@by,@at)
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",period),("@r",riderId),("@line",nextLine++),
                ("@stage",stage),("@code",code),("@amt",amt),("@acc",acc),
                ("@debt",(object?)debt??DBNull.Value),("@by",actor.Identity!.Name!),("@at",now));
        }
        Ins("level1","mudad_salary",mudadAmount,"112505",null);
        Ins("net","net_after_l1",netAfterL1,"212103",null);
        foreach(var t in l2Takes)if(t.Take>0)Ins("level2",t.Code,t.Take,t.Account,t.DebtId);
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","pay_run_rider")} SET salary_net=@net,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@net",salaryNet),("@by",actor.Identity!.Name!),("@at",now),("@id",riderId));
        return salaryNet;
    }

    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (n,v) in values)Add(cmd,n,v);
        return cmd.ExecuteNonQuery();
    }
    static void Add(DbCommand cmd,string name,object value)
    {var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);}
}
