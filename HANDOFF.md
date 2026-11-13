# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/run-input-snapshots (from feat/live-ops-erp-reads; draft PR #36 base) |
| Updated | 2026-11-13 by Cursor (L2 run-input snapshots); committed locally by Claude agents-hub, **not pushed**; review R in `~/.agents/reviews/R-L2-run-input-snapshots.md` |
| Live | `/health` = **0.20.0** (this branch not deployed) |

## L2 done (Cursor)
- Design: `docs/design/run-inputs.md` (EGM Stage 5 only; artifacts `~/.agents/work/egm/gate-payroll/`; Stages 6–9 → Claude R).
- Table: `PayRunInputs` dual-engine (`pay.PayRunInput` / `pay_run_input`); Ensure from `Store` ctor.
- Capture: `POST /api/pay/cycles/{id}/capture-inputs`; `allowedActions` includes `capture_inputs` until sealed; audit `pay.inputs_captured`; sealed → 409.
- Live path: stage pages outside TX then replace; period/platform filters in `LiveOperationsQuery.BuildWhere`; store path for CI.
- Readers swapped to freeze when captured: Calculation, Sources Fingerprint, ValidateGaps (prefer freeze), PayReviewDraft/Category (prefer freeze), NinjaInvoices.Actions (prefer freeze when cycle exists).
- Advance requires `inputsCapturedAt` (`DemandCaptured`).
- Mirror TODO 2026-11-13 on `JobsSchedule.cs`.
- Tests: `PayRunInputTests` + Through/Capture helpers; HTTP/browser capture calls. Local engine filter ~495 pass; SQL True case needs CI host 11433.
- CI: `web/tests/ops-live-ui.mjs` + `tests/r8-contract.mjs` added to browser list; OpenAPI ops row OneOf; ENDPOINTS inventory includes capture-inputs.

### Cycle-bound vs not (HANDOFF matrix)
| File | Cycle-bound after L2? |
|---|---|
| `PayCycles.Calculation.cs` | yes |
| `PayCycles.Sources.cs` Fingerprint | yes (after capture) |
| `PayCycles.ValidateGaps.cs` | yes when open cycle captured; else store diagnose |
| `PayReviewDraft.cs` | prefer freeze if open cycle captured; else store (pre-cycle) |
| `PayReviewCategory.cs` | prefer freeze if open cycle captured |
| `NinjaInvoices.Actions.cs` CaptainAccounts | prefer freeze if open Ninja cycle; else store (invoice upload) |
| `ChefzOperations.cs` | no (pre-cycle view) |
| `KeetaInvoices.cs` | no |
| `HungerStationInvoices.Parse.cs` | no |
| `InvoiceDeductionClassification.cs` | no |

## Next step (exact)
1. **Claude R:** migration/money EGM (Stages 6–9 + C6 live test); BR draft; review seal-at-partial-approve open question in design doc.
2. **U2 — Antigravity:** cycle UI capture button + captured-at + replace `any` ops rows with L2 contract types.
3. Push/PR only after Ghazal's yes (public repo; merge deploys).

## Do not
- Do not write to ERP. Do not put secrets in git. Do not merge/deploy without Ghazal's yes.
