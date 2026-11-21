# Release 0.25.0 — rider app v2

Branch `release/0.25.0` (from `main` at `cc3cbb5`, release 0.24.0). Part: `feat/rider-app-v2` (merged no-ff, no conflicts).
Hard limits: no merge / deploy without Ghazal's «ادمج». Merge to `main` = production deploy. Never write ERP `db67927`. No secret values in this file.

## What changed for riders (mobile app)
| Area | Change |
|---|---|
| Languages | The app runs in Arabic, English or Urdu; the rider picks the language. |
| Short statement | A short pay statement per period in the rider's language (earnings, deductions, net), with debts and installments. |
| Notices | Notices from the office with read receipts: the office sees who opened each notice and when. |
| Requests | Two-way requests: the rider sends a request (annual leave, IBAN update, data update, statement, complaint, appointment) and the office replies in the same thread; the rider sees the status. Confidential complaints are visible only to holders of `mobile.complaints.confidential`. |
| IBAN / data updates | Two approvals by two different people (`riders.iban_data.approve_first`, then `riders.iban_data.approve_second`); nothing changes before the second approval. |
| Annual leave | An annual leave request form in the app; the office reviews it as a request. |
| Push | Push messages are sent in the rider's chosen language. |
| Rate limits | Per-IP limits on rider endpoints: 180 reads and 60 writes per minute by default (`RiderMobile:IpReadLimitPerMinute`, `RiderMobile:IpWriteLimitPerMinute`). A bad value logs a warning at start-up and falls back to the default; it does not stop the app. |

## What changed for the office (web)
| Area | Change |
|---|---|
| Forms builder / campaigns | Forms and campaigns can target all, riders or staff, and narrow by company, city or listed riders; unknown target values are rejected. |
| Statements | `POST /api/rider-mobile/statements/notify-period` (permission `mobile.broadcast`) tells riders that a period statement is ready. |
| Requests | `/api/rider-mobile/service-requests` list and reply, by permission (see `AccessPolicy.cs`). |

## Admin maintenance job: rider indexes (SQL Server)
- Start-up does **not** create any index or column on SQL Server. Without the job the app works; rider screens are only slower.
- The admin job adds 13 non-persisted computed columns (`rec_*`) on `dbo.records` and 15 filtered indexes, one at a time, and records each step in `ops.maintenance_step`.
- Run it **off-hours** only, as administrator: `GET /api/system/maintenance/rider-indexes` (status), then `POST` the same path (start). Only one run at a time; it refuses to start when free space is below `RiderIndexes:MinFreeDataMb` (512) / `RiderIndexes:MinFreeLogMb` (256). Command timeout: `RiderIndexes:CommandTimeoutSeconds` (3600).
- Note: the status `GET` creates the `ops` schema and the `ops.maintenance_step` table if missing (small, no data change).
- On SQLite (local/dev) the matching indexes are created at start-up as before.

## 1. Before go-live (ops, Ghazal)
1. Grant the three new permissions only to the intended reviewers: `mobile.complaints.confidential`, `riders.iban_data.approve_first`, `riders.iban_data.approve_second`. The first and second IBAN/data approval must be held by **two different people**. No blanket grant.
2. Check the admin riders screen for riders with **empty names** and fix them through the approved path (the mobile form rejects an empty name with 409).
3. Hosting settings (MonsterASP panel; names only): no new required key. Optional: `RiderMobile__IpReadLimitPerMinute`, `RiderMobile__IpWriteLimitPerMinute`, `RiderIndexes__CommandTimeoutSeconds`, `RiderIndexes__MinFreeDataMb`, `RiderIndexes__MinFreeLogMb` (defaults ship in `appsettings.json`).

## 2. Merge and deploy (Ghazal)
1. Write «ادمج». The planner pushes and merges the release to `main`; CI deploys over FTPS.
2. CI checks live `/health` = `0.25.0` with `dataPreservationVerified=true` and `releaseCheckpointPresent=true`.

## 3. After deploy
| Check | Expected |
|---|---|
| `/health` | `version=0.25.0`, `operationsSource=erp_live`, `erpReadonlyConfigured=true` |
| Rider app | login, language switch, statement, notices, requests, leave form |
| Office | requests list and reply; notice read receipts |
| Off-hours, admin | run the rider index job; status shows every index `done` |

## 4. Rollback
- Redeploy the last green 0.24.0 artifact (release checkpoint under `App_Data/release-backups`). Nothing is deleted.
- If the index job already ran, 0.24.0 ignores the extra `rec_*` columns, indexes and `ops.maintenance_step`; no data change is needed.
