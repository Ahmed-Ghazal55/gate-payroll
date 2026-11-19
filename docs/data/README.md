# ERP → payroll database workflow

Current target: **قاعدة الرواتب db77738** (BR33). Ghazal alone runs credential-dependent commands. No payroll database writes before the reviewed **«انقل»** cutover; ERP first-kind loads still need fresh **«حمّل»** afterward. Follow [DB-CUTOVER](../ops/DB-CUTOVER.md). Legacy identifiers `staging.env` / `STAGING_DB` are retained for compatibility. Historical counts below are dated evidence, not a fresh preview.

All reports contain metadata/counts only. Read `ERP-DATA-MAP.md`, `TARGET-KINDS.md` and `RECONCILE-2026-11-03.md` first. Source archives and the ERP checkout stay read-only. Nothing here changes Payroll production or deploys a branch.

Ghazal confirmed the FTP rotation, payroll database readiness and an off-hours live read on 2026-11-03. The source is the approved ERP read-only account from `~/Private/gate-data/erp-readonly.env`; the approved payroll database is read from `~/Private/gate-data/staging.env`. Neither connection is printed or persisted by the tool. The live read changed during the approved load attempt, so the tool refused it before any target writes. Existing live snapshot isolation was disabled and was not changed. The explicitly permitted backup fallback was restored locally into SQL Server 2025 and made READ_ONLY. Its 224 table names, column definitions, key columns and FKs match the prior live metadata; row counts differ. payroll database now contains 6451 domain documents from that stable backup, with the exact drift recorded in RECONCILE.

## Sync / copy status (S1)

ERP mirror sync, `erp_copy` local loads, Hangfire jobs, and `tools/ErpImport` were **removed**. Use live read-only ERP (`ConnectionStrings:ErpReadonly`) and pay-cycle capture snapshots. Historical load evidence below remains for audit; do not replay the deleted CLI.

Administrative account/vehicle profiles are deliberately preserved as references, with personRole=administrative and isDeliveryRider=false; they are excluded from the rider directory/search/statistics/data-quality views. There are 2092 delivery riders and 38 such administrative profiles.

Current accepted load exclusions/gaps: no authoritative sponsorship-period table; source commercial-registration numbers are missing; invalid rider identities are not imported. These gaps must be accepted or resolved before a complete payroll database dataset can be claimed. Existing target policy and verification fields are preserved; source-owned profile fields are refreshed. A changed natural identity or cross-kind ID collision stops for review.

## Ghazal's local review against payroll database

The approved payroll database load is complete. Run the API locally using the payroll database env value. This code keeps the connection in process memory, does not echo it, and stores runtime files outside the checkout:

```bash
python3 - <<'PY'
import os
from pathlib import Path
path = Path.home() / 'Private/gate-data/staging.env'
entries = [line.strip().split('=', 1)[1].strip() for line in path.read_text().splitlines()
           if line.strip().startswith('STAGING_DB=')]
if len(entries) != 1:
    raise SystemExit('Invalid payroll database environment file')
connection = entries[0]
if len(connection) >= 2 and connection[0] == connection[-1] and connection[0] in ('"', "'"):
    connection = connection[1:-1]
env = dict(os.environ, ConnectionStrings__DefaultConnection=connection,
           PAYROLL_DATA_DIR=str(Path.home() / 'Private/gate-data/erp/staging-review-runtime'),
           ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS='http://127.0.0.1:19480')
os.execvpe('dotnet', ['dotnet', 'run', '--project', 'Payroll.Api', '--no-build'], env)
PY
```

Review at `http://127.0.0.1:19480/app/` (the old UI remains at `/`). This worktree has an ignored copy of the existing Next static export from the identical 0.10.0 web sources; no UI source was modified. In a fresh checkout build `web/` and run `python3 scripts/package_web.py` before reviewing `/app/`. Startup may seed the payroll database company catalog and schema; this is why it follows load approval. Do not save connection strings, API responses, logs or screenshots containing real records under this repo or `~/.agents`. New operational kinds are stored for later UI work; this importer does not claim to provide every operating-assets screen.

Production keeps its existing SQLite. Switching production storage, resetting balances, posting opening entries, publishing or merging are separate decisions.

The historical Docker `erp_copy` image/volume workflow is retired with S1 (local ERP copy removed from the machine).

## R2 company certificates (M1.2)

`POST /api/companies/import/preview` accepts `{companies:[...]}`. An administrator receives normalized `rows`, a process-signed `fingerprint` and counts per kind. Confirm with `POST /api/companies/import/commit` and `{rows,fingerprint,confirm:true}`. A changed company/registration state, actor or row invalidates the preview. Every changed document uses Store Save/Audit; an unchanged repeat creates no audit rows.

Rows map `code` to the existing BW/EX/TR sponsor key and upsert by `unifiedNumber`. Identity conflicts stop for review. Fields include the CR number/type/version, entity type/characteristic, Gregorian/Hijri issue and annual-confirmation dates, city/address/capital, and ISIC activities with an explicit incomplete flag. Other fields survive the merge. Expiry reminders prefer `annualConfirmation`, retaining the legacy expiry fallback.

Company certificate preview/commit uses `POST /api/companies/import/preview|commit` (administrator). The old ErpImport `--companies-input` CLI path is gone with S1.
