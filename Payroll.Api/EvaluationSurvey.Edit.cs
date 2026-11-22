using System.Data.Common;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    public const int MaxEdits = 3;

    public static JsonObject Edit(DbConnection c, DbTransaction tx, string token, JsonObject input, string authenticatedIqama, Store? store = null)
    {
        var round = ByToken(c, token, tx);
        string iqama = J.Iqama(authenticatedIqama);
        string id = ReplyId(round.S("id"), iqama);
        var reply = Store.Get(c, id, ResponseKind, tx) ?? throw new ApiError("الرد غير موجود.", 404, "not_found");
        J.Need(reply.S("iqama") == iqama, "هوية المندوب لا تطابق الرد.", "identity_mismatch", 403);
        J.Need(round.S("status") == "open", "الجولة مغلقة للتعديل.", "round_closed", 409);
        int edits = (int)reply.N("editCount");
        J.Need(edits < MaxEdits, "وصلت للحد الأقصى لتعديل هذا الرد (3 مرات).", "edit_limit", 409);
        foreach (string locked in new[] { "iqama", "name", "city", "nationality", "nationalityId", "phone", "workApps" })
            J.Need(input[locked] == null || input.S(locked) == "" || input.S(locked) == reply.S(locked), "لا يمكن تعديل الهوية أو المدينة أو الجنسية.", "edit_identity_forbidden");

        long replyBytes = 0;
        var staffInput = WithPreservedAttachments(reply, input, true);
        var appInput = WithPreservedAttachments(reply, input, false);
        reply["staffAnswers"] = Answers(round, staffInput, true, reply.S("city"), store, c, tx, id, ref replyBytes);
        reply["appAnswers"] = Answers(round, appInput, false, reply.S("city"), store, c, tx, id, ref replyBytes);
        reply["editCount"] = edits + 1;
        reply["updatedAt"] = Now();
        Store.Save(c, ResponseKind, reply, tx);

        foreach (var answer in reply["staffAnswers"]!.AsArray().OfType<JsonObject>().Where(a => a.B("complaint")))
        {
            bool exists = Store.ListWhere(c, RiderAsks.ServiceKind, tx, null, ("evaluationResponseId", id))
                .Any(r => r["payload"].S("targetId") == answer.S("staffId"));
            if (!exists) CreateComplaint(c, tx, reply, answer);
        }
        Store.Audit(c, tx, "evaluation.edited", id + "#" + reply.N("editCount"), "rider-app");
        int remaining = MaxEdits - (int)reply.N("editCount");
        return new()
        {
            ["receipt"] = id,
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
