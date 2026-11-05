# Advance requests (BR §25.3)

`advance_request` statuses: `under_review` → `approved` | `rejected` | `change_proposed` → (`under_review` after accept | `cancelled` after reject) ; `approved` → `paid`.

| Route | Who | Body / result |
|---|---|---|
| POST `/api/advance-requests` | `advances.request` + active supervision of the rider | `{riderIqama, type: cash|bank, amount, reason, vaultCode?}`. The default vault is the rider's company cash box or bank. One open request per rider. Notifies the routed reviewers |
| GET `/api/advance-requests?role=mine|review|payout&status=` | request / review / payout | Visible rows only |
| GET `/api/advance-requests/{id}` | visible | Includes `decisions[]` (actor, action, note, at, snapshot{department, rank, via, delegationId}) |
| GET `/api/advance-requests/{id}/summary` | routed reviewer | `ordersThisMonth`, `statement{available, cards, net}`, `openDebtBalance`, `openDebts`, `plannedInstallments`, `lastPayrollSettlementAt`, `advancesSinceLastSettlement[]`, `forecastNet`, `covered`, `coverageKnown`, `vaults[{type, code, balance, selected}]` |
| POST `/api/advance-requests/{id}/review` | `advances.review` + routed responsible; never the requester | `{action: approve, monthlyInstallment?, coverageReason (required when not covered)}` · `{action: reject, note}` · `{action: propose, note, proposed:{type?, amount?, vaultCode?}}`. Optional `revision` guard |
| POST `/api/advance-requests/{id}/respond` | the requester | `{action: accept|reject, note?}`. Accept applies the proposal and bumps `revision` |
| POST `/api/advance-requests/{id}/payout` | `advances.payout` + custodian of the vault | `{reference, note?}`. Creates a `CashAdvance` / `BankAdvance` debt (`sourceRef = ADV-REQ:{id}`) and the Dr 1210 / Cr vault journal. **409** when the vault balance is short; the request stays approved |

**Notifications** are personal (`category: personal`, `assignedTo`), with `taskKind: advance_request`, `entityId`, `requiresAction` and `actionStatus` (`open` → `decided` when the next step happens). Reading never changes `actionStatus`.
