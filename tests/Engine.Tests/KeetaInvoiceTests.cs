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
        Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>KeetaInvoices.Workflow.Approve(c,tx,Actor("checker"),id)));
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
}
