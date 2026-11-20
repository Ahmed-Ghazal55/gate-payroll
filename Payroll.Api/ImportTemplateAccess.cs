using System.Security.Claims;
using System.Text.Json.Nodes;

namespace Payroll;

/// <summary>Template GET permission ↔ <c>download_template</c> in page <c>allowedActions</c> (FU1-e).</summary>
public static class ImportTemplateAccess
{
    public const string DownloadAction = "download_template";

    public static void AddIfAllowed(JsonArray actions, ClaimsPrincipal user, string templatePath)
    {
        if (!AccessPolicy.Allowed(user, "GET", templatePath)) return;
        if (actions.Any(a => a?.ToString() == DownloadAction)) return;
        actions.Add(DownloadAction);
    }
}
