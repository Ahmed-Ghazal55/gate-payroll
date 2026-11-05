using System.Text.RegularExpressions;
namespace Payroll;

// BR24.8: the account table is shared by posting and read-only legacy projections.
public static class PayPlatforms
{
    public sealed record Accounts(string App,string Platform,string Receivable,string? Principal,string? Agent);
    public static readonly Accounts[] Table=[
        new("jahez","Jahez","112801","411106","411401"),
        new("keeta","Keeta","112804","411101","411501"),
        new("hs","HungerStation","112805","411102","411601"),
        new("ninja","Ninja","112806","411104",null),
        new("chefz","Chefz","112807","411103",null),
        new("amazon","Amazon","112808","411105",null)
    ];
    static string Normalize(string value){string code=value.Trim().ToLowerInvariant().Replace(" ","-").Replace("_","-");return code switch{"hungerstation" or "hunger-station"=>"hs","the-chefz" or "thechefz"=>"chefz",_=>code};}
    public static string App(string value)
    {
        string code=Normalize(value);
        J.Need(Regex.IsMatch(code,"^[a-z][a-z0-9-]{0,39}$"),"رمز التطبيق غير صالح.");return code;
    }
    public static Accounts For(string app)
    {string code=Normalize(app);return Table.FirstOrDefault(a=>a.App==code)??new(Regex.IsMatch(code,"^[a-z][a-z0-9-]{0,39}$")?code:"other","Other","112802",null,null);}
    public static string Active(string account)
    {J.Need(FinanceViews.Accounts.Any(a=>a.S("code")==account&&a.B("posting")&&a.S("status")=="active"),"الحساب غير معتمد للترحيل؛ لا نفعّل حسابًا موقوفًا تلقائيًا.");return account;}
    public static string Revenue(string app,string role)
    {J.Need(role is "principal" or "agent","حدد صفة الأصيل أو الوكيل.");return Active((role=="principal"?For(app).Principal:For(app).Agent)??throw new ApiError("حساب إيراد هذا النظام غير محدد للتطبيق؛ راجع ملف المنصة.",409));}
}
