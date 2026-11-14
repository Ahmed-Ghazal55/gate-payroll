# HANDOFF

| Field | Value |
|---|---|
| Current writer | — |
| Branch | feat/run-input-snapshots |
| Updated | 2026-11-14 by Cursor (L2-fix2 B3 category) |
| Live | `/health` = **0.20.0** (this branch not deployed) |
| Tip | `3c4eaeb` pushed; CI run 43539261832 green |

## L2-fix2 B3 category (2026-11-14)
- `PayReviewDraft.Map` stages `ninja_daily`, `vehicle_assignment`, and referenced `vehicle` (`PayReviewDraft.cs` Map + `KindAwareLiveOps`).
- `Build` passes `liveOps` into `PayReviewCategory.Derive` (`PayReviewDraft.cs` Derive call).
- `PayCycles.ValidateGapsLiveOps` — unknown kind → 503 (`PayCycles.ValidateGaps.cs`, wired in `PayCycles.cs` validate-gaps Map).
- Tests: `DeriveUsesStagedVehicleNotEmptyStoreMirror`, `KindAwareLiveOpsThrowsOnUnknownKind` (`PayRunInputTests.cs`).
- Local SQL (`gate-sql-test`): **538/538 passed, 0 skipped**. `npm run api:check` OK.
- **L2 must not deploy before U2.** Next: Antigravity U2.

## Do not
- Do not write to ERP. Do not put secrets in git. No merge/deploy/version bump without Ghazal's yes.
