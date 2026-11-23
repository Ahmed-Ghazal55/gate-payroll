using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWT1a2F — failing-first coverage for Codex RWT1a2 findings (exact numbers).</summary>
[TestClass]
public sealed class TreasuryW2a2FixTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-rwt1a2-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-rwt1a2-checker","Admin");
    /// <summary>Company in scope, but accountCodes point at another sponsor's bank — excludes cash 111103 and this company's BANK.*.</summary>
    static ClaimsPrincipal NarrowOtherBank=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-rwt1a2-narrow",["role"]="Reviewer",
        ["customPermissions"]=new JsonArray("treasury.view","treasury.bank_reconcile","treasury.vault_reconcile"),
        ["financialScope"]=new JsonObject
        {
            ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id),
            ["platforms"]=new JsonArray("Jahez","Chefz"),
            ["accountCodes"]=new JsonArray($"BANK.{FinancialCore.Sponsors[1].Code}"),
            ["categoryCodes"]=new JsonArray()
        }
    });

    [TestMethod]
    public void UntiedHandover_WithoutVaultOrMovement_IsExcluded_AndBlocksApprove()
    {
        // Finding JahezReconciliation.cs:513 — Omar handover with no vault id must not match every vault.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-untied",["companyId"]=company,["businessDate"]="2026-11-16",["date"]="2026-11-16",
                ["status"]="confirmed",["statusCode"]="2",
                // no vaultId — must stay out of vault-main day
                ["cashboxAmount"]=80,["bankAmount"]=0,["petroAppAmount"]=0
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-16",["vaultId"]="vault-main"
            });
        });
        Assert.AreEqual("waiting_handover",day.S("reconciliationState"));
        Assert.AreEqual(0m,day.N("handedCashAmount"));
        Assert.AreEqual(0,day.N("lineCount"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id"));
            return true;
        })).Status);
    }

    [TestMethod]
    public void UntiedHandover_MatchesOnlyViaSettlementMovementVault()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-move-vault",["erpSourceId"]="ho-move-vault",["companyId"]=company,
                ["businessDate"]="2026-11-16",["date"]="2026-11-16",
                ["status"]="confirmed",["statusCode"]="2",
                ["cashboxAmount"]=55,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-v")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-v",["erpSourceId"]="mv-v",["companyId"]=company,["occurredAt"]="2026-11-16T10:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=55,["vaultId"]="vault-main",["handoverId"]="ho-move-vault"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-16",["vaultId"]="vault-main"
            });
        });
        Assert.AreEqual("open",day.S("reconciliationState"));
        Assert.AreEqual(55m,day.N("handedCashAmount"));
    }

    [TestMethod]
    public void NoMovementLinks_PostNoCompanyCash_AllExternal_BlocksApprove()
    {
        // Finding JahezReconciliation.cs:464 — aggregates without fund/purpose must not clear as company cash.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string dayId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-agg",["companyId"]=company,["businessDate"]="2026-11-17",["date"]="2026-11-17",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=120,["bankAmount"]=40,["petroAppAmount"]=10
                // no moneyMovementSourceIds
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-17",["vaultId"]="vault-main"
            });
            dayId=day.S("id");
            Assert.AreEqual(0m,day.N("handedCashAmount"));
            Assert.AreEqual(0m,day.N("handedBankAmount"));
            Assert.AreEqual(170m,day.N("handedExternalAmount")); // 120+40+10 stay external
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            JahezReconciliation.SubmitDay(c,tx,Maker,dayId);
            return true;
        })).Status);
    }

    [TestMethod]
    public void ApproveDay_RecomputesFromSource_AndPostsNegativeBankAsDisbursement()
    {
        // Finding JahezReconciliation.cs:242 — recompute at approve; signed bank &lt; 0 posts opposite accounts.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var approved=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-recomp",["companyId"]=company,["businessDate"]="2026-11-18",["date"]="2026-11-18",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=90,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-cash-re")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-cash-re",["erpSourceId"]="mv-cash-re",["companyId"]=company,["occurredAt"]="2026-11-18T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=90,["vaultId"]="vault-main"
            },tx);
            // Net bank settlement out: receipt 30 − disbursement 100 = −70
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-bank-in",["companyId"]=company,["occurredAt"]="2026-11-18T11:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=30,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-bank-out",["companyId"]=company,["occurredAt"]="2026-11-18T12:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="2",
                ["amount"]=100,["vaultId"]="vault-main"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-18",["vaultId"]="vault-main"
            });
            Assert.AreEqual(90m,day.N("handedCashAmount"));
            Assert.AreEqual(-70m,day.N("omarTransferAmount"));
            // Stale stored amounts must not win at approve: mutate document to wrong values.
            day["handedAmount"]=1;day["omarTransferAmount"]=1;
            Store.Save(c,"jahez_recon_day",day,tx);
            JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="صرف بنكي صافٍ مقبول للاختبار"});
            JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id"));
            return JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id"));
        });
        Assert.AreEqual("approved",approved.S("status"));
        Assert.AreEqual(90m,approved.N("handedAmount"));
        Assert.AreEqual(-70m,approved.N("omarTransferAmount"));
        Assert.AreEqual(2,approved["journalEntryIds"]!.AsArray().Count); // clearing + disbursement
        // Disbursement journal: Dr receivable 112801 / Cr bank 111204 for 70
        f.Store.Read(c=>
        {
            object? bankCredit=SchemaSql.Scalar(c,null,$"""
                SELECT jl.credit FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                WHERE j.source_type='jahez_recon_day' AND j.source_id=@sid AND a.code='111204' AND jl.credit>0
                """,("@sid",approved.S("id")));
            Assert.AreEqual(70m,Convert.ToDecimal(bankCredit,CultureInfo.InvariantCulture));
            return true;
        });
    }

    [TestMethod]
    public void InvoiceBypass_DoesNotCover_JahezDayApprove()
    {
        // Finding JahezReconciliation.cs:230 — invoice bypass must not waive maker-checker on Jahez day.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string dayId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"user",new(){["id"]="u-inv-bypass",["username"]="synthetic-rwt1a2-maker",["role"]="Admin",["status"]="active"},tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new()
            {
                ["username"]="synthetic-rwt1a2-maker",["from"]="2026-11-01",["to"]="2026-12-31",
                ["reason"]="invoice only for Ninja parallel",["scope"]=AccessPolicy.ScopeInvoiceApprovals
            });
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-bypass",["companyId"]=company,["businessDate"]="2026-11-19",["date"]="2026-11-19",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=25,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-byp")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-byp",["erpSourceId"]="mv-byp",["companyId"]=company,["occurredAt"]="2026-11-19T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=25,["vaultId"]="vault-main"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-19",["vaultId"]="vault-main"
            });
            dayId=day.S("id");
            JahezReconciliation.SubmitDay(c,tx,Maker,dayId);
            return true;
        });
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopeInvoiceApprovals,AccessPolicy.ScopeTreasuryJahez));
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,AccessPolicy.ScopeTreasuryJahez));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            JahezReconciliation.ApproveDay(c,tx,Maker,dayId))).Status);
    }

    [TestMethod]
    public void ApproveDay_StampsClearingOnCashLines_AndTransferOnBankLines()
    {
        // Finding JahezReconciliation.cs:256 — clearing vs transfer journal ids on the right lines.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var approved=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-stamp",["companyId"]=company,["businessDate"]="2026-11-20",["date"]="2026-11-20",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=40,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-c40")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-c40",["erpSourceId"]="mv-c40",["companyId"]=company,["occurredAt"]="2026-11-20T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=40,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-b25",["companyId"]=company,["occurredAt"]="2026-11-20T10:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=25,["vaultId"]="vault-main"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-20",["vaultId"]="vault-main"
            });
            JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="فرق تحويل مقبول للاختبار"});
            JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id"));
            return JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id"));
        });
        Assert.AreEqual(2,approved["journalEntryIds"]!.AsArray().Count);
        f.Store.Read(c=>
        {
            using var cmd=Store.Command(c,null,$"""
                SELECT expected_amount,handed_amount,journal_id FROM {SchemaSql.Q(c,"treasury","jahez_recon_line")}
                WHERE recon_run_id=@id ORDER BY handed_amount DESC
                """,("@id",approved.S("id")));
            using var r=cmd.ExecuteReader();
            Assert.IsTrue(r.Read());
            Assert.AreEqual(40m,Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture));
            string cashJournal=r.IsDBNull(2)?"":r.GetValue(2) switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(r.GetValue(2),CultureInfo.InvariantCulture)!};
            Assert.AreNotEqual("",cashJournal);
            Assert.IsTrue(r.Read());
            Assert.AreEqual(25m,Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture)); // Omar transfer line
            string bankJournal=r.IsDBNull(2)?"":r.GetValue(2) switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(r.GetValue(2),CultureInfo.InvariantCulture)!};
            Assert.AreNotEqual("",bankJournal);
            Assert.AreNotEqual(cashJournal,bankJournal);
            Assert.IsFalse(r.Read());
            return true;
        });
    }

    [TestMethod]
    public void VaultCount_BookBalance_AsOfCountDate_RejectsLaterPostedLines()
    {
        // Finding TreasuryVaultCountsApi.cs:196 — book through count date; later lines block the count.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string box=EnsureCashBox(c,tx,company,reg,"111103");
            string accountId=AccountId(c,tx,"111103");
            string equity=AccountId(c,tx,"212103");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,200,100,0,300,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            // Receipt on count date Nov 10 → included in book (opening 200 + 50 = 250).
            string jEarly=PostSyntheticJournal(c,tx,company,periodId,accountId,equity,"2026-11-10",50);
            Assert.AreNotEqual("",jEarly);
            var ok=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-10",["countedAmount"]=250
            });
            Assert.AreEqual(250m,ok.N("bookBalance"));
            Assert.AreEqual(0m,ok.N("variance"));
            // Later posted line → reject new count on same box for an earlier cutoff.
            PostSyntheticJournal(c,tx,company,periodId,accountId,equity,"2026-11-20",100);
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-10",["countedAmount"]=250
            })).Status);
            return true;
        });
    }

    [TestMethod]
    public void PayBypass_DoesNotCover_VaultCountApprove()
    {
        // Finding TreasuryVaultCountsApi.cs:105 — pay bypass must not waive vault-count maker-checker.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string countId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"user",new(){["id"]="u-pay-bypass",["username"]="synthetic-rwt1a2-maker",["role"]="Admin",["status"]="active"},tx);
            AccessPolicy.GrantBypass(c,tx,PayFixture.Maker,new()
            {
                ["username"]="synthetic-rwt1a2-maker",["from"]="2026-11-01",["to"]="2026-12-31",
                ["reason"]="pay approvals only",["scope"]=AccessPolicy.ScopePayApprovals
            });
            string reg=EnsureRegistration(c,tx,company);
            string box=EnsureCashBox(c,tx,company,reg,"111103");
            string accountId=AccountId(c,tx,"111103");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,40,0,0,40,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-12",["countedAmount"]=40
            });
            countId=created.S("id");
            TreasuryVaultCountsApi.Submit(c,tx,Maker,countId);
            return true;
        });
        Assert.IsFalse(AccessPolicy.ScopeCovers(AccessPolicy.ScopePayApprovals,AccessPolicy.ScopeTreasuryVaultCount));
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            TreasuryVaultCountsApi.Approve(c,tx,Maker,countId))).Status);
    }

    [TestMethod]
    public void AutoMatch_RequiresReference_BlankDoesNotSteal()
    {
        // Finding TreasuryBankStatementsApi.cs:114 — blank reference never auto-matches.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            string bankAcc=AccountId(c,tx,"111201");
            string equity=AccountId(c,tx,"212103");
            PostSyntheticJournal(c,tx,company,periodId,bankAcc,equity,"2026-11-10",80,narration:"REF-TWO",voucher:"V-REF2");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=160,
                ["lines"]=new JsonArray(
                    new JsonObject{["occurredOn"]="2026-11-10",["amount"]=80,["reference"]=""},
                    new JsonObject{["occurredOn"]="2026-11-10",["amount"]=80,["reference"]="REF-TWO"})
            });
            var matched=TreasuryBankStatementsApi.AutoMatch(c,tx,Maker,uploaded.S("id"));
            Assert.AreEqual(1m,matched.N("matchedCount"));
            Assert.AreEqual(1,matched["unmatchedLineIds"]!.AsArray().Count);
            return true;
        });
    }

    [TestMethod]
    public void BankAndVaultReads_SkipRowsOutsideAccountScope()
    {
        // Finding TreasuryBankStatementsApi.cs:211 — read/list enforce account scope.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="",countId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            string box=EnsureCashBox(c,tx,company,reg,"111103");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=5,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=5,["reference"]="R1"})
            });
            statementId=uploaded.S("id");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            string accountId=AccountId(c,tx,"111103");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,0,50,0,50,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-10",["countedAmount"]=50
            });
            countId=created.S("id");
            return true;
        });
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,NarrowOtherBank,statementId))).Status);
        var list=f.Store.Read(c=>TreasuryVaultCountsApi.List(c,null,NarrowOtherBank,company));
        Assert.AreEqual(0,list["items"]!.AsArray().Count);
        Assert.AreNotEqual("",countId);
    }

    [TestMethod]
    public void BalancesTree_RegistrationNodeUnderCompany()
    {
        // Finding TreasuryBalancesApi.cs:54 — registration node; accounts under registration.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string regId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            regId=EnsureRegistration(c,tx,company);
            EnsureBankAccount(c,tx,company,regId,"111201");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            SeedBalance(c,tx,company,periodId,"111201",120);
            return true;
        });
        var tree=f.Store.Read(c=>TreasuryBalancesApi.Tree(c,null,Maker,"2026-11",company));
        var companyNode=tree["items"]!.AsArray().OfType<JsonObject>().Single(n=>n.S("companyId")==company);
        var regs=companyNode["children"]!.AsArray().OfType<JsonObject>().Where(ch=>ch.S("kind")=="registration").ToList();
        Assert.AreEqual(1,regs.Count);
        Assert.AreEqual(regId,regs[0].S("registrationId"));
        var bank=regs[0]["children"]!.AsArray().OfType<JsonObject>().Single(ch=>ch.S("accountCode")=="111201");
        Assert.AreEqual(120m,bank.N("balance"));
        Assert.AreEqual(120m,companyNode.N("bank"));
    }

    static string PostSyntheticJournal(DbConnection c,DbTransaction tx,string company,Guid periodId,string debitAccount,string creditAccount,string journalDate,decimal amount,string narration="",string voucher="")
    {
        string journalId=SchemaSql.GuidText(SchemaSql.NewId());
        string voucherNo=voucher==""?journalId:voucher;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal")}
            (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,created_at)
            VALUES(@id,@v,@p,@c,'2026-11-01',@d,'synthetic',@id,'manual',1,0,{SchemaSql.NowLiteral(c)})
            """,("@id",journalId),("@v",voucherNo),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@d",journalDate));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
            (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,narration_ar,created_at)
            VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,@amt,0,@n,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),
            ("@a",debitAccount),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@amt",amount),("@n",narration));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
            (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,@amt,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),
            ("@j",journalId),("@a",creditAccount),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@amt",amount));
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));
        return journalId;
    }
    static void SeedBalance(DbConnection c,DbTransaction tx,string company,Guid periodId,string code,decimal closing)
    {
        string accountId=AccountId(c,tx,code);
        Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
        byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
            (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
            VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,0,@d,0,@cl,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
            ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@d",closing),("@cl",closing),("@dim",dim));
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
