# HANDOFF

| Field | Value |
|---|---|
| Current writer | none (next: Antigravity R4 fix round) |
| Branch | feat/r4-integrate = feat/r4-api (0d3ff76) + feat/r4-web (e148a01, includes feat/r3-web d106714) |
| Live | 0.12.1 (5fe403b); local 0.14.0 |
| Updated | 2026-11-05 by Claude |

## Next step (exact)
Antigravity: `~/.agents/prompts/antigravity-R4-fix1.md` on this branch. Then Claude reviews and Ghazal says «ادمج» for 0.14.0.


## R4 backend (Codex)
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


## R4 web (Antigravity)
| Updated | 2026-11-04 by Antigravity |

## Goal completed
Release train R4 web completed on `feat/r4-web`:
1. **Part 0 (R3 fixes on `feat/r3-web`, commit `d106714`)**:
   - 0.1 TGA Split: isolated percent mode payload `{ riderPercent, riderId }` vs amount mode `{ companyAmount, riderAmount, riderId }`, labeled preview «تقريبي — المبلغ النهائي من الخادم». Live test verifies mixed mode 400 rejection.
   - 0.2 Swallowed errors: replaced `.catch(() => [])` with `<ErrorState onRetry={...} />` in treasury, debts, companies.
   - 0.3 HS card: labeled invoices card «إجمالي الصفحة الحالية».
2. **Part A (Sidebar IA & SectionTabs, commit `3f21ca5`)**:
   - Collapsible groups matching spec 09 lines 37–50, including «تطبيق المناديب», active group persistence, 360px drawer.
   - Reusable `SectionTabs` component (`?tab=`, ARIA tabs keyboard navigation, deep linking).
3. **Part B & C (All workspaces, commits `ff2a679` to `7acb033`)**:
   - W3 Riders (`/app/riders/`): دليل المناديب · ملف 360 · جودة واكتمال البيانات · المزامنة.
   - W4 Statement (`/app/riders/statement/`): Plan §3 layout (header, monthly cards, timeline table, له/عليه badge, PDF print, CSV export).
   - W5 Operations (`/app/operations/`): المدن · السكن · المركبات · العهد · الكفالات · القضايا · حسابات المنصات («غير متاح بعد»).
   - W6 Admin (`/app/admin/`): المستخدمين والصلاحيات (`/api/users`), سجل التدقيق (`/api/audit`), إعدادات المنظومة.
   - W7 Imports (`/app/imports/`): معالج استيراد من 5 خطوات ومحفوظات الدفعات.
   - W8 & W9 Payroll (`/app/payroll/`): هيكل متعدد المنصات (6 منصات)، دورة رواتب 0–7 مع شارة «يحتاج قرار» للصافي السالب، محرر أنظمة مُمرحل شهرياً ومصفوفة فئات (6 أعمدة).
   - W10 Rider App Admin (`/app/rider-app/`): الإشعارات والإرسال، إعدادات Firebase، معاينة كشف التطبيق.
4. **Part D (Retirement Check, commit `047d43d`)**:
   - `grep -rn "/legacy" web/` confirmed zero legacy links in source (only allowed login `returnUrl` check remains).
   - Legacy views in `Frontend/` with no `/app` screen: **NONE** (100% of legacy screens migrated to `/app`).

## Verification summary
- **Static build:** `npm run build` compiled 19/19 routes cleanly without any `/app/app/` links.
- **Static E2E & screenshots:** `npm run test:e2e` passed all 8 suites; 16 screenshots at 1440 and 360 in `test-results/r4-web/`.
- **Live E2E suite:** `node web/tests/e2e-live.mjs` passed all 10 steps against real .NET API and live database.
- **Regression tests:** `node tests/browser-iban.mjs` and `node tests/finance-views-ui.mjs` both passed cleanly.

## Commits on `feat/r4-web`
1. `3f21ca5` — `feat(web): sidebar groups and SectionTabs component (R4-W1)`
2. `ff2a679` — `feat(web): riders directory workspace (R4-W3)`
3. `8e6f651` — `feat(web): rider financial statement view (R4-W4)`
4. `c7a3170` — `feat(web): operations and assets workspace (R4-W5)`
5. `7e140a9` — `feat(web): admin and audit workspace (R4-W6)`
6. `cc28779` — `feat(web): operational data imports workspace (R4-W7)`
7. `51f13ba` — `feat(web): payroll multi-platform framework and rules editor (R4-W8)`
8. `7acb033` — `feat(web): rider mobile app admin workspace (R4-W10)`
9. `047d43d` — `feat(web): complete legacy links removal (R4-W11)`
10. `7a6e4e4` — `test(web): e2e static and live verification for R4 workspaces (R4-verify)`

## API gaps for Codex
- `GET /api/operations/{kind}`: query parameters and data models for cities, housing, vehicles, custody, guarantees, cases, platform accounts (Codex R4.6).
- `GET /api/rider-mobile/sessions`: rider active mobile sessions and device fingerprints.
- `GET /api/finance/statement/rider/{iqama}/monthly`: monthly multi-platform aggregated statement view (Codex R4.8).
- Database reporting views/tables in `rpt` schema (Codex R4.7).

## Capacity status
- Before session: ~48% weekly limit used (~52% remaining).
- After session: ~62% weekly limit used (~38% remaining).

## How to verify
```bash
git -C /home/ghazal/Projects/active/gate-payroll-r4web status
cd /home/ghazal/Projects/active/gate-payroll-r4web/web && npm run build && npm run test:e2e
cd /home/ghazal/Projects/active/gate-payroll-r4web && node web/tests/e2e-live.mjs
cd /home/ghazal/Projects/active/gate-payroll-r4web && node tests/browser-iban.mjs && node tests/finance-views-ui.mjs
```
