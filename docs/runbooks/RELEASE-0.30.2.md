# Release 0.30.2 (2026-11-21)

Follow-up release on top of 0.30.1: the open items from the 0.30.0 release review (task FU1 by Cursor, reviewed and finished by Claude in DEP8).

## What changed for users
- **Workforce page: riders-file upload removed.** The button «استيراد ملف المناديب» is gone from `/workforce`. People data is read live from Omar's ERP (Ghazal 2026-11-21). The generic `/api/excel/{kind}` API route stays, with no screen calling it for riders.
- **Rider list and old 360 profile follow the same scope as the workforce page.** `GET /api/riders` now also hides riders outside the user's platforms. `GET /api/riders/{iqama}/profile-360` returns 404 (not 403) for a rider outside the user's company or platforms, so it does not reveal that the rider exists. IBAN and Saudi (muda) salary stay hidden without `riders.iban.view` / `riders.saudi_salary.view` (now covered by HTTP tests, JSON and the workforce export).
- **Audit log is paged on the server.** The admin tab «سجل التدقيق الشامل» shows 50 rows per page with Previous/Next, and its search and action filter run on the server over the whole log (before, it searched only the first 100 rows). The count on the tab and the card is the real total. `GET /api/audit` filters (`from`, `to`, `action`, `q`) and pages in SQL, newest first, with a stable order.
- **Template download buttons follow the endpoint permission.** On companies, debts, accounting (opening balances) and payroll (Ninja/Keeta/HungerStation operations), «تحميل القالب» shows only when the API lists `download_template` in the page `allowedActions`, i.e. when the user holds the permission the template endpoint requires (`imports.view` for `/api/imports/templates/*`, otherwise the endpoint's own).

## Ops note for Ghazal
- Users who download operations or companies templates need `imports.view`. Without it the button is hidden (before it was shown and the download failed with 403).

## Live safety
- No schema change on SQL Server, no data wipe, no ERP writes, no first-boot migration.
- One new SQLite-only index (`ix_audit_at`, `CREATE INDEX IF NOT EXISTS`); live runs on SQL Server, so it does not run there.
- The audit query is read-only SQL (`COUNT` + `OFFSET/FETCH`).
