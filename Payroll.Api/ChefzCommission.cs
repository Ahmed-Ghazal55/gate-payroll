using System.Globalization;
namespace Payroll;

/// <summary>BR §36 Chefz full-time half-cycle commission. Percentage schemes unchanged elsewhere.</summary>
public static class ChefzCommission
{
    public const decimal DailyFee=25m;
    public const int MaxDaysPerHalf=15;
    public sealed record Half(DateOnly From,DateOnly ToExclusive,int CalendarDays);
    public static IReadOnlyList<Half> Halves(int year,int month)
    {
        var first=new DateOnly(year,month,1);var next=first.AddMonths(1);
        var mid=new DateOnly(year,month,16);
        return [new(first,mid,15),new(mid,next,next.DayNumber-mid.DayNumber)];
    }
    /// <summary>Full-time fee for active days that fall inside one half. Cap 15 days × 25.</summary>
    public static decimal FullTimeFee(IEnumerable<DateOnly> activeDays,Half half)
    {
        int days=activeDays.Count(d=>d>=half.From&&d<half.ToExclusive);
        if(days<=0)return 0m;
        return J.Money(Math.Min(days,MaxDaysPerHalf)*DailyFee);
    }
    public static decimal FullTimeFeeForMonth(IEnumerable<DateOnly> activeDays,int year,int month)
        =>Halves(year,month).Sum(h=>FullTimeFee(activeDays,h));
    public static string Describe(int year,int month)=>string.Join("; ",Halves(year,month).Select(h=>$"{h.From.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)}..{h.ToExclusive.AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)} ({Math.Min(h.CalendarDays,MaxDaysPerHalf)} billable max)"));
}
