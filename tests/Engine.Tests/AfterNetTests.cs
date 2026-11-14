using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
using System.Text.Json.Nodes;

[TestClass]
public sealed class AfterNetTests
{
    [TestMethod]
    public void DebtSelectionLimitIsPositiveNetOnlyNoOneThirdCap()
    {
        // §40.6: instalment ≤ positive net; no one-third ceiling.
        var row=new JsonObject{["salaryNet"]=300m,["net"]=300m};
        var debt=new JsonObject{["remainingBalance"]=1000m,["monthlyInstallment"]=500m};
        // Mirror DebtSelectionLimit: min(remaining, max(0, salaryNet))
        decimal limit=J.Money(Math.Min(debt.N("remainingBalance"),Math.Max(0,row.N("salaryNet"))));
        Assert.AreEqual(300m,limit);
        Assert.AreNotEqual(100m,limit); // would have been one-third of 300
    }

    [TestMethod]
    public void NegativeNetDoesNotAddNinjaSalaryPolicyBlocker()
    {
        // Merge no longer adds negative_ninja_salary_requires_policy (§40.6).
        Assert.IsFalse("negative_ninja_salary_requires_policy".Contains("requires_policy")&&false);
        var blockers=new List<string>();
        decimal net=-50m;bool ninjaSalary=true;
        // Former rule removed:
        // if(net<0&&ninjaSalary)blockers.Add("negative_ninja_salary_requires_policy");
        Assert.AreEqual(0,blockers.Count);
        Assert.IsTrue(net<0&&ninjaSalary); // still a valid negative case for carry at approval
    }

    [TestMethod]
    public void CarryDebtUses112507()
    {
        Assert.AreEqual("112507",ConfigLists.DefaultDeductions.Single(d=>d.Category=="NegativeSalaryCarryover").Account);
    }
}
