using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaSalInvoiceTests
{
    [DataTestMethod]
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
