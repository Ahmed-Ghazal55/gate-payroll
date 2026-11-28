using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>NFLOW1: invoice self-approve via approvals.approve_own / pay_approvals bypass covering invoices;
/// deduction defaults to account_level; dues-split stage; no-ops are accountant lines.</summary>
[TestClass]
public sealed class NFlow1InvoiceApprovalTests
{
    static string Company=>FinancialCore.Sponsors[0].Id;
    static ClaimsPrincipal Actor(string name="synthetic-maker")=>HungerStationInvoiceTests.Actor(name);
    static ClaimsPrincipal MakerWithApproveOwn()=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-maker",
        ["role"]="Admin",
        ["department"]="FIN",
        ["rankLevel"]=5,
        ["customPermissions"]=new JsonArray(AccessPolicy.ApproveOwnPermission,"pay.approve","pay.manage","pay.view")
    });

    sealed class Fixture:IDisposable
    {
        readonly HungerStationInvoiceTests.Fixture inner=new();
        public Store Store=>inner.Store;
        public Fixture()=>Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="ninja",["companyId"]=Company},tx);Store.Save(c,"ninja_account",new(){["id"]="synthetic-ninja-account",["platformAccountId"]="synthetic-ninja-platform",["CaptainId"]="SYN-A"},tx);return true;});
        public JsonObject Commit(byte[] bytes)
        {
            var input=new JsonObject{["period"]="2026-10",["companyId"]=Company,["reference"]="SYNTHETIC-NINJA-INVOICE"};
            var preview=Store.Read(c=>NinjaInvoices.Preview(c,Actor(),input,bytes));
            input["rows"]=preview["rows"]!.DeepClone();input["fingerprint"]=preview.S("fingerprint");input["confirm"]=true;
            return Store.Write((c,tx)=>NinjaInvoices.Commit(c,tx,Actor(),input,bytes));
        }
        public void Dispose()=>inner.Dispose();
    }

    [TestMethod]
    public void PayApprovalsBypassScopeCoversInvoiceApprovals()
    {
        Assert.IsTrue(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,AccessPolicy.ScopeInvoiceApprovals));
        Assert.IsTrue(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,"platform.invoice_approve"));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualBankFile),"money-out dual stays excluded");
    }

    [TestMethod]
    public void ApproveOwnShowsApproveOnNinjaInvoiceViewAndApproves()
    {
        using var f=new Fixture();
        f.Store.Write((c,tx)=>{using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_account'";d.ExecuteNonQuery();}Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="NINJA",["companyId"]=Company,["externalAccountId"]="SYN-A"},tx);return true;});
        var invoice=f.Commit(NinjaInvoiceImportTests.Workbook(penalty:50));
        var lines=f.Store.Read(c=>NinjaInvoices.Lines(c,null,invoice.S("id")));
        Assert.IsTrue(lines.SelectMany(l=>(l["deductions"] as JsonArray??[]).OfType<JsonObject>()).All(d=>d.S("level")=="account_level"));
        var maker=MakerWithApproveOwn();
        var view=f.Store.Read(c=>NinjaInvoices.View(c,null,maker,invoice));
        Assert.IsTrue(view["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"),string.Join("|",view["approvalBlockers"]!.AsArray()));
        var approved=f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,maker,invoice.S("id")));
        Assert.AreEqual("approved",approved.S("approvalStatus"));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="approval.approve_own"&&a.S("recordId")==invoice.S("id"))));
        var after=f.Store.Read(c=>NinjaInvoices.View(c,null,maker,approved));
        Assert.IsTrue(after["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="split_dues"));
    }

    [TestMethod]
    public void PayApprovalsBypassUnlocksInvoiceApproveForUploader()
    {
        using var f=new Fixture();
        f.Store.Write((c,tx)=>{using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_account'";d.ExecuteNonQuery();}Store.Save(c,"platform_account",new(){["id"]="synthetic-ninja-platform",["platformCode"]="NINJA",["companyId"]=Company,["externalAccountId"]="SYN-A"},tx);return true;});
        var invoice=f.Commit(NinjaInvoiceImportTests.Workbook(penalty:20));
        Assert.IsFalse(f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),invoice))["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        string today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd");
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="synthetic-maker-user",["username"]="synthetic-maker",["role"]="Admin",["department"]="FIN"},tx);AccessPolicy.GrantBypass(c,tx,Actor("synthetic-checker"),new(){["username"]="synthetic-maker",["from"]=today,["to"]=today,["reason"]="NFLOW1 pay scope covers invoice",["scope"]=AccessPolicy.ScopePayApprovals});return true;});
        var maker=f.Store.Read(c=>NinjaInvoices.View(c,null,Actor(),invoice));
        Assert.IsTrue(maker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"),string.Join("|",maker["approvalBlockers"]!.AsArray()));
        var approved=f.Store.Write((c,tx)=>NinjaInvoices.Approve(c,tx,Actor(),invoice.S("id")));
        Assert.AreEqual("approved",approved.S("approvalStatus"));
    }

    [TestMethod]
    public void DuesSplitPutsNoOpsOnAccountantLineNotBlocker()
    {
        using var f=PayReviewDraftTests.Setup();
        f.Store.Write((c,tx)=>
        {
            using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_daily'";d.ExecuteNonQuery();}
            Store.Save(c,"platform_account",new(){["id"]="synthetic-r8-noops",["companyId"]=PayReviewDraftTests.Company,["platformCode"]="ninja",["assignments"]=new JsonArray()},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-r8-noops-line",["invoiceId"]=PayReviewDraftTests.Invoice,["platformAccountId"]="synthetic-r8-noops",["lineType"]="account",["baseGross"]=10,["incentives"]=0,["orders"]=1,["deductionAmount"]=0,["deductions"]=new JsonArray(),["difference"]=0,["captainId"]="NOOPS"},tx);
            return true;
        });
        var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        Assert.IsTrue(draft["accountantLines"]!.AsArray().OfType<JsonObject>().Any(a=>a.S("kind")=="no_ops_account"));
        Assert.IsFalse(draft["blockers"]!.AsArray().OfType<JsonObject>().Any(b=>b.S("code")=="operations_missing"));
        var split=f.Store.Read(c=>PayDuesSplit.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        Assert.AreEqual("split_dues",split.S("stage"));
        Assert.IsTrue(split["counts"].N("users")>=1);
        Assert.IsTrue(split["accountantLines"]!.AsArray().Count>=1);
    }

    /// <summary>NFLOW1b FIX#4: multi-rider parts sum exactly to invoiceDue (incentives + company-level cuts).</summary>
    [TestMethod]
    public void DuesSplitPartsSumToInvoiceDueIncludingIncentivesAndCompanyCuts()
    {
        using var f=PayReviewDraftTests.Setup();
        const string riderB="2000000082";
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=riderB,["iqama"]=riderB,["sponsorId"]=PayReviewDraftTests.Company,["isDeliveryRider"]=true,["payCategory"]="sponsored_private"},tx);
            var account=Store.Get(c,PayReviewDraftTests.Account,"platform_account",tx)!;
            account["assignments"]=new JsonArray(
                new JsonObject{["riderId"]=PayReviewDraftTests.Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["PaymentModel"]="1",["NinjaRiderType"]="1"},
                new JsonObject{["riderId"]=riderB,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["PaymentModel"]="1",["NinjaRiderType"]="1"});
            Store.Save(c,"platform_account",account,tx);
            Store.Save(c,"platform_invoice_line",new()
            {
                ["id"]=PayReviewDraftTests.Line,["invoiceId"]=PayReviewDraftTests.Invoice,["platformAccountId"]=PayReviewDraftTests.Account,
                ["lineType"]="account",["baseGross"]=100,["incentives"]=20,["orders"]=10,
                ["deductionAmount"]=30,
                ["deductions"]=new JsonArray(
                    new JsonObject{["category"]="penalty",["amount"]=10,["level"]="account_level"},
                    new JsonObject{["category"]="company_fee",["amount"]=20,["level"]="company_level"}),
                ["difference"]=0,["captainId"]="SYN"
            },tx);
            using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_daily'";d.ExecuteNonQuery();}
            Store.Save(c,"ninja_daily",new(){["id"]="synthetic-r8-day-a",["companyId"]=PayReviewDraftTests.Company,["platformAccountId"]=PayReviewDraftTests.Account,["riderId"]=PayReviewDraftTests.Rider,["date"]="2026-10-01",["orders"]=6},tx);
            Store.Save(c,"ninja_daily",new(){["id"]="synthetic-r8-day-b",["companyId"]=PayReviewDraftTests.Company,["platformAccountId"]=PayReviewDraftTests.Account,["riderId"]=riderB,["date"]="2026-10-02",["orders"]=4},tx);
            return true;
        });
        var split=f.Store.Read(c=>PayDuesSplit.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        var account=split["accounts"]!.AsArray().OfType<JsonObject>().First(a=>a.S("accountId")==PayReviewDraftTests.Account);
        decimal invoiceDue=account.N("invoiceDue");
        Assert.AreEqual(90m,invoiceDue,"100 base + 20 incentives − 30 cuts");
        var riders=account["riders"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(2,riders.Count);
        Assert.AreEqual(invoiceDue,riders.Sum(r=>r.N("due")));
    }

    /// <summary>NFLOW1b/1d FIX#6: no-ops with exactly one assignment stays accountant line on split AND review draft (never auto-paid).</summary>
    [TestMethod]
    public void DuesSplitNoOpsWithSingleAssignmentIsAccountantLineNeverAutoPaid()
    {
        using var f=PayReviewDraftTests.Setup();
        f.Store.Write((c,tx)=>
        {
            using(var d=c.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM records WHERE kind='ninja_daily'";d.ExecuteNonQuery();}
            Store.Save(c,"platform_account",new()
            {
                ["id"]="synthetic-r8-noops-one",
                ["companyId"]=PayReviewDraftTests.Company,
                ["platformCode"]="ninja",
                ["assignments"]=new JsonArray(new JsonObject{["riderId"]=PayReviewDraftTests.Rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["PaymentModel"]="2"})
            },tx);
            Store.Save(c,"platform_invoice_line",new()
            {
                ["id"]="synthetic-r8-noops-one-line",["invoiceId"]=PayReviewDraftTests.Invoice,["platformAccountId"]="synthetic-r8-noops-one",
                ["lineType"]="account",["baseGross"]=50,["incentives"]=5,["orders"]=2,["deductionAmount"]=0,["deductions"]=new JsonArray(),["difference"]=0,["captainId"]="NOOPS1"
            },tx);
            var invoice=Store.Get(c,PayReviewDraftTests.Invoice,"platform_invoice",tx)!;
            invoice["amountExclVat"]=155;
            Store.Save(c,"platform_invoice",invoice,tx);
            return true;
        });
        var draft=f.Store.Read(c=>PayReviewDraft.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        Assert.IsFalse(draft["warnings"]!.AsArray().OfType<JsonObject>().Any(w=>w.S("code")=="operations_missing_using_assignment"));
        foreach(var acct in draft["invoices"]!.AsArray().OfType<JsonObject>().SelectMany(i=>(i["accounts"] as JsonArray??[]).OfType<JsonObject>()))
            Assert.AreEqual(0,acct["riders"]!.AsArray().Count,"Review draft must not pay a no-ops account to its sole assignment");
        Assert.IsTrue(draft["accountantLines"]!.AsArray().OfType<JsonObject>().Any(a=>a.S("kind")=="no_ops_account"&&a.S("sourceLineId")=="synthetic-r8-noops-one-line"&&a.N("assigneeCount")==1&&a.S("decisionDefault")=="hold_for_accountant"));
        Assert.IsTrue(draft["accountantLines"]!.AsArray().OfType<JsonObject>().Any(a=>a.S("kind")=="no_ops_account"&&a.S("sourceLineId")==PayReviewDraftTests.Line&&a.N("assigneeCount")==1));
        Assert.IsTrue(draft.B("canCreate"),draft["blockers"]!.ToJsonString());
        Assert.AreEqual(155m,draft["invoices"]!.AsArray().OfType<JsonObject>().SelectMany(i=>(i["accounts"] as JsonArray??[]).OfType<JsonObject>()).Sum(a=>a.N("baseGross")+a.N("incentives")));

        var split=f.Store.Read(c=>PayDuesSplit.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        var line=split["accountantLines"]!.AsArray().OfType<JsonObject>().First(a=>a.S("sourceLineId")=="synthetic-r8-noops-one-line");
        Assert.AreEqual("no_ops_account",line.S("kind"));
        Assert.AreEqual(1m,line.N("assigneeCount"));
        Assert.IsFalse(split["riderSplits"]!.AsArray().OfType<JsonObject>().Any(r=>r.S("accountId")=="synthetic-r8-noops-one"||r.B("fromAssignmentOnly")));
        var account=split["accounts"]!.AsArray().OfType<JsonObject>().First(a=>a.S("accountId")=="synthetic-r8-noops-one");
        Assert.AreEqual(0,account["riders"]!.AsArray().Count);
    }

    /// <summary>NFLOW1b FIX#7: payModel + ninjaRiderType from Omar payment method and internal scheme by kafala/vehicle.</summary>
    [TestMethod]
    public void DuesSplitAppliesPayModelFromOmarAndInternalSchemeByCategory()
    {
        using var f=PayReviewDraftTests.Setup();
        f.Store.Write((c,tx)=>
        {
            var account=Store.Get(c,PayReviewDraftTests.Account,"platform_account",tx)!;
            account["paymentModelCode"]="1";
            account["assignments"]=new JsonArray(new JsonObject
            {
                ["riderId"]=PayReviewDraftTests.Rider,
                ["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",
                ["PaymentModel"]="1",
                ["NinjaRiderType"]="3"
            });
            Store.Save(c,"platform_account",account,tx);
            return true;
        });
        var split=f.Store.Read(c=>PayDuesSplit.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company));
        var share=split["riderSplits"]!.AsArray().OfType<JsonObject>().First(r=>r.S("rider")==PayReviewDraftTests.Rider&&r.S("accountId")==PayReviewDraftTests.Account);
        Assert.AreEqual("PayPerOrder",share.S("omarPaymentMethod"));
        Assert.AreEqual("percentage",share.S("payModel"),"Setup scheme is percent_of_gross → percentage");
        Assert.AreEqual("percent_of_gross",share.S("schemeType"));
        Assert.AreEqual("InternalMotorcycle",share.S("ninjaRiderType"));
        Assert.AreEqual("sponsored_private",share.S("category"));
        Assert.IsFalse(string.IsNullOrEmpty(share.S("schemeId")));
        Assert.IsNotNull(share["schemeCell"]);
    }

    /// <summary>NFLOW1b FIX#8: HANDOFF must not record live-register suffixes or live trial counts.</summary>
    [TestMethod]
    public void HandoffHasNoLiveRegisterSuffixOrLiveCounts()
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
        string handoff=Path.Combine(root,"HANDOFF.md");
        Assert.IsTrue(File.Exists(handoff),handoff);
        string text=File.ReadAllText(handoff);
        StringAssert.DoesNotMatch(text,new System.Text.RegularExpressions.Regex(@"…\d{4}|BW\s*…|7015658094"));
        StringAssert.DoesNotMatch(text,new System.Text.RegularExpressions.Regex(@"users\s+\d+|riderSplits\s+\d+|fuel\s+\d+|custody\s+\d+|accountant\s+\d+|blockers\s+\d+"));
        StringAssert.DoesNotMatch(text,new System.Text.RegularExpressions.Regex(@"Local verify BW|gate_rel340: users"));
    }

    /// <summary>NFLOW1c FIX#4: live Map returns the same fuel list for three kind aliases — totals equal ERP source once.</summary>
    [TestMethod]
    public void DuesSplitFuelTotalsEqualErpSourceOnceWhenLiveOpsAliasesTriple()
    {
        using var f=PayReviewDraftTests.Setup();
        var erpFuel=new List<JsonObject>
        {
            new(){["id"]="synthetic-fuel-src-1",["riderId"]=PayReviewDraftTests.Rider,["liters"]=12.5m,["amount"]=40m},
            new(){["id"]="synthetic-fuel-src-2",["riderId"]=PayReviewDraftTests.Rider,["liters"]=7.5m,["amount"]=20m}
        };
        decimal expectedLiters=20m,expectedAmount=60m;
        var daily=f.Store.Read(c=>Store.List(c,"ninja_daily").Where(d=>d.S("companyId")==PayReviewDraftTests.Company).Select(d=>d.DeepClone()!.AsObject()).ToList());
        // Same wiring as PayDuesSplit.Map live path: one ERP list served for fuel_usage / fuel_consumption / fuel.
        Func<string,IEnumerable<JsonObject>> liveOps=kind=>kind switch
        {
            "fuel_usage" or "fuel_consumption" or "fuel"=>erpFuel,
            "ninja_daily"=>daily,
            "vehicle_assignment"=>[],
            "vehicle"=>[],
            _=>[]
        };
        var split=f.Store.Read(c=>PayDuesSplit.Build(c,null,PayFixture.Maker,"ninja","2026-10",PayReviewDraftTests.Company,liveOps:liveOps));
        var share=split["riderSplits"]!.AsArray().OfType<JsonObject>().First(r=>r.S("rider")==PayReviewDraftTests.Rider&&r.S("accountId")==PayReviewDraftTests.Account);
        var fuel=share["fuel"]!.AsObject();
        Assert.IsTrue(fuel.B("attributed"));
        Assert.AreEqual(2m,fuel.N("rows"),"rows must match ERP source count, not 6");
        Assert.AreEqual(expectedLiters,fuel.N("liters"));
        Assert.AreEqual(expectedAmount,fuel.N("amount"));
        Assert.AreEqual(2m,split["counts"]!.AsObject().N("fuelRows"));
    }
}
