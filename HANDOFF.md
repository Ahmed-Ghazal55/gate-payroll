# HANDOFF

| Field | Value |
|---|---|
| Current writer | — (next: Antigravity `~/.agents/prompts/antigravity-U2-cycle-capture-ui.md`) |
| Branch | feat/run-input-snapshots |
| Updated | 2026-11-14 by Claude (B3 check + cycle contract) |
| Live | `/health` = **0.20.0** (this branch not deployed) |
| Tip | `3c4eaeb` pushed; CI run 43539261832 green |

## Claude check of L2-fix2 (2026-11-14)
- **B3 ✅ closed.** `PayReviewCategory.Derive` reads only `vehicle` and `vehicle_assignment`; both come from `KindAwareLiveOps` in live mode (`PayReviewDraft.cs:92`, Map at `:139`). Unknown kind → 503. CI 43539261832 green on `3c4eaeb`. All blockers B1–B5 are closed.
- Note (not a blocker): the live draft opens 3 ERP reads in a row and loads all company vehicles in the period before filtering by id. Fine for Ninja Oct; revisit if slow.
- Contract gap fixed (M8 leftover): the cycle schema now has `inputsCapturedAt`, `inputsCapturedBy`, `inputsCaptureCounts` and the capture-inputs `capture` result (`ApiContracts.cs` `Cycle()`, `Counts()`). Snapshot `docs/api/openapi.v1.json` and `web/src/lib/api-contract.ts` regenerated; r8 contract 29 passed; `api:check` OK; web build OK.
- Next: Antigravity U2 → `~/.agents/prompts/antigravity-U2-cycle-capture-ui.md`.

## L2-fix2 B3 category (2026-11-14)
- `PayReviewDraft.Map` stages `ninja_daily`, `vehicle_assignment`, and referenced `vehicle` (`PayReviewDraft.cs` Map + `KindAwareLiveOps`).
- `Build` passes `liveOps` into `PayReviewCategory.Derive` (`PayReviewDraft.cs` Derive call).
- `PayCycles.ValidateGapsLiveOps` — unknown kind → 503 (`PayCycles.ValidateGaps.cs`, wired in `PayCycles.cs` validate-gaps Map).
- Tests: `DeriveUsesStagedVehicleNotEmptyStoreMirror`, `KindAwareLiveOpsThrowsOnUnknownKind` (`PayRunInputTests.cs`).
- Local SQL (`gate-sql-test`): **538/538 passed, 0 skipped**. `npm run api:check` OK.
- **L2 must not deploy before U2.** Next: Antigravity U2.

## Do not
- Do not write to ERP. Do not put secrets in git. No merge/deploy/version bump without Ghazal's yes.
