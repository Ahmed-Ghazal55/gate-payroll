using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

// One route policy is used by the API, UI capability list and offline review tests.
public static partial class AccessPolicy
{
    public static bool Administrator(ClaimsPrincipal user)=>user.IsInRole("Admin")||user.IsInRole("SuperAdmin")||user.IsInRole("SystemAdmin");
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
    public static void Demand(ClaimsPrincipal user,string permission)
    {
        if(Has(user,permission))return;
        string nameAr=UserAccess.PermissionsCatalog.FirstOrDefault(p=>p.Code==permission)?.NameAr??permission;
        throw new ApiError("ليست لديك صلاحية لهذه العملية: "+nameAr,403);
    }
    // One mapping for the statements tab / preview: sidebar, AccessPolicy.Allowed, and PayStatements.Build.
    public static bool CanAccessRiderMonthlyStatement(ClaimsPrincipal user)=>
        Administrator(user)||Has(user,"mobile.broadcast")||(Has(user,"pay.view")&&Has(user,"payroll.view")&&Has(user,"debts.view"));
    // Broadcast-only users may read approved-only previews; full finance viewers may also read drafts.
    public static void DemandRiderMonthlyStatement(ClaimsPrincipal user,bool approvedOnly)
    {
        if(Administrator(user))return;
        if(Has(user,"pay.view")&&Has(user,"payroll.view")&&Has(user,"debts.view"))return;
        if(approvedOnly&&Has(user,"mobile.broadcast"))return;
        throw new ApiError("ليست لديك صلاحية لهذه العملية: pay.view",403);
    }
    public static string CategoryPermission(string category)=>category switch {
        "WorkPermit" or "IqamaRenewal" or "SponsorshipTransfer" or "ExitReEntryVisa" or "MedicalInsurance" or "ProfessionChange" or "DriverLicense" or "DriverCard" or "OtherHrService"=>"debts.create.hr",
        "Advance" or "CashAdvance" or "BankAdvance" or "InternetAdvance" or "HsWalletTopUp" or "CashCustody"=>"debts.create.advance",
        "HousingDeduction"=>"debts.create.housing",
        "PlatformTransferredDebt" or "NegativeSalaryCarryover"=>"debts.create.platform",
        "EquipmentLoan"=>"debts.create.equipment",
        "TrafficFine" or "AccidentDamage" or "VehicleInstallment" or "FuelOverage"=>"debts.create.fleet",
        "AdministrativeDeduction" or "LegacyOpeningDebt"=>"debts.create.administrative",
        _=>"invalid.permission"
    };
    public static bool Allowed(ClaimsPrincipal user,string method,string path,JsonObject? input=null)
    {
        if(user.Identity?.IsAuthenticated!=true)return false;
        // A temporary password opens nothing but the screen that replaces it, for administrators too.
        if(user.HasClaim("mustChangePassword","true"))return path is "/api/me" or "/api/me/password" or "/api/auth/logout";
        if(Administrator(user))return true;
        bool Get(string prefix)=>method=="GET"&&path.StartsWith(prefix,StringComparison.Ordinal);
        bool Has(string permission)=>AccessPolicy.Has(user,permission);
        if(path is "/api/me" or "/api/roles-catalog")return method=="GET";
        if(path=="/api/me/password")return method=="POST";
        if(path.StartsWith("/api/roles/",StringComparison.Ordinal))return false;
        if(path.StartsWith("/api/config/lists",StringComparison.Ordinal))return method=="GET"||method=="POST"&&Has(path.EndsWith("/approve",StringComparison.Ordinal)?"pay.approve":"pay.manage");
        if(path.StartsWith("/api/users",StringComparison.Ordinal))return method is "GET" or "POST" or "PUT"&&Has("users.manage"); // Handlers guard escalation and self-edit.
        if(path=="/api/responses")return method=="GET"&&Has("forms.view");
        if(path=="/api/search")return method=="GET";
        if(path.StartsWith("/api/operations/",StringComparison.Ordinal)||path.StartsWith("/api/riders/",StringComparison.Ordinal)&&path.EndsWith("/accounts",StringComparison.Ordinal))return method=="GET"&&Has("operations.view");
        if(path=="/api/pay/apps/chefz/operations/review")return method=="POST"&&Has("pay.view");
        if(path.StartsWith("/api/pay/cycles/",StringComparison.Ordinal)&&path.EndsWith("/advance",StringComparison.Ordinal))return method=="POST"&&(Has("pay.manage")||Has("pay.approve"));
        if(path.StartsWith("/api/pay/",StringComparison.Ordinal))
        {if(method=="GET")return Has("pay.view");return (method=="POST"||method=="DELETE"&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/api/pay/(settings/[^/]+|apps/[^/]+/(terms|schemes|eligibility)/[^/]+)$"))&&Has(path.EndsWith("/approve",StringComparison.Ordinal)?"pay.approve":"pay.manage");}
        if(path=="/api/platforms/ninja/terms/seed")return method=="GET"&&Has("pay.view");
        if(path=="/api/platforms/ninja/invoices/reconcile")return method=="POST"&&Has("pay.view"); // read-only recalculation
        if(path.StartsWith("/api/platforms/ninja/invoices",StringComparison.Ordinal)||path.StartsWith("/api/platforms/keeta/invoices",StringComparison.Ordinal))
            return method=="GET"?Has("pay.view"):method=="POST"&&Has(path.EndsWith("/approve",StringComparison.Ordinal)||path.EndsWith("/post",StringComparison.Ordinal)||path.EndsWith("/resolve",StringComparison.Ordinal)?"pay.approve":"pay.manage");
        if(path.StartsWith("/api/platforms/hs/",StringComparison.Ordinal))
        {
            if(path.EndsWith("/posting",StringComparison.Ordinal))return false;
            if(method=="GET")return Has("platforms.hs.view");
            if(method!="POST")return false;
            if(path.EndsWith("/approve",StringComparison.Ordinal))return Has("platforms.hs.approve");
            if(path.EndsWith("/resolve",StringComparison.Ordinal))return Has(input?.S("mode")=="dispute"?"platforms.hs.approve":"platforms.hs.manage");
            return Has("platforms.hs.manage");
        }
        if(path.StartsWith("/api/companies/import/",StringComparison.Ordinal))return false; // administrators returned above
        if(path=="/api/companies"||path.StartsWith("/api/companies/",StringComparison.Ordinal))return method=="GET"?Has("companies.view"):method is "POST" or "PUT" or "DELETE"&&Has("companies.manage");
        // Section-9 exports (forms/broadcasts) need the same view perms as the sidebar tabs that call them.
        if(path=="/api/excel/export")return method=="POST"&&(Has("riders.view")||Has("companies.view")||Has("forms.view")||Has("mobile.broadcast"));
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
        if(path=="/api/riders/lookup")return method=="GET"; // RiderLookup.Allowed re-checks the picker permissions in the handler.
        if(path=="/api/riders/data-quality"||path=="/api/riders")return method=="GET"?Has("riders.view"):method=="POST"&&Has("riders.edit");
        if(path=="/api/workforce"||path=="/api/workforce/summary"||path=="/api/workforce/distinct"||path=="/api/workforce/export"||path=="/api/workforce/data-quality"||path=="/api/workforce/saudi-payroll-employees"||path=="/api/workforce/terminated"||path=="/api/workforce/absence-cases")return method=="GET"&&Has("riders.view");
        if(path=="/api/workforce/legal-cases")return method=="GET"&&Has("riders.legal_cases.view");
        if(path.StartsWith("/api/workforce/",StringComparison.Ordinal)&&path.EndsWith("/360",StringComparison.Ordinal))return method=="GET"&&Has("riders.view_360")&&Has("debts.view");
        if(path.StartsWith("/api/hr-catalogs/",StringComparison.Ordinal))return method=="GET"&&Has("riders.view");
        if(path.StartsWith("/api/riders/",StringComparison.Ordinal))
        {
            if(path.EndsWith("/profile-360",StringComparison.Ordinal))return method=="GET"&&Has("riders.view_360")&&Has("debts.view");
            return method=="GET"&&Has("riders.view_360");
        }
        if(path.StartsWith("/api/debts",StringComparison.Ordinal))
        {
            if(path.Contains("/reconcile-opening/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.opening");
            if(method=="GET")return Has("debts.view");
            if(path=="/api/debts")return method=="POST"&&Has(input==null?"invalid.permission":CategoryPermission(input.S("category")));
            // Opening balances post against equity: commit needs the treasury opening permission too.
            if(path.StartsWith("/api/debts/import-opening/",StringComparison.Ordinal))return method=="POST"&&Has("debts.import_opening")&&(!path.EndsWith("/commit",StringComparison.Ordinal)||Has("treasury.opening"));
            if(path.StartsWith("/api/debts/bulk-collect/",StringComparison.Ordinal))return method=="POST"&&Has("debts.collect");
            if(path.EndsWith("/collect",StringComparison.Ordinal))return method=="POST"&&Has("debts.collect")&&((input?.N("waivedAmount")??0)==0||Has("debts.waive"));
            if(path.EndsWith("/transfer-sponsor",StringComparison.Ordinal))return method=="POST"&&Has("debts.transfer_sponsor");
            return method=="PUT"&&Has("debts.reschedule");
        }
        if(path=="/api/finance/mada/balance")return method=="GET"&&(Has("advances.payout")||Has("treasury.view"));
        if(path=="/api/finance/accounting-adjustments"||path.StartsWith("/api/finance/accounting-adjustments/",StringComparison.Ordinal))return method=="GET"?Has("finance.journal.create"):method=="POST"&&Has(path.EndsWith("/approve",StringComparison.Ordinal)?"finance.journal.approve":"finance.journal.create");
        if(path=="/api/finance/journals/manual"||path.StartsWith("/api/finance/journals/manual/",StringComparison.Ordinal))
            return method=="GET"?(Has("finance.journal.create")||Has("finance.journal.approve")):method=="DELETE"?Has("finance.journal.create")&&path.Count(ch=>ch=='/')==5:method=="POST"&&Has(path.EndsWith("/approve",StringComparison.Ordinal)||path.EndsWith("/reject",StringComparison.Ordinal)?"finance.journal.approve":"finance.journal.create");
        // Preview for «إرسال كشوف الحساب» (mobile.broadcast) or full finance viewers.
        if(path.StartsWith("/api/finance/statement/rider/",StringComparison.Ordinal)&&path.EndsWith("/monthly",StringComparison.Ordinal))return method=="GET"&&CanAccessRiderMonthlyStatement(user);
        if(path.StartsWith("/api/finance/statement/",StringComparison.Ordinal))return method=="GET"&&Has("debts.view");
        if(path is "/api/finance/bank-file/preview" or "/api/finance/bank-file/export")return method=="POST"&&Has("finance.bankfile.export")&&Has("payroll.view");
        if(path is "/api/finance/trial-balance" or "/api/finance/income-statement")return method=="GET"&&Has("treasury.journals");
        if(path=="/api/finance/catalog")return method=="GET"&&(Has("debts.view")||Has("treasury.view"));
        if(path=="/api/finance/coa"||path.StartsWith("/api/finance/coa/",StringComparison.Ordinal))
        {
            if(method=="GET")return Has("accounting.chart.view")||Has("treasury.view");
            if(method=="DELETE")return false;
            if(method=="PUT")return Has("accounting.chart.edit");
            if(method!="POST")return false;
            if(path.EndsWith("/deactivate",StringComparison.Ordinal)||path.EndsWith("/reactivate",StringComparison.Ordinal))return Has("accounting.chart.deactivate");
            return path=="/api/finance/coa"&&Has("accounting.chart.create");
        }
        if(path.StartsWith("/api/finance/opening/",StringComparison.Ordinal))return Has("treasury.opening");
        if(path.StartsWith("/api/finance/legacy-cash-allocation/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.opening");
        if(path.StartsWith("/api/finance/jahez-handovers/",StringComparison.Ordinal))return method=="POST"&&Has("treasury.jahez.receive")&&(path.EndsWith("/confirm",StringComparison.Ordinal)||path.EndsWith("/reject",StringComparison.Ordinal));
        if(path.StartsWith("/api/finance/",StringComparison.Ordinal))return method=="GET"?Has("treasury.view"):method=="POST"&&(path switch{
            "/api/finance/vault-transfer"=>Has("treasury.transfer"),
            "/api/finance/liability-payment"=>Has("treasury.liability.settle"),
            "/api/finance/jahez-transfer"=>Has("treasury.transfer"),
            "/api/finance/jahez-platform-settlement"=>Has("treasury.transfer"),
            "/api/finance/jahez-fee-deposit"=>Has("treasury.transfer"),
            "/api/finance/jahez-fee"=>Has("treasury.jahez.manage"),
            "/api/finance/jahez-handover"=>Has("treasury.jahez.handover"),
            "/api/finance/jahez-reconciliations"=>Has("treasury.transfer")||Has("treasury.jahez.receive"),
            _ when path.StartsWith("/api/finance/jahez-reconciliations/",StringComparison.Ordinal)=>Has("treasury.transfer")||Has("treasury.jahez.receive"),
            _=>false});
        if(path=="/api/org/supervision"&&method=="GET")return Has("advances.request")||Has("org.manage"); // mine=1 for supervisors; the full list is re-checked in the handler.
        if(path.StartsWith("/api/org/",StringComparison.Ordinal))return method=="GET"&&path=="/api/org/catalog"||Has("org.manage");
        if(path.StartsWith("/api/advance-requests",StringComparison.Ordinal))
        {
            if(method=="GET")return Has("advances.request")||Has("advances.review")||Has("advances.payout");
            if(method!="POST")return false;
            if(path=="/api/advance-requests"||path.EndsWith("/respond",StringComparison.Ordinal))return Has("advances.request");
            if(path.StartsWith("/api/advance-requests/rider-asks/",StringComparison.Ordinal))return Has("advances.request")&&(path.EndsWith("/convert",StringComparison.Ordinal)||path.EndsWith("/decline",StringComparison.Ordinal));
            if(path.EndsWith("/review",StringComparison.Ordinal))return Has("advances.review");
            return (path.EndsWith("/payout",StringComparison.Ordinal)||path.EndsWith("/request-payout",StringComparison.Ordinal))&&Has("advances.payout");
        }
        if(path.StartsWith("/api/approvals",StringComparison.Ordinal))return method=="GET"?Has("approvals.view"):method=="POST"&&path.EndsWith("/decide",StringComparison.Ordinal)&&Has("approvals.decide");
if(path.StartsWith("/api/admin/org/approval-limits",StringComparison.Ordinal)||path.StartsWith("/api/admin/org/sla-policies",StringComparison.Ordinal))
            return (method=="GET"||method=="PUT")&&(Has("admin.org.manage")||Has("org.manage"));
        if(path=="/api/sal/simulations")
            return method=="POST"&&Has("salaries.simulation.run");
        if(path.StartsWith("/api/sal/simulations/",StringComparison.Ordinal))
            return path.EndsWith("/journal-preview",StringComparison.Ordinal)||path.EndsWith("/discard",StringComparison.Ordinal)
                ?(method=="GET"||method=="POST")&&Has("salaries.simulation.run")
                :(method=="GET"||method=="POST")&&Has("pay.manage");
        // U-W2-12/13 money capture + distribution/bearer/settlement writes.
        if(path.StartsWith("/api/sal/",StringComparison.Ordinal))
            return method=="POST"&&Has("pay.manage");
        if(path=="/api/audit")return method=="GET"&&(Has("audit.view")||Has("admin.audit.view"));
        // DELETE / campaign PUT stay admin-only (blanket deny). forms.delete is deprecated and unused.
        if(method=="DELETE"||path.EndsWith("/delete-preview",StringComparison.Ordinal)||path=="/api/submissions/bulk-delete"||method=="PUT"&&path.StartsWith("/api/campaigns/",StringComparison.Ordinal))return false;
        if(method=="POST"&&path.StartsWith("/api/notifications/center/",StringComparison.Ordinal)&&path.EndsWith("/toggle-todo",StringComparison.Ordinal))return true; // Handler checks view permission or ownership and visibility.
        if(path=="/api/notifications"||path=="/api/notifications/summary")return method=="GET";
        if(method=="POST"&&(path=="/api/notifications/read-all"||path.StartsWith("/api/notifications/",StringComparison.Ordinal)&&path.EndsWith("/read",StringComparison.Ordinal)))return true;
        if(path.StartsWith("/api/notifications",StringComparison.Ordinal))return method=="GET"?Has("approvals.view"):method=="POST"&&Has("mobile.broadcast");
        if(path=="/api/inquiry-access")return method=="POST"&&Has("inquiry.issue");
        if(path=="/api/rider-mobile/sessions"||path.StartsWith("/api/rider-mobile/sessions/",StringComparison.Ordinal))
            return Has("mobile.sessions")&&(method=="GET"&&path=="/api/rider-mobile/sessions"||method=="POST"&&(path=="/api/rider-mobile/sessions/revoke-all"||path.EndsWith("/revoke",StringComparison.Ordinal)));
        if(path.StartsWith("/api/rider-mobile/firebase-config",StringComparison.Ordinal))return false;
        if(path=="/api/rider-mobile/service-requests"||path.StartsWith("/api/rider-mobile/service-requests/",StringComparison.Ordinal))
            return method=="GET"
                ?(Has("mobile.broadcast")||Has("forms.view")||Has("forms.review")||Has("mobile.complaints.confidential")||Has("riders.iban_data.approve_first")||Has("riders.iban_data.approve_second"))
                :method=="POST"&&(Has("mobile.broadcast")||Has("forms.review")||Has("mobile.complaints.confidential")||Has("riders.iban_data.approve_first")||Has("riders.iban_data.approve_second"));
        if(path=="/api/rider-mobile/statements/notify-period")return method=="POST"&&Has("mobile.broadcast");
        if(path.StartsWith("/api/rider-mobile/",StringComparison.Ordinal))return method=="GET"?Has("mobile.broadcast"):method=="POST"&&path=="/api/rider-mobile/broadcast"&&Has("mobile.broadcast");
        if(path.StartsWith("/api/promissory-notes",StringComparison.Ordinal))return method=="GET"&&Has("debts.view");
        if(path.StartsWith("/api/payroll",StringComparison.Ordinal))return method=="GET"?Has("payroll.view"):method=="POST"&&path=="/api/payroll/approve"&&Has("payroll.approve");
        if(path.StartsWith("/api/ledger",StringComparison.Ordinal))return method=="GET"&&Has("payroll.view");
        if(path=="/api/payments")return method=="POST"&&Has("payments.record");
        if(path=="/api/adjustments")return method=="POST"&&Has("adjustments.create");
        if(path.StartsWith("/api/policies",StringComparison.Ordinal))return method=="GET"?Has("policies.view"):Has("policies.manage");
        if((method=="GET"&&path=="/api/imports")||path=="/api/template.csv")return Has("imports.view");
        // Per-page empty templates (imports centre catalog list and free-upload endpoints removed).
        if(path.StartsWith("/api/imports/templates/",StringComparison.Ordinal))return method=="GET"&&Has("imports.view");
        if(path.StartsWith("/api/campaigns",StringComparison.Ordinal))return method=="GET"?Has("forms.view"):method=="DELETE"?Has("forms.delete"):Has("forms.manage");
        if(path.StartsWith("/api/submissions",StringComparison.Ordinal))return method=="GET"?Has("forms.view"):method=="POST"&&(path.EndsWith("/review",StringComparison.Ordinal)||path=="/api/submissions/bulk-approve")&&Has("forms.review");
        if(Get("/api/forms/catalog")||Get("/api/forms/analytics")||Get("/api/forms/responses"))return Has("forms.view");
// GET builder list is forms.view; create/publish/close stay forms.manage.
        if(path.StartsWith("/api/forms/builder",StringComparison.Ordinal))
            return method=="GET"?Has("forms.view"):Has("forms.manage");
        if(path=="/api/admin/audit/self-approvals")return method=="GET"&&Has("admin.audit.view");
        if(path=="/api/admin/read-summary")return method=="GET"&&Has("admin.read_summary.view");
        if(path=="/api/admin/settings")return (method=="GET"||method=="PUT")&&Has("admin.settings.manage");
        if(path=="/api/admin/notify-settings")return (method=="GET"||method=="PUT")&&Has("admin.notify.manage");
        if(path=="/api/notify/send")return method=="POST"&&(Has("notify.send.general")||Has("notify.send.custom"));
        if(path=="/api/tasks"||path.StartsWith("/api/tasks/",StringComparison.Ordinal))
            return method=="GET"?(Has("tasks.assign")||Has("tasks.manage")):(method is "POST" or "PUT")&&(Has("tasks.assign")||Has("tasks.manage"));
        if(path.StartsWith("/api/users/",StringComparison.Ordinal)&&path.EndsWith("/effective-permissions",StringComparison.Ordinal))return method=="GET"&&Has("users.manage");
        if(path.StartsWith("/api/users/",StringComparison.Ordinal)&&path.EndsWith("/copy-permissions",StringComparison.Ordinal))return method=="POST"&&Has("users.manage");
        return false;
    }
    /// <summary>BR §35 / M-0672: permission-only in general; two-person only when the server-derived dual code
    /// (bank file, payment out, shared-rider release) applies. Client dualPermission is ignored when kind maps.</summary>
    public static void ValidateApproval(ClaimsPrincipal actor,JsonObject request,JsonObject input,string permission="approvals.decide",System.Data.Common.DbConnection? c=null,System.Data.Common.DbTransaction? tx=null,string operation="approval.decide",JsonObject? record=null,string? dualPermission=null)
    {
        Demand(actor,permission);
        string? dual=dualPermission??ApprovalsArea.ResolveDual(request);
        if(ApprovalsArea.RequiresDual(dual))Demand(actor,dual!);
        string maker=request.S("requestedBy",request.S("createdBy"));
        // BR §35 / M-0672: maker≠checker only for dedicated dual money-out / shared-rider codes.
        // M-0667: SystemAdmin self-approval is allowed and must still be audited via Independent.
        if(ApprovalsArea.RequiresDual(dual))
            J.Need(Independent(actor,maker,operation,c,tx,record??request,scope:dual),"لا يجوز اعتماد طلبك بنفسك؛ يلزم مراجع مستقل أو صلاحية تجاوز مؤرخة.",403);
        else if(SamePerson(actor,maker)&&actor.IsInRole("SystemAdmin"))
            Independent(actor,maker,operation,c,tx,record??request);
        // Bypass lifts maker≠checker only — never department, rank, approval limit, or company/platform scope.
        if(!Administrator(actor)&&(actor.FindFirstValue("department")!=request.S("targetDepartment")||!int.TryParse(actor.FindFirstValue("rankLevel"),out int rank)||rank<(int)request.N("requiredRankLevel",1)))
            throw new ApiError("القسم أو الرتبة لا يسمحان بالبت في الطلب.",403);
        DemandApprovalLimit(actor,request,c,tx);
    }

    public static bool WithinApprovalLimit(ClaimsPrincipal actor,JsonObject request,System.Data.Common.DbConnection? c=null,System.Data.Common.DbTransaction? tx=null)
    {
        if(Administrator(actor)||c==null)return true;
        decimal amount=request.N("amountSar",request.N("amount",request.N("total")));
        if(amount<=0)return true;
        string rank=actor.FindFirstValue("rank")??RankCode(actor.FindFirstValue("rankLevel"));
        string department=actor.FindFirstValue("department")??request.S("targetDepartment");
        ApprovalsArea.EnsureLimitsSeeded(c,tx);
        var row=SchemaApprovals.FindLimit(c,tx,rank,department)
            ??Store.List(c,ApprovalsArea.LimitsKind,tx).FirstOrDefault(l=>l.S("rank")==rank&&l.S("department")==department);
        if(row==null||row.B("unlimited")||row["limitSar"] is null)return true;
        return amount<=row.N("limitSar");
    }

    public static void DemandApprovalLimit(ClaimsPrincipal actor,JsonObject request,System.Data.Common.DbConnection? c=null,System.Data.Common.DbTransaction? tx=null)
    {
        J.Need(WithinApprovalLimit(actor,request,c,tx),"المبلغ يتجاوز حد الاعتماد للرتبة والقسم.",403);
    }

    static string RankCode(string? level)=>level switch{"5"=>"GM","4"=>"A+","3"=>"A","2"=>"B",_=>"C"};
}
