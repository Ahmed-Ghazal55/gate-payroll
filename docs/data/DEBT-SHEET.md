# Debt workbook adapter (R6a-6)

The CLI reads both source XLSX files in place. No source file is modified or copied into the repo. `debt-sheet-mapping.v1.json` is a **draft** containing the actual column headers and proposed categories/accounts/signs. It requires Ghazal's approval; unresolved labels and the ERP HungerStation-wallet 112506 versus target HR 112506 conflict remain `needs_decision`. An effective opening month must also be chosen.

## FIFO rule (Ghazal, 2026-11-05)

1. Read debt buckets left to right in column order (oldest first).
2. Add monthly deductions, app deductions, Petro payments, cash payments and debt reductions; allocate that total to each oldest bucket up to its value, then continue to the next.
3. Combine the remaining buckets by category and target account. Zero balances create no record. References are `DEBT-SHEET:<Excel row>:<category>`.
4. An excess payment creates a separate `rider_credit` with reference `DEBT-SHEET:<row>:CREDIT`; it never creates a negative debt or changes a debt category.
5. Validate `sum(category opening balances) - separate rider credit = sheet net`, rounded to 0.01 with absolute tolerance ≤ 0.01. Check gross and deduction totals independently. A negative debt/payment input requires a decision; it is not silently reclassified.

The row report contains only Excel row numbers and fixed aggregate reason counts. It includes company/category totals, accepted debt/credit totals and unresolved **column letters**. Raw names, identities, cell values, row amounts and workbook fingerprints never appear in the report. Formula cells are read only from cached numeric values; absent caches in relevant financial columns block the row. No formula is executed. Changed/missing headers, unsupported versions, stopped/nonposting accounts, duplicate identities and contradictory category/account mappings are rejected.

Monthly deduction flows are reconciled independently from the historical opening stock. The detail sheet `تفصيلي الاستقطاعات` must total correctly by row; columns C–G are compared against the explicit total cells and proposed company ranges in `اجماليات السلف والاستقطاعات`. The latter is a formatted summary rather than a row-1 table. Source control coordinates are included in the draft for review. Source `الاجماليات` is read separately for payment-reference counts, company totals and grand-total reconciliation. Exact nonpersonal company-label aliases can be proposed in `companyAliases` (source label → existing sponsor code); they are part of the same mapping approval and never default to a company. Its explicit company evidence attributes detail rows only when all receipts for that rider agree; ambiguous or absent company evidence is reported by row number, never replaced by a guess. No unrelated ERP Jahez request is treated as an ADV payment request; current ERP metadata has no authoritative ADV request table, so that comparison is explicitly unavailable.

```bash
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --live --consistent --dry-run \
  --debt-sheet "$HOME/Private/gate-data/imports/debts-2026-11.xlsx" \
  --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" \
  --mapping docs/data/debt-sheet-mapping.v1.json \
  --report "$HOME/Private/gate-data/sync-logs/r6a-debt-sheet-preview.json"
```

Dry-run opens SELECT-only staging access and never constructs `Store`. Approval is explicit metadata (`status=approved`, `approvedBy=Ghazal`) applied only after Ghazal reviews the exact mapping. A draft, unresolved mapping, missing effective period, row mismatch or control mismatch refuses the whole commit. Only after mapping approval **and a fresh «حمّل»** may the operator replace `--dry-run` with `--commit`.

Commit rebuilds the preview from the files and current staging rider mapping. Inside one serialized transaction it checks the rider/company again and refuses changes to a previously imported row fingerprint. It preserves idempotency by source reference, stages category balances as `debt` with `entryMode=OpeningBalance`, and stages overpayments separately as `rider_credit`. Records remain `pending_approval` / `openingReviewRequired`: importing this historical evidence does not choose an equity/credit posting counterpart, activate repayment deductions, create cash movements, or post a journal. Financial opening review is a separate action; no unspecified counterpart is invented. The original files and per-row lineage remain available privately for that review.
