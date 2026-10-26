using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Payroll;

Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var dataDir = HostingSecurity.DataDirectory(Environment.GetEnvironmentVariable("PAYROLL_DATA_DIR") ?? builder.Configuration["Payroll:DataDir"] ?? "App_Data", builder.Environment.ContentRootPath, builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
Directory.CreateDirectory(dataDir);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 12 * 1024 * 1024);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var proxy in HostingSecurity.TrustedProxies(builder.Configuration["PAYROLL_KNOWN_PROXIES"] ?? "")) o.KnownProxies.Add(proxy);
});
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys"))).SetApplicationName("Gate.Payroll");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "Gate.Payroll.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnValidatePrincipal = UserAccess.ValidateCookie;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return c.Response.WriteAsJsonAsync(new { error = "سجل الدخول للإدارة." }); };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache(o => o.SizeLimit = 10000);
builder.Services.AddSingleton<InquiryLimits>();
builder.Services.AddRateLimiter(options => { options.RejectionStatusCode = 429; options.AddPolicy("sensitive", ctx => RateLimitPartition.GetFixedWindowLimiter((ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (ctx.Request.Path.StartsWithSegments("/api/auth") ? ":auth" : ":forms"), _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = 30, QueueLimit = 0 })); });
builder.Services.AddSingleton(new Store(dataDir, builder.Configuration.GetConnectionString("DefaultConnection")));
var app = builder.Build(); var store = app.Services.GetRequiredService<Store>();
CampaignLinks.Migrate(store);
app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff"; ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer"; ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    try
    {
        if (ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !HostingSecurity.SameOrigin(origin, ctx.Request.Scheme, ctx.Request.Host.Value)) throw new ApiError("طلب من مصدر غير مسموح.", 403);
            if (ctx.Request.Headers["Sec-Fetch-Site"] == "cross-site") throw new ApiError("طلب عبر موقع آخر غير مسموح.", 403);
        }
        if (!app.Environment.IsDevelopment() && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.IsHttps)
            throw new ApiError("استخدم اتصال HTTPS آمنًا.", 403);
        await next();
    }
    catch (Exception e) when (e is ApiError or JsonException or FormatException or BadHttpRequestException)
    { ctx.Response.StatusCode = e is ApiError ae ? ae.Status : 400; await ctx.Response.WriteAsJsonAsync(new { error = e is ApiError ? e.Message : "بيانات الطلب غير صحيحة." }); }
    catch (Exception e)
    { app.Logger.LogError(e, "Payroll request failed: {Path}", ctx.Request.Path); ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { error = "تعذر إتمام العملية. راجع سجل الخادم دون إعادة الاعتماد عشوائيًا." }); }
});
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => { if (context.Context.Request.Path.StartsWithSegments("/fonts")) context.Context.Response.Headers.CacheControl="public, max-age=86400"; } }); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.MapGet("/health", () => new { ok = true, application = "Gate Payroll", version = ReleaseSafety.Version, dataPreservationVerified=store.DataPreservationVerified, releaseCheckpointPresent=store.ReleaseCheckpointPresent });
app.MapGet("/api/auth/status", () => new { needsSetup = store.Read(c => Store.List(c, "user").Count == 0), environment = app.Environment.EnvironmentName });
app.MapPost("/api/auth/setup", async (JsonObject input, HttpContext ctx) =>
{
    bool local = app.Environment.IsDevelopment() && ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    string bootstrap = Environment.GetEnvironmentVariable("PAYROLL_BOOTSTRAP_KEY") ?? builder.Configuration["Payroll:BootstrapKey"] ?? "";
    if (!local && !HostingSecurity.StrongBootstrapKey(bootstrap)) throw new ApiError("تهيئة الإنتاج غير متاحة؛ اضبط مفتاح تهيئة سريًا في الخادم.", 503);
    J.Need(HostingSecurity.BootstrapAllowed(bootstrap, input.S("bootstrapKey"), local), "مفتاح التهيئة غير صحيح.");
    var created = UserAccess.Create(input.Required("username"), input.Required("password"), "Admin", "administrator");
    store.Write((c, tx) => { J.Need(Store.List(c, "user", tx).Count == 0, "تم إعداد المسؤول بالفعل."); Store.Save(c, "user", created, tx); Store.Audit(c, tx, "admin.setup", "administrator", created.S("username")); return true; });
    await SignIn(ctx, created); return Results.Ok(UserAccess.Public(created));
}).RequireRateLimiting("sensitive");
app.MapPost("/api/auth/login", async (JsonObject input, HttpContext ctx) =>
{
    var user = store.Read(c => UserAccess.Find(c, input.S("username")));
    if (!UserAccess.Password(user, input.S("password"))) return Results.Json(new { error = "اسم المستخدم أو كلمة المرور غير صحيحة." }, statusCode: 401);
    var loggedIn = store.Write((c,tx) => {
        var current = Store.Get(c, user!.S("id"), "user", tx)!;
        J.Need(UserAccess.Status(current)=="active", "الحساب غير متاح.");
        current["lastLoginAt"]=Now(); Store.Save(c,"user",current,tx); Store.Audit(c,tx,"admin.login",current.S("id"),current.S("username"));return current;
    });
    await SignIn(ctx, loggedIn); return Results.Ok(UserAccess.Public(loggedIn));
}).RequireRateLimiting("sensitive");
app.MapPost("/api/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });
var api = app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async (context, next) => {
    var ctx=context.HttpContext;
    if (!AccessPolicy.Allowed(ctx.User,ctx.Request.Method,ctx.Request.Path.Value??"",context.Arguments.OfType<JsonObject>().FirstOrDefault()))
        return Results.Json(new { error="ليست لديك صلاحية لهذا الإجراء." }, statusCode:403);
    return await next(context);
});
TableExcelApi.Map(api, store);
UserAccess.Map(api, store);
RiderDirectory.Map(api, store);
Debts.Map(api, store);
FinancialCore.Map(app, api, store); ReleaseSafety.Map(api,store);
TreasuryEntries.Map(api,store);
JahezTreasury.Map(api,store);
FormBuilderApi.Map(app, api, store);
InquiryApi.Map(app, api, store);
api.MapGet("/me", (HttpContext ctx) =>
{
    var u = store.Read(c => UserAccess.Find(c, ctx.User.Identity?.Name ?? ""));
    if (u != null) return Results.Ok(UserAccess.Public(u));
    return Results.Ok(new { username = ctx.User.Identity!.Name, role = ctx.User.FindFirstValue(ClaimTypes.Role), permissions=ctx.User.FindAll("permission").Select(p=>p.Value),department=ctx.User.FindFirstValue("department"),rankLevel=ctx.User.FindFirstValue("rankLevel"), id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) });
});
api.MapGet("/dashboard", (HttpContext ctx) => store.Read(c => new { riders = Store.List(c, "rider").Count, imports = Store.List(c, "batch").Count, pendingResponses = Store.List(c, "submission").Count(x => x.S("status") == "pending"), approvedRuns = Store.List(c, "run").Count, balance = Store.List(c, "ledger").Where(l=>FinancialScopes.Ledger(ctx.User,l)).Sum(x => x.N("amount")), latestAudit = Store.List(c, "audit").Where(_=>AccessPolicy.Administrator(ctx.User)).Take(8) }));
api.MapGet("/riders", (HttpContext ctx) => store.Read(c => Store.List(c, "rider").Select(r => !AccessPolicy.Has(ctx.User,"riders.view_360") ? UserAccess.RiderView(r) : r).ToList()));
api.MapPost("/riders", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    var iqama = J.Iqama(input.Required("iqama")); var existing = Store.Get(c, iqama, "rider", tx);
    foreach (var key in new[] { "iban", "policyId", "financeSponsorId" })
        if (input.ContainsKey(key) && input.S(key) != (existing?.S(key) ?? "")) AccessPolicy.Demand(ctx.User, "riders.financial_edit");
    if(existing!=null)FinancialScopes.DemandRider(ctx.User,existing);
    var rider = RiderDirectory.Normalize(input, existing);
    if(rider.S("financeSponsorId")!=""||rider.S("sponsorId")!="")FinancialScopes.DemandRider(ctx.User,rider);
    if (input.ContainsKey("policyId")) { if (input.S("policyId") != "") J.Need(Store.Get(c, input.S("policyId"), "policy", tx)?.S("status") == "approved", "اختر سياسة معتمدة."); rider["policyId"] = input.S("policyId"); }
    else if (!rider.ContainsKey("policyId")) rider["policyId"]="";
    Store.Save(c, "rider", rider, tx); Store.Audit(c, tx, "rider.save", iqama, Actor(ctx)); return rider;
}));
api.MapGet("/policies", () => store.Read(c => Store.List(c, "policy")));
api.MapPost("/policies", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    var kind = input.Required("kind"); J.Need(new[] { "commission", "tiers", "approved_pay" }.Contains(kind), "نوع سياسة غير صالح.");
    var config = input["config"] as JsonObject ?? new(); ValidatePolicy(kind, config);
    var doc = new JsonObject { ["id"] = Id(), ["name"] = input.Required("name"), ["kind"] = kind, ["platform"] = input.Required("platform"), ["effectiveFrom"] = J.Period(input.Required("effectiveFrom")), ["status"] = "draft", ["config"] = config.DeepClone(), ["createdAt"] = Now() };
    Store.Save(c, "policy", doc, tx); Store.Audit(c, tx, "policy.create", doc.S("id"), Actor(ctx)); return doc;
}));
api.MapPost("/policies/{id}/approve", (string id, HttpContext ctx) => store.Write((c, tx) => { var p = Store.Get(c, id, "policy", tx) ?? throw new ApiError("السياسة غير موجودة.", 404); J.Need(p.S("status") == "draft", "هذه النسخة معتمدة بالفعل."); ValidatePolicy(p.S("kind"), p["config"]!.AsObject()); p["status"] = "approved"; p["approvedAt"] = Now(); Store.Save(c, "policy", p, tx); Store.Audit(c, tx, "policy.approve", id, Actor(ctx)); return p; }));
api.MapPost("/imports/inspect", (JsonObject input, HttpContext ctx) =>
{
    var filename = Path.GetFileName(input.Required("filename")); byte[] bytes = Convert.FromBase64String(input.Required("dataBase64")); var sheets = Imports.Read(bytes, filename);
    string id = Id(); string file = Path.Combine(dataDir, "imports", id + Path.GetExtension(filename)); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes);
    var doc = new JsonObject { ["id"] = id, ["filename"] = filename, ["sha256"] = Hash(bytes), ["path"] = file, ["createdAt"] = Now(), ["status"] = "preview" };
    store.Write((c, tx) => { Store.Save(c, "upload", doc, tx); Store.Audit(c, tx, "import.inspect", id, Actor(ctx)); return true; });
    return new { id, filename, sheets = sheets.Select(s => new { name = s.Name, rows = s.Rows.Count, columns = s.Rows.Select(r => r.Count).DefaultIfEmpty().Max() }) };
});
api.MapGet("/imports/{id}/sheet", (string id, string sheet, int? headerRow) =>
{
    var selected = LoadSheet(id, sheet); int header = headerRow ?? 1; J.Need(header >= 1 && header <= selected.Rows.Count, "صف عنوان غير صالح.");
    return new { headers = selected.Rows[header - 1].Select((text, index) => new { index, text }), sample = selected.Rows.Skip(header).Take(5), rowCount = selected.Rows.Count };
});
api.MapPost("/imports/{id}/preview", (string id, JsonObject spec) => Imports.Normalize(LoadSheet(id, spec.Required("sheet")), spec));
api.MapPost("/imports/{id}/commit", (string id, JsonObject spec, HttpContext ctx) =>
{
    var preview = Imports.Normalize(LoadSheet(id, spec.Required("sheet")), spec); J.Need(preview.Errors.Count == 0 && preview.Rows.Count > 0, "عالج أخطاء المعاينة قبل الحفظ.");
    return store.Write((c, tx) =>
    {
        var existingRuns = Store.List(c, "run", tx); var existingRows = Store.List(c, "work", tx).Select(x => x.S("id")).ToHashSet();
        var docs = preview.Rows.Select(r => { var d = JsonSerializer.SerializeToNode(r, JsonSerializerOptions.Web)!.AsObject(); d["id"] = Hash(Encoding.UTF8.GetBytes($"{r.Period}|{r.Platform}|{r.SourceRef}|{r.Iqama}|{r.AccountId}|{r.From}|{r.To}")); d["uploadId"] = id; return d; }).ToList();
        J.Need(docs.All(d => !existingRows.Contains(d.S("id"))), "توجد سجلات مستوردة بالفعل. رُفضت الدفعة بالكامل لمنع مضاعفة الرواتب.");
        J.Need(docs.All(d => !existingRuns.Any(r => r.S("iqama") == d.S("iqama") && r.S("period") == d.S("period"))), "فترة أحد المناديب معتمدة ومقفلة؛ لا يمكن تغيير مدخلاتها.");
        var upload = Store.Get(c, id, "upload", tx)!;
        foreach (var doc in docs)
        {
            Store.Save(c, "work", doc, tx);
            if (Store.Get(c, doc.S("iqama"), "rider", tx) == null) Store.Save(c, "rider", new() { ["id"] = doc.S("iqama"), ["iqama"] = doc.S("iqama"), ["name"] = doc.S("name") == "" ? doc.S("iqama") : doc.S("name"), ["city"] = doc.S("city"), ["policyId"] = "", ["phone"] = "", ["status"] = "needs_review" }, tx);
        }
        var batch = new JsonObject { ["id"] = Id(), ["uploadId"] = id, ["filename"] = upload.S("filename"), ["sourceSha256"] = upload.S("sha256"), ["period"] = spec.S("period"), ["platform"] = spec.S("platform"), ["sheet"] = spec.S("sheet"), ["mapping"] = spec["mapping"]?.DeepClone(), ["count"] = docs.Count, ["createdAt"] = Now() };
        Store.Save(c, "batch", batch, tx); Store.Audit(c, tx, "import.commit", batch.S("id"), Actor(ctx)); return new { count = docs.Count, batchId = batch.S("id") };
    });
});
api.MapGet("/imports", () => store.Read(c => Store.List(c, "batch")));
api.MapGet("/template.csv", () => Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("sourceRef,iqama,name,accountId,from,to,orders,platformRevenue,riderPay,isValid,company,city\r\n")).ToArray(), "text/csv; charset=utf-8", "payroll-template.csv"));
api.MapPost("/adjustments", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    string iqama = J.Iqama(input.Required("iqama")); string period = J.Period(input.Required("period"));
    J.Need(Store.Get(c, iqama, "rider", tx) != null, "المندوب غير موجود."); J.Need(!Store.List(c, "run", tx).Any(r => r.S("iqama") == iqama && r.S("period") == period), "الفترة مقفلة.");
    J.Need(!new[]{"already_paid","debt_installment","platform_settlement","company_fee","base_salary","approved_pay","extra_orders","fuel_allowance","internet_allowance"}.Contains(input.S("category")),"هذه الفئة محجوزة لمحرك المسير ولا تُدخل كتعديل يدوي.");
    var adjustmentRider=Store.Get(c,iqama,"rider",tx)!;FinancialScopes.DemandRider(ctx.User,adjustmentRider);
    decimal amount = input.N("amount"); J.Need(amount != 0 && Math.Abs(amount) < 100000000 && amount == J.Money(amount), "قيمة التحمل/الإضافة غير صحيحة. السالب خصم والموجب إضافة.");
    var doc = new JsonObject { ["id"] = Id(), ["iqama"] = iqama, ["period"] = period, ["amount"] = amount, ["category"] = input.Required("category"), ["description"] = input.Required("description"), ["sourceRef"] = input.Required("sourceRef"), ["createdAt"] = Now() };
    J.Need(!Store.List(c, "adjustment", tx).Any(a => a.S("sourceRef") == doc.S("sourceRef") && a.S("iqama") == iqama && a.S("period") == period), "مرجع هذا البند مسجل من قبل.");
    Store.Save(c, "adjustment", doc, tx); Store.Audit(c, tx, "adjustment.add", doc.S("id"), Actor(ctx)); return doc;
}));
api.MapGet("/payroll/preview", (string period,HttpContext ctx) => { J.Period(period); return store.Read(c => Store.List(c, "work").Where(w => w.S("period") == period).Select(w => w.S("iqama")).Distinct().Where(iq=>Store.Get(c,iq,"rider") is {} rider&&(FinancialScopes.Rider(ctx.User,rider)||FinancialScopes.AllCompanies(ctx.User))).Select(iq => { var doc = JsonSerializer.SerializeToNode(Preview(c, iq, period), JsonSerializerOptions.Web)!.AsObject(); doc["fingerprint"] = Fingerprint(c, iq, period); return doc; }).ToList()); });
api.MapGet("/payroll/runs", (HttpContext ctx) => store.Read(c => Store.List(c, "run").Where(r=>FinancialScopes.Ledger(ctx.User,r))));
api.MapPost("/payroll/approve", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    string iqama = J.Iqama(input.Required("iqama")); string period = J.Period(input.Required("period"));
    J.Need(!Store.List(c, "run", tx).Any(r => r.S("iqama") == iqama && r.S("period") == period), "المسير معتمد بالفعل؛ لن يُرحل مرتين.");
    J.Need(!Store.List(c, "ledger", tx).Any(r => r.S("iqama") == iqama && string.CompareOrdinal(r.S("period"), period) > 0), "توجد حركات لاحقة؛ لا يمكن إدخال اعتماد بأثر رجعي يغير الأرصدة المقفلة.");
    var result = Preview(c, iqama, period, tx); J.Need(result.Blockers.Count == 0, string.Join(" | ", result.Blockers));
    var rider = Store.Get(c, iqama, "rider", tx)!; var work = Store.List(c, "work", tx).Where(x => x.S("iqama") == iqama && x.S("period") == period).Select(x => x.Clone()).ToList();
    string fingerprint = Fingerprint(c, iqama, period, tx); J.Need(input.S("fingerprint") == fingerprint, "تغيرت البيانات منذ المعاينة؛ أعد المعاينة قبل الاعتماد.");
    var doc = new JsonObject { ["id"] = Id(), ["iqama"] = iqama, ["name"] = rider.S("name"), ["period"] = period, ["approvedAt"] = Now(), ["approvedBy"] = Actor(ctx), ["fingerprint"] = fingerprint, ["result"] = JsonSerializer.SerializeToNode(result, JsonSerializerOptions.Web), ["sourceRows"] = JsonSerializer.SerializeToNode(work), ["policySnapshot"] = Store.Get(c, rider.S("policyId"), "policy", tx)?.DeepClone() };
    var payrollSponsor=AccountingEvents.PayrollSponsor(rider,input);
    FinancialScopes.DemandSponsor(ctx.User,payrollSponsor.Id);
    foreach(var deduction in result.Lines.Where(l=>l.Category=="debt_installment"))FinancialScopes.DemandDebt(ctx.User,Store.Get(c,deduction.Source,"debt",tx)!);
    AccountingEvents.PostPayroll(c,tx,result,doc,payrollSponsor.Id,Actor(ctx));
    Store.Save(c, "run", doc, tx); Store.Save(c, "ledger", new() { ["id"] = "run:" + doc.S("id"), ["iqama"] = iqama, ["period"] = period, ["sponsorId"]=payrollSponsor.Id,["accountingVersion"]=AccountingEvents.Version,["journalEntryId"]=doc.S("journalEntryId"),["amount"] = result.PeriodMovement - result.Lines.Where(x => x.Category is "debt_installment" or "already_paid").Sum(x => x.Amount), ["kind"] = "approved_payroll", ["sourceRef"] = doc.S("id"), ["createdAt"] = Now() }, tx); Debts.Post(c, tx, result, doc.S("id")); AccountingEvents.VerifyPayrollClosing(c,tx,result,payrollSponsor.Id); Store.Audit(c, tx, "payroll.approve", doc.S("id"), Actor(ctx)); return doc;
}));
api.MapGet("/payroll/fingerprint", (string iqama, string period,HttpContext ctx) => store.Read(c => {var rider=Store.Get(c,J.Iqama(iqama),"rider")??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(ctx.User,rider);return new { fingerprint = Fingerprint(c,iqama,J.Period(period)) };}));
api.MapGet("/ledger", (HttpContext ctx) => store.Read(c => Store.List(c,"ledger").Where(l=>FinancialScopes.Ledger(ctx.User,l))));
api.MapGet("/ledger/{iqama}", (string iqama,HttpContext ctx) => store.Read(c => { J.Iqama(iqama); var rows = Store.List(c, "ledger").Where(x => x.S("iqama") == iqama&&FinancialScopes.Ledger(ctx.User,x)).OrderBy(x => x.S("period")).ThenBy(x => x.S("createdAt")).ToList(); return new { rows, balance = rows.Sum(x => x.N("amount")) }; }));
api.MapPost("/payments", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    string iqama = J.Iqama(input.Required("iqama")); string period = J.Period(input.Required("period")); var ledger = Store.List(c, "ledger", tx).Where(x => x.S("iqama") == iqama).ToList(); decimal amount = input.N("amount");
    J.Need(!ledger.Any(x => string.CompareOrdinal(x.S("period"), period) > 0), "لا يمكن تسجيل دفعة بأثر رجعي قبل حركات لاحقة.");
    J.Need(amount > 0 && amount == J.Money(amount) && amount <= ledger.Sum(x => x.N("amount")), "الدفعة يجب أن تكون موجبة ولا تتجاوز الرصيد المستحق.");
    string reference = input.Required("sourceRef"); J.Need(!Store.List(c, "ledger", tx).Any(x => x.S("kind") == "payment" && x.S("sourceRef") == reference), "مرجع التحويل مسجل بالفعل.");
    var rider=Store.Get(c,iqama,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);
    var sponsor=AccountingEvents.PayrollSponsor(rider,input);
    string vault=input.S("paymentVaultCode",FinancialCore.DefaultVaultForSponsor(sponsor.Id,"BANK"));
    FinancialScopes.DemandSponsor(ctx.User,sponsor.Id);FinancialScopes.DemandAccount(ctx.User,vault);
    var doc = new JsonObject { ["id"] = Id(), ["iqama"] = iqama, ["period"] = period, ["amount"] = -amount, ["kind"] = "payment", ["sourceRef"] = reference, ["createdAt"] = Now(),["sponsorId"]=sponsor.Id,["paymentVaultCode"]=vault,["accountingVersion"]=AccountingEvents.Version };
    var journal=AccountingEvents.PostPayment(c,tx,doc,sponsor.Id,vault,Actor(ctx));doc["journalEntryId"]=journal.S("id");
    Store.Save(c, "ledger", doc, tx); Store.Audit(c, tx, "payment.record", doc.S("id"), Actor(ctx)); return doc;
}));
api.MapGet("/payroll/export.csv", (string period,HttpContext ctx) => store.Read(c =>
{
    J.Period(period); var rows = Store.List(c, "run").Where(x => x.S("period") == period&&FinancialScopes.Ledger(ctx.User,x)).ToList();
    string CsvCell(string value) { if (value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@')) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
    var lines = new List<string> { "iqama,name,period,openingBalance,periodMovement,closingBalance,payable,debt" };
    lines.AddRange(rows.Select(r => string.Join(',', new[] { r.S("iqama"), r.S("name"), period, r["result"]!.S("openingBalance"), r["result"]!.S("periodMovement"), r["result"]!.S("closingBalance"), r["result"]!.S("payable"), r["result"]!.S("debt") }.Select(CsvCell))));
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray(), "text/csv; charset=utf-8", $"payroll-{period}.csv");
}));

// Public forms are collection links, never a lookup endpoint for existing personal records.
api.MapGet("/campaigns", (HttpContext ctx) => store.Read(c => Store.List(c, "campaign").Select(x => new { id=x.S("id"), title=x.S("title"), expiresAt=x.S("expiresAt"), status=x.S("status"), fields=CampaignOperations.Fields(x), publicUrl=CampaignLinks.Base(app.Configuration,ctx)+"/forms/"+x.S("sharingToken"), qrCodeUrl="/api/campaigns/"+x.S("id")+"/qr.svg" })));
api.MapGet("/campaigns/{id}/qr.svg", (string id, HttpContext ctx) => store.Read(c => { var d=Store.Get(c,id,"campaign")??throw new ApiError("الحملة غير موجودة.",404); return Results.Text(CampaignLinks.Svg(CampaignLinks.Base(app.Configuration,ctx)+"/forms/"+d.S("sharingToken")),"image/svg+xml"); }));
api.MapPost("/campaigns", (JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    int days = (int)Math.Clamp(input.N("days", 14), 1, 30); string token = Token(); var doc = new JsonObject { ["id"] = Id(), ["tokenHash"] = Hash(Encoding.UTF8.GetBytes(token)), ["sharingToken"] = token, ["title"] = input.Required("title"), ["status"] = "open", ["expiresAt"] = DateTimeOffset.UtcNow.AddDays(days).ToString("O"), ["createdAt"] = Now() };
    if(input.ContainsKey("fields"))doc["fields"]=CampaignOperations.ValidateFields(input["fields"]);
    Store.Save(c, "campaign", doc, tx); Store.Audit(c, tx, "form.publish", doc.S("id"), Actor(ctx)); return new { id=doc.S("id"), path="/forms/"+token, publicUrl=CampaignLinks.Base(app.Configuration,ctx)+"/forms/"+token, qrCodeUrl="/api/campaigns/"+doc.S("id")+"/qr.svg", expiresAt=doc.S("expiresAt") };
}));
api.MapPost("/campaigns/{id}/close", (string id, HttpContext ctx) => store.Write((c, tx) => { var d = Store.Get(c, id, "campaign", tx) ?? throw new ApiError("النموذج غير موجود.", 404); d["status"] = "closed"; Store.Save(c, "campaign", d, tx); Store.Audit(c, tx, "form.close", id, Actor(ctx)); return new { ok = true }; }));
api.MapGet("/submissions", () => store.Read(c => Store.List(c, "submission").Select(SubmissionChoices.PublicAdmin)));
api.MapPost("/submissions/{id}/review", (string id, JsonObject input, HttpContext ctx) => store.Write((c, tx) =>
{
    var d=Store.Get(c,id,"submission",tx)??throw new ApiError("الرد غير موجود.",404);
    CampaignOperations.Review(c,tx,d,input,Actor(ctx));return SubmissionChoices.PublicAdmin(d);
}));
api.MapGet("/audit", () => store.Read(c => Store.List(c, "audit")));
app.MapGet("/api/public/forms/{token}", (string token) => { var d = Campaign(token); return Results.Ok(new { title = d.S("title"), expiresAt = d.S("expiresAt"), schema = "work-choice-v1", fields=CampaignOperations.Fields(d) }); }).RequireRateLimiting("sensitive");
SubmissionChoices.Map(app, store, Campaign);
CampaignOperations.Map(api,store);
FormDeletion.Map(api,store);
app.MapGet("/forms/{token}", (string token) => { Campaign(token); return Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "rider-form.html"), "text/html; charset=utf-8"); });
app.MapGet("/rider-form.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "rider-form.js"), "text/javascript; charset=utf-8"));
app.MapGet("/", () => Results.File(Path.Combine(app.Environment.WebRootPath, "index.html"), "text/html; charset=utf-8"));
app.Run();

async Task SignIn(HttpContext ctx, JsonObject user) => await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserAccess.SessionPrincipal(user));
static string Id() => Guid.NewGuid().ToString("N");
static string Now() => DateTimeOffset.UtcNow.ToString("O");
static string Actor(HttpContext ctx) => ctx.User.Identity?.Name ?? "administrator";
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
Imports.Sheet LoadSheet(string id, string name)
{
    var doc = store.Read(c => Store.Get(c, id, "upload")) ?? throw new ApiError("الملف غير موجود.", 404);
    var bytes = File.ReadAllBytes(doc.S("path")); J.Need(Hash(bytes) == doc.S("sha256"), "تغير ملف المصدر بعد رفعه.");
    return Imports.Read(bytes, doc.S("filename")).FirstOrDefault(s => s.Name == name) ?? throw new ApiError("الورقة غير موجودة.");
}
JsonObject Campaign(string token)
{
    J.Need(token.Length == 64 && token.All(Uri.IsHexDigit), "رابط غير صالح.");
    var doc = store.Read(c => Store.List(c, "campaign").FirstOrDefault(d => d.S("tokenHash") == Hash(Encoding.UTF8.GetBytes(token)) || d.S("sharingToken") == token)) ?? throw new ApiError("الرابط غير صالح أو منتهي.", 404);
    J.Need(doc.S("status") == "open" && DateTimeOffset.Parse(doc.S("expiresAt"), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow, "النموذج مغلق أو انتهت صلاحيته."); return doc;
}
PayrollResult Preview(System.Data.Common.DbConnection c, string iqama, string period, System.Data.Common.DbTransaction? tx = null)
{
    var rider = Store.Get(c, iqama, "rider", tx); var policy = rider == null ? null : Store.Get(c, rider.S("policyId"), "policy", tx);
    var rows = Store.List(c, "work", tx).Where(w => w.S("iqama") == iqama && w.S("period") == period).Select(w => w.Deserialize<WorkLine>(JsonSerializerOptions.Web)!).ToList();
    var adjustments = Store.List(c, "adjustment", tx).Where(a => a.S("iqama") == iqama && a.S("period") == period).ToList();
    var opening = Store.List(c, "ledger", tx).Where(a => a.S("iqama") == iqama && string.CompareOrdinal(a.S("period"), period) < 0).Sum(a => a.N("amount"));
    return Engine.Calculate(iqama, period, policy, rows, adjustments, opening, Store.List(c,"debt",tx),Store.List(c,"ledger",tx).Where(l=>l.S("iqama")==iqama&&l.S("period")==period&&l.S("kind")=="payment").ToList());
}
string Fingerprint(System.Data.Common.DbConnection c, string iqama, string period, System.Data.Common.DbTransaction? tx = null)
{
    var rider = Store.Get(c, iqama, "rider", tx); var policy = rider == null ? null : Store.Get(c, rider.S("policyId"), "policy", tx);
    return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { rider, policy, debts = Store.List(c,"debt",tx).Where(x=>x.S("iqama")==iqama).OrderBy(x=>x.S("id")), result = Preview(c, iqama, period, tx), rows = Store.List(c, "work", tx).Where(x => x.S("iqama") == iqama && x.S("period") == period).OrderBy(x => x.S("id")) })));
}
static void ValidatePolicy(string kind, JsonObject config)
{
    void Number(JsonObject obj, string key, decimal maximum, bool required = true)
    {
        if (!required && obj[key] == null) return;
        J.Need(decimal.TryParse(obj.S(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= maximum && value == J.Money(value), $"قيمة السياسة غير صحيحة أو غير مكتملة: {key}");
    }
    Number(config, "internetAllowance", 100000, false);
    if (kind == "commission") Number(config, "companyFee", 100000);
    if (kind != "tiers") return;
    var tiers = (config["tiers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
    J.Need(tiers.Count is > 0 and <= 20 && tiers.Select(t => t.N("minOrders")).Distinct().Count() == tiers.Count, "حدد شرائح واضحة دون تكرار بداية الشريحة.");
    foreach (var t in tiers)
    {
        Number(t, "minOrders", 100000); Number(t, "salary", 100000); Number(t, "extraRate", 100000); Number(t, "fuelAllowance", 100000);
        J.Need(t.N("minOrders") == decimal.Truncate(t.N("minOrders")), "حدود الطلبات أعداد صحيحة.");
    }
}
