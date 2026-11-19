# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/rider-app-v2` (worktree `/home/ghazal/Projects/active/gate-payroll-rider-app`) |
| Updated | 2026-11-19 by Cursor (Task M4F: live-safe rider indexes) |
| Live | unchanged; no push/deploy from this branch |
| Next | Planner re-review `feat/rider-app-v2` after M4F; Ghazal places `google-services.json` and runs `mobile/scripts/build-debug.sh` |

## Goal now
Rider SQL Server indexes are out of startup; admin maintenance job builds them one-by-one. Ready for planner re-review. Do not push.

## Done (Task M4F — 2026-11-19)
- Startup creates no rider columns/indexes; catalog in `RiderDataIndexes`.
- Admin `GET/POST /api/system/maintenance/rider-indexes`: free-space check, session app lock, `ops.maintenance_step`, definition verify, `allowedActions`.
- Queries use allow-listed kind literals + CONVERT expressions matching computed columns.
- IP rate-limit invalid config → defaults + warning (no crash).
- Ops notes: `docs/rider-app/RELEASE-NOTES-OPS.md`.
- Verify: Engine.Tests 583/583 (SQL `gp-m4f-sql:14333` removed); web build+14; mobile 19/19.

## How to verify
```bash
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true
npm --prefix web run build && npm --prefix web test
(cd mobile && npm test)
```

## Do not
- No merge/deploy/push or live setting change without Ghazal's explicit yes. ERP read-only only.
