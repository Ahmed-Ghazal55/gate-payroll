using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class JournalPostingTests
{
    static (System.Data.Common.DbConnection c,string company,string period,string debit,string credit) Open(bool sqlServer=false)
    {
        var c=MoneySchemaFixture.Open(sqlServer);
        SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
        string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=1 ORDER BY code LIMIT 1")!;
        string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=2 ORDER BY code LIMIT 1")!;
        return (c,company,period,debit,credit);
    }

    static JournalPosting.Draft Draft(string company,string period,string debit,string credit,Guid? source=null,string purpose="manual",byte runMode=0,bool blocked=false,decimal amount=10m)=>
        new(Guid.Parse(company),Guid.Parse(period),new DateOnly(2026,11,1),new DateOnly(2026,11,15),
            "test_source",source??Guid.NewGuid(),purpose,runMode,blocked,"synthetic",
            [
                new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",amount,0m,null),
                new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,amount,null)
            ]);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PostsBalancedJournalAndUpsertsPeriodBalance(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var result=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
            Assert.IsFalse(result.AlreadyPosted);
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal WHERE status=2")!));
            Assert.AreEqual(2L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal_line")!));
            Assert.AreEqual(2L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_period_balance")!));
            Assert.AreEqual(10m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IdempotentRetryReturnsExistingWithoutDoubleBalance(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var source=Guid.NewGuid();
            var first=JournalPosting.Post(c,null,Draft(company,period,debit,credit,source));
            var second=JournalPosting.Post(c,null,Draft(company,period,debit,credit,source));
            Assert.IsTrue(second.AlreadyPosted);
            Assert.AreEqual(first.JournalId,second.JournalId);
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
            Assert.AreEqual(10m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));
        }
    }

    [TestMethod]
    public void UnbalancedAndClosedPeriodAndNonLeafAreRejected()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var bad=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,9m,null)
                ]
            };
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,bad)).Status);

            MoneySchemaFixture.Exec(c,null,"UPDATE fin_accounting_period SET state=2 WHERE id=@id",("@id",period));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,Draft(company,period,debit,credit))).Status);
            MoneySchemaFixture.Exec(c,null,"UPDATE fin_accounting_period SET state=1 WHERE id=@id",("@id",period));

            string parent=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=0 ORDER BY code LIMIT 1")!;
            var nonLeaf=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(parent),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,10m,null)
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,nonLeaf)).Status);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CorrectionIsReversalOnlyAndPayoutPurposeRespectsBlock(bool sqlServer)
    {
        var (c,company,period,debit,credit)=Open(sqlServer);
        using(c)
        {
            var posted=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
            var reverse=JournalPosting.Reverse(c,null,posted.JournalId,"synthetic",Guid.NewGuid(),"عكس قيد اختبار");
            Assert.IsFalse(reverse.AlreadyPosted);
            Assert.AreEqual(3L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT status FROM fin_journal WHERE id=@id",("@id",SchemaSql.GuidText(posted.JournalId)))!));
            Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit-credit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));

            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,
                Draft(company,period,debit,credit,purpose:"bank_claim",blocked:true))).Status);
        }
    }

    [TestMethod]
    public void AmountsRoundAwayFromZeroToTwoDecimals()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var draft=Draft(company,period,debit,credit,amount:10.005m);
            JournalPosting.Post(c,null,draft);
            Assert.AreEqual(10.01m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_journal_line WHERE debit>0")!));
        }
    }

    [TestMethod]
    public void LateLineFailureRollsBackOwnedTransaction()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            string foreignCc=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO org_cost_centre(id,code,name_ar,kind,company_id,status,created_at) VALUES(@id,'cc-b','مركز ب','city',@c,1,datetime('now'))",
                ("@id",foreignCc),("@c",MoneySchemaFixture.Company(c)));
            var draft=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"none",10m,0m,null),
                    new(Guid.Parse(credit),Guid.Parse(foreignCc),"none",0m,10m,null)
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,draft)).Status);
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
            Assert.AreEqual(0L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_period_balance")!));
        }
    }

    [TestMethod]
    public void RejectsCostCentreOwnedByAnotherCompany()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            string other=MoneySchemaFixture.Company(c),cc=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO org_cost_centre(id,code,name_ar,kind,company_id,status,created_at) VALUES(@id,'cc-other','مركز آخر','city',@c,1,datetime('now'))",
                ("@id",cc),("@c",other));
            var draft=Draft(company,period,debit,credit) with
            {
                Lines=[
                    new(Guid.Parse(debit),Guid.Parse(cc),"none",10m,0m,null),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"none",0m,10m,null)
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,draft)).Status);
        }
    }

    [TestMethod]
    public void RiderLineRequiresBothRiderAndPlatformAccount()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var riderOnly=Draft(company,period,debit,credit,purpose:"salary") with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"ninja",10m,0m,null,null,Guid.NewGuid()),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"ninja",0m,10m,null,null,Guid.NewGuid())
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,riderOnly)).Status);
            // Salary/agent with both ids empty must also reject.
            var bothEmpty=Draft(company,period,debit,credit,purpose:"salary");
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,bothEmpty)).Status);
            var agentEmpty=Draft(company,period,debit,credit,purpose:"agent_commission");
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,agentEmpty)).Status);
        }
    }

    [TestMethod]
    public void FingerprintIncludesRunModeAndPayoutBlocked()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var source=Guid.NewGuid();
            JournalPosting.Post(c,null,Draft(company,period,debit,credit,source,runMode:0,blocked:false));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,
                Draft(company,period,debit,credit,source,runMode:0,blocked:true))).Status);
            Assert.AreEqual(1L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
        }
    }

    [TestMethod]
    public void SqlitePostingTakesImmediateWriteLockAgainstConcurrentPeriodClose()
    {
        string path=Path.Combine(Path.GetTempPath(),"jp-lock-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path);
            c.Open();
            MoneySchemaFixture.Exec(c,null,"PRAGMA foreign_keys=ON;");
            SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaPayPayable.Ensure(c);
            SchemaOrgCostCentre.Ensure(c);SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
            string company=MoneySchemaFixture.Company(c),period=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",period),("@c",company));
            string debit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=1 ORDER BY code LIMIT 1")!;
            string credit=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_account WHERE is_postable=1 AND nature=2 ORDER BY code LIMIT 1")!;
            using var c2=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+path);
            c2.Open();
            using var postTx=c.BeginTransaction(deferred:false);
            var closeAttempt=System.Threading.Tasks.Task.Run(()=>
            {
                try
                {
                    using var closeTx=c2.BeginTransaction(deferred:false);
                    using var cmd=c2.CreateCommand();
                    cmd.Transaction=closeTx;
                    cmd.CommandText="UPDATE fin_accounting_period SET state=2 WHERE id=$id";
                    cmd.Parameters.AddWithValue("$id",period);
                    cmd.CommandTimeout=1;
                    cmd.ExecuteNonQuery();
                    closeTx.Commit();
                    return "closed";
                }
                catch(Exception ex){return ex.GetType().Name;}
            });
            System.Threading.Thread.Sleep(200);
            JournalPosting.Post(c,postTx,Draft(company,period,debit,credit));
            Assert.IsFalse(closeAttempt.IsCompleted,"second connection must still be waiting on the IMMEDIATE write lock");
            Assert.AreEqual(1,Convert.ToInt32(MoneySchemaFixture.Scalar(c,postTx,"SELECT state FROM fin_accounting_period WHERE id=@id",("@id",period))!));
            postTx.Commit();
            closeAttempt.GetAwaiter().GetResult();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(File.Exists(path))File.Delete(path);
        }
    }

    [TestMethod]
    public void IdempotentMatchRejectsChangedRiderAttribution()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var source=Guid.NewGuid();
            var plat=Guid.NewGuid();var rider=Guid.NewGuid();
            var first=Draft(company,period,debit,credit,source,purpose:"salary") with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"ninja",10m,0m,null,plat,rider),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"ninja",0m,10m,null,plat,rider)
                ]
            };
            JournalPosting.Post(c,null,first);
            var changed=first with
            {
                Lines=[
                    new(Guid.Parse(debit),SchemaOrgCostCentre.NoneId,"ninja",10m,0m,null,plat,Guid.NewGuid()),
                    new(Guid.Parse(credit),SchemaOrgCostCentre.NoneId,"ninja",0m,10m,null,plat,Guid.NewGuid())
                ]
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>JournalPosting.Post(c,null,changed)).Status);
        }
    }

    [TestMethod]
    public void ReverseRetryReturnsExistingAndCanPostIntoOpenPeriod()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            var posted=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
            string nov=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-12-01',1,datetime('now'))",("@id",nov),("@c",company));
            MoneySchemaFixture.Exec(c,null,"UPDATE fin_accounting_period SET state=2 WHERE id=@id",("@id",period));
            var source=Guid.NewGuid();
            var first=JournalPosting.Reverse(c,null,posted.JournalId,"synthetic",source,"عكس",Guid.Parse(nov),new DateOnly(2026,12,5));
            var again=JournalPosting.Reverse(c,null,posted.JournalId,"synthetic",source,"عكس",Guid.Parse(nov),new DateOnly(2026,12,5));
            Assert.IsTrue(again.AlreadyPosted);
            Assert.AreEqual(first.JournalId,again.JournalId);
            Assert.AreEqual(3L,Convert.ToInt64(MoneySchemaFixture.Scalar(c,null,"SELECT status FROM fin_journal WHERE id=@id",("@id",SchemaSql.GuidText(posted.JournalId)))!));
        }
    }

    [TestMethod]
    public void SqliteBalanceUpdateRoundsRepeatedFractionalPosts()
    {
        var (c,company,period,debit,credit)=Open();
        using(c)
        {
            for(int i=0;i<100;i++)
                JournalPosting.Post(c,null,Draft(company,period,debit,credit,amount:0.01m));
            Assert.AreEqual(1.00m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT debit FROM fin_period_balance WHERE account_id=@a",("@a",debit))!));
        }
    }

    [TestMethod]
    public void DateReadersStayCultureInvariant()
    {
        var previous=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-GB");
            var (c,company,period,debit,credit)=Open();
            using(c)
            {
                var posted=JournalPosting.Post(c,null,Draft(company,period,debit,credit));
                var reverse=JournalPosting.Reverse(c,null,posted.JournalId,"synthetic",Guid.NewGuid());
                Assert.IsFalse(reverse.AlreadyPosted);
                Assert.AreEqual("2026-11-01",MoneySchemaFixture.Scalar(c,null,"SELECT period_month FROM fin_journal WHERE id=@id",("@id",SchemaSql.GuidText(reverse.JournalId)))!.ToString()![..10]);
            }
        }
        finally{CultureInfo.CurrentCulture=previous;}
    }
}
