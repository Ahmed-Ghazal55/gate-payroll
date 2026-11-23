using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Codex RWP7 findings on feat/w2-payments recurring payees — exact amounts.</summary>
[TestClass]
public sealed class Rwp7FixTests
{
    static void SetAccount(PaymentsEngineTests.Fixture f,string key,string code)=>
        f.Store.Write((c,tx)=>AdminSettings.Put(c,tx,f.Maker,new(){["key"]=key,["value"]=new JsonObject{["accountCode"]=code}}));

    static JsonObject Payee(PaymentsEngineTests.Fixture f,string name,decimal amount,string currency="SAR")=>
        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]=name,["payeeType"]="other",["amount"]=amount,["currencyCode"]=currency,
            ["paymentMethod"]="bank_transfer",["bankDetails"]="IBAN-SYN-KEEP",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),
            ["activeFrom"]="2026-01-01",["activeTo"]="2026-12-31"
        }));

    static void OpenPeriod(PaymentsEngineTests.Fixture f,string month)=>
        f.Store.Write((c,tx)=>{
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})",
                ("@id",MoneySchemaFixture.Id()),("@c",f.Company),("@m",month+"-01"));
            return true;
        });

    static void FundBank(PaymentsEngineTests.Fixture f,string month,decimal amount)=>
        f.Store.Write((c,tx)=>{
            string period=MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_accounting_period WHERE company_id=@c AND period_month=@m",("@c",f.Company),("@m",month+"-01"))!.ToString()!;
            Guid Account(string code)=>Guid.Parse(MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_account WHERE code=@code",("@code",code))!.ToString()!);
            JournalPosting.Post(c,tx,new(Guid.Parse(f.Company),Guid.Parse(period),DateOnly.Parse(month+"-01"),DateOnly.Parse(month+"-01"),"synthetic_opening",Guid.NewGuid(),"opening",0,false,"wp-maker",[
                new(Account("111101"),SchemaOrgCostCentre.NoneId,"none",amount,0,null),new(Account("331301"),SchemaOrgCostCentre.NoneId,"none",0,amount,null)]));
            return true;
        });

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row1_OneOffAndExecuteApproveWithAuthenticatedActorNotFabricatedAdmin(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        // Maker creates one-off; document must be approved by authenticated Maker, never by fabricated wp-checker Admin.
        var reserved=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="مرة",["amount"]=80m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="cash",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7-oneoff-maker-80"
        }));
        Assert.AreEqual(80m,reserved.N("amount"));
        var doc=f.Store.Read(c=>PaymentsEngine.Get(c,null,f.Maker,reserved.S("documentId")));
        Assert.AreEqual(3,(int)doc.N("status"));
        string approvedBy=doc.S("approvedBy");
        Assert.IsFalse(string.IsNullOrWhiteSpace(approvedBy));
        string makerAuth=f.Store.Write((c,tx)=>SchemaApprovals.EnsureAuthUserId(c,tx,"wp-maker"));
        string checkerAuth=f.Store.Write((c,tx)=>SchemaApprovals.EnsureAuthUserId(c,tx,"wp-checker"));
        Assert.AreEqual(makerAuth,approvedBy,"document approved by authenticated maker, not fabricated checker");
        Assert.AreNotEqual(checkerAuth,approvedBy);

        Payee(f,"تشغيلي",100m);
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
        var exec=f.Store.Write((c,tx)=>RecurringPayees.ExecuteRun(c,tx,f.Maker,run.S("id"),new(){["paidOn"]="2026-11-20",["bankAccountCode"]="111101"}));
        Assert.AreEqual(1,exec["vouchers"]!.AsArray().Count);
        Assert.AreEqual(100m,exec["vouchers"]!.AsArray().OfType<JsonObject>().First().N("amount"));
        var runDoc=f.Store.Read(c=>PaymentsEngine.Get(c,null,f.Maker,exec["vouchers"]!.AsArray().OfType<JsonObject>().First().S("documentId")));
        Assert.AreEqual(makerAuth,runDoc.S("approvedBy"),"execute approves with authenticated actor, not fabricated run-approver Admin");
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row3_NovemberRunPaidInDecemberUsesDecemberDocumentPeriod(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        OpenPeriod(f,"2026-12");
        FundBank(f,"2026-12",2000m);
        Payee(f,"نوفمبر",500m);
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        Assert.IsTrue(run.S("periodMonth").StartsWith("2026-11",StringComparison.Ordinal));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
        var reserved=f.Store.Write((c,tx)=>RecurringPayees.ExecuteRun(c,tx,f.Maker,run.S("id"),new(){["paidOn"]="2026-12-05",["bankAccountCode"]="111101"}));
        var voucher=reserved["vouchers"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(500m,voucher.N("amount"));
        var doc=f.Store.Read(c=>PaymentsEngine.Get(c,null,f.Maker,voucher.S("documentId")));
        Assert.IsTrue(doc.S("periodMonth").StartsWith("2026-12",StringComparison.Ordinal),"payment document accounting month follows paidOn");
        Assert.AreEqual("2026-12-05",doc.S("occurredOn")[..10]);
        Assert.IsTrue(run.S("periodMonth").StartsWith("2026-11",StringComparison.Ordinal));
        f.Dual(voucher);f.Pay(voucher.S("id"));
        Assert.AreEqual(500m,Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'")));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row4_UpdatePayeeOmitsBankDetailsUnlessCleared(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var payee=Payee(f,"بنك",200m);
        f.Store.Write((c,tx)=>RecurringPayees.UpdatePayee(c,tx,f.Maker,payee.S("id"),new()
        {
            ["nameAr"]="بنك محدّث",["payeeType"]="other",["amount"]=200m,["currencyCode"]="SAR",
            ["paymentMethod"]="bank_transfer",["activeFrom"]="2026-01-01",["notes"]="no bank field"
        }));
        string kept=f.Store.Read(c=>SchemaSql.Scalar(c,null,$"SELECT bank_details FROM {SchemaSql.Q(c,"pay","recurring_payee")} WHERE id=@id",("@id",payee.S("id")))!.ToString()!);
        Assert.AreEqual("IBAN-SYN-KEEP",kept);
        f.Store.Write((c,tx)=>RecurringPayees.UpdatePayee(c,tx,f.Maker,payee.S("id"),new()
        {
            ["nameAr"]="بنك محدّث",["payeeType"]="other",["amount"]=200m,["currencyCode"]="SAR",
            ["paymentMethod"]="bank_transfer",["activeFrom"]="2026-01-01",["clearBankDetails"]=true
        }));
        string cleared=f.Store.Read(c=>SchemaSql.Scalar(c,null,$"SELECT bank_details FROM {SchemaSql.Q(c,"pay","recurring_payee")} WHERE id=@id",("@id",payee.S("id")))?.ToString()??"");
        Assert.AreEqual("",cleared);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5_SignedFxGainAndLossPostExactBankAndFx(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        SetAccount(f,RecurringPayees.FeeAccountKey,"526201");
        SetAccount(f,RecurringPayees.FxAccountKey,"542101");
        // Loss: sar 1000 + fee 15 + fx 25 → bank 1040; FX debit 25
        var loss=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="خسارة",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=15m,["fxDifference"]=25m,["paymentMethod"]="bank_transfer",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-20",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7-fx-loss-1040"
        }));
        Assert.AreEqual(1040m,loss.N("amount"));
        f.Dual(loss);f.Pay(loss.S("id"));
        // Gain: sar 1000 + fee 0 + fx -40 → bank 960; FX credit 40; expense debit 1000
        var gain=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="ربح",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=-40m,["paymentMethod"]="bank_transfer",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7-fx-gain-960"
        }));
        Assert.AreEqual(960m,gain.N("amount"));
        f.Dual(gain);f.Pay(gain.S("id"));
        decimal expense=Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526101' AND j.source_type='payment_document'"));
        decimal fee=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.debit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='526201' AND j.source_type='payment_document'"));
        decimal fxDebit=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.debit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document'"));
        decimal fxCredit=Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(l.credit),0) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='542101' AND j.source_type='payment_document'"));
        decimal bank=Convert.ToDecimal(f.Scalar("SELECT SUM(l.credit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id JOIN fin_account a ON a.id=l.account_id WHERE a.code='111101' AND j.source_type='payment_document'"));
        Assert.AreEqual(2000m,expense,"1000 loss + 1000 gain");
        Assert.AreEqual(15m,fee);
        Assert.AreEqual(25m,fxDebit);
        Assert.AreEqual(40m,fxCredit);
        Assert.AreEqual(2000m,bank,"1040 + 960");
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row6_SkippedForeignLineAllowsZeroRate(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        Payee(f,"جنيه",20000m,"EGP");
        var draft=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        var egp=draft["lines"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(0m,egp.N("exchangeRate"));
        var updated=f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,draft.S("id"),new()
        {
            ["lines"]=new JsonArray(new JsonObject{["id"]=egp.S("id"),["skipped"]=true})
        }));
        var line=updated["lines"]!.AsArray().OfType<JsonObject>().Single();
        Assert.IsTrue(line.B("skipped"));
        Assert.AreEqual(0m,line.N("exchangeRate"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row8_OneOffRequiresStableKeyAndReplaysSameVoucher(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="بدون مفتاح",["amount"]=50m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="cash",["paidOn"]="2026-11-21",
            ["bankAccountCode"]="111101",["period"]="2026-11"
        }))).Status);
        var body=new JsonObject
        {
            ["companyId"]=f.Company,["nameAr"]="مفتاح ثابت",["amount"]=90m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="cash",["bankDetails"]="",
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]="rwp7-stable-key-90"
        };
        var first=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,body));
        Assert.AreEqual(90m,first.N("amount"));
        var second=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,body));
        Assert.AreEqual(first.S("id"),second.S("id"));
        Assert.AreEqual(90m,second.N("amount"));
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document WHERE idempotency_key='rwp7-stable-key-90'"));
    }
}
