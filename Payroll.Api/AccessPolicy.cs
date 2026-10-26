using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// One route policy is used by the API, UI capability list and offline review tests.
public static class AccessPolicy
{
    public static bool Administrator(ClaimsPrincipal user)=>user.IsInRole("Admin")||user.IsInRole("SuperAdmin");
    public static string[] CompatibilityDefaults(string role)=>role switch {
        "Accountant"=>["payments.record","adjustments.create","imports.view","policies.view","inquiry.issue","treasury.disburse","treasury.jahez.receive","treasury.journals","treasury.liability.settle","debts.create.housing","debts.create.administrative","debts.create.equipment"],
        "FinancialAccountant"=>["payments.record","adjustments.create","imports.view","policies.view","inquiry.issue","treasury.disburse","treasury.jahez.receive","treasury.journals","treasury.opening","treasury.liability.settle","debts.create.housing","debts.create.administrative","debts.create.equipment"],
        "FleetSupervisor"=>["debts.create.equipment"],
        "OperationsManager"=>["debts.create.equipment","forms.view"],
        "Reviewer"=>["forms.view"],
        "HRSpecialist"=>["forms.view"],
        "HousingSupervisor"=>["debts.create.housing"],
        _=>[]
    };
    public static bool Has(ClaimsPrincipal user,string permission)=>user.Identity?.IsAuthenticated==true&&(Administrator(user)||user.HasClaim("permission",permission));
    public static void Demand(ClaimsPrincipal user,string permission){if(!Has(user,permission))throw new ApiError("ليست لديك صلاحية لهذه العملية: "+permission,403);}
    public static string CategoryPermission(string category)=>category switch {
        "WorkPermit" or "IqamaRenewal" or "SponsorshipTransfer" or "ExitReEntryVisa" or "MedicalInsurance" or "ProfessionChange" or "DriverLicense" or "DriverCard" or "OtherHrService"=>"debts.create.hr",
        "Advance" or "CashAdvance" or "BankAdvance" or "CashCustody"=>"debts.create.advance",
        "HousingDeduction"=>"debts.create.housing",
        "PlatformTransferredDebt" or "NegativeSalaryCarryover"=>"debts.create.platform",
        "EquipmentLoan"=>"debts.create.equipment",
        "TrafficFine" or "AccidentDamage" or "VehicleInstallment" or "FuelOverage"=>"debts.create.fleet",
        "AdministrativeDeduction"=>"debts.create.administrative",
        _=>"invalid.permission"
    };
    public static bool Allowed(ClaimsPrincipal user,string method,string path,JsonObject? input=null)
    {
        if(user.Identity?.IsAuthenticated!=true)return false;
        if(Administrator(user))return true;
        bool Get(string prefix)=>method=="GET"&&path.StartsWith(prefix,StringComparison.Ordinal);
        bool Has(string permission)=>AccessPolicy.Has(user,permission);
        if(path is "/api/me" or "/api/roles-catalog")return method=="GET";
        if(path.StartsWith("/api/users",StringComparison.Ordinal))return false; // User administration remains administrator-only.
        if(path=="/api/excel/export")return method=="POST"&&Has("riders.view");
        if(path.StartsWith("/api/excel/",StringComparison.Ordinal))
        {
            var parts=path.Split('/');if(parts.Length!=5)return false;
            string kind=parts[3],operation=parts[4];
            if(method=="GET"&&operation=="template")return Has(kind switch{"riders"=>"riders.view","choices"=>"forms.view","work"=>"imports.view",_=>"invalid.permission"});
            if(method!="POST"||operation is not ("preview" or "commit"))return false;
            return kind switch{"riders"=>Has("riders.edit")&&(operation!="commit"||Has("riders.financial_edit")),"choices"=>Has("forms.manage"),"work"=>Has(operation=="commit"?"imports.commit":"imports.view"),_=>false};
        }
        if(path=="/api/dashboard")return method=="GET"&&Has("payroll.view");
        if(path.StartsWith("/api/system/",StringComparison.Ordinal))return false;
        if(path=="/api/riders/sync-baseline"||path=="/api/riders/enrich-missing")return false;
        if(path=="/api/riders/data-quality"||path=="/api/riders")return method=="GET"?Has("riders.view"):method=="POST"&&Has("riders.edit");
        if(path.StartsWith("/api/riders/",StringComparison.Ordinal))return path.EndsWith("/profile-360",StringComparison.Ordinal)?method=="GET"&&Has("riders.view_360")&&Has("debts.view"):method=="GET"?Has("riders.view_360"):method=="POST"&&path.EndsWith("/documents",StringComparison.Ordinal)&&Has("riders.edit");
        if(path.StartsWith("/api/debts",StringComparison.Ordinal))
        {
            if(path.Contains("/reconcile-opening/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.opening");
            if(method=="GET")return Has("debts.view");
            if(path=="/api/debts")return method=="POST"&&Has(input==null?"invalid.permission":CategoryPermission(input.S("category")));
            if(path.StartsWith("/api/debts/import-opening/",StringComparison.Ordinal))return method=="POST"&&Has("debts.import_opening");
            if(path.StartsWith("/api/debts/bulk-collect/",StringComparison.Ordinal))return method=="POST"&&Has("debts.collect");
            if(path.EndsWith("/collect",StringComparison.Ordinal))return method=="POST"&&Has("debts.collect")&&((input?.N("waivedAmount")??0)==0||Has("debts.waive"));
            if(path.EndsWith("/transfer-sponsor",StringComparison.Ordinal))return method=="POST"&&Has("debts.transfer_sponsor");
            return method=="PUT"&&Has("debts.reschedule");
        }
        if(path.StartsWith("/api/finance/statement/",StringComparison.Ordinal))return method=="GET"&&Has("debts.view");
        if(path=="/api/finance/catalog")return method=="GET"&&(Has("debts.view")||Has("treasury.view"));
        if(path.StartsWith("/api/finance/opening/",StringComparison.Ordinal))return Has("treasury.opening");
        if(path.StartsWith("/api/finance/legacy-cash-allocation/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.opening");
        if(path.StartsWith("/api/finance/jahez-handovers/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.jahez.receive")&&(path.EndsWith("/confirm",StringComparison.Ordinal)||path.EndsWith("/reject",StringComparison.Ordinal));
        if(path.StartsWith("/api/debts/",StringComparison.Ordinal)&&path.Contains("/reconcile-opening/",StringComparison.Ordinal))return Has("treasury.opening");
        if(path.StartsWith("/api/finance/",StringComparison.Ordinal))return method=="GET"?Has("treasury.view"):method=="POST"&&(path switch{
            "/api/finance/vault-transfer"=>Has("treasury.transfer"),
            "/api/finance/liability-payment"=>Has("treasury.liability.settle"),
            "/api/finance/jahez-transfer"=>Has("treasury.transfer"),
            "/api/finance/jahez-platform-settlement"=>Has("treasury.transfer"),
            "/api/finance/jahez-fee-deposit"=>Has("treasury.transfer"),
            "/api/finance/jahez-fee"=>Has("treasury.jahez.manage"),
            "/api/finance/jahez-handover"=>Has("treasury.jahez.handover"),_=>false});
        if(path.StartsWith("/api/approvals",StringComparison.Ordinal))return method=="GET"?Has("approvals.view"):method=="POST"&&path.EndsWith("/decide",StringComparison.Ordinal)&&Has("approvals.decide");
        if(path.StartsWith("/api/notifications",StringComparison.Ordinal))return method=="GET"?Has("approvals.view"):method=="POST"&&Has("mobile.broadcast");
        if(path=="/api/inquiry-access")return method=="POST"&&Has("inquiry.issue");
        if(path.StartsWith("/api/rider-mobile/firebase-config",StringComparison.Ordinal))return false;
        if(path.StartsWith("/api/rider-mobile/",StringComparison.Ordinal))return method=="GET"?Has("mobile.broadcast"):method=="POST"&&path=="/api/rider-mobile/broadcast"&&Has("mobile.broadcast");
        if(path.StartsWith("/api/promissory-notes",StringComparison.Ordinal))return method=="GET"&&Has("debts.view");
        if(path.StartsWith("/api/payroll",StringComparison.Ordinal))return method=="GET"?Has("payroll.view"):method=="POST"&&path=="/api/payroll/approve"&&Has("payroll.approve");
        if(path.StartsWith("/api/ledger",StringComparison.Ordinal))return method=="GET"&&Has("payroll.view");
        if(path=="/api/payments")return method=="POST"&&Has("payments.record");
        if(path=="/api/adjustments")return method=="POST"&&Has("adjustments.create");
        if(path.StartsWith("/api/policies",StringComparison.Ordinal))return method=="GET"?Has("policies.view"):Has("policies.manage");
        if(Get("/api/imports")||path=="/api/template.csv")return Has("imports.view");
        if(path.StartsWith("/api/imports/",StringComparison.Ordinal))return method=="POST"&&(path.EndsWith("/commit",StringComparison.Ordinal)?Has("imports.commit"):Has("imports.view"));
        if(path.StartsWith("/api/excel/choices/",StringComparison.Ordinal))return method=="GET"&&Has("forms.view");
        if(path.StartsWith("/api/excel/riders/",StringComparison.Ordinal))return method=="GET"&&Has("riders.view");
        if(path.StartsWith("/api/campaigns",StringComparison.Ordinal))return method=="GET"?Has("forms.view"):method=="DELETE"?Has("forms.delete"):Has("forms.manage");
        if(path.StartsWith("/api/submissions",StringComparison.Ordinal))return method=="GET"?Has("forms.view"):method=="POST"&&(path.EndsWith("/review",StringComparison.Ordinal)||path=="/api/submissions/bulk-approve")&&Has("forms.review");
        if(Get("/api/forms/catalog")||Get("/api/forms/analytics")||Get("/api/forms/responses"))return Has("forms.view");
        if(path.StartsWith("/api/forms/builder",StringComparison.Ordinal))return Has("forms.manage");
        return false;
    }
    public static void ValidateApproval(ClaimsPrincipal actor,JsonObject request,JsonObject input)
    {
        Demand(actor,"approvals.decide");
        string name=actor.Identity?.Name??"";
        bool same=string.Equals(request.S("requestedBy",request.S("createdBy")),name,StringComparison.OrdinalIgnoreCase);
        if(same&&(!Administrator(actor)||!input.B("overrideSelf")||string.IsNullOrWhiteSpace(input.S("note"))))throw new ApiError("لا يجوز اعتماد طلبك بنفسك؛ يلزم مراجع مستقل أو تجاوز إداري معلل وصريح.",403);
        if(!Administrator(actor)&&(actor.FindFirstValue("department")!=request.S("targetDepartment")||!int.TryParse(actor.FindFirstValue("rankLevel"),out int rank)||rank<(int)request.N("requiredRankLevel",1)))throw new ApiError("القسم أو الرتبة لا يسمحان بالبت في الطلب.",403);
    }
}
