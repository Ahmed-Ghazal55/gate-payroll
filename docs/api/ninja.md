# Ninja engine and invoice reconciliation (R6 N2/N3, BR §26)

| Method | Route | Permission | Notes |
|---|---|---|---|
| GET | `/api/platforms/ninja/terms/seed` | `pay.view` | Seed contract terms: the starting point for a `pay_terms` draft with `billingModel: ninja_target`. |
| POST | `/api/platforms/ninja/invoices/reconcile` | `pay.view` | Read-only. Takes `{period, files:[{fileName, dataBase64}]}` (1–5 supplier xlsx files, sent as Ninja delivers them). |

**Terms used:** the latest approved `pay_terms` for app `ninja` and the month with `billingModel=ninja_target`. If there is none, the seed is used and the response says `termsSource: "seed_unapproved"`.

## Formula (per captain; every rate comes from the terms)
1. `days` is the first attended date to the end of the month. `monthDays` is the calendar month.
2. `adjustedTarget = floor(contractTarget × days / monthDays)`; `adjustedAmount = contractAmount × days / monthDays`.
3. **Target total:**
   - **orders ≥ adjustedTarget:** target total = adjustedAmount + (orders − adjustedTarget) × `extraOrderRate`.
   - **Below target:** `below = (adjustedTarget − orders) / adjustedTarget`. The first level with `below ≤ maxBelow` applies: target total = adjustedAmount − missing × `missingOrderRate`.
   - **Beyond the last level:** target total = orders × `perOrderRate`.
4. **Attendance:** if `attendedDays / days < minRatio`, deduct `deductionPercent` % of the target total.
5. **Short days:** `contractAmount / monthDays × deductionPercentOfDailyAmount % × days under 8 hours`.
6. `final = targetTotal − attendance − shortDays + pass-through`. The pass-through columns are taken as delivered, signed: refund, gasoline, Weekend 40/42/43, 100 SAR, supplier performance, Naqil blocked, penalty, on demand, stops without notice.
7. `VAT = final × vatPercent %`; `TOTAL COST = final + VAT`.

## Reconcile output
- **Per invoice:** captain count, matched, different, `adjustments`, `totals`, and `lines`.
  - `adjustments` are invoice-level rows with no contract, such as `DEDUCTIONS`. They are kept, never recalculated.
  - `totals` has invoice vs engine vs difference for each field.
  - `lines` give status, level and differences per captain.
- **Reasons** attached to a difference: `capability_label_edited:<label>`, `contract_amount_differs_from_terms`, `unexplained`.
- There is no commit or posting here. Posting is Codex R6 C4.

## Verification (2026-11-05, October 2026 invoices, three suppliers)
- 206 of 207 captains match to 0.01.
- The one difference is a hand-edited capability (`BIKE`) priced at the car per-order rate.
- Synthetic tests: `tests/Engine.Tests/NinjaBillingTests.cs`.
