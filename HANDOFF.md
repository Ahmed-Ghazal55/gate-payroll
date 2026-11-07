# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/k-review (Claude K1–K4), from main 0.15.0 |
| Updated | 2026-11-05 by Claude |

## Wave 2 — Claude K1–K4 (2026-11-07), branch `feat/k-review`
- K1: config lists API (`ConfigLists.cs`), 6 groups, custom fields, versions, maker-checker for financial groups, seeded defaults (BR §27/§29).
- K2: internet advance (`type=internet`, fulfilment cash|bank|recharge_card, package price from lists, custody `MADA`). Card payout is 409 until Codex C3.
- K3: every cycle deduction has phase/order/label; installments taken in phase order; rider summary + statement show gross → commission → phase1 → المستحق الأولي → phase2 → phase3 → net.
- K4: review grid `GET/POST /api/pay/cycles/{id}/review…` (BR §32); stage 7 needs all rows approved.
- COA regenerated from the spec: +112404, 421302, 512122 (411 total / 235 posting / new 89). Generator accepts «جديد (BR …)».
- Verified: .NET 323 passed / 10 skipped (SQL needs Docker). Docs: `docs/api/pay-review.md`.
- Parallel: Codex `feat/r6b-accounting` (prompt `~/.agents/prompts/codex-R6b-accounting.md`), Antigravity `feat/r6b-ui` (prompt `antigravity-R6b-ui.md`). Antigravity merges `feat/k-review` for A3–A6.

## Release 0.15.0 (Claude)
- Merges `feat/r6-erp-pulls` (Codex R6a + fix1 + Hangfire R6j), `feat/r6-ui-qa` (Antigravity R6ui) and `feat/ninja-engine` (Claude N2/N3) on top of 0.14.2.
- Release fixes by Claude:
  - `Newtonsoft.Json 13.0.3` pinned (Hangfire floor 11.0.1 fails NuGet audit as an error); `Pkcs 10.0.13` pinned in `Payroll.Sync`.
  - `JobsOptions`: a wrong Jobs target no longer crashes startup; jobs stay off and `GET /api/system/jobs` shows `blocked_unapproved_target`.
- Verified locally: .NET 331 passed / 0 skipped (with SQL Server 2022 container), HTTP suites (erp-sync, integration, enterprise, accounting, submissions, iban-form, campaigns, finance-views), web build, release_source, check_publish.
- Hangfire stays OFF in production: the guard accepts only a site whose own DefaultConnection is staging `db77738`. Production cutover needs «حوّل الإنتاج» plus a reviewed guard change.
- Next: Wave 2 (K1–K3 Claude, R6b Codex, R6b Antigravity). Ghazal decisions still open: BIKE difference, deductions control mismatch, «حمّل».

## R6a / R6j details (Codex)
### Current scope
Manual ERP sync is the primary workflow, with startup catch-up and optional daily10:00 Riyadh execution. The revised R6a2 prompt supersedes the old three-hour schedule. No active VPS, deployment, live enqueue or staging write was performed.

## Done
- R6a baseline f86596b; fixes F1..F5: ced524c, 3630d2a, d585bf2, 0277aff, 860d6ae. Mapping proposed_approved; LegacyOpeningDebt/InternetAdvance/HsWalletTopUp categories; exact branch aliases and inactive debt-only riders, excluded from payroll/cycles, same-identity reactivation. First-load approval/month/reconciliation remain required.
- R6j-1 79d5c8c: Payroll.Sync core shared with API/CLI, caller-owned sponsor catalog; manual first acceptance unchanged. Earlier fix/library suites312 passed /0failed /0skipped.
- Revised R6j-2 5b2837f supersedes f4b9d64 scheduling: POST /api/system/jobs/erp-sync/run and /dry-run require erp.sync.run; GET /api/system/jobs/erp-sync/{jobId} returns counts-only progress. Manual run returns jobId; shared durable admission/cooldown15min from accepted request,409while queued/running. Actor/time audited, never exposed in progress.
- Permission catalog + one-time startup seeder reach existing SystemAdmin/FinancialManager through customPermissions; explicit denies and later revocation preserved. New SystemAdmin is a limited sync role, without users.manage or pause/resume. Admin/SuperAdmin retain existing grants.
- Startup: CatchUpEnabled defaults true but only after Jobs + ErpSync are enabled; CatchUpHours defaults24. At most one admission per process start, based on latest successful commit (not preview). Optional daily ID erp-sync-daily, cron0 10 * * *, Asia/Riyadh; DailyEnabled defaults false.
- Upgrade removes recurring ID erp-sync. Old serialized entry point is inert (legacy_schedule_disabled). All actual triggers use ExecuteRequest and the same safe path without overrides; single worker + SQL distributed lock + local gate, zero retries, cooperative30min cancellation. Pause/config/reservation ownership checked inside target transaction.
- Completion/counts/gate release/refusal notification are atomic. Progress uses primary-key job links instead of JSON scans beside the writer. Four later-job IDs remain disabled metadata only. GET /api/system/jobs is Admin/SuperAdmin; pause/resume remain SuperAdmin with reason/audit and preserve admission state.
- Revised R6j-3 da6baf2: docs/ops/HANGFIRE.md covers manual/progress/cooldown/catch-up/daily, optional AlwaysRunning and IIS /health fragment. Old systemd files remain disabled/uninstalled; VPS documentation is historical optional guidance requiring a new decision. No Dashboard mapped.
- Revised R6j-4: admission, concurrency, permission seeding/revocation, restart/cooldown, fresh/stale success and daily SQL tests; HTTP checks and API inventory refreshed. Learning-notes appended without committing ~/.agents.

## Verified and validation limit
- Cached1.8.23 compatibility only: build0warnings/errors; full .NET324 passed /0failed /0skipped on synthetic SQLite + isolated Docker SQL Server2022; node tests/erp-sync.mjs54 HTTP checks. Includes startup/daily and actual single-worker/storage checks. No browser/frontend verification.
- Source boundary246; inventory245total /235API; git diff --check clean. Own synthetic SQL container removed; gate-erp-copy-20261103 remains stopped/untouched.
- Tracked Hangfire.AspNetCore/SqlServer remain1.8.25. Cache-only final restore fails NU1102 (only1.8.23 exists); pinned-version build/tests NOT RUN per Ghazal's no-download decision. Cached compatibility does not establish1.8.25 acceptance. No package downloads or NuGet source changes; /tmp override not committed. Rerestore before building: final assets reflect the failed pinned-version restore.
- No live ERP query in this update, staging writes/enqueues, push, merge, deploy, host settings, VPS actions or original-file changes. All runtime tests used synthetic local data; no secrets/personal data printed.

## Earlier source blockers (unchanged; no fresh live preview)
- F5 SELECT-only snapshot2026-11-05T15:22:16Z–15:22:44Z: vehicle1/558changed,557unchanged; plateNumberAr1/plateNumberEn1/registrationType1 overlap. New odometer facts13303; refused new_kind_requires_acceptance:vehicle_odometer_reading, no overrides/load. Pre-fix drift moved from317 to332/558, dominated by daily fields. Private counts: sync-logs/r6a-f2-before.json and r6a-f2-after.json.
- Debt preview808rows:370valid /438flagged (unmatched_company417, negative16, invalid_identity5). Inactive riders planned0, branch fallback0; D blank707/populated101, no exact approved aliases. Control reconciliation blocked23row numbers. Private report: sync-logs/r6a-fix1-debt-sheet-preview.json. Never guess companies. Missing authoritative sources: sponsorship periods, ADV/payment request tables.

## Next step (exact)
- When approved1.8.25 packages exist in the local cache, restore/build/full-test without the temporary override; use isolated loopback PAYROLL_TEST_SQL_CONNECTION and require0skipped. Rerun node ERP-sync, source boundary and inventory. Do not accept cached preflight as final verification.
- Resolve company evidence and debt month/reconciliation, then obtain fresh «حمّل» for reviewed CLI first loads and per-kind acceptance. Hosted jobs never auto-accept kinds or load the debt workbook.
- After successful verification/deployment and explicit activation approval, Ghazal sets secret Jobs + DefaultConnection to staging db77738 (same server), ErpReadonly SELECT-only connection, Jobs:Enabled=true. Keep ErpSync:Enabled=false until first loads accepted. Then enable ErpSync, restart (startup catch-up may enqueue once); DailyEnabled remains false unless requested. AlwaysRunning is optional and host-controlled.
- Production requires «حوّل الإنتاج» and a later reviewed target-guard change; current guards reject production. No Dashboard. No VPS/three-hour scheduler is active.

## Verify (once pinned packages are cached)
- dotnet restore tests/Engine.Tests/Engine.Tests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1
- dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 (isolated loopback SQL only); node tests/erp-sync.mjs
- python3 scripts/release_source.py --check; python3 tools/api_inventory.py; git diff --check. Operations: docs/ops/HANGFIRE.md; CLI/debt: docs/data/SYNC.md and DEBT-SHEET.md.
