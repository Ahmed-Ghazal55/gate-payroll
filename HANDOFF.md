# HANDOFF

| Field | Value |
|---|---|
| Current writer | (free) |
| Branch | `fix/keeta-invoice-big-sheet` |
| Updated | 2026-11-27 by Cursor (KUP1) |
| Live | unchanged (local commit only; never pushed) |
| Next | Planner: review KUP1, then release/PR when Ghazal asks. Note: real Keeta October headers use `معرف الشريك` / no bare `الإجمالي` — size fix unblocks upload; column match may still fail until a follow-up. |

## Goal now
KUP1 done: stream XLSX sheets (XmlReader + shared strings), raise per-sheet uncompressed cap to 250 MB, batch Keeta invoice line writes, synthetic large-sheet tests, timed the 4 real `#2026-10#` invoices (read-only).

## Done (KUP1)
- `Payroll.Api/Imports.cs`: stream sheets/shared strings; caps 250 MB sheet / 400 MB workbook / 120k rows / 12 MB zip; Arabic oversize message.
- `TableExcelApi.cs`: formula scan streams (no XDocument); same sheet cap.
- `KeetaInvoices.cs`: `BufferedWrites` for lines; formula-cache via `Imports.MissingFormulaCacheRows`; upload limit 12 MB.
- Tests: `ImportsStreamingTests` + existing `KeetaInvoiceTests` (9 passed).
- Report: `~/.cache/agents-runs/cursor-KUP1-report.md`.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj -p:SkipFrontendBuild=true
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~ImportsStreamingTests|FullyQualifiedName~KeetaInvoiceTests"
```
