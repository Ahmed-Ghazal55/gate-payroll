using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Payroll;

public static class Debts
{
    public static readonly string[] Categories =
    [
        // الفئات التقليدية المتوافقة مع الاختبارات السابقة
        "Advance", "TrafficFine", "EquipmentLoan", "VehicleInstallment", "AdministrativeDeduction",
        // بنود الموارد البشرية (HR) الثمانية + الإضافية
        "WorkPermit", "IqamaRenewal", "SponsorshipTransfer", "ExitReEntryVisa", "MedicalInsurance",
        "ProfessionChange", "DriverLicense", "DriverCard", "OtherHrService",
        // السلف النقدية والبنكية والعهد
        "CashAdvance", "BankAdvance", "CashCustody",
        // الحركة والأسطول والمنصات والسكن
        "AccidentDamage", "FuelOverage", "PlatformTransferredDebt", "NegativeSalaryCarryover", "HousingDeduction"
    ];

    public static decimal PaidAmount(JsonObject debt)=>debt.N("paidAmount",Math.Max(0m,debt.N("totalAmount")-debt.N("remainingBalance")-debt.N("waivedAmount")));
    public static decimal Amount(JsonObject input, string key)
    {
        J.Need(decimal.TryParse(input.S(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n > 0 && n < 100000000 && n == J.Money(n), "قيمة مالية غير صالحة: " + key);
        return n;
    }

    public static decimal OptionalAmount(JsonObject input, string key, decimal fallback = 0m)
    {
        var raw = input.S(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        J.Need(decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < 100000000 && n == J.Money(n), "قيمة مالية غير صالحة: " + key);
        return n;
    }

    public static void PostCreationJournalIfNeeded(DbConnection c, DbTransaction tx, JsonObject debt, string actor)
    {
        if (!string.IsNullOrWhiteSpace(debt.S("journalEntryId"))) return;

        var cat = FinancialCore.ResolveCategory(debt.S("category"));
        string allocationMode = debt.S("allocationMode", "RiderFull");
        decimal totalCost = debt.N("totalCostAmount", debt.N("totalAmount"));
        decimal companyShare = debt.N("companyShareAmount", 0m);
        decimal riderShare = debt.N("totalAmount", totalCost - companyShare);
        string originSpId = debt.S("originSponsorId");
        string creditorSpId = debt.S("creditorSponsorId", originSpId);
        string vaultCode = debt.S("paymentVaultCode", FinancialCore.DefaultVaultForSponsor(originSpId));
        var vaultAcc = FinancialCore.ResolveVault(vaultCode);
        var vaultSp = FinancialCore.ResolveSponsor(vaultAcc.DefaultSponsorId ?? originSpId);
        string receivCode = debt.S("debtorType") == "Employee" ? "1220" : cat.ReceivableAccountCode;

        var lines = new JsonArray();
        if (companyShare > 0)
        {
            lines.Add(FinancialCore.CreateJournalLine(cat.ExpenseAccountCode, originSpId, companyShare, 0m, $"تحمل الشركة لبند ({cat.NameAr})", debt.S("iqama"), debt.S("id")));
        }
        if (riderShare > 0)
        {
            lines.Add(FinancialCore.CreateJournalLine(receivCode, creditorSpId, riderShare, 0m, $"إثبات ذمة مدينة على المندوب/الموظف لبند ({cat.NameAr})", debt.S("iqama"), debt.S("id")));
        }
        lines.Add(FinancialCore.CreateJournalLine(vaultAcc.Code, vaultSp.Id, 0m, totalCost, $"سداد/صرف مقابل ({cat.NameAr}) من ({vaultAcc.NameAr})", debt.S("iqama"), debt.S("id")));

        if (creditorSpId != vaultSp.Id && riderShare > 0)
        {
            var credSp = FinancialCore.ResolveSponsor(creditorSpId);
            lines.Add(FinancialCore.CreateJournalLine("1310", vaultSp.Id, riderShare, 0m, $"إثبات حق لسجل ({vaultSp.ShortNameAr}) طرف ({credSp.ShortNameAr}) مقابل تمويل ذمة المندوب", debt.S("iqama"), debt.S("id")));
            lines.Add(FinancialCore.CreateJournalLine("1310", credSp.Id, 0m, riderShare, $"إثبات التزام على ({credSp.ShortNameAr}) لصالح ({vaultSp.ShortNameAr}) مقابل تمويل ذمة المندوب", debt.S("iqama"), debt.S("id")));
        }

        var journal = FinancialCore.PostJournal(
            c, tx,
            allocationMode == "CompanyFull" ? "company_expense" : "debt_creation",
            $"قيد إثبات ({cat.NameAr}) — تحمل ({allocationMode}) — مرجع ({debt.S("sourceRef")})",
            debt.S("effectiveFrom", DateTime.UtcNow.ToString("yyyy-MM")),
            "DEBT-INIT-" + debt.S("sourceRef"),
            lines,
            actor,
            debt.S("iqama"),
            debt.S("id"),
            originSpId,
            creditorSpId
        );
        debt["journalEntryId"] = journal.S("id");
        debt["journalNumber"] = journal.S("journalNumber");
        Store.Save(c, "debt", debt, tx);
    }

    public static void Map(RouteGroupBuilder api, Store store)
    {
        api.MapGet("/debts", (string? sponsorId, string? category, string? status, string? iqama) => store.Read(c =>
        {
            var list = Store.List(c, "debt");
            if (!string.IsNullOrWhiteSpace(sponsorId))
            {
                var spId = FinancialCore.ResolveSponsor(sponsorId).Id;
                list = list.Where(d => FinancialCore.AssignedDebtSponsor(d)?.Id == spId || FinancialCore.FindSponsor(d.S("originSponsorId"))?.Id == spId).ToList();
            }
            if (!string.IsNullOrWhiteSpace(category)) list = list.Where(d => d.S("category") == category).ToList();
            if (!string.IsNullOrWhiteSpace(status)) list = list.Where(d => d.S("status") == status).ToList();
            if (!string.IsNullOrWhiteSpace(iqama)) list = list.Where(d => d.S("iqama") == iqama.Trim()).ToList();
            return list;
        }));

        api.MapGet("/debts/settlements", (string? debtId, string? iqama) => store.Read(c =>
        {
            var list = Store.List(c, "debt_settlement");
            if (!string.IsNullOrWhiteSpace(debtId)) list = list.Where(s => s.S("debtId") == debtId).ToList();
            if (!string.IsNullOrWhiteSpace(iqama)) list = list.Where(s => s.S("iqama") == iqama.Trim()).ToList();
            return list;
        }));

        api.MapPost("/debts", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            string iqama = DirectoryProfiles.Iqama(input.Required("iqama"));
            var rider = Store.Get(c, iqama, "rider", tx);
            J.Need(rider != null, "المندوب غير موجود.");

            string category = input.Required("category");
            J.Need(Categories.Contains(category), "فئة دين غير صالحة.");
            var catMeta = FinancialCore.ResolveCategory(category);

            string allocationMode = input.S("allocationMode", "RiderFull"); // RiderFull | CompanyFull | SharedSplit
            J.Need(allocationMode is "RiderFull" or "CompanyFull" or "SharedSplit", "نوع التحمل غير صالح: استخدم RiderFull أو CompanyFull أو SharedSplit.");

            decimal totalRiderShare;
            decimal companyShare = OptionalAmount(input, "companyShareAmount", 0m);
            decimal totalCost;

            if (allocationMode == "CompanyFull")
            {
                totalCost = input.ContainsKey("totalCostAmount") ? Amount(input, "totalCostAmount") : Amount(input, "totalAmount");
                companyShare = totalCost;
                totalRiderShare = 0m;
            }
            else if (allocationMode == "SharedSplit")
            {
                if (input.ContainsKey("totalCostAmount") && companyShare > 0)
                {
                    totalCost = Amount(input, "totalCostAmount");
                    J.Need(companyShare < totalCost, "حصة الشركة في التحمل الجزئي يجب أن تكون أقل من إجمالي التكلفة.");
                    totalRiderShare = J.Money(totalCost - companyShare);
                }
                else
                {
                    totalRiderShare = Amount(input, "totalAmount");
                    J.Need(companyShare > 0, "حدد مبلغ تحمل الشركة في التحمل الجزئي.");
                    totalCost = J.Money(totalRiderShare + companyShare);
                }
            }
            else
            {
                totalRiderShare = Amount(input, "totalAmount");
                companyShare = 0m;
                totalCost = totalRiderShare;
            }

            decimal installment = totalRiderShare > 0
                ? (input.ContainsKey("monthlyInstallment") && !string.IsNullOrWhiteSpace(input.S("monthlyInstallment")) ? Amount(input, "monthlyInstallment") : totalRiderShare)
                : 0m;
            J.Need(installment <= totalRiderShare, "القسط يتجاوز إجمالي الدين.");
            J.Need(input.B("agreementReviewed"), "يلزم تأكيد مراجعة اتفاق الاستقطاع.");

            string reference = input.Required("sourceRef");
            J.Need(!Store.List(c, "debt", tx).Any(d => d.S("sourceRef") == reference), "مرجع الدين مسجل من قبل.");

            var sponsorshipSp = FinancialCore.ResolveSponsor(input.S("sponsorshipSponsorId", rider!.S("sponsor")));
            var originSp = FinancialCore.ResolveSponsor(input.S("originSponsorId", sponsorshipSp.Id));
            var creditorSp = FinancialCore.ResolveSponsor(input.S("creditorSponsorId", originSp.Id));
            string vaultCode = input.S("paymentVaultCode", FinancialCore.DefaultVaultForSponsor(originSp.Id, category == "BankAdvance" ? "BANK" : "MAIN"));
            FinancialCore.ResolveVault(vaultCode);

            bool requireMutualApproval = input.B("requireMutualApproval");
            string initiatingDept = input.S("initiatingDepartment", catMeta.Sector == "HR" ? "HR" : "FIN");
            string targetApprovalDept = initiatingDept == "HR" ? "FIN" : "HR";
            string effectivePeriod = J.Period(input.S("effectiveFrom", DateTime.UtcNow.ToString("yyyy-MM")));
            string status = requireMutualApproval ? "pending_approval" : (totalRiderShare > 0 ? "active" : "settled");

            var d = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["iqama"] = iqama,
                ["riderName"] = rider.S("name"),
                ["debtorType"] = input.S("debtorType", rider.S("operationalRole", "Rider").Contains("موظف") ? "Employee" : "Rider"),
                ["category"] = category,
                ["categoryAr"] = catMeta.NameAr,
                ["sector"] = catMeta.Sector,
                ["expenseAccountCode"] = catMeta.ExpenseAccountCode,
                ["receivableAccountCode"] = catMeta.ReceivableAccountCode,
                ["allocationMode"] = allocationMode,
                ["totalCostAmount"] = totalCost,
                ["companyShareAmount"] = companyShare,
                ["totalAmount"] = totalRiderShare,
                ["paidAmount"] = 0m,
                ["waivedAmount"] = 0m,
                ["remainingBalance"] = totalRiderShare,
                ["monthlyInstallment"] = installment,
                ["sponsorshipSponsorId"] = sponsorshipSp.Id,
                ["sponsorshipSponsorNameAr"] = sponsorshipSp.ShortNameAr,
                ["originSponsorId"] = originSp.Id,
                ["originSponsorNameAr"] = originSp.ShortNameAr,
                ["creditorSponsorId"] = creditorSp.Id,
                ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                ["paymentVaultCode"] = vaultCode,
                ["initiatingDepartment"] = initiatingDept,
                ["requireMutualApproval"] = requireMutualApproval,
                ["status"] = status,
                ["notes"] = input.S("notes"),
                ["sourceRef"] = reference,
                ["effectiveFrom"] = effectivePeriod,
                ["createdBy"] = ctx.User.Identity!.Name!,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };

            Store.Save(c, "debt", d, tx);

            if (requireMutualApproval)
            {
                var approval = FinancialCore.CreateMutualApproval(
                    c, tx,
                    "debt_creation",
                    $"اعتماد قيد ({catMeta.NameAr}) للمندوب {rider.S("name")} ({iqama})",
                    $"طلب وارد من قسم ({initiatingDept}) لإثبات ({catMeta.NameAr}) بإجمالي تكلفة {totalCost:0.00} ر.س (تحمل الشركة: {companyShare:0.00} ر.س | ذمة المندوب: {totalRiderShare:0.00} ر.س) على ({creditorSp.ShortNameAr}).",
                    initiatingDept,
                    targetApprovalDept,
                    2,
                    d.S("id"),
                    new JsonObject { ["debtId"] = d.S("id"), ["iqama"] = iqama, ["totalCostAmount"] = totalCost, ["riderShare"] = totalRiderShare },
                    ctx.User.Identity!.Name!
                );
                d["approvalRequestId"] = approval.S("id");
                Store.Save(c, "debt", d, tx);
            }
            else
            {
                PostCreationJournalIfNeeded(c, tx, d, ctx.User.Identity!.Name!);
            }

            Store.Audit(c, tx, "debt.created", d.S("id"), ctx.User.Identity!.Name!);
            return d;
        }));

        api.MapPut("/debts/{id}", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var d = Store.Get(c, id, "debt", tx) ?? throw new ApiError("الدين غير موجود.", 404);
            J.Need(d.S("status") is "active" or "paused", "المديونية تنتظر الاعتماد أو ملغاة؛ لا يمكن إعادة جدولتها.");
            J.Need(d.N("remainingBalance") > 0, "الدين مسدد.");
            string status = input.S("status", d.S("status"));
            J.Need(status is "active" or "paused", "تسديد الدين يتم بقيد مالي معتمد.");
            if (input.ContainsKey("monthlyInstallment"))
            {
                J.Need(input.B("agreementReviewed"), "راجع اتفاق إعادة الجدولة.");
                decimal newInst = Amount(input, "monthlyInstallment");
                J.Need(newInst <= d.N("remainingBalance"), "القسط الجديد يتجاوز الرصيد المتبقي.");
                d["monthlyInstallment"] = newInst;
            }
            if(!string.IsNullOrWhiteSpace(input.S("creditorSponsorId")))
            {
                if(!ctx.User.IsInRole("Admin"))throw new ApiError("ربط الدين القديم يتطلب مسؤول النظام.",403);
                J.Need(FinancialCore.AssignedDebtSponsor(d)==null&&input.B("agreementReviewed")&&input.ContainsKey("expectedRemainingBalance")&&input.N("expectedRemainingBalance")==d.N("remainingBalance"),"تغير الدين أو سبق ربطه؛ أعد المراجعة.");
                var sponsor=FinancialCore.ResolveSponsor(input.Required("creditorSponsorId"));d["creditorSponsorId"]=sponsor.Id;d["creditorSponsorNameAr"]=sponsor.ShortNameAr;
                Store.Audit(c,tx,"debt.sponsor_assigned",id,ctx.User.Identity!.Name!);
            }
            d["status"] = status;
            d["notes"] = input.S("notes", d.S("notes"));
            d["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");
            Store.Save(c, "debt", d, tx);
            Store.Audit(c, tx, "debt.rescheduled", id, ctx.User.Identity!.Name!);
            return d;
        }));

        // تحصيل مباشر (نقدي أو بنكي، كلي أو جزئي) + إمكانية إعفاء إداري مشروط لجزء من المديونية + إيداع في صندوق أي سجل
        api.MapPost("/debts/{id}/collect", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var d = Store.Get(c, id, "debt", tx) ?? throw new ApiError("الدين غير موجود.", 404);
            decimal remaining = d.N("remainingBalance");
            J.Need(remaining > 0 && d.S("status") is "active" or "paused", "الدين غير نشط أو مسدد بالكامل.");

            decimal collected = OptionalAmount(input, "collectedAmount", 0m);
            decimal waived = OptionalAmount(input, "waivedAmount", 0m);
            decimal totalReduction = J.Money(collected + waived);
            J.Need(totalReduction > 0, "أدخل مبلغ التحصيل أو مبلغ الإعفاء الإداري.");
            J.Need(totalReduction <= remaining, $"إجمالي السداد والإعفاء ({totalReduction:0.00}) يتجاوز الرصيد المتبقي ({remaining:0.00}).");

            string receiptRef = input.Required("receiptRef");
            J.Need(!Store.List(c, "debt_settlement", tx).Any(s => s.S("receiptRef") == receiptRef), "رقم سند التحصيل/التسوية مسجل من قبل.");

            var creditorSp = FinancialCore.RequireDebtSponsor(d);
            string targetVaultCode = input.S("targetVaultCode", FinancialCore.DefaultVaultForSponsor(creditorSp.Id, "MAIN"));
            var vaultAcc = FinancialCore.ResolveVault(targetVaultCode);
            var collectionSp = FinancialCore.ResolveSponsor(vaultAcc.DefaultSponsorId ?? input.S("collectionSponsorId", creditorSp.Id));
            string receivCode = d.S("receivableAccountCode", "1210");
            string period = J.Period(input.S("period", DateTime.UtcNow.ToString("yyyy-MM")));

            var lines = new JsonArray();
            if (collected > 0)
            {
                lines.Add(FinancialCore.CreateJournalLine(vaultAcc.Code, collectionSp.Id, collected, 0m, $"قبض وتوريد نقدي/بنكي إلى ({vaultAcc.NameAr})", d.S("iqama"), id));
            }
            if (waived > 0)
            {
                lines.Add(FinancialCore.CreateJournalLine("5202", creditorSp.Id, waived, 0m, $"إعفاء وتنازل إداري معتمد عن جزء من المديونية ({input.S("waiverReason", "تسوية سداد")})", d.S("iqama"), id));
            }
            lines.Add(FinancialCore.CreateJournalLine(receivCode, creditorSp.Id, 0m, totalReduction, $"تخفيض الذمة المدينة للمندوب ({d.S("riderName")}) بالسداد/الإعفاء", d.S("iqama"), id));

            if (collected > 0 && collectionSp.Id != creditorSp.Id)
            {
                lines.Add(FinancialCore.CreateJournalLine("1310", creditorSp.Id, collected, 0m, $"إثبات حق لسجل ({creditorSp.ShortNameAr}) طرف ({collectionSp.ShortNameAr}) الذي قبض المديونية نيابة عنه", d.S("iqama"), id));
                lines.Add(FinancialCore.CreateJournalLine("1310", collectionSp.Id, 0m, collected, $"إثبات التزام على ({collectionSp.ShortNameAr}) لصالح ({creditorSp.ShortNameAr}) مقابل التحصيل بالنيابة", d.S("iqama"), id));
            }

            var journal = FinancialCore.PostJournal(
                c, tx,
                waived > 0 && collected > 0 ? "collection_with_waiver" : (waived > 0 ? "administrative_waiver" : "cash_collection"),
                $"سند تحصيل/تسوية مديونية ({d.S("categoryAr", d.S("category"))}) — محصل: {collected:0.00} ر.س — إعفاء إداري: {waived:0.00} ر.س",
                period,
                receiptRef,
                lines,
                ctx.User.Identity!.Name!,
                d.S("iqama"),
                id,
                collectionSp.Id,
                creditorSp.Id
            );

            d["paidAmount"] = J.Money(PaidAmount(d) + collected);
            d["waivedAmount"] = J.Money(d.N("waivedAmount") + waived);
            d["remainingBalance"] = J.Money(remaining - totalReduction);
            if (d.N("remainingBalance") == 0m) d["status"] = "settled";
            d["lastCollectionAt"] = DateTimeOffset.UtcNow.ToString("O");
            Store.Save(c, "debt", d, tx);

            var settlement = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["debtId"] = id,
                ["iqama"] = d.S("iqama"),
                ["riderName"] = d.S("riderName"),
                ["category"] = d.S("category"),
                ["creditorSponsorId"] = creditorSp.Id,
                ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                ["collectionSponsorId"] = collectionSp.Id,
                ["collectionSponsorNameAr"] = collectionSp.ShortNameAr,
                ["targetVaultCode"] = vaultAcc.Code,
                ["targetVaultNameAr"] = vaultAcc.NameAr,
                ["method"] = input.S("method", vaultAcc.Code.StartsWith("BANK", StringComparison.Ordinal) ? "bank_transfer" : "cash_collection"),
                ["collectedAmount"] = collected,
                ["waivedAmount"] = waived,
                ["totalReduction"] = totalReduction,
                ["remainingAfter"] = d.N("remainingBalance"),
                ["receiptRef"] = receiptRef,
                ["period"] = period,
                ["collectionDate"] = input.S("collectionDate", DateTime.UtcNow.ToString("yyyy-MM-dd")),
                ["notes"] = input.S("notes"),
                ["journalEntryId"] = journal.S("id"),
                ["journalNumber"] = journal.S("journalNumber"),
                ["createdBy"] = ctx.User.Identity!.Name!,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c, "debt_settlement", settlement, tx);

            // في حال كان التحصيل من HR، ننشئ إشعار علم وتصديق للمالية (والعكس)
            string collectorDept = input.S("collectorDepartment", "FIN");
            if (collectorDept == "HR")
            {
                FinancialCore.CreateMutualApproval(
                    c, tx,
                    "collection_verification",
                    $"تصديق تحصيل مديونية وارد من HR — سند ({receiptRef})",
                    $"قام قسم HR بتحصيل مبلغ {collected:0.00} ر.س (وإعفاء {waived:0.00} ر.س) من المندوب {d.S("riderName")} ({d.S("iqama")}) وإيداعه في ({vaultAcc.NameAr}). يرجى مطابقة الصندوق والتصديق.",
                    "HR", "FIN", 1, settlement.S("id"),
                    new JsonObject { ["settlementId"] = settlement.S("id"), ["debtId"] = id, ["collectedAmount"] = collected },
                    ctx.User.Identity!.Name!
                );
            }

            Store.Audit(c, tx, "debt.collected", id, ctx.User.Identity!.Name!);
            return new { debt = d, settlement, journal };
        }));

        // شراء المديونية / ترحيل ملكية المديونية مالياً من سجل تجاري إلى سجل تجاري آخر
        api.MapPost("/debts/{id}/transfer-sponsor", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            var d = Store.Get(c, id, "debt", tx) ?? throw new ApiError("الدين غير موجود.", 404);
            decimal remaining = d.N("remainingBalance");
            J.Need(remaining > 0&&d.S("status") is "active" or "paused", "لا يمكن نقل مديونية غير نشطة أو مسددة.");

            var oldSp = FinancialCore.RequireDebtSponsor(d);
            var newSp = FinancialCore.ResolveSponsor(input.Required("newCreditorSponsorId"));
            J.Need(oldSp.Id != newSp.Id, "السجل المشتري للمديونية يجب أن يكون مختلفاً عن السجل الدائن الحالي.");

            string transferRef = input.Required("transferRef");
            string receivCode = d.S("receivableAccountCode", "1210");
            string settlementMode = input.S("settlementMode", "intercompany"); // intercompany | cash_purchase

            var lines = new JsonArray
            {
                FinancialCore.CreateJournalLine(receivCode, newSp.Id, remaining, 0m, $"إثبات شراء/استحواذ ({newSp.ShortNameAr}) على مديونية المندوب ({d.S("iqama")})", d.S("iqama"), id),
                FinancialCore.CreateJournalLine(receivCode, oldSp.Id, 0m, remaining, $"إقفال وتنازل ({oldSp.ShortNameAr}) عن مديونية المندوب لصالح ({newSp.ShortNameAr})", d.S("iqama"), id)
            };

            if (settlementMode == "cash_purchase")
            {
                string buyerVault = FinancialCore.DefaultVaultForSponsor(newSp.Id, "MAIN");
                string sellerVault = FinancialCore.DefaultVaultForSponsor(oldSp.Id, "MAIN");
                lines.Add(FinancialCore.CreateJournalLine(sellerVault, oldSp.Id, remaining, 0m, $"قبض ثمن بيع المديونية من ({newSp.ShortNameAr})", d.S("iqama"), id));
                lines.Add(FinancialCore.CreateJournalLine(buyerVault, newSp.Id, 0m, remaining, $"سداد ثمن شراء المديونية إلى ({oldSp.ShortNameAr})", d.S("iqama"), id));
            }
            else
            {
                lines.Add(FinancialCore.CreateJournalLine("1310", oldSp.Id, remaining, 0m, $"إثبات مديونية على ({newSp.ShortNameAr}) مقابل شراء ذمة المندوب", d.S("iqama"), id));
                lines.Add(FinancialCore.CreateJournalLine("1310", newSp.Id, 0m, remaining, $"إثبات التزام لصالح ({oldSp.ShortNameAr}) مقابل شراء ذمة المندوب", d.S("iqama"), id));
            }

            var journal = FinancialCore.PostJournal(
                c, tx,
                "intercompany_debt_transfer",
                $"شراء وترحيل مديونية المندوب ({d.S("iqama")}) من ({oldSp.ShortNameAr}) إلى ({newSp.ShortNameAr})",
                DateTime.UtcNow.ToString("yyyy-MM"),
                transferRef,
                lines,
                ctx.User.Identity!.Name!,
                d.S("iqama"),
                id,
                oldSp.Id,
                newSp.Id
            );

            d["previousCreditorSponsorId"] = oldSp.Id;
            d["previousCreditorSponsorNameAr"] = oldSp.ShortNameAr;
            d["creditorSponsorId"] = newSp.Id;
            d["creditorSponsorNameAr"] = newSp.ShortNameAr;
            d["transferredAt"] = DateTimeOffset.UtcNow.ToString("O");
            d["transferJournalId"] = journal.S("id");
            Store.Save(c, "debt", d, tx);

            Store.Audit(c, tx, "debt.transferred_sponsor", id, ctx.User.Identity!.Name!);
            return new { debt = d, journal };
        }));

        // قالب إكسل التحصيل المجمع (بالإقامة والاسم والمبلغ المحصل والإعفاء والصندوق)
        api.MapGet("/debts/bulk-collect/template", () => store.Read(c =>
        {
            var activeDebts = Store.List(c, "debt").Where(d => d.S("status") == "active" && d.N("remainingBalance") > 0).Take(10).ToList();
            var vaultChoices = FinancialCore.ChartOfAccounts.Where(a => a.IsVault).Select(a => a.Code).ToArray();
            var cols = new List<ExcelColumn>
            {
                new("رقم الإقامة", false, null, true),
                new("اسم المندوب / الموظف"),
                new("المبلغ المحصل", true),
                new("مبلغ الإعفاء الإداري", true),
                new("رمز الصندوق أو البنك", false, vaultChoices),
                new("رقم السند / المرجع"),
                new("ملاحظات التسوية")
            };
            var rows = new List<object?[]>();
            int seq = 1;
            foreach (var d in activeDebts)
            {
                rows.Add(
                [
                    d.S("iqama"),
                    d.S("riderName"),
                    d.N("monthlyInstallment"),
                    0m,
                    d.S("paymentVaultCode", "MAIN.BW.SET"),
                    $"BLK-{DateTime.UtcNow:yyyyMMdd}-{seq++:D3}",
                    $"تحصيل قسط ({d.S("categoryAr", d.S("category"))})"
                ]);
            }
            if (rows.Count == 0)
            {
                rows.Add(["2491234567", "مثال: محمد أحمد", 500m, 0m, "MAIN.BW.SET", "BLK-202610-001", "سداد دفعة نقدية للصندوق الداخلي"]);
            }
            return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "قالب_التحصيل_المجمع_للمديونيات.xlsx");
        }));

        // معاينة ملف Excel أو CSV لشيت التحصيل المجمع قبل الحفظ
        api.MapPost("/debts/bulk-collect/preview-file", (JsonObject input,HttpContext ctx) => store.Read(c =>
        {
            string filename = Path.GetFileName(input.Required("filename"));
            byte[] bytes = FinancialImports.FileBytes(input);
            var sheets = Imports.Read(bytes, filename);
            J.Need(sheets.Count > 0 && sheets[0].Rows.Count > 1, "الملف فارغ أو لا يحتوي على صفوف بيانات.");
            J.Need(sheets[0].Rows[0].SequenceEqual(new[]{"رقم الإقامة","اسم المندوب / الموظف","المبلغ المحصل","مبلغ الإعفاء الإداري","رمز الصندوق أو البنك","رقم السند / المرجع","ملاحظات التسوية"})||sheets[0].Rows[0].SequenceEqual(new[]{"iqama","riderName","collectedAmount","waivedAmount","targetVaultCode","receiptRef","notes"}),"أعمدة الملف لا تطابق القالب المعتمد؛ نزّل القالب دون إعادة ترتيب العناوين.");

            J.Need(sheets[0].Rows.Count<=1001,"حد دفعة التحصيل 1000 صف.");
            string defaultVault = input.S("defaultVaultCode", "MAIN.BW.SET");
            var rows = new JsonArray();
            var errors = new JsonArray();
            var sheetRows = sheets[0].Rows;
            var seenReceipts=new HashSet<string>(StringComparer.Ordinal);var reserved=new Dictionary<string,decimal>(StringComparer.Ordinal);
            var existingReceipts=Store.List(c,"debt_settlement").Select(x=>x.S("batchReceiptRef",x.S("receiptRef"))).ToHashSet(StringComparer.Ordinal);
            var allDebts = Store.List(c, "debt").Where(d => d.S("status") is "active" or "paused" && d.N("remainingBalance") > 0).ToList();

            for (int i = 1; i < sheetRows.Count; i++)
            {
                var r = sheetRows[i];
                if (r.All(string.IsNullOrWhiteSpace)) continue;
                try
                {
                    string Cell(int idx) => idx < r.Count ? r[idx].Trim() : "";
                    string iqama = DirectoryProfiles.Iqama(Cell(0));
                    string name = Cell(1);
                    decimal collected = string.IsNullOrWhiteSpace(Cell(2)) ? 0m : decimal.Parse(J.Digits(Cell(2)), CultureInfo.InvariantCulture);
                    decimal waived = string.IsNullOrWhiteSpace(Cell(3)) ? 0m : decimal.Parse(J.Digits(Cell(3)), CultureInfo.InvariantCulture);
                    string vault = string.IsNullOrWhiteSpace(Cell(4)) ? defaultVault : Cell(4);
                    string receiptRef = string.IsNullOrWhiteSpace(Cell(5)) ? $"BLK-{DateTime.UtcNow:yyyyMMdd}-{i:D3}" : Cell(5);
                    string notes = Cell(6);
                    J.Need(!existingReceipts.Contains(receiptRef)&&seenReceipts.Add(receiptRef),"مرجع تحصيل مكرر.");

                    FinancialCore.ResolveVault(vault);
                    J.Need(collected>=0&&waived>=0&&collected==J.Money(collected)&&waived==J.Money(waived),"مبالغ غير صالحة.");
                    J.Need(allDebts.Where(d=>d.S("iqama")==iqama).All(d=>FinancialCore.AssignedDebtSponsor(d)!=null),"راجع ربط السجل التجاري للديون القديمة قبل التحصيل.");
                    decimal riderRemaining = allDebts.Where(d => d.S("iqama") == iqama).Sum(d => d.N("remainingBalance"));
                    J.Need(collected + waived > 0, "المبلغ المحصل والإعفاء يساويان صفراً.");
                    J.Need(riderRemaining > 0, $"لا توجد مديونية نشطة على الإقامة ({iqama}).");
                    decimal requested=reserved.GetValueOrDefault(iqama)+collected+waived;reserved[iqama]=requested;
                    J.Need(requested <= riderRemaining, $"المبلغ ({collected + waived:0.00}) يتجاوز مديونية المندوب ({riderRemaining:0.00}).");

                    rows.Add(new JsonObject
                    {
                        ["row"] = i + 1,
                        ["iqama"] = iqama,
                        ["riderName"] = string.IsNullOrWhiteSpace(name) ? (allDebts.FirstOrDefault(d => d.S("iqama") == iqama)?.S("riderName") ?? "") : name,
                        ["collectedAmount"] = J.Money(collected),
                        ["waivedAmount"] = J.Money(waived),
                        ["riderRemainingBefore"] = J.Money(riderRemaining),
                        ["targetVaultCode"] = vault,
                        ["receiptRef"] = receiptRef,
                        ["notes"] = notes
                    });
                }
                catch (Exception e)
                {
                    errors.Add($"الصف {i + 1}: {e.Message}");
                }
            }
            return new { filename, validCount = rows.Count, errorCount = errors.Count, rows, errors, fingerprint=errors.Count==0?FinancialImports.Issue(c,"bulk",rows,ctx.User.Identity!.Name!):"" };
        }));

        // اعتماد شيت التحصيل المجمع (سواء من جدول مباشر أو بعد قراءة ملف Excel/CSV)
        api.MapPost("/debts/bulk-collect/commit", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            FinancialImports.Verify(c,tx,input,"bulk",ctx.User.Identity!.Name!);
            var items = input["rows"]?.AsArray() ?? throw new ApiError("أرسل صفوف التحصيل المجمع في الحقل rows.");
            J.Need(items.Count > 0 && items.Count <= 1000, "عدد صفوف التحصيل المجمع يجب أن يكون بين 1 و 1000 صف.");

            string defaultVault = input.S("defaultVaultCode", "MAIN.BW.SET");
            string period = J.Period(input.S("period", DateTime.UtcNow.ToString("yyyy-MM")));
            var results = new JsonArray();
            decimal totalCollectedBatch = 0m, totalWaivedBatch = 0m;

            int rowIdx = 1;
            foreach (var node in items)
            {
                var r = node!.AsObject();
                string iqama = DirectoryProfiles.Iqama(r.Required("iqama"));
                decimal collected = OptionalAmount(r, "collectedAmount", 0m);
                decimal waived = OptionalAmount(r, "waivedAmount", 0m);
                decimal rowTotal = J.Money(collected + waived);
                J.Need(rowTotal > 0, $"الصف {rowIdx}: أدخل مبلغاً محصلاً أو إعفاءً أكبر من صفر.");

                string vaultCode = r.S("targetVaultCode", defaultVault);
                var vaultAcc = FinancialCore.ResolveVault(vaultCode);
                string baseRef = r.S("receiptRef", $"BLK-{DateTime.UtcNow:yyyyMMddHHmm}-{rowIdx:D3}");
                J.Need(!Store.List(c,"debt_settlement",tx).Any(x=>x.S("batchReceiptRef",x.S("receiptRef"))==baseRef),"مرجع التحصيل مكرر.");

                // جلب مديونيات المندوب النشطة مرتبة من الأقدم للأحدث لتوزيع المبلغ المحصل عليها
                var riderDebts = Store.List(c, "debt", tx)
                    .Where(d => d.S("iqama") == iqama && d.S("status") is "active" or "paused" && d.N("remainingBalance") > 0)
                    .OrderBy(d => d.S("createdAt"))
                    .ToList();

                decimal totalAvailableDebt = riderDebts.Sum(d => d.N("remainingBalance"));
                J.Need(riderDebts.Count > 0 && rowTotal <= totalAvailableDebt, $"الصف {rowIdx}: المبلغ ({rowTotal:0.00}) يتجاوز إجمالي مديونية المندوب ({iqama}) البالغة ({totalAvailableDebt:0.00}).");

                decimal remCollect = collected;
                decimal remWaive = waived;
                int subIdx = 1;

                foreach (var d in riderDebts)
                {
                    if (remCollect <= 0 && remWaive <= 0) break;
                    decimal dRem = d.N("remainingBalance");
                    decimal takeCollect = Math.Min(dRem, remCollect);
                    decimal takeWaive = Math.Min(dRem - takeCollect, remWaive);
                    decimal takeTotal = J.Money(takeCollect + takeWaive);
                    if (takeTotal <= 0) continue;

                    remCollect = J.Money(remCollect - takeCollect);
                    remWaive = J.Money(remWaive - takeWaive);

                    var creditorSp = FinancialCore.RequireDebtSponsor(d);
                    var collectionSp = FinancialCore.ResolveSponsor(vaultAcc.DefaultSponsorId ?? creditorSp.Id);
                    string receivCode = d.S("receivableAccountCode", "1210");
                    string subRef = subIdx == 1 ? baseRef : $"{baseRef}-{subIdx}";
                    subIdx++;

                    var lines = new JsonArray();
                    if (takeCollect > 0) lines.Add(FinancialCore.CreateJournalLine(vaultAcc.Code, collectionSp.Id, takeCollect, 0m, "تحصيل مجمع إلى الخزينة", iqama, d.S("id")));
                    if (takeWaive > 0) lines.Add(FinancialCore.CreateJournalLine("5202", creditorSp.Id, takeWaive, 0m, "إعفاء إداري ضمن شيت التحصيل المجمع", iqama, d.S("id")));
                    lines.Add(FinancialCore.CreateJournalLine(receivCode, creditorSp.Id, 0m, takeTotal, "تخفيض ذمة مدينة ضمن شيت التحصيل المجمع", iqama, d.S("id")));

                    if (takeCollect > 0 && collectionSp.Id != creditorSp.Id)
                    {
                        lines.Add(FinancialCore.CreateJournalLine("1310", creditorSp.Id, takeCollect, 0m, "مقاصة تحصيل مجمع بين السجلات", iqama, d.S("id")));
                        lines.Add(FinancialCore.CreateJournalLine("1310", collectionSp.Id, 0m, takeCollect, "مقاصة تحصيل مجمع بين السجلات", iqama, d.S("id")));
                    }

                    var journal = FinancialCore.PostJournal(c, tx, "bulk_collection", $"تحصيل مجمع ({subRef}) للمندوب ({iqama})", period, subRef, lines, ctx.User.Identity!.Name!, iqama, d.S("id"), collectionSp.Id, creditorSp.Id);

                    d["paidAmount"] = J.Money(PaidAmount(d) + takeCollect);
                    d["waivedAmount"] = J.Money(d.N("waivedAmount") + takeWaive);
                    d["remainingBalance"] = J.Money(dRem - takeTotal);
                    if (d.N("remainingBalance") == 0m) d["status"] = "settled";
                    Store.Save(c, "debt", d, tx);

                    var settlement = new JsonObject
                    {
                        ["id"] = Guid.NewGuid().ToString("N"),
                        ["debtId"] = d.S("id"),
                        ["iqama"] = iqama,
                        ["riderName"] = d.S("riderName", r.S("riderName")),
                        ["category"] = d.S("category"),
                        ["creditorSponsorId"] = creditorSp.Id,
                        ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                        ["collectionSponsorId"] = collectionSp.Id,
                        ["targetVaultCode"] = vaultAcc.Code,
                        ["method"] = "bulk_sheet",
                        ["collectedAmount"] = takeCollect,
                        ["waivedAmount"] = takeWaive,
                        ["totalReduction"] = takeTotal,
                        ["remainingAfter"] = d.N("remainingBalance"),
                        ["receiptRef"] = subRef,
                        ["batchReceiptRef"] = baseRef,
                        ["period"] = period,
                        ["collectionDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ["notes"] = r.S("notes", "شيت تحصيل مجمع"),
                        ["journalEntryId"] = journal.S("id"),
                        ["journalNumber"] = journal.S("journalNumber"),
                        ["createdBy"] = ctx.User.Identity!.Name!,
                        ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
                    };
                    Store.Save(c, "debt_settlement", settlement, tx);
                    results.Add(settlement);
                }

                totalCollectedBatch = J.Money(totalCollectedBatch + collected);
                totalWaivedBatch = J.Money(totalWaivedBatch + waived);
                rowIdx++;
            }

            Store.Audit(c, tx, "debt.bulk_collect", $"rows:{items.Count}", ctx.User.Identity!.Name!);
            return new
            {
                processedRows = items.Count,
                settlementsCreated = results.Count,
                totalCollectedBatch,
                totalWaivedBatch,
                settlements = results
            };
        }));

        // ==========================================================================================================
        // شيت رفع واستيراد أرصدة أول المدة للمديونيات والبنود والحسابات مع بيان مالي تفصيلي قبل التحميل والاعتماد
        // ==========================================================================================================
        api.MapGet("/debts/import-opening/template", () =>
        {
            var catChoices = FinancialCore.CategoriesCatalog.Select(x => x.Code).ToArray();
            var spChoices = new[] { "BW", "EX", "TR" };
            var allocChoices = new[] { "RiderFull", "SharedSplit", "CompanyFull" };
            var modeChoices = new[] { "OpeningBalance", "Disbursement" };
            var contraChoices = new[] { "3101" }.Concat(FinancialCore.ChartOfAccounts.Where(a => a.IsVault).Select(a => a.Code)).ToArray();

            var cols = new List<ExcelColumn>
            {
                new("رقم الإقامة (10 أرقام)", false, null, true),
                new("اسم المندوب / الموظف"),
                new("تصنيف المدين (Rider/Employee)", false, new[] { "Rider", "Employee" }),
                new("كود البند أو الخدمة (Category)", false, catChoices),
                new("السجل التجاري الدائن (BW/EX/TR)", false, spChoices),
                new("نوع القيد (OpeningBalance/Disbursement)", false, modeChoices),
                new("آلية التحمل (RiderFull/SharedSplit/CompanyFull)", false, allocChoices),
                new("إجمالي مبلغ المديونية على المندوب", true),
                new("المسدد سابقاً (إن وجد)", true),
                new("حصة تحمل الشركة (في حال SharedSplit)", true),
                new("القسط الشهري المستقطع", true),
                new("شهر بداية الاستقطاع (YYYY-MM)"),
                new("الحساب الدائن المقابل (3101 أو رمز الصندوق)", false, contraChoices),
                new("رقم السند / المرجع"),
                new("ملاحظات البيان")
            };

            string curMonth = DateTime.UtcNow.ToString("yyyy-MM");
            var rows = new List<object?[]>
            {
                new object?[] { "2491234567", "مثال: محمد أحمد علي", "Rider", "Advance", "BW", "OpeningBalance", "RiderFull", 4500m, 1000m, 0m, 500m, curMonth, "3101", "OB-2026-001", "رصيد أول المدة — سلفة نقدية سابقة على سجل البوابا" },
                new object?[] { "2519876543", "مثال: عبد الله سالم", "Rider", "WorkPermit", "EX", "OpeningBalance", "SharedSplit", 5000m, 0m, 4600m, 1000m, curMonth, "3101", "OB-2026-002", "رصيد أول المدة — رخصة عمل بتحمل مشترك على إكسبرس" },
                new object?[] { "2381122334", "مثال: فهد الحربي", "Rider", "TrafficFine", "TR", "OpeningBalance", "RiderFull", 900m, 0m, 0m, 300m, curMonth, "3101", "OB-2026-003", "رصيد أول المدة — مخالفة مرورية على سجل النقل والتجارة" }
            };
            return Results.File(ExcelFiles.Write(cols, rows), ExcelFiles.Mime, "قالب_استيراد_أرصدة_أول_المدة_والمديونيات.xlsx");
        });

        api.MapPost("/debts/import-opening/preview", (JsonObject input,HttpContext ctx) => store.Read(c =>
        {
            string filename = Path.GetFileName(input.Required("filename"));
            byte[] bytes = FinancialImports.FileBytes(input);
            var sheets = Imports.Read(bytes, filename);
            J.Need(sheets.Count > 0 && sheets[0].Rows.Count > 1, "الملف فارغ أو لا يحتوي على صفوف بيانات.");
            J.Need(sheets[0].Rows[0].SequenceEqual(new[]{"رقم الإقامة (10 أرقام)","اسم المندوب / الموظف","تصنيف المدين (Rider/Employee)","كود البند أو الخدمة (Category)","السجل التجاري الدائن (BW/EX/TR)","نوع القيد (OpeningBalance/Disbursement)","آلية التحمل (RiderFull/SharedSplit/CompanyFull)","إجمالي مبلغ المديونية على المندوب","المسدد سابقاً (إن وجد)","حصة تحمل الشركة (في حال SharedSplit)","القسط الشهري المستقطع","شهر بداية الاستقطاع (YYYY-MM)","الحساب الدائن المقابل (3101 أو رمز الصندوق)","رقم السند / المرجع","ملاحظات البيان"})||sheets[0].Rows[0].SequenceEqual(new[]{"iqama","riderName","debtorType","category","creditorSponsor","entryMode","allocationMode","totalAmount","priorPaidAmount","companyShareAmount","monthlyInstallment","effectiveFrom","contraAccountCode","sourceRef","notes"}),"أعمدة الملف لا تطابق القالب المعتمد؛ نزّل القالب دون إعادة ترتيب العناوين.");

            J.Need(sheets[0].Rows.Count<=2001,"حد دفعة الأرصدة 2000 صف.");
            var existingRiders = Store.List(c, "rider").ToDictionary(r => r.S("id"));
            var existingRefs = Store.List(c, "debt").Select(d => d.S("sourceRef")).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
            var seenRefs = new HashSet<string>();

            var validRows = new JsonArray();
            var errors = new JsonArray();
            var sheetRows = sheets[0].Rows;
            string curMonth = DateTime.UtcNow.ToString("yyyy-MM");

            for (int i = 1; i < sheetRows.Count; i++)
            {
                var r = sheetRows[i];
                if (r.All(string.IsNullOrWhiteSpace)) continue;
                try
                {
                    string Cell(int idx) => idx < r.Count ? r[idx].Trim() : "";
                    string iqama = DirectoryProfiles.Iqama(Cell(0));
                    string name = string.IsNullOrWhiteSpace(Cell(1))
                        ? (existingRiders.TryGetValue(iqama, out var exR) ? exR.S("name") : $"مندوب {iqama}")
                        : Cell(1);
                    string debtorType = Cell(2).Equals("Employee", StringComparison.OrdinalIgnoreCase) || Cell(2).Contains("موظف") ? "Employee" : "Rider";

                    string rawCat = string.IsNullOrWhiteSpace(Cell(3)) ? "Advance" : Cell(3);
                    var catMeta = FinancialCore.CategoriesCatalog.FirstOrDefault(x =>
                        x.Code.Equals(rawCat, StringComparison.OrdinalIgnoreCase) ||
                        x.NameAr.Contains(rawCat, StringComparison.OrdinalIgnoreCase) ||
                        rawCat.Contains(x.NameAr, StringComparison.OrdinalIgnoreCase))
                        ?? FinancialCore.ResolveCategory(rawCat);

                    var creditorSp = FinancialCore.ResolveSponsor(string.IsNullOrWhiteSpace(Cell(4)) ? "BW" : Cell(4));
                    string entryMode = Cell(5).Equals("Disbursement", StringComparison.OrdinalIgnoreCase) || Cell(5).Contains("صرف") ? "Disbursement" : "OpeningBalance";
                    string allocationMode = Cell(6) switch
                    {
                        var s when s.Equals("SharedSplit", StringComparison.OrdinalIgnoreCase) || s.Contains("جزئي") || s.Contains("مشترك") => "SharedSplit",
                        var s when s.Equals("CompanyFull", StringComparison.OrdinalIgnoreCase) || s.Contains("شركة") => "CompanyFull",
                        _ => "RiderFull"
                    };

                    decimal totalAmount = string.IsNullOrWhiteSpace(Cell(7)) ? 0m : decimal.Parse(J.Digits(Cell(7)), CultureInfo.InvariantCulture);
                    decimal priorPaid = string.IsNullOrWhiteSpace(Cell(8)) ? 0m : decimal.Parse(J.Digits(Cell(8)), CultureInfo.InvariantCulture);
                    decimal companyShare = string.IsNullOrWhiteSpace(Cell(9)) ? 0m : decimal.Parse(J.Digits(Cell(9)), CultureInfo.InvariantCulture);
                    J.Need(new[]{totalAmount,priorPaid,companyShare}.All(n=>n>=0&&n<100000000&&n==J.Money(n)),"مبالغ الرصيد غير صالحة.");
                    decimal remaining = J.Money(totalAmount - priorPaid);
                    decimal installment = string.IsNullOrWhiteSpace(Cell(10)) ? Math.Min(remaining > 0 ? remaining : 1m, 500m) : J.Money(decimal.Parse(J.Digits(Cell(10)), CultureInfo.InvariantCulture));
                    string effectiveFrom = string.IsNullOrWhiteSpace(Cell(11)) ? curMonth : J.Period(Cell(11));

                    string defaultContra = entryMode == "OpeningBalance" ? "3101" : FinancialCore.DefaultVaultForSponsor(creditorSp.Id, "MAIN");
                    string contraCode = string.IsNullOrWhiteSpace(Cell(12)) ? defaultContra : Cell(12);
                    var contraAcc = FinancialCore.ResolveAccount(contraCode);

                    string sourceRef = string.IsNullOrWhiteSpace(Cell(13)) ? $"OB-{DateTime.UtcNow:yyyyMMdd}-{i:D4}" : Cell(13);
                    string notes = Cell(14);

                    if (allocationMode != "CompanyFull")
                    {
                        J.Need(totalAmount > 0, "إجمالي مبلغ المديونية على المندوب يجب أن يكون أكبر من صفر.");
                        J.Need(priorPaid >= 0 && priorPaid < totalAmount, "المبلغ المسدد سابقاً يجب أن يكون أقل من إجمالي المديونية.");
                        J.Need(installment > 0 && installment <= remaining, $"القسط الشهري ({installment:0.00}) يجب أن يكون بين 0.01 والرصيد المتبقي ({remaining:0.00}).");
                    }
                    else
                    {
                        J.Need(companyShare > 0 || totalAmount > 0, "أدخل مبلغ تحمل الشركة.");
                        if (companyShare == 0m) companyShare = totalAmount;
                        totalAmount = 0m; priorPaid = 0m; remaining = 0m;
                    }

                    J.Need(!existingRefs.Contains(sourceRef) && seenRefs.Add(sourceRef), $"رقم المرجع ({sourceRef}) مكرر أو مسجل مسبقاً.");

                    string receivCode = debtorType == "Employee" ? "1220" : catMeta.ReceivableAccountCode;
                    var receivAcc = FinancialCore.ResolveAccount(receivCode);
                    var expAcc = FinancialCore.ResolveAccount(catMeta.ExpenseAccountCode);

                    validRows.Add(new JsonObject
                    {
                        ["row"] = i + 1,
                        ["iqama"] = iqama,
                        ["riderName"] = name,
                        ["riderExists"] = existingRiders.ContainsKey(iqama),
                        ["debtorType"] = debtorType,
                        ["category"] = catMeta.Code,
                        ["categoryAr"] = catMeta.NameAr,
                        ["sector"] = catMeta.Sector,
                        ["creditorSponsorId"] = creditorSp.Id,
                        ["creditorSponsorCode"] = creditorSp.Code,
                        ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                        ["entryMode"] = entryMode,
                        ["entryModeAr"] = entryMode == "OpeningBalance" ? "رصيد أول المدة (مرحل)" : "صرف جديد من الخزينة",
                        ["allocationMode"] = allocationMode,
                        ["totalAmount"] = totalAmount,
                        ["priorPaidAmount"] = priorPaid,
                        ["remainingBalance"] = remaining,
                        ["companyShareAmount"] = companyShare,
                        ["totalTransactionAmount"] = J.Money(remaining + companyShare),
                        ["monthlyInstallment"] = installment,
                        ["effectiveFrom"] = effectiveFrom,
                        ["receivableAccountCode"] = receivAcc.Code,
                        ["receivableAccountNameAr"] = receivAcc.NameAr,
                        ["expenseAccountCode"] = expAcc.Code,
                        ["expenseAccountNameAr"] = expAcc.NameAr,
                        ["contraAccountCode"] = contraAcc.Code,
                        ["contraAccountNameAr"] = contraAcc.NameAr,
                        ["sourceRef"] = sourceRef,
                        ["notes"] = notes
                    });
                }
                catch (Exception e)
                {
                    errors.Add($"الصف {i + 1}: {e.Message}");
                }
            }

            var parsedList = validRows.OfType<JsonObject>().ToList();

            // 1. ملخص البيان حسب السجل التجاري الدائن
            var summaryBySponsor = parsedList
                .GroupBy(x => x.S("creditorSponsorId"))
                .Select(g =>
                {
                    var sp = FinancialCore.ResolveSponsor(g.Key);
                    return new
                    {
                        sponsorId = sp.Id,
                        shortCode = sp.Code,
                        nameAr = sp.NameAr,
                        shortNameAr = sp.ShortNameAr,
                        unifiedNumber = sp.UnifiedNumber,
                        rowsCount = g.Count(),
                        uniqueRidersCount = g.Select(x => x.S("iqama")).Distinct().Count(),
                        totalOriginalDebt = J.Money(g.Sum(x => x.N("totalAmount"))),
                        totalPriorPaid = J.Money(g.Sum(x => x.N("priorPaidAmount"))),
                        totalOpeningRemaining = J.Money(g.Sum(x => x.N("remainingBalance"))),
                        totalCompanyShare = J.Money(g.Sum(x => x.N("companyShareAmount")))
                    };
                }).ToList();

            // 2. ملخص البيان حسب تصنيف البند والحسابات المحاسبية
            var summaryByCategory = parsedList
                .GroupBy(x => x.S("category"))
                .Select(g =>
                {
                    var first = g.First();
                    return new
                    {
                        categoryCode = g.Key,
                        categoryAr = first.S("categoryAr"),
                        sector = first.S("sector"),
                        receivableAccountCode = first.S("receivableAccountCode"),
                        receivableAccountNameAr = first.S("receivableAccountNameAr"),
                        expenseAccountCode = first.S("expenseAccountCode"),
                        expenseAccountNameAr = first.S("expenseAccountNameAr"),
                        rowsCount = g.Count(),
                        totalOpeningRemaining = J.Money(g.Sum(x => x.N("remainingBalance"))),
                        totalCompanyShare = J.Money(g.Sum(x => x.N("companyShareAmount")))
                    };
                }).ToList();

            // 3. ملخص القيد المحاسبي الإجمالي المتوقع قبل الترحيل
            decimal totalDebits = J.Money(parsedList.Sum(x => x.N("remainingBalance") + x.N("companyShareAmount")));
            decimal totalCredits = totalDebits;

            return new
            {
                filename,
                fingerprint=errors.Count==0?FinancialImports.Issue(c,"opening",validRows,ctx.User.Identity!.Name!):"",
                validCount = validRows.Count,
                errorCount = errors.Count,
                newRidersToCreateCount = parsedList.Count(x => !x.B("riderExists")),
                totalOriginalDebt = J.Money(parsedList.Sum(x => x.N("totalAmount"))),
                totalPriorPaid = J.Money(parsedList.Sum(x => x.N("priorPaidAmount"))),
                totalOpeningRemaining = J.Money(parsedList.Sum(x => x.N("remainingBalance"))),
                totalCompanyExpenseShare = J.Money(parsedList.Sum(x => x.N("companyShareAmount"))),
                totalDebits,
                totalCredits,
                isBalanced = totalDebits == totalCredits,
                summaryBySponsor,
                summaryByCategory,
                rows = validRows,
                errors
            };
        }));

        api.MapPost("/debts/import-opening/commit", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
        {
            FinancialImports.Verify(c,tx,input,"opening",ctx.User.Identity!.Name!);
            var items = input["rows"]?.AsArray() ?? throw new ApiError("أرسل صفوف الاستيراد المعاينة في الحقل rows.");
            J.Need(items.Count > 0 && items.Count <= 2000, "عدد صفوف الاستيراد يجب أن يكون بين 1 و 2000 صف.");

            var createdDebts = new JsonArray();
            var createdJournals = new JsonArray();
            decimal totalRemainingImported = 0m, totalCompanyExpenseImported = 0m;
            int autoCreatedRiders = 0;

            foreach (var node in items)
            {
                var r = node!.AsObject();
                string iqama = DirectoryProfiles.Iqama(r.Required("iqama"));
                string riderName = r.S("riderName", iqama);
                var creditorSp = FinancialCore.ResolveSponsor(r.S("creditorSponsorId", "BW"));

                // إذا لم يكن المندوب مسجلاً بعد في دليل المناديب، ننشئ له ملفاً تلقائياً مع ربطه بالسجل التجاري
                var existingRider = Store.Get(c, iqama, "rider", tx);
                if (existingRider == null)
                {
                    string cleanName = new string(riderName.Where(ch => !char.IsDigit(ch)).ToArray()).Trim();
                    if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "مندوب رصيد افتتاحي";
                    var newRider = RiderDirectory.Normalize(new JsonObject
                    {
                        ["iqama"] = iqama,
                        ["name"] = cleanName,
                        ["sponsor"] = creditorSp.NameAr,
                        ["sponsorId"] = creditorSp.Id,
                        ["status"] = "NeedsReview"
                    });
                    Store.Save(c, "rider", newRider, tx);
                    autoCreatedRiders++;
                }

                string catCode = r.S("category", "Advance");
                var catMeta = FinancialCore.ResolveCategory(catCode);
                string debtorType = r.S("debtorType", "Rider");
                string entryMode = r.S("entryMode", "OpeningBalance");
                string allocationMode = r.S("allocationMode", "RiderFull");

                decimal totalAmount = J.Money(r.N("totalAmount"));
                decimal priorPaid = J.Money(r.N("priorPaidAmount"));
                decimal remaining = J.Money(totalAmount - priorPaid);
                decimal companyShare = J.Money(r.N("companyShareAmount"));
                decimal installment = J.Money(r.N("monthlyInstallment", Math.Min(remaining > 0 ? remaining : 1m, 500m)));
                string effectiveFrom = J.Period(r.S("effectiveFrom", DateTime.UtcNow.ToString("yyyy-MM")));
                string sourceRef = r.Required("sourceRef");
                J.Need(!Store.List(c,"debt",tx).Any(d=>d.S("sourceRef")==sourceRef),"مرجع الدين مكرر؛ أعد المعاينة.");

                string receivCode = debtorType == "Employee" ? "1220" : catMeta.ReceivableAccountCode;
                string expCode = catMeta.ExpenseAccountCode;
                string contraCode = r.S("contraAccountCode", entryMode == "OpeningBalance" ? "3101" : FinancialCore.DefaultVaultForSponsor(creditorSp.Id, "MAIN"));
                var contraAcc = FinancialCore.ResolveAccount(contraCode);

                string debtId = Guid.NewGuid().ToString("N");
                var lines = new JsonArray();
                if (remaining > 0)
                {
                    lines.Add(FinancialCore.CreateJournalLine(receivCode, creditorSp.Id, remaining, 0m, $"إثبات رصيد أول المدة / ذمة مدينة ({catMeta.NameAr}) على ({riderName})", iqama, debtId));
                }
                if (companyShare > 0)
                {
                    lines.Add(FinancialCore.CreateJournalLine(expCode, creditorSp.Id, companyShare, 0m, $"إثبات حصة تحمل الشركة ({catMeta.NameAr}) للمندوب ({riderName})", iqama, debtId));
                }
                decimal creditTotal = J.Money(remaining + companyShare);
                lines.Add(FinancialCore.CreateJournalLine(contraAcc.Code, creditorSp.Id, 0m, creditTotal, $"الطرف الدائن المقابل ({contraAcc.NameAr}) — مرجع ({sourceRef})", iqama, debtId));

                var journal = FinancialCore.PostJournal(
                    c, tx,
                    entryMode == "OpeningBalance" ? "opening_balance_debt" : "debt_recognition",
                    $"{(entryMode == "OpeningBalance" ? "رصيد أول المدة" : "إثبات مديونية")} — {catMeta.NameAr} — {riderName} ({iqama})",
                    effectiveFrom,
                    sourceRef,
                    lines,
                    ctx.User.Identity!.Name!,
                    iqama,
                    debtId,
                    creditorSp.Id,
                    creditorSp.Id
                );

                var doc = new JsonObject
                {
                    ["id"] = debtId,
                    ["iqama"] = iqama,
                    ["riderName"] = riderName,
                    ["debtorType"] = debtorType,
                    ["category"] = catMeta.Code,
                    ["categoryAr"] = catMeta.NameAr,
                    ["sector"] = catMeta.Sector,
                    ["allocationMode"] = allocationMode,
                    ["entryMode"] = entryMode,
                    ["originSponsorId"] = creditorSp.Id,
                    ["originSponsorNameAr"] = creditorSp.ShortNameAr,
                    ["creditorSponsorId"] = creditorSp.Id,
                    ["creditorSponsorCode"] = creditorSp.Code,
                    ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                    ["paymentVaultCode"] = contraAcc.Code,
                    ["receivableAccountCode"] = receivCode,
                    ["expenseAccountCode"] = expCode,
                    ["totalCostAmount"] = J.Money(totalAmount + companyShare),
                    ["companyShareAmount"] = companyShare,
                    ["totalAmount"] = totalAmount,
                    ["paidAmount"] = priorPaid,
                    ["waivedAmount"] = 0m,
                    ["remainingBalance"] = remaining,
                    ["monthlyInstallment"] = installment,
                    ["effectiveFrom"] = effectiveFrom,
                    ["sourceRef"] = sourceRef,
                    ["notes"] = r.S("notes", "استيراد رصيد أول المدة"),
                    ["agreementReviewed"] = true,
                    ["status"] = remaining > 0 ? "active" : "settled",
                    ["journalEntryId"] = journal.S("id"),
                    ["journalNumber"] = journal.S("journalNumber"),
                    ["createdBy"] = ctx.User.Identity!.Name!,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
                };
                Store.Save(c, "debt", doc, tx);
                createdDebts.Add(doc);
                createdJournals.Add(journal);
                totalRemainingImported = J.Money(totalRemainingImported + remaining);
                totalCompanyExpenseImported = J.Money(totalCompanyExpenseImported + companyShare);
            }

            Store.Audit(c, tx, "debt.opening_imported", $"rows:{items.Count},rem:{totalRemainingImported}", ctx.User.Identity!.Name!);
            return new
            {
                importedCount = createdDebts.Count,
                autoCreatedRiders,
                totalRemainingImported,
                totalCompanyExpenseImported
            };
        }));
    }

    public static void Post(DbConnection c, DbTransaction tx, PayrollResult result, string runId)
    {
        foreach (var line in result.Lines.Where(x => x.Category == "debt_installment"))
        {
            var d = Store.Get(c, line.Source, "debt", tx) ?? throw new ApiError("تغير الدين؛ أعد المعاينة.");
            decimal amount = -line.Amount;
            J.Need(amount > 0 && amount <= d.N("remainingBalance") && d.S("status") == "active", "تغير رصيد الدين؛ أعد المعاينة.");
            string key = "debt:" + line.Source + ":" + result.Period;
            J.Need(Store.Get(c, key, "ledger", tx) == null, "قسط الفترة مرحل بالفعل.");

            d["paidAmount"] = J.Money(PaidAmount(d) + amount);
            d["remainingBalance"] = J.Money(d.N("remainingBalance") - amount);
            if (d.N("remainingBalance") == 0) d["status"] = "settled";
            d["lastDeductionPeriod"] = result.Period;
            Store.Save(c, "debt", d, tx);

            Store.Save(c, "ledger", new()
            {
                ["id"] = key,
                ["iqama"] = result.Iqama,
                ["period"] = result.Period,
                ["amount"] = -amount,
                ["kind"] = "debt_installment",
                ["debtId"] = line.Source,
                ["sourceRef"] = runId,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            }, tx);

            if(FinancialCore.AssignedDebtSponsor(d)==null)continue; // Legacy payroll ledger remains authoritative until a reviewed sponsor link exists.
            // تسجيل القيد المحاسبي المزدوج لاستقطاع القسط من الراتب (من ح/ ذمم دائنة رواتب 2110 إلى ح/ ذمم مدينة مناديب 1210)
            var creditorSp = FinancialCore.RequireDebtSponsor(d);
            string receivCode = d.S("receivableAccountCode", "1210");
            var jLines = new JsonArray
            {
                FinancialCore.CreateJournalLine("2110", creditorSp.Id, amount, 0m, $"استقطاع قسط مديونية ({d.S("categoryAr", d.S("category"))}) من راتب فترة ({result.Period})", result.Iqama, d.S("id")),
                FinancialCore.CreateJournalLine(receivCode, creditorSp.Id, 0m, amount, $"تخفيض الذمة المدينة للمندوب باستقطاع الراتب لفترة ({result.Period})", result.Iqama, d.S("id"))
            };
            var journal = FinancialCore.PostJournal(c, tx, "payroll_deduction", $"استقطاع قسط راتب ({result.Period}) للمندوب ({result.Iqama})", result.Period, key, jLines, "payroll_engine", result.Iqama, d.S("id"), creditorSp.Id, creditorSp.Id);

            Store.Save(c, "debt_settlement", new()
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["debtId"] = d.S("id"),
                ["iqama"] = result.Iqama,
                ["riderName"] = d.S("riderName"),
                ["category"] = d.S("category"),
                ["creditorSponsorId"] = creditorSp.Id,
                ["creditorSponsorNameAr"] = creditorSp.ShortNameAr,
                ["targetVaultCode"] = "2110",
                ["targetVaultNameAr"] = "استقطاع مباشر من مسير الرواتب",
                ["method"] = "payroll_deduction",
                ["collectedAmount"] = amount,
                ["waivedAmount"] = 0m,
                ["totalReduction"] = amount,
                ["remainingAfter"] = d.N("remainingBalance"),
                ["receiptRef"] = key,
                ["period"] = result.Period,
                ["collectionDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["notes"] = $"استقطاع تلقائي من مسير راتب ({result.Period})",
                ["journalEntryId"] = journal.S("id"),
                ["journalNumber"] = journal.S("journalNumber"),
                ["createdBy"] = "payroll_engine",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
            }, tx);
        }
    }
}
