# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-11 14:00 +03 (R12-FIX1 — push / wait CI) |
| Branch | release/0.20.0 |
| Updated | 2026-11-11 by Cursor (R12-FIX1) |
| Live | `/health` still 0.19.0 until merge/deploy |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Wait CI green on the R12-FIX1 push tip. Then clear Current writer. Cursor reviews FIX1 once after green CI (separate prompt).

## R12-FIX1 (pushed locally verified)
Items 1–7: housing union days, housing rate blocker, Chefz FT half-month amount, Jahez month start bound, `/api/health`, statement/360 empty state, RiderIdentityRepair dry-run tool + runbook S1 block.

Local matrix: Engine 508/508 (SQLite+SQL 11433, 0 skipped, serial), `npm --prefix web run api:check`, `tests/r8-contract.mjs`, workflow browser list + `web/tests/ui-qa.mjs`.

## Not done / PENDING
- CI green confirmation for this tip.
- Cursor review of FIX1 after green CI.
- Part B hosted «حمّل» slices (needs Ghazal; identity dry-run tool not run on hosted).

## Ghazal's steps
1. Wait FIX1 CI green + Cursor review.
2. Merge when ready; then «حمّل S1» only after identity dry-run reviewed.
