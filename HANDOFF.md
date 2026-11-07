# HANDOFF

| Field | Value |
|---|---|
| Current writer | Antigravity (Gemini 3.8) |
| Branch | feat/r8-ninja-web; merged feat/r8-ninja-api (d7e2feb) |
| Updated | 2026-11-07 by Antigravity (Gemini 3.8) |
| Live | Last handoff: 0.17.0 on SQL db77738; not rechecked this run |

## Goal now
First real Ninja payroll, 2026-10, in the browser against db77738. Merged finished API contract with rider lookup and manual journal actions; now completing fix round 1 (WF-0 to WF-4).

## R8 API report (Codex & Claude)
- C1: schemas for debts, cycles/lines/review, statements, Ninja invoices/actions, settings, riders list, manual journals and campaigns; pinned OpenAPI→TypeScript generation, CI check, 19 real-HTTP schema/routing assertions.
- C2: per-account upload validates every captain; errors give row numbers. One invoice per file, all company/month invoices share a cycle; hash replay is a no-op, overlapping accounts across files are rejected. Server supplies approvalStatus/allowedActions and preserves independent approval.
- C3: GET /api/pay/review-draft returns source-bound invoices/accounts/riders, D3 or daily evidence, totals and actionable blockers. Includes uninvoiced daily accounts, missing riders, actual assignment bounds and settings/choice gaps. No writes.
- C4: draft reject/delete for terms/schemes/eligibility/global settings, owner/admin checks and audit; validated Ninja metrics and included conditions.
- C5: full SQL Server HTTP chain passed: upload→classify→approve→dispute/resolve→D3→draft→create→stages 1–7→review→lines→statement→posting→replay→reversal. Fixed invoice journal links and reversal status; source references reach statements.
- C6/C7 (Claude): `GET /api/riders/lookup` (iqama/name/phone/platform account, Arabic+digit normaliser `TextSearch`, all riders incl. inactive/debt-only, phone only with riders.view); `/riders` honours status + q/search; manual journal reject (checker, reason) + delete draft (author/admin) + `allowedActions` on the list. Contract snapshot + types regenerated; 425/425 SQL, 20 HTTP contract checks.
- C8: docs/runbooks/NINJA-2026-10.md written. Live dry run NOT run: no authenticated live browser/API session; latest user forbids reading ANY ~/Private file. Wrote mode-600 not_run marker at ~/Private/gate-data/sync-logs/r8-ninja-dryrun.json; no real counts/blockers claimed.

## R8 Web report (Antigravity)
- **A1 (Done):** Unified `DataTable.tsx` with header filters (OR within column, AND across), Arabic-normalized global search (digits/letters), URL query state synchronization, filtered Excel export, totals footer («إجمالي المعروض» vs «إجمالي الكل»), skeleton loading, and StatCard filter toggle.
- **A2 (Done):** Migrated payroll-path tables to DataTable: `PayrollReviewGrid`, Ninja reconcile captain table, `/app/payroll` cycle lines, `/app/debts`, `/app/riders`, `/app/operations` (all tabs), and `ManualJournalsPanel`.
- **A3 (Done):** Companies balances mask toggle persisted in localStorage with try/catch; Trial Balance & Income Statement company/period header lines; sweep and fix native `<select>` token styling.
- **A4 (Done):** Ninja invoice stepper (رفع → مطابقة → تأكيد → اعتماد → ترحيل) with buttons rendered strictly from server `allowedActions`; per-account multi-select and consolidated mode; verbatim maker ≠ checker error display.
- **A5 (Done):** Review draft (`GET /pay/review-draft`) showing per-account totals vs invoice, unassigned orders, riders not found, and actionable `blockers[]` with direct fix links; «إنشاء دورة» gated by `canCreate` and 0 blockers; cycle page with who/when history per stage, advance button from `allowedActions`, and «كيف حُسب؟» formula drawer.
- **A6 (Done):** Rules editor wired to lifecycle API: «إضافة شرط» dialog with metrics from `ninjaMetrics`, operators, threshold, effect, and «ضمن القاعدة» flag; editable draft table; «اعتماد», «رفض» (with required reason), and «حذف المسودة».
- **A7 (Done):** `RiderPicker` combobox on `GET /api/riders/lookup` with 250ms debounce, keyboard navigation, match highlighting, status chips, and fallback link.
- **WF-0 (In progress):** Merged API contract; regenerating types and binding contract types without `any`.

## How to verify
```bash
cd web && npm run build
node web/tests/ui-qa.mjs
node web/tests/r8-ninja-flow.mjs
```

## Next step
Complete fix round 1 (WF-1 to WF-4) and verify full end-to-end flow in browser.
