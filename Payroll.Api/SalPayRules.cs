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
    /// <summary>Optional reshape of L1 for written exceptions (e.g. Keeta §22.9.2). Returns personal residual debt not recovered.</summary>
    decimal AdjustLevel1(JsonObject rider,List<JsonObject> l1Items,ref decimal level1Personal,ref decimal companyOperating,ref decimal wagesRoom);
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
        return new SharedAppRules(code);
    }

    public static IReadOnlyList<string> KnownApps=>ByApp.Keys.OrderBy(a=>a,StringComparer.Ordinal).ToList();
}

/// <summary>Shared written path: L1 operating/personal → NET → L2 TGA/HR/old debt (M-0669).</summary>
public sealed class SharedAppRules(string app):IAppPayRules
{
    public string AppCode{get;}=app;
    public void ValidateRider(JsonObject rider){}
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)
    {
        if(!string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))return false;
        if(item.ContainsKey("operating"))return item.B("operating");
        string code=item.S("itemCode");
        return code is "fuel" or "vehicle" or "housing";
    }
    public decimal AdjustLevel1(JsonObject rider,List<JsonObject> l1Items,ref decimal level1Personal,ref decimal companyOperating,ref decimal wagesRoom)=>0m;
}

/// <summary>Keeta: scheme from forms (M-0707); ineligible salary rider — §22.9.2 fuel/D recovery + 512121; personal residual open.</summary>
public sealed class KeetaPayRules:IAppPayRules
{
    readonly SharedAppRules _shared=new("keeta");
    public string AppCode=>"keeta";
    public void ValidateRider(JsonObject rider)=>_shared.ValidateRider(rider);
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)
    {
        // Protected salary path uses AdjustLevel1; do not dump all operating to company here.
        if(rider.B("keetaIneligible")&&string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))
            return false;
        return _shared.OperatingCostOnCompany(rider,item);
    }
    public decimal AdjustLevel1(JsonObject rider,List<JsonObject> l1Items,ref decimal level1Personal,ref decimal companyOperating,ref decimal wagesRoom)
    {
        if(!rider.B("keetaIneligible"))return 0m;
        if(!string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))return 0m;
        // Commission riders never use §22.9.2.
        // §22.9.2 journal order: operating (D/fuel) first, then personal — never caller order.
        decimal room=Math.Max(0,wagesRoom);
        decimal residualPersonal=0;
        var rebuilt=new List<JsonObject>();
        level1Personal=0;companyOperating=0;
        var ordered=l1Items
            .Select((item,idx)=>(item,idx,operating:IsOperating(item)))
            .OrderBy(x=>x.operating?0:1).ThenBy(x=>x.idx)
            .Select(x=>x.item);
        foreach(var item in ordered)
        {
            decimal amount=J.Money(item.N("amount"));
            string code=item.S("itemCode","l1");
            bool operating=IsOperating(item);
            if(operating)
            {
                decimal recover=J.Money(Math.Min(amount,room));
                decimal unrecovered=J.Money(amount-recover);
                room=J.Money(room-recover);
                if(recover>0)
                {
                    level1Personal+=recover;
                    rebuilt.Add(CloneItem(item,recover,true,item.S("accountCode","112511")));
                }
                if(unrecovered>0)
                {
                    companyOperating+=unrecovered;
                    rebuilt.Add(new JsonObject
                    {
                        ["stageCode"]="level1",["itemCode"]=code+"_unrecovered",["amount"]=unrecovered,
                        ["affectsNet"]=false,["accountCode"]="512121",["debtId"]=""
                    });
                }
            }
            else
            {
                decimal recover=J.Money(Math.Min(amount,room));
                decimal leftover=J.Money(amount-recover);
                room=J.Money(room-recover);
                if(recover>0)
                {
                    level1Personal+=recover;
                    rebuilt.Add(CloneItem(item,recover,true,item.S("accountCode","112505")));
                }
                if(leftover>0)residualPersonal+=leftover;
            }
        }
        l1Items.Clear();
        l1Items.AddRange(rebuilt);
        wagesRoom=room;
        return residualPersonal;
    }

    static bool IsOperating(JsonObject item)
    {
        string code=item.S("itemCode","l1");
        return code is "fuel" or "vehicle" or "housing" || item.B("operating");
    }

    static JsonObject CloneItem(JsonObject item,decimal amount,bool affects,string account)=>new()
    {
        ["stageCode"]="level1",["itemCode"]=item.S("itemCode","l1"),["amount"]=amount,
        ["affectsNet"]=affects,["accountCode"]=account,["debtId"]=item.S("debtId")
    };
}

/// <summary>HungerStation: custody top-up 112501; invoice close R on 112513; collected top-ups never payroll (file 68).
/// Wallet L1 must match stamped frozen hs_wallet evidence; close_refund offsets same-holder top-ups (file 68 §4.1).</summary>
public sealed class HungerStationPayRules:IAppPayRules
{
    readonly SharedAppRules _shared=new("hs");
    public string AppCode=>"hs";
    public void ValidateRider(JsonObject rider)
    {
        _shared.ValidateRider(rider);
        var evidence=(rider["frozenWallet"] as JsonArray??[]).OfType<JsonObject>().ToList();
        foreach(var item in (rider["level1"] as JsonArray??[]).OfType<JsonObject>())
        {
            string code=item.S("itemCode");
            if(!IsWallet(code))continue;
            string funding=item.S("fundingType").Trim().ToLowerInvariant();
            J.Need(funding is "custody" or "invoice_close" or "collected","محفظة هنقر: حدّد نوع التمويل (custody / invoice_close / collected).",400);
            if(funding=="collected")
                throw new ApiError("شحن محفظة محصّل من المندوب لا يُخصم من الراتب.",400);
            if(funding=="custody")
                J.Need(item.S("accountCode") is "" or "112501","شحن العهدة على 112501 فقط.",400);
            if(funding=="invoice_close")
                J.Need(item.S("accountCode") is "" or "112513","إقفال محفظة الفاتورة على 112513 فقط.",400);
            if(string.Equals(code,"hs_wallet_topup",StringComparison.OrdinalIgnoreCase)&&funding=="invoice_close")
                item["itemCode"]="hs_wallet_close";
            if(funding=="custody"&&item.S("accountCode")=="")item["accountCode"]="112501";
            if(funding=="invoice_close"&&item.S("accountCode")=="")item["accountCode"]="112513";
            decimal amount=J.Money(item.N("amount"));
            bool matched=evidence.Any(e=>
                string.Equals(e.S("fundingType"),funding,StringComparison.OrdinalIgnoreCase)
                &&J.Money(e.N("amount",e.N("balance")))==amount);
            J.Need(matched,"محفظة هنقر: البند غير مطابق لدليل التجميد المختوم.",409);
        }
    }
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)=>_shared.OperatingCostOnCompany(rider,item);
    public decimal AdjustLevel1(JsonObject rider,List<JsonObject> l1Items,ref decimal level1Personal,ref decimal companyOperating,ref decimal wagesRoom)
    {
        // Same-holder close_refund offsets custody top-up (file 68 synthetic: 300 − 250 = 50).
        decimal topup=0,refund=0;
        var kept=new List<JsonObject>();
        foreach(var item in l1Items)
        {
            string code=item.S("itemCode");
            if(!IsWallet(code)){kept.Add(item);continue;}
            string funding=item.S("fundingType").Trim().ToLowerInvariant();
            string eventType=item.S("eventType").Trim().ToLowerInvariant();
            decimal amount=J.Money(item.N("amount"));
            if(funding=="custody")topup=J.Money(topup+amount);
            else if(funding=="invoice_close"&&(eventType is "close_refund" or "refund"||item.B("offsetsTopup")))
                refund=J.Money(refund+amount);
            else kept.Add(item);
        }
        decimal netWallet=J.Money(Math.Max(0,topup-refund));
        level1Personal=0;
        foreach(var item in kept)
            if(!item.ContainsKey("affectsNet")||item.B("affectsNet"))level1Personal=J.Money(level1Personal+item.N("amount"));
        l1Items.Clear();
        l1Items.AddRange(kept);
        if(netWallet>0)
        {
            level1Personal=J.Money(level1Personal+netWallet);
            l1Items.Add(new JsonObject
            {
                ["stageCode"]="level1",["itemCode"]="hs_wallet_topup",["amount"]=netWallet,
                ["affectsNet"]=true,["accountCode"]="112501",["fundingType"]="custody",["debtId"]=""
            });
        }
        return 0m;
    }
    static bool IsWallet(string code)=>code is "hs_wallet_topup" or "hs_wallet_close" or "hs_wallet";
}

/// <summary>Chefz: consume Omar RiderAmount only from frozen settlement provenance (M-0704 / RWC4b H9).</summary>
public sealed class ChefzPayRules:IAppPayRules
{
    public string AppCode=>"chefz";
    public void ValidateRider(JsonObject rider)
    {
        J.Need(rider.ContainsKey("riderAmount")&&rider["riderAmount"] is not null,"شيفز: RiderAmount من تسوية عمر مطلوب.",409);
        decimal riderAmount=J.Money(rider.N("riderAmount"));
        decimal gross=J.Money(rider.N("gross"));
        J.Need(gross==riderAmount,"شيفز: الاستحقاق من RiderAmount لعمر فقط؛ إعادة احتساب الإجمالي ممنوعة.",409);
        J.Need(rider.B("frozenSettlement"),"شيفز: مطلوب دليل تسوية عمر المجمّد.",409);
        decimal frozen=J.Money(rider.N("frozenRiderAmount"));
        J.Need(frozen==riderAmount,"شيفز: RiderAmount لا يطابق التسوية المجمّدة.",409);
    }
    public bool OperatingCostOnCompany(JsonObject rider,JsonObject item)
    {
        if(!string.Equals(rider.S("schemeType"),"salary",StringComparison.OrdinalIgnoreCase))return false;
        if(item.ContainsKey("operating"))return item.B("operating");
        return item.S("itemCode") is "fuel" or "vehicle" or "housing";
    }
    public decimal AdjustLevel1(JsonObject rider,List<JsonObject> l1Items,ref decimal level1Personal,ref decimal companyOperating,ref decimal wagesRoom)=>0m;
}
