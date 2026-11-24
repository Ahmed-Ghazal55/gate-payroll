using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>TRIAL6: bank recon line actions + new CoA; approve-own permission; calculatedBy Independent.</summary>
[TestClass]
public sealed class Trial6FixesTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-trial6-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-trial6-checker","Admin");
    static ClaimsPrincipal Actor(string name,string role="Admin",JsonArray? custom=null)
        =>UserAccess.Principal(new JsonObject
        {
            ["username"]=name,["role"]=role,
            ["customPermissions"]=custom??new JsonArray()
        });

    [TestMethod]
    public void CoA_Seeds_BankReconAccounts_WithExpectedCodes()
    {
        using var f=new PayFixture();
        f.Store.Write((c,tx)=>{SchemaFinAccount.Ensure(c,tx);return true;});
        f.Store.Read(c=>
        {
            Assert.AreEqual("مبالغ تحت التسوية",Name(c,"114108"));
            Assert.AreEqual("إيرادات بنكية",Name(c,"422103"));
            Assert.AreEqual("مصاريف وعمولات بنكية",Name(c,"541401"));
            Assert.AreEqual("مصاريف وعمولات بنكية",Name(c,"5414"));
            return true;
        });
        static string Name(DbConnection c,string code)
            =>SchemaSql.Scalar(c,null,$"SELECT name_ar FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code))?.ToString()??"";
    }

    [TestMethod]
    public void BankRecon_TimingDifference_NoJournal_CarriedAndCounted()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=50,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-12",["amount"]=50,["reference"]="TIMING-DEP"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var timed=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="timing",["note"]="شيك لم يُحصّل بعد"});
            Assert.AreEqual("timing",timed.S("disposition"));
            Assert.IsTrue(timed["allowedActions"]!.AsArray().Count==0||timed["allowedActions"]!.AsArray().All(a=>a?.ToString()!="create_payment"));
            long journals=Convert.ToInt64(SchemaSql.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L);
            Assert.AreEqual(0L,journals);
            return true;
        });
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.AreEqual(1m,view.N("timingCount"));
        Assert.AreEqual(0m,view.N("unmatchedCount")); // TRIAL7 / grok-TRIAL6: timing only in timingCount
        Assert.AreEqual(0m,view.N("matchedCount"));
        Assert.AreEqual(50m,view.N("bankClosingBalance"));
        Assert.AreEqual(50m,view.N("reconciledBalance")); // opening + timing when residual unmatched=0
        var line=view["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("id")==lineId);
        Assert.AreEqual("timing",line.S("disposition"));
        Assert.IsTrue(line["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="clear_timing")||line.B("timing"));
    }

    [TestMethod]
    public void BankRecon_BankCharges_PaymentDraft_PostsAfterApproval_AndAutoMatches()
    {
        // Negative bank line → payment voucher (expense 541401) → Independent approve → match.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="",draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=100,["closingBalance"]=90,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-15",["amount"]=-10,["reference"]="FEE-NOV"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var started=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="payment",["note"]="عمولة بنكية شهرية"});
            Assert.AreEqual("payment",started.S("reconKind"));
            Assert.AreEqual("541401",started.S("counterAccountCode"));
            draftId=started.S("documentId");
            Assert.IsTrue(draftId.StartsWith("manual-journal:",StringComparison.Ordinal));
            Assert.IsTrue(started["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="approve")||started.S("status")=="draft");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Maker,draftId))).Status);
        var approved=f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Checker,draftId));
        Assert.AreEqual("approved",approved.S("status"));
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.AreEqual(1m,view.N("matchedCount"));
        Assert.AreEqual(0m,view.N("unmatchedCount"));
        Assert.AreEqual(90m,view.N("reconciledBalance"));
        Assert.AreEqual(90m,view.N("bankClosingBalance"));
        f.Store.Read(c=>
        {
            decimal fee=Convert.ToDecimal(SchemaSql.Scalar(c,null,$"""
                SELECT COALESCE(SUM(jl.debit),0) FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id WHERE a.code='541401'
                """)??0m);
            Assert.AreEqual(10m,fee);
            object? variance=SchemaSql.Scalar(c,null,$"""
                SELECT COUNT(*) FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id WHERE a.code IN ('422202','542102')
                """);
            Assert.AreEqual(0L,Convert.ToInt64(variance??0L));
            return true;
        });
    }

    [TestMethod]
    public void BankRecon_BankIncome_ReceiptDraft_PostsAndMatches()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="",draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=25,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-18",["amount"]=25,["reference"]="INT-INC"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var started=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="receipt",["note"]="فائدة/إيراد بنكي"});
            Assert.AreEqual("422103",started.S("counterAccountCode"));
            draftId=started.S("documentId");
            return true;
        });
        f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Checker,draftId));
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.AreEqual(1m,view.N("matchedCount"));
        Assert.AreEqual(25m,view.N("reconciledBalance"));
    }

    [TestMethod]
    public void BankRecon_UnknownAmount_UsesSuspense_ThenClearsByJournal()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="",draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=40,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-20",["amount"]=40,["reference"]="UNK-40"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var started=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="journal",["note"]="مبلغ غير معروف تحت التسوية"});
            Assert.AreEqual("114108",started.S("counterAccountCode"));
            draftId=started.S("documentId");
            return true;
        });
        f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Checker,draftId));
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.AreEqual(1m,view.N("matchedCount"));
        f.Store.Read(c=>
        {
            // Money-in journal credits suspense 114108 (bank Dr / suspense Cr).
            decimal suspenseCredit=Convert.ToDecimal(SchemaSql.Scalar(c,null,$"""
                SELECT COALESCE(SUM(jl.credit),0) FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id WHERE a.code='114108'
                """)??0m);
            Assert.AreEqual(40m,suspenseCredit);
            return true;
        });
    }

    [TestMethod]
    public void BankRecon_OpenOriginal_ReturnsReversePrefill_NeverEditsPosted()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",lineId="",journalId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            string bankAcc=AccountId(c,tx,"111201");
            string equity=AccountId(c,tx,"212103");
            journalId=SchemaSql.GuidText(SchemaSql.NewId());
            string jlBank=SchemaSql.GuidText(SchemaSql.NewId());
            // Draft first — posted journals freeze lines (TR_fin_journal_line_frozen).
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,narration_ar,created_at)
                VALUES(@id,'JV-ERR',@p,@c,'2026-11-01','2026-11-08','synthetic',@id,'manual',1,0,'WRONG-BOOK',{SchemaSql.NowLiteral(c)})
                """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,narration_ar,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,15,0,'WRONG-BOOK',{SchemaSql.NowLiteral(c)})
                """,("@id",jlBank),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",bankAcc),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,15,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=15,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-08",["amount"]=15,["reference"]="WRONG-BOOK"})
            });
            statementId=uploaded.S("id");
            lineId=uploaded["lines"]!.AsArray().OfType<JsonObject>().Single().S("id");
            var opened=TreasuryBankStatementsApi.LineAction(c,tx,Maker,statementId,lineId,new(){["kind"]="open_original"});
            Assert.AreEqual(journalId,opened.S("originalJournalId"));
            Assert.AreEqual("reverse",opened.S("formAction"));
            Assert.IsTrue(opened["prefill"] is JsonObject);
            Assert.IsTrue(opened.S("documentId").StartsWith("manual-journal:",StringComparison.Ordinal),"TRIAL7: reversing draft created");
            // Posted journal untouched (still status 2, no edit).
            Assert.AreEqual(2,Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT status FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",journalId))));
            return true;
        });
    }

    [TestMethod]
    public void BankRecon_GenericAdjustPath_Removed_UseLineActions()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            string bank=EnsureBank(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=5,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-01",["amount"]=5,["reference"]="OLD-ADJ"})
            });
            statementId=uploaded.S("id");
            return true;
        });
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryBankStatementsApi.RequestAdjust(c,tx,Maker,statementId,new(){["amount"]=5,["reason"]="فرق توقيت بنكي تجريبي"})));
        Assert.AreEqual(409,err.Status);
        Assert.IsTrue(err.Message.Contains("سطر",StringComparison.Ordinal)||err.Message.Contains("قيد",StringComparison.Ordinal));
        var view=f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,Maker,statementId));
        Assert.IsFalse(view["allowedActions"]!.AsArray().Any(a=>a?.ToString()=="adjust"));
        var line=view["lines"]!.AsArray().OfType<JsonObject>().Single();
        var actions=line["allowedActions"]!.AsArray().Select(a=>a!.ToString()).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(actions.Contains("create_payment"));
        Assert.IsTrue(actions.Contains("create_receipt"));
        Assert.IsTrue(actions.Contains("create_journal"));
        Assert.IsTrue(actions.Contains("mark_timing"));
        Assert.IsTrue(actions.Contains("open_original"));
    }

    [TestMethod]
    public void ApproveOwn_WithoutPermission_Blocked_WithPermission_Stamped_MoneyOutStillBlocked()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        // Manual journal self-approve without permission → 403
        string draftId="";
        f.Store.Write((c,tx)=>
        {
            EnsureCompany(c,tx,company);
            EnsurePeriod(c,tx,company,"2026-11-01");
            var draft=ManualJournals.Create(c,tx,Maker,new()
            {
                ["companyId"]=company,["date"]="2026-11-05",["reference"]="SYN-SELF-1",
                ["description"]="اختبار اعتماد ذاتي",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=10,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=10,["note"]="b"})
            });
            draftId=draft.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ManualJournals.Approve(c,tx,Maker,draftId))).Status);

        // Grant approve-own → allowed + stamped
        var withPerm=Actor("synthetic-trial6-maker","Admin",new JsonArray(AccessPolicy.ApproveOwnPermission));
        // Recreate as same maker with permission on a fresh draft
        string draft2="";
        f.Store.Write((c,tx)=>
        {
            var draft=ManualJournals.Create(c,tx,withPerm,new()
            {
                ["companyId"]=company,["date"]="2026-11-06",["reference"]="SYN-SELF-2",
                ["description"]="اعتماد ذاتي مصرح",
                ["lines"]=new JsonArray(
                    new JsonObject{["accountCode"]="512101",["debit"]=7,["credit"]=0,["note"]="a"},
                    new JsonObject{["accountCode"]="212103",["debit"]=0,["credit"]=7,["note"]="b"})
            });
            draft2=draft.S("id");
            var approved=ManualJournals.Approve(c,tx,withPerm,draft2);
            Assert.AreEqual("approved",approved.S("status"));
            Assert.IsTrue(approved.B("selfApproved")||approved.B("selfApprovedException"));
            return true;
        });
        var audit=f.Store.Read(c=>Store.List(c,"audit").First(a=>a.S("recordId")==draft2&&(a.B("selfApprovedException")||a.S("action").Contains("self",StringComparison.OrdinalIgnoreCase)||a.S("badge")=="approve_own")));
        Assert.IsFalse(string.IsNullOrWhiteSpace(audit.S("id")));

        // Money-out still blocked even with approve-own
        var moneyOut=Actor("synthetic-trial6-payout","Admin",new JsonArray(AccessPolicy.ApproveOwnPermission,"payments.disburse","advances.payout"));
        bool blocked=AccessPolicy.Independent(moneyOut,moneyOut.Identity!.Name!,"advance.payout",null,null,new JsonObject{["id"]="syn-payout"},scope:ApprovalsArea.DualPaymentOut);
        Assert.IsFalse(blocked);
        // With connection+tx the money-out dual scope must still refuse approve-own
        f.Store.Write((c,tx)=>
        {
            var rec=new JsonObject{["id"]="syn-money-out-1",["createdBy"]=moneyOut.Identity!.Name};
            Assert.IsFalse(AccessPolicy.Independent(moneyOut,moneyOut.Identity!.Name!,"payment.out.pay",c,tx,rec,scope:ApprovalsArea.DualPaymentOut));
            return true;
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SalaryApprove_RecalculatorCannotApprove_UsesCalculatedByList(bool sqlServer)
    {
        // grok-TRIAL5 MEDIUM: A calculates, B recalculates, B confirm+approve → 403 and zero journals.
        if(sqlServer&&string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")))
        {Assert.Inconclusive("SQL Server connection not configured");return;}
        using var c=OpenSal(sqlServer);
        string company=MoneySchemaFixture.Company(c);
        // Guid-shaped usernames (same as Trial5 Admin) — Store/ops capture stay GUID-safe on SQL Server.
        var a=SalAdmin(MoneySchemaFixture.Id());
        var b=SalAdmin(MoneySchemaFixture.Id());
        using var tx=c.BeginTransaction();
        string rider=MoneySchemaFixture.Id();
        EnsureSalPeriod(c,tx,company);
        string run=FrozenKeeta(c,tx,a,company,rider,300,0);
        var calcA=SalEvidence.Calculate(c,tx,a,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["schemeType"]="commission",
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        string riderId=calcA["riders"]![0]!["id"]!.GetValue<string>();
        SalEvidence.Calculate(c,tx,b,run,new JsonObject
        {
            ["riders"]=new JsonArray(new JsonObject
            {
                ["erpRiderId"]=rider,["schemeType"]="commission",
                ["level1"]=new JsonArray(),["level2"]=new JsonArray()
            })
        });
        // Confirm as a third person so B is not also the debt confirmer trail edge-case.
        var confirmer=SalAdmin(MoneySchemaFixture.Id());
        SalDebtConfirm.Confirm(c,tx,confirmer,run,new JsonObject{["rows"]=new JsonArray(new JsonObject{["payRunRiderId"]=riderId,["amount"]=0})});
        long journalsBefore=Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L);
        var err=Assert.ThrowsExactly<ApiError>(()=>SalSalaryApprove.Approve(c,tx,b,run,new JsonObject{["payRunRiderIds"]=new JsonArray(riderId)}));
        Assert.AreEqual(403,err.Status);
        Assert.AreEqual(journalsBefore,Convert.ToInt64(MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal")??0L));
        tx.Commit();
    }

    static ClaimsPrincipal SalAdmin(string name)
    {
        var id=new System.Security.Claims.ClaimsIdentity("synthetic");
        id.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name,name));
        id.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"Admin"));
        id.AddClaim(new System.Security.Claims.Claim("permission","pay.manage"));
        id.AddClaim(new System.Security.Claims.Claim("permission","pay.approve"));
        return new ClaimsPrincipal(id);
    }
    static DbConnection OpenSal(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,null,"IF OBJECT_ID(N'dbo.records',N'U') IS NULL CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL);");
        else
            MoneySchemaFixture.Exec(c,null,"CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL)");
        SchemaSalInvoice.Ensure(c);SchemaSalDistribution.Ensure(c);
        SchemaSalPayRun.Ensure(c);SchemaDebt.Ensure(c);
        SchemaOperationCapture.Ensure(c);SchemaSalStatement.Ensure(c);SchemaFinPeriodBalance.Ensure(c);
        return c;
    }
    static string FrozenKeeta(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string company,string rider,decimal gross,decimal commission)
    {
        string run=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,tx,"INSERT INTO sal_pay_run(id,company_id,app_code,period_month) VALUES(@id,@c,'keeta','2026-10-01')",("@id",run),("@c",company));
        var facts=new FakeOpsLiveFactsReader();
        foreach(string kind in new[]{"ninja_daily","ninja_daily_deduction","platform_account","assignment","housing","fuel","vehicle","scheme","eligibility",
            "keeta_invoice_row","hs_ops","hs_wallet","platform_ops","chefz_settlement","jahez_day"})
            facts.Rows[kind]=[];
        string account=MoneySchemaFixture.Id();
        facts.Rows["keeta_invoice_row"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["erpPlatformAccountId"]=account,["riderProfileId"]=rider,["erpRiderId"]=rider,["orders"]=1,["reportDate"]="2026-10-01",["RiderAmount"]=gross,["gross"]=gross,["commission"]=commission}];
        facts.Rows["platform_account"]=[new(){["id"]=account,["externalAccountId"]="A1",["code"]="A1"}];
        facts.Rows["assignment"]=[new(){["id"]=MoneySchemaFixture.Id(),["platformRiderAccountId"]=account,["actualRiderProfileId"]=rider,["effectiveFrom"]="2026-10-01",["effectiveTo"]="2026-10-31"}];
        facts.Rows["scheme"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["schemeCode"]="commission",["source"]="pay_settings"}];
        facts.Rows["eligibility"]=[new(){["id"]=MoneySchemaFixture.Id(),["erpRiderId"]=rider,["eligible"]=true,["source"]="pay_settings"}];
        var reasons=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var kv in facts.Rows)if(kv.Value.Count==0)reasons[kv.Key]="none";
        OpsSnapshotCapture.Capture(c,tx,actor,run,facts,reasons);
        OpsSnapshotCapture.Freeze(c,tx,actor,run);
        return run;
    }
    static void EnsureSalPeriod(DbConnection c,DbTransaction tx,string company)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",company),("@m","2026-10-01"));
        if(existing is not null and not DBNull)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at)
            VALUES(@id,@c,'2026-10-01',1,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company));
    }

    static void EnsureCompany(DbConnection c,DbTransaction tx,string companyId)
    {
        SchemaHrCompany.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);SchemaOrgCostCentre.Ensure(c,tx);
        SchemaTreasury.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);SchemaFinPeriod.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);
        var sp=FinancialCore.ResolveSponsor(companyId);
        object? exists=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId));
        if(exists is not null and not DBNull)return;
        string unified=sp.UnifiedNumber.Length>=10?sp.UnifiedNumber:"7"+companyId.Replace("-","")[..9];
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at)
            VALUES(@id,@code,@u,@n,1,{SchemaSql.NowLiteral(c)})
            """,("@id",companyId),("@code",sp.Code),("@u",unified),("@n",sp.NameAr));
    }
    static Guid EnsurePeriod(DbConnection c,DbTransaction tx,string companyId,string month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",("@c",companyId),("@m",month));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})",
            ("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month));
        return id;
    }
    static string EnsureBank(DbConnection c,DbTransaction tx,string companyId)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE company_id=@c",("@c",companyId));
        string regId;
        if(existing is not null and not DBNull)
            regId=existing switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(existing,CultureInfo.InvariantCulture)!};
        else
        {
            regId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"hr","commercial_registration")}(id,company_id,cr_number,status,created_at)
                VALUES(@id,@c,@cr,1,{SchemaSql.NowLiteral(c)})
                """,("@id",regId),("@c",companyId),("@cr",regId.Replace("-","")[..20]));
        }
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_account")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'بنك اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",regId),("@code","BNK-"+id[..8]),("@a",AccountId(c,tx,"111201")));
        return id;
    }
    static string AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        Assert.IsNotNull(id,$"missing CoA {code}");
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
}
