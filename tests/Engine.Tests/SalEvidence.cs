using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Payroll;

/// <summary>WC4D H1 test plumbing: seed per-rider distribution evidence (invoice line of the rider's account → sal.distribution)
/// so a calc test states its gross as fixture evidence, not as a caller value. Keeta/Chefz bind from frozen capture instead.</summary>
public static class SalEvidence
{
    public static JsonObject Calculate(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string run,JsonObject input)
    {
        string app=MoneySchemaFixture.Scalar(c,tx,"SELECT app_code FROM sal_pay_run WHERE id=@id",("@id",run))?.ToString()??"";
        if(app is not "keeta" and not "chefz")
            foreach(var rider in (input["riders"] as JsonArray??[]).OfType<JsonObject>())
                if(rider.ContainsKey("gross"))Gross(c,tx,run,rider.S("erpRiderId"),J.Money(rider.N("gross")));
        return SalPayCalc.Calculate(c,tx,actor,run,input);
    }

    public static void Gross(DbConnection c,DbTransaction tx,string run,string rider,decimal gross,string? account=null)
    {
        string company=MoneySchemaFixture.Scalar(c,tx,"SELECT company_id FROM sal_pay_run WHERE id=@id",("@id",run))!.ToString()!;
        string app=MoneySchemaFixture.Scalar(c,tx,"SELECT app_code FROM sal_pay_run WHERE id=@id",("@id",run))!.ToString()!;
        object pm=MoneySchemaFixture.Scalar(c,tx,"SELECT period_month FROM sal_pay_run WHERE id=@id",("@id",run))!;
        string month=pm is DateTime d?d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):pm.ToString()![..10];
        object? inv=MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM sal_platform_invoice WHERE city_code=@run",("@run",run));
        string invoice;
        if(inv is null or DBNull)
        {
            invoice=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_platform_invoice(id,app_code,company_id,erp_cr_id,city_code,period_from,period_to,period_month,file_hash,gross,deductions,net,posting_owner) VALUES(@id,@app,@c,@cr,@run,@m,@m,@m,randomblob(32),0,0,0,'invoice')",
                ("@id",invoice),("@app",app),("@c",company),("@cr",MoneySchemaFixture.Id()),("@run",run),("@m",month));
        }
        else invoice=inv.ToString()!;
        MoneySchemaFixture.Exec(c,tx,"DELETE FROM sal_distribution WHERE invoice_id=@inv AND erp_rider_id=@r",("@inv",invoice),("@r",rider));
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_distribution(id,period_month,invoice_id,erp_platform_account_id,erp_rider_id,share_amount,method,status) VALUES(@id,@m,@inv,@acc,@r,@amt,'order_share','ready')",
            ("@id",MoneySchemaFixture.Id()),("@m",month),("@inv",invoice),("@acc",account??MoneySchemaFixture.Id()),("@r",rider),("@amt",gross));
    }
}
