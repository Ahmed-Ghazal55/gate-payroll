using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Codex RWP7a2 remaining rows — exact amounts (TDD).</summary>
[TestClass]
public sealed class Rwp7a2FixTests
{
    static void SetAccount(PaymentsEngineTests.Fixture f,string key,string code)=>
        f.Store.Write((c,tx)=>AdminSettings.Put(c,tx,f.Maker,new(){["key"]=key,["value"]=new JsonObject{["accountCode"]=code}}));

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5_Three320PaymentsAllocateFxGain40ViaCumulativeShares(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        SetAccount(f,RecurringPayees.FxAccountKey,"542101");
        var full=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="ثلاث حصص",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=-40m,["paymentMethod"]="bank_transfer",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7a2-three-320-gain-40"
        }));
        Assert.AreEqual(960m,full.N("amount"));
        string docId=full.S("documentId");
        f.Store.Write((c,tx)=>PaymentsEngine.CancelPayment(c,tx,f.Maker,full.S("id")));
        var a=f.Reserve(docId,320m);
        var b=f.Reserve(docId,320m);
        var cPay=f.Reserve(docId,320m);
        Assert.AreEqual(320m,a.N("amount"));
        Assert.AreEqual(320m,b.N("amount"));
        Assert.AreEqual(320m,cPay.N("amount"));
        decimal FxCredit()=>Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.credit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document' AND j.purpose='fx_gain_reclass'"));
        // Cumulative AwayFromZero: Round(320/960*40)=13.33, Round(640/960*40)=26.67, Round(960/960*40)=40
        // Shares = 13.33, 13.34, 13.33 → sum 40 (not 39.99 from per-payment Round(thisPay/doc*gain))
        f.Dual(a);f.Pay(a.S("id"));
        Assert.AreEqual(13.33m,FxCredit());
        f.Dual(b);f.Pay(b.S("id"));
        Assert.AreEqual(26.67m,FxCredit());
        f.Dual(cPay);f.Pay(cPay.S("id"));
        Assert.AreEqual(40.00m,FxCredit());
        decimal expense=Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'"));
        decimal bank=Convert.ToDecimal(f.Scalar("SELECT SUM(l.credit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='111101' AND j.source_type='payment_document'"));
        Assert.AreEqual(1000m,expense);
        Assert.AreEqual(960m,bank);
    }
}
