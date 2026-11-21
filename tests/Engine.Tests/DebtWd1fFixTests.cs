using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>WD1F: failing-first coverage for grok-RWD1 findings (checker, D11, absorb, period, audit, accounts).</summary>
[TestClass]
public sealed class DebtWd1fFixTests
{
    static ClaimsPrincipal Maker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="debt-maker",["department"]="FIN",["rankLevel"]=3,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","debts.reschedule","approvals.decide")
    });
    static ClaimsPrincipal Checker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="debt-checker",["department"]="FIN",["rankLevel"]=4,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","approvals.decide")
    });

    [TestMethod,DataRow(false)]
    public void CheckerUsesRealUserIdAndValidateApproval_NotTypedAdmin(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',40,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            Guid recv=DebtLedgerFixture.Acc(c,"112501"),exp=DebtLedgerFixture.Acc(c,"526601");
            using var tx=c.BeginTransaction();
            DebtLedger.Waive(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,10m,"إعفاء معتمد","526601",recv,exp);
            string approvedBy=(string)MoneySchemaFixture.Scalar(c,tx,"SELECT CAST(approved_by AS TEXT) FROM debt_waiver LIMIT 1")!;
            Assert.IsFalse(string.IsNullOrWhiteSpace(approvedBy));
            Assert.AreNotEqual(Guid.Empty.ToString("D"),approvedBy,ignoreCase:true);
            // Must be the auth user id for debt-checker, not a fresh random id with no user row.
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM auth_user WHERE id=@id OR username='debt-checker'",("@id",approvedBy)));
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void WaiverAccountComesFromDebtPlatform_NotClient(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            EnsureD11Accounts(c);
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='PlatformTransferredDebt',platform='Keeta' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',30,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            using var tx=c.BeginTransaction();
            var resolved=DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt));
            Assert.AreEqual("526801",resolved.Code);
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Waive(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,5m,"إعفاء","526601",
                    DebtLedger.AccountId(c,tx,"112508"),DebtLedger.AccountId(c,tx,"526601"))).Status);
            DebtLedger.Waive(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,5m,"إعفاء منصة",resolved.Code,
                DebtLedger.AccountId(c,tx,"112508"),resolved.AccountId);
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_waiver WHERE expense_account_code='526801'"));
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void AbsorbUsesAbsorptionSourceAndAbsorbedStatus(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',20,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            using var tx=c.BeginTransaction();
            Guid recv=DebtLedger.AccountId(c,tx,"112501"),exp=DebtLedger.AccountId(c,tx,"526602");
            DebtLedger.Absorb(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,20m,"تحمل سلفة","526602",recv,exp);
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE source_type='absorption'"));
            Assert.AreEqual("absorbed",(string)MoneySchemaFixture.Scalar(c,tx,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void MissingOpenPeriodRefusesWithoutInsert(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            MoneySchemaFixture.Exec(c,null,"DELETE FROM fin_accounting_period");
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.RequireOpenPeriod(c,null,Guid.Parse(company),new DateOnly(2026,12,1))).Status);
            Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_accounting_period"));
        }
    }

    [TestMethod,DataRow(false)]
    public void ReduceWritesOneChangeLogWithBulkCountAndHash(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',100,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            byte[] file=Encoding.UTF8.GetBytes("iqama,debt_id,amount,reason\n2000000001,"+debt+",25,تصحيح رصيد افتتاحي");
            string hash=Convert.ToHexString(SHA256.HashData(file));
            var preview=DebtLedger.PreviewReduce(c,null,companyG,Maker,file,"reduce.csv",new DateOnly(2026,11,1));
            using var tx=c.BeginTransaction();
            DebtLedger.CommitReduce(c,tx,companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,file,"reduce.csv",preview.S("fingerprint"),
                DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"331301"));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM audit_change_log WHERE source='import' AND bulk_count=1 AND file_hash IS NOT NULL"));
            Assert.AreEqual(hash.ToLowerInvariant(),Convert.ToHexString((byte[])MoneySchemaFixture.Scalar(c,tx,"SELECT file_hash FROM audit_change_log WHERE source='import'")!).ToLowerInvariant());
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void CollectIgnoresBodyRunModeAndRequiresCashVault(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',50,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            using var tx=c.BeginTransaction();
            // Body run_mode is ignored; non-cash/bank debit is refused with 400.
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,10m,"تحصيل",
                    DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"212103"),runMode:1,payoutBlocked:false)).Status);
            var flags=DebtLedger.ResolveMoneyFlags(c,tx,companyG,new DateOnly(2026,11,1));
            Assert.AreEqual(0,flags.RunMode);
            DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,10m,"تحصيل",
                DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"111101"),runMode:1,payoutBlocked:true);
            tx.Commit();
        }
    }

    [TestMethod]
    public void LegacyDisbursementAndOpeningVaultCreditClosedWithArabic409()
    {
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>DebtLedger.DemandDisburseAvailable()).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.DemandOpeningVaultCreditClosed()).Status);
        Assert.IsTrue(Assert.ThrowsExactly<ApiError>(()=>Debts.DemandOpeningVaultCreditClosed()).Message.Contains("غير متاح",StringComparison.Ordinal));
    }

    static void EnsureD11Accounts(System.Data.Common.DbConnection c)
    {
        DebtLedger.EnsureD11WaiverAccounts(c,null);
    }
}
