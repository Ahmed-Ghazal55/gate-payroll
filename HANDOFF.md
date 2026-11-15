# HANDOFF

| Field | Value |
|---|---|
| Current writer | _(none — M2 done; awaiting Claude M2 review)_ |
| Branch | `feat/api-for-ui` |
| Updated | 2026-11-15 by Cursor (M2 complete) |
| Live | `/health` = **0.22.0** (unchanged; no deploy) |
| Tip | `133352b` on `feat/api-for-ui` |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/43 |

## Goal now
M2 complete on branch. Next: Claude reviews M2, then Antigravity runs `~/.agents/prompts/antigravity-M3-pipeline-screens.md`.

## Done (M2)
- R1–R3 (M1 leftovers): Ninja RequiredKinds slimmed; rejected server-side only; dual Iqama/unmatched blockers.
- Item 1: `allowedActions` / blockers on debts, advances, Jahez handovers, manual journals, config lists, PaySettings, HS invoices/allocations/disputes, cycle `confirm_debt_deductions_all`, PlatformPosting `open_posting`.
- Items 2–3: `GET /pay/cycles/{id}/pipeline` (§40 stages); `GET /finance/treasury/summary` (posting CoA cash/bank/other).
- Items 4–7: ApiContracts + r8 cases; legacy payroll schemas; regenerate OpenAPI / `web` types / `ENDPOINTS.md`; api-cleanup PASS.

### Dead client call (Antigravity — delete)
- `web/src/lib/api.ts` → `getPayAppDefaults` calls `/pay/apps/{app}/defaults` — **no API route**. Do not add a route. Delete the client helper (and any callers) in M3. Catalog defaults stay on `GET /pay/catalog` (`defaults` + `defaultsAreDraftOnly`).

### Antigravity — `allowedActions` map (use these names; do not invent buttons)

| Record | Where | Action names |
|---|---|---|
| Pay cycle | `PayCycles.View` | `capture_inputs`, `cancel`, `advance`, `review`, `approve_salaries`, `review_debt_deductions`, `confirm_debt_deductions`, `confirm_debt_deductions_all`, `reversal_request` |
| Debt deduction row | `PayCycles.DebtDeductions` | `edit`, `confirm` |
| Review rider row | `PayCycles.Review` | `adjust`, `edit`, `approve` |
| Debt | `Debts.View` | `edit`, `collect`, `transfer_sponsor`, `decide` |
| Advance request | `AdvanceRequests` | `approve`, `reject`, `propose`, `accept`, `reject_proposal`, `payout` |
| Jahez hand-over | `JahezHandovers` | `approve`, `reject` (reject needs reason) |
| Manual journal | `ManualJournals` | `approve`, `reject`, `delete`, `reverse` |
| Config list change | `ConfigLists` | `propose`, `approve` |
| Pay settings / terms / schemes / eligibility | `PaySettings.WithActions` | `approve` (+ `approvalBlockers`) |
| HS invoice | `HungerStationInvoices.InvoiceView` | `dispute`, `approve` |
| HS allocation | `HungerStationInvoices.AllocationView` | `approve` |
| HS dispute | `HungerStationInvoices.DisputeView` | `resolve` |
| Platform posting gate | `PlatformPosting` | `open_posting`, `close_posting` (admin) |

### New reads for M3 screens
- `GET /api/pay/cycles/{id}/pipeline` — per-rider §40 stages 1–6.
- `GET /api/finance/treasury/summary` — cash / bank / other / total per company (posting accounts only).
- Cycle `status` may be `partially_approved` (documented in contract).

## Done (M1)
- Schema probe + `ErpSchemaContractTests`; snapshot committed.
- A1–A4 period columns, Riyadh UTC, `UnifiedNationalNumber`, `ErpPlatformCode`, `GET /api/system/erp-sponsor-map`.
- A5–A7 keyset source Id, capture account holders, rejected + Iqama blocker.
- A9a–c + ERR: fuel by FuelCardId; EventType 5 = PermissionRenewed; §24.3 same-day; ERP → 503.
- A10/A8 capture kinds + HungerStation `finance_read` views; Keeta note.
- Proof: `~/.agents/reviews/M1-live-capture-proof.md` (ninja_daily source **3751** all companies; 4 keyset pages).
- API inventory + OpenAPI regenerated for erp-sponsor-map.

## Open questions
- `ninja_daily_deduction` nearly empty for BW/TR in Oct — confirm with Ghazal whether zero-kind reason is OK for those companies.
- Vehicle/assignment aggregates under unscoped ERP dump (expected with A6 period+platform scope).

## Do not
- No merge/deploy/version bump without Ghazal's yes. ERP read-only only.
