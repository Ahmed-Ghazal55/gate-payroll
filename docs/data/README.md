# ERP → staging workflow

All reports contain metadata/counts only. Read `ERP-DATA-MAP.md`, `TARGET-KINDS.md` and `RECONCILE-2026-11-03.md` first. Source archives and the ERP checkout stay read-only. Nothing here changes Payroll production or deploys a branch.

Ghazal confirmed the FTP rotation, staging readiness and an off-hours live read on 2026-11-03. The source is the approved ERP read-only account from `~/Private/gate-data/erp-readonly.env`; the approved staging database is read from `~/Private/gate-data/staging.env`. Neither connection is printed or persisted by the tool. The latest prompt amendment makes a local backup restore optional; no Docker restore was needed for this run. A live-versus-backup comparison remains unverified.

## Build and inspect

From `gate-payroll-data`:

```bash
dotnet build tools/ErpImport/ErpImport.csproj --no-restore -p:SkipFrontendBuild=true -m:1
# Default is dry-run. Never pipe output to a file that could contain records.
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --dry-run
```

The API project expects its generated legacy assets. In a fresh checkout, build them with the pinned TypeScript compiler (the normal CI build installs the lockfile dependencies). `SkipFrontendBuild=true` is for a backend worktree where those generated assets already exist. It does not modify the UI sources. NuGet was restored from the local package cache with a single MSBuild worker; no source/feed change was needed.

The adapter selects explicit columns, one ordered pass per table. No credential versions, usernames/passwords, binary attachments or financial transaction tables are read. It captures the database rowversion before/after and refuses a changed read; it does not claim snapshot isolation or detect every possible hard delete. Reports record the actual read window and rejected counts. Non-rider employees, invalid identities and unknown enum codes are explicit reasons, not silently repaired data.

## Load approval

**Do not run `--commit` until Ghazal explicitly writes «حمّل» after reviewing the dry-run counts.** This approval has not yet been given. The command below is for that approved step only:

```bash
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --commit
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --dry-run
```

On commit the target Store upgrades its schema, checkpoints only beneath `~/Private/gate-data/erp/staging-import-runtime`, saves by stable key and writes normal audit entries as `erp-import`. It never deletes. SQL Server transport batches the normal Save/Audit documents in the same transaction, avoiding one network round-trip per record. The commit command also runs a SELECT-only second comparison against its exact in-memory source snapshot and reports `sameSnapshotVerification`; this must have zero new/changed rows. A failed write transaction rolls back records and audits. The next dry-run must report zero new/changed records for an unchanged source. If the live source changed meanwhile, report that separately rather than calling the extra changes an idempotency failure. Refresh RECONCILE with aggregate ERP/staging counts after loading; its current state is explicitly pre-load.

Current gaps: no authoritative sponsorship-period table; source commercial-registration numbers are missing; invalid rider identities are not imported. These gaps must be accepted or resolved before a complete staging dataset can be claimed. Existing target policy and verification fields are preserved; source-owned profile fields are refreshed. A changed natural identity or cross-kind ID collision stops for review.

## Ghazal's local review against staging

Only after the staging load is approved and complete, run the API locally using the staging env value. This code keeps the connection in process memory, does not echo it, and stores runtime files outside the checkout:

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

Review at `http://127.0.0.1:19480/app/`. Startup may seed the staging company catalog and schema; this is why it follows load approval. Do not save connection strings, API responses, logs or screenshots containing real records under this repo or `~/.agents`. New operational kinds are stored for later UI work; this importer does not claim to provide every operating-assets screen.

Production keeps its existing SQLite. Switching production storage, resetting balances, posting opening entries, publishing or merging are separate decisions.
