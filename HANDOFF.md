# HANDOFF

| Field | Value |
|---|---|
| Current writer | — (next: Cursor `~/.agents/prompts/cursor-W1-fix-after-R1.md`) |
| Branch | feat/pay-pipeline-six-stages |
| Updated | 2026-11-14 by Cursor |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | W1 complete — Claude reviews money + migration next |


## Claude review R1 of W1 (2026-11-14)
- Fixed the stale `HousingChargeTests` expectation (`dbf405a`): engine tests green; CI still red in browser test `r8-ninja-flow` (negative captain not "ready" on the review grid).
- Review `~/.agents/reviews/R1-W1-pay-pipeline.md`: **B1** stage-4 rows never posted nor settled (double deduction next month, payable ≠ net); **B2** fuel_usage field mapping / month filter (silent zero risk); **M1** bank file from remainder + payout journal not built; **M2** fuel journal per §22.9 missing; **M3** golden run not executed; **M4** month items use instalment instead of full balance.
- Next: Cursor one fix round `~/.agents/prompts/cursor-W1-fix-after-R1.md`.
## In flight
W1 six-stage pay pipeline per `prompts/cursor-W1-pay-pipeline-six-stages.md` and BR §40. Open decisions as settings with ✅ defaults. No ERP/prod writes. Push branch only.

## Done this branch
- **Step 0** — `docs/design/pay-pipeline.md` (`f847577`). CI dispatched.
- **Step 1** — `InvoiceDeductionClassification.cs` allocations + Propose/Migrate/MultiRiderReport; Ninja/Platform classify+view proposal; ExpandForCycle in `PayCycles.Calculation.cs`; tests `DeductionAllocationTests` (`398b432`). CI `43597829708`.
- **Step 2** — `PayReviewCategory` weights orders|days|periods|compliance; pay_terms `riderAllocationWeight` default orders / tie-break days; `AllocationWeightTests` (`8fde0dd`). CI `43598003004`.
- **Step 3** — `PayEvaluator` commission base after penalties; salary operational expense lines (`PayExpenseAccounts`); `PayCycles.Calculation` housing/vehicle expense; tests in `PayEvaluatorTests` (`c8776a5`).
- **Step 4** — stage-4 before net (this-month debts); fuel_usage capture; FuelOverage `112511`; HS wallet `112506`; `PipelineSettings` open-decision defaults; `Stage4DeductionTests` (`77dc7a0`).
- **Step 5** — `SharedRiderConsolidation` hold/queue/release; Approve skips held; `SharedRiderConsolidationTests` (`74435a6`).
- **Step 6** — remove Ninja negative-net Merge blocker; debt limit = positive net; `AfterNetTests`. ENDPOINTS inventory for multi-rider + shared-riders.
- **Step 7** — `tools/SheetReconcile` zip/XML reader (Mac xlsx); `SheetReconcileTests`; report `~/.agents/reviews/W1-golden-september.md` (1064 usable sheet riders; local cycle DB missing).
- **Step 8** — openapi/`api-contract` for `fuel_usage`; CI fixes (classify company lines, carry ≠ stage4, §40.3 journal no double penalty, negative-net tests); engine **572/572 / 0 skipped** local SQL.

## Do not
- Do not write to ERP or db77738. No secrets in git. No merge/deploy/version bump without Ghazal's yes.
