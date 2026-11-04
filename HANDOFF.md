# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex (R4 backend) |
| Branch | feat/r4-api from main 5fe403b |
| Live | 0.12.1 (5fe403b); no deployment in this task |
| Updated | 2026-11-04 by Codex |
| Weekly remaining | Before100% (Ghazal confirmed renewed); after not yet measured |

## Goal now
Implement codex-R4-api.md in numbered commits: additive pay framework, platform target accounts, operations, reporting projections, rider statement and local0.14.0. Keep Engine.cs, /api/payroll/*, old financial records and all UI source unchanged. BR24 wins over older documents.

## Baseline
Preflight main5fe403b clean/current; build clean. Baseline .NET191 passed/0failed/6SQL skipped; Python26; eleven HTTP and six browser suites passed; source179. COA --check failed as expected403→408. No source/store data loaded.

## Done
- R4.0: COA regenerated408/232 active posting/86new; source metadata BR24 updated. Twenty-one finance/generator tests plus finance HTTP/browser suites passed; --check now passes.
- Read current prompt, HANDOFF/rules/capacity, BR17/22/23/24 and platform/ERP mapping specs; ERP14:00 Riyadh logic inspected read-only. Start permitted after Ghazal confirmed100% renewed capacity.

## Next step (exact)
R4.1: shared per-platform account table and new HS112805 postings; keep old journals unchanged. Continue ordered R4.2–R4.8 and R4.10; R4.9 waits for explicit «حوّل الإنتاج». No staging writes without fresh «حمّل».

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
node tests/r2-backend.mjs
git diff --check
```
