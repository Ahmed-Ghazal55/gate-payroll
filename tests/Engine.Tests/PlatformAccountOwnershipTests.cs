using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PlatformAccountOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PeriodMap_ResolvesOwner_AndOverlapRejected(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        SchemaOpsPlatformAccount.Ensure(c);
        string a=MoneySchemaFixture.Company(c),b=MoneySchemaFixture.Company(c),account=MoneySchemaFixture.Id();
        using var tx=c.BeginTransaction();
        PlatformAccountOwnership.RecordOwnership(c,tx,account,a,"2026-09-01","2026-10-01",PlatformAccountOwnership.SourceManual,"t");
        PlatformAccountOwnership.RecordOwnership(c,tx,account,b,"2026-10-01",null,PlatformAccountOwnership.SourceManual,"t");
        Assert.AreEqual(a,PlatformAccountOwnership.ResolveOwnerCompany(c,tx,account,"2026-09-01"));
        Assert.AreEqual(b,PlatformAccountOwnership.ResolveOwnerCompany(c,tx,account,"2026-10-01"));
        Assert.AreEqual(b,PlatformAccountOwnership.ResolveOwnerCompany(c,tx,account,"2026-10-15"));
        tx.Commit();
        // Overlap reject outside the committed tx — SQL Server aborts the open transaction on THROW.
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"""
            INSERT INTO ops_platform_account_ownership(id,erp_platform_account_id,company_id,valid_from,valid_to,source,taken_at,created_at)
            VALUES(@id,@acc,@c,'2026-10-15',NULL,'manual',datetime('now'),datetime('now'))
            """,("@id",MoneySchemaFixture.Id()),("@acc",account),("@c",a)),
            "Open ownership intervals cannot overlap");
    }
}
