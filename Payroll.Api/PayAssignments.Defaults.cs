using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
namespace Payroll;

public static partial class PayAssignments
{
    /// <summary>
    /// Preview default rider-to-scheme assignments (counts breakdown + draft/choice rows when approved scheme IDs are supplied).
    /// Default rules never auto-approve and never issue a commit fingerprint; they must be saved as drafts and approved by an independent checker.
    /// Keeta work system comes from work-choice form responses in a later task — no Keeta-gate / SYS defaults here.
    /// </summary>
    public static JsonObject PreviewDefaults(
        DbConnection c,
        ClaimsPrincipal actor,
        string app,
        JsonObject input,
        IConfiguration? config = null,
        IHostEnvironment? env = null)
        => PreviewDefaultsInternal(c, null, actor, app, input, config, env);

    static JsonObject PreviewDefaultsInternal(
        DbConnection c,
        DbTransaction? tx,
        ClaimsPrincipal actor,
        string app,
        JsonObject input,
        IConfiguration? config,
        IHostEnvironment? env)
    {
        app = PayPlatforms.App(app);
        Admin(actor, app);
        // NSET1: Ninja-only defaults (Keeta = form responses later; SYS Keeta gates removed).
        J.Need(app == "ninja", "قواعد التوزيع الافتراضي متاحة لنينجا فقط في هذا الإصدار.");
        string period = J.Period(input.Required("period"));
        PayValues.Fields(input, "period", "campaignId", "schemeMap", "enforceCutoff");
        _ = !input.ContainsKey("enforceCutoff") || input.B("enforceCutoff");

        Dictionary<string, JsonObject>? schemesById = null;
        JsonObject? schemeMap = ParseSchemeMap(c, tx, app, period, input["schemeMap"], out schemesById);

        if (config != null && env != null && Operations.SourceMode(config, env) != Operations.SourceStore)
        {
            var monthStart = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var liveRows = new List<JsonObject>();
            using var erp = Operations.OpenErp(config);
            var scope = Operations.LiveScope(c, actor);
            int page = 1;
            const int pageSize = 500;
            while (true)
            {
                var res = NinjaInputsLiveQuery.PaymentMethods(erp, scope, page, pageSize, monthStart, monthEnd);
                liveRows.AddRange(res.Items);
                if (res.Items.Count < pageSize || page * pageSize >= res.Total) break;
                page++;
            }
            // Custody/vehicle for the month from LIVE ERP by iqama — never invent private-car from empty local rows.
            var liveOps = LoadLiveVehicleOps(erp, scope, monthStart, monthEnd);
            return PreviewNinjaLiveRows(c, tx, actor, period, schemeMap, liveRows, liveOps);
        }

        return PreviewNinjaStoreDefaults(c, tx, actor, period, schemeMap);
    }

    /// <summary>Live ERP vehicle assignments overlapping the month, keyed for PayReviewCategory.Derive (riderId = iqama).</summary>
    static Func<string, IEnumerable<JsonObject>> LoadLiveVehicleOps(
        DbConnection erp,
        LiveOperationsQuery.Scope scope,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        string from = monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = monthEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        int size = LiveOperationsQuery.MaxPageSize;
        var assignments = new List<JsonObject>();
        int pageNum = 1;
        while (true)
        {
            int skip = (pageNum - 1) * size;
            var request = new LiveOperationsQuery.PageRequest(pageNum, size, "id", "", skip, size, "", from, to, "", "");
            var page = LiveOperationsQuery.Page(erp, "vehicle_assignment", request, scope, FinancialCore.Sponsors);
            var raw = LiveOperationsSource.Documents(page.Snapshot, FinancialCore.Sponsors, "vehicle_assignment").ToList();
            assignments.AddRange(raw.Select(d => Operations.Project(d, "vehicle_assignment")));
            if (raw.Count < size || pageNum * (long)size >= page.Total) break;
            pageNum++;
        }
        var vehicleIds = assignments.Select(a => a.S("vehicleId")).Where(id => id != "").ToHashSet(StringComparer.Ordinal);
        var vehicles = new List<JsonObject>();
        if (vehicleIds.Count > 0)
        {
            pageNum = 1;
            while (true)
            {
                int skip = (pageNum - 1) * size;
                var request = new LiveOperationsQuery.PageRequest(pageNum, size, "id", "", skip, size, "", "", "", "", "");
                var page = LiveOperationsQuery.Page(erp, "vehicle", request, scope, FinancialCore.Sponsors);
                var raw = LiveOperationsSource.Documents(page.Snapshot, FinancialCore.Sponsors, "vehicle").ToList();
                vehicles.AddRange(raw.Select(d => Operations.Project(d, "vehicle")).Where(v => vehicleIds.Contains(v.S("id"))));
                if (raw.Count < size || pageNum * (long)size >= page.Total) break;
                pageNum++;
            }
        }
        return PayReviewDraft.KindAwareLiveOps([], assignments, vehicles);
    }

    static JsonObject? ParseSchemeMap(
        DbConnection c,
        DbTransaction? tx,
        string app,
        string period,
        JsonNode? rawMap,
        out Dictionary<string, JsonObject>? schemesById)
    {
        schemesById = null;
        if (rawMap is not JsonObject mapObj || mapObj.Count == 0) return null;
        var schemeMap = new JsonObject();
        schemesById = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var p in mapObj)
        {
            if (p.Value == null) continue;
            string id = p.Value.ToString();
            if (id != "")
            {
                var scheme = Scheme(c, tx, id, app, period);
                schemeMap[p.Key] = id;
                schemesById[id] = scheme;
            }
        }
        return schemeMap.Count > 0 ? schemeMap : null;
    }

    /// <summary>
    /// Save default-assignment rows as unapproved drafts requiring independent approval before Resolve.
    /// </summary>
    public static JsonObject SaveDefaultDrafts(
        DbConnection c,
        DbTransaction tx,
        ClaimsPrincipal actor,
        string app,
        JsonObject input,
        IConfiguration? config = null,
        IHostEnvironment? env = null)
    {
        app = PayPlatforms.App(app);
        Admin(actor, app);
        J.Need(input["schemeMap"] is JsonObject map && map.Count > 0, "حدد خريطة الأنظمة المعتمدة أولًا.");
        var preview = PreviewDefaultsInternal(c, tx, actor, app, input, config, env);
        var errors = preview["errors"]!.AsArray();
        J.Need(errors.Count == 0, "توجد أخطاء تمنع حفظ المسودات: " + string.Join(", ", errors.OfType<JsonObject>().Select(e => e.S("code")).Distinct()));
        var rows = preview["rows"]!.AsArray().OfType<JsonObject>().ToList();
        J.Need(rows.Count > 0, "لا توجد صفوف صالحة للحفظ.");

        string actorName = actor.Identity!.Name!;
        string now = DateTimeOffset.UtcNow.ToString("O");
        int drafted = 0, approvedFromSubmission = 0, existing = 0;
        var savedRows = new JsonArray();

        foreach (var row in rows)
        {
            string id = row.S("id");
            if (Store.Get(c, id, Kind, tx) is { } current)
            {
                J.Need(
                    current.S("schemeId") == row.S("schemeId")
                    && FinancialImports.Canonical(current["source"]) == FinancialImports.Canonical(row["source"]),
                    $"تعارض مع تكليف محفوظ للمندوب {row.S("rider")}؛ تغيّر النظام أو المصدر عن المحفوظ مسبقًا.",
                    409);
                existing++;
                savedRows.Add(current.Clone());
                continue;
            }
            var doc = row.Clone();
            if (doc.S("status") == "approved")
            {
                J.Need(doc["source"].S("kind") == "submission" && doc.S("choiceApprovedAt") != "",
                    "الصف المعتمد يحتاج دليل اعتماد اختيار مستقل.");
                doc["importedAt"] = now;
                doc["importedBy"] = actorName;
                Store.Save(c, Kind, doc, tx);
                Store.Audit(c, tx, "pay.assignment_imported", id, actorName);
                approvedFromSubmission++;
            }
            else
            {
                doc["status"] = "draft";
                doc["choiceApprovedAt"] = "";
                doc["createdBy"] = actorName;
                doc["createdAt"] = now;
                Store.Save(c, Kind, doc, tx);
                Store.Audit(c, tx, "pay.assignment_drafted", id, actorName);
                drafted++;
            }
            savedRows.Add(doc.Clone());
        }

        return new JsonObject
        {
            ["app"] = app,
            ["period"] = preview.S("period"),
            ["drafted"] = drafted,
            ["approvedFromSubmission"] = approvedFromSubmission,
            ["existing"] = existing,
            ["rows"] = savedRows
        };
    }

    public static JsonObject Approve(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string app, string id)
    {
        app = PayPlatforms.App(app);
        PaySettings.Scope(actor, "pay.approve", app, true);
        var doc = Store.Get(c, id, Kind, tx)?.Clone() ?? throw new ApiError("تكليف المندوب غير موجود.", 404);
        J.Need(doc.S("app") == app, "التكليف يخص تطبيقًا آخر.");
        AccessPolicy.ValidateApproval(
            actor,
            new JsonObject { ["requestedBy"] = doc.S("createdBy"), ["targetDepartment"] = "FIN", ["requiredRankLevel"] = 1 },
            new JsonObject(),
            "pay.approve",
            c,
            tx,
            "pay.assignment_approved",
            doc);
        if (doc.S("status") == "approved") return doc;
        J.Need(doc.S("status") == "draft", "الاعتماد متاح للمسودات فقط.");
        Scheme(c, tx, doc.S("schemeId"), app, doc.S("period"));
        doc["status"] = "approved";
        doc["approvedBy"] = actor.Identity!.Name;
        doc["approvedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, Kind, doc, tx);
        Store.Audit(c, tx, "pay.assignment_approved", id, actor.Identity!.Name!);
        return doc;
    }

    static bool OverlapsPeriod(string effectiveFrom, string effectiveTo, string periodStart, string periodEnd)
    {
        string from = effectiveFrom.Length >= 10 ? effectiveFrom[..10] : "";
        string to = effectiveTo.Length >= 10 ? effectiveTo[..10] : "";
        if (from != "" && string.CompareOrdinal(from, periodEnd) > 0) return false;
        if (to != "" && string.CompareOrdinal(to, periodStart) < 0) return false;
        return true;
    }

    static string MapNinjaPaymentModel(string raw)
    {
        string v = raw.Trim().ToLowerInvariant();
        if (v is "1" or "payperorder" or "commission" or "عمولة") return "commission";
        if (v is "2" or "salary" or "بالراتب" or "راتب") return "salary";
        return "";
    }

    public static JsonObject PreviewNinjaLiveRows(
        DbConnection c,
        DbTransaction? tx,
        ClaimsPrincipal actor,
        string period,
        JsonObject? schemeMap,
        IEnumerable<JsonObject> liveRows,
        Func<string, IEnumerable<JsonObject>>? liveOps = null)
    {
        var profileToRider = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in Store.List(c, "rider", tx))
        {
            string id = r.S("id");
            if (id == "") continue;
            profileToRider[id] = id;
            if (r.S("erpSourceId") != "") profileToRider.TryAdd(r.S("erpSourceId"), id);
            if (r["erpRiderProfileIds"] is JsonArray ids)
            {
                foreach (var n in ids)
                {
                    string pid = n?.ToString() ?? "";
                    if (pid != "") profileToRider.TryAdd(pid, id);
                }
            }
        }

        var riderModels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var unknownProfileIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in liveRows)
        {
            string rawId = row.S("openActualRiderProfileId");
            if (rawId == "")
            {
                if (row.S("openAssignmentId") != "") unknownProfileIds.Add("");
                continue;
            }
            if (!profileToRider.TryGetValue(rawId, out string? rid))
            {
                unknownProfileIds.Add(rawId);
                continue;
            }
            string mapped = MapNinjaPaymentModel(row.S("payScheme"));
            if (mapped == "") mapped = "unmapped";
            riderModels.TryAdd(rid, []);
            riderModels[rid].Add(mapped);
        }

        return FinalizeNinjaPreview(c, tx, actor, period, schemeMap, "erp_live", riderModels, unknownProfileIds, liveOps);
    }

    static JsonObject PreviewNinjaStoreDefaults(
        DbConnection c,
        DbTransaction? tx,
        ClaimsPrincipal actor,
        string period,
        JsonObject? schemeMap)
    {
        var riderModels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
        var end = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var ninjaAccounts = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var pa in Store.List(c, "platform_account", tx))
        {
            if (string.Equals(pa.S("platformCode"), "NINJA", StringComparison.OrdinalIgnoreCase))
                ninjaAccounts[pa.S("id")] = pa;
        }

        foreach (var raa in Store.List(c, "rider_account_assignment", tx))
        {
            if (!ninjaAccounts.TryGetValue(raa.S("platformAccountId"), out var pa)) continue;
            string rid = raa.S("riderId");
            if (rid == "" || !OverlapsPeriod(raa.S("effectiveFrom"), raa.S("effectiveTo"), start, end)) continue;
            string mapped = MapNinjaPaymentModel(raa.S("paymentModelCode", pa.S("paymentModelCode")));
            if (mapped == "") mapped = "unmapped";
            riderModels.TryAdd(rid, []);
            riderModels[rid].Add(mapped);
        }

        foreach (var pa in ninjaAccounts.Values)
        {
            if (pa["assignments"] is not JsonArray nested) continue;
            foreach (var a in nested.OfType<JsonObject>())
            {
                string rid = a.S("riderId");
                if (rid == "" || !OverlapsPeriod(a.S("EffectiveFrom", a.S("effectiveFrom")), a.S("EffectiveTo", a.S("effectiveTo")), start, end)) continue;
                string mapped = MapNinjaPaymentModel(a.S("PaymentModel", a.S("paymentModelCode", pa.S("paymentModelCode"))));
                if (mapped == "") mapped = "unmapped";
                riderModels.TryAdd(rid, []);
                riderModels[rid].Add(mapped);
            }
        }

        return FinalizeNinjaPreview(c, tx, actor, period, schemeMap, "store", riderModels, []);
    }

    static JsonObject FinalizeNinjaPreview(
        DbConnection c,
        DbTransaction? tx,
        ClaimsPrincipal actor,
        string period,
        JsonObject? schemeMap,
        string sourceLabel,
        Dictionary<string, HashSet<string>> riderModels,
        HashSet<string> unknownProfileIds,
        Func<string, IEnumerable<JsonObject>>? liveOps = null)
    {
        int commissionCount = 0, salaryCount = 0, conflictCount = 0, unmappedCount = 0, unknownRiderCount = 0, totalRiders = 0;
        int sponsoredCount = 0, externalCount = 0, companyVehicleCount = 0, privateVehicleCount = 0, unknownCategoryCount = 0;
        var categoryCounts = new JsonObject();
        foreach (string cat in PaySettings.Categories) categoryCounts[cat] = 0;
        var errors = new JsonArray();
        var rows = new JsonArray();

        foreach (string pid in unknownProfileIds.OrderBy(x => x, StringComparer.Ordinal))
        {
            unknownRiderCount++;
            errors.Add(new JsonObject { ["code"] = "rider_not_found_in_system", ["profileId"] = pid });
        }

        foreach (var (rid, models) in riderModels.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var person = Store.Get(c, rid, "rider", tx);
            if (person == null)
            {
                unknownRiderCount++;
                errors.Add(new JsonObject { ["code"] = "rider_not_found_in_system", ["rider"] = rid });
                continue;
            }
            if (!ErpRiderClassification.ListedAsRider(person)) continue;
            FinancialScopes.DemandRider(actor, person);

            totalRiders++;
            if (models.Count > 1)
            {
                conflictCount++;
                errors.Add(new JsonObject { ["code"] = "conflicting_ninja_payment_model", ["rider"] = rid });
                continue;
            }
            string model = models.Single();
            if (model == "unmapped")
            {
                unmappedCount++;
                errors.Add(new JsonObject { ["code"] = "missing_ninja_payment_model", ["rider"] = rid });
                continue;
            }
            if (model == "commission") commissionCount++;
            else salaryCount++;

            // Vehicle custody for the month: liveOps = LIVE ERP by iqama when SourceMode=live (no empty-local → private-car invent).
            string category = PayReviewCategory.Derive(c, tx, person, period, liveOps);
            if (PaySettings.Categories.Contains(category))
            {
                categoryCounts[category] = categoryCounts.N(category) + 1;
                if (category.StartsWith("sponsored_", StringComparison.Ordinal)) sponsoredCount++;
                else if (category.StartsWith("external_", StringComparison.Ordinal)) externalCount++;
                if (category.Contains("private", StringComparison.Ordinal)) privateVehicleCount++;
                else companyVehicleCount++;
            }
            else
            {
                // Category from custody/kafala is informational for Ninja scheme pick (commission/salary);
                // missing vehicle data must not block draft assignment from Omar payment method.
                unknownCategoryCount++;
            }

            if (schemeMap != null)
            {
                string schemeId = schemeMap.S(model);
                if (schemeId == "")
                {
                    errors.Add(new JsonObject { ["code"] = "missing_scheme_for_model", ["rider"] = rid, ["model"] = model });
                    continue;
                }
                string id = "pay-assignment:" + Hash("ninja\n" + period + "\n" + rid + "\nerp_payment_model\n" + model + "\n1");
                rows.Add(new JsonObject
                {
                    ["id"] = id,
                    ["rider"] = rid,
                    ["app"] = "ninja",
                    ["schemeId"] = schemeId,
                    ["period"] = period,
                    ["companyId"] = person.S("financeSponsorId", person.S("sponsorId")),
                    ["status"] = "draft",
                    ["choiceApprovedAt"] = "",
                    ["source"] = new JsonObject
                    {
                        ["kind"] = "erp_payment_model",
                        ["fieldId"] = "PaymentModel",
                        ["choice"] = model,
                        ["category"] = category,
                        ["period"] = period
                    }
                });
            }
        }

        bool hasErrors = errors.Count > 0;
        // Every Ninja row comes from an ERP payment model (a default rule), so it stays draft and never gets a commit fingerprint.
        return new JsonObject
        {
            ["app"] = "ninja",
            ["period"] = period,
            ["source"] = sourceLabel == "erp_live" ? "omar_erp_payment_model" : "synced_erp_payment_model",
            ["counts"] = new JsonObject
            {
                ["eligibleRiders"] = totalRiders,
                ["commission"] = commissionCount,
                ["salary"] = salaryCount,
                ["sponsoredKafala"] = sponsoredCount,
                ["external"] = externalCount,
                ["companyVehicleCustody"] = companyVehicleCount,
                ["privateVehicle"] = privateVehicleCount,
                ["unknownCategory"] = unknownCategoryCount,
                ["conflictingPaymentModel"] = conflictCount,
                ["unmapped"] = unmappedCount,
                ["unknownRider"] = unknownRiderCount,
                ["byCategory"] = categoryCounts
            },
            ["rows"] = !hasErrors && schemeMap != null ? rows : new JsonArray(),
            ["canCommit"] = false,
            ["errors"] = errors,
            ["fingerprint"] = ""
        };
    }
}
