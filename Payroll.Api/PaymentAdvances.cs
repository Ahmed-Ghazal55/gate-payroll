using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-07 stub: engine approve calls this; full advances land in U-W2-09.</summary>
public static class PaymentAdvances
{
    internal static void DemandApproval(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject input)
    {
        if(doc.S("type") is not ("advance" or "hs_topup"))return;
        throw new ApiError("مسار السلف تحت المدفوعات غير مفعل بعد.",409);
    }
}
