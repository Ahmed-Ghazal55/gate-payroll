using System.Text.Json.Nodes;
namespace Payroll;

public static class ErpRiderClassification
{
    // ERP permits administrative employees to have account/vehicle profiles.
    // Preserve those references, but keep them out of the delivery-rider directory.
    // Existing documents without this import provenance retain their old behavior.
    public static bool ListedAsRider(JsonObject row)=>!row.B("debtOnly")&&row.S("personRole")!="debt_only"&&(row.S("erpSource")!="rider"||row.S("personRole")!="administrative");
}
