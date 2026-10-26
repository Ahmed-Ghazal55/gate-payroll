using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

public static class RiderDirectory
{
    public sealed record DocTypeDef(string Id, string Code, string NameAr, bool RequiresExpiry);

    // أنواع ملفات ومستندات المندوب الرسمية المطابقة تماماً لـ Logistic-ERP (DocumentType.cs)
    public static readonly DocTypeDef[] DocumentTypes =
    [
        new("019c18d5-62e1-7000-8000-000000000030", "ResidencyPermit", "هوية مقيم (الإقامة)", true),
        new("019c18d5-62e1-7000-8000-000000000031", "DriverLicense", "رخصة القيادة", true),
        new("019c18d5-62e1-7000-8000-000000000032", "RiderCard", "بطاقة سائق (هيئة النقل)", true),
        new("019c18d5-62e1-7000-8000-000000000033", "HealthCard", "الكرت الصحي", true),
        new("019c18d5-62e1-7000-8000-000000000034", "PromissoryNote", "سند لأمر (منصة نافذ)", false),
        new("019c18d5-62e1-7000-8000-000000000035", "MedicalInsurance", "وثيقة التأمين الطبي", true),
        new("019c18d5-62e1-7000-8000-000000000036", "AjeerContract", "عقد أجير / تصريح عمل", true)
    ];

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
        // 1. مزامنة الدليل الخاص (عبر رفع ملف JSON أو مباشرة من ملف data/riders-baseline.json المحلي لاستكمال البيانات المفقودة)
        api.MapPost("/riders/sync-baseline", (JsonObject input, HttpContext ctx) =>
        {
            var raw = input["riders"] as JsonArray;
            if (raw == null && input.B("useLocalBaseline"))
            {
                string? localPath = FindLocalBaselineFile() ?? throw new ApiError("ملف الدليل المرجعي المحلي غير موجود في data/riders-baseline.json.", 404);
                raw = JsonNode.Parse(File.ReadAllText(localPath))?.AsArray();
            }
            if (raw == null) throw new ApiError("ارفع مصفوفة الدليل الخاصة.");
            J.Need(raw.Count is > 0 and <= 5000 && raw.All(x => x is JsonObject), "حجم أو شكل دليل غير صالح.");
            var people = raw.OfType<JsonObject>().Where(x => x.S("personRole") != "administrative" && !x.B("isEmployee")).ToList();
            J.Need(people.Count > 0, "لا توجد سجلات مناديب.");
            var ids = new HashSet<string>();
            var normalized = new List<JsonObject>();
            var errors = new JsonArray();
            for (int i = 0; i < people.Count; i++)
            {
                try
                {
                    var row = Normalize(people[i]);
                    J.Need(ids.Add(row.S("id")), "إقامة مكررة في المصدر.");
                    row["policyId"] = "";
                    row["sourceName"] = people[i].S("sourceName", row.S("name"));
                    normalized.Add(row);
                }
                catch (ApiError e)
                {
                    errors.Add(new JsonObject { ["row"] = i + 1, ["error"] = e.Message });
                }
            }
            string source = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToJsonString())));
            object Process(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction? tx)
            {
                var records = Store.List(c, "rider", tx).OrderBy(x => x.S("id")).ToList();
                var existing = records.ToDictionary(x => x.S("id"));
                var added = normalized.Where(x => !existing.ContainsKey(x.S("id"))).ToList();
                var updates = new List<JsonObject>();
                var conflicts = new JsonArray();
                foreach (var row in normalized.Where(x => existing.ContainsKey(x.S("id"))))
                {
                    var current = existing[row.S("id")].Clone();
                    bool changed = false;
                    var fields = new JsonArray();
                    foreach (string field in new[] { "name", "phone", "nationality", "engagementType", "sponsor", "sponsorId", "city", "operationalRole", "drivingLicense", "platforms", "iban" })
                    {
                        if (current.S(field) == "" && row.S(field) != "")
                        {
                            current[field] = row.S(field);
                            changed = true;
                        }
                        else if (current.S(field) != "" && row.S(field) != "" && current.S(field) != row.S(field) && field is not ("sponsorId" or "drivingLicense" or "platforms" or "iban"))
                        {
                            fields.Add(field);
                        }
                    }
                    if (changed) updates.Add(current);
                    if (fields.Count > 0) conflicts.Add(new JsonObject { ["iqama"] = row.S("id"), ["fields"] = fields });
                }
                string state = string.Join('|', records.Select(x => x.ToJsonString()));
                string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "|" + state)));
                if (input.S("mode") != "commit")
                    return new { fingerprint, sourceFingerprint = source, total = people.Count, excludedAdministrative = raw.Count - people.Count, addCount = added.Count, updateCount = updates.Count, existingCount = people.Count - added.Count, conflicts, errors, rows = added };
                J.Need(errors.Count == 0, "الدليل يحتوي أخطاء؛ لم تُكتب أي سجلات.");
                var prior = Store.Get(c, "baseline:" + source, "baseline_import", tx);
                if (prior != null && added.Count == 0 && updates.Count == 0)
                    return new { count = 0, alreadyImported = true, batchId = prior.S("id") };
                J.Need(input.S("fingerprint") == fingerprint, "تغير الدليل منذ المعاينة؛ أعد المعاينة.");
                string batch = "baseline:" + source;
                foreach (var row in added)
                {
                    row["baselineSourceFingerprint"] = source;
                    row["createdAt"] = DateTimeOffset.UtcNow.ToString("O");
                    Store.Save(c, "rider", row, tx!);
                }
                foreach (var row in updates)
                {
                    row["baselineSourceFingerprint"] = source;
                    Store.Save(c, "rider", row, tx!);
                }
                Store.Save(c, "baseline_import", new() { ["id"] = batch, ["sourceFingerprint"] = source, ["count"] = added.Count, ["updatedCount"] = updates.Count, ["at"] = DateTimeOffset.UtcNow.ToString("O") }, tx!);
                Store.Audit(c, tx!, "rider.baseline_imported", batch, ctx.User.Identity!.Name!);
                return new { count = added.Count, updatedCount = updates.Count, alreadyImported = false, batchId = batch };
            }
            return input.S("mode") == "commit" ? store.Write((c, tx) => Process(c, tx)) : store.Read(c => Process(c, null));
        });

        // 2. استكمال وجلب البيانات المفقودة آلياً (Auto-Enrich Missing Fields) من المصادر المتاحة (الملف المرجعي + ردود المناديب + دفعات التشغيل)
        api.MapPost("/riders/enrich-missing", (HttpContext ctx) => store.Write((c, tx) =>
        {
            var riders = Store.List(c, "rider", tx).ToDictionary(r => r.S("id"));
            var submissions = Store.List(c, "submission", tx);
            var works = Store.List(c, "work", tx);
            JsonArray? localBaseline = null;
            string? path = FindLocalBaselineFile();
            if (path != null) localBaseline = JsonNode.Parse(File.ReadAllText(path))?.AsArray();

            int enrichedExisting = 0, addedFromSources = 0;

            // أ) استكمال الحقول المفقودة من الدليل المرجعي المحلي (إن وجد)
            if (localBaseline != null)
            {
                foreach (var item in localBaseline.OfType<JsonObject>())
                {
                    if (item.S("personRole") == "administrative" || item.B("isEmployee")) continue;
                    string iq = item.S("iqama").Trim();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(iq, "^[12][0-9]{9}$")) continue;
                    if (riders.TryGetValue(iq, out var cur))
                    {
                        bool mod = false;
                        foreach (var (targetKey, srcKey) in new[] { ("phone", "phone"), ("nationality", "nationality"), ("sponsor", "sponsor"), ("city", "city"), ("drivingLicense", "driving_license"), ("platforms", "platforms"), ("iban", "iban") })
                        {
                            if (string.IsNullOrWhiteSpace(cur.S(targetKey)) && !string.IsNullOrWhiteSpace(item.S(srcKey)))
                            {
                                cur[targetKey] = targetKey == "phone" ? Phone(item.S(srcKey)) : item.S(srcKey);
                                mod = true;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(cur.S("engagementType")) && !string.IsNullOrWhiteSpace(item.S("engagement_type")))
                        {
                            cur["engagementType"] = item.S("engagement_type") == "SponsoredInternal" ? "SponsoredInternal" : "External";
                            mod = true;
                        }
                        if (string.IsNullOrWhiteSpace(cur.S("operationalRole")) && !string.IsNullOrWhiteSpace(item.S("operational_role")))
                        {
                            cur["operationalRole"] = item.S("operational_role").Contains("سيارة") ? "Car" : "Motorcycle";
                            mod = true;
                        }
                        if (!string.IsNullOrWhiteSpace(cur.S("sponsor")) && string.IsNullOrWhiteSpace(cur.S("sponsorId")))
                        {
                            if(FinancialCore.FindSponsor(cur.S("sponsor")) is {} sp)cur["sponsorId"] = sp.Id;
                            mod = true;
                        }
                        if (mod)
                        {
                            Store.Save(c, "rider", cur, tx);
                            enrichedExisting++;
                        }
                    }
                }
            }

            // ب) استكمال الجوال والمدينة وتفضيل العمل من ردود المناديب ومصادر التشغيل
            foreach (var sub in submissions)
            {
                string iq = sub.S("iqama");
                if (riders.TryGetValue(iq, out var cur))
                {
                    bool mod = false;
                    if (string.IsNullOrWhiteSpace(cur.S("phone")) && !string.IsNullOrWhiteSpace(sub.S("phone"))) { cur["phone"] = sub.S("phone"); mod = true; }
                    if (string.IsNullOrWhiteSpace(cur.S("city")) && !string.IsNullOrWhiteSpace(sub.S("city"))) { cur["city"] = sub.S("city"); mod = true; }
                    if (mod) { Store.Save(c, "rider", cur, tx); enrichedExisting++; }
                }
            }

            foreach (var w in works)
            {
                string iq = w.S("iqama");
                if (riders.TryGetValue(iq, out var cur))
                {
                    bool mod = false;
                    if (string.IsNullOrWhiteSpace(cur.S("city")) && !string.IsNullOrWhiteSpace(w.S("city"))) { cur["city"] = w.S("city"); mod = true; }
                    if (string.IsNullOrWhiteSpace(cur.S("sponsor")) && !string.IsNullOrWhiteSpace(w.S("company")))
                    {
                        cur["sponsor"] = w.S("company");
                        if(FinancialCore.FindSponsor(w.S("company")) is {} sp)cur["sponsorId"] = sp.Id;
                        mod = true;
                    }
                    if (string.IsNullOrWhiteSpace(cur.S("platforms")) && !string.IsNullOrWhiteSpace(w.S("platform"))) { cur["platforms"] = w.S("platform"); mod = true; }
                    if (mod) { Store.Save(c, "rider", cur, tx); enrichedExisting++; }
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(iq, "^[12][0-9]{9}$") && !string.IsNullOrWhiteSpace(w.S("name")))
                {
                    try
                    {
                        var created = Normalize(new JsonObject
                        {
                            ["iqama"] = iq,
                            ["name"] = w.S("name"),
                            ["city"] = w.S("city"),
                            ["sponsor"] = w.S("company"),
                            ["platforms"] = w.S("platform"),
                            ["status"] = "NeedsReview"
                        });
                        Store.Save(c, "rider", created, tx);
                        riders[iq] = created;
                        addedFromSources++;
                    }
                    catch { }
                }
            }

            Store.Audit(c, tx, "rider.enriched_missing", $"enriched:{enrichedExisting},added:{addedFromSources}", ctx.User.Identity!.Name!);
            return new { enrichedExisting, addedFromSources, totalRidersNow = riders.Count };
        }));

        // 3. تشخيص جودة واكتمال بيانات المناديب (Data Completeness & Missing Fields Report)
        api.MapGet("/riders/data-quality", () => store.Read(c =>
        {
            var riders = Store.List(c, "rider");
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
        }));

        // 4. الملف الشامل للمندوب 360° ومستنداته ومرفقاته الرسمية (متوافق مع EmployeeDocumentsController في البوابة)
        api.MapGet("/riders/{iqama}/profile-360", (string iqama, HttpContext ctx) => store.Read(c =>
        {
            J.Iqama(iqama);
            var rider = Store.Get(c, iqama, "rider") ?? throw new ApiError("المندوب غير موجود.", 404);
            FinancialScopes.DemandRider(ctx.User,rider);
            var view = !AccessPolicy.Has(ctx.User,"riders.view_360") ? UserAccess.RiderView(rider) : rider;
            var docs = Store.List(c, "rider_document").Where(d => d.S("iqama") == iqama).ToList();
            var debts = Store.List(c, "debt").Where(d => d.S("iqama") == iqama&&FinancialScopes.Debt(ctx.User,d)).ToList();
            var works = Store.List(c, "work").Where(w => w.S("iqama") == iqama).ToList();
            var submissions = Store.List(c, "submission").Where(s => s.S("iqama") == iqama).ToList();

            var checklist = DocumentTypes.Select(dt =>
            {
                var found = docs.FirstOrDefault(d => d.S("documentTypeCode") == dt.Code || d.S("documentTypeId") == dt.Id);
                return new
                {
                    documentTypeId = dt.Id,
                    code = dt.Code,
                    nameAr = dt.NameAr,
                    requiresExpiry = dt.RequiresExpiry,
                    isUploaded = found != null,
                    documentId = found?.S("id") ?? "",
                    documentNumber = found?.S("documentNumber") ?? "",
                    issueDate = found?.S("issueDate") ?? "",
                    expiryDate = found?.S("expiryDate") ?? "",
                    fileName = found?.S("fileName") ?? "",
                    storagePath = found?.S("storagePath") ?? $"wwwroot/private/employee-documents/{iqama}/{dt.Id}",
                    status = found?.S("status") ?? "Missing"
                };
            }).ToList();

            return new
            {
                rider = view,
                documentChecklist = checklist,
                documentsCount = docs.Count,
                activeDebtsCount = debts.Count(d => d.S("status") == "active"),
                totalRemainingDebt = J.Money(debts.Sum(d => d.N("remainingBalance"))),
                workRecordsCount = works.Count,
                totalOrders = works.Sum(w => (int)w.N("orders")),
                submissions
            };
        }));

        // 5. رفع / تحديث وثيقة رسمية للمندوب (الإقامة، رخصة القيادة، بطاقة السائق، الكرت الصحي، عقد أجير...)
        api.MapPost("/riders/{iqama}/documents", (string iqama, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            J.Iqama(iqama);
            var rider = Store.Get(c, iqama, "rider", tx) ?? throw new ApiError("المندوب غير موجود.", 404);
            FinancialScopes.DemandRider(ctx.User,rider);
            string code = input.Required("documentTypeCode");
            var dt = DocumentTypes.FirstOrDefault(x => x.Code == code || x.Id == code) ?? throw new ApiError("نوع المستند غير صالح.");

            var existing=Store.List(c,"rider_document",tx).FirstOrDefault(d=>d.S("iqama")==iqama&&d.S("documentTypeCode")==dt.Code);
            string docId=Guid.NewGuid().ToString("N"),recordId=existing?.S("id")??docId;
            string fileName=Path.GetFileName(input.S("fileName",existing?.S("fileName")??$"{dt.Code}-{iqama}.pdf"));
            J.Need(fileName.Length is >0 and <=200&&!fileName.Contains('\\'),"اسم ملف غير صالح.");
            string extension=Path.GetExtension(fileName).ToLowerInvariant();
            J.Need(new[]{".pdf",".png",".jpg",".jpeg"}.Contains(extension),"المرفقات PDF أو PNG أو JPG فقط.");
            string contentType=extension==".pdf"?"application/pdf":extension==".png"?"image/png":"image/jpeg";
            string base64=input.S("dataBase64"),relative=existing?.S("fileRelativePath")??"";
            if(!string.IsNullOrWhiteSpace(base64))
            {
                J.Need(base64.Length<=14*1024*1024,"المرفق طويل جدًا.");byte[] bytes=Convert.FromBase64String(base64);
                J.Need(bytes.Length is >0 and <=10*1024*1024,"حجم ملف المستند غير صالح.");
                relative=Path.Combine("rider-documents",iqama,dt.Code,docId+extension);
                string disk=Path.Combine(store.DirectoryPath,relative);Directory.CreateDirectory(Path.GetDirectoryName(disk)!);File.WriteAllBytes(disk,bytes);
            }
            string relPath=$"/api/riders/{iqama}/documents/{recordId}/download";
            var history=(existing?["history"] as JsonArray)?.DeepClone().AsArray()??new JsonArray();
            if(existing!=null){var previous=existing.Clone();previous.Remove("history");history.Add(previous);}
            int version = existing != null ? (int)existing.N("versionNumber", 1) + 1 : 1;
            var doc = new JsonObject
            {
                ["id"] = existing?.S("id") ?? docId,
                ["iqama"] = iqama,
                ["riderName"] = rider.S("name"),
                ["documentTypeId"] = dt.Id,
                ["documentTypeCode"] = dt.Code,
                ["documentTypeNameAr"] = dt.NameAr,
                ["documentNumber"] = input.S("documentNumber", iqama),
                ["issueDate"] = input.S("issueDate"),
                ["expiryDate"] = input.S("expiryDate"),
                ["fileName"] = fileName,
                ["contentType"] = string.IsNullOrWhiteSpace(base64)?existing?.S("contentType",contentType)??contentType:contentType,
                ["fileRelativePath"] = relative,
                ["history"] = history,
                ["storagePath"] = relPath,
                ["versionNumber"] = version,
                ["hasBinary"] = !string.IsNullOrWhiteSpace(base64)||existing?.B("hasBinary")==true,
                ["status"] = "Active",
                ["notes"] = input.S("notes"),
                ["uploadedBy"] = ctx.User.Identity!.Name!,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "rider_document", doc, tx);
            Store.Audit(c, tx, "rider.document_saved", $"{iqama}:{dt.Code}", ctx.User.Identity!.Name!);
            return doc;
        }));
        api.MapGet("/riders/{iqama}/documents/{id}/download",(string iqama,string id,HttpContext ctx)=>store.Read(c=>
        {
            FinancialScopes.DemandRider(ctx.User,Store.Get(c,J.Iqama(iqama),"rider")??throw new ApiError("المندوب غير موجود.",404));
            var doc=Store.Get(c,id,"rider_document")??throw new ApiError("المستند غير موجود.",404);
            J.Need(doc.S("iqama")==J.Iqama(iqama),"المستند لا يخص هذا المندوب.");
            string relative=doc.S("fileRelativePath");
            if(!doc.B("hasBinary")||string.IsNullOrWhiteSpace(relative))throw new ApiError("لا يوجد مرفق محفوظ لهذا السجل.",404);
            string path=Path.GetFullPath(Path.Combine(store.DirectoryPath,relative));
            J.Need(path.StartsWith(Path.GetFullPath(store.DirectoryPath)+Path.DirectorySeparatorChar,StringComparison.Ordinal),"مسار مرفق غير صالح.");
            if(!File.Exists(path))throw new ApiError("المرفق غير متاح.",404);
            return Results.File(path,doc.S("contentType","application/octet-stream"),doc.S("fileName","document.pdf"));
        }));
    }
}
