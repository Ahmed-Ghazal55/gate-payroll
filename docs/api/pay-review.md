# Payroll review grid, config lists, internet advance (K1–K4, BR §27, §29, §32, §25.9)

## Review grid and independent salary approval (R9 P2)
- `GET /api/pay/cycles/{id}/review?review=pending|approved&status=ready|needs_decision` (`pay.view`) returns `{cycle,totals,rows}`. Rows include phase totals, items, edit history, `blockers`, `allowedActions`, `salaryApprovedAt` and `debtDeductionStatus`.
- Stages 4–6 remain reachable when a rider needs a decision. At stage 7, independently approved rows post; unreviewed/blocked rows remain editable in `partially_approved` and cannot enter the bank file. Repeating stage 7 posts only newly reviewed rows. Posted rows require reversal for correction.
- `POST /api/pay/cycles/{id}/review/{iqama}` (`pay.manage`):
  - `{action:"approve"}` approves a ready row. A negative row with no other blocker requires a written `reason`; its deficit is carried only at final salary approval.
  - `{action:"edit",key,amount,reason}` chooses an instalment from zero to the due amount without deducting it yet.
  - `{action:"adjust",itemType,amount,reason}` adds a signed salary item. `itemType` selects an active, independently approved `pay_adjustment_types` entry with its target account and `direction:increase|decrease|both`. An optional `accountCode` must match that entry.
  - Editing an existing adjustment also requires `itemType`; editing a vehicle/platform deduction adds an explicit compensating item, preserving the original invoice/vehicle facts.
  - An edit approves the row if it is ready. `approve-all` skips `needs_decision` rows.
- An independent `pay.approve` user posts stage 7. Negative commission/invoice entitlements become `NegativeSalaryCarryover` on `112507`; no negative debit/credit amounts are written.
- BR §22.9.2 remains: an ineligible Keeta salary fallback recovers operating cost and current-month fuel only up to available earnings; the rest is expense `512121`, never a new rider debt. `GET /api/pay/cycles/{id}/unrecovered-costs` exposes the management breakdown. A negative Ninja target-salary result is blocked for a separate policy decision; the Keeta exception is not extended to it.

## Debt confirmation after salary approval (stage 8)
- `GET /api/pay/cycles/{id}/debt-deductions` returns each rider's approved salary, due items, proposed capped amounts, current debt balances and allowed actions.
- `POST` to the same route accepts `{action:"edit",rider,key,amount,reason}` with `amount` from zero to due, or `{action:"confirm",rider}`, or `{action:"confirm-all"}`. Selection and confirmation require independent users (`pay.manage` / `pay.approve`).
- Confirmation caps the sum at positive salary net and available posted liability. Debt balances, instalment journals and cycle totals commit atomically. Zero defers a due item without a ledger movement; uncovered personal debt remains open. Replays never post an instalment twice.
- `POST /api/pay/cycles/{id}/advance {stage:8}` confirms all eligible rows. The cycle reaches stage 8 after every salary row and deduction choice is confirmed. Confirmed rows can reach the bank file while another rider remains blocked.
- Bank preview/export excludes pending salary or debt-confirmation rows. The exported amount comes from closing posted liability after confirmed deductions.
- Invoice classification supports `company_level` with an expense account, `account_level`, or `rider_level` with an explicit actual operator `riderId`. Supplier `DEDUCTIONS` rows retain `captainId`; changing their bearer does not change invoice amounts.

## Deduction phases (BR §29)
- Every cycle deduction carries `phase`, `order` and `labelAr`, taken from the approved `deduction_types` list, or from the built-in defaults.
- Debt installments are taken in phase order.
- The monthly statement returns `phases`, with the same subtotals.

## Config lists (BR §27)
- `GET /api/config/lists` and `GET /api/config/lists/{group}`: any signed-in user.
- `POST …/{group}/items` (`pay.manage`):
  - a financial group (`recharge_packages`, `deduction_types`, `pay_adjustment_types`) becomes `pending_approval`;
  - the other groups apply directly.
- `POST …/{group}/items/{code}/approve` (`pay.approve`, not the maker).
- `POST …/{group}/fields` (`pay.manage`): field types `text|number|select|account|bool`.
- Groups: `recharge_networks`, `recharge_packages`, `deduction_types`, `pay_adjustment_types`, `operating_systems`, `pay_systems`, `salary_classifications`. Items are versioned and never deleted.

## Internet advance (BR §25.9)
- `POST /api/advance-requests` with `type:"internet"` and `fulfilment: cash|bank|recharge_card`.
- A card needs `networkCode` and `packageCode`; the amount is the approved package price.
- Category `InternetAdvance` → `112503`.
- A card uses the custody code `MADA` (routing `advance.payout:MADA`). Its payout returns 409 until the mada machine posting (`112404`, Codex C3) lands.
