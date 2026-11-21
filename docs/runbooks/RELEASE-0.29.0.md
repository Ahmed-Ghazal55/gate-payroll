# Release 0.29.0 (2026-11-19)

## What changed for users
- Companies section «الشركات والسجلات»: one sidebar entry with four inner tabs: الشركات · السجلات التجارية · الوثائق المنتهية · «الإحصائيات والمؤشرات». Old `?tab=` links still open the right tab, and the sidebar stays highlighted on every tab.
- New tab «الإحصائيات والمؤشرات»: live counts for each company, read only from Omar's ERP. Counts are sponsorships (riders, admins, active, on leave) and platform accounts by platform (total, active, assigned). Platforms outside the standard list are shown in their own columns, so the platform columns always add up to the total.
- Counts follow the user's company scope AND platform scope. A user limited to some platforms sees only their accounts.
- Filters: the KPI cards and the table footer recount for the current search («كل الشركات المفلترة» or «كل الشركات ضمن صلاحية المستخدم»). Each count links to the workforce list with that company or platform already filtered. Workforce keeps those filters in the address bar and clears them correctly.
- Company drawer: details, documents, statistics, a commercial-registration tab, and the audit tab with Arabic action names.
- «الوثائق المنتهية» shows the real expiring CRs and uploaded documents, with «منتهي» for expired ones. Invented defaults (city, CR version, CR type, zero counts) are gone and show «—» instead.
- Excel export works on all four tabs for users with `companies.view`. The export endpoint only formats the rows the screen sends.
- New endpoints: `GET /api/companies/stats` and `GET /api/companies/{id}/stats` (permission `companies.view`).

## Later (not in this release)
- Balances per company: the table is shown as «الأرصدة بعد ربط المحاسبة» until the accounting period balances exist. The new permission `companies.balances.view` exists now but shows no amounts yet.

## Ops step for Ghazal (after deploy)
- Nothing required. Optional: grant `companies.balances.view` only to the roles that should see company balances once accounting is linked.

## Live safety
- No schema, seed, data or setting change on first boot. New reads are SELECTs on Omar's ERP via the read-only login (`ApplicationIntent=ReadOnly`, 15 s timeout) and on our own company records. No ERP writes.
