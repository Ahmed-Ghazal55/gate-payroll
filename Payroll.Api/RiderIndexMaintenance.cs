using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Payroll;

/// <summary>Admin-only rider index maintenance: status + one-at-a-time background create.</summary>
public static class RiderIndexMaintenance
{
    static readonly object Gate = new();
    static int _running;
    static string? _currentStep;
    static string? _lastError;
    static DateTimeOffset? _startedAt;
    static DateTimeOffset? _finishedAt;
    static readonly ConcurrentDictionary<string, string> LiveStatus = new(StringComparer.Ordinal);

    public static void ResetForTests()
    {
        Interlocked.Exchange(ref _running, 0);
        _currentStep = null;
        _lastError = null;
        _startedAt = null;
        _finishedAt = null;
        LiveStatus.Clear();
    }

    public static void Map(RouteGroupBuilder api, Store store, IConfiguration config, ILoggerFactory logFactory)
    {
        var log = logFactory.CreateLogger("RiderIndexMaintenance");

        api.MapGet("/system/maintenance/rider-indexes", (HttpContext ctx) =>
        {
            DemandAdmin(ctx);
            return store.Read(c => StatusPayload(c, ctx.User));
        }).WithTags("Administration").WithSummary("Read rider index maintenance status with allowedActions");

        api.MapPost("/system/maintenance/rider-indexes", (HttpContext ctx) =>
        {
            DemandAdmin(ctx);
            int timeout = config.GetValue("RiderIndexes:CommandTimeoutSeconds", RiderDataIndexes.DefaultCommandTimeoutSeconds);
            if (timeout < 60) timeout = RiderDataIndexes.DefaultCommandTimeoutSeconds;
            int minData = config.GetValue("RiderIndexes:MinFreeDataMb", RiderDataIndexes.DefaultMinFreeDataMb);
            int minLog = config.GetValue("RiderIndexes:MinFreeLogMb", RiderDataIndexes.DefaultMinFreeLogMb);
            if (minData < 0) minData = RiderDataIndexes.DefaultMinFreeDataMb;
            if (minLog < 0) minLog = RiderDataIndexes.DefaultMinFreeLogMb;

            return store.Read(c =>
            {
                if (Volatile.Read(ref _running) == 1 || AppLockHeld(c))
                    throw new ApiError("صيانة فهارس المندوب قيد التشغيل بالفعل.", 409);
                if (RiderDataIndexes.RollbackInProgress(c))
                    throw new ApiError("ما زال التراجع عن فهرس سابق جارياً؛ انتظر ثم أعد المحاولة.", 409);

                var (dataFree, logFree) = RiderDataIndexes.FreeSpaceMb(c);
                if (dataFree < minData || logFree < minLog)
                    throw new ApiError($"المساحة الحرة غير كافية (بيانات {dataFree} ميغابايت، سجل {logFree} ميغابايت؛ المطلوب بيانات ≥{minData} وسجل ≥{minLog}).", 409);

                if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                    throw new ApiError("صيانة فهارس المندوب قيد التشغيل بالفعل.", 409);

                _currentStep = null;
                _lastError = null;
                _startedAt = DateTimeOffset.UtcNow;
                _finishedAt = null;
                LiveStatus.Clear();
                foreach (var index in RiderDataIndexes.Indexes)
                    LiveStatus[index.Name] = "pending";

                string? sql = store.Provider == "mssql"
                    ? (config.GetConnectionString("DefaultConnection") ?? throw new ApiError("اتصال SQL غير مضبوط.", 503))
                    : null;
                string dir = store.DirectoryPath;
                string actor = ctx.User.Identity?.Name ?? "administrator";
                _ = Task.Run(() => RunJob(dir, sql, timeout, minData, minLog, actor, log));

                return StatusPayload(c, ctx.User);
            });
        }).WithTags("Administration").WithSummary("Start rider index maintenance one index at a time");
    }

    static void DemandAdmin(HttpContext ctx)
    {
        if (!AccessPolicy.Administrator(ctx.User))
            throw new ApiError("صيانة فهارس المندوب لمسؤول النظام فقط.", 403);
    }

    static JsonObject StatusPayload(DbConnection c, System.Security.Claims.ClaimsPrincipal user)
    {
        RiderDataIndexes.EnsureOpsTable(c);
        bool running = Volatile.Read(ref _running) == 1;
        var indexes = new JsonArray();
        foreach (var index in RiderDataIndexes.Indexes)
        {
            string hash = RiderDataIndexes.DefinitionHash(index);
            string status;
            if (LiveStatus.TryGetValue(index.Name, out string? live))
                status = live;
            else if (RiderDataIndexes.StepCompleted(c, index.Name, hash) && RiderDataIndexes.IndexMatches(c, index))
                status = "done";
            else if (RiderDataIndexes.IndexMatches(c, index))
                status = "done";
            else
                status = "pending";
            if (status == "pending" && string.Equals(_currentStep, index.Name, StringComparison.Ordinal) && running)
                status = "running";
            if (status != "done" && !string.IsNullOrEmpty(_lastError) && string.Equals(_currentStep, index.Name, StringComparison.Ordinal) && !running)
                status = "failed";
            indexes.Add(new JsonObject
            {
                ["name"] = index.Name,
                ["kind"] = index.KindLiteral,
                ["definitionHash"] = hash,
                ["status"] = status
            });
        }
        var actions = new JsonArray();
        var blockers = new JsonArray();
        if (!AccessPolicy.Administrator(user)) blockers.Add("صيانة فهارس المندوب لمسؤول النظام فقط.");
        else if (running) blockers.Add("صيانة فهارس المندوب قيد التشغيل بالفعل.");
        else if (RiderDataIndexes.RollbackInProgress(c)) blockers.Add("ما زال التراجع عن فهرس سابق جارياً.");
        else actions.Add("run");
        return new JsonObject
        {
            ["running"] = running,
            ["currentStep"] = _currentStep,
            ["lastError"] = _lastError,
            ["startedAt"] = _startedAt?.ToString("O"),
            ["finishedAt"] = _finishedAt?.ToString("O"),
            ["indexes"] = indexes,
            ["allowedActions"] = actions,
            ["approvalBlockers"] = blockers
        };
    }

    static bool AppLockHeld(DbConnection c)
    {
        if (c is SqliteConnection) return false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM sys.dm_tran_locks
            WHERE resource_type = N'APPLICATION'
              AND resource_description LIKE N'%RiderIndexMaintenance%';
            """;
        try { return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0; }
        catch { return false; }
    }

    static void RunJob(string dir, string? sqlConnection, int timeout, int minData, int minLog, string actor, ILogger log)
    {
        try
        {
            var store = new Store(dir, sqlConnection);
            store.Write((c, tx) =>
            {
                RiderDataIndexes.EnsureOpsTable(c);
                Store.Audit(c, tx, "maintenance.rider_indexes.start", "rider-indexes", actor);
                return true;
            }, invalidateSummaries: false);

            ExecuteAll(store, timeout, minData, minLog, log);

            store.Write((c, tx) =>
            {
                Store.Audit(c, tx, "maintenance.rider_indexes.done", "rider-indexes", actor);
                return true;
            }, invalidateSummaries: false);
        }
        catch (Exception ex)
        {
            _lastError = "فشلت صيانة فهارس المندوب.";
            if (!string.IsNullOrEmpty(_currentStep))
                LiveStatus[_currentStep] = "failed";
            log.LogWarning(ex, "Rider index maintenance failed at {Step}", _currentStep);
        }
        finally
        {
            _finishedAt = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>Run the full job synchronously (tests). Refuses when another run is active.</summary>
    public static void RunSynchronously(Store store, int timeout = 120, int minData = 0, int minLog = 0)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new ApiError("صيانة فهارس المندوب قيد التشغيل بالفعل.", 409);
        _currentStep = null;
        _lastError = null;
        _startedAt = DateTimeOffset.UtcNow;
        _finishedAt = null;
        LiveStatus.Clear();
        foreach (var index in RiderDataIndexes.Indexes)
            LiveStatus[index.Name] = "pending";
        try
        {
            ExecuteAll(store, timeout, minData, minLog, null);
        }
        catch (ApiError)
        {
            if (!string.IsNullOrEmpty(_currentStep)) LiveStatus[_currentStep!] = "failed";
            throw;
        }
        catch (Exception ex)
        {
            _lastError = "فشلت صيانة فهارس المندوب.";
            if (!string.IsNullOrEmpty(_currentStep)) LiveStatus[_currentStep!] = "failed";
            throw new ApiError("فشلت صيانة فهارس المندوب: " + ex.GetType().Name, 500);
        }
        finally
        {
            _finishedAt = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>Holds one connection for the session app-lock for the whole DDL loop.</summary>
    static void ExecuteAll(Store store, int timeout, int minData, int minLog, ILogger? log)
    {
        store.WithConnection(c =>
        {
            if (RiderDataIndexes.RollbackInProgress(c))
                throw new ApiError("ما زال التراجع عن فهرس سابق جارياً؛ انتظر ثم أعد المحاولة.", 409);
            var (dataFree, logFree) = RiderDataIndexes.FreeSpaceMb(c);
            if (dataFree < minData || logFree < minLog)
                throw new ApiError($"المساحة الحرة غير كافية (بيانات {dataFree} ميغابايت، سجل {logFree} ميغابايت؛ المطلوب بيانات ≥{minData} وسجل ≥{minLog}).", 409);
            if (!TryAcquireAppLock(c, timeout))
                throw new ApiError("صيانة فهارس المندوب قيد التشغيل بالفعل.", 409);
            try
            {
                RiderDataIndexes.EnsureOpsTable(c);
                RiderDataIndexes.EnsureColumns(c, timeout);
                foreach (var index in RiderDataIndexes.Indexes)
                {
                    string hash = RiderDataIndexes.DefinitionHash(index);
                    _currentStep = index.Name;
                    LiveStatus[index.Name] = "running";
                    if (RiderDataIndexes.StepCompleted(c, index.Name, hash) && RiderDataIndexes.IndexMatches(c, index))
                    {
                        LiveStatus[index.Name] = "done";
                        continue;
                    }
                    while (RiderDataIndexes.RollbackInProgress(c))
                        Thread.Sleep(500);
                    RiderDataIndexes.CreateIndex(c, index, timeout);
                    if (store.Provider == "mssql" && !RiderDataIndexes.IndexMatches(c, index))
                        throw new InvalidOperationException("فشل التحقق من تعريف الفهرس: " + index.Name);
                    RiderDataIndexes.RecordStep(c, index.Name, hash);
                    LiveStatus[index.Name] = "done";
                    log?.LogInformation("Rider index maintenance completed {Index}", index.Name);
                }
            }
            finally { ReleaseAppLock(c); }
        });
    }

    static bool TryAcquireAppLock(DbConnection c, int timeoutSeconds)
    {
        if (c is SqliteConnection) return true;
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
              @Resource=@resource,
              @LockMode='Exclusive',
              @LockOwner='Session',
              @LockTimeout=0;
            SELECT @result;
            """;
        var p = cmd.CreateParameter(); p.ParameterName = "@resource"; p.Value = RiderDataIndexes.AppLockResource; cmd.Parameters.Add(p);
        int result = Convert.ToInt32(cmd.ExecuteScalar() ?? -999);
        return result >= 0;
    }

    static void ReleaseAppLock(DbConnection c)
    {
        if (c is SqliteConnection) return;
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "EXEC sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
            var p = cmd.CreateParameter(); p.ParameterName = "@resource"; p.Value = RiderDataIndexes.AppLockResource; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }
        catch { /* session end releases */ }
    }

    public static JsonObject ReadStatus(Store store, System.Security.Claims.ClaimsPrincipal user) =>
        store.Read(c => StatusPayload(c, user));
}
