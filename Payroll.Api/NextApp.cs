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
    public static void Map(WebApplication app)
    {
        app.MapGet("/app", () => Fallback(null, app.Environment.WebRootPath)).WithTags("Administration").WithSummary("Serve the optional Next application entry point");
        app.MapGet("/app/{**path}", (string? path) => Fallback(path, app.Environment.WebRootPath)).WithTags("Administration").WithSummary("Serve extensionless Next client routes; unknown assets remain 404");
    }
}
