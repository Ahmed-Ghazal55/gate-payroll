# Payroll review grid, config lists, internet advance (K1–K4, BR §27, §29, §32, §25.9)

## Review grid (stage 6, before final approval)
- `GET /api/pay/cycles/{id}/review?review=pending|approved&status=ready|needs_decision` (`pay.view`)
  → `{cycle, totals:{riders,approved,pending,needsDecision,net}, rows:[…]}`.
  Each row: `rider, name, status, review, reviewedBy, reviewedAt, gross, commission, afterCommission, phase1, preliminaryDue, phase2, phase3, net, items[], edits[]`.
  Each item: `key, phase, order, labelAr, amount (negative = deduction), editable, max, source`.
- `POST /api/pay/cycles/{id}/review/{iqama}` (`pay.manage`):
  - `{action:"approve"}`: only a `ready` row;
  - `{action:"edit",key,amount,reason}`: a debt installment between 0 and its computed amount;
  - `{action:"adjust",amount,reason}`: a phase-3 salary adjustment. An increase is Dr `512122` / Cr `212103`; a decrease is Dr `212103` / Cr `512101`.
  - An edit approves the row when the row is `ready`.
- `POST /api/pay/cycles/{id}/review/approve-all` (`pay.manage`) → `{approved, skipped}`. `needs_decision` rows are skipped.
- Stage 7 (`pay.approve`, a different user) is refused until every row is approved.

## Deduction phases (BR §29)
- Every cycle deduction carries `phase`, `order` and `labelAr`, taken from the approved `deduction_types` list, or from the built-in defaults.
- Debt installments are taken in phase order.
- The monthly statement returns `phases`, with the same subtotals.

## Config lists (BR §27)
- `GET /api/config/lists` and `GET /api/config/lists/{group}`: any signed-in user.
- `POST …/{group}/items` (`pay.manage`):
  - a financial group (`recharge_packages`, `deduction_types`) becomes `pending_approval`;
  - the other groups apply directly.
- `POST …/{group}/items/{code}/approve` (`pay.approve`, not the maker).
- `POST …/{group}/fields` (`pay.manage`): field types `text|number|select|account|bool`.
- Groups: `recharge_networks`, `recharge_packages`, `deduction_types`, `operating_systems`, `pay_systems`, `salary_classifications`. Items are versioned and never deleted.

## Internet advance (BR §25.9)
- `POST /api/advance-requests` with `type:"internet"` and `fulfilment: cash|bank|recharge_card`.
- A card needs `networkCode` and `packageCode`; the amount is the approved package price.
- Category `InternetAdvance` → `112503`.
- A card uses the custody code `MADA` (routing `advance.payout:MADA`). Its payout returns 409 until the mada machine posting (`112404`, Codex C3) lands.
