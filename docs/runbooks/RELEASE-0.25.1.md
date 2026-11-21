# Release 0.25.1 — debts list paged on the server

Branch `release/0.25.1` (from `main` at `b7d9070`, release 0.25.0). Part: `feat/debts-server-paging` (merged no-ff, no conflicts).
Hard limits: no merge / deploy without Ghazal's «ادمج». Merge to `main` = production deploy. Never write ERP `db67927`. No secret values in this file.

## What changed (office web, debts page)
| Area | Change |
|---|---|
| Paging | `GET /api/debts` filters, sorts and pages in SQL. With `page`/`pageSize` (max 200) it returns `{items,total,page,pageSize}`; without `page` it still returns the old JSON array, so other callers keep working. |
| Summary cards | New `GET /api/debts/summary` computes the cards with SQL aggregates. Cards follow the company/permission scope only; the status, category and search filters of the table do not change them (same as the old page). |
| Advances card | Now includes the `Advance` category with `CashAdvance` and `BankAdvance`. The old page missed `Advance`, so the card may show a **higher** number than 0.25.0. This is a fix, not new data. |
| Active card | Sums the remaining balance of `active` debts only, as the old page did (the server version keeps that rule). |
| Export | Export takes all rows that match the current filters (all pages), not only the visible page. |
| Category filter | The list comes from the finance categories catalog, limited to the user's category scope. |
| Search | Name/iqama search folds Arabic letter forms (أ إ آ ٱ → ا, ة → ه, ى/ئ → ي, ؤ → و), Arabic/Persian digits, harakat, tatweel and repeated spaces on the SQL side. Stale requests are cancelled while typing. |
| Table totals | Footer totals on server-paged tables are labelled "this page only". |
| Scope | Non-admin users see the same rows as before (category, sponsor aliases, platform). Admins see all. |

## 1. Before go-live (ops, Ghazal)
1. No new permission, no new setting, no schema change. Nothing to do on the hosting panel.
2. Tell the finance team that the advances card may be higher than in 0.25.0 (see the table above).

## 2. Merge and deploy (Ghazal)
1. Write «ادمج». The planner pushes and merges the release to `main`; CI deploys over FTPS.
2. CI checks live `/health` = `0.25.1` with `dataPreservationVerified=true` and `releaseCheckpointPresent=true`.

## 3. After deploy
| Check | Expected |
|---|---|
| `/health` | `version=0.25.1`, `operationsSource=erp_live`, `erpReadonlyConfigured=true` |
| Debts page | loads page 1 quickly; next/previous pages work; total count matches the cards' count |
| Filters | status, category, sponsor and search (try a name with and without hamza) |
| Export | row count = filtered total, not page size |
| Cards | active = active only; advances include `Advance` |

## 4. Rollback
- Redeploy the last green 0.25.0 artifact (release checkpoint under `App_Data/release-backups`). Nothing is deleted.
- 0.25.1 writes no new data and changes no schema, so no data step is needed on rollback.
