using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using System.Data.Common;

namespace Payroll;

public static class TableExcelApi
{
    public static ExcelColumn[] Columns(string kind) => kind switch
    {
        "choices" => [new("رقم الإقامة", Identity: true), new("الاسم الكامل"), new("نظام العمل", Choices: ["نظام الراتب الجديد", "نظام العمولة السابق"])],
        "riders" => [new("رقم الإقامة", Identity: true), new("الاسم الكامل"), new("الجوال"), new("المدينة"), new("معرف سياسة الحساب"), new("الجنسية"), new("نوع العلاقة", Choices:["SponsoredInternal","External"]), new("الكفيل"), new("نوع المركبة", Choices:["Car","Motorcycle"]), new("الحالة", Choices:["Active","OnLeave","Archived","NeedsReview"])],
        "work" => new[] { "sourceRef", "iqama", "name", "accountId", "from", "to", "orders", "platformRevenue", "riderPay", "isValid", "company", "city" }.Select(h => new ExcelColumn(h, h is "orders" or "platformRevenue" or "riderPay", h == "isValid" ? ["true", "false"] : null, h == "iqama")).ToArray(),
        _ => throw new ApiError("لا يوجد مخطط استيراد معتمد لهذا الجدول.", 404)
    };
    public sealed record ImportRow(int Row, JsonObject Data);
    public sealed record ImportIssue(int Row, string Message);
    public sealed record ImportPreview(string Fingerprint, List<ImportRow> Rows, List<ImportIssue> Errors);

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/excel/{kind}/template", (string kind) => Results.File(ExcelFiles.Write(Columns(kind), []), ExcelFiles.Mime, $"{kind}-template-v2.xlsx"));
        api.MapPost("/excel/export", (JsonObject input) =>
        {
            var headers = (input["headers"] as JsonArray)?.Select(x => x?.ToString() ?? "").ToArray() ?? [];
            J.Need(headers.Length is > 0 and <= 100 && headers.All(h => h.Length is > 0 and <= 150), "عناوين تصدير غير صحيحة.");
            var numbers = (input["numericColumns"] as JsonArray)?.Select(x => int.TryParse(x?.ToString(), out int i) ? i : -1).ToHashSet() ?? [];
            var cols = headers.Select((h, i) => new ExcelColumn(h, numbers.Contains(i))).ToArray();
            var rows = new List<object?[]>();
            foreach (var item in (input["rows"] as JsonArray ?? throw new ApiError("صفوف التصدير مطلوبة.")))
            {
                J.Need(item is JsonArray cells && cells.Count == headers.Length && rows.Count < 24999, "حجم أو شكل بيانات التصدير غير صحيح.");
                rows.Add(((JsonArray)item!).Select((value, i) => numbers.Contains(i) && decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n) ? (object)n : value?.ToString() ?? "").ToArray());
            }
            return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "table-export.xlsx");
        });
        api.MapPost("/excel/{kind}/preview", (string kind, JsonObject input) => store.Read(c => Preview(c, kind, input)));
        api.MapPost("/excel/{kind}/commit", (string kind, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            // Revalidate against current records inside the transaction. No partial batch writes.
            var preview = Preview(c, kind, input, tx);
            J.Need(preview.Errors.Count == 0 && preview.Rows.Count > 0, "عالج أخطاء الملف قبل الاستيراد. لم يُحفظ أي صف.");
            J.Need(input.S("fingerprint") == preview.Fingerprint, "تغير الملف أو وجهة الاستيراد منذ المعاينة؛ أعد المعاينة.");
            string batchId = Guid.NewGuid().ToString("N"), now = DateTimeOffset.UtcNow.ToString("O");
            foreach (var row in preview.Rows)
            {
                var doc = row.Data.Clone();
                doc["id"] = kind == "choices" ? Guid.NewGuid().ToString("N") : doc.S("iqama");
                if(kind=="choices") { doc["status"]="pending"; doc["version"]=1; doc["isEdited"]=false; doc["history"]=new JsonArray(); } else if(doc.S("status")=="") doc["status"]="needs_review";
                doc["createdAt"] = now; doc["importBatchId"] = batchId;
                if (kind == "choices") { doc["campaignId"] = input.S("campaignId"); doc["schema"] = "work-choice-v1"; }
                Store.Save(c, kind == "choices" ? "submission" : "rider", doc, tx);
            }
            Store.Save(c, "table_import", new() { ["id"] = batchId, ["kind"] = kind, ["schemaVersion"] = 1, ["sourceFingerprint"] = preview.Fingerprint, ["filename"] = Path.GetFileName(input.S("filename")), ["campaignId"] = input.S("campaignId"), ["count"] = preview.Rows.Count, ["createdAt"] = now }, tx);
            Store.Audit(c, tx, "excel.import." + kind, batchId, ctx.User.Identity?.Name ?? "administrator");
            return new { count = preview.Rows.Count, batchId };
        }));
    }

    public static ImportPreview Preview(DbConnection c, string kind, JsonObject input, DbTransaction? tx = null)
    {
        J.Need(kind is "choices" or "riders", "استيراد التشغيل من شاشة مصادر الرواتب، وبقية جداول الاعتماد تستخدم مسارها الإداري.");
        var columns = Columns(kind); string campaignId = input.S("campaignId");
        if (kind == "choices")
        {
            var campaign = Store.Get(c, campaignId, "campaign", tx) ?? throw new ApiError("اختر حملة استقبال موجودة.");
            J.Need(campaign.S("status") == "open" && DateTimeOffset.Parse(campaign.S("expiresAt"), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow, "الحملة مغلقة أو انتهت صلاحيتها.");
        }
        J.Need(Path.GetExtension(input.S("filename")).Equals(".xlsx", StringComparison.OrdinalIgnoreCase), "استخدم قالب Excel بصيغة xlsx.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(input.Required("dataBase64")); } catch (FormatException) { throw new ApiError("محتوى الملف غير صالح."); }
        J.Need(bytes.Length is > 0 and <= 8 * 1024 * 1024, "الملف يجب ألا يتجاوز8ميجابايت.");
        List<Imports.Sheet> sheets;
        try
        {
            // Canonical entry templates contain values, not calculated spreadsheet formulas.
            using (var zip = new ZipArchive(new MemoryStream(bytes)))
            {
                J.Need(zip.Entries.Count <= 3000 && zip.Entries.Sum(e => e.Length) <= 160L * 1024 * 1024, "ملف أكبر من حد المعالجة.");
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml")))
                {
                    J.Need(entry.Length < 40 * 1024 * 1024, "ورقة كبيرة جدًا.");
                    using var stream = entry.Open(); J.Need(!XDocument.Load(stream).Descendants(ns + "f").Any(), "قالب الإدخال يقبل قيمًا مباشرة فقط؛ احذف الصيغ أو الصق القيم.");
                }
            }
            sheets = Imports.Read(bytes, input.S("filename"));
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new ApiError("مصنف Excel غير صالح؛ نزّل القالب وأعد التعبئة."); }
        J.Need(sheets.Count == 1 && sheets[0].Name == "البيانات", "استخدم ورقة «البيانات» فقط كما في القالب.");
        var sheet = sheets[0];
        bool legacyRiders = kind == "riders" && sheet.Rows.Count>0 && sheet.Rows[0].Select(x=>x.Trim()).SequenceEqual(Columns(kind).Take(5).Select(x=>x.Header));
        if (legacyRiders) columns=columns.Take(5).ToArray();
        J.Need(sheet.Rows.Count > 0 && sheet.Rows[0].Select(h => h.Trim()).SequenceEqual(columns.Select(x => x.Header)), "عناوين أو ترتيب الأعمدة مختلف عن القالب المعتمد. نزّل القالب دون تغيير رأس الجدول.");
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "|v1|" + campaignId + "|" + Convert.ToHexString(SHA256.HashData(bytes)))));
        var rows = new List<ImportRow>(); var errors = new List<ImportIssue>(); var seen = new HashSet<string>();
        var existing = Store.List(c, kind == "choices" ? "submission" : "rider", tx).Where(d => kind != "choices" || d.S("campaignId") == campaignId).Select(d => d.S("iqama")).ToHashSet();
        var approvedPolicies = Store.List(c, "policy", tx).Where(p => p.S("status") == "approved").Select(p => p.S("id")).ToHashSet();
        for (int index = 1; index < sheet.Rows.Count; index++)
        {
            var values = sheet.Rows[index]; if (values.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                J.Need(values.Skip(columns.Length).All(string.IsNullOrWhiteSpace), "توجد بيانات في أعمدة خارج القالب.");
                string Value(int col) => col < values.Count ? values[col].Trim() : "";
                string system = kind != "choices" ? "tiers" : Value(2) switch { "نظام الراتب الجديد" => "tiers", "نظام العمولة السابق" => "commission", _ => throw new ApiError("نظام العمل يجب اختياره من أحد النظامين في القالب.") };
                var choice = kind == "choices"
                    ? WorkChoices.Validate(new() { ["iqama"] = Value(0), ["name"] = Value(1), ["workSystem"] = system })
                    : new WorkChoice(DirectoryProfiles.Iqama(Value(0)), DirectoryProfiles.Name(Value(1)), "");
                J.Need(seen.Add(choice.Iqama), "إقامة مكررة داخل الملف.");
                J.Need(!existing.Contains(choice.Iqama), "الإقامة مسجلة بالفعل في هذا الجدول/الحملة؛ الاستيراد لا يستبدل السجلات الحالية.");
                JsonObject doc = new() { ["iqama"] = choice.Iqama, ["name"] = choice.Name };
                if (kind == "choices") doc["workSystem"] = choice.WorkSystem;
                else
                {
                    J.Iqama(choice.Iqama); string policy = Value(4);
                    J.Need(policy == "" || approvedPolicies.Contains(policy), "معرف سياسة الحساب غير موجود أو غير معتمد.");
                    J.Need(Value(3).Length <= 100, "اسم المدينة طويل جدًا.");
                    doc["phone"] = Value(2) == "" ? "" : RiderDirectory.Phone(Value(2)); doc["city"] = Value(3); doc["policyId"] = policy;
                    if (!legacyRiders) {doc["nationality"]=Value(5);doc["engagementType"]=Value(6);doc["sponsor"]=Value(7);doc["operationalRole"]=Value(8);doc["status"]=Value(9);doc=RiderDirectory.Normalize(doc);}
                }
                rows.Add(new(index + 1, doc));
            }
            catch (ApiError e) { errors.Add(new(index + 1, e.Message)); }
        }
        if (rows.Count + errors.Count == 0) errors.Add(new(2, "القالب فارغ؛ أدخل بيانات أولًا."));
        return new(fingerprint, rows, errors);
    }
}
