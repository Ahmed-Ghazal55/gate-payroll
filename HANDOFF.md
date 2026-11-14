# HANDOFF

| Field | Value |
|---|---|
| Current writer | — |
| Branch | feat/run-input-snapshots |
| Updated | 2026-11-14 by Antigravity (U2 cycle capture UI) |
| Live | `/health` = **0.20.0** (this branch not deployed) |
| Tip | `1a2f66a` pushed; CI run [43553059387](https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/43553059387) green |

## U2 cycle capture UI (2026-11-14)
- `web/src/lib/api.ts:97,109-137,1330-1334`: Added `PayCycleCaptureResponse` and `api.capturePayCycleInputs(id)` from `api-contract.ts`; replaced `Record<string, any>[]` in `OperationsResponse` and `RiderAccountsResponse` with contract-derived `OperationRow`, `RiderAccountRow`, and `RiderAccountAssignment`.
- `web/src/app/payroll/page.tsx:69,123-129,431-432,1524-1546,3530-3546,3660-3831`: Added `#cycle-inputs-card` (`data-testid="cycle-inputs-card"`) with `#btn-capture-cycle-inputs` («التقاط المدخلات») gated strictly by `allowedActions?.includes('capture_inputs')`, uncaptured blocker link in `#cycle-next-step`, 409/503 alerts with retry, Riyadh capture timestamp (`formatRiyadhDateTime`), actor (`inputsCapturedBy`), and counts table (`inputsCaptureCounts`) with `var(--warning)` highlight on 0-row kinds.
- `web/src/app/operations/page.tsx:23,60,128-135,258-465,595`: Replaced `any` rows/errors with `OperationRow` and `unknown`; fixed 503 retry button dark-mode contrast (`bg-[var(--accent)] text-[var(--accent-ink)]`).
- `web/src/components/riders/RiderAccountsPanel.tsx:13-19,30,69-76,151,231,244`: Replaced `any` rows/errors with `RiderAccountRow`/`RiderAccountAssignment` and `unknown`; fixed 503 retry button dark-mode contrast.
- `web/src/components/ui/SourceBadge.tsx:35-60`: Switched badge colors to `var(--success)` and `var(--warning)` for `dim` dark-mode contrast.
- Tests: `npm run build` OK, `npm run api:check` OK, `node tests/r8-contract.mjs` (29 passed), `node web/tests/r8-ninja-flow.mjs` PASS, `node web/tests/r9-pay-settings-flow.mjs` PASS, `node web/tests/ops-live-ui.mjs` PASS, `node web/tests/r8-keeta-flow.mjs` PASS; CI run [43553059387](https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/43553059387) green on `1a2f66a`.

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
