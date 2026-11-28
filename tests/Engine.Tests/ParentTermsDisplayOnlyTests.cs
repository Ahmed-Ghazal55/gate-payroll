using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;
using Payroll;

[TestClass]
public sealed class ParentTermsDisplayOnlyTests
{
    [TestMethod]
    public void NinjaReviewDraft_DoesNotBlockOnMissingParentTerms()
    {
        using var f = new PayFixture();
        f.Approve("pay_setting", "global", f.Global());
        // Eligibility still required; parent terms intentionally omitted.
        f.Approve("pay_eligibility", "ninja", f.Draft("pay_eligibility", "ninja", PayFixture.Eligibility()));
        string company = FinancialCore.Sponsors[0].Id;
        var draft = f.Store.Read(c => PayReviewDraft.Build(c, null, PayFixture.Maker, "ninja", "2026-10", company));
        Assert.IsFalse(draft["blockers"]!.AsArray().OfType<JsonObject>().Any(b => b.S("code") == "pay_terms_missing"),
            draft["blockers"]!.ToJsonString());
    }

    [TestMethod]
    public void TryResolve_ReturnsNullWithoutThrowing_WhenParentTermsMissing()
    {
        using var f = new PayFixture();
        Assert.IsNull(f.Store.Read(c => PaySettings.TryResolve(c, null, "pay_terms", "keeta", "2026-10")));
    }

    [TestMethod]
    public void Scheme_AllowsOptionalBikeOnCarCells()
    {
        using var f = new PayFixture();
        var data = PayFixture.Scheme("daily_commission");
        var cells = data["parameters"]!["categories"]!.AsObject();
        cells["sponsored_bike_on_car_id"] = new JsonObject { ["enabled"] = true, ["dailyCommission"] = 7, ["chargeCompanyVehicle"] = true };
        cells["external_bike_on_car_id"] = new JsonObject { ["enabled"] = false };
        var draft = f.Draft("pay_scheme", "keeta", data, "bike-on-car");
        Assert.AreEqual("draft", draft.S("status"));
        Assert.IsTrue(draft["data"]!["parameters"]!["categories"]!.AsObject().ContainsKey("sponsored_bike_on_car_id"));
    }
}
