# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) — next: Cursor L1 |
| Branch | feat/live-ops-erp-reads (draft PR #36) |
| Updated | 2026-11-12 by Claude (C0 + new plan) |
| Live | `/health` = **0.20.0**, jobs off, `dataPreservationVerified=true` |
| Main tip | `ea529c2` (PR #35 merged) — **its deploy failed**, live does not have it |

Note: `main:HANDOFF.md` still shows "Current writer: Cursor 2026-11-11". That lock is stale; this file replaces it.

## Decision 2026-11-12 (Ghazal): no ERP mirror; live reads + frozen run inputs, BEFORE the first payroll
Plans: `.cursor/plans/live_ops_erp_reads_891ef52a.plan.md` (todos) and `~/.claude/plans/sharded-yawning-kettle.md`. BR draft: `gate-finance-spec/docs/proposals/BR-39-live-reads-run-inputs.md` (waits for Ghazal).
Cancelled: hosted S1 load and `cursor-S1-faster-load.md`. The PR #35 redeploy is off the critical path.

## Next step (exact)
1. **L1 — Cursor:** `~/.agents/prompts/cursor-L1-live-reads-paging.md` on this branch (paged SQL per kind, scope in SQL, search, rider accounts, contract).
2. **U1 — Antigravity Flash 3.8:** `~/.agents/prompts/antigravity-U1-ops-live-ui.md`, after L1 is green.
3. **S1 — Claude:** security pass on PR #36 + one fix round.
4. **L2 — Cursor:** `~/.agents/prompts/cursor-L2-run-input-snapshots.md` (new branch `feat/run-input-snapshots`). Then U2 (Antigravity), then Claude's review, BR, release.

## Draft PR #36 state
- **C0 done (Claude, `0991530`):** `Operations.SourceMode` (`Payroll.Api/Operations.cs`): Production is always `erp_live` (no secret → 503). Development/CI use `store` without an ERP login (fixes `web/tests/r8-ninja-flow.mjs:397`).
  Permission and kind are checked before any ERP connection. 20 s live-read timeout → 503. `SqlException` → 503 without details. Read-only check cached 10 min.
  The response adds `source`, `asOf`, `allowedActions: []`. Audit `operations.rider_accounts_read`. `/health` + `/api/health`: `operationsSource`, `erpReadonlyConfigured`. Tests: `OperationsSourceModeTests` (3).
- CI run 43263608876: **green**. Local: 500 passed. 2 need local SQL (`NinjaPayrollSqlTests`, `R9PayrollHttpTests` SQL case): they run in CI.
- Still open (L1): no SQL paging (`Payroll.Sync/LiveOperationsSource.cs` reads whole tables); scope after read (N+1 `Store.Get`); `q` in memory.
- Before merge: hosted secret `ConnectionStrings__ErpReadonly` (readonly login, set by Ghazal in MonsterASP), and Claude's security pass (replaces Codex).

## Kept for Ghazal's decision
- `stash@{0}` "On release/0.20.0: handoff mid-merge"; `stash@{1}` "On main: stale 2026-11-04 edits". Planner recommends drop both.
- Old local and remote branches (list in `~/.agents/NEXT.md`).

## Do not
- Do not write to ERP. Do not put secrets in git (repo is public).
- Do not lift BR §38.3 (HungerStation deferred).
