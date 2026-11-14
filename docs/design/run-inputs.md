# Run input snapshots (L2)

## Problem

Operations screens read logistics ERP live. Payroll calculation and approval must not move when ERP rows change mid-month. A cycle needs a frozen bag of ops rows (one company, month, and the apps on that cycle) before stages advance.

## Goals

1. Maker captures inputs once (recapture allowed only while **stage ≤ 1** — Ghazal seal decision **A0**).
2. After capture, cycle readers and fingerprint use `pay_run_input` only for swapped kinds (never silent Store/ERP fallback).
3. Live capture scopes by **cycle company** (commercial reg + that company's riders), not the actor.
4. Keyset paging (`Id > @after`) for capture walks; recount at end; mismatch → 503, nothing written.
5. Required kinds (Ninja: `ninja_daily`, `platform_account`, `vehicle_assignment`) with 0 rows need an explicit reason; `housing_residence` may be 0 without a reason.

## Non-Goals

- Invoice / pay-terms seal (sibling later).
- Full ERP mirror / re-enabling `ErpSyncJob` (TODO remains in `JobsSchedule.cs`).
- UI that calls capture (**U2**). **L2 must not deploy before U2.**
- Writing to ERP.

## Architecture flow

1. `POST /api/pay/cycles/{id}/capture-inputs` (optional `zeroKindReasons`).
2. Live mode: stage pages outside the write TX via `LiveOperationsQuery` (period + platform in SQL, cycle `CaptureScope`, keyset + end recount) → short TX `PayRunInputs.Replace`.
3. Store/CI mode: freeze from payroll Store fixtures inside the TX.
4. One projection: `Operations.Project` in both modes.
5. Advance/calc/review/fingerprint: `CycleOps` / `PayRunInputs.List|Get` after capture.
6. Pre-cycle draft / validate-gaps / category: live ERP when `operationsSource=erp_live`, else Store; captured open cycle uses freeze.

## Alternatives considered

| Option | Choice |
|---|---|
| Seal after any calc vs stage≤1 (A0) vs richer seal (C) | **A0** (Ghazal default if undecided); `CaptureAllowed` is one function |
| Actor scope vs cycle scope on capture | Cycle scope |
| OFFSET vs keyset | Keyset for capture; OFFSET remains for UI without `afterId` |
| Fat vs slim freeze table | Slim: cycle/kind/doc/body/captured_* only |

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Deploy before U2 | HANDOFF block; no screen calls capture |
| Captured cycle, empty freeze | Startup guard |
| Mid-capture ERP churn | End recount → 503 |
| Multi-app cycle | Capture union of apps' kinds (stage every app) |

## Open questions by role

- **Ghazal:** confirm A0 seal remains; approve push of this branch.
- **Omar:** deploy only after U2.
- **Planner:** invoice seal sibling timing.

## File list (this fix round)

- `Payroll.Api/PayRunInputs.cs`, `PayCycles.Capture.cs`, `PayCycles.View.cs`, `PayCycles.Sources.cs`, `PayCycles.cs`, `PayCycles.ValidateGaps.cs`, `PayReviewDraft.cs`, `PayReviewCategory.cs`, `NinjaInvoices.Actions.cs`, `Operations.cs`, `ApiContracts.cs`, `ReleaseSafety.cs`, `Store.cs`
- `Payroll.Sync/LiveOperationsQuery.cs`
- `tests/Engine.Tests/PayRunInputTests.cs`, `PayCycleTests.cs`
- `docs/design/run-inputs.md`

## In-flight cycles rule

Do not deploy L2 onto a host that already has open cycles mid-stage without a freeze bag. Captured cycles without `pay_run_input` rows fail startup. Cancel/recreate after upgrade if needed.
