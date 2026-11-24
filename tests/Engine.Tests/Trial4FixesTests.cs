using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>TRIAL4: FLOW1 / TRACE no-decision fixes (failing tests first).</summary>
[TestClass]
public sealed class Trial4FixesTests
{
    static ClaimsPrincipal Actor(string? name=null)=>UserAccess.Principal(new JsonObject{["username"]=name??MoneySchemaFixture.Id(),["role"]="Admin"});

    [TestMethod]
    public void Flow1_RiderRequestApprove_SamePerson_403()
    {
        var c=MoneySchemaFixture.Payments(false);
        using(c)
        {
            SchemaSalPayRun.Ensure(c);SchemaAuthCatalog.Ensure(c);
            if(!SchemaSql.IsSql(c))
                MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
            string company=MoneySchemaFixture.Company(c);
            var maker=Actor();
            using var tx=c.BeginTransaction();
            var hold=SalRiderRequests.Create(c,tx,maker,new JsonObject
            {
                ["companyId"]=company,["erpRiderId"]=MoneySchemaFixture.Id(),["periodMonth"]="2026-10-01",["appCode"]="ninja",
                ["kind"]="hold",["reasonCode"]="shared",["note"]="إيقاف مصطنع"
            });
            var err=Assert.ThrowsExactly<ApiError>(()=>SalRiderRequests.Approve(c,tx,maker,hold.S("id"),new JsonObject{["note"]="نفس الشخص"}));
            Assert.AreEqual(403,err.Status);
        }
    }

    [TestMethod]
    public void Trace_H2_PayApprovalsBypass_DoesNotCoverDualBankOrPaymentOut()
    {
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualBankFile));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,ApprovalsArea.DualPaymentOut));
        Assert.IsTrue(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,"pay.approve"));
        Assert.IsTrue(AccessPolicy.ScopeCovers(ApprovalsArea.DualBankFile,ApprovalsArea.DualBankFile));
    }

    [TestMethod]
    public void Trace_ManualJournal_CancelKeepsRecord_NoHardDelete()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        var author=HungerStationInvoiceTests.Actor("synthetic-author");
        var draft=f.Store.Write((c,tx)=>ManualJournals.Create(c,tx,author,new JsonObject
        {
            ["companyId"]=FinancialCore.Sponsors[0].Id,
            ["date"]="2026-10-01",
            ["reference"]="SYN-CANCEL-"+MoneySchemaFixture.Id()[..8],
            ["description"]="مسودة مصطنعة للإلغاء",
            ["lines"]=new JsonArray(
                new JsonObject{["accountCode"]="512101",["debit"]=10m,["credit"]=0m},
                new JsonObject{["accountCode"]="212103",["debit"]=0m,["credit"]=10m})
        }));
        string id=draft.S("id");
        var cancelled=f.Store.Write((c,tx)=>ManualJournals.Delete(c,tx,author,id));
        Assert.AreEqual("cancelled",cancelled.S("status"));
        Assert.IsFalse(cancelled.B("deleted"));
        var kept=f.Store.Read(c=>Store.Get(c,id,ManualJournals.Kind));
        Assert.IsNotNull(kept);
        Assert.AreEqual("cancelled",kept!.S("status"));
    }

    [TestMethod]
    public void Flow1_BankAdjust_RejectClosesPendingApproval()
    {
        using var f=new PayFixture();
        string adjustId="";
        f.Store.Write((c,tx)=>
        {
            SchemaTreasury.Ensure(c,tx);
            adjustId=SchemaSql.GuidText(SchemaSql.NewId());
            Store.Save(c,"bank_statement_adjust",new JsonObject
            {
                ["id"]=adjustId,["kind"]="bank_statement_adjust",["statementId"]=MoneySchemaFixture.Id(),
                ["companyId"]=FinancialCore.Sponsors[0].Id,["amount"]=5m,["reason"]="فرق توقيت مصطنع",
                ["status"]="pending_approval",["createdBy"]="maker",["createdAt"]="2026-10-01T00:00:00Z",
                ["approvedBy"]="",["journalId"]=""
            },tx);
            return true;
        });
        var rejected=f.Store.Write((c,tx)=>TreasuryBankStatementsApi.RejectAdjust(c,tx,Actor("checker"),adjustId,new JsonObject{["reason"]="رفض مصطنع لإغلاق الحالة"}));
        Assert.AreEqual("rejected",rejected.S("status"));
        Assert.AreEqual("checker",rejected.S("rejectedBy"));
    }

    [TestMethod]
    public void Flow1_PaymentDocumentApprove_SamePerson_403_ForPurchase()
    {
        // Non-advance documents must also refuse maker==checker (FLOW1 Critical).
        using var f=new PaymentsEngineTests.Fixture(false);
        // purchase posts 211101 on approve — FLOW1 requires Independent (expense stays permission-based until voucher dual).
        var input=f.Input("purchase");
        input["supplierId"]=MoneySchemaFixture.Id();
        // Ensure supplier row exists for purchase create.
        f.Store.Write((c,tx)=>{
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"pay","supplier")}(id,company_id,name_ar,status,created_by,created_at)
                VALUES(@id,@c,'مورد مصطنع',1,@by,{SchemaSql.NowLiteral(c)})
                """,("@id",input.S("supplierId")),("@c",f.Company),("@by",f.Maker.Identity!.Name!));
            return true;
        });
        var doc=f.Create(input);
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,doc.S("id"),"submit",new()));
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,doc.S("id"),"approve",new())));
        Assert.AreEqual(403,err.Status);
    }

    [TestMethod]
    public void Flow1_AdvanceView_OffersRequestPayout_BeforeDual()
    {
        using var f=new HungerStationInvoiceTests.Fixture();
        string company=FinancialCore.Sponsors[0].Id,rider=HungerStationInvoiceTests.Rider,reqId="synthetic-adv-view-rp";
        var custodian=UserAccess.Principal(new JsonObject
        {
            ["username"]="synthetic-custodian-view",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=5,
            ["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide","approvals.view")
        });
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"user",new()
            {
                ["id"]="synthetic-custodian-view",["username"]="synthetic-custodian-view",["role"]="FinancialAccountant",
                ["department"]="FIN",["rankLevel"]=5,
                ["customPermissions"]=new JsonArray("advances.payout",ApprovalsArea.DualPaymentOut,"approvals.decide","approvals.view")
            },tx);
            OrgDirectory.AddMembership(c,tx,PayFixture.Maker,new(){["userId"]="synthetic-custodian-view",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,PayFixture.Maker,new(){["operation"]="advance.payout:MAIN.BW.DEBT",["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("synthetic-custodian-view"),["validFrom"]="2026-01-01"});
            Store.Save(c,AdvanceRequests.Kind,new()
            {
                ["id"]=reqId,["type"]="cash",["vaultCode"]="MAIN.BW.DEBT",["companyId"]=company,["riderIqama"]=rider,
                ["riderName"]="Synthetic Rider",["amount"]=80,["monthlyInstallment"]=40,
                ["requestedBy"]="synthetic-supervisor",["approvedBy"]="synthetic-independent-reviewer",
                ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["status"]="approved",["decisions"]=new JsonArray()
            },tx);
            return true;
        });
        var view=f.Store.Read(c=>AdvanceRequests.Read(c,custodian,reqId));
        var actions=(view["allowedActions"] as JsonArray)?.Select(x=>x?.ToString()).ToHashSet(StringComparer.Ordinal)??[];
        Assert.IsTrue(actions.Contains("request_payout"),"approved advance must offer request_payout before dual opens");
        Assert.IsFalse(actions.Contains("payout"),"payout stays hidden until dual request exists");
    }

    [TestMethod]
    public void Flow1_BankStatementUpload_UsesStoreAudit()
    {
        // grok-TRIAL4: do not treat a source-text search as a pass — real Upload asserts entity/recordId.
        // Behaviour covered by Trial5FixesTests.BankStatement_Upload_AuditHasEntityRecordIdLineCountFileHash
        // and TreasuryW2ApiTests.BankStatement_UploadAutoMatch_AndAdjustPostsAfterApproval.
        string? dir=AppContext.BaseDirectory;
        string? srcPath=null;
        for(int i=0;i<8&&dir!=null;i++)
        {
            string candidate=System.IO.Path.Combine(dir,"Payroll.Api","TreasuryBankStatementsApi.cs");
            if(System.IO.File.Exists(candidate)){srcPath=candidate;break;}
            dir=System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }
        Assert.IsNotNull(srcPath,"TreasuryBankStatementsApi.cs not found from test base");
        var src=System.IO.File.ReadAllText(srcPath!);
        Assert.IsTrue(src.Contains("Store.Audit(c,tx,\"treasury.bank_statement_uploaded\"",StringComparison.Ordinal));
        Assert.IsTrue(src.Contains("lineCount",StringComparison.Ordinal));
        Assert.IsTrue(src.Contains("fileHash",StringComparison.Ordinal));
    }
}
