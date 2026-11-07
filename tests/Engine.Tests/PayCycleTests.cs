using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class PayCycleTests
{
    static string Rider(int i)=>"2"+i.ToString("D9");
    static JsonObject Source(string rider,int orders,decimal gross)=>new(){["rider"]=rider,["orders"]=orders,["baseGross"]=gross,["deduction"]=0,["category"]="sponsored_private",["days"]=1,["metrics"]=new JsonObject()};
    internal static (PayFixture F,JsonObject Input,string Debt) Setup(bool debt=true)
    {
        var f=new PayFixture();f.Approve("pay_setting","global",f.Global());string company=FinancialCore.Sponsors[0].Id,period="2026-10";
        foreach(string app in new[]{"keeta","hs"}){f.Approve("pay_terms",app,f.Draft("pay_terms",app,PayFixture.Terms()));var scheme=f.Approve("pay_scheme",app,f.Draft("pay_scheme",app,PayFixture.Scheme(),"synthetic"));f.Store.Write((c,tx)=>{foreach(int i in new[]{1,2}){string rider=Rider(i);Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic cycle rider",["isDeliveryRider"]=true,["sponsorId"]=company,["status"]="Active"},tx);Store.Save(c,"pay_assignment",new(){["id"]="synthetic-choice-"+app+i,["rider"]=rider,["app"]=app,["period"]=period,["status"]="approved",["schemeId"]=scheme.S("id"),["choiceApprovedAt"]="2026-09-29T00:00:00.0000000+00:00"},tx);}PlatformPosting.Set(c,tx,PayFixture.Maker,app,new(){["enabled"]=true});return true;});}
        var invoices=new JsonArray();foreach(string app in new[]{"keeta","hs"})
        {
            string account="synthetic-account-"+app,invoice="synthetic-invoice-"+app,line="synthetic-line-"+app;var shares=new JsonArray(Source(Rider(1),10,100));if(app=="keeta")shares.Add(Source(Rider(2),20,200));decimal gross=app=="keeta"?300:100,incentive=app=="keeta"?30:0;
            f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]=app,["registeredRiderId"]=Rider(2),["assignments"]=new JsonArray(new JsonObject{["riderId"]=Rider(1),["RawEffectiveFrom"]="2026-10-01T00:00:00Z"},new JsonObject{["riderId"]=Rider(2),["RawEffectiveFrom"]="2026-10-01T00:00:00Z"})},tx);Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]=PayPlatforms.For(app).Platform,["period"]=period,["approvalStatus"]="approved",["amountExclVat"]=gross+incentive,["vat"]=(gross+incentive)*.15m,["deductionsTotal"]=app=="keeta"?30:0},tx);Store.Save(c,"platform_invoice_line",new(){["id"]=line,["invoiceId"]=invoice,["platformAccountId"]=account,["lineType"]="rider"},tx);return true;});
            var deductions=new JsonArray();if(app=="keeta")deductions.Add(new JsonObject{["sourceLineId"]=line,["level"]="company_level",["amount"]=30,["expenseAccount"]="526402"});
            invoices.Add(new JsonObject{["app"]=app,["invoiceId"]=invoice,["accounts"]=new JsonArray(new JsonObject{["accountId"]=account,["sourceLineId"]=line,["baseGross"]=gross,["incentives"]=incentive,["orders"]=app=="keeta"?30:10,["riders"]=shares}),["deductions"]=deductions});
        }
        if(debt)f.Store.Write((c,tx)=>{Store.Save(c,"debt",new(){["id"]="synthetic-cycle-debt",["iqama"]=Rider(1),["category"]="Advance",["creditorSponsorId"]=company,["status"]="active",["remainingBalance"]=50,["paidAmount"]=0,["totalAmount"]=50,["monthlyInstallment"]=15,["effectiveFrom"]="2026-01"},tx);FinancialCore.PostJournal(c,tx,"opening","Synthetic approved debt opening",period,"synthetic-debt-opening",new JsonArray(FinancialCore.CreateJournalLine("1210",company,50,0,iqama:Rider(1),debtId:"synthetic-cycle-debt"),FinancialCore.CreateJournalLine("3101",company,0,50,iqama:Rider(1))),"synthetic-opening-checker");return true;});
        return(f,new(){["period"]=period,["companyId"]=company,["invoices"]=invoices},"synthetic-cycle-debt");
    }
    static JsonObject Create(PayFixture f,JsonObject input)=>f.Store.Write((c,tx)=>PayCycles.Create(c,tx,PayFixture.Maker,input));
    internal static JsonObject Advance(PayFixture f,string id,int stage,bool checker=false)=>f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,checker?PayFixture.Checker:PayFixture.Maker,id,new(){["stage"]=stage}));
    // BR §32: stage 7 needs every row reviewed; helpers approve all ready rows unless a test reviews them itself.
    internal static JsonObject Through(PayFixture f,JsonObject input,int stage=6,bool review=true){var d=Create(f,input);for(int s=1;s<=stage;s++)d=Advance(f,d.S("id"),s);
        if(review&&stage==6){string id=d.S("id");f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,id));d=f.Store.Read(c=>Store.Get(c,id,PayCycles.Kind)!);}return d;}
    [TestMethod]
    public void ReviewGridShowsPhasesEditsApproveRowsAndGatesFinalApproval()
    {
        var(f,input,debt)=Setup();using(f){var cycle=Through(f,input,6,review:false);string id=cycle.S("id");
        var view=f.Store.Read(c=>PayCycles.ReviewView(c,PayFixture.Maker,id,"",""));Assert.AreEqual(2m,view["totals"]!.AsObject().N("riders"));Assert.AreEqual(0m,view["totals"]!.AsObject().N("approved"));
        var row=view["rows"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("rider")==Rider(1));var item=row["items"]!.AsArray().OfType<JsonObject>().Single(i=>i.S("key")=="debt:"+debt);
        Assert.AreEqual(1m,item.N("phase"),"Advance is a phase-1 consumption deduction (BR §29)");Assert.AreEqual(-15m,item.N("amount"));Assert.AreEqual(15m,item.N("max"));Assert.IsTrue(item.B("editable"));
        Assert.AreEqual(row.N("afterCommission")+row.N("phase1"),row.N("preliminaryDue"));Assert.AreEqual(row.N("preliminaryDue")+row.N("phase2")+row.N("phase3"),row.N("net"));
        Assert.ThrowsExactly<ApiError>(()=>Advance(f,id,7,true),"final approval waits for the review");
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,Rider(1),new(){["action"]="edit",["key"]="debt:"+debt,["amount"]=16,["reason"]="more than due"})));
        decimal net=row.N("net");var edited=f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,Rider(1),new(){["action"]="edit",["key"]="debt:"+debt,["amount"]=5,["reason"]="اتفاق مع المندوب"}));
        Assert.AreEqual("approved",edited.S("review"));Assert.AreEqual(net+10,edited.N("net"));Assert.AreEqual(1,edited["edits"]!.AsArray().Count);
        var adjusted=f.Store.Write((c,tx)=>PayCycles.ReviewRider(c,tx,PayFixture.Maker,id,Rider(2),new(){["action"]="adjust",["amount"]=20,["reason"]="حافز إضافي"}));
        Assert.AreEqual("approved",adjusted.S("review"));Assert.IsTrue(adjusted["items"]!.AsArray().OfType<JsonObject>().Any(i=>i.N("phase")==3&&i.N("amount")==20));
        var all=f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,id));Assert.AreEqual(0m,all.N("approved"));Assert.AreEqual(0m,all.N("skipped"));
        Assert.AreEqual("approved",Advance(f,id,7,true).S("status"));Assert.AreEqual(45m,f.Store.Read(c=>Store.Get(c,debt,"debt")!.N("remainingBalance")),"the edited installment is what gets deducted");
        var lines=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==id).SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).ToList());
        Assert.AreEqual(20m,lines.Where(l=>l.S("accountCode")=="512122").Sum(l=>l.N("debit")),"a review increase is Dr 512122 (BR §29)");
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,PayFixture.Maker,id)),"no review after final approval");}
    }
    [TestMethod]
    public void TwoAppsSharedAccountCompanyCostAndOneMonthlyDebtReconcileWithoutMutatingPreviews()
    {
        var (f,input,debt)=Setup();using(f){string before=f.Store.Read(c=>Store.Get(c,debt,"debt")!.ToJsonString());var cycle=Through(f,input);Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,debt,"debt")!.ToJsonString()));Assert.AreEqual(2,cycle["riders"]!.AsArray().Count);Assert.AreEqual(1,cycle["deductions"]!.AsArray().Count);Assert.AreEqual(175m,cycle["riders"]!.AsArray().Single(r=>r.S("rider")==Rider(1))!.N("net"));
        var lines=f.Store.Read(c=>PayCycles.Lines(c,null,cycle.S("id")));Assert.AreEqual(3,lines.Count);Assert.AreEqual(10m,lines.Single(l=>l.S("rider")==Rider(1)&&l.S("app")=="keeta").N("incentives"));Assert.AreEqual(20m,lines.Single(l=>l.S("rider")==Rider(2)).N("orders"));
        Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),7));var approved=Advance(f,cycle.S("id"),7,true);Assert.AreEqual("approved",approved.S("status"));Assert.AreEqual(35m,f.Store.Read(c=>Store.Get(c,debt,"debt")!.N("remainingBalance")));int journals=f.Store.Read(c=>Store.List(c,"journal").Count);Advance(f,cycle.S("id"),7,true);Assert.AreEqual(journals,f.Store.Read(c=>Store.List(c,"journal").Count));
        var posted=f.Store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==cycle.S("id")).ToList());Assert.AreEqual(2,posted.Count);foreach(var j in posted){Assert.AreEqual(j.N("totalDebit"),j.N("totalCredit"));Assert.IsTrue(j["lines"]!.AsArray().All(l=>l.S("chart")=="target"));}var all=posted.SelectMany(j=>j["lines"]!.AsArray().OfType<JsonObject>()).ToList();Assert.IsFalse(all.Any(l=>l.S("accountCode") is "2110" or "1241" or "4102"));Assert.AreEqual(30m,all.Where(l=>l.S("accountCode")=="526402").Sum(l=>l.N("debit")));Assert.AreEqual(385m,all.Where(l=>l.S("accountCode")=="212103").Sum(l=>l.N("credit")-l.N("debit")));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>{Store.Save(c,"run",new(){["id"]="synthetic-duplicate-run",["iqama"]=Rider(1),["period"]="2026-10",["approvedAt"]="2026-11-01"},tx);return true;}));}
    }
    [TestMethod]
    public void SourceMutationAndSkippedStagesAreRefusedAndDraftCanBeCancelled()
    {var(f,input,_)=Setup(false);using(f){var cycle=Create(f,input);Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),2));f.Store.Write((c,tx)=>{Store.Save(c,"adjustment",new(){["id"]="synthetic-later-adjustment",["iqama"]=Rider(1),["period"]="2026-10",["amount"]=-1},tx);return true;});Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),1));f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["action"]="cancel"}));Assert.AreEqual("draft",Create(f,input).S("status"));}}
    [TestMethod]
    public void RegisteredOwnerCannotReplaceAnActualOperatorAndGateOffPreventsAllWrites()
    {var(f,input,_)=Setup(false);using(f){var cycle=Through(f,input);f.Store.Write((c,tx)=>{PlatformPosting.Set(c,tx,PayFixture.Maker,"keeta",new(){["enabled"]=false});return true;});Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),7,true));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));}
     var(g,other,_d)=Setup(false);using(g){other["invoices"]![0]!["accounts"]![0]!["riders"]![0]!["rider"]=Rider(3);var d=Create(g,other);Advance(g,d.S("id"),1);Advance(g,d.S("id"),2);Assert.ThrowsExactly<ApiError>(()=>Advance(g,d.S("id"),3));}}
    [TestMethod]
    public void NegativeNetNeedsDecisionAndNeverCreatesDebt()
    {var(f,input,_)=Setup(false);using(f){var data=PayFixture.Scheme("monthly_commission");data["parameters"]!["categories"]!["sponsored_private"]!["prorate"]=false;var scheme=f.Draft("pay_scheme","hs",data,"negative");f.Approve("pay_scheme","hs",scheme);f.Store.Write((c,tx)=>{var choice=Store.Get(c,"synthetic-choice-hs1","pay_assignment",tx)!;choice["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",choice,tx);return true;});var cycle=Through(f,input,4);Assert.AreEqual("needs_decision",cycle.S("status"));Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),5));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));}}
    [TestMethod]
    public void IndependentReversalAppendsJournalsRestoresDebtAndKeepsOriginalBodies()
    {var(f,input,debt)=Setup();using(f){var before=f.Store.Read(c=>Store.Get(c,debt,"debt")!.Clone());var cycle=Through(f,input);Advance(f,cycle.S("id"),7,true);var journals=f.Store.Read(c=>Store.List(c,"journal").ToDictionary(j=>j.S("id"),j=>j.ToJsonString()));f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["action"]="reversal_request",["period"]="2026-11",["note"]="Synthetic corrected source"}));Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Maker,cycle.S("id"),new(){["action"]="reversal_approve"})));var reverse=f.Store.Write((c,tx)=>PayCycles.Advance(c,tx,PayFixture.Checker,cycle.S("id"),new(){["action"]="reversal_approve"}));Assert.AreEqual("reversed",reverse.S("status"));Assert.IsTrue(JsonNode.DeepEquals(before,f.Store.Read(c=>Store.Get(c,debt,"debt"))));foreach(var old in journals)Assert.AreEqual(old.Value,f.Store.Read(c=>Store.Get(c,old.Key,"journal")!.ToJsonString()));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"ledger_reversal").Count));}}
    [TestMethod]
    public void MissingDebtOpeningRollsBackApprovalAndEveryDebtChange()
    {var(f,input,debt)=Setup();using(f){f.Store.Write((c,tx)=>{var d=Store.Get(c,debt,"debt",tx)!;d["remainingBalance"]=500;d["monthlyInstallment"]=100;Store.Save(c,"debt",d,tx);return true;});var before=f.Store.Read(c=>Store.Get(c,debt,"debt")!.ToJsonString());var cycle=Through(f,input);Assert.ThrowsExactly<ApiError>(()=>Advance(f,cycle.S("id"),7,true));Assert.AreEqual(before,f.Store.Read(c=>Store.Get(c,debt,"debt")!.ToJsonString()));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));Assert.AreEqual("review",f.Store.Read(c=>Store.Get(c,cycle.S("id"),PayCycles.Kind)!.S("status")));Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"ledger").Count));}}
    [TestMethod]
    public void RentIsChargedOnceAcrossAppsAndActualClosingTargetLiabilityFeedsBankPreview()
    {var(f,input,_)=Setup(false);using(f){foreach(var invoice in input["invoices"]!.AsArray())foreach(var share in invoice!["accounts"]![0]!["riders"]!.AsArray())share!["category"]="sponsored_company_bike";
        foreach(string app in new[]{"keeta","hs"}){var data=PayFixture.Scheme("percent_monthly_with_vehicle");var scheme=f.Approve("pay_scheme",app,f.Draft("pay_scheme",app,data,"vehicle"));f.Store.Write((c,tx)=>{foreach(int i in new[]{1,2}){var choice=Store.Get(c,"synthetic-choice-"+app+i,"pay_assignment",tx)!;choice["schemeId"]=scheme.S("id");Store.Save(c,"pay_assignment",choice,tx);}return true;});}
        f.Store.Write((c,tx)=>{Store.Save(c,"vehicle",new(){["id"]="synthetic-bike",["vehicleType"]=1,["ownershipType"]=1},tx);foreach(int i in new[]{1,2})Store.Save(c,"vehicle_assignment",new(){["id"]="synthetic-bike-assignment-"+i,["vehicleId"]="synthetic-bike",["riderId"]=Rider(i),["startedAt"]="2026-10-01T00:00:00+03:00",["endedAt"]="2026-10-02T00:00:00+03:00"},tx);return true;});
        var cycle=Through(f,input);var lines=f.Store.Read(c=>PayCycles.Lines(c,null,cycle.S("id")));Assert.AreEqual(26.67m,lines.Where(l=>l.S("rider")==Rider(1)).Sum(l=>l.N("allocatedVehicleCharge")));Assert.AreEqual(162.33m,cycle["riders"]!.AsArray().Single(r=>r.S("rider")==Rider(1))!.N("net"));Advance(f,cycle.S("id"),7,true);
        var preview=f.Store.Read(c=>BankPayrollFiles.Preview(c,PayFixture.Maker,new(){["sponsorId"]=input.S("companyId"),["period"]="2026-10"}));Assert.AreEqual("posted_2110_212103_closing_payable",preview.S("basis"));Assert.AreEqual(162.33m,preview["rows"]!.AsArray().Single(r=>r.S("iqama")==Rider(1))!.N("amount"));}}

    [TestMethod]
    public void NormalizedUploadStaysPendingUntilIndependentCycleApproval()
    {var(f,input,_)=Setup(false);using(f){foreach(var invoice in input["invoices"]!.AsArray().OfType<JsonObject>()){string app=invoice.S("app");invoice.Remove("invoiceId");var a=invoice["accounts"]![0]!;invoice["invoice"]=new JsonObject{["reference"]="synthetic-normalized-"+app,["sourceSha256"]=new string('A',64),["amountExclVat"]=a.N("baseGross")+a.N("incentives"),["vat"]=0,["deductionsTotal"]=app=="keeta"?30:0,["lines"]=new JsonArray(new JsonObject{["lineId"]=a.S("sourceLineId"),["accountId"]=a.S("accountId"),["baseGross"]=a.N("baseGross"),["incentives"]=a.N("incentives"),["orders"]=a.N("orders"),["deductionAmount"]=app=="keeta"?30:0})};}
    var cycle=Through(f,input);var sourceIds=cycle["invoices"]!.AsArray().Select(i=>i.S("invoiceId")).ToList();foreach(string id in sourceIds)Assert.AreEqual("cycle_pending",f.Store.Read(c=>Store.Get(c,id,"platform_invoice")!.S("approvalStatus")));Advance(f,cycle.S("id"),7,true);foreach(string id in sourceIds)Assert.AreEqual("approved",f.Store.Read(c=>Store.Get(c,id,"platform_invoice")!.S("approvalStatus")));}}

}
