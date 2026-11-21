using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class Rwp1FixTests
{
    const string Rider="2000000611";
    static string Bw=>FinancialCore.Sponsors[0].Id;

    [TestMethod]
    public void Row1_LegacyPaymentsRouteRefusesMoneyOutWithoutDual()
    {
        Assert.IsFalse(UserAccess.Allowed("Accountant","POST","/api/payments"));
        Assert.IsFalse(AccessPolicy.Allowed(UserAccess.Principal(new(){["username"]="acct",["role"]="Accountant"}),"POST","/api/payments"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row2_LeaseDepositCopiesRunModeAndPayoutBlocked(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var supplier=f.Store.Write((c,tx)=>PaymentDocuments.Supplier(c,tx,f.Maker,new(){["companyId"]=f.Company,["nameAr"]="مؤجر وديعة"}));
        var property=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"properties",new(){["companyId"]=f.Company,["nameAr"]="عقار وديعة",["propertyType"]="office",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")}));
        var lease=f.Store.Write((c,tx)=>PaymentContracts.Create(c,tx,f.Maker,"leases",new(){["companyId"]=f.Company,["nameAr"]="إيجار محاكاة",["propertyId"]=property.S("id"),["landlordSupplierId"]=supplier.S("id"),["startOn"]="2026-11-01",["months"]=3,["frequencyMonths"]=3,["rentPerPayment"]=300m,["deposit"]=150m,["runMode"]=1,["payoutBlocked"]=true}));
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document WHERE reference LIKE '%:deposit'"));
        Assert.AreEqual(1L,f.Scalar("SELECT run_mode FROM pay_document WHERE reference LIKE '%:deposit'"));
        Assert.AreEqual(1L,f.Scalar("SELECT payout_blocked FROM pay_document WHERE reference LIKE '%:deposit'"));
        Assert.AreEqual(150m,Convert.ToDecimal(f.Scalar("SELECT amount FROM pay_document WHERE reference LIKE '%:deposit'")));
        _=lease;
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row3_ScopedCashierMayPayAssignedVaultMappedChartCode(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        f.Store.Write((c,tx)=>
        {
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'BW','7015658094','شركة البوابة',1,{SchemaSql.NowLiteral(c)})",("@id",Bw));
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,{SchemaSql.NowLiteral(c)})",("@id",MoneySchemaFixture.Id()),("@c",Bw));
            Guid Account(string code)=>Guid.Parse(MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_account WHERE code=@code",("@code",code))!.ToString()!);
            string period=MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_accounting_period WHERE company_id=@c",("@c",Bw))!.ToString()!;
            JournalPosting.Post(c,tx,new(Guid.Parse(Bw),Guid.Parse(period),new(2026,11,1),new(2026,11,1),"synthetic_opening",Guid.NewGuid(),"opening",0,false,"wp-maker",[
                new(Account("111201"),SchemaOrgCostCentre.NoneId,"none",5000,0,null),new(Account("331301"),SchemaOrgCostCentre.NoneId,"none",0,5000,null)]));
            return true;
        });
        var cashier=UserAccess.Principal(new JsonObject
        {
            ["username"]="wp-cashier",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray("BW"),["accountCodes"]=new JsonArray("BANK.BW"),["platforms"]=new JsonArray("Ninja"),["categoryCodes"]=new JsonArray("CashAdvance")},
            ["customPermissions"]=new JsonArray("payments.disburse","payments.view","approvals.decide",ApprovalsArea.DualPaymentOut)
        });
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="wp-cashier",["username"]="wp-cashier",["role"]="FinancialAccountant"},tx);return true;});
        var input=new JsonObject{["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=Bw,["type"]="expense",["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-SCOPE",["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=80m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString()})};
        var doc=f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,input));
        f.Approve(doc.S("id"));
        var reserved=f.Store.Write((c,tx)=>PaymentsEngine.Reserve(c,tx,cashier,doc.S("id"),new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["amount"]=80m,["paidOn"]="2026-11-20",["bankAccountCode"]="111201",["reference"]="SYN-SCOPED"}));
        Assert.AreEqual(80m,reserved.N("amount"));
        Assert.AreEqual("111201",f.Store.Read(c=>MoneySchemaFixture.Scalar(c,null,"SELECT code FROM fin_account WHERE id=@id",("@id",reserved.S("bankAccountId")))!.ToString()));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row4_BearerDocumentResolvesTrustedPlatformAccountScope(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D"),foreign=Guid.NewGuid().ToString("D");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee,["assignments"]=new JsonArray(new JsonObject{["riderId"]=employee,["EffectiveFrom"]="2026-11-01"})},tx);
            Store.Save(c,"platform_account",new(){["id"]=foreign,["companyId"]=FinancialCore.Sponsors[1].Id,["sponsorId"]=FinancialCore.Sponsors[1].Id,["platformCode"]="keeta",["registeredEmployeeId"]=employee},tx);
            return true;
        });
        var ok=f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"hr_fee",new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-ATTR",["bearer"]="rider",["amount"]=55m,["riderShare"]=55m,["companyShare"]=0m,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["category"]="WorkPermit",["companyExpenseAccountCode"]="526101"}));
        Assert.AreEqual(55m,ok.N("amount"));
        Assert.AreEqual("Ninja",ok.S("platform"));
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"hr_fee",new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-MISS",["bearer"]="rider",["amount"]=10m,["riderShare"]=10m,["companyShare"]=0m,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=Guid.NewGuid().ToString("D"),["category"]="WorkPermit",["companyExpenseAccountCode"]="526101"}))).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentDocuments.BearerDocument(c,tx,f.Maker,"hr_fee",new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-FOR",["bearer"]="rider",["amount"]=10m,["riderShare"]=10m,["companyShare"]=0m,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=foreign,["category"]="WorkPermit",["companyExpenseAccountCode"]="526101"}))).Status);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5_OneOpenAdvanceSharedAcrossLegacyAndPaymentRoutes(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Open Advance",["sponsorId"]=f.Company,["financeSponsorId"]="BW",["erpId"]=employee},tx);
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee},tx);
            OrgDirectory.AddSupervision(c,tx,f.Maker,new(){["supervisorUserId"]="wp-maker",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            Store.Save(c,AdvanceRequests.Kind,new(){["id"]="adv:open-legacy",["riderIqama"]=Rider,["riderName"]="Synthetic",["companyId"]=Bw,["type"]="cash",["amount"]=90m,["vaultCode"]="MAIN.BW.DEBT",["reason"]="open",["status"]="under_review",["requestedBy"]="wp-maker",["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["decisions"]=new JsonArray()},tx);
            return true;
        });
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-DUP",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["advanceType"]="cash",["amount"]=40m}))).Status);
        f.Store.Write((c,tx)=>{var r=Store.Get(c,"adv:open-legacy",AdvanceRequests.Kind,tx)!;r["status"]="paid";Store.Save(c,AdvanceRequests.Kind,r,tx);return true;});
        var created=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-OK",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["advanceType"]="cash",["amount"]=40m}));
        Assert.AreEqual(40m,created.N("amount"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>AdvanceRequests.Create(c,tx,f.Maker,new(){["riderIqama"]=Rider,["type"]="cash",["amount"]=25m,["reason"]="second open"}))).Status);
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row4c_GenericQuickDocumentRequiresTrustedAttribution(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D"),foreign=Guid.NewGuid().ToString("D");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee,["assignments"]=new JsonArray(new JsonObject{["riderId"]=employee})},tx);
            Store.Save(c,"platform_account",new(){["id"]=foreign,["companyId"]=FinancialCore.Sponsors[1].Id,["sponsorId"]=FinancialCore.Sponsors[1].Id,["platformCode"]="keeta",["registeredEmployeeId"]=employee},tx);
            return true;
        });
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="expense",["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-QD-MISS",["lines"]=new JsonArray(new JsonObject{["accountCode"]="112501",["amount"]=65m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["erpRiderId"]=employee,["erpPlatformAccountId"]=Guid.NewGuid().ToString("D")})}))).Status);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="expense",["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-QD-FOR",["lines"]=new JsonArray(new JsonObject{["accountCode"]="112501",["amount"]=65m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["erpRiderId"]=employee,["erpPlatformAccountId"]=foreign})}))).Status);
        var ok=f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="expense",["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-QD-OK",["lines"]=new JsonArray(new JsonObject{["accountCode"]="112501",["amount"]=65m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["erpRiderId"]=employee,["erpPlatformAccountId"]=platform})}));
        Assert.AreEqual(65m,ok.N("amount"));
        Assert.AreEqual("Ninja",ok.S("platform"));
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document_line WHERE erp_rider_id IS NOT NULL AND amount=65"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row4d_IdempotentCreateReplayChecksSavedDocumentPlatformScope(bool sql)
    {
        // RWP1d Finding 4: same original body after platform access is revoked must Scope the saved doc (404), not return it.
        // Exact numbers from Codex counterexample: amount 65, stored platform Ninja, actor platforms={Keeta}.
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D"),key="synthetic-key-rwp1d-replay";
        f.Store.Write((c,tx)=>
        {
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'BW','7015658094','شركة البوابة',1,{SchemaSql.NowLiteral(c)})",("@id",Bw));
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,{SchemaSql.NowLiteral(c)})",("@id",MoneySchemaFixture.Id()),("@c",Bw));
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=Bw,["sponsorId"]=Bw,["platformCode"]="ninja",["registeredEmployeeId"]=employee,["assignments"]=new JsonArray(new JsonObject{["riderId"]=employee})},tx);
            return true;
        });
        var body=new JsonObject{["type"]="expense",["companyId"]=Bw,["idempotencyKey"]=key,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-REPLAY",["lines"]=new JsonArray(new JsonObject{["accountCode"]="112501",["amount"]=65m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["erpRiderId"]=employee,["erpPlatformAccountId"]=platform})};
        var created=f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,body.DeepClone().AsObject()));
        Assert.AreEqual(65m,created.N("amount"));
        Assert.AreEqual("Ninja",created.S("platform"));
        Assert.AreEqual(created.S("id"),f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,f.Maker,body.DeepClone().AsObject())).S("id"));
        var restricted=UserAccess.Principal(new JsonObject
        {
            ["username"]="wp-keeta-only",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray("BW"),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray("Keeta"),["categoryCodes"]=new JsonArray()},
            ["customPermissions"]=new JsonArray("payments.disburse","payments.view")
        });
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="wp-keeta-only",["username"]="wp-keeta-only",["role"]="FinancialAccountant"},tx);return true;});
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PaymentsEngine.Get(c,null,restricted,created.S("id")))).Status);
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,restricted,body.DeepClone().AsObject()))).Status);
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document WHERE idempotency_key='"+key+"'"));
        Assert.AreEqual(65m,Convert.ToDecimal(f.Scalar("SELECT amount FROM pay_document WHERE idempotency_key='"+key+"'")));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5c_SameAdvanceKeyAcrossCompaniesKeepsOneOpen(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platformA=Guid.NewGuid().ToString("D"),platformB=Guid.NewGuid().ToString("D"),companyB=FinancialCore.Sponsors[1].Id,key="synthetic-retry-key-rwp1c";
        f.Store.Write((c,tx)=>
        {
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'EX','7034861059','شركة إكسبرس',1,{SchemaSql.NowLiteral(c)})",("@id",companyB));
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,{SchemaSql.NowLiteral(c)})",("@id",MoneySchemaFixture.Id()),("@c",companyB));
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic Cross Key",["sponsorId"]=f.Company,["financeSponsorId"]="BW",["erpId"]=employee},tx);
            Store.Save(c,"platform_account",new(){["id"]=platformA,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee},tx);
            Store.Save(c,"platform_account",new(){["id"]=platformB,["companyId"]=companyB,["sponsorId"]=companyB,["platformCode"]="ninja",["registeredEmployeeId"]=employee},tx);
            OrgDirectory.AddSupervision(c,tx,f.Maker,new(){["supervisorUserId"]="wp-maker",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            return true;
        });
        var first=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=key,["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-KEY-A",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platformA,["advanceType"]="cash",["amount"]=45m}));
        Assert.AreEqual(45m,first.N("amount"));
        Assert.AreEqual(f.Company,first.S("companyId"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=key,["companyId"]=companyB,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-KEY-B",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platformB,["advanceType"]="cash",["amount"]=45m}))).Status);
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_advance_request"));
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document WHERE type='advance' AND status IN (1,2,3)"));
        var retry=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=key,["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-KEY-A",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platformA,["advanceType"]="cash",["amount"]=45m}));
        Assert.AreEqual(first.S("id"),retry.S("id"));
        Assert.AreEqual(45m,retry.N("amount"));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row6_WalletMetadataOnlyFromAuthorizedFundingDebit(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var wallet=f.Store.Write((c,tx)=>PaymentWallets.Create(c,tx,f.Maker,"prepaid",new(){["companyId"]=f.Company,["provider"]="مقدم",["accountCode"]="112404",["kind"]="mada"}));
        var sneaky=f.Input("expense",70m);sneaky["walletId"]=wallet.S("id");sneaky["walletKind"]="prepaid";
        var doc=f.Create(sneaky);f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),70);f.Dual(p);
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Pay(p.S("id"))).Status);
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM pay_prepaid_wallet_movement"));
        var fund=f.Input("mada_topup",100m);fund.Remove("lines");fund["amount"]=100m;
        var funded=f.Store.Write((c,tx)=>PaymentWallets.Fund(c,tx,f.Maker,"prepaid",wallet.S("id"),fund));
        f.Approve(funded.S("id"));var pay=f.Reserve(funded.S("id"),100);f.Dual(pay);f.Pay(pay.S("id"));
        Assert.AreEqual(100m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(amount),0) FROM pay_prepaid_wallet_movement")));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row7_MadaCardIssueWithdrawsWalletAtomically(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D");
        ConfigLists.Seed(f.Store);
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Card Rider",["sponsorId"]=f.Company,["erpId"]=employee},tx);
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee},tx);
            OrgDirectory.AddSupervision(c,tx,f.Maker,new(){["supervisorUserId"]="wp-maker",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            OrgDirectory.AddMembership(c,tx,f.Maker,new(){["userId"]="wp-maker",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddMembership(c,tx,f.Maker,new(){["userId"]="wp-checker",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,f.Maker,new(){["operation"]="advance.payout:"+AdvanceRequests.Mada,["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("wp-checker"),["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,f.Maker,new(){["operation"]="advance.review",["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("wp-checker"),["validFrom"]="2026-01-01"});
            return true;
        });
        f.Store.Write((c,tx)=>{ConfigLists.Upsert(c,tx,f.Maker,"recharge_packages",new(){["code"]="STC40",["nameAr"]="باقة",["values"]=new JsonObject{["network"]="STC",["price"]=40m}});ConfigLists.Approve(c,tx,f.Checker,"recharge_packages","STC40");return true;});
        var wallet=f.Store.Write((c,tx)=>PaymentWallets.Create(c,tx,f.Maker,"prepaid",new(){["companyId"]=f.Company,["provider"]="مدى",["accountCode"]="112404",["kind"]="mada"}));
        var top=f.Input("mada_topup",100m);top.Remove("lines");top["amount"]=100m;
        var funded=f.Store.Write((c,tx)=>PaymentWallets.Fund(c,tx,f.Maker,"prepaid",wallet.S("id"),top));f.Approve(funded.S("id"));var fundPay=f.Reserve(funded.S("id"),100);f.Dual(fundPay);f.Pay(fundPay.S("id"));
        Assert.AreEqual(100m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(amount),0) FROM pay_prepaid_wallet_movement")));
        var adv=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-CARD",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,["advanceType"]="internet",["fulfilment"]="recharge_card",["networkCode"]="STC",["packageCode"]="STC40",["amount"]=40m,["monthlyInstallment"]=40m,["payoutAccountCode"]="112404"}));
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,adv.S("id"),"submit",new()));
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Checker,adv.S("id"),"approve",new(){["coverageReason"]="Synthetic package covered for test"}));
        var reserved=f.Store.Write((c,tx)=>PaymentsEngine.Reserve(c,tx,f.Checker,adv.S("id"),new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["amount"]=40m,["paidOn"]="2026-11-20",["bankAccountCode"]="112404",["reference"]="SYN-CARD-PV"}));
        // Dual approval must be independent of the payout requester (checker).
        f.Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,f.Maker,reserved.S("approvalRequestId"),new(){["decision"]="approve"}));
        var paid=f.Store.Write((c,tx)=>PaymentAdvances.IssueCard(c,tx,f.Checker,new(){["paymentId"]=reserved.S("id")}));
        Assert.AreEqual(2m,paid.N("status"));
        Assert.AreEqual(60m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(amount),0) FROM pay_prepaid_wallet_movement")));
        Assert.AreEqual(-40m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(amount),0) FROM pay_prepaid_wallet_movement WHERE source_type='card_issue'")));
        f.Store.Write((c,tx)=>PaymentAdvances.IssueCard(c,tx,f.Checker,new(){["paymentId"]=reserved.S("id")}));
        Assert.AreEqual(60m,Convert.ToDecimal(f.Scalar("SELECT COALESCE(SUM(amount),0) FROM pay_prepaid_wallet_movement")));
    }
}
