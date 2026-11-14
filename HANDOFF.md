# HANDOFF

| Field | Value |
|---|---|
| Current writer | — |
| Branch | feat/run-input-snapshots |
| Updated | 2026-11-14 by Cursor (L2-fix after R) |
| Live | `/health` = **0.20.0** (this branch not deployed) |
| Tip | local L2-fix commit (not pushed) |

## Done this round (review R → L2-fix)
Seal **A0**: `PayRunInputs.CaptureAllowed` = not sealed and stage ≤ 1 (`PayRunInputs.cs`, `PayCycles.Capture.cs`, `PayCycles.View.cs`).
B2 cycle `CaptureScope` (`PayCycles.Capture.cs`). B3 pre-cycle live via `StagePreCycleKind` (`PayReviewDraft`, `ValidateGaps`, `PayReviewCategory`). B4 keyset `AfterId` + end recount (`LiveOperationsQuery.cs`, `StageKindKeyset`). B5 required kinds + multi-app union + missing staged kind 503. M1–M12 / m / D1 as coded; OpenAPI from `Operations.FieldMap` (`ApiContracts.cs`); r8 + `api-contract.ts` regenerated.

## Do not
- **L2 must not deploy before U2** (no screen calls capture).
- Do not write to ERP. Do not put secrets in git. Do not push/merge/deploy without Ghazal's yes.
- Local SQL `localhost,11433` was **not** available this run → SQL/FCM cases that need it failed/skipped; do not invent SA passwords.

## Verify locally
- `dotnet test tests/Engine.Tests` → 513 passed without SQL; SQL suite needs `PAYROLL_TEST_SQL_CONNECTION`.
- `node tests/r8-contract.mjs` passed after `--update-snapshot`.
- `npm run api:check` / `npm run build` in `web/` passed.
- `ops-live-ui.mjs` needs Playwright under `/tmp/gate-browser` (not installed here).
