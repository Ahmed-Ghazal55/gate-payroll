# Release 0.30.0 (2026-11-21)

Import centre removed (IM1, decision H2), whole-app integrity fixes (INT1 review → INT1F → A2), forms all-companies permission (RFP1 review → FP1F).

## OPS STEP for Ghazal — BEFORE deploying
- Grant `forms.responses.allCompanies` («عرض ردود كل الشركات») to every live reviewer who has **no company** in their financial scope and must keep seeing all form responses. Without it, after the deploy their response lists, counts and analytics are empty and out-of-scope reviews return 404. Scoped reviewers are unchanged; Admin/SuperAdmin always see all.

## What changed for users
- **Import centre removed.** The «استيراد الملفات» page and sidebar item are gone. Each upload is now a button on its own page with the same endpoint, permission and preview → approve flow (map: `docs/imports-removal-map.md`):
  - Companies → «استيراد الشركات» on `/companies` (admin only).
  - Riders → «استيراد ملف المناديب» on `/workforce` (approve also needs `riders.financial_edit`).
  - Treasury opening balances → `/accounting`.
  - Opening debts and bulk collection from file → `/debts`.
  - Ninja / Keeta / HungerStation operations files → buttons on `/payroll` platform screens; invoices stay on their existing payroll screens; Chefz stays review-only.
  - `GET /api/imports/catalog` is removed; empty templates (`/api/imports/templates/*`) stay.
- **Integrity fixes (INT1).**
  - `/api/riders` and `/api/riders/{iqama}/profile-360` mask `iban` / `verifiedIban` without `riders.iban.view` and `mudadSalary` without `riders.saudi_salary.view`; the workforce Excel export blanks «راتب مدد» without that permission. `/api/riders` is now limited to the user's companies (same rule as the workforce list).
  - Sidebar: items inherit their group's permission; bank-file, accounting, audit and rider-statement tabs use the API's permissions; users without `payroll.view` land on a page they can open.
  - New permission `audit.view` («عرض سجل التدقيق») for the audit log; `/api/audit` is paged with a date filter.
  - Broken in-app links fixed (dashboard button, operations 360 link, Jahez link, search deep-links).
  - Shared money formatter `formatSar` («ر.س», 2 decimals) on accounting, treasury, debts, payroll, payroll review grid and drawer, invoice upload, rider statement, manual journals, debt deductions, Jahez and adjustments.
  - Ninja / Keeta invoice upload: the company has **no default** any more. The user must pick it («اختر الشركة») before the file input and the reconcile button are enabled.
  - Legacy `/legacy/` audit view reads the paged `/api/audit` result.
  - Company names come from the API, not hard-coded; admin fake Firebase form replaced by a link to rider-app settings; internal notes and Latin labels removed from several screens; dead client functions removed.
- **Forms all-companies permission.** Unscoped staff see form responses of every company only with `forms.responses.allCompanies`; campaign response counts and the dashboard count follow the same scope; empty lists show an Arabic hint.

## Other ops notes
- Grant `audit.view` to non-admin users who must read the audit log (admins have it by default).
- Operational users with no company in their financial scope now get an empty `/api/riders` list (payroll rider picker): assign their companies if they need it.

## Live safety
- No schema, seed, data or setting change on first boot. No ERP writes. Two new permission codes only (catalog entries, no stored-user change).
