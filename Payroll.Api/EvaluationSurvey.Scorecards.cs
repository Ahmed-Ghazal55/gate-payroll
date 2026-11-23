using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    const int LowSampleThreshold = 5;
    const int IdentityMinReplies = 3;
    const string ConfidenceNoteAr =
        "عينة صغيرة: النتائج ذات عدد ردود أقل من 5 تُعرض مع تنبيه ثقة منخفض، ولا تُكشف هوية المندوب في التجميعات عندما يقل العدد عن 3.";

    public sealed record ScorecardFilter(string? City = null, string? App = null, string? Department = null, string? SupervisorId = null, string? NationalityId = null);

    static ScorecardFilter F(string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null) =>
        new(city, app, department, supervisorId, nationalityId);

    static List<JsonObject> FilteredReplies(DbConnection c, ClaimsPrincipal user, string id, ScorecardFilter filter)
    {
        var rows = Responses(c, user, id);
        if (!string.IsNullOrEmpty(filter.City)) rows = rows.Where(r => r.S("city") == filter.City).ToList();
        if (!string.IsNullOrEmpty(filter.NationalityId))
            rows = rows.Where(r => r.S("nationalityId") == filter.NationalityId || r.S("nationality") == filter.NationalityId).ToList();
        // Same rule as the replies table: keep a reply only when workApps contains the platform.
        if (!string.IsNullOrEmpty(filter.App))
            rows = rows.Where(r => Strings(r, "workApps").Contains(filter.App!)).ToList();
        if (!string.IsNullOrEmpty(filter.SupervisorId))
            rows = rows.Where(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>().Any(a => a.S("staffId") == filter.SupervisorId)).ToList();
        if (!string.IsNullOrEmpty(filter.Department))
        {
            var deptIds = Round(c, id)["staff"]!.AsArray().OfType<JsonObject>()
                .Where(s => Strings(s, "departments").Contains(filter.Department!)).Select(s => s.S("id")).ToHashSet();
            // Count only replies that rated someone in the department (skipped cards do not count).
            rows = rows.Where(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>()
                .Any(a => deptIds.Contains(a.S("staffId")) && a.S("reason") == "")).ToList();
        }
        return rows;
    }

    static bool StaffMatches(JsonObject staff, ScorecardFilter filter)
    {
        if (!string.IsNullOrEmpty(filter.SupervisorId) && staff.S("id") != filter.SupervisorId) return false;
        if (!string.IsNullOrEmpty(filter.City) && !ServesCity(staff, filter.City!)) return false;
        if (!string.IsNullOrEmpty(filter.App) && !Strings(staff, "apps").Contains(filter.App!)) return false;
        if (!string.IsNullOrEmpty(filter.Department) && !Strings(staff, "departments").Contains(filter.Department!)) return false;
        return true;
    }

    static decimal? Round2(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2, MidpointRounding.AwayFromZero);
    }

    static JsonObject? PreviousRound(DbConnection c, JsonObject current)
    {
        string created = current.S("createdAt");
        string id = current.S("id");
        bool sqlite = c is Microsoft.Data.Sqlite.SqliteConnection;
        string createdExpr = sqlite ? "json_extract(body,'$.createdAt')" : "CONVERT(nvarchar(40),JSON_VALUE(body,'$.createdAt'))";
        string sql = sqlite
            ? $"SELECT body FROM records WHERE kind=@kind AND id<>@id AND {createdExpr}<@created ORDER BY {createdExpr} DESC LIMIT 1"
            : $"SELECT TOP (1) body FROM records WHERE kind=@kind AND id<>@id AND {createdExpr}<@created ORDER BY {createdExpr} DESC";
        using var cmd = Store.Command(c, null, sql, ("@kind", RoundKind), ("@id", id), ("@created", created));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? System.Text.Json.Nodes.JsonNode.Parse(reader.GetString(0))!.AsObject() : null;
    }

    static Dictionary<string, decimal> PreviousStaffAverages(DbConnection c, ClaimsPrincipal user, JsonObject? previous, ScorecardFilter filter)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (previous == null) return result;
        var rows = FilteredReplies(c, user, previous.S("id"), filter);
        foreach (var g in rows.SelectMany(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>())
            .Where(a => a.S("reason") == "").GroupBy(a => a.S("staffId")))
        {
            var avg = Round2(g.Select(a => StaffDimensions.Average(d => a["ratings"]!.N(d))));
            if (avg != null) result[g.Key] = avg.Value;
        }
        return result;
    }

    static JsonObject DrillDown(string target, ScorecardFilter filter, string? supervisorId = null, string? app = null, string? city = null) => new()
    {
        ["target"] = target,
        ["city"] = city ?? filter.City ?? "",
        ["app"] = app ?? filter.App ?? "",
        ["department"] = filter.Department ?? "",
        ["supervisorId"] = supervisorId ?? filter.SupervisorId ?? "",
        ["nationalityId"] = filter.NationalityId ?? ""
    };

    public static JsonObject SupervisorScorecard(DbConnection c, ClaimsPrincipal user, string id,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        var filter = F(city, app, department, supervisorId, nationalityId);
        Demand(user);
        var round = Round(c, id);
        var rows = FilteredReplies(c, user, id, filter);
        var prevAvgs = PreviousStaffAverages(c, user, PreviousRound(c, round), filter);
        var staffCards = round["staff"]!.AsArray().OfType<JsonObject>().Where(s => StaffMatches(s, filter)).ToList();
        var answers = rows.SelectMany(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>()).ToList();

        var drafts = staffCards.SelectMany(staff =>
        {
            string sid = staff.S("id");
            var cards = answers.Where(a => a.S("staffId") == sid).ToList();
            var rated = cards.Where(a => a.S("reason") == "").ToList();
            int n = rated.Count;
            int complaints = rated.Count(a => a.B("complaint"));
            var overall = Round2(rated.Select(a => StaffDimensions.Average(d => a["ratings"]!.N(d))));
            var depts = Strings(staff, "departments");
            if (!string.IsNullOrEmpty(filter.Department))
                depts = depts.Where(d => d == filter.Department).ToArray();
            if (depts.Length == 0) depts = [""];
            decimal? change = overall != null && prevAvgs.TryGetValue(sid, out var prev) ? Math.Round(overall.Value - prev, 2, MidpointRounding.AwayFromZero) : null;
            var dimensions = StaffDimensions.Select(d =>
            {
                var dimAvg = Round2(rated.Select(a => a["ratings"]!.N(d)));
                return new JsonObject
                {
                    ["key"] = d,
                    ["count"] = n,
                    ["average"] = dimAvg == null ? null : JsonValue.Create(dimAvg.Value)
                };
            }).ToList();
            // One card row per department membership so rank uses the filtered department.
            return depts.Select(dept => new
            {
                Staff = staff,
                Sid = sid,
                Dept = dept,
                ReplyCount = n,
                Complaints = complaints,
                Average = overall,
                Change = change,
                Dimensions = dimensions
            });
        }).ToList();

        // Dense rank within each department among those with an average (higher = better).
        var ranks = new Dictionary<(string Sid, string Dept), int>();
        foreach (var deptGroup in drafts.Where(d => d.Average != null).GroupBy(d => d.Dept))
        {
            int rank = 0; decimal? last = null;
            foreach (var row in deptGroup.OrderByDescending(d => d.Average!.Value).ThenBy(d => d.Staff.S("name"), StringComparer.Ordinal))
            {
                if (last == null || row.Average != last) { rank++; last = row.Average; }
                ranks[(row.Sid, row.Dept)] = rank;
            }
        }

        var items = drafts.Select(d =>
        {
            int n = d.ReplyCount;
            bool identityVisible = n >= IdentityMinReplies;
            var item = new JsonObject
            {
                ["staffId"] = d.Sid,
                ["name"] = d.Staff.S("name"),
                ["department"] = d.Dept,
                ["replyCount"] = n,
                ["complaintCount"] = d.Complaints,
                ["complaintRate"] = n == 0 ? null : JsonValue.Create(Math.Round((decimal)d.Complaints / n, 2, MidpointRounding.AwayFromZero)),
                ["average"] = d.Average == null ? null : JsonValue.Create(d.Average.Value),
                ["rankInDepartment"] = ranks.TryGetValue((d.Sid, d.Dept), out var rk) ? rk : null,
                ["changeVsPrevious"] = d.Change == null ? null : JsonValue.Create(d.Change.Value),
                ["lowSample"] = n > 0 && n < LowSampleThreshold,
                ["identityVisible"] = identityVisible,
                ["dimensions"] = Array(d.Dimensions)
            };
            if (identityVisible) item["drillDown"] = DrillDown("replies", filter, supervisorId: d.Sid);
            return item;
        }).OrderBy(i => i.S("department"), StringComparer.Ordinal).ThenBy(i => i.S("name"), StringComparer.Ordinal).ToList();

        bool anyLow = items.Any(i => i.B("lowSample"));
        return new()
        {
            ["roundId"] = id,
            ["totalResponses"] = rows.Count,
            ["confidenceNoteAr"] = anyLow || rows.Count < LowSampleThreshold ? ConfidenceNoteAr : "",
            ["labelsAr"] = new JsonObject
            {
                ["title"] = "بطاقة أداء المشرف / الموظف",
                ["average"] = "المتوسط العام",
                ["replyCount"] = "عدد التقييمات",
                ["complaintRate"] = "معدل الشكاوى",
                ["rankInDepartment"] = "الترتيب داخل القسم",
                ["changeVsPrevious"] = "التغير عن الجولة السابقة",
                ["lowSample"] = "عينة صغيرة"
            },
            ["items"] = Array(items),
            ["allowedActions"] = AccessPolicy.Has(user, ExportPermission) ? new JsonArray("export") : new JsonArray()
        };
    }

    public static JsonObject AppScorecard(DbConnection c, ClaimsPrincipal user, string id,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        var filter = F(city, app, department, supervisorId, nationalityId);
        Demand(user);
        var round = Round(c, id);
        var rows = FilteredReplies(c, user, id, filter);
        var apps = Strings(round, "apps").Where(a => string.IsNullOrEmpty(filter.App) || a == filter.App).ToList();
        var answers = rows.SelectMany(r => r["appAnswers"]!.AsArray().OfType<JsonObject>()).ToList();
        var items = apps.Select(platform =>
        {
            var cards = answers.Where(a => a.S("app") == platform).ToList();
            var rated = cards.Where(a => a.S("reason") == "").ToList();
            int n = rated.Count;
            var dist = Enumerable.Range(1, 5).Select(stars => new JsonObject
            {
                ["stars"] = stars,
                ["count"] = rated.Count(a => a.N("overall") == stars)
            }).ToList();
            decimal high = n == 0 ? 0 : 100m * rated.Count(a => a.N("overall") >= 4) / n;
            decimal low = n == 0 ? 0 : 100m * rated.Count(a => a.N("overall") <= 2) / n;
            var factorRows = Factors.Select(factor =>
            {
                var vals = rated.Where(a => a["factorRatings"]?[factor] is JsonValue).Select(a => a["factorRatings"]!.N(factor)).ToList();
                return new JsonObject
                {
                    ["key"] = factor,
                    ["count"] = vals.Count,
                    ["average"] = vals.Count == 0 ? null : JsonValue.Create(Math.Round(vals.Average(), 2, MidpointRounding.AwayFromZero))
                };
            }).Where(f => f.N("count") > 0).ToList();
            var ordered = factorRows.OrderByDescending(f => f.N("average")).ToList();
            int topN = Math.Min(3, (ordered.Count + 1) / 2);
            int bottomN = Math.Min(3, ordered.Count / 2);
            var top = ordered.Take(topN).ToList();
            var topKeys = top.Select(f => f.S("key")).ToHashSet(StringComparer.Ordinal);
            var bottom = ordered.OrderBy(f => f.N("average")).Where(f => !topKeys.Contains(f.S("key"))).Take(bottomN).ToList();
            bool identityVisible = n >= IdentityMinReplies;
            var item = new JsonObject
            {
                ["app"] = platform,
                ["replyCount"] = n,
                ["average"] = Round2(rated.Select(a => a.N("overall"))) is { } avg ? JsonValue.Create(avg) : null,
                ["distribution"] = Array(dist),
                ["netScore"] = n == 0 ? null : JsonValue.Create(Math.Round(high - low, 2, MidpointRounding.AwayFromZero)),
                ["topFactors"] = Array(top),
                ["bottomFactors"] = Array(bottom),
                ["lowSample"] = n > 0 && n < LowSampleThreshold,
                ["identityVisible"] = identityVisible
            };
            if (identityVisible) item["drillDown"] = DrillDown("replies", filter, app: platform);
            return item;
        }).ToList();
        bool anyLow = items.Any(i => i.B("lowSample"));
        return new()
        {
            ["roundId"] = id,
            ["totalResponses"] = rows.Count,
            ["confidenceNoteAr"] = anyLow || rows.Count < LowSampleThreshold ? ConfidenceNoteAr : "",
            ["labelsAr"] = new JsonObject
            {
                ["title"] = "بطاقة أداء المنصة",
                ["average"] = "متوسط النجوم",
                ["netScore"] = "صافي التقييم (%٤–٥ ناقص %١–٢)",
                ["topFactors"] = "أعلى العوامل",
                ["bottomFactors"] = "أدنى العوامل",
                ["lowSample"] = "عينة صغيرة"
            },
            ["items"] = Array(items),
            ["allowedActions"] = AccessPolicy.Has(user, ExportPermission) ? new JsonArray("export") : new JsonArray()
        };
    }

    public static JsonObject SupervisorDimensionHeatmap(DbConnection c, ClaimsPrincipal user, string id,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        var filter = F(city, app, department, supervisorId, nationalityId);
        Demand(user);
        var round = Round(c, id);
        var rows = FilteredReplies(c, user, id, filter);
        var staffCards = round["staff"]!.AsArray().OfType<JsonObject>().Where(s => StaffMatches(s, filter)).ToList();
        var answers = rows.SelectMany(r => r["staffAnswers"]!.AsArray().OfType<JsonObject>()).ToList();
        var cells = new List<JsonObject>();
        foreach (var staff in staffCards)
        {
            string sid = staff.S("id");
            var rated = answers.Where(a => a.S("staffId") == sid && a.S("reason") == "").ToList();
            int n = rated.Count;
            foreach (var d in StaffDimensions)
            {
                bool identityVisible = n >= IdentityMinReplies;
                var cell = new JsonObject
                {
                    ["staffId"] = sid,
                    ["name"] = staff.S("name"),
                    ["dimension"] = d,
                    ["replyCount"] = n,
                    ["average"] = n == 0 ? null : JsonValue.Create(Round2(rated.Select(a => a["ratings"]!.N(d)))),
                    ["lowSample"] = n > 0 && n < LowSampleThreshold,
                    ["identityVisible"] = identityVisible
                };
                if (identityVisible) cell["drillDown"] = DrillDown("replies", filter, supervisorId: sid);
                cells.Add(cell);
            }
        }
        bool anyLow = cells.Any(i => i.B("lowSample"));
        return new()
        {
            ["roundId"] = id,
            ["totalResponses"] = rows.Count,
            ["dimensions"] = Array(StaffDimensions),
            ["confidenceNoteAr"] = anyLow || rows.Count < LowSampleThreshold ? ConfidenceNoteAr : "",
            ["labelsAr"] = new JsonObject { ["title"] = "خريطة حرارية: مشرف × بُعد" },
            ["cells"] = Array(cells),
            ["allowedActions"] = AccessPolicy.Has(user, ExportPermission) ? new JsonArray("export") : new JsonArray()
        };
    }

    public static JsonObject CityAppMatrix(DbConnection c, ClaimsPrincipal user, string id,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        var filter = F(city, app, department, supervisorId, nationalityId);
        Demand(user);
        var round = Round(c, id);
        var rows = FilteredReplies(c, user, id, filter);
        var cities = rows.Select(r => r.S("city")).Where(x => x != "").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (!string.IsNullOrEmpty(filter.City)) cities = cities.Where(x => x == filter.City).ToList();
        var apps = Strings(round, "apps").Where(a => string.IsNullOrEmpty(filter.App) || a == filter.App).ToList();
        var cells = new List<JsonObject>();
        foreach (var cityName in cities)
        foreach (var platform in apps)
        {
            var rated = rows.Where(r => r.S("city") == cityName)
                .SelectMany(r => r["appAnswers"]!.AsArray().OfType<JsonObject>())
                .Where(a => a.S("app") == platform && a.S("reason") == "").ToList();
            int n = rated.Count;
            bool identityVisible = n >= IdentityMinReplies;
            var cell = new JsonObject
            {
                ["city"] = cityName,
                ["app"] = platform,
                ["replyCount"] = n,
                ["average"] = Round2(rated.Select(a => a.N("overall"))) is { } avg ? JsonValue.Create(avg) : null,
                ["lowSample"] = n > 0 && n < LowSampleThreshold,
                ["identityVisible"] = identityVisible
            };
            if (identityVisible) cell["drillDown"] = DrillDown("replies", filter, app: platform, city: cityName);
            cells.Add(cell);
        }
        bool anyLow = cells.Any(i => i.B("lowSample"));
        return new()
        {
            ["roundId"] = id,
            ["totalResponses"] = rows.Count,
            ["cities"] = Array(cities),
            ["apps"] = Array(apps),
            ["confidenceNoteAr"] = anyLow || rows.Count < LowSampleThreshold ? ConfidenceNoteAr : "",
            ["labelsAr"] = new JsonObject { ["title"] = "مصفوفة المدينة × المنصة" },
            ["cells"] = Array(cells),
            ["allowedActions"] = AccessPolicy.Has(user, ExportPermission) ? new JsonArray("export") : new JsonArray()
        };
    }

    public static byte[] ExportSupervisorScorecard(DbConnection c, ClaimsPrincipal user, string id,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        AccessPolicy.Demand(user, ExportPermission);
        var card = SupervisorScorecard(c, user, id, city, app, department, supervisorId, nationalityId);
        var columns = new List<ExcelColumn>
        {
            new("الموظف"), new("القسم"), new("عدد التقييمات", true), new("المتوسط", true),
            new("الشكاوى", true), new("معدل الشكاوى", true), new("الترتيب في القسم", true),
            new("التغير عن السابقة", true), new("عينة صغيرة")
        };
        columns.AddRange(StaffDimensions.Select(d => new ExcelColumn(d, true)));
        var rows = new List<object?[]>();
        foreach (var item in card["items"]!.AsArray().OfType<JsonObject>())
        {
            var values = new List<object?>
            {
                item.S("name"), item.S("department"), item.N("replyCount"),
                item["average"] == null ? null : item.N("average"),
                item.N("complaintCount"),
                item["complaintRate"] == null ? null : item.N("complaintRate"),
                item["rankInDepartment"] == null ? null : item.N("rankInDepartment"),
                item["changeVsPrevious"] == null ? null : item.N("changeVsPrevious"),
                item.B("lowSample") ? "نعم" : "لا"
            };
            values.AddRange(StaffDimensions.Select(d =>
            {
                var dim = item["dimensions"]!.AsArray().OfType<JsonObject>().SingleOrDefault(x => x.S("key") == d);
                return (object?)(dim?["average"] == null ? null : dim.N("average"));
            }));
            rows.Add(values.ToArray());
        }
        return ExportWorkbook(columns, rows);
    }

    public static byte[] ExportScorecard(DbConnection c, ClaimsPrincipal user, string id, string kind,
        string? city = null, string? app = null, string? department = null, string? supervisorId = null, string? nationalityId = null)
    {
        AccessPolicy.Demand(user, ExportPermission);
        return kind switch
        {
            "supervisors" => ExportSupervisorScorecard(c, user, id, city, app, department, supervisorId, nationalityId),
            "apps" => ExportAppScorecard(c, user, id, city, app, department, supervisorId, nationalityId),
            "heatmap" => ExportHeatmap(c, user, id, city, app, department, supervisorId, nationalityId),
            "city-app" => ExportCityApp(c, user, id, city, app, department, supervisorId, nationalityId),
            _ => throw new ApiError("نوع بطاقة غير معروف.", 404)
        };
    }

    static byte[] ExportAppScorecard(DbConnection c, ClaimsPrincipal user, string id,
        string? city, string? app, string? department, string? supervisorId, string? nationalityId)
    {
        var card = AppScorecard(c, user, id, city, app, department, supervisorId, nationalityId);
        var columns = new List<ExcelColumn>
        {
            new("المنصة"), new("عدد التقييمات", true), new("متوسط النجوم", true), new("صافي التقييم", true),
            new("نجمة ١", true), new("نجمة ٢", true), new("نجمة ٣", true), new("نجمة ٤", true), new("نجمة ٥", true),
            new("أعلى عامل"), new("أدنى عامل"), new("عينة صغيرة")
        };
        var rows = card["items"]!.AsArray().OfType<JsonObject>().Select(item =>
        {
            var dist = item["distribution"]!.AsArray().OfType<JsonObject>().ToDictionary(d => (int)d.N("stars"), d => d.N("count"));
            return new object?[]
            {
                item.S("app"), item.N("replyCount"),
                item["average"] == null ? null : item.N("average"),
                item["netScore"] == null ? null : item.N("netScore"),
                dist.GetValueOrDefault(1), dist.GetValueOrDefault(2), dist.GetValueOrDefault(3), dist.GetValueOrDefault(4), dist.GetValueOrDefault(5),
                item["topFactors"]!.AsArray().OfType<JsonObject>().FirstOrDefault()?.S("key") ?? "",
                item["bottomFactors"]!.AsArray().OfType<JsonObject>().FirstOrDefault()?.S("key") ?? "",
                item.B("lowSample") ? "نعم" : "لا"
            };
        }).ToList();
        return ExportWorkbook(columns, rows);
    }

    static byte[] ExportHeatmap(DbConnection c, ClaimsPrincipal user, string id,
        string? city, string? app, string? department, string? supervisorId, string? nationalityId)
    {
        var card = SupervisorDimensionHeatmap(c, user, id, city, app, department, supervisorId, nationalityId);
        var columns = new List<ExcelColumn> { new("الموظف"), new("البُعد"), new("عدد التقييمات", true), new("المتوسط", true), new("عينة صغيرة") };
        var rows = card["cells"]!.AsArray().OfType<JsonObject>().Select(cell => new object?[]
        {
            cell.S("name"), cell.S("dimension"), cell.N("replyCount"),
            cell["average"] == null ? null : cell.N("average"),
            cell.B("lowSample") ? "نعم" : "لا"
        }).ToList();
        return ExportWorkbook(columns, rows);
    }

    static byte[] ExportCityApp(DbConnection c, ClaimsPrincipal user, string id,
        string? city, string? app, string? department, string? supervisorId, string? nationalityId)
    {
        var card = CityAppMatrix(c, user, id, city, app, department, supervisorId, nationalityId);
        var columns = new List<ExcelColumn> { new("المدينة"), new("المنصة"), new("عدد التقييمات", true), new("المتوسط", true), new("عينة صغيرة") };
        var rows = card["cells"]!.AsArray().OfType<JsonObject>().Select(cell => new object?[]
        {
            cell.S("city"), cell.S("app"), cell.N("replyCount"),
            cell["average"] == null ? null : cell.N("average"),
            cell.B("lowSample") ? "نعم" : "لا"
        }).ToList();
        return ExportWorkbook(columns, rows);
    }
}
