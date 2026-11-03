using System.Text.Json.Nodes;
namespace Payroll;

public static class ApiErrors
{
    public static string Code(int status) => status switch
    {
        400 => "invalid_request", 401 => "unauthorized", 403 => "forbidden", 404 => "not_found",
        409 => "conflict", 423 => "account_locked", 429 => "rate_limited", _ => "request_failed"
    };
    public static object Body(int status, string? message = null) => new { error = message ?? status switch
    {
        401 => "سجل الدخول أولًا.", 403 => "ليست لديك صلاحية لهذا الإجراء.",
        404 => "العنصر غير موجود.", 409 => "تعارض الطلب مع الحالة الحالية.",
        423 => "الحساب مقفل مؤقتًا.", 429 => "محاولات كثيرة؛ حاول بعد قليل.", _ => "تعذر إتمام الطلب."
    }, code = Code(status) };
    public static JsonObject Duplicate(JsonObject body)
    {
        body["code"] = "already_submitted";
        body["error"] = "سبق إرسال هذا الرد.";
        return body;
    }
}
