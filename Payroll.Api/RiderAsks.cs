using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

// Track M (BR §25.8 & M-0359):
// 1. Advance asks go only to the rider's current supervisor, who converts to a formal advance request (§25) or declines.
// 2. Service requests (annual_leave, iban_update, data_update, statement_request, complaint, appointment) work both ways
//    with a status timeline, two-way replies, confidential complaints, and a second independent approver for IBAN/data updates.
// 3. Uses the same FormSchemas / IbanChoices structure as RiderForm (one forms system).
public static class RiderAsks
{
    public const string Kind = "rider_ask";
    public const string ServiceKind = "rider_service_request";
    static readonly string[] Open = ["open"];

    public static readonly string[] ServiceRequestTypes =
    [
        "annual_leave",
        "iban_update",
        "data_update",
        "statement_request",
        "complaint",
        "appointment"
    ];

    static JsonObject TimelineEntry(string at, string status, string ar, string en, string ur, string note = "", string byRole = "rider") => new()
    {
        ["at"] = at,
        ["status"] = status,
        ["labelAr"] = ar,
        ["labelEn"] = en,
        ["labelUr"] = ur,
        ["note"] = note,
        ["byRole"] = byRole
    };

    static JsonArray BuildAdvanceTimeline(JsonObject a, JsonObject? formal)
    {
        var list = new JsonArray();
        string created = a.S("createdAt");
        list.Add(TimelineEntry(created, "open", "أُرسل الطلب إلى المشرف", "Sent to supervisor", "سپروائزر کو بھیج دی گئی", a.S("reason"), "rider"));
        if (a.S("status") == "cancelled")
        {
            list.Add(TimelineEntry(a.S("decidedAt", created), "cancelled", "أُلغي الطلب بواسطة المندوب", "Cancelled by rider", "رائیڈر نے منسوخ کر دی", "", "rider"));
        }
        else if (a.S("status") == "declined")
        {
            list.Add(TimelineEntry(a.S("decidedAt", created), "declined", "رُفض الطلب من المشرف", "Declined by supervisor", "سپروائزر نے مسترد کر دی", a.S("note"), "supervisor"));
        }
        else if (a.S("status") == "converted")
        {
            list.Add(TimelineEntry(a.S("decidedAt", created), "converted", "رفعه المشرف إلى قسم الحسابات", "Forwarded by supervisor to accounting", "سپروائزر نے اکاؤنٹس کو بھیج دی", "", "supervisor"));
            if (formal != null)
            {
                string fStatus = formal.S("status");
                if (fStatus == "approved" || fStatus == "paid")
                {
                    list.Add(TimelineEntry(formal.S("approvedAt", formal.S("updatedAt", created)), "approved", "تم اعتماد السلفة في الحسابات", "Approved by accounting", "اکاؤنٹس سے منظور شدہ", "", "accountant"));
                }
                if (fStatus == "paid")
                {
                    list.Add(TimelineEntry(formal.S("paidAt", formal.S("updatedAt", created)), "paid", "تم صرف السلفة", "Advance paid out", "ایڈوانس ادا کر دیا گیا", "", "accountant"));
                }
                else if (fStatus == "rejected")
                {
                    list.Add(TimelineEntry(formal.S("updatedAt", created), "rejected", "مرفوض من الحسابات", "Rejected by accounting", "اکاؤنٹس سے مسترد", "", "accountant"));
                }
                else if (fStatus == "cancelled")
                {
                    list.Add(TimelineEntry(formal.S("updatedAt", created), "cancelled", "أُلغي طلب السلفة", "Advance request cancelled", "ایڈوانس کی درخواست منسوخ", "", "supervisor"));
                }
            }
        }
        if (a["timeline"] is JsonArray extra)
        {
            foreach (var item in extra)
                if (item != null) list.Add(item.DeepClone());
        }
        return list;
    }

    static JsonObject RiderView(DbConnection c, JsonObject a)
    {
        JsonObject? formal = a.S("advanceRequestId") != "" ? Store.Get(c, a.S("advanceRequestId"), AdvanceRequests.Kind) : null;
        var v = new JsonObject
        {
            ["id"] = a.S("id"),
            ["requestType"] = "advance",
            ["direction"] = "rider_to_admin",
            ["type"] = a.S("type"),
            ["amount"] = a.N("amount"),
            ["reason"] = a.S("reason"),
            ["status"] = a.S("status"),
            ["createdAt"] = a.S("createdAt"),
            ["decidedAt"] = a.S("decidedAt"),
            ["note"] = a.S("status") == "declined" ? a.S("note") : "",
            ["confidential"] = false,
            ["requiresSecondApprover"] = false,
            ["timeline"] = BuildAdvanceTimeline(a, formal),
            ["replies"] = (a["replies"] as JsonArray)?.DeepClone() ?? new JsonArray()
        };
        // After conversion the rider sees the formal request's stage only, never reviewer notes or vault balances.
        if (formal != null)
        {
            v["advanceStatus"] = formal.S("status");
            v["amount"] = formal.N("amount");
            v["type"] = formal.S("type");
        }
        return v;
    }

    static JsonObject ServiceRiderView(JsonObject r)
    {
        var payload = (r["payload"] as JsonObject)?.Clone() ?? new JsonObject();
        // Mask full IBAN on read views; keep ibanLast4 and bankCode.
        if (payload.ContainsKey("iban"))
        {
            string fullIban = payload.S("iban");
            payload["ibanLast4"] = fullIban.Length >= 4 ? fullIban[^4..] : "";
            payload.Remove("iban");
        }
        return new JsonObject
        {
            ["id"] = r.S("id"),
            ["requestType"] = r.S("requestType"),
            ["direction"] = r.S("direction", "rider_to_admin"),
            ["titleAr"] = r.S("titleAr"),
            ["titleEn"] = r.S("titleEn"),
            ["titleUr"] = r.S("titleUr"),
            ["reason"] = r.S("reason"),
            ["status"] = r.S("status"),
            ["approvalStage"] = r.S("approvalStage", "none"),
            ["requiresSecondApprover"] = r.B("requiresSecondApprover"),
            ["confidential"] = r.B("confidential"),
            ["createdAt"] = r.S("createdAt"),
            ["updatedAt"] = r.S("updatedAt", r.S("createdAt")),
            ["decidedAt"] = r.S("decidedAt"),
            ["note"] = r.S("note"),
            ["payload"] = payload,
            ["timeline"] = (r["timeline"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["replies"] = (r["replies"] as JsonArray)?.DeepClone() ?? new JsonArray()
        };
    }

    public static (string Ar, string En, string Ur) RequestTypeTitles(string requestType) =>
        requestType switch
        {
            "advance" => ("طلب سلفة", "Advance Request", "ایڈوانس کی درخواست"),
            "annual_leave" => ("طلب إجازة سنوية", "Annual Leave Request", "سالانہ چھٹی کی درخواست"),
            "iban_update" => ("تحديث بيانات الآيبان", "IBAN Update Request", "آئی بین کی تبدیلی کی درخواست"),
            "data_update" => ("تحديث البيانات الشخصية", "Personal Data Update", "ذاتی معلومات کی تبدیلی"),
            "statement_request" => ("طلب كشف حساب", "Account Statement Request", "اکاؤنٹ اسٹیٹمنٹ کی درخواست"),
            "complaint" => ("شكوى سرية", "Confidential Complaint", "خفیہ شکایت"),
            "appointment" => ("طلب موعد مراجعة", "Appointment Request", "ملاقات کے وقت کی درخواست"),
            _ => ("طلب خدمة", "Service Request", "سروس کی درخواست")
        };

    public static JsonObject Ask(DbConnection c, DbTransaction tx, HttpContext ctx, JsonObject input)
    {
        string iqama = MobileSessions.Validate(c, ctx, null, tx).S("iqama");
        var rider = Store.Get(c, iqama, "rider", tx)!;
        string type = input.Required("type");
        J.Need(type is "cash" or "bank", "نوع السلفة نقدية أو بنكية.");
        decimal amount = Debts.Amount(input, "amount");
        J.Need(amount > 0 && amount <= 100000, "مبلغ السلفة غير صالح.");
        string reason = input.S("reason").Trim();
        J.Need(reason.Length is > 2 and <= 500, "اكتب سبب الطلب.");
        var supervisors = OrgDirectory.SupervisorsOf(c, tx, iqama);
        J.Need(supervisors.Count > 0, "لا يوجد مشرف مسجل لك حاليًا؛ تواصل مع الإدارة.", 409);
        J.Need(!Store.ListWhere(c, Kind, tx, null, ("riderIqama", iqama)).Any(a => Open.Contains(a.S("status"))), "لديك طلب مفتوح بالفعل.", 409);
        J.Need(!Store.ListWhere(c, AdvanceRequests.Kind, tx, null, ("riderIqama", iqama)).Any(r => r.S("status") is not ("rejected" or "cancelled" or "paid")), "لديك طلب سلفة قيد المراجعة بالفعل.", 409);
        var a = new JsonObject
        {
            ["id"] = "ask:" + Guid.NewGuid().ToString("N"),
            ["riderIqama"] = iqama,
            ["riderName"] = rider.S("name"),
            ["type"] = type,
            ["amount"] = amount,
            ["reason"] = reason,
            ["status"] = "open",
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["supervisors"] = new JsonArray(supervisors.Select(u => (JsonNode)u).ToArray()),
            ["replies"] = new JsonArray()
        };
        Store.Save(c, Kind, a, tx);
        Store.Audit(c, tx, "rider_ask.created", a.S("id"), "rider:" + iqama);
        Notifications.RaiseFor(c, tx, supervisors, "ask:" + a.S("id"), "طلب سلفة من مندوب", $"{rider.S("name")} يطلب {amount:0.##} ({(type == "cash" ? "نقدي" : "بنكي")})", "rider:" + iqama, Kind, a.S("id"), true);
        return RiderView(c, a);
    }

    public static JsonObject CreateServiceRequest(DbConnection c, DbTransaction tx, HttpContext ctx, JsonObject input)
    {
        string iqama = MobileSessions.Validate(c, ctx, null, tx).S("iqama");
        var rider = Store.Get(c, iqama, "rider", tx) ?? throw new ApiError("المندوب غير موجود.", 404);
        string reqType = input.Required("requestType").Trim().ToLowerInvariant();
        J.Need(ServiceRequestTypes.Contains(reqType), "نوع الطلب غير صالح.");

        string now = DateTimeOffset.UtcNow.ToString("O");
        var (titleAr, titleEn, titleUr) = RequestTypeTitles(reqType);
        bool confidential = reqType == "complaint";
        bool requiresSecondApprover = reqType is "iban_update" or "data_update";
        string status = "open";
        string approvalStage = requiresSecondApprover ? "awaiting_first" : "awaiting_review";
        string reason = input.S("reason", input.S("details", input.S("subject"))).Trim();
        var payload = new JsonObject();
        var timeline = new JsonArray();

        if (reqType == "annual_leave")
        {
            string leaveType = input.S("leaveType", "annual").Trim();
            J.Need(leaveType is "annual" or "emergency" or "unpaid", "نوع الإجازة غير صالح (annual / emergency / unpaid).");
            string startStr = input.Required("startDate").Trim();
            string endStr = input.Required("endDate").Trim();
            bool validStart = DateOnly.TryParseExact(startStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate);
            bool validEnd = DateOnly.TryParseExact(endStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate);
            J.Need(validStart && validEnd, "تاريخ بداية أو نهاية الإجازة غير صالح (yyyy-MM-dd).");
            J.Need(endDate >= startDate, "تاريخ نهاية الإجازة يجب ألا يسبق تاريخ البداية.");
            int days = endDate.DayNumber - startDate.DayNumber + 1;
            J.Need(days is >= 1 and <= 180, "مدة الإجازة يجب أن تكون بين يوم واحد و180 يوماً.");
            string destination = input.S("destination", "inside_ksa").Trim();
            J.Need(destination is "inside_ksa" or "outside_ksa", "وجهة الإجازة داخل المملكة أو خارجها.");
            string contactPhone = input.S("contactPhone", rider.S("phone")).Trim();
            if (!string.IsNullOrEmpty(contactPhone)) contactPhone = RiderDirectory.Phone(contactPhone);
            J.Need(reason.Length is >= 2 and <= 1000, "اكتب سبب الإجازة.");
            payload["leaveType"] = leaveType;
            payload["startDate"] = startStr;
            payload["endDate"] = endStr;
            payload["days"] = days;
            payload["destination"] = destination;
            payload["contactPhone"] = contactPhone;
            timeline.Add(TimelineEntry(now, "open", "تم إرسال طلب الإجازة السنوية للمراجعة", "Annual leave request submitted for review", "سالانہ چھٹی کی درخواست جمع کر دی گئی", reason, "rider"));
        }
        else if (reqType == "iban_update")
        {
            string rawIban = IbanChoices.NormalizeIban(input.Required("iban"));
            string validIban = J.Iban(rawIban);
            string bankCode = validIban.Substring(4, 2);
            J.Need(!new[] { "40", "50" }.Contains(bankCode), "هذا الآيبان من بنك اندمج في بنك آخر ولم يعد يستقبل التحويلات.");
            J.Need(IbanChoices.SalaryBanks.Contains(bankCode), "بنك هذا الآيبان غير مقبول لاستلام الرواتب.");
            string chosenBank = input.S("bankCode", bankCode).Trim();
            J.Need(chosenBank == bankCode, "البنك المختار لا يطابق رقم الآيبان.");
            string currentIban = IbanChoices.NormalizeIban(rider.S("iban") != "" ? rider.S("iban") : rider.S("verifiedIban"));
            payload["iban"] = validIban;
            payload["ibanLast4"] = validIban[^4..];
            payload["bankCode"] = bankCode;
            payload["bank"] = BankPayrollFiles.BankFor(bankCode);
            payload["previousIbanLast4"] = currentIban.Length >= 4 ? currentIban[^4..] : "";
            if (string.IsNullOrEmpty(reason)) reason = $"تحديث آيبان إلى {payload.S("bank")} (••••{validIban[^4..]})";

            // Section 1e/1f rule: if submitted IBAN matches the rider's already recorded IBAN -> auto-approve!
            if (!string.IsNullOrEmpty(currentIban) && string.Equals(currentIban, validIban, StringComparison.OrdinalIgnoreCase))
            {
                status = "approved";
                approvalStage = "auto_approved";
                requiresSecondApprover = false;
                timeline.Add(TimelineEntry(now, "approved", "الآيبان مطابق للمسجل — تم الاعتماد تلقائياً", "IBAN matches current record — auto-approved", "آئی بین موجودہ ریکارڈ کے مطابق ہے — خودکار منظور شدہ", reason, "system"));
            }
            else
            {
                timeline.Add(TimelineEntry(now, "open", "تم إرسال طلب تحديث الآيبان (يتطلب اعتمادين مستقلين)", "IBAN update submitted (requires two independent approvers)", "آئی بین تبدیلی کی درخواست جمع (دو منظوریاں درکار ہیں)", reason, "rider"));
            }
        }
        else if (reqType == "data_update")
        {
            string field = input.Required("fieldToUpdate").Trim().ToLowerInvariant();
            J.Need(field is "phone" or "city" or "housing" or "emergency_contact" or "other", "الحقل المطلوب تحديثه غير صالح.");
            string newValue = input.Required("newValue").Trim();
            J.Need(newValue.Length is >= 2 and <= 300, "القيمة الجديدة غير صالحة.");
            if (field == "phone") newValue = RiderDirectory.Phone(newValue);
            J.Need(reason.Length is >= 2 and <= 1000, "اكتب سبب تحديث البيانات.");
            payload["fieldToUpdate"] = field;
            payload["previousValue"] = field is "phone" or "city" ? rider.S(field) : "";
            payload["newValue"] = newValue;
            timeline.Add(TimelineEntry(now, "open", "تم إرسال طلب تحديث البيانات (يتطلب اعتمادين مستقلين)", "Data update submitted (requires two independent approvers)", "معلومات کی تبدیلی جمع (دو منظوریاں درکار ہیں)", reason, "rider"));
        }
        else if (reqType == "statement_request")
        {
            string scope = input.S("statementScope", "monthly_payslip").Trim();
            J.Need(scope is "monthly_payslip" or "full_account" or "debt_clearance", "نوع كشف الحساب غير صالح.");
            string periodFrom = input.S("periodFrom", DateTimeOffset.UtcNow.ToString("yyyy-MM")).Trim();
            string periodTo = input.S("periodTo", periodFrom).Trim();
            J.Period(periodFrom);
            J.Period(periodTo);
            J.Need(string.CompareOrdinal(periodFrom, periodTo) <= 0, "فترة البداية يجب ألا تتجاوز فترة النهاية.");
            if (string.IsNullOrEmpty(reason)) reason = $"طلب كشف ({scope}) للفترة {periodFrom} إلى {periodTo}";
            payload["statementScope"] = scope;
            payload["periodFrom"] = periodFrom;
            payload["periodTo"] = periodTo;
            timeline.Add(TimelineEntry(now, "open", "تم إرسال طلب كشف الحساب", "Account statement request submitted", "اکاؤنٹ اسٹیٹمنٹ کی درخواست جمع کر دی گئی", reason, "rider"));
        }
        else if (reqType == "complaint")
        {
            string targetKind = input.S("targetKind", "other").Trim().ToLowerInvariant();
            J.Need(targetKind is "supervisor" or "employee" or "operations" or "finance" or "other", "جهة الشكوى غير صالحة.");
            string subject = input.Required("subject").Trim();
            string details = input.S("details", reason).Trim();
            J.Need(subject.Length is >= 3 and <= 200, "عنوان الشكوى مطلوب (3–200 حرف).");
            J.Need(details.Length is >= 5 and <= 2000, "تفاصيل الشكوى مطلوبة (5–2000 حرف).");
            reason = subject;
            payload["targetKind"] = targetKind;
            payload["subject"] = subject;
            payload["details"] = details;
            timeline.Add(TimelineEntry(now, "open", "تم تسجيل الشكوى السرية — لا يطلع عليها إلا الإدارة المختصة", "Confidential complaint recorded — restricted to authorized management", "خفیہ شکایت درج کر لی گئی — صرف مجاز انتظامیہ دیکھ سکتی ہے", subject, "rider"));
        }
        else if (reqType == "appointment")
        {
            string department = input.S("department", "hr").Trim().ToLowerInvariant();
            J.Need(department is "hr" or "finance" or "fleet" or "operations" or "management", "القسم المطلوب غير صالح.");
            string preferredDate = input.Required("preferredDate").Trim();
            J.Need(DateOnly.TryParseExact(preferredDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "التاريخ المفضل غير صالح (yyyy-MM-dd).");
            string preferredSlot = input.S("preferredSlot", "morning").Trim().ToLowerInvariant();
            J.Need(preferredSlot is "morning" or "afternoon", "الفترة الزمنية (morning / afternoon) غير صالحة.");
            J.Need(reason.Length is >= 2 and <= 1000, "اكتب سبب الموعد.");
            payload["department"] = department;
            payload["preferredDate"] = preferredDate;
            payload["preferredSlot"] = preferredSlot;
            timeline.Add(TimelineEntry(now, "open", "تم إرسال طلب حجز الموعد", "Appointment request submitted", "ملاقات کی درخواست جمع کر دی گئی", reason, "rider"));
        }

        var doc = new JsonObject
        {
            ["id"] = "sreq:" + Guid.NewGuid().ToString("N"),
            ["requestType"] = reqType,
            ["direction"] = "rider_to_admin",
            ["riderIqama"] = iqama,
            ["riderName"] = rider.S("name"),
            ["companyId"] = rider.S("financeSponsorId", rider.S("sponsorId")),
            ["titleAr"] = titleAr,
            ["titleEn"] = titleEn,
            ["titleUr"] = titleUr,
            ["reason"] = reason,
            ["status"] = status,
            ["approvalStage"] = approvalStage,
            ["requiresSecondApprover"] = requiresSecondApprover,
            ["confidential"] = confidential,
            ["payload"] = payload,
            ["timeline"] = timeline,
            ["replies"] = new JsonArray(),
            ["createdAt"] = now,
            ["updatedAt"] = now,
            ["decidedAt"] = status == "approved" ? now : ""
        };

        Store.Save(c, ServiceKind, doc, tx);
        Store.Audit(c, tx, "rider_service_request.created", doc.S("id"), "rider:" + iqama);
        return ServiceRiderView(doc);
    }

    public static JsonObject Cancel(DbConnection c, DbTransaction tx, HttpContext ctx, string id)
    {
        string iqama = MobileSessions.Validate(c, ctx, null, tx).S("iqama");
        if (id.StartsWith("sreq:", StringComparison.Ordinal))
        {
            var s = Store.Get(c, id, ServiceKind, tx)?.Clone() ?? throw new ApiError("الطلب غير موجود.", 404);
            J.Need(s.S("riderIqama") == iqama, "الطلب غير موجود.", 404);
            J.Need(s.S("status") is "open" or "need_info" or "first_approved", "لا يمكن إلغاء طلب تم البت النهائي فيه.", 409);
            string now = DateTimeOffset.UtcNow.ToString("O");
            s["status"] = "cancelled";
            s["decidedAt"] = now;
            s["updatedAt"] = now;
            var tl = s["timeline"] as JsonArray ?? new JsonArray();
            tl.Add(TimelineEntry(now, "cancelled", "أُلغي الطلب بواسطة المندوب", "Cancelled by rider", "رائیڈر نے درخواست منسوخ کر دی", "", "rider"));
            s["timeline"] = tl;
            Store.Save(c, ServiceKind, s, tx);
            Store.Audit(c, tx, "rider_service_request.cancelled", id, "rider:" + iqama);
            return ServiceRiderView(s);
        }

        var a = Store.Get(c, id, Kind, tx)?.Clone() ?? throw new ApiError("الطلب غير موجود.", 404);
        J.Need(a.S("riderIqama") == iqama, "الطلب غير موجود.", 404);
        J.Need(a.S("status") == "open", "لا يمكن إلغاء طلب تم البت فيه.", 409);
        a["status"] = "cancelled";
        a["decidedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, Kind, a, tx);
        Store.Audit(c, tx, "rider_ask.cancelled", id, "rider:" + iqama);
        Notifications.CloseTasks(c, tx, Kind, id, "rider:" + iqama);
        return RiderView(c, a);
    }

    public static JsonObject RiderReply(DbConnection c, DbTransaction tx, HttpContext ctx, string id, JsonObject input)
    {
        string iqama = MobileSessions.Validate(c, ctx, null, tx).S("iqama");
        string message = input.Required("message").Trim();
        J.Need(message.Length is >= 2 and <= 1000, "اكتب نص الرد (2–1000 حرف).");
        string now = DateTimeOffset.UtcNow.ToString("O");

        if (id.StartsWith("sreq:", StringComparison.Ordinal))
        {
            var s = Store.Get(c, id, ServiceKind, tx)?.Clone() ?? throw new ApiError("الطلب غير موجود.", 404);
            J.Need(s.S("riderIqama") == iqama, "الطلب غير موجود.", 404);
            J.Need(s.S("status") is not ("cancelled" or "rejected"), "لا يمكن الرد على طلب ملغي أو مرفوض.", 409);
            var replies = s["replies"] as JsonArray ?? new JsonArray();
            replies.Add(new JsonObject
            {
                ["id"] = "rep_" + Guid.NewGuid().ToString("N")[..12],
                ["senderRole"] = "rider",
                ["message"] = message,
                ["createdAt"] = now
            });
            s["replies"] = replies;
            var tl = s["timeline"] as JsonArray ?? new JsonArray();
            tl.Add(TimelineEntry(now, "rider_replied", "رد المندوب على الطلب", "Rider replied", "رائیڈر کا جواب", message, "rider"));
            s["timeline"] = tl;
            if (s.S("status") is "need_info" or "awaiting_rider")
            {
                s["status"] = "open";
            }
            s["updatedAt"] = now;
            Store.Save(c, ServiceKind, s, tx);
            Store.Audit(c, tx, "rider_service_request.rider_replied", id, "rider:" + iqama);
            return ServiceRiderView(s);
        }

        var a = Store.Get(c, id, Kind, tx)?.Clone();
        J.Need(a != null && a.S("riderIqama") == iqama, "الطلب غير موجود.", 404);
        var askReplies = a!["replies"] as JsonArray ?? new JsonArray();
        askReplies.Add(new JsonObject
        {
            ["id"] = "rep_" + Guid.NewGuid().ToString("N")[..12],
            ["senderRole"] = "rider",
            ["message"] = message,
            ["createdAt"] = now
        });
        a["replies"] = askReplies;
        var askTl = a["timeline"] as JsonArray ?? new JsonArray();
        askTl.Add(TimelineEntry(now, "rider_replied", "رد المندوب", "Rider replied", "رائیڈر کا جواب", message, "rider"));
        a["timeline"] = askTl;
        Store.Save(c, Kind, a, tx);
        Store.Audit(c, tx, "rider_ask.rider_replied", id, "rider:" + iqama);
        return RiderView(c, a);
    }

    static bool CanStaffSeeServiceRequest(DbConnection c, ClaimsPrincipal user, JsonObject req, DbTransaction? tx = null)
    {
        if (req.B("confidential") || req.S("requestType") == "complaint")
        {
            if (!AccessPolicy.Administrator(user) && !AccessPolicy.Has(user, "mobile.complaints.confidential"))
                return false;
        }
        var rider = Store.Get(c, req.S("riderIqama"), "rider", tx);
        if (rider == null)
            return false;
        if (!FinancialScopes.Rider(user, rider) && !FinancialScopes.AllCompanies(user))
            return false;
        return true;
    }

    public static JsonObject CreateAdminRequestToRider(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, JsonObject input)
    {
        string iqama = J.Iqama(input.Required("iqama"));
        var rider = Store.Get(c, iqama, "rider", tx) ?? throw new ApiError("المندوب غير موجود.", 404);
        FinancialScopes.DemandRider(actor, rider);
        string reqType = input.S("requestType", "appointment").Trim().ToLowerInvariant();
        J.Need(ServiceRequestTypes.Contains(reqType), "نوع الطلب غير صالح.");
        string reason = input.Required("reason").Trim();
        J.Need(reason.Length is >= 3 and <= 1000, "اكتب تفاصيل الطلب الموجه للمندوب.");
        var (defAr, defEn, defUr) = RequestTypeTitles(reqType);
        string titleAr = input.S("titleAr").Trim() is { Length: > 0 } ta ? ta : defAr;
        string titleEn = input.S("titleEn").Trim() is { Length: > 0 } te ? te : defEn;
        string titleUr = input.S("titleUr").Trim() is { Length: > 0 } tu ? tu : defUr;
        string now = DateTimeOffset.UtcNow.ToString("O");
        string actorName = actor.Identity?.Name ?? "administrator";

        var timeline = new JsonArray
        {
            TimelineEntry(now, "awaiting_rider", "طلب موجه من الإدارة بانتظار رد المندوب", "Request from administration awaiting rider reply", "انتظامیہ کی جانب سے درخواست — رائیڈر کے جواب کا انتظار", reason, "admin")
        };

        var doc = new JsonObject
        {
            ["id"] = "sreq:" + Guid.NewGuid().ToString("N"),
            ["requestType"] = reqType,
            ["direction"] = "admin_to_rider",
            ["riderIqama"] = iqama,
            ["riderName"] = rider.S("name"),
            ["companyId"] = rider.S("financeSponsorId", rider.S("sponsorId")),
            ["titleAr"] = titleAr,
            ["titleEn"] = titleEn,
            ["titleUr"] = titleUr,
            ["reason"] = reason,
            ["status"] = "awaiting_rider",
            ["approvalStage"] = "awaiting_rider",
            ["requiresSecondApprover"] = false,
            ["confidential"] = reqType == "complaint",
            ["payload"] = (input["payload"] as JsonObject)?.Clone() ?? new JsonObject(),
            ["timeline"] = timeline,
            ["replies"] = new JsonArray(),
            ["createdBy"] = actorName,
            ["createdAt"] = now,
            ["updatedAt"] = now
        };
        Store.Save(c, ServiceKind, doc, tx);
        Store.Audit(c, tx, "rider_service_request.admin_created", doc.S("id"), actorName);
        string reasonEn = input.S("reasonEn").Trim() is { Length: > 0 } re ? re : reason;
        string reasonUr = input.S("reasonUr").Trim() is { Length: > 0 } ru ? ru : reason;
        PushNotifications.Enqueue(c, tx, iqama, titleAr, reason, "/rider-app/requests", titleEn, titleUr, reasonEn, reasonUr);
        return doc;
    }

    public const string IbanDataApproveFirstPermission = "riders.iban_data.approve_first";
    public const string IbanDataApproveSecondPermission = "riders.iban_data.approve_second";

    static string ActorUserId(ClaimsPrincipal actor)
    {
        string? id = actor.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim();
        J.Need(!string.IsNullOrWhiteSpace(id), "معرّف المستخدم مطلوب لتنفيذ هذا الاعتماد.", 403);
        return id!;
    }

    public static JsonObject DecideServiceRequest(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        var req = Store.Get(c, id, ServiceKind, tx)?.Clone() ?? throw new ApiError("الطلب غير موجود.", 404);
        J.Need(CanStaffSeeServiceRequest(c, actor, req, tx), "الطلب غير موجود.", 404);
        string status = req.S("status");
        J.Need(status is "open" or "first_approved" or "need_info" or "awaiting_rider", "تم البت النهائي في هذا الطلب بالفعل.", 409);

        string action = input.Required("action").Trim().ToLowerInvariant();
        string note = input.S("note").Trim();
        string actorName = actor.Identity?.Name ?? "administrator";
        string now = DateTimeOffset.UtcNow.ToString("O");
        bool twoStep = req.B("requiresSecondApprover") || req.S("requestType") is "iban_update" or "data_update";
        var tl = req["timeline"] as JsonArray ?? new JsonArray();

        if (action is "reject" or "decline")
        {
            if (twoStep)
            {
                J.Need(AccessPolicy.Has(actor, IbanDataApproveFirstPermission) || AccessPolicy.Has(actor, IbanDataApproveSecondPermission),
                    "ليست لديك صلاحية مخصصة للبت في طلبات الآيبان أو البيانات الشخصية.", 403);
            }
            J.Need(note.Length is >= 3 and <= 500, "اكتب سبب الرفض ليظهر للمندوب.");
            req["status"] = "rejected";
            req["approvalStage"] = "rejected";
            req["note"] = note;
            req["decidedBy"] = actorName;
            req["decidedAt"] = now;
            tl.Add(TimelineEntry(now, "rejected", "تم رفض الطلب", "Request rejected", "درخواست مسترد کر دی گئی", note, "admin"));
            PushNotifications.Enqueue(c, tx, req.S("riderIqama"),
                $"تحديث على {req.S("titleAr")}", $"تم رفض الطلب: {note}", "/rider-app/requests",
                $"Update on {req.S("titleEn", req.S("titleAr"))}", $"{req.S("titleUr", req.S("titleAr"))} پر تازہ کاری",
                $"Request rejected: {note}", $"درخواست مسترد کر دی گئی: {note}");
        }
        else if (action == "need_info")
        {
            if (twoStep)
            {
                J.Need(AccessPolicy.Has(actor, IbanDataApproveFirstPermission) || AccessPolicy.Has(actor, IbanDataApproveSecondPermission),
                    "ليست لديك صلاحية مخصصة للبت في طلبات الآيبان أو البيانات الشخصية.", 403);
            }
            J.Need(note.Length is >= 3 and <= 500, "اكتب الاستفسار المطلوب من المندوب.");
            req["status"] = "need_info";
            req["note"] = note;
            var replies = req["replies"] as JsonArray ?? new JsonArray();
            replies.Add(new JsonObject
            {
                ["id"] = "rep_" + Guid.NewGuid().ToString("N")[..12],
                ["senderRole"] = "admin",
                ["message"] = note,
                ["createdAt"] = now
            });
            req["replies"] = replies;
            tl.Add(TimelineEntry(now, "need_info", "طلبت الإدارة توضيحاً من المندوب", "Administration requested clarification", "انتظامیہ نے وضاحت طلب کی ہے", note, "admin"));
            PushNotifications.Enqueue(c, tx, req.S("riderIqama"),
                $"استفسار حول {req.S("titleAr")}", note, "/rider-app/requests",
                $"Clarification requested on {req.S("titleEn", req.S("titleAr"))}", $"{req.S("titleUr", req.S("titleAr"))} کے بارے میں وضاحت",
                note, note);
        }
        else if (action is "approve" or "first_approve" or "second_approve")
        {
            if (twoStep && status != "first_approved")
            {
                AccessPolicy.Demand(actor, IbanDataApproveFirstPermission);
                string actorUserId = ActorUserId(actor);
                J.Need(action is "approve" or "first_approve", "يلزم الاعتماد الأول أولاً.");
                req["status"] = "first_approved";
                req["approvalStage"] = "awaiting_second";
                req["firstApprovedById"] = actorUserId;
                req["firstApprovedBy"] = actorName;
                req["firstApprovedAt"] = now;
                if (!string.IsNullOrEmpty(note)) req["note"] = note;
                tl.Add(TimelineEntry(now, "first_approved", "تم الاعتماد الأول — بانتظار المعتمد الثاني المستقل", "First approval complete — awaiting independent second approver", "پہلی منظوری مکمل — دوسری خود مختار منظوری کا انتظار", note, "admin"));
                PushNotifications.Enqueue(c, tx, req.S("riderIqama"),
                    $"تحديث على {req.S("titleAr")}", "تم الاعتماد الأول لطلبك وبانتظار الاعتماد النهائي.", "/rider-app/requests",
                    $"Update on {req.S("titleEn", req.S("titleAr"))}", $"{req.S("titleUr", req.S("titleAr"))} پر تازہ کاری",
                    "First approval is complete; awaiting final approval.", "آپ کی درخواست کی پہلی منظوری مکمل ہو گئی ہے اور حتمی منظوری کا انتظار ہے۔");
            }
            else
            {
                if (twoStep)
                {
                    AccessPolicy.Demand(actor, IbanDataApproveSecondPermission);
                    J.Need(action is "approve" or "second_approve", "الطلب بانتظار الاعتماد الثاني.");
                    string actorUserId = ActorUserId(actor);
                    string firstApprovedById = req.S("firstApprovedById");
                    bool sameUserId = !string.IsNullOrEmpty(firstApprovedById) &&
                        string.Equals(firstApprovedById, actorUserId, StringComparison.OrdinalIgnoreCase);
                    J.Need(!sameUserId && !AccessPolicy.SamePerson(actor, req.S("firstApprovedBy")),
                        "يلزم معتمد ثانٍ مستقل لتحديث الآيبان أو البيانات الشخصية (M-0359).", 403);
                    req["secondApprovedById"] = actorUserId;
                    req["secondApprovedBy"] = actorName;
                    req["secondApprovedAt"] = now;
                }
                req["status"] = "approved";
                req["approvalStage"] = "completed";
                req["decidedBy"] = actorName;
                req["decidedAt"] = now;
                if (!string.IsNullOrEmpty(note)) req["note"] = note;
                tl.Add(TimelineEntry(now, "approved", "تم اعتماد الطلب نهائياً", "Request finally approved", "درخواست حتمی طور پر منظور ہو گئی", note, "admin"));

                // Apply held snapshot / rider update for data_update or iban_update upon final second approval
                var rider = Store.Get(c, req.S("riderIqama"), "rider", tx)?.Clone();
                var payload = req["payload"] as JsonObject;
                if (rider != null && payload != null)
                {
                    if (req.S("requestType") == "iban_update" && payload.S("iban") != "")
                    {
                        rider["verifiedIban"] = payload.S("iban");
                        rider["ibanReviewedAt"] = now;
                        Store.Save(c, "rider", rider, tx);
                    }
                    else if (req.S("requestType") == "data_update" && payload.S("fieldToUpdate") is "phone" or "city")
                    {
                        rider[payload.S("fieldToUpdate")] = payload.S("newValue");
                        Store.Save(c, "rider", rider, tx);
                    }
                }
                PushNotifications.Enqueue(c, tx, req.S("riderIqama"),
                    $"تم اعتماد {req.S("titleAr")}", string.IsNullOrEmpty(note) ? "تم اعتماد طلبك بنجاح." : note, "/rider-app/requests",
                    $"Approved: {req.S("titleEn", req.S("titleAr"))}", $"منظور شدہ: {req.S("titleUr", req.S("titleAr"))}",
                    string.IsNullOrEmpty(note) ? "Your request has been approved." : note,
                    string.IsNullOrEmpty(note) ? "آپ کی درخواست منظور ہو گئی ہے۔" : note);
            }
        }
        else
        {
            throw new ApiError("الإجراء غير صالح (approve / first_approve / second_approve / reject / need_info).");
        }

        req["timeline"] = tl;
        req["updatedAt"] = now;
        Store.Save(c, ServiceKind, req, tx);
        Store.Audit(c, tx, $"rider_service_request.{action}", id, actorName);
        return req;
    }

    public static JsonObject StaffReplyServiceRequest(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        var req = Store.Get(c, id, ServiceKind, tx)?.Clone() ?? throw new ApiError("الطلب غير موجود.", 404);
        J.Need(CanStaffSeeServiceRequest(c, actor, req, tx), "الطلب غير موجود.", 404);
        string message = input.Required("message").Trim();
        J.Need(message.Length is >= 2 and <= 1000, "اكتب نص الرد (2–1000 حرف).");
        string now = DateTimeOffset.UtcNow.ToString("O");
        string actorName = actor.Identity?.Name ?? "administrator";

        var replies = req["replies"] as JsonArray ?? new JsonArray();
        replies.Add(new JsonObject
        {
            ["id"] = "rep_" + Guid.NewGuid().ToString("N")[..12],
            ["senderRole"] = "admin",
            ["senderName"] = actorName,
            ["message"] = message,
            ["createdAt"] = now
        });
        req["replies"] = replies;
        var tl = req["timeline"] as JsonArray ?? new JsonArray();
        tl.Add(TimelineEntry(now, "admin_replied", "رد الإدارة على الطلب", "Administration replied", "انتظامیہ کا جواب", message, "admin"));
        req["timeline"] = tl;
        req["updatedAt"] = now;
        Store.Save(c, ServiceKind, req, tx);
        Store.Audit(c, tx, "rider_service_request.admin_replied", id, actorName);
        PushNotifications.Enqueue(c, tx, req.S("riderIqama"),
            $"رد جديد على {req.S("titleAr")}", message, "/rider-app/requests",
            $"New reply on {req.S("titleEn", req.S("titleAr"))}", $"{req.S("titleUr", req.S("titleAr"))} پر نیا جواب",
            message, message);
        return req;
    }

    public static HashSet<string> RiderCompanyKeys(JsonObject? rider)
    {
        var riderCompanies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rider == null) return riderCompanies;
        foreach (string key in new[] { "financeSponsorId", "sponsorId", "companyId" })
        {
            string val = rider.S(key).Trim();
            if (string.IsNullOrEmpty(val)) continue;
            riderCompanies.Add(val);
            var sp = FinancialCore.Sponsors.FirstOrDefault(s =>
                string.Equals(s.Id, val, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Code, val, StringComparison.OrdinalIgnoreCase));
            if (sp != null)
            {
                riderCompanies.Add(sp.Id);
                riderCompanies.Add(sp.Code);
            }
        }
        return riderCompanies;
    }

    public static bool IsFormTargetedToRider(JsonObject form, JsonObject? rider, string iqama)
    {
        if (rider == null) return false;

        string target = form.S("target", form.S("targetAudience")).Trim();
        if (!string.IsNullOrEmpty(target) &&
            !target.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
            !target.Equals("rider", StringComparison.OrdinalIgnoreCase) &&
            !target.Equals("riders", StringComparison.OrdinalIgnoreCase))
        {
            if (target.Length == 10 && target.All(char.IsAsciiDigit))
            {
                if (!string.Equals(target, iqama, StringComparison.Ordinal)) return false;
            }
            else
            {
                return false;
            }
        }

        string targetIqama = form.S("targetIqama").Trim();
        if (!string.IsNullOrEmpty(targetIqama) &&
            !targetIqama.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetIqama, iqama, StringComparison.Ordinal))
            return false;

        if (form["targetIqamas"] is JsonArray targetIqamas && targetIqamas.Count > 0)
        {
            var allowedIqamas = targetIqamas.Select(x => x?.ToString()?.Trim() ?? "").Where(x => x != "").ToList();
            if (allowedIqamas.Count > 0 &&
                !allowedIqamas.Any(x => x.Equals("ALL", StringComparison.OrdinalIgnoreCase)) &&
                !allowedIqamas.Any(x => string.Equals(x, iqama, StringComparison.Ordinal)))
                return false;
        }

        var riderCompanies = RiderCompanyKeys(rider);

        foreach (string key in new[] { "companyId", "sponsorId", "targetCompanyId", "targetSponsorId" })
        {
            string requiredCompany = form.S(key).Trim();
            if (string.IsNullOrEmpty(requiredCompany) || requiredCompany.Equals("ALL", StringComparison.OrdinalIgnoreCase)) continue;
            if (!riderCompanies.Contains(requiredCompany)) return false;
        }

        foreach (string key in new[] { "companyIds", "sponsorIds", "targetCompanyIds" })
        {
            if (form[key] is JsonArray arr && arr.Count > 0)
            {
                var allowed = arr.Select(x => x?.ToString()?.Trim() ?? "").Where(x => x != "").ToList();
                if (allowed.Count > 0 &&
                    !allowed.Any(x => x.Equals("ALL", StringComparison.OrdinalIgnoreCase)) &&
                    !allowed.Any(riderCompanies.Contains))
                    return false;
            }
        }

        string targetCity = form.S("targetCity").Trim();
        if (!string.IsNullOrEmpty(targetCity) &&
            !targetCity.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetCity, rider.S("city").Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public static JsonObject FormSchemasCatalog(DbConnection c, string iqama)
    {
        var rider = Store.Get(c, iqama, "rider");
        var schemas = new JsonArray
        {
            new JsonObject
            {
                ["requestType"] = "advance",
                ["schema"] = "advance-v1",
                ["titleAr"] = "طلب سلفة",
                ["titleEn"] = "Advance Request",
                ["titleUr"] = "ایڈوانس کی درخواست",
                ["descriptionAr"] = "يُرسل إلى مشرفك المباشر ثم إلى الحسابات.",
                ["descriptionEn"] = "Sent to your current supervisor and then to accounting.",
                ["descriptionUr"] = "آپ کے موجودہ سپروائزر اور پھر اکاؤنٹس کو بھیجی جاتی ہے۔",
                ["requiresSecondApprover"] = false,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "type", ["type"] = "select", ["labelAr"] = "نوع السلفة", ["labelEn"] = "Advance Type", ["labelUr"] = "ایڈوانس کی قسم", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "cash", ["labelAr"] = "نقدي", ["labelEn"] = "Cash", ["labelUr"] = "نقد" },
                        new JsonObject { ["value"] = "bank", ["labelAr"] = "تحويل بنكي", ["labelEn"] = "Bank Transfer", ["labelUr"] = "بینک ٹرانسفر" }) },
                    new JsonObject { ["id"] = "amount", ["type"] = "number", ["labelAr"] = "المبلغ", ["labelEn"] = "Amount", ["labelUr"] = "رقم", ["required"] = true },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "السبب", ["labelEn"] = "Reason", ["labelUr"] = "وجہ", ["required"] = true })
            },
            new JsonObject
            {
                ["requestType"] = "annual_leave",
                ["schema"] = "annual-leave-v1",
                ["titleAr"] = "طلب إجازة سنوية",
                ["titleEn"] = "Annual Leave Request",
                ["titleUr"] = "سالانہ چھٹی کی درخواست",
                ["descriptionAr"] = "حدد نوع الإجازة وتاريخ البداية والنهاية ووجهة الإجازة.",
                ["descriptionEn"] = "Select leave type, start and end dates, and destination.",
                ["descriptionUr"] = "چھٹی کی قسم، آغاز اور اختتام کی تاریخ اور مقام منتخب کریں۔",
                ["requiresSecondApprover"] = false,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "leaveType", ["type"] = "select", ["labelAr"] = "نوع الإجازة", ["labelEn"] = "Leave Type", ["labelUr"] = "چھٹی کی قسم", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "annual", ["labelAr"] = "إجازة سنوية", ["labelEn"] = "Annual Leave", ["labelUr"] = "سالانہ چھٹی" },
                        new JsonObject { ["value"] = "emergency", ["labelAr"] = "إجازة اضطرارية", ["labelEn"] = "Emergency Leave", ["labelUr"] = "ہنگامی چھٹی" },
                        new JsonObject { ["value"] = "unpaid", ["labelAr"] = "إجازة بدون راتب", ["labelEn"] = "Unpaid Leave", ["labelUr"] = "بلا معاوضہ چھٹی" }) },
                    new JsonObject { ["id"] = "startDate", ["type"] = "date", ["labelAr"] = "تاريخ البداية", ["labelEn"] = "Start Date", ["labelUr"] = "شروع ہونے کی تاریخ", ["required"] = true },
                    new JsonObject { ["id"] = "endDate", ["type"] = "date", ["labelAr"] = "تاريخ النهاية", ["labelEn"] = "End Date", ["labelUr"] = "ختم ہونے کی تاریخ", ["required"] = true },
                    new JsonObject { ["id"] = "destination", ["type"] = "radio", ["labelAr"] = "الوجهة", ["labelEn"] = "Destination", ["labelUr"] = "مقام", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "inside_ksa", ["labelAr"] = "داخل المملكة", ["labelEn"] = "Inside Saudi Arabia", ["labelUr"] = "سعودی عرب کے اندر" },
                        new JsonObject { ["value"] = "outside_ksa", ["labelAr"] = "خارج المملكة (سفر)", ["labelEn"] = "Outside Saudi Arabia (Travel)", ["labelUr"] = "بیرون ملک سفر" }) },
                    new JsonObject { ["id"] = "contactPhone", ["type"] = "phone", ["labelAr"] = "جوال التواصل أثناء الإجازة", ["labelEn"] = "Contact Phone During Leave", ["labelUr"] = "چھٹی کے دوران رابطہ نمبر", ["required"] = false },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "السبب والملاحظات", ["labelEn"] = "Reason & Notes", ["labelUr"] = "وجہ اور نوٹس", ["required"] = true })
            },
            new JsonObject
            {
                ["requestType"] = "iban_update",
                ["schema"] = "iban-v1",
                ["titleAr"] = "تحديث بيانات الآيبان",
                ["titleEn"] = "IBAN Update",
                ["titleUr"] = "آئی بین کی تبدیلی",
                ["descriptionAr"] = "يتطلب مراجعة واعتمادين مستقلين قبل التفعيل.",
                ["descriptionEn"] = "Requires verification and two independent approvals before activation.",
                ["descriptionUr"] = "فعال ہونے سے پہلے تصدیق اور دو خود مختار منظوریوں کی ضرورت ہے۔",
                ["requiresSecondApprover"] = true,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "iban", ["type"] = "iban", ["labelAr"] = "رقم الآيبان (SA + 22 رقماً)", ["labelEn"] = "IBAN (SA + 22 digits)", ["labelUr"] = "آئی بین نمبر (SA + 22 ہندسے)", ["required"] = true },
                    new JsonObject { ["id"] = "bankCode", ["type"] = "select", ["labelAr"] = "البنك", ["labelEn"] = "Bank", ["labelUr"] = "بینک", ["required"] = true },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "ملاحظة إضافية", ["labelEn"] = "Additional Note", ["labelUr"] = "اضافی نوٹ", ["required"] = false })
            },
            new JsonObject
            {
                ["requestType"] = "data_update",
                ["schema"] = "data-update-v1",
                ["titleAr"] = "تحديث البيانات الشخصية",
                ["titleEn"] = "Personal Data Update",
                ["titleUr"] = "ذاتی معلومات کی تبدیلی",
                ["descriptionAr"] = "يتطلب مراجعة واعتمادين مستقلين قبل التحديث.",
                ["descriptionEn"] = "Requires two independent approvals before updating.",
                ["descriptionUr"] = "اپ ڈیٹ سے پہلے دو خود مختار منظوریوں کی ضرورت ہے۔",
                ["requiresSecondApprover"] = true,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "fieldToUpdate", ["type"] = "select", ["labelAr"] = "البيان المطلوب تعديله", ["labelEn"] = "Field to Update", ["labelUr"] = "تبدیل کرنے والی معلومات", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "phone", ["labelAr"] = "رقم الجوال", ["labelEn"] = "Phone Number", ["labelUr"] = "موبائل نمبر" },
                        new JsonObject { ["value"] = "city", ["labelAr"] = "المدينة", ["labelEn"] = "City", ["labelUr"] = "شہر" },
                        new JsonObject { ["value"] = "housing", ["labelAr"] = "السكن", ["labelEn"] = "Housing", ["labelUr"] = "رہائش" },
                        new JsonObject { ["value"] = "emergency_contact", ["labelAr"] = "رقم الطوارئ", ["labelEn"] = "Emergency Contact", ["labelUr"] = "ہنگامی رابطہ نمبر" }) },
                    new JsonObject { ["id"] = "newValue", ["type"] = "text", ["labelAr"] = "القيمة الجديدة", ["labelEn"] = "New Value", ["labelUr"] = "نئی معلومات", ["required"] = true },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "سبب التعديل", ["labelEn"] = "Reason for Update", ["labelUr"] = "تبدیلی کی وجہ", ["required"] = true })
            },
            new JsonObject
            {
                ["requestType"] = "statement_request",
                ["schema"] = "statement-request-v1",
                ["titleAr"] = "طلب كشف حساب",
                ["titleEn"] = "Account Statement Request",
                ["titleUr"] = "اکاؤنٹ اسٹیٹمنٹ کی درخواست",
                ["descriptionAr"] = "اطلب مراجعة أو إصدار بيان حساب لفترة محددة.",
                ["descriptionEn"] = "Request an account or debt statement review for a specific period.",
                ["descriptionUr"] = "مخصوص مدت کے لیے اکاؤنٹ یا قرض کے بیان کی درخواست کریں۔",
                ["requiresSecondApprover"] = false,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "statementScope", ["type"] = "select", ["labelAr"] = "نوع البيان", ["labelEn"] = "Statement Type", ["labelUr"] = "بیان کی قسم", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "monthly_payslip", ["labelAr"] = "كشف راتب شهري", ["labelEn"] = "Monthly Statement", ["labelUr"] = "ماہانہ تنخواہ کا بیان" },
                        new JsonObject { ["value"] = "full_account", ["labelAr"] = "كشف حساب شامل", ["labelEn"] = "Full Account Statement", ["labelUr"] = "مکمل اکاؤنٹ اسٹیٹمنٹ" },
                        new JsonObject { ["value"] = "debt_clearance", ["labelAr"] = "بيان مديونيات وأقساط", ["labelEn"] = "Debts & Instalments Statement", ["labelUr"] = "قرض اور اقساط کا بیان" }) },
                    new JsonObject { ["id"] = "periodFrom", ["type"] = "month", ["labelAr"] = "من شهر", ["labelEn"] = "From Month", ["labelUr"] = "کس مہینے سے", ["required"] = true },
                    new JsonObject { ["id"] = "periodTo", ["type"] = "month", ["labelAr"] = "إلى شهر", ["labelEn"] = "To Month", ["labelUr"] = "کس مہینے تک", ["required"] = true },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "ملاحظات", ["labelEn"] = "Notes", ["labelUr"] = "نوٹس", ["required"] = false })
            },
            new JsonObject
            {
                ["requestType"] = "complaint",
                ["schema"] = "complaint-v1",
                ["titleAr"] = "شكوى سرية",
                ["titleEn"] = "Confidential Complaint",
                ["titleUr"] = "خفیہ شکایت",
                ["descriptionAr"] = "سرية تماماً؛ لا يطلع عليها المشرفون وتصل للإدارة المختصة فقط.",
                ["descriptionEn"] = "Strictly confidential; hidden from supervisors and visible only to authorized management.",
                ["descriptionUr"] = "مکمل طور پر خفیہ؛ سپروائزرز سے پوشیدہ اور صرف مجاز انتظامیہ کو نظر آتی ہے۔",
                ["requiresSecondApprover"] = false,
                ["confidential"] = true,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "targetKind", ["type"] = "select", ["labelAr"] = "تخص من؟", ["labelEn"] = "Regarding", ["labelUr"] = "کس کے متعلق؟", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "supervisor", ["labelAr"] = "مشرف", ["labelEn"] = "Supervisor", ["labelUr"] = "سپروائزر" },
                        new JsonObject { ["value"] = "employee", ["labelAr"] = "موظف إداري", ["labelEn"] = "Administrative Employee", ["labelUr"] = "انتظامی ملازم" },
                        new JsonObject { ["value"] = "operations", ["labelAr"] = "التشغيل", ["labelEn"] = "Operations", ["labelUr"] = "آپریشنز" },
                        new JsonObject { ["value"] = "finance", ["labelAr"] = "الحسابات", ["labelEn"] = "Accounting", ["labelUr"] = "اکاؤنٹس" },
                        new JsonObject { ["value"] = "other", ["labelAr"] = "أخرى", ["labelEn"] = "Other", ["labelUr"] = "دیگر" }) },
                    new JsonObject { ["id"] = "subject", ["type"] = "text", ["labelAr"] = "عنوان الشكوى", ["labelEn"] = "Subject", ["labelUr"] = "عنوان", ["required"] = true },
                    new JsonObject { ["id"] = "details", ["type"] = "text", ["labelAr"] = "التفاصيل الكاملة", ["labelEn"] = "Full Details", ["labelUr"] = "مکمل تفصیل", ["required"] = true })
            },
            new JsonObject
            {
                ["requestType"] = "appointment",
                ["schema"] = "appointment-v1",
                ["titleAr"] = "طلب موعد مراجعة",
                ["titleEn"] = "Appointment Request",
                ["titleUr"] = "ملاقات کے وقت کی درخواست",
                ["descriptionAr"] = "احجز موعداً لمراجعة القسم المختص.",
                ["descriptionEn"] = "Book an appointment to visit the relevant department.",
                ["descriptionUr"] = "متعلقہ شعبے سے ملاقات کے لیے وقت بک کریں۔",
                ["requiresSecondApprover"] = false,
                ["confidential"] = false,
                ["fields"] = new JsonArray(
                    new JsonObject { ["id"] = "department", ["type"] = "select", ["labelAr"] = "القسم", ["labelEn"] = "Department", ["labelUr"] = "شعبہ", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "hr", ["labelAr"] = "الموارد البشرية", ["labelEn"] = "Human Resources", ["labelUr"] = "ہیومن ریسورس" },
                        new JsonObject { ["value"] = "finance", ["labelAr"] = "الحسابات والمالية", ["labelEn"] = "Finance & Accounting", ["labelUr"] = "مالیات اور اکاؤنٹس" },
                        new JsonObject { ["value"] = "fleet", ["labelAr"] = "الأسطول والمركبات", ["labelEn"] = "Fleet & Vehicles", ["labelUr"] = "گاڑیاں اور فلیٹ" },
                        new JsonObject { ["value"] = "operations", ["labelAr"] = "التشغيل", ["labelEn"] = "Operations", ["labelUr"] = "آپریشنز" },
                        new JsonObject { ["value"] = "management", ["labelAr"] = "الإدارة", ["labelEn"] = "Management", ["labelUr"] = "انتظامیہ" }) },
                    new JsonObject { ["id"] = "preferredDate", ["type"] = "date", ["labelAr"] = "التاريخ المفضل", ["labelEn"] = "Preferred Date", ["labelUr"] = "پسندیدہ تاریخ", ["required"] = true },
                    new JsonObject { ["id"] = "preferredSlot", ["type"] = "radio", ["labelAr"] = "الفترة", ["labelEn"] = "Time Slot", ["labelUr"] = "وقت", ["required"] = true, ["options"] = new JsonArray(
                        new JsonObject { ["value"] = "morning", ["labelAr"] = "صباحاً (9 ص – 12 ظ)", ["labelEn"] = "Morning (9 AM – 12 PM)", ["labelUr"] = "صبح (9 سے 12)" },
                        new JsonObject { ["value"] = "afternoon", ["labelAr"] = "ظهراً (1 م – 4 م)", ["labelEn"] = "Afternoon (1 PM – 4 PM)", ["labelUr"] = "دوپہر (1 سے 4)" }) },
                    new JsonObject { ["id"] = "reason", ["type"] = "text", ["labelAr"] = "سبب الموعد", ["labelEn"] = "Reason for Appointment", ["labelUr"] = "ملاقات کی وجہ", ["required"] = true })
            }
        };

        var submissions = Store.ListWhere(c, "submission", null, null, ("iqama", iqama)).ToDictionary(s => s.S("campaignId"), s => s);
        var companyKeys = RiderCompanyKeys(rider);
        string nowIso = DateTimeOffset.UtcNow.ToString("O");
        var activeCampaigns = new JsonArray();
        foreach (var camp in Store.ListOpenFormsForRider(c, "campaign", iqama, companyKeys, nowIso)
                     .Where(x => IsFormTargetedToRider(x, rider, iqama)))
        {
            submissions.TryGetValue(camp.S("id"), out var sub);
            activeCampaigns.Add(new JsonObject
            {
                ["id"] = camp.S("id"),
                ["token"] = camp.S("sharingToken"),
                ["kind"] = camp.S("kind", "work-choice"),
                ["schema"] = IbanChoices.Is(camp) ? IbanChoices.Schema : "work-choice-v1",
                ["title"] = camp.S("title"),
                ["expiresAt"] = camp.S("expiresAt"),
                ["fields"] = CampaignOperations.Fields(camp).Select(f => (JsonNode?)JsonValue.Create(f)).ToArray() is var fArr ? new JsonArray(fArr) : new JsonArray(),
                ["cities"] = IbanChoices.Is(camp) ? IbanChoices.Cities(camp) : null,
                ["alreadySubmitted"] = sub != null,
                ["submissionStatus"] = sub?.S("status") ?? "",
                ["version"] = sub != null ? SubmissionChoices.Version(sub) : 1
            });
        }
        foreach (var qf in Store.ListOpenFormsForRider(c, "custom_form", iqama, companyKeys, nowIso)
                     .Where(x => IsFormTargetedToRider(x, rider, iqama)))
        {
            activeCampaigns.Add(new JsonObject
            {
                ["id"] = qf.S("id"),
                ["token"] = qf.S("sharingToken"),
                ["kind"] = "custom_form",
                ["schema"] = "custom-v1",
                ["titleAr"] = qf.S("titleAr"),
                ["titleEn"] = qf.S("titleEn"),
                ["titleUr"] = qf.S("titleUr"),
                ["descriptionAr"] = qf.S("descriptionAr"),
                ["descriptionEn"] = qf.S("descriptionEn"),
                ["descriptionUr"] = qf.S("descriptionUr"),
                ["expiresAt"] = qf.S("expiresAt"),
                ["fields"] = qf["fields"]?.DeepClone()
            });
        }

        return new JsonObject
        {
            ["rider"] = new JsonObject
            {
                ["iqama"] = iqama,
                ["name"] = rider?.S("name") ?? "",
                ["phone"] = rider?.S("phone") ?? "",
                ["city"] = rider?.S("city") ?? ""
            },
            ["schemas"] = schemas,
            ["activeCampaigns"] = activeCampaigns
        };
    }

    public static IResult SubmitInAppForm(Store store, HttpContext ctx, string token, JsonObject input)
    {
        var session = PushNotifications.AuthenticateAndRateLimit(store, ctx, write: true);
        string iqama = session.S("iqama");
        var rider = store.Read(c => Store.Get(c, iqama, "rider")) ?? throw new ApiError("المندوب غير موجود.", 404);
        string riderName = rider.S("name").Trim();
        J.Need(!string.IsNullOrWhiteSpace(riderName), "اسم المندوب غير مسجل في الدليل؛ تواصل مع الإدارة.", 409);

        // Always take iqama and name strictly from the authenticated rider record — never from client input.
        var payload = input.Clone();
        payload["iqama"] = iqama;
        payload["name"] = riderName;

        var customForm = store.Read(c => Store.List(c, "custom_form").FirstOrDefault(x => x.S("sharingToken") == token));
        if (customForm != null)
        {
            J.Need(IsFormTargetedToRider(customForm, rider, iqama), "النموذج غير موجود أو غير متاح لهذا المندوب.", 404);
            return store.Write<IResult>((c, tx) =>
            {
                J.Need(customForm.S("status") == "open" && DateTimeOffset.Parse(customForm.S("expiresAt"), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow, "النموذج مغلق.");
                var fields = (customForm["fields"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
                var cleanInput = new JsonObject();
                foreach (var f in fields)
                {
                    string fId = f.S("id");
                    if (f.S("type") == "iqama") cleanInput[fId] = iqama;
                    else if (payload.ContainsKey(fId)) cleanInput[fId] = payload[fId]?.DeepClone();
                }
                var answers = FormSchemas.Answers(customForm, cleanInput);
                var responses = Store.List(c, "custom_response", tx).Where(x => x.S("formId") == customForm.S("id")).ToList();
                J.Need(responses.Count < customForm.N("maxSubmissions", 10000), "اكتمل عدد الردود.");
                var response = new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["formId"] = customForm.S("id"),
                    ["iqama"] = iqama,
                    ["schemaVersion"] = customForm.N("schemaVersion", 1),
                    ["answers"] = answers,
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O")
                };
                Store.Save(c, "custom_response", response, tx);
                Store.Audit(c, tx, "builder.response_received", response.S("id"), "rider:" + iqama);
                return Results.Ok(new { ok = true, receipt = response.S("id"), iqama });
            });
        }

        var campaign = store.Read(c => Store.List(c, "campaign").FirstOrDefault(d => d.S("sharingToken") == token))
            ?? throw new ApiError("النموذج غير موجود أو انتهت صلاحيته.", 404);
        J.Need(IsFormTargetedToRider(campaign, rider, iqama), "النموذج غير موجود أو غير متاح لهذا المندوب.", 404);

        if (IbanChoices.Is(campaign))
        {
            string phone = payload.S("phone").Trim();
            if (string.IsNullOrEmpty(phone)) phone = rider.S("phone").Trim();
            J.Need(!string.IsNullOrEmpty(phone), "رقم الجوال مطلوب.");
            string city = payload.S("city").Trim();
            if (string.IsNullOrEmpty(city)) city = rider.S("city").Trim();
            J.Need(!string.IsNullOrEmpty(city), "اختر المدينة من القائمة.");
            var cleanIban = new JsonObject
            {
                ["iqama"] = iqama,
                ["name"] = payload.S("name"),
                ["phone"] = phone,
                ["city"] = city,
                ["iban"] = payload.S("iban"),
                ["bankCode"] = payload.S("bankCode"),
                ["platforms"] = (payload["platforms"] as JsonArray)?.DeepClone() ?? new JsonArray("keeta")
            };
            bool existing = store.Read(c => Store.ListWhere(c, "submission", null, 1, ("campaignId", campaign.S("id")), ("iqama", iqama)).Count > 0);
            return existing ? IbanChoices.Edit(store, campaign, cleanIban) : IbanChoices.Create(store, campaign, cleanIban);
        }

        return store.Write<IResult>((c, tx) =>
        {
            var openCamp = SubmissionChoices.Open(c, tx, campaign);
            var choice = WorkChoices.Validate(new JsonObject
            {
                ["iqama"] = iqama,
                ["name"] = payload.S("name"),
                ["workSystem"] = payload.Required("workSystem")
            });
            var existing = SubmissionChoices.Find(c, tx, openCamp, iqama);
            var answers = CampaignOperations.Answers(openCamp, payload);
            if (existing != null)
            {
                SubmissionChoices.ArchiveAndEdit(existing, choice);
                foreach (var f in answers) existing[f.Key] = f.Value?.DeepClone();
                Store.Save(c, "submission", existing, tx);
                Store.Audit(c, tx, "submission.edited", existing.S("id"), "rider:" + iqama);
                return Results.Ok(new { ok = true, receipt = existing.S("id"), iqama, status = existing.S("status"), version = SubmissionChoices.Version(existing) });
            }
            var d = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["campaignId"] = openCamp.S("id"),
                ["schema"] = "work-choice-v1",
                ["iqama"] = choice.Iqama,
                ["name"] = choice.Name,
                ["workSystem"] = choice.WorkSystem,
                ["status"] = "pending",
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["version"] = 1,
                ["isEdited"] = false,
                ["history"] = new JsonArray()
            };
            foreach (var f in answers) d[f.Key] = f.Value?.DeepClone();
            Store.Save(c, "submission", d, tx);
            Store.Audit(c, tx, "submission.received", d.S("id"), "rider:" + iqama);
            return Results.Ok(new { ok = true, receipt = d.S("id"), iqama, status = "pending", version = 1 });
        });
    }

    static JsonObject ForSupervisor(DbConnection c, DbTransaction? tx, ClaimsPrincipal actor, string id)
    {
        AccessPolicy.Demand(actor, "advances.request");
        var a = Store.Get(c, id, Kind, tx)?.Clone();
        // Not found and not yours look the same, so ids cannot be probed.
        J.Need(a != null && OrgDirectory.Supervises(c, tx, actor.Identity!.Name!, a.S("riderIqama")), "الطلب غير موجود.", 404);
        return a!;
    }

    public static JsonObject Convert(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        var a = ForSupervisor(c, tx, actor, id);
        J.Need(a.S("status") == "open", "تم البت في الطلب بالفعل.", 409);
        var request = AdvanceRequests.Create(c, tx, actor, new()
        {
            ["riderIqama"] = a.S("riderIqama"),
            ["type"] = input.S("type", a.S("type")),
            ["amount"] = input["amount"]?.DeepClone() ?? a.N("amount"),
            ["reason"] = input.S("reason", "").Trim() is { Length: > 2 } r ? r : "طلب المندوب من التطبيق: " + a.S("reason")
        });
        a["status"] = "converted";
        a["advanceRequestId"] = request.S("id");
        a["decidedBy"] = actor.Identity!.Name;
        a["decidedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, Kind, a, tx);
        Store.Audit(c, tx, "rider_ask.converted", id, actor.Identity!.Name!);
        Notifications.CloseTasks(c, tx, Kind, id, actor.Identity!.Name!);
        PushNotifications.Enqueue(c, tx, a.S("riderIqama"),
            "تحديث طلب السلفة", $"تم رفع طلب السلفة برقم {request.S("id")} إلى الإدارة المالية.", "/rider-app/requests",
            "Advance Request Update", "ایڈوانس درخواست کی تازہ کاری",
            $"Advance request {request.S("id")} has been forwarded to accounting.",
            $"ایڈوانس درخواست نمبر {request.S("id")} اکاؤنٹس کو بھیج دی گئی ہے۔");
        return new() { ["ask"] = a, ["advanceRequest"] = request };
    }

    public static JsonObject Decline(DbConnection c, DbTransaction tx, ClaimsPrincipal actor, string id, JsonObject input)
    {
        var a = ForSupervisor(c, tx, actor, id);
        J.Need(a.S("status") == "open", "تم البت في الطلب بالفعل.", 409);
        string note = input.S("note").Trim();
        J.Need(note.Length is > 2 and <= 500, "اكتب سبب الرفض ليظهر للمندوب.");
        a["status"] = "declined";
        a["note"] = note;
        a["decidedBy"] = actor.Identity!.Name;
        a["decidedAt"] = DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c, Kind, a, tx);
        Store.Audit(c, tx, "rider_ask.declined", id, actor.Identity!.Name!);
        Notifications.CloseTasks(c, tx, Kind, id, actor.Identity!.Name!);
        PushNotifications.Enqueue(c, tx, a.S("riderIqama"),
            "تحديث طلب السلفة", $"تم رفض طلب السلفة: {note}", "/rider-app/requests",
            "Advance Request Update", "ایڈوانس درخواست کی تازہ کاری",
            $"Advance request declined: {note}",
            $"ایڈوانس درخواست مسترد کر دی گئی: {note}");
        return a;
    }

    public static void Map(WebApplication app, RouteGroupBuilder api, Store store)
    {
        app.MapGet("/api/rider-mobile/forms", (HttpContext ctx) =>
        {
            var session = PushNotifications.AuthenticateAndRateLimit(store, ctx, write: false);
            return store.Read(c => FormSchemasCatalog(c, session.S("iqama")));
        }).WithTags("Riders").WithSummary("Read unified form schemas and active campaigns for the authenticated rider app");

        app.MapPost("/api/rider-mobile/forms/{token}/submit", (string token, JsonObject input, HttpContext ctx) =>
        {
            return SubmitInAppForm(store, ctx, token, input);
        }).WithTags("Riders").WithSummary("Submit an open form or campaign from inside the rider app using the authenticated session identity");

        app.MapGet("/api/rider-mobile/requests", (HttpContext ctx) =>
        {
            PushNotifications.AuthenticateAndRateLimit(store, ctx, write: false);
            return store.Read(c =>
            {
                string iqama = MobileSessions.Validate(c, ctx).S("iqama");
                var asks = Store.ListWhere(c, Kind, null, PushNotifications.MaxRiderListItems, ("riderIqama", iqama))
                    .Select(a => RiderView(c, a));
                var serviceReqs = Store.ListWhere(c, ServiceKind, null, PushNotifications.MaxRiderListItems, ("riderIqama", iqama))
                    .Select(ServiceRiderView);
                var combined = asks.Concat(serviceReqs)
                    .OrderByDescending(x => x.S("createdAt"))
                    .Take(PushNotifications.MaxRiderListItems)
                    .ToList();
                return ApiPagination.Apply(combined, ctx.Request.Query);
            });
        }).WithTags("Riders").WithSummary("Read own advance asks and two-way service requests with status timeline and replies");

        app.MapPost("/api/rider-mobile/requests", (JsonObject input, HttpContext ctx) =>
        {
            PushNotifications.AuthenticateAndRateLimit(store, ctx, write: true);
            string reqType = input.S("requestType", "advance").Trim().ToLowerInvariant();
            if (reqType == "" || reqType == "advance")
                return store.Write((c, tx) => Ask(c, tx, ctx, input));
            return store.Write((c, tx) => CreateServiceRequest(c, tx, ctx, input));
        }).WithTags("Riders").WithSummary("Rider submits an advance ask or a service request (annual leave, IBAN update, data update, statement, complaint, appointment)");

        app.MapPost("/api/rider-mobile/requests/{id}/cancel", (string id, HttpContext ctx) =>
        {
            PushNotifications.AuthenticateAndRateLimit(store, ctx, write: true);
            return store.Write((c, tx) => Cancel(c, tx, ctx, id));
        }).WithTags("Riders").WithSummary("Rider cancels an open advance ask or service request");

        app.MapPost("/api/rider-mobile/requests/{id}/reply", (string id, JsonObject input, HttpContext ctx) =>
        {
            PushNotifications.AuthenticateAndRateLimit(store, ctx, write: true);
            return store.Write((c, tx) => RiderReply(c, tx, ctx, id, input));
        }).WithTags("Riders").WithSummary("Rider sends a reply on an existing request");

        api.MapGet("/rider-mobile/service-requests", (string? requestType, string? status, string? iqama, HttpContext ctx) =>
            store.Read(c =>
            {
                var list = Store.List(c, ServiceKind)
                    .Where(r => CanStaffSeeServiceRequest(c, ctx.User, r) &&
                                (string.IsNullOrWhiteSpace(requestType) || r.S("requestType") == requestType) &&
                                (string.IsNullOrWhiteSpace(status) || r.S("status") == status) &&
                                (string.IsNullOrWhiteSpace(iqama) || r.S("riderIqama") == iqama))
                    .OrderByDescending(r => r.S("createdAt"))
                    .ToList();
                return ApiPagination.Apply(list, ctx.Request.Query);
            })).WithTags("Riders").WithSummary("List rider service requests within caller scope, hiding confidential complaints from unauthorized staff");

        api.MapPost("/rider-mobile/service-requests", (JsonObject input, HttpContext ctx) =>
        {
            var res = store.Write((c, tx) => CreateAdminRequestToRider(c, tx, ctx.User, input));
            PushNotifications.FlushOutbox(store);
            return res;
        }).WithTags("Riders").WithSummary("Admin or staff sends a two-way service request to a rider");

        api.MapPost("/rider-mobile/service-requests/{id}/decide", (string id, JsonObject input, HttpContext ctx) =>
        {
            var res = store.Write((c, tx) => DecideServiceRequest(c, tx, ctx.User, id, input));
            PushNotifications.FlushOutbox(store);
            return res;
        }).WithTags("Riders").WithSummary("Approve (with second-approver enforcement for IBAN/data), reject, or request info on a rider service request");

        api.MapPost("/rider-mobile/service-requests/{id}/reply", (string id, JsonObject input, HttpContext ctx) =>
        {
            var res = store.Write((c, tx) => StaffReplyServiceRequest(c, tx, ctx.User, id, input));
            PushNotifications.FlushOutbox(store);
            return res;
        }).WithTags("Riders").WithSummary("Staff replies to a rider service request and notifies the rider via FCM");

        api.MapGet("/advance-requests/rider-asks", (string? status, HttpContext ctx) => store.Read(c =>
        {
            AccessPolicy.Demand(ctx.User, "advances.request");
            string me = ctx.User.Identity!.Name!;
            return Store.List(c, Kind).Where(a => OrgDirectory.Supervises(c, null, me, a.S("riderIqama")) && (string.IsNullOrWhiteSpace(status) || a.S("status") == status)).OrderByDescending(a => a.S("createdAt")).ToList();
        }))
            .WithTags("Debts and advances").WithSummary("Supervisor lists advance asks from currently supervised riders");
        api.MapPost("/advance-requests/rider-asks/{id}/convert", (string id, JsonObject input, HttpContext ctx) => { var res = store.Write((c, tx) => Convert(c, tx, ctx.User, id, input)); PushNotifications.FlushOutbox(store); return res; })
            .WithTags("Debts and advances").WithSummary("Supervisor converts a rider ask into a formal advance request");
        api.MapPost("/advance-requests/rider-asks/{id}/decline", (string id, JsonObject input, HttpContext ctx) => { var res = store.Write((c, tx) => Decline(c, tx, ctx.User, id, input)); PushNotifications.FlushOutbox(store); return res; })
            .WithTags("Debts and advances").WithSummary("Supervisor declines a rider ask with a reason shown to the rider");
    }
}
