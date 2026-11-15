using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

// ERP assignments carry date-only EffectiveFrom/EffectiveTo (inclusive, Saudi calendar days).
// Older reviewed documents carry exact RawEffectiveFrom/To instants. Both mean the same span.
// BR §24.3: after 14:00 Riyadh, pickup starts the next day; return after 14:00 keeps today for the previous holder.
public static class AssignmentSpan
{
    static readonly TimeSpan RiyadhOffset=TimeSpan.FromHours(3);
    static bool DateOnlyValue(string value,out DateTimeOffset start)
    {
        start=default;if(value.Length<10||!DateOnly.TryParse(value[..10],CultureInfo.InvariantCulture,out var day))return false;
        start=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),RiyadhOffset);return true;
    }
    public static bool From(JsonObject a,out DateTimeOffset from)
    {
        string raw=a.S("RawEffectiveFrom",a.S("startedAt",a.S("StartedAtUtc")));
        if(raw!=""&&raw.Contains('T',StringComparison.Ordinal))return DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out from);
        return DateOnlyValue(a.S("EffectiveFrom",a.S("effectiveFrom",a.S("startedAt"))),out from);
    }
    // Exclusive end; null means still open.
    public static bool To(JsonObject a,out DateTimeOffset? to)
    {
        to=null;string raw=a.S("RawEffectiveTo",a.S("endedAt",a.S("EndedAtUtc")));
        if(raw!=""&&raw.Contains('T',StringComparison.Ordinal))
        {
            if(!DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var end))return false;
            to=end;return true;
        }
        string date=a.S("EffectiveTo",a.S("effectiveTo",a.S("endedAt")));if(date=="")return true;
        if(!DateOnlyValue(date,out var last))return false;to=last.AddDays(1);return true;
    }
    public static bool Overlaps(JsonObject a,DateTimeOffset start,DateTimeOffset endExclusive)=>From(a,out var from)&&from<endExclusive&&To(a,out var to)&&(to==null||to>start);
    public static bool Overlaps(JsonObject a,string period){var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00",CultureInfo.InvariantCulture);return Overlaps(a,month,month.AddMonths(1));}
    public static bool Covers(JsonObject a,string date)=>Covers(a,date,null,null);
    /// <summary>Prefer ERP day contributions when present; else §24.3 cutoff on timed spans so a same-day hand-over after 14:00 counts for the previous holder only.</summary>
    public static bool Covers(JsonObject a,string date,JsonObject? values,IEnumerable<JsonObject>? dayContributions)
    {
        if(date.Length<10||!DateOnly.TryParse(date[..10],CultureInfo.InvariantCulture,out var day))return false;
        string accountId=a.S("platformAccountId",a.S("PlatformRiderAccountId"));
        string rider=a.S("riderId",a.S("RiderProfileId"));
        if(dayContributions!=null)
        {
            var forAccount=dayContributions.Where(c=>
            {
                string id=c.S("PlatformRiderAccountId");if(id=="")id=c.S("platformRiderAccountId");if(id=="")id=c.S("platformAccountId");
                return accountId==""||id==accountId;
            }).ToList();
            var forDay=forAccount.Where(c=>c.S("BusinessDate",c.S("businessDate")).StartsWith(date[..10],StringComparison.Ordinal)).ToList();
            if(forDay.Count>0)
                return forDay.Any(c=>{string r=c.S("RiderProfileId");if(r=="")r=c.S("riderProfileId");if(r=="")r=c.S("riderId");return rider==""||r==rider;});
        }
        if(!From(a,out var from)||!To(a,out var to))return false;
        bool timed=a.S("RawEffectiveFrom",a.S("startedAt",a.S("StartedAtUtc"))).Contains('T',StringComparison.Ordinal)
            ||a.S("RawEffectiveTo",a.S("endedAt",a.S("EndedAtUtc"))).Contains('T',StringComparison.Ordinal);
        if(timed)
        {
            var v=values??PaySettings.Defaults()["values"]!.AsObject();
            int first=BoundaryDay(from,v);
            int last=to is null?int.MaxValue:BoundaryDay(to.Value,v);
            return day.DayNumber>=first&&day.DayNumber<last;
        }
        var dayStart=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),RiyadhOffset);
        return Overlaps(a,dayStart,dayStart.AddDays(1));
    }
    static int BoundaryDay(DateTimeOffset time,JsonObject values)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(values.Required("timeZone"));
        var cutoff=TimeOnly.ParseExact(values.Required("billingCutoff"),"HH:mm",CultureInfo.InvariantCulture);
        var local=TimeZoneInfo.ConvertTime(time,zone);
        return DateOnly.FromDateTime(local.DateTime).DayNumber+(TimeOnly.FromDateTime(local.DateTime)>cutoff?1:0);
    }
}
