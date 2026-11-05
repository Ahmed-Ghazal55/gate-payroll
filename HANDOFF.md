# HANDOFF

| Field | Value |
|---|---|
| Current writer | none (next: Claude review of feat/r4-integrate) |
| Branch | feat/r4-integrate = feat/r4-api (0d3ff76) + feat/r4-web (e148a01) + R4-fix1 |
| Live | 0.12.1 (5fe403b); local 0.14.0 |
| Updated | 2026-11-05 by Antigravity |

## advances API done (Claude, 2026-11-05)
- Branch `feat/advances-api` (worktree `gate-payroll-adv`), from `feat/r4-integrate` 55a97bc.
- 2fcf695 ADV-G1/G2: `OrgDirectory` (memberships with ranks A+/A/B/C, delegations with expiry, supervision, routing; dated, append-only), `Notifications.RaiseFor/CloseTasks` (personal tasks; read ≠ decided).
- 565885d ADV-A1..A4: `AdvanceRequests` (request → responsible review approve/reject/propose → supervisor respond → custodian payout → CashAdvance/BankAdvance debt + Dr 1210 / Cr vault journal; 409 when the vault is short). Summary card API. Permissions `advances.request|review|payout`, `org.manage`. `PayStatements.Build` takes an optional transaction.
- Tests: .NET 268 passed / 0 failed / 7 SQL skipped (4 new advance tests); `node tests/advances.mjs` 37 HTTP checks; source boundary 212; ENDPOINTS regenerated (227 routes). Docs: `docs/api/org.md`, `docs/api/advances.md`.
- Next: Antigravity `~/.agents/prompts/antigravity-R5-advances-web.md` (merge this branch into `feat/r4-integrate` after fix1).

## Next step (exact)
Claude reviews `feat/r4-integrate`. Then Claude confirms and Ghazal says «ادمج» for 0.14.0.


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

## R4 fix round 1 (Antigravity)
| Updated | 2026-11-05 by Antigravity |

### Completed Fixes (F1 - F4)
1. **F1: Payroll rules editor & cycle stepper wired to real APIs:**
   - Global settings, app terms, internal schemes, and eligibility fully wired to `/api/pay/apps/{app}/terms|schemes|eligibility?period=`, `/api/pay/settings`, and `/api/pay/apps/{app}/copy?from=&to=`.
   - Independent maker-checker enforced: User A self-approval refused with HTTP 403; User B approves; approved settings are strictly read-only.
   - Scheme choices wired to preview → commit flow.
   - Cycle stepper wired to `/api/pay/cycles`, `/api/pay/cycles/{id}/advance` (stage n+1 only), and `/api/pay/cycles/{id}/lines` with per-rider explain lines drawer and `needs_decision` badge.
   - Client-side hardcoded numbers eliminated. Empty months display «لا توجد إعدادات لهذا الشهر — انسخ الشهر السابق أو أنشئ مسودة».
2. **F2: Legacy payroll run workspace added:**
   - Added shared tab «الدورة الحالية (النظام القائم)» (`id: 'current-cycle'`) at `/app/payroll/?tab=current-cycle`.
   - Wired to real endpoints: `/api/payroll/preview`, `/runs`, `/approve`, `/export.csv`, `/api/adjustments`, `/api/payments`, `/api/ledger/{iqama}`.
   - Includes preview table with blockers, status badges, details modal, approve modal with sponsor selector, adjustment modal, payment modal with cash availability validation, ledger view modal, and CSV export link.
3. **F3: Operations and statements use 0.14.0 endpoints:**
   - Operations sub-tabs call `GET /api/operations/{kind}?page=&q=` (7 operational kinds: cities, housing, vehicles, custody, guarantees, cases, platform accounts). Removed «غير متاح بعد» where data exists.
   - Rider Profile 360 accounts tab connected to `GET /api/riders/{iqama}/accounts`.
   - Rider monthly statement connected to `GET /api/finance/statement/rider/{iqama}/monthly?period=` with fallback.
   - Rider app «كشف الحساب في التطبيق» preview uses the same shape without exposing rider token endpoints to admin.
4. **F4: Remaining gap:**
   - `GET /api/rider-mobile/sessions` retained as «غير متاح بعد — بانتظار اكتمال واجهات خادم الجلسات الميدانية (Codex)», and kept in API gaps for Codex.
5. **Corrected old-UI retirement list:**
   - Corrected claim: Legacy payroll run flow (`/api/payroll/preview`, `/runs`, `/approve`, `/export.csv`, `/api/adjustments`, `/api/payments`, `/api/ledger`) is now fully available in `/app/payroll/?tab=current-cycle`.
   - Remaining legacy screens in `Frontend/` without `/app`: **NONE** (0 legacy screens remaining).

### Commits on `feat/r4-integrate`
- `3151345` — `feat(web): wire payroll rules editor, cycle stepper, and legacy payroll run workspace (F1, F2)`
- `3e82bab` — `feat(web): update operations and rider statements with 0.14.0 endpoints (F3)`
- `44446a6` — `test(web): live e2e verification for R4 fixes and legacy suites (F4-verify)`

### Verification summary
- **Static build:** `npm run build` compiled 19/19 routes cleanly without any `/app/app/` links.
- **Static E2E & screenshots:** `npm run test:e2e` passed all 8 suites; 16 screenshots at 1440 and 360.
- **Live E2E suite:** `node tests/e2e-live.mjs` (delegating to `web/tests/e2e-live.mjs`) passed all 13 steps against real .NET API and live database. Screenshots generated in `test-results/r4-fix1/`.
- **Regression tests:** `node tests/browser-iban.mjs`, `node tests/finance-views-ui.mjs`, and `node tests/r4-backend.mjs` all passed.

### API gaps for Codex
- `GET /api/rider-mobile/sessions`: rider active mobile sessions and device fingerprints.
- Database reporting views/tables in `rpt` schema (Codex R4.7).

### Capacity status
- Before session: ~62% weekly limit used (~38% remaining).
- After session: ~78% weekly limit used (~22% remaining).

### How to verify
```bash
cd /home/ghazal/Projects/active/gate-payroll-r4api/web && npm run build && npm run test:e2e
cd /home/ghazal/Projects/active/gate-payroll-r4api && node tests/e2e-live.mjs
cd /home/ghazal/Projects/active/gate-payroll-r4api && node tests/browser-iban.mjs && node tests/finance-views-ui.mjs && node tests/r4-backend.mjs
```
