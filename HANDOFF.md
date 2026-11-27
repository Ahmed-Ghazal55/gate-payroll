# HANDOFF

| Field | Value |
|---|---|
| Current writer | (free) |
| Branch | `fix/keeta-invoice-big-sheet` |
| Updated | 2026-11-27 by Cursor (KUP1b) |
| Live | unchanged (local commits only; never pushed) |
| Next | Planner/Grok re-check; two real October files still abort Parse on credit-note / positive company-adjustment rows (see report). |

## Goal now
KUP1b done: Grok formula-cache + counting-stream fixes; October Keeta header aliases + header-row find; dash blanks; currency money; company-row net-only.

## Done (KUP1b)
- `Imports.cs`: no `Skip` on `<f>`; `LimitSheetBytes` / counting stream on all four sheet reads.
- `KeetaInvoices.cs`: aliases (`معرف سائق التوصيل`, due-only net=due), header-row scan, currency/`-` money, company adjustment ignores component cols.
- Tests: 12 passed (`ImportsStreamingTests` + `KeetaInvoiceTests`).
- Report: `~/.cache/agents-runs/cursor-KUP1b-report.md`.
- Commits local: `38bfee7a` (KUP1), `a1fddd60` (KUP1b). Never pushed.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj -p:SkipFrontendBuild=true
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~ImportsStreamingTests|FullyQualifiedName~KeetaInvoiceTests"
```
