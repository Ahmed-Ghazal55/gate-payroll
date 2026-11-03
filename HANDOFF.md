# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/1.0-data from main 3582faf |
| Live | 0.10.0 (3582faf, 2026-11-03) |
| Updated | 2026-11-03 by Codex |

## Goal now
Prompt B: ERP data map and additive staging importer. Ghazal confirmed FTP rotation, staging readiness and off-hours live read. Source is approved live read-only SQL; backup restore is optional under the latest prompt amendment. Never load until the explicit «حمّل» after dry-run review.

## Done (latest first)
- Importer uses the normal Save/Audit path with optional same-transaction SQL Server bulk transport; one target-state recheck guards concurrent changes. Commit also verifies the exact in-memory source snapshot without rereading live data.
- Final checks: .NET 148 passed / 0 failed / 6 SQL skipped, including eight importer behavior tests. Build 0 warnings/errors; generator, source boundaries (148 files), whitespace and new-file sensitive-literal checks passed. SQL Server provider execution remains unverified.
- Read live schema metadata (224 tables), current ERP entities/configurations/controllers/pages and reference-only SQL/model history. docs/data/ERP-DATA-MAP.md covers 29 visible tables and the absent sponsorship-period table. Reports contain metadata/counts only.
- docs/data/TARGET-KINDS.md defines 12 kinds, source/natural keys, relations and existing-database indexes for SQLite/SQL Server; company reuses Prompt A's existing sponsor keys.
- Final dry-run: 6489 new documents, zero target writes; 432 sponsored + 1708 external riders. Stable database rowversion over the eight-second read. Counts/reasons/read window in docs/data/RECONCILE-2026-11-03.md.
- Explicit gaps: all three CR numbers missing; sponsorship periods absent; 14 invalid rider identities; 16 employees lack rider profiles; two custody events are not handover/return. No dates or classification guessed.
- Shared learning entry recorded. Source archives, production SQLite, ERP production write access and config files were untouched. No push, PR, merge or deployment.

## Next step (exact)
Claude reviews feat/1.0-data code; Ghazal reviews docs/data/RECONCILE-2026-11-03.md and decides «حمّل». Only then Codex loads staging and records actual counts plus a zero-change second comparison. Current staging is untouched; post-load reconciliation remains pending.

## Open problems / decisions
- Accept/resolve the explicit gaps before claiming a complete dataset. Source reads use a rowversion-stability check, not snapshot isolation; live-versus-backup drift is unverified because restore was not needed.
- SQL Server indexes are implemented but provider execution is not verified by the synthetic SQLite tests. Production storage remains unchanged.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 scripts/release_source.py --check
git diff --check
# Approved off-hours only; default is SELECT-only dry-run:
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --dry-run
```
