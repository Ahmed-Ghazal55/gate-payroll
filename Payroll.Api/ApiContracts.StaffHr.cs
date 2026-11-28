using Microsoft.OpenApi;
namespace Payroll;

public static partial class ApiContracts
{
    static OpenApiSchema StaffEmployee() => O(new()
    {
        ["id"] = S(), ["displayName"] = S(), ["iqama"] = S(), ["companyId"] = S(),
        ["department"] = S(), ["jobTitle"] = S(), ["city"] = S(), ["nationality"] = S(),
        ["employmentType"] = S(), ["erpEmployeeId"] = S(), ["erpUserEmployeeId"] = S(),
        ["linkedUserId"] = S(), ["status"] = S(), ["basicSalary"] = N(),
        ["contractEndDate"] = S(), ["contractIssueDate"] = S(), ["currentContractId"] = S(),
        ["notes"] = S(), ["allowedActions"] = A(S()),
    }, "id", "displayName", "iqama", "companyId", "status", "allowedActions");

    static OpenApiSchema StaffContract() => O(new()
    {
        ["id"] = S(), ["employeeId"] = S(), ["companyId"] = S(),
        ["issueDate"] = S(), ["endDate"] = S(), ["basicSalary"] = N(),
        ["attachmentId"] = S(), ["fileHash"] = S(), ["allowedActions"] = A(S()),
    }, "id", "employeeId", "issueDate", "endDate", "basicSalary", "allowedActions");

    static OpenApiSchema StaffMonth() => O(new()
    {
        ["id"] = S(), ["employeeId"] = S(), ["companyId"] = S(), ["period"] = S(),
        ["attendanceDays"] = N(), ["overtimeHours"] = N(), ["latenessHours"] = N(),
        ["bonusAmount"] = N(), ["deductionAmount"] = N(), ["notes"] = S(),
        ["allowedActions"] = A(S()),
    }, "id", "employeeId", "period", "allowedActions");

    static OpenApiSchema StaffAdvance() => O(new()
    {
        ["id"] = S(), ["sourceRef"] = S(), ["remainingBalance"] = N(),
        ["monthlyInstallment"] = N(), ["journalEntryId"] = S(), ["status"] = S(),
        ["allowedActions"] = A(S()),
    }, "id", "sourceRef", "status", "allowedActions");

    static OpenApiSchema StaffRun() => O(new()
    {
        ["id"] = S(), ["period"] = S(), ["companyId"] = S(), ["status"] = S(),
        ["lineCount"] = N(), ["blockedCount"] = N(), ["grossTotal"] = N(), ["netTotal"] = N(),
        ["lines"] = A(Free()), ["blocked"] = A(Free()), ["createdBy"] = S(), ["preparedBy"] = S(),
        ["approvedBy"] = S(), ["journalEntryId"] = S(), ["mudadFileId"] = S(),
        ["postRefused"] = B(), ["postRefuseReason"] = S(), ["postRefusedAt"] = S(),
        ["mudadVoided"] = B(), ["reversalJournalId"] = S(), ["allowedActions"] = A(S()),
    }, "id", "period", "companyId", "status", "allowedActions");

    static OpenApiSchema StaffDashboard() => O(new()
    {
        ["contractAlerts"] = A(Free()), ["recentRuns"] = A(Free()),
        ["employeeCount"] = N(), ["allowedActions"] = A(S()),
    }, "contractAlerts", "recentRuns", "employeeCount", "allowedActions");

    static OpenApiSchema StaffCatalogs() => O(new()
    {
        ["employmentTypes"] = A(S()), ["departments"] = A(S()), ["cities"] = A(S()),
        ["nationalities"] = A(S()), ["jobTitles"] = A(S()),
    }, "employmentTypes", "departments", "cities", "nationalities", "jobTitles");

    static OpenApiSchema? StaffHrResponse(string method, string path)
    {
        bool get = method == "GET";
        if (path == "/api/staff/hr/catalogs" && get) return StaffCatalogs();
        if (path == "/api/staff/hr/dashboard" && get) return StaffDashboard();
        if (path == "/api/staff/hr/employees") return get ? A(StaffEmployee()) : StaffEmployee();
        if (path == "/api/staff/hr/employees/{id}") return StaffEmployee();
        if (path == "/api/staff/hr/employees/{id}/contracts") return StaffContract();
        if (path == "/api/staff/hr/employees/{id}/months") return StaffMonth();
        if (path == "/api/staff/hr/employees/{id}/advances") return StaffAdvance();
        if (path == "/api/staff/hr/employees/{id}/advances/{debtId}/approve") return StaffAdvance();
        if (path == "/api/staff/hr/runs") return get ? A(StaffRun()) : StaffRun();
        if (path.StartsWith("/api/staff/hr/runs/", StringComparison.Ordinal)
            && !path.EndsWith("/mudad", StringComparison.Ordinal))
            return StaffRun();
        return null;
    }
}
