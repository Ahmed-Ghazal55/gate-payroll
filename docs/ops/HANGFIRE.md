# Hangfire in the API (R6j)

Hangfire replaces the VPS as the selected scheduler. Nothing is enabled or deployed by this change. The CLI remains available for reviewed manual loads. The four later-job IDs are placeholders only: reporting-rebuild, fcm-notifications, ninja-invoice-compare and large-imports; none can be enqueued or scheduled yet.

## Enable only after verification and approved initial loads

1. Verify the pinned Hangfire.AspNetCore and Hangfire.SqlServer **1.8.25** packages and the SQL tests first. The current offline cache has only 1.8.23; Ghazal chose no download on 2026-11-05. Any cached-version compatibility results are separate from final-version acceptance. No NuGet sources were changed.
2. Ghazal configures the website's secret settings in MonsterASP himself. Set `ConnectionStrings:Jobs` and `ConnectionStrings:DefaultConnection` to the approved staging database `db77738` on the same server. Set `ConnectionStrings:ErpReadonly` (or `ERP_READONLY_DB`) to the existing SELECT-only ERP account. Do not paste values into commands, tickets, Git, logs or screenshots. Environment names use double underscores, for example `ConnectionStrings__Jobs`.
3. Set `Jobs:Enabled=true`, keep `Jobs:ErpSync:Enabled=false`, then restart the application. Missing Jobs connection keeps jobs disabled. A wrong database or mismatched business target fails startup with a fixed message before Hangfire is initialized. Locally/tests, both flags default to false. No real staging initialization was performed in this task.
4. Only after fresh **«حمّل»**, run the reviewed CLI first loads with explicit `--accept-new-kind KIND` for each approved new kind. Verify same-snapshot idempotency. Debt-sheet loading has its own reconciliation and approval; it is never part of this recurring job.
5. Ghazal sets `Jobs:ErpSync:Enabled=true` and restarts only after those initial loads. The recurring ID is `erp-sync`, cron `15 */3 * * *`, timezone `Asia/Riyadh` (00:15, 03:15, …, 21:15). No automatic `--accept-*` option exists.

The implementation currently accepts only staging, plus explicit isolated loopback `GateJobsTest_*` databases in the Testing environment. It does **not** accept a production target after changing a flag. A later reviewed change and **«حوّل الإنتاج»** are required for a production cutover.

## MonsterASP AlwaysRunning and warm-up

Ghazal opens a support ticket from the MonsterASP control panel for the intended website and requests **AlwaysRunning** plus confirmation that IIS Application Initialization is available. The official host documentation says AlwaysRunning is enabled by support on eligible Premium plans; it is unavailable on Free. Nothing is purchased, enabled or sent to support by the agent. [MonsterASP instructions](https://help.monsterasp.net/websites/configuration/always-running).

If the host confirms that the IIS section is installed and unlocked, Ghazal can merge this **fragment** into the existing `system.webServer` section during a separately approved deployment. Preserve the existing ASP.NET Core handler/settings; do not replace the whole config file. No real config was opened or modified here.

```xml
<applicationInitialization doAppInitAfterRestart="true">
  <add initializationPage="/health" />
</applicationInitialization>
```

The host must also configure the application pool start mode to AlwaysRunning and application preload. The XML fragment alone cannot guarantee this on shared hosting. [Microsoft IIS prerequisites](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/applicationinitialization/).

If the app sleeps, scheduling stops while it is asleep. Once the app/server returns and Hangfire starts, `MisfireHandlingMode.Relaxed` coalesces missed occurrences into **one** catch-up execution. It does not replay every missed interval, and waking the app is still required. The job rechecks source consistency and thresholds at that time. [Hangfire recurring jobs](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html), [misfire modes](https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.Core/MisfireHandlingMode.cs).

## Safety and operations

- SQL Server storage uses schema `hangfire`, `PrepareSchemaIfNecessary=true`, one worker, and a SQL distributed execution lock. A process-local gate also protects direct calls. No Dashboard is mapped in any environment.
- The 30-minute deadline is an execution cancellation token, separate from the distributed-lock wait limit. Reads cancel active SQL commands; the target write gate and transaction observe cancellation, closing the active target connection so an unfinished transaction rolls back. This is cooperative cancellation; it does not terminate the API process. [Hangfire cancellation](https://docs.hangfire.io/en/latest/background-methods/using-cancellation-tokens.html).
- AutomaticRetry has zero attempts. Refusals are recorded as `erp_sync_run.status=refused`, with fixed codes and counts, plus an admin-only notification. No domain rows are written on refusal. Failed operations have fixed failure codes; no raw exception messages or connection values are persisted.
- Both hosted commits and hosted dry-runs record staging telemetry; the `mode` field distinguishes `commit` from `dry_run`. A dry-run does not save business documents. CLI dry-runs keep their existing private-file-only logging behavior. None was enqueued against real staging here.
- `GET /api/system/jobs` is Admin/SuperAdmin only and projects IDs, enabled/status, last status, cron/timezone and last/next times. It never returns arguments, raw Hangfire errors or connection strings. `POST /api/system/jobs/erp-sync/dry-run` is Admin/SuperAdmin and only enqueues preview mode when Jobs is enabled.
- `POST /api/system/jobs/erp-sync/pause` and `/resume` require **SuperAdmin** and a nonblank `reason`, saved with an audit record. Admin alone gets 403. Pause persists across restarts. Already queued commit jobs recheck pause inside their business transaction. Resume cannot bypass the config switches or first-load review. Pause allows explicit dry-runs for investigation.
- `/health` contains `jobs: {enabled, lastErpSync, nextErpSync}` only. When Jobs is disabled, it instantiates no Hangfire storage/server and does no jobs-related SQL. The dates are cached safe timestamps, not a source freshness guarantee.
- Turning off `Jobs:Enabled` and restarting stops this scheduler. Do not run both Hangfire and the optional VPS timer. If telemetry/storage is unavailable, inspect operational availability without auto-accepting or blindly retrying a refused import.

## Local verification

Use only a disposable SQL Server database/container with a loopback `PAYROLL_TEST_SQL_CONNECTION`; never the old ERP copy container or a real staging/production database. Run full .NET tests, `node tests/erp-sync.mjs`, `python3 scripts/release_source.py --check`, and `python3 tools/api_inventory.py`. SQL skips are not a valid acceptance result. Remove only the test container you created afterwards.

**Offline validation limit:** required-version 1.8.25 restore/build/tests remain unavailable until those packages exist in the local cache. Cached 1.8.23 compatibility checks, if reported, do not clear this limit. Do not deploy/enable jobs based on them alone.
