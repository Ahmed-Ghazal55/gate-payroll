# Schema-only release draft (masters from `feat/w1-db-masters` on 0.31.0)

Draft for Ghazal. Not a deploy yes. Branch: `feat/schema-release`. Money behaviour change (HS wallet 112506→112513) is parked on `feat/hs-wallet-account-move` and is **not** in this release.

## Goal
First boot on live creates the additive master schemas/tables/triggers and insert-if-missing seeds only. Live money behaviour stays exactly as 0.31.0.

## Startup path (one transaction + applock)
`Store` ctor → `SchemaSql.WithMigrationLock` → in order:

1. `dbo.records` / SQLite `records` DDL — `IF OBJECT_ID IS NULL` / `CREATE TABLE IF NOT EXISTS` (same as 0.31.0; no-op on live).
2. `ErpDataIndexes.Ensure` — computed columns + indexes on `dbo.records` only when missing (same as 0.31.0; no row writes).
3. `PayRunInputs.Ensure` — `pay` schema + `pay.PayRunInput` only when missing (same as 0.31.0).
4. New masters (create-if-missing only): `SchemaRef`, `SchemaHrCompany`, `SchemaRef.EnsureLegacyAccountMapAfterCompany`, `SchemaOrgCostCentre`, `SchemaAuthCatalog`, `SchemaFormApp`, `SchemaFinAccount`, `SchemaFinPeriod`, `SchemaFinJournal`, `SchemaFinPeriodBalance`, `SchemaPayPayable`, `SchemaPayAllocation`, `SchemaSalInvoice`, `SchemaSalPayRun`, `SchemaOperationCapture`, `SchemaDebt`, `SchemaTreasury`, `SchemaBankFile`.

No `SchemaHrCompany.Ensure` outside this lock (`Companies.Seed` unchanged).

## What touches existing objects
| Statement class | Existing tables/data? |
|---|---|
| New schemas/tables/indexes/FKs/triggers | New objects only (`hr`,`ref`,`org`,`auth`,`form`,`app`,`fin`,`sal`,`debt`,`treasury`, and new `pay.*` masters). No FK to `dbo.*` / `pay.PayRunInput` / `ops.*` / `rpt.*`. |
| Seeds (`fin.account`, auth catalogue, banks, ERP event types, cost centre "none") | Insert-if-missing by code. CoA seed uses **0.31.0** `target-coa.v1.json` (no 112513 rename, no 5268xx). |
| `dbo.records` DDL / ERP computed columns / `pay.PayRunInput` | IF-missing only; identical intent to 0.31.0; no UPDATE/DELETE of row bodies. |
| Config / pipeline / FinancialCore categories | Unchanged from 0.31.0 (no v4 HS wallet migration). |

Grep confirmation: `Payroll.Api/Schema*.cs` have no `INSERT`/`UPDATE`/`DELETE` against `dbo.records`, `pay.PayRunInput`, `ops.*`, or `rpt.*`.

## Not in this release
- `ConfigLists` v4 (`HsCustodyAdvance` / `HsInvoiceCloseAdvance`, deactivate `HsWalletTopUp`)
- `PipelineSettings` default `hsWalletAccount` 112513
- `FinancialCore` drop of `HsWalletTopUp`
- `target-coa` 112513 meaning change and 5268xx
- Parked on `feat/hs-wallet-account-move` until a live data count and Ghazal's yes

## Verify after first boot (read-only)
- `/health` version + `dataPreservationVerified` + `releaseCheckpointPresent`
- New table count in the new schemas = 68
- `SELECT COUNT(*) FROM dbo.records` unchanged vs pre-deploy backup
- Second boot is a no-op (fast)

## Rollback
Redeploy 0.31.0 first (it starts with the new schemas present). Drop new tables only with a fresh backup, app stopped, and Ghazal's yes (see DBS1 §4).
