# HANDOFF

| Field | Value |
|---|---|
| Current writer | |
| Branch | feat/pay-pipeline-six-stages |
| Updated | 2026-11-14 by Cursor |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | `5845558` — W1-fix done (CI green `43610333573`); Claude re-checks blockers; M3 golden needs local Sept DB |

## In flight
None for Cursor. Next: Claude verifies R1 blockers closed; Antigravity W3 screens after that.

## W1-fix (2026-11-14) — `prompts/cursor-W1-fix-after-R1.md`
| ID | Result | Where / tests |
|---|---|---|
| CI | green | percent share signed `PayEvaluator.cs`; `PercentCommissionKeepsNegativePlatformShareReady`; browser `r8-ninja-flow`; CI `43610333573` / earlier `43608718875` |
| B1 | fixed | `SettleStage4` + PostEarnings stage4 journals; `ThisMonthAdvanceWithZeroInstalmentSettlesInFullAtApproval` |
| B2 | fixed | `LiveOperationsSource` fuel_usage map; ReportMonth SQL; capture month filter; `FuelUsagePreviewSqlFiltersReportMonth`, `ErpShapedFuelUsageBecomesStage4Amount` |
| M4 | fixed | this-month takes full remaining (`PayCycles.Calculation.cs`) |
| M2 | fixed | §22.9 fuel journal Dr expense+112511 / Cr `114106`; ledger `fuel_usage` |
| M1 | fixed | bank `basis=stage6_remainder`; payout only on `markSent`; parallel posts nothing |
| m1 | fixed | `salaryFuelCompanyPaysWhenNoCap` default false |
| m2 | fixed | design note: salary vehicle/housing expense report-only |
| M3 | left | SheetReconcile tool ready; September local cycle DB still missing — for Ghazal |

Local engine **577/577 / 0 skipped**. No ERP/prod writes. Push branch only.

## Do not
- Do not write to ERP or db77738. No secrets in git. No merge/deploy/version bump without Ghazal's yes.
