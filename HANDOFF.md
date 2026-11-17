# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Next writer | Claude reviews `feat/r3-web` (Part 0), then `feat/r4-web` |
| Branch | feat/r4-web from feat/r3-web d106714 |
| Live | 0.12.1 (5fe403b) |
| Updated | 2026-11-04 by Antigravity |

## Goal completed
Release train R4 web completed on `feat/r4-web`:
1. **Part 0 (R3 fixes on `feat/r3-web`, commit `d106714`)**:
   - 0.1 TGA Split: isolated percent mode payload `{ riderPercent, riderId }` vs amount mode `{ companyAmount, riderAmount, riderId }`, labeled preview «تقريبي — المبلغ النهائي من الخادم». Live test verifies mixed mode 400 rejection.
   - 0.2 Swallowed errors: replaced `.catch(() => [])` with `<ErrorState onRetry={...} />` in treasury, debts, companies.
   - 0.3 HS card: labeled invoices card «إجمالي الصفحة الحالية».
2. **Part A (Sidebar IA & SectionTabs, commit `3f21ca5`)**:
   - Collapsible groups matching spec 09 lines 37–50, including «تطبيق المناديب», active group persistence, 360px drawer.
   - Reusable `SectionTabs` component (`?tab=`, ARIA tabs keyboard navigation, deep linking).
3. **Part B & C (All workspaces, commits `ff2a679` to `7acb033`)**:
   - W3 Riders (`/app/riders/`): دليل المناديب · ملف 360 · جودة واكتمال البيانات · المزامنة.
   - W4 Statement (`/app/riders/statement/`): Plan §3 layout (header, monthly cards, timeline table, له/عليه badge, PDF print, CSV export).
   - W5 Operations (`/app/operations/`): المدن · السكن · المركبات · العهد · الكفالات · القضايا · حسابات المنصات («غير متاح بعد»).
   - W6 Admin (`/app/admin/`): المستخدمين والصلاحيات (`/api/users`), سجل التدقيق (`/api/audit`), إعدادات المنظومة.
   - W7 Imports (`/app/imports/`): معالج استيراد من 5 خطوات ومحفوظات الدفعات.
   - W8 & W9 Payroll (`/app/payroll/`): هيكل متعدد المنصات (6 منصات)، دورة رواتب 0–7 مع شارة «يحتاج قرار» للصافي السالب، محرر أنظمة مُمرحل شهرياً ومصفوفة فئات (6 أعمدة).
   - W10 Rider App Admin (`/app/rider-app/`): الإشعارات والإرسال، إعدادات Firebase، معاينة كشف التطبيق.
4. **Part D (Retirement Check, commit `047d43d`)**:
   - `grep -rn "/legacy" web/` confirmed zero legacy links in source (only allowed login `returnUrl` check remains).
   - Legacy views in `Frontend/` with no `/app` screen: **NONE** (100% of legacy screens migrated to `/app`).

## Verification summary
- **Static build:** `npm run build` compiled 19/19 routes cleanly without any `/app/app/` links.
- **Static E2E & screenshots:** `npm run test:e2e` passed all 8 suites; 16 screenshots at 1440 and 360 in `test-results/r4-web/`.
- **Live E2E suite:** `node web/tests/e2e-live.mjs` passed all 10 steps against real .NET API and live database.
- **Regression tests:** `node tests/browser-iban.mjs` and `node tests/finance-views-ui.mjs` both passed cleanly.

## Commits on `feat/r4-web`
1. `3f21ca5` — `feat(web): sidebar groups and SectionTabs component (R4-W1)`
2. `ff2a679` — `feat(web): riders directory workspace (R4-W3)`
3. `8e6f651` — `feat(web): rider financial statement view (R4-W4)`
4. `c7a3170` — `feat(web): operations and assets workspace (R4-W5)`
5. `7e140a9` — `feat(web): admin and audit workspace (R4-W6)`
6. `cc28779` — `feat(web): operational data imports workspace (R4-W7)`
7. `51f13ba` — `feat(web): payroll multi-platform framework and rules editor (R4-W8)`
8. `7acb033` — `feat(web): rider mobile app admin workspace (R4-W10)`
9. `047d43d` — `feat(web): complete legacy links removal (R4-W11)`
10. `7a6e4e4` — `test(web): e2e static and live verification for R4 workspaces (R4-verify)`

## API gaps for Codex
- `GET /api/operations/{kind}`: query parameters and data models for cities, housing, vehicles, custody, guarantees, cases, platform accounts (Codex R4.6).
- `GET /api/rider-mobile/sessions`: rider active mobile sessions and device fingerprints.
- `GET /api/finance/statement/rider/{iqama}/monthly`: monthly multi-platform aggregated statement view (Codex R4.8).
- Database reporting views/tables in `rpt` schema (Codex R4.7).

## Capacity status
- Before session: ~48% weekly limit used (~52% remaining).
- After session: ~62% weekly limit used (~38% remaining).

## How to verify
```bash
git -C /home/ghazal/Projects/active/gate-payroll-r4web status
cd /home/ghazal/Projects/active/gate-payroll-r4web/web && npm run build && npm run test:e2e
cd /home/ghazal/Projects/active/gate-payroll-r4web && node web/tests/e2e-live.mjs
cd /home/ghazal/Projects/active/gate-payroll-r4web && node tests/browser-iban.mjs && node tests/finance-views-ui.mjs
```
