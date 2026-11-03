# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex (feat/1.0-api) |
| Branch | feat/1.0-api from main d063230 |
| Live | 0.9.3 (d063230); local work only |
| Updated | 2026-11-03 by Codex |

## Goal now
Prompt A: API cleanup for 0.10.0 / 1.0. Preserve Minimal APIs and document storage. Antigravity owns web/ in its separate worktree. Never edit Frontend/, RiderForm/ or web/ (Ghazal confirmed version files stay excluded).

## Done (latest first)
- Task 6: global search over four collections, minimum query length and twenty-result cap, same collection gates and company/debt scope; synthetic permission/scope and HTTP tests.
- Task 5: additive company/registration/document CRUD, private attachments and expiry listing; idempotent startup seed for existing databases, companies permissions in live role catalog, read-only finance sponsor alias and audit on every write. DELETE archives; financial references/files remain.
- Task 4: notification inbox/read/summary with per-user state and category permissions; transactional approval/import/answer notices, hourly answer throttle and daily quality notices. Synthetic tests cover isolation, visibility, throttle and rollback.
- Task 3: one opt-in pagination/filter/sort helper for all nine lists; legacy response bytes tested unchanged, page bounds/search/sort tested with synthetic rows.
- Task 2: policy/handler agreement, user PUT alias, owner/view permission for todo, plain broadcast route, JSON errors including duplicate submissions; two policy behavior tests and HTTP checks pass.
- Task 1: built-in admin-only OpenAPI, tags/summaries, generated docs/api/ENDPOINTS.md, bidirectional inventory/OpenAPI HTTP coverage. Includes dynamic deletion and inquiry routes.
- Baseline: .NET 128 passed / 0 failed / 6 SQL skipped; new API HTTP 137 checks passed. Python 21 passed (mocked network); generator/source/whitespace checks passed.
- Restore used only local NuGet cache; no source change or network restore. Frontend compiled from unchanged source using existing TypeScript, output only under Payroll.Api/wwwroot. SkipFrontendBuild supports backend-only builds.

## Next step (exact)
Continue prompt A tasks 7–9, one commit per task. Final next = Claude review of feat/1.0-api; no push, PR, merge or deploy.

## Open problems / decisions
- UI release labels remain 0.9.3 under the explicit no-UI-edit rule; Antigravity handles them.
- SQL provider and production are unverified; only synthetic temporary SQLite is authorized.

## How to verify
```bash
python3 tools/api_inventory.py
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
node tests/api-cleanup.mjs
```
