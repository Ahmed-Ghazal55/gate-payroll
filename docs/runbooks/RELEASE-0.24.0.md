# Release 0.24.0 — ERP sync removed, web review fixes

Branch `release/0.24.0` (from `main` at `7eabc08`). Parts: `chore/s1-remove-sync-hangfire` (W0, decisions M-0577, M-0590) and `fix/review-web-fixes` (W1), plus M-0005 (docs-only pushes do not deploy).
Hard limits: no merge / deploy without Ghazal's «ادمج». Merge to `main` = production deploy. Never write ERP `db67927`. No secret values in this file.

## What changed for users
| Area | Change |
|---|---|
| Import centre | Removed: the free-import tab, the ERP sync drawer and the "sync" buttons in the header, riders and operations screens. |
| Pay-file imports | `/imports` stays as a pay-file imports page (platform invoice files only; no free import, no sync). |
| IBAN and bank | One shared IBAN display: the full IBAN with the bank name checked from the IBAN's own bank code (forms review history, responses, rider statement, treasury). Treasury no longer shows the bank column twice. |
| Accounting | The sidebar `?tab=` links open the right tab; company names come from the companies API; internal rule numbers are hidden. |
| Debts | Only the open tab loads; statistics and badges show a dash until loaded; one pager over the full filtered list; refresh on the statements tab reloads the statement. |
| Labels | Arabic-only labels on the accounting and manual-journal screens. |
| Company pickers | Company dropdowns load from `GET /companies`; lookup matches the company code or the sponsor id. |
| Campaigns | The list shows a short link with a copy button. |
| Phone layout | The rider form fits a 360 px phone; the phone breakpoint is 480 px. |

## What was removed
- ERP sync: sync jobs, sync runs and requests, the `ErpImport` tool and its systemd timer, the local ERP copy path (`erp_copy`), `POST /api/riders/sync-baseline`, `/api/system/jobs/erp-sync/*`.
- Hangfire: packages, job schedule, job admission and job controls. `/health` no longer has a `jobs` field.
- The `erp.sync.run` permission stays in the catalogue as a hidden, deprecated alias, so stored users and roles that carry it still validate.

## What stays
- Live ERP reads (read-only login to `db67927`): operations screens, rider 360 accounts, cycle input capture, the admin sponsor map (`GET /api/system/erp-sponsor-map`).
- All payroll data. Nothing is deleted or migrated on start-up.

## CI
- A push to `main` that changes only `docs/**` or `*.md` files no longer runs the deploy workflow (M-0005). Pull requests and manual runs are unchanged.
- The deleted `web/tests/import-center.mjs` is no longer called by CI.

## 1. Hosting setting (Ghazal, MonsterASP panel; names only)
- No new setting. `ConnectionStrings__ErpReadonly` stays as in 0.21.0.
- Old `Jobs__*` / Hangfire keys may stay on the host: the app no longer reads them. Clean them later if wanted.
- Hangfire tables in the payroll SQL database are left in place. Nothing drops them. Dropping them needs Ghazal's yes.

## 2. Merge and deploy (Ghazal)
1. Write «ادمج». The planner pushes and merges the release to `main`; CI deploys over FTPS.
2. CI checks live `/health` = `0.24.0` with `dataPreservationVerified=true` and `releaseCheckpointPresent=true`.

## 3. After deploy (read only)
| Check | Expected |
|---|---|
| `/health` | `version=0.24.0`, `operationsSource=erp_live`, `erpReadonlyConfigured=true`, no `jobs` field |
| Header, riders, operations | no sync button or drawer |
| `/imports` | pay-file imports page opens |
| Debts, accounting, treasury, forms | the fixes in the table above |

## 4. Rollback
- Redeploy the last green 0.23.0 artifact (release checkpoint under `App_Data/release-backups`). Nothing is deleted.
- If the host still has Hangfire enabled, 0.23.0 uses its existing tables again; no data change is needed either way.
