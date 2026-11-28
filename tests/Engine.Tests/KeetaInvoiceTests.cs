using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class KeetaInvoiceTests
{
    internal static string Company=>FinancialCore.Sponsors[0].Id;
    internal static ClaimsPrincipal Actor(string name="synthetic-maker")=>HungerStationInvoiceTests.Actor(name);
    internal static void Seed(Store store)=>store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account",["externalAccountId"]="SYN-K1",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
    internal static JsonObject Input()=>new(){["companyId"]=Company,["period"]="2026-10",["reference"]="SYNTHETIC-KEETA",["accountingRole"]="principal"};
    internal static byte[] Workbook(string deduction="0",string net="100",string inclusive="115",bool missingCache=false,bool multiple=false)
    {
        string[] headers=["المعرف","الاسم بالكامل","الطلبات المُسلمة","الإجمالي","الخصم","إجمالي المبلغ المستحق","صافي الراتب","الايبان"];
        string[] row=["SYN-K1","SYNTHETIC_ACCOUNT_HOLDER","10",missingCache?"=NOCACHE":net,deduction,inclusive,"99999","SYNTHETIC_IGNORED_IBAN"];
        return DebtSheetReviewTests.Book(multiple?[("Synthetic",[headers,row]),("Second",[headers,row])]:[("Synthetic",[headers,row])]);
    }
    internal static JsonObject Commit(Store store,byte[] bytes,JsonObject? input=null)
    {
        input??=Input();var preview=store.Read(c=>KeetaInvoices.Preview(c,Actor(),input,bytes));
        var confirmed=input.Clone();confirmed["rows"]=preview["rows"]!.DeepClone();confirmed["fingerprint"]=preview.S("fingerprint");confirmed["confirm"]=true;
        return store.Write((c,tx)=>KeetaInvoices.Commit(c,tx,Actor(),confirmed,bytes));
    }
    [TestMethod]
    public void DiscardedUploadCanBeReplacedButPendingCycleCannotBeDiscarded()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);var bytes=Workbook();var first=Commit(f.Store,bytes);string id=first.S("id");
        f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,Actor(),"keeta",id,new(){["reason"]="Replace incorrect upload"}));
        var second=Commit(f.Store,bytes);Assert.AreNotEqual(id,second.S("id"));Assert.AreEqual(2,f.Store.Read(c=>Store.List(c,"platform_invoice_line").Count));
        f.Store.Write((c,tx)=>{Store.Save(c,PayCycles.Kind,new(){["id"]="synthetic-pending-cycle",["status"]="draft",["invoices"]=new JsonArray(new JsonObject{["invoiceId"]=second.S("id")})},tx);return true;});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>InvoiceDiscard.Discard(c,tx,Actor(),"keeta",second.S("id"),new(){["reason"]="Cycle protects its source"})));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Approve(c,tx,Actor("checker"),id)));
    }
    [TestMethod]
    public void InvoiceFinalTotalsIgnoreAppendedLocalPayrollAndBindFileActorAndCompanyState()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);var bytes=Workbook();var input=Input();var preview=f.Store.Read(c=>KeetaInvoices.Preview(c,Actor(),input,bytes));
        Assert.AreEqual(100m,preview["rows"]![0]!.N("amountExclVat"));Assert.AreEqual(15m,preview["rows"]![0]!.N("vat"));
        Assert.DoesNotContain("SYNTHETIC_ACCOUNT_HOLDER",preview.ToJsonString());Assert.DoesNotContain("SYNTHETIC_IGNORED_IBAN",preview.ToJsonString());
        var forged=input.Clone();forged["rows"]=preview["rows"]!.DeepClone();forged["fingerprint"]=preview.S("fingerprint");forged["confirm"]=true;
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Commit(c,tx,Actor("other"),forged,bytes)));
        forged["rows"]![0]!["amountExclVat"]=1;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Commit(c,tx,Actor(),forged,bytes)));
        var invoice=Commit(f.Store,bytes);Assert.AreEqual("pending",invoice.S("approvalStatus"));Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>KeetaInvoices.Preview(c,Actor(),Input(),bytes)));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"journal").Count));
    }
    [TestMethod]
    public void ClassifiedInvoiceNeedsIndependentApprovalAndFormalDisputesBlockPosting()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);var invoice=Commit(f.Store,Workbook("10"));string id=invoice.S("id");
        var line=f.Store.Read(c=>KeetaInvoices.Workflow.Lines(c,null,id).Single());Assert.AreEqual(110m,line.N("baseGross"));Assert.AreEqual(100m,line.N("invoiced"));Assert.AreEqual(10m,line.N("deductionAmount"));
        // Account-row penalty defaults to account_level (invoice final); checker can approve without a forced classify.
        Assert.AreEqual("account_level",(line["deductions"] as JsonArray)!.OfType<JsonObject>().Single(d=>d.S("category")=="penalty").S("level"));
        Assert.IsTrue(f.Store.Read(c=>KeetaInvoices.Workflow.View(c,null,Actor("checker"),invoice))["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        // Override to company for principal posting path.
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Classify(c,tx,Actor(),id,new(){["lineId"]=line.S("id"),["category"]="penalty",["level"]="company_level",["expenseAccount"]="512101"}));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Approve(c,tx,Actor(),id)));
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Approve(c,tx,Actor("checker"),id));
        f.Store.Write((c,tx)=>PlatformPosting.Set(c,tx,Actor(),"keeta",new(){["confirm"]=true,["enabled"]=true,["reason"]="Synthetic gate"}));
        var dispute=f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Dispute(c,tx,Actor(),id,new(){["reason"]="Synthetic formal dispute"}));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Post(c,tx,Actor("checker"),id)));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Dispute(c,tx,Actor(),id,new(){["reason"]="Synthetic same actor",["disputeId"]=dispute.S("id")},true)));
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Dispute(c,tx,Actor("checker"),id,new(){["reason"]="Synthetic resolution",["disputeId"]=dispute.S("id")},true));
        var posted=f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Post(c,tx,Actor("checker"),id));var journal=f.Store.Read(c=>Store.Get(c,posted.S("journalEntryId"),"journal"))!;
        Assert.AreEqual(journal.N("totalDebit"),journal.N("totalCredit"));Assert.IsTrue(journal["lines"]!.AsArray().Any(l=>l.S("accountCode")==PayPlatforms.For("keeta").Receivable));
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Post(c,tx,Actor("checker"),id));Assert.AreEqual(1,f.Store.Read(c=>Store.List(c,"journal").Count));
        Assert.AreEqual(0,f.Store.Read(c=>Store.List(c,"debt").Count));Assert.AreEqual("Keeta",f.Store.Read(c=>Store.List(c,PlatformDeductions.Kind).Single()).S("platform"));
    }
    /// <summary>KAPP1 / r8-keeta-flow: browser-shaped upload (penalty on user + auto-detected company adjustment). After maker classifies only supplier_adjustment, checker view must include approve.</summary>
    [TestMethod]
    public void BrowserShapedDefaultOnUserAllowsCheckerApproveAfterCompanyAdjustmentClassify()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-browser-acc",["externalAccountId"]="998877",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        // Same shape as web/tests/r8-keeta-flow.mjs syntheticKeetaInvoice (row 2 account + row 3 blank-account company adj).
        string[] headers=["المعرف","الطلبات المُسلمة","الخصم","الإجمالي","إجمالي المبلغ المستحق"];
        string[] captain=["998877","210","-150","4200","4830"];
        string[] companyAdj=["","0","0","-80","-92"];
        byte[] bytes=DebtSheetReviewTests.Book([("KeetaSynthetic",[headers,captain,companyAdj])]);
        var input=Input();var preview=f.Store.Read(c=>KeetaInvoices.Preview(c,Actor(),input,bytes));
        Assert.IsTrue(preview["rows"]![0]!["detectedCompanyRows"]!.AsArray().Any(x=>x!.GetValue<int>()==3),"blank-account company row must auto-detect");
        var confirmed=input.Clone();confirmed["rows"]=preview["rows"]!.DeepClone();confirmed["fingerprint"]=preview.S("fingerprint");confirmed["confirm"]=true;
        var invoice=f.Store.Write((c,tx)=>KeetaInvoices.Commit(c,tx,Actor(),confirmed,bytes));string id=invoice.S("id");
        var lines=f.Store.Read(c=>KeetaInvoices.Workflow.Lines(c,null,id));
        Assert.AreEqual(2,lines.Count);
        var accountLine=lines.Single(l=>l.S("lineType")=="account");
        var companyLine=lines.Single(l=>l.S("lineType")=="company");
        Assert.AreEqual("account_level",(accountLine["deductions"] as JsonArray)!.OfType<JsonObject>().Single(d=>d.S("category")=="penalty").S("level"));
        Assert.AreEqual("unclassified",(companyLine["deductions"] as JsonArray)!.OfType<JsonObject>().Single(d=>d.S("category")=="supplier_adjustment").S("level"));
        // Before company adjustment classify: checker blocked (unclassified supplier_adjustment).
        var blocked=f.Store.Read(c=>KeetaInvoices.Workflow.View(c,null,Actor("synthetic-checker"),invoice));
        Assert.IsFalse(blocked["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"),string.Join("|",blocked["approvalBlockers"]!.AsArray()));
        // UI sends company_level + expense account for supplier_adjustment (same as r8-keeta-flow).
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Classify(c,tx,Actor(),id,new(){["lineId"]=companyLine.S("id"),["category"]="supplier_adjustment",["level"]="company_level",["expenseAccount"]="512101"}));
        var maker=f.Store.Read(c=>KeetaInvoices.Workflow.View(c,null,Actor(),invoice));
        Assert.IsFalse(maker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"));
        StringAssert.Contains(string.Join("|",maker["approvalBlockers"]!.AsArray()),"مستخدم آخر");
        var checker=f.Store.Read(c=>KeetaInvoices.Workflow.View(c,null,Actor("synthetic-checker"),invoice));
        Assert.IsTrue(checker["allowedActions"]!.AsArray().Any(a=>a!.ToString()=="approve"),string.Join("|",checker["approvalBlockers"]!.AsArray()));
        f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Approve(c,tx,Actor("synthetic-checker"),id));
        var after=f.Store.Read(c=>KeetaInvoices.Workflow.View(c,null,Actor("synthetic-checker"),KeetaInvoices.Workflow.Get(c,null,Actor("synthetic-checker"),id)));
        Assert.AreEqual("approved",after.S("approvalStatus"));
    }
    [TestMethod]
    public void MissingCachesMultipleSheetsUnknownAccountsAndScopeChangesFailClosed()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),Workbook(missingCache:true))));
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),Workbook(multiple:true))));
        var selected=Input();selected["sheetName"]="Synthetic";Assert.AreEqual(100m,f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),selected,Workbook(multiple:true))).N("amountExclVat"));
        var scoped=UserAccess.Principal(new JsonObject{["username"]="synthetic-scoped",["role"]="FinancialAccountant",["permissions"]=new JsonArray("pay.manage"),["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[1].Id),["platforms"]=new JsonArray("Keeta")}});
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>KeetaInvoices.Parse(c,null,scoped,Input(),Workbook())));
        var input=Input();var bytes=Workbook();var preview=f.Store.Read(c=>KeetaInvoices.Preview(c,Actor(),input,bytes));
        f.Store.Write((c,tx)=>{var account=Store.Get(c,"synthetic-keeta-account","platform_account",tx)!;account["companyId"]=FinancialCore.Sponsors[1].Id;Store.Save(c,"platform_account",account,tx);return true;});
        input["rows"]=preview["rows"]!.DeepClone();input["fingerprint"]=preview.S("fingerprint");input["confirm"]=true;Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Commit(c,tx,Actor(),input,bytes)));
    }
    internal static byte[] OctoberLayoutWorkbook(bool withPositiveCompany=false,bool withRiderCredit=false,string? partnerTax=null,string? partnerDue=null)
    {
        // Real October rider sheet headers (diacritics + driver id + due ex-VAT only; no bare net).
        string[] headers=["معرف الشريك","اسم الشريك","دورة الفوترة","معرّف سائق التوصيل","اسم سائق التوصيل","صالح","السبب","أيام الاتصال-صالحة","ساعات الاتصال اليومي-صالحة","ساعات الاتصال اليومي خلال وقت الذروة-صالحة","الطلبات المُسلمة","مسافة التوصيل","الخصم","تعويض عن تلف الطعام","رسوم خدمة التسجيل","خصومات المعدات","تعديل آخر","خصم TGA (بدون ضريبة القيمة المضافة).","إجمالي المبلغ المستحق"];
        string[] title=["عنوان وهمي","","","","","","","","","","","","","","","","","",""];
        string[] row=["999","SYN_PARTNER","أكتوبر 2026","SYN-K1","SYNTHETIC_DRIVER","صالح","سبب آخر","-","-","-","10","1km","0","0","0","0","0","0","\u200f100.00\xa0ر.س.\u200f"];
        var riderRows=new List<string[]>{title,headers,row};
        decimal riderDueSum=100m;
        if(withRiderCredit)
        {
            riderRows.Add(["999","SYN_PARTNER","أكتوبر 2026","SYN-K2","SYNTHETIC_DRIVER_2","-","-","-","-","-","0","-","0","0","0","0","0","0","\u200f-10.00\xa0ر.س.\u200f"]);
            riderDueSum+=-10m;
        }
        else
        {
            riderRows.Add(["999","SYN_PARTNER","أكتوبر 2026","SYN-K2","SYNTHETIC_DRIVER_2","-","-","-","-","-","-","-","0","0","0","0","0","0","\u200f15.00\xa0ر.س.\u200f"]);
            riderDueSum+=15m;
        }
        if(withPositiveCompany)
        {
            riderRows.Add(["999","SYN_PARTNER","أكتوبر 2026","","","-","-","-","-","-","0","-","0","0","0","0","5","0","\u200f20.00\xa0ر.س.\u200f"]);
            riderDueSum+=20m;
        }
        // Partner sheet: tax + payable due (not مبلغ الفاتورة). Rider due sum + tax = partner due.
        decimal tax=partnerTax!=null?decimal.Parse(partnerTax,System.Globalization.CultureInfo.InvariantCulture):J.Money(riderDueSum*0.15m);
        decimal dueTotal=partnerDue!=null?decimal.Parse(partnerDue,System.Globalization.CultureInfo.InvariantCulture):J.Money(riderDueSum+tax);
        string[] partnerHeaders=["معرّف الفاتورة","معرف الشريك","اسم الشريك","مبلغ الضريبة","مبلغ الفاتورة","إجمالي المبلغ المستحق"];
        string[] partnerRow=["1","999","SYN_PARTNER",tax.ToString(System.Globalization.CultureInfo.InvariantCulture),"999999.99",dueTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        string[] detailHeaders=["معرف الشريك","معرّف سائق التوصيل","إجمالي المبلغ المستحق"];
        string[] detailRow=["999","SYN-K1","10"];
        return DebtSheetReviewTests.Book([
            ("تفاصيل الشركاء",[partnerHeaders,partnerRow]),
            ("تفاصيل سائق التوصيل",riderRows.ToArray()),
            ("تفاصيل طلب السائق",[detailHeaders,detailRow])
        ]);
    }
    [TestMethod]
    public void OctoberUnlinkedUserWithNoOrdersIsKeptAsAccountantLine()
    {
        // Ghazal 11-28 row 21: a user/ID with only a carried reward, no account in our system and no orders → accepted, flagged.
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        var parsed=f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),OctoberLayoutWorkbook()));
        var lines=parsed["lines"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(2,lines.Count);
        var unlinked=lines.Single(l=>l.B("accountantReview"));
        Assert.AreEqual("",unlinked.S("platformAccountId"));
        Assert.AreEqual("company",unlinked.S("lineType"));
        Assert.AreEqual(15m,unlinked.N("invoiced"));
        Assert.AreEqual(115m,parsed.N("amountExclVat"));
    }
    [TestMethod]
    public void OctoberHeaderAliasesFindHeaderRowAndParseCurrencyDueWithoutBareNet()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account-2",["externalAccountId"]="SYN-K2",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        var parsed=f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),OctoberLayoutWorkbook()));
        Assert.AreEqual("تفاصيل سائق التوصيل",parsed.S("sourceSheet"));
        Assert.AreEqual(2,parsed["lines"]!.AsArray().Count);
        Assert.AreEqual(115m,parsed.N("amountExclVat"));
        Assert.AreEqual(17.25m,parsed.N("vat"));
        Assert.AreEqual(0m,parsed["lines"]![0]!.N("vat"));
        Assert.AreEqual(100m,parsed["lines"]![0]!.N("invoiced"));
        Assert.AreEqual(0m,parsed["lines"]![1]!.N("orders"));
        Assert.DoesNotContain("SYNTHETIC_DRIVER",parsed.ToJsonString());
    }
    [TestMethod]
    public void OctoberPositiveCompanyRowAndRiderCreditStayInsidePartnerTotal()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account-2",["externalAccountId"]="SYN-K2",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        // Auto-detect blank-account company row — no manual companyRows required.
        var bytes=OctoberLayoutWorkbook(withPositiveCompany:true,withRiderCredit:true);
        var parsed=f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),bytes));
        Assert.AreEqual(3,parsed["lines"]!.AsArray().Count);
        Assert.IsTrue(parsed["detectedCompanyRows"]!.AsArray().Any(x=>x!.GetValue<int>()==5));
        var company=parsed["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("lineType")=="company");
        Assert.AreEqual(20m,company.N("invoiced"));Assert.AreEqual(20m,company.N("baseGross"));Assert.AreEqual(0m,company.N("deductionAmount"));
        Assert.IsTrue(company.B("companyAdjustmentDetected"));
        var credit=parsed["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("lineType")=="account"&&l.N("invoiced")<0);
        Assert.AreEqual(-10m,credit.N("invoiced"));Assert.AreEqual(0m,credit.N("orders"));
        // 100 + (-10) + 20 = 110; tax 16.50; partner due 126.50
        Assert.AreEqual(16.50m,parsed.N("vat"));
        Assert.AreEqual(110m,parsed["lines"]!.AsArray().OfType<JsonObject>().Sum(l=>l.N("invoiced")));
    }
    [TestMethod]
    public void OctoberManualCompanyRowsOverrideStillAccepted()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account-2",["externalAccountId"]="SYN-K2",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        var input=Input();input["companyRows"]=new JsonArray(5);
        var parsed=f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),input,OctoberLayoutWorkbook(withPositiveCompany:true)));
        Assert.AreEqual(1,parsed["lines"]!.AsArray().OfType<JsonObject>().Count(l=>l.S("lineType")=="company"));
    }
    [TestMethod]
    public void OctoberPartnerResidualBecomesCompanyAdjustmentLine()
    {
        // Albwaba-shaped gap: rider dues + tax leave 100 SAR only on the partner sheet (no rider row).
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account-2",["externalAccountId"]="SYN-K2",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        // Rider dues 115; tax 17.25; partner due inflated by +100 → residual company line.
        var parsed=f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),OctoberLayoutWorkbook(partnerTax:"17.25",partnerDue:"232.25")));
        Assert.AreEqual(100m,parsed.N("partnerResidual"));
        var residual=parsed["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.B("partnerResidual"));
        Assert.AreEqual("company",residual.S("lineType"));
        Assert.AreEqual(100m,residual.N("invoiced"));
        Assert.AreEqual(0,residual.N("sourceRow"));
        Assert.AreEqual(215m,parsed["lines"]!.AsArray().OfType<JsonObject>().Sum(l=>l.N("invoiced")));
    }
    [TestMethod]
    public void OctoberNegativePartnerTaxAbortsInArabic()
    {
        using var f=new HungerStationInvoiceTests.Fixture();Seed(f.Store);
        f.Store.Write((c,tx)=>{Store.Save(c,"platform_account",new(){["id"]="synthetic-keeta-account-2",["externalAccountId"]="SYN-K2",["platformCode"]="keeta",["companyId"]=Company},tx);return true;});
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>KeetaInvoices.Parse(c,null,Actor(),Input(),OctoberLayoutWorkbook(partnerTax:"-1",partnerDue:"114"))));
        Assert.Contains("ضريبة",err.Message);
    }
}
