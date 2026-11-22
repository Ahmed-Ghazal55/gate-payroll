using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Codex RWP7a1 remaining rows — exact amounts (TDD).</summary>
[TestClass]
public sealed class Rwp7a1FixTests
{
    static void SetAccount(PaymentsEngineTests.Fixture f,string key,string code)=>
        f.Store.Write((c,tx)=>AdminSettings.Put(c,tx,f.Maker,new(){["key"]=key,["value"]=new JsonObject{["accountCode"]=code}}));

    static string SeedCostCentre(PaymentsEngineTests.Fixture f,string code,string nameAr)=>
        f.Store.Write((c,tx)=>{
            string id=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"org","cost_centre")}(id,code,name_ar,kind,company_id,status,created_at)
                VALUES(@id,@code,@name,'office',@c,1,{SchemaSql.NowLiteral(c)})
                """,("@id",id),("@code",code),("@name",nameAr),("@c",f.Company));
            return id;
        });

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row9_CreateRejectsCallerSuppliedFxGainReclass_AndDoesNotInjectBankCredit(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        SetAccount(f,RecurringPayees.FxAccountKey,"542101");
        var forged=f.Input("expense",100m);
        forged["fxGainReclass"]=new JsonObject
        {
            ["expenseAccountCode"]="526101",["fxAccountCode"]="542101",["amount"]=1000m,
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")
        };
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Create(forged));
        Assert.AreEqual(400,err.Status);

        // Trusted one-off gain path still posts bank 960 and FX credit 40 only (not 1000).
        var gain=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="ربح موثوق",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=-40m,["paymentMethod"]="bank_transfer",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7a1-trusted-gain-960"
        }));
        Assert.AreEqual(960m,gain.N("amount"));
        f.Dual(gain);f.Pay(gain.S("id"));
        decimal bank=Convert.ToDecimal(f.Scalar("SELECT SUM(l.credit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='111101' AND j.source_type='payment_document'"));
        decimal fxCredit=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.credit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document'"));
        decimal expense=Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'"));
        Assert.AreEqual(960m,bank);
        Assert.AreEqual(40m,fxCredit);
        Assert.AreEqual(1000m,expense);
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM pay_document WHERE amount=100 AND metadata LIKE '%fxGainReclass%'"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5_CancelAndSplit960AllocatesFxGain40AcrossTwo480Payments(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        SetAccount(f,RecurringPayees.FxAccountKey,"542101");
        var full=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="تقسيم ربح",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=-40m,["paymentMethod"]="bank_transfer",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7a1-split-gain-960"
        }));
        Assert.AreEqual(960m,full.N("amount"));
        string docId=full.S("documentId");
        f.Store.Write((c,tx)=>PaymentsEngine.CancelPayment(c,tx,f.Maker,full.S("id")));
        var first=f.Reserve(docId,480m);
        var second=f.Reserve(docId,480m);
        Assert.AreEqual(480m,first.N("amount"));
        Assert.AreEqual(480m,second.N("amount"));
        f.Dual(first);f.Pay(first.S("id"));
        f.Dual(second);f.Pay(second.S("id"));
        decimal expense=Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'"));
        decimal fxCredit=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.credit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document'"));
        decimal bank=Convert.ToDecimal(f.Scalar("SELECT SUM(l.credit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='111101' AND j.source_type='payment_document'"));
        Assert.AreEqual(1000m,expense,"expense must stay 1000, not 1040 from double gain");
        Assert.AreEqual(40m,fxCredit,"FX gain allocated once across both 480 vouchers");
        Assert.AreEqual(960m,bank);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row7_ListPayeesReturnsRealCostCentreOptions(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string centreId=SeedCostCentre(f,"OFF-RWP7A1","مركز تشغيل اختبار");
        var list=f.Store.Read(c=>RecurringPayees.ListPayees(c,f.Maker,f.Company));
        var centres=list["costCentres"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.IsTrue(centres.Count>=2,"must include none sentinel plus seeded centre");
        Assert.IsTrue(centres.Any(c=>c.S("id")==SchemaOrgCostCentre.NoneId.ToString("D")));
        Assert.IsTrue(centres.Any(c=>c.S("id")==centreId&&c.S("nameAr")=="مركز تشغيل اختبار"));
    }
}
