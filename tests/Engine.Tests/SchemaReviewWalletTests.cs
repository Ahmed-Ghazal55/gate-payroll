using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaReviewWalletTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void WalletFundingAndCloseAreDistinctAndCollectedOrUnknownEventsNeverMapToRiderDebt(bool sqlServer)
    {
        using var c=MoneySchemaFixture.Payments(sqlServer);
        Assert.IsNull(FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]="HsWalletTopUp"},null,null));
        Assert.IsNull(FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]="HsCollectedTopUp"},null,null));
        foreach(var (category,account) in new[]{("HsCustodyAdvance","112501"),("HsInvoiceCloseAdvance","112513")})
        {
            Assert.AreEqual(account,ConfigLists.DefaultDeductions.Single(d=>d.Category==category).Account);
            Assert.AreEqual(account,FinanceViews.MapAccount(new(){["accountCode"]="1210"},new(),new(){["category"]=category},null,null));
            Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_account WHERE code=@code AND is_postable=1",("@code",account)));
        }
        Assert.IsFalse(ConfigLists.DefaultDeductions.Any(d=>d.Category=="HsWalletTopUp"));
    }
}
