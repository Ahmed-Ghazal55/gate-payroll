# HANDOFF

| Field | Value |
|---|---|
| Current writer | Claude 2026-11-14 (W1 final check + release) |
| Branch | release/0.22.0 (W1 pipeline + user rename PR #40 + bypass view PR #39) |
| Updated | 2026-11-14 by Claude (release 0.22.0) |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | release/0.22.0 — engine 586/586 local SQL, 0 skipped |

## Release 0.22.0 (2026-11-14, Claude; Ghazal «ادمج وانشر»)
- Contents: W1 six-stage pipeline (BR §40) + W1-fix + golden tools; PR #40 safe login rename + stale permission codes; PR #39 invoice views read the bypass grant. Merge conflicts in `NinjaInvoices.Actions.cs` / `PlatformInvoiceWorkflow.cs` resolved: W1 `Classified`/`AllCompany` checks + `IndependentForView(...,bypass)`.
- Engine fix found by the September golden check: **no commission on a non-positive share** (`PayEvaluator.cs` percent fee = `Math.Max(0,base) × %`); sheet practice «الصافي بعد العمولة = Final Amount» when negative. Test `PercentCommissionKeepsNegativePlatformShareReady`.
- Golden loader: salary rows detected from the answer key per (captain, rider) when ops «نظام العمل» is blank (`tools/GoldenNinja/LoadNinja.cs`).
- UI/API text: «على اليوزر» → «على المندوب اللي اشتغل على اليوزر» (BR §40.1).
- Version `0.22.0` in the 7 pins + `docs/api/ENDPOINTS.md`. No new tables (first-boot check not affected).
- **September golden (Ninja-only, 176 riders): due 154/176 = 87.5%, deductions 100%, fuel 99.4%, instalment 99.4%, transfer 137/176 = 77.8%. Not at 95% → the new engine is NOT yet proven equal to the sheet.** Open: order-weight vs the sheet's per-rider final (6), sheet due column oddities (12), 3 riders without ops rows, البوابة cycle stopped at stage 6 (debt outside user scope at stage 7) so its transfer is not the confirmed remainder.
- Live note: Ninja October invoices classified «على اليوزر» with more than one rider need review after this release (`GET /api/platforms/ninja/invoices/{id}/multi-rider-deductions`).

## Done (W1-M3b)
- Gate restored: orders column «تسليم عدد طلبات» ties 190/191 (0 mismatches). Check 4 fails 3/191 (1.57%). STOP = no.
- Due **147/178 (82.6%)**, transfer **138/178 (77.5%)**. (a)=0. Not 95%, and (d) remains — not relabelled.
- Causes: (c) 19 sheet CJ ≠ BU×0.9; (b) 6 §40.2 order weight; (d) 3 with no ops share. 6 salary riders excluded (negative allowance).
- Fuel once on the main company. Engine tests **580/580 / 0 skipped** with local `gate-sql-test`.
- Reports: `~/.agents/reviews/W1-golden-september.md`, `W1-M3-data-check.md`.

## Next step (exact)
Claude reads both reports. Due is below 95% and three riders stay (d). No merge and no deploy. Antigravity W3 waits for Claude.

## Do not
- Do not write to ERP or db77738. No secrets or PII in git. No merge, deploy, or version bump without Ghazal's yes.
