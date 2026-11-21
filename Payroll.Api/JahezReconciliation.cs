using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>BR §37 Jahez reconciliation: collections + payments on behalf vs summary invoice through last-transaction date; VAT as its own line; proposed journals; maker-checker.</summary>
public static class JahezReconciliation
{
    public const string Kind="jahez_reconciliation";
    public const string Receivable="112801",Clearing="212601",SettlementBank="111204",OutputVat="213201";
    static void DemandWrite(ClaimsPrincipal actor)=>J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.transfer")||AccessPolicy.Has(actor,"treasury.jahez.receive"),"صلاحية خزينة جاهز مطلوبة.",403);
    static void RefuseDuplicateActive(DbConnection c,DbTransaction tx,string companyId,string period,string? exceptId=null)
    {
        bool clash=Store.List(c,Kind,tx).Any(d=>d.S("companyId")==companyId&&d.S("period")==period&&(d.S("status") is "draft" or "pending_approval" or "approved")&&(exceptId==null||d.S("id")!=exceptId));
        J.Need(!clash,"تقرير مطابقة جاهز لنفس الشركة والشهر موجود؛ أغلقه أو اعتمد واحدًا فقط.",409);
    }
    public static JsonObject Build(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandWrite(actor);PayValues.Fields(input,"companyId","period","invoiceNet","invoiceVat","lastTransactionDate","invoiceReference","notes");
        var sponsor=FinancialCore.ResolveSponsor(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,sponsor.Id);
        string period=J.Period(input.Required("period"));string lastRaw=input.Required("lastTransactionDate");
        J.Need(DateOnly.TryParseExact(lastRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var last),"تاريخ آخر معاملة غير صالح.");
        var monthStart=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);J.Need(last>=monthStart&&last<monthStart.AddMonths(1),"آخر معاملة داخل شهر الفاتورة.");
        RefuseDuplicateActive(c,tx,sponsor.Id,period);
        decimal invoiceNet=J.Money(PayValues.Number(input,"invoiceNet",-1000000000)),invoiceVat=J.Money(PayValues.Number(input,"invoiceVat"));
        J.Need(invoiceVat>=0,"ضريبة الفاتورة غير سالبة.");
        decimal deliveryFees=0,cashCollections=0,driverAdjustments=0,bonusesTips=0,paidOnBehalf=0,commission=0;
        var details=new JsonArray();
        foreach(var row in Store.List(c,"jahez_transaction",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("occurredAt"),monthStart,last)))
        {
            decimal delivery=row.N("deliveryPrice"),cash=row.N("cashAmount"),adj=row.N("driverAdjustment");
            deliveryFees=J.Money(deliveryFees+delivery);cashCollections=J.Money(cashCollections+cash);driverAdjustments=J.Money(driverAdjustments+adj);
            details.Add(Line("transaction",row.S("id"),row.S("riderId"),row.S("occurredAt"),delivery,cash,adj,0,0));
        }
        foreach(var row in Store.List(c,"jahez_earnings_statement",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&StatementInRange(r,monthStart,last)))
        {
            decimal tips=row.N("tips")+row.N("bonuses");bonusesTips=J.Money(bonusesTips+tips);
            details.Add(Line("earnings",row.S("id"),row.S("riderId"),row.S("to",row.S("from")),0,0,0,tips,0));
        }
        foreach(var row in Store.List(c,"erp_money_movement",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("occurredAt"),monthStart,last)))
        {
            // Payments the company made on behalf of Jahez (direction/purpose tagged in ERP).
            if(row.S("directionCode") is "2" or "Out" or "out"||row.S("purposeCode").Contains("Behalf",StringComparison.OrdinalIgnoreCase)||row.S("purposeCode").Contains("OnBehalf",StringComparison.OrdinalIgnoreCase)||row.N("dashboardDebtPaid")>0)
            {
                decimal amount=row.N("amount")+row.N("dashboardDebtPaid");paidOnBehalf=J.Money(paidOnBehalf+amount);
                details.Add(Line("money_movement",row.S("id"),row.S("riderId"),row.S("occurredAt"),0,0,0,0,amount));
            }
        }
        foreach(var row in Store.List(c,"erp_jahez_operation",tx).Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("executedAt"),monthStart,last)))
        {
            decimal fee=row.N("commission");if(fee==0)fee=row.N("calculatedCommission");commission=J.Money(commission+fee);
            details.Add(Line("operation_commission",row.S("id"),row.S("riderId"),row.S("executedAt"),0,0,0,0,0,fee));
        }
        // BR §37: company due − collections + paid on behalf = expected transfer net.
        decimal companyDue=J.Money(deliveryFees+bonusesTips+commission+driverAdjustments);
        decimal expectedNet=J.Money(companyDue-cashCollections+paidOnBehalf);
        decimal variance=J.Money(expectedNet-invoiceNet);
        var proposed=new JsonArray
        {
            Journal("clearing","مقاصة جاهز",Clearing,Receivable,Math.Min(Math.Max(0,cashCollections),Math.Max(0,companyDue))),
            Journal("transfer","تحويل تسوية جاهز",SettlementBank,Receivable,Math.Max(0,expectedNet)),
            Journal("vat","إثبات ضريبة مخرجات جاهز",Receivable,OutputVat,invoiceVat)
        };
        var doc=new JsonObject
        {
            ["id"]="jahez-recon:"+Guid.NewGuid().ToString("N"),["kind"]=Kind,["companyId"]=sponsor.Id,["period"]=period,
            ["invoiceReference"]=input.S("invoiceReference"),["lastTransactionDate"]=last.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["invoiceNet"]=invoiceNet,["invoiceVat"]=invoiceVat,["notes"]=input.S("notes"),
            ["totals"]=new JsonObject{["deliveryFees"]=deliveryFees,["cashCollections"]=cashCollections,["driverAdjustments"]=driverAdjustments,["bonusesTips"]=bonusesTips,["paidOnBehalf"]=paidOnBehalf,["commission"]=commission,["companyDue"]=companyDue,["expectedNet"]=expectedNet,["variance"]=variance},
            ["details"]=details,["proposedJournals"]=proposed,
            ["status"]="draft",["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["submittedBy"]="",["submittedAt"]="",["approvedBy"]="",["approvedAt"]="",["journalEntryIds"]=new JsonArray()
        };
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_built",doc.S("id"),actor.Identity!.Name!);return doc;
    }
    public static JsonObject Submit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status")=="draft","التقرير ليس مسودة.");
        doc["status"]="pending_approval";doc["submittedBy"]=actor.Identity!.Name;doc["submittedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_submitted",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject DecideVariance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status") is "draft" or "pending_approval","التقرير مغلق.");
        string decision=input.S("varianceDecision").Trim();J.Need(decision.Length is >=3 and <=500,"قرار الفرق من 3 إلى 500 حرف.");
        doc["varianceDecision"]=decision;doc["varianceDecidedBy"]=actor.Identity!.Name;doc["varianceDecidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_variance_decided",id,actor.Identity!.Name!);return doc;
    }
    public static JsonObject Approve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);var doc=Store.Get(c,id,Kind,tx)??throw new ApiError("تقرير المطابقة غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(doc.S("status")=="pending_approval","التقرير بانتظار الاعتماد.");
        RefuseDuplicateActive(c,tx,doc.S("companyId"),doc.S("period"),doc.S("id"));
        // BR §37: maker-checker for Jahez reconciliation (submitter and creator); §35 bypass via Independent.
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=doc.S("submittedBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"treasury.jahez.receive",c,tx,"jahez.reconciliation_approved",doc);
        J.Need(AccessPolicy.Independent(actor,doc.S("submittedBy"),"jahez.reconciliation_approved.submittedBy",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),"لا يجوز اعتماد تقرير قدّمته؛ يلزم مراجع مستقل أو صلاحية تجاوز مؤرخة.",403);
        J.Need(AccessPolicy.Independent(actor,doc.S("createdBy"),"jahez.reconciliation_approved.createdBy",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),"لا يجوز اعتماد تقرير أنشأته؛ يلزم مراجع مستقل أو صلاحية تجاوز مؤرخة.",403);
        decimal variance=doc["totals"]!.AsObject().N("variance");
        J.Need(variance==0||doc.S("varianceDecision").Length>=3,"فرق المطابقة يحتاج قرارًا مكتوبًا قبل الاعتماد.");
        var ids=new JsonArray();string period=doc.S("period"),company=doc.S("companyId"),actorName=actor.Identity!.Name!;
        foreach(var j in doc["proposedJournals"]!.AsArray().OfType<JsonObject>().Where(j=>j.N("amount")>0))
        {
            // VAT posts with named accounts (BR §37). Clearing/transfer use current chart.
            string debit=j.S("debit"),credit=j.S("credit");decimal amount=j.N("amount");
            var lines=new JsonArray(
                FinancialCore.CreateJournalLine(debit,company,amount,0,j.S("labelAr"),targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine(credit,company,0,amount,j.S("labelAr"),targetChart:true,payChart:true));
            var journal=FinancialCore.PostJournal(c,tx,"jahez_reconciliation",j.S("labelAr"),period,doc.S("id")+":"+j.S("code"),lines,actorName,"","",company,company,targetChart:true,payChart:true);
            ids.Add(journal.S("id"));
        }
        doc["status"]="approved";doc["approvedBy"]=actorName;doc["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");doc["journalEntryIds"]=ids;
        Store.Save(c,Kind,doc,tx);Store.Audit(c,tx,"jahez.reconciliation_approved",id,actorName);return doc;
    }
    /// <summary>U-W2-19: open a day run into treasury.jahez_recon_run. Al-Togary vaults stay isolated (X18).</summary>
    public static JsonObject OpenDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandWrite(actor);SchemaTreasury.Ensure(c,tx);
        var sponsor=FinancialCore.ResolveSponsor(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,sponsor.Id);
        DateOnly businessDate=ParseDay(input.Required("businessDate"));
        DateOnly periodMonth=new(businessDate.Year,businessDate.Month,1);
        byte version=(byte)(input.ContainsKey("versionNo")?(int)PayValues.Number(input,"versionNo",3):3);
        J.Need(version is 2 or 3,"إصدار المطابقة 2 أو 3 فقط.");
        string vaultCode=input.S("vaultCode").Trim();
        bool togaryRun=IsTogaryVault(vaultCode)||TreasuryBalancesApi.IsTogary(sponsor.Code,sponsor.ShortNameAr,sponsor.Id);
        if(togaryRun&&vaultCode=="")vaultCode="togary";
        // One open/approved day per company+date+version (UQ_treasury_jahez_recon_day).
        object? existing=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"treasury","jahez_recon_run")}
            WHERE company_id=@c AND business_date=@d AND version_no=@v AND status<>5
            """,("@c",sponsor.Id),("@d",businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@v",version));
        J.Need(existing is null or DBNull,"تشغيل مطابقة جاهز لهذا اليوم موجود مسبقاً.",409);

        string state="open";
        bool waiting=Store.List(c,"jahez_handover",tx).Any(h=>h.S("sponsorId")==sponsor.Id
            &&h.S("businessDate").StartsWith(businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),StringComparison.Ordinal)
            &&h.S("status") is "pending" or "Pending");
        if(waiting)state="waiting_handover";

        var lines=BuildDayLines(c,tx,sponsor.Id,businessDate,vaultCode,togaryRun);
        decimal omar=J.Money(lines.Sum(l=>l.N("expectedAmount")));
        decimal handed=J.Money(lines.Sum(l=>l.N("handedAmount")));
        decimal transferOmar=J.Money(Store.List(c,"erp_money_movement",tx)
            .Where(r=>InScope(c,tx,r,sponsor.Id)&&InRange(r.S("occurredAt"),businessDate,businessDate)
                &&(r.S("fund").Contains("Bank",StringComparison.OrdinalIgnoreCase)||r.S("purposeCode").Contains("Settlement",StringComparison.OrdinalIgnoreCase)))
            .Where(r=>VaultMatches(r,vaultCode,togaryRun))
            .Sum(r=>r.N("amount")));
        decimal transferDiff=J.Money(transferOmar-omar);

        string id=SchemaSql.GuidText(SchemaSql.NewId());
        string actorName=actor.Identity!.Name!;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","jahez_recon_run")}
            (id,company_id,period_month,business_date,version_no,reconciliation_state,status,created_by,created_at)
            VALUES(@id,@c,@m,@d,@v,@st,1,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",id),("@c",sponsor.Id),("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@d",businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@v",version),
            ("@st",state),("@by",actorName));
        foreach(var line in lines)
        {
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"treasury","jahez_recon_line")}
                (id,recon_run_id,company_id,period_month,erp_vault_id,erp_handover_id,expected_amount,handed_amount)
                VALUES(@id,@r,@c,@m,@v,@h,@e,@ha)
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@r",id),("@c",sponsor.Id),
                ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@v",line.S("erpVaultId")),("@h",(object?)NullIfEmpty(line.S("erpHandoverId"))??DBNull.Value),
                ("@e",line.N("expectedAmount")),("@ha",line.N("handedAmount")));
        }
        // Document mirror for allowedActions / variance text (first journals stay on the run after approve).
        var doc=new JsonObject
        {
            ["id"]=id,["kind"]="jahez_recon_day",["companyId"]=sponsor.Id,["businessDate"]=businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["periodMonth"]=periodMonth.ToString("yyyy-MM",CultureInfo.InvariantCulture),["versionNo"]=version,
            ["vaultCode"]=vaultCode,["isAlTogary"]=togaryRun,["reconciliationState"]=state,["status"]="draft",
            ["omarTransferAmount"]=transferOmar,["opsSumAmount"]=omar,["handedAmount"]=handed,["transferDiff"]=transferDiff,
            ["lineCount"]=lines.Count,["createdBy"]=actorName,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["varianceDecision"]="",["journalEntryIds"]=new JsonArray(),["firstJournalsImmutable"]=true,
            ["allowedActions"]=new JsonArray("submit","variance")
        };
        Store.Save(c,"jahez_recon_day",doc,tx);
        Store.Audit(c,tx,"jahez.day_opened",id,actorName);
        return doc;
    }

    public static JsonObject SubmitDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status")=="draft","تشغيل اليوم ليس مسودة.");
        J.Need(doc.S("reconciliationState")!="waiting_handover","لا يُعتمد يوم بانتظار تسليم عهدة مؤكد.",409);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"treasury","jahez_recon_run")} SET status=2,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
        doc["status"]="pending_approval";doc["submittedBy"]=actor.Identity!.Name;doc["submittedAt"]=DateTimeOffset.UtcNow.ToString("O");
        doc["allowedActions"]=new JsonArray("approve","variance");
        Store.Save(c,"jahez_recon_day",doc,tx);Store.Audit(c,tx,"jahez.day_submitted",id,actor.Identity!.Name!);
        return doc;
    }

    public static JsonObject DecideDayVariance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        DemandWrite(actor);var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status") is "draft" or "pending_approval","تشغيل اليوم مغلق.");
        string decision=input.S("varianceDecision").Trim();J.Need(decision.Length is >=3 and <=500,"قرار الفرق من 3 إلى 500 حرف.");
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"treasury","jahez_recon_run")} SET reconciliation_state='variance_decided',updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
        doc["varianceDecision"]=decision;doc["reconciliationState"]="variance_decided";
        doc["varianceDecidedBy"]=actor.Identity!.Name;doc["varianceDecidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,"jahez_recon_day",doc,tx);Store.Audit(c,tx,"jahez.day_variance_decided",id,actor.Identity!.Name!);
        return doc;
    }

    /// <summary>Approve-the-day: posts missing summary journals once; first journals immutable (T9). Maker ≠ checker.</summary>
    public static JsonObject ApproveDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandWrite(actor);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);SchemaTreasury.Ensure(c,tx);
        var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status")=="pending_approval","تشغيل اليوم بانتظار الاعتماد.");
        J.Need(doc.S("reconciliationState")!="waiting_handover","لا اعتماد مع تسليم غير مؤكد.",409);
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=doc.S("submittedBy",doc.S("createdBy")),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"treasury.jahez.receive",c,tx,"jahez.day_approved",doc);
        J.Need(AccessPolicy.Independent(actor,doc.S("submittedBy",doc.S("createdBy")),"jahez.day_approved",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),
            "لا يجوز اعتماد يوم قدّمته؛ يلزم مراجع مستقل.",403);
        J.Need(AccessPolicy.Independent(actor,doc.S("createdBy"),"jahez.day_approved.createdBy",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),
            "لا يجوز اعتماد يوم أنشأته؛ يلزم مراجع مستقل.",403);
        decimal transferDiff=doc.N("transferDiff");
        J.Need(transferDiff==0||doc.S("varianceDecision").Length>=3,"فرق التحويل يحتاج قرارًا مكتوبًا قبل اعتماد اليوم.");

        // First journals: clearing + transfer for matched day totals; never rewritten — corrections are reverse/correct later (T9).
        var ids=new JsonArray();
        string company=doc.S("companyId"),period=doc.S("periodMonth"),actorName=actor.Identity!.Name!;
        decimal handed=doc.N("handedAmount"),ops=doc.N("opsSumAmount");
        decimal clearingAmt=J.Money(Math.Min(Math.Max(0,handed),Math.Max(0,ops)));
        decimal transferAmt=J.Money(Math.Max(0,ops));
        if(clearingAmt>0)
            ids.Add(PostLegacyDayJournal(c,tx,company,period,id,"clearing","مقاصة جاهز يومية",Clearing,Receivable,clearingAmt,actorName));
        if(transferAmt>0)
            ids.Add(PostLegacyDayJournal(c,tx,company,period,id,"transfer","تحويل تسوية جاهز يومية",SettlementBank,Receivable,transferAmt,actorName));

        string approverId=SchemaSql.GuidText(SchemaSql.NewId());
        try{approverId=SchemaApprovals.EnsureAuthUserId(c,tx,actorName);}catch(ApiError){/* synthetic */}
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","jahez_recon_run")}
            SET status=3,reconciliation_state='day_approved',approved_by=@by,approved_at={SchemaSql.NowLiteral(c)},updated_at={SchemaSql.NowLiteral(c)}
            WHERE id=@id
            """,("@id",id),("@by",approverId));
        doc["status"]="approved";doc["reconciliationState"]="day_approved";doc["approvedBy"]=actorName;
        doc["approvedAt"]=DateTimeOffset.UtcNow.ToString("O");doc["journalEntryIds"]=ids;doc["firstJournalsImmutable"]=true;
        doc["allowedActions"]=new JsonArray();
        Store.Save(c,"jahez_recon_day",doc,tx);
        Store.Audit(c,tx,"jahez.day_approved",id,actorName);
        return doc;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        // Day routes before {id} so "day" is not captured as an id.
        api.MapPost("/finance/jahez-reconciliations/day",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>OpenDay(c,tx,ctx.User,input))).WithTags("Banks and treasury").WithSummary("Open Jahez recon day run (Al-Togary isolated)");
        api.MapGet("/finance/jahez-reconciliations/day/{id}",(string id,HttpContext ctx)=>store.Read(c=>LoadDay(c,null,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Read Jahez day recon run");
        api.MapPost("/finance/jahez-reconciliations/day/{id}/submit",(string id,HttpContext ctx)=>store.Write((c,tx)=>SubmitDay(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Submit Jahez day recon for approval");
        api.MapPost("/finance/jahez-reconciliations/day/{id}/variance",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>DecideDayVariance(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Record day transfer variance decision");
        api.MapPost("/finance/jahez-reconciliations/day/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>ApproveDay(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Approve Jahez day (immutable first journals)");
        api.MapGet("/finance/jahez-reconciliations",(HttpContext ctx)=>
        {
            AccessPolicy.Demand(ctx.User,"treasury.view");
            return store.Read(c=>Store.List(c,Kind).Where(d=>FinancialScopes.Sponsor(ctx.User,d.S("companyId"))).OrderByDescending(d=>d.S("createdAt")));
        }).WithTags("Banks and treasury").WithSummary("List Jahez reconciliation reports");
        api.MapGet("/finance/jahez-reconciliations/{id}",(string id,HttpContext ctx)=>store.Read(c=>
        {
            AccessPolicy.Demand(ctx.User,"treasury.view");var doc=Store.Get(c,id,Kind)??throw new ApiError("تقرير المطابقة غير موجود.",404);
            FinancialScopes.DemandSponsor(ctx.User,doc.S("companyId"));return doc;
        })).WithTags("Banks and treasury").WithSummary("Read one Jahez reconciliation report");
        api.MapPost("/finance/jahez-reconciliations",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Build(c,tx,ctx.User,input))).WithTags("Banks and treasury").WithSummary("Build Jahez reconciliation report §37");
        api.MapPost("/finance/jahez-reconciliations/{id}/submit",(string id,HttpContext ctx)=>store.Write((c,tx)=>Submit(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Submit Jahez reconciliation for approval");
        api.MapPost("/finance/jahez-reconciliations/{id}/variance",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>DecideVariance(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Record variance decision for Jahez reconciliation");
        api.MapPost("/finance/jahez-reconciliations/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>Approve(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Approve Jahez reconciliation (maker-checker)");
    }
    static bool InScope(DbConnection c,DbTransaction tx,JsonObject row,string companyId)
    {
        if(row.S("companyId")==companyId)return true;
        string account=row.S("accountId",row.S("platformAccountId"));if(account=="")return false;
        var acc=Store.Get(c,account,"platform_account",tx);return acc!=null&&acc.S("companyId")==companyId;
    }
    static bool InRange(string occurredAt,DateOnly first,DateOnly last)
    {
        if(occurredAt=="")return false;
        if(DateTimeOffset.TryParse(occurredAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)){var timestampDay=DateOnly.FromDateTime(at.DateTime);return timestampDay>=first&&timestampDay<=last;}
        if(DateOnly.TryParse(occurredAt,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dateOnly))return dateOnly>=first&&dateOnly<=last;
        return false;
    }
    static bool StatementInRange(JsonObject row,DateOnly first,DateOnly last)
    {
        string from=row.S("from"),to=row.S("to",from);return DateOnly.TryParse(from,CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)&&DateOnly.TryParse(to,CultureInfo.InvariantCulture,DateTimeStyles.None,out var end)&&start>=first&&end<=last;
    }
    static JsonObject Line(string kind,string sourceId,string riderId,string at,decimal delivery,decimal cash,decimal adj,decimal tips,decimal paid,decimal commission=0)=>
        new(){["kind"]=kind,["sourceId"]=sourceId,["riderId"]=riderId,["at"]=at,["deliveryFees"]=J.Money(delivery),["cashCollections"]=J.Money(cash),["driverAdjustments"]=J.Money(adj),["bonusesTips"]=J.Money(tips),["paidOnBehalf"]=J.Money(paid),["commission"]=J.Money(commission)};
    static JsonObject Journal(string code,string label,string debit,string credit,decimal amount)=>
        new(){["code"]=code,["labelAr"]=label,["debit"]=debit,["credit"]=credit,["amount"]=J.Money(Math.Max(0,amount))};
    static JsonObject LoadDay(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        var doc=Store.Get(c,id,"jahez_recon_day",tx)??throw new ApiError("تشغيل مطابقة اليوم غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));
        return doc;
    }
    static List<JsonObject> BuildDayLines(DbConnection c,DbTransaction tx,string companyId,DateOnly day,string vaultCode,bool togaryRun)
    {
        var lines=new List<JsonObject>();
        foreach(var h in Store.List(c,"jahez_handover",tx).Where(r=>InScope(c,tx,r,companyId)
            &&r.S("businessDate").StartsWith(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),StringComparison.Ordinal)
            &&r.S("status") is "confirmed" or "Confirmed" or "approved")
            .Where(r=>VaultMatches(r,vaultCode,togaryRun)))
        {
            lines.Add(new()
            {
                ["erpVaultId"]=VaultGuid(h.S("vaultCode",h.S("cashboxCode","MAIN"))),
                ["erpHandoverId"]=AsGuidOrEmpty(h.S("erpSourceId",h.S("id"))),
                ["expectedAmount"]=J.Money(h.N("cashboxAmount")+h.N("petroAppAmount")+h.N("bankAmount")),
                ["handedAmount"]=J.Money(h.N("cashboxAmount")+h.N("petroAppAmount")+h.N("bankAmount"))
            });
        }
        if(lines.Count==0)
        {
            // Ops rollup when no confirmed handover rows exist yet (still open / matched from operations).
            decimal ops=J.Money(Store.List(c,"erp_jahez_operation",tx)
                .Where(r=>InScope(c,tx,r,companyId)&&InRange(r.S("executedAt"),day,day))
                .Where(r=>VaultMatches(r,vaultCode,togaryRun))
                .Sum(r=>r.N("net",r.N("commission"))));
            if(ops!=0)
                lines.Add(new(){["erpVaultId"]=VaultGuid(vaultCode==""?"MAIN":vaultCode),["erpHandoverId"]="",["expectedAmount"]=ops,["handedAmount"]=0m});
        }
        return lines;
    }
    static bool VaultMatches(JsonObject row,string vaultCode,bool togaryRun)
    {
        string code=row.S("vaultCode",row.S("fund",row.S("cashboxCode")));
        bool rowTogary=IsTogaryVault(code)||IsTogaryVault(row.S("nameAr"));
        if(togaryRun)return rowTogary||IsTogaryVault(vaultCode);
        if(vaultCode!="")return string.Equals(code,vaultCode,StringComparison.OrdinalIgnoreCase)||code=="";
        return !rowTogary; // non-Togary day never pulls Al-Togary vault rows
    }
    static bool IsTogaryVault(string? text)=>!string.IsNullOrWhiteSpace(text)&&(text.Contains("togary",StringComparison.OrdinalIgnoreCase)||text.Contains("تجار",StringComparison.Ordinal)||text.Contains("التجارية",StringComparison.Ordinal));
    static string VaultGuid(string vaultCode)
    {
        if(Guid.TryParse(vaultCode,out var g))return SchemaSql.GuidText(g);
        byte[] hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("jahez-vault:"+vaultCode));
        Span<byte> bytes=stackalloc byte[16];hash.AsSpan(0,16).CopyTo(bytes);
        bytes[6]=(byte)((bytes[6]&0x0F)|0x50);bytes[8]=(byte)((bytes[8]&0x3F)|0x80);
        return SchemaSql.GuidText(new Guid(bytes));
    }
    static string AsGuidOrEmpty(string raw)
    {
        if(raw=="")return "";
        if(Guid.TryParse(raw,out var g))return SchemaSql.GuidText(g);
        return VaultGuid(raw);
    }
    static string? NullIfEmpty(string s)=>string.IsNullOrWhiteSpace(s)?null:s;
    static DateOnly ParseDay(string raw)
    {
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"تاريخ يوم المطابقة غير صالح.");
        return d;
    }
    static string PostLegacyDayJournal(DbConnection c,DbTransaction tx,string company,string period,string runId,string code,string label,string debit,string credit,decimal amount,string actor)
    {
        var lines=new JsonArray(
            FinancialCore.CreateJournalLine(debit,company,amount,0,label,targetChart:true,payChart:true),
            FinancialCore.CreateJournalLine(credit,company,0,amount,label,targetChart:true,payChart:true));
        var journal=FinancialCore.PostJournal(c,tx,"jahez_recon_day",label,period,runId+":"+code,lines,actor,"","",company,company,targetChart:true,payChart:true);
        return journal.S("id");
    }
}
