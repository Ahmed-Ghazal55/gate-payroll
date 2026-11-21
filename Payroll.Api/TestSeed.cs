using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

/// <summary>Development-only HTTP seed helpers for Node HTTP contracts. Not mapped in Production.</summary>
public static class TestSeed
{
    public static void Map(RouteGroupBuilder api, Store store, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return;
        api.MapPost("/test/seed-work", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            J.Need(AccessPolicy.Administrator(ctx.User), "تهيئة الاختبار لمسؤول النظام فقط.", 403);
            var rows = input["rows"] as JsonArray ?? throw new ApiError("صفوف التهيئة مطلوبة.");
            J.Need(rows.Count is > 0 and <= 500, "حد صفوف تهيئة العمل 1 إلى 500.");
            var ids = new JsonArray();
            foreach (var item in rows)
            {
                var row = item as JsonObject ?? throw new ApiError("صف تهيئة غير صالح.");
                string period = J.Period(row.Required("period"));
                string platform = row.Required("platform");
                string iqama = J.Iqama(row.Required("iqama"));
                string accountId = row.S("accountId");
                string from = row.S("from", period + "-01");
                string to = row.S("to");
                if (to == "")
                {
                    var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    to = start.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                string sourceRef = row.S("sourceRef");
                if (sourceRef == "") sourceRef = $"{platform}:{accountId}:{iqama}:{from}:{to}";
                string id = WorkId(period, platform, sourceRef, iqama, accountId, from, to);
                var doc = new JsonObject
                {
                    ["id"] = id,
                    ["uploadId"] = row.S("uploadId", "synthetic-seed"),
                    ["sourceRef"] = sourceRef,
                    ["iqama"] = iqama,
                    ["name"] = row.S("name"),
                    ["platform"] = platform,
                    ["accountId"] = accountId,
                    ["period"] = period,
                    ["from"] = from,
                    ["to"] = to,
                    ["orders"] = (int)row.N("orders"),
                    ["platformRevenue"] = row.ContainsKey("platformRevenue") && row["platformRevenue"] is not null ? row.N("platformRevenue") : null,
                    ["riderPay"] = row.ContainsKey("riderPay") && row["riderPay"] is not null ? row.N("riderPay") : null,
                    ["isValid"] = row.ContainsKey("isValid") && row["isValid"] is not null ? row.B("isValid") : null,
                    ["company"] = row.S("company"),
                    ["city"] = row.S("city"),
                    ["datesConfirmed"] = row.S("from") != "" && row.S("to") != ""
                };
                Store.Save(c, "work", doc, tx);
                ids.Add(id);
            }
            return new JsonObject { ["ids"] = ids };
        })).ExcludeFromDescription().WithTags("Administration").WithSummary("Development-only synthetic work seed for HTTP tests");
    }

    static string WorkId(string period, string platform, string sourceRef, string iqama, string accountId, string from, string to)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{period}|{platform}|{sourceRef}|{iqama}|{accountId}|{from}|{to}"))).ToLowerInvariant();
}
