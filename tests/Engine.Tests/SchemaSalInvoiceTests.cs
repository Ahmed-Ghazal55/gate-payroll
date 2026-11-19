using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaSalInvoiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ActiveInvoiceUniqueAndPartitionBoundChildrenRejectInvalidWrites(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalInvoice.Ensure(c);
        string company=MoneySchemaFixture.Company(c),cr=MoneySchemaFixture.Id(),invoice=MoneySchemaFixture.Invoice(c,company,cr);
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Invoice(c,company,cr),"UQ_sal_invoice_active",sqliteToken:"sal_platform_invoice.app_code, sal_platform_invoice.erp_cr_id");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET status=4 WHERE id=@id",("@id",invoice));
        MoneySchemaFixture.Invoice(c,company,cr); // cancelled invoice no longer owns the active key
        void InsertLine(string month,int sourceRow=1)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@invoice,@month,@row,'synthetic',@id,100,10,90,10)",("@id",MoneySchemaFixture.Id()),("@invoice",invoice),("@month",month),("@row",sourceRow));
        SchemaConstraintAssert.ThrowsNamed(()=>InsertLine("2026-12-01"),"FK_sal_invoice_line_header",sqliteToken:"FOREIGN KEY");
        InsertLine("2026-11-01");
        SchemaConstraintAssert.ThrowsNamed(()=>InsertLine("2026-11-01"),"UQ_sal_invoice_line_row",sqliteToken:"sal_platform_invoice_line.invoice_id, sal_platform_invoice_line.source_row");
        string line=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM sal_platform_invoice_line")!;
        void Item(string month)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_item(id,line_id,period_month,item_code,amount) VALUES(@id,@line,@month,'TGA',10)",("@id",MoneySchemaFixture.Id()),("@line",line),("@month",month));
        SchemaConstraintAssert.ThrowsNamed(()=>Item("2026-12-01"),"FK_sal_invoice_item_line",sqliteToken:"FOREIGN KEY");
        Item("2026-11-01");
        SchemaConstraintAssert.ThrowsNamed(()=>Item("2026-11-01"),"UQ_sal_invoice_item",sqliteToken:"sal_platform_invoice_item.line_id, sal_platform_invoice_item.item_code");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET gross=-1 WHERE id=@id",("@id",invoice)),"CK_sal_invoice_totals");
        SchemaConstraintAssert.ThrowsNamed(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET run_mode=2 WHERE id=@id",("@id",invoice)),"CK_sal_invoice_flags");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_platform_invoice WHERE id=@id",("@id",invoice)));
        SchemaSalInvoice.Ensure(c);Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM sal_platform_invoice_item"));
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApprovedInvoiceIsFrozenNetBalancesAndCancelledFilesAndPeriodsAreReleased(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalInvoice.Ensure(c);
        string company=MoneySchemaFixture.Company(c),cr=MoneySchemaFixture.Id();byte[] hash=new byte[32];hash[0]=7;
        string Insert(string from,string to,byte[]? file=null)
        {
            string id=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice(id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,posting_owner) VALUES(@id,'ninja',@c,@cr,'synthetic',@from,@to,'2026-11-01',@hash,100,10,90,'invoice')",("@id",id),("@c",company),("@cr",cr),("@from",from),("@to",to),("@hash",file??System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            return id;
        }
        void Line(string invoice,int row)=>MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_platform_invoice_line(id,invoice_id,period_month,source_row,account_key,erp_platform_account_id,gross,deductions,net,orders) VALUES(@id,@invoice,'2026-11-01',@row,'synthetic',@id,100,10,90,10)",("@id",MoneySchemaFixture.Id()),("@invoice",invoice),("@row",row));
        void Set(string id,string set)=>MoneySchemaFixture.Exec(c,null,$"UPDATE sal_platform_invoice SET {set} WHERE id=@id",("@id",id));
        string first=Insert("2026-11-01","2026-11-15",hash);Line(first,1);
        SchemaConstraintAssert.ThrowsNamed(()=>Set(first,"net=80"),"CK_sal_invoice_net");
        MoneySchemaFixture.Reject(()=>Insert("2026-11-10","2026-11-20")); // overlaps the active first half
        string second=Insert("2026-11-16","2026-11-30");
        Set(first,"approved_at=datetime('now'),status=2");
        foreach(string set in new[]{"gross=200,net=190","period_to='2026-11-14'","city_code='other'"})
            MoneySchemaFixture.Reject(()=>Set(first,set));
        MoneySchemaFixture.Reject(()=>Line(first,2));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice_line SET gross=200,net=190 WHERE invoice_id=@id",("@id",first)));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_platform_invoice_line WHERE invoice_id=@id",("@id",first)));
        MoneySchemaFixture.Reject(()=>Insert("2026-12-01","2026-12-01",hash)); // same file while the first is active
        Set(first,"status=4"); // cancel stays possible after approval
        string again=Insert("2026-11-01","2026-11-15",hash); // cancelled file and period can be uploaded again
        Assert.AreEqual(3L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM sal_platform_invoice WHERE id IN (@a,@b,@c)",("@a",first),("@b",second),("@c",again)));
    }

    [TestMethod]
    public void StartupCreatesEmptyInvoiceHierarchy()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-invoice-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{foreach(string t in new[]{"platform_invoice","platform_invoice_line","platform_invoice_item"})Assert.IsTrue(SchemaSql.TableExists(c,null,"sal",t),t);return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
