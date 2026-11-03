# ERP → staging workflow

All reports contain metadata/counts only. Read `ERP-DATA-MAP.md`, `TARGET-KINDS.md` and `RECONCILE-2026-11-03.md` first. Source archives and the ERP checkout stay read-only. Nothing here changes Payroll production or deploys a branch.

Ghazal confirmed the FTP rotation, staging readiness and an off-hours live read on 2026-11-03. The source is the approved ERP read-only account from `~/Private/gate-data/erp-readonly.env`; the approved staging database is read from `~/Private/gate-data/staging.env`. Neither connection is printed or persisted by the tool. The live read changed during the approved load attempt, so the tool refused it before any target writes. Existing live snapshot isolation was disabled and was not changed. The explicitly permitted backup fallback was restored locally into SQL Server 2025 and made READ_ONLY. Its 224 table names, column definitions, key columns and FKs match the prior live metadata; row counts differ. Staging now contains 6451 domain documents from that stable backup, with the exact drift recorded in RECONCILE.

## Build and inspect

From `gate-payroll-data`:

```bash
dotnet build tools/ErpImport/ErpImport.csproj --no-restore -p:SkipFrontendBuild=true -m:1
# Start the preserved local copy if it is stopped:
docker start gate-erp-copy-20261103
# A source change during a live read safely refuses the run; do not disable the guard.
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --local-copy --dry-run
```

The API project expects its generated legacy assets. In a fresh checkout, build them with the pinned TypeScript compiler (the normal CI build installs the lockfile dependencies). `SkipFrontendBuild=true` is for a backend worktree where those generated assets already exist. It does not modify the UI sources. NuGet was restored from the local package cache with a single MSBuild worker; no source/feed change was needed.

The adapter selects explicit columns, one ordered pass per table. No credential versions, usernames/passwords, binary attachments or financial transaction tables are read. It captures the database rowversion before/after and refuses a changed read; it does not claim snapshot isolation or detect every possible hard delete. Reports record the actual read window and rejected counts. Employees without a required account/vehicle profile, invalid identities and unknown enum codes have explicit rejection reasons. Administrative employees with such profiles are retained with their role, rather than silently counted as delivery riders.

## Load approval

**Ghazal explicitly approved «حمّل» for this run.** The backup load completed, and a separate second commit run returned zero new/changed rows. Following the explicit role-metadata correction, an independent final dry-run again returned zero new/changed rows. The following commands describe an approved replay against the preserved local source; a new load scope or production change still needs a separate instruction:

```bash
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --local-copy --commit
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --local-copy --dry-run
```

On commit the target Store upgrades its schema, checkpoints only beneath `~/Private/gate-data/erp/staging-import-runtime`, saves by stable key and writes normal audit entries as `erp-import`. It never deletes. SQL Server transport batches the normal Save/Audit documents in the same transaction, avoiding one network round-trip per record. The import-only target connection uses a bounded 120-second command timeout; SQL failures expose only a fixed phase code and number. The commit command also runs a SELECT-only second comparison against its exact in-memory source snapshot and reports `sameSnapshotVerification`; this must have zero new/changed rows. A failed write transaction rolls back records and audits. The next dry-run must report zero new/changed records for an unchanged source. If the live source changed meanwhile, report that separately rather than calling the extra changes an idempotency failure. RECONCILE now contains actual staging counts, audit/index counts, the second-run proof and the older-backup/live differences. A private SQL checkpoint was created under the private runtime directory; no record bodies were written in this checkout.

Administrative account/vehicle profiles are deliberately preserved as references, with personRole=administrative and isDeliveryRider=false; they are excluded from the rider directory/search/statistics/data-quality views. There are 2092 delivery riders and 38 such administrative profiles.

Current accepted load exclusions/gaps: no authoritative sponsorship-period table; source commercial-registration numbers are missing; invalid rider identities are not imported. These gaps must be accepted or resolved before a complete staging dataset can be claimed. Existing target policy and verification fields are preserved; source-owned profile fields are refreshed. A changed natural identity or cross-kind ID collision stops for review.

## Ghazal's local review against staging

The approved staging load is complete. Run the API locally using the staging env value. This code keeps the connection in process memory, does not echo it, and stores runtime files outside the checkout:

```bash
python3 - <<'PY'
import os
from pathlib import Path
path = Path.home() / 'Private/gate-data/staging.env'
entries = [line.strip().split('=', 1)[1].strip() for line in path.read_text().splitlines()
           if line.strip().startswith('STAGING_DB=')]
if len(entries) != 1:
    raise SystemExit('Invalid staging environment file')
connection = entries[0]
if len(connection) >= 2 and connection[0] == connection[-1] and connection[0] in ('"', "'"):
    connection = connection[1:-1]
env = dict(os.environ, ConnectionStrings__DefaultConnection=connection,
           PAYROLL_DATA_DIR=str(Path.home() / 'Private/gate-data/erp/staging-review-runtime'),
           ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS='http://127.0.0.1:19480')
os.execvpe('dotnet', ['dotnet', 'run', '--project', 'Payroll.Api', '--no-build'], env)
PY
```

Review at `http://127.0.0.1:19480/app/` (the old UI remains at `/`). This worktree has an ignored copy of the existing Next static export from the identical 0.10.0 web sources; no UI source was modified. In a fresh checkout build `web/` and run `python3 scripts/package_web.py` before reviewing `/app/`. Startup may seed the staging company catalog and schema; this is why it follows load approval. Do not save connection strings, API responses, logs or screenshots containing real records under this repo or `~/.agents`. New operational kinds are stored for later UI work; this importer does not claim to provide every operating-assets screen.

Production keeps its existing SQLite. Switching production storage, resetting balances, posting opening entries, publishing or merging are separate decisions.

The Docker image is `mcr.microsoft.com/mssql/server:2025-latest` (SQL major 17). The original backup was copied with docker cp and hash-verified; restore used FILELISTONLY/MOVE to local files, then ALTER DATABASE erp_copy SET READ_ONLY. The container is stopped after reconciliation to release memory; its private volume and backup remain intact. SA is loaded internally only from `~/Private/gate-data/mssql/.env`; `--local-copy` avoids putting it in command arguments.

## R2 company certificates (M1.2)

`POST /api/companies/import/preview` accepts `{companies:[...]}`. An administrator receives normalized `rows`, a process-signed `fingerprint` and counts per kind. Confirm with `POST /api/companies/import/commit` and `{rows,fingerprint,confirm:true}`. A changed company/registration state, actor or row invalidates the preview. Every changed document uses Store Save/Audit; an unchanged repeat creates no audit rows.

Rows map `code` to the existing BW/EX/TR sponsor key and upsert by `unifiedNumber`. Identity conflicts stop for review. Fields include the CR number/type/version, entity type/characteristic, Gregorian/Hijri issue and annual-confirmation dates, city/address/capital, and ISIC activities with an explicit incomplete flag. Other fields survive the merge. Expiry reminders prefer `annualConfirmation`, retaining the legacy expiry fallback.

Private file review, counts only (no record values in stdout or report):
```bash
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --companies-input "$HOME/Private/gate-data/companies-2026-11-04.json" --dry-run
```
The 2026-11-04 dry-run proposed 3 changed companies and 3 new registrations; zero DB writes. **A fresh «حمّل» approval for this certificate load is pending.** Only after that approval may the operator replace `--dry-run` with `--commit`. No production target is accepted.
