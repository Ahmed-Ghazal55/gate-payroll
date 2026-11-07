using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using F=DebtSheetReviewTests;

[TestClass]
public sealed class DebtOpeningTests
{
    static byte[] Deductions(bool includeMissingRejected=false,bool conflict=false,bool summaryDifference=false)
    {
        string[] headers=F.RequestHeader();var request=F.Request();var total=F.Request();request[9]=total[9]="112501 سلف المناديب النقدية";
        if(conflict)total[4]="31";
        var ex=new List<string[]>{headers,request};if(includeMissingRejected)ex.Add(F.Request(reference:"SYN/2026/002",status:"مرفوض"));
        var control=Enumerable.Range(0,40).Select(_=>Enumerable.Repeat("",20).ToArray()).ToArray();
        control[3][1]="SyntheticApp";control[3][3]="30";control[9][18]="30";
        if(summaryDifference){control[3][3]="31";control[9][18]="31";}
        return F.Book([
            ("اكسبريس",ex.ToArray()),("البوابا",[headers]),("التجارية",[headers]),("الاجماليات",[headers,total]),("المرفوض",[headers]),
            ("تفصيلي الاستقطاعات",[["رقم الهوية","الاسم","استقطاعات المناديب","سلف المناديب النقدية","سلف انترنت مناديب","سلف محافظ هنقرستيشن","مخالفات مرورية مناديب","الإجمالي"],[F.Id,"SYNTHETIC","0","30","0","0","0","30"]]),
            ("اجماليات السلف والاستقطاعات",control),
            ("ما لم يخصم سابقا",[["SYNTHETIC","2026-09-20","مدير الحسابات","", "30",F.Id,"SYN/2026/001"],["SYNTHETIC","2026-09-20","مدير الحسابات","", "10",F.Id,"SYN/2026/009"]])]);
    }
    static Dictionary<string,List<JsonObject>> History()=>new()
    {
        ["Sponsors"]=[new(){["Id"]="source-bw",["EmployerIdentityNumber"]=FinancialCore.ResolveSponsor("BW").UnifiedNumber},new(){["Id"]="source-ex",["EmployerIdentityNumber"]=FinancialCore.ResolveSponsor("EX").UnifiedNumber}],
        ["Employees"]=[new(){["Id"]="former",["IqamaNo"]=F.Id,["SponsorId"]="source-bw",["Status"]=4,["IsDeleted"]=true}],
        ["EmployeeSponsorshipPeriods"]=[new(){["Id"]="closed",["EmployeeId"]="former",["SponsorId"]="source-ex",["EffectiveFrom"]="2026-01-01",["EffectiveTo"]="2026-10-20",["Status"]=4},new(){["Id"]="new",["EmployeeId"]="former",["SponsorId"]="source-bw",["EffectiveFrom"]="2026-10-21",["EffectiveTo"]=null,["Status"]=2}]
    };
    [TestMethod]
    public void HistoryUsesLatestEndedPeriodIncludingFormerDeletedEmployeesAndAmbiguityStaysBlocked()
    {
        var h=History();var r=DebtCreditorHistory.Resolve(h,F.Id,FinancialCore.Sponsors);
        Assert.AreEqual(FinancialCore.ResolveSponsor("EX").Id,r.Company);Assert.AreEqual("latest_period_ended_by_balance_date",r.Basis);
        h["EmployeeSponsorshipPeriods"].Add(new(){["Id"]="conflict",["EmployeeId"]="former",["SponsorId"]="source-bw",["EffectiveFrom"]="2026-02-01",["EffectiveTo"]="2026-10-20",["Status"]=4});
        Assert.AreEqual("erp_history_ambiguous_company",DebtCreditorHistory.Resolve(h,F.Id,FinancialCore.Sponsors).Reason);
        h.Remove("EmployeeSponsorshipPeriods");Assert.AreEqual(FinancialCore.ResolveSponsor("BW").Id,DebtCreditorHistory.Resolve(h,F.Id,FinancialCore.Sponsors).Company);
        Assert.AreEqual("creditor_unresolved",DebtCreditorHistory.Resolve(h,"2000000899",FinancialCore.Sponsors).Reason);
        Assert.IsFalse(DebtCreditorHistory.Catalog.Any(t=>t.Fields.Contains("Name")||t.Fields.Contains("Phone")||t.Fields.Contains("Iban")));
    }
    [TestMethod]
    public void HistorySqliteReadHasNoStatusFilterAndNoWritesOrSensitiveColumns()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();
        foreach(var table in DebtCreditorHistory.Catalog.Where(t=>!t.Optional))
        {
            using var create=c.CreateCommand();create.CommandText=$"CREATE TABLE [{table.Name}] ({string.Join(',',table.Fields.Split(' ').Select(f=>$"[{f}] {(f=="IsDeleted"?"INTEGER":"TEXT")}"))})";create.ExecuteNonQuery();
        }
        using(var insert=c.CreateCommand()){insert.CommandText="INSERT INTO Employees(Id,IqamaNo,SponsorId,Status,IsDeleted) VALUES('former','2000000801','source',4,1)";insert.ExecuteNonQuery();}
        var read=DebtCreditorHistory.Read(c);Assert.AreEqual(1,read["Employees"].Count);Assert.AreEqual("4",read["Employees"][0]["Status"]!.ToString());Assert.IsFalse(read.ContainsKey("EmployeeSponsorshipPeriods"));
        using var count=c.CreateCommand();count.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";Assert.AreEqual(2L,count.ExecuteScalar());
    }
    [TestMethod]
    public void ReconciliationExcludesRejectedAndCountsCarriedDuplicateOnceAndDetectsChangedSummary()
    {
        var evidence=DebtSheetImporter.ReconcileDeductions(DebtSheetImporter.FreezeInput(Deductions(true)),F.Mapping());
        Assert.IsTrue(evidence.Summary.B("matched"),evidence.Summary.ToJsonString());Assert.AreEqual(1m,evidence.Summary.N("uniqueApprovedRequests"));Assert.AreEqual(30m,evidence.Summary.N("approvedTotal"));
        Assert.AreEqual(1m,evidence.Summary.N("rejectedMissingFromRejectedSheet"));Assert.AreEqual(1m,evidence.Summary.N("carriedAlreadyInTotals"));Assert.AreEqual(1m,evidence.Summary.N("carriedPreviousPeriod"));
        Assert.AreEqual(0m,evidence.Summary.N("octoberDocumentsToLoad"));Assert.AreEqual("SYN/2026/001",evidence.Lineage[0].S("odooVoucherNo"));
        Assert.IsFalse(DebtSheetImporter.ReconcileDeductions(DebtSheetImporter.FreezeInput(Deductions(conflict:true)),F.Mapping()).Summary.B("matched"));
        var warnings=DebtSheetImporter.ReconcileDeductions(DebtSheetImporter.FreezeInput(Deductions(summaryDifference:true)),F.Mapping());Assert.IsTrue(warnings.Summary.B("matched"));Assert.IsTrue(warnings.Issues.OfType<JsonObject>().Any(i=>i.S("reason").StartsWith("summary_",StringComparison.Ordinal)));
        var strict=F.Mapping();strict.Remove("deductionReconciliationPolicy");Assert.IsFalse(DebtSheetImporter.ReconcileDeductions(DebtSheetImporter.FreezeInput(Deductions(summaryDifference:true)),strict).Summary.B("matched"));
    }
    [TestMethod]
    public void ApprovedOpeningKeepsNegativeRowsBlockedAndCreatesOnlyInactiveDebtOnlyPeopleAtomically()
    {
        byte[] debt=F.Debt([F.DebtRow(),F.DebtRow("2000000802",bucket:50,payment:-1)]);
        var opening=DebtSheetImporter.PreviewReviewedOpening(debt,Deductions(),F.Mapping(),[],History());var p=opening.Preview;
        Assert.IsTrue(p.Summary.B("canCommit"),p.Summary.ToJsonString());Assert.AreEqual(1m,p.Summary.N("blockedRows"));Assert.AreEqual(1,p.NewRiders.Count);
        Assert.AreEqual("Inactive",p.NewRiders[0].S("status"));Assert.AreEqual("erp_history",p.Documents.Single().Body.S("creditorSource"));Assert.AreEqual("2026-10",p.Documents[0].Body.S("effectiveFrom"));Assert.IsFalse(p.Documents[0].Body.ContainsKey("odooVoucherNo"));
        string dir=Path.Combine(Path.GetTempPath(),"gate-opening-synthetic-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            Assert.ThrowsExactly<ImportFailure>(()=>DebtSheetImporter.CommitReviewedOpening(store,p,p.Fingerprint,false));
            Assert.ThrowsExactly<ImportFailure>(()=>DebtSheetImporter.CommitReviewedOpening(store,p,p.Fingerprint,true));Assert.AreEqual(0,store.Read(c=>Store.List(c,"rider").Count));
            store.Write((c,tx)=>{Store.Save(c,"cutover_receipt",new(){["id"]="synthetic-cutover"},tx);return true;});
            var saved=DebtSheetImporter.CommitReviewedOpening(store,p,p.Fingerprint,true);Assert.AreEqual(1m,saved.N("added"));Assert.AreEqual(1m,saved.N("inactiveRidersAdded"));
            Assert.IsTrue(store.Read(c=>DebtSheetImporter.VerifyReviewedOpening(c,p)).B("verified"));Assert.AreEqual(0m,DebtSheetImporter.CommitReviewedOpening(store,p,p.Fingerprint,true).N("added"));
            Assert.AreEqual(0,store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual("pending_approval",store.Read(c=>Store.List(c,"debt").Single()).S("status"));
            Assert.DoesNotContain(F.Id,p.Summary.ToJsonString());Assert.DoesNotContain("SYNTHETIC_NAME",opening.Review.PrivateReport.ToJsonString());
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [TestMethod]
    public void CrossCompanySheetWinsAndLaterFixedRowsAddOnlyTheNewReferences()
    {
        var mapping=F.Mapping();var first=DebtSheetImporter.PreviewReviewedOpening(F.Debt(ex:[["100",F.Id]]),Deductions(),mapping,[F.Rider()]);
        Assert.IsTrue(first.Preview.Summary.B("canCommit"));Assert.AreEqual(FinancialCore.ResolveSponsor("EX").Id,first.Preview.Documents[0].Body.S("creditorSponsorId"));
        var second=DebtSheetImporter.PreviewReviewedOpening(F.Debt([F.DebtRow(),F.DebtRow("2000000802",50)],ex:[["100",F.Id],["50","2000000802"]]),Deductions(),mapping,[F.Rider(),new(){["id"]="2000000802",["iqama"]="2000000802",["financeSponsorId"]="BW"}]);
        Assert.AreNotEqual(first.Preview.Fingerprint,second.Preview.Fingerprint);Assert.AreEqual(first.Preview.Documents[0].Body.S("sourceRowFingerprint"),second.Preview.Documents[0].Body.S("sourceRowFingerprint"));
        string dir=Path.Combine(Path.GetTempPath(),"gate-opening-synthetic-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);store.Write((c,tx)=>{Store.Save(c,"rider",F.Rider(),tx);Store.Save(c,"rider",new(){["id"]="2000000802",["iqama"]="2000000802",["financeSponsorId"]="BW"},tx);Store.Save(c,"cutover_receipt",new(){["id"]="synthetic-cutover"},tx);return true;});
            Assert.AreEqual(1m,DebtSheetImporter.CommitReviewedOpening(store,first.Preview,first.Preview.Fingerprint,true).N("added"));
            Assert.AreEqual(1m,DebtSheetImporter.CommitReviewedOpening(store,second.Preview,second.Preview.Fingerprint,true).N("added"));
            Assert.AreEqual(0m,DebtSheetImporter.CommitReviewedOpening(store,second.Preview,second.Preview.Fingerprint,true).N("added"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
