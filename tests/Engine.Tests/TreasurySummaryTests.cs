using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class TreasurySummaryTests
{
    [TestMethod]
    public void SummaryUsesPostingAccountsOnlyWithoutParentDoubleCount()
    {
        using var f=new PayFixture();
        string company=FinancialCore.Sponsors[0].Id;
        string cash=FinanceViews.Accounts.First(a=>a.B("posting")&&a.S("type")=="cash"&&a.S("status")=="active").S("code");
        string bank=FinanceViews.Accounts.First(a=>a.B("posting")&&a.S("type")=="bank"&&a.S("status")=="active").S("code");
        f.Store.Write((c,tx)=>
        {
            FinancialCore.PostJournal(c,tx,"treasury_test","Synthetic treasury balances","2026-10","TREASURY-SUM-1",
                new JsonArray(
                    PayPosting.Line(cash,company,100,0,"","", "2026-10","treasury-cash"),
                    PayPosting.Line(bank,company,40,0,"","", "2026-10","treasury-bank"),
                    PayPosting.Line("112501",company,25,0,"","", "2026-10","treasury-advance"),
                    PayPosting.Line("311101",company,0,165,"","", "2026-10","treasury-eq")),
                PayFixture.Maker.Identity!.Name!,originSponsorId:company,creditorSponsorId:company,targetChart:true,payChart:true);
            return true;
        });
        var summary=f.Store.Read(c=>TreasurySummary.Build(c,null,PayFixture.Maker,company));
        var row=summary["companies"]!.AsArray().OfType<JsonObject>().Single(r=>r.S("companyId")==company);
        Assert.AreEqual(100m,row.N("cash"));
        Assert.AreEqual(40m,row.N("bank"));
        Assert.AreEqual(140m,row.N("total"),"a rider advance (112501) is a receivable, not treasury");
        Assert.IsFalse(row.ContainsKey("other"));
        // Non-posting parents are excluded from the CoA filter by construction.
        Assert.IsFalse(FinanceViews.Accounts.First(a=>a.S("code")=="1111").B("posting"));
    }
}
