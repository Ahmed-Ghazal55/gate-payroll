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

A nonzero CAR Deduction creates a formal open dispute with source line, month and amount, a company/platform-scoped notification and invoice status `disputed`. It is never charged to a rider or expense. BR §23.2 explicitly keeps the disputed balance in its receivable account until the platform replies. Accounting approval is therefore separate from the open dispute/settlement hold: when posting is enabled, the disputed part uses 112803 and the operational dispute remains open.

Every nonzero TGA allocation amount starts `pending_allocation`. The core split operation accepts either an explicit `riderPercent` (with a rider for a positive rider share), explicit `riderAmount` plus `companyAmount`, or `riders:[{riderId,amount}]` plus `companyAmount`. Methods cannot be mixed. Shares must equal the complete amount; no default split is applied. A company-level line can allocate to several explicitly selected riders. Proposals are retained by revision; only the latest pending revision can be approved, and an approved split requires a later correction/reversal workflow rather than overwrite.

The proposer cannot approve his own split, including an administrator. Approval uses the existing AccessPolicy checker with HS approval permission; nonadministrators also need the finance department/rank and company/platform scope. Approval updates workflow metadata and audit only; import/allocation still do not post with the gate closed. The review HTTP routes are added in M2.6.

## Stage-2 double-deduction protection (M2.3)
Approved facts create append-only `platform_deduction` documents whose `platformDeducted` key is `{category,rider,month}`. TGA rider shares require independently approved allocations; penalties and positive Rider Balance facts require the actual rider and an approved invoice. An unapproved invoice/proposal and a raw negative application balance create no active tags or rider debt. Repeating confirmation creates no new tags/audits.

The monthly Engine skips a matching negative adjustment or debt installment once the key is confirmed. Canonical categories map TGA/TrafficFine, platform penalties/AdministrativeDeduction, and RiderBalance/PlatformTransferredDebt. An explicit other-platform deduction is retained; another rider/month, a pending tag, an unrelated advance and a positive adjustment are also retained. Payroll preview fingerprints include the relevant tags so a change invalidates approval of stale output. Tags classify actual platform deductions; they do not open a wallet ledger or post journals.
