using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ErpImport;
using Payroll;

[TestClass]
public sealed class HungerStationFactTests
{
    static SourceSnapshot Snapshot()=>new(new(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic_sqlite");
    static JsonObject Event(string id,string type="RiderAdvance",decimal amount=100)=>new(){["SourceEventId"]=id,["IdempotencyKey"]="synthetic-key-"+id,["EventType"]=type,["SponsorId"]="synthetic-sponsor",["Amount"]=amount,["Currency"]="SAR",["ApprovedAt"]="2026-11-01T00:00:00Z",["ApprovedBy"]="synthetic-checker",["ReversesEventId"]=""};
    static ImportPlan Plan(SourceSnapshot source){var plan=new ImportPlan();HsFactsSource.AddToPlan(source,plan,new(){["synthetic-sponsor"]="synthetic-company"});return plan;}
    [TestMethod]
    public void MissingContractsReportUnavailableWithoutQueryingAssumedTables()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();var tables=new Dictionary<string,List<JsonObject>>();HsFactsSource.Read(c,tables);Assert.AreEqual(0,tables.Count);var plan=Plan(Snapshot());foreach(string kind in HungerStationFacts.Kinds)Assert.AreEqual("source not available",plan.Unavailable[kind]);
        string report=JsonSerializer.Serialize(ImportExecutor.Run(Snapshot(),plan,[]));StringAssert.Contains(report,"source not available");Assert.IsFalse(report.Contains("sourceFacts"));
    }
    [TestMethod]
    public void ContractDiscoveryUsesColumnsAndOrdersStableKeysWhileAnEmptyPresentViewIsAvailable()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();var contract=HsFactsSource.Contracts.Single(x=>x.Kind=="hs_wallet_event");using(var cmd=c.CreateCommand()){cmd.CommandText="CREATE TABLE [SyntheticFacts]("+string.Join(',',contract.Fields.Split(' ').Select(f=>"["+f+"] "+(f=="RowVersion"?"BLOB":"TEXT")))+")";cmd.ExecuteNonQuery();}
        var tables=new Dictionary<string,List<JsonObject>>();HsFactsSource.Read(c,tables);Assert.IsTrue(tables.ContainsKey(HsFactsSource.Table(contract.Kind)));Assert.AreEqual(0,tables.Values.Single().Count);Assert.IsFalse(Plan(new(tables,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,"synthetic")).Unavailable.ContainsKey(contract.Kind));
        using(var cmd=c.CreateCommand()){cmd.CommandText="INSERT INTO SyntheticFacts(SourceEventId,RowVersion) VALUES ('synthetic-b',X'02'),('synthetic-a',X'01')";cmd.ExecuteNonQuery();}tables=[];HsFactsSource.Read(c,tables);Assert.AreEqual("synthetic-a",tables.Values.Single()[0].S("SourceEventId"));Assert.AreEqual("01",tables.Values.Single()[0].S("RowVersion"));
    }
    [TestMethod]
    public void ApprovedFactsAreImmutableAuditedIdempotentAndReversalsAreNewRows()
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-hs-facts-"+Guid.NewGuid().ToString("N"));try
        {
            var store=new Store(path);var source=Snapshot();source.Tables[HsFactsSource.Table("hs_wallet_event")]=[Event("synthetic-original")];var plan=Plan(source);ImportExecutor.Run(source,plan,store.Read(ImportExecutor.Target),store);string original=store.Read(c=>Store.List(c,"hs_wallet_event").Single().ToJsonString());int audits=store.Read(c=>Store.List(c,"audit").Count);
            var repeated=ImportExecutor.Run(source,plan,store.Read(ImportExecutor.Target),store);Assert.AreEqual(0,repeated.Counts.Sum(k=>k.New+k.Changed));Assert.AreEqual(audits,store.Read(c=>Store.List(c,"audit").Count));
            source.Tables[HsFactsSource.Table("hs_wallet_event")][0]["Amount"]=200;Assert.ThrowsExactly<ImportFailure>(()=>ImportExecutor.Run(source,Plan(source),store.Read(ImportExecutor.Target),store));Assert.AreEqual(original,store.Read(c=>Store.List(c,"hs_wallet_event").Single().ToJsonString()));
            source.Tables[HsFactsSource.Table("hs_wallet_event")][0]["Amount"]=100;var reverse=Event("synthetic-reverse","reversal",-100);reverse["ReversesEventId"]="synthetic-original";source.Tables[HsFactsSource.Table("hs_wallet_event")].Add(reverse);ImportExecutor.Run(source,Plan(source),store.Read(ImportExecutor.Target),store);Assert.AreEqual(2,store.Read(c=>Store.List(c,"hs_wallet_event").Count));Assert.AreEqual(original,store.Read(c=>Store.List(c,"hs_wallet_event").Single(e=>e.S("sourceEventId")=="synthetic-original").ToJsonString()));Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [TestMethod]
    public void UnapprovedFactsAndMissingCompanyRelationsNeverBecomeImportedApprovedFacts()
    {
        var source=Snapshot();var pending=Event("synthetic-pending");pending.Remove("ApprovedBy");var unknown=Event("synthetic-unknown");unknown["SponsorId"]="unknown";source.Tables[HsFactsSource.Table("hs_wallet_event")]=[pending,unknown];source.Tables[HsFactsSource.Table("hs_month_close")]=[new(){["CloseSnapshotId"]="synthetic-close",["C"]=100,["A"]=-40,["M"]=-80}];var plan=Plan(source);Assert.AreEqual(0,plan.Documents.Count);Assert.AreEqual(1,plan.Rejected["hs_wallet_event"]["approval_not_available"]);Assert.AreEqual(1,plan.Rejected["hs_wallet_event"]["company_relation_unknown"]);Assert.AreEqual(1,plan.Rejected["hs_month_close"]["approval_not_available"]);
    }
    [TestMethod]
    public void D20UsesKnownCmaAndNeverTreatsTheRawBalanceAsDebt()
    {
        var known=HungerStationFacts.MonthClose(new(){["C"]=100,["A"]=-40,["M"]=-80});Assert.AreEqual(-100m,known.N("expected"));Assert.AreEqual(60m,known.N("remainingOldCarry"));Assert.AreEqual(-140m,known.N("currentPeriodBalance"));Assert.IsFalse(known.B("postingAllowed"));
        foreach(string missing in new[]{"C","A","M"}){var raw=new JsonObject{["C"]=100,["A"]=-40,["M"]=-80};raw.Remove(missing);var result=HungerStationFacts.MonthClose(raw);Assert.AreEqual("unknown",result.S("status"));Assert.IsFalse(result.ContainsKey("currentPeriodBalance"));Assert.IsFalse(result.B("postingAllowed"));}
        foreach(var mapping in HungerStationFacts.AccountMapping){var proposal=HungerStationFacts.Proposal("hs_wallet_event",new(){["status"]="approved",["sourceFacts"]=new JsonObject{["EventType"]=mapping.Key,["Amount"]=100,["C"]=100,["A"]=-40,["M"]=-80}});Assert.AreEqual(mapping.Value,proposal.S("accountCode"));Assert.IsFalse(proposal.B("postingAllowed"));}
        var blocked=HungerStationFacts.Proposal("hs_wallet_event",new(){["sourceFacts"]=new JsonObject{["EventType"]="NegativeRiderNet",["Amount"]=-80}});Assert.AreEqual("",blocked.S("accountCode"));StringAssert.Contains(blocked.ToJsonString(),"cma_unknown");
    }
}
