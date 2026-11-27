# Operations import (actual operators)

## Ninja / HungerStation

`POST /api/pay/apps/{ninja|hs}/operations/import/preview` accepts JSON `{invoiceId,dataBase64}` (one XLSX, ≤8 MiB). Use the English catalog template, remove its marked example, and enter local `platformAccountId`, actual operator `iqama`, period, orders, baseGross, deduction, days and one of the six pay categories.

`POST .../commit` accepts the identical invoiceId/dataBase64 plus returned rows/fingerprint and `confirm:true`. HMAC binds actor, bytes, rows and current source state. Saves `operations_import` and Audit only. No cycle, journal, installment or debt is created.

## Keeta (register sheet redo)

`POST /api/pay/apps/keeta/operations/import/preview` accepts JSON `{invoiceId,register,dataBase64}`:

- **register** (required before upload): `EX` / `BW` / `TR` (or Express / Albwaba / Togary). Must match the approved invoice company.
- **One sheet = one register.** A single-sheet workbook is accepted as-is. A multi-sheet workbook must contain the register sheet name (`Express` / `Albwaba` / `Togary`); helper sheets are ignored for parsing selection.
- **Columns:** Arabic headers A..AH in the same order as the register sheets of Ghazal’s Keeta ops workbook. Trailing comparison columns are ignored. Download `/api/imports/templates/operations-keeta`.
- **Rows:** one row per rider per ID (the same ID may repeat). Dismissed IDs (`الحالة` false) that still have performance in the month are allowed and flagged (`dismissed` on `displayRows` and under rider `metrics`).
- **Dates:** `تاريخ الاستلام1` / `تاريخ التسليم` kept as real start/stop on rider metrics. The span must **overlap** the invoice month (Keeta only; Ninja/HS still require both ends inside the month). Assignment matching clips to the invoice month without changing the shared Ninja/HS rule.
- **Same rider, multiple IDs:** allowed; days/category/metrics may differ per ID. Duplicate key remains `accountId:rider`. Per-account sheet order totals must still match the approved invoice.
- **`كفالة اليوزر`:** display-only owner register (no settings).
- **`الصلاحية المتوقعة`:** stored as expected only (`metrics.expectedApproval = expected_only`, `expectedFinal = false`). Final eligibility is the approved invoice.

Preview also returns `columns`, `displayRows` (Excel order), `counts` `{rows,dismissed,duplicateIdRows,uniqueIds}`, `register`, `registerSheet`. The signed `rows[0]` review stays the closed PayCycles shape (`invoiceId`,`app`,`accounts`,`deductions`).

`GET /api/pay/apps/{app}/operations/imports/{id}` returns the saved document (Keeta includes the display envelope beside `review`).
