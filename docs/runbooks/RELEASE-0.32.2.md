# Release 0.32.2 (2026-11-21)

Screen QA fixes (DEP12). Antigravity checked every screen of 0.32.0 (AQ1, 20 rows, synthetic data). Cursor fixed them (AQ1F), Claude reviewed, Cursor ran one fix round (AQ1G: 3 regressions and the rows that were only partly fixed). Web only, plus one API message text.

## What changed for users
- Tables: money columns stay under their headers (the `td.num` merge bug is gone). Footer totals show «ر.س» only on money columns; counts stay plain numbers.
- Treasury: cashboxes (1110–1120) and banks (1130) are labelled correctly; the account drawer shows an Arabic account type. The Jahez and «إضافات» forms have Arabic labels, company pickers instead of a typed `BW`, and no raw `{}` JSON. Jahez reports show Arabic statuses and totals names. The bank file shows a calm «اختر شركة كفيلة» prompt instead of a red error.
- Dashboard and Admin audit: Arabic action and actor names (the raw key is in the hover title), readable Gregorian date/time, short IDs on mobile, `@username` shown left-to-right. The Users and Audit tables have a real Excel export with Arabic headers. Admin cards load once; one main button per tab.
- Payroll: blocker codes in Arabic; English words and spec references (`BR §…`, `R4…`, `Level N`, `Maker-Checker`) removed from the screen; the Ninja import button shows only on Ninja; the blocker badge is readable.
- Debts: promissory note statuses in Arabic; money footers with «ر.س».
- Accounting: account counts come from the data; trial balance hides zero rows by default, filters all rows and then shows 75 per page; export takes all filtered rows. Manual journal IDs are short.
- Companies: one import button for the Excel file (`ImportUploadDialog`); the ministry certificates plan moved to «المزيد».
- Workforce: phones show `+966…` left-to-right; the «القضايا» tab stays visible for users with the legal-cases permission; a clear «يحتاج اتصال حي» message when the live ERP is not reachable.
- Rider statement: a real Excel export, debit/credit spacing.
- Header breadcrumbs match the sidebar; the role shows in Arabic. The sidebar now lists every tab (Ninja invoice upload, debt actions, manual journals, Mudad adjustments, lists, approval bypass). Active tabs scroll into view on mobile.
- Small texts: rider form iqama placeholder spacing; the column filter search hint in Arabic; empty values in filters read «(بدون قيمة)» and are not shown as selected.
- API: the 503 message when the voucher signing key is missing is now plain Arabic (no config key name).

## Not in this release (decisions for Ghazal)
- Duplicate in-page top tab bars (the sidebar now lists every tab). Recommendation: remove them like rider-app (page-tabs-rule 3.0); keep in-page tabs only for inner views (`?app=`, `?sub=`).
- Header period vs page period pickers. Recommendation: one period in the URL; pages default to the header period and changing either updates both.

## API
- No new or changed routes or schemas. `Vouchers.cs`: message text only.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes.

## Ops for Ghazal
- None required.
