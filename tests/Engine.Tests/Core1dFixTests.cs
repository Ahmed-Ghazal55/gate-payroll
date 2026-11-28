using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>CORE1d fix round — every PARTLY/new row from grok-CORE1c.md (synthetic data only).</summary>
[TestClass]
public sealed class Core1dFixTests
{
    [TestMethod]
    public void High_KeetaDeriveCategory_BikeOnCar_NotPrivate_TriggersMismatch()
    {
        // PARTLY HIGH #2: دراجة + سيارة must not become sponsored_private; bike-on-car cell must run.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1d-boc-inv",account="synthetic-core1d-boc-acc",rider="2000000301";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["sponsorId"]=company,["isDeliveryRider"]=true,["engagementType"]="SponsoredInternal"},tx);
            Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="keeta",["externalAccountId"]="SYN-CORE1D-BOC",["code"]="SYN-CORE1D-BOC",
                ["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1d-boc-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=5,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            return true;
        });
        var row=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        row["المعرف"]="SYN-CORE1D-BOC";row["الاسم بالكامل"]="Synthetic BikeOnCar";row["رقم بطاقة الهوية"]=rider;row["رقم الهوية1"]=rider;
        row["كفالة اليوزر"]="سجل";row["المركبة"]="دراجة";row["الحالة"]="TRUE";row["نوع المركبة"]="سيارة";
        row["الأيام الصالحة"]=20;row["الطلبات"]=5;row["المخالفات"]=0;row["المسافة المدفوعة"]=1m;row["عدد ايام عمل المندوب"]=18;
        row["الصلاحية المتوقعة"]="متوقع";row["اجمالي المسافه"]=1m;
        row["تاريخ الاستلام1"]="2026-10-01";row["تاريخ التسليم"]="2026-10-31";
        var bytes=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{KeetaOperationsImport.Headers.Select(h=>row[h]).ToArray()});
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new JsonObject{["invoiceId"]=invoice,["register"]="BW"},bytes));
        var share=preview["rows"]![0]!["accounts"]![0]!["riders"]![0]!.AsObject();
        Assert.AreEqual("sponsored_bike_on_car_id",share.S("category"));
        Assert.AreNotEqual("sponsored_private",share.S("category"));
        // Custody/rent type for bike_on_car_id is company_bike — private vs car must NOT silence the mismatch.
        string categoryType=share.S("category").EndsWith("_bike_on_car_id",StringComparison.Ordinal)?"company_bike"
            :share.S("category").EndsWith("_company_bike",StringComparison.Ordinal)?"company_bike"
            :share.S("category").EndsWith("_company_car",StringComparison.Ordinal)?"company_car":"private";
        Assert.AreEqual(VehicleIdentity.Bike,categoryType);
        Assert.AreEqual(VehicleIdentity.Car,VehicleIdentity.NormalizePlatform(share.S("platformVehicleType")));
        Assert.AreEqual(VehicleIdentity.MismatchBikeOnCar,
            VehicleIdentity.MismatchCode(categoryType,VehicleIdentity.NormalizePlatform(share.S("platformVehicleType"))));
    }

    [TestMethod]
    public void Medium_ParentTerms_MatchingEligibility_Equal_WhenInvoiceTierMissing()
    {
        // PARTLY MEDIUM #6: Keeta one expected label must not force tier delta / differs.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1d-pt-inv",account="synthetic-core1d-pt-acc",rider="2000000302";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Keeta",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1d-pt-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=10,["deductionAmount"]=0,["deductions"]=new JsonArray(),
                ["sourceMetrics"]=new JsonObject{["صالح"]="شريحة أ"}},tx);
            Store.Save(c,OperationsImports.Kind,new()
            {
                ["id"]="synthetic-core1d-pt-ops",["app"]="keeta",["period"]="2026-10",["invoiceId"]=invoice,["companyId"]=company,
                ["review"]=new JsonObject{["accounts"]=new JsonArray(new JsonObject{
                    ["accountId"]=account,
                    ["riders"]=new JsonArray(new JsonObject{
                        ["rider"]=rider,
                        // Only eligibility label — no separate expectedTier key.
                        ["metrics"]=new JsonObject{["expectedEligibility"]="شريحة أ"}
                    })
                })}
            },tx);
            return true;
        });
        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"keeta","2026-10"));
        var row=display["comparison"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual("equal",row.S("match"));
        Assert.AreEqual("",row.S("expectedTier"));
        Assert.IsFalse(row.B("tierDelta"));
        Assert.IsFalse(row.B("statusDelta"));
    }

    [TestMethod]
    public void Medium_ParentTerms_NinjaSnapshot_DoesNotUsePolicyVersionAsTier_AndKeepsAccountIds()
    {
        // PARTLY #6: policyVersion ≠ tier; Operations FieldMap keeps snapshot account ids.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1d-nj-inv",account="synthetic-core1d-nj-acc";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Ninja",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=40,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1d-nj-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=40,["incentives"]=0,["orders"]=4,["deductionAmount"]=0,["deductions"]=new JsonArray(),
                ["sourceMetrics"]=new JsonObject{["صالح"]="متوقع"}},tx);
            Store.Save(c,"ninja_evaluation_snapshot",new()
            {
                ["id"]="synthetic-core1d-snap",["period"]="2026-10",["frozen"]=true,["policyVersion"]="v9-not-a-tier",
                ["inputRevision"]="1",["reason"]="synthetic",["createdAt"]="2026-10-15T00:00:00Z",
                ["expectedStatus"]="متوقع",
                ["accountIds"]=new JsonArray(account),
                ["accountIdsJson"]="[\""+account+"\"]"
            },tx);
            return true;
        });
        var projected=Operations.Project(new JsonObject
        {
            ["id"]="synthetic-core1d-snap",["frozen"]=true,["policyVersion"]="v9-not-a-tier",
            ["inputRevision"]="1",["reason"]="synthetic",["createdAt"]="2026-10-15T00:00:00Z",
            ["accountIds"]=new JsonArray(account),["accountIdsJson"]="[\""+account+"\"]"
        },"ninja_evaluation_snapshot");
        Assert.AreEqual("[\""+account+"\"]",projected.S("accountIdsJson"));
        Assert.IsTrue(projected["accountIds"] is JsonArray ids&&ids.Count==1);

        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"ninja","2026-10"));
        var row=display["comparison"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("accountId")==account);
        Assert.AreNotEqual("v9-not-a-tier",row.S("expectedTier"));
        Assert.AreEqual("",row.S("expectedTier"));
        Assert.AreEqual("equal",row.S("match"));
    }

    [TestMethod]
    public void Medium_ParentTerms_NoInventedInvoiceRate_FromGrossOverOrders()
    {
        // NEW MEDIUM #1: without a rate column, do not treat baseGross/orders as invoiceRate.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1d-rate-inv",account="synthetic-core1d-rate-acc",rider="2000000303";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Keeta",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1d-rate-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=10,["deductionAmount"]=0,["deductions"]=new JsonArray(),
                ["sourceMetrics"]=new JsonObject{["صالح"]="شريحة أ"}},tx);
            Store.Save(c,OperationsImports.Kind,new()
            {
                ["id"]="synthetic-core1d-rate-ops",["app"]="keeta",["period"]="2026-10",["invoiceId"]=invoice,["companyId"]=company,
                ["review"]=new JsonObject{["accounts"]=new JsonArray(new JsonObject{
                    ["accountId"]=account,
                    ["riders"]=new JsonArray(new JsonObject{
                        ["rider"]=rider,
                        ["metrics"]=new JsonObject{["expectedEligibility"]="شريحة أ",["expectedStatus"]="شريحة أ",["expectedRate"]=9m}
                    })
                })}
            },tx);
            return true;
        });
        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"keeta","2026-10"));
        var row=display["comparison"]!.AsArray().OfType<JsonObject>().Single();
        Assert.IsTrue(row["invoiceRate"] is null || row["invoiceRate"]!.GetValueKind()==System.Text.Json.JsonValueKind.Null,
            "invoiceRate must stay null when no rate column (not 100/10=10)");
        Assert.IsFalse(row.B("rateDelta"));
        Assert.AreEqual("equal",row.S("match"));
    }
}
