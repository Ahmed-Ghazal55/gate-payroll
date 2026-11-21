using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWD1cF: failing-first coverage for codex-RWD1c residual findings (exact money amounts).</summary>
[TestClass]
public sealed class DebtRwd1cFixTests
{
    static ClaimsPrincipal Maker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1c-maker",["department"]="FIN",["rankLevel"]=3,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","debts.reschedule","approvals.decide")
    });
    static ClaimsPrincipal Checker=>UserAccess.Principal(new()
    {
        ["role"]="FinancialAccountant",["username"]="rwd1c-checker",["department"]="FIN",["rankLevel"]=4,
        ["customPermissions"]=new JsonArray("debts.waive","debts.create.administrative","debts.collect","approvals.decide","treasury.journals")
    });

    [TestMethod,DataRow(false)]
    public void H1_ApprovalRequestBoundToKindAmountAndConsumed(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            string requestId=SchemaSql.GuidText(SchemaSql.NewId());
            using(var tx=c.BeginTransaction())
            {
                SchemaApprovals.SaveRequest(c,tx,new()
                {
                    ["id"]=requestId,["kind"]="debt_waiver",["status"]="pending",["referenceId"]=debt,
                    ["requestedBy"]="rwd1c-maker",["targetDepartment"]="FIN",["requiredRankLevel"]=1,["amountSar"]=15m,
                    ["titleAr"]="إعفاء",["fromDepartment"]="FIN"
                });
                tx.Commit();
            }

            // Wrong kind → 409 (bind operation).
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveMakerForTests(c,null!,new(){["approvalRequestId"]=requestId},Checker,
                    expectedReference:debt,expectedAmount:15m,expectedKind:"debt_absorb")).Status);

            // Amount omitted while request present → 409 (amount binding required).
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveMakerForTests(c,null!,new(){["approvalRequestId"]=requestId},Checker,
                    expectedReference:debt,expectedAmount:null,expectedKind:"debt_waiver")).Status);

            // Wrong amount 12 ≠ approved 15 → 409.
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveMakerForTests(c,null!,new(){["approvalRequestId"]=requestId},Checker,
                    expectedReference:debt,expectedAmount:12m,expectedKind:"debt_waiver")).Status);

            using(var tx=c.BeginTransaction())
            {
                var maker=Debts.ResolveMakerForTests(c,tx,new(){["approvalRequestId"]=requestId},Checker,
                    expectedReference:debt,expectedAmount:15m,expectedKind:"debt_waiver");
                Assert.AreEqual("rwd1c-maker",maker.Identity!.Name);
                // Consume so a second use fails.
                Debts.ConsumeApprovalRequestForTests(c,tx,requestId);
                tx.Commit();
            }
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveMakerForTests(c,null!,new(){["approvalRequestId"]=requestId},Checker,
                    expectedReference:debt,expectedAmount:15m,expectedKind:"debt_waiver")).Status);
        }
    }

    [TestMethod,DataRow(false)]
    public void H4_PlatformDebtMissingPlatformRejected_Not526601(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            DebtLedger.EnsureD11WaiverAccounts(c,null);
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='PlatformTransferredDebt',platform=NULL WHERE id=@id",("@id",debt));
            using var tx=c.BeginTransaction();
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt))).Status);
            MoneySchemaFixture.Exec(c,tx,"UPDATE debt_debt SET platform='' WHERE id=@id",("@id",debt));
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt))).Status);
            // Non-platform CashAdvance without platform still maps to general 526601.
            MoneySchemaFixture.Exec(c,tx,"UPDATE debt_debt SET category='CashAdvance',platform=NULL WHERE id=@id",("@id",debt));
            Assert.AreEqual("526601",DebtLedger.ResolveWaiverExpense(c,tx,Guid.Parse(debt)).Code);
        }
    }

    [TestMethod,DataRow(false)]
    public void H5_SqliteRebuildWithMovementsKeepsAbsorbedAndSqlServerDropsUnnamed(bool sqlServer)
    {
        if(sqlServer)
        {
            // Named + unnamed status CHECKs without absorbed must both be replaced.
            using var sql=MoneySchemaFixture.Payments(true);
            string sqlCompany=MoneySchemaFixture.Company(sql);
            SchemaDebt.Ensure(sql);
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
            // Force legacy status CHECK without absorbed while a child movement exists (FK must survive rebuild).
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
            Assert.ThrowsExactly<Microsoft.Data.Sqlite.SqliteException>(()=>
                MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET status='absorbed' WHERE id=@id",("@id",debt)));
            SchemaDebt.Ensure(c);
            // Zero via ledger movement (balance trigger), then absorbed is allowed by the upgraded CHECK.
            SchemaDebtTests.Move(c,company,debt,-40);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET status='absorbed' WHERE id=@id AND balance=0",("@id",debt));
            Assert.AreEqual("absorbed",(string)MoneySchemaFixture.Scalar(c,null,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);
            Assert.AreEqual(2L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d",("@d",debt)));
        }
    }

    [TestMethod,DataRow(false)]
    public void H7_RescheduleTenDrivesPayCycleInstalmentNotDocumentTwentyFive(bool sqlServer)
    {
        var (c,company,_,companyG,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance',instalment=25,balance=0 WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-10-01','2026-10-05',80,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // Document starts with stale instalment 25; ledger reschedule must drive pay-cycle to 10.
            using(var tx=c.BeginTransaction())
            {
                Store.Save(c,"debt",new()
                {
                    ["id"]=debt,["iqama"]="2000000001",["category"]="CashAdvance",["creditorSponsorId"]=company,
                    ["receivableAccountCode"]="112501",["status"]="active",["totalAmount"]=80m,["remainingBalance"]=80m,
                    ["paidAmount"]=0m,["monthlyInstallment"]=25m,["effectiveFrom"]="2026-10",["createdAt"]="2026-10-01T00:00:00Z"
                },tx);
                var res=DebtLedger.Reschedule(c,tx,Guid.Parse(debt),Maker,10m);
                Assert.AreEqual(10m,res.N("instalment"));
                Assert.AreEqual(10m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT instalment FROM debt_debt WHERE id=@id",("@id",debt))));
                tx.Commit();
            }
            // Pay-cycle calculation must take ledger instalment 10 (document synced to 10 by Reschedule).
            Assert.AreEqual(10m,Store.Get(c,debt,"debt")!.N("monthlyInstallment"));
            Assert.AreEqual(10m,PayCycles.DebtInstalmentAmountForTests(c,null,Store.Get(c,debt,"debt")!));
            // Even if document were stale at 25, ledger column wins.
            var stale=Store.Get(c,debt,"debt")!.Clone();stale["monthlyInstallment"]=25m;
            Assert.AreEqual(10m,PayCycles.DebtInstalmentAmountForTests(c,null,stale));
        }
    }

    [TestMethod,DataRow(false)]
    public void H8_ClientFileHashAloneRejected_MustBindPreviewOrFile(bool sqlServer)
    {
        // Unsigned 64-char hex without file / preview binding must be refused.
        var input=new JsonObject{["fileHash"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("forged-client-hash")))};
        Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>Debts.ResolveImportFileHashForTests(input)).Status);

        byte[] file=Encoding.UTF8.GetBytes("opening-bytes-rwd1c");
        string hash=Convert.ToHexString(SHA256.HashData(file));
        var withFile=new JsonObject{["dataBase64"]=Convert.ToBase64String(file),["fileHash"]="00"+hash[2..]};
        // When file bytes present, hash comes from the file (exact), not the forged client hex.
        Assert.AreEqual(hash,Convert.ToHexString(Debts.ResolveImportFileHashForTests(withFile)),ignoreCase:true);

        // Preview-bound path: fingerprint + matching fileHash from preview is accepted without re-upload.
        var (c,_,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            var rows=new JsonArray(new JsonObject{["iqama"]="2000000001",["remainingBalance"]=30m});
            string fingerprint=FinancialImports.Issue(c,"opening:"+hash,rows,"rwd1c-checker");
            var bound=new JsonObject
            {
                ["fileHash"]=hash,["fingerprint"]=fingerprint,["rows"]=rows,["confirm"]=true,
                ["previewFileHash"]=hash
            };
            Assert.AreEqual(hash,Convert.ToHexString(Debts.ResolveImportFileHashForTests(bound,c,null,"opening","rwd1c-checker")),ignoreCase:true);
        }
    }

    [TestMethod,DataRow(false)]
    public void NewHigh_PayrollReversalReversesLedgerMovementAndJournal(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
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
            string cycleId="pc-rwd1c-rev";
            using var tx=c.BeginTransaction();
            Store.Save(c,"debt",doc,tx);
            var before=doc.Clone();
            Store.Save(c,"pay_cycle",new()
            {
                ["id"]=cycleId,["companyId"]=company,["period"]="2026-11",["sponsorId"]=company,
                ["status"]="approved",["debtDeductionApprovedBy"]="rwd1c-checker",
                ["debtBefore"]=new JsonArray(before),["debtAfter"]=new JsonArray(),
                ["riders"]=new JsonArray(new JsonObject{["rider"]="2000000001",["net"]=75m,["salaryNet"]=100m}),
                ["deductions"]=new JsonArray(),["invoices"]=new JsonArray()
            },tx);
            Debts.Post(c,tx,new PayrollResult("2000000001","2026-11","pay_cycle",0,100,100,100,0,0,[new PayLine("debt_installment","قسط",-25m,debt)],[]),cycleId,payCycle:true);
            Assert.AreEqual(55m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            object? journalId=MoneySchemaFixture.Scalar(c,tx,"SELECT journal_id FROM debt_debt_movement WHERE debt_id=@d AND amount=-25",("@d",debt));
            Assert.IsNotNull(journalId);
            var after=Store.Get(c,debt,"debt",tx)!.Clone();
            var cycle=Store.Get(c,cycleId,"pay_cycle",tx)!;
            cycle["debtAfter"]=new JsonArray(after);
            Store.Save(c,"pay_cycle",cycle,tx);

            PayCycles.ReverseLedgerDebtEffectsForTests(c,tx,cycle,"rwd1c-checker","2026-11");
            // Restore documents as the full reverse path does.
            Store.Save(c,"debt",before.Clone(),tx);
            Assert.AreEqual(80m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE debt_id=@d AND amount=25 AND source_type='payroll_reversal'",("@d",debt)));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM fin_journal WHERE reverses_journal_id=@j AND status=2",("@j",journalId!.ToString()!)));
            Assert.AreEqual(80m,Store.Get(c,debt,"debt",tx)!.N("remainingBalance"));
        }
    }

    [TestMethod,DataRow(false)]
    public void M5_NumericCash111101RequiresAssignedAccountScope(bool sqlServer)
    {
        var (c,company,_,companyG,periodG)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET category='CashAdvance' WHERE id=@id",("@id",debt));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',50,'synthetic',@id)",
                ("@id",MoneySchemaFixture.Id()),("@d",debt),("@c",company));
            // All sponsors in scope, but no accountCodes and no treasury.journals → numeric 111101 must 403.
            var noCash=UserAccess.Principal(new()
            {
                ["role"]="FinancialAccountant",["username"]="rwd1c-nocash",["department"]="FIN",["rankLevel"]=3,
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
                DebtLedger.Collect(c,tx,Guid.Parse(debt),companyG,periodG,new DateOnly(2026,11,1),noCash,10m,"تحصيل",
                    DebtLedger.AccountId(c,tx,"112501"),DebtLedger.AccountId(c,tx,"111101"),0,false));
            Assert.AreEqual(403,err.Status,err.Message);
            Assert.AreEqual(50m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
        }
    }
}
