# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r8-ninja-web; based on feat/r8-ninja-api merged |
| Updated | 2026-11-07 by Antigravity (Gemini 3.8) |
| Live | Last handoff: 0.17.0 on SQL db77738; not rechecked this run |

## Goal now
First real Ninja payroll, 2026-10, in the browser against db77738. All frontend tasks A1 through A7 completed, verified with real API end-to-end tests and screenshots.

## R8 Web report (Antigravity)
- **A1 (Done):** Unified `DataTable.tsx` with header filters (OR within column, AND across), Arabic-normalized global search (digits/letters), URL query state synchronization, filtered Excel export, totals footer («إجمالي المعروض» vs «إجمالي الكل»), skeleton loading, and StatCard filter toggle.
- **A2 (Done):** Migrated payroll-path tables to DataTable: `PayrollReviewGrid`, Ninja reconcile captain table, `/app/payroll` cycle lines, `/app/debts`, `/app/riders`, `/app/operations` (all tabs), and `ManualJournalsPanel`.
- **A3 (Done):** Companies balances mask toggle persisted in localStorage with try/catch; Trial Balance & Income Statement company/period header lines; sweep and fix native `<select>` token styling.
- **A4 (Done):** Ninja invoice stepper (رفع → مطابقة → تأكيد → اعتماد → ترحيل) with buttons rendered strictly from server `allowedActions`; per-account multi-select and consolidated mode; verbatim maker ≠ checker error display.
- **A5 (Done):** Review draft (`GET /pay/review-draft`) showing per-account totals vs invoice, unassigned orders, riders not found, and actionable `blockers[]` with direct fix links; «إنشاء دورة» gated by `canCreate` and 0 blockers; cycle page with who/when history per stage, advance button from `allowedActions`, and «كيف حُسب؟» formula drawer.
- **A6 (Done):** Rules editor wired to lifecycle API: «إضافة شرط» dialog with metrics from `ninjaMetrics`, operators, threshold, effect, and «ضمن القاعدة» flag; editable draft table; «اعتماد», «رفض» (with required reason), and «حذف المسودة».
- **A7 (Done):** `RiderPicker` combobox on `GET /api/riders/lookup` with 250ms debounce, keyboard navigation, match highlighting, status chips (كفالة/خارجي، نشط/غير نشط), and fallback link; wired in debts and rider statements.
- **Verification:**
  - `npm run build`: exits 0, all 19 static routes prerendered.
  - `node web/tests/ui-qa.mjs`: all verifications passed with zero failures.
  - `node web/tests/r8-ninja-flow.mjs`: live ASP.NET Core API + static export E2E passed; 16 screenshots saved in `test-results/r8-web/` at 1440 and 360 in light and dim themes.
  - Added `node web/tests/r8-ninja-flow.mjs` to CI browser step in `.github/workflows/build-and-deploy.yml`.

## Requests to Codex
1. Implement `GET /api/riders/lookup?q=...` (Task C6) in `Payroll.Api/` so `RiderPicker` queries the backend search directly in production instead of in-memory fallback.

## How to verify
```bash
cd web && npm run build
node web/tests/ui-qa.mjs
node web/tests/r8-ninja-flow.mjs
```

## Next step
1. Run review round with Claude Code on `feat/r8-ninja-web`.
2. Perform live acceptance walkthrough with Ghazal on Ninja October 2026 payroll flow in the browser.
