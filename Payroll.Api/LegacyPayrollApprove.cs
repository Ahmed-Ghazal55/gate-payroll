using System.Security.Claims;

namespace Payroll;

/// <summary>TRIAL9 / grok-TRIAL8: legacy one-step POST /payroll/approve must not post alone.
/// Prefer Independent via Program.cs; RefuseOneStepPost remains for closed-route tests.</summary>
public static class LegacyPayrollApprove
{
    public const string DisabledMessage=
        "اعتماد المسير السابق متوقف؛ استخدم مسار دورة الرواتب باعتماد شخص مستقل (أو صلاحية اعتماد مستنداته بنفسه خارج الصرف).";

    public static void RefuseOneStepPost(ClaimsPrincipal? actor=null)
    {
        _=actor;
        throw new ApiError(DisabledMessage,409);
    }
}
