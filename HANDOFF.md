# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-14 W1 pay-pipeline-six-stages |
| Branch | feat/pay-pipeline-six-stages (creating from origin/main) |
| Updated | 2026-11-14 by Cursor |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | W1 in progress — step 2 pushed; step 3 next |

## In flight
W1 six-stage pay pipeline per `prompts/cursor-W1-pay-pipeline-six-stages.md` and BR §40. Open decisions as settings with ✅ defaults. No ERP/prod writes. Push branch only.

## Done this branch
- **Step 0** — `docs/design/pay-pipeline.md` (`f847577`). CI dispatched.
- **Step 1** — `InvoiceDeductionClassification.cs` allocations + Propose/Migrate/MultiRiderReport; Ninja/Platform classify+view proposal; ExpandForCycle in `PayCycles.Calculation.cs`; tests `DeductionAllocationTests` (`398b432`). CI `43597829708`.
- **Step 2** — `PayReviewCategory` weights orders|days|periods|compliance; pay_terms `riderAllocationWeight` default orders / tie-break days; `AllocationWeightTests` (`8fde0dd`). CI `43598003004`.

## Do not
- Do not write to ERP or db77738. No secrets in git. No merge/deploy/version bump without Ghazal's yes.
