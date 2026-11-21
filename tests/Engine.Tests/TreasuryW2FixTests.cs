using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>WT1F — failing-first coverage for Codex RWT1 findings (exact numbers).</summary>
[TestClass]
public sealed class TreasuryW2FixTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-wt1f-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-wt1f-checker","Admin");
    static ClaimsPrincipal NoChefz=>UserAccess.Principal(new JsonObject
    {
        ["username"]="synthetic-wt1f-nochefz",["role"]="FinancialAccountant",
        ["financialScope"]=new JsonObject
        {
            ["sponsorIds"]=new JsonArray(FinancialCore.Sponsors[0].Id),
            ["platforms"]=new JsonArray("Jahez","Ninja"),
            ["accountCodes"]=new JsonArray($"MAIN.{FinancialCore.Sponsors[0].Code}.SET"),
            ["categoryCodes"]=new JsonArray()
        }
    });

    [TestCleanup]
    public void Cleanup()=>ErpSettlementsApi.LiveBatchProvider=null;

    [TestMethod]
    public void Live_UsesProviderTimestamp_NotStoreAndFailsWithoutProvider()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var asOf=DateTimeOffset.Parse("2026-11-20T09:15:00Z",CultureInfo.InvariantCulture);
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch(
            [new JsonObject{["id"]="live-1",["erpRowId"]="live-1",["gross"]=100,["companyAmount"]=15,["riderAmount"]=85}],
            asOf,"omar_live");
        var live=f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.AreEqual("omar_live",live.S("source"));
        Assert.AreEqual(asOf.ToString("O"),live.S("asOf"));
        Assert.AreEqual(100m,live["items"]!.AsArray().OfType<JsonObject>().Single().N("gross"));
        ErpSettlementsApi.LiveBatchProvider=null;
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>ErpSettlementsApi.Live(c,null,Maker,"chefz",new(){["companyId"]=company,["period"]="2026-11"})));
        Assert.AreEqual(503,err.Status);
    }

    [TestMethod]
    public void Live_DemandsPlatformScope()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        ErpSettlementsApi.LiveBatchProvider=(_,_,_,_,_,_)=>new ErpSettlementsApi.LiveBatch([],DateTimeOffset.UtcNow,"omar_live");
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>ErpSettlementsApi.Live(c,null,NoChefz,"chefz",new(){["companyId"]=company,["period"]="2026-11"}))).Status);
    }

    [TestMethod]
    public void ChefzFreeze_SealsOneInvoiceAndSettlement_BlocksSecondFreeze()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string settlementId=SchemaSql.GuidText(SchemaSql.NewId());
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=settlementId,["erpSourceId"]=settlementId,["companyId"]=company,["platformAccountId"]="syn-acc",
                ["riderId"]="2000000099",["periodFrom"]="2026-11-01",["periodTo"]="2026-11-15",
                ["source"]="Orders",["gross"]=1000,["companyAmount"]=450,["riderAmount"]=550,
                ["commissionRate"]=0.15m,["dailyFee"]=30,["fullTimeDays"]=10,["freelancerCommissionBase"]=1000,
                ["fullTimeFeeAmount"]=300,["sourceDeleted"]=false
            },tx);
            Store.Save(c,"platform_invoice",new()
            {
                ["id"]="syn-chefz-inv-seal",["companyId"]=company,["platform"]="Chefz",["period"]="2026-11",
                ["approvalStatus"]="approved",["status"]="approved"
            },tx);
            return true;
        });
        var freeze=f.Store.Write((c,tx)=>ErpSettlementsApi.FreezeChefzMoney(c,tx,Maker,new()
        {
            ["companyId"]=company,["invoiceId"]="syn-chefz-inv-seal",["settlementId"]=settlementId
        }));
        Assert.AreEqual("frozen",freeze.S("status"));
        Assert.AreEqual(settlementId,freeze.S("settlementId"));
        Assert.AreEqual("syn-chefz-inv-seal",freeze.S("invoiceId"));
        Assert.AreEqual(1m,freeze.N("frozenCount"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>ErpSettlementsApi.FreezeChefzMoney(c,tx,Maker,new()
        {
            ["companyId"]=company,["invoiceId"]="syn-chefz-inv-seal",["settlementId"]=settlementId
        }))).Status);
    }

    [TestMethod]
    public void ChefzMixedRate_UsesOmarFullTimeFeeAmount_450Not400()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            // Omar: 1000*0.15 + FullTimeFeeAmount 300 (10 days @ 30) = 450. Hardcoded 25 would wrongly expect 400.
            Store.Save(c,"chefz_settlement",new()
            {
                ["id"]=SchemaSql.GuidText(SchemaSql.NewId()),["companyId"]=company,["platformAccountId"]="syn-mixed",
                ["riderId"]="2000000088",["periodFrom"]="2026-11-01",["periodTo"]="2026-11-30",
                ["source"]="Orders",["gross"]=2000,["companyAmount"]=450,["riderAmount"]=1550,
                ["commissionRate"]=0.15m,["fullTimeDays"]=10,["freelancerCommissionBase"]=1000,
                ["fullTimeFeeAmount"]=300,["dailyFee"]=30,["sourceDeleted"]=false
            },tx);
            return true;
        });
        var report=f.Store.Read(c=>ErpSettlementsApi.ChefzDifferenceReport(c,null,Maker,new(){["companyId"]=company,["period"]="2026-11"}));
        Assert.AreEqual(0m,report["summary"]!.AsObject().N("mixed"));
    }

    [TestMethod]
    public void JahezDay_RequiresConfirmedHandover_NoOpsFallback_TransferFromBankOnly()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        // No confirmed handover → open may draft, but submit/approve refuse.
        var bare=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_jahez_operation",new()
            {
                ["id"]="syn-ops-only",["companyId"]=company,["executedAt"]="2026-11-18T10:00:00Z",
                ["net"]=100,["commission"]=10,["vaultId"]="vault-main",["vaultCode"]="MAIN"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new(){["companyId"]=company,["businessDate"]="2026-11-18",["vaultCode"]="MAIN",["vaultId"]="vault-main"});
        });
        Assert.AreEqual("waiting_handover",bare.S("reconciliationState"));
        Assert.AreEqual(0m,bare.N("opsSumAmount")); // no ops fallback into lines
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>JahezReconciliation.SubmitDay(c,tx,Maker,bare.S("id")))).Status);

        var day=f.Store.Write((c,tx)=>
        {
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="syn-ho-1",["erpSourceId"]="syn-ho-1",["companyId"]=company,
                ["businessDate"]="2026-11-19",["date"]="2026-11-19",["status"]="confirmed",["statusCode"]="2",
                ["vaultId"]="vault-main",["vaultCode"]="MAIN",
                ["cashboxAmount"]=100,["bankAmount"]=0,["petroAppAmount"]=40,["purpose"]="Settlement"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="syn-bank-xfer",["companyId"]=company,["occurredAt"]="2026-11-19T12:00:00Z",
                ["fund"]="Bank",["fundCode"]="3",["purposeCode"]="Settlement",["amount"]=55,
                ["vaultId"]="vault-main",["vaultCode"]="MAIN"
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new(){["companyId"]=company,["businessDate"]="2026-11-19",["vaultCode"]="MAIN",["vaultId"]="vault-main"});
        });
        Assert.AreEqual(100m,day.N("handedCashAmount"));
        Assert.AreEqual(40m,day.N("handedExternalAmount"));
        Assert.AreEqual(0m,day.N("handedBankAmount"));
        Assert.AreEqual(55m,day.N("omarTransferAmount")); // bank movements only, not ops 100
        f.Store.Write((c,tx)=>JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id")));
        f.Store.Write((c,tx)=>JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="فرق تحويل مقبول للاختبار"}));
        var approved=f.Store.Write((c,tx)=>JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id")));
        Assert.AreEqual("approved",approved.S("status"));
        Assert.IsTrue(approved.B("firstJournalsImmutable"));
        Assert.IsTrue(approved["journalEntryIds"]!.AsArray().Count>=1);
        // Journals landed in fin.journal via JournalPosting (status posted=2).
        f.Store.Read(c=>
        {
            foreach(var jid in approved["journalEntryIds"]!.AsArray().Select(x=>x!.ToString()!))
            {
                object? st=SchemaSql.Scalar(c,null,$"SELECT status FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",jid));
                Assert.AreEqual(2,Convert.ToInt32(st,CultureInfo.InvariantCulture));
            }
            return true;
        });
    }

    [TestMethod]
    public void AlTogary_MatchesVaultIdOnly_NotNameContainsOnRequest()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var day=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-main",["companyId"]=company,["businessDate"]="2026-11-17",["date"]="2026-11-17",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",["vaultCode"]="MAIN",
                ["cashboxAmount"]=10,["bankAmount"]=0,["petroAppAmount"]=0
            },tx);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-togary",["companyId"]=company,["businessDate"]="2026-11-17",["date"]="2026-11-17",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-togary",["vaultCode"]="togary-cash",
                ["cashboxAmount"]=77,["bankAmount"]=0,["petroAppAmount"]=0
            },tx);
            return JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-17",["vaultCode"]="togary",["vaultId"]="vault-togary"
            });
        });
        Assert.IsTrue(day.B("isAlTogary"));
        Assert.AreEqual(77m,day.N("handedCashAmount")); // MAIN 10 excluded despite request vaultCode containing togary
    }

    [TestMethod]
    public void BalancesTree_ExcludesRevenue_CustodyOnlyDefinedRanges()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            SeedBalance(c,tx,company,periodId,"111201",100); // bank
            SeedBalance(c,tx,company,periodId,"411401",100); // revenue — must not enter custody/total
            SeedBalance(c,tx,company,periodId,"112304",40); // custody
            return true;
        });
        var tree=f.Store.Read(c=>TreasuryBalancesApi.Tree(c,null,Maker,"2026-11",company));
        var companyNode=tree["items"]!.AsArray().OfType<JsonObject>().Single(n=>n.S("companyId")==company);
        var children=companyNode["children"]!.AsArray().OfType<JsonObject>().Where(ch=>ch.S("source")=="period_balance").ToList();
        Assert.IsTrue(children.Any(ch=>ch.S("accountCode")=="111201"&&ch.S("kind")=="bank"));
        Assert.IsTrue(children.Any(ch=>ch.S("accountCode")=="112304"&&ch.S("kind")=="custody"));
        Assert.IsFalse(children.Any(ch=>ch.S("accountCode")=="411401"));
        Assert.AreEqual(140m,companyNode.N("total")); // 100 bank + 40 custody, not zeroed by revenue
    }

    [TestMethod]
    public void VaultCount_ServerBookBalance_RejectsClientOverride_AsOfCountDate()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string countId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string box=EnsureCashBox(c,tx,company,reg,"111103");
            string accountId=AccountId(c,tx,"111103");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,0,200,0,200,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-20",["countedAmount"]=90,["bookBalance"]=90
            });
            countId=created.S("id");
            Assert.AreEqual(200m,created.N("bookBalance")); // server book, client 90 ignored
            Assert.AreEqual(-110m,created.N("variance"));
            return true;
        });
        Assert.AreNotEqual("",countId);
    }

    [TestMethod]
    public void BankAdjust_BlocksVaultVarianceAccounts()
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
            var draft=TreasuryBankStatementsApi.RequestAdjust(c,tx,Maker,uploaded.S("id"),new(){["amount"]=5,["reason"]="فرق توقيت بنكي"});
            adjustId=draft.S("id");
            return true;
        });
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>TreasuryBankStatementsApi.ApproveAdjust(c,tx,Checker,adjustId)));
        Assert.AreEqual(409,err.Status);
        Assert.IsTrue(err.Message.Contains("توقيت",StringComparison.Ordinal)||err.Message.Contains("حساب",StringComparison.Ordinal));
    }

    [TestMethod]
    public void AutoMatch_LeavesEqualAmountTiesUnmatched()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string statementId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string bank=EnsureBankAccount(c,tx,company,reg,"111201");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            string bankAcc=AccountId(c,tx,"111201");
            string equity=AccountId(c,tx,"212103");
            // Two equal journal lines, different dates/refs — ambiguous.
            foreach(var (day,reference) in new[]{("2026-11-05","A"),("2026-11-12","B")})
            {
                string journalId=SchemaSql.GuidText(SchemaSql.NewId()),lineId=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","journal")}
                    (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,created_at)
                    VALUES(@id,@id,@p,@c,'2026-11-01',@d,'synthetic',@id,'manual',1,0,{SchemaSql.NowLiteral(c)})
                    """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@d",day));
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                    (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,narration_ar,created_at)
                    VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,50,0,@ref,{SchemaSql.NowLiteral(c)})
                    """,("@id",lineId),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),
                    ("@a",bankAcc),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@ref",reference));
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                    (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
                    VALUES(@id,@p,@c,'2026-11-01',@j,2,@a,@cc,0,50,{SchemaSql.NowLiteral(c)})
                    """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),
                    ("@j",journalId),("@a",equity),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)));
                SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=2,posted_at={SchemaSql.NowLiteral(c)},posted_by='tester' WHERE id=@id",("@id",journalId));
            }
            var uploaded=TreasuryBankStatementsApi.Upload(c,tx,Maker,new()
            {
                ["bankAccountId"]=bank,["periodMonth"]="2026-11",["openingBalance"]=0,["closingBalance"]=50,
                ["lines"]=new JsonArray(new JsonObject{["occurredOn"]="2026-11-10",["amount"]=50,["reference"]="Z"})
            });
            statementId=uploaded.S("id");
            var matched=TreasuryBankStatementsApi.AutoMatch(c,tx,Maker,statementId);
            Assert.AreEqual(0m,matched.N("matchedCount"));
            return true;
        });
        Assert.AreNotEqual("",statementId);
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
