# HANDOFF

| Field | Value |
|---|---|
| Current writer | (free) |
| Branch | `fix/keeta-invoice-big-sheet` |
| Updated | 2026-11-27 by Cursor (KUP1d) |
| Live | unchanged (local commits only; never pushed) |
| Next | Planner/Grok re-check of KUP1d (company line in cycle base-gross). |

## Goal now
KUP1d done: cycle base-gross includes company lines with no platform account and no rider; ops import keeps those lines out of accounts; synthetic cycle test + 4 real-file Parse + draft Create.

## Done (KUP1d)
- `PayCycles.cs`: `amountExclVat` check = review accounts + `lineType=company` base gross.
- `OperationsImports.cs`: blank-account company lines with positive baseGross skipped from accounts (no platform account / rider required).
- Test: `KeetaPositiveCompanyLineCountsInCycleBaseGrossWithoutPlatformAccountOrRider`.
- Report: `~/.cache/agents-runs/cursor-KUP1d-report.md`.
- Never pushed.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj -p:SkipFrontendBuild=true
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~ImportsStreamingTests|FullyQualifiedName~KeetaInvoiceTests|FullyQualifiedName~OperationsImportTests"
```
