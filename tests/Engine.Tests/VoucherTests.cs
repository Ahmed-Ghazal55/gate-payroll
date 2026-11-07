using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class VoucherTests
{
    static JsonObject Document(string id,string company,string date="2026-10-01",string odoo="")=>new(){["id"]=id,["companyId"]=company,["date"]=date,["status"]="draft",["odooVoucherNo"]=odoo,["riderIqama"]="synthetic-rider"};
    static JsonObject Save(Store store,string id,string company,string date="2026-10-01",string odoo="",string kind="advance_request")=>store.Write((c,tx)=>{var d=Document(id,company,date,odoo);Store.Save(c,kind,d,tx);return d;});
    [TestMethod]
    public void PerCompanyTypeYearSequenceHmacAndRollbackHaveNoGaps()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string company=FinancialCore.Sponsors[0].Id;
        var first=Save(f.Store,"synthetic-first",company);Assert.IsTrue(first.S("voucherNo").StartsWith("AV-2026-000001-"));
        string stem="AV-2026-000001",expected=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SyntheticVoucherSettings.Key),Encoding.UTF8.GetBytes(company+":"+stem)))[..8];Assert.AreEqual(stem+"-"+expected,first.S("voucherNo"));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write<int>((c,tx)=>{var d=Document("synthetic-rollback",company);Store.Save(c,"advance_request",d,tx);throw new ApiError("Synthetic rollback");}));
        var next=Save(f.Store,"synthetic-next",company);Assert.IsTrue(next.S("voucherNo").Contains("-000002-"));
        var otherCompany=Save(f.Store,"synthetic-other-company",FinancialCore.Sponsors[1].Id);Assert.IsTrue(otherCompany.S("voucherNo").Contains("-000001-"));Assert.AreNotEqual(first.S("voucherNo"),otherCompany.S("voucherNo"));
        Assert.IsTrue(Save(f.Store,"synthetic-year",company,"2027-01-01").S("voucherNo").StartsWith("AV-2027-000001-"));
        Assert.IsTrue(Save(f.Store,"synthetic-payment",company,kind:"payment_voucher").S("voucherNo").StartsWith("PV-2026-000001-"));
    }
    [TestMethod]
    public void AssignedNumbersAreImmutableCancelledNumbersRemainAndPublicProjectionHasNoValues()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string company=FinancialCore.Sponsors[0].Id;var doc=Save(f.Store,"synthetic-private-doc",company);string number=doc.S("voucherNo");
        var changed=doc.Clone();changed["voucherNo"]="AV-2026-000900-00000000";Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{Store.Save(c,"advance_request",changed,tx);return true;}));
        var forged=Document("synthetic-forgery",company);forged["voucherNo"]=number;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{Store.Save(c,"advance_request",forged,tx);return true;}));
        doc["status"]="cancelled";f.Store.Write((c,tx)=>{Store.Save(c,"advance_request",doc,tx);return true;});var result=f.Store.Read(c=>Vouchers.Verify(c,number));Assert.AreEqual("cancelled",result.S("status"));Assert.AreEqual("AV",result.S("type"));Assert.AreEqual(2,result.Count);Assert.IsFalse(result.ToJsonString().Contains("private"));Assert.AreEqual("invalid",f.Store.Read(c=>Vouchers.Verify(c,number[..^1]+(number[^1]=='A'?'B':'A'))).S("status"));
    }
    [TestMethod]
    public void DuplicateOdooReferenceIsRejectedPerCompanyAndPartyAndCanBeSearchedWithScope()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string company=FinancialCore.Sponsors[0].Id;var doc=Save(f.Store,"synthetic-odoo",company,odoo:"SYNTHETIC/ODOO/001",kind:"payment_voucher");
        Assert.ThrowsExactly<ApiError>(()=>Save(f.Store,"synthetic-duplicate",company,odoo:"synthetic/odoo/001",kind:"payment_voucher"));
        Save(f.Store,"synthetic-independent-company",FinancialCore.Sponsors[1].Id,odoo:"SYNTHETIC/ODOO/001",kind:"payment_voucher");
        var actor=HungerStationInvoiceTests.Actor();Assert.IsTrue(f.Store.Read(c=>GlobalSearch.Search(c,actor,doc.S("voucherNo"))).Any(h=>h.Id==doc.S("id")));Assert.IsTrue(f.Store.Read(c=>GlobalSearch.Search(c,actor,"SYNTHETIC/ODOO/001")).Any(h=>h.Id==doc.S("id")));
        var reviewer=UserAccess.Principal(new(){["username"]="synthetic-reviewer",["role"]="Reviewer"});Assert.AreEqual(0,f.Store.Read(c=>Vouchers.Search(c,reviewer,doc.S("voucherNo")).Count()));
    }
    [TestMethod]
    public void HistoricalSnapshotBodiesAreNotRenumberedAndManualJournalSharesItsDraftNumber()
    {
        using var f=new HungerStationInvoiceTests.Fixture();string company=FinancialCore.Sponsors[0].Id;var historic=Document("synthetic-historic",company);string before=historic.ToJsonString();f.Store.Write((c,tx)=>{Store.Save(c,"journal",historic,tx,preserveSnapshot:true);return true;});Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,"synthetic-historic","journal"))!.ToJsonString());
        var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,HungerStationInvoiceTests.Actor(),new(){["companyId"]=company,["date"]="2026-10-01",["reference"]="SYNTHETIC-JV",["description"]="Synthetic manual operation",["lines"]=new JsonArray(new JsonObject{["accountCode"]="512101",["debit"]=1,["credit"]=0},new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=1})}));
        var approved=f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,HungerStationInvoiceTests.Actor("synthetic-checker"),draft.S("id")));Assert.AreEqual(draft.S("voucherNo"),f.Store.Read(c=>Store.Get(c,approved.S("journalEntryId"),"journal"))!.S("voucherNo"));Assert.AreEqual("approved",f.Store.Read(c=>Vouchers.Verify(c,draft.S("voucherNo"))).S("status"));
    }
    [TestMethod]
    public void MissingSigningKeyRefusesNewFinancialWritesWithoutConsumingNumbers()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Vouchers.Configure(null);
        try{var error=Assert.ThrowsExactly<ApiError>(()=>Save(f.Store,"synthetic-unconfigured",FinancialCore.Sponsors[0].Id));Assert.AreEqual(503,error.Status);Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"voucher_sequence").Count));}
        finally{Vouchers.Configure(SyntheticVoucherSettings.Key);}
    }
    [TestMethod]
    public void SyntheticSqlConcurrentWritersAndBufferedRepeatedSavesAllocateOnce()
    {
        string? configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION");if(string.IsNullOrEmpty(configured)){Assert.Inconclusive("Explicit isolated loopback SQL required.");return;}
        var b=new SqlConnectionStringBuilder(configured);Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(b.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));string db="GateVoucherTest_"+Guid.NewGuid().ToString("N"),dir=Path.Combine(Path.GetTempPath(),db);b.InitialCatalog="master";using var admin=new SqlConnection(b.ToString());admin.Open();using(var q=admin.CreateCommand()){q.CommandText="CREATE DATABASE ["+db+"]";q.ExecuteNonQuery();}
        try
        {
            b.InitialCatalog=db;var stores=new[]{new Store(dir,b.ToString()),new Store(dir,b.ToString())};string company=FinancialCore.Sponsors[0].Id;
            var tasks=Enumerable.Range(0,16).Select(n=>Task.Run(()=>Save(stores[n%2],"synthetic-concurrent-"+n,company))).ToArray();Task.WaitAll(tasks);
            var numbers=tasks.Select(t=>int.Parse(t.Result.S("voucherNo").Split('-')[2])).Order().ToArray();CollectionAssert.AreEqual(Enumerable.Range(1,16).ToArray(),numbers);
            stores[0].Write((c,tx)=>{Store.BufferedWrites(c,tx,()=>{var doc=Document("synthetic-buffered",company);Store.Save(c,"advance_request",doc,tx);doc["status"]="cancelled";Store.Save(c,"advance_request",doc,tx);});return true;});Assert.IsTrue(stores[0].Read(c=>Store.Get(c,"synthetic-buffered","advance_request"))!.S("voucherNo").Contains("-000017-"));Assert.IsTrue(Save(stores[0],"synthetic-after-buffer",company).S("voucherNo").Contains("-000018-"));
        }
        finally{SqlConnection.ClearAllPools();using var q=admin.CreateCommand();q.CommandText="ALTER DATABASE ["+db+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["+db+"]";q.ExecuteNonQuery();Directory.Delete(dir,true);}
    }
}
