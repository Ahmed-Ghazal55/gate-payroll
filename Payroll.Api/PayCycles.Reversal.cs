using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PayCycles
{
    static JsonObject Reverse(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject cycle,JsonObject input)
    {
        PayValues.Fields(input,"action","period","note");string action=input.S("action");Scope(actor,cycle,action=="reversal_request"?"pay.manage":"pay.approve");J.Need(cycle.S("status") is "approved" or "partially_approved","العكس لدورة لها اعتماد رواتب فقط.");
        if(action=="reversal_request")
        {
            J.Need(cycle["reversalRequest"]==null,"طلب عكس موجود بالفعل.");string note=input.Required("note");J.Need(note.Length<=2000,"الملاحظة أطول من المسموح.");cycle["reversalRequest"]=new JsonObject{["requestedBy"]=actor.Identity!.Name,["period"]=J.Period(input.Required("period")),["note"]=note};Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_reversal_requested",cycle.S("id"),actor.Identity!.Name!);return cycle;
        }
        J.Need(action=="reversal_approve"&&cycle["reversalRequest"] is JsonObject,"طلب عكس مستقل مطلوب.");var request=cycle["reversalRequest"]!.AsObject();AccessPolicy.ValidateApproval(actor,new(){["requestedBy"]=request.S("requestedBy"),["targetDepartment"]="FIN",["requiredRankLevel"]=1},new JsonObject(),"pay.approve",c,tx,"pay.cycle_reversed",cycle);
        // Later debt activity must be resolved explicitly; never overwrite it with an old snapshot.
        foreach(var after in Rows(cycle["debtAfter"]).OfType<JsonObject>())J.Need(JsonNode.DeepEquals(after,Store.Get(c,after.S("id"),"debt",tx)),"حدثت حركة لاحقة على الدين؛ راجعها قبل عكس الدورة.");
        J.Need(!Store.List(c,Kind,tx).Any(d=>d.S("id")!=cycle.S("id")&&d.S("status") is "approved" or "partially_approved"&&string.CompareOrdinal(d.S("period"),cycle.S("period"))>0&&Lines(c,tx,d.S("id")).Any(l=>Lines(c,tx,cycle.S("id")).Any(old=>old.S("rider")==l.S("rider")))),"توجد دورة لاحقة؛ سوّ الحركات اللاحقة قبل العكس.");
        foreach(var rider in Rows(cycle["riders"]).OfType<JsonObject>())
        {
            decimal payable=Store.List(c,"journal",tx).Where(FinanceViews.Posted).SelectMany(j=>(j["lines"] as JsonArray??[]).OfType<JsonObject>()).Where(l=>l.S("sponsorId")==cycle.S("companyId")&&l.S("iqama")==rider.S("rider")&&l.S("accountCode") is "2110" or "212103").Sum(l=>l.N("credit")-l.N("debit"));
            J.Need(payable>=rider.N("net"),"تم صرف المستحق؛ سوّ الصرف قبل عكس الدورة.");
        }
        var journals=Store.List(c,"journal",tx).Where(j=>j.S("payCycleId")==cycle.S("id")&&j.S("reversesJournalId")=="").ToList();J.Need(journals.Count>0,"قيود الدورة غير موجودة.");
        var reversalIds=new Dictionary<string,string>();
        foreach(var journal in journals)
        {
            var reverse=new JsonArray();foreach(var old in journal["lines"]!.AsArray().OfType<JsonObject>()){var line=old.Clone();line["debit"]=old.N("credit");line["credit"]=old.N("debit");line["period"]=request.S("period");reverse.Add(line);}var posted=PayPosting.Post(c,tx,cycle,reverse,actor.Identity!.Name!,":reverse:"+journal.S("id"),journal.S("id"),request.S("period"));reversalIds[journal.S("id")]=posted.S("id");
        }
        foreach(var review in Rows(cycle["invoices"]).OfType<JsonObject>())
        {
            var invoice=Store.Get(c,review.S("invoiceId"),"platform_invoice",tx)!;
            if(invoice.S("payCycleId")!=cycle.S("id"))continue;
            invoice["reversalJournalEntryId"]=reversalIds[invoice.S("journalEntryId")];invoice["accountingStatus"]="reversed";Store.Save(c,"platform_invoice",invoice,tx);
        }
        // W1 ledger: reverse payroll_instalment / carryover movements and their journals before restoring documents.
        ReverseLedgerDebtEffects(c,tx,cycle,actor.Identity!.Name!,request.S("period"));
        foreach(var before in Rows(cycle["debtBefore"]).OfType<JsonObject>())Store.Save(c,"debt",before.Clone(),tx);
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("carryDebtId")!=""))
        {var debt=Store.Get(c,row.S("carryDebtId"),"debt",tx)!;debt["status"]="reversed";debt["remainingBalance"]=0;debt["reversedBy"]=actor.Identity!.Name;debt["reversalCycleId"]=cycle.S("id");Store.Save(c,"debt",debt,tx);
            if(Guid.TryParse(debt.S("id"),out var carryGuid))
                SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET status='cancelled' WHERE id=@id AND balance=0",("@id",SchemaSql.GuidText(carryGuid)));}
        foreach(var ledger in Store.List(c,"ledger",tx).Where(l=>l.S("sourceRef")==cycle.S("id")&&l.S("kind") is "debt_installment" or "stage4" or "fuel_usage"))Store.Save(c,"ledger_reversal",new(){["id"]="pay-reverse:"+ledger.S("id"),["reversesLedgerId"]=ledger.S("id"),["cycleId"]=cycle.S("id"),["amount"]=-ledger.N("amount"),["approvedBy"]=actor.Identity!.Name,["period"]=request.S("period")},tx);
        cycle["status"]="reversed";cycle["reversedBy"]=actor.Identity!.Name;cycle["reversalPeriod"]=request.S("period");Store.Save(c,Kind,cycle,tx);Store.Audit(c,tx,"pay.cycle_reversed",cycle.S("id"),actor.Identity!.Name!);return cycle;
    }

    static void ReverseLedgerDebtEffects(DbConnection c,DbTransaction tx,JsonObject cycle,string actor,string period)
    {
        SchemaDebt.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        var periodMonth=DateOnly.Parse(J.Period(period)+"-01");
        string cycleId=cycle.S("id");
        // Movements posted for this cycle's instalments (ledger document journalEntryId) and carryovers.
        var journalIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var ledger in Store.List(c,"ledger",tx).Where(l=>l.S("sourceRef")==cycleId&&l.S("kind")=="debt_installment"&&l.S("journalEntryId")!=""))
            journalIds.Add(ledger.S("journalEntryId"));
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("carryDebtId")!=""))
        {
            if(!Guid.TryParse(row.S("carryDebtId"),out var carryId))continue;
            using var cmd=Store.Command(c,tx,$"""
                SELECT id,journal_id,amount,debt_id,company_id,source_type,source_id FROM {SchemaSql.Q(c,"debt","debt_movement")}
                WHERE debt_id=@d AND source_type IN ('carryover_doc','carryover_112507','payroll_instalment')
                """,("@d",SchemaSql.GuidText(carryId)));
            using var reader=cmd.ExecuteReader();
            while(reader.Read())
            {
                if(reader["journal_id"] is not DBNull)journalIds.Add(reader["journal_id"].ToString()!);
            }
        }
        // Also pick payroll_instalment movements whose journal is linked from ledger rows above, or by period + debt in debtBefore/After.
        foreach(var debtDoc in Rows(cycle["debtBefore"]).OfType<JsonObject>().Concat(Rows(cycle["debtAfter"]).OfType<JsonObject>()))
        {
            if(!Guid.TryParse(debtDoc.S("id"),out var debtId))continue;
            using var cmd=Store.Command(c,tx,$"""
                SELECT id,journal_id,amount,debt_id,company_id,source_id FROM {SchemaSql.Q(c,"debt","debt_movement")}
                WHERE debt_id=@d AND source_type='payroll_instalment' AND period_month=@pm
                """,("@d",SchemaSql.GuidText(debtId)),("@pm",periodMonth.ToString("yyyy-MM-01")));
            using var reader=cmd.ExecuteReader();
            var pending=new List<(Guid MovId,Guid? JournalId,decimal Amount,Guid DebtId,Guid CompanyId,Guid SourceId)>();
            while(reader.Read())
            {
                Guid movId=reader["id"] is Guid g?g:Guid.Parse(reader["id"].ToString()!);
                Guid? jid=reader["journal_id"] is DBNull?null:reader["journal_id"] is Guid jg?jg:Guid.Parse(reader["journal_id"].ToString()!);
                decimal amt=Convert.ToDecimal(reader["amount"]);
                Guid companyId=reader["company_id"] is Guid cg?cg:Guid.Parse(reader["company_id"].ToString()!);
                Guid sourceId=reader["source_id"] is Guid sg?sg:Guid.Parse(reader["source_id"].ToString()!);
                pending.Add((movId,jid,amt,debtId,companyId,sourceId));
                if(jid is Guid jj)journalIds.Add(SchemaSql.GuidText(jj));
            }
            reader.Close();
            foreach(var (movId,jid,amt,dId,companyId,sourceId) in pending)
            {
                // Already reversed?
                object? prior=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"debt","debt_movement")} WHERE source_type='payroll_reversal' AND source_id=@s",
                    ("@s",SchemaSql.GuidText(movId)));
                if(prior!=null)continue;
                Guid revSource=SchemaSql.NewId();
                Guid? revJournal=null;
                if(jid is Guid originalJournal)
                {
                    var reversed=JournalPosting.Reverse(c,tx,originalJournal,actor,revSource,"عكس استقطاع مديونية من المسير");
                    revJournal=reversed.JournalId;
                }
                string now=DateTimeOffset.UtcNow.ToString("O");
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
                    (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,journal_id,run_mode,payout_blocked,created_by,created_at)
                    VALUES(@id,@pm,@d,@c,@on,@amt,'payroll_reversal',@sid,@j,0,0,@by,@at)
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",periodMonth.ToString("yyyy-MM-01")),
                    ("@d",SchemaSql.GuidText(dId)),("@c",SchemaSql.GuidText(companyId)),
                    ("@on",periodMonth.ToString("yyyy-MM-dd")),("@amt",-amt),
                    ("@sid",SchemaSql.GuidText(movId)),
                    ("@j",(object?)(revJournal is Guid rj?SchemaSql.GuidText(rj):null)??DBNull.Value),
                    ("@by",actor),("@at",now));
            }
        }
        // Reverse carryover openings for debts created by this cycle.
        foreach(var row in Rows(cycle["riders"]).OfType<JsonObject>().Where(r=>r.S("carryDebtId")!=""))
        {
            if(!Guid.TryParse(row.S("carryDebtId"),out var carryId))continue;
            using var cmd=Store.Command(c,tx,$"""
                SELECT id,journal_id,amount,company_id FROM {SchemaSql.Q(c,"debt","debt_movement")}
                WHERE debt_id=@d AND source_type IN ('carryover_doc','carryover_112507')
                """,("@d",SchemaSql.GuidText(carryId)));
            using var reader=cmd.ExecuteReader();
            var rows=new List<(Guid MovId,Guid? JournalId,decimal Amount,Guid CompanyId)>();
            while(reader.Read())
            {
                rows.Add((
                    reader["id"] is Guid g?g:Guid.Parse(reader["id"].ToString()!),
                    reader["journal_id"] is DBNull?null:reader["journal_id"] is Guid jg?jg:Guid.Parse(reader["journal_id"].ToString()!),
                    Convert.ToDecimal(reader["amount"]),
                    reader["company_id"] is Guid cg?cg:Guid.Parse(reader["company_id"].ToString()!)));
            }
            reader.Close();
            foreach(var (movId,jid,amt,companyId) in rows)
            {
                object? prior=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"debt","debt_movement")} WHERE source_type='payroll_reversal' AND source_id=@s",
                    ("@s",SchemaSql.GuidText(movId)));
                if(prior!=null)continue;
                Guid revSource=SchemaSql.NewId();
                Guid? revJournal=null;
                if(jid is Guid originalJournal)
                {
                    var reversed=JournalPosting.Reverse(c,tx,originalJournal,actor,revSource,"عكس ترحيل عجز الراتب");
                    revJournal=reversed.JournalId;
                }
                string now=DateTimeOffset.UtcNow.ToString("O");
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
                    (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,journal_id,run_mode,payout_blocked,created_by,created_at)
                    VALUES(@id,@pm,@d,@c,@on,@amt,'payroll_reversal',@sid,@j,0,0,@by,@at)
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pm",periodMonth.ToString("yyyy-MM-01")),
                    ("@d",SchemaSql.GuidText(carryId)),("@c",SchemaSql.GuidText(companyId)),
                    ("@on",periodMonth.ToString("yyyy-MM-dd")),("@amt",-amt),
                    ("@sid",SchemaSql.GuidText(movId)),
                    ("@j",(object?)(revJournal is Guid rj?SchemaSql.GuidText(rj):null)??DBNull.Value),
                    ("@by",actor),("@at",now));
            }
        }
    }

    public static JsonObject ReverseForTests(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject cycle,JsonObject input)=>Reverse(c,tx,actor,cycle,input);
    public static void ReverseLedgerDebtEffectsForTests(DbConnection c,DbTransaction tx,JsonObject cycle,string actor,string period)=>ReverseLedgerDebtEffects(c,tx,cycle,actor,period);
    public static decimal DebtInstalmentAmountForTests(DbConnection c,DbTransaction? tx,JsonObject debt)=>DebtLedger.InstalmentForPayCycle(c,tx,debt);
    public static void GuardLegacyRun(DbConnection c,DbTransaction tx,JsonObject run)
    {if(run.S("approvedAt")=="")return;J.Need(!Store.List(c,Kind,tx).Any(cycle=>cycle.S("status") is "approved" or "partially_approved"&&cycle.S("period")==run.S("period")&&Lines(c,tx,cycle.S("id")).Any(l=>l.S("rider")==run.S("iqama"))),"المندوب معتمد في المسار الجديد لنفس الشهر؛ لا تنشئ مسيرًا ثانيًا.");}
}
