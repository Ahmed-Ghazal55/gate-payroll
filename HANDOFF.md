# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) — next: Cursor L2 |
| Branch | feat/live-ops-erp-reads (draft PR #36) |
| Updated | 2026-11-13 by Antigravity (U1 live ops UI) |
| Live | `/health` = **0.20.0** (this branch not deployed) |

## U1 done (Antigravity)
- Operations screens (`web/src/app/operations/page.tsx`):
  - Server paging and search: sends `page`, `pageSize` (25/50/100/200) and `q` to `/api/operations/{kind}`. 300ms debounce on search input. Total from response. Server-side pagination via `DataTable` (`serverSide={true}`). Never pages or filters on client.
  - Source badge (`web/src/components/ui/SourceBadge.tsx`): shows «مباشر من نظام الموارد — آخر قراءة <asOf بتوقيت الرياض>» when `source === "erp_live"`, and «بيانات تجريبية محلية» when `source === "store"`.
  - Read-only rows: `allowedActions` empty hides edit/import/delete buttons. Link to Import Center (`/imports/`) preserved.
  - 503 handling: clear Arabic card «مصدر التشغيل غير متاح الآن» with retry button; keeps last page shown when data was already loaded. 400 shows API message. Skeleton rows while loading.
  - Stat cards: uses factual `total` and active-tab metrics only; removed fake counts for inactive tabs.
- Rider accounts panel (`web/src/components/riders/RiderAccountsPanel.tsx`):
  - Integrated into `/app/riders/` (Profile 360 "accounts" tab) and `/app/riders/statement/`.
  - Paged reads against `/api/riders/{iqama}/accounts` (25/50/100/200), source badge, 503 card with retry, and read-only rows.
- Tests: verified via `web/tests/ops-live-ui.mjs` (all tabs, search, pagination, 360px mobile viewport, 503 error & retry), `web/tests/e2e.mjs`, `web/tests/ui-qa.mjs`, `web/tests/r8-ninja-flow.mjs`, `web/tests/import-center.mjs`, `web/tests/r8-keeta-flow.mjs`, `web/tests/r9-pay-settings-flow.mjs`, `tests/browser.mjs`.

## L1 done (Cursor)
- `Payroll.Sync/LiveOperationsQuery.cs`: per-kind allowlist, `COUNT_BIG(*)` + `ORDER BY Id OFFSET/FETCH`, parents by page keys (batches ≤500), `CommandTimeout` 15s.
- Scope in SQL: company via `Sponsors.CommercialRegistrationNumber`, rider via employee/profile ids, housing via EXISTS residences, platform_account via `ClientPlatforms.Code`.
- `q`/`search`: `TextSearch.Normalize` on input only; ERP columns use CI collation (Arabic folding is input-only — note).
- Live sort: `id` / `-id` only. `pageSize` max 200.
- Rider accounts: filter by that rider’s ERP employee/profile keys (`RiderAccountEmployeeId` / profiles).
- API no longer calls `LiveOperationsSource.Read` (full tables). OpenAPI + `web/src/lib/api-contract.ts` regenerated (`source`,`asOf`,`allowedActions`).
- Tests: `LiveOperationsQueryTests` + prior live/ops tests. Local SQL 11433 fixtures still need the CI SQL host (same as C0).

### Live ERP page-1 timings (readonly, counts only, 2026-11-12)
| kind | ms | total | page_rows |
|---|---:|---:|---:|
| city | 1148 | 5 | 5 |
| housing | 1980 | 7 | 7 |
| housing_residence | 1395 | 212 | 50 |
| vehicle | 1494 | 558 | 50 |
| vehicle_assignment | 1602 | 881 | 50 |
| custody_handover | 1858 | 1019 | 50 |
| sponsorship_period | 0 | 0 | 0 |
| legal_case | 1699 | 13 | 13 |
| platform_account | 1929 | 1948 | 50 |
| vehicle_operational_status | 1250 | 3023 | 50 |
| platform_day_usage | 1815 | 1871 | 50 |
| rider_work_timeline | 1837 | 1372 | 50 |
| custody_equipment | 1247 | 0 | 0 |
| vehicle_expense | 2209 | 1737 | 50 |
| chefz_account | 1511 | 464 | 50 |
| chefz_account_type_change | 1512 | 48 | 48 |
| chefz_daily | 1970 | 6224 | 50 |
| chefz_settlement | 1994 | 496 | 50 |
| chefz_order_row | 1562 | 9574 | 50 |
| ninja_daily | 1675 | 3629 | 50 |
| ninja_daily_deduction | 1461 | 10 | 10 |
| ninja_evaluation_snapshot | 915 | 0 | 0 |
| jahez_commission_policy | 1374 | 0 | 0 |
| jahez_daily_order | 1668 | 6172 | 50 |

Target &lt;2s mostly met; `vehicle_expense` ~2.2s on cold path (loads sponsors + vehicles for the page).

## CI
- L1 push `fb595e0`: run [43274331025](https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/43274331025) **green** (validate 9m28s). Deploy steps skipped (draft PR).

## Next step (exact)
1. **L2 — Cursor:** pay_run_input table + capture_inputs action + RunInputs.List readers swap; design-review (EGM) first.
2. **U2 — Antigravity:** cycle screen capture inputs button, captured-at, rows per kind (after L2 is pushed and green).

## Do not
- Do not write to ERP. Do not put secrets in git.

## S1 security pass (Claude 2026-11-12) — done, one fix round
Report: `~/.agents/reviews/S1-pr36-security-claude.md`. Fixed: custody_handover company/rider scope (IDOR), vehicle_assignment vehicle-company check,
platform limit in SQL for every non-admin (correct totals), > 500 ids via one OPENJSON parameter, search typed + normalised Arabic. +5 tests.
Open: verify ERP compatibility level ≥ 130 for OPENJSON (read-only metadata); rider-accounts total vs items (low).
