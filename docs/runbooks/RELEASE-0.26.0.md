# Release 0.26.0 (2026-11-19)

## What changed for users
- Full iqama number everywhere (tables, search, payroll review screens); no more masked iqama.
- New «القوى العاملة» page: live paged list from the ERP with filters, summary cards (sponsored, riders, admins, missing IBAN, by company) and Excel export of the full filtered list (one query).
- Rider 360 reduced to the agreed fields; HR catalogs page (`/hr/catalogs`) reads the live ERP catalogs.
- Field permissions (new): `riders.iban.view` (IBAN), `riders.saudi_salary.view` (Saudi salary), `riders.legal_cases.view` (legal cases), `riders.unified_number.view` (unified number). Without them the API removes these fields from the list, 360 and export.
- Scope: non-admin users see only their companies AND their platforms, in the list, totals, export and 360; an out-of-scope person returns 404.
- Removed: the rider document upload checklist and its upload routes.

## Ops step for Ghazal (after deploy)
- Grant the 4 field permissions to the right roles/users. Until then only admins see IBAN, Saudi salary, legal cases and unified number.
- Optional: `Workforce:ExportTimeoutSeconds` (30–600, default 120) if the full export is slow.

## Live safety
- No schema, seed, data or setting change on first boot. The new endpoints only run read-only SELECT queries against the live ERP.
