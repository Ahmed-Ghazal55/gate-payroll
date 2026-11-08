using System.Globalization;
using System.Text.Json.Nodes;
namespace Payroll;

// ERP assignments carry date-only EffectiveFrom/EffectiveTo (inclusive, Saudi calendar days).
// Older reviewed documents carry exact RawEffectiveFrom/To instants. Both mean the same span.
public static class AssignmentSpan
{
    static readonly TimeSpan Riyadh=TimeSpan.FromHours(3);
    static bool DateOnlyValue(string value,out DateTimeOffset start)
    {
        start=default;if(value.Length<10||!DateOnly.TryParse(value[..10],CultureInfo.InvariantCulture,out var day))return false;
        start=new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue),Riyadh);return true;
    }
    public static bool From(JsonObject a,out DateTimeOffset from)
    {
        string raw=a.S("RawEffectiveFrom");
        if(raw!="")return DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out from);
        return DateOnlyValue(a.S("EffectiveFrom"),out from);
    }
    // Exclusive end; null means still open.
    public static bool To(JsonObject a,out DateTimeOffset? to)
    {
        to=null;string raw=a.S("RawEffectiveTo");
        if(raw!=""){if(!DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var end))return false;to=end;return true;}
        string date=a.S("EffectiveTo");if(date=="")return true;
        if(!DateOnlyValue(date,out var last))return false;to=last.AddDays(1);return true;
    }
    public static bool Overlaps(JsonObject a,DateTimeOffset start,DateTimeOffset endExclusive)=>From(a,out var from)&&from<endExclusive&&To(a,out var to)&&(to==null||to>start);
    public static bool Overlaps(JsonObject a,string period){var month=DateTimeOffset.Parse(period+"-01T00:00:00+03:00",CultureInfo.InvariantCulture);return Overlaps(a,month,month.AddMonths(1));}
    public static bool Covers(JsonObject a,string date)
    {
        if(!DateOnlyValue(date,out var day))return false;
        return Overlaps(a,day,day.AddDays(1));
    }
}
