using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWD1a1F: failing-first coverage for residual Codex findings H5 restore order and H8 fingerprint (exact amounts).</summary>
[TestClass]
public sealed class DebtRwd1a1FixTests
{
    static void ForceLegacySqliteDebtStatus(System.Data.Common.DbConnection c)
    {
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
    }

    [TestMethod,DataRow(false)]
    public void H5_ApprovedOpeningImportRestoresSourcesBeforeMovements(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            string batch=MoneySchemaFixture.Id(),line=MoneySchemaFixture.Id(),move=MoneySchemaFixture.Id();
            // Approved opening import of exactly 40 (source line + ledger movement).
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_batch(id,company_id,period_month,amount,reason,file_hash,import_kind) VALUES(@id,@c,'2026-11-01',40,'Opening 40',randomblob(32),'opening')",("@id",batch),("@c",company));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_line(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount) VALUES(@id,@b,'2026-11-01',1,1,@d,@c,40)",("@id",line),("@b",batch),("@d",debt),("@c",company));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_import_batch SET status=2 WHERE id=@id",("@id",batch));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_import_batch SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",batch),("@u",MoneySchemaFixture.Id()));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',40,'import',@line)",
                ("@id",move),("@d",debt),("@c",company),("@line",line));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));

            ForceLegacySqliteDebtStatus(c);
            using var tx=c.BeginTransaction();
            SchemaDebt.Ensure(c,tx);
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_import_line WHERE id=@id",("@id",line))));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_debt_movement WHERE id=@id",("@id",move))));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_import_line WHERE id=@id",("@id",line)));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,tx,"SELECT COUNT(*) FROM debt_debt_movement WHERE source_type='import' AND source_id=@line AND amount=40",("@line",line)));
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void H5_SettledDebtHistoricalImportRestoresWithoutOpenGuard(bool sqlServer)
    {
        var (c,company,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            string debt=SchemaDebtTests.Debt(c,company);
            string batch=MoneySchemaFixture.Id(),line=MoneySchemaFixture.Id(),move=MoneySchemaFixture.Id(),receipt=MoneySchemaFixture.Id(),collectMove=MoneySchemaFixture.Id();
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_batch(id,company_id,period_month,amount,reason,file_hash,import_kind) VALUES(@id,@c,'2026-11-01',40,'Opening 40',randomblob(32),'opening')",("@id",batch),("@c",company));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_import_line(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount) VALUES(@id,@b,'2026-11-01',1,1,@d,@c,40)",("@id",line),("@b",batch),("@d",debt),("@c",company));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_import_batch SET status=2 WHERE id=@id",("@id",batch));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_import_batch SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",batch),("@u",MoneySchemaFixture.Id()));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-05',40,'import',@line)",
                ("@id",move),("@d",debt),("@c",company),("@line",line));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_collection(id,company_id,period_month,debt_id,amount,reason) VALUES(@id,@c,'2026-11-01',@d,40,'Full collect 40')",("@id",receipt),("@c",company),("@d",debt));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_collection SET status=2 WHERE id=@id",("@id",receipt));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_collection SET status=3,approved_by=@u,approved_at=datetime('now') WHERE id=@id",("@id",receipt),("@u",MoneySchemaFixture.Id()));
            MoneySchemaFixture.Exec(c,null,"INSERT INTO debt_debt_movement(id,debt_id,company_id,period_month,occurred_on,amount,source_type,source_id) VALUES(@id,@d,@c,'2026-11-01','2026-11-10',-40,'collection',@r)",
                ("@id",collectMove),("@d",debt),("@c",company),("@r",receipt));
            MoneySchemaFixture.Exec(c,null,"UPDATE debt_debt SET status='settled' WHERE id=@id AND balance=0",("@id",debt));
            Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,null,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual("settled",(string)MoneySchemaFixture.Scalar(c,null,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);

            ForceLegacySqliteDebtStatus(c);
            using var tx=c.BeginTransaction();
            SchemaDebt.Ensure(c,tx);
            Assert.AreEqual("settled",(string)MoneySchemaFixture.Scalar(c,tx,"SELECT status FROM debt_debt WHERE id=@id",("@id",debt))!);
            Assert.AreEqual(0m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT balance FROM debt_debt WHERE id=@id",("@id",debt))));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_import_line WHERE id=@id",("@id",line))));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_collection WHERE id=@id",("@id",receipt))));
            Assert.AreEqual(40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_debt_movement WHERE id=@id",("@id",move))));
            Assert.AreEqual(-40m,Convert.ToDecimal(MoneySchemaFixture.Scalar(c,tx,"SELECT amount FROM debt_debt_movement WHERE id=@id",("@id",collectMove))));
            tx.Commit();
        }
    }

    [TestMethod,DataRow(false)]
    public void H8_UploadedBytesWithoutFingerprintRejectedOnBulkAndOpening(bool sqlServer)
    {
        var (c,_,_,_,_)=DebtLedgerFixture.Open(sqlServer);
        using(c)
        {
            byte[] file=Encoding.UTF8.GetBytes("unsigned-opening-bytes-rwd1a1");
            string hash=Convert.ToHexString(SHA256.HashData(file));
            var unsignedOpening=new JsonObject
            {
                ["dataBase64"]=Convert.ToBase64String(file),
                ["fileHash"]=hash,
                ["previewFileHash"]=hash,
                ["rows"]=new JsonArray(new JsonObject{["iqama"]="2000000001",["remainingBalance"]=40m})
            };
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveImportFileHashForTests(unsignedOpening,c,null,"opening","rwd1a1-checker")).Status);

            byte[] bulk=Encoding.UTF8.GetBytes("unsigned-bulk-bytes-rwd1a1");
            string bulkHash=Convert.ToHexString(SHA256.HashData(bulk));
            var unsignedBulk=new JsonObject
            {
                ["dataBase64"]=Convert.ToBase64String(bulk),
                ["fileHash"]=bulkHash,
                ["previewFileHash"]=bulkHash,
                ["rows"]=new JsonArray(new JsonObject{["iqama"]="2000000001",["collectedAmount"]=40m})
            };
            Assert.AreEqual(400,Assert.ThrowsExactly<ApiError>(()=>
                Debts.ResolveImportFileHashForTests(unsignedBulk,c,null,"bulk","rwd1a1-checker")).Status);

            // Bound preview of exact hash 40-row succeeds once.
            var rows=new JsonArray(new JsonObject{["iqama"]="2000000001",["remainingBalance"]=40m});
            string fingerprint=FinancialImports.Issue(c,"opening:"+hash,rows,"rwd1a1-checker");
            var bound=new JsonObject
            {
                ["dataBase64"]=Convert.ToBase64String(file),
                ["fileHash"]=hash,
                ["previewFileHash"]=hash,
                ["fingerprint"]=fingerprint,
                ["rows"]=rows.DeepClone(),
                ["confirm"]=true
            };
            Assert.AreEqual(hash,Convert.ToHexString(Debts.ResolveImportFileHashForTests(bound,c,null,"opening","rwd1a1-checker")),ignoreCase:true);
        }
    }
}
