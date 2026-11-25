using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWD1bF: failing-first coverage for codex-RWD1b residual findings (exact money amounts).</summary>
[TestClass]
public sealed class DebtRwd1bFixTests
{
    static ClaimsPrincipal Maker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1b-maker",["department"]="FIN",["rankLevel"]=3,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","approvals.decide")
    });
    static ClaimsPrincipal Checker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1b-checker",["department"]="FIN",["rankLevel"]=4,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","approvals.decide","treasury.journals")
    });
    static ClaimsPrincipal NarrowChecker=>UserAccess.Principal(new()
    {
        ["role"]="Accountant",["username"]="rwd1b-narrow",["department"]="OPS",["rankLevel"]=2,
        ["customPermissions"]=new JsonArray("approvals.decide","debts.waive"),
        ["financialScope"]=new JsonObject
        {
            ["sponsorIds"]=new JsonArray("EX"),
            ["accountCodes"]=new JsonArray(),
            ["platforms"]=new JsonArray("Ninja"),
            ["categoryCodes"]=new JsonArray("CashAdvance","PlatformTransferredDebt")
        }
    });

    [TestMethod,DataRow(false)]
    public void H1_ReduceApprovalAmountIsTotal_NotZero(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',100,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // Rank C / department FIN limit seed is typically low; amount 25 must be checked (not skipped via 0).
            SchemaApprovals.Ensure(c,null!);
            ApprovalsArea.EnsureLimitsSeeded(c,null);
            Store.Save(c,"approval_limit",new(){["rank"]="A+",["department"]="FIN",["limitSar"]=10m,["unlimited"]=false,["updatedBy"]="rwd1b"},null!);
            byte[] file=Encoding.UTF8.GetBytes("iqama,debt_id,amount,reason\n2000000001,"+debt+",25,تصحيح رصيد افتتاحي");
            var preview=DebtLedger.PreviewReduce(c,null,companyG,Maker,file,"reduce.csv",new DateOnly(2026,11,1));
            Assert.AreEqual(25m,preview.N("totalReduce"));
            using var tx=c.BeginTransaction();
            var err=Assert.ThrowsExactly<ApiError>(()=>            DebtLedger.CommitReduce(c,tx,companyG,periodG,new DateOnly(2026,11,1),Maker,Checker,file,"reduce.csv",preview.S("fingerprint"),
                DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"331301")));
            Assert.AreEqual(403,err.Status);
            Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            tx.Rollback();
        }
    }

    [TestMethod,DataRow(false)]
    public void H1_UnrelatedApprovalRequestCannotSupplyMaker(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            string otherRef=MoneySchemaFixture.Id();
            string requestId=SchemaSql.GuidText(SchemaSql.NewId());
            using(var tx=c.BeginTransaction())
            {
                SchemaApprovals.SaveRequest(c,tx,new()
                {
                    ["id"]=requestId,["kind"]="debt_waiver",["status"]="pending",["referenceId"]=otherRef,
                    ["requestedBy"]="rwd1b-maker",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["amountSar"]=15m,
                    ["titleAr"]="إعفاء",["fromDepartment"]="FIN"
                });
                tx.Commit();
            }
            var input=new JsonObject{["approvalRequestId"]=requestId,["amount"]=15,["reason"]="إعفاء"};
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveMakerForTests(c,null!,input,Checker,expectedReference:debt,expectedAmount:15m)).Status);
        }
    }

    [TestMethod]
    public void H2_PostCreationDisbursementBlockedInsideSharedPoster()
    {
        var debt=new JsonObject
        {
            ["id"]="syn-disb-1",["category"]="CashAdvance",["entryMode"]="Disbursement",["allocationMode"]="RiderFull",
            ["totalAmount"]=50m,["totalCostAmount"]=50m,["companyShareAmount"]=0m,["originSponsorId"]="BW",["creditorSponsorId"]="BW",
            ["paymentVaultCode"]="111101",["iqama"]="2000000001",["sourceRef"]="SYN-DISB",["debtorType"]="Rider"
        };
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.PostCreationJournalIfNeeded(null!,null!,debt,"rwd1b-checker")).Status);
    }

    [TestMethod,DataRow(false)]
    public void H4_UnknownPlatformWaiverRejected_Not526601(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            DebtLedger.EnsureD11WaiverAccounts(c,null);
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='PlatformTransferredDebt',platform='Other' WHERE id=@id",("@id",debt));
            using var tx=c.BeginTransaction();
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt))).Status);
            MoneySchemaFixture.Exec(c,tx,"UPDATE debt_debt SET platform='Keeta' WHERE id=@id",("@id",debt));
            Assert.AreEqual("526801",DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt)).Code);
        }
    }

    [TestMethod,DataRow(false)]
    public void H5_LegacyStatusConstraintAllowsAbsorbedAfterUpgrade(bool sqlServer)
    {
        if(sqlServer){Assert.Inconclusive("SQLite upgrade path");return;}
        using var c=MoneySchemaFixture.Payments(false);
        string company=MoneySchemaFixture.Company(c);
        // Simulate pre-fix table: status CHECK without absorbed, then SchemaDebt.Ensure upgrades it.
        MoneySchemaFixture.Exec(c,null,"""
            CREATE TABLE debt_debt(
              id TEXT NOT NULL PRIMARY KEY, erp_rider_id TEXT NOT NULL, company_id TEXT NOT NULL,
              category TEXT NOT NULL, principal REAL NOT NULL, balance REAL NOT NULL DEFAULT 0,
              instalment REAL NULL, status TEXT NOT NULL DEFAULT 'open'
              CHECK(status IN ('open','settled','waived','cancelled','transferred')),
              promissory_flag INTEGER NOT NULL DEFAULT 0, created_by TEXT NOT NULL DEFAULT 't', created_at TEXT NOT NULL DEFAULT '2026-11-01'
            );
            """);
        string debt=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt(id,erp_rider_id,company_id,category,principal,balance,status) VALUES(@id,@id,@c,'CashAdvance',20,20,'open')",("@id",debt),("@c",company));
        Assert.ThrowsExactly<Microsoft.Data.Sqlite.SqliteException>(()=>
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET status='absorbed' WHERE id=@id",("@id",debt)));
        SchemaDebt.Ensure(c);
        MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET balance=0,status='absorbed' WHERE id=@id",("@id",debt));
        Assert.AreEqual("absorbed",(string)MoneySchemaFixture.Scalar(c,null,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);
    }

    [TestMethod,DataRow(false)]
    public void H6_WaiverDemandsCheckerScope_AndNoteUsesBeneficiarySponsor(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',20,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            using var tx=c.BeginTransaction();
            Guid recv=DebtLedger.AccountId(c,tx,"112501"),exp=DebtLedger.AccountId(c,tx,"526601");
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Waive(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),Maker,NarrowChecker,5m,"إعفاء","526601",recv,exp)).Status);

            string noteId="note-"+MoneySchemaFixture.Id()[..8];
            // Ownership is beneficiarySponsorId (FinancialCore promissory field); companyId alone must not authorize.
            Store.Save(c,"promissory_note",new(){["id"]=noteId,["beneficiarySponsorId"]="EX",["companyId"]="BW",["amount"]=20m,["status"]="active"},tx);
            var bwOnly=UserAccess.Principal(new()
            {
                ["role"]="FinancialAccountant",["username"]="rwd1b-bw",["department"]="FIN",["rankLevel"]=3,
                ["customPermissions"]=new JsonArray("debts.create.administrative"),
                ["financialScope"]=new JsonObject
                {
                    ["sponsorIds"]=new JsonArray("BW"),
                    ["accountCodes"]=new JsonArray(FinancialCore.ChartOfAccounts.Where(a=>a.IsVault).Select(a=>(JsonNode?)a.Code).ToArray()),
                    ["platforms"]=new JsonArray("Ninja","Keeta","HungerStation","Jahez","Chefz","Amazon","Other"),
                    ["categoryCodes"]=new JsonArray(FinancialCore.CategoriesCatalog.Select(x=>(JsonNode?)x.Code).ToArray())
                }
            });
            Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>DebtLedger.LinkPromissory(c,tx,noteId,Guid.Parse(debt),bwOnly)).Status);
        }
    }

    [TestMethod,DataRow(false)]
    public void H7_MissingOpenPeriodCarryRefuses_AndInstalmentUsesLedgerBalance(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            MoneySchemaFixture.Exec(c,null,"DELETE FROM fin_accounting_period");
            var cycle=new JsonObject{["id"]="cyc-rwd1b",["companyId"]=company,["period"]="2026-11",["createdBy"]="rwd1b-maker",["app"]="ninja"};
            var row=new JsonObject{["rider"]="2000000999",["net"]=-40m};
            using(var tx=c.BeginTransaction())
            {
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>PayCycles.CreateCarryForTests(c,tx,cycle,row,Maker)).Status);
                Assert.AreEqual(0,Store.List(c,"debt",tx).Count(d=>d.S("category")=="NegativeSalaryCarryover"));
            }

            // Restore open period; instalment against W1 debt must move ledger balance 80→55 (exact 25).
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,datetime('now'))",("@id",SchemaSql.GuidText(periodG)),("@c",company));
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',80,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            var doc=new JsonObject
            {
                ["id"]=debt,["iqama"]="2000000001",["category"]="CashAdvance",["creditorSponsorId"]=company,
                ["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=80m,["remainingBalance"]=80m,["paidAmount"]=0m
            };
            using var tx2=c.BeginTransaction();
            Store.Save(c,"debt",doc,tx2);
            var result=new PayrollResult("2000000001","2026-11","pay_cycle",0,100,100,100,0,0,[new PayLine("debt_installment","قسط",-25m,debt)],[]);
            Store.Save(c,"pay_cycle",new(){["id"]="pc-rwd1b",["companyId"]=company,["period"]="2026-11",["sponsorId"]=company,["debtDeductionApprovedBy"]="rwd1b-checker"},tx2);
            Debts.Post(c,tx2,result,"pc-rwd1b",payCycle:true);
            Assert.AreEqual(55m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx2,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx2,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND amount=-25",("@d",debt)));
            Assert.AreEqual(55m,Store.Get(c,debt,"debt",tx2)!.N("remainingBalance"));
        }
    }

    [TestMethod,DataRow(false)]
    public void H8_LegacyOpeningImportHashesSourceFileNotPreviewToken(bool sqlServer)
    {
        byte[] file=Encoding.UTF8.GetBytes("synthetic-opening-file-bytes-rwd1b");
        string expected=Convert.ToHexString(SHA256.HashData(file));
        string tokenHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("999999.deadbeef")));
        Assert.AreNotEqual(expected,tokenHash,ignoreCase:true);
        Assert.AreEqual(expected,Convert.ToHexString(Debts.SourceFileHashForTests(file)),ignoreCase:true);
        Assert.AreNotEqual(expected,Convert.ToHexString(Debts.SourceFileHashForTests(Encoding.UTF8.GetBytes("999999.deadbeef"))),ignoreCase:true);
    }

    [TestMethod,DataRow(false)]
    public void M4_EntryErrorReduceReversesOpeningContra_NotSameReceivable(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            Guid recv=DebtLedgerFixture.Acc(c,"112501"),equity=DebtLedgerFixture.Acc(c,"331301");
            using var tx=c.BeginTransaction();
            var opening=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                companyG,periodG,new DateOnly(2026,11,1),new DateOnly(2026,11,5),"debt_opening",Guid.Parse(debt),"debt_opening",0,false,"rwd1b-maker",
                [new(recv,SchemaOrgCostCentre.NoneId,"none",100m,0m,"افتتاح"),new(equity,SchemaOrgCostCentre.NoneId,"none",0m,100m,"حقوق ملكية")],
                NarrationAr:"افتتاح"));
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id,journal_id,created_by,created_at)
                VALUES(@id,@d,@c,'2026-11-01','2026-11-05',100,'synthetic',@sid,@j,'rwd1b-maker',datetime('now'))
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@d",debt),("@c",company),("@sid",SchemaSql.GuidText(SchemaSql.NewId())),("@j",SchemaSql.GuidText(opening.JournalId)));
            Guid contra=DebtLedger.ResolveReduceContra(c,tx,"خطأ إدخال",null,Guid.Parse(debt));
            Assert.AreEqual(equity,contra);
            Assert.AreNotEqual(recv,contra);
        }
    }

    [TestMethod,DataRow(false)]
    public void M5_CollectRefusesCashOutsideActorAccountScope(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',50,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // Chart vault exists in fin.account; actor has company scope but no assigned vault codes.
            string vault="MAIN.BW.DEBT";
            if(MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_account WHERE code=@c",("@c",vault)) is 0L or 0)
                MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_account(id,code,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_at) VALUES(@id,@c,@c,1,1,1,1,1,0,6,datetime('now'))",("@id",MoneySchemaFixture.Id()),("@c",vault));
            var noVault=UserAccess.Principal(new()
            {
                ["role"]="FinancialAccountant",["username"]="rwd1b-novault",["department"]="FIN",["rankLevel"]=3,
                ["customPermissions"]=new JsonArray("debts.collect"),
                ["financialScope"]=new JsonObject
                {
                    ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors.Select(s=>(JsonNode?)s.Id).ToArray()),
                    ["accountCodes"]=new JsonArray(),
                    ["platforms"]=new JsonArray("Ninja","Keeta","HungerStation","Jahez","Chefz","Amazon","Other"),
                    ["categoryCodes"]=new JsonArray(FinancialCore.CategoriesCatalog.Select(x=>(JsonNode?)x.Code).ToArray())
                }
            });
            using var tx=c.BeginTransaction();
            var err=Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),noVault,10m,"تحصيل",
                    DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,vault),0,false));
            Assert.AreEqual(403,err.Status,err.Message);
            Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        }
    }

    [TestMethod]
    public void NewHigh_LegacySingleCollectWaiverRefuses5202()
    {
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>Debts.DemandLegacyCollectWaiverClosed(12.5m)).Status);
        Debts.DemandLegacyCollectWaiverClosed(0m);
    }

}