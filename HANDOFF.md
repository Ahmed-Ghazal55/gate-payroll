# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/1.0-api from main d063230 |
| Live | 0.9.3 (d063230); local work only |
| Updated | 2026-11-03 by Codex |

## Goal now
Fix Round 1 (Claude review of 229f7cc..20507f1) complete: NextApp deep links in /app fixed and verified. One commit: `fix(api): Next deep links (1.0-A)`. No push, PR, merge or deployment without Ghazal.

## Done (latest first)
- Fix Round 1 (Claude Review A1–A2):
  - A1 (blocking): Fixed `NextApp.Fallback(path, webRoot)` deep links resolution order:
    1. `app/<path>/index.html`
    2. `app/<path>.html`
    3. `app/404.html` (returned with HTTP 404)
    4. `app/index.html` (SPA fallback)
    Enforced extension rule (unknown assets with extensions return 404) and path traversal guard rejecting `..` and `\`.
    - Unit tests in `NextAppTests.cs` verify synthetic webroot with `index.html`, `forms/index.html`, and `404.html`.
    - HTTP checks in `tests/api-cleanup.mjs` verify `/app/forms/` returns the forms file and traversal `/app/x/../../appsettings.json` is refused.
  - A2 (minor): Noted that `/health` reports 0.10.0 while old UI footer stays 0.9.3 (Frontend/ kept untouched per Ghazal's instruction; Antigravity handles UI version bump).
- Task 9: optional Next build/copy before publish, required /app/index.html payload checks, physical static files before explicit routing, extensionless-only SPA fallback. Packaging tested with synthetic exports and clean skip when web/ is absent; backend/test/deploy release metadata now 0.10.0; API_CONTRACT points to ENDPOINTS.md.
- Final checks: build 0 warnings/errors; .NET 140 passed / 0 failed / 6 SQL skipped; Python 24 passed (network mocked); all 10 HTTP suites and 5 Chromium suites passed. API cleanup alone: 251 checks. Screenshots in ignored test-results/.
- Inventory: 155 API operations (24 new), 6 deprecated; 166 total including HTML/health/OpenAPI. Every /api operation and its tags/summary verified in both directions against OpenAPI. Caller inventory also handles the old UI API helper.
- Final compatibility fixes: duplicate-409 tests preserve the previous fields plus error/code; answer notices use a rolling hour and include Excel answers; static file/fallback HTTP checks use a temporary webroot. Shared learning entry recorded.
- Task 8: six legacy operations explicitly deprecated in the human index and OpenAPI (including test-only release-check); all routes retained. HTTP verifies deprecation metadata.
- Task 7: unified campaign/custom responses inbox with one row shape, source/status/parent filters and opt-in pagination; review mutations remain on existing endpoints. Synthetic permission/shape/filter and HTTP tests.
- Task 6: global search over four collections, minimum query length and twenty-result cap, same collection gates and company/debt scope; synthetic permission/scope and HTTP tests.
- Task 5: additive company/registration/document CRUD, private attachments and expiry listing; idempotent startup seed for existing databases, companies permissions in live role catalog, read-only finance sponsor alias and audit on every write. DELETE archives; financial references/files remain.
- Task 4: notification inbox/read/summary with per-user state and category permissions; transactional approval/import/answer notices, hourly answer throttle and daily quality notices. Synthetic tests cover isolation, visibility, throttle and rollback.
- Task 3: one opt-in pagination/filter/sort helper for all nine lists; legacy response bytes tested unchanged, page bounds/search/sort tested with synthetic rows.
- Task 2: policy/handler agreement, user PUT alias, owner/view permission for todo, plain broadcast route, JSON errors including duplicate submissions; two policy behavior tests and HTTP checks pass.
- Task 1: built-in admin-only OpenAPI, tags/summaries, generated docs/api/ENDPOINTS.md, bidirectional inventory/OpenAPI HTTP coverage. Includes dynamic deletion and inquiry routes.
- Generator --check, source boundary (134 allowlisted files), and whitespace checks passed. No Frontend/, RiderForm/ or web/ source changes; no push, PR, merge, deployment or production connection.
- Restore used only local NuGet cache; no source change or network restore. Frontend compiled from unchanged source using existing TypeScript, output only under Payroll.Api/wwwroot. SkipFrontendBuild supports backend-only builds.

## Next step (exact)
Claude or Ghazal review of feat/1.0-api fix round 1. No push, PR, merge or deployment without Ghazal.

## Open problems / decisions
- UI release labels remain 0.9.3 under the explicit no-UI-edit rule; Antigravity handles them.
- SQL provider and production are unverified; only synthetic temporary SQLite was used. Real Next build/export is skipped here because web/ is absent on this branch; the conditional CI path and packaging contract are tested with synthetic fixtures.

## How to verify
```bash
python3 tools/api_inventory.py
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
node tests/iban-form.mjs
node tests/api-cleanup.mjs
env ConnectionStrings__DefaultConnection= PLAYWRIGHT_MODULE=/tmp/gate-browser/node_modules/playwright/index.mjs node tests/browser.mjs
# Also browser-v05, browser-iban, rider-statement-ui, finance-views-ui (same environment).
```
