# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/1.0-data from main 3582faf |
| Live | 0.10.0 (3582faf, 2026-11-03) |
| Updated | 2026-11-03 by Codex |

## Goal now
Prompt B completed: ERP map, additive staging importer and approved staging load. Ghazal confirmed prerequisites, off-hours read-only access and explicitly approved «حمّل». Production storage/deployment remains unchanged.

## Done (latest first)
- Final independent dry-run: 0 new, 0 changed, 6451 unchanged after role correction. The separate second commit run and automatic same-snapshot checks also returned zero changes.
- Actual staging: 6451 domain documents, 8581 erp-import audit rows, eight ERP indexes. People/profile records: 2092 delivery riders (394 sponsored, 1698 external) plus 38 administrative references, excluded from rider lists/search/stats/quality views.
- Live read changed during the first commit attempt, safely refused before writes. Restored the authorized backup locally into SQL Server 2025, verified original bytes, set erp_copy READ_ONLY, then loaded its stable snapshot. The preserved container is stopped after reconciliation.
- docs/data/ERP-DATA-MAP.md combines 224-table metadata with 29 scoped tables and code/routes/pages. TARGET-KINDS.md defines 12 kinds and source keys; SQLite/SQL Server indexes apply to existing stores.
- tools/ErpImport defaults to SELECT-only dry-run. Approved commit uses normal Store Save/Audit with atomic buffered transport, concurrent-target checks, private checkpoints, no deletion and bounded import-target SQL timeout.
- Verified build: zero warnings/errors; .NET 149 passed / 0 failed / 6 optional SQL-backed cases skipped; nine synthetic importer tests included. Standalone synthetic HTTP: 255 checks. Source boundaries: 149 files; whitespace and new/added sensitive-literal checks passed.
- docs/data/README.md explains the preserved local source and localhost API review against staging. Existing static web assets copied into ignored output; no UI source edits. Shared learning entry recorded.
- Earlier commits: b989fc0 (map/kinds), 6c51ab1 (importer/tests). Final branch commit records backup load, classification and reconciliation. No push, PR, merge or deployment.

## Next step (exact)
Claude reviews feat/1.0-data and docs/data/RECONCILE-2026-11-03.md. Ghazal can use docs/data/README.md to run the local API against staging. Accept/resolve source gaps and the older-backup drift before claiming complete current data or considering production migration.

## Open problems / decisions
- Backup loaded 38 fewer documents than the prior live dry-run: 10 people/profiles, 10 vehicle assignments, 18 custody events. Nested history also differs; exact counts are documented. No schema column/key/FK drift across the 224 tables; row counts changed.
- Missing three CR numbers and sponsorship-period source table; 14 invalid identities and 16 employees without required profiles excluded; two non-custody events excluded. No dates/values invented. ERP production and Payroll production SQLite were never written.
- Six optional SQL-backed suite cases were skipped; actual approved importer execution on staging was verified separately. Production remains SQLite.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
node tests/api-cleanup.mjs
python3 scripts/release_source.py --check
git diff --check
# Preserved local-copy start and SELECT-only comparison: see docs/data/README.md.
```
