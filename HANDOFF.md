# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) — next: Antigravity U1 |
| Branch | feat/live-ops-erp-reads (draft PR #36) |
| Updated | 2026-11-12 by Cursor (L1 paging) |
| Live | `/health` = **0.20.0** (this branch not deployed) |

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
1. **U1 — Antigravity:** `~/.agents/prompts/antigravity-U1-ops-live-ui.md`
2. **S1 — Claude:** security pass on PR #36 (stop here for S1)

## Do not
- Do not write to ERP. Do not put secrets in git.

## S1 security pass (Claude 2026-11-12) — done, one fix round
Report: `~/.agents/reviews/S1-pr36-security-claude.md`. Fixed: custody_handover company/rider scope (IDOR), vehicle_assignment vehicle-company check,
platform limit in SQL for every non-admin (correct totals), > 500 ids via one OPENJSON parameter, search typed + normalised Arabic. +5 tests.
Open: verify ERP compatibility level ≥ 130 for OPENJSON (read-only metadata); rider-accounts total vs items (low).
