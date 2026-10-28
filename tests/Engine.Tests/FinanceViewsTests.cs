using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class FinanceViewsTests
{
    private static string Bw=>FinancialCore.Sponsors[0].Id;
    private static string Ex=>FinancialCore.Sponsors[1].Id;
    private const string Rider="2000000111";
    private static ClaimsPrincipal Admin=>UserAccess.Principal(new(){["username"]="synthetic-admin",["role"]="Admin"});
    private static ClaimsPrincipal Manager(string company="BW")=>UserAccess.Principal(new(){["username"]="synthetic-manager",["role"]="FinancialManager",["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(company),["accountCodes"]=new JsonArray("MAIN."+company+".DEBT"),["categoryCodes"]=new JsonArray("Advance"),["platforms"]=new JsonArray("Jahez")}});
    private static void InStore(Action<Store> action){string path=Path.Combine(Path.GetTempPath(),"gate-views-"+Guid.NewGuid().ToString("N"));try{action(new Store(path));}finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}}
    private static void Journal(Store store,string id,string period,params JsonObject[] lines)=>store.Write((c,tx)=>{Store.Save(c,"journal",new(){["id"]=id,["period"]=period,["status"]="posted",["transactionType"]="synthetic",["lines"]=new JsonArray(lines.Select(l=>(JsonNode?)l).ToArray())},tx);return true;});
    private static JsonObject Line(string code,string sponsor,decimal debit,decimal credit,string iqama="")=>FinancialCore.CreateJournalLine(code,sponsor,debit,credit,"Synthetic",iqama);
    private static JsonObject Company(JsonObject report)=>report["companies"]!.AsArray().OfType<JsonObject>().Single(c=>c.S("sponsorId")==Bw);
    private static JsonObject Row(JsonObject company,string code)=>company["rows"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("code")==code);
    private static string Snapshot(Store store)=>store.Read(c=>JsonSerializer.Serialize(ReleaseSafety.Capture(c)));
    public static string FakeIban(string code="80")
    {
        string body=code+"000000000000000123";int remainder=0;foreach(char ch in body+"281000")remainder=(remainder*10+ch-'0')%97;
        return "SA"+(98-remainder).ToString("D2")+body;
    }
    private static void SalaryFixture(Store store,string iban="",decimal amount=1000)
    {
        store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Rider",["iban"]=iban==""?FakeIban():iban,["address"]="Synthetic City"},tx);return true;});
        Journal(store,"payroll-approved","2026-10",Line("5301",Bw,amount,0,Rider),Line("2110",Bw,0,amount,Rider));
    }
    private static JsonObject Input()=>new(){["sponsorId"]="BW",["period"]="2026-10"};
    private static JsonObject ExportInput(JsonObject preview)
    {
        var input=Input();input["previewToken"]=preview.S("previewToken");input["rows"]=preview["rows"]!.DeepClone();return input;
    }
    [TestMethod] public void GeneratedChartHasUniqueCodesValidParentsAndPostingLeaves()
    {
        var resource=FinanceViews.TargetChart();Assert.AreEqual(1m,resource.N("schemaVersion"));
        var accounts=resource["accounts"]!.AsArray().OfType<JsonObject>().ToArray();Assert.AreEqual(392,accounts.Length);Assert.AreEqual(222,accounts.Count(a=>a.B("posting")));
        var codes=accounts.Select(a=>a.S("code")).ToHashSet();Assert.AreEqual(accounts.Length,codes.Count);
        foreach(var account in accounts){if(account["parent"]!=null)Assert.IsTrue(codes.Contains(account.S("parent")));if(account.B("posting"))Assert.IsFalse(accounts.Any(a=>a.S("parent")==account.S("code")));}
        resource["accounts"]=new JsonArray();Assert.AreEqual(392,FinanceViews.TargetChart()["accounts"]!.AsArray().Count);
    }
    [TestMethod] public void TrialBalancesRollUpAndKeepAmbiguousAndUnknownCodesPerCompanyWithoutWrites()
    {
        InStore(store=>{
            Journal(store,"opening","2026-09",Line("MAIN.BW.DEBT",Bw,500,0),Line("3101",Bw,0,500));
            Journal(store,"fee","2026-10",Line("BANK.BW",Bw,100,0),Line("4101",Bw,0,100));
            Journal(store,"cross","2026-10",Line("1310",Bw,25,0),Line("MAIN.BW.DEBT",Bw,0,25),Line("MAIN.EX.DEBT",Ex,25,0),Line("1310",Ex,0,25));
            var unknown=Line("5301",Bw,9,0);unknown["accountCode"]="LEGACY.UNKNOWN";Journal(store,"unknown","2026-10",unknown,Line("3101",Bw,0,9));
            store.Write((c,tx)=>{Store.Save(c,"journal",new(){["id"]="unposted",["status"]="draft",["period"]="2026-10",["lines"]=new JsonArray(Line("5301",Bw,9999,0))},tx);return true;});
            string before=Snapshot(store);
            foreach(string view in new[]{"current","target"})
            {
                var report=store.Read(c=>FinanceViews.TrialBalance(c,Admin,null,"2026-10","2026-10",view));
                foreach(var company in report["companies"]!.AsArray().OfType<JsonObject>()){Assert.IsTrue(company.B("isBalanced"));foreach(string key in new[]{"opening","period","closing"})Assert.AreEqual(company["totals"].N(key+"Debit"),company["totals"].N(key+"Credit"));}
                var bw=Company(report);Assert.AreEqual(134m,bw["totals"].N("periodDebit"));Assert.AreEqual(500m,bw["totals"].N("openingDebit"));Assert.AreEqual(609m,bw["totals"].N("closingDebit"));
                if(view=="target"){Assert.AreEqual(500m,Row(bw,"1").N("openingDebit"));Assert.AreEqual(475m,Row(bw,"111101").N("closingDebit"));Assert.AreEqual(25m,Row(bw,"112702").N("closingDebit"));Assert.AreEqual(2,bw["unmapped"]!.AsArray().Count);Assert.AreEqual(109m,bw["unmappedTotals"].N("periodDebit"));}
            }
            Assert.AreEqual(before,Snapshot(store));
            var scoped=store.Read(c=>FinanceViews.TrialBalance(c,Manager(),null,"2026-10","2026-10","target"));Assert.AreEqual(1,scoped["companies"]!.AsArray().Count);Assert.IsTrue(Company(scoped).B("scopeLimited"));Assert.AreEqual(1m,Company(scoped).N("excludedLineCount"));Assert.IsFalse(Company(scoped).B("isComplete"));
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>FinanceViews.TrialBalance(c,Manager(),"EX","2026-10","2026-10"))).Status);
        });
    }
    [TestMethod] public void TargetIncomeUsesPeriodNotOpeningAndDoesNotDoubleCountHeaders()
    {
        InStore(store=>{Journal(store,"prior-income","2026-09",Line("MAIN.BW.DEBT",Bw,700,0),Line("4101",Bw,0,700));SalaryFixture(store,amount:60);Journal(store,"fee","2026-10",Line("MAIN.BW.DEBT",Bw,100,0),Line("4101",Bw,0,100));
            foreach(string view in new[]{"current","target"}){var company=Company(store.Read(c=>FinanceViews.IncomeStatement(c,Admin,"BW","2026-10","2026-10",view)));Assert.AreEqual(100m,company.N("revenue"));Assert.AreEqual(60m,company.N("expense"));Assert.AreEqual(40m,company.N("net"));Assert.IsTrue(company.B("netIsComplete"));}
        });
    }
    [TestMethod] public void HeadersSumBothBalanceColumnsAndUnmappedIncomeStaysExplicit()
    {
        InStore(store=>{
            Journal(store,"cash-opening","2026-09",Line("MAIN.BW.DEBT",Bw,100,0),Line("3101",Bw,0,100));
            Journal(store,"opposite-cash","2026-09",Line("3101",Bw,40,0),Line("MAIN.BW.SET",Bw,0,40));
            Journal(store,"unresolved-expense","2026-10",Line("5201",Bw,10,0),Line("MAIN.BW.DEBT",Bw,0,10));
            var bw=Company(store.Read(c=>FinanceViews.TrialBalance(c,Admin,"BW","2026-10","2026-10","target")));
            Assert.AreEqual(100m,Row(bw,"1111").N("openingDebit"));Assert.AreEqual(40m,Row(bw,"1111").N("openingCredit"));Assert.AreEqual(90m,Row(bw,"1111").N("closingDebit"));Assert.AreEqual(40m,Row(bw,"1111").N("closingCredit"));Assert.IsTrue(bw.B("isBalanced"));
            var income=Company(store.Read(c=>FinanceViews.IncomeStatement(c,Admin,"BW","2026-10","2026-10")));Assert.IsFalse(income.B("netIsComplete"));Assert.AreEqual(10m,income["unmapped"]![0].N("periodDebit"));
        });
    }
    [TestMethod] public void ContextualMappingsUseHistoricalEvidenceAndKeepUnprovenSplitsUnmapped()
    {
        var journal=new JsonObject{["lines"]=new JsonArray()};JsonObject LineFor(string code)=>new(){["accountCode"]=code,["sponsorId"]=Bw,["iqama"]=Rider};
        Assert.AreEqual("112506",FinanceViews.MapAccount(LineFor("1210"),journal,new(){["category"]="WorkPermit"},null,null));
        Assert.IsNull(FinanceViews.MapAccount(LineFor("1210"),journal,null,null,null));
        Assert.IsNull(FinanceViews.MapAccount(LineFor("5201"),journal,null,new(){["vehicleType"]="Car"},null));
        Assert.AreEqual("512111",FinanceViews.MapAccount(LineFor("5201"),journal,new(){["category"]="AccidentDamage"},null,null));
        Assert.AreEqual("212101",FinanceViews.MapAccount(LineFor("2110"),journal,null,new(){["isEmployee"]=true},null));
        Assert.AreEqual("411501",FinanceViews.MapAccount(LineFor("4102"),journal,null,null,new(){["sourceRows"]=new JsonArray(new JsonObject{["platform"]="Keeta"})}));
        Assert.IsNull(FinanceViews.MapAccount(LineFor("4102"),journal,null,null,new(){["sourceRows"]=new JsonArray(new JsonObject{["platform"]="Keeta"},new JsonObject{["platform"]="Jahez"})}));
        Assert.IsNull(FinanceViews.MapAccount(LineFor("BANK.BW"),journal,null,null,null));
    }
    [TestMethod] public void IbanChecksumBankMappingAndCode40RequireIndependentValidation()
    {
        foreach(string code in new[]{"80","40","99"})Assert.IsTrue(BankPayrollFiles.ValidIban(FakeIban(code)));
        Assert.IsFalse(BankPayrollFiles.ValidIban(FakeIban()[..23]+"4"));Assert.IsFalse(BankPayrollFiles.ValidIban("SA00"));
        InStore(store=>{SalaryFixture(store,FakeIban("99"));var preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));CollectionAssert.Contains(preview["rows"]![0]!["errors"]!.AsArray().Select(v=>v!.ToString()).ToArray(),"unknown_bank_code");Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,ExportInput(preview))));Assert.AreEqual(0,store.Read(c=>Store.List(c,"audit").Count));});
        InStore(store=>{SalaryFixture(store,FakeIban("40"));var preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));Assert.AreEqual("NCBK",preview["rows"]![0].S("bank"));Assert.IsFalse(preview.B("canExport"));var input=ExportInput(preview);Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input)));input["confirmBankCode40"]=true;Assert.IsTrue(store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input)).Bytes.Length>0);});
    }
    [TestMethod] public void PreviewUsesVerifiedIbanAndOnlyPosted2110ClosingPayableForTheRequestedCompany()
    {
        InStore(store=>{SalaryFixture(store);Journal(store,"prior","2026-09",Line("5301",Bw,300,0,Rider),Line("2110",Bw,0,300,Rider));Journal(store,"paid","2026-10",Line("2110",Bw,200,0,Rider),Line("MAIN.BW.DEBT",Bw,0,200,Rider));Journal(store,"other","2026-10",Line("5301",Ex,900,0,Rider),Line("2110",Ex,0,900,Rider));Journal(store,"future","2026-11",Line("5301",Bw,400,0,Rider),Line("2110",Bw,0,400,Rider));
            store.Write((c,tx)=>{var rider=Store.Get(c,Rider,"rider",tx)!;rider["verifiedIban"]=FakeIban("10");Store.Save(c,"rider",rider,tx);Store.Save(c,"run",new(){["id"]="unapproved",["iqama"]=Rider,["period"]="2026-10",["result"]=new JsonObject{["payable"]=90000}},tx);return true;});
            Journal(store,"old-inactive","2026-08",Line("5301",Bw,700,0,"2000000222"),Line("2110",Bw,0,700,"2000000222"));
            string before=Snapshot(store);var preview=store.Read(c=>BankPayrollFiles.Preview(c,Manager(),Input()));Assert.AreEqual(1,preview["rows"]!.AsArray().Count);Assert.AreEqual(1100m,preview["rows"]![0].N("amount"));Assert.AreEqual(FakeIban("10"),preview["rows"]![0].S("iban"));Assert.AreEqual("NCBK",preview["rows"]![0].S("bank"));Assert.AreEqual(before,Snapshot(store));
            var other=Input();other["sponsorId"]="EX";Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>store.Read(c=>BankPayrollFiles.Preview(c,Manager(),other))).Status);
        });
    }
    [TestMethod] public void CsvHasExactBomCrlfAndHeaderAndAuditContainsOnlyMetadata()
    {
        InStore(store=>{SalaryFixture(store);var preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));var input=ExportInput(preview);input["rows"]![0]!["amount"]=900.25m;input["rows"]![0]!["notes"]="Synthetic, \"note\"\nsecond line";
            string journals=store.Read(c=>new JsonArray(Store.List(c,"journal").Select(j=>(JsonNode?)j).ToArray()).ToJsonString());
            var file=store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input));CollectionAssert.AreEqual(new byte[]{0xEF,0xBB,0xBF},file.Bytes[..3]);string csv=Encoding.UTF8.GetString(file.Bytes[3..]);Assert.IsTrue(csv.StartsWith(BankPayrollFiles.Header+"\r\n",StringComparison.Ordinal));Assert.IsTrue(csv.EndsWith("\r\n",StringComparison.Ordinal));Assert.IsFalse(csv.Replace("\r\n","").Contains('\n'));Assert.IsFalse(csv.Replace("\r\n","").Contains('\r'));StringAssert.Contains(csv,"900.25,\"Synthetic, \"\"note\"\"\r\nsecond line\"");StringAssert.Matches(file.Filename,new System.Text.RegularExpressions.Regex(@"^[0-9]+_bulkPayroll\.csv$"));
            var audit=store.Read(c=>Store.List(c,"audit").Single());Assert.AreEqual(1m,audit.N("rowCount"));Assert.AreEqual(1m,audit.N("editedRowCount"));Assert.AreEqual(900.25m,audit.N("total"));Assert.AreEqual("synthetic-admin",audit.S("actor"));Assert.AreEqual(7,audit.Count);Assert.IsFalse(audit.ToJsonString().Contains(Rider));Assert.IsFalse(audit.ToJsonString().Contains(FakeIban()));Assert.IsFalse(audit.ToJsonString().Contains("Synthetic Rider"));Assert.AreEqual(journals,store.Read(c=>new JsonArray(Store.List(c,"journal").Select(j=>(JsonNode?)j).ToArray()).ToJsonString()));
        });
    }
    [TestMethod] public void ExportRejectsInvalidEditsTamperedIdentityStaleSourceAndOtherActor()
    {
        InStore(store=>{SalaryFixture(store);var preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));
            foreach(decimal amount in new[]{0m,-1m,1.001m}){var input=ExportInput(preview);input["rows"]![0]!["amount"]=amount;Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input)));}
            foreach(string field in new[]{"bank","iban","name","iqama","address"}){var input=ExportInput(preview);input["rows"]![0]![field]="tampered";Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input)));}
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Manager(),ExportInput(preview)))).Status);
            Journal(store,"new-payment","2026-10",Line("2110",Bw,10,0,Rider),Line("MAIN.BW.DEBT",Bw,0,10,Rider));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,ExportInput(preview)))).Status);Assert.AreEqual(0,store.Read(c=>Store.List(c,"audit").Count));
        });
    }
    [TestMethod] public void MissingOrInvalidVerifiedIbanAndNonpositivePayableBlockExportEvenWhenClientClearsErrors()
    {
        InStore(store=>{SalaryFixture(store);store.Write((c,tx)=>{var rider=Store.Get(c,Rider,"rider",tx)!;rider["verifiedIban"]="SA00"+new string('0',20);Store.Save(c,"rider",rider,tx);return true;});
            var preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));Assert.IsFalse(preview.B("canExport"));var input=ExportInput(preview);input["rows"]![0]!["errors"]=new JsonArray();Assert.ThrowsExactly<ApiError>(()=>store.Write((c,tx)=>BankPayrollFiles.Export(c,tx,Admin,input)));
            store.Write((c,tx)=>{var rider=Store.Get(c,Rider,"rider",tx)!;rider["verifiedIban"]="";rider["iban"]="";Store.Save(c,"rider",rider,tx);return true;});preview=store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input()));CollectionAssert.Contains(preview["rows"]![0]!["errors"]!.AsArray().Select(v=>v!.ToString()).ToArray(),"missing_iban");
        });
        InStore(store=>{SalaryFixture(store);Journal(store,"fully-paid","2026-10",Line("2110",Bw,1000,0,Rider),Line("MAIN.BW.DEBT",Bw,0,1000,Rider));Assert.IsFalse(store.Read(c=>BankPayrollFiles.Preview(c,Admin,Input())).B("canExport"));});
    }
    [TestMethod] public void BankPermissionIsExplicitAndAssignedToFinancialManagerAndAdministrators()
    {
        foreach(string role in new[]{"Admin","SuperAdmin","FinancialManager","Accountant","FinancialAccountant","OperationsManager"}){var user=UserAccess.Principal(new(){["role"]=role,["username"]="synthetic"});bool expected=role is "Admin" or "SuperAdmin" or "FinancialManager";Assert.AreEqual(expected,AccessPolicy.Allowed(user,"POST","/api/finance/bank-file/preview"));Assert.AreEqual(expected,AccessPolicy.Allowed(user,"POST","/api/finance/bank-file/export"));}
    }
}
