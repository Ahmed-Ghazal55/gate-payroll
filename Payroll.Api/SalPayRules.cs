using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>Per-app pay rules selected by approved app settings (M-0711: every app is equal).
/// Only rules written in BUSINESS_RULES / Ghazal answers / design docs — never invent.</summary>
public interface IAppPayRules
{
    string AppCode{get;}
    /// <summary>Validate and normalize rider input before L1/L2. Throws ApiError when a written rule is violated.</summary>
    void ValidateRider(JsonObject rider);
    /// <summary>True when an operating L1 item must stay as company cost (salary scheme §40.3).</summary>
    bool OperatingCostOnCompany(JsonObject rider,JsonObject item);
}

public static class AppPayRules
{
    static readonly Dictionary<string,IAppPayRules> ByApp=new(StringComparer.Ordinal)
    {
        ["ninja"]=new SharedAppRules("ninja"),
        ["keeta"]=new KeetaPayRules(),
        ["hs"]=new HungerStationPayRules(),
        ["chefz"]=new ChefzPayRules(),
        ["jahez"]=new SharedAppRules("jahez"),
        ["amazon"]=new SharedAppRules("amazon"),
    };

    public static IAppPayRules For(string app)
    {
        string code=PayPlatforms.App(app);
        if(ByApp.TryGetValue(code,out var rules))return rules;
        // Unknown configured app: shared L1→NET→L2 only; missing app-specific rules reported by caller.
        return new SharedAppRules(code);
    }

    public static IReadOnlyList<string> KnownApps=>ByApp.Keys.OrderBy(a=>a,StringComparer.Ordinal).ToList();
}

/// <summary>Shared written path: L1 operating/personal → NET → L2 TGA/HR/old debt (M-0669).</summary>
public sealed class SharedAppRules(string app):IAppPayRules
{
    public string AppCode{get;}=app;
    public void ValidateRider(JsonObject rider)
    {
        J.Need(rider.N("gross")>=0||rider.B("keetaIneligible")||true,"الاستحقاق يُمرَّر كما هو (قد يكون سالباً بعد الخصم فقط).");
    }
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)
    {
        if(!string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))return false;
        if(item.ContainsKey("operating"))return item.B("operating");
        string code=item.S("itemCode");
        return code is "fuel" or "vehicle" or "housing";
    }
}

/// <summary>Keeta: scheme from forms (M-0707); ineligible salary rider — no 112507 carryover (§22.9.2).</summary>
public sealed class KeetaPayRules:IAppPayRules
{
    readonly SharedAppRules _shared=new("keeta");
    public string AppCode=>"keeta";
    public void ValidateRider(JsonObject rider)=>_shared.ValidateRider(rider);
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)=>_shared.OperatingCostOnCompany(rider,item);
}

/// <summary>HungerStation: wallet top-ups L1 on 112513 (file 68 / P14); TGA rider share is L2.</summary>
public sealed class HungerStationPayRules:IAppPayRules
{
    readonly SharedAppRules _shared=new("hs");
    public string AppCode=>"hs";
    public void ValidateRider(JsonObject rider)
    {
        _shared.ValidateRider(rider);
        foreach(var item in (rider["level1"] as JsonArray??[]).OfType<JsonObject>())
            if(string.Equals(item.S("itemCode"),"hs_wallet_topup",StringComparison.OrdinalIgnoreCase))
                J.Need(item.S("accountCode") is "" or "112513","محفظة هنقر على 112513 فقط.",400);
    }
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)=>_shared.OperatingCostOnCompany(rider,item);
}

/// <summary>Chefz: consume Omar RiderAmount only — never recompute gross/commission (M-0704). Full settlement freeze waits U-W2-18.</summary>
public sealed class ChefzPayRules:IAppPayRules
{
    public string AppCode=>"chefz";
    public void ValidateRider(JsonObject rider)
    {
        if(!rider.ContainsKey("riderAmount")||rider["riderAmount"] is null)return;
        decimal riderAmount=J.Money(rider.N("riderAmount"));
        decimal gross=J.Money(rider.N("gross"));
        // M-0704: never rebuild Chefz gross/commission; RiderAmount is the only entitlement source.
        J.Need(gross==riderAmount,"شيفز: الاستحقاق من RiderAmount لعمر فقط؛ إعادة احتساب الإجمالي ممنوعة.",409);
    }
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)
    {
        if(!string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))return false;
        if(item.ContainsKey("operating"))return item.B("operating");
        return item.S("itemCode") is "fuel" or "vehicle" or "housing";
    }
}
