using System.Data.Common;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    public const int MaxEdits = 3;

    public static JsonObject Edit(DbConnection c, DbTransaction tx, string token, JsonObject input, string authenticatedIqama, Store? store = null, AttachmentWriteBatch? attachments = null)
    {
        var round = ByToken(c, token, tx);
        var section = ActiveSection(round);
        string iqama = J.Iqama(authenticatedIqama);
        string id = section == null ? ReplyId(round.S("id"), iqama) : ReplyId(round.S("id"), section.S("key"), iqama);
        var reply = Store.Get(c, id, ResponseKind, tx) ?? throw new ApiError("الرد غير موجود.", 404, "not_found");
        J.Need(reply.S("iqama") == iqama, "هوية المندوب لا تطابق الرد.", "identity_mismatch", 403);
        J.Need(round.S("status") == "open", "الجولة مغلقة للتعديل.", "round_closed", 409);
        int edits = (int)reply.N("editCount");
        J.Need(edits < MaxEdits, "وصلت للحد الأقصى لتعديل هذا الرد (3 مرات).", "edit_limit", 409);
        foreach (string locked in new[] { "iqama", "name", "city", "workApps" })
            J.Need(input[locked] == null || input.S(locked) == "" || input.S(locked) == reply.S(locked), "لا يمكن تعديل الهوية أو المدينة أو الجنسية.", "edit_identity_forbidden");
        // Section flow sends nationality id (egyptian) and local 05 phone; stored values are display + E.164.
        if (input["nationality"] != null && input.S("nationality") != "")
        {
            string raw = input.S("nationality");
            string canonicalId = CanonicalNationality(raw, out string display);
            J.Need(raw == reply.S("nationality") || raw == reply.S("nationalityId") || canonicalId == reply.S("nationalityId") || display == reply.S("nationality"),
                "لا يمكن تعديل الهوية أو المدينة أو الجنسية.", "edit_identity_forbidden");
        }
        if (input["nationalityId"] != null && input.S("nationalityId") != "")
            J.Need(input.S("nationalityId") == reply.S("nationalityId"), "لا يمكن تعديل الهوية أو المدينة أو الجنسية.", "edit_identity_forbidden");
        if (input["phone"] != null && input.S("phone") != "")
        {
            string normalized = RiderDirectory.Phone(input.S("phone"));
            J.Need(normalized == reply.S("phone") || input.S("phone") == reply.S("phone"),
                "لا يمكن تعديل الهوية أو المدينة أو الجنسية.", "edit_identity_forbidden");
        }

        long replyBytes = 0;
        var staffInput = WithPreservedAttachments(reply, input, true);
        var appInput = WithPreservedAttachments(reply, input, false);
        reply["staffAnswers"] = Answers(round, staffInput, true, reply.S("city"), store, c, tx, id, ref replyBytes, attachments);
        reply["appAnswers"] = Answers(round, appInput, false, reply.S("city"), store, c, tx, id, ref replyBytes, attachments);
        if (section != null && section.S("kind") == "app")
        {
            reply["financeAnswer"] = ParseFinanceAnswer(input, store, c, tx, id, ref replyBytes, attachments);
            reply["systemsAnswer"] = ParseSystemsAnswer(input, store, c, tx, id, ref replyBytes, attachments);
        }
        reply["editCount"] = edits + 1;
        reply["updatedAt"] = Now();
        if (reply.S("editReceipt") == "") reply["editReceipt"] = NewEditReceipt();
        Store.Save(c, ResponseKind, reply, tx);

        foreach (var answer in reply["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => a.B("complaint")))
        {
            bool exists = Store.ListWhere(c, RiderAsks.ServiceKind, tx, null, ("evaluationResponseId", id))
                .Any(r => r["payload"].S("targetId") == answer.S("staffId"));
            if (!exists) CreateComplaint(c, tx, reply, answer);
        }
        if (reply["financeAnswer"] is JsonObject finance && finance.B("complaint"))
        {
            bool exists = Store.ListWhere(c, RiderAsks.ServiceKind, tx, null, ("evaluationResponseId", id))
                .Any(r => r["payload"].S("targetKind") == "department" && r["payload"].S("target") == DeptFinance);
            if (!exists) CreateDepartmentComplaint(c, tx, reply, finance, DeptFinance);
        }
        if (reply["systemsAnswer"] is JsonObject systems && systems.B("complaint"))
        {
            bool exists = Store.ListWhere(c, RiderAsks.ServiceKind, tx, null, ("evaluationResponseId", id))
                .Any(r => r["payload"].S("targetKind") == "department" && r["payload"].S("target") == DeptSystems);
            if (!exists) CreateDepartmentComplaint(c, tx, reply, systems, DeptSystems);
        }
        Store.Audit(c, tx, "evaluation.edited", id + "#" + reply.N("editCount"), "rider-app");
        int remaining = MaxEdits - (int)reply.N("editCount");
        return new()
        {
            ["receipt"] = reply.S("editReceipt"),
            ["editCount"] = reply.N("editCount"),
            ["editsRemaining"] = remaining,
            ["allowedActions"] = remaining > 0 ? new JsonArray("edit") : new JsonArray()
        };
    }

    static JsonObject WithPreservedAttachments(JsonObject reply, JsonObject input, bool staff)
    {
        string field = staff ? "staffAnswers" : "appAnswers", key = staff ? "staffId" : "app";
        var existing = reply[field]!.AsArray().OfType<JsonObject>().ToDictionary(a => a.S(key), a => a);
        var cards = Cards(input, field);
        var rebuilt = new JsonArray();
        foreach (var card in cards)
        {
            var clone = card.Clone();
            if (clone["attachments"] == null && existing.TryGetValue(card.S(key), out var prior))
                clone["attachments"] = prior["attachments"]?.DeepClone() ?? new JsonArray();
            rebuilt.Add(clone);
        }
        return new() { [field] = rebuilt };
    }
}
