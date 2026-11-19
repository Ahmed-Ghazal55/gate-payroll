# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/rider-app-v2` (worktree `/home/ghazal/Projects/active/gate-payroll-rider-app`) |
| Updated | 2026-11-19 by Cursor (Task M4 done: merge 0.24.0 + RA2 close-out) |
| Live | unchanged; no push/deploy from this branch |
| Next | Planner re-review `feat/rider-app-v2`; Ghazal places `google-services.json` and runs `mobile/scripts/build-debug.sh` |

## Goal now
Branch is on release 0.24.0 with RA2 close-out (M4 indexes, H3, L3, rate-limit). Ready for planner review. Do not push.

## Done (Task M4 — 2026-11-19)
- Merge `main` (0.24.0 `cc3cbb5`) into `feat/rider-app-v2`; conflicts: `HANDOFF.md` (kept main + branch note), `ENDPOINTS.md` (regenerated). Auto-merged AccessPolicy/FinancialCore/Program/UserAccess kept both intents. Regenerated OpenAPI + `api-contract.ts`.
- M4: SQL Server rider filter indexes (`RiderDataIndexes`) + SQL-filtered open campaign/form lists.
- H3: mobile hides short lines when `shortLinesBalanced=false`; mismatch via `ILogger`.
- L3: web target company/type dropdowns; server rejects unknown target/company.
- Rate limit: configurable per-IP (`RiderMobile:*`); O(expired) eviction + test.
- Tests: SQL Server 581/581; web build+14 tests; mobile 19/19. Container `gp-m4-sql` removed.

## How to verify
```bash
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true
npm --prefix web run build && npm --prefix web test
(cd mobile && npm test)
```

## Do not
- No merge/deploy/push or live setting change without Ghazal's explicit yes. ERP read-only only.
