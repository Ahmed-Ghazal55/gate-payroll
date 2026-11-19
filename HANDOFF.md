# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `chore/s1-remove-sync-hangfire` |
| Updated | 2026-11-19 by Cursor (S1F fix round done, not pushed) |
| Live | unchanged (no push/deploy) |
| Next | Planner/Ghazal review S1+S1F; do **not** drop live Hangfire tables until Ghazal says yes |

## Goal now
S1F review fixes on the S1 branch: pay-file imports page back, ImportPlan tests restored, role-editor/sync-baseline cleanup.

## Done (S1F)
- HIGH: `web/src/app/imports/page.tsx` catalog uploads; sidebar «استيراد الملفات»; payroll `import_operations` → `/app/imports`.
- MEDIUM: `tests/Engine.Tests/ImportPlanTests.cs` (6 assertions, no ImportExecutor).
- LOW: `erp.sync.run` hidden/deprecated in editor; sync-baseline out of contract/e2e; `/api/imports/*` keep note.
- Commits: `e407495`, `6da0233`, `ef2a5d2`. Report: `~/.cache/agents-runs/cursor-S1F-report.md`.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true
npm --prefix web run build
```

## Open for Ghazal
- Drop live `hangfire` schema/tables? (code already gone)
- Later: delete `/api/imports/*` free-upload with old Frontend
