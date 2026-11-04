# HungerStation invoice intake (R2)

The ERP owns wallet/custody/remittance ledgers. This module stores reviewed invoice facts and later approved accounting entries; it does not create another mutable wallet ledger. HS posting defaults off. Importing a workbook never posts a journal or creates a rider debt.

## Signed workbook import

- `POST /api/platforms/hs/invoices/import/preview`
- `POST /api/platforms/hs/invoices/import/commit`

The preview input has `period` (`yyyy-MM`), `files:[{fileName,dataBase64}]`, optional `lineFacts` and `confirmActualRider`. Supply one to three XLSX files, with at most 8 MiB raw bytes in total; larger sources must be split. Each workbook maps its own CR to one existing company book. Required sheets: Co LVL, Rider LVL and Breakdown; Car Damage is retained separately when present. Column order is irrelevant. English header names after an Arabic label and month-tagged support/adjustment/deduction headers are recognized. A tagged month must equal the requested period.

The response contains normalized `rows`, `canCommit`, `fingerprint`, `errors` and counts. Errors have only file index, canonical sheet name, source row number and fixed code. VAT arithmetic, component sums, completed-order totals, duplicate keys, unknown CRs and wrong tagged months block the complete batch. `V != Z` and `T - Z != AA` produce flags only; they never become inferred deductions.

Confirm with the same `files` and `period`, plus the returned `rows`, `fingerprint` and `confirm:true`. The 20-minute signature binds the actor, normalized facts and current company/account/rider/invoice state. Tampered/stale previews cannot write. The natural invoice key is platform, company/CR and period (the URL ID is hashed); a repeat of the exact facts is unchanged, while different facts for an existing invoice require review rather than overwrite. Every new invoice, line and dispute is audited in the same transaction. Original bytes stay under the private runtime `platform-imports/hs/` directory, addressed by content hash; a rollback may retain evidence but rolls back all document/audit writes.

## Rider facts and source lines

Rider LVL IDs are platform-account IDs, not assumed identities. The registered account owner is never substituted for the actual operator. An unambiguous assignment covering the complete period can establish the operator; overlaps or partial histories remain unallocated. Explicit `lineFacts` may supply `companyCr`, `platformId`, `scheme`, `riderId`, `riderNet`, `companyCommission` and `platformAmountExclVat`. A supplied rider requires `confirmActualRider:true` and a delivery-rider record. Commission splits are approved facts, not an invented rate. Missing facts and an unknown/target scheme cannot silently become salary.

Kinds are `platform_invoice`, `platform_invoice_line`, `platform_dispute` and `platform_allocation`. Lines distinguish `company`, `rider` and `car_damage`. The company line retains Co LVL totals. When Rider LVL also supplies TGA, its sum must match the company total and only the rider lines are allocated; otherwise the company line owns the allocation amount. This prevents allocating both levels. Car Damage retains its own loading period/date and needs period review; CAR financing is a different field.

## CAR and TGA

A nonzero CAR Deduction creates a formal open dispute with source line, month and amount, a company/platform-scoped notification and invoice status `disputed`. It is never charged to a rider or expense. Ghazal clarified on 2026-11-04: the entire invoice journal is blocked until the dispute is resolved, including when the posting gate is enabled. The gate cannot override an open dispute. Independent facts approval with the gate off remains available without posting; a posting attempt while the dispute is open returns `409` and writes no invoice/journal/audit changes. Signed proposals expose `postingBlockedByDispute`, `postingAllowed` and `postingBlockers`. After independent documented resolution, a fresh signed proposal is required; any residual CAR claim uses `112803`, and a credit awaiting accounting reconciliation keeps its settlement hold.

**Exception (BR §23.2, Ghazal 2026-11-04):** when HungerStation rejects a CAR claim, an independent approver records `outcome=platform_rejected` with `platformReference` and `reason`. The dispute stays open as `escalated`, the invoice may post with W debited to `112803` against `112802`, and `settlementHold` stays true until the dispute is settled. Only CAR claims use this exception; any other open dispute still blocks posting. W is never charged to a rider.

Every nonzero TGA allocation amount starts `pending_allocation`. The core split operation accepts either an explicit `riderPercent` (with a rider for a positive rider share), explicit `riderAmount` plus `companyAmount`, or `riders:[{riderId,amount}]` plus `companyAmount`. Methods cannot be mixed. Shares must equal the complete amount; no default split is applied. A company-level line can allocate to several explicitly selected riders. Proposals are retained by revision; only the latest pending revision can be approved, and an approved split requires a later correction/reversal workflow rather than overwrite.

The proposer cannot approve his own split, including an administrator. Approval uses the existing AccessPolicy checker with HS approval permission; nonadministrators also need the finance department/rank and company/platform scope. Approval updates workflow metadata and audit only; import/allocation still do not post with the gate closed. The review HTTP routes are added in M2.6.

## Stage-2 double-deduction protection (M2.3)
Approved facts create append-only `platform_deduction` documents whose `platformDeducted` key is `{category,rider,month}`. TGA rider shares require independently approved allocations; penalties and positive Rider Balance facts require the actual rider and an approved invoice. An unapproved invoice/proposal and a raw negative application balance create no active tags or rider debt. Repeating confirmation creates no new tags/audits.

The monthly Engine skips a matching negative adjustment or debt installment once the key is confirmed. Canonical categories map TGA/TrafficFine, platform penalties/AdministrativeDeduction, and RiderBalance/PlatformTransferredDebt. An explicit other-platform deduction is retained; another rider/month, a pending tag, an unrelated advance and a positive adjustment are also retained. Payroll preview fingerprints include the relevant tags so a change invalidates approval of stale output. Tags classify actual platform deductions; they do not open a wallet ledger or post journals.

## Approved posting and correction (M2.4)
A signed posting proposal binds the approver, invoice facts, allocations, deductions, periods, gates and existing journals. Missing commission splits or an unknown/target scheme block approval with an Arabic error. A FIN checker must differ from the importer and every fact-revision proposer; administrators also cannot self-approve. Facts may be approved while the posting gate is off; enabling it later requires a fresh signed proposal and resolved invoice disputes.

Salary credits 411102; commission credits 212103 for the approved actual rider net and 411601 for the approved company commission. Both debit 112802; VAT credits 213201. CAR debits `112803` against `112802` only after documented dispute resolution; an open dispute blocks every invoice leg. Approved TGA debits 112502/526402 against 112802. All amounts must reconcile exactly; raw wallet balances never provide an inferred split. Car Damage rows stay separate for period/source review and are not automatically posted.

Preapproval corrections append `platform_invoice_action` fact revisions; original source lines remain byte-identical. Posted corrections require a separately approved reversal in an open period at or after the original period. The reversing journal links `reversesJournalId`; the original journal is never rewritten. A corrected workbook explicitly names `replacesInvoiceId`, can only follow its approved reversal, and posts in that open reversal period. Approved replacements append deduction-cancellation records and new tags without rewriting old deductions. Repeated approval/reversal is idempotent.

## Read-only ERP facts scaffold (M2.5 / CR-002)
The importer recognizes five kinds: `hs_wallet_event`, `hs_remittance`, `hs_remittance_allocation`, `hs_handover_review`, `hs_month_close`. CR-002 supplies required columns without relation names. Discovery uses schema metadata and requires exactly one complete table/view per contract; no match reports `source not available` (distinct from an existing empty view), and ambiguity stops the import. Fields are explicitly allowlisted, ordered by each immutable source key. Wallet RowVersion is retained as hexadecimal metadata; other binary payloads stay forbidden. Logs contain only typed aggregate counts/reasons.

Each document's normalized `sourceEventId` is its contract identity: SourceEventId / BatchId / AllocationId / ReviewId / CloseSnapshotId. An allocation's linked wallet SourceEventId remains separate inside sourceFacts. Exact repeats are unchanged; changing any imported approved fact stops the entire import and requires a new reversing source row. No delete, wallet balance update, debt or journal is generated by pulling facts. Duplicate identities/idempotency keys refuse the source.

Wallet approval needs ApprovedAt/ApprovedBy; remittance needs ConfirmedAt/ConfirmedBy; allocation also needs its confirmed parent batch and approved source event. The current review/month-close contracts do not guarantee an approver: they stay rejected with `approval_not_available` unless the source exposes ApprovedAt/ApprovedBy too. Missing/ambiguous legal company relations also block those facts. This is an ERP integration gap rather than invented approval.

Review-only proposals expose the CR-002 account mapping (112306,112501,112513,114107,114101,112802,112803,411601,411102,112507); they remain unpostable until signed event directions, counterpart legs, actual rider/company attribution and independent approval are supplied. Confirmed receipt additionally requires the exact company cashbox/bank. No source sign convention is guessed. D20 returns Expected=-C, RemainingOldCarry=C+A, CurrentPeriodBalance=M-(C+A); a missing C, A or M returns `unknown` and no posting. Only an attributed negative current-period result may propose112507; a raw negative application balance never becomes debt.

## Review API (M2.6)
All HS routes require a current authenticated user, explicit permission, legal-company scope and HungerStation platform scope. Existing users receive current role defaults on every cookie validation without rewriting their stored documents. Accountant gets view/manage; FinancialAccountant gets view/manage/approve; FinancialManager gets view; administrator gets all. Explicit denied permissions still win for nonadministrators. Approval also requires FIN/rank and an independent identity. Platform gate settings remain administrator-only.

| Method / path below `/api/platforms/hs` | Behavior |
|---|---|
| GET `/invoices`, `/disputes`, `/allocations` | Scoped review with items/total/page/pageSize; optional companyId/period/status, pageSize capped at200 |
| GET `/facts?kind=&page=` | Five CR-002 kinds only; never arbitrary document kinds; empty means no imported facts, not proof of current source availability |
| GET `/proposed-journals?invoiceId=` | One signed rows/fingerprint proposal, bound to the requesting approver |
| GET `/proposed-journals` | Paginated signed invoice proposals; `kind=` instead selects preview-only ERP fact projections |
| POST `/invoices/{id}/approve` | rows/fingerprint/confirm for independent facts approval/posting; alternatively reversalRequestId for independent reversal |
| POST `/invoices/{id}/dispute` | lineId/category/amount/reason; audited formal claim and scoped notice, no automatic journal or rider charge |
| POST `/invoices/{id}/resolve` | mode=facts appends lineFacts/reason; mode=request_reversal requires open period/reason; mode=dispute requires approval permission, disputeId/outcome=platform_credit or platform_rejected (CAR only)/platformReference/reason |
| POST `/allocations/{lineId}` | Explicit TGA split, retained by revision |
| POST `/allocations/{id}/approve` | Independent approval of the latest pending TGA split |

Recording a platform credit response resolves the formal dispute but never rewrites the original source lines/journal or assumes receipt. An already posted claim retains its settlement hold and `pending_reversal_or_credit_note` reconciliation until an approved correction supplies the accounting evidence; no cash receipt or credit-note journal is invented. Corrected workbook/reversal mechanics remain separate. Car Damage automatic posting and source-approved review/month-close contracts remain integration gaps.

Behavior checks cover signed tamper rejection, gate-off facts approval then gate-on posting, two users, company/platform denials, current-cookie scope refresh, closed-period reversal, original journal byte preservation, readable target journals in existing finance endpoints, OpenAPI routes and byte-identical restart of every synthetic document.
