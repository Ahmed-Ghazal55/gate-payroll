# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r8-ninja-api; base d31adc1 (0.17.1) |
| Updated | 2026-11-07 by Codex |
| Live | Last handoff: 0.17.0 on SQL db77738; not rechecked this run |

## Goal now
First real Ninja payroll, 2026-10, in the browser against db77738. Backend HTTP/SQL verification is complete; real-browser acceptance is NOT complete. Worktree is isolated while other R8 agents write. No push, merge, deploy or live database writes performed.

## R8 report (current)
- C1: schemas for debts, cycles/lines/review, statements, Ninja invoices/actions, settings, riders list, manual journals and campaigns; pinned OpenAPI→TypeScript generation, CI check, 19 real-HTTP schema/routing assertions. Lookup awaits Claude C6; regenerate after C6/C7 integration.
- C2: per-account upload validates every captain; errors give row numbers. One invoice per file, all company/month invoices share a cycle; hash replay is a no-op, overlapping accounts across files are rejected. Server supplies approvalStatus/allowedActions and preserves independent approval.
- C3: GET /api/pay/review-draft returns source-bound invoices/accounts/riders, D3 or daily evidence, totals and actionable blockers. Includes uninvoiced daily accounts, missing riders, actual assignment bounds and settings/choice gaps. No writes.
- C4: draft reject/delete for terms/schemes/eligibility/global settings, owner/admin checks and audit; validated Ninja metrics and included conditions. Real October approval/readiness NOT verified.
- C5: full SQL Server HTTP chain passed: upload→classify→approve→dispute/resolve→D3→draft→create→stages 1–7→review→lines→statement→posting→replay→reversal. Fixed invoice journal links and reversal status; source references reach statements.
- C6/C7: deliberately skipped; owned by Claude. Frontend and mobile worktrees untouched.
- C8: docs/runbooks/NINJA-2026-10.md written. Live dry run NOT run: no authenticated live browser/API session; latest user forbids reading ANY ~/Private file. Wrote mode-600 not_run marker at ~/Private/gate-data/sync-logs/r8-ninja-dryrun.json; no real counts/blockers claimed.
- Tests: 421 passed / 0 failed / 0 skipped (full dotnet test including SQL, final TRX /tmp/gate-r8-results/r8-final.trx); 19 HTTP contract assertions; generated TypeScript compiles and regeneration check passes.
- Weekly usage: 40% before (prompt supplied) → after unavailable; no usage/quota telemetry exposed in this session.

## How to verify (synthetic only)
```bash
export PAYROLL_TEST_SQL_CONNECTION='Server=localhost,11433;Database=master;User ID=sa;Password=GateSynthetic_CI_Only_723!;Encrypt=True;TrustServerCertificate=True'
export Vouchers__Secret=GateSynthetic_CI_Only_Voucher_Key_000001
dotnet test tests/Engine.Tests --no-restore -m:1
node web/scripts/gen-api-types.mjs --check
```
Requires pinned npm dependencies in RiderForm and web. Existing Docker gate-sql-test runs on 11433; tests create/drop their own isolated synthetic databases. Never point the test connection at db77738/db67927.

## Next step (exact)
1. Claude reviews this branch once and integrates Antigravity UI plus C6/C7 in the main project workflow. New UI types must import web/src/lib/api-contract.ts; no hand-written response guesses.
2. After C6/C7: build, run node tests/r8-contract.mjs --update-snapshot, then node web/scripts/gen-api-types.mjs; extend lookup/manual-action contract fixtures to the actual implemented responses and rerun checks.
3. Verify actual browser clicks with Ghazal; current Ninja screen in this base is reconcile-only. Run the GET-only browser snippet in docs/runbooks/NINJA-2026-10.md against the authorized existing server, after the operator confirms db77738; save counts-only output privately, mode 600.
4. Real writes require Ghazal's «حمّل» for each specific step. Do not start a normal API against the live DB merely for a dry run: startup seeds/migrations write. No ERP writes, push, merge or deploy from this task.

## Existing business follow-up (from prior handoff; not rechecked)
- Opening debts: 419 / 953,933.71 pending approval, 6 credits / 2,443.70 still lack approval path. Blocked: 379 creditor decisions + 22 other rows; totals row 809 excluded. Ghazal owns the private decision sheet.
- Ghazal: approve finance-spec CR-003 for Omar; password rotation remains at final ERP integration.
