# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r2-m2 rebased on feat/r2-m1 5f9149d (main e776f78) |
| Live | 0.10.2 (e776f78); local M1 0.11.0 / M2 0.12.0 ready for review |
| Updated | 2026-11-04 by Codex |

## Goal now
R2 ordered backend milestones: M1 0.11.0 is complete on feat/r2-m1; M2 0.12.0 HS finance intake is complete on this stacked branch. No web/Frontend/RiderForm source changes outside the prompt's favicon/version exceptions. Production is never written.

## Baseline before R2
- Build clean; .NET 149 passed / 0 failed / 6 optional SQL-backed cases skipped; Python 24 passed; ten HTTP and six browser suites passed. NuGet local cache only. COA expected mismatch (396→403) assigned to M2.0.

## M1 complete (feat/r2-m1)
- 5fc433a M1.1: root→/app/, session-only /legacy/, safe returnUrl/direct-index protection; public pages retained, byte-identical restart test.
- 9ad6b84 M1.2: signed administrator-only certificate import, additive fields, audit and annual-confirmation reminders.
- 47db423 M1.3: double-read sync (three attempts), original percentage guards (superseded by fix1 below), no deletes, private typed logs, preserved certificate metadata; user units delivered uninstalled/disabled, 03:15 Asia/Riyadh + Persistent=true.
- ef015de M1.4: favicon on all served pages; both payload assets required, protected legacy bytes verified by FTPS.
- 0f86fa4 M1.5: 0.11.0 version touchpoints + generated inventory (157 API routes, six existing deprecations). Final .NET 158 passed / 0 failed / 6 SQL skipped; Python 26 passed; eleven HTTP and six browser suites passed; source boundary 163 files.

## M2 done
- Follow-up 2026-11-04: Ghazal requires the entire invoice journal held until its dispute is resolved, even with the gate on. Guarded approval/core posting, fresh signed preview after resolution, residual-credit settlement hold; .NET 185 passed / 0 failed / 6 SQL skipped and 86 R2 HTTP checks passed. This supersedes earlier open-dispute posting behavior.
- M2.7: 0.12.0 prepared; build zero warnings/errors; final .NET 184 passed / 0 failed / 6 optional SQL cases skipped, Python 26 passed, eleven HTTP and six browser suites passed. COA --check, 179-file source boundary, frontend scope and diff checks passed.
- a75a476 M2.6: ten scoped paginated review routes, signed approvals, formal disputes/responses, explicit TGA split/reversal actions; live role defaults for existing users and target-journal read scope. Twenty-three focused .NET tests and 82 R2 HTTP checks passed; inventory 171 API routes / six existing deprecations.
- 1b4b5ab M2.5: five optional CR-002 contracts discovered by columns, source-not-available counts, immutable approved-fact keys/reversal rows, preview-only account mapping and D20 unknown blockers. Eighteen import/consistency/facts tests passed; no live ERP pull or posting.
- b9e2ab4 M2.4: signed independent facts approval and gated salary/commission/VAT/CAR/TGA journals; target blocks in Arabic. Append-only fact revisions, open-period independent reversal, explicit replacement and deduction cancellation; original journals/source lines retained. Four new behavior tests passed.
- 76dc76d M2.3: approved platformDeducted category/rider/month tags, idempotent audit, stage-2 skip with rider/month/platform isolation and payroll fingerprint binding. Full .NET 172 passed / 0 failed / 6 SQL skipped, including three new deduction behavior tests.
- de9785d M2.2: HS XLSX signed preview/commit, header/month matching, exact VAT/components/orders checks, flags-only V/Z and T-Z/AA, private original evidence, idempotent company/CR/period keys. CAR creates scoped dispute/notice; TGA requires explicit independently approved allocations; Car Damage remains separate.
- M2.2 checks: full .NET 169 passed / 0 failed / 6 SQL skipped; nine HS workbook/rollback/scope/operator tests and 41 R2 HTTP checks passed. Import/allocation never creates journals or rider debts.
- 541e108 M2.1: default-off per-platform settings (administrator + audit); explicit HS target-chart path reuses FinancialCore/AccountingPolicy, refuses legacy 1241/2141 and preserves legacy behavior. Sixteen posting/report tests plus 38 R2 HTTP checks passed.
- b7e38a9 M2.0: regenerated approved COA: 403 accounts, 227 active posting, sources 294 excel / 28 modified / 81 new, including all seven approved additions. Generator --check, 14 finance behavior tests, HTTP and browser finance suites passed.

## Staging / integration
- M1.2: Ghazal approved «حمّل» on2026-11-04. Matching dry-run then staging load: 3 companies changed, 3 registrations added, 6 audits; 0 missing records / unexpected changes, all other raw bodies identical, repeat preview0changes. Counts-only evidence: ~/Private/gate-data/sync-logs/companies-2026-11-04-load-verification.json. Daily sync remains uninstalled/disabled.
- HS ERP contracts remain unavailable per CR-002; no live ERP pull in R2. Review/month-close approvals and signed event/counterparty contracts still need ERP evidence. Car Damage auto-posting, credit-note reconciliation and target scheme remain explicitly blocked/pending.
- Antigravity's separate Next login branch must be combined before release. No push, PR, merge, FTP upload or deployment by Codex.

## Next step (exact)
Claude reviews feat/r2-m1 (5fc433a–5f9149d, including F1/F2), then feat/r2-m2 (b7e38a9–HEAD), including fix1, the latest CAR hold decision and the earlier completed certificate staging load. Planner propagates the clarified CAR decision to the read-only finance spec. Combine separate Next login work before release. No production writes, real posting-gate changes, sync enablement or deployment occurred.

## How to verify
```bash
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
node tests/r2-backend.mjs
# Full validation also ran every executable HTTP/browser tests/*.mjs; financial-fixtures.mjs is a helper.
# Browsers used PLAYWRIGHT_MODULE=/tmp/gate-browser/node_modules/playwright/index.mjs
git diff --check
```

## R2 fix1
- F1 3781961 and F2 5f9149d are complete on feat/r2-m1: safe absolute/percentage thresholds, explicit per-kind acceptance and logged overrides, Release-build/refusal documentation. M2 rebase completed (original8cc0624→6c2574d; backup refs retained). Debug/Release builds clean; .NET189 passed /0failed /6SQL skipped, Python26, eleven HTTP and six browser suites passed; COA/source179 checks passed. No staging/ERP connection, timer enablement or overrides in its unit during fix1.
