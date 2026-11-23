using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWT1a1F — failing-first coverage for Codex RWT1a1 findings (exact numbers).</summary>
[TestClass]
public sealed class TreasuryW2a1FixTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-rwt1a1-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-rwt1a1-checker","Admin");
    static ClaimsPrincipal NarrowBank=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-rwt1a1-narrow",["role"]="Reviewer",
        ["customPermissions"]=new JsonArray("treasury.view","treasury.bank_reconcile","treasury.vault_reconcile"),
        ["financialScope"]=new JsonObject
        {
            ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id),
            ["platforms"]=new JsonArray("Jahez","Chefz"),
            ["accountCodes"]=new JsonArray($"BANK.{FinancialCore.Sponsors[1].Code}"),
            ["categoryCodes"]=new JsonArray()
        }
    });

    [TestCleanup]
    public void Cleanup()=>ErpSettlementsApi.LiveBatchProvider=null;

    [TestMethod]
    public void Live_JahezKind_IsFinancialOperations_NotOrderCounts()
    {
        Assert.AreEqual("erp_jahez_operation",ErpSettlementsApi.LiveKindFor("jahez"));
        Assert.AreEqual("chefz_settlement",ErpSettlementsApi.LiveKindFor("chefz"));
        Assert.IsTrue(LiveOperationsQuery.HasKind("erp_jahez_operation"));
    }

    [TestMethod]
    public void Live_Paging_AdvancesSkip_250SourceYields250Unique()
    {
        // Codex probe: Skip always 0 → 400 returned / 200 unique / last 50 lost.
        Assert.AreEqual(0,ErpSettlementsApi.LivePageSkip(1,200));
        Assert.AreEqual(200,ErpSettlementsApi.LivePageSkip(2,200));
        var ids=new List<int>();
        int page=1,size=200,total=250;
        while(true)
        {
            int skip=ErpSettlementsApi.LivePageSkip(page,size);
            int take=Math.Min(size,Math.Max(0,total-skip));
            if(take<=0)break;
            ids.AddRange(Enumerable.Range(skip+1,take));
            if(ids.Count>=total||page>10)break;
            page++;
        }
        Assert.AreEqual(250,ids.Count);
        Assert.AreEqual(250,ids.Distinct().Count());
        Assert.AreEqual(250,ids.Max());
    }

    [TestMethod]
    public void Handover_CompanyIdsContract_IsInScope()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-ids",["erpSourceId"]="ho-ids",
                ["companyIds"]=new JsonArray(company),
                ["businessDate"]="2026-11-10",["date"]="2026-11-10",
                ["status"]="confirmed",["statusCode"]="2",
                ["vaultId"]="vault-main",
                ["cashboxAmount"]=100,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("M1")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="M1",["erpSourceId"]="M1",["companyId"]=company,
                ["occurredAt"]="2026-11-10T08:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=100,["vaultId"]="vault-main"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-10",["vaultId"]="vault-main"
            });
        });
        Assert.AreEqual("open",day.S("reconciliationState"));
        Assert.AreEqual(100m,day.N("handedCashAmount"));
    }

    [TestMethod]
    public void OpenDay_RequiresVaultId_NoNameMatchFallback()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-11",["vaultCode"]="togary-cash"
            });
        }));
        Assert.AreEqual(400,err.Status);
    }

    [TestMethod]
    public void BankTotals_UseDirectionAndSettlementPurpose_Only()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-bank",["companyId"]=company,["businessDate"]="2026-11-12",["date"]="2026-11-12",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=80,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-cash-bank")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-cash-bank",["erpSourceId"]="mv-cash-bank",["companyId"]=company,["occurredAt"]="2026-11-12T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=80,["vaultId"]="vault-main"
            },tx);
            // Settlement receipt 55 − disbursement 10 + fuel bank 99 ignored + purpose-less bank 40 ignored = 45
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="xfer-in",["companyId"]=company,["occurredAt"]="2026-11-12T10:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=55,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="xfer-out",["companyId"]=company,["occurredAt"]="2026-11-12T11:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="2",
                ["amount"]=10,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="xfer-fuel",["companyId"]=company,["occurredAt"]="2026-11-12T12:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Fuel",["directionCode"]="1",
                ["amount"]=99,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="xfer-loose",["companyId"]=company,["occurredAt"]="2026-11-12T13:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="",["directionCode"]="1",
                ["amount"]=40,["vaultId"]="vault-main"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-12",["vaultId"]="vault-main"
            });
        });
        Assert.AreEqual(45m,day.N("omarTransferAmount"));
        Assert.AreEqual(80m,day.N("handedCashAmount"));
    }

    [TestMethod]
    public void PurposeSplit_AccountFeeAndFuelStayExternal_NotCompanyCash()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-split",["companyId"]=company,["businessDate"]="2026-11-13",["date"]="2026-11-13",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                // Aggregates would wrongly treat fee+fuel as company cash/external without purpose filter.
                ["cashboxAmount"]=200,["bankAmount"]=0,["petroAppAmount"]=30,
                ["moneyMovementSourceIds"]=new JsonArray("mv-set","mv-fee","mv-fuel")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-set",["erpSourceId"]="mv-set",["companyId"]=company,["occurredAt"]="2026-11-13T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=100,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-fee",["erpSourceId"]="mv-fee",["companyId"]=company,["occurredAt"]="2026-11-13T09:05:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="AccountFee",["directionCode"]="1",
                ["amount"]=100,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-fuel",["erpSourceId"]="mv-fuel",["companyId"]=company,["occurredAt"]="2026-11-13T09:10:00Z",
                ["fund"]="PetroApp",["fundCode"]="2",["purposeCode"]="Fuel",["directionCode"]="1",
                ["amount"]=30,["vaultId"]="vault-main"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-13",["vaultId"]="vault-main"
            });
        });
        Assert.AreEqual(100m,day.N("handedCashAmount")); // settlement only
        Assert.AreEqual(130m,day.N("handedExternalAmount")); // fee 100 + fuel 30
        Assert.AreEqual(0m,day.N("handedBankAmount"));
    }

    [TestMethod]
    public void ApproveDay_WritesJournalIdOnReconLines()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var approved=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-jl",["companyId"]=company,["businessDate"]="2026-11-14",["date"]="2026-11-14",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=60,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-jl")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-jl",["erpSourceId"]="mv-jl",["companyId"]=company,["occurredAt"]="2026-11-14T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=60,["vaultId"]="vault-main"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-14",["vaultId"]="vault-main"
            });
            JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="فرق تحويل مقبول للاختبار"});
            JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id"));
            return JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id"));
        });
        Assert.AreEqual("approved",approved.S("status"));
        f.Store.Read(c=>
        {
            object? linked=SchemaSql.Scalar(c,null,$"""
                SELECT COUNT(*) FROM {SchemaSql.Q(c,"treasury","jahez_recon_line")}
                WHERE recon_run_id=@id AND journal_id IS NOT NULL
                """,("@id",approved.S("id")));
            Assert.AreEqual(1,Convert.ToInt32(linked,CultureInfo.InvariantCulture));
            return true;
        });
    }

    [TestMethod]
    public void CorrectDay_RejectsCallerTotals_RequiresIndependent_UsesSourceAmounts()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string dayId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-corr",["companyId"]=company,["businessDate"]="2026-11-15",["date"]="2026-11-15",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=90,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-corr-cash")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-corr-cash",["erpSourceId"]="mv-corr-cash",["companyId"]=company,["occurredAt"]="2026-11-15T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=90,["vaultId"]="vault-main"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="xfer-corr",["companyId"]=company,["occurredAt"]="2026-11-15T12:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=70,["vaultId"]="vault-main"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-15",["vaultId"]="vault-main"
            });
            dayId=day.S("id");
            JahezReconciliation.SubmitDay(c,tx,Maker,dayId);
            JahezReconciliation.DecideDayVariance(c,tx,Maker,dayId,new(){["varianceDecision"]="فرق تحويل مقبول للاختبار"});
            JahezReconciliation.ApproveDay(c,tx,Checker,dayId);
            return true;
        });
        // Same actor as approver → blocked
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            JahezReconciliation.CorrectDay(c,tx,Checker,dayId,new(){["notes"]="محاولة اعتماد سابق"}))).Status);
        // Caller totals rejected
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
            JahezReconciliation.CorrectDay(c,tx,Maker,dayId,new(){["clearingAmount"]=1,["transferAmount"]=1}))).Status);
        // Independent maker may correct from source only → 90 / 70
        var corrected=f.Store.Write((c,tx)=>JahezReconciliation.CorrectDay(c,tx,Maker,dayId,new()
        {
            ["notes"]="تصحيح من المصدر"
        }));
        Assert.AreEqual(90m,corrected.N("handedAmount"));
        Assert.AreEqual(70m,corrected.N("omarTransferAmount"));
        Assert.IsTrue(corrected["correctionJournalEntryIds"]!.AsArray().Count>=1);
    }

    [TestMethod]
    public void ChefzFreeze_RejectsUnrelatedSameMonthSettlement()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string settleA=SchemaSql.GuidText(SchemaSql.NewId());
        string settleB=SchemaSql.GuidText(SchemaSql.NewId());
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=settleA,["erpSourceId"]=settleA,["companyId"]=company,["platformAccountId"]="acc-A",
                ["periodFrom"]="2026-11-01",["periodTo"]="2026-11-15",["gross"]=100,["companyAmount"]=15,["riderAmount"]=85,["sourceDeleted"]=false
            },tx);
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=settleB,["erpSourceId"]=settleB,["companyId"]=company,["platformAccountId"]="acc-B",
                ["periodFrom"]="2026-11-01",["periodTo"]="2026-11-15",["gross"]=200,["companyAmount"]=30,["riderAmount"]=170,["sourceDeleted"]=false
            },tx);
            Store.Save(c,"platform_invoice",new()
            {
                ["id"]="inv-bind",["companyId"]=company,["platform"]="Chefz",["period"]="2026-11",
                ["approvalStatus"]="approved",["status"]="approved"
            },tx);
            Store.Save(c,"platform_invoice_line",new()
            {
                ["id"]="inv-line-a",["invoiceId"]="inv-bind",["companyId"]=company,["platformAccountId"]="acc-A",
                ["period"]="2026-11",["platform"]="Chefz"
            },tx);
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ErpSettlementsApi.FreezeChefzMoney(c,tx,Maker,new()
        {
            ["companyId"]=company,["invoiceId"]="inv-bind",["settlementId"]=settleB
        }))).Status);
        var ok=f.Store.Write((c,tx)=>ErpSettlementsApi.FreezeChefzMoney(c,tx,Maker,new()
        {
            ["companyId"]=company,["invoiceId"]="inv-bind",["settlementId"]=settleA
        }));
        Assert.AreEqual("frozen",ok.S("status"));
    }

    [TestMethod]
    public void BankAndVaultReads_EnforceAccountScope()
    {
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
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>TreasuryBankStatementsApi.View(c,null,NarrowBank,statementId))).Status);
        var list=f.Store.Read(c=>TreasuryVaultCountsApi.List(c,null,NarrowBank,company));
        Assert.AreEqual(0,list["items"]!.AsArray().Count);
        Assert.AreNotEqual("",countId);
    }

    [TestMethod]
    public void BalancesTree_GroupsByCommercialRegistration()
    {
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
    }

    [TestMethod]
    public void VaultCount_BookBalance_AsOfCountDate_ExcludesLaterMovements()
    {
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
            // Opening 200; monthly closing 300 must not be used. Later Nov-20 lines block a Nov-10 count.
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,200,100,0,300,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            string jid=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,created_at)
                VALUES(@id,@id,@p,@c,'2026-11-01','2026-11-20','synthetic',@id,'manual',1,0,{SchemaSql.NowLiteral(c)})
                """,("@id",jid),("@p",SchemaSql.GuidText(periodId)),("@c",company));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,100,0,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),
                ("@j",jid),("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,100,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),
                ("@j",jid),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",jid));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-10",["countedAmount"]=200
            })).Status);
            return true;
        });
    }

    [TestMethod]
    public void BankAdjust_StillBlocked409_Regression()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string adjustId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=5,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=5,["reference"]="TIMING"})
            });
            adjustId=TreasuryBankStatementsApi.RequestAdjust(c,tx,Maker,uploaded.S("id"),new(){["amount"]=5,["reason"]="فرق توقيت بنكي"}).S("id");
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryBankStatementsApi.ApproveAdjust(c,tx,Checker,adjustId))).Status);
    }

    [TestMethod]
    public void AutoMatch_BlankReferenceUnmatched_AndCompetingRowsDoNotSteal()
    {
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
            string journalId=SchemaSql.GuidText(SchemaSql.NewId()),lineId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,created_at)
                VALUES(@id,'V-REF',@p,@c,'2026-11-01','2026-11-10','synthetic',@id,'manual',1,0,{SchemaSql.NowLiteral(c)})
                """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,narration_ar,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,100,0,'REF-ONE',{SchemaSql.NowLiteral(c)})
                """,("@id",lineId),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),
                ("@a",bankAcc),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,100,{SchemaSql.NowLiteral(c)})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),
                ("@j",journalId),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=300,
                ["lines"]=new JsonArray(
                    new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]=""},
                    new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]="REF-ONE"},
                    new JsonObject{["occurredOn"]="2026-11-10",["amount"]=100,["reference"]="REF-ONE"})
            });
            var matched=TreasuryBankStatementsApi.AutoMatch(c,tx,Maker,uploaded.S("id"));
            Assert.AreEqual(1m,matched.N("matchedCount")); // blank skipped; second competing row unmatched
            Assert.AreEqual(2,matched["unmatchedLineIds"]!.AsArray().Count);
            return true;
        });
    }

    [TestMethod]
    public void PullContract_ChefzSettlement_MapsMixedRateFields()
    {
        var contract=PullContracts.Catalog.Single(c=>c.Kind=="chefz_settlement");
        Assert.IsTrue(contract.Fields.Contains("FullTimeFeeAmount",StringComparison.Ordinal));
        Assert.IsTrue(contract.Fields.Contains("FreelancerCommissionBase",StringComparison.Ordinal));
        Assert.IsTrue(contract.Fields.Contains("FullTimeDays",StringComparison.Ordinal));
        Assert.IsTrue(contract.Map.Contains("fullTimeFeeAmount:FullTimeFeeAmount",StringComparison.Ordinal));
        Assert.IsTrue(contract.Map.Contains("freelancerCommissionBase:FreelancerCommissionBase",StringComparison.Ordinal));
        Assert.IsTrue(contract.Map.Contains("fullTimeDays:FullTimeDays",StringComparison.Ordinal));
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
