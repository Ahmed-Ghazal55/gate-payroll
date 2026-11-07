# SQLite → payroll database db77738

**Operator: Ghazal. No live command below was run by Codex. No database writes before «انقل».**
ERP db67927 stays SELECT-only and is never a cutover target. `db77738` is the payroll database (BR33).
This is a maintenance-window cutover, not a deployment authorization. Review/merge/release this tooling separately before using it on the site. Keep Hangfire disabled during the window.

## Merge rules and evidence

`StorageTransfer.PlanMerge` is explicit mode `erp-hr-sqlite-payroll-v1`:

- The union retains target-only and SQLite-only records. Same-id/different-kind and case-only identities are refused.
- Classified identity/HR fields (including name, sponsor, status and vehicle identity) use the existing ERP target value.
- IBAN choice and classified payroll fields use SQLite. Listed financial/payroll kinds (users, settings, debts, journals, cycles, ledger, forms, policies and invoices) use the complete SQLite body; both originals are archived. Other mixed records retain target-only fields.
- Unclassified conflicting fields are **unresolved**, with hashed IDs and a reason only. No force/overwrite flag exists. Ghazal resolves the policy/source evidence, then regenerates the preview. The whole import refuses any unresolved conflict.
- Each changed existing document records `mergedFrom` with both original fingerprints. Both entire originals are archived as `cutover_origin` records **in the same transaction**; the private plan also retains both full source snapshots. New records also retain their source in the archive. No records are deleted.
- Cross-record username, rider identity, period/rider and campaign/rider collisions refuse the plan. Logical relation remapping is never guessed.
- The reviewed plan fingerprint binds the complete source, target baseline, policy and output. The import takes the normal SQL application lock and rechecks the complete baseline in a serializable transaction. A changed target requires a new preview. Verification failure rolls everything back.
- `cutover_receipt` supports a no-write replay of the exact successful plan. A later change to migrated output refuses replay. Verification checks every expected record fingerprint, all retained target records and the receipt; additional archive/receipt records are expected.
- Plans contain private data: mode600, directories700, outside all repos. Print only `summary`: kind counts, added/merged counts, hashed collision IDs and fixed refusal reasons. Do not share plan bodies.

## Ghazal: prepare tooling locally (no database access)

```bash
cd ~/Projects/active/gate-payroll-r6b
dotnet restore tools/StoreMigration/StoreMigration.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1
dotnet build tools/StoreMigration/StoreMigration.csproj --no-restore -p:SkipFrontendBuild=true -m:1
umask 077
mkdir -p "$HOME/Private/gate-data/cutover"
chmod 700 "$HOME/Private/gate-data/cutover"
```

Ghazal supplies credentials to **his own shell** without putting values in command history. No agent reads them. Use the same payroll FTPS account as approved deployment; the SQL value must target db77738. Rotation required by the security policy must already be complete.

```bash
for cutover_var in MONSTERASP_FTP_SERVER MONSTERASP_FTP_USERNAME MONSTERASP_FTP_PASSWORD PAYROLL_MIGRATION_SQL; do
  read -r -s -p "$cutover_var: " "$cutover_var"
  export "$cutover_var"
  echo
done
export PAYROLL_CUTOVER_DIR="$HOME/Private/gate-data/cutover"
```

## Ghazal: freeze and take the snapshot (remote writes require his maintenance action)

First pause ERP/background sync and all API workers against db77738. Keep both source and target frozen until verification/switch completes. Inform users of the maintenance window.

```bash
python3 scripts/monsterasp_ftps.py prepare
python3 scripts/monsterasp_ftps.py download-data
```

`prepare` owns `app_offline.htm`, waits for IIS shutdown and records protected runtime metadata. Stop if shutdown is not confirmed or other writers exist. **Do not run `finish` yet.** `download-data` is remote read-only and requires maintenance already in place. It downloads only payroll.db and optional WAL/SHM to a new private directory, folds committed WAL pages using SQLite backup, checks integrity and rechecks stable remote metadata. Raw downloads remain private. It never downloads configs or secrets. Record the printed local snapshot path.

```bash
read -r -p 'Printed private snapshot path: ' cutover_snapshot
cutover_run="$(dirname "$cutover_snapshot")"
dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll snapshot "$cutover_snapshot" "$cutover_run/source.json"
dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-preview "$cutover_run/source.json" "$cutover_run/plan.json"
```

`merge-preview` opens an ordinary SQL connection and SELECT transaction, **never Store initialization, DDL, telemetry, checkpoint or business writes**. It prints inventories, hashed collisions, unresolved conflicts and `planFingerprint`. Keep the full private plan as the SQL target baseline backup. Inspect `ready=true`; if false, stop and resolve before asking «انقل». New attempts need a new output filename; files are never overwritten. Do not paste IDs/bodies/credentials into chat.

## Ghazal: only after saying «انقل» for this reviewed plan

```bash
read -r -p 'Reviewed planFingerprint: ' cutover_fingerprint
dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-import "$cutover_run/plan.json" "$cutover_fingerprint" --approved
dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-verify "$cutover_run/plan.json" "$cutover_fingerprint"
```

Require `verified=true`, `missing=0`, `changed=0`, `receiptPresent=true`. The CLI accepts only db77738 or isolated loopback GateCutoverTest_* test databases, and suppresses private exception text. No command targets ERP. On any failure keep maintenance; do not switch or retry blindly. Check the receipt with `merge-verify` if the connection broke after commit.

Then **Ghazal**, MonsterASP → Scripting → Environment variables:

1. Set `ConnectionStrings__DefaultConnection` to the reviewed payroll SQL value and, if enabling jobs later, `ConnectionStrings__Jobs` to **the same server and db77738**. Never paste values into tickets/commits. Keep `Jobs__Enabled=false` during verification. Provision a new random32–256character `Vouchers__Secret` in server environment settings before enabling financial writes; no agent reads it.
2. Keep `App_Data`, Data Protection keys, SQLite, WAL/SHM and local settings untouched. The original SQLite file is the rollback source.
3. Restart the site in the panel, then remove only the owned maintenance marker and check health:

```bash
python3 scripts/monsterasp_ftps.py finish
python3 scripts/monsterasp_ftps.py health
unset MONSTERASP_FTP_SERVER MONSTERASP_FTP_USERNAME MONSTERASP_FTP_PASSWORD PAYROLL_MIGRATION_SQL
```

`finish` requires unchanged protected runtime metadata; if the host changed these during environment configuration, keep maintenance and inspect manually. Never bypass the check automatically. `/health` must show `ok=true`, version0.15.0, `dataPreservationVerified=true`, `releaseCheckpointPresent=true`. These flags verify startup preservation; the migration's separate `merge-verify` proves cutover contents. Perform an authenticated counts-only SQL release check to confirm provider=mssql and expected inventory. Do not accept a healthy SQLite fallback as cutover success.

Only after successful cutover and first-kind acceptance may Ghazal enable Hangfire per [HANGFIRE.md](HANGFIRE.md). The db77738 guard is unchanged. A new ERP first load still needs fresh «حمّل»; this task's stricter no-payroll-write-before-«انقل» remains binding.

## Rollback

Keep the site offline. Disable jobs. Ghazal removes/restores DefaultConnection to its previous SQLite setting and restarts against the **untouched** SQLite. Verify provider/counts and health before resuming. Never delete SQL records or rerun an empty-target import. If SQL accepted user writes after reopening, freeze both sides and reconcile those writes before reverting: pointing back alone would lose post-cutover work. Retain the plan, raw SQLite snapshot, SQL archives and receipt.

## Local proof

`StorageCutoverTests` exercises the records schema from Store on isolated Docker SQL2022 with synthetic data: HR/payroll precedence, archives, atomicity, tampering, stale baseline, uniqueness, exact-repeat idempotency, reporting consistency, startup checkpoint and preservation without a SQLite file. FTPS fakes cover maintenance ownership, no remote write on download, WAL folding, metadata drift and SQL-only deployments. No live inventory/preview/import was performed by Codex.
