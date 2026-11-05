# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex — R6a ERP pulls, isolated worktree |
| Branch | feat/r6-erp-pulls, based on release/0.14.2 |
| Updated | 2026-11-05 |
| Weekly usage | Before unavailable; after not yet measured |

## Goal now
Execute ~/.agents/prompts/codex-R6a-erp-pulls.md. Add allowlisted operational/financial source facts, debts XLSX preview/import, counts-only sync runs, admin API and three-hour VPS timer documentation. Real ERP is SELECT-only; target is staging db77738 only. No staging writes until Ghazal says «حمّل»; debts additionally require approval of the mapping. No push/merge/deploy or changes to other worktrees.

## Current evidence
- R6a-1 implemented: allowlisted fuel cards, dated assignments and monthly usage; unmatched riders rejected; counts-only numeric totals. Build clean; focused ErpImport tests 11/11. Baseline .NET 281/0/7 (SQL skipped). Live fuel dry-run pending.
- Hostinger read-only VPS list returned zero instances; no server created or changed.
- Source schema metadata exported privately to ~/Private/gate-data/sync-logs/r6a-source-schema.json; no personal rows or secrets printed.
- Ghazal provided debts-2026-11.xlsx in ~/Private/gate-data/imports/. Real files are read in place only; private results must contain totals/counts/row numbers, never personal values.
- Inherited release/0.14.2 includes prior mobile sessions, advances fixes and rider asks (BR §25.8). Historical status is in git; the previous handoff was copied from the integration branch.

## Next step (exact)
Codex continues R6a-2..8 and synthetic tests; prepare reviewable mapping and counts-only dry-run. Request «حمّل» only after the concrete dry-run is ready.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1
```
