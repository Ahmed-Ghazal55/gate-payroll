using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-21: one source for the short rider statement in ar/en/ur.
/// Date range; snapshot at final approval when present — never live-reads Omar per request.</summary>
public static partial class RiderStatements
{
    static readonly string[] RiderLangs=["ar","en","ur"];

    public static JsonObject BuildRiderFacing(DbConnection c,string iqama,string from,string to,string? sponsorId,string lang,ClaimsPrincipal user,DbTransaction? tx=null)
    {
        lang=string.IsNullOrWhiteSpace(lang)?"ar":lang.Trim().ToLowerInvariant();
        J.Need(RiderLangs.Contains(lang),"اللغة المدعومة: العربية أو الإنجليزية أو الأوردية.");
        // Prefer an approved payroll snapshot for the period; never call Omar ERP here (X10).
        var snapshot=FindApprovalSnapshot(c,iqama,from,to,sponsorId,tx);
        var core=snapshot??BuildSimple(c,iqama,from,to,sponsorId,user,tx);
        decimal closing=core.N("closingNet");
        string closingKey=closing>0?"you":closing<0?"company":"even";
        var labels=Labels(lang);
        var details=new JsonArray();
        foreach(var row in (core["details"] as JsonArray??[]).OfType<JsonObject>())
        {
            string bucket=row.S("bucket");
            details.Add(new JsonObject
            {
                ["date"]=row.S("date"),["period"]=row.S("period"),["bucket"]=bucket,
                ["label"]=TranslateBucket(lang,bucket,row.S("labelAr")),
                ["amount"]=row.N("amount"),["netAmount"]=row.N("netAmount"),
                ["youOwe"]=row.N("netAmount")<0?J.Money(-row.N("netAmount")):0m,
                ["owedToYou"]=row.N("netAmount")>0?row.N("netAmount"):0m
            });
        }
        return new()
        {
            ["iqama"]=iqama,["from"]=from,["to"]=to,["lang"]=lang,
            ["currency"]="SAR",
            ["source"]=snapshot!=null?"final_approval_snapshot":"posted_journal_lines",
            ["disclaimer"]=labels.Disclaimer,
            ["closingNet"]=closing,
            ["closingLabel"]=closingKey switch{"you"=>labels.You,"company"=>labels.Company,_=>labels.Even},
            ["riderName"]=core["rider"].S("name"),
            ["openingBalance"]=core.N("openingBalance"),
            ["netMovement"]=core.N("netMovement"),
            ["details"]=details,
            ["periodTotals"]=core["periodTotals"]?.DeepClone(),
            ["liveOmarRead"]=false,
            ["allowedActions"]=new JsonArray()
        };
    }

    static JsonObject? FindApprovalSnapshot(DbConnection c,string iqama,string from,string to,string? sponsorId,DbTransaction? tx=null)
    {
        // Snapshot documents are stored at final salary approval; never synthesize from Omar.
        var snap=Store.List(c,"rider_statement_snapshot",tx)
            .Where(s=>s.S("iqama")==iqama&&string.CompareOrdinal(s.S("from"),from)<=0&&string.CompareOrdinal(s.S("to"),to)>=0)
            .Where(s=>string.IsNullOrWhiteSpace(sponsorId)||s.S("sponsorId")==sponsorId||s.S("sponsorId")=="")
            .OrderByDescending(s=>s.S("approvedAt"),StringComparer.Ordinal)
            .FirstOrDefault();
        if(snap?["statement"] is JsonObject body)return body;
        return null;
    }

    /// <summary>Call from final salary approval to freeze the short statement (one source for later rider reads).</summary>
    public static void SaveApprovalSnapshot(DbConnection c,DbTransaction tx,string iqama,string from,string to,string? sponsorId,ClaimsPrincipal approver,JsonObject statement)
    {
        var doc=new JsonObject
        {
            ["id"]="stmt-snap:"+iqama+":"+from+":"+to+(sponsorId==null?"":":"+sponsorId),
            ["iqama"]=iqama,["from"]=from,["to"]=to,["sponsorId"]=sponsorId??"",
            ["approvedAt"]=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture),
            ["approvedBy"]=approver.Identity?.Name??"",
            ["statement"]=statement.DeepClone()
        };
        Store.Save(c,"rider_statement_snapshot",doc,tx);
    }

    static (string Disclaimer,string You,string Company,string Even) Labels(string lang)=>lang switch
    {
        "en"=>(
            "For information only — not a legal document.",
            "Balance owed to you",
            "Balance you owe the company",
            "No remaining balance"),
        "ur"=>(
            "صرف معلومات کے لیے — قانونی دستاویز نہیں۔",
            "آپ کے لیے واجب الادا",
            "کمپنی کے لیے آپ پر واجب",
            "کوئی باقی ماندہ رقم نہیں"),
        _=>(
            "للاطلاع فقط — ليست مستندًا قانونيًا",
            "لك",
            "عليك",
            "متوازن")
    };

    static string TranslateBucket(string lang,string bucket,string arabicFallback)=>lang switch
    {
        "en"=>bucket switch
        {
            "earned"=>"Approved earnings","deducted"=>"Deduction from your dues",
            "received"=>"Amount you received","transferred"=>"Transfer between companies",
            "debt_added"=>"Added to your debt","debt_collected"=>"Debt reduced by payment or waiver",
            "jahez_cash"=>"Jahez cash movement — separate from salary",_=>arabicFallback
        },
        "ur"=>bucket switch
        {
            "earned"=>"منظور شدہ آمدنی","deducted"=>"آپ کی مستحقات سے کٹوتی",
            "received"=>"آپ کو موصول رقم","transferred"=>"کمپنیوں کے درمیان منتقلی",
            "debt_added"=>"آپ کے قرض میں اضافہ","debt_collected"=>"ادائیگی یا معافی سے قرض میں کمی",
            "jahez_cash"=>"جاہز نقدی — تنخواہ سے الگ",_=>arabicFallback
        },
        _=>arabicFallback
    };
}
