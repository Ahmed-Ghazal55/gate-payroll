using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ValidateGapsAndBankParallelTests
{
    [TestMethod]
    public void DiagnoseValidateGapsReturnsCountsOnlyForMissingAssignmentAndCategory()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]="2000000091",["iqama"]="2000000091",["name"]="Synthetic",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active"},tx);
            Store.Save(c,"rider",new(){["id"]="2000000092",["iqama"]="2000000092",["name"]="Synthetic",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["payCategory"]="sponsored_private"},tx);
            Store.Save(c,"platform_account",new(){["id"]="gap-none",["companyId"]=company,["platformCode"]="ninja",["assignments"]=new JsonArray()},tx);
            Store.Save(c,"platform_account",new(){["id"]="gap-one",["companyId"]=company,["platformCode"]="ninja",["assignments"]=new JsonArray(new JsonObject{["riderId"]="2000000091",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]="2026-10-31"})},tx);
            Store.Save(c,"platform_account",new(){["id"]="gap-multi",["companyId"]=company,["platformCode"]="ninja",["assignments"]=new JsonArray(
                new JsonObject{["riderId"]="2000000091",["EffectiveFrom"]="2026-10-01",["EffectiveTo"]="2026-10-15"},
                new JsonObject{["riderId"]="2000000092",["EffectiveFrom"]="2026-10-16",["EffectiveTo"]="2026-10-31"})},tx);
            return true;
        });
        var report=f.Store.Read(c=>PayCycles.DiagnoseValidateGaps(c,PayFixture.Maker,company,"2026-10","ninja"));
        var counts=report["counts"]!.AsObject();
        Assert.AreEqual(3m,counts.N("accounts_total"));
        Assert.AreEqual(1m,counts.N("accounts_no_assignment_overlap"));
        Assert.AreEqual(1m,counts.N("accounts_with_one_operator"));
        Assert.AreEqual(1m,counts.N("accounts_multi_operator"));
        Assert.AreEqual(1m,counts.N("riders_missing_category"));
        Assert.AreEqual(0m,counts.N("riders_invalid_category"));
        string json=report.ToJsonString();
        Assert.IsFalse(json.Contains("2000000091"));Assert.IsFalse(json.Contains("2000000092"));Assert.IsFalse(json.Contains("gap-"));
    }
    [TestMethod]
    public void BankFileComparisonOnlyMarksDoNotSendOnPreviewAndExportFilename()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id,rider="2000000093";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic bank rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["iban"]="SA0380000000608010167519",["verifiedIban"]="SA0380000000608010167519"},tx);
            FinancialCore.PostJournal(c,tx,"opening","Synthetic payable", "2026-10","synthetic-bank-parallel",new JsonArray(
                FinancialCore.CreateJournalLine("212103",company,0,100,iqama:rider,targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine("411104",company,100,0,iqama:rider,targetChart:true,payChart:true)),"synthetic-opening",originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
            return true;
        });
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10",["comparisonOnly"]=true}));
        Assert.IsTrue(preview.B("doNotSend"));Assert.AreEqual(BankPayrollFiles.DoNotSendMark,preview.S("doNotSendMark"));
        Assert.IsTrue(preview["rows"]!.AsArray().OfType<JsonObject>().All(r=>!r.S("notes").Contains(BankPayrollFiles.DoNotSendMark,StringComparison.Ordinal)));
        var file=f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10",["comparisonOnly"]=true,["previewToken"]=preview.S("previewToken"),["rows"]=preview["rows"]!.DeepClone()}));
        Assert.IsTrue(file.Filename.StartsWith("DO-NOT-SEND_",StringComparison.Ordinal));
        string csv=Encoding.UTF8.GetString(file.Bytes);Assert.DoesNotContain(BankPayrollFiles.DoNotSendMark,csv);
        Assert.IsTrue(csv.StartsWith("\uFEFF"+BankPayrollFiles.Header+"\r\n",StringComparison.Ordinal)||(file.Bytes[0]==0xEF&&Encoding.UTF8.GetString(file.Bytes[3..]).StartsWith(BankPayrollFiles.Header+"\r\n",StringComparison.Ordinal)));
        var audit=f.Store.Read(c=>Store.List(c,"audit").Single(a=>a.S("action")=="finance.bankfile.export_comparison_only"));
        Assert.IsTrue(audit.B("doNotSend"));
    }

    [TestMethod]
    public void MarkSentUsesStoredPayoutBlockedNotRequestFlags()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id,rider="2000000094";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic bank rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["iban"]="SA0380000000608010167519",["verifiedIban"]="SA0380000000608010167519"},tx);
            Store.Save(c,PayCycles.Kind,new(){["id"]="cycle-blocked",["companyId"]=company,["period"]="2026-10",["status"]="approved",["payoutBlocked"]=true,["runMode"]=0,["riders"]=new JsonArray(new JsonObject{["rider"]=rider,["salaryApprovedAt"]=DateTimeOffset.UtcNow.ToString("O"),["net"]=100})},tx);
            return true;
        });
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10"}));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,new()
        {
            ["sponsorId"]=company,["period"]="2026-10",["markSent"]=true,["previewToken"]=preview.S("previewToken"),
            ["rows"]=preview["rows"]!.DeepClone()
        }))).Status);
    }

    [TestMethod]
    public void MarkSentRequiresRecordedTwoPersonDualWithoutBypass()
    {
        using var f=new PayFixture();string company=FinancialCore.Sponsors[0].Id,rider="2000000095";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic bank rider",["sponsorId"]=company,["isDeliveryRider"]=true,["status"]="Active",["iban"]="SA0380000000608010167519",["verifiedIban"]="SA0380000000608010167519"},tx);
            FinancialCore.PostJournal(c,tx,"opening","Synthetic payable","2026-10","synthetic-bank-dual",new JsonArray(
                FinancialCore.CreateJournalLine("212103",company,0,100,iqama:rider,targetChart:true,payChart:true),
                FinancialCore.CreateJournalLine("411104",company,100,0,iqama:rider,targetChart:true,payChart:true)),"synthetic-opening",originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
            return true;
        });
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10"}));
        var mark=new JsonObject{["sponsorId"]=company,["period"]="2026-10",["markSent"]=true,["bankAccountCode"]="111201",["previewToken"]=preview.S("previewToken"),["rows"]=preview["rows"]!.DeepClone()};
        // Permission alone is not enough — no recorded two-person dual decision yet.
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,mark))).Status);
        string reference="bank-payout:"+company+":2026-10:2026-10";
        // TRACE H2 / M-0677: pay_approvals bypass must NOT self-approve DualBankFile (explicit dual scope required).
        f.Store.Write((c,tx)=>{
            var maker=UserAccess.Find(c,"synthetic-pay-maker",tx)??Store.Get(c,"synthetic-pay-maker","user",tx)??new JsonObject{["id"]="u-bank-maker",["username"]="synthetic-pay-maker",["role"]="Admin"};
            maker["customPermissions"]=new JsonArray(ApprovalsArea.DualBankFile,"approvals.decide");
            Store.Save(c,"user",maker,tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new(){["username"]="synthetic-pay-maker",["from"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd"),["to"]=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).AddDays(7).ToString("yyyy-MM-dd"),["reason"]="pay approvals scope",["scope"]=AccessPolicy.ScopePayApprovals});
            var req=FinancialCore.CreateMutualApproval(c,tx,"bank_sent","اعتماد إرسال ملف بنك","مرجع اختبار", "OPS","FIN",1,reference,new(){["amountSar"]=100}, "synthetic-pay-maker");
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>ApprovalsArea.Decide(c,tx,PayFixture.Maker,req.S("id"),new(){["decision"]="approve",["note"]="self via pay_approvals bypass"})).Status);
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,mark))).Status);
        // Two different people, no bypass — mark-sent may post only when the approved file hash still matches.
        var checker=UserAccess.Principal(new JsonObject{["username"]="synthetic-pay-checker",["role"]="Admin",["department"]="FIN",["rankLevel"]=5});
        string fileHash=BankPayrollFiles.FileContentHash(preview["rows"]!.AsArray().OfType<JsonObject>(),preview["rows"]!.AsArray().OfType<JsonObject>().Sum(r=>r.N("amount")));
        f.Store.Write((c,tx)=>{
            var req=FinancialCore.CreateMutualApproval(c,tx,"bank_sent","اعتماد إرسال ملف بنك","مرجع اختبار ٢","OPS","FIN",1,reference,new(){["amountSar"]=100,["fileContentHash"]=fileHash,["total"]=100},"synthetic-pay-maker");
            ApprovalsArea.Decide(c,tx,checker,req.S("id"),new(){["decision"]="approve",["note"]="independent checker"});
            return true;
        });
        // Amount edit after approval voids the dual decision (hash mismatch).
        var edited=mark.DeepClone().AsObject();
        var editedRows=edited["rows"]!.AsArray();
        editedRows[0]!.AsObject()["amount"]=editedRows[0]!.AsObject().N("amount")+1;
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,edited))).Status);
        f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,mark));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"journal").Any(j=>j.S("transactionType")=="bank_payroll_payout")));
    }
}
