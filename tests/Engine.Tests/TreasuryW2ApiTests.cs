using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>U-W2-18 / 19 / 35 / 36 / 37 — SQLite behavior tests (SQL Server covered via SchemaTreasury + sqltest.sh).</summary>
[TestClass]
public sealed class TreasuryW2ApiTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-treas-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-treas-checker","Admin");
    static ClaimsPrincipal Viewer=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-treas-viewer",["role"]="FinancialManager",
        ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id)}
    });

    [TestMethod]
    public void BalancesTree_ReturnsPeriodBalanceNodes_AndTransferReturnsArabic409()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string accountId=AccountId(c,tx,"111201");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,0,250,0,250,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            return true;
        });
        var tree=f.Store.Read(c=>TreasuryBalancesApi.Tree(c,null,Maker,"2026-11",company));
        Assert.AreEqual("2026-11",tree.S("periodMonth"));
        Assert.IsTrue(tree["items"]!.AsArray().Count>=1);
        var companyNode=tree["items"]!.AsArray().OfType<JsonObject>().Single(n=>n.S("companyId")==company);
        Assert.IsTrue(companyNode["children"]!.AsArray().OfType<JsonObject>().Any(ch=>ch.S("accountCode")=="111201"&&ch.N("balance")==250m));
        Assert.IsTrue(tree["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="request_transfer"));
        var err=Assert.ThrowsExactly<ApiError>(()=>TreasuryBalancesApi.RefuseTransfer(Maker,new(){["fromAccountId"]="x",["toAccountId"]="y",["amount"]=10}));
        Assert.AreEqual(409,err.Status);
        Assert.IsTrue(err.Message.Contains("محرك المدفوعات",StringComparison.Ordinal));
    }

    [TestMethod]
    public void VaultCount_ApprovePostsShortJournal_MakerCannotApprove()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string countId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string box=EnsureCashBox(c,tx,company,reg,"111103");
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-20",["countedAmount"]=90,["bookBalance"]=100
            });
            countId=created.S("id");
            Assert.AreEqual(-10m,created.N("variance"));
            Assert.AreEqual("542102",created.S("varianceAccount"));
            TreasuryVaultCountsApi.Submit(c,tx,Maker,countId);
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryVaultCountsApi.Approve(c,tx,Maker,countId))).Status);
        var approved=f.Store.Write((c,tx)=>TreasuryVaultCountsApi.Approve(c,tx,Checker,countId));
        Assert.AreEqual("approved",approved.S("status"));
        Assert.AreNotEqual("",approved.S("journalId"));
        f.Store.Read(c=>
        {
            object? purpose=SchemaSql.Scalar(c,null,$"SELECT purpose FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",approved.S("journalId")));
            Assert.AreEqual("vault_over_short",purpose?.ToString());
            object? shortAmt=SchemaSql.Scalar(c,null,$"""
                SELECT COALESCE(SUM(l.debit),0) FROM {SchemaSql.Q(c,"fin","journal_line")} l
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=l.account_id
                WHERE l.journal_id=@j AND a.code='542102'
                """,("@j",approved.S("journalId")));
            Assert.AreEqual(10m,Convert.ToDecimal(shortAmt,CultureInfo.InvariantCulture));
            return true;
        });
    }

    [TestMethod]
    public void BankStatement_UploadAutoMatch_AndAdjustPostsAfterApproval()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",adjustId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            // Posted journal line to match (+100 deposit on bank).
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            string journalId=SchemaSql.GuidText(SchemaSql.NewId()),lineId=SchemaSql.GuidText(SchemaSql.NewId());
            string bankAcc=AccountId(c,tx,"111201");
            string equity=AccountId(c,tx,"212103");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,created_at)
                VALUES(@id,@id,@p,@c,'2026-11-01','2026-11-10','synthetic',@id,'manual',1,{SchemaSql.NowLiteral(c)})
                """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,100,0,{SchemaSql.NowLiteral(c)})
                """,("@id",lineId),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",bankAcc),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,100,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));

            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=100,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]="SYN-DEP"})
            });
            statementId=uploaded.S("id");
            Assert.AreEqual(1,uploaded["lines"]!.AsArray().Count);
            var matched=TreasuryBankStatementsApi.AutoMatch(c,tx,Maker,statementId);
            Assert.AreEqual(1m,matched.N("matchedCount"));
            var draft=TreasuryBankStatementsApi.RequestAdjust(c,tx,Maker,statementId,new(){["amount"]=5,["reason"]="فرق توقيت بنكي تجريبي"});
            adjustId=draft.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryBankStatementsApi.ApproveAdjust(c,tx,Maker,adjustId))).Status);
        var posted=f.Store.Write((c,tx)=>TreasuryBankStatementsApi.ApproveAdjust(c,tx,Checker,adjustId));
        Assert.AreEqual("approved",posted.S("status"));
        Assert.AreNotEqual("",posted.S("journalId"));
        Assert.IsTrue(f.Store.Read(c=>Store.List(c,"audit").Any(a=>a.S("action")=="treasury.bank_statement_uploaded"&&(a.S("entity")==statementId||a.S("recordId")==statementId))));
    }

    [TestMethod]
    public void ErpSettlements_LiveSnapshotFreezeDifference_AndChefzCommissionIsCheckOnly()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["companyId"]=company,["platformAccountId"]="syn-chefz-acc",
                ["riderId"]="2000000001",["periodFrom"]="2026-11-01",["periodTo"]="2026-11-15",
                ["chargedFrom"]="2026-11-01",["chargedTo"]="2026-11-10",["assignedDays"]=10,
                ["source"]="Orders",["gross"]=100,["companyAmount"]=25,["riderAmount"]=75,
                ["commissionRate"]=0,["dailyFee"]=25,["deliveryFees"]=100,["vatAmount"]=0,["tips"]=0,["compensation"]=0,["bonuses"]=0,["deductions"]=0,
                ["sourceDeleted"]=false
            },tx);
            Store.Save(c,"platform_invoice",new()
            {
                ["id"]="syn-chefz-inv",["companyId"]=company,["platform"]="Chefz",["period"]="2026-11",
                ["approvalStatus"]="approved",["status"]="approved"
            },tx);
            return true;
        });
        var live=f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11",["half"]="1"}));
        Assert.AreEqual("omar_live",live.S("source"));
        Assert.AreEqual(1m,live.N("total"));
        Assert.AreEqual(100m,live["items"]!.AsArray().OfType<JsonObject>().Single().N("gross"));
        var snap=f.Store.Write((c,tx)=>ErpSettlementsApi.TakeDisplaySnapshot(c,tx,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11",["businessDate"]="2026-11-20"}));
        Assert.AreEqual("display",snap.S("purpose"));
        Assert.IsFalse(snap.B("moneyFrozen"));
        var freeze=f.Store.Write((c,tx)=>ErpSettlementsApi.FreezeChefzMoney(c,tx,Maker,new(){["companyId"]=company,["invoiceId"]="syn-chefz-inv"}));
        Assert.AreEqual("frozen",freeze.S("status"));
        Assert.IsTrue(freeze.N("frozenCount")>=1);
        // Identity mismatch sample for difference report.
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["companyId"]=company,["platformAccountId"]="syn-chefz-acc-2",
                ["riderId"]="2000000002",["periodFrom"]="2026-11-16",["periodTo"]="2026-11-30",
                ["source"]="Orders",["gross"]=100,["companyAmount"]=40,["riderAmount"]=50, // identity fail: 100-40≠50
                ["commissionRate"]=0.15m,["dailyFee"]=0,["deliveryFees"]=100,["vatAmount"]=0,["tips"]=0,["compensation"]=0,["bonuses"]=0,["deductions"]=0,
                ["sourceDeleted"]=false
            },tx);
            return true;
        });
        var report=f.Store.Read(c=>ErpSettlementsApi.ChefzDifferenceReport(c,null,Maker,new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.IsTrue(report["summary"]!.AsObject().N("identity")>=1);
        Assert.IsTrue(report["items"]!.AsArray().Count>=1);
        // ChefzCommission stays a helper — FullTimeFeeForMonth is 10*25 capped at 15*25 = 250 for half.
        Assert.AreEqual(250m,ChefzCommission.FullTimeFeeForMonth(Enumerable.Range(0,10).Select(i=>new DateOnly(2026,11,1+i)),2026,11));
    }

    [TestMethod]
    public void JahezDay_AlTogaryIsolated_AndFirstJournalsImmutableFlag()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_jahez_operation",new()
            {
                ["id"]="syn-day-op",["companyId"]=company,["executedAt"]="2026-11-20T10:00:00Z",
                ["net"]=80,["commission"]=10,["vaultCode"]="MAIN"
            },tx);
            Store.Save(c,"erp_jahez_operation",new()
            {
                ["id"]="syn-togary-op",["companyId"]=company,["executedAt"]="2026-11-20T11:00:00Z",
                ["net"]=999,["commission"]=1,["vaultCode"]="togary-cash"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new(){["companyId"]=company,["businessDate"]="2026-11-20",["vaultCode"]="MAIN"});
        });
        Assert.AreEqual("draft",day.S("status"));
        Assert.IsFalse(day.B("isAlTogary"));
        Assert.AreEqual(80m,day.N("opsSumAmount")); // togary ops excluded
        f.Store.Write((c,tx)=>JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id")));
        // transferDiff may be non-zero with no bank movements — decide then approve
        f.Store.Write((c,tx)=>JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="فرق تحويل مقبول للاختبار"}));
        var approved=f.Store.Write((c,tx)=>JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id")));
        Assert.AreEqual("approved",approved.S("status"));
        Assert.AreEqual("day_approved",approved.S("reconciliationState"));
        Assert.IsTrue(approved.B("firstJournalsImmutable"));
        Assert.IsTrue(approved["journalEntryIds"]!.AsArray().Count>=1);
        // Re-approve closed day refuses; maker≠checker checked on a fresh submit.
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.ApproveDay(c,tx,Maker,day.S("id")))).Status);
        var day2=f.Store.Write((c,tx)=>
        {
            Store.Save(c,"erp_jahez_operation",new(){["id"]="syn-day-op-2",["companyId"]=company,["executedAt"]="2026-11-22T10:00:00Z",["net"]=50,["commission"]=5,["vaultCode"]="MAIN"},tx);
            var d=JahezReconciliation.OpenDay(c,tx,Maker,new(){["companyId"]=company,["businessDate"]="2026-11-22",["vaultCode"]="MAIN"});
            JahezReconciliation.SubmitDay(c,tx,Maker,d.S("id"));
            JahezReconciliation.DecideDayVariance(c,tx,Maker,d.S("id"),new(){["varianceDecision"]="قرار فرق يوم ثانٍ"});
            return d;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.ApproveDay(c,tx,Maker,day2.S("id")))).Status);

        var togary=f.Store.Write((c,tx)=>JahezReconciliation.OpenDay(c,tx,Maker,new(){["companyId"]=company,["businessDate"]="2026-11-21",["vaultCode"]="togary"}));
        Assert.IsTrue(togary.B("isAlTogary"));
    }

    [TestMethod]
    public void ViewerCannotOpenTreasuryWrites()
    {
        using var f=new PayFixture();
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryVaultCountsApi.Create(c,tx,Viewer,new(){["cashBoxId"]="x",["countedOn"]="2026-11-01",["countedAmount"]=1}))).Status);
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>TreasuryBalancesApi.RefuseTransfer(Viewer,new())).Status);
    }

    static void EnsureSponsorCompany(DbConnection c,DbTransaction tx,string companyId)
    {
        var sp=FinancialCore.ResolveSponsor(companyId);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId));
        if(exists is not null and not DBNull)return;
        string unified=sp.UnifiedNumber.Length>=10?sp.UnifiedNumber:"7"+companyId.Replace("-","")[..9];
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at)
            VALUES(@id,@code,@u,@n,1,{SchemaSql.NowLiteral(c)})
            """,("@id",companyId),("@code",sp.Code),("@u",unified),("@n",sp.NameAr));
    }
    static string EnsureRegistration(DbConnection c,DbTransaction tx,string companyId)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE company_id=@c",("@c",companyId));
        if(existing is not null and not DBNull)return existing switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(existing,CultureInfo.InvariantCulture)!};
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","commercial_registration")}(id,company_id,cr_number,status,created_at)
            VALUES(@id,@c,@cr,1,{SchemaSql.NowLiteral(c)})
            """,("@id",id),("@c",companyId),("@cr",id.Replace("-","")[..20]));
        return id;
    }
    static string EnsureCashBox(DbConnection c,DbTransaction tx,string companyId,string registrationId,string accountCode)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","cash_box")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'خزنة اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",registrationId),("@code","BOX-"+id[..8]),("@a",AccountId(c,tx,accountCode)));
        return id;
    }
    static string EnsureBankAccount(DbConnection c,DbTransaction tx,string companyId,string registrationId,string accountCode)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_account")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'بنك اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",registrationId),("@code","BNK-"+id[..8]),("@a",AccountId(c,tx,accountCode)));
        return id;
    }
    static string AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        Assert.IsNotNull(id,$"missing CoA {code}");
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
    static Guid EnsurePeriod(DbConnection c,DbTransaction tx,string companyId,string month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",companyId),("@m",month));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at)
            VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month));
        return id;
    }
}
