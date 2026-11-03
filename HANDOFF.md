# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex (R2 backend) |
| Branch | feat/r2-m2 rebased on feat/r2-m1 5f9149d (main e776f78) |
| Live | 0.10.2 (e776f78); local M1 prepared at 0.11.0 |
| Updated | 2026-11-04 by Codex |

## Goal now
R2 ordered backend milestones: M1 0.11.0 is complete on feat/r2-m1; M2 0.12.0 HS finance intake is in progress on this stacked branch. No web/Frontend/RiderForm source changes outside the prompt's favicon/version exceptions. Production is never written.

## Baseline before R2
- Build clean; .NET 149 passed / 0 failed / 6 optional SQL-backed cases skipped; Python 24 passed; ten HTTP and six browser suites passed. NuGet local cache only. COA expected mismatch (396→403) assigned to M2.0.

## M1 complete (feat/r2-m1)
- 5fc433a M1.1: root→/app/, session-only /legacy/, safe returnUrl/direct-index protection; public pages retained, byte-identical restart test.
- 9ad6b84 M1.2: signed administrator-only certificate import, additive fields, audit and annual-confirmation reminders.
- 47db423 M1.3: double-read sync (three attempts), >10% changes / >2% drop guards, no deletes, private typed logs, preserved certificate metadata; user units delivered uninstalled/disabled, 03:15 Asia/Riyadh + Persistent=true.
- ef015de M1.4: favicon on all served pages; both payload assets required, protected legacy bytes verified by FTPS.
- 0f86fa4 M1.5: 0.11.0 version touchpoints + generated inventory (157 API routes, six existing deprecations). Final .NET 158 passed / 0 failed / 6 SQL skipped; Python 26 passed; eleven HTTP and six browser suites passed; source boundary 163 files.

## M2 done
- M2.2: HS XLSX signed preview/commit, header/month matching, exact VAT/components/orders checks, flags-only V/Z and T-Z/AA, private original evidence, idempotent company/CR/period keys. CAR creates scoped dispute/notice; TGA requires explicit independently approved allocations; Car Damage remains separate.
- M2.2 checks: full .NET 169 passed / 0 failed / 6 SQL skipped; nine HS workbook/rollback/scope/operator tests and 41 R2 HTTP checks passed. Import/allocation never creates journals or rider debts.
- M2.1: default-off per-platform settings (administrator + audit); explicit HS target-chart path reuses FinancialCore/AccountingPolicy, refuses legacy 1241/2141 and preserves legacy behavior. Sixteen posting/report tests plus 38 R2 HTTP checks passed.
- M2.0: regenerated approved COA: 403 accounts, 227 active posting, sources 294 excel / 28 modified / 81 new, including all seven approved additions. Generator --check, 14 finance behavior tests, HTTP and browser finance suites passed.

## Pending staging / integration
- M1.2 SELECT-only dry-run proposed 3 changed companies + 3 new registrations. Fresh «حمّل» approval pending; zero new staging writes in R2. Daily sync not run/installed/enabled.
- Antigravity's separate Next login branch must be combined before release. No push, PR, merge, FTP upload or deployment by Codex.

## Next step (exact)
M2.3: persist approved platform-deduction tags and skip matching rider/month/category in stage-2 deductions, with behavior tests. Then M2.4: Ghazal's pending CAR question must be resolved before choosing the disputed-invoice posting behavior. Certificate load also awaits fresh «حمّل». No later ordered tasks may be skipped ahead.

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

## R2 fix1
- F1 3781961 and F2 5f9149d are complete on feat/r2-m1: safe absolute/percentage thresholds, explicit per-kind acceptance and logged overrides, Release-build/refusal documentation. M2 rebase and full local validation are in progress; staging is untouched in fix1.
