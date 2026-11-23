using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Payroll;

public static partial class EvaluationSurvey
{
    public const string LaunchKind = "evaluation_launch";
    public const string SurveySessionKind = "evaluation_survey_session";
    public static readonly TimeSpan LaunchTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SurveySessionTtl = TimeSpan.FromHours(2);
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static bool EqualHash(string a, string b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public static string LaunchRecordId(string code) => "evlaunch:" + Hash(code);
    static string SurveySessionId(string token) => "evsurvey:" + Hash(token);

    public static JsonObject IssueLaunch(DbConnection c, DbTransaction tx, string iqama, string sharingToken, string publicBaseUrl)
    {
        J.Iqama(iqama);
        var round = ByToken(c, sharingToken, tx);
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" || baseUri.AbsolutePath is not "/" and not "")
            throw new ApiError("عنوان الاستضافة غير صالح.", 500, "launch_base_invalid");
        string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var row = new JsonObject
        {
            ["id"] = LaunchRecordId(code),
            ["codeHash"] = Hash(code),
            ["iqama"] = iqama,
            ["roundId"] = round.S("id"),
            ["sharingToken"] = sharingToken,
            ["createdAt"] = Now(),
            ["expiresAt"] = DateTimeOffset.UtcNow.Add(LaunchTtl).ToString("O"),
            ["usedAt"] = ""
        };
        Store.Save(c, LaunchKind, row, tx);
        // Put the one-time code in the URL fragment so proxies never log it on the document request.
        string url = publicBaseUrl.TrimEnd('/') + "/rider-form.html?evaluation=" + Uri.EscapeDataString(sharingToken)
            + "&mode=app#launch=" + Uri.EscapeDataString(code);
        return new() { ["url"] = url };
    }

    static void DemandActiveRider(DbConnection c, string iqama, DbTransaction? tx)
    {
        var rider = Store.Get(c, iqama, "rider", tx);
        if (rider == null || !rider.S("status").Equals("Active", StringComparison.OrdinalIgnoreCase))
            throw new ApiError("جلسة التقييم منتهية أو الحساب موقوف.", 401, "survey_session_inactive");
    }

    public static JsonObject ExchangeLaunch(DbConnection c, DbTransaction tx, string sharingToken, string code)
    {
        J.Need(code.Length == 64 && code.All(Uri.IsHexDigit), "رمز التشغيل غير صالح.", "launch_invalid", 401);
        var round = ByToken(c, sharingToken, tx);
        var row = Store.Get(c, LaunchRecordId(code), LaunchKind, tx);
        if (row == null || !EqualHash(row.S("codeHash"), Hash(code)) || row.S("sharingToken") != sharingToken || row.S("roundId") != round.S("id"))
            throw new ApiError("رمز التشغيل غير صالح.", 401, "launch_invalid");
        if (row.S("usedAt") != "") throw new ApiError("رمز التشغيل مستخدم من قبل.", 401, "launch_used");
        if (!DateTimeOffset.TryParse(row.S("expiresAt"), out var until) || until <= DateTimeOffset.UtcNow)
            throw new ApiError("انتهت صلاحية رمز التشغيل.", 401, "launch_expired");
        DemandActiveRider(c, row.S("iqama"), tx);
        row["usedAt"] = Now();
        Store.Save(c, LaunchKind, row, tx);

        string accessToken = "eves_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var session = new JsonObject
        {
            ["id"] = SurveySessionId(accessToken),
            ["iqama"] = row.S("iqama"),
            ["roundId"] = round.S("id"),
            ["sharingToken"] = sharingToken,
            ["createdAt"] = Now(),
            ["expiresAtUtc"] = DateTimeOffset.UtcNow.Add(SurveySessionTtl).ToString("O")
        };
        Store.Save(c, SurveySessionKind, session, tx);
        return new() { ["accessToken"] = accessToken, ["iqama"] = row.S("iqama"), ["expiresAt"] = session.S("expiresAtUtc") };
    }

    public static JsonObject AuthenticateEvaluation(Store store, HttpContext ctx, string sharingToken)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer eves_", StringComparison.Ordinal) && header.Length == 76 && header[12..].All(Uri.IsHexDigit))
            return store.Read(c => ValidateSurveySession(c, header[7..], sharingToken));
        var session = MobileSessions.Authenticate(store, ctx);
        store.Read(c => ByToken(c, sharingToken));
        return session;
    }

    public static JsonObject ValidateEvaluation(DbConnection c, HttpContext ctx, string sharingToken, DbTransaction? tx = null)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer eves_", StringComparison.Ordinal) && header.Length == 76 && header[12..].All(Uri.IsHexDigit))
            return ValidateSurveySession(c, header[7..], sharingToken, tx);
        var session = MobileSessions.Validate(c, ctx, null, tx);
        ByToken(c, sharingToken, tx);
        return session;
    }

    static JsonObject ValidateSurveySession(DbConnection c, string accessToken, string sharingToken, DbTransaction? tx = null)
    {
        var session = Store.Get(c, SurveySessionId(accessToken), SurveySessionKind, tx);
        if (session == null || session.S("sharingToken") != sharingToken
            || !DateTimeOffset.TryParse(session.S("expiresAtUtc"), out var until) || until <= DateTimeOffset.UtcNow)
            throw new ApiError("جلسة التقييم منتهية أو غير صالحة.", 401, "survey_session_invalid");
        var round = ByToken(c, sharingToken, tx);
        J.Need(session.S("roundId") == round.S("id"), "جلسة التقييم لا تطابق هذه الجولة.", "survey_session_round", 401);
        DemandActiveRider(c, session.S("iqama"), tx);
        return session;
    }
}
