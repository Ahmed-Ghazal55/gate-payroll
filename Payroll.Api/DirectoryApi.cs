using System.Text.Json.Nodes;
namespace Payroll;

public static class RiderDirectory
{
    public static string Phone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string n = new(value.Where(c => !char.IsWhiteSpace(c) && c is not '(' and not ')' and not '-').ToArray());
        if (n.StartsWith("00", StringComparison.Ordinal)) n = "+" + n[2..];
        if (n.StartsWith("05", StringComparison.Ordinal)) n = "+966" + n[1..];
        if (n.StartsWith("966", StringComparison.Ordinal)) n = "+" + n;
        J.Need(System.Text.RegularExpressions.Regex.IsMatch(n, @"^\+[1-9][0-9]{7,14}$"), "الجوال يجب أن يكون رقمًا دوليًا صحيحًا E.164.");
        return n;
    }

    public static string State(string value) => value switch
    {
        "active" or "نشط" or "Active" => "Active",
        "Inactive" => "Inactive",
        "في إجازة" or "OnLeave" => "OnLeave",
        "مؤرشف" or "هروب / انقطاع" or "Archived" => "Archived",
        "" or "مسودة" or "needs_review" or "NeedsReview" => "NeedsReview",
        _ => throw new ApiError("حالة المندوب غير صالحة.")
    };

    public static JsonObject Normalize(JsonObject input, JsonObject? existing = null)
    {
        var r = existing?.Clone() ?? new JsonObject();
        string Value(string key, string legacy = "") => input.ContainsKey(key) ? input.S(key) : legacy != "" && input.ContainsKey(legacy) ? input.S(legacy) : r.S(key);
        string iqama = DirectoryProfiles.Iqama(input.Required("iqama"));
        r["id"] = iqama;
        r["iqama"] = iqama;
        r["name"] = DirectoryProfiles.Name(input.Required("name"));
        r["phone"] = Phone(Value("phone"));
        r["nationality"] = Value("nationality");
        r["sponsor"] = Value("sponsor");
        r["city"] = Value("city");

        // ربط تلقائي بمعرف السجل التجاري الرسمي في نظام البوابة عند توفر اسم الكفيل
        if (!string.IsNullOrWhiteSpace(r.S("sponsor")))
        {
            var sp = FinancialCore.FindSponsor(r.S("sponsor"));
            if(sp!=null)r["sponsorId"] = sp.Id;
        }
        else if (input.ContainsKey("sponsorId") && !string.IsNullOrWhiteSpace(input.S("sponsorId")))
        {
            var sp = FinancialCore.ResolveSponsor(input.S("sponsorId"));
            r["sponsorId"] = sp.Id;
            r["sponsor"] = sp.NameAr;
        }

        string engagement = Value("engagementType", "engagement_type");
        engagement = engagement switch
        {
            "SponsoredInternal" or "كفالة الشركة" => "SponsoredInternal",
            "External" or "OutsideRider" or "خارجي" => "External",
            "" => "",
            _ => throw new ApiError("نوع العلاقة غير صالح.")
        };
        r["engagementType"] = engagement;

        string role = Value("operationalRole", "operational_role");
        role = role switch
        {
            "Car" or "سيارة" => "Car",
            "Motorcycle" or "دباب" or "دراجة نارية" => "Motorcycle",
            "" => "",
            _ => ""
        };
        r["operationalRole"] = role;
        r["drivingLicense"] = Value("drivingLicense", "driving_license");
        r["platforms"] = Value("platforms");
        r["iban"] = Value("iban");
        r["status"] = State(Value("status"));
        foreach (string k in new[] { "nationality", "sponsor", "city", "drivingLicense", "platforms" })
            J.Need(r.S(k).Length <= 200, "قيمة وصفية طويلة جدًا.");
        r["policyId"] = Value("policyId");
        if(input.ContainsKey("financeSponsorId"))r["financeSponsorId"]=input.S("financeSponsorId")==""?"":FinancialCore.ResolveSponsor(input.S("financeSponsorId")).Id;
        if(existing?.B("debtOnly")==true&&r.S("status")=="Active"){r["debtOnly"]=false;r["personRole"]="rider";r["isDeliveryRider"]=true;}
        r["schemaVersion"] = 2;
        return r;
    }

    public static string? FindLocalBaselineFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "riders-baseline.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "riders-baseline.json"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "data", "riders-baseline.json")),
            "/home/ghazal/Projects/pro win/Projectes/08_MultiPlatform_Payroll_&_Rider_Ledger_App/04_Payroll_&_Ledger_App_Source/data/riders-baseline.json"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        // 3. تشخيص جودة واكتمال بيانات المناديب (Data Completeness & Missing Fields Report)
        api.MapGet("/riders/data-quality", () => store.Read(c =>
        {
            var riders = Store.List(c, "rider").Where(ErpRiderClassification.ListedAsRider).ToList();
            string? baselinePath = FindLocalBaselineFile();
            return new
            {
                totalRiders = riders.Count,
                localBaselineAvailable = baselinePath != null,
                missingPhoneCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("phone"))),
                missingCityCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("city"))),
                missingSponsorCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("sponsor"))),
                missingNationalityCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("nationality"))),
                missingPolicyCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("policyId"))),
                missingIbanCount = riders.Count(r => string.IsNullOrWhiteSpace(r.S("iban"))),
                needsReviewCount = riders.Count(r => r.S("status") == "NeedsReview")
            };
        })).WithTags("Riders").WithSummary("Read riders data quality");

        // Reduced 360 (H6): prefer GET /api/workforce/{id}/360. Legacy route kept for statement links.
        api.MapGet("/riders/{iqama}/profile-360", (string iqama, HttpContext ctx) => store.Read(c =>
        {
            J.Iqama(iqama);
            var rider = Store.Get(c, iqama, "rider") ?? throw new ApiError("المندوب غير موجود.", 404);
            FinancialScopes.DemandRider(ctx.User, rider);
            // view_360 is required by AccessPolicy; still project + mask IBAN / muda salary by field permission.
            var view = WorkforceApi.ProjectDirectoryRider(rider, ctx.User);
            var debts = Store.List(c, "debt").Where(d => d.S("iqama") == iqama && FinancialScopes.Debt(ctx.User, d)).ToList();
            var works = Store.List(c, "work").Where(w => w.S("iqama") == iqama).ToList();
            return new
            {
                person = view,
                rider = view,
                workRecord3Months = works.Take(50).Select(w => new
                {
                    month = w.S("period", w.S("from")),
                    platform = w.S("platform"),
                    workDays = ErpImport.WorkforceLiveQuery.NoSourceYet,
                    orders = (int)w.N("orders"),
                    netDue = ErpImport.WorkforceLiveQuery.NoSourceYet,
                    status = w.S("status")
                }),
                financialEvaluation = new { status = "unavailable", message = ErpImport.WorkforceLiveQuery.NoSourceYet },
                iqamaImage = new { status = "pending_permission", message = ErpImport.WorkforceLiveQuery.ImagePending },
                ibanImage = new { status = "pending_permission", message = ErpImport.WorkforceLiveQuery.ImagePending },
                promissoryNotes = Array.Empty<object>(),
                platformAccounts = Array.Empty<object>(),
                financialPosition = new
                {
                    activeDebtsCount = debts.Count(d => d.S("status") == "active"),
                    totalRemainingDebt = J.Money(debts.Sum(d => d.N("remainingBalance"))),
                    statementPath = "/app/riders/statement/?iqama=" + Uri.EscapeDataString(iqama),
                    message = "افتح كشف الحساب التفصيلي"
                },
                // Legacy fields kept empty so old clients do not invent a documents checklist.
                documentChecklist = Array.Empty<object>(),
                documentsCount = 0,
                activeDebtsCount = debts.Count(d => d.S("status") == "active"),
                totalRemainingDebt = J.Money(debts.Sum(d => d.N("remainingBalance"))),
                workRecordsCount = works.Count,
                totalOrders = works.Sum(w => (int)w.N("orders")),
                submissions = Array.Empty<object>(),
                allowedActions = new[] { "open_statement" }
            };
        })).WithTags("Riders").WithSummary("Read reduced riders {iqama} profile 360 (H6)");
    }
}
