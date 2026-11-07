# Debt workbook adapter (R6a-6)

Current target: **قاعدة الرواتب db77738** (BR33). Ghazal alone runs credential-dependent commands. No payroll database writes before the reviewed **«انقل»** cutover; ERP first-kind loads still need fresh **«حمّل»** afterward. Follow [DB-CUTOVER](../ops/DB-CUTOVER.md). Legacy identifiers `staging.env` / `STAGING_DB` are retained for compatibility. Historical counts below are dated evidence, not a fresh preview.

The CLI reads both source XLSX files in place. No source file is modified or copied into the repo. `debt-sheet-mapping.v1.json` is a **draft** containing the actual column headers and proposed categories/accounts/signs. It requires Ghazal's approval; unresolved labels and the ERP HungerStation-wallet 112506 versus target HR 112506 conflict remain `needs_decision`. An effective opening month must also be chosen.

## R6c continuation — approved October decisions (2026-11-07)

This section supersedes the paused D1.6 decision below. Mapping metadata is approved by Ghazal: `octoberLoadMode=included_in_net`, `effectiveFrom=2026-10`, balance date 2026-10-31. **None of the 394 October requests creates a debt or cash/journal entry.** Odoo request references remain private reconciliation lineage. The prior heuristic/20 ambiguous groups are retained as evidence, with no load action.

`DebtCreditorHistory` reads only allowlisted columns in app.Sponsors, Employees (all statuses/deleted), optional PayrollEmployees, RiderProfiles and EmployeeSponsorshipPeriods. It selects the latest period ending by the balance date, otherwise latest known. Missing periods are reported unavailable; current employee sponsor is an explicit latest-known fallback, never fabricated history dates. Two company candidates or absent evidence remain blocked. Only unresolved creditors are searched; sheets then current payroll rider records retain precedence. Report evidence contains table/column names, not source personal values. All credential-dependent commands below are **operator only**.

`PreviewReviewedOpening` shares the existing FIFO allocator. Each ready row is independent: invalid/duplicate identities, negative buckets/payments, creditor conflicts and unresolved creditors stay blocked; archive net differences retain the main net. No active rider is created. Missing identities get an inactive debt-only rider atomically with their accepted documents. Zero balances add nothing, excess payment is separate rider_credit. New debts stay pending_approval/openingReviewRequired, with no GL/cash movement. Installments need opening review; no installment amount is invented.

A preview fingerprint binds source bytes, mapping and accepted row fingerprints. Replay is per source row/category, so fixing other rows later never replaces an already imported row. Changed previously imported financial rows are refused. `--commit` requires the fingerprint, `--after-db-move` and a target cutover receipt before Store initialization; writes are serialized and verified in the same transaction. `--verify` uses SELECT only. The agent has **not run a real commit** or completed the live ERP-history/payroll preview.

Offline reconciliation reproduces approved394 /112,202.64, rejected-sheet omissions4, draft1, carried19 /4,478 with3 already in totals and16 earlier-period items. It normalizes explicitly recorded app-label aliases and reads carried values from their actual shifted columns. A real formatted-summary difference remains: cash+115 and internet−115; company sheets, receipt totals and per-rider/type detail agree. Ghazal approved company sheets, receipt totals and per-rider/type detail as authoritative on2026-11-07; formatted-summary differences are warnings only, retained in JSON/XLSX. They do not block otherwise ready opening rows. Offline output is `r6c-debts-continuation-offline.{json,xlsx}` so Claude's prior live lookup report is preserved.

### Operator commands — not executed against real databases by Codex

**Ghazal or Claude runs preview**, after building this worktree, then reviews counts/category totals/blocked reasons. Use the same inputs for all following commands:
```bash
cd "$HOME/Projects/active/gate-payroll-r6c"
debt_args=(--debt-sheet "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx" --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" --mapping docs/data/debt-sheet-mapping.v1.json --lookup-riders --lookup-erp-history --report "$HOME/Private/gate-data/sync-logs/r6c-debts-review.json")
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --dry-run
```
**STOP** until canCommit=true, the database move is complete and Ghazal gives fresh «حمّل». Then **Claude runs**, using the exact fingerprint from the reviewed output:
```bash
read -r -p 'Reviewed previewFingerprint: ' debt_preview_fingerprint
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --commit --after-db-move --preview-fingerprint "$debt_preview_fingerprint"
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --verify --preview-fingerprint "$debt_preview_fingerprint"
```
Require verified=true, missing=0, changed=0, totals=preview. Re-running the identical approved commit adds0. If new evidence changes accepted rows, create a fresh preview and approval; never reuse an obsolete fingerprint. These commands only read ERP; writes target db77738.

## Historical R6c — paused after D1 step 6 (2026-11-07)

This section supersedes the older company-from-branch proposal below. The corrected source is `~/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx`; both workbooks stay read-only in place. `octoberLoadMode=pending_decision` prevents the legacy adapter from committing even if the mapping otherwise passes. D1 steps 7–11 and D2–D4 have **not** been implemented or run in this session.

`--review-through-six` extends the existing debt adapter: immutable byte snapshots and SHA-256/sheet manifests; cached-value-only formula handling; exact exponent expansion and identity normalization; all three row equations; archive joins by column **headers**; creditor precedence; conservative per-rider October evidence. Missing formula caches block their row. Invalid and duplicate identities, negative buckets/payments and archive conflicts retain separate reasons. Archive net differences preserve the main-sheet net and both archive comparisons for review. No FIFO allocation, deduction reconciliation, load planning or database write is performed in this mode.

Creditor precedence is the company archive sheet (EX/TR), then the payroll rider's `financeSponsorId`, then `sponsorId`, else blocked. Only exact company IDs/codes are accepted from rider records. Sheet/record disagreements keep the sheet and flag `cross_company`. Neither a name, branch nor the deductions workbook's company column is evidence. Approved October requests come only from the three company sheets (EX/BW/TR), keyed by their payment request reference; duplicate/missing references refuse further progression rather than silently removing a transaction. Per-company rider counts can overlap; request counts and amounts are additive.

`included_in_net` requires an explicit net-inclusion note naming October **and all request references**, distinct matching unpaid buckets, enough net and a compatible known metadata date. Equal amounts alone, partial/negative notes, duplicate identities, invalid request identity/date, or negative source inputs remain `ambiguous`. No matching amount/note is only a **heuristic**, not proof of exclusion. The real debt workbook has no export-date metadata, so date evidence is unavailable; the agreed **balance cutoff** 2026-10-31 is not invented as its export date. No real request was proven included.

### Verified offline result, steps 1–6 only

- Main sheet: 808 rows / 807 unique normalized identities; raw cached net rounded once = **2,701,431.66**. Sum of row-rounded nets = **2,701,431.65**; the 0.01 difference comes from 158 subcent cached values. No row equation mismatch.
- K5 reproduced: 93 flagged rows; invalid identity 5, duplicate rows 2, negative payment 16, negative bucket 2, both archives 2. `archive_net_differs` = **74 unique main rows / 76 archive comparisons**; two main rows differ against both archives. These are not 76 different people.
- Approved October: 394 requests / **112,202.64**, 275 normalized identities. K5's heuristic is reproduced exactly: 266 / 102,857.64 separate, 9 / 9,345.00 ambiguous.
- Strict result: **255 / 99,925.14 separate**, **20 / 12,277.50 ambiguous**, 0 proven included. Eleven extra ambiguous groups: ten have negative inputs (one also mentions October), one has an invalid request identity. These are explained changes to the K5 heuristic, not unexplained reconciliation differences.
- Separate / ambiguous totals by creditor sheet: BW **34,321.59 / 7,195.00**; EX **45,679.10 / 3,394.50**; TR **19,924.45 / 1,688.00**. Details and evidence codes are private, referenced by sheet and Excel row only.
- Offline creditor resolution: EX 66 rows / 213,790.53; TR 55 / 231,741.45; blocked 687 / 2,255,899.67 (row-rounded totals). **455 nonzero rows need rider-record lookup**. It has not run; cross-company results are therefore incomplete. Two invalid archive identities and one invalid approved request identity remain flagged.
- Private outputs only: `~/Private/gate-data/sync-logs/r6c-debts-review.json` and `.xlsx` (files 600, directory 700). JSON has SHA-256 and sheet manifests, row reasons, both nets, creditor source, October evidence and strict/K5 differences. XLSX contains the four K5 review sections relevant through step 6; it omits names, identities, raw notes and voucher references. No source is copied or changed.

### Exact next commands and decisions — in order

**Ghazal runs** this credential-dependent, SELECT-only rider lookup; Codex has not run it. It does not initialize Store or access ERP. It updates the same private reports; no payroll writes:

```bash
cd "$HOME/Projects/active/gate-payroll-r6c"
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --dry-run --review-through-six --lookup-riders \
  --debt-sheet "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx" \
  --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" \
  --mapping docs/data/debt-sheet-mapping.v1.json \
  --report "$HOME/Private/gate-data/sync-logs/r6c-debts-review.json"
```

**Ghazal decides** October mode: (a) separate October items on top of the opening balances, with phase by type; or (b) already in net, so do not load them. The evidence cannot automatically settle the 20 ambiguous groups. Also specify the effective opening month before a later load. No decision or permission is inferred here.

**Codex waits here.** Only after the October decision: continue D1 step 7 (deduction reconciliation), then 8–10 (mapping, existing FIFO/credit, full database preview and review). Step 11 needs completed «انقل» and a fresh «حمّل». D2 → D3 → D4 follow D1; their code/catalog status is unchanged.

## Historical R6a implementation — FIFO rule (2026-11-05)

1. Read debt buckets left to right in column order (oldest first).
2. Add monthly deductions, app deductions, Petro payments, cash payments and debt reductions; allocate that total to each oldest bucket up to its value, then continue to the next.
3. Combine the remaining buckets by category and target account. Zero balances create no record. References are `DEBT-SHEET:<Excel row>:<category>`.
4. An excess payment creates a separate `rider_credit` with reference `DEBT-SHEET:<row>:CREDIT`; it never creates a negative debt or changes a debt category.
5. Validate `sum(category opening balances) - separate rider credit = sheet net`, rounded to 0.01 with absolute tolerance ≤ 0.01. Check gross and deduction totals independently. A negative debt/payment input requires a decision; it is not silently reclassified.

The row report contains only Excel row numbers and fixed aggregate reason counts. It includes company/category totals, accepted debt/credit totals and unresolved **column letters**. Raw names, identities, cell values, row amounts and workbook fingerprints never appear in the report. Formula cells are read only from cached numeric values; absent caches in relevant financial columns block the row. No formula is executed. Changed/missing headers, unsupported versions, stopped/nonposting accounts, duplicate identities and contradictory category/account mappings are rejected.

Monthly deduction flows are reconciled independently from the historical opening stock. The detail sheet `تفصيلي الاستقطاعات` must total correctly by row; columns C–G are compared against the explicit total cells and proposed company ranges in `اجماليات السلف والاستقطاعات`. The latter is a formatted summary rather than a row-1 table. Source control coordinates are included in the draft for review. Source `الاجماليات` is read separately for payment-reference counts, company totals and grand-total reconciliation. Exact nonpersonal company-label aliases can be proposed in `companyAliases` (source label → existing sponsor code); they are part of the same mapping approval and never default to a company. Its explicit company evidence attributes detail rows only when all receipts for that rider agree; ambiguous or absent company evidence is reported by row number, never replaced by a guess. No unrelated ERP Jahez request is treated as an ADV payment request; current ERP metadata has no authoritative ADV request table, so that comparison is explicitly unavailable.

```bash
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --live --consistent --dry-run \
  --debt-sheet "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx" \
  --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" \
  --mapping docs/data/debt-sheet-mapping.v1.json \
  --report "$HOME/Private/gate-data/sync-logs/r6a-debt-sheet-preview.json"
```

Dry-run opens SELECT-only payroll database access and never constructs `Store`. Approval is explicit metadata (`status=approved`, `approvedBy=Ghazal`) applied only after Ghazal reviews the exact mapping. A draft, unresolved mapping, missing effective period, row mismatch or control mismatch refuses the whole commit. Only after mapping approval **and a fresh «حمّل»** may the operator replace `--dry-run` with `--commit`.

Commit rebuilds the preview from the files and current payroll database rider mapping. Inside one serialized transaction it checks the rider/company again and refuses changes to a previously imported row fingerprint. It preserves idempotency by source reference, stages category balances as `debt` with `entryMode=OpeningBalance`, and stages overpayments separately as `rider_credit`. Records remain `pending_approval` / `openingReviewRequired`: importing this historical evidence does not choose an equity/credit posting counterpart, activate repayment deductions, create cash movements, or post a journal. Financial opening review is a separate action; no unspecified counterpart is invented. The original files and per-row lineage remain available privately for that review.

## R6a fix round 1 preview (2026-11-05)
Mapping decisions are proposed_approved, with no unresolved account columns. This does not authorize loading: effective month and final approval/reconciliation still apply. Unknown valid riders are planned in memory as inactive debt-only identities and only created atomically with opening debts after a fresh approved load. Reactivation reuses their identity. Company aliases are exact approved labels, never inferred from personal names.
Latest SELECT-only preview: 808 rows, 370 valid / 438 flagged (417 unmatched_company, 16 negative_bucket_needs_decision, 5 invalid_identity); 0 planned inactive riders and 0 company_from_branch rows. Column D is blank in 707 rows; the 101 populated labels do not exactly match the approved aliases. Missing/conflicting company evidence stays blocked; do not guess aliases or companies. Control reconciliation remains blocked (23 flagged control row numbers). No payroll database writes.
