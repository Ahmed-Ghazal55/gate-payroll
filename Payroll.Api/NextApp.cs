using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
namespace Payroll;

public static class NextApp
{
    public static IResult Fallback(string? path, string webRoot)
    {
        if (path != null && (path.Contains("..") || path.Contains('\\'))) return Results.NotFound();
        string clean = (path ?? "").Trim().Trim('/');
        if (!string.IsNullOrEmpty(clean) && Path.HasExtension(clean)) return Results.NotFound();

        string folderIndex = Path.Combine(webRoot, "app", clean, "index.html");
        if (File.Exists(folderIndex)) return Results.File(folderIndex, "text/html; charset=utf-8");

        if (!string.IsNullOrEmpty(clean))
        {
            string htmlFile = Path.Combine(webRoot, "app", clean + ".html");
            if (File.Exists(htmlFile)) return Results.File(htmlFile, "text/html; charset=utf-8");
        }

        string notFound = Path.Combine(webRoot, "app", "404.html");
        if (File.Exists(notFound)) return Results.Content(File.ReadAllText(notFound), "text/html; charset=utf-8", statusCode: 404);

        string index = Path.Combine(webRoot, "app", "index.html");
        return File.Exists(index) ? Results.File(index, "text/html; charset=utf-8") : Results.NotFound();
    }
    public const string BasePolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    static readonly Regex InlineScript = new(@"<script\b(?![^>]*\bsrc\s*=)[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly ConcurrentDictionary<string, (DateTime Stamp, string Policy)> Policies = new();

    // Next.js static export boots React with inline scripts. Allow exactly those scripts by hash; never 'unsafe-inline'.
    public static string PolicyFor(string html)
    {
        var hashes = InlineScript.Matches(html).Select(m => m.Groups[1].Value).Where(v => v.Length > 0)
            .Select(v => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(v))) + "'").Distinct().ToList();
        return hashes.Count == 0 ? BasePolicy : BasePolicy.Replace("script-src 'self'", "script-src 'self' " + string.Join(' ', hashes));
    }
    public static string PolicyForFile(string file)
    {
        var stamp = File.GetLastWriteTimeUtc(file);
        if (Policies.TryGetValue(file, out var cached) && cached.Stamp == stamp) return cached.Policy;
        string policy = PolicyFor(File.ReadAllText(file)); Policies[file] = (stamp, policy); return policy;
    }
    static async ValueTask<object?> HashedScripts(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        string? policy = result switch
        {
            PhysicalFileHttpResult f when f.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) => PolicyForFile(f.FileName),
            ContentHttpResult c when c.ContentType?.StartsWith("text/html") == true => PolicyFor(c.ResponseContent ?? ""),
            _ => null
        };
        if (policy != null) context.HttpContext.Response.Headers["Content-Security-Policy"] = policy;
        return result;
    }
    public static void Map(WebApplication app)
    {
        app.MapMethods("/app", ["GET", "HEAD"], () => Fallback(null, app.Environment.WebRootPath)).AddEndpointFilter(HashedScripts).WithTags("Administration").WithSummary("Serve the optional Next application entry point");
        app.MapMethods("/app/{**path}", ["GET", "HEAD"], (string? path) => Fallback(path, app.Environment.WebRootPath)).AddEndpointFilter(HashedScripts).WithTags("Administration").WithSummary("Serve extensionless Next client routes; unknown assets remain 404");
    }
}
