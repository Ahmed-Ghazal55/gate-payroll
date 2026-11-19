using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>Shared validation for campaign/custom_form target type and target company (no free-text unknowns).</summary>
public static class FormTargeting
{
    public static readonly string[] AllowedTargets = ["ALL", "rider", "riders", "staff"];

    public static void ApplyAndValidate(JsonObject input, JsonObject doc)
    {
        foreach (string k in new[] { "companyId", "targetCompanyId", "targetIqama", "target", "targetCity", "targetAudience", "targetSponsorId" })
        {
            string v = input.S(k).Trim();
            if (v == "") continue;
            ValidateField(k, v);
            if (k == "targetAudience") doc["target"] = v;
            else doc[k] = v;
        }
        foreach (string k in new[] { "targetCompanyIds", "sponsorIds", "companyIds" })
        {
            if (input[k] is not JsonArray arr) continue;
            var clean = new JsonArray();
            foreach (var item in arr)
            {
                string v = (item?.ToString() ?? "").Trim();
                if (v == "") continue;
                ValidateCompany(v);
                clean.Add(v);
            }
            if (clean.Count > 0) doc[k] = clean;
        }
        if (input["targetIqamas"] is JsonArray iqamas)
        {
            var clean = new JsonArray();
            foreach (var item in iqamas)
            {
                string v = (item?.ToString() ?? "").Trim();
                if (v == "") continue;
                ValidateIqamaOrAll(v);
                clean.Add(v);
            }
            if (clean.Count > 0) doc["targetIqamas"] = clean;
        }
    }

    public static void ValidateField(string key, string value)
    {
        if (key is "target" or "targetAudience")
        {
            if (value.Length == 10 && value.All(char.IsAsciiDigit)) return;
            J.Need(AllowedTargets.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)),
                "نوع الجمهور المستهدف غير معروف. اختر من القائمة المعتمدة.");
            return;
        }
        if (key is "companyId" or "targetCompanyId" or "targetSponsorId" or "sponsorId")
        {
            ValidateCompany(value);
            return;
        }
        if (key == "targetIqama")
        {
            ValidateIqamaOrAll(value);
            return;
        }
        if (key == "targetCity")
        {
            J.Need(value.Length <= 100, "مدينة الاستهداف غير صالحة.");
        }
    }

    public static void ValidateCompany(string value)
    {
        if (value.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return;
        bool known = FinancialCore.Sponsors.Any(s =>
            string.Equals(s.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Code, value, StringComparison.OrdinalIgnoreCase));
        J.Need(known, "الشركة المستهدفة غير معروفة. اختر شركة من القائمة المعتمدة.");
    }

    static void ValidateIqamaOrAll(string value)
    {
        if (value.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return;
        J.Need(value.Length == 10 && value.All(char.IsAsciiDigit), "إقامة الاستهداف غير صالحة.");
    }
}
