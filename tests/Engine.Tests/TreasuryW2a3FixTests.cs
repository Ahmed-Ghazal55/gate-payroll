using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>RWT1a3F — failing-first coverage for Codex RWT1a3 partial rows (exact numbers).</summary>
[TestClass]
public sealed class TreasuryW2a3FixTests
{
    static ClaimsPrincipal Maker=>PayFixture.Actor("synthetic-rwt1a3-maker","Admin");
    static ClaimsPrincipal Checker=>PayFixture.Actor("synthetic-rwt1a3-checker","Admin");

    [TestMethod]
    public void UntiedHandover_ScopesCashToDayVault_Only_Posts10_Not10010()
    {
        // Finding 1 — JahezReconciliation.cs:492–505: untied handover taken via one vault movement
        // must not pull another vault's 10000 into company clearing (expect cash 10, not 10010).
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        var approved=f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-multi-vault",["erpSourceId"]="ho-multi-vault",["companyId"]=company,
                ["businessDate"]="2026-11-19",["date"]="2026-11-19",
                ["status"]="confirmed",["statusCode"]="2",
                // no vaultId on handover
                ["cashboxAmount"]=10010,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-day-vault","mv-other-vault")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-day-vault",["erpSourceId"]="mv-day-vault",["companyId"]=company,
                ["occurredAt"]="2026-11-19T09:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=10,["vaultId"]="vault-main",["handoverId"]="ho-multi-vault"
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-other-vault",["erpSourceId"]="mv-other-vault",["companyId"]=company,
                ["occurredAt"]="2026-11-19T09:05:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="Settlement",["directionCode"]="1",
                ["amount"]=10000,["vaultId"]="vault-other",["handoverId"]="ho-multi-vault"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-19",["vaultId"]="vault-main"
            });
            Assert.AreEqual(10m,day.N("handedCashAmount"));
            Assert.AreEqual(0m,day.N("handedBankAmount"));
            Assert.AreEqual(0m,day.N("omarTransferAmount"));
            // Cash 10 with no Omar bank settlement → transferDiff ≠ 0; record decision before approve.
            JahezReconciliation.DecideDayVariance(c,tx,Maker,day.S("id"),new(){["varianceDecision"]="نقد بلا تحويل بنكي — مقبول للاختبار"});
            JahezReconciliation.SubmitDay(c,tx,Maker,day.S("id"));
            return JahezReconciliation.ApproveDay(c,tx,Checker,day.S("id"));
        });
        Assert.AreEqual("approved",approved.S("status"));
        Assert.AreEqual(10m,approved.N("handedAmount"));
        f.Store.Read(c=>
        {
            // Clearing journal: Dr 212601 / Cr 112801 for company cash only (vault-scoped 10).
            object? clearing=SchemaSql.Scalar(c,null,$"""
                SELECT jl.debit FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                WHERE j.source_type='jahez_recon_day' AND j.source_id=@sid AND a.code='212601' AND jl.debit>0
                """,("@sid",approved.S("id")));
            Assert.AreEqual(10m,Convert.ToDecimal(clearing,CultureInfo.InvariantCulture));
            return true;
        });
    }

    [TestMethod]
    public void MovementsWithoutCashboxSettlement_StayExternal_AndBlockApprove()
    {
        // Finding 2 — JahezReconciliation.cs:506–509: movements present but not Cashbox+Settlement
        // must keep amounts external AND set unresolved so submit/approve return 409.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string dayId="";
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            Store.Save(c,"erp_cashbox_handover",new()
            {
                ["id"]="ho-no-settle",["erpSourceId"]="ho-no-settle",["companyId"]=company,
                ["businessDate"]="2026-11-20",["date"]="2026-11-20",
                ["status"]="confirmed",["statusCode"]="2",["vaultId"]="vault-main",
                ["cashboxAmount"]=75,["bankAmount"]=0,["petroAppAmount"]=0,
                ["moneyMovementSourceIds"]=new JsonArray("mv-other-purpose")
            },tx);
            Store.Save(c,"erp_money_movement",new()
            {
                ["id"]="mv-other-purpose",["erpSourceId"]="mv-other-purpose",["companyId"]=company,
                ["occurredAt"]="2026-11-20T10:00:00Z",
                ["fund"]="Cashbox",["fundCode"]="1",["purposeCode"]="TransferredDebtRepayment",["directionCode"]="1",
                ["amount"]=75,["vaultId"]="vault-main",["handoverId"]="ho-no-settle"
            },tx);
            var day=JahezReconciliation.OpenDay(c,tx,Maker,new()
            {
                ["companyId"]=company,["businessDate"]="2026-11-20",["vaultId"]="vault-main"
            });
            dayId=day.S("id");
            Assert.AreEqual(0m,day.N("handedCashAmount"));
            Assert.AreEqual(75m,day.N("handedExternalAmount"));
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>
        {
            JahezReconciliation.SubmitDay(c,tx,Maker,dayId);
            return true;
        })).Status);
    }

    [TestMethod]
    public void VaultCount_Approve_RecomputesBook_AndBlocksLaterPostedLines()
    {
        // Finding 5 — TreasuryVaultCountsApi.cs:122–141: approve must recompute book/variance;
        // a line dated on/before count posted after create changes the journal; a later line blocks approve.
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
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,100,0,0,100,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            // Create with book=100, counted=130 → stored variance +30.
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-15",["countedAmount"]=130
            });
            Assert.AreEqual(100m,created.N("bookBalance"));
            Assert.AreEqual(30m,created.N("variance"));
            string countId=created.S("id");
            TreasuryVaultCountsApi.Submit(c,tx,Maker,countId);
            // Post +40 on count date AFTER create → live book=140, variance=130-140=−10.
            PostSyntheticJournal(c,tx,company,periodId,accountId,equity,"2026-11-15",40);
            var approved=TreasuryVaultCountsApi.Approve(c,tx,Checker,countId);
            Assert.AreEqual("approved",approved.S("status"));
            Assert.AreEqual(140m,approved.N("bookBalance"));
            Assert.AreEqual(-10m,approved.N("variance"));
            object? shortDebit=SchemaSql.Scalar(c,tx,$"""
                SELECT jl.debit FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                WHERE j.source_type='vault_count' AND j.source_id=@sid AND a.code='542102' AND jl.debit>0
                """,("@sid",countId));
            Assert.AreEqual(10m,Convert.ToDecimal(shortDebit,CultureInfo.InvariantCulture));
            return true;
        });
        // Separate count: later-dated posted line must block approve (not only create).
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            string reg=EnsureRegistration(c,tx,company);
            string box=EnsureCashBox(c,tx,company,reg,"111104");
            string accountId=AccountId(c,tx,"111104");
            string equity=AccountId(c,tx,"212103");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            Guid companyGuid=Guid.Parse(company),accountGuid=Guid.Parse(accountId);
            byte[] dim=SchemaFinPeriodBalance.ComputeDimensionKey(companyGuid,accountGuid,SchemaOrgCostCentre.NoneId,"none");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                (id,company_id,period_id,period_month,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_at)
                VALUES(@id,@c,@p,'2026-11-01',@a,@cc,'none',@dim,50,0,0,50,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",company),("@p",SchemaSql.GuidText(periodId)),
                ("@a",accountId),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@dim",dim));
            var created=TreasuryVaultCountsApi.Create(c,tx,Maker,new()
            {
                ["cashBoxId"]=box,["countedOn"]="2026-11-12",["countedAmount"]=50
            });
            string countId=created.S("id");
            TreasuryVaultCountsApi.Submit(c,tx,Maker,countId);
            PostSyntheticJournal(c,tx,company,periodId,accountId,equity,"2026-11-25",20);
            Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>
                TreasuryVaultCountsApi.Approve(c,tx,Checker,countId)).Status);
            return true;
        });
    }

    [TestMethod]
    public void BalancesTree_TwoBoxesOnOneAccount_DoNotDoubleCash()
    {
        // Finding 9 / new HIGH — TreasuryBalancesApi.cs:39–48 + SchemaTreasury: join must not
        // multiply closing; active+inactive box on same account seeds 250 → cash 250, not 500.
        // Second active box on the same company+account is rejected by unique index.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        f.Store.Write((c,tx)=>
        {
            EnsureSponsorCompany(c,tx,company);
            SchemaTreasury.Ensure(c,tx);
            string regA=EnsureRegistration(c,tx,company);
            string regB=EnsureSecondRegistration(c,tx,company);
            InsertCashBox(c,tx,company,regA,"111103","BOX-A3A",status:1);
            InsertCashBox(c,tx,company,regB,"111103","BOX-A3B",status:2); // inactive twin still joins if query multiplies
            bool secondActiveRejected=false;
            try{InsertCashBox(c,tx,company,regB,"111103","BOX-A3C",status:1);}
            catch{secondActiveRejected=true;}
            Assert.IsTrue(secondActiveRejected,"second active cash_box on same account must violate unique index");
            Guid periodId=EnsurePeriod(c,tx,company,"2026-11-01");
            SeedBalance(c,tx,company,periodId,"111103",250);
            return true;
        });
        var tree=f.Store.Read(c=>TreasuryBalancesApi.Tree(c,null,Maker,"2026-11",company));
        var companyNode=tree["items"]!.AsArray().OfType<JsonObject>().Single(n=>n.S("companyId")==company);
        Assert.AreEqual(250m,companyNode.N("cash"));
        Assert.AreEqual(250m,companyNode.N("total"));
        decimal accountSum=companyNode["children"]!.AsArray().OfType<JsonObject>()
            .SelectMany(ch=>ch.S("kind")=="registration"
                ?ch["children"]!.AsArray().OfType<JsonObject>()
                :new[]{ch})
            .Where(n=>n.S("accountCode")=="111103")
            .Sum(n=>n.N("balance"));
        Assert.AreEqual(250m,accountSum);
    }

    static string PostSyntheticJournal(DbConnection c,DbTransaction tx,string company,Guid periodId,string debitAccount,string creditAccount,string journalDate,decimal amount)
    {
        string journalId=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal")}
            (id,voucher_no,period_id,company_id,period_month,journal_date,source_type,source_id,purpose,status,run_mode,created_at)
            VALUES(@id,@id,@p,@c,'2026-11-01',@d,'synthetic',@id,'manual',1,0,{SchemaSql.NowLiteral(c)})
            """,("@id",journalId),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@d",journalDate));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
            (id,period_id,company_id,period_month,journal_id,line_no,account_id,cost_centre_id,debit,credit,created_at)
            VALUES(@id,@p,@c,'2026-11-01',@j,1,@a,@cc,@amt,0,{SchemaSql.NowLiteral(c)})
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(periodId)),("@c",company),("@j",journalId),
            ("@a",debitAccount),("@cc",SchemaSql.GuidText(SchemaOrgCostCentre.NoneId)),("@amt",amount));
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
    static string EnsureSecondRegistration(DbConnection c,DbTransaction tx,string companyId)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"hr","commercial_registration")}(id,company_id,cr_number,status,created_at)
            VALUES(@id,@c,@cr,1,{SchemaSql.NowLiteral(c)})
            """,("@id",id),("@c",companyId),("@cr","9"+id.Replace("-","")[..19]));
        return id;
    }
    static string EnsureCashBox(DbConnection c,DbTransaction tx,string companyId,string registrationId,string accountCode)
    {
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","cash_box")}(id,company_id,registration_id,code,name_ar,account_id)
            VALUES(@id,@c,@r,@code,'خزنة اختبار',@a)
            """,("@id",id),("@c",companyId),("@r",registrationId),("@code","BOX-"+id.Replace("-","")[..16]),("@a",AccountId(c,tx,accountCode)));
        return id;
    }
    static void InsertCashBox(DbConnection c,DbTransaction tx,string companyId,string registrationId,string accountCode,string code,int status)
    {
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","cash_box")}(id,company_id,registration_id,code,name_ar,account_id,status)
            VALUES(@id,@c,@r,@code,'خزنة مكررة',@a,@s)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@c",companyId),("@r",registrationId),
            ("@code",code),("@a",AccountId(c,tx,accountCode)),("@s",status));
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
