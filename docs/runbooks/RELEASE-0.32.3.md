# Release 0.32.3 (2026-11-21)

Tabs and period cleanup (DEP13), from Ghazal's decisions M-0709 / M-0710 after the 0.32.2 QA. Antigravity built it (UT1, `fix/tabs-period`), Claude reviewed it in the built app, and Cursor did one fix round (UT1F: remaining English labels). Web only.

## What changed for users
- No duplicate top tab bars: Payroll, Debts, Treasury, Accounting and Admin show only the section picked in the sidebar (like the rider-app page). The page heading names the open section. Tabs inside a page stay (`?app=` / `?sub=` in payroll, `?view=` in operations, workforce, companies, HR catalogs, notifications, rider-app inner tabs).
- The sidebar gains «الدورة الحالية (النظام القائم)» under Payroll; each group opens its first section.
- Old links still work: an old `?tab=` value opens the right section and keeps the other URL values. Map: payroll `current_cycle→current-cycle`, `apps→platforms`, `keeta_upload→platforms&app=keeta&sub=settlements`, `ninja→platforms&app=ninja&sub=settlements`; debts `statements→statement`, `list→debts`, `advances→advance_requests`; treasury `payouts→advance_payouts`, `bank→bank_file`, `journals→movements`; accounting `trial→trial_balance`, `income→income_statement`, `manual→manual_journals`; admin `roles→users`, `audit_log→audit`; operations `cases→/workforce/?tab=cases`, `accounts→/workforce/`, `custody|guarantees→vehicles`. Unknown values open the first section.
- One period: `?period=YYYY-MM` in the page URL. The header picker and every page picker (payroll cycle / current cycle / rules, Ninja upload, treasury bank file / Jahez / extras, accounting reports, operations fuel, debt and rider statements, rider-app statements) read and change the same value; sidebar, header and breadcrumb links keep it. With no period in the URL every page uses the current month. The header list now covers the last 18 months plus next month (it had only 2026-08..10).
- Arabic text: «Maker ≠ Checker» and the other English words in labels, buttons, tooltips and messages are now plain Arabic (e.g. «لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة»). Codes stay as they are (CSV, IBAN codes, vault/city/department codes).

## API
- No route, schema or message change.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes.

## Ops for Ghazal
- None required. Old bookmarks with `?tab=` still open the right section.
