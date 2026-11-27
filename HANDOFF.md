# HANDOFF

| Field | Value |
|---|---|
| Current writer | (free) |
| Branch | `fix/keeta-invoice-big-sheet` |
| Updated | 2026-11-27 by Cursor (KUP1c) |
| Live | unchanged (local commits only; never pushed) |
| Next | Planner/Grok re-check of KUP1c (partner VAT + company/credit rows). |

## Goal now
KUP1c done: partner-sheet VAT + rider-due reconcile; positive blank company row; rider credit line; October fixture uses partner tax.

## Done (KUP1c)
- `KeetaInvoices.cs`: due-only layout → line VAT 0, header VAT = partner `مبلغ الضريبة`; rider due sum + tax = partner due (±0.01); positive `companyRows` invoiced/baseGross = due; negative account due allowed.
- Tests: October partner tax; positive company + rider credit; mismatch abort. Old net+inclusive layout unchanged.
- Full Parse on 4 real `#2026-10#` files: all OK, delta 0 (totals only in report).
- Report: `~/.cache/agents-runs/cursor-KUP1c-report.md`.
- Never pushed.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj -p:SkipFrontendBuild=true
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true --filter "FullyQualifiedName~ImportsStreamingTests|FullyQualifiedName~KeetaInvoiceTests"
```
