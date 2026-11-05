# HANDOFF

| Field | Value |
|---|---|
| Current writer | Claude: release 0.14.0 (PR → CI → squash merge → deploy) |
| Branch | feat/r4-integrate = feat/r4-api + feat/r4-web + R4-fix1 + feat/advances-api + R5-web |
| Live | 0.12.1 (5fe403b); local 0.14.0 |
| Updated | 2026-11-05 by Antigravity |

## Next step (exact)
Claude reviews `feat/r4-integrate` (R5 web + advances API + fix1). Then Claude confirms and Ghazal says «ادمج» for 0.14.0 release.


## Claude review R5 + release prep (2026-11-05)
- Merged feat/mobile-sessions (Codex R5-C1/C2) into feat/r4-integrate.
- Web fixes cefd521: admin org loader and advance summary no longer swallow errors; approve blocked without summary; removed auto-filled coverage reason (BR §25 needs a written reason).
- API fixes 99fbdc6 (Codex MEDIUM ×3): per-rider approved settlement cutoff; numeric statement net (FinancialAccountant reviewer got 500); ordered org dates.
- Test e2e label after R5-4.
- Verified: .NET 281/0/7; HTTP advances 63, mobile-sessions 77, r4-backend 56, integration 75, enterprise 150, accounting 56, api-cleanup 322; browser ×5; web e2e; live e2e incl. advances 3 users; release_source 216; python unittest OK.
- Follow-up (web): sessions list + revoke screen replacing «غير متاح بعد» (docs/api/mobile-sessions.md).
- After deploy, Ghazal enters org data in production: memberships, advance.review routing, advance.payout:<vault> custodians, supervision.

## R5 web: advance requests, ranks and responsibilities, notifications (Antigravity)
| Updated | 2026-11-05 by Antigravity |

### Completed Features (R5-0 to R5-5)
1. **R5-0: Removed swallowed error fallbacks (commit `3a68295`)**:
   - Cleaned `payroll/page.tsx`, `statement/page.tsx`, `login/page.tsx`, `Header.tsx`.
   - Verified `grep -rn "catch(() =>" web/src/app` returns 0 occurrences (only `res.json().catch` in `api.ts` remains).
2. **R5-1: Debts and Advance Requests workspace (commit `bd06d59`)**:
   - Synchronized `?tab=` URL routing in `/app/debts/?tab=advance_requests` and added per-item permissions in `Sidebar.tsx`.
   - Supervisor view (`advances.request`) with supervised riders picker (`GET /api/org/supervision?mine=1`), status chips, and proposal response modal (`POST /api/advance-requests/{id}/respond`).
   - Accountant view (`advances.review`) with review queue, drawer summary card (`GET /api/advance-requests/{id}/summary`), coverage flag banner, vault balances, and approve/reject/propose actions.
   - History timeline showing decisions, actor, rank snapshot, notes, and timestamps.
3. **R5-2: Advance payouts queue for custodians (commit `ca87452`)**:
   - Payouts queue at `/app/treasury/?tab=advance_payouts` (`advances.payout`), filtered to responsible vaults.
   - Payout modal with reference input, creating Dr 1210 / Cr vault journal, with 409 insufficient funds handling.
4. **R5-3: Organization directory, ranks and routing (commit `62e39fa`)**:
   - Directory workspace at `/app/admin/?tab=org` (`org.manage`) with 4 sub-tabs: Memberships (A+/A/B/C ranks), Delegations, Supervision (supervisor ↔ rider), and Routing (`advance.review`, `advance.payout:<vault>`).
   - Responsible users inspector (`GET /api/org/responsible?operation=`) and append-only ended rows view.
5. **R5-4: Targeted notifications and badge decoupling (commit `546bed4`)**:
   - Notifications page and panel with tabs: الكل · عام · مخصص (personal) · طلبات ومهام (`requiresAction`).
   - Separate badges for read state («مقروء» / «غير مقروء») vs decision state («تم البت» / «بانتظار البت»).
   - Direct count assignment prevents double-counting upon reconnect.
6. **R5-5: Live E2E test suite (commit `afd89aa`)**:
   - Added Step 14 to `web/tests/e2e-live.mjs`: admin org setup → supervisor cash request → negative check (outsider 403) → negative check (requester self-approval 403) → accountant propose bank → supervisor accept → accountant approve with 200 SAR installment → custodian payout with reference `SYN-TRF-LIVE-9` → verified debt with category `BankAdvance` and journal entry.
   - 10 screenshots generated in `test-results/r5-web/` at 1440 and 360 viewports.

### Verification summary
- **Static build:** `npm run build` compiled 19/19 routes cleanly without any `/app/app/` links.
- **Backend tests:** `dotnet build` succeeded with 0 warnings/errors; `node tests/advances.mjs` passed 37 HTTP checks.
- **Live E2E suite:** `node tests/e2e-live.mjs` passed all 14 steps cleanly against the real .NET API and browser.
- **Screenshots:** All 10 screenshots captured in `test-results/r5-web/` (at 1440px and 360px).

### Capacity status
- Before session: ~78% weekly limit used (~22% remaining).
- After session: ~88% weekly limit used (~12% remaining).

## advances API done (Claude, 2026-11-05)
- Branch `feat/advances-api` (worktree `gate-payroll-adv`), from `feat/r4-integrate` 55a97bc.
- 2fcf695 ADV-G1/G2: `OrgDirectory` (memberships with ranks A+/A/B/C, delegations with expiry, supervision, routing; dated, append-only), `Notifications.RaiseFor/CloseTasks` (personal tasks; read ≠ decided).
- 565885d ADV-A1..A4: `AdvanceRequests` (request → responsible review approve/reject/propose → supervisor respond → custodian payout → CashAdvance/BankAdvance debt + Dr 1210 / Cr vault journal; 409 when the vault is short). Summary card API. Permissions `advances.request|review|payout`, `org.manage`. `PayStatements.Build` takes an optional transaction.
- Tests: .NET 268 passed / 0 failed / 7 SQL skipped (4 new advance tests); `node tests/advances.mjs` 37 HTTP checks; source boundary 212; ENDPOINTS regenerated (227 routes). Docs: `docs/api/org.md`, `docs/api/advances.md`.
- Next: Antigravity `~/.agents/prompts/antigravity-R5-advances-web.md` (merge this branch into `feat/r4-integrate` after fix1).


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

## R5 Codex: mobile sessions + advances security review
| Field | Value |
|---|---|
| Current writer | none |
| Branch / worktree | feat/mobile-sessions / gate-payroll-mob; based on feat/advances-api 565885d |
| Updated | 2026-11-05 by Codex |
| Weekly usage | Before unavailable → after unavailable; usage screen not exposed |

## Goal completed
R5-C1 and R5-C2 completed locally. Next: Claude merges feat/mobile-sessions into the 0.14.0 release. No push, merge, deploy, production/real DB access, web/ edits or access to the concurrent gate-payroll-r4api worktree.

## Done
- R5-C1 commit 6971a0e: mobile.sessions permission/catalog; company-scoped list, own list, audited reason-required revoke/revoke-all and own logout; safe 16-hex session prefix and 8-hex FCM fingerprint; retained history; shared revoked/expired checks for portal/statement/broadcast/ack; five-minute lastSeen throttle with serialized revalidation. Contract: docs/api/mobile-sessions.md.
- R5-C2: fixed the two HIGH findings below; 6 additional Engine behavior tests cover supervision, personal privacy, payout replay/concurrency/overdraft, mass assignment and org write permissions. Existing maker-checker and routed vault custody remain enforced.
- Final verification: clean build (0 warnings/errors); .NET 280 passed / 0 failed / 7 isolated SQL checks skipped; HTTP advances 57, mobile-sessions 77, R4 backend 56 all passed. Source boundary 215 and git diff --check passed; inventory regenerated (232 total / 222 API). .NET also ran its existing SQLite HTTP contracts. No SQL Server or browser checks executed.
- C1 checkpoint: .NET 274/0/7 and HTTP 77/37/56 passed. Cached NuGet restore and cached TypeScript compiler generated Payroll.Api assets; no frontend source edits. The new HTTP suite uses an automatic local port after a fixed-port collision with R4 was corrected. Learning log appended.
- Inherited advances: dated org memberships/ranks/delegations/supervision/routing, personal tasks, request → review/propose → respond → payout/debt/journal (BR §25). See docs/api/advances.md and org.md.
- Inherited R4 integration: local 0.14.0. Reporting staging commit still needs fresh «حمّل»; production switch needs «حوّل الإنتاج». Neither action occurred here.

## Codex security review R5
Scoped review of AdvanceRequests.cs, OrgDirectory.cs, Notifications.RaiseFor/CloseTasks, new AccessPolicy lines and their reachable legacy notification paths; one fix round. No CRITICAL finding confirmed.
- HIGH fixed — FinancialCore.cs:714 and :754, Notifications.cs:26: legacy center/toggle exposed personal tasks to other approvers, creators and administrators. Fix: personal-recipient visibility on both paths. The HTTP regression failed against the original implementation, then passed; Engine and HTTP assert no cross-user disclosure and read ≠ decision.
- HIGH fixed — AdvanceRequests.cs:117 and :157: the original requester retained read/reply access after supervision ended. Fix: require current supervision plus requester identity/permission; routed reviewers/custodians also require their operation permission, summary requires advances.review. Tests deny the former and replacement supervisor; state remains unchanged.
- MEDIUM open — OrgDirectory.cs:71, :81, :87: supervision/routing accept end-before-start, and delegation ending lacks the same interval check. Fix proposed: require a nonempty start and end ≥ start, and validate delegation end against its start. Synthetic HTTP probe confirmed reversed routing dates are accepted. Not fixed per requested severity scope.
- MEDIUM open — AdvanceRequests.cs:60: last settlement is selected globally, including unrelated riders and unapproved legacy rows. It can hide this rider's advances from the reviewer. Fix proposed: select only this rider's approved run/cycle and its approval timestamp. Synthetic probe confirmed another rider's future run becomes the cutoff. Not fixed.
- MEDIUM open — AdvanceRequests.cs:66: Summary indexes scalar closing as closing.net; a routed reviewer with normal financial permissions receives 500. Fix proposed: read the numeric contract explicitly and distinguish an absent statement; review the forecast against already-applied deductions. Synthetic HTTP probe confirmed 500 for a FinancialAccountant reviewer. Not fixed.
- Verified: requester cannot review; payout needs the selected vault's current routing; client workflow/debt/revision fields are ignored or checked; org writes demand org.manage; personal tasks are absent from broadcasts. Independent SQLite Store instances prove one payout on replay and no overdraft across competing approved requests. Store.Write serializes balance/sourceRef/state/journal/audit together; SQL Server application-lock behavior was inspected only, not executed.

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
## Next step (exact)
Claude: merge feat/mobile-sessions into the 0.14.0 release in the integration worktree when authorized; decide the three MEDIUM items above with Ghazal. No further HIGH/CRITICAL fixes remain from this scoped review.
Antigravity: replace «غير متاح بعد» with the sessions list and reason-required revoke action. Use docs/api/mobile-sessions.md and only the returned prefix as the row identifier.

### How to verify
```bash
cd /home/ghazal/Projects/active/gate-payroll-r4api/web && npm run build && npm run test:e2e
cd /home/ghazal/Projects/active/gate-payroll-r4api && node tests/e2e-live.mjs
cd /home/ghazal/Projects/active/gate-payroll-r4api && node tests/browser-iban.mjs && node tests/finance-views-ui.mjs && node tests/r4-backend.mjs
# Clean worktrees first need cached NuGet restore and generated frontend assets.
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 -v minimal
node tests/advances.mjs
node tests/mobile-sessions.mjs
node tests/r4-backend.mjs
python3 tools/api_inventory.py
python3 scripts/release_source.py --check
git diff --check
```
