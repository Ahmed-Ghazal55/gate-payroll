# HANDOFF

| Field | Value |
|---|---|
| Current writer | none — next: Ghazal decides the B3 leftover, then Antigravity U2 |
| Branch | feat/run-input-snapshots |
| Updated | 2026-11-14 by Claude (blocker check after L2-fix) |
| Live | `/health` = **0.20.0** (this branch not deployed) |
| Tip | `feat/run-input-snapshots` pushed (Ghazal allowed branch push 11-14); CI by `workflow_dispatch` |

## Done this round (review R → L2-fix)
Seal **A0**: `PayRunInputs.CaptureAllowed` = not sealed and stage ≤ 1 (`PayRunInputs.cs`, `PayCycles.Capture.cs`, `PayCycles.View.cs`).
B2 cycle `CaptureScope` (`PayCycles.Capture.cs`). B3 pre-cycle live via `StagePreCycleKind` (`PayReviewDraft`, `ValidateGaps`, `PayReviewCategory`). B4 keyset `AfterId` + end recount (`LiveOperationsQuery.cs`, `StageKindKeyset`). B5 required kinds + multi-app union + missing staged kind 503. M1–M12 / m / D1 as coded; OpenAPI from `Operations.FieldMap` (`ApiContracts.cs`); r8 + `api-contract.ts` regenerated.

## Claude blocker check after L2-fix (2026-11-14)
- Local engine tests **with SQL** (`gate-sql-test`): **536/536 passed, 0 skipped**.
- B1 ✅ `PayRunInputs.CaptureAllowed` (stage ≤ 1) checked before any fingerprint work (`PayCycles.Capture.cs:79`). B2 ✅ live capture scope from the cycle (`CaptureScope`, `PayCycles.Capture.cs:195`). B4 ✅ keyset `AfterId` + end recount → 503 (`PayCycles.Capture.cs:229-256`). B5 ✅ required kinds → 409 in live mode; housing may be 0 (riders without housing).
- **B3 partly open (money):** `PayReviewDraft.cs:92` calls `PayReviewCategory.Derive(c,tx,personRow,period)` **without live rows**, so in live mode the pre-cycle category reads the empty Store mirror → every rider gets `*_private` → no vehicle rent proposed. Also the draft's `liveOps` is `_=>staged` (ninja_daily for every kind), so it can't be passed as is. Fix: stage `vehicle_assignment` + `vehicle` for the draft too and pass a kind-aware reader to `Derive`. Waits for Ghazal (rule 4: after the fix round, leftovers go to him).
- Minor: store mode `vehicle_assignment`/`housing_residence` take all rows when the cycle has no riders yet (`PayCycles.Capture.cs:97-98`); live mode is company-scoped in SQL.
- CI run 43536930320 **green** on `feat/run-input-snapshots`.
- CI fixes (tests only): `r8-ninja-flow.mjs` waits for the create-cycle POST; `r9-pay-settings-flow.mjs` captures through the API, then opens the cycle tab (until U2 adds the button).

## Do not
- **L2 must not deploy before U2** (no screen calls capture).
- Do not write to ERP. Do not put secrets in git. Do not push/merge/deploy without Ghazal's yes.
- Local SQL `localhost,11433` was **not** available this run → SQL/FCM cases that need it failed/skipped; do not invent SA passwords.

## Verify locally
- `dotnet test tests/Engine.Tests` → 513 passed without SQL; SQL suite needs `PAYROLL_TEST_SQL_CONNECTION`.
- `node tests/r8-contract.mjs` passed after `--update-snapshot`.
- `npm run api:check` / `npm run build` in `web/` passed.
- `ops-live-ui.mjs` needs Playwright under `/tmp/gate-browser` (not installed here).
