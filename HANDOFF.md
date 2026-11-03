# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex (R2 backend) |
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
- M1.2 complete: additive company/CR fields, signed administrator-only preview/commit, identity checks, audit and annual-confirmation reminders. Dry-run: 3 changed companies + 3 new registrations, zero writes; fresh «حمّل» approval pending.
- M1.2 checks: .NET 154 passed / 0 failed / 6 SQL skipped; 3 certificate behavior tests and 32 R2 HTTP checks passed.
- M1.1 complete: root 302 to /app/, session-only /legacy/, safe returnUrl validation and direct-index guard; public routes unchanged. Restart test verifies every existing record body byte-identical.
- M1.1 checks: .NET 151 passed / 0 failed / 6 SQL skipped; 26 R2 HTTP checks and all six browser suites passed.

## Next step (exact)
M1.3: implement double-read consistent ERP sync, count/change guards and private counts-only run logs; ship disabled user timer/service. Company certificate staging load stays pending Ghazal's fresh «حمّل»; do not treat the earlier Prompt B approval as authorization for this new load.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 scripts/release_source.py --check
node tests/r2-backend.mjs
git diff --check
```
