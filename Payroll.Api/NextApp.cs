namespace Payroll;

public static class NextApp
{
    public static IResult Fallback(string? path,string webRoot)
    {
        if(Path.HasExtension(path??""))return Results.NotFound();
        string index=Path.Combine(webRoot,"app","index.html");
        return File.Exists(index)?Results.File(index,"text/html; charset=utf-8"):Results.NotFound();
    }
    public static void Map(WebApplication app)
    {
        app.MapGet("/app",()=>Fallback(null,app.Environment.WebRootPath)).WithTags("Administration").WithSummary("Serve the optional Next application entry point");
        app.MapGet("/app/{**path}",(string? path)=>Fallback(path,app.Environment.WebRootPath)).WithTags("Administration").WithSummary("Serve extensionless Next client routes; unknown assets remain 404");
    }
}
