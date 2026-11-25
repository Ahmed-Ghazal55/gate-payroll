# Mada recharge (Payments) and retired adjustment routes

Structured accounting-adjustment routes are **gone** (`410`):

| Route | Status |
|---|---|
| `GET/POST /api/finance/accounting-adjustments` | `410` Arabic gone message |
| `POST /api/finance/accounting-adjustments/{id}/approve` | `410` |
| `GET /api/finance/mada/balance` | `410` — use Payments Mada wallets |

Replacement UI: `/payments/mada` (U-W3-29) on U-W2-08c endpoints:

- `GET /api/payments/mada/wallets`
- `POST /api/payments/mada/topups`
- `POST /api/payments/mada/reconcile`
- `POST /api/payments/mada/cards/issue`

`MadaAccounting.PayInternet` remains for legacy advance payout fulfilment (`recharge_card` / cash / bank). Stored `finance_adjustment` rows are not deleted; vouchers may still resolve visibility for historical docs.

Legacy cash allocation (`/api/finance/legacy-cash-allocation/*`) is also `410`; posting a `closingLegacyGroup` journal line is refused.
