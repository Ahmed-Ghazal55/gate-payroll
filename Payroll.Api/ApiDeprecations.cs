namespace Payroll;

// Compatibility routes remain registered; deprecation changes documentation only.
public static class ApiDeprecations
{
    public static bool IsDeprecated(string method,string path)=>method switch
    {
        "GET" => path is "/api/template.csv" or "/api/payroll/fingerprint" or "/api/imports" or "/api/debts/settlements" or "/api/system/release-check",
        "POST" => path=="/api/public/forms/{token}/resume",
        _ => false
    };
}
