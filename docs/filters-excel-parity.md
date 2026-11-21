# Excel AutoFilter Parity Report (Task FL2)

## Architecture & Library Choice

- **Library:** `@tanstack/react-table` pinned to exact `8.21.3` (`@tanstack/table-core@8.21.3`).
- **Why TanStack Table v8:** Headless core with zero CSS lock-in, full React 19 / Next.js 16 compatibility, built-in column filtering, sorting, and faceted unique/min-max models, paired with our own Arabic/RTL Excel-style popover (`web/src/components/ui/ColumnFilterMenu.tsx`) matching the existing design system (`--surface`, `--raised`, `--line`, `--accent`, logical CSS).
- **Bundle size:** `@tanstack/react-table` + `@tanstack/table-core` is ~138 KB unminified ESM (**27.3 KB gzipped** raw; ~14–16 KB gzipped after Next.js Turbopack tree-shaking of unused row models).
- **Shared primitives:**
  - `web/src/lib/columnFilter.ts`: Pure filter/sort/distinct/serialization engine (Arabic collation via `Intl.Collator('ar', { numeric: true, sensitivity: 'base' })`, Arabic-Indic/Persian digit normalization, alef/taa marbuta/diacritics normalization, blank handling, label↔value mapping, semantic tone inference, and server contract serialization).
  - `web/src/components/ui/ColumnFilterMenu.tsx`: Shared Excel AutoFilter popover (`ColumnFilterMenu`) plus lightweight hook/header/bar (`useTableAutoFilter`, `AutoFilterTh`, `AutoFilterBar`) for custom `<table>` elements.
  - `web/src/components/ui/DataTable.tsx`: Shared table component wired to TanStack Table v8, `ColumnFilterMenu`, quick-filter row (`SearchableSelect` for `list` columns), URL state sync, totals footer, and Excel export.
  - `Payroll.Api/TableFilterContract.cs`: Generic server contract (`filters=[{col,op,value,valueTo,values}]`, `sort=[{col,dir,tone?}]`, and faceted `BuildDistinct` with counts) enforcing per-endpoint column whitelists, caller company/platform scope, and endpoint permissions.

## Excel AutoFilter Feature Comparison

| Excel AutoFilter Feature | Status | Implementation / Details |
|---|---|---|
| Sort A → Z / Z → A (Text) | **Done** | Type-aware Arabic collation (`Intl.Collator('ar', { numeric: true, sensitivity: 'base' })` on client, `ar-SA` `CompareInfo` on server); blanks always stay at the bottom just like Excel. |
| Sort Smallest → Largest / Largest → Smallest (Numbers) | **Done** | Numeric parsing (`parseNumberValue` / `TryParseDecimal`) handling commas, currency suffixes, and Arabic-Indic digits; blanks always at bottom. |
| Sort Oldest → Newest / Newest → Oldest (Dates) | **Done** | Timezone-safe UTC day epoch (`parseDateValue`) / `DateOnly` comparison supporting ISO dates, `YYYY-MM` periods, and `DD/MM/YYYY`; blanks always at bottom. |
| Sort by Color (Cell / Badge Tone) | **Done** | When a column renders semantic badge tones (`toneOf` or status inference: `success`, `info`, `accent`, `warning`, `danger`, `neutral`), choosing a color tone places matching rows at the top, both locally and via `sort=[{col,dir,tone}]` on server tables. |
| Filter by Color (Cell / Badge Tone) | **Done** | Swatch pills with per-tone counts inside `ColumnFilterMenu`; filters locally via `toneOf` and on server via `filters=[{col,op:'color',value:tone,values:[...]}]`. |
| Text Filters: Contains (`يحتوي على`) | **Done** | Normalized Arabic/digit substring match against both displayed Arabic label (`labelOf`) and raw stored value, client + server. |
| Text Filters: Does Not Contain (`لا يحتوي على`) | **Done** | `notContains` operator, client + server. |
| Text Filters: Begins With (`يبدأ بـ`) | **Done** | `beginsWith` (alias `startsWith`) operator, client + server. |
| Text Filters: Ends With (`ينتهي بـ`) | **Done** | `endsWith` operator, client + server. |
| Text Filters: Equals / Not Equals (`يساوي` / `لا يساوي`) | **Done** | `eq` / `neq` exact normalized match (prevents `active`/`نشط` from matching `inactive`/`غير نشط`), client + server. |
| Number Filters: `=`, `≠`, `>`, `≥`, `<`, `≤` | **Done** | `eq`, `neq`, `gt`, `gte`, `lt`, `lte` operators with strict numeric comparison (ID/Iqama/Phone/IBAN columns stay text), client + server. |
| Number Filters: Between (`بين قيمتين`) | **Done** | Inclusive dual-input `between` (`value` + `valueTo`), auto-handles swapped bounds, client + server. |
| Number Filters: Top N (`أعلى N قيم`) | **Done** | `topN` computes the N-th highest threshold across non-blank values (`computeTopNThreshold` / server `topNThresholds`), client + server. |
| Number Filters: Above / Below Average | **Not implemented** | Not requested in FL2 binding scope; easy to add later as `aboveAvg`/`belowAvg` operators in `columnFilter.ts` and `TableFilterContract.cs` if needed. |
| Date Filters: Equals / Not Equals / Before / After / Between | **Done** | `eq`, `neq`, `before`, `after`, `between` using native `<input type="date">` pickers, client + server. |
| Date Filters: This Month / Last Month | **Done** | `thisMonth` and `lastMonth` relative to Riyadh time (`UTC+3` on server, current date on client), client + server. |
| Date Filters: Hierarchical Year → Month → Day tree | **Not applicable** | ERP tables already provide explicit period/month selectors plus `between`, `before`, `after`, `thisMonth`, and `lastMonth` operators; flat distinct values with search box keeps the popover compact on 14" laptops and RTL viewports. |
| Distinct Value Checkbox List with Counts | **Done** | Every column popover shows sorted unique values with occurrence counts (`extractDistinctOptions` locally; faceted `GET .../distinct` with `counts` on all 5 server-paged tables). |
| Value List Search Box + Select All (`تحديد الكل`) | **Done** | Arabic/digit-normalized search input above checkbox list; master checkbox with indeterminate state (`تحديد الكل` / `تحديد كل نتائج البحث`). |
| Enter in Search Box → "Select All Search Results" | **Done** | Pressing `Enter` in the value search box (or clicking `تحديد نتائج البحث فقط (N)`) immediately selects all matching results' underlying raw values; hovering any value also offers a 1-click `فقط` ("Only") action. |
| Search & Display via Arabic Label ↔ Stored Raw Value | **Done** | Columns declare `labelOf(value)` (and `TableFilterContract.DisplayLabel` on server) so users search and see Arabic labels (e.g. `بانتظار الاعتماد`, `شركة البوابة`) while filtering applies to the underlying stored key (`pending_approval`, `BW`). |
| Blanks (`(فارغ)`) Handling | **Done** | Blanks appear at the end of the checkbox list as `(فارغ)`, can be included/excluded, and always sort to the bottom in ascending and descending order. |
| Per-Column Clear & Global Clear All (`مسح كل الفلاتر`) | **Done** | Per-column «إعادة تعيين» clears both filter and sort on that column; table toolbar / `AutoFilterBar` shows active filter/sort count and «مسح كل الفلاتر». |
| Active Filter / Sort Visual Indicator on Header | **Done** | Filtered/sorted headers highlight (`bg-[var(--accent)]/5`), show active sort arrow (`aria-sort`), and display an accent dot + highlighted filter icon. |
| Multi-Column AND + Within-Column OR + Page Search | **Done** | Filters combine with `AND` across columns and with page-level global search; checkbox selections within a column combine with `OR`. Faceted distinct endpoints apply other columns' filters while keeping the current column's full options visible. |
| Exports Use Active Filters & Sort | **Done** | Client `DataTable` exports `sortedData` (filtered + sorted); server-paged tables (`debts`, `workforce`, `operations`, `broadcasts`, `service-requests`) and `companies` pass active `filters` and `sort` to their export calls. |
| Server-Side Execution with Whitelists, Scope & Permissions | **Done** | All 5 server-paged endpoints enforce strict per-endpoint column whitelists (400 on unknown column), caller company/platform scopes, sensitive-field stripping (e.g. `riders.iban.view`), and exact list permissions on both list and `/distinct` endpoints. |
| Custom Filter with Wildcards (`*`, `?`) | **Not applicable** | Covered by explicit `contains`, `notContains`, `beginsWith`, `endsWith`, `eq`, and `neq` operators, which are clearer for Arabic ERP users and safer to translate across SQL/JSON evaluators. |
| Font Color Filter / Conditional Formatting Icon Filter | **Not applicable** | Web UI cells use semantic badge tones (`toneOf`) rather than arbitrary Excel font colors or icon sets; Filter/Sort by Color covers all colored badges and status cells. |
| Multi-Column Sort Stack UI (Custom Sort Dialog) | **Partial (Backend Ready)** | Server contract accepts `sort=[{col,dir,tone?}, ...]` arrays; UI currently triggers single-column primary sort (plus optional color priority) to match Excel's single-click AutoFilter header menu without extra modal complexity. |
| Keyboard Navigation & Accessibility | **Done** | Portaled popover (`role="dialog"`), `Escape` and click-outside dismiss, `aria-sort` on headers, accessible Arabic `aria-label` on every button/input, and logical RTL/LTR positioning with upward flip near viewport bottom. |
