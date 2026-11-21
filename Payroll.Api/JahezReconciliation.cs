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
    static void DemandDayWrite(ClaimsPrincipal actor)
    {
        DemandWrite(actor);
        J.Need(FinancialScopes.Platform(actor,"Jahez"),"المنصة خارج النطاق المالي.",403);
    }
    static void RefuseDuplicateActive(DbConnection c,DbTransaction tx,string companyId,string period,string? exceptId=null)
    {
        bool clash=Store.List(c,Kind,tx).Any(d=>d.S("companyId")==companyId&&d.S("period")==period&&(d.S("status") is "draft" or "pending_approval" or "approved")&&(exceptId==null||d.S("id")!=exceptId));
        J.Need(!clash,"تقرير مطابقة جاهز لنفس الشركة والشهر موجود؛ أغلقه أو اعتمد واحدًا فقط.",409);
    }
    public static JsonObject Build(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandWrite(actor);PayValues.Fields(input,"companyId","period","invoiceNet","invoiceVat","lastTransactionDate","invoiceReference","notes");
        var sponsor=FinancialCore.ResolveSponsor(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,sponsor.Id);
        J.Need(FinancialScopes.Platform(actor,"Jahez"),"المنصة خارج النطاق المالي.",403);
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
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(FinancialScopes.Platform(actor,"Jahez"),"المنصة خارج النطاق المالي.",403);J.Need(doc.S("status")=="draft","التقرير ليس مسودة.");
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
        FinancialScopes.DemandSponsor(actor,doc.S("companyId"));J.Need(FinancialScopes.Platform(actor,"Jahez"),"المنصة خارج النطاق المالي.",403);J.Need(doc.S("status")=="pending_approval","التقرير بانتظار الاعتماد.");
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
        DemandDayWrite(actor);SchemaTreasury.Ensure(c,tx);
        var sponsor=FinancialCore.ResolveSponsor(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,sponsor.Id);
        DateOnly businessDate=ParseDay(input.Required("businessDate"));
        DateOnly periodMonth=new(businessDate.Year,businessDate.Month,1);
        byte version=(byte)(input.ContainsKey("versionNo")?(int)PayValues.Number(input,"versionNo",3):3);
        J.Need(version is 2 or 3,"إصدار المطابقة 2 أو 3 فقط.");
        string vaultCode=input.S("vaultCode").Trim();
        string vaultId=input.S("vaultId").Trim();
        bool togaryRun=IsTogaryVault(vaultCode)||TreasuryBalancesApi.IsTogary(sponsor.Code,sponsor.ShortNameAr,sponsor.Id);
        if(togaryRun&&vaultCode=="")vaultCode="togary";
        // One open/approved day per company+date+version (UQ_treasury_jahez_recon_day).
        object? existing=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"treasury","jahez_recon_run")}
            WHERE company_id=@c AND business_date=@d AND version_no=@v AND status<>5
            """,("@c",sponsor.Id),("@d",businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@v",version));
        J.Need(existing is null or DBNull,"تشغيل مطابقة جاهز لهذا اليوم موجود مسبقاً.",409);

        bool hasConfirmed=ConfirmHandoverExists(c,tx,sponsor.Id,businessDate,vaultId,vaultCode,togaryRun);
        string state=hasConfirmed?"open":"waiting_handover";

        var lines=BuildDayLines(c,tx,sponsor.Id,businessDate,vaultId,vaultCode,togaryRun);
        decimal handedCash=J.Money(lines.Sum(l=>l.N("handedCashAmount")));
        decimal handedBank=J.Money(lines.Sum(l=>l.N("handedBankAmount")));
        decimal handedExternal=J.Money(lines.Sum(l=>l.N("handedExternalAmount")));
        decimal opsSum=handedCash;
        decimal handedAmount=handedCash;
        decimal transferOmar=OmarTransferAmount(c,tx,sponsor.Id,businessDate,vaultId,vaultCode,togaryRun);
        decimal transferDiff=J.Money(transferOmar-handedCash);

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
        var allowedActions=state=="waiting_handover"?new JsonArray("variance"):new JsonArray("submit","variance");
        var doc=new JsonObject
        {
            ["id"]=id,["kind"]="jahez_recon_day",["companyId"]=sponsor.Id,["businessDate"]=businessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["periodMonth"]=periodMonth.ToString("yyyy-MM",CultureInfo.InvariantCulture),["versionNo"]=version,
            ["vaultCode"]=vaultCode,["vaultId"]=vaultId,["isAlTogary"]=togaryRun,["reconciliationState"]=state,["status"]="draft",
            ["omarTransferAmount"]=transferOmar,["opsSumAmount"]=opsSum,
            ["handedAmount"]=handedAmount,["handedCashAmount"]=handedCash,["handedBankAmount"]=handedBank,["handedExternalAmount"]=handedExternal,
            ["transferDiff"]=transferDiff,
            ["lineCount"]=lines.Count,["createdBy"]=actorName,["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),
            ["varianceDecision"]="",["journalEntryIds"]=new JsonArray(),["correctionJournalEntryIds"]=new JsonArray(),["reversalJournalEntryIds"]=new JsonArray(),
            ["firstJournalsImmutable"]=true,
            ["allowedActions"]=allowedActions
        };
        Store.Save(c,"jahez_recon_day",doc,tx);
        Store.Audit(c,tx,"jahez.day_opened",id,actorName);
        return doc;
    }

    public static JsonObject SubmitDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandDayWrite(actor);var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status")=="draft","تشغيل اليوم ليس مسودة.");
        RefuseIfWaitingHandover(c,tx,doc);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"treasury","jahez_recon_run")} SET status=2,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
        doc["status"]="pending_approval";doc["submittedBy"]=actor.Identity!.Name;doc["submittedAt"]=DateTimeOffset.UtcNow.ToString("O");
        doc["allowedActions"]=new JsonArray("approve","variance");
        Store.Save(c,"jahez_recon_day",doc,tx);Store.Audit(c,tx,"jahez.day_submitted",id,actor.Identity!.Name!);
        return doc;
    }

    public static JsonObject DecideDayVariance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        DemandDayWrite(actor);var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status") is "draft" or "pending_approval","تشغيل اليوم مغلق.");
        RefuseIfWaitingHandover(c,tx,doc);
        string decision=input.S("varianceDecision").Trim();J.Need(decision.Length is >=3 and <=500,"قرار الفرق من 3 إلى 500 حرف.");
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"treasury","jahez_recon_run")} SET reconciliation_state='variance_decided',updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@id",id));
        doc["varianceDecision"]=decision;doc["reconciliationState"]="variance_decided";
        doc["varianceDecidedBy"]=actor.Identity!.Name;doc["varianceDecidedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,"jahez_recon_day",doc,tx);Store.Audit(c,tx,"jahez.day_variance_decided",id,actor.Identity!.Name!);
        return doc;
    }

    /// <summary>Approve-the-day: posts summary journals through JournalPosting; first journals immutable (T9). Maker ≠ checker.</summary>
    public static JsonObject ApproveDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandDayWrite(actor);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);SchemaTreasury.Ensure(c,tx);
        var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status")=="pending_approval","تشغيل اليوم بانتظار الاعتماد.");
        RefuseIfWaitingHandover(c,tx,doc);
        AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=doc.S("submittedBy",doc.S("createdBy")),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"treasury.jahez.receive",c,tx,"jahez.day_approved",doc);
        J.Need(AccessPolicy.Independent(actor,doc.S("submittedBy",doc.S("createdBy")),"jahez.day_approved",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),
            "لا يجوز اعتماد يوم قدّمته؛ يلزم مراجع مستقل.",403);
        J.Need(AccessPolicy.Independent(actor,doc.S("createdBy"),"jahez.day_approved.createdBy",c,tx,doc,scope:AccessPolicy.ScopeInvoiceApprovals),
            "لا يجوز اعتماد يوم أنشأته؛ يلزم مراجع مستقل.",403);
        decimal transferDiff=doc.N("transferDiff");
        J.Need(transferDiff==0||doc.S("varianceDecision").Length>=3,"فرق التحويل يحتاج قرارًا مكتوبًا قبل اعتماد اليوم.");

        string company=doc.S("companyId");
        DateOnly businessDate=ParseDay(doc.S("businessDate"));
        DateOnly periodMonth=ParsePeriodMonth(doc.S("periodMonth"));
        Guid runId=Guid.Parse(id);
        string actorName=actor.Identity!.Name!;
        decimal clearingAmt=J.Money(Math.Max(0,doc.N("handedAmount")));
        decimal transferAmt=J.Money(Math.Max(0,doc.N("omarTransferAmount")));
        var ids=new JsonArray();
        if(clearingAmt>0)
        {
            var jid=PostDayJournal(c,tx,company,periodMonth,businessDate,runId,"jahez_recon_clearing","مقاصة جاهز يومية",Clearing,Receivable,clearingAmt,actorName);
            if(jid!="")ids.Add(jid);
        }
        if(transferAmt>0)
        {
            var jid=PostDayJournal(c,tx,company,periodMonth,businessDate,runId,"jahez_recon_transfer","تحويل تسوية جاهز يومية",SettlementBank,Receivable,transferAmt,actorName);
            if(jid!="")ids.Add(jid);
        }

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

    /// <summary>After approval: reverse immutable first journals and post corrected entries (T9).</summary>
    public static JsonObject CorrectDay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        DemandDayWrite(actor);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        var doc=LoadDay(c,tx,actor,id);
        J.Need(doc.S("status")=="approved","تشغيل اليوم غير معتمد.");
        J.Need(doc.B("firstJournalsImmutable"),"اليوم ليس بقيد أولي ثابت.",409);
        var prior=doc["journalEntryIds"]!.AsArray().OfType<JsonValue>().Select(v=>v.ToString()).Where(s=>!string.IsNullOrWhiteSpace(s)).ToList();
        J.Need(prior.Count>0,"لا قيود أولية لعكسها.",409);

        string company=doc.S("companyId");
        DateOnly businessDate=ParseDay(doc.S("businessDate"));
        DateOnly periodMonth=ParsePeriodMonth(doc.S("periodMonth"));
        Guid runId=Guid.Parse(id);
        string actorName=actor.Identity!.Name!;
        decimal clearingAmt=input.ContainsKey("clearingAmount")?J.Money(PayValues.Number(input,"clearingAmount")):J.Money(Math.Max(0,doc.N("handedAmount")));
        decimal transferAmt=input.ContainsKey("transferAmount")?J.Money(PayValues.Number(input,"transferAmount")):J.Money(Math.Max(0,doc.N("omarTransferAmount")));

        var reversed=new JsonArray();
        foreach(var jid in prior)
        {
            var rev=JournalPosting.Reverse(c,tx,Guid.Parse(jid!),actorName,SchemaSql.NewId(),"عكس قيد مطابقة جاهز يومية");
            reversed.Add(SchemaSql.GuidText(rev.JournalId));
        }
        var corrected=new JsonArray();
        if(clearingAmt>0)
        {
            var jid=PostDayJournal(c,tx,company,periodMonth,businessDate,runId,"jahez_recon_clearing_correct","تصحيح مقاصة جاهز يومية",Clearing,Receivable,clearingAmt,actorName);
            if(jid!="")corrected.Add(jid);
        }
        if(transferAmt>0)
        {
            var jid=PostDayJournal(c,tx,company,periodMonth,businessDate,runId,"jahez_recon_transfer_correct","تصحيح تحويل تسوية جاهز يومية",SettlementBank,Receivable,transferAmt,actorName);
            if(jid!="")corrected.Add(jid);
        }

        doc["reversalJournalEntryIds"]=reversed;doc["correctionJournalEntryIds"]=corrected;
        doc["correctedBy"]=actorName;doc["correctedAt"]=DateTimeOffset.UtcNow.ToString("O");
        if(input.ContainsKey("notes"))doc["correctionNotes"]=input.S("notes");
        Store.Save(c,"jahez_recon_day",doc,tx);
        Store.Audit(c,tx,"jahez.day_corrected",id,actorName);
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
        api.MapPost("/finance/jahez-reconciliations/day/{id}/correct",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>CorrectDay(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Reverse and correct Jahez day journals after approval");
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
        if(row.S("companyId")==companyId||row.S("sponsorId")==companyId)return true;
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
    static bool HandoverDay(JsonObject row,DateOnly day)
    {
        string d=row.S("businessDate",row.S("date"));
        return d.StartsWith(day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),StringComparison.Ordinal);
    }
    static bool IsConfirmedHandover(JsonObject h)=>h.S("status") is "confirmed" or "Confirmed" or "approved" or "2"
        ||h.S("statusCode") is "2" or "Confirmed";
    static bool ConfirmHandoverExists(DbConnection c,DbTransaction tx,string companyId,DateOnly day,string vaultId,string vaultCode,bool togaryRun)=>
        Store.List(c,"erp_cashbox_handover",tx)
            .Where(h=>InScope(c,tx,h,companyId)&&HandoverDay(h,day)&&IsConfirmedHandover(h))
            .Any(h=>VaultMatches(h,vaultId,vaultCode,togaryRun));
    static void RefuseIfWaitingHandover(DbConnection c,DbTransaction tx,JsonObject doc)
    {
        DateOnly day=ParseDay(doc.S("businessDate"));
        bool hasConfirmed=ConfirmHandoverExists(c,tx,doc.S("companyId"),day,doc.S("vaultId"),doc.S("vaultCode"),doc.B("isAlTogary"));
        J.Need(hasConfirmed,"لا يُعتمد يوم بانتظار تسليم عهدة مؤكد.",409);
    }
    static List<JsonObject> BuildDayLines(DbConnection c,DbTransaction tx,string companyId,DateOnly day,string vaultId,string vaultCode,bool togaryRun)
    {
        var lines=new List<JsonObject>();
        foreach(var h in Store.List(c,"erp_cashbox_handover",tx)
                    .Where(r=>InScope(c,tx,r,companyId)&&HandoverDay(r,day)&&IsConfirmedHandover(r))
                    .Where(r=>VaultMatches(r,vaultId,vaultCode,togaryRun)))
        {
            decimal cash=J.Money(h.N("cashboxAmount"));
            decimal bank=J.Money(h.N("bankAmount"));
            decimal external=J.Money(h.N("petroAppAmount"));
            lines.Add(new()
            {
                ["erpVaultId"]=VaultGuid(h.S("vaultId",h.S("vaultCode",h.S("cashboxCode","MAIN")))),
                ["erpHandoverId"]=AsGuidOrEmpty(h.S("erpSourceId",h.S("id"))),
                ["expectedAmount"]=cash,
                ["handedAmount"]=cash,
                ["handedCashAmount"]=cash,
                ["handedBankAmount"]=bank,
                ["handedExternalAmount"]=external
            });
        }
        return lines;
    }
    static decimal OmarTransferAmount(DbConnection c,DbTransaction tx,string companyId,DateOnly day,string vaultId,string vaultCode,bool togaryRun)=>
        J.Money(Store.List(c,"erp_money_movement",tx)
            .Where(r=>InScope(c,tx,r,companyId)&&InRange(r.S("occurredAt"),day,day)&&IsBankTransferMovement(r))
            .Where(r=>VaultMatches(r,vaultId,vaultCode,togaryRun))
            .Sum(r=>r.N("amount")));
    static bool IsBankTransferMovement(JsonObject row)
    {
        string fund=row.S("fund"),fundCode=row.S("fundCode"),purpose=row.S("purposeCode");
        return fund is "Bank" or "3"||fundCode is "3"
            ||fund.Contains("Bank",StringComparison.OrdinalIgnoreCase)
            ||purpose.Contains("Settlement",StringComparison.OrdinalIgnoreCase);
    }
    static bool VaultMatches(JsonObject row,string vaultId,string vaultCode,bool togaryRun)
    {
        string rowVaultId=row.S("vaultId");
        string code=row.S("vaultCode",row.S("cashboxCode",row.S("fund")));
        bool rowTogary=IsTogaryVault(code);
        if(togaryRun)
        {
            if(vaultId!="")
                return string.Equals(rowVaultId,vaultId,StringComparison.OrdinalIgnoreCase);
            return rowTogary||IsTogaryVault(code);
        }
        if(rowTogary)return false;
        if(vaultId!="")
            return string.Equals(rowVaultId,vaultId,StringComparison.OrdinalIgnoreCase);
        if(vaultCode!="")
            return string.Equals(code,vaultCode,StringComparison.OrdinalIgnoreCase);
        return true;
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
    static DateOnly ParsePeriodMonth(string periodRaw)
    {
        if(periodRaw.Length==7)periodRaw+="-01";
        if(periodRaw.Length>10)periodRaw=periodRaw[..10];
        J.Need(DateOnly.TryParseExact(periodRaw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var m),"شهر الفترة غير صالح.");
        return new DateOnly(m.Year,m.Month,1);
    }
    static string PostDayJournal(DbConnection c,DbTransaction tx,string companyId,DateOnly periodMonth,DateOnly journalDate,Guid runId,string purpose,string narration,string debitCode,string creditCode,decimal amount,string actor)
    {
        if(amount<=0)return "";
        string debitId=AccountIdByCode(c,tx,debitCode);
        string creditId=AccountIdByCode(c,tx,creditCode);
        Guid periodId=EnsureOpenPeriod(c,tx,companyId,periodMonth);
        var lines=new List<JournalPosting.Line>
        {
            new(Guid.Parse(debitId),SchemaOrgCostCentre.NoneId,"Jahez",amount,0m,narration),
            new(Guid.Parse(creditId),SchemaOrgCostCentre.NoneId,"Jahez",0m,amount,narration)
        };
        var posted=JournalPosting.Post(c,tx,new(
            Guid.Parse(companyId),periodId,periodMonth,journalDate,
            "jahez_recon_day",runId,purpose,0,false,actor,lines,NarrationAr:narration));
        return SchemaSql.GuidText(posted.JournalId);
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
}