using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Payroll;

/// <summary>
/// Display-only «الشروط الأصلية»: Omar ERP expected eligibility vs final approved invoice.
/// Never feeds calculation (Ghazal 2026-11-27 / T10).
/// </summary>
public static class ParentTermsDisplay
{
    public static void Map(RouteGroupBuilder api,Store store,IConfiguration config,IHostEnvironment env)
    {
        api.MapGet("/pay/apps/{app}/parent-terms/display",(string app,HttpContext ctx)=>
            store.Read(c=>Build(c,ctx.User,PayPlatforms.App(app),J.Period(ctx.Request.Query["period"].ToString()),config,env)))
            .WithTags("Payroll").WithSummary("Display-only parent terms: ERP expected eligibility vs approved invoice (no calculation effect)");
    }

    public static JsonObject Build(DbConnection c,ClaimsPrincipal actor,string app,string period,IConfiguration? config=null,IHostEnvironment? env=null)
    {
        AccessPolicy.Demand(actor,"pay.view");
        if(!FinancialScopes.Platform(actor,PayPlatforms.For(app).Platform))throw new ApiError("التطبيق خارج نطاق المستخدم.",403);
        J.Need(app is "keeta" or "ninja","عرض الشروط الأصلية متاح لكيتا ونينجا حالياً.");

        var localTerms=Store.List(c,"pay_terms").Where(d=>d.S("app")==app&&d.S("period")==period)
            .OrderByDescending(d=>d.N("version")).Select(d=>PaySettings.WithActions(actor,d,c)).ToList();

        var invoices=Store.List(c,"platform_invoice")
            .Where(i=>InvoiceDiscard.Active(i)&&i.S("period")==period&&i.S("status")!="reversed"&&i.S("accountingStatus")!="reversed"
                &&PayPlatforms.For(i.S("platform",i.S("platformCode"))).App==app
                &&i.S("approvalStatus")=="approved")
            .OrderBy(i=>i.S("id"),StringComparer.Ordinal).ToList();

        // Expected eligibility: one entry per account+rider (Keeta multi-rider IDs keep every rider).
        var expectedList=LoadExpectedRows(c,app,period);
        var expectedByAccount=expectedList.GroupBy(e=>e.S("accountId"),StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.Ordinal);
        var erpRows=new JsonArray();
        foreach(var exp in expectedList)
            erpRows.Add(exp.DeepClone());

        JsonArray? liveErp=null;string erpNote="تُقرأ الصلاحية المتوقعة من نظام عمر للقراءة فقط.";
        string source=Operations.SourceStore;
        if(app=="keeta"&&config!=null&&env!=null&&Operations.SourceMode(config,env)==Operations.SourceLive)
        {
            source=Operations.SourceLive;
            erpNote="صلاحية كيتا المتوقعة من عرض عمر (قراءة فقط — ليست اعتماداً).";
        }
        else if(app=="ninja")
        {
            List<JsonObject> snaps;
            if(config!=null&&env!=null&&Operations.SourceMode(config,env)==Operations.SourceLive)
            {
                source=Operations.SourceLive;
                snaps=LoadNinjaSnapshotsLive(c,actor,config,period);
                erpNote=snaps.Count==0
                    ?"لا توجد لقطات أهلية نينجا حية في نظام عمر لهذا الشهر."
                    :"لقطات أهلية نينجا من نظام عمر (قراءة فقط — متوقعة فقط، ليست اعتماداً).";
            }
            else
            {
                snaps=Store.List(c,"ninja_evaluation_snapshot")
                    .Where(s=>SnapshotMatchesPeriod(s,period))
                    .OrderByDescending(s=>s.S("createdAt"),StringComparer.Ordinal).Take(50).ToList();
                erpNote=snaps.Count==0
                    ?"لا توجد لقطات أهلية نينجا محلية؛ فعّل القراءة الحية لعرضها من نظام عمر."
                    :"لقطات أهلية نينجا المحلية (متوقعة فقط) مربوطة بخطوط الفاتورة عند توفر معرف الحساب.";
            }
            liveErp=new JsonArray(snaps.Select(s=>(JsonNode)new JsonObject{
                ["id"]=s.S("id"),["frozen"]=s.B("frozen"),["reason"]=s.S("reason"),
                ["policyVersion"]=s.S("policyVersion"),["createdAt"]=s.S("createdAt"),
                ["approval"]="expected_only",["source"]=source
            }).ToArray());
            MergeNinjaSnapshotAccounts(c,snaps,expectedList,expectedByAccount);
        }

        var comparison=new JsonArray();
        foreach(var inv in invoices)
        {
            foreach(var line in InvoiceLines(c,inv))
            {
                string accountId=line.S("platformAccountId",line.S("accountId"));
                string riderKey=line.S("riderId",line.S("iqama",line.S("reportIdNumber")));
                var invoiceSide=InvoiceSide(line,inv);
                var expecteds=LookupExpected(expectedByAccount,expectedList,accountId,riderKey);
                if(expecteds.Count==0)
                {
                    comparison.Add(ComparisonRow(inv,line,accountId,riderKey==""?null:riderKey,null,invoiceSide));
                    continue;
                }
                foreach(var expected in expecteds)
                    comparison.Add(ComparisonRow(inv,line,accountId,expected.S("riderKey")==""?riderKey:expected.S("riderKey"),expected,invoiceSide));
            }
        }

        return new()
        {
            ["app"]=app,
            ["period"]=period,
            ["displayOnly"]=true,
            ["affectsCalculation"]=false,
            ["acceptanceIsNorm"]=true,
            ["noteAr"]="الشروط الأصلية للعرض والمقارنة فقط. لا تؤثر على الاحتساب. الفاتورة المعتمدة هي المرجع؛ القبول هو الأصل.",
            ["source"]=source,
            ["erpNoteAr"]=erpNote,
            ["erpExpected"]=liveErp??erpRows,
            ["localTermsArchive"]=new JsonArray(localTerms.Select(t=>(JsonNode)t.DeepClone()).ToArray()),
            ["approvedInvoices"]=new JsonArray(invoices.Select(i=>(JsonNode)new JsonObject{
                ["id"]=i.S("id"),["companyId"]=i.S("companyId"),["period"]=i.S("period"),
                ["approvalStatus"]=i.S("approvalStatus"),["approvedAt"]=i.S("approvedAt")
            }).ToArray()),
            ["comparison"]=comparison,
            ["allowedActions"]=new JsonArray("view")
        };
    }

    static List<JsonObject> LookupExpected(Dictionary<string,List<JsonObject>> byAccount,List<JsonObject> all,string accountId,string riderKey)
    {
        if(accountId!=""&&byAccount.TryGetValue(accountId,out var list)&&list.Count>0)
        {
            if(riderKey!="")
            {
                var forRider=list.Where(e=>e.S("riderKey")==riderKey).ToList();
                if(forRider.Count>0)return forRider;
            }
            return list;
        }
        if(riderKey!="")
        {
            var byRider=all.Where(e=>e.S("riderKey")==riderKey).ToList();
            if(byRider.Count>0)return byRider;
        }
        return [];
    }

    static JsonObject InvoiceSide(JsonObject line,JsonObject inv)
    {
        var sm=line["sourceMetrics"] as JsonObject??new JsonObject();
        string status=sm.S("صالح",sm.S("invoiceStatus",sm.S("status",line.S("status"))));
        string tier=sm.S("الشريحة",sm.S("tier",sm.S("segment",sm.S("experienceTier",line.S("tier",line.S("segment"))))));
        // Only an explicit rate column counts — never invent unit rate from baseGross/orders (CORE1d).
        string rateRaw=sm.S("السعر",sm.S("rate",sm.S("perOrderRate",line.S("rate"))));
        decimal? rate=null;
        if(rateRaw!=""&&decimal.TryParse(J.Digits(rateRaw),NumberStyles.Number,CultureInfo.InvariantCulture,out var parsed))
            rate=parsed;
        return new JsonObject
        {
            ["invoiceStatus"]=status!=""?status:inv.S("approvalStatus"),
            ["invoiceTier"]=tier==""?null:tier,
            ["invoiceRate"]=rate,
            ["invoiceEligibility"]=status==""?null:status
        };
    }

    static JsonObject ComparisonRow(JsonObject inv,JsonObject line,string accountId,string? riderKey,JsonObject? expected,JsonObject invoiceSide)
    {
        string? expectedStatus=expected?.S("expectedStatus");
        string? expectedTier=expected?.S("expectedTier",expected?.S("expectedExperienceTier")??"");
        string? expectedCapacity=expected?.S("expectedCapacityTier");
        string expectedEligibility=expected?.S("expectedEligibility")??"";
        if(expectedTier=="")expectedTier=null;
        if(expectedCapacity=="")expectedCapacity=null;
        if(expectedStatus=="")expectedStatus=null;
        decimal? expectedRate=null;
        if(expected!=null&&expected.ContainsKey("expectedRate"))
            expectedRate=expected.N("expectedRate");

        string invoiceStatus=invoiceSide.S("invoiceStatus");
        string? invoiceTier=invoiceSide.S("invoiceTier")==""?null:invoiceSide.S("invoiceTier");
        decimal? invoiceRate=invoiceSide.ContainsKey("invoiceRate")&&invoiceSide["invoiceRate"] is not null?invoiceSide.N("invoiceRate"):null;
        string? invoiceEligibility=invoiceSide.S("invoiceEligibility")==""?null:invoiceSide.S("invoiceEligibility");

        bool hasInvoice=true;
        bool hasExpected=expected!=null&&(expectedStatus!=null||expectedTier!=null||expectedEligibility!=""||expectedRate!=null);
        string? match=null;
        var deltas=new List<string>();
        string deltaNote="الفاتورة المعتمدة هي المرجع؛ العرض للمراجعة فقط (القبول هو الأصل).";

        if(hasInvoice&&hasExpected)
        {
            string expLabel=expectedStatus??expectedEligibility;
            string invLabel=invoiceEligibility??invoiceStatus;
            if(expLabel!=""&&invLabel!=""&&!LabelsEqual(expLabel,invLabel))
                deltas.Add("الحالة: متوقع «"+expLabel+"» × فاتورة «"+invLabel+"»");
            // Tier delta only when both sides have a real tier — missing invoice tier is not differs alone.
            if(expectedTier!=null&&invoiceTier!=null&&!LabelsEqual(expectedTier,invoiceTier))
                deltas.Add("الشريحة: متوقع «"+expectedTier+"» × فاتورة «"+invoiceTier+"»");
            if(expectedRate!=null&&invoiceRate!=null&&expectedRate.Value!=invoiceRate.Value)
                deltas.Add("السعر: متوقع "+expectedRate.Value.ToString(CultureInfo.InvariantCulture)+" × فاتورة "+invoiceRate.Value.ToString(CultureInfo.InvariantCulture));

            match=deltas.Count==0?"equal":"differs";
            deltaNote=deltas.Count==0
                ?"لا فرق في الحالة/الشريحة/السعر المعروض — الفاتورة المعتمدة هي المرجع."
                :string.Join("؛ ",deltas)+" — الفاتورة المعتمدة هي المرجع.";
        }
        // Leave match unset until both sides exist (brief D / §31).

        var cmp=new JsonObject{
            ["invoiceId"]=inv.S("id"),
            ["companyId"]=inv.S("companyId"),
            ["accountId"]=accountId,
            ["riderKey"]=riderKey,
            ["invoiceOrders"]=line.N("orders"),
            ["invoiceBaseGross"]=line.ContainsKey("baseGross")?line.N("baseGross"):null,
            ["invoiceStatus"]=invoiceStatus,
            ["invoiceTier"]=invoiceTier,
            ["invoiceRate"]=invoiceRate,
            ["invoiceEligibility"]=invoiceEligibility,
            ["expectedStatus"]=expectedStatus,
            ["expectedTier"]=expectedTier,
            ["expectedCapacityTier"]=expectedCapacity,
            ["expectedEligibility"]=expectedEligibility==""?null:expectedEligibility,
            ["expectedRate"]=expectedRate,
            ["deltaNoteAr"]=deltaNote,
            ["statusDelta"]=deltas.Any(d=>d.StartsWith("الحالة",StringComparison.Ordinal)),
            ["tierDelta"]=deltas.Any(d=>d.StartsWith("الشريحة",StringComparison.Ordinal)),
            ["rateDelta"]=deltas.Any(d=>d.StartsWith("السعر",StringComparison.Ordinal))
        };
        if(match!=null)cmp["match"]=match;
        return cmp;
    }

    static bool LabelsEqual(string a,string b)=>
        string.Equals(a.Trim(),b.Trim(),StringComparison.OrdinalIgnoreCase);

    static List<JsonObject> LoadExpectedRows(DbConnection c,string app,string period)
    {
        var rows=new List<JsonObject>();
        // Keeta register ops import: metrics.expectedEligibility per rider share on an account.
        foreach(var ops in Store.List(c,OperationsImports.Kind).Where(o=>o.S("app")==app&&o.S("period")==period))
        {
            foreach(var account in (ops["review"]?["accounts"] as JsonArray??[]).OfType<JsonObject>())
            {
                string accountId=account.S("accountId");
                foreach(var share in (account["riders"] as JsonArray??[]).OfType<JsonObject>())
                {
                    var metrics=share["metrics"] as JsonObject??new JsonObject();
                    string expected=metrics.S("expectedEligibility");
                    if(expected==""&&metrics.S("expectedStatus")==""&&!metrics.ContainsKey("expectedRate"))continue;
                    // Keep eligibility label on status/eligibility only — do not copy it into tier
                    // (that made matching eligibility "differs" when the invoice has no tier column).
                    var row=new JsonObject
                    {
                        ["accountId"]=accountId,
                        ["riderKey"]=share.S("rider"),
                        ["expectedEligibility"]=expected,
                        ["expectedStatus"]=metrics.S("expectedStatus",expected),
                        ["expectedTier"]=metrics.S("expectedTier"),
                        ["expectedCapacityTier"]=metrics.S("expectedCapacityTier"),
                        ["approval"]="expected_only",
                        ["source"]="operations_import"
                    };
                    if(metrics.ContainsKey("expectedRate"))row["expectedRate"]=metrics.N("expectedRate");
                    rows.Add(row);
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Match Omar evaluation snapshot to the requested pay month via stored period or QueryJson Year/Month.
    /// Never uses createdAt prefix (snapshot may be created after the month).
    /// </summary>
    static bool SnapshotMatchesPeriod(JsonObject snap,string period)
    {
        if(snap.S("period")==period)return true;
        if(TryQueryMonth(snap.S("queryJson"),out string qMonth)&&qMonth==period)return true;
        if(TryQueryMonth(snap.S("resultJson"),out string rMonth)&&rMonth==period)return true;
        return false;
    }

    static bool TryQueryMonth(string json,out string month)
    {
        month="";
        if(string.IsNullOrWhiteSpace(json))return false;
        try
        {
            if(JsonNode.Parse(json) is not JsonObject obj)return false;
            int year=(int)obj.N("Year",obj.N("year"));
            int mon=(int)obj.N("Month",obj.N("month"));
            if(year is < 2000 or > 2100||mon is < 1 or > 12)return false;
            month=$"{year:D4}-{mon:D2}";
            return true;
        }
        catch{return false;}
    }

    /// <summary>Read-only live Ninja evaluation snapshots from Omar ERP (ninja.EvaluationSnapshots).</summary>
    static List<JsonObject> LoadNinjaSnapshotsLive(DbConnection payroll,ClaimsPrincipal actor,IConfiguration config,string period)
    {
        using var erp=Operations.OpenErp(config);
        var scope=Operations.LiveScope(payroll,actor);
        int size=LiveOperationsQuery.MaxPageSize;
        var matched=new List<JsonObject>();
        int pageNum=1;long total=0;
        while(true)
        {
            int skip=(pageNum-1)*size;
            var request=new LiveOperationsQuery.PageRequest(pageNum,size,"id","",skip,size,"","","","","");
            var page=LiveOperationsQuery.Page(erp,"ninja_evaluation_snapshot",request,scope,FinancialCore.Sponsors);
            total=page.Total;
            var raw=LiveOperationsSource.Documents(page.Snapshot,FinancialCore.Sponsors,"ninja_evaluation_snapshot").ToList();
            matched.AddRange(raw.Select(d=>Operations.Project(d,"ninja_evaluation_snapshot")).Where(s=>SnapshotMatchesPeriod(s,period)));
            if(raw.Count<size||pageNum*(long)size>=total)break;
            pageNum++;
        }
        return matched
            .OrderByDescending(s=>s.S("createdAt"),StringComparer.Ordinal)
            .Take(50)
            .ToList();
    }

    static void MergeNinjaSnapshotAccounts(DbConnection c,List<JsonObject> snaps,List<JsonObject> expectedList,Dictionary<string,List<JsonObject>> expectedByAccount)
    {
        foreach(var snap in snaps)
        {
            var accountIds=new List<string>();
            if(snap["accountIds"] is JsonArray ids)
                accountIds.AddRange(ids.OfType<JsonValue>().Select(v=>v.TryGetValue<string>(out var s)?s:"").Where(s=>s!=""));
            string raw=snap.S("accountIdsJson");
            if(accountIds.Count==0&&raw!="")
            {
                try
                {
                    if(JsonNode.Parse(raw) is JsonArray parsed)
                        accountIds.AddRange(parsed.OfType<JsonValue>().Select(v=>v.TryGetValue<string>(out var s)?s:"").Where(s=>s!=""));
                }
                catch{/* ignore malformed snapshot JSON */}
            }
            // policyVersion is a version stamp, not an eligibility tier (CORE1d).
            string status=snap.S("expectedStatus",snap.S("status","expected_only"));
            string tier=snap.S("expectedTier");
            foreach(string accountId in accountIds.Distinct(StringComparer.Ordinal))
            {
                if(expectedByAccount.ContainsKey(accountId))continue;
                var row=new JsonObject
                {
                    ["accountId"]=accountId,
                    ["riderKey"]="",
                    ["expectedStatus"]=status,
                    ["expectedTier"]=tier==""?null:tier,
                    ["expectedCapacityTier"]=null,
                    ["approval"]="expected_only",
                    ["source"]="ninja_evaluation_snapshot",
                    ["snapshotId"]=snap.S("id"),
                    ["policyVersion"]=snap.S("policyVersion")
                };
                expectedList.Add(row);
                expectedByAccount[accountId]=[row];
            }
        }
        _=c;
    }

    static IEnumerable<JsonObject> InvoiceLines(DbConnection c,JsonObject invoice)
    {
        string id=invoice.S("id");
        if(invoice.S("platform")=="Ninja"||PayPlatforms.For(invoice.S("platform",invoice.S("platformCode"))).App=="ninja")
            return NinjaInvoices.Lines(c,null,id);
        return Store.List(c,"platform_invoice_line").Where(l=>l.S("invoiceId")==id);
    }
}
