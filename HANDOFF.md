# HANDOFF

| Field | Value |
|---|---|
| Current writer | — |
| Branch | main = `8e1b6e3` (0.21.0, PR #37); open fix: `fix/release-safety-first-boot` |
| Updated | 2026-11-14 by Claude (0.21.0 deployed + first-boot incident) |
| Live | `/health` = **0.21.0**, `dataPreservationVerified=true`, `releaseCheckpointPresent=true`, `operationsSource=erp_live`, **`erpReadonlyConfigured=false`** (Ghazal sets `ConnectionStrings__ErpReadonly`) |
| Deploy | run [43555124388](https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/43555124388): FTPS upload OK; first boot failed (500.30); fixed by restart; health step red |

## Deploy 0.21.0 and first-boot incident (2026-11-14, Claude)
- Ghazal «ادمج» → PR #37 squash `8e1b6e3`; PR #36 closed (included).
- **Incident (~04:20–04:45 UTC, site down with HTTP 500.30):** `Store` takes the before-upgrade snapshot before `PayRunInputs.Ensure`. The old DB had no `pay.PayRunInput` → snapshot without `pay_run_input`; after Ensure → `pay_run_input:0` → "Startup document preservation verification failed".
- Recovery: the table was created on the failed boot (read-only check: schema + table exist, 233 647 records intact). Restart via `scripts/monsterasp_ftps.py prepare` → `finish` (marker only; protected metadata unchanged) → `/health` 0.21.0 green.
- Code fix on `fix/release-safety-first-boot`: `ReleaseSafety.Capture` always records `pay_run_input` (0 when the table is absent). Test `FirstBootOfPreL2DatabaseCreatesRunInputTableAndStarts` (`EnterpriseTests.cs`) failed before, passes after; related tests 48/48 with SQL. Not urgent for hosted (table now exists); merge = deploy, so it waits for Ghazal's «ادمج».
- Next: Ghazal sets `ConnectionStrings__ErpReadonly` on MonsterASP (operations screens return 503 until then). Then Ninja 2026-10 parallel run (`docs/runbooks/NINJA-2026-10.md`).

## Release 0.21.0 (2026-11-14, Claude)
- Version `0.21.0`: `ReleaseSafety.cs:10`, health pin `scripts/monsterasp_ftps.py:184-190` (+ its test), node tests (`tests/enterprise.mjs`, `campaigns.mjs`, `finance-views.mjs`, `rider-statement-simple.mjs`), `docs/api/ENDPOINTS.md` title.
- Contract: `platform_account.assignments` typed (`ApiContracts.cs` `AssignmentRow()`, same fields as `Operations.Assignment`) + `relationship`; web `RiderAccountAssignment` now derived from the contract (`web/src/lib/api.ts`).
- Runbook `docs/runbooks/RELEASE-0.21.0.md`; Ninja runbook gets the capture step (`docs/runbooks/NINJA-2026-10.md` §5).
- Live payroll DB: **0 pay cycles** (read-only count, 2026-11-14), so no in-flight cycle (M10).
- Next (Ghazal): set `ConnectionStrings__ErpReadonly` (read-only login) on MonsterASP, then «ادمج». Close draft PR #36 after the merge (it is inside this release).

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
