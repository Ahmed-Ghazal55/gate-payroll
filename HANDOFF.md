# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r4-api from main5fe403b, worktree gate-payroll-r4api |
| Live at preflight | 0.12.1 (5fe403b); local backend0.14.0, no deployment |
| Updated | 2026-11-04 by Codex |
| Weekly remaining | Before100% (Ghazal confirmed renewed) → after not measured; requested actual number |

## Goal now
Claude reviews feat/r4-api. Backend implementation R4.0–R4.8/R4.10 is locally usable/tested. R4.7 staging initialization still waits for a fresh reporting «حمّل»; R4.9 waits for «حوّل الإنتاج». No push, PR, merge, deploy or production access occurred.

## Baseline and final verification
Baseline: .NET191 passed/0failed/6SQL skipped; Python26;11HTTP+6browser suites passed; source179. COA check expected fail403→408.
Final: clean build0warnings/errors; .NET264 passed/0failed/7SQL skipped; Python26;12HTTP+6browser suites passed; COA check and source208 passed. TypeScript compiled from cached compiler. FTPS/deploy Python checks use mocks; production.mjs is a local synthetic server, not a production probe. No network restore or NuGet source changes.
No diff from base in Engine.cs, web/, Frontend/ or RiderForm/src. Old payroll endpoints keep their implementation; additive shared guards prevent duplicate new/legacy approval. Legacy footer stays for the frontend writer; manifests/backend version0.14.0.

## Done (one local commit per task)
- R4.10: local0.14.0 touch points/inventory; all25 new API routes indexed (196API/206total); two-decimal settings enforced, reporting child projection avoids copying entire parent arrays. Full verification above; learning log appended.
- R4.8 51cac2d: scoped monthly cards/explain/deductions/snapshot balances, immutable approved legacy fallback and own active rider-token statement;4 behavior tests and56 R4 HTTP checks.
- R4.7 3f51d9b: typed rpt tables plus cycle header, transactional immediate/buffered refresh, guarded rebuild/SHA consistency and SQLite no-op;10 focused tests passed, isolated SQL integration skipped. No report schema at startup.
- R4.6 1161c60: operations.view, nine allowlisted/paginated kinds and actual rider-account dates; company/platform/parent scope and safe nested projections.
- R4.5 4580598: audited stages0–7, actual account/operator evidence, normalized pending invoice upload, merged apps, rent/debt once monthly, independent target posting and append-only reversals. Debt opening mismatch rolls back approval; old journals remain intact.
- R4.4 12388eb: pure category/scheme evaluators, Riyadh billing boundaries, capped days, data-driven rent, exact order-share incentives, eligibility/explain and needs_decision.
- R4.3 1739ba3: signed admin mapping of approved current/history form choices before month start; latest choice applies, imported versions append, source/rider bodies unchanged.
- R4.2 5d986cd: versioned monthly terms/schemes/own-app eligibility/effective globals, immutable approvals, independent checker, exact eligibility snapshots on copy and current role defaults without rewriting users.
- R4.1 f35a95e: PayPlatforms table; new HS receivable112805, original112802 journals/projections/reversals preserved.
- R4.0 a851956: approved COA408/232 active posting/86new, updated count asserts and metadata.

## Decisions and limits
Ghazal: billed days cap30/28, including31-day/leap-February months. Existing debts update via Debts.Post at actual approval only. Negative new-path net needs_decision; no automatic debt.
Jahez remains a separate irregular-settlement cycle; principal411106 is stopped in the chart and blocked, agent411401 valid. Keeta-specific thresholds are not invented. Rates/conditions must be configured and independently approved.
Cycle inputs review actual per-operator facts and explicit category/days; no owner fallback. Raw invoices without a proved deductionsTotal need normalization/reconciliation. Normalized manual upload stays pending until stage7. Bank preview uses posted2110/212103 liability; monthly statement balances are labelled snapshots.
R4.7 read-only staging dry-run: rpt absent;6797 projected rows =2130 riders+3 companies+1874 platform accounts+1024 actual account assignments+558 vehicles+800 vehicle assignments+408 COA. Other tables0. Source counts may move; no staging writes, SQL DDL/bulk/incremental behavior not yet verified against SQL Server.

## Next step (exact)
Claude reviews feat/r4-api against BR24 and docs/api/pay-framework.md; do not merge/push/deploy. Main/other worktrees were not edited.
Only after fresh reporting «حمّل»: build tools/ReportRebuild with cached packages, rerun --dry-run, then --commit and --check; capture counts/consistency, preserve records, and update this handoff. Tool refuses any target except approved staging. See docs/data/REPORTING.md.
Only after «حوّل الإنتاج»: follow ~/.agents/prompts/codex-6.3-production-db.md; no production file/DB access was attempted here. Actual weekly remaining is still awaiting Ghazal's number.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
for s in accounting api-cleanup campaigns enterprise finance-views iban-form integration production rider-statement-simple submissions r2-backend r4-backend; do node tests/$s.mjs || break; done
for s in browser browser-v05 browser-iban finance-views-ui rider-statement-ui theme-screenshots; do PLAYWRIGHT_MODULE=/tmp/gate-browser/node_modules/playwright/index.mjs node tests/$s.mjs || break; done
git diff --check
```
