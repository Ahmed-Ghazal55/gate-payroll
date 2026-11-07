# Mada recharge and structured adjustments (BR25.9/29)

The generated target chart now includes active112404 (Mada),512122 (salary increases) and421302 (initial administrative charges), directly from the approved finance spec. It contains411 accounts /235 active posting accounts. The generator preserves BR annotations on new source rows.

`POST /api/finance/accounting-adjustments` requires finance.journal.create and `{type,companyId,date,reference,description,amount,riderIqama?,bankAccountCode?}`. Types:

| Type | Debit | Credit | Additional checks |
|---|---|---|---|
| mada_topup |112404|selected bank|Active actual bank leaf, same-company bank scope; excludes isolated Jahez111204|
| salary_increase |512122|212103|Actual active rider in the same financial company|
| salary_decrease |212103|512101|Same rider/company checks|
| administrative_charge |112505|421302|Initial charge only; salary recovery credits112505 separately|

Drafts have no balance effect. `POST /api/finance/accounting-adjustments/{id}/approve` requires finance.journal.approve and an independent actor, then rechecks company/rider/bank scope and open period. Mada top-up rechecks the selected bank's **posted target-chart** balance in the locked transaction. Repeated approval does not post twice. GET collection is company scoped and paginated. The original journal is immutable; manual-journal endpoints refuse these system-generated IDs.

Bank codes111201/111202/111203 are selected explicitly; **legacy BANK.<company> money is not guessed into an unspecified actual bank**. Its approved mapping/opening must exist before target-bank funding can be used. Cash advances can use the proven MAIN debt-vault mapping. Target outflows retain a validated `legacyVaultCode` so legacy availability checks cannot spend the same funds again.

`GET /api/finance/mada/balance?companyId=...` requires advances.payout or treasury.view and the company's financial scope. Supervisors receive no balance access. Mada balances are isolated by company even though the operational machine alias is `MADA`.

## K2 integration contract (Claude owns request creation/review)

This work does **not** implement K2's request/catalog/review changes. Existing request creation still accepts cash/bank until K2 is merged. The new payout branch consumes an already approved request with:

- `type=internet`, `fulfilment=cash|bank|recharge_card`;
- `riderIqama`, companyId, amount, monthlyInstallment, requestedBy, approvedBy and normal decision history;
- `vaultCode` (or `vault` fallback): MADA for a card, MAIN.<company>.DEBT for cash, BANK.<company> for bank;
- approved `networkCode` and `packageCode` for a recharge card (K2 validates the catalogs).

Use dated routing `advance.payout:MADA` for the machine custodian, or the exact ordinary cash/bank vault operation. Payout requires the assignment **and** financial company scope; bank/cash also require their vault scope. Route registration now recognizes MADA without pretending it is a legacy cash account. POST `/api/advance-requests/{id}/payout` keeps `{reference,odooVoucherNo?}`; bank fulfilment additionally selects `bankAccountCode` explicitly. Card and cash ignore that field.

Every fulfilment debits112503. Card credits112404; cash/bank credit the selected funding account. A failed balance/period/scope check leaves the request approved and creates no debt/journal/voucher. A successful payout atomically creates one InternetAdvance debt, one target journal, one PV and the paid workflow transition. Installment recovery resolves112503 through the existing InternetAdvance category; no second debt is created for the same request. Company balance and replay checks run inside Store.Write's transaction/distributed lock.

The salary/admin structured postings are a standalone accounting path. K4's payroll-review API remains Claude's scope; reuse the account directions when integrating a cycle, and do not post the same adjustment twice through both paths. The approved account codes are available to K4 even without calling the standalone endpoints.
