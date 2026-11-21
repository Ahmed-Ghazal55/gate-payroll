# Release 0.33.0 (prepared 2026-11-21, NOT merged)

Additive master schemas only (branch `feat/schema-release` = `feat/w1-db-masters` W1 masters + M4f vault/settlement guards, on top of 0.32.0). Reviewed in DBS1, finished by Cursor (DBR1/DBR3), re-checked and prepared by Claude pro2 (DBS2).

**Merge only after Ghazal's live DB backup (section "Before merge").** The merge deploys automatically.

## What changed for users
- Nothing on screen. No endpoint, page, permission behaviour or money rule changes.
- The auth catalogue lists 3 new dual-approval permissions (`approvals.dual.bank_file`, `approvals.dual.payment_out`, `approvals.dual.shared_rider_release`). No code checks them yet; do not grant them now.

## Not in this release (parked on `feat/hs-wallet-account-move` @ 8ac24e1)
- `ConfigLists` v4 (`HsCustodyAdvance` / `HsInvoiceCloseAdvance`, `HsWalletTopUp` deactivated).
- `PipelineSettings` default `hsWalletAccount` 112513 (stays 112506).
- `FinancialCore` drop of `HsWalletTopUp`.
- `target-coa` 112513 meaning change and 5268xx accounts.
These wait for a read-only live count and Ghazal's yes (68 §4.3).

## What runs on first boot (one transaction + `sp_getapplock`)
`Store` constructor → `ReleaseSafety.Checkpoint` (new version → fresh `release-backups/0.33.0` records snapshot) → `SchemaSql.WithMigrationLock`:
1. `dbo.records` DDL, `ErpDataIndexes.Ensure`, `PayRunInputs.Ensure`: IF-missing only, same as 0.32.0 (no-op on live).
2. New masters, create-if-missing only, in this order: `SchemaRef`, `SchemaHrCompany`, `SchemaRef.EnsureLegacyAccountMapAfterCompany`, `SchemaOrgCostCentre`, `SchemaAuthCatalog`, `SchemaFormApp`, `SchemaFinAccount`, `SchemaFinPeriod`, `SchemaFinJournal`, `SchemaFinPeriodBalance`, `SchemaPayPayable`, `SchemaPayAllocation`, `SchemaSalInvoice`, `SchemaSalPayRun`, `SchemaOperationCapture`, `SchemaDebt`, `SchemaTreasury` (M4f vault net check), `SchemaBankFile` (inter-company settlement guards).
3. Each step: `CREATE SCHEMA` / `CREATE TABLE` / indexes / FKs / `CREATE OR ALTER` triggers on the new objects, then insert-if-missing seeds by code: `fin.account` (the current chart, same count as the accounting page; CoA file unchanged from 0.32.0), auth permission/role catalogue, Saudi banks, ERP event types, operation required kinds, one "none" cost centre.
- No statement reads or writes `dbo.records` rows, `pay.PayRunInput`, `ops.*`, `rpt.*` or Hangfire tables. No FK to them. `DROP`/`ALTER` statements target only the new tables.
- Any error rolls the whole transaction back; the host does not start (500.30).
- New tables: 67 by code count (`hr` 5, `ref` 6, `org` 1, `auth` 5, `form` 5, `app` 5, `fin` 10, `pay` 4 new, `sal` 14, `debt` 7, `treasury` 8). DBS1 estimated 68; the name check below is the real guard.

## Before merge (Ghazal, in the morning)
1. MonsterASP panel → Databases → the payroll DB → **full backup**; then **download** the `.bak` and keep it off the server.
2. Size check (read-only, in the panel SQL tool): `EXEC sp_spaceused;` Write down `database_size` and `unallocated space`. The cap is 2 GB. The new empty tables plus seeds add well under 5 MB; stop if the DB is already near the cap.
3. Optional: `SELECT @@VERSION, compatibility_level FROM sys.databases WHERE name = DB_NAME();` must be SQL Server 2017+ / level ≥ 140 (`CREATE OR ALTER`, `STRING_AGG … WITHIN GROUP`).
4. Turn stdout logging on for the first boot if the panel allows it (`web.config` has `stdoutLogEnabled="false"`).
5. Then merge the PR (squash) at a quiet hour and wait for the deploy run.

## Verify after first boot (read-only)
- `/health`: `version` 0.33.0, `ok` true, `dataPreservationVerified` true, `releaseCheckpointPresent` true.
- New tables, names only:
  ```sql
  SELECT s.name+'.'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE s.name IN ('hr','ref','org','auth','form','app','fin','sal','debt','treasury')
     OR (s.name='pay' AND t.name IN ('payable','payment_request','payment','payment_allocation'))
  ORDER BY 1;
  ```
  Expect 67 rows, all from the list above.
- `SELECT COUNT(*) FROM dbo.records;` = the count before the deploy (nothing else writes during the boot).
- Open payroll, debts, deduction types and accounting pages: same as before.
- Restart once from the panel: the second boot must be fast (no-op).

## Rollback
Redeploy 0.32.0 first: it ignores the new schemas and starts. Drop the new tables only with the app stopped, a fresh backup and Ghazal's yes (DBS1 §4 script; it must list only the new tables).

## Follow-ups (after release, before W2 writers)
- A1: every boot re-runs about 120 `CREATE OR ALTER TRIGGER` and the seed checks; skip unchanged ones (definition hash or schema version in `ref.setting`).
- A2: create-if-missing never alters an existing table; any later column change needs an explicit ALTER step and an upgrade test.
- A3: the CoA seed only inserts; later name/parent edits never reach `fin.account`.
- A4: `SchemaSql.cs:26` applock waits 60 s but the command timeout is the 30 s default; set `CommandTimeout=90`.
