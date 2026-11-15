# HANDOFF

| Field | Value |
|---|---|
| Current writer | — |
| Branch | main = `f900e8f`; open: `fix/iban-full-in-responses` (this), `fix/live-erp-reads` (M1, Cursor, waits for review) |
| Updated | 2026-11-15 by Claude (full IBAN in form responses) |
| Live | `/health` = **0.22.0** (PR #41 `f900e8f`); `erpReadonlyConfigured=false` until Ghazal sets the read-only ERP login |
| Tip | release/0.22.0 — engine 586/586 local SQL, 0 skipped |

## Responses screen: bank name and campaign name (2026-11-15, Claude; Ghazal's report after PR #42)
- Branch `fix/responses-bank-and-campaign` from main `aff86f0`. Web only.
- Bank under the IBAN: `web/src/lib/saudi-banks.ts` `bankLabel` = Arabic name + SWIFT from the API `bankCode`, else from the IBAN's bank code (characters 5–6). Unknown code shows the code; never a fixed bank. Used in `ResponsesTab.tsx` (list + export) and `ReviewDrawer.tsx`. Cause: `GET /api/responses` returns `bankCode`, not `bank`.
- Campaign column: the API returns the campaign or form name as `title`; `web/src/lib/api.ts` `getSubmissions` now maps it to `campaignTitle`. The internal id is never shown («—» when unknown).
- Campaign filter: choices now include every campaign and custom form that has responses (before: campaigns only, so custom-form responses could not be picked). Column filter and sort work on the name (`getValue` on the campaign, IBAN/bank and status columns).
- Checks: `npx tsc --noEmit`, `npm run build`, `npm run api:check` green. Not checked in a browser against live data.

## Full IBAN in form responses (2026-11-15, Claude; Ghazal's request)
- Master plan and status: `~/.agents/plans/MASTER-2026-11-15.md`.
- `web/src/features/forms/ResponsesTab.tsx`: the responses list shows the full IBAN grouped by four (`formatIban`); Excel and CSV export carry the full IBAN as one unbroken value (`cleanIban`). The mask was client-side only; the API already returns the full value to users allowed to list responses, so no new access is opened.
- Removed the fake bank fallback «مصرف الراجحي» when a response has no bank (`ResponsesTab.tsx`, `ReviewDrawer.tsx`) → «—».
- Tests updated: `web/tests/e2e.mjs`, `web/tests/e2e-live.mjs` (not in CI) now assert the full IBAN. `tsc` and web build pass.
- Merge = deploy: waits for Ghazal's «ادمج».

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
