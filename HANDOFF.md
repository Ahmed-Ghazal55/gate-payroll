# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) |
| Branch | feat/live-ops-erp-reads (draft PR #36) |
| Updated | 2026-11-12 by Claude (5-day review) |
| Live | `/health` = **0.20.0**, jobs off, `dataPreservationVerified=true` |
| Main tip | `ea529c2` (PR #35 merged) — **its deploy failed**, live does not have it |

Note: `main:HANDOFF.md` still shows "Current writer: Cursor 2026-11-11". That lock is stale; this file replaces it.

## Next step (exact, first-payroll path)
1. Ghazal's yes → rerun the failed deploy of PR #35: `gh run rerun 43044008213 --failed`.
   Cause: CI could not resolve `azure.archive.ubuntu.com` in "Browser installation" (network, not code). Same on PR run 43041170662.
2. Check `/api/health` and that the hosted S1 append-only fix is live.
3. Identity dry-run on hosted, then Ghazal «حمّل S1».
4. Ninja 2026-10 payroll end to end (`docs/runbooks/NINJA-2026-10.md`).

## Draft PR #36 — live ERP reads for operations (not on the payroll path)
- `GET /api/operations/{kind}` and `GET /api/riders/{iqama}/accounts` read ERP live via `ConnectionStrings:ErpReadonly` / `ERP_READONLY_DB` (`db67927` + `db67927_readonly` only). Money stays on Store / `db77738`.
- 36 tests pass locally (`LiveOperations|Operations`).
- Must fix before merge:
  1. No SQL paging: whole tables per request (`Payroll.Sync/LiveOperationsSource.cs:64` → `SourceReader.ReadTables`). Approved design: one table, `OFFSET/FETCH` after company filters.
  2. Hosted secret `ConnectionStrings__ErpReadonly` = readonly login (not set).
  3. One Codex security review.
- Merge only after the first payroll and Ghazal's decision (reverses R11 mirror design).

## Kept for Ghazal's decision
- `stash@{0}` "On release/0.20.0: handoff mid-merge"; `stash@{1}` "On main: stale 2026-11-04 edits". Planner recommends drop both.
- Old local and remote branches (list in `~/.agents/NEXT.md`).

## Do not
- Do not write to ERP. Do not put secrets in git (repo is public).
- Do not lift BR §38.3 (HungerStation deferred).
