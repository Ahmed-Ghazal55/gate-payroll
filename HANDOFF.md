# HANDOFF

| Field | Value |
|---|---|
| Current writer | Claude 2026-11-15 19:30 release 0.23.0 |
| Branch | `release/0.23.0` = main + PR #43 (M1 live ERP reads, M2 API for the screens) + PR #44 (responses: bank and campaign names) |
| Updated | 2026-11-15 by Claude (release 0.23.0) |
| Live | `/health` = **0.22.0** until this release merges; `erpReadonlyConfigured=true` since 2026-11-15 (Claude added `ConnectionStrings:ErpReadonly`, read-only login, to the live `wwwroot/appsettings.Local.json`; backup in `~/Private/gate-data/cutover/`) |
| Next | Claude reviews M3 (`feat/pipeline-screens`, Antigravity) → release 0.24.0 |

## Release 0.23.0 (2026-11-15, Claude; Ghazal asked to merge and deploy)
- Contents: PR #43 + PR #44. Not included: M3 (`feat/pipeline-screens`), not reviewed yet.
- Live settings: the live site reads `ConnectionStrings:DefaultConnection` from the hosting environment and `ConnectionStrings:ErpReadonly` from `wwwroot/appsettings.Local.json` (the deploy never overwrites that file).
- Responses screen (PR #44): `web/src/lib/saudi-banks.ts` `bankLabel` (bank name from the API `bankCode` or the IBAN's bank code); campaign name from the API `title`; campaign filter lists every campaign and form that has responses.

## Claude review of M2 (2026-11-15): accepted, 3 fixes applied by Claude
- Merged `origin/main` (`aff86f0`, PR #42 full IBAN in form responses) into this branch on 2026-11-15 (Claude); the earlier note that main was already merged was wrong. Only HANDOFF conflicted.
- **Pipeline (`PayCycles.Pipeline.cs`)**: (1) stage-4 deduction rows were also listed under `4_expenseLines` (operator precedence + missing `affectsNet` key) → now only `company_expense` rows or rows with an explicit `affectsNet=false`; (2) `2_accountShare` was the account *deduction* → now the rider's share of account earnings (Σ sources `baseGross`+`incentive`), `2_penaltiesAllocated` = Σ `platformPenalties`; (3) `8_remainder` was the closing balance (carried + net) → positive net − old-debt instalment, or the confirmed stage-8 net; new `8_confirmed`; `6_net` reads `salaryNet` after confirmation. Lines are loaded once per cycle. Test `PipelineSeparatesStage4FromExpensesAndComputesRemainder` (fails on the old code).
- **Treasury (`TreasurySummary.cs`)**: `other` was every remaining asset account (receivables, rider advances) and was added to the total → removed; total = cash + bank only. Test extended with a rider-advance line.
- Checked and fine: `allowedActions` use `IndependentForView`/`BypassForView` (no audit on read); posting gate is admin-only; pipeline and treasury demand `pay.view` / `treasury.view` and the rider/sponsor scope.
- Minor, not fixed: `Debts.View` offers `decide` to any `debts.view` holder who is independent; the decide endpoint still enforces department and rank, so the button can appear and then be refused.
- Engine 614/614 with local SQL, r8 contract 36, api-cleanup 423, `api:check`, `tsc`.
- **For Antigravity (M3):** stage keys on `GET /pay/cycles/{id}/pipeline` → `1_gross`, `2_accountShare`, `2_penaltiesAllocated`, `3_commission`, `3_afterCommission`, `4_items[]`, `4_expenseLines[]` (not deducted), `5_*`, `6_net`, `8_oldDebtInstallment`, `8_remainder`, `8_confirmed`. Treasury summary has `cash`, `bank`, `total` only.

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
