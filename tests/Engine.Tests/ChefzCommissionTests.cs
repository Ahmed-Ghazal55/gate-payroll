using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class ChefzCommissionTests
{
    static DateOnly D(int y,int m,int d)=>new(y,m,d);
    static IEnumerable<DateOnly> Range(DateOnly from,DateOnly toInclusive)
    {for(var d=from;d<=toInclusive;d=d.AddDays(1))yield return d;}
    [TestMethod]
    public void FullMonthCapsEachHalfAtFifteenEvenIn31DayMonth()
    {
        // BR §36: second half of a 31-day month has 16 calendar days; billable max stays 15.
        var days=Range(D(2026,1,1),D(2026,1,31)).ToList();
        Assert.AreEqual(31,days.Count);
        var halves=ChefzCommission.Halves(2026,1);
        Assert.AreEqual(15,halves[0].CalendarDays);Assert.AreEqual(16,halves[1].CalendarDays);
        Assert.AreEqual(375m,ChefzCommission.FullTimeFee(days,halves[0]));
        Assert.AreEqual(375m,ChefzCommission.FullTimeFee(days,halves[1]));
        Assert.AreEqual(750m,ChefzCommission.FullTimeFeeForMonth(days,2026,1));
    }
    [TestMethod]
    [DataRow(2026,2,28)] // non-leap
    [DataRow(2024,2,29)] // leap
    [DataRow(2026,4,30)]
    [DataRow(2026,10,31)]
    public void Months28_29_30_31(int year,int month,int lastDay)
    {
        var days=Range(D(year,month,1),D(year,month,lastDay)).ToList();
        Assert.AreEqual(lastDay,days.Count);
        var halves=ChefzCommission.Halves(year,month);
        Assert.AreEqual(15,halves[0].CalendarDays);
        Assert.AreEqual(lastDay-15,halves[1].CalendarDays);
        decimal expected=ChefzCommission.FullTimeFee(days,halves[0])+ChefzCommission.FullTimeFee(days,halves[1]);
        Assert.AreEqual(expected,ChefzCommission.FullTimeFeeForMonth(days,year,month));
        Assert.IsTrue(ChefzCommission.FullTimeFee(days,halves[1])<=375m);
    }
    [TestMethod]
    public void RiderActiveOnlyOnDay31PaysOneDay()
    {
        Assert.AreEqual(25m,ChefzCommission.FullTimeFeeForMonth([D(2026,10,31)],2026,10));
    }
    [TestMethod]
    public void PartialHalfIsDaysTimes25CappedAt15()
    {
        var half=ChefzCommission.Halves(2026,10)[0];
        Assert.AreEqual(75m,ChefzCommission.FullTimeFee([D(2026,10,1),D(2026,10,2),D(2026,10,3)],half));
        Assert.AreEqual(375m,ChefzCommission.FullTimeFee(Range(D(2026,10,1),D(2026,10,20)),half));
    }
}
