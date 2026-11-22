using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class RecurringPayeesTests
{
    static void SetAccount(PaymentsEngineTests.Fixture f,string key,string code)=>
        f.Store.Write((c,tx)=>AdminSettings.Put(c,tx,f.Maker,new(){["key"]=key,["value"]=new JsonObject{["accountCode"]=code}}));

    static JsonObject Payee(PaymentsEngineTests.Fixture f,string name,string type,decimal amount,string currency,string method="bank_transfer")=>
        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]=name,["payeeType"]=type,["amount"]=amount,["currencyCode"]=currency,
            ["paymentMethod"]=method,["bankDetails"]="IBAN-SYN-NOT-LOGGED",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),
            ["activeFrom"]="2026-01-01",["activeTo"]="2026-12-31",["notes"]="synthetic"
        }));

    static JsonObject ProposeAndApprove(PaymentsEngineTests.Fixture f,string period="2026-11")
    {
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]=period}));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        return f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
    }

    static void PayRunFully(PaymentsEngineTests.Fixture f,string runId)
    {
        var reserved=f.Store.Write((c,tx)=>RecurringPayees.ExecuteRun(c,tx,f.Maker,runId,new(){["paidOn"]="2026-11-20",["bankAccountCode"]="111101"}));
        foreach(var v in reserved["vouchers"]!.AsArray().OfType<JsonObject>())
        {
            f.Dual(v);
            f.Pay(v.S("id"));
        }
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void SarAndEgpRunPostsExpenseFeeAndFxWithExactAmounts(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        SetAccount(f,RecurringPayees.FeeAccountKey,"526201");
        SetAccount(f,RecurringPayees.FxAccountKey,"542101");
        Payee(f,"مستفيد تجريبي","trial_outside_sponsorship",3000m,"SAR");
        Payee(f,"مكتب مصر","egypt_office",20000m,"EGP");

        var draft=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        Assert.AreEqual(2,draft["lines"]!.AsArray().Count);
        var egp=draft["lines"]!.AsArray().OfType<JsonObject>().First(l=>l.S("currencyCode")=="EGP");
        f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,draft.S("id"),new()
        {
            ["lines"]=new JsonArray(new JsonObject
            {
                ["id"]=egp.S("id"),["amount"]=20000m,["currencyCode"]="EGP",["exchangeRate"]=0.075m,
                ["transferFee"]=15m,["fxDifference"]=0m,["skipped"]=false
            })
        }));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,draft.S("id")));
        f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,draft.S("id")));
        PayRunFully(f,draft.S("id"));

        decimal expense=Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'"));
        decimal fee=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.debit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526201' AND j.source_type='payment_document'"));
        decimal bank=Convert.ToDecimal(f.Scalar("SELECT SUM(l.credit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='111101' AND j.source_type='payment_document'"));
        // SAR 3000 + EGP 20000*0.075=1500 expense; fee 15; bank credit 3000+1500+15=4515
        Assert.AreEqual(4500m,expense);
        Assert.AreEqual(15m,fee);
        Assert.AreEqual(4515m,bank);
        Assert.AreEqual(0m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.debit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document'")));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void EmptyExpenseAccountAllowsApproveButBlocksExecuteWithArabic409(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        Payee(f,"بدون حساب","other",500m,"SAR");
        var run=ProposeAndApprove(f);
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.ExecuteRun(c,tx,f.Maker,run.S("id"),new(){["paidOn"]="2026-11-20",["bankAccountCode"]="111101"})));
        Assert.AreEqual(409,err.Status);
        Assert.AreEqual("حدد حساب المصروف في الإعدادات أولاً",err.Message);
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM fin_journal WHERE source_type='payment_document'"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void MakerCannotApproveOwnRunAndBankDetailsStayOutOfAudit(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        var payee=Payee(f,"سري","other",100m,"SAR");
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Maker,run.S("id")))).Status);
        f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
        string blob=f.Store.Read(c=>string.Join("|",Store.List(c,"audit").Select(a=>a.ToJsonString())));
        Assert.IsFalse(blob.Contains("IBAN-SYN-NOT-LOGGED",StringComparison.Ordinal));
        var history=f.Store.Read(c=>RecurringPayees.PayeeHistory(c,f.Maker,payee.S("id")));
        Assert.IsTrue(history["items"]!.AsArray().Count>=1);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void OneOffWithoutProfileAndSkipLineWork(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        Payee(f,"سيُتخطى","other",900m,"SAR");
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        var line=run["lines"]!.AsArray().OfType<JsonObject>().First();
        f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,run.S("id"),new()
        {
            ["lines"]=new JsonArray(
                new JsonObject{["id"]=line.S("id"),["skipped"]=true},
                new JsonObject{["nameAr"]="دفعة لمرة",["amount"]=250m,["currencyCode"]="SAR",["exchangeRate"]=1m,["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="bank_transfer",["bankDetails"]="ONE-OFF",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["skipped"]=false})
        }));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
        PayRunFully(f,run.S("id"));
        Assert.AreEqual(250m,Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'")));
        var oneOff=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="مرة واحدة",["amount"]=80m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="cash",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",["period"]="2026-11"
        }));
        f.Dual(oneOff);f.Pay(oneOff.S("id"));
        Assert.AreEqual(330m,Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'")));
    }
}
