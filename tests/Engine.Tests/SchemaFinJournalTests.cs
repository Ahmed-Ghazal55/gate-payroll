using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.RegularExpressions;

[TestClass]
public sealed class SchemaFinJournalTests
{
    static SqliteConnection Open()
    {
        var c=new SqliteConnection("Data Source=:memory:");c.Open();
        using(var fk=c.CreateCommand()){fk.CommandText="PRAGMA foreign_keys=ON;";fk.ExecuteNonQuery();}
        SchemaRef.Ensure(c);SchemaHrCompany.Ensure(c);SchemaOrgCostCentre.Ensure(c);
        SchemaFinAccount.Ensure(c);SchemaFinPeriod.Ensure(c);SchemaFinJournal.Ensure(c);
        return c;
    }

    static (string Company,string Period,string Journal,string PostingAccount) SeedHeader(SqliteConnection c)
    {
        string company=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO hr_company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'J1','UJ1','n',1,datetime('now'))",("@id",company));
        string period=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,datetime('now'))",
            ("@id",period),("@cid",company));
        string journal=SchemaSql.GuidText(SchemaSql.NewId());
        string source=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,run_mode,payout_blocked,currency_code,created_at)
            VALUES(@id,'V-1',@pid,'2026-11-01','2026-11-15','manual',@sid,@cid,'manual',1,0,0,'SAR',datetime('now'))
            """,("@id",journal),("@pid",period),("@sid",source),("@cid",company));
        string posting=(string)SchemaSql.Scalar(c,null,"SELECT id FROM fin_account WHERE is_posting=1 AND is_active=1 ORDER BY code LIMIT 1")!;
        return (company,period,journal,posting);
    }

    [TestMethod]
    public void EnsureTwiceIsIdempotent()
    {
        using var c=Open();
        SchemaFinJournal.Ensure(c);
        Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","journal"));
        Assert.IsTrue(SchemaSql.TableExists(c,null,"fin","journal_line"));
        Assert.AreEqual(0L,Convert.ToInt64(SchemaSql.Scalar(c,null,"SELECT COUNT(1) FROM fin_journal")!));
    }

    [TestMethod]
    public void LineChecksRejectBothSidesNegativeAndZero()
    {
        using var c=Open();
        var h=SeedHeader(c);
        string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,10,5,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",h.Journal),("@aid",h.PostingAccount),("@cid",h.Company),("@cc",cc)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,-1,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",h.Journal),("@aid",h.PostingAccount),("@cid",h.Company),("@cc",cc)));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,0,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",h.Journal),("@aid",h.PostingAccount),("@cid",h.Company),("@cc",cc)));
        SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,10,0,datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",h.Journal),("@aid",h.PostingAccount),("@cid",h.Company),("@cc",cc));
    }

    [TestMethod]
    public void DuplicateSourceKeyIsRejected()
    {
        using var c=Open();
        var h=SeedHeader(c);
        string source=(string)SchemaSql.Scalar(c,null,"SELECT source_id FROM fin_journal WHERE id=@id",("@id",h.Journal))!;
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,currency_code,created_at)
            VALUES(@id,'V-2',@pid,'2026-11-01','2026-11-15','manual',@sid,@cid,'manual',1,'SAR',datetime('now'))
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",h.Period),("@sid",source),("@cid",h.Company)));
    }

    [TestMethod]
    public void ReversalLinkAndStoreStartup()
    {
        using var c=Open();
        var h=SeedHeader(c);
        string rev=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,null,"""
            INSERT INTO fin_journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,currency_code,reverses_journal_id,created_at)
            VALUES(@id,'V-R',@pid,'2026-11-01','2026-11-16','manual',@sid,@cid,'reversal',1,'SAR',@orig,datetime('now'))
            """,("@id",rev),("@pid",h.Period),("@sid",SchemaSql.GuidText(SchemaSql.NewId())),("@cid",h.Company),("@orig",h.Journal));
        Assert.AreEqual(h.Journal.ToLowerInvariant(),((string)SchemaSql.Scalar(c,null,"SELECT lower(reverses_journal_id) FROM fin_journal WHERE id=@id",("@id",rev))!).ToLowerInvariant());

        string dir=Path.Combine(Path.GetTempPath(),"gate-fin-j-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(cx=>{Assert.IsTrue(SchemaSql.TableExists(cx,null,"fin","journal_line"));return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }

    [TestMethod]
    public void SqlServerFreshRepeatChecksAndLeafOnlyPosting()
    {
        if(!TrySql(out var builder,out var admin))return;
        string db="GateFinJ_"+Guid.NewGuid().ToString("N");
        string dir=Path.Combine(Path.GetTempPath(),db);Directory.CreateDirectory(dir);
        using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        builder.InitialCatalog=db;
        try
        {
            var first=new Store(dir,builder.ToString());
            var second=new Store(dir,builder.ToString());
            Assert.IsTrue(first.DataPreservationVerified);Assert.IsTrue(second.DataPreservationVerified);
            first.Read(c=>{
                string company=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO hr.company(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'J1','UJ1',N'n',1,SYSUTCDATETIME())",("@id",company));
                string period=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"INSERT INTO fin.accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@cid,'2026-11-01',1,SYSUTCDATETIME())",("@id",period),("@cid",company));
                string journal=SchemaSql.GuidText(SchemaSql.NewId());
                string source=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,currency_code,created_at)
                    VALUES(@id,'V-1',@pid,'2026-11-01','2026-11-15','manual',@sid,@cid,'manual',1,'SAR',SYSUTCDATETIME())
                    """,("@id",journal),("@pid",period),("@sid",source),("@cid",company));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.journal(id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,currency_code,created_at)
                    VALUES(@id,'V-2',@pid,'2026-11-01','2026-11-15','manual',@sid,@cid,'manual',1,'SAR',SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@pid",period),("@sid",source),("@cid",company)));
                string posting=(string)SchemaSql.Scalar(c,null,"SELECT TOP 1 CONVERT(varchar(36),id) FROM fin.account WHERE is_posting=1 AND is_active=1 ORDER BY code")!;
                string nonLeaf=(string)SchemaSql.Scalar(c,null,"SELECT TOP 1 CONVERT(varchar(36),id) FROM fin.account WHERE is_posting=0 ORDER BY code")!;
                string cc=SchemaSql.GuidText(SchemaOrgCostCentre.NoneId);
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
                    VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,5,5,SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",journal),("@aid",posting),("@cid",company),("@cc",cc)));
                Assert.ThrowsExactly<SqlException>(()=>SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
                    VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,10,0,SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",journal),("@aid",nonLeaf),("@cid",company),("@cc",cc)));
                SchemaSql.Exec(c,null,"""
                    INSERT INTO fin.journal_line(id,period_month,journal_id,line_no,account_id,company_id,cost_centre_id,debit,credit,created_at)
                    VALUES(@id,'2026-11-01',@jid,1,@aid,@cid,@cc,10,0,SYSUTCDATETIME())
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@jid",journal),("@aid",posting),("@cid",company),("@cc",cc));
                return true;
            });
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using(var q=admin.CreateCommand()){q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";q.ExecuteNonQuery();}
            if(Directory.Exists(dir))Directory.Delete(dir,true);admin.Dispose();
        }
    }

    static bool TrySql(out SqlConnectionStringBuilder builder,out SqlConnection admin)
    {
        builder=new SqlConnectionStringBuilder();admin=null!;
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");
        if(string.IsNullOrWhiteSpace(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return false;}
        builder=new SqlConnectionStringBuilder(configured);
        Assert.IsTrue(Regex.IsMatch(builder.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase));
        builder.InitialCatalog="master";admin=new SqlConnection(builder.ToString());admin.Open();return true;
    }
}
