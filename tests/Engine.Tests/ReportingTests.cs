using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Data;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ReportingTests
{
    [TestMethod]
    public void RequestedTablesHaveTypedMoneyDatesAndIndexedRelationColumns()
    {
        foreach(string name in new[]{"Riders","Companies","PlatformAccounts","AccountAssignments","Vehicles","VehicleAssignments","Debts","DebtSettlements","JournalLines","PayCycleLines","PlatformInvoices","PlatformInvoiceLines","Accounts"})Assert.IsTrue(Reporting.Tables.Any(t=>t.Name==name));Assert.AreEqual("decimal(19,4)",Reporting.Tables.Single(t=>t.Name=="JournalLines").Extra.Single(c=>c.Name=="Debit").SqlType);Assert.AreEqual("datetimeoffset(7)",Reporting.Tables.Single(t=>t.Name=="AccountAssignments").Extra.Single(c=>c.Name=="FromAt").SqlType);Assert.AreEqual(411,Reporting.Project(Reporting.Chart()).Count);
    }
    [TestMethod]
    public void SharedAccountProducesActualAssignmentsWithoutOwnerFallbackOrSecretColumns()
    {
        var doc=new JsonObject{["id"]="synthetic-report-account",["companyId"]="synthetic-company",["platformCode"]="keeta",["registeredRiderId"]="synthetic-owner",["password"]="synthetic-never-project",["assignments"]=new JsonArray(new JsonObject{["riderId"]="synthetic-operator-a",["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00"},new JsonObject{["riderId"]="synthetic-operator-b",["RawEffectiveFrom"]="2026-10-10T00:00:00Z",["RawEffectiveTo"]="2026-10-31T00:00:00Z"})};string body=doc.ToJsonString();var rows=Reporting.Project(new(doc.S("id"),"platform_account",body));Assert.AreEqual(3,rows.Count);var assignments=rows.Where(r=>r.Table=="AccountAssignments").ToList();Assert.AreEqual("synthetic-operator-a",assignments[0].Values.S("RiderId"));Assert.AreEqual("2026-09-30T21:00:00.0000000+00:00",assignments[0].Values.S("FromAt"));Assert.AreNotEqual(assignments[0].Values.S("RowKey"),assignments[1].Values.S("RowKey"));Assert.IsFalse(string.Join("",rows.Select(r=>r.Values.ToJsonString())).Contains("synthetic-never-project"));Assert.AreEqual(body,doc.ToJsonString());
        doc["assignments"]=new JsonArray();Assert.AreEqual(1,Reporting.Project(new(doc.S("id"),"platform_account",doc.ToJsonString())).Count);
    }
    [TestMethod]
    public void JournalLineCompanyPlatformRiderPeriodAndSourceRemainExactAndHashDetectsTampering()
    {
        var body=new JsonObject{["id"]="synthetic-report-journal",["period"]="2026-10",["sourceRef"]="synthetic-reviewed-source",["chart"]="target",["creditorSponsorId"]="synthetic-other-company",["lines"]=new JsonArray(new JsonObject{["sponsorId"]="synthetic-company",["platform"]="Keeta",["iqama"]="synthetic-rider",["accountCode"]="212103",["debit"]=0,["credit"]=123.45m})};var rows=Reporting.Project(new(body.S("id"),"journal",body.ToJsonString()));Assert.AreEqual(1,rows.Count);var row=rows[0].Values;Assert.AreEqual("synthetic-company",row.S("CompanyId"));Assert.AreEqual("Keeta",row.S("Platform"));Assert.AreEqual("synthetic-rider",row.S("RiderId"));Assert.AreEqual("2026-10",row.S("Period"));Assert.AreEqual("synthetic-reviewed-source",row.S("SourceId"));string original=Reporting.RowsSha(rows);row["Credit"]=123.46m;Assert.AreNotEqual(original,Reporting.RowsSha(rows));Assert.AreNotEqual(Reporting.Sha(" { } "),Reporting.Sha("{}"));
    }
    [TestMethod]
    public void SqliteRefreshIsAnExplicitNoOpAndSourceBodiesStayByteIdentical()
    {
        using var f=new PayFixture();var doc=new JsonObject{["id"]="synthetic-report-debt",["remainingBalance"]=50};f.Store.Write((c,tx)=>{Store.Save(c,"debt",doc,tx);return true;});string before=f.Store.Read(c=>Store.Get(c,doc.S("id"),"debt")!.ToJsonString());f.Store.Write((c,tx)=>{Reporting.Refresh(c,tx,"debt",doc);Assert.IsFalse(Reporting.Initialized(c,tx));Assert.AreEqual("sqlite_no_rpt_schema",Reporting.Check(c,tx).S("reason"));return true;});Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,doc.S("id"),"debt")!.ToJsonString()));Assert.AreEqual(0,f.Store.Read(c=>{using var cmd=Store.Command(c,null,"SELECT count(*) FROM sqlite_master WHERE name LIKE 'rpt%'");return Convert.ToInt32(cmd.ExecuteScalar());}));
    }
    [TestMethod]
    public void InvalidNumbersNeverQuietlyRoundIntoTheReport()
    {var doc=new JsonObject{["id"]="synthetic-invalid-projection",["remainingBalance"]=1.00001m};Assert.ThrowsExactly<InvalidOperationException>(()=>Reporting.Project(new(doc.S("id"),"debt",doc.ToJsonString())));Assert.AreEqual(0,Reporting.Project(new("synthetic-unrelated","user","{}" )).Count);}
}
