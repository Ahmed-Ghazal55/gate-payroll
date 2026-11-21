using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Payroll;

/// <summary>WC1I: export / request-release / request-payout open dual approvals in the approvals area;
/// mark-sent / release / payout need the recorded two-person decision. Exercises API-layer methods.</summary>
[TestClass]
public sealed class MoneyOutDualRuntimeTests
{
    static ClaimsPrincipal Checker=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-pay-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5,
        ["customPermissions"]=new JsonArray(ApprovalsArea.DualBankFile,ApprovalsArea.DualPaymentOut,ApprovalsArea.DualSharedRiderRelease,"approvals.decide","approvals.view")
    });

    [TestMethod]
    public void BankExportOpensDual_MarkSentNeedsRecordedDecision_ThroughApprovalsApi()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id,rider="2000000191";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic bank rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["iban"]="SA0380000000608010167519",["verifiedIban"]="SA0380000000608010167519"},tx);
            FinancialCore.PostJournal(c,tx,"opening","Synthetic payable","2026-10","synthetic-bank-open-dual",new JsonArray(
                FinancialCore.CreateJournalLine("212103",company,0,100,iqama:rider,targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine("411104",company,100,0,iqama:rider,targetChart:true,payChart:true)),"synthetic-opening",originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
            return true;
        });
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10"}));
        var exportInput=new JsonObject{["sponsorId"]=company,["period"]="2026-10",["previewToken"]=preview.S("previewToken"),["rows"]=preview["rows"]!.DeepClone()};
        // Ready-to-send export must open the dual request (kind, hash, amount) in the approvals area.
        f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,exportInput));
        string reference="bank-payout:"+company+":2026-10:2026-10";
        string fileHash=BankPayrollFiles.FileContentHash(preview["rows"]!.AsArray().OfType<JsonObject>(),preview["rows"]!.AsArray().OfType<JsonObject>().Sum(r=>r.N("amount")));
        var inbox=f.Store.Read(c=>ApprovalsArea.ListApprovals(c,Checker,null,false,null)["items"]!.AsArray().OfType<JsonObject>()
            .Where(a=>a.S("referenceId")==reference&&a.S("status")=="pending").ToList());
        Assert.AreEqual(1,inbox.Count);
        Assert.AreEqual(ApprovalsArea.DualBankFile,ApprovalsArea.ResolveDual(inbox[0]));
        Assert.AreEqual(fileHash,inbox[0].S("fileContentHash"));
        Assert.AreEqual(100m,inbox[0].N("amountSar"));
        // Mark-sent without the recorded decision is refused.
        var mark=exportInput.DeepClone().AsObject();mark["markSent"]=true;mark["bankAccountCode"]="111101";
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,mark))).Status);
        // Checker decides via approvals API; mark-sent then posts.
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,inbox[0].S("id"),new(){["decision"]="approve",["note"]="independent checker"}));
        f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,mark));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("transactionType")=="bank_payroll_payout")));
    }

    [TestMethod]
    public void SharedRiderRequestReleaseOpensDual_ReleaseNeedsRecordedDecision()
    {
        using var f=new PayFixture();
        string rider="2000000192",period="2026-10",company=FinancialCore.Sponsors[0].Id;
        string consolId=SharedRiderConsolidation.Id(rider,period);
        f.Store.Write((c,tx)=>{
            var doc=SharedRiderConsolidation.Ensure(c,tx,rider,period,company);
            doc["apps"]=new JsonObject{["a"]=new JsonObject{["net"]=50,["apps"]=new JsonArray("ninja")}};
            doc["missingApps"]=new JsonArray("keeta");
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var opened=f.Store.Write((c,tx)=>SharedRiderConsolidation.RequestRelease(c,tx,PayFixture.Maker,rider,period,"وصلت فاتورة كيتا يدويًا"));
        Assert.AreEqual("pending",opened.S("status"));
        Assert.AreEqual(consolId,opened.S("referenceId"));
        Assert.AreEqual(ApprovalsArea.DualSharedRiderRelease,ApprovalsArea.ResolveDual(opened));
        Assert.AreEqual(50m,opened.N("amountSar"));
        var inbox=f.Store.Read(c=>ApprovalsArea.ListApprovals(c,Checker,null,false,"pending")["items"]!.AsArray().OfType<JsonObject>()
            .Where(a=>a.S("referenceId")==consolId).ToList());
        Assert.AreEqual(1,inbox.Count);
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,Checker,rider,period,"وصلت فاتورة كيتا يدويًا"))).Status);
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,opened.S("id"),new(){["decision"]="approve",["note"]="independent checker"}));
        var released=f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,Checker,rider,period,"وصلت فاتورة كيتا يدويًا"));
        Assert.IsTrue(released.B("released"));
    }

    [TestMethod]
    public void AdvanceRequestPayoutOpensDual_PayOutNeedsRecordedDecision()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        string company=FinancialCore.Sponsors[0].Id,rider=HungerStationInvoiceTests.Rider;
        var custodian=UserAccess.Principal(new JsonObject{["username"]="synthetic-custodian",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=5,["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide","approvals.view")});
        string reqId="synthetic-adv-dual";
        f.Store.Write((c,tx)=>{
            var riderDoc=Store.Get(c,rider,"rider",tx)!;riderDoc["sponsorId"]=company;Store.Save(c,"rider",riderDoc,tx);
            foreach(var (id,role,perms) in new (string,string,JsonArray)[]{
                ("synthetic-pay-maker","Admin",new()),
                ("synthetic-pay-checker","Admin",new JsonArray(ApprovalsArea.DualPaymentOut,"approvals.decide","approvals.view")),
                ("synthetic-custodian","FinancialAccountant",new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide","approvals.view"))})
                Store.Save(c,"user",new(){["id"]=id,["username"]=id,["role"]=role,["department"]="FIN",["rankLevel"]=5,["customPermissions"]=perms},tx);
            OrgDirectory.AddMembership(c,tx,PayFixture.Maker,new(){["userId"]="synthetic-custodian",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,PayFixture.Maker,new(){["operation"]="advance.payout:MAIN.BW.DEBT",["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("synthetic-custodian"),["validFrom"]="2026-01-01"});
            FinancialCore.PostJournal(c,tx,"opening","Synthetic cash","2026-11","synthetic-adv-cash",new JsonArray(
                FinancialCore.CreateJournalLine("MAIN.BW.DEBT",company,500,0),
                FinancialCore.CreateJournalLine("3101",company,0,500)),"synthetic");
            Store.Save(c,AdvanceRequests.Kind,new(){["id"]=reqId,["type"]="cash",["vaultCode"]="MAIN.BW.DEBT",["companyId"]=company,["riderIqama"]=rider,["riderName"]="Synthetic Rider",["amount"]=100,["monthlyInstallment"]=50,["requestedBy"]="synthetic-supervisor",["approvedBy"]="synthetic-independent-reviewer",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["status"]="approved",["decisions"]=new JsonArray()},tx);
            return true;
        });
        var opened=f.Store.Write((c,tx)=>AdvanceRequests.RequestPayout(c,tx,custodian,reqId));
        Assert.AreEqual("pending",opened.S("status"));
        Assert.AreEqual("advance_payout",opened.S("kind"));
        Assert.AreEqual(ApprovalsArea.DualPaymentOut,ApprovalsArea.ResolveDual(opened));
        Assert.AreEqual(100m,opened.N("amountSar"));
        Assert.AreEqual("advance-payout:"+reqId,opened.S("referenceId"));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,custodian,reqId,new(){["reference"]="SYN-ADV-PAY"}))).Status);
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,opened.S("id"),new(){["decision"]="approve",["note"]="independent checker"}));
        var paid=f.Store.Write((c,tx)=>AdvanceRequests.PayOut(c,tx,custodian,reqId,new(){["reference"]="SYN-ADV-PAY"}));
        Assert.AreEqual("paid",paid.S("status"));
    }

    [TestMethod]
    public void DualRequestsPersistCompanyScope_AndReleaseHashRejectsChangedPayload()
    {
        using var f=new PayFixture();
        string rider="2000000193",period="2026-10",company=FinancialCore.Sponsors[0].Id;
        string consolId=SharedRiderConsolidation.Id(rider,period);
        f.Store.Write((c,tx)=>{
            var doc=SharedRiderConsolidation.Ensure(c,tx,rider,period,company);
            doc["apps"]=new JsonObject{["a"]=new JsonObject{["net"]=80,["apps"]=new JsonArray("ninja")}};
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var opened=f.Store.Write((c,tx)=>SharedRiderConsolidation.RequestRelease(c,tx,PayFixture.Maker,rider,period,"سبب الطلب الأصلي"));
        Assert.AreEqual(company,opened.S("companyId",opened.S("sponsorId")));
        Assert.IsTrue(f.Store.Read(c=>Convert.ToInt64(SchemaSql.Scalar(c,null,$"SELECT COUNT(1) FROM {SchemaSql.Q(c,"approvals","request")} WHERE id=@id",("@id",opened.S("id")))!)==1));
        // Scoped non-admin in the same company department sees the request.
        var scoped=UserAccess.Principal(new JsonObject
        {
            ["username"]="synthetic-scoped-fin",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray("Ninja"),["categoryCodes"]=new JsonArray()},
            ["customPermissions"]=new JsonArray(ApprovalsArea.DualSharedRiderRelease,"approvals.decide","approvals.view")
        });
        Assert.IsTrue(f.Store.Read(c=>FinancialScopes.Approval(scoped,opened,c)));
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,opened.S("id"),new(){["decision"]="approve",["note"]="hash bound"}));
        // Change a recorded net after approval → release recomputes hash and must refuse.
        f.Store.Write((c,tx)=>{
            var doc=Store.Get(c,consolId,SharedRiderConsolidation.Kind,tx)!;
            var apps=doc["apps"] as JsonObject??new JsonObject();
            apps["a"]=new JsonObject{["net"]=99,["apps"]=new JsonArray("ninja"),["cycleId"]="post-approval"};
            doc["apps"]=apps;
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,Checker,rider,period,"سبب الطلب الأصلي"))).Status);
        // Restore the approved net → release recomputes the same hash and proceeds.
        f.Store.Write((c,tx)=>{
            var doc=Store.Get(c,consolId,SharedRiderConsolidation.Kind,tx)!;
            var apps=doc["apps"] as JsonObject??new JsonObject();
            apps["a"]=new JsonObject{["net"]=80,["apps"]=new JsonArray("ninja")};
            doc["apps"]=apps;
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var released=f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,Checker,rider,period,"سبب الطلب الأصلي"));
        Assert.IsTrue(released.B("released"));
        Assert.IsTrue(f.Store.Read(c=>SchemaApprovals.ListDecisions(c,null).Any(d=>d.S("requestId")==opened.S("id")&&d.S("outcome")=="approved")));
    }

    [TestMethod]
    public void Release_RecomputesPayloadHash_RejectsWhenNetChangedAfterApproval()
    {
        using var f=new PayFixture();
        string rider="2000000194",period="2026-10",company=FinancialCore.Sponsors[0].Id,reason="وصلت فاتورة كيتا يدويًا";
        f.Store.Write((c,tx)=>{
            var doc=SharedRiderConsolidation.Ensure(c,tx,rider,period,company);
            doc["apps"]=new JsonObject{["a"]=new JsonObject{["net"]=50,["apps"]=new JsonArray("ninja"),["cycleId"]="c1",["companyId"]=company}};
            doc["missingApps"]=new JsonArray("keeta");
            Store.Save(c,SharedRiderConsolidation.Kind,doc,tx);
            return true;
        });
        var opened=f.Store.Write((c,tx)=>SharedRiderConsolidation.RequestRelease(c,tx,PayFixture.Maker,rider,period,reason));
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,opened.S("id"),new(){["decision"]="approve",["note"]="independent checker"}));
        // RecordAppNet-equivalent: another app net arrives after approval and changes CombinedNet.
        f.Store.Write((c,tx)=>{
            var cycle=new JsonObject{["id"]="c2",["period"]=period,["companyId"]=company};
            SharedRiderConsolidation.RecordAppNet(c,tx,cycle,rider,40m,["keeta"],["ninja","keeta"]);
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>SharedRiderConsolidation.Release(c,tx,Checker,rider,period,reason))).Status);
    }
}
