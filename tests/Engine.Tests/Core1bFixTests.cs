using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>CORE1b fix round — one test per Grok HIGH row (synthetic data only).</summary>
[TestClass]
public sealed class Core1bFixTests
{
    static JsonObject Values()=>PaySettings.Defaults()["values"]!.AsObject();
    static JsonObject Global()=>new(){["id"]="synthetic-global",["status"]="approved",["effectiveFrom"]="2026-01-01",["data"]=PaySettings.Defaults()};
    static JsonObject Scheme(string type="daily_commission")=>new(){["id"]="synthetic-scheme",["app"]="keeta",["period"]="2026-10",["status"]="approved",["data"]=PayFixture.Scheme(type)};

    [TestMethod]
    public void High_BikeOnCarId_ChargesCompanyBikeRent_NotPrivate()
    {
        // HIGH PayCycles.Calculation Vehicle + PayEvaluator: *_bike_on_car_id → company_bike rent (§24.4 = 800).
        var scheme=Scheme("percent_monthly_with_vehicle");
        var cells=scheme["data"]!["parameters"]!["categories"]!.AsObject();
        cells["sponsored_bike_on_car_id"]=new JsonObject
        {
            ["enabled"]=true,["companyPercent"]=10,["chargeCompanyVehicle"]=true,["prorate"]=true
        };
        var facts=new PayFacts("2000000101","keeta","2026-10","sponsored_bike_on_car_id",10,1000,0,15,"company_bike",15,null);
        var result=PayEvaluator.Evaluate(facts,scheme,null,Global());
        Assert.AreEqual(400m,result.VehicleCharge,"bike-on-car must charge half-month bike rent (800/30*15)");
        Assert.AreEqual("ready",result.Status);
        // Wrong vehicle type (private) must throw when charge is on — proves category expects company_bike.
        Assert.ThrowsExactly<ApiError>(()=>PayEvaluator.Evaluate(
            facts with{VehicleType="private",VehicleDays=0},scheme,null,Global()));
    }

    [TestMethod]
    public void High_MissingPlatformVehicle_ForcesNeedsDecision_NotSilentEqual()
    {
        // HIGH: empty platform type must not be replaced with rider type (would hide bike-on-car).
        // CORE1c: empty = platform does not supply type (no silent equal, and no forever-block).
        Assert.IsNull(VehicleIdentity.MismatchCode(VehicleIdentity.Bike,VehicleIdentity.Unknown));
        Assert.AreEqual(VehicleIdentity.Unknown,VehicleIdentity.Normalize(""));
        Assert.AreEqual(VehicleIdentity.MismatchBikeOnCar,
            VehicleIdentity.MismatchCode(VehicleIdentity.Bike,VehicleIdentity.Car));
        // When platform raw is present as car and rider is bike → mismatch cell category.
        Assert.AreEqual("sponsored_bike_on_car_id",
            VehicleIdentity.PayCategory("sponsored",VehicleIdentity.Bike,VehicleIdentity.Car));
    }

    [TestMethod]
    public void High_KeetaCompanyShortfall2000_ImportKeepsCompanyLevelOffRiders()
    {
        // HIGH KeetaOperationsImport: §24.6 company shortfall must not reject import; stay off rider accounts.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1b-inv",account="synthetic-core1b-acc",rider="2000000102";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["sponsorId"]=company,["isDeliveryRider"]=true,["engagementType"]="SponsoredInternal"},tx);
            Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="keeta",["externalAccountId"]="SYN-CORE1B",["code"]="SYN-CORE1B",
                ["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=120,["vat"]=0,["deductionsTotal"]=2000},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1b-acc-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=5,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            // Company shortfall row (§24.6): blank account, company_level deduction 2000, plus positive company baseGross 20.
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1b-co-line",["invoiceId"]=invoice,["platformAccountId"]="",["lineType"]="company",["baseGross"]=20,["incentives"]=0,["orders"]=0,["deductionAmount"]=2000,
                ["deductions"]=new JsonArray(new JsonObject{["category"]="company_shortfall",["amount"]=2000,["level"]="company_level",["expenseAccount"]="512101"})},tx);
            return true;
        });
        var row=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        row["المعرف"]="SYN-CORE1B";row["الاسم بالكامل"]="Synthetic Rider";row["رقم بطاقة الهوية"]=rider;row["رقم الهوية1"]=rider;
        row["كفالة اليوزر"]="سجل البوابا";row["المركبة"]="دراجة";row["الحالة"]="TRUE";row["نوع المركبة"]="دراجة شركة";
        row["الأيام الصالحة"]=20;row["الطلبات"]=5;row["المخالفات"]=0;row["المسافة المدفوعة"]=1m;row["عدد ايام عمل المندوب"]=18;
        row["الصلاحية المتوقعة"]="شريحة متوقعة";row["اجمالي المسافه"]=1m;
        row["تاريخ الاستلام1"]="2026-10-01";row["تاريخ التسليم"]="2026-10-31";
        // ExcelFiles.Write emits the header row; pass data rows only.
        var bytes=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{KeetaOperationsImport.Headers.Select(h=>row[h]).ToArray()});
        var input=new JsonObject{["invoiceId"]=invoice,["register"]="BW"};
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",input,bytes));
        Assert.AreEqual(1,preview["rows"]!.AsArray().Count);
        var review=preview["rows"]![0]!.AsObject();
        Assert.AreEqual(1,review["accounts"]!.AsArray().Count,"company line must stay off rider accounts");
        Assert.IsTrue(review["deductions"]!.AsArray().OfType<JsonObject>().Any(d=>d.S("level")=="company_level"&&d.N("amount")==2000));
        Assert.IsFalse(review["accounts"]![0]!["riders"]!.AsArray().OfType<JsonObject>().Any(r=>r.N("deduction")==2000));
        var share=review["accounts"]![0]!["riders"]![0]!.AsObject();
        Assert.AreNotEqual("",share.S("platformVehicleType"),"platform vehicle must be copied onto the share");
    }

    [TestMethod]
    public void High_NormalizePrivate_DoesNotBecomeCompanyCar()
    {
        Assert.AreEqual(VehicleIdentity.PrivateCar,VehicleIdentity.Normalize("private"));
        Assert.AreEqual(VehicleIdentity.Car,VehicleIdentity.Normalize("company_car"));
        Assert.AreEqual(VehicleIdentity.Bike,VehicleIdentity.Normalize("private bike"));
        Assert.AreEqual("sponsored_private",
            VehicleIdentity.PayCategory("sponsored",VehicleIdentity.PrivateCar,VehicleIdentity.Car));
        Assert.AreEqual(VehicleIdentity.MismatchCarOnBike,
            VehicleIdentity.MismatchCode(VehicleIdentity.PrivateCar,VehicleIdentity.Bike));
    }

    [TestMethod]
    public void Medium_PercentMonthlyBikeOnCar_RequiresVehicleCharge()
    {
        using var f=new PayFixture();
        var data=PayFixture.Scheme("percent_monthly_with_vehicle");
        var cells=data["parameters"]!["categories"]!.AsObject();
        cells["sponsored_bike_on_car_id"]=new JsonObject
        {
            ["enabled"]=true,["companyPercent"]=10,["chargeCompanyVehicle"]=false,["prorate"]=true
        };
        Assert.ThrowsExactly<ApiError>(()=>f.Draft("pay_scheme","keeta",data,"bike-charge-required"));
    }

    [TestMethod]
    public void Medium_ParentTermsComparison_MatchUnsetUntilExpectedExists()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1b-pt-inv",account="synthetic-core1b-pt-acc";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Keeta",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=50,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1b-pt-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=50,["incentives"]=0,["orders"]=2,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            return true;
        });
        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"keeta","2026-10"));
        var row=display["comparison"]!.AsArray().OfType<JsonObject>().Single();
        // match must stay unset/null until expected eligibility exists (never invoice_final alone).
        Assert.AreNotEqual("invoice_final",row.S("match"));
        Assert.AreEqual("",row.S("match"));
        Assert.AreEqual("",row.S("expectedTier"));
        Assert.AreEqual("",row.S("expectedStatus"));
    }

    [TestMethod]
    public void Medium_AllocationSetting_IgnoresParentTermsWeight()
    {
        using var f=new PayFixture();
        var terms=PayFixture.Terms();
        terms["parameters"]!["riderAllocationWeight"]="days";
        terms["parameters"]!["riderAllocationTieBreak"]="orders";
        f.Approve("pay_terms","ninja",f.Draft("pay_terms","ninja",terms));
        var (weight,tie)=f.Store.Read(c=>PayReviewCategory.AllocationSetting(c,null,"ninja","2026-10"));
        Assert.AreEqual("orders",weight);
        Assert.AreEqual("days",tie);
    }

    [TestMethod]
    public void Medium_SeededOperatingSystem_RefuseHardDelete()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-core1b-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store=new Store(dir);
            ConfigLists.Seed(store);
            foreach(var code in new[]{"kafala","external","kafala.company_bike","external.own_vehicle","bike","own_car"})
            {
                var err=Assert.ThrowsExactly<ApiError>(()=>
                    store.Write((c,tx)=>ConfigLists.DeleteOperatingSystem(c,tx,PayFixture.Maker,code)));
                Assert.AreEqual(409,err.Status,code);
            }
            // Extra unused leaf still deletable.
            store.Write((c,tx)=>{
                ConfigLists.Upsert(c,tx,PayFixture.Maker,"operating_systems",new JsonObject
                {
                    ["code"]="kafala.extra_core1b",["nameAr"]="إضافي",["active"]=true,
                    ["values"]=new JsonObject{["kind"]="leaf",["parent"]="kafala",["vehicleMode"]="company_bike",["operatingCostD"]=1m}
                });
                return true;
            });
            var deleted=store.Write((c,tx)=>ConfigLists.DeleteOperatingSystem(c,tx,PayFixture.Maker,"kafala.extra_core1b"));
            Assert.IsTrue(deleted.B("deleted"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if(Directory.Exists(dir))Directory.Delete(dir,true);
        }
    }
}
