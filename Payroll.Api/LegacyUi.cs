namespace Payroll;

public static class LegacyUi
{
    public static bool SafeReturnUrl(string? value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>2048)return false;
        string decoded=value;
        try { for(int i=0;i<4&&decoded.Contains('%');i++)decoded=Uri.UnescapeDataString(decoded); }
        catch(UriFormatException){return false;}
        if(decoded.Contains('%')||decoded.Contains('\\')||decoded.Any(char.IsControl))return false;
        string path=decoded.Split('?', '#')[0];
        return (path.StartsWith("/app/",StringComparison.Ordinal)||path.StartsWith("/legacy/",StringComparison.Ordinal))
            &&!path.Split('/').Any(part=>part is "." or "..");
    }

    // Run after authentication and before static files, including direct index.html requests.
    public static void Protect(WebApplication app)=>app.Use(async (ctx,next)=>
    {
        if(ctx.Request.Path=="/index.html"){ctx.Response.Redirect("/legacy/");return;}
        if(ctx.Request.Path.StartsWithSegments("/legacy")&&ctx.User.Identity?.IsAuthenticated!=true)
        {ctx.Response.Redirect("/app/login/?returnUrl=/legacy/");return;}
        if(ctx.Request.Path.StartsWithSegments("/app/login")&&ctx.Request.Query.TryGetValue("returnUrl",out var returnUrl)
            &&(returnUrl.Count!=1||!SafeReturnUrl(returnUrl[0])))
        {ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(ApiErrors.Body(400,"مسار العودة غير مسموح."));return;}
        await next();
    });

    public static void Map(WebApplication app)
    {
        app.MapGet("/",()=>Results.Redirect("/app/"))
            .WithTags("Administration").WithSummary("Redirect to the current administration app");
        app.MapGet("/legacy",(HttpContext ctx)=>ctx.Request.Path.Value!.EndsWith('/')
            ?Results.File(Path.Combine(app.Environment.WebRootPath,"index.html"),"text/html; charset=utf-8")
            :Results.Redirect("/legacy/"))
            .WithTags("Administration").WithSummary("Serve legacy administration to a signed-in session");
    }
}
