# Release 0.32.4 (2026-11-22)

Cascading column filters and one responses table per form (DEP14), branch `fix/cascading-column-filters` from the forms session. Claude (pro2) reviewed it independently; no fix round was needed.

## What changed for users
- Column filters work like Excel: each column's value list shows only the values left after the other active column filters and the page search. The column you filter keeps all its own values. This applies to every client table (`DataTable`, `useTableAutoFilter`). Server-paged tables already sent the other filters to `/distinct`; a test now covers it.
- Rider app → Forms → Responses: the table shows one campaign or builder form at a time, picked at the top («اختيار الحملة أو النموذج», with its reply count). There is no "all campaigns" view now. The columns come from that form's questions: work-system campaign = «نظام العمل» + city/phone if asked; IBAN campaign = phone, city, IBAN, platforms; builder form = one column per question (list answers in Arabic, number columns with a total, dates). Name and iqama stay as fixed columns.
- A line under the picker shows the form type, open/closed, and the reply / pending counts in your company scope.
- Export (Excel/CSV) uses the same columns as the screen, one file named after the form. Number answers export as numbers; iqama, phone and IBAN stay text. IBAN still appears only with `riders.iban.view`.
- Review, approve, bulk approve/delete stay on campaigns. Builder-form replies are view and export only (no row actions, no selection).
- `?form=<id>` in the link opens the same table.

## API
- New `GET /api/responses/forms` (`forms.view`): every campaign and published builder form with its column list and scoped counts. Drafts are hidden. Read-only.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes.

## Ops for Ghazal
- None required.

## Known small points (not blocking)
- Builder-form replies have no review step, but their status shows «قيد المراجعة» and they count as pending.
- A builder question whose id is also a row field (for example `status` or `title`) shows the row value in its column.
