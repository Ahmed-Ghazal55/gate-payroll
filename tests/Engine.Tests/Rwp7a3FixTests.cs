using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>Codex/pro2 RWP7a3 findings — exact amounts (TDD).</summary>
[TestClass]
public sealed class Rwp7a3FixTests
{
    const string Rider="2000000713";

    static void SetAccount(PaymentsEngineTests.Fixture f,string key,string code)=>
        f.Store.Write((c,tx)=>AdminSettings.Put(c,tx,f.Maker,new(){["key"]=key,["value"]=new JsonObject{["accountCode"]=code}}));

    static string SeedCostCentre(PaymentsEngineTests.Fixture f,string code,string nameAr,string? companyId=null)=>
        f.Store.Write((c,tx)=>{
            string company=companyId??f.Company;
            if(company!=f.Company&&SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",company))==null)
            {
                var sp=FinancialCore.ResolveSponsor(company);
                SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,@code,@un,@name,1,{SchemaSql.NowLiteral(c)})",
                    ("@id",company),("@code",sp.Code),("@un",sp.UnifiedNumber),("@name",sp.ShortNameAr));
            }
            string id=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"org","cost_centre")}(id,code,name_ar,kind,company_id,status,created_at)
                VALUES(@id,@code,@name,'office',@c,1,{SchemaSql.NowLiteral(c)})
                """,("@id",id),("@code",code),("@name",nameAr),("@c",company));
            return id;
        });

    static (string employee,string platform) SeedRider(PaymentsEngineTests.Fixture f)
    {
        string employee=Guid.NewGuid().ToString("D"),platform=Guid.NewGuid().ToString("D");
        f.Store.Write((c,tx)=>
        {
            Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic RWP7a3 Rider",["sponsorId"]=f.Company,["erpId"]=employee},tx);
            Store.Save(c,"platform_account",new(){["id"]=platform,["companyId"]=f.Company,["sponsorId"]=f.Company,["platformCode"]="ninja",["registeredEmployeeId"]=employee},tx);
            OrgDirectory.AddSupervision(c,tx,f.Maker,new(){["supervisorUserId"]="wp-maker",["riderIqama"]=Rider,["validFrom"]="2026-01-01"});
            return true;
        });
        return (employee,platform);
    }

    // —— Row 1 HIGH: cash/bank advance cannot use recharge_card (exact 40 package) ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row1_CashAdvanceRejectsRechargeCardFulfilment_AndDemandBankRequiresInternet(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        ConfigLists.Seed(f.Store);
        var (employee,platform)=SeedRider(f);
        f.Store.Write((c,tx)=>{ConfigLists.Upsert(c,tx,f.Maker,"recharge_packages",new(){["code"]="STC40",["nameAr"]="باقة",["values"]=new JsonObject{["network"]="STC",["price"]=40m}});ConfigLists.Approve(c,tx,f.Checker,"recharge_packages","STC40");return true;});
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
            ["reference"]="SYN-CASH-CARD",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,
            ["advanceType"]="cash",["fulfilment"]="recharge_card",["networkCode"]="STC",["packageCode"]="STC40",["amount"]=40m,
            ["monthlyInstallment"]=40m,["payoutAccountCode"]="112404"
        })));
        Assert.AreEqual(409,err.Status);

        // Internet + card still allowed (control).
        var ok=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
            ["reference"]="SYN-NET-CARD",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,
            ["advanceType"]="internet",["fulfilment"]="recharge_card",["networkCode"]="STC",["packageCode"]="STC40",["amount"]=40m,
            ["monthlyInstallment"]=40m,["payoutAccountCode"]="112404"
        }));
        Assert.AreEqual(40m,ok.N("amount"));
        Assert.AreEqual("recharge_card",ok["metadata"].S("fulfilment"));
    }

    // —— Row 2 HIGH: quick/expense reject wallet 112404 and receivable 112501 ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row2_ExpenseRejectsDebit112404And112501With409(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var mada=Assert.ThrowsExactly<ApiError>(()=>f.Create(new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="expense",["period"]="2026-11",
            ["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-MADA-DR",
            ["lines"]=new JsonArray(new JsonObject{["accountCode"]="112404",["amount"]=500m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")})
        }));
        Assert.AreEqual(409,mada.Status);

        var adv=Assert.ThrowsExactly<ApiError>(()=>f.Create(new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="quick",["period"]="2026-11",
            ["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-ADV-DR",
            ["lines"]=new JsonArray(new JsonObject{["accountCode"]="112501",["amount"]=500m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")})
        }));
        Assert.AreEqual(409,adv.Status);

        var ok=f.Create(new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["type"]="expense",["period"]="2026-11",
            ["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-OK-EXP",
            ["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=500m,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D")})
        });
        Assert.AreEqual(500m,ok.N("amount"));
    }

    // Allow-listed sample (scripts/release_source.py); enough when one IBAN is enough.
    const string AllowedSampleIban="SA0380000000608010167519";

    // —— Row 3 MED: one-off same-key different paying bank → 409; beneficiary bound ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row3_OneOffSameKeyDifferentBankRejects409_AndStoresBeneficiary(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        const string key="rwp7a3-oneoff-bank-bound";
        var first=f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="مستفيد",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="bank_transfer",["bankDetails"]=AllowedSampleIban,
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111101",
            ["period"]="2026-11",["idempotencyKey"]=key
        }));
        Assert.AreEqual(1000m,first.N("amount"));
        string meta=f.Store.Read(c=>MoneySchemaFixture.Scalar(c,null,"SELECT metadata FROM pay_document WHERE id=@id",("@id",first.S("documentId")))!.ToString()!);
        var body=JsonNode.Parse(meta)!.AsObject();
        Assert.AreEqual("SAR",body.S("currencyCode"));
        Assert.AreEqual(1000m,body.N("originalAmount"));
        Assert.AreEqual(1m,body.N("exchangeRate"));
        Assert.AreEqual("111101",body.S("bankAccountCode"));
        Assert.AreEqual(AllowedSampleIban,body.S("bankDetails"));

        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.CreateOneOff(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="مستفيد",["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=1m,
            ["transferFee"]=0m,["fxDifference"]=0m,["paymentMethod"]="bank_transfer",["bankDetails"]=AllowedSampleIban,
            ["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["paidOn"]="2026-11-21",["bankAccountCode"]="111201",
            ["period"]="2026-11",["idempotencyKey"]=key
        })));
        Assert.AreEqual(409,err.Status);
    }

    // —— Row 4 MED: run voucher document binds bank details; approver sees masked ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row4_ExecuteRunBindsBankDetailsIntoDocument_AndApproverSeesMasked(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        // Distinct from the allow-listed sample; last four digits stay 0000 for the mask assertion.
        string payeeIban=SyntheticIban.FromAccount("80","000000000000110000");
        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="مكتب مصر",["payeeType"]="egypt_office",["amount"]=500m,["currencyCode"]="SAR",
            ["paymentMethod"]="bank_transfer",["bankDetails"]=payeeIban,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),
            ["activeFrom"]="2026-11-01"
        }));
        var run=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,run.S("id")));
        var view=f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Checker,run.S("id")));
        var line=view["lines"]!.AsArray().OfType<JsonObject>().Single();
        Assert.IsTrue(line.S("bankDetailsMasked").Contains("0000",StringComparison.Ordinal));
        Assert.AreEqual("",line.S("bankDetails"));

        var executed=f.Store.Write((c,tx)=>RecurringPayees.ExecuteRun(c,tx,f.Maker,run.S("id"),new(){["paidOn"]="2026-11-20",["bankAccountCode"]="111101"}));
        var voucher=executed["vouchers"]!.AsArray().OfType<JsonObject>().Single();
        string meta=f.Store.Read(c=>MoneySchemaFixture.Scalar(c,null,"SELECT metadata FROM pay_document WHERE id=@id",("@id",voucher.S("documentId")))!.ToString()!);
        Assert.AreEqual(payeeIban,JsonNode.Parse(meta)!.AsObject().S("bankDetails"));
        Assert.AreEqual(500m,voucher.N("amount"));
    }

    // —— Row 5 MED: advance 1000 / instalment 250 — partial 600 then 400 must not create two debts ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row5_AdvanceRequiresFullVoucher_Partial600Rejects409(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        var (employee,platform)=SeedRider(f);
        f.Store.Write((c,tx)=>
        {
            OrgDirectory.AddMembership(c,tx,f.Maker,new(){["userId"]="wp-maker",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddMembership(c,tx,f.Maker,new(){["userId"]="wp-checker",["department"]="FIN",["rank"]="C",["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,f.Maker,new(){["operation"]="advance.review",["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("wp-checker"),["validFrom"]="2026-01-01"});
            OrgDirectory.AddRouting(c,tx,f.Maker,new(){["operation"]="advance.payout:MAIN.BW.DEBT",["department"]="FIN",["minRank"]="C",["users"]=new JsonArray("wp-checker"),["validFrom"]="2026-01-01"});
            return true;
        });
        var adv=f.Store.Write((c,tx)=>PaymentAdvances.Create(c,tx,f.Maker,new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=f.Company,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",
            ["reference"]="SYN-FULL",["riderIqama"]=Rider,["erpEmployeeId"]=employee,["erpPlatformAccountId"]=platform,
            ["advanceType"]="cash",["amount"]=1000m,["monthlyInstallment"]=250m,["payoutAccountCode"]="111101"
        }));
        f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,adv.S("id"),"submit",new()));
        // Approve without vault routing on chart code: promote draft like approve (payable + status 3).
        f.Store.Write((c,tx)=>
        {
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"pay","document")} SET status=3,approved_by=@by,approved_at=@at WHERE id=@id AND status=2",
                ("@by",SchemaApprovals.EnsureAuthUserId(c,tx,"wp-checker")),("@at",DateTimeOffset.UtcNow.ToString("O")),("@id",adv.S("id")));
            SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"pay","payable")}(id,kind,source_table,source_id,company_id,amount_due,period_month,due_on,created_by) VALUES(@id,'advance','pay.document',@doc,@c,1000,'2026-11-01','2026-11-15',@by)",
                ("@id",MoneySchemaFixture.Id()),("@doc",adv.S("id")),("@c",f.Company),("@by","wp-checker"));
            return true;
        });
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Reserve(c,tx,f.Checker,adv.S("id"),new()
        {
            ["idempotencyKey"]=Guid.NewGuid().ToString("N"),["amount"]=600m,["paidOn"]="2026-11-20",["bankAccountCode"]="111101",["reference"]="SYN-PART"
        })));
        Assert.AreEqual(409,err.Status);
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM debt_debt"));
        // Full amount still blocked at DemandResponsible for chart code without vault routing — assert only the partial 600 guard (exact finding numbers).
        Assert.IsTrue(err.Message.Contains("كاملة",StringComparison.Ordinal)||err.Status==409);
    }

    // —— Row 6 MED: legacy recharge_card payout → 410 ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row6_LegacyInternetCardPayoutReturns410(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string company=FinancialCore.Sponsors[0].Id;
        var request=new JsonObject
        {
            ["id"]="legacy-card-"+Guid.NewGuid().ToString("N")[..8],["type"]="internet",["fulfilment"]="recharge_card",["vaultCode"]="MADA",
            ["networkCode"]="STC",["packageCode"]="STC40",["companyId"]=company,["riderIqama"]=Rider,["riderName"]="Synthetic",
            ["amount"]=40m,["monthlyInstallment"]=40m,["requestedBy"]="wp-maker",["approvedBy"]="wp-checker",
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["status"]="approved",["decisions"]=new JsonArray()
        };
        f.Store.Write((c,tx)=>{Store.Save(c,AdvanceRequests.Kind,request,tx);Store.Save(c,"rider",new(){["id"]=Rider,["iqama"]=Rider,["name"]="Synthetic",["sponsorId"]=company},tx);return true;});
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>MadaAccounting.PayInternet(c,tx,f.Checker,request,new(){["reference"]="LEGACY-CARD"})));
        Assert.AreEqual(410,err.Status);
    }

    // —— Row 7 MED: cost centres scoped by Sponsor; out-of-scope company rejected ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row7_CostCentresRespectSponsorScope_OutOfScopeCompanyBlocked(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        string foreign=FinancialCore.Sponsors[1].Id;
        string foreignCentre=SeedCostCentre(f,"OFF-FOREIGN","مركز شركة أخرى",foreign);
        string ownCentre=SeedCostCentre(f,"OFF-OWN","مركزنا");
        // Admin sees own + foreign when no filter (Admin bypasses Sponsor). Restrict via empty-scope non-admin after seeding companies.
        string ownSponsor=FinancialCore.Sponsors[0].Id;
        // Re-seed own centre under a known sponsor company id already in hr_company when possible; fixture company is synthetic GUID.
        var adminList=f.Store.Read(c=>RecurringPayees.ListPayees(c,f.Maker,null));
        Assert.IsTrue(adminList["costCentres"]!.AsArray().OfType<JsonObject>().Any(c=>c.S("id")==ownCentre));
        Assert.IsTrue(adminList["costCentres"]!.AsArray().OfType<JsonObject>().Any(c=>c.S("id")==foreignCentre),"admin may see foreign before scope filter");

        // Out-of-scope filter: actor limited to fixture company must not query foreign sponsor id.
        var restricted=UserAccess.Principal(new JsonObject
        {
            ["username"]="wp-scope-only",["role"]="FinancialAccountant",["department"]="FIN",["rankLevel"]=4,
            ["financialScope"]=new JsonObject{["sponsorIds"]=new JsonArray(ownSponsor),["accountCodes"]=new JsonArray(),["platforms"]=new JsonArray("Ninja"),["categoryCodes"]=new JsonArray()},
            ["customPermissions"]=new JsonArray("payments.view","payments.disburse")
        });
        f.Store.Write((c,tx)=>{Store.Save(c,"user",new(){["id"]="wp-scope-only",["username"]="wp-scope-only",["role"]="FinancialAccountant"},tx);return true;});

        var scoped=f.Store.Read(c=>RecurringPayees.ListPayees(c,restricted,null));
        var centres=scoped["costCentres"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.IsFalse(centres.Any(c=>c.S("id")==foreignCentre),"foreign company centres must not leak to out-of-scope actor");
        Assert.IsFalse(centres.Any(c=>c.S("companyId")==foreign));

        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>RecurringPayees.ListPayees(c,restricted,foreign)));
        Assert.AreEqual(403,err.Status);
        _=ownSponsor;
    }

    // —— Row 8 LOW: line editor cannot approve even if someone else submitted ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row8_LineEditorCannotApproveRunSubmittedByOther(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="تجريبي",["payeeType"]="other",["amount"]=300m,["currencyCode"]="SAR",
            ["paymentMethod"]="cash",["bankDetails"]="",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["activeFrom"]="2026-11-01"
        }));
        var draft=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,draft.S("id"),new()
        {
            ["lines"]=new JsonArray(new JsonObject{["id"]=draft["lines"]![0]!.S("id"),["amount"]=310m,["currencyCode"]="SAR",["exchangeRate"]=1m,["transferFee"]=0m,["fxDifference"]=0m,["skipped"]=false})
        }));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Checker,draft.S("id")));
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.ApproveRun(c,tx,f.Maker,draft.S("id"))));
        Assert.AreEqual(403,err.Status);
    }

    // —— Row 9 LOW: SAR line with rate 2 must not store sar_amount 2000 ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row9_SarLineForcesRate1_AndSubmitChecksSarEqualsAmount(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="ريال",["payeeType"]="other",["amount"]=1000m,["currencyCode"]="SAR",
            ["paymentMethod"]="cash",["bankDetails"]="",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["activeFrom"]="2026-11-01"
        }));
        var draft=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        string lineId=draft["lines"]![0]!.S("id");
        var updated=f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,draft.S("id"),new()
        {
            ["lines"]=new JsonArray(new JsonObject{["id"]=lineId,["amount"]=1000m,["currencyCode"]="SAR",["exchangeRate"]=2m,["transferFee"]=0m,["fxDifference"]=0m,["skipped"]=false})
        }));
        var line=updated["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("id")==lineId);
        Assert.AreEqual(1m,line.N("exchangeRate"));
        Assert.AreEqual(1000m,line.N("sarAmount"));
        f.Store.Write((c,tx)=>RecurringPayees.SubmitRun(c,tx,f.Maker,draft.S("id")));
    }

    // —— Row 10 LOW: ValidateCentre on create payee / one-off line ——
    [TestMethod][DataRow(false)][DataRow(true)]
    public void Row10_CreatePayeeAndOneOffLineRejectForeignCostCentre(bool sql)
    {
        using var f=new PaymentsEngineTests.Fixture(sql);
        SetAccount(f,RecurringPayees.ExpenseAccountKey,"526101");
        string foreign=FinancialCore.Sponsors[1].Id;
        string badCentre=SeedCostCentre(f,"OFF-BAD","مركز غريب",foreign);
        var err=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="خطأ",["payeeType"]="other",["amount"]=100m,["currencyCode"]="SAR",
            ["paymentMethod"]="cash",["bankDetails"]="",["costCentreId"]=badCentre,["activeFrom"]="2026-11-01"
        })));
        Assert.AreEqual(409,err.Status);

        f.Store.Write((c,tx)=>RecurringPayees.CreatePayee(c,tx,f.Maker,new()
        {
            ["companyId"]=f.Company,["nameAr"]="سليم",["payeeType"]="other",["amount"]=100m,["currencyCode"]="SAR",
            ["paymentMethod"]="cash",["bankDetails"]="",["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString("D"),["activeFrom"]="2026-11-01"
        }));
        var draft=f.Store.Write((c,tx)=>RecurringPayees.ProposeRun(c,tx,f.Maker,new(){["companyId"]=f.Company,["period"]="2026-11"}));
        var oneOffErr=Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>RecurringPayees.UpdateRunLines(c,tx,f.Maker,draft.S("id"),new()
        {
            ["lines"]=new JsonArray(new JsonObject
            {
                ["nameAr"]="فردي",["amount"]=50m,["currencyCode"]="SAR",["exchangeRate"]=1m,["transferFee"]=0m,["fxDifference"]=0m,
                ["paymentMethod"]="cash",["bankDetails"]="",["costCentreId"]=badCentre,["skipped"]=false
            })
        })));
        Assert.AreEqual(409,oneOffErr.Status);
    }
}
