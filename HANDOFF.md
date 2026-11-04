# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Next writer | Claude reviews `feat/r3-web` |
| Branch | feat/r3-web from main 5fe403b |
| Live | 0.12.1 (5fe403b) |
| Updated | 2026-11-04 by Antigravity |

## Goal completed
Release train R3 web completed on `feat/r3-web`:
1. **Part A (HungerStation review workspace under `/app/payroll/`)**:
   - A1 Invoices: 1-3 XLSX upload, signed preview with blocking checks in red, warning flags for V=Z and T-Z=AA, maker-checker approval.
   - A2 Disputes: CAR disputes table, resolve dialog with `platform_credit` and `platform_rejected` (CAR exception per BR §23.2), escalated badge «مرحّل لحساب النزاعات 112803 لحين فض النزاع», Excel export.
   - A3 TGA Distribution: starts as «بانتظار التوزيع», split calculator dialog by amount/percent, live sum check against total, maker-checker second-user approval.
   - A4 ERP Wallet Facts: read-only, toggle between accounts and riders, allowlisted kinds, reversal badges, missing source banner.
   - A5 Month Close (C/M/A): formula cards with `Expected = -C`, `RemainingOldCarry = C + A`, `CurrentPeriodBalance = M - (C + A)`, renders «غير معروف» when C or A is missing.
   - A6 Proposed Journals: balance check, banner «الترحيل موقوف لحين اعتماد ملف المنصة» when gate is off, target scheme lines disabled with «نظام التارجت لم يُعتمد بعد» badge.
   - A7 Live E2E: synthetic workbook import, CAR dispute rejection -> escalated 112803 badge, TGA split by User A, refusal when User A approves own split, and User B (independent checker) approval.
2. **Part B (Track L2 Part 1: move first old sections to `/app`)**:
   - B1 Treasury & Banks (`/app/treasury/`): cash boxes, bank accounts, balances, period CSV bank payroll preview/export, journals list, account details drawer.
   - B2 Debts & Advances (`/app/debts/`): debts/advances/costs table with category filters, rider statement (Detailed 360 & Simple), mutual approvals with decision modal, promissory note archive.
   - B3 Accounting & Reports (`/app/accounting/`): Target COA tree (403 accounts: 294 excel, 28 modified, 81 new; active vs posting), double-entry journals with reversal linkages, trial balance (Target & Current view, 6 columns, balanced check), income statement (revenue & expense groups, net income, netIsComplete status).
   - B4 Dashboard (`/app/page.tsx`): «استيراد بيانات تشغيل (مؤقت)» button points to `/legacy/#/imports` with label «مؤقت».

## Verification summary
- **Static build:** `npm run build` cleanly compiled 16/16 pages statically without any `/app/app/` links.
- **Static E2E & screenshots:** `npm run test:e2e` passed all 7 suites; captured 1440 desktop (sand, light, dim) and 360 mobile screenshots in `test-results/r3-web/` for all workspaces.
- **Live E2E suite:** `node web/tests/e2e-live.mjs` passed 10/10 test blocks against the real .NET server and database under the real server CSP (zero blocked inline scripts).
- **Regression tests:** `node tests/browser-iban.mjs` and `node tests/finance-views-ui.mjs` both passed with zero regressions.

## Commits on `feat/r3-web`
1. `a7f67ab` — `feat(web): HungerStation platform review workspace (R3-A)`
2. `ea9f53b` — `feat(web): treasury and banks workspace (R3-B1)`
3. `4f3b3cb` — `feat(web): debts and advances workspace (R3-B2)`
4. `51ea710` — `feat(web): accounting and financial reports workspace (R3-B3)`
5. `a440e71` — `feat(web): dashboard temporary import link (R3-B4)`
6. `0567722` — `test(web): live and static e2e tests with screenshots (R3-A7)`

## API gaps for Codex
- **Zero blocking gaps for R3:** All endpoints (`/api/platforms/hs/*`, `/api/finance/accounts`, `/api/finance/journals`, `/api/finance/bank-file/*`, `/api/debts`, `/api/approvals`, `/api/promissory-notes`, `/api/finance/coa/target`, `/api/finance/trial-balance`, `/api/finance/income-statement`) functioned cleanly and returned expected structures.
- **Note for Track L3 (future enhancement):** When importing an invoice with TGA deductions, the lines exist in `platform_invoice_line` with `status: "pending_allocation"`. Currently, `/api/platforms/hs/allocations` only lists records after a split proposal is created (`platform_allocation`). Providing a dedicated query for unallocated TGA invoice lines or including them in invoice details will make initial discovery smoother in L3.

## Capacity status
- Before R3: 35% weekly limit used (~65% remaining).
- After R3: ~48% weekly limit used (~52% remaining).
- Safe margin maintained within weekly quota.

## How to verify
```bash
git -C /home/ghazal/Projects/active/gate-payroll-r3web status
cd /home/ghazal/Projects/active/gate-payroll-r3web/web && npm run build && npm run test:e2e && node tests/e2e-live.mjs
cd /home/ghazal/Projects/active/gate-payroll-r3web && node tests/browser-iban.mjs && node tests/finance-views-ui.mjs
```
