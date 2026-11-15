# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/pipeline-screens` |
| Updated | 2026-11-15 by Antigravity (M3 complete) |
| Live | `/health` = **0.22.0** (unchanged; no deploy) |
| Tip | `5304242` on `feat/pipeline-screens` |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/43 |

## Claude review of M2 (2026-11-15): accepted, 3 fixes applied by Claude
- Merged `origin/main` (`aff86f0`, PR #42 full IBAN in form responses) into this branch on 2026-11-15 (Claude); the earlier note that main was already merged was wrong. Only HANDOFF conflicted.
- **Pipeline (`PayCycles.Pipeline.cs`)**: (1) stage-4 deduction rows were also listed under `4_expenseLines` (operator precedence + missing `affectsNet` key) → now only `company_expense` rows or rows with an explicit `affectsNet=false`; (2) `2_accountShare` was the account *deduction* → now the rider's share of account earnings (Σ sources `baseGross`+`incentive`), `2_penaltiesAllocated` = Σ `platformPenalties`; (3) `8_remainder` was the closing balance (carried + net) → positive net − old-debt instalment, or the confirmed stage-8 net; new `8_confirmed`; `6_net` reads `salaryNet` after confirmation. Lines are loaded once per cycle. Test `PipelineSeparatesStage4FromExpensesAndComputesRemainder` (fails on the old code).
- **Treasury (`TreasurySummary.cs`)**: `other` was every remaining asset account (receivables, rider advances) and was added to the total → removed; total = cash + bank only. Test extended with a rider-advance line.
- Checked and fine: `allowedActions` use `IndependentForView`/`BypassForView` (no audit on read); posting gate is admin-only; pipeline and treasury demand `pay.view` / `treasury.view` and the rider/sponsor scope.
- Minor, not fixed: `Debts.View` offers `decide` to any `debts.view` holder who is independent; the decide endpoint still enforces department and rank, so the button can appear and then be refused.
- Engine 614/614 with local SQL, r8 contract 36, api-cleanup 423, `api:check`, `tsc`.
- **For Antigravity (M3):** stage keys on `GET /pay/cycles/{id}/pipeline` → `1_gross`, `2_accountShare`, `2_penaltiesAllocated`, `3_commission`, `3_afterCommission`, `4_items[]`, `4_expenseLines[]` (not deducted), `5_*`, `6_net`, `8_oldDebtInstallment`, `8_remainder`, `8_confirmed`. Treasury summary has `cash`, `bank`, `total` only.

## Goal now
M3 complete on `feat/pipeline-screens` (branched from `feat/api-for-ui`). Next: Claude reviews M3, then merge PR #43 / `feat/pipeline-screens` and deploy for M5 (Ninja October live run).

## Done (M3 — Antigravity, 2026-11-15)
- Item 1 (`b3894d7`): Per-row bearer editor (`Company`/`Rider`/`Split` with diff=0 check) + multi-rider alert in `NinjaInvoiceUploadScreen.tsx`; deleted client `computedView`/Keeta totals; replaced hardcoded companies with `api.getCompanies()` + `SearchableSelect`.
- Item 2 (`fe0b3d7`): Added `#cycle-pipeline-section` per-rider §40 pipeline table + detail panel (`4_expenseLines` marked «مصروف على الشركة — لا يُخصم») and real stage-4 tab in `payroll/page.tsx`.
- Item 3 (`985fa2f`): Added `shared_riders` tab («مندوب عمل مشترك») with `missingApps` badges and release `<Dialog>` (`reason >= 3`).
- Item 4 (`d07183e`): Displayed `bankFilePreview.basis`, added `#btn-bank-mark-sent` confirmation `<Dialog>` (`markSent: true`), updated bank link condition in `payroll/page.tsx`.
- Item 5 (`0085bc0`): Removed client maker/checker & stage rules across payroll, settings, review grid, debt deductions, Jahez, accounting adjustments, config lists, debts, treasury; gated buttons from `allowedActions` only.
- Item 6 (`156d385`): Deleted `RuleRecordWorkspace.tsx` & `getPayAppDefaults`; removed `COMPANY_NAME_MAP` (`accounting/page.tsx`) and `OFFICIAL_COMPANIES_PAYLOAD` (`companies/page.tsx`, replaced with JSON upload/paste); wired `admin/page.tsx` Firebase settings to `getFirebaseConfig`/`saveFirebaseConfig`.
- Item 7 (`afa7a7d`): Replaced prefix-based cash/bank sums in `treasury/page.tsx` with `GET /api/finance/treasury/summary` (`cash`, `bank`, `total`).
- Item 8 (`b13486c`): Removed `lib/types.ts` imports and `any` on M3 covered screens (`payroll`, `features/payroll`, `treasury`, `debts`, `admin`).
- Item 9 (`1a75638`): Added `?tab=` deep links on `accounting/page.tsx` and `companies/page.tsx`; added `fuel_usage`, `vehicle_assignment`, `housing_residence`, `ninja_daily` to `operations/page.tsx` and capture labels; replaced native `<select>`s with `SearchableSelect`.
- Item 10 (`5201aac`): Added debt edit, sponsor transfer (`debts/page.tsx`), and vault transfer (`treasury/page.tsx`) dialogs.
- Fix (`5304242`): Re-match saved Ninja/Keeta invoice once `companies` loads; updated E2E selectors for `SearchableSelect` & `allowedActions`.
- Verified: `npm run build`, `npm run api:check`, `npx tsc --noEmit`, `node tests/r8-contract.mjs` (36 passed), `web/tests/r8-ninja-flow.mjs`, `web/tests/r8-keeta-flow.mjs`, `web/tests/r9-pay-settings-flow.mjs`, `web/tests/ops-live-ui.mjs`, and CI run [43711105378](https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/43711105378) green.

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
