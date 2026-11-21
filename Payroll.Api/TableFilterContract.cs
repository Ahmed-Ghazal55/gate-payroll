using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

/// <summary>
/// Generic Excel-style table filter, sort, and distinct-values contract for server-paged tables (FL2).
/// Supports query parameters:
///   filters=[{"col":"...","op":"...","value":"...","valueTo":"...","values":["..."]}]
///   sort=[{"col":"...","dir":"asc"|"desc","tone":"..."}] (plus legacy sort=col / sort=-col)
/// Enforces column whitelists per endpoint while sharing the endpoint's authorization and company/platform scope.
/// </summary>
public static class TableFilterContract
{
    public sealed record FilterClause(
        string Col,
        string Op,
        string? Value = null,
        string? ValueTo = null,
        IReadOnlyList<string>? Values = null);

    public sealed record SortClause(
        string Col,
        string Dir = "asc",
        string? Tone = null);

    public sealed record FilterOptions(
        IReadOnlyDictionary<string, string>? LabelOverrides = null,
        IReadOnlyDictionary<string, string>? ToneOverrides = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? CompositeColumns = null);

    static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "contains", "notContains", "beginsWith", "startsWith", "endsWith",
        "eq", "equals", "neq", "notEquals",
        "gt", "gte", "lt", "lte", "between", "topN",
        "before", "after", "thisMonth", "lastMonth",
        "in", "color"
    };

    static readonly Regex SafeIdentifier = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    static readonly CompareInfo ArabicCompare = CultureInfo.GetCultureInfo("ar-SA").CompareInfo;

    static readonly HashSet<string> IdLikeColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "iqama", "phone", "mobile", "iban", "personalIban", "mudadIban", "formIban",
        "unifiedNationalNumber", "sponsorUnified", "commercialReg", "receiptNumber",
        "supplierInvoiceNumber", "cardNumber", "code", "plate", "plateNumber", "plateNumberAr",
        "assetNumber", "platformWorkId", "reference"
    };

    static readonly Dictionary<string, string> CommonStatusLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["active"] = "نشط",
        ["inactive"] = "غير نشط",
        ["pending"] = "بانتظار الاعتماد",
        ["pending_approval"] = "بانتظار الاعتماد",
        ["approved"] = "معتمد",
        ["rejected"] = "مرفوض",
        ["cancelled"] = "ملغي",
        ["canceled"] = "ملغي",
        ["waived"] = "معفي",
        ["disputed"] = "متنازع عليه",
        ["settled"] = "مسدد بالكامل",
        ["paid"] = "مسدد",
        ["posted"] = "مرحّل",
        ["reversed"] = "معكوس",
        ["open"] = "جديد",
        ["need_info"] = "بانتظار توضيح المندوب",
        ["awaiting_rider"] = "بانتظار رد المندوب",
        ["first_approved"] = "اعتماد أول — بانتظار الثاني",
        ["Terminated"] = "فصل",
        ["Fleeing"] = "هروب",
        ["Archived"] = "مؤرشف",
        ["SponsoredInternal"] = "على الكفالة",
        ["sponsored"] = "على الكفالة",
        ["External"] = "أجير خارجي",
        ["OutsideRider"] = "أجير خارجي",
        ["external"] = "أجير خارجي",
        ["rider"] = "مندوب",
        ["admin"] = "إداري",
        ["Car"] = "سيارة",
        ["Motorcycle"] = "دراجة نارية",
        ["ALL"] = "كل المناديب",
        ["general"] = "تعميم عام",
        ["individual"] = "إشعار فردي",
        ["admin_to_rider"] = "من الإدارة للمندوب",
        ["rider_to_admin"] = "من المندوب",
        ["annual_leave"] = "إجازة سنوية",
        ["appointment"] = "حجز موعد",
        ["iban_update"] = "تحديث الآيبان",
        ["data_update"] = "تحديث البيانات",
        ["complaint"] = "شكوى سرية"
    };

    static readonly HashSet<string> SuccessTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "approved", "posted", "paid", "completed", "ready", "salary_approved",
        "settled", "matched", "valid", "open", "enabled", "true", "yes",
        NormalizeText("نشط"), NormalizeText("نشطة"), NormalizeText("معتمد"),
        NormalizeText("معتمدة"), NormalizeText("مرحل"), NormalizeText("مرحّل"),
        NormalizeText("مسدد"), NormalizeText("مسدد بالكامل"), NormalizeText("مكتمل"),
        NormalizeText("جاهز"), NormalizeText("ساري"), NormalizeText("سارية"),
        NormalizeText("مطابق"), NormalizeText("مقبول"), NormalizeText("متاح"),
        NormalizeText("مدفوع"), NormalizeText("نعم")
    };

    static readonly HashSet<string> WarningTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "pending_approval", "draft", "review", "partial", "hold", "paused",
        "warning", "expiring", "under_review", "proposed", "processing", "need_info",
        "awaiting_rider", "first_approved",
        NormalizeText("معلق"), NormalizeText("معلقة"), NormalizeText("بانتظار الاعتماد"),
        NormalizeText("مسودة"), NormalizeText("قيد المراجعة"), NormalizeText("جزئي"),
        NormalizeText("موقوف مؤقتا"), NormalizeText("موقوف"), NormalizeText("موقوفة"),
        NormalizeText("تنبيه"), NormalizeText("قرب الانتهاء"), NormalizeText("قيد الانتظار"),
        NormalizeText("مقترح"), NormalizeText("بانتظار توضيح المندوب"), NormalizeText("بانتظار رد المندوب")
    };

    static readonly HashSet<string> DangerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "inactive", "rejected", "cancelled", "canceled", "reversed", "overdue", "expired",
        "failed", "error", "terminated", "fleeing", "absent", "blocked", "unmatched",
        "disputed", "missing", "disabled", "false", "no",
        NormalizeText("غير نشط"), NormalizeText("غير نشطة"), NormalizeText("مرفوض"),
        NormalizeText("ملغي"), NormalizeText("ملغى من المندوب"), NormalizeText("معكوس"),
        NormalizeText("متأخر"), NormalizeText("متعثر"), NormalizeText("منتهي"),
        NormalizeText("منتهية"), NormalizeText("فصل"), NormalizeText("هروب"),
        NormalizeText("تغيب"), NormalizeText("محظور"), NormalizeText("خطأ"),
        NormalizeText("مفقود"), NormalizeText("غير مطابق"), NormalizeText("معترض"),
        NormalizeText("آيبان مفقود للمكفول")
    };

    static readonly HashSet<string> InfoTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "new", "transferred", "assigned", "external", "outsiderider",
        NormalizeText("جديد"), NormalizeText("محول"), NormalizeText("مسند"),
        NormalizeText("خارجي"), NormalizeText("أجير خارجي")
    };

    static readonly HashSet<string> AccentTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "sponsored", "sponsoredinternal", "special",
        NormalizeText("مكفول"), NormalizeText("على الكفالة"), NormalizeText("كفالة")
    };

    static readonly HashSet<string> NeutralTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "archived", "closed", "none",
        NormalizeText("مؤرشف"), NormalizeText("مغلق"), NormalizeText("عادي")
    };

    public const int MaxFilterClauses = 30;
    public const int MaxValuesPerClause = 500;
    public const int MaxSortClauses = 10;
    public const int MaxValueLength = 500;
    public const int MaxRawQueryLength = 32_768;

    public static bool HasJsonContractParams(IQueryCollection query) =>
        (query.ContainsKey("filters") && !string.IsNullOrWhiteSpace(query["filters"].ToString())) ||
        (query.ContainsKey("sort") && query["sort"].ToString().TrimStart().StartsWith('['));

    public static bool HasContractParams(IQueryCollection query) => HasJsonContractParams(query);

    public static IReadOnlyList<FilterClause> ParseFilters(
        IQueryCollection query,
        IReadOnlySet<string>? whitelist = null,
        IReadOnlyDictionary<string, string>? columnAliases = null)
    {
        string raw = (query["filters"].ToString() ?? "").Trim();
        if (raw.Length == 0) return [];
        if (raw.Length > MaxRawQueryLength)
            throw new ApiError("معامل الفلاتر يتجاوز الحد المسموح به.", 400);
        JsonArray? arr;
        try
        {
            arr = JsonNode.Parse(raw) as JsonArray;
        }
        catch (JsonException)
        {
            throw new ApiError("صيغة معامل الفلاتر (filters) غير صالحة.", 400);
        }
        if (arr == null) throw new ApiError("معامل الفلاتر (filters) يجب أن يكون مصفوفة JSON.", 400);
        if (arr.Count > MaxFilterClauses)
            throw new ApiError($"عدد شروط الفلترة يتجاوز الحد الأقصى ({MaxFilterClauses}).", 400);

        var list = new List<FilterClause>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject obj)
                throw new ApiError("عنصر الفلتر غير صالح.", 400);
            string rawCol = (obj["col"]?.ToString() ?? "").Trim();
            string op = (obj["op"]?.ToString() ?? "").Trim();
            if (rawCol.Length == 0 || !SafeIdentifier.IsMatch(rawCol))
                throw new ApiError("عمود التصفية غير مسموح به.", 400);

            string mappedCol = columnAliases != null && columnAliases.TryGetValue(rawCol, out var alias)
                ? alias
                : rawCol;
            if (whitelist != null && !whitelist.Contains(rawCol) && !whitelist.Contains(mappedCol))
                throw new ApiError($"عمود التصفية غير مسموح به: {rawCol}", 400);

            if (op.Length == 0 || !AllowedOperators.Contains(op))
                throw new ApiError($"عامل التصفية غير صالح: {op}", 400);

            string? val = obj["value"]?.ToString();
            string? valTo = obj["valueTo"]?.ToString();
            if ((val != null && val.Length > MaxValueLength) || (valTo != null && valTo.Length > MaxValueLength))
                throw new ApiError($"طول قيمة التصفية يتجاوز الحد الأقصى ({MaxValueLength}).", 400);
            List<string>? values = null;
            if (obj["values"] is JsonArray valArr)
            {
                if (valArr.Count > MaxValuesPerClause)
                    throw new ApiError($"عدد قيم التصفية يتجاوز الحد الأقصى ({MaxValuesPerClause}).", 400);
                values = new List<string>(valArr.Count);
                foreach (var v in valArr)
                {
                    string s = v?.ToString() ?? "";
                    if (s.Length > MaxValueLength)
                        throw new ApiError($"طول قيمة التصفية يتجاوز الحد الأقصى ({MaxValueLength}).", 400);
                    values.Add(s);
                }
            }
            else if (op.Equals("in", StringComparison.OrdinalIgnoreCase) && val != null)
            {
                values = [val];
            }

            list.Add(new FilterClause(mappedCol, NormalizeOp(op), val, valTo, values));
        }
        return list;
    }

    public static IReadOnlyList<SortClause> ParseSort(
        IQueryCollection query,
        IReadOnlySet<string>? whitelist = null,
        IReadOnlyDictionary<string, string>? columnAliases = null)
    {
        string raw = (query["sort"].ToString() ?? "").Trim();
        if (raw.Length == 0) return [];
        if (raw.Length > MaxRawQueryLength)
            throw new ApiError("معامل الترتيب يتجاوز الحد المسموح به.", 400);

        if (raw.StartsWith('['))
        {
            JsonArray? arr;
            try
            {
                arr = JsonNode.Parse(raw) as JsonArray;
            }
            catch (JsonException)
            {
                throw new ApiError("صيغة معامل الترتيب (sort) غير صالحة.", 400);
            }
            if (arr == null) throw new ApiError("معامل الترتيب (sort) يجب أن يكون مصفوفة JSON.", 400);
            if (arr.Count > MaxSortClauses)
                throw new ApiError($"عدد شروط الترتيب يتجاوز الحد الأقصى ({MaxSortClauses}).", 400);
            var list = new List<SortClause>(arr.Count);
            foreach (var node in arr)
            {
                if (node is not JsonObject obj)
                    throw new ApiError("عنصر الترتيب غير صالح.", 400);
                string rawCol = (obj["col"]?.ToString() ?? "").Trim();
                if (rawCol.Length == 0 || !SafeIdentifier.IsMatch(rawCol))
                    throw new ApiError("حقل الترتيب غير صالح.", 400);
                string mappedCol = columnAliases != null && columnAliases.TryGetValue(rawCol, out var alias)
                    ? alias
                    : rawCol;
                if (whitelist != null && !whitelist.Contains(rawCol) && !whitelist.Contains(mappedCol))
                    throw new ApiError($"حقل الترتيب غير صالح: {rawCol}", 400);
                string dir = (obj["dir"]?.ToString() ?? "asc").Trim().ToLowerInvariant();
                if (dir is not ("asc" or "desc"))
                    throw new ApiError("اتجاه الترتيب يجب أن يكون asc أو desc.", 400);
                string? tone = obj["tone"]?.ToString()?.Trim();
                if (tone != null && tone.Length > 32)
                    throw new ApiError("لون الترتيب غير صالح.", 400);
                list.Add(new SortClause(mappedCol, dir, string.IsNullOrEmpty(tone) ? null : tone));
            }
            return list;
        }

        // Legacy sort=col or sort=-col (+ optional dir=asc|desc)
        bool desc = raw.StartsWith('-') ||
                    string.Equals(query["dir"].ToString(), "desc", StringComparison.OrdinalIgnoreCase);
        string legacyCol = raw.TrimStart('-').Trim();
        if (legacyCol.Length == 0 || !SafeIdentifier.IsMatch(legacyCol))
            throw new ApiError("حقل الترتيب غير صالح.", 400);
        string mappedLegacy = columnAliases != null && columnAliases.TryGetValue(legacyCol, out var legacyAlias)
            ? legacyAlias
            : legacyCol;
        if (whitelist != null && !whitelist.Contains(legacyCol) && !whitelist.Contains(mappedLegacy))
            throw new ApiError($"حقل الترتيب غير صالح: {legacyCol}", 400);
        return [new SortClause(mappedLegacy, desc ? "desc" : "asc", null)];
    }

    public static List<JsonObject> Apply(
        IEnumerable<JsonObject> source,
        IQueryCollection query,
        IReadOnlySet<string>? whitelist = null,
        IReadOnlyDictionary<string, string>? columnAliases = null,
        DateOnly? referenceDate = null,
        FilterOptions? options = null)
    {
        var filters = ParseFilters(query, whitelist, columnAliases);
        var sorts = ParseSort(query, whitelist, columnAliases);
        var rows = source.ToList();
        if (filters.Count > 0) rows = ApplyFilters(rows, filters, referenceDate, options);
        if (sorts.Count > 0) rows = ApplySort(rows, sorts, options);
        return rows;
    }

    public static List<JsonObject> ApplyFilters(
        IEnumerable<JsonObject> source,
        IReadOnlyList<FilterClause> filters,
        DateOnly? referenceDate = null,
        FilterOptions? options = null)
    {
        var rows = source.ToList();
        if (filters.Count == 0 || rows.Count == 0) return rows;

        DateOnly refDate = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var topNThresholds = new Dictionary<int, decimal?>();
        for (int i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            if (f.Op == "topN" && TryParseDecimal(f.Value, out decimal nDec) && nDec >= 1)
            {
                int n = (int)Math.Floor(nDec);
                var nums = rows
                    .Select(r => TryParseDecimal(GetRawString(r, f.Col), out var d) ? (decimal?)d : null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .OrderByDescending(d => d)
                    .ToList();
                topNThresholds[i] = nums.Count == 0 ? null : nums[Math.Min(n, nums.Count) - 1];
            }
        }

        return rows.Where(row =>
        {
            for (int i = 0; i < filters.Count; i++)
            {
                topNThresholds.TryGetValue(i, out decimal? threshold);
                if (!MatchesClause(row, filters[i], threshold, refDate, options))
                    return false;
            }
            return true;
        }).ToList();
    }

    public static List<JsonObject> ApplySort(
        IEnumerable<JsonObject> source,
        IReadOnlyList<SortClause> sorts,
        FilterOptions? options = null)
    {
        var rows = source.ToList();
        if (sorts.Count == 0 || rows.Count <= 1) return rows;

        rows.Sort((a, b) =>
        {
            foreach (var s in sorts)
            {
                int cmp = CompareCells(a, b, s, options);
                if (cmp != 0) return cmp;
            }
            return 0;
        });
        return rows;
    }

    public static JsonObject BuildDistinct(
        IEnumerable<JsonObject> source,
        string rawColumn,
        IQueryCollection? query = null,
        IReadOnlySet<string>? whitelist = null,
        IReadOnlyDictionary<string, string>? columnAliases = null,
        FilterOptions? options = null)
    {
        string col = (rawColumn ?? "").Trim();
        if (col.Length == 0)
            return new JsonObject { ["column"] = "", ["values"] = new JsonArray(), ["counts"] = new JsonObject(), ["hasEmpty"] = false, ["emptyCount"] = 0 };

        string mappedCol = columnAliases != null && columnAliases.TryGetValue(col, out var alias)
            ? alias
            : col;
        if (whitelist != null && !whitelist.Contains(col) && !whitelist.Contains(mappedCol))
            return new JsonObject { ["column"] = col, ["values"] = new JsonArray(), ["counts"] = new JsonObject(), ["hasEmpty"] = false, ["emptyCount"] = 0 };

        var rows = source.ToList();
        if (query != null && query.ContainsKey("filters"))
        {
            // Apply filters from OTHER columns so distinct values & counts are faceted
            var allFilters = ParseFilters(query, whitelist, columnAliases);
            var otherFilters = allFilters
                .Where(f => !string.Equals(f.Col, mappedCol, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(f.Col, col, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (otherFilters.Count > 0)
                rows = ApplyFilters(rows, otherFilters, options: options);
        }

        var counts = new Dictionary<string, (string Canonical, int Count)>(StringComparer.OrdinalIgnoreCase);
        int emptyCount = 0;
        foreach (var r in rows)
        {
            string val = GetRawString(r, mappedCol).Trim();
            if (IsBlank(val))
            {
                emptyCount++;
                continue;
            }
            if (counts.TryGetValue(val, out var existing))
                counts[val] = (existing.Canonical, existing.Count + 1);
            else
                counts[val] = (val, 1);
        }

        var ordered = counts.Values
            .OrderBy(x => x.Canonical, Comparer<string>.Create((a, b) =>
                ArabicCompare.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)))
            .Take(500)
            .ToList();

        var valuesArr = new JsonArray(ordered.Select(x => (JsonNode?)x.Canonical).ToArray());
        var countsObj = new JsonObject();
        foreach (var item in ordered)
            countsObj[item.Canonical] = item.Count;

        return new JsonObject
        {
            ["column"] = col,
            ["values"] = valuesArr,
            ["counts"] = countsObj,
            ["hasEmpty"] = emptyCount > 0,
            ["emptyCount"] = emptyCount
        };
    }

    static bool MatchesClause(
        JsonObject row,
        FilterClause clause,
        decimal? topNThreshold,
        DateOnly refDate,
        FilterOptions? options = null)
    {
        string rawStr = GetRawString(row, clause.Col).Trim();
        bool blank = IsBlank(rawStr);
        if (blank) rawStr = "";
        string dispStr = DisplayLabel(row, clause.Col, rawStr, options);
        string normRaw = NormalizeText(rawStr);
        string normDisp = NormalizeText(dispStr);

        switch (clause.Op)
        {
            case "in":
            {
                if (clause.Values == null || clause.Values.Count == 0) return false;
                foreach (string allowed in clause.Values)
                {
                    string trimAllowed = (allowed ?? "").Trim();
                    if (trimAllowed.Length == 0 || trimAllowed == "(فارغ)")
                    {
                        if (blank) return true;
                        continue;
                    }
                    string normAllowed = NormalizeText(trimAllowed);
                    string allowedDisp = NormalizeText(DisplayLabel(row, clause.Col, trimAllowed, options));
                    if (normAllowed == normRaw || normAllowed == normDisp ||
                        (allowedDisp.Length > 0 && (allowedDisp == normRaw || allowedDisp == normDisp)))
                        return true;
                }
                return false;
            }

            case "color":
            {
                if (clause.Values != null && clause.Values.Count > 0)
                {
                    foreach (string allowed in clause.Values)
                    {
                        string trimAllowed = (allowed ?? "").Trim();
                        string normAllowed = NormalizeText(trimAllowed);
                        string allowedDisp = NormalizeText(DisplayLabel(row, clause.Col, trimAllowed, options));
                        if (normAllowed.Length > 0 &&
                            (normAllowed == normRaw || normAllowed == normDisp ||
                             (allowedDisp.Length > 0 && (allowedDisp == normRaw || allowedDisp == normDisp))))
                            return true;
                    }
                    return false;
                }
                string targetTone = (clause.Value ?? "").Trim().ToLowerInvariant();
                if (targetTone.Length == 0) return true;
                string? rowTone = InferTone(rawStr, dispStr, options);
                return string.Equals(rowTone, targetTone, StringComparison.OrdinalIgnoreCase);
            }

            case "topN":
            {
                if (!TryParseDecimal(clause.Value, out decimal n) || n <= 0) return true;
                if (blank || !TryParseDecimal(rawStr, out decimal cellNum) || !topNThreshold.HasValue)
                    return false;
                return cellNum >= topNThreshold.Value;
            }

            case "thisMonth":
            {
                if (blank || !TryParseDate(rawStr, out DateOnly d)) return false;
                return d.Year == refDate.Year && d.Month == refDate.Month;
            }

            case "lastMonth":
            {
                if (blank || !TryParseDate(rawStr, out DateOnly d)) return false;
                int lastM = refDate.Month == 1 ? 12 : refDate.Month - 1;
                int lastY = refDate.Month == 1 ? refDate.Year - 1 : refDate.Year;
                return d.Year == lastY && d.Month == lastM;
            }

            case "before":
            case "after":
            {
                if (!TryParseDate(clause.Value, out DateOnly targetDate)) return true;
                if (blank || !TryParseDate(rawStr, out DateOnly cellDate)) return false;
                return clause.Op == "before" ? cellDate < targetDate : cellDate > targetDate;
            }

            case "between":
            {
                bool hasD1 = TryParseDate(clause.Value, out DateOnly d1);
                bool hasD2 = TryParseDate(clause.ValueTo, out DateOnly d2);
                if ((hasD1 || hasD2) && (clause.Value?.Contains('-') == true || clause.ValueTo?.Contains('-') == true))
                {
                    if (blank || !TryParseDate(rawStr, out DateOnly cellDate)) return false;
                    if (hasD1 && hasD2)
                    {
                        var lo = d1 <= d2 ? d1 : d2;
                        var hi = d1 <= d2 ? d2 : d1;
                        return cellDate >= lo && cellDate <= hi;
                    }
                    return hasD1 ? cellDate >= d1 : cellDate <= d2;
                }

                bool hasN1 = TryParseDecimal(clause.Value, out decimal n1);
                bool hasN2 = TryParseDecimal(clause.ValueTo, out decimal n2);
                if (!hasN1 && !hasN2) return true;
                if (blank || !TryParseDecimal(rawStr, out decimal cellNum)) return false;
                if (hasN1 && hasN2)
                {
                    decimal lo = Math.Min(n1, n2);
                    decimal hi = Math.Max(n1, n2);
                    return cellNum >= lo && cellNum <= hi;
                }
                return hasN1 ? cellNum >= n1 : cellNum <= n2;
            }

            case "gt":
            case "gte":
            case "lt":
            case "lte":
            {
                if (TryParseDate(clause.Value, out DateOnly targetDate) && clause.Value?.Contains('-') == true && !TryParseDecimal(clause.Value, out _))
                {
                    if (blank || !TryParseDate(rawStr, out DateOnly cellDate)) return false;
                    return clause.Op switch
                    {
                        "gt" => cellDate > targetDate,
                        "gte" => cellDate >= targetDate,
                        "lt" => cellDate < targetDate,
                        "lte" => cellDate <= targetDate,
                        _ => true
                    };
                }
                if (!TryParseDecimal(clause.Value, out decimal targetNum)) return true;
                if (blank || !TryParseDecimal(rawStr, out decimal cellNum)) return false;
                return clause.Op switch
                {
                    "gt" => cellNum > targetNum,
                    "gte" => cellNum >= targetNum,
                    "lt" => cellNum < targetNum,
                    "lte" => cellNum <= targetNum,
                    _ => true
                };
            }

            case "eq":
            case "neq":
            {
                string opVal = (clause.Value ?? "").Trim();
                if (opVal.Length == 0) return true;

                // Date equality if both are YYYY-MM-DD dates
                if (opVal.Contains('-') && TryParseDate(opVal, out DateOnly targetDate) && TryParseDate(rawStr, out DateOnly cellDate))
                {
                    bool dateEq = cellDate == targetDate;
                    return clause.Op == "eq" ? dateEq : !dateEq;
                }

                // Numeric equality if not an ID column and both parse as numbers
                if (!IdLikeColumns.Contains(clause.Col) && TryParseDecimal(opVal, out decimal targetNum))
                {
                    if (blank || !TryParseDecimal(rawStr, out decimal cellNum))
                        return clause.Op == "neq";
                    bool numEq = Math.Abs(cellNum - targetNum) <= 0.0001m;
                    return clause.Op == "eq" ? numEq : !numEq;
                }

                string normTarget = NormalizeText(opVal);
                string normTargetDisp = NormalizeText(DisplayLabel(row, clause.Col, opVal, options));
                bool textEq = normRaw == normTarget || normDisp == normTarget ||
                              (normTargetDisp.Length > 0 && (normRaw == normTargetDisp || normDisp == normTargetDisp));
                return clause.Op == "eq" ? textEq : !textEq;
            }

            case "contains":
            case "notContains":
            case "beginsWith":
            case "endsWith":
            {
                string normTarget = NormalizeText(clause.Value);
                if (normTarget.Length == 0) return true;
                return clause.Op switch
                {
                    "contains" => normDisp.Contains(normTarget, StringComparison.Ordinal) ||
                                  normRaw.Contains(normTarget, StringComparison.Ordinal),
                    "notContains" => !normDisp.Contains(normTarget, StringComparison.Ordinal) &&
                                     !normRaw.Contains(normTarget, StringComparison.Ordinal),
                    "beginsWith" => normDisp.StartsWith(normTarget, StringComparison.Ordinal) ||
                                    normRaw.StartsWith(normTarget, StringComparison.Ordinal),
                    "endsWith" => normDisp.EndsWith(normTarget, StringComparison.Ordinal) ||
                                  normRaw.EndsWith(normTarget, StringComparison.Ordinal),
                    _ => true
                };
            }

            default:
                return true;
        }
    }

    static int CompareCells(JsonObject a, JsonObject b, SortClause sort, FilterOptions? options = null)
    {
        string rawA = GetRawString(a, sort.Col).Trim();
        string rawB = GetRawString(b, sort.Col).Trim();
        bool blankA = IsBlank(rawA);
        bool blankB = IsBlank(rawB);
        string dispA = blankA ? "" : DisplayLabel(a, sort.Col, rawA, options);
        string dispB = blankB ? "" : DisplayLabel(b, sort.Col, rawB, options);

        if (!string.IsNullOrEmpty(sort.Tone))
        {
            string? toneA = InferTone(rawA, dispA, options);
            string? toneB = InferTone(rawB, dispB, options);
            bool matchA = string.Equals(toneA, sort.Tone, StringComparison.OrdinalIgnoreCase);
            bool matchB = string.Equals(toneB, sort.Tone, StringComparison.OrdinalIgnoreCase);
            if (matchA != matchB) return matchA ? -1 : 1;
            int rankA = ToneRank(toneA);
            int rankB = ToneRank(toneB);
            if (rankA != rankB) return rankA.CompareTo(rankB);
        }

        if (blankA && blankB) return 0;
        if (blankA) return 1; // Blanks always at bottom (Excel behavior)
        if (blankB) return -1;

        int factor = string.Equals(sort.Dir, "desc", StringComparison.OrdinalIgnoreCase) ? -1 : 1;

        if (!IdLikeColumns.Contains(sort.Col) &&
            TryParseDecimal(rawA, out decimal numA) &&
            TryParseDecimal(rawB, out decimal numB))
        {
            return factor * numA.CompareTo(numB);
        }

        if (rawA.Contains('-') && rawB.Contains('-') &&
            TryParseDate(rawA, out DateOnly dateA) &&
            TryParseDate(rawB, out DateOnly dateB))
        {
            return factor * dateA.CompareTo(dateB);
        }

        return factor * ArabicCompare.Compare(dispA, dispB, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
    }

    static string GetRawString(JsonObject row, string col)
    {
        if (row.TryGetPropertyValue(col, out var node) && node != null)
            return node.ToString();
        // Case-insensitive fallback
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, col, StringComparison.OrdinalIgnoreCase))
                return kv.Value?.ToString() ?? "";
        }
        return "";
    }

    static string DisplayLabel(JsonObject row, string col, string rawValue, FilterOptions? options = null)
    {
        if (rawValue.Length == 0) return "";
        if (options?.LabelOverrides != null && options.LabelOverrides.TryGetValue(rawValue, out string? customLabel))
            return customLabel;
        if (col.Equals("category", StringComparison.OrdinalIgnoreCase) && row.S("categoryAr").Length > 0)
            return row.S("categoryAr");
        var cat = FinancialCore.CategoriesCatalog.FirstOrDefault(c =>
            string.Equals(c.Code, rawValue, StringComparison.OrdinalIgnoreCase));
        if (cat != null) return cat.NameAr;

        if (col is "creditorSponsorId" or "originSponsorId" or "companyId" or "company" or "sponsorId" or "sponsorUnified")
        {
            var sp = FinancialCore.FindSponsor(rawValue);
            if (sp != null) return sp.NameAr;
        }

        if (CommonStatusLabels.TryGetValue(rawValue, out string? label))
            return label;

        return rawValue;
    }

    public static string? InferTone(string? rawValue, string? displayValue = null, FilterOptions? options = null)
    {
        if (options?.ToneOverrides != null)
        {
            foreach (string? candidate in new[] { rawValue, displayValue })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (options.ToneOverrides.TryGetValue(candidate.Trim(), out string? customTone))
                    return customTone;
                string norm = NormalizeText(candidate);
                if (norm.Length > 0 && options.ToneOverrides.TryGetValue(norm, out string? customNormTone))
                    return customNormTone;
            }
        }
        foreach (string? candidate in new[] { rawValue, displayValue })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string norm = NormalizeText(candidate);
            if (norm.Length == 0) continue;
            if (DangerTokens.Contains(norm) || DangerTokens.Contains(candidate)) return "danger";
            if (SuccessTokens.Contains(norm) || SuccessTokens.Contains(candidate)) return "success";
            if (WarningTokens.Contains(norm) || WarningTokens.Contains(candidate)) return "warning";
            if (InfoTokens.Contains(norm) || InfoTokens.Contains(candidate)) return "info";
            if (AccentTokens.Contains(norm) || AccentTokens.Contains(candidate)) return "accent";
            if (NeutralTokens.Contains(norm) || NeutralTokens.Contains(candidate)) return "neutral";
        }
        return null;
    }

    static int ToneRank(string? tone) => tone switch
    {
        "success" => 1,
        "info" => 2,
        "accent" => 3,
        "warning" => 4,
        "danger" => 5,
        "neutral" => 6,
        _ => 99
    };

    static string NormalizeOp(string op) => op.ToLowerInvariant() switch
    {
        "contains" => "contains",
        "notcontains" => "notContains",
        "beginswith" or "startswith" => "beginsWith",
        "endswith" => "endsWith",
        "eq" or "equals" => "eq",
        "neq" or "notequals" => "neq",
        "gt" => "gt",
        "gte" => "gte",
        "lt" => "lt",
        "lte" => "lte",
        "between" => "between",
        "topn" => "topN",
        "before" => "before",
        "after" => "after",
        "thismonth" => "thisMonth",
        "lastmonth" => "lastMonth",
        "in" => "in",
        "color" => "color",
        _ => op
    };

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return TextSearch.Normalize(value);
    }

    static bool IsBlank(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        string t = s.Trim();
        return t == "—" || t == "-" || t == "لا يوجد مصدر بعد";
    }

    static bool TryParseDecimal(string? raw, out decimal result)
    {
        result = 0m;
        if (IsBlank(raw)) return false;
        string digits = NormalizeAsciiDigits(raw!)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("،", "", StringComparison.Ordinal)
            .Replace("ر.س", "", StringComparison.OrdinalIgnoreCase)
            .Replace("SAR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "", StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    static bool TryParseDate(string? raw, out DateOnly result)
    {
        result = default;
        if (IsBlank(raw)) return false;
        string s = NormalizeAsciiDigits(raw!).Trim();
        if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;
        if (s.Length == 7 && DateOnly.TryParseExact(s + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            result = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    static string NormalizeAsciiDigits(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c is >= '٠' and <= '٩') chars[i] = (char)('0' + (c - '٠'));
            else if (c is >= '۰' and <= '۹') chars[i] = (char)('0' + (c - '۰'));
        }
        return new string(chars);
    }
}
