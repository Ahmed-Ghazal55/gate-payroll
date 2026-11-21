# Release 0.32.0 (2026-11-21)

Operations screen fixes and an Excel-style column filter for every table (branch `fix/ops-ui-and-filters`: OF1 + FL2 built by Antigravity/Gemini, reviewed by Gemini (OF1R, 24 findings) and fixed in the same branch; independent review, merge and release by Claude in DEP10).

The import-centre removal (`feat/remove-import-centre`) is not part of this release: it was already released in 0.30.0 (#56).

## What changed for users
Operations (read-only, live from Omar's ERP):
- Vehicle purchase invoices: every receipt of the period appears (matched by supplier bill date OR received date, Riyadh time), not only one.
- Vehicle drawer: rider assignment history is an Arabic table (rider, iqama, from, to, real rider yes/no), newest first. No raw JSON anywhere on screen.
- Fuel: the iqama shows under the rider name; a «اسم البطاقة» column (Omar's card table has no name field, so it shows the provider in Arabic plus the card notes); statuses in Arabic (نشطة / غير نشطة).
- New Excel exports for the fuel log, oil changes and purchase invoices; every export uses the toolbar search and the active column filters.
- Summary cards recount when column filters are applied; the oil-changes view has its own cards.

All tables:
- Every column header has an Excel-style menu: sort A→Z / Z→A (Arabic order), smallest→largest, newest→oldest, sort by colour; filter by colour; number filters (=, ≠, >, ≥, <, ≤, between, top N); date filters (before, after, between, this month, last month); text filters (contains, does not contain, begins with, ends with, equals); a value list with checkboxes, counts, «(فارغ)», search and «تحديد الكل»; clear per column and «مسح كل الفلاتر».
- Server-paged tables (workforce, debts, operations, rider-app notices and requests) run the same filters and sorts on the server; the value lists come from `GET …/distinct`.
- Toolbar filters for lists (company, status, kind, …) are type-in searchable dropdowns. The header company picker lists companies from the API.
- Money cells and totals use the shared «ر.س» formatter. Totals skip non-additive columns (odometer, ratios, running balance).

## API
- New: `GET /api/workforce/distinct`, `/api/debts/distinct`, `/api/operations/distinct`, `/api/rider-mobile/broadcasts/distinct`, `/api/rider-mobile/service-requests/distinct`; `GET /api/operations/fuel-log/export`, `/api/operations/vehicles/oil-changes/export`, `/api/operations/vehicles/purchase-invoices/export`.
- List endpoints accept `filters=[{col,op,value(s)}]` and `sort=[{col,dir,tone}]`. Columns are whitelisted per route (unknown column → 400); at most 30 filter clauses, 500 values per clause, 10 sort clauses, 500 characters per value. Same permission and company scope as the list. Routes without a whitelist reject `filters` (400).
- IBAN filters return nothing for users without `riders.iban.view` (the value is removed before filtering).

## Known limits (follow-up, not blockers)
- Live operations and workforce filters with column filters read the full scoped set from Omar's ERP and filter in memory (no SQL push-down). Fine for today's sizes (≈2,200 people, ≈560 vehicles); revisit with the volume rule.
- Audit log (Administration): column filters apply to the loaded page only; the server already filters by search and action.
- `/api/debts` without `page` ignores `sort` (default order).
- Web filter tests cover the filter logic (pure functions), not a rendered component.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes. ERP reads stay read-only through `Operations.OpenErp`.

## Ops for Ghazal
- None required. Optional: open Operations → Vehicles → purchase invoices for November and check the count against Omar's receipts screen.
