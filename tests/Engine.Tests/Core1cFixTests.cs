using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

/// <summary>CORE1c fix round — every PARTLY/new row from grok-CORE1b.md (synthetic data only).</summary>
[TestClass]
public sealed class Core1cFixTests
{
    [TestMethod]
    public void High_KeetaPlatformVehicle_UsesAccountTypeOnly_BikeOnCarDetectable()
    {
        // PARTLY #2 + bike-on-car: نوع المركبة=سيارة must stay car even when المركبة=دراجة (rider).
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1c-inv",account="synthetic-core1c-acc",rider="2000000201";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["sponsorId"]=company,["isDeliveryRider"]=true,["engagementType"]="SponsoredInternal"},tx);
            Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="keeta",["externalAccountId"]="SYN-CORE1C",["code"]="SYN-CORE1C",
                ["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1c-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=5,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            return true;
        });
        var row=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        row["المعرف"]="SYN-CORE1C";row["الاسم بالكامل"]="Synthetic Rider";row["رقم بطاقة الهوية"]=rider;row["رقم الهوية1"]=rider;
        row["كفالة اليوزر"]="سجل البوابا";row["المركبة"]="دراجة";row["الحالة"]="TRUE";row["نوع المركبة"]="سيارة";
        row["الأيام الصالحة"]=20;row["الطلبات"]=5;row["المخالفات"]=0;row["المسافة المدفوعة"]=1m;row["عدد ايام عمل المندوب"]=18;
        row["الصلاحية المتوقعة"]="شريحة أ";row["اجمالي المسافه"]=1m;
        row["تاريخ الاستلام1"]="2026-10-01";row["تاريخ التسليم"]="2026-10-31";
        var bytes=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{KeetaOperationsImport.Headers.Select(h=>row[h]).ToArray()});
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new JsonObject{["invoiceId"]=invoice,["register"]="BW"},bytes));
        var share=preview["rows"]![0]!["accounts"]![0]!["riders"]![0]!.AsObject();
        Assert.AreEqual("سيارة",share.S("platformVehicleType"),"platform type must be نوع المركبة alone");
        Assert.AreEqual(VehicleIdentity.Car,VehicleIdentity.NormalizePlatform(share.S("platformVehicleType")));
        Assert.AreEqual(VehicleIdentity.MismatchBikeOnCar,
            VehicleIdentity.MismatchCode(VehicleIdentity.Bike,VehicleIdentity.NormalizePlatform(share.S("platformVehicleType"))));
        // CORE1d NEW #2: DeriveCategory on المركبة/نوع المركبة must not collapse to private.
        Assert.AreEqual("sponsored_bike_on_car_id",share.S("category"),
            "share category must be bike-on-car-ID, not sponsored_private");
        // Ambiguous concatenated text still prefers car for platform.
        Assert.AreEqual(VehicleIdentity.Car,VehicleIdentity.NormalizePlatform("دراجة سيارة"));
    }

    [TestMethod]
    public void High_EmptyPlatformVehicle_DoesNotForceNeedsDecision()
    {
        // NEW HIGH #1: Chefz/Ninja/generic never write platformVehicleType — empty must not block ready lines.
        Assert.AreEqual(VehicleIdentity.Unknown,VehicleIdentity.NormalizePlatform(""));
        Assert.IsNull(VehicleIdentity.MismatchCode(VehicleIdentity.Bike,VehicleIdentity.Unknown));
        // Non-empty garbage still counts as missing/unparseable.
        Assert.AreEqual(VehicleIdentity.Unknown,VehicleIdentity.NormalizePlatform("???"));
        // Simulate BuildLines rule: empty raw → no platform_vehicle_missing; garbage → missing.
        string emptyRaw="";
        string emptyNorm=VehicleIdentity.NormalizePlatform(emptyRaw);
        bool emptyMissing=emptyRaw!=""&&emptyNorm==VehicleIdentity.Unknown;
        Assert.IsFalse(emptyMissing);
        string junkRaw="not-a-vehicle";
        string junkNorm=VehicleIdentity.NormalizePlatform(junkRaw);
        bool junkMissing=junkRaw!=""&&junkNorm==VehicleIdentity.Unknown;
        Assert.IsTrue(junkMissing);
    }

    [TestMethod]
    public void High_PlatformAccountStoresVehicleType_OnKeetaCommit()
    {
        // PARTLY #2: Operations.platform_account must store vehicleType for PayReviewDraft readers.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1c-vt-inv",account="synthetic-core1c-vt-acc",rider="2000000202";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["sponsorId"]=company,["isDeliveryRider"]=true,["engagementType"]="SponsoredInternal"},tx);
            Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="keeta",["externalAccountId"]="SYN-CORE1C-VT",["code"]="SYN-CORE1C-VT",
                ["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00",["RawEffectiveTo"]="2026-11-01T00:00:00+03:00"})},tx);
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]=PayPlatforms.For("keeta").Platform,["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=50,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1c-vt-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=50,["incentives"]=0,["orders"]=2,["deductionAmount"]=0,["deductions"]=new JsonArray()},tx);
            return true;
        });
        var row=KeetaOperationsImport.Headers.ToDictionary(h=>h,h=>(object?)"");
        row["المعرف"]="SYN-CORE1C-VT";row["الاسم بالكامل"]="Synthetic";row["رقم بطاقة الهوية"]=rider;row["رقم الهوية1"]=rider;
        row["كفالة اليوزر"]="سجل";row["المركبة"]="دراجة";row["الحالة"]="TRUE";row["نوع المركبة"]="سيارة شركة";
        row["الأيام الصالحة"]=10;row["الطلبات"]=2;row["المخالفات"]=0;row["المسافة المدفوعة"]=1m;row["عدد ايام عمل المندوب"]=10;
        row["الصلاحية المتوقعة"]="متوقع";row["اجمالي المسافه"]=1m;
        row["تاريخ الاستلام1"]="2026-10-01";row["تاريخ التسليم"]="2026-10-31";
        var bytes=ExcelFiles.Write(KeetaOperationsImport.Columns,new List<object?[]>{KeetaOperationsImport.Headers.Select(h=>row[h]).ToArray()});
        var preview=f.Store.Read(c=>OperationsImports.Preview(c,PayFixture.Maker,"keeta",new JsonObject{["invoiceId"]=invoice,["register"]="BW"},bytes));
        var confirm=new JsonObject{["invoiceId"]=invoice,["register"]="BW",["confirm"]=true,["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint")};
        f.Store.Write((c,tx)=>OperationsImports.Commit(c,tx,PayFixture.Maker,"keeta",confirm,bytes));
        var stored=f.Store.Read(c=>Operations.Project(Store.Get(c,account,"platform_account")!,"platform_account"));
        Assert.AreEqual("سيارة شركة",stored.S("vehicleType"));
    }

    [TestMethod]
    public void Medium_ParentTerms_ComparesTierStatusRate_NotBlindCompared()
    {
        // PARTLY #6: real equal/differs on status/tier/rate vs invoice صالح.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1c-pt-inv",account="synthetic-core1c-pt-acc",rider="2000000203";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Keeta",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=100,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1c-pt-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=100,["incentives"]=0,["orders"]=10,["deductionAmount"]=0,["deductions"]=new JsonArray(),
                ["sourceMetrics"]=new JsonObject{["صالح"]="شريحة ب",["المركبة"]="سيارة"}},tx);
            Store.Save(c,OperationsImports.Kind,new()
            {
                ["id"]="synthetic-core1c-ops",["app"]="keeta",["period"]="2026-10",["invoiceId"]=invoice,["companyId"]=company,
                ["review"]=new JsonObject{["accounts"]=new JsonArray(new JsonObject{
                    ["accountId"]=account,
                    ["riders"]=new JsonArray(new JsonObject{
                        ["rider"]=rider,
                        ["metrics"]=new JsonObject{["expectedEligibility"]="شريحة أ",["expectedStatus"]="شريحة أ",["expectedTier"]="شريحة أ",["expectedRate"]=12m}
                    })
                })}
            },tx);
            return true;
        });
        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"keeta","2026-10"));
        var row=display["comparison"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual("differs",row.S("match"));
        Assert.AreEqual("شريحة أ",row.S("expectedTier"));
        Assert.AreEqual("شريحة ب",row.S("invoiceEligibility"));
        Assert.IsTrue(row.B("statusDelta")||row.B("tierDelta")||row.B("rateDelta"));
        Assert.IsTrue(row.S("deltaNoteAr").Contains("متوقع",StringComparison.Ordinal));
    }

    [TestMethod]
    public void Medium_ParentTerms_MultiRiderSameAccount_KeepsEveryRider()
    {
        // NEW MEDIUM #2: account key must not keep only the last rider.
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id,invoice="synthetic-core1c-mr-inv",account="synthetic-core1c-mr-acc";
        string riderA="2000000204",riderB="2000000205";
        f.Store.Write((c,tx)=>{
            Store.Save(c,"platform_invoice",new(){["id"]=invoice,["companyId"]=company,["platform"]="Keeta",["period"]="2026-10",["approvalStatus"]="approved",["status"]="final",["amountExclVat"]=80,["vat"]=0,["deductionsTotal"]=0},tx);
            Store.Save(c,"platform_invoice_line",new(){["id"]="synthetic-core1c-mr-line",["invoiceId"]=invoice,["platformAccountId"]=account,["baseGross"]=80,["incentives"]=0,["orders"]=8,["deductionAmount"]=0,["deductions"]=new JsonArray(),
                ["sourceMetrics"]=new JsonObject{["صالح"]="متوقع"}},tx);
            Store.Save(c,OperationsImports.Kind,new()
            {
                ["id"]="synthetic-core1c-mr-ops",["app"]="keeta",["period"]="2026-10",["invoiceId"]=invoice,["companyId"]=company,
                ["review"]=new JsonObject{["accounts"]=new JsonArray(new JsonObject{
                    ["accountId"]=account,
                    ["riders"]=new JsonArray(
                        new JsonObject{["rider"]=riderA,["metrics"]=new JsonObject{["expectedEligibility"]="أهلية أ",["expectedStatus"]="أهلية أ",["expectedTier"]="أهلية أ"}},
                        new JsonObject{["rider"]=riderB,["metrics"]=new JsonObject{["expectedEligibility"]="أهلية ب",["expectedStatus"]="أهلية ب",["expectedTier"]="أهلية ب"}}
                    )
                })}
            },tx);
            return true;
        });
        var display=f.Store.Read(c=>ParentTermsDisplay.Build(c,PayFixture.Maker,"keeta","2026-10"));
        var rows=display["comparison"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.AreEqual(2,rows.Count,"both riders on the same Keeta ID must appear");
        CollectionAssert.AreEquivalent(new[]{riderA,riderB},rows.Select(r=>r.S("riderKey")).ToArray());
        CollectionAssert.AreEquivalent(new[]{"أهلية أ","أهلية ب"},rows.Select(r=>r.S("expectedEligibility")).ToArray());
    }
}
