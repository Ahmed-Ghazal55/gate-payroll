using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>U-W2-20a: import template path + reduce preview/commit + reschedule/ageing (X28 start).</summary>
[TestClass]
public sealed class DebtImportReduceTests
{
    [TestMethod,DataRow(false),DataRow(true)]
    public void ImportReducePreviewAndCommitMovesBalancePostsJournalAndOneAudit(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',100,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));

            byte[] file=Encoding.UTF8.GetBytes("iqama,debt_id,amount,reason\n2000000001,"+debt+",40,تصحيح رصيد افتتاحي");
            string hash=Convert.ToHexString(SHA256.HashData(file));
            var preview=DebtLedger.PreviewReduce(c,null,companyG,DebtLedgerFixture.Admin,file,"reduce.csv",new DateOnly(2026,11,1));
            Assert.AreEqual(1,preview.N("validCount"));
            Assert.AreEqual(40m,preview.N("totalReduce"));
            Assert.AreEqual(hash,preview.S("fileHash"),ignoreCase:true);
            Guid debit=DebtLedgerFixture.Acc(c,"112501"),credit=DebtLedgerFixture.Acc(c,"331301");

            using var tx=c.BeginTransaction();
            var commit=DebtLedger.CommitReduce(c,tx,companyG,periodG,new DateOnly(2026,11,1),DebtLedgerFixture.Admin,DebtLedgerFixture.Checker,file,"reduce.csv",preview.S("fingerprint"),debit,credit);
            tx.Commit();
            Assert.AreEqual(60m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_debt_movement WHERE source_type='import' AND amount=-40"));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_journal WHERE purpose='debt_reduce' AND status=2"));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_import_batch WHERE import_kind='reduce' AND status=3"));
            Assert.AreEqual(1,commit.N("rowCount"));
            Assert.AreEqual(hash,commit.S("fileHash"),ignoreCase:true);
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_import_batch WHERE import_kind='reduce' AND file_hash IS NOT NULL"));
        }
    }

    [TestMethod,DataRow(false),DataRow(true)]
    public void RescheduleAgeingAndFlagsSurfaceOnLedgerDebt(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-09-01','2026-09-01',90,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            using var tx=c.BeginTransaction();
            DebtLedger.Reschedule(c,tx,Guid.Parse(debt),DebtLedgerFixture.Admin,30m);
            tx.Commit();
            Assert.AreEqual(30m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT instalment FROM debt_debt WHERE id=@id",("@id",debt))));
            var ageing=DebtLedger.Ageing(c,null,Guid.Parse(company),DebtLedgerFixture.Admin);
            Assert.IsTrue(ageing.N("totalOpen")>=90m);
            Assert.IsTrue(ageing["buckets"]!.AsArray().Count>=1);
            var print=DebtLedger.PrintRegister(c,null,Guid.Parse(company),DebtLedgerFixture.Admin);
            Assert.IsTrue(print["items"]!.AsArray().Count>=1);
        }
    }
}
