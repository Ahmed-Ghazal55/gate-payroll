# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r2-m1 from main e776f78 |
| Live | 0.10.2 (e776f78) |
| Updated | 2026-11-04 by Codex |

## Goal now
R2 fix1: correct daily-sync guard behavior on feat/r2-m1, then rebase completed feat/r2-m2. Backend only; frontend work stays on its own branch. Fix1 never connects to staging or ERP; no push, merge, production access or timer enablement.

## Baseline before changes (2026-11-04)
- main e776f78 / live 0.10.2; clean checkout, fast-forward pull already current.
- Build zero warnings/errors; .NET 149 passed / 0 failed / 6 optional SQL-backed cases skipped. Local NuGet cache only.
- Python 24 passed; ten standalone HTTP suites and six browser suites passed. Source boundary check: 150 files.
- COA --check failed as expected (resource 396 versus approved spec 403); assigned to M2.0.

## Done
- Fix1 F2 (this commit): SYNC.md now documents the exact Release build, repeatable override flags, private log fields and refused-run recovery. F1 is 3781961; rebase M2 onto this head and run the complete local suite next.
- Fix1 F1: per-kind absolute/percentage sync thresholds, explicit repeatable new-kind/large-change acceptance and allowlisted overrides in private counts-only logs. Seventeen synthetic import/guard tests and CLI validation passed; staging is untouched in fix1.
- M1.5 complete: release 0.11.0 prepared locally, including allowed footer/package/deploy version touchpoints; route inventory regenerated (157 API routes, six existing deprecations retained). No push/merge/deploy.
- M1 final: .NET 158 passed / 0 failed / 6 optional SQL-backed cases skipped; Python 26 passed; all 11 standalone HTTP and six browser suites passed. Build clean; source boundary 163 files. COA expected mismatch remains assigned to M2.0.
- M1.4 complete: shared SVG favicon and ICO alias on legacy/forms/questionnaires/inquiry/rider app; both payload assets required and hashed. Protected legacy index is verified by FTPS, not unsigned HTTP.
- M1.4 checks: build clean, Python 26 passed, 34 R2 HTTP checks plus legacy browser smoke passed; source boundary 163 files.
- M1.3 complete: two ordered reads per source table, at most three attempts; >10% change / >2% drop refusal before writes; private counts-only logs (700/600), no deletes and preserved certificate fields.
- M1.3 checks: .NET 158 passed / 0 failed / 6 SQL skipped. User unit syntax/timezone verified (03:15 Asia/Riyadh, Persistent=true); units delivered uninstalled/disabled. No live sync executed.
- M1.2 complete: additive company/CR fields, signed administrator-only preview/commit, identity checks, audit and annual-confirmation reminders. Historical M1 dry-run: 3 changed companies + 3 new registrations. The later approved load and preservation evidence are recorded on M2; no data access in fix1.
- M1.2 checks: .NET 154 passed / 0 failed / 6 SQL skipped; 3 certificate behavior tests and 32 R2 HTTP checks passed.
- M1.1 complete: root 302 to /app/, session-only /legacy/, safe returnUrl validation and direct-index guard; public routes unchanged. Restart test verifies every existing record body byte-identical.
- M1.1 checks: .NET 151 passed / 0 failed / 6 SQL skipped; 26 R2 HTTP checks and all six browser suites passed.

## Next step (exact)
Fix1: rebase existing feat/r2-m2 onto this M1 head, preserving its completed CAR hold and certificate-load evidence; then run all local .NET/Python/HTTP/browser suites. Fix1 never connects to staging or ERP and does not enable a timer. Claude reviews M1 then M2; combine the separate login frontend before any release.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 scripts/release_source.py --check
node tests/r2-backend.mjs
git diff --check
```
