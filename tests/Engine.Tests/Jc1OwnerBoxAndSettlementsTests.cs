using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class Jc1OwnerBoxAndSettlementsTests
{
    static ClaimsPrincipal Maker=>PayFixture.Maker;
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-jc1b-checker","Admin");

    [TestCleanup]
    public void Cleanup()=>ErpSettlementsApi.LiveBatchProvider=null;

    [TestMethod]
    public void OwnerExternalBox_OpeningFee_RequiresSecondPerson_AndPostsCompanyIncomeJournal()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string company=FinancialCore.Sponsors[0].Id;
        var tree=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker));
        Assert.IsFalse(tree.B("hitsCompanyBooks"));
        string leafId=tree["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode).S("id");
        var pending=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-10",["direction"]="in",["amount"]=200,
            ["sourceKind"]="jahez_account_opening_fee",["notes"]="synthetic-owner-box",["companyId"]=company
        }));
        Assert.AreEqual("pending",pending.S("status"));
        Assert.IsFalse(pending.B("hitsCompanyBooks"));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Maker,pending.S("id")))).Status);
        var posted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pending.S("id")));
        Assert.AreEqual("posted",posted.S("status"));
        Assert.IsFalse(posted.B("hitsCompanyBooks"));
        Assert.AreNotEqual("",posted.S("companyJournalId"));
        var lines=f.Store.Read(c=>Store.Get(c,posted.S("companyJournalId"),"journal")!["lines"]!.AsArray().OfType<JsonObject>().ToList());
        Assert.IsTrue(lines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.OtherIncomeOpeningFee&&l.N("credit")==200m));
        // BR §22.1 collected: Dr cash 111104, never accrued 112512 (would never clear while leaf cash stays off books).
        Assert.IsTrue(lines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.CashOpeningFee&&l.N("debit")==200m));
        Assert.IsFalse(lines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.AccruedOpeningFee));
        var bal=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).N("balance"));
        Assert.AreEqual(200m,bal);
    }

    [TestMethod]
    public void OwnerExternalBox_ApproveOwnBlocked_ReversePostsCompanyJournal_SecondReverseBlocked()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string company=FinancialCore.Sponsors[0].Id;
        string leafId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode).S("id"));
        var pending=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-12",["direction"]="in",["amount"]=200,
            ["sourceKind"]="jahez_account_opening_fee",["companyId"]=company,["notes"]="synthetic-jc1c-dual"
        }));
        // Same person with approvals.approve_own must still be blocked (money-out / owner_box).
        var makerWithOwn=UserAccess.Principal(new JsonObject
        {
            ["username"]=Maker.Identity!.Name!,["role"]="Admin",["department"]="FIN",["rankLevel"]=5,
            ["customPermissions"]=new JsonArray(AccessPolicy.ApproveOwnPermission,"treasury.jahez.manage","treasury.transfer")
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,makerWithOwn,pending.S("id")))).Status);
        var posted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pending.S("id")));
        Assert.AreNotEqual("",posted.S("companyJournalId"));
        var revPending=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,posted.S("id")));
        Assert.AreEqual("pending",revPending.S("status"));
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,posted.S("id")))).Status,"second reverse blocked while first pending/posted");
        var revPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,revPending.S("id")));
        Assert.AreNotEqual("",revPosted.S("companyJournalId"));
        var revJournal=f.Store.Read(c=>Store.Get(c,revPosted.S("companyJournalId"),"journal")!);
        Assert.AreEqual(posted.S("companyJournalId"),revJournal.S("reversesJournalId"));
        var origAfter=f.Store.Read(c=>
        {
            using var cmd=c.CreateCommand();
            cmd.CommandText=$"SELECT status FROM {SchemaSql.Q(c,"treasury","owner_box_entry")} WHERE id=@id";
            var p=cmd.CreateParameter();p.ParameterName="@id";p.Value=posted.S("id");cmd.Parameters.Add(p);
            return cmd.ExecuteScalar()?.ToString()??"";
        });
        Assert.AreEqual("reversed",origAfter);
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,posted.S("id")))).Status);
    }

    [TestMethod]
    public void OwnerExternalBox_PendingCountsInBalance_ConfirmBlockedOnArchivedLeaf()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1C_PENDING",["labelAr"]="ورقة رصيد معلّق",["sortOrder"]=95
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1C_PENDING").S("id");
        var pendIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-13",["direction"]="in",["amount"]=75,
            ["sourceKind"]="collection",["notes"]="synthetic-pending-bal"
        }));
        var afterPending=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker));
        var leaf=afterPending["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId);
        Assert.AreEqual(75m,leaf.N("balance"),"pending in must block zero-balance one-person archive");
        // JC1d: archive refused while any pending exists (cannot leave stuck movements on an archived leaf).
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId))).Status);
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pendIn.S("id")));
        var requested=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId));
        var reqLeaf=requested["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId);
        Assert.AreEqual("",reqLeaf.S("archivedAt"));
        Assert.AreNotEqual("",reqLeaf.S("archiveRequestedBy"));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmArchive(c,tx,Checker,leafId));
        // Confirm / post on archived leaf must refuse.
        var grown2=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1C_ARCH",["labelAr"]="ورقة مؤرشفة",["sortOrder"]=96
        }));
        string leaf2=grown2["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1C_ARCH").S("id");
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leaf2)); // zero posted → instant
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-13",["direction"]="in",["amount"]=10,
            ["sourceKind"]="collection"
        }))).Status);
    }

    [TestMethod]
    public void OwnerExternalBox_PendingOutDoesNotZeroBalance_CannotArchiveAlone()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string company=FinancialCore.Sponsors[0].Id;
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1D_POUT",["labelAr"]="ورقة خروج معلّق",["sortOrder"]=97
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1D_POUT").S("id");
        var pendIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-14",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1d-posted-in"
        }));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pendIn.S("id")));
        // Prepared out equal to posted balance must NOT make the leaf look empty (JC1d HIGH #1).
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-14",["direction"]="out",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1d-pending-out"
        }));
        var leaf=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId));
        Assert.AreEqual(100m,leaf.N("balance"),"pending out must not reduce archive/display balance");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId))).Status,
            "archive blocked while pending out exists");
        // Prepared reversal of a companion-journal entry likewise must not zero-balance archive.
        string feeLeaf=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode).S("id"));
        var feePend=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=feeLeaf,["occurredOn"]="2026-11-14",["direction"]="in",["amount"]=80,
            ["sourceKind"]="jahez_account_opening_fee",["companyId"]=company,["notes"]="synthetic-jc1d-fee"
        }));
        var feePosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,feePend.S("id")));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,feePosted.S("id")));
        var feeAfter=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==feeLeaf));
        Assert.AreEqual(80m,feeAfter.N("balance"),"pending reversal out must not zero the leaf");
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,feeLeaf))).Status);
    }

    [TestMethod]
    public void OwnerExternalBox_CustodyWalletTopup_PostsCompanyExpense_AndArchiveWithBalanceNeedsSecondPerson()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string company=FinancialCore.Sponsors[0].Id;
        string leafId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("code")==SchemaOwnerExternalBox.WalletTopupCollectionsCode).S("id"));
        // Seed posted rider-cash in so a custody OUT has available cash (JC1e: expense on out, not in).
        var cashIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-11",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-rider-cash"
        }));
        var cashPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,cashIn.S("id")));
        Assert.AreEqual("",cashPosted.S("companyJournalId"),"rider-cash in on wallet leaf has no company expense");
        var pending=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-11",["direction"]="out",["amount"]=50,
            ["sourceKind"]="custody_wallet_topup",["companyId"]=company,["notes"]="synthetic-custody"
        }));
        var posted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pending.S("id")));
        Assert.AreNotEqual("",posted.S("companyJournalId"));
        var jLines=f.Store.Read(c=>Store.Get(c,posted.S("companyJournalId"),"journal")!["lines"]!.AsArray().OfType<JsonObject>().ToList());
        Assert.IsTrue(jLines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.HungerWalletChargeExpense&&l.N("debit")==50m));
        Assert.IsTrue(jLines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.HungerWalletCustody&&l.N("credit")==50m));
        var balAfter=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("id")==leafId).N("balance"));
        Assert.AreEqual(50m,balAfter);

        var requested=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId));
        var leaf=requested["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("id")==leafId);
        Assert.AreEqual("",leaf.S("archivedAt"));
        Assert.AreNotEqual("",leaf.S("archiveRequestedBy"));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmArchive(c,tx,Maker,leafId))).Status);
        var archivedTree=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmArchive(c,tx,Checker,leafId));
        decimal rootBal=archivedTree["roots"]!.AsArray().OfType<JsonObject>().Single().N("balance");
        Assert.AreEqual(50m,rootBal,"archived leaf balance stays in parent rollup until reversed");
        var active=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker,includeArchived:false));
        Assert.IsFalse(active["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>().Any(ch=>ch.S("id")==leafId));
        Assert.AreEqual(50m,active["roots"]!.AsArray().OfType<JsonObject>().Single().N("balance"));
    }

    /// <summary>JC1e HIGH N1/#1: rider-cash IN on wallet leaf posts no expense; custody OUT does; custody+IN rejected.</summary>
    [TestMethod]
    public void OwnerExternalBox_WalletLeaf_RiderCashIn_NoExpense_CustodyOut_PostsExpense()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string company=FinancialCore.Sponsors[0].Id;
        string leafId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("code")==SchemaOwnerExternalBox.WalletTopupCollectionsCode).S("id"));
        // Custody source with direction IN must be rejected (expense belongs on OUT).
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-15",["direction"]="in",["amount"]=40,
            ["sourceKind"]="custody_wallet_topup",["companyId"]=company,["notes"]="synthetic-jc1e-bad-in"
        }))).Status);
        var cashIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-15",["direction"]="in",["amount"]=80,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-rider-in"
        }));
        var cashPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,cashIn.S("id")));
        Assert.AreEqual("",cashPosted.S("companyJournalId"),"rider-cash arrival must not book company expense");
        Assert.AreEqual(80m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("id")==leafId).N("balance")));
        var custodyOut=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-15",["direction"]="out",["amount"]=30,
            ["sourceKind"]="custody_wallet_topup",["companyId"]=company,["notes"]="synthetic-jc1e-custody-out"
        }));
        var outPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,custodyOut.S("id")));
        Assert.AreNotEqual("",outPosted.S("companyJournalId"));
        var lines=f.Store.Read(c=>Store.Get(c,outPosted.S("companyJournalId"),"journal")!["lines"]!.AsArray().OfType<JsonObject>().ToList());
        Assert.IsTrue(lines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.HungerWalletChargeExpense&&l.N("debit")==30m));
        Assert.IsTrue(lines.Any(l=>l.S("accountCode")==SchemaOwnerExternalBox.HungerWalletCustody&&l.N("credit")==30m));
        Assert.AreEqual(50m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(ch=>ch.S("id")==leafId).N("balance")));
    }

    /// <summary>JC1e #2: second pending/confirmed out cannot overdraw posted cash minus other pending outs.</summary>
    [TestMethod]
    public void OwnerExternalBox_PendingOutCapacity_BlocksOverdraw()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1E_CAP",["labelAr"]="ورقة سعة خروج",["sortOrder"]=98
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1E_CAP").S("id");
        var pendIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-16",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-cap-in"
        }));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pendIn.S("id")));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-16",["direction"]="out",["amount"]=70,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-cap-out1"
        }));
        // Display balance still shows posted 100 (pending out ignored for archive), but prepare of another 70 must fail.
        Assert.AreEqual(100m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).N("balance")));
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-16",["direction"]="out",["amount"]=70,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-cap-out2"
        }))).Status);
        // Fitting remainder 30 still allowed.
        var ok=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-16",["direction"]="out",["amount"]=30,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-cap-out3"
        }));
        Assert.AreEqual("pending",ok.S("status"));
    }

    /// <summary>JC1f HIGH #1: confirmed reversal must not double-count cash (skip reversed + count reversal).</summary>
    [TestMethod]
    public void OwnerExternalBox_ReversalDoesNotDoubleCountAvailableCash()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1F_REV",["labelAr"]="ورقة عكس مزدوج",["sortOrder"]=97
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1F_REV").S("id");
        var inPend=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-18",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-in"
        }));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,inPend.S("id")));
        var outPend=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-18",["direction"]="out",["amount"]=40,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-out"
        }));
        var outPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,outPend.S("id")));
        Assert.AreEqual(60m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).N("balance")));
        var revPend=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,outPosted.S("id")));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,revPend.S("id")));
        // After reversing the out of 40, cash is back to 100 — not 140 (double count) and not stuck.
        Assert.AreEqual(100m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).N("balance")));
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-18",["direction"]="out",["amount"]=140,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-overdraw"
        }))).Status,"must not accept out of 140 after reverse restored only 100");
        var ok=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-18",["direction"]="out",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-exact"
        }));
        Assert.AreEqual("pending",ok.S("status"));

        // Reverse of an in: available must return to 0, not go negative then block re-out.
        string leaf2=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1F_REV_IN",["labelAr"]="ورقة عكس دخول",["sortOrder"]=96
        }))["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1F_REV_IN").S("id");
        var in2=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-18",["direction"]="in",["amount"]=200,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-in2"
        }));
        var in2Posted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,in2.S("id")));
        var revIn=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,in2Posted.S("id")));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,revIn.S("id")));
        Assert.AreEqual(0m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leaf2).N("balance")));
        var rein=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-18",["direction"]="in",["amount"]=200,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-rein"
        }));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,rein.S("id")));
        var leave=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-18",["direction"]="out",["amount"]=200,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-leave"
        }));
        Assert.AreEqual("pending",leave.S("status"),"cash re-entered after reverse-in must be spendable");
    }

    /// <summary>JC1f MEDIUM #2: cancel pending; reversal out uses capacity so it cannot strand another pending out.</summary>
    [TestMethod]
    public void OwnerExternalBox_CancelPending_AndReversalOutRespectsCapacity()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1F_CAN",["labelAr"]="ورقة إلغاء معلّق",["sortOrder"]=95
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1F_CAN").S("id");
        var inPend=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-19",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-can-in"
        }));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,inPend.S("id")));
        var pendingOut=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-19",["direction"]="out",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-can-out"
        }));
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId))).Status);
        var cancelled=f.Store.Write((c,tx)=>OwnerExternalBoxApi.CancelEntry(c,tx,Maker,pendingOut.S("id")));
        Assert.AreEqual("cancelled",cancelled.S("status"));
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pendingOut.S("id")))).Status);
        // After cancel, leaf can archive at zero pending with balance remaining → request path.
        var req=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId));
        Assert.AreNotEqual("",req["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).S("archiveRequestedBy"));

        // Reversal out blocked while another pending out reserves the cash.
        string leaf2=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_JC1F_CAPR",["labelAr"]="ورقة سعة عكس",["sortOrder"]=94
        }))["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_JC1F_CAPR").S("id");
        var a=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-19",["direction"]="in",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-capr-in"
        }));
        var aPosted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,a.S("id")));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leaf2,["occurredOn"]="2026-11-19",["direction"]="out",["amount"]=100,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1f-capr-pend"
        }));
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>OwnerExternalBoxApi.ReverseEntry(c,tx,Maker,aPosted.S("id")))).Status,
            "reversal out must not skip capacity while another out is pending");
    }

    /// <summary>JC1e #3: opening-fee leaf «تحصيل نقدي فقط» must not journal income.</summary>
    [TestMethod]
    public void OwnerExternalBox_OpeningFee_CollectionOnly_NoCompanyJournal()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{
            if(UserAccess.Find(c,"synthetic-jc1b-checker",tx)==null)
                Store.Save(c,"user",new(){["id"]="synthetic-jc1b-checker",["username"]="synthetic-jc1b-checker",["role"]="Admin",["status"]="active",["department"]="FIN",["rankLevel"]=5},tx);
            return true;
        });
        string leafId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode).S("id"));
        var pending=f.Store.Write((c,tx)=>OwnerExternalBoxApi.PostEntry(c,tx,Maker,new()
        {
            ["nodeId"]=leafId,["occurredOn"]="2026-11-17",["direction"]="in",["amount"]=200,
            ["sourceKind"]="collection",["notes"]="synthetic-jc1e-cash-only"
        }));
        var posted=f.Store.Write((c,tx)=>OwnerExternalBoxApi.ConfirmEntry(c,tx,Checker,pending.S("id")));
        Assert.AreEqual("",posted.S("companyJournalId"));
        Assert.AreEqual(200m,f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("id")==leafId).N("balance")));
    }

    [TestMethod]
    public void OwnerExternalBox_GrowArchiveRestore_ZeroBalance_LivingTree()
    {
        using var f=new PayFixture();
        string rootId=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker)["roots"]!.AsArray().OfType<JsonObject>().Single().S("id"));
        var grown=f.Store.Write((c,tx)=>OwnerExternalBoxApi.AddNode(c,tx,Maker,new()
        {
            ["parentId"]=rootId,["kind"]="leaf",["code"]="SYN_OWNER_LEAF",["labelAr"]="ورقة اختبار اصطناعية",["sortOrder"]=90
        }));
        string leafId=grown["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Single(c=>c.S("code")=="SYN_OWNER_LEAF").S("id");
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.ArchiveNode(c,tx,Maker,leafId));
        var active=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker,includeArchived:false));
        Assert.IsFalse(active["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Any(c=>c.S("code")=="SYN_OWNER_LEAF"));
        f.Store.Write((c,tx)=>OwnerExternalBoxApi.RestoreNode(c,tx,Maker,leafId));
        var restored=f.Store.Read(c=>OwnerExternalBoxApi.Tree(c,null,Maker,includeArchived:false));
        Assert.IsTrue(restored["roots"]!.AsArray().OfType<JsonObject>().Single()["children"]!.AsArray().OfType<JsonObject>()
            .Any(c=>c.S("code")=="SYN_OWNER_LEAF"));
    }

    [TestMethod]
    public void JahezPeriodReport_MatchesReconciliationBuildTotals_NoDoubleDebtPaid()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var op=new JsonObject
        {
            ["id"]="synthetic-jahez-op",["erpSourceId"]="synthetic-jahez-op",["companyId"]=company,
            ["executedAt"]="2026-10-12T10:00:00Z",["net"]=100,["commission"]=15,["calculatedCommission"]=15,
            ["transferToRider"]=40,["fund"]="Cashbox",["purpose"]="Settlement",["status"]="Completed"
        };
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch([op],DateTimeOffset.Parse("2026-10-20T08:00:00Z"),"omar_live");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"platform_account",new(){["id"]="synthetic-jahez-acc",["companyId"]=company,["platformCode"]="jahez"},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-jtx",["accountId"]="synthetic-jahez-acc",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-11T12:00:00Z",["deliveryPrice"]=80,["cashAmount"]=20,["driverAdjustment"]=5,["net"]=60},tx);
            Store.Save(c,"jahez_earnings_statement",new(){["id"]="synthetic-earn",["accountId"]="synthetic-jahez-acc",["companyId"]=company,["riderId"]="2000000001",["from"]="2026-10-01",["to"]="2026-10-15",["tips"]=3,["bonuses"]=2},tx);
            // Same source as JahezReconciliation.Build: local erp_jahez_operation copy.
            Store.Save(c,"erp_jahez_operation",op,tx);
            // Tagged behalf: amount 10; dashboardDebtPaid must NOT be added on top.
            Store.Save(c,"erp_money_movement",new(){["id"]="synthetic-jmm",["accountId"]="synthetic-jahez-acc",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-13T12:00:00Z",["directionCode"]="Out",["purposeCode"]="OnBehalf",["amount"]=10,["dashboardDebtPaid"]=10},tx);
            // Untagged outbound must not count as paid-on-behalf.
            Store.Save(c,"erp_money_movement",new(){["id"]="synthetic-jmm-out",["accountId"]="synthetic-jahez-acc",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-14T12:00:00Z",["directionCode"]="Out",["purposeCode"]="Transfer",["amount"]=99,["dashboardDebtPaid"]=0},tx);
            return true;
        });
        var report=f.Store.Read(c=>ErpSettlementsApi.JahezPeriodReport(c,null,Maker,new()
        {
            ["companyId"]=company,["from"]="2026-10-01",["to"]="2026-10-20",["invoiceNet"]=85,["invoiceVat"]=12.75m
        }));
        var totals=report["totals"]!.AsObject();
        Assert.AreEqual(15m,totals.N("commission"));
        Assert.AreEqual(40m,totals.N("transferToRider"));
        Assert.AreEqual(20m,totals.N("cashCollections"));
        Assert.AreEqual(10m,totals.N("paidOnBehalf"),"do not sum amount+dashboardDebtPaid; ignore untagged Out");
        Assert.AreEqual(5m,totals.N("bonusesTips"));
        Assert.AreEqual(5m,totals.N("driverAdjustments"));
        // companyDue = 80 + 5 tips/bonuses + 15 commission + 5 adj = 105; expected = 105 - 20 + 10 = 95
        Assert.AreEqual(105m,totals.N("companyDue"));
        Assert.AreEqual(95m,totals.N("expectedNet"));
        var agg=f.Store.Read(c=>
        {
            var a=JahezReconciliation.AggregatePeriod(c,null,company,DateOnly.Parse("2026-10-01"),DateOnly.Parse("2026-10-20"));
            return new JsonObject{["companyDue"]=a.CompanyDue,["expectedNet"]=a.ExpectedNet,["paidOnBehalf"]=a.PaidOnBehalf};
        });
        Assert.AreEqual(totals.N("companyDue"),agg.N("companyDue"));
        Assert.AreEqual(totals.N("expectedNet"),agg.N("expectedNet"));
        Assert.AreEqual(totals.N("paidOnBehalf"),agg.N("paidOnBehalf"));
    }

    [TestMethod]
    public void ChefzLive_BlankStatus_IsNotClosed_BlocksSnapshot()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var blank=new JsonObject
        {
            ["id"]="synthetic-chefz-blank",["erpSourceId"]="synthetic-chefz-blank",["companyId"]=company,
            ["platformAccountId"]="syn-c-acc",["riderId"]="2000000001",
            ["periodFrom"]="2026-11-01",["periodTo"]="2026-11-10",["chargedFrom"]="2026-11-01",["chargedTo"]="2026-11-10",
            ["assignedDays"]=10,["source"]="Orders",["gross"]=100,["companyAmount"]=25,["riderAmount"]=75,
            ["commissionRate"]=0,["dailyFee"]=25,["status"]="",["sourceDeleted"]=false
        };
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch([blank],DateTimeOffset.Parse("2026-11-20T08:00:00Z"),"omar_live");
        var live=f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.IsFalse(live.B("snapshotSafe"));
        Assert.IsFalse(live["items"]!.AsArray().OfType<JsonObject>().Single().B("isCompletedClosed"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ErpSettlementsApi.TakeDisplaySnapshot(c,tx,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11",["businessDate"]="2026-11-20"}))).Status);
    }

    [TestMethod]
    public void ChefzLive_SnapshotSafeOnlyWhenCompletedClosed()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var openRow=new JsonObject
        {
            ["id"]="synthetic-chefz-open",["erpSourceId"]="synthetic-chefz-open",["companyId"]=company,
            ["platformAccountId"]="syn-c-acc",["riderId"]="2000000001",
            ["periodFrom"]="2026-11-01",["periodTo"]="2099-12-31",["chargedFrom"]="2026-11-01",["chargedTo"]="2099-12-31",
            ["assignedDays"]=10,["source"]="Orders",["gross"]=100,["companyAmount"]=25,["riderAmount"]=75,
            ["commissionRate"]=0,["dailyFee"]=25,["status"]="Open",["sourceDeleted"]=false
        };
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch([openRow],DateTimeOffset.Parse("2026-11-20T08:00:00Z"),"omar_live");
        var live=f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.IsFalse(live.B("snapshotSafe"));
        Assert.IsTrue(live.S("snapshotSafetyRuleAr").Contains("مكتملة"));
        Assert.IsFalse(live["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="snapshot"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ErpSettlementsApi.TakeDisplaySnapshot(c,tx,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11",["businessDate"]="2026-11-20"}))).Status);

        var closed=new JsonObject
        {
            ["id"]="synthetic-chefz-closed",["erpSourceId"]="synthetic-chefz-closed",["companyId"]=company,
            ["platformAccountId"]="syn-c-acc",["riderId"]="2000000001",
            ["periodFrom"]="2026-11-01",["periodTo"]="2026-11-15",["chargedFrom"]="2026-11-01",["chargedTo"]="2026-11-10",
            ["assignedDays"]=10,["source"]="Orders",["gross"]=100,["companyAmount"]=25,["riderAmount"]=75,
            ["commissionRate"]=0,["dailyFee"]=25,["status"]="Completed",["sourceDeleted"]=false
        };
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch([closed],DateTimeOffset.Parse("2026-11-20T08:00:00Z"),"omar_live");
        var live2=f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.IsTrue(live2.B("snapshotSafe"));
        Assert.IsTrue(live2["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="snapshot"));
        var snap=f.Store.Write((c,tx)=>ErpSettlementsApi.TakeDisplaySnapshot(c,tx,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11",["businessDate"]="2026-11-20"}));
        Assert.AreEqual("display",snap.S("purpose"));
        Assert.IsFalse(snap.B("moneyFrozen"));
    }

    [TestMethod]
    public void JahezReconciliation_FromDateFiltersRange_AndTracksTransferToRider()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"platform_account",new(){["id"]="synthetic-jahez-account-2",["companyId"]=company,["platformCode"]="jahez"},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-tx-early",["accountId"]="synthetic-jahez-account-2",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-05T12:00:00Z",["deliveryPrice"]=200,["cashAmount"]=0,["driverAdjustment"]=0,["net"]=200},tx);
            Store.Save(c,"jahez_transaction",new(){["id"]="synthetic-tx-late",["accountId"]="synthetic-jahez-account-2",["companyId"]=company,["riderId"]="2000000001",["occurredAt"]="2026-10-12T12:00:00Z",["deliveryPrice"]=50,["cashAmount"]=10,["driverAdjustment"]=0,["net"]=40},tx);
            Store.Save(c,"erp_jahez_operation",new(){["id"]="synthetic-op-xfer",["accountId"]="synthetic-jahez-account-2",["companyId"]=company,["riderId"]="2000000001",["executedAt"]="2026-10-12T12:00:00Z",["commission"]=15,["calculatedCommission"]=15,["transferToRider"]=30},tx);
            return true;
        });
        var doc=f.Store.Write((c,tx)=>JahezReconciliation.Build(c,tx,Maker,new()
        {
            ["companyId"]=company,["period"]="2026-10",["invoiceNet"]=55,["invoiceVat"]=8.25m,
            ["lastTransactionDate"]="2026-10-20",["fromDate"]="2026-10-10",["invoiceReference"]="synthetic-range"
        }));
        var totals=doc["totals"]!.AsObject();
        Assert.AreEqual(50m,totals.N("deliveryFees"));
        Assert.AreEqual(10m,totals.N("cashCollections"));
        Assert.AreEqual(15m,totals.N("commission"));
        Assert.AreEqual(30m,totals.N("transferToRider"));
        Assert.AreEqual("2026-10-10",doc.S("fromDate"));
    }
}
