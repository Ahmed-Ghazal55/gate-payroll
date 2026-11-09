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
        Assert.IsTrue(preview["rows"]!.AsArray().OfType<JsonObject>().All(r=>r.S("notes").Contains(BankPayrollFiles.DoNotSendMark,StringComparison.Ordinal)));
        var file=f.Store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,PayFixture.Maker,new(){["sponsorId"]=company,["period"]="2026-10",["comparisonOnly"]=true,["previewToken"]=preview.S("previewToken"),["rows"]=preview["rows"]!.DeepClone()}));
        Assert.IsTrue(file.Filename.StartsWith("DO-NOT-SEND_",StringComparison.Ordinal));
        string csv=Encoding.UTF8.GetString(file.Bytes);Assert.Contains(BankPayrollFiles.DoNotSendMark,csv);
    }
}
