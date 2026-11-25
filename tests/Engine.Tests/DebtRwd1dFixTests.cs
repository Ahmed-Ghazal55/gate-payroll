using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWD1dF: failing-first coverage for codex-RWD1d residual findings (exact money amounts).</summary>
[TestClass]
public sealed class DebtRwd1dFixTests
{
    static ClaimsPrincipal Maker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1d-maker",["department"]="FIN",["rankLevel"]=3,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","debts.reschedule","approvals.decide","pay.manage","pay.view")
    });
    static ClaimsPrincipal Checker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1d-checker",["department"]="FIN",["rankLevel"]=4,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","approvals.decide","treasury.journals","pay.approve","pay.view")
    });

    [TestMethod,DataRow(false)]
    public void H1_ReduceApprovalAmountMustEqualRecomputedFileTotal_NotClientTotal(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',100,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // File total = 40; client can claim totalReduce=25 matching a weak approval.
            byte[] file=Encoding.UTF8.GetBytes("iqama,debt_id,amount,reason\n2000000001,"+debt+",40,تصحيح رصيد افتتاحي");
            var preview=DebtLedger.PreviewReduce(c,null,companyG,Maker,file,"reduce.csv",new DateOnly(2026,11,1));
            Assert.AreEqual(40m,preview.N("totalReduce"));
            string fingerprint=preview.S("fingerprint");
            string requestId=SchemaSql.GuidText(SchemaSql.NewId());
            using(var tx=c.BeginTransaction())
            {
                SchemaApprovals.SaveRequest(c,tx,new()
                {
                    ["id"]=requestId,["kind"]="debt_reduce",["status"]="pending",["referenceId"]=fingerprint,
                    ["requestedBy"]="rwd1d-maker",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["amountSar"]=25m,
                    ["titleAr"]="تخفيض",["fromDepartment"]="FIN"
                });
                tx.Commit();
            }
            var input=new JsonObject
            {
                ["companyId"]=company,["period"]="2026-11",["filename"]="reduce.csv",
                ["dataBase64"]=Convert.ToBase64String(file),["fingerprint"]=fingerprint,
                ["approvalRequestId"]=requestId,["totalReduce"]=25m
            };
            // Must refuse: approval/client 25 ≠ recomputed file total 40.
            // Admin checker bypasses synthetic-company sponsor demand used by fixture companies.
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.CommitReduceImportForTests(c,null,input,DebtLedgerFixture.Admin)).Status);
            Assert.AreEqual(100m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            // Matching approval of exact file total 40 succeeds and posts exactly 40.
            string okId=SchemaSql.GuidText(SchemaSql.NewId());
            using(var tx=c.BeginTransaction())
            {
                SchemaApprovals.SaveRequest(c,tx,new()
                {
                    ["id"]=okId,["kind"]="debt_reduce",["status"]="pending",["referenceId"]=fingerprint,
                    ["requestedBy"]="rwd1d-maker",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["amountSar"]=40m,
                    ["titleAr"]="تخفيض",["fromDepartment"]="FIN"
                });
                tx.Commit();
            }
            input["approvalRequestId"]=okId;input["totalReduce"]=40m;
            using(var tx=c.BeginTransaction())
            {
                var committed=Debts.CommitReduceImportForTests(c,tx,input,DebtLedgerFixture.Checker);
                Assert.AreEqual(40m,committed.N("totalReduce"));
                Assert.AreEqual(60m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
                tx.Commit();
            }
        }
    }

    [TestMethod,DataRow(false)]
    public void H5_TableLevelSqlServerCheckAndSqliteRebuildInsideOpenTransaction(bool sqlServer)
    {
        if(sqlServer)
        {
            using var sql=MoneySchemaFixture.Payments(true);
            string sqlCompany=MoneySchemaFixture.Company(sql);
            SchemaDebt.Ensure(sql);
            // Table-level unnamed CHECK (parent_column_id=0) omits absorbed — must be dropped on Ensure.
            MoneySchemaFixture.Exec(sql,null,"""
                IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'debt.debt') AND name=N'CK_debt_debt_status')
                  ALTER TABLE debt.debt DROP CONSTRAINT CK_debt_debt_status;
                ALTER TABLE debt.debt ADD CHECK (status IN ('open','settled','waived','cancelled','transferred'));
                """);
            string sqlDebt=SchemaDebtTests.Debt(sql,sqlCompany);
            Assert.ThrowsExactly<Microsoft.Data.SqlClient.SqlException>(()=>
                MoneySchemaFixture.Exec(sql,null,"UPDATE debt.debt SET balance=0,status='absorbed' WHERE id=@id",("@id",sqlDebt)));
            SchemaDebt.Ensure(sql);
            MoneySchemaFixture.Exec(sql,null,"UPDATE debt.debt SET balance=0,status='absorbed' WHERE id=@id",("@id",sqlDebt));
            Assert.AreEqual("absorbed",(string)MoneySchemaFixture.Scalar(sql,null,"SELECT status FROM debt.debt WHERE id=@id",("@id",sqlDebt))!);
            return;
        }

        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            SchemaDebtTests.Move(c,company,debt,40);
            MoneySchemaFixture.Exec(c,null,"PRAGMA foreign_keys=OFF");
            var triggerNames=new List<string>();
            using(var cmd=Store.Command(c,null,"SELECT name FROM sqlite_master WHERE type='trigger'"))
            using(var reader=cmd.ExecuteReader())
                while(reader.Read())triggerNames.Add(reader.GetString(0));
            foreach(var name in triggerNames)
                MoneySchemaFixture.Exec(c,null,"DROP TRIGGER IF EXISTS "+name);
            MoneySchemaFixture.Exec(c,null,"""
                CREATE TABLE debt_debt__legacy(
                  id TEXT NOT NULL PRIMARY KEY, erp_rider_id TEXT NOT NULL, company_id TEXT NOT NULL,
                  category TEXT NOT NULL, principal REAL NOT NULL, balance REAL NOT NULL DEFAULT 0,
                  instalment REAL NULL, status TEXT NOT NULL DEFAULT 'open'
                  CHECK(status IN ('open','settled','waived','cancelled','transferred')),
                  platform TEXT NULL, promissory_flag INTEGER NOT NULL DEFAULT 0,
                  created_by TEXT NOT NULL DEFAULT 't', created_at TEXT NOT NULL DEFAULT '2026-11-01',
                  UNIQUE(id,company_id)
                )
                """);
            MoneySchemaFixture.Exec(c,null,"""
                INSERT INTO debt_debt__legacy(id,erp_rider_id,company_id,category,principal,balance,instalment,status,platform,promissory_flag,created_by,created_at)
                SELECT id,erp_rider_id,company_id,category,principal,balance,instalment,status,platform,COALESCE(promissory_flag,0),COALESCE(created_by,'t'),COALESCE(created_at,'2026-11-01') FROM debt_debt
                """);
            MoneySchemaFixture.Exec(c,null,"DROP TABLE debt_debt");
            MoneySchemaFixture.Exec(c,null,"ALTER TABLE debt_debt__legacy RENAME TO debt_debt");
            MoneySchemaFixture.Exec(c,null,"PRAGMA foreign_keys=ON");
            // Rebuild must succeed inside an open transaction while movements still reference the debt.
            using var tx=c.BeginTransaction();
            SchemaDebt.Ensure(c,tx);
            MoneySchemaFixture.Exec(c,tx,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-20',@a,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@a",-40m));
            MoneySchemaFixture.Exec(c,tx,"UPDATE debt_debt SET status='absorbed' WHERE id=@id AND balance=0",("@id",debt));
            Assert.AreEqual("absorbed",(string)MoneySchemaFixture.Scalar(c,tx,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);
            Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d",("@d",debt)));
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void H7_EditTwentyFiveAgainstInstalmentTenRejected(bool sqlServer)
    {
        var (c,company,_,companyG,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance',instalment=10,balance=0 WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-10-01','2026-10-05',80,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            string cycleId="pc-rwd1d-h7";
            string sponsor=FinancialCore.Sponsors[0].Id;
            using var tx=c.BeginTransaction();
            Store.Save(c,"debt",new()
            {
                ["id"]=debt,["iqama"]="2000000001",["category"]="CashAdvance",["creditorSponsorId"]=sponsor,
                ["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=80m,["remainingBalance"]=80m,
                ["paidAmount"]=0m,["monthlyInstallment"]=10m,["effectiveFrom"]="2026-10",["createdAt"]="2026-10-01T00:00:00Z"
            },tx);
            Store.Save(c,"pay_cycle",new()
            {
                ["id"]=cycleId,["companyId"]=sponsor,["period"]="2026-11",["sponsorId"]=sponsor,
                ["status"]="draft",["stage"]=6,["createdBy"]="rwd1d-maker",
                ["riders"]=new JsonArray(new JsonObject{["rider"]="2000000001",["net"]=100m,["salaryNet"]=100m}),
                ["deductions"]=new JsonArray(new JsonObject
                {
                    ["rider"]="2000000001",["type"]="debt",["source"]=debt,["phase"]=4,["order"]=1,
                    ["labelAr"]="قسط",["debtCategory"]="CashAdvance",["accountCode"]="112501",
                    ["computed"]=-10m,["requestedAmount"]=10m,["amount"]=0m
                }),
                ["debtBefore"]=new JsonArray(),["invoices"]=new JsonArray()
            },tx);
            // Edit 25 against ledger instalment 10 must 409 (exact).
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                PayCycles.DebtDeductionsAct(c,tx,Maker,cycleId,new()
                {
                    ["action"]="edit",["rider"]="2000000001",["key"]="debt:"+debt,["amount"]=25m,["reason"]="تجاوز القسط"
                })).Status);
            // Edit exactly 10 is allowed.
            var view=PayCycles.DebtDeductionsAct(c,tx,Maker,cycleId,new()
            {
                ["action"]="edit",["rider"]="2000000001",["key"]="debt:"+debt,["amount"]=10m,["reason"]="ضمن القسط"
            });
            Assert.AreEqual(10m,view["rows"]!.AsArray()[0]!["items"]!.AsArray()[0]!.N("amount"));
        }
    }

    [TestMethod,DataRow(false)]
    public void H8_VerifyOnceBeforeWrites_AndReplacementBytesMustMatchPreviewHash(bool sqlServer)
    {
        var (c,_,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            byte[] previewFile=Encoding.UTF8.GetBytes("opening-preview-rwd1d");
            byte[] replacement=Encoding.UTF8.GetBytes("opening-replacement-rwd1d-DIFFERENT");
            string previewHash=Convert.ToHexString(SHA256.HashData(previewFile));
            string replacementHash=Convert.ToHexString(SHA256.HashData(replacement));
            Assert.AreNotEqual(previewHash,replacementHash,ignoreCase:true);

            var rows=new JsonArray(new JsonObject{["iqama"]="2000000001",["remainingBalance"]=30m});
            string fingerprint=FinancialImports.Issue(c,"opening:"+previewHash,rows,"rwd1d-checker");

            // Replacement bytes with preview fingerprint/hash must be refused (exact hash mismatch).
            var swapped=new JsonObject
            {
                ["dataBase64"]=Convert.ToBase64String(replacement),
                ["fileHash"]=previewHash,
                ["previewFileHash"]=previewHash,
                ["fingerprint"]=fingerprint,
                ["rows"]=rows.DeepClone(),
                ["confirm"]=true
            };
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveImportFileHashForTests(swapped,c,null,"opening","rwd1d-checker")).Status);

            // Matching bytes succeed once; a second verify after a signed-state write must not be required by commit helpers.
            var matching=new JsonObject
            {
                ["dataBase64"]=Convert.ToBase64String(previewFile),
                ["fileHash"]=previewHash,
                ["previewFileHash"]=previewHash,
                ["fingerprint"]=fingerprint,
                ["rows"]=rows.DeepClone(),
                ["confirm"]=true
            };
            Assert.AreEqual(previewHash,Convert.ToHexString(Debts.ResolveImportFileHashForTests(matching,c,null,"opening","rwd1d-checker")),ignoreCase:true);

            // After debt state changes (signed preview kinds include debt), hash resolution without re-verify still returns the bound hash.
            using(var tx=c.BeginTransaction())
            {
                Store.Save(c,"debt",new(){["id"]="rwd1d-h8-debt",["iqama"]="2000000001",["status"]="active",["remainingBalance"]=1m},tx);
                byte[] once=Debts.ResolveImportFileHashForTests(matching,c,tx,"opening","rwd1d-checker",verifyPreview:false);
                Assert.AreEqual(previewHash,Convert.ToHexString(once),ignoreCase:true);
                // Re-verify after state change fails — commit must not call this path after writes.
                Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                    Debts.ResolveImportFileHashForTests(matching,c,tx,"opening","rwd1d-checker",verifyPreview:true)).Status);
                tx.Rollback();
            }
        }
    }

    [TestMethod,DataRow(false)]
    public void NewHigh_LaterMonthReversalUsesCyclePeriodAndSkipsOtherCycleMovements(bool sqlServer)
    {
        var (c,company,periodId,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            // Open December for destination-period reverse journals.
            MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_accounting_period(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-12-01',1,datetime('now'))",
                ("@id",MoneySchemaFixture.Id()),("@c",company));
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',80,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            var doc=new JsonObject
            {
                ["id"]=debt,["iqama"]="2000000001",["category"]="CashAdvance",["creditorSponsorId"]=company,
                ["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=80m,["remainingBalance"]=80m,["paidAmount"]=0m,
                ["monthlyInstallment"]=25m,["effectiveFrom"]="2026-10"
            };
            string cycleA="pc-rwd1d-a",cycleB="pc-rwd1d-b";
            using var tx=c.BeginTransaction();
            Store.Save(c,"debt",doc,tx);
            var before=doc.Clone();
            // Cycle A posts instalment 25 in November.
            Store.Save(c,"pay_cycle",new()
            {
                ["id"]=cycleA,["companyId"]=company,["period"]="2026-11",["sponsorId"]=company,
                ["status"]="approved",["debtDeductionApprovedBy"]="rwd1d-checker",
                ["debtBefore"]=new JsonArray(before),["debtAfter"]=new JsonArray(),
                ["riders"]=new JsonArray(new JsonObject{["rider"]="2000000001",["net"]=75m,["salaryNet"]=100m}),
                ["deductions"]=new JsonArray(),["invoices"]=new JsonArray()
            },tx);
            Debts.Post(c,tx,new PayrollResult("2000000001","2026-11","pay_cycle",0,100,100,100,0,0,[new PayLine("debt_installment","قسط",-25m,debt)],[]),cycleA,payCycle:true);
            Assert.AreEqual(55m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            object? journalA=MoneySchemaFixture.Scalar(c,tx,"SELECT journal_id FROM debt_debt_movement WHERE debt_id=@d AND amount=-25 AND source_type='payroll_instalment'",("@d",debt));
            Assert.IsNotNull(journalA);
            var afterA=Store.Get(c,debt,"debt",tx)!.Clone();
            var cycA=Store.Get(c,cycleA,"pay_cycle",tx)!;cycA["debtAfter"]=new JsonArray(afterA.DeepClone());Store.Save(c,"pay_cycle",cycA,tx);

            // Same-month sibling cycle B: Debts.Post blocks a second period post, so insert a sibling
            // payroll_instalment with a distinct journal (not in cycle A's ledger journal set).
            Guid srcB=SchemaSql.NewId();
            Guid recv=DebtLedger.AccountId(c,tx,"112501"),payable=DebtLedger.AccountId(c,tx,"212103");
            var postedB=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                companyG,periodG,new DateOnly(2026,11,1),new DateOnly(2026,11,1),
                "pay_cycle",srcB,"debt_payroll_instalment",0,false,"payroll_engine",
                [
                    new(payable,SchemaOrgCostCentre.NoneId,"none",10m,0m,"قسط"),
                    new(recv,SchemaOrgCostCentre.NoneId,"none",0m,10m,"تخفيض")
                ],NarrationAr:"قسط دورة أخرى"));
            // Drop source guard for the synthetic sibling row, then restore schema guards.
            MoneySchemaFixture.Exec(c,tx,"DROP TRIGGER IF EXISTS TR_debt_movement_source_INSERT");
            MoneySchemaFixture.Exec(c,tx,"DROP TRIGGER IF EXISTS TR_debt_movement_source_UPDATE");
            MoneySchemaFixture.Exec(c,tx,"""
                INSERT INTO debt_debt_movement(id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,journal_id,run_mode,payout_blocked,created_by,created_at)
                VALUES(@id,'2026-11-01',@d,@c,'2026-11-01',-10,'payroll_instalment',@src,@j,0,0,'payroll_engine',datetime('now'))
                """,("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company),("@src",SchemaSql.GuidText(srcB)),("@j",SchemaSql.GuidText(postedB.JournalId)));
            SchemaDebt.Ensure(c,tx);
            Store.Save(c,"ledger",new()
            {
                ["id"]="debt:"+debt+":2026-11:"+cycleB,["iqama"]="2000000001",["period"]="2026-11",["amount"]=-10m,
                ["kind"]="debt_installment",["debtId"]=debt,["sourceRef"]=cycleB,["journalEntryId"]=SchemaSql.GuidText(postedB.JournalId),
                ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
            },tx);
            Assert.AreEqual(45m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));

            // Later-month reversal of A: destination period 2026-12, original movements stay on 2026-11.
            PayCycles.ReverseLedgerDebtEffectsForTests(c,tx,cycA,"rwd1d-checker","2026-12");
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND amount=25 AND source_type='payroll_reversal' AND period_month='2026-12-01'",("@d",debt)));
            Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND amount=10 AND source_type='payroll_reversal'",("@d",debt)));
            // Balance: 45 + 25 (reverse A only) = 70; B's -10 remains.
            Assert.AreEqual(70m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal WHERE reverses_journal_id=@j AND status=2",("@j",journalA!.ToString()!)));
        }
    }

    [TestMethod,DataRow(false)]
    public void M5_AssignedOtherVaultDoesNotUnlockNumericCash111101(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',50,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // MAIN.BW.SET maps to 111103 — must NOT unlock numeric cash 111101.
            var otherVault=UserAccess.Principal(new()
            {
                ["role"]="FinancialAccountant",["username"]="rwd1d-othervault",["department"]="FIN",["rankLevel"]=3,
                ["customPermissions"]=new JsonArray("debts.collect"),
                ["financialScope"]=new JsonObject
                {
                    ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors.Select(s=>(JsonNode?)s.Id).ToArray()),
                    ["accountCodes"]=new JsonArray("MAIN.BW.SET"),
                    ["platforms"]=new JsonArray("Ninja","Keeta","HungerStation","Jahez","Chefz","Amazon","Other"),
                    ["categoryCodes"]=new JsonArray(FinancialCore.CategoriesCatalog.Select(x=>(JsonNode?)x.Code).ToArray())
                }
            });
            using var tx=c.BeginTransaction();
            var err=Assert.ThrowsExactly<ApiError>(()=>
                DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),otherVault,10m,"تحصيل",
                    DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"111101"),0,false));
            Assert.AreEqual(403,err.Status,err.Message);
            Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            // MAIN.BW.DEBT maps to 111101 — collects exactly 10 → balance 40.
            var matched=UserAccess.Principal(new()
            {
                ["role"]="FinancialAccountant",["username"]="rwd1d-cash",["department"]="FIN",["rankLevel"]=3,
                ["customPermissions"]=new JsonArray("debts.collect"),
                ["financialScope"]=new JsonObject
                {
                    ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors.Select(s=>(JsonNode?)s.Id).ToArray()),
                    ["accountCodes"]=new JsonArray("MAIN.BW.DEBT"),
                    ["platforms"]=new JsonArray("Ninja","Keeta","HungerStation","Jahez","Chefz","Amazon","Other"),
                    ["categoryCodes"]=new JsonArray(FinancialCore.CategoriesCatalog.Select(x=>(JsonNode?)x.Code).ToArray())
                }
            });
            var ok=DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),matched,10m,"تحصيل",
                DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"111101"),0,false);
            Assert.AreEqual(40m,ok.N("balance"));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        }
    }
}
