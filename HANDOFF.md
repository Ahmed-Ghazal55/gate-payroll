# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex (R2 fix1) |
| Branch | feat/r2-m1 from main e776f78 |
| Live | 0.10.2 (e776f78) |
| Updated | 2026-11-04 by Codex |

## Goal now
R2 backend: M1 0.11.0 on feat/r2-m1, then M2 0.12.0 on stacked feat/r2-m2. Backend only; frontend work stays on its own branch. Production is never a target. New staging loads await a fresh «حمّل» after dry-run.

## Baseline before changes (2026-11-04)
- main e776f78 / live 0.10.2; clean checkout, fast-forward pull already current.
- Build zero warnings/errors; .NET 149 passed / 0 failed / 6 optional SQL-backed cases skipped. Local NuGet cache only.
- Python 24 passed; ten standalone HTTP suites and six browser suites passed. Source boundary check: 150 files.
- COA --check failed as expected (resource 396 versus approved spec 403); assigned to M2.0.

## Done
- Fix1 F1: per-kind absolute/percentage sync thresholds, explicit repeatable new-kind/large-change acceptance and allowlisted overrides in private counts-only logs. Seventeen synthetic import/guard tests and CLI validation passed; staging is untouched in fix1.
- M1.5 complete: release 0.11.0 prepared locally, including allowed footer/package/deploy version touchpoints; route inventory regenerated (157 API routes, six existing deprecations retained). No push/merge/deploy.
- M1 final: .NET 158 passed / 0 failed / 6 optional SQL-backed cases skipped; Python 26 passed; all 11 standalone HTTP and six browser suites passed. Build clean; source boundary 163 files. COA expected mismatch remains assigned to M2.0.
- M1.4 complete: shared SVG favicon and ICO alias on legacy/forms/questionnaires/inquiry/rider app; both payload assets required and hashed. Protected legacy index is verified by FTPS, not unsigned HTTP.
- M1.4 checks: build clean, Python 26 passed, 34 R2 HTTP checks plus legacy browser smoke passed; source boundary 163 files.
- M1.3 complete: two ordered reads per source table, at most three attempts; >10% change / >2% drop refusal before writes; private counts-only logs (700/600), no deletes and preserved certificate fields.
- M1.3 checks: .NET 158 passed / 0 failed / 6 SQL skipped. User unit syntax/timezone verified (03:15 Asia/Riyadh, Persistent=true); units delivered uninstalled/disabled. No live sync executed.
- M1.2 complete: additive company/CR fields, signed administrator-only preview/commit, identity checks, audit and annual-confirmation reminders. Dry-run: 3 changed companies + 3 new registrations, zero writes; fresh «حمّل» approval pending.
- M1.2 checks: .NET 154 passed / 0 failed / 6 SQL skipped; 3 certificate behavior tests and 32 R2 HTTP checks passed.
- M1.1 complete: root 302 to /app/, session-only /legacy/, safe returnUrl validation and direct-index guard; public routes unchanged. Restart test verifies every existing record body byte-identical.
- M1.1 checks: .NET 151 passed / 0 failed / 6 SQL skipped; 26 R2 HTTP checks and all six browser suites passed.

## Next step (exact)
Create feat/r2-m2 from this M1 head and start M2.0 (403-account regeneration), then implement the ordered HS intake tasks. Claude reviews feat/r2-m1 before feat/r2-m2; combine the separate Antigravity login frontend before any release merge. Certificate staging load remains pending fresh «حمّل»; no timer installed/enabled.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 scripts/release_source.py --check
node tests/r2-backend.mjs
git diff --check
```
