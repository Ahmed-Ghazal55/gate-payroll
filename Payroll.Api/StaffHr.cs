using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>HR1 simple staff HR (not riders): employee card, Qiwa contracts, monthly summary,
/// advances via debt documents (Employee), staff run draft→review→approve→Mudad→one journal.</summary>
public static class StaffHr
{
    public const string EmployeeKind = "staff_employee";
    public const string ContractKind = "staff_contract";
    public const string MonthKind = "staff_month";
    public const string RunKind = "staff_run";
    public const string ViewPermission = "staff.hr.view";
    public const string ManagePermission = "staff.hr.manage";
    public const string ApprovePermission = "staff.hr.approve";
    public const string ContractUploadPermission = "staff.contract.upload";

    static readonly string[] EmploymentTypes = ["kafala", "external", "office", "management"];
    static readonly string[] Departments = ["HR", "FIN", "OPS", "IT", "MGMT", "EGYPT", "OTHER"];
    static readonly string[] Cities = ["جدة", "الرياض", "الدمام", "مكة", "المدينة", "أخرى"];
    static readonly string[] Nationalities = ["سعودي", "مصري", "يمني", "هندي", "باكستاني", "أخرى"];
    static readonly string[] JobTitles = ["محاسب", "مشرف", "موارد بشرية", "إداري", "مدير", "دعم فني", "أخرى"];

    public static JsonObject Catalogs() => new()
    {
        ["employmentTypes"] = new JsonArray(EmploymentTypes.Select(v => (JsonNode?)v).ToArray()),
        ["departments"] = new JsonArray(Departments.Select(v => (JsonNode?)v).ToArray()),
        ["cities"] = new JsonArray(Cities.Select(v => (JsonNode?)v).ToArray()),
        ["nationalities"] = new JsonArray(Nationalities.Select(v => (JsonNode?)v).ToArray()),
        ["jobTitles"] = new JsonArray(JobTitles.Select(v => (JsonNode?)v).ToArray()),
    };

    static string ActorName(ClaimsPrincipal actor) => actor.Identity?.Name ?? "";
    static string DateYmd(string raw, string label)
    {
        J.Need(DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d), label + " بصيغة yyyy-MM-dd.");
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    static string Pick(string value, string[] allowed, string label)
    {
        J.Need(allowed.Contains(value), label + " غير صالح.");
        return value;
    }
    static void DemandView(ClaimsPrincipal actor) => AccessPolicy.Demand(actor, ViewPermission);
    static void DemandManage(ClaimsPrincipal actor) => AccessPolicy.Demand(actor, ManagePermission);
    static void DemandApprove(ClaimsPrincipal actor) => AccessPolicy.Demand(actor, ApprovePermission);
    static void ScopeCompany(ClaimsPrincipal actor, string companyId) => FinancialScopes.DemandSponsor(actor, companyId);

    static JsonObject RequireEmployee(DbConnection c, DbTransaction? tx, string id)
        => Store.Get(c, id, EmployeeKind, tx) ?? throw new ApiError("بطاقة الموظف غير موجودة.", 404);

    static IEnumerable<JsonObject> ContractsFor(DbConnection c, DbTransaction? tx, string employeeId)
        => Store.List(c, ContractKind, tx).Where(x => x.S("employeeId") == employeeId)
            .OrderByDescending(x => x.S("issueDate")).ThenByDescending(x => x.S("createdAt"));

    /// <summary>Latest contract by issue date (may be expired) — used for expiry alerts only.</summary>
    static JsonObject? LatestContractAny(DbConnection c, DbTransaction? tx, string employeeId)
        => ContractsFor(c, tx, employeeId).FirstOrDefault();

    /// <summary>Current contract = latest issue whose end date still covers <paramref name="asOf"/> (brief: latest valid).</summary>
    static JsonObject? CurrentContract(DbConnection c, DbTransaction? tx, string employeeId, DateOnly? asOf = null)
    {
        var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return ContractsFor(c, tx, employeeId)
            .FirstOrDefault(x => DateOnly.TryParseExact(x.S("endDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end >= day);
    }

    /// <summary>Contract valid for a pay-month: end date covers the month (end ≥ first day of period).</summary>
    static JsonObject? ContractForPeriod(DbConnection c, DbTransaction? tx, string employeeId, string period)
    {
        var first = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return CurrentContract(c, tx, employeeId, first);
    }

    static JsonArray ActionsForEmployee(ClaimsPrincipal actor, JsonObject emp)
    {
        var a = new JsonArray();
        if (AccessPolicy.Has(actor, ViewPermission)) a.Add("view");
        if (AccessPolicy.Has(actor, ManagePermission) && emp.S("status") != "archived")
        {
            a.Add("edit"); a.Add("upsert_month");
            // LOW: create_advance only when both manage and debts.create.advance (handler requires both).
            if (AccessPolicy.Has(actor, "debts.create.advance")) a.Add("create_advance");
            if (AccessPolicy.Has(actor, ContractUploadPermission)) a.Add("upload_contract");
        }
        return a;
    }

    static bool PostWasRefused(JsonObject run) => run.B("postRefused") || run.S("postRefusedAt") != "";

    static JsonArray ActionsForRun(ClaimsPrincipal actor, JsonObject run)
    {
        var a = new JsonArray();
        bool manage = AccessPolicy.Has(actor, ManagePermission);
        bool approve = AccessPolicy.Has(actor, ApprovePermission);
        string status = run.S("status");
        if (AccessPolicy.Has(actor, ViewPermission)) a.Add("view");
        if (manage && status == "draft") { a.Add("submit_review"); a.Add("recalculate"); }
        // Review: preparer/manager may return. Approved: only after post refused, or the approver (approval stays binding).
        if (manage && status == "review") a.Add("back_to_draft");
        if (status == "approved" && run.S("journalEntryId") == ""
            && (PostWasRefused(run) ? manage || approve : approve))
            a.Add("back_to_draft");
        if (approve && status == "review" && !AccessPolicy.SamePerson(actor, run.S("createdBy"))
            && !AccessPolicy.SamePerson(actor, run.S("preparedBy")))
            a.Add("approve");
        if (manage && status == "approved") { a.Add("generate_mudad"); a.Add("post_journal"); }
        if (approve && status == "posted" && run.S("journalEntryId") != "" && run.S("reversalJournalId") == ""
            && !AccessPolicy.SamePerson(actor, run.S("postedBy")))
            a.Add("reverse");
        if (AccessPolicy.Has(actor, ViewPermission) && run.S("mudadFileId") != "" && !run.B("mudadVoided"))
            a.Add("download_mudad");
        return a;
    }

    static JsonObject PublicEmployee(DbConnection c, DbTransaction? tx, ClaimsPrincipal actor, JsonObject emp)
    {
        var contract = CurrentContract(c, tx, emp.S("id"));
        var view = emp.DeepClone()!.AsObject();
        view["basicSalary"] = contract?.N("basicSalary") ?? 0m;
        view["contractEndDate"] = contract?.S("endDate") ?? "";
        view["contractIssueDate"] = contract?.S("issueDate") ?? "";
        view["currentContractId"] = contract?.S("id") ?? "";
        view["allowedActions"] = ActionsForEmployee(actor, emp);
        // Linked ERP user id is read-only on the card (for later KPIs).
        view["erpUserEmployeeId"] = emp.S("erpEmployeeId");
        return view;
    }

    static void UpsertPersonStub(DbConnection c, DbTransaction tx, JsonObject emp)
    {
        // Administrative stub so Debts can bind by iqama without listing as an operating rider.
        // Refuse any existing person row unless it is already this employee's staff stub (no overwrite of admin/debt-only profiles).
        string iqama = emp.S("iqama");
        var existing = Store.Get(c, iqama, "rider", tx);
        if (existing != null)
        {
            bool ownStub = existing.B("staffHrStub") && existing.S("staffEmployeeId") == emp.S("id");
            if (!ownStub)
            {
                if (ErpRiderClassification.ListedAsRider(existing))
                    throw new ApiError("الهوية مسجّلة لمندوب تشغيل؛ لا تُستخدم لبطاقة موظف.", 409);
                throw new ApiError("الهوية مسجّلة لملف شخص موجود؛ لا يُستبدل ببطاقة موظف.", 409);
            }
        }
        var stub = new JsonObject
        {
            ["id"] = iqama,
            ["iqama"] = iqama,
            ["name"] = emp.S("displayName"),
            ["sponsorId"] = emp.S("companyId"),
            ["financeSponsorId"] = emp.S("companyId"),
            ["personRole"] = "administrative",
            ["erpSource"] = "rider",
            ["operationalRole"] = "موظف إداري",
            ["staffHrStub"] = true,
            ["staffEmployeeId"] = emp.S("id"),
            ["status"] = emp.S("status") == "active" ? "Active" : "Archived",
            ["debtOnly"] = false,
        };
        Store.Save(c, "rider", stub, tx);
    }

    public static JsonObject CreateEmployee(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, JsonObject input)
    {
        DemandManage(actor);
        PayValues.Fields(input, "displayName", "iqama", "companyId", "department", "jobTitle", "city", "nationality", "employmentType", "erpEmployeeId", "linkedUserId", "notes");
        string iqama = J.Iqama(input.Required("iqama"));
        string company = FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        ScopeCompany(actor, company);
        J.Need(!Store.List(c, EmployeeKind, tx).Any(e => e.S("iqama") == iqama && e.S("status") != "archived"), "بطاقة موظف بنفس الهوية موجودة.");
        string linkedUser = input.S("linkedUserId");
        var emp = new JsonObject
        {
            ["id"] = "staff:" + Guid.NewGuid().ToString("N"),
            ["displayName"] = input.Required("displayName").Trim(),
            ["iqama"] = iqama,
            ["companyId"] = company,
            ["department"] = Pick(input.Required("department"), Departments, "القسم"),
            ["jobTitle"] = Pick(input.Required("jobTitle"), JobTitles, "المسمى"),
            ["city"] = Pick(input.Required("city"), Cities, "المدينة"),
            ["nationality"] = Pick(input.Required("nationality"), Nationalities, "الجنسية"),
            ["employmentType"] = Pick(input.Required("employmentType"), EmploymentTypes, "نوع التوظيف"),
            ["erpEmployeeId"] = input.S("erpEmployeeId").Trim(),
            ["linkedUserId"] = "",
            ["notes"] = input.S("notes").Trim(),
            ["status"] = "active",
            ["createdBy"] = ActorName(actor),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        J.Need(emp.S("displayName").Length is >= 2 and <= 120, "الاسم المختصر بين 2 و120 حرفًا.");
        UpsertPersonStub(c, tx, emp);
        if (linkedUser != "")
        {
            var user = Store.Get(c, linkedUser, "user", tx) ?? throw new ApiError("مستخدم النظام غير موجود.", 404);
            J.Need(user.S("employeeId") == "", "المستخدم مربوط بموظف آخر.");
            user["employeeId"] = emp.S("id");
            Store.Save(c, "user", user, tx);
            emp["linkedUserId"] = linkedUser;
        }
        Store.Save(c, EmployeeKind, emp, tx);
        Store.Audit(c, tx, "staff.employee_created", emp.S("id"), ActorName(actor));
        return PublicEmployee(c, tx, actor, emp);
    }

    public static JsonObject UpdateEmployee(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        DemandManage(actor);
        var emp = RequireEmployee(c, tx, id);
        ScopeCompany(actor, emp.S("companyId"));
        PayValues.Fields(input, "displayName", "department", "jobTitle", "city", "nationality", "employmentType", "erpEmployeeId", "linkedUserId", "notes", "status");
        if (input.ContainsKey("displayName")) emp["displayName"] = input.Required("displayName").Trim();
        if (input.ContainsKey("department")) emp["department"] = Pick(input.Required("department"), Departments, "القسم");
        if (input.ContainsKey("jobTitle")) emp["jobTitle"] = Pick(input.Required("jobTitle"), JobTitles, "المسمى");
        if (input.ContainsKey("city")) emp["city"] = Pick(input.Required("city"), Cities, "المدينة");
        if (input.ContainsKey("nationality")) emp["nationality"] = Pick(input.Required("nationality"), Nationalities, "الجنسية");
        if (input.ContainsKey("employmentType")) emp["employmentType"] = Pick(input.Required("employmentType"), EmploymentTypes, "نوع التوظيف");
        if (input.ContainsKey("erpEmployeeId")) emp["erpEmployeeId"] = input.S("erpEmployeeId").Trim();
        if (input.ContainsKey("notes")) emp["notes"] = input.S("notes").Trim();
        if (input.ContainsKey("status"))
        {
            string st = input.Required("status");
            J.Need(st is "active" or "inactive" or "archived", "حالة الموظف غير صالحة.");
            emp["status"] = st;
        }
        if (input.ContainsKey("linkedUserId"))
        {
            string linkedUser = input.S("linkedUserId");
            string prev = emp.S("linkedUserId");
            if (prev != "" && prev != linkedUser)
            {
                var oldUser = Store.Get(c, prev, "user", tx);
                if (oldUser != null && oldUser.S("employeeId") == emp.S("id"))
                {
                    oldUser["employeeId"] = "";
                    Store.Save(c, "user", oldUser, tx);
                }
            }
            if (linkedUser != "")
            {
                var user = Store.Get(c, linkedUser, "user", tx) ?? throw new ApiError("مستخدم النظام غير موجود.", 404);
                J.Need(user.S("employeeId") == "" || user.S("employeeId") == emp.S("id"), "المستخدم مربوط بموظف آخر.");
                user["employeeId"] = emp.S("id");
                Store.Save(c, "user", user, tx);
            }
            emp["linkedUserId"] = linkedUser;
        }
        emp["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        UpsertPersonStub(c, tx, emp);
        Store.Save(c, EmployeeKind, emp, tx);
        Store.Audit(c, tx, "staff.employee_updated", id, ActorName(actor));
        return PublicEmployee(c, tx, actor, emp);
    }

    public static JsonArray ListEmployees(DbConnection c, ClaimsPrincipal actor, string? companyId, string? status)
    {
        DemandView(actor);
        return new JsonArray(Store.List(c, EmployeeKind)
            .Where(e => companyId == null || companyId == "" || e.S("companyId") == companyId)
            .Where(e => status == null || status == "" || status == "all" || e.S("status") == status)
            .Where(e => FinancialScopes.Sponsor(actor, e.S("companyId")))
            .OrderBy(e => e.S("displayName"))
            .Select(e => (JsonNode?)PublicEmployee(c, null, actor, e))
            .ToArray());
    }

    public static JsonObject GetEmployee(DbConnection c, ClaimsPrincipal actor, string id)
    {
        DemandView(actor);
        var emp = RequireEmployee(c, null, id);
        ScopeCompany(actor, emp.S("companyId"));
        var view = PublicEmployee(c, null, actor, emp);
        view["contracts"] = new JsonArray(Store.List(c, ContractKind).Where(x => x.S("employeeId") == id)
            .OrderByDescending(x => x.S("issueDate")).Select(x =>
            {
                var row = x.DeepClone()!.AsObject();
                row.Remove("dataBase64");
                var actions = new JsonArray();
                if (AccessPolicy.Has(actor, ViewPermission) && row.S("attachmentId") != "") actions.Add("download");
                row["allowedActions"] = actions;
                return (JsonNode?)row;
            }).ToArray());
        view["months"] = new JsonArray(Store.List(c, MonthKind).Where(x => x.S("employeeId") == id)
            .OrderByDescending(x => x.S("period")).Select(x => (JsonNode?)x.DeepClone()).ToArray());
        view["advances"] = new JsonArray(Store.List(c, "debt")
            .Where(d => d.S("iqama") == emp.S("iqama") && d.S("debtorType") == "Employee"
                && AccountingEvents.IsAdvance(d.S("category")))
            .Select(d =>
            {
                var actions = new JsonArray();
                if (d.S("status") == "draft" && AccessPolicy.Has(actor, ApprovePermission)
                    && !AccessPolicy.SamePerson(actor, d.S("createdBy")))
                    actions.Add("approve_advance");
                return (JsonNode?)new JsonObject
                {
                    ["id"] = d.S("id"),
                    ["category"] = d.S("category"),
                    ["sourceRef"] = d.S("sourceRef"),
                    ["remainingBalance"] = d.N("remainingBalance"),
                    ["monthlyInstallment"] = d.N("monthlyInstallment"),
                    ["status"] = d.S("status"),
                    ["journalEntryId"] = d.S("journalEntryId"),
                    ["agreementReviewed"] = d.B("agreementReviewed"),
                    ["allowedActions"] = actions,
                };
            }).ToArray());
        return view;
    }

    public static JsonObject AddContract(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string employeeId, JsonObject input)
    {
        DemandManage(actor);
        AccessPolicy.Demand(actor, ContractUploadPermission);
        var emp = RequireEmployee(c, tx, employeeId);
        ScopeCompany(actor, emp.S("companyId"));
        PayValues.Fields(input, "issueDate", "endDate", "basicSalary", "attachment");
        string issue = DateYmd(input.Required("issueDate"), "تاريخ الإصدار/التجديد");
        string end = DateYmd(input.Required("endDate"), "تاريخ الانتهاء");
        J.Need(string.CompareOrdinal(end, issue) >= 0, "تاريخ الانتهاء بعد الإصدار.");
        decimal basic = J.Money(input.N("basicSalary"));
        J.Need(basic > 0 && basic <= 200000m, "الراتب الأساسي في العقد غير صالح.");
        var attachmentNode = input["attachment"] as JsonObject ?? throw new ApiError("مرفق عقد قوى مطلوب.");
        PayValues.Fields(attachmentNode, "fileName", "dataBase64");
        byte[] data = FinancialImports.FileBytes(attachmentNode);
        J.Need(data.Length is > 0 and <= 5 * 1024 * 1024, "حد ملف العقد 5 ميجابايت.");
        bool pdf = data.AsSpan().StartsWith("%PDF-"u8);
        bool png = data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool jpg = data.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        J.Need(pdf || png || jpg, "عقد قوى PDF أو صورة PNG/JPEG.");
        string attId = "staff-contract-file:" + Guid.NewGuid().ToString("N");
        Store.Save(c, "financial_attachment", new()
        {
            ["id"] = attId,
            ["companyId"] = emp.S("companyId"),
            ["fileName"] = pdf ? "qiwa-contract.pdf" : png ? "qiwa-contract.png" : "qiwa-contract.jpg",
            ["contentType"] = pdf ? "application/pdf" : png ? "image/png" : "image/jpeg",
            ["dataBase64"] = Convert.ToBase64String(data),
            ["createdBy"] = ActorName(actor),
        }, tx);
        var contract = new JsonObject
        {
            ["id"] = "staff-contract:" + Guid.NewGuid().ToString("N"),
            ["employeeId"] = employeeId,
            ["companyId"] = emp.S("companyId"),
            ["issueDate"] = issue,
            ["endDate"] = end,
            ["basicSalary"] = basic,
            ["attachmentId"] = attId,
            ["fileHash"] = Convert.ToHexString(SHA256.HashData(data)),
            ["createdBy"] = ActorName(actor),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        Store.Save(c, ContractKind, contract, tx);
        Store.Audit(c, tx, "staff.contract_added", contract.S("id"), ActorName(actor));
        var pub = contract.DeepClone()!.AsObject();
        pub.Remove("dataBase64");
        pub["allowedActions"] = new JsonArray("download");
        return pub;
    }

    public static IResult DownloadContract(DbConnection c, ClaimsPrincipal actor, string employeeId, string contractId)
    {
        DemandView(actor);
        var emp = RequireEmployee(c, null, employeeId);
        ScopeCompany(actor, emp.S("companyId"));
        var contract = Store.Get(c, contractId, ContractKind) ?? throw new ApiError("العقد غير موجود.", 404);
        J.Need(contract.S("employeeId") == employeeId, "العقد لا يخص هذا الموظف.", 404);
        var file = Store.Get(c, contract.S("attachmentId"), "financial_attachment") ?? throw new ApiError("مرفق العقد غير موجود.", 404);
        FinancialScopes.DemandSponsor(actor, file.S("companyId"));
        return Results.File(Convert.FromBase64String(file.S("dataBase64")), file.S("contentType"), file.S("fileName"));
    }

    public static JsonObject UpsertMonth(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string employeeId, JsonObject input)
    {
        DemandManage(actor);
        var emp = RequireEmployee(c, tx, employeeId);
        ScopeCompany(actor, emp.S("companyId"));
        PayValues.Fields(input, "period", "attendanceDays", "overtimeHours", "latenessHours", "bonusAmount", "deductionAmount", "notes");
        string period = J.Period(input.Required("period"));
        decimal attendance = input.N("attendanceDays");
        decimal ot = input.N("overtimeHours");
        decimal late = input.N("latenessHours");
        decimal bonus = J.Money(input.N("bonusAmount"));
        decimal deduct = J.Money(input.N("deductionAmount"));
        J.Need(attendance is >= 0 and <= 31 && attendance == decimal.Truncate(attendance), "أيام الحضور بين 0 و31.");
        J.Need(ot is >= 0 and <= 200, "ساعات الإضافي غير صالحة.");
        J.Need(late is >= 0 and <= 200, "ساعات التأخير غير صالحة.");
        J.Need(bonus >= 0 && bonus <= 100000m, "المكافأة غير صالحة.");
        J.Need(deduct >= 0 && deduct <= 100000m, "الخصم اليدوي غير صالح.");
        // Block month edits while a run for this company+period is in review/approved/posted.
        bool locked = Store.List(c, RunKind, tx).Any(r =>
            r.S("companyId") == emp.S("companyId") && r.S("period") == period
            && r.S("status") is "review" or "approved" or "posted");
        J.Need(!locked, "لا تعديل للشهر أثناء مسير موظفين قيد المراجعة أو معتمد أو مُرحّل.");
        string id = "staff-month:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(employeeId + "|" + period))).ToLowerInvariant();
        var existing = Store.Get(c, id, MonthKind, tx);
        var row = new JsonObject
        {
            ["id"] = id,
            ["employeeId"] = employeeId,
            ["companyId"] = emp.S("companyId"),
            ["period"] = period,
            ["attendanceDays"] = attendance,
            ["overtimeHours"] = ot,
            ["latenessHours"] = late,
            ["bonusAmount"] = bonus,
            ["deductionAmount"] = deduct,
            ["notes"] = input.S("notes").Trim(),
            ["createdBy"] = existing?.S("createdBy") ?? ActorName(actor),
            ["createdAt"] = existing?.S("createdAt") ?? DateTimeOffset.UtcNow.ToString("O"),
            ["updatedBy"] = ActorName(actor),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["allowedActions"] = new JsonArray("edit"),
        };
        Store.Save(c, MonthKind, row, tx);
        Store.Audit(c, tx, "staff.month_upserted", id, ActorName(actor));
        return row;
    }

    /// <summary>Create an Employee advance draft (unique sourceRef). Cash moves only after a different person approves.</summary>
    public static JsonObject CreateAdvance(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string employeeId, JsonObject input)
    {
        DemandManage(actor);
        AccessPolicy.Demand(actor, "debts.create.advance");
        var emp = RequireEmployee(c, tx, employeeId);
        ScopeCompany(actor, emp.S("companyId"));
        UpsertPersonStub(c, tx, emp);
        PayValues.Fields(input, "category", "amount", "monthlyInstallment", "uniqueKey", "notes", "paymentVaultCode");
        string category = input.S("category", "CashAdvance");
        J.Need(category is "Advance" or "CashAdvance" or "BankAdvance", "فئة السلفة غير صالحة.");
        decimal amount = J.Money(input.N("amount"));
        J.Need(amount > 0 && amount <= 50000m, "مبلغ السلفة غير صالح.");
        decimal instalment = input.ContainsKey("monthlyInstallment") && input.S("monthlyInstallment") != ""
            ? J.Money(input.N("monthlyInstallment")) : amount;
        J.Need(instalment > 0 && instalment <= amount, "القسط الشهري غير صالح.");
        string uniqueKey = input.Required("uniqueKey").Trim();
        J.Need(uniqueKey.Length is >= 4 and <= 80, "المفتاح الفريد بين 4 و80 حرفًا.");
        string sourceRef = "STAFF-ADV:" + emp.S("id") + ":" + uniqueKey;
        J.Need(!Store.List(c, "debt", tx).Any(d => d.S("sourceRef") == sourceRef), "مرجع السلفة مسجّل من قبل (منع التكرار).");
        var catMeta = FinancialCore.ResolveCategory(category);
        string vault = input.S("paymentVaultCode");
        J.Need(vault != "", "صندوق/بنك الصرف مطلوب لسلفة الموظف.");
        var vaultAcc = FinancialCore.ResolveVault(vault);
        FinancialScopes.DemandAccount(actor, vault);
        string period = J.Period(DateTime.UtcNow.ToString("yyyy-MM"));
        var debt = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["iqama"] = emp.S("iqama"),
            ["riderName"] = emp.S("displayName"),
            ["debtorType"] = "Employee",
            ["category"] = category,
            ["categoryAr"] = catMeta.NameAr,
            ["sector"] = catMeta.Sector,
            ["expenseAccountCode"] = catMeta.ExpenseAccountCode,
            ["receivableAccountCode"] = "1220",
            ["allocationMode"] = "full",
            ["entryMode"] = "Disbursement",
            ["recognitionAccountCode"] = "",
            ["accountingVersion"] = AccountingEvents.Version,
            ["totalCostAmount"] = amount,
            ["companyShareAmount"] = 0m,
            ["totalAmount"] = amount,
            ["paidAmount"] = 0m,
            ["waivedAmount"] = 0m,
            ["remainingBalance"] = amount,
            ["monthlyInstallment"] = instalment,
            ["sponsorshipSponsorId"] = emp.S("companyId"),
            ["originSponsorId"] = emp.S("companyId"),
            ["creditorSponsorId"] = emp.S("companyId"),
            ["paymentVaultCode"] = vault,
            ["paymentVaultSponsorId"] = vaultAcc.DefaultSponsorId ?? emp.S("companyId"),
            ["status"] = "draft",
            ["notes"] = input.S("notes").Trim(),
            ["sourceRef"] = sourceRef,
            ["staffEmployeeId"] = emp.S("id"),
            ["effectiveFrom"] = period,
            ["postingPeriod"] = period,
            ["createdBy"] = ActorName(actor),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            // Maker never marks agreement reviewed; checker sets it on approve.
            ["agreementReviewed"] = false,
            ["journalEntryId"] = "",
        };
        Store.Save(c, "debt", debt, tx);
        Store.Audit(c, tx, "staff.advance_drafted", debt.S("id"), ActorName(actor));
        var actions = new JsonArray();
        if (AccessPolicy.Has(actor, ApprovePermission) && !AccessPolicy.SamePerson(actor, debt.S("createdBy")))
            actions.Add("approve_advance");
        return new JsonObject
        {
            ["id"] = debt.S("id"),
            ["sourceRef"] = sourceRef,
            ["remainingBalance"] = amount,
            ["monthlyInstallment"] = instalment,
            ["journalEntryId"] = "",
            ["status"] = "draft",
            ["allowedActions"] = actions,
        };
    }

    /// <summary>Second person with staff.hr.approve posts the advance journal (maker-checker; money-out needs two people).</summary>
    public static JsonObject ApproveAdvance(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string employeeId, string debtId)
    {
        DemandApprove(actor);
        var emp = RequireEmployee(c, tx, employeeId);
        ScopeCompany(actor, emp.S("companyId"));
        var debt = Store.Get(c, debtId, "debt", tx) ?? throw new ApiError("السلفة غير موجودة.", 404);
        J.Need(debt.S("staffEmployeeId") == emp.S("id") && debt.S("debtorType") == "Employee", "السلفة لا تخص هذا الموظف.");
        J.Need(debt.S("status") == "draft", "اعتماد السلفة من المسودة فقط.");
        J.Need(!AccessPolicy.SamePerson(actor, debt.S("createdBy")), "منشئ السلفة لا يعتمدها.", 403);
        string vault = debt.S("paymentVaultCode");
        var vaultAcc = FinancialCore.ResolveVault(vault);
        FinancialScopes.DemandAccount(actor, vault);
        decimal amount = debt.N("totalAmount");
        AccountingEvents.DemandAvailable(c, tx, vault, amount);
        string period = debt.S("postingPeriod", J.Period(DateTime.UtcNow.ToString("yyyy-MM")));
        var debit = FinancialCore.CreateJournalLine("1220", emp.S("companyId"), amount, 0, "سلفة موظف إداري", emp.S("iqama"), debt.S("id"));
        debit["category"] = debt.S("category");
        debit["partyType"] = "Employee";
        var credit = FinancialCore.CreateJournalLine(vault, vaultAcc.DefaultSponsorId ?? emp.S("companyId"), 0, amount, "صرف سلفة موظف", emp.S("iqama"), debt.S("id"));
        credit["category"] = debt.S("category");
        credit["partyType"] = "Employee";
        var lines = new JsonArray(debit, credit);
        var journal = FinancialCore.PostJournal(c, tx, "staff_advance", "صرف سلفة موظف", period, debt.S("sourceRef"), lines, ActorName(actor), emp.S("iqama"), debt.S("id"), emp.S("companyId"), emp.S("companyId"));
        debt["journalEntryId"] = journal.S("id");
        debt["status"] = "active";
        debt["agreementReviewed"] = true;
        debt["approvedBy"] = ActorName(actor);
        debt["approvedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, "debt", debt, tx);
        Store.Audit(c, tx, "staff.advance_approved", debt.S("id"), ActorName(actor));
        return new JsonObject
        {
            ["id"] = debt.S("id"),
            ["sourceRef"] = debt.S("sourceRef"),
            ["remainingBalance"] = debt.N("remainingBalance"),
            ["monthlyInstallment"] = debt.N("monthlyInstallment"),
            ["journalEntryId"] = journal.S("id"),
            ["status"] = "active",
            ["allowedActions"] = new JsonArray(),
        };
    }

    public static JsonObject Dashboard(DbConnection c, ClaimsPrincipal actor)
    {
        DemandView(actor);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var alerts = new JsonArray();
        foreach (var emp in Store.List(c, EmployeeKind).Where(e => e.S("status") == "active" && FinancialScopes.Sponsor(actor, e.S("companyId"))))
        {
            var contract = LatestContractAny(c, null, emp.S("id"));
            if (contract == null) continue;
            if (!DateOnly.TryParseExact(contract.S("endDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) continue;
            int days = end.DayNumber - today.DayNumber;
            string level = days < 0 ? "expired" : days <= 30 ? "30" : days <= 60 ? "60" : "";
            if (level == "") continue;
            alerts.Add(new JsonObject
            {
                ["employeeId"] = emp.S("id"),
                ["displayName"] = emp.S("displayName"),
                ["companyId"] = emp.S("companyId"),
                ["contractId"] = contract.S("id"),
                ["endDate"] = contract.S("endDate"),
                ["daysRemaining"] = days,
                ["level"] = level,
            });
        }
        var runs = Store.List(c, RunKind).Where(r => FinancialScopes.Sponsor(actor, r.S("companyId")))
            .OrderByDescending(r => r.S("period")).ThenByDescending(r => r.S("createdAt")).Take(8)
            .Select(r => (JsonNode?)new JsonObject { ["id"] = r.S("id"), ["period"] = r.S("period"), ["status"] = r.S("status"), ["companyId"] = r.S("companyId") })
            .ToArray();
        return new JsonObject
        {
            ["contractAlerts"] = alerts,
            ["recentRuns"] = new JsonArray(runs),
            ["employeeCount"] = Store.List(c, EmployeeKind).Count(e => e.S("status") == "active" && FinancialScopes.Sponsor(actor, e.S("companyId"))),
            ["allowedActions"] = ActionsDashboard(actor),
        };
    }

    static JsonArray ActionsDashboard(ClaimsPrincipal actor)
    {
        var a = new JsonArray();
        if (AccessPolicy.Has(actor, ManagePermission)) { a.Add("create_employee"); a.Add("create_run"); }
        return a;
    }

    static string LinesFingerprint(JsonArray lines)
    {
        // Stable hash of the pay snapshot so approval rejects a stale draft after month/contract edits.
        var ordered = lines.OfType<JsonObject>()
            .OrderBy(l => l.S("employeeId"), StringComparer.Ordinal)
            .Select(l => string.Join('|',
                l.S("employeeId"), l.S("contractId"),
                l.N("basicSalary").ToString("0.00", CultureInfo.InvariantCulture),
                l.N("bonusAmount").ToString("0.00", CultureInfo.InvariantCulture),
                l.N("manualDeduction").ToString("0.00", CultureInfo.InvariantCulture),
                l.N("instalmentDeduction").ToString("0.00", CultureInfo.InvariantCulture),
                l.N("net").ToString("0.00", CultureInfo.InvariantCulture),
                string.Join(',', (l["debtLines"] as JsonArray ?? []).OfType<JsonObject>()
                    .OrderBy(d => d.S("debtId"), StringComparer.Ordinal)
                    .Select(d => d.S("debtId") + ":" + d.N("amount").ToString("0.00", CultureInfo.InvariantCulture)))));
        string payload = string.Join('\n', ordered);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    static void ApplyRunLines(JsonObject run, JsonArray lines, JsonArray blocked)
    {
        run["lines"] = lines;
        run["blocked"] = blocked;
        run["blockedCount"] = blocked.Count;
        run["lineCount"] = lines.Count;
        run["grossTotal"] = lines.OfType<JsonObject>().Sum(l => l.N("gross"));
        run["netTotal"] = lines.OfType<JsonObject>().Sum(l => l.N("net"));
        run["linesFingerprint"] = LinesFingerprint(lines);
    }

    static (JsonArray Lines, JsonArray Blocked) RebuildRunPayload(DbConnection c, DbTransaction? tx, string company, string period)
    {
        var lines = new JsonArray();
        var blocked = new JsonArray();
        foreach (var emp in Store.List(c, EmployeeKind, tx).Where(e => e.S("companyId") == company && e.S("status") == "active"))
        {
            var contract = ContractForPeriod(c, tx, emp.S("id"), period);
            if (contract == null)
            {
                blocked.Add(new JsonObject
                {
                    ["employeeId"] = emp.S("id"),
                    ["iqama"] = emp.S("iqama"),
                    ["displayName"] = emp.S("displayName"),
                    ["reason"] = "no_valid_contract",
                    ["reasonAr"] = "لا عقد سارٍ يغطي شهر المسير",
                });
                continue;
            }
            lines.Add(BuildLine(c, tx, emp, period, contract));
        }
        return (lines, blocked);
    }

    static JsonArray RebuildRunLines(DbConnection c, DbTransaction? tx, string company, string period)
        => RebuildRunPayload(c, tx, company, period).Lines;

    static JsonObject BuildLine(DbConnection c, DbTransaction? tx, JsonObject emp, string period, JsonObject contract)
    {
        decimal basic = contract.N("basicSalary");
        J.Need(basic > 0, "راتب العقد الأساسي مطلوب لمسير الموظفين.");
        string monthId = "staff-month:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(emp.S("id") + "|" + period))).ToLowerInvariant();
        var month = Store.Get(c, monthId, MonthKind, tx) ?? new JsonObject
        {
            ["attendanceDays"] = 0, ["overtimeHours"] = 0, ["latenessHours"] = 0,
            ["bonusAmount"] = 0, ["deductionAmount"] = 0,
        };
        var debts = Store.List(c, "debt", tx).Where(d => d.S("iqama") == emp.S("iqama") && d.S("debtorType") == "Employee"
            && d.S("status") == "active" && d.N("remainingBalance") > 0 && d.N("monthlyInstallment") > 0).ToList();
        decimal instalmentTotal = 0m;
        var debtLines = new JsonArray();
        foreach (var d in debts)
        {
            decimal take = J.Money(Math.Min(d.N("monthlyInstallment"), d.N("remainingBalance")));
            if (take <= 0) continue;
            instalmentTotal = J.Money(instalmentTotal + take);
            debtLines.Add(new JsonObject
            {
                ["debtId"] = d.S("id"),
                ["sourceRef"] = d.S("sourceRef"),
                ["amount"] = take,
                ["category"] = d.S("category"),
            });
        }
        decimal bonus = month.N("bonusAmount");
        decimal manualDeduct = month.N("deductionAmount");
        decimal gross = J.Money(basic + bonus);
        decimal net = J.Money(gross - manualDeduct - instalmentTotal);
        J.Need(net >= 0, "صافي الموظف سالب بعد الخصوم: " + emp.S("displayName"));
        return new JsonObject
        {
            ["employeeId"] = emp.S("id"),
            ["iqama"] = emp.S("iqama"),
            ["displayName"] = emp.S("displayName"),
            ["erpEmployeeId"] = emp.S("erpEmployeeId"),
            ["contractId"] = contract.S("id"),
            ["basicSalary"] = basic,
            ["attendanceDays"] = month.N("attendanceDays"),
            ["overtimeHours"] = month.N("overtimeHours"),
            ["latenessHours"] = month.N("latenessHours"),
            ["bonusAmount"] = bonus,
            ["manualDeduction"] = manualDeduct,
            ["instalmentDeduction"] = instalmentTotal,
            ["debtLines"] = debtLines,
            ["gross"] = gross,
            ["net"] = net,
        };
    }

    public static JsonObject CreateRun(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, JsonObject input)
    {
        DemandManage(actor);
        PayValues.Fields(input, "period", "companyId");
        string period = J.Period(input.Required("period"));
        string company = FinancialCore.ResolveSponsor(input.Required("companyId")).Id;
        ScopeCompany(actor, company);
        J.Need(!Store.List(c, RunKind, tx).Any(r => r.S("companyId") == company && r.S("period") == period && r.S("status") is not ("cancelled" or "reversed")),
            "مسير موظفين لنفس الشركة والشهر موجود.");
        var (lines, blocked) = RebuildRunPayload(c, tx, company, period);
        J.Need(lines.Count > 0, "لا موظفون نشطون بعقود لهذا المسير.");
        var run = new JsonObject
        {
            ["id"] = "staff-run:" + Guid.NewGuid().ToString("N"),
            ["period"] = period,
            ["companyId"] = company,
            ["status"] = "draft",
            ["createdBy"] = ActorName(actor),
            ["preparedBy"] = ActorName(actor),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };
        ApplyRunLines(run, lines, blocked);
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_created", run.S("id"), ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    static JsonObject RequireRun(DbConnection c, DbTransaction? tx, ClaimsPrincipal actor, string id, bool write)
    {
        var run = Store.Get(c, id, RunKind, tx) ?? throw new ApiError("مسير الموظفين غير موجود.", 404);
        ScopeCompany(actor, run.S("companyId"));
        if (write) DemandManage(actor); else DemandView(actor);
        return run;
    }

    public static JsonObject GetRun(DbConnection c, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, null, actor, id, false);
        var view = run.DeepClone()!.AsObject();
        view["allowedActions"] = ActionsForRun(actor, run);
        return view;
    }

    public static JsonArray ListRuns(DbConnection c, ClaimsPrincipal actor, string? companyId)
    {
        DemandView(actor);
        return new JsonArray(Store.List(c, RunKind)
            .Where(r => companyId == null || companyId == "" || r.S("companyId") == companyId)
            .Where(r => FinancialScopes.Sponsor(actor, r.S("companyId")))
            .OrderByDescending(r => r.S("period")).ThenByDescending(r => r.S("createdAt"))
            .Select(r =>
            {
                var row = new JsonObject
                {
                    ["id"] = r.S("id"),
                    ["period"] = r.S("period"),
                    ["companyId"] = r.S("companyId"),
                    ["status"] = r.S("status"),
                    ["lineCount"] = r.N("lineCount"),
                    ["blockedCount"] = r.N("blockedCount"),
                    ["blocked"] = (r["blocked"] as JsonArray)?.DeepClone() ?? new JsonArray(),
                    ["netTotal"] = r.N("netTotal"),
                    ["createdBy"] = r.S("createdBy"),
                    ["postRefused"] = PostWasRefused(r),
                    ["postRefuseReason"] = r.S("postRefuseReason"),
                    ["mudadFileId"] = r.S("mudadFileId"),
                    ["allowedActions"] = ActionsForRun(actor, r),
                };
                return (JsonNode?)row;
            }).ToArray());
    }

    public static JsonObject Recalculate(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, tx, actor, id, true);
        J.Need(run.S("status") == "draft", "إعادة الاحتساب للمسودة فقط.");
        var rebuilt = RebuildRunPayload(c, tx, run.S("companyId"), run.S("period"));
        ApplyRunLines(run, rebuilt.Lines, rebuilt.Blocked);
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_recalculated", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    /// <summary>True when at least one pay line would produce a journal debit/credit (net or instalment).</summary>
    static bool HasPostableAmount(JsonArray lines)
        => lines.OfType<JsonObject>().Any(l => J.Money(l.N("net") + l.N("instalmentDeduction")) > 0);

    public static JsonObject SubmitReview(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, tx, actor, id, true);
        J.Need(run.S("status") == "draft", "الإرسال للمراجعة من المسودة فقط.");
        // Rebuild inside submit so the review snapshot matches current cards/contracts/instalments.
        var rebuilt = RebuildRunPayload(c, tx, run.S("companyId"), run.S("period"));
        ApplyRunLines(run, rebuilt.Lines, rebuilt.Blocked);
        J.Need(run.N("lineCount") > 0, "لا موظفون نشطون بعقود لهذا المسير.");
        J.Need(run.N("blockedCount") == 0, "يوجد موظفون محجوبون بدون عقد سارٍ؛ أصلح العقود قبل الإرسال.");
        J.Need(HasPostableAmount(rebuilt.Lines), "مسير بصفر مصروف/صافي لا يُرسل؛ القيد سيكون بلا أسطر.");
        run["status"] = "review";
        run["preparedBy"] = ActorName(actor);
        run["submittedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_submitted", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    public static JsonObject BackToDraft(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        // Do not RequireRun(write): approved return may be the approver without manage.
        var run = Store.Get(c, id, RunKind, tx) ?? throw new ApiError("مسير الموظفين غير موجود.", 404);
        ScopeCompany(actor, run.S("companyId"));
        string status = run.S("status");
        J.Need(status is "review" or "approved", "الإرجاع للمسودة من المراجعة أو بعد الاعتماد قبل الترحيل.");
        J.Need(run.S("journalEntryId") == "", "مسير مُرحّل؛ استخدم العكس لا الإرجاع للمسودة.");
        if (status == "review")
            DemandManage(actor);
        else
        {
            // Approval stays binding until post is refused, unless the approver returns it.
            bool refused = PostWasRefused(run);
            if (refused)
                J.Need(AccessPolicy.Has(actor, ManagePermission) || AccessPolicy.Has(actor, ApprovePermission),
                    "مسير معتمد بعد رفض الترحيل: يلزم إدارة أو اعتماد.", 403);
            else
                DemandApprove(actor);
        }
        // Never clear a generated Mudad without voiding the attachment in the same step.
        string mudadId = run.S("mudadFileId");
        if (mudadId != "")
        {
            var file = Store.Get(c, mudadId, "financial_attachment", tx);
            if (file != null)
            {
                file["voided"] = true;
                file["voidedAt"] = DateTimeOffset.UtcNow.ToString("O");
                file["voidedBy"] = ActorName(actor);
                file["voidReason"] = "staff_run_back_to_draft";
                Store.Save(c, "financial_attachment", file, tx);
            }
            run["mudadVoided"] = true;
            run["mudadVoidedAt"] = DateTimeOffset.UtcNow.ToString("O");
            run["mudadFileId"] = "";
            run["mudadGeneratedAt"] = "";
        }
        run["status"] = "draft";
        run["approvedBy"] = "";
        run["approvedAt"] = "";
        run["postRefused"] = false;
        run["postRefusedAt"] = "";
        run["postRefuseReason"] = "";
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_back_draft", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    /// <summary>Record a refused post outside the rolled-back post transaction so the UI can escape via back_to_draft.</summary>
    public static JsonObject MarkPostRefused(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, string reason)
    {
        var run = RequireRun(c, tx, actor, id, true);
        J.Need(run.S("status") == "approved", "تسجيل رفض الترحيل للمسير المعتمد فقط.");
        J.Need(run.S("journalEntryId") == "", "مسير مُرحّل.");
        run["postRefused"] = true;
        run["postRefusedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["postRefuseReason"] = reason.Length > 240 ? reason[..240] : reason;
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_post_refused", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    internal static bool IsPostRefuseError(ApiError err)
        => err.Message.Contains("صفر", StringComparison.Ordinal)
           || err.Message.Contains("بلا أسطر", StringComparison.Ordinal)
           || err.Message.Contains("رصيد السلفة", StringComparison.Ordinal)
           || err.Message.Contains("تغيرت ذمة", StringComparison.Ordinal)
           || err.Message.Contains("ملف مدد", StringComparison.Ordinal);

    public static JsonObject Approve(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        DemandApprove(actor);
        var run = Store.Get(c, id, RunKind, tx) ?? throw new ApiError("مسير الموظفين غير موجود.", 404);
        ScopeCompany(actor, run.S("companyId"));
        J.Need(run.S("status") == "review", "الاعتماد من حالة المراجعة فقط.");
        // Preparer never approves (brief + BR maker-checker).
        J.Need(!AccessPolicy.SamePerson(actor, run.S("createdBy")), "مجهّز المسير لا يعتمده.", 403);
        J.Need(!AccessPolicy.SamePerson(actor, run.S("preparedBy")), "مجهّز المسير لا يعتمده.", 403);
        // Reject if cards/contracts/instalments changed, or if anyone is now blocked without a covering contract.
        var livePayload = RebuildRunPayload(c, tx, run.S("companyId"), run.S("period"));
        J.Need(livePayload.Blocked.Count == 0, "يوجد موظفون محجوبون بدون عقد سارٍ؛ أعد المسير للمسودة.");
        string live = LinesFingerprint(livePayload.Lines);
        J.Need(live == run.S("linesFingerprint"), "تغيّر ملخص المسير بعد الإرسال؛ أعده للمسودة وأعد الاحتساب.");
        run["status"] = "approved";
        run["approvedBy"] = ActorName(actor);
        run["approvedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_approved", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    public static JsonObject GenerateMudad(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, tx, actor, id, true);
        J.Need(run.S("status") == "approved", "ملف مدد بعد الاعتماد فقط.");
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.Append("iqama,name,basic_salary,bonus,deductions,net,erp_employee_id\r\n");
        foreach (var line in run["lines"]!.AsArray().OfType<JsonObject>())
        {
            decimal deduct = J.Money(line.N("manualDeduction") + line.N("instalmentDeduction"));
            sb.Append(line.S("iqama")).Append(',')
                .Append(Csv(line.S("displayName"))).Append(',')
                .Append(line.N("basicSalary").ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(line.N("bonusAmount").ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(deduct.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(line.N("net").ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                .Append(line.S("erpEmployeeId")).Append("\r\n");
        }
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        string fileId = "staff-mudad:" + Guid.NewGuid().ToString("N");
        Store.Save(c, "financial_attachment", new()
        {
            ["id"] = fileId,
            ["companyId"] = run.S("companyId"),
            ["fileName"] = "staff-mudad-" + run.S("period") + ".csv",
            ["contentType"] = "text/csv; charset=utf-8",
            ["dataBase64"] = Convert.ToBase64String(bytes),
            ["createdBy"] = ActorName(actor),
        }, tx);
        run["mudadFileId"] = fileId;
        run["mudadGeneratedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["mudadVoided"] = false;
        run["mudadVoidedAt"] = "";
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_mudad", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    public static IResult DownloadMudad(DbConnection c, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, null, actor, id, false);
        J.Need(run.S("mudadFileId") != "", "ملف مدد غير مُنشأ.", 404);
        var file = Store.Get(c, run.S("mudadFileId"), "financial_attachment") ?? throw new ApiError("ملف مدد غير موجود.", 404);
        J.Need(!file.B("voided"), "ملف مدد ملغى.", 404);
        FinancialScopes.DemandSponsor(actor, file.S("companyId"));
        return Results.File(Convert.FromBase64String(file.S("dataBase64")), file.S("contentType"), file.S("fileName"));
    }

    public static JsonObject PostJournal(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        var run = RequireRun(c, tx, actor, id, true);
        J.Need(run.S("status") == "approved", "ترحيل القيد بعد الاعتماد فقط.");
        J.Need(run.S("mudadFileId") != "", "أنشئ ملف مدد قبل ترحيل القيد.");
        J.Need(run.S("journalEntryId") == "", "القيد مُرحّل مسبقًا.");
        string company = run.S("companyId");
        string period = run.S("period");
        decimal gross = run.N("grossTotal");
        decimal net = run.N("netTotal");
        decimal instalments = run["lines"]!.AsArray().OfType<JsonObject>().Sum(l => l.N("instalmentDeduction"));
        decimal manual = run["lines"]!.AsArray().OfType<JsonObject>().Sum(l => l.N("manualDeduction"));
        J.Need(J.Money(gross - instalments - manual) == net, "إجمالي المسير غير متسق.");
        // Expense = net + instalments (gross − manual). Credit 2110 with net only — never leave manual stuck on payable.
        decimal expense = J.Money(net + instalments);
        J.Need(expense == J.Money(gross - manual), "مصروف المسير غير متسق مع الخصم اليدوي.");
        J.Need(HasPostableAmount(run["lines"]!.AsArray()), "مسير بصفر مصروف/صافي؛ أعده للمسودة.");
        var lines = new JsonArray();
        // Per-employee expense/payable: current 5111 (maps to target 521101) with employee party — not rider 5301.
        foreach (var row in run["lines"]!.AsArray().OfType<JsonObject>())
        {
            string iqama = row.S("iqama");
            decimal rowNet = row.N("net");
            decimal rowExpense = J.Money(rowNet + row.N("instalmentDeduction"));
            if (rowExpense > 0)
            {
                var exp = FinancialCore.CreateJournalLine("5111", company, rowExpense, 0, "مصروف راتب موظف إداري " + period, iqama);
                exp["partyType"] = "Employee";
                lines.Add(exp);
            }
            if (rowNet > 0)
            {
                var pay = FinancialCore.CreateJournalLine("2110", company, 0, rowNet, "صافي راتب موظف مستحق " + period, iqama);
                pay["partyType"] = "Employee";
                lines.Add(pay);
            }
            // One 1220 credit per debt with debt id + category so target map clears the receivable.
            foreach (var dl in (row["debtLines"] as JsonArray ?? []).OfType<JsonObject>())
            {
                decimal take = dl.N("amount");
                if (take <= 0) continue;
                var adv = FinancialCore.CreateJournalLine("1220", company, 0, take, "استقطاع قسط سلفة موظف " + period, iqama, dl.S("debtId"));
                adv["category"] = dl.S("category");
                adv["partyType"] = "Employee";
                lines.Add(adv);
            }
        }
        J.Need(lines.Count >= 2, "القيد بلا أسطر كافية؛ أعد المسير للمسودة.");
        // Apply instalment reductions on debt balances (unique debt ids; no double post).
        foreach (var line in run["lines"]!.AsArray().OfType<JsonObject>())
        {
            foreach (var dl in (line["debtLines"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var debt = Store.Get(c, dl.S("debtId"), "debt", tx) ?? throw new ApiError("تغيرت ذمة السلفة؛ أعد الاحتساب.");
                J.Need(debt.S("debtorType") == "Employee", "الذمة ليست لموظف.");
                decimal take = dl.N("amount");
                J.Need(debt.N("remainingBalance") >= take, "رصيد السلفة لا يكفي للاستقطاع.");
                string payKey = "staff-run-instalment:" + run.S("id") + ":" + debt.S("id");
                J.Need(!Store.List(c, "ledger", tx).Any(l => l.S("sourceRef") == payKey), "استقطاع السلفة مُسجّل مسبقًا.");
                debt["remainingBalance"] = J.Money(debt.N("remainingBalance") - take);
                debt["paidAmount"] = J.Money(debt.N("paidAmount") + take);
                if (debt.N("remainingBalance") == 0) debt["status"] = "settled";
                Store.Save(c, "debt", debt, tx);
                Store.Save(c, "ledger", new()
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["iqama"] = debt.S("iqama"),
                    ["period"] = period,
                    ["kind"] = "debt_installment",
                    ["amount"] = -take,
                    ["sourceRef"] = payKey,
                    ["debtId"] = debt.S("id"),
                    ["sponsorId"] = company,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                }, tx);
            }
        }
        string reference = "STAFF-PAY:" + run.S("id");
        var journal = FinancialCore.PostJournal(c, tx, "staff_payroll", "مسير رواتب موظفين إداريين", period, reference, lines, ActorName(actor), "", "", company, company);
        run["journalEntryId"] = journal.S("id");
        run["status"] = "posted";
        run["postedBy"] = ActorName(actor);
        run["postedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["postRefused"] = false;
        run["postRefusedAt"] = "";
        run["postRefuseReason"] = "";
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_posted", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    public static JsonObject Reverse(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id)
    {
        DemandApprove(actor);
        var run = Store.Get(c, id, RunKind, tx) ?? throw new ApiError("مسير الموظفين غير موجود.", 404);
        ScopeCompany(actor, run.S("companyId"));
        J.Need(run.S("status") == "posted", "العكس لمسير مُرحّل فقط.");
        J.Need(run.S("journalEntryId") != "", "لا قيد للعكس.");
        J.Need(run.S("reversalJournalId") == "", "العكس مُنفّذ مسبقًا.");
        J.Need(!AccessPolicy.SamePerson(actor, run.S("postedBy")), "من رحّل المسير لا يعكسه.", 403);
        var original = Store.Get(c, run.S("journalEntryId"), "journal", tx) ?? throw new ApiError("القيد الأصلي غير موجود.", 404);
        var revLines = new JsonArray();
        foreach (var line in (original["lines"] as JsonArray ?? []).OfType<JsonObject>())
        {
            revLines.Add(FinancialCore.CreateJournalLine(
                line.S("accountCode"), line.S("sponsorId"),
                line.N("credit"), line.N("debit"),
                "عكس: " + line.S("note"), line.S("iqama"), line.S("debtId")));
        }
        string reference = "STAFF-PAY-REV:" + run.S("id");
        var journal = FinancialCore.PostJournal(c, tx, "staff_payroll_reversal", "عكس مسير موظفين", run.S("period"), reference, revLines, ActorName(actor), "", "", run.S("companyId"), run.S("companyId"), reversesJournalId: original.S("id"));
        // Restore instalment balances (unique ledger keys).
        foreach (var line in run["lines"]!.AsArray().OfType<JsonObject>())
        {
            foreach (var dl in (line["debtLines"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var debt = Store.Get(c, dl.S("debtId"), "debt", tx);
                if (debt == null) continue;
                decimal take = dl.N("amount");
                debt["remainingBalance"] = J.Money(debt.N("remainingBalance") + take);
                debt["paidAmount"] = J.Money(Math.Max(0, debt.N("paidAmount") - take));
                if (debt.S("status") == "settled" && debt.N("remainingBalance") > 0) debt["status"] = "active";
                Store.Save(c, "debt", debt, tx);
                string payKey = "staff-run-instalment:" + run.S("id") + ":" + debt.S("id");
                foreach (var led in Store.List(c, "ledger", tx).Where(l => l.S("sourceRef") == payKey).ToList())
                {
                    led["reversed"] = true;
                    Store.Save(c, "ledger", led, tx);
                }
            }
        }
        run["reversalJournalId"] = journal.S("id");
        run["status"] = "reversed";
        run["reversedBy"] = ActorName(actor);
        run["reversedAt"] = DateTimeOffset.UtcNow.ToString("O");
        run["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, RunKind, run, tx);
        Store.Audit(c, tx, "staff.run_reversed", id, ActorName(actor));
        run["allowedActions"] = ActionsForRun(actor, run);
        return run;
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/staff/hr/catalogs", (HttpContext ctx) => { DemandView(ctx.User); return Catalogs(); })
            .WithTags("Staff HR").WithSummary("Read staff HR pick-lists");
        api.MapGet("/staff/hr/dashboard", (HttpContext ctx) => store.Read(c => Dashboard(c, ctx.User)))
            .WithTags("Staff HR").WithSummary("Read staff HR dashboard with contract expiry alerts");
        api.MapGet("/staff/hr/employees", (HttpContext ctx) => store.Read(c => ListEmployees(c, ctx.User, ctx.Request.Query["companyId"], ctx.Request.Query["status"])))
            .WithTags("Staff HR").WithSummary("List staff employees");
        api.MapPost("/staff/hr/employees", (JsonObject input, HttpContext ctx) => store.Write((c, tx) => CreateEmployee(c, tx, ctx.User, input)))
            .WithTags("Staff HR").WithSummary("Create staff employee card");
        api.MapGet("/staff/hr/employees/{id}", (string id, HttpContext ctx) => store.Read(c => GetEmployee(c, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Read staff employee card");
        api.MapPut("/staff/hr/employees/{id}", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => UpdateEmployee(c, tx, ctx.User, id, input)))
            .WithTags("Staff HR").WithSummary("Update staff employee card");
        api.MapPost("/staff/hr/employees/{id}/contracts", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => AddContract(c, tx, ctx.User, id, input)))
            .WithTags("Staff HR").WithSummary("Add Qiwa contract (keeps history)");
        api.MapGet("/staff/hr/employees/{id}/contracts/{contractId}/file", (string id, string contractId, HttpContext ctx) => store.Read(c => DownloadContract(c, ctx.User, id, contractId)))
            .WithTags("Staff HR").WithSummary("Download Qiwa contract file");
        api.MapPut("/staff/hr/employees/{id}/months", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => UpsertMonth(c, tx, ctx.User, id, input)))
            .WithTags("Staff HR").WithSummary("Upsert monthly attendance/OT/lateness/bonus/deduction");
        api.MapPost("/staff/hr/employees/{id}/advances", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) => CreateAdvance(c, tx, ctx.User, id, input)))
            .WithTags("Staff HR").WithSummary("Create staff advance draft (unique key; no cash until approve)");
        api.MapPost("/staff/hr/employees/{id}/advances/{debtId}/approve", (string id, string debtId, HttpContext ctx) => store.Write((c, tx) => ApproveAdvance(c, tx, ctx.User, id, debtId)))
            .WithTags("Staff HR").WithSummary("Approve staff advance and post cash journal (second person)");
        api.MapGet("/staff/hr/runs", (HttpContext ctx) => store.Read(c => ListRuns(c, ctx.User, ctx.Request.Query["companyId"])))
            .WithTags("Staff HR").WithSummary("List staff payroll runs");
        api.MapPost("/staff/hr/runs", (JsonObject input, HttpContext ctx) => store.Write((c, tx) => CreateRun(c, tx, ctx.User, input)))
            .WithTags("Staff HR").WithSummary("Create staff payroll run draft");
        api.MapGet("/staff/hr/runs/{id}", (string id, HttpContext ctx) => store.Read(c => GetRun(c, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Read staff payroll run");
        api.MapPost("/staff/hr/runs/{id}/recalculate", (string id, HttpContext ctx) => store.Write((c, tx) => Recalculate(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Recalculate staff run draft");
        api.MapPost("/staff/hr/runs/{id}/submit-review", (string id, HttpContext ctx) => store.Write((c, tx) => SubmitReview(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Submit staff run for review");
        api.MapPost("/staff/hr/runs/{id}/back-to-draft", (string id, HttpContext ctx) => store.Write((c, tx) => BackToDraft(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Return staff run to draft");
        api.MapPost("/staff/hr/runs/{id}/approve", (string id, HttpContext ctx) => store.Write((c, tx) => Approve(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Approve staff run (preparer never approves)");
        api.MapPost("/staff/hr/runs/{id}/mudad", (string id, HttpContext ctx) => store.Write((c, tx) => GenerateMudad(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Generate staff Mudad CSV");
        api.MapGet("/staff/hr/runs/{id}/mudad", (string id, HttpContext ctx) => store.Read(c => DownloadMudad(c, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Download staff Mudad CSV");
        api.MapPost("/staff/hr/runs/{id}/post", (string id, HttpContext ctx) =>
            {
                try
                {
                    return store.Write((c, tx) => PostJournal(c, tx, ctx.User, id));
                }
                catch (ApiError ex) when (IsPostRefuseError(ex))
                {
                    // Persist refuse flag after the post transaction rolled back, so back_to_draft unlocks.
                    try { store.Write((c, tx) => MarkPostRefused(c, tx, ctx.User, id, ex.Message)); }
                    catch { /* keep original post error if mark fails */ }
                    throw;
                }
            })
            .WithTags("Staff HR").WithSummary("Post one journal for the staff run");
        api.MapPost("/staff/hr/runs/{id}/reverse", (string id, HttpContext ctx) => store.Write((c, tx) => Reverse(c, tx, ctx.User, id)))
            .WithTags("Staff HR").WithSummary("Reverse staff run journal");
    }
}
