# ERP jobs: manual first, startup catch-up, optional daily

Hangfire runs inside the API. The primary workflow is the manual sync action; there is no three-hour recurrence and no active VPS scheduler. Nothing is deployed or enabled by this change. The CLI remains the route for individually approved first loads.

## Activation (Ghazal performs these steps)

1. Verify the pinned Hangfire.AspNetCore/SqlServer **1.8.25** packages and full SQL tests before enabling this feature. Ghazal chose cache-only validation: local1.8.23 compatibility results do not verify1.8.25. No packages were downloaded or NuGet sources changed.
2. In MonsterASP secret settings, set `ConnectionStrings:Jobs` and `ConnectionStrings:DefaultConnection` to staging `db77738` on the same server, and `ConnectionStrings:ErpReadonly` (or `ERP_READONLY_DB`) to the enforced SELECT-only ERP account. Ghazal sets the values himself; never put them in Git, tickets, screenshots or commands. Environment names use double underscores, such as `ConnectionStrings__Jobs`.
3. Set `Jobs:Enabled=true` and keep `Jobs:ErpSync:Enabled=false` until the first manual loads are approved. Missing Jobs connection keeps jobs disabled; an unexpected/mismatched target fails closed. No real database initialization or enqueue was performed in this task.
4. After fresh **«حمّل»**, use the reviewed CLI with exact `--accept-new-kind KIND` for each accepted first load, and verify same-snapshot idempotency. The separate debt workbook still needs its final mapping/month/reconciliation; hosted jobs never load it.
5. After first-load acceptance, set `Jobs:ErpSync:Enabled=true` and restart. Keep `Jobs:ErpSync:DailyEnabled=false` unless Ghazal wants the optional daily schedule. Startup catch-up defaults to on, after the ERP flag is enabled; account for one possible catch-up when restarting.

Current guards accept only staging, plus explicitly isolated loopback `GateJobsTest_*` databases in Testing. A production flag change alone is rejected; a later reviewed guard change and **«حوّل الإنتاج»** are required for cutover.

## Three entry points, one execution path

| Entry point | Default / behavior |
|---|---|
| Manual `POST /api/system/jobs/erp-sync/run` | Main workflow; requires `erp.sync.run`; returns `jobId` |
| Startup catch-up | `Jobs:ErpSync:CatchUpEnabled=true`, `CatchUpHours=24`; at most one admission per process start when no successful commit exists or the latest is older than the threshold |
| Optional daily | `Jobs:ErpSync:DailyEnabled=false`; when enabled, recurring ID `erp-sync-daily`, cron `0 10 * * *`, timezone `Asia/Riyadh` (10:00 Riyadh) |

All three reserve the same durable gate, enqueue `ErpSyncJob.ExecuteRequest`, and run the same consistent read/threshold/commit path without overrides. A queued or running request yields409 to a second manual request, even after15minutes. Otherwise a15-minute cooldown applies from the **accepted request time**, across triggers, users, API instances and restarts. Preview requests share the gate/cooldown but can run while commit scheduling is paused or not yet enabled.

The worker rechecks pause, config enablement and reservation ownership in the target write transaction. There is one worker, a SQL distributed execution lock, a process-local execution gate, zero automatic retries and cooperative30-minute execution cancellation. Failed/refused completion, counts and gate release are atomic with `erp_sync_run`; refusals notify administrators. A failed enqueue remains subject to cooldown. No source bodies, amounts, actor names or raw exceptions appear in progress responses.

Startup uses the latest **successful commit**, including earlier committed CLI telemetry with no mode field. A successful dry-run cannot suppress catch-up. A second startup callback in the same process never queues another catch-up, even if time advances. Recent success, pause, disabled flags, an active request or cooldown suppress the catch-up. Daily and startup requests cannot bypass the same admission gate.

On upgrade, reconciliation removes the old recurring ID `erp-sync`; its old serialized execution entry point is inert and records `legacy_schedule_disabled`. The optional daily recurrence coalesces missed intervals into one dispatch when the app returns; that dispatch still shares the admission gate with startup/manual work. [Hangfire recurrence](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html).

## Permission, progress and controls

- `erp.sync.run` is a catalog permission. A one-time startup migration grants it through `customPermissions` to existing SystemAdmin/FinancialManager records, preserving other permissions and explicit denies. New users in these roles receive the custom grant too; later revocation is not reseeded. Admin/SuperAdmin retain their existing permission behavior.
- SystemAdmin is a new **limited sync-operator role** because no such role existed in this backend. It gains no user-administration or scheduler-control powers. Finance managers likewise do not gain `users.manage`.
- `POST /api/system/jobs/erp-sync/dry-run` also requires `erp.sync.run`. It enqueues preview only. Hosted previews write counts telemetry, not business documents; CLI previews retain private-file-only logging.
- `GET /api/system/jobs/erp-sync/{jobId}` requires `erp.sync.run`, and returns queued/running/succeeded/refused/failed, mode, safe timestamps, refusal code and typed counts. It reads only a registered ERP request; arbitrary Hangfire jobs/arguments cannot be inspected. Finished request status is readable even when Jobs is disabled. Actor identity/time are retained in private request/audit records, not exposed here.
- `GET /api/system/jobs` remains Admin/SuperAdmin only. It lists the manual pipeline, optional daily recurrence and four off placeholders: reporting-rebuild, fcm-notifications, ninja-invoice-compare and large-imports.
- Pause/resume remain **SuperAdmin only**, require a reason, and are audited. Pause preserves active admission/cooldown, persists across restarts, and removes the daily recurrence. Resume cannot bypass config flags or first-load acceptance.
- The enqueue reservation is saved before Hangfire enqueue; the job records its own mapping too. Known terminal/interrupted admissions can be reconciled without business replay. An unlinked queued reservation is abandoned only after two minutes; a linked queued/running request is never cleared solely because time elapsed. No blind retries or automatic acceptance flags exist.
- `/health` returns `jobs: {enabled,lastErpSync,nextErpSync}` only. Last success is cached; next refers to the optional daily recurrence and is null when off. Disabled Jobs instantiates no Hangfire storage/server. There is no Dashboard in any environment.

## Optional AlwaysRunning / IIS warm-up

AlwaysRunning is **optional** for this workflow: manual use wakes the app and startup catch-up covers stale synchronization. It can reduce cold starts and keep the optional daily schedule punctual. It does not replace the acceptance/config controls.

If wanted, Ghazal opens a support ticket from the MonsterASP control panel for the intended website, requesting AlwaysRunning and confirmation that Application Initialization is available. The host enables this on eligible plans; no request, purchase or setting change is made by the agent. [MonsterASP instructions](https://help.monsterasp.net/websites/configuration/always-running).

Only if the host confirms the IIS section is available/unlocked, merge this fragment into the existing `system.webServer` section during an approved deployment, preserving the existing handler/settings:

```xml
<applicationInitialization doAppInitAfterRestart="true">
  <add initializationPage="/health" />
</applicationInitialization>
```

Application pool AlwaysRunning/preload is host-controlled; the fragment alone does not set it. No real config file was opened or modified. [Microsoft IIS requirements](https://learn.microsoft.com/en-us/iis/configuration/system.webserver/applicationinitialization/).

## Local verification and limits

Use disposable loopback SQL Server databases only, with `PAYROLL_TEST_SQL_CONNECTION`; never the old ERP-copy container or real staging/production. Verify admission/cooldown, process-start bounds, optional daily schedule, custom-permission migration, counts-only progress, pause and threshold refusal. Run the full .NET suite with0skipped, `node tests/erp-sync.mjs`, source boundary and inventory. Remove only the synthetic container created for the tests.

The pinned1.8.25 build/tests remain unavailable while those packages are missing from the local cache. Cached1.8.23 compatibility is reported separately and does not establish release acceptance. No browser/frontend work, deployment, live sync or production cutover is part of this task. The old systemd files remain disabled; [VPS-SYNC.md](../data/VPS-SYNC.md) is historical optional guidance requiring a new explicit decision.
