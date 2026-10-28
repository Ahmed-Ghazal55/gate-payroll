# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/v0.8.0-rider-simple-statement |
| Updated | 2026-10-28 by Antigravity |

## Goal now
0.8.0 plain rider statement frontend and backend verified locally; no push or deployment.

## Done (latest first)
- B2 frontend fixed: cards add up to net (`movementSum === statValues[5]`), Net Split cards display salary account (`closingSalaryNet`), Jahez cash (`closingJahezCashNet`), and other debts (`closingOtherDebtNet`). When `scopeLimited=true`, clear warning banner is displayed and "مستحق لك / مستحق عليك" phrases are suppressed (showing `صافي جزئي ضمن نطاقك — لا يمثل الرصيد الكامل`), in screen and print. Frontend build (`npm run build:admin`) and browser tests (`tests/rider-statement-ui.mjs`, `tests/browser.mjs`, `tests/browser-v05.mjs`) pass cleanly.
- B1 backend fixed: scopeLimited/excludedLineCount/closingNetIsComplete flag scope-hidden posted lines (including opening history), disclose no hidden amounts/details, and label partial balances. Scope assertions + four exclusion cases: 102 .NET passed / 6 SQL Server skipped; API docs and learning log updated. No push.
- Plain rider statement UI created in Frontend/src/rider-statement.ts with minimal wiring in enterprise.ts; Payroll.Api untouched.
- Sub-tab "كشف حساب مبسّط" added next to statement-360; "كشف مبسّط" button added in rider rows and debt rows.
- Filters for rider, company and month-range; six stat cards (اتستحقله، اتخصم، استلم، اتحوّل، مديونية، الصافي مع تمييز دائن/مدين بالأخضر والأحمر).
- Month summary table with expandable itemized detail rows; plain Arabic labels without debit/credit terms.
- A4 RTL print stylesheet hiding app chrome and expanding full report for printing.
- Responsive mobile layout, loading, empty, and error handling states.
- Verified with browser tests (tests/browser.mjs, tests/browser-v05.mjs, tests/rider-statement-ui.mjs), dotnet tests, python scripts, and release boundary check.
- GET /api/finance/statement/rider/{iqama}/simple?from=YYYY-MM&to=YYYY-MM&sponsorId= uses store.Read; no schema/migration or existing posting changes.
- Posted lines carrying the rider identity drive opening, monthly buckets, audit details, totals and closing; counterpart rows remain visible without counting twice. All 23 existing transaction types documented; unknowns remain other.
- Closing matches 2110 minus 1210/1211/1230; Jahez cash is separate from salary. Legacy unverified debt flags survive without manufacturing journal openings.
- Sponsor/category/platform scopes and payroll.view checked before aggregation, including historical balances; current legacy warnings follow the current creditor. Admin override retained.
- 98 .NET tests passed, 6 SQL Server tests skipped (isolated connection absent); 21 Python tests passed with mocks. Build includes the existing TypeScript checks. Read-only fixtures and HTTP verify unchanged document snapshots.
- Release literals, health/checkpoint expectations and package metadata are 0.8.0. The only frontend edit is its version label; no UI behavior changed.
- Request/response/field and transaction-type reference: docs/api/rider-statement-simple.md. Learning note recorded in shared log.
- Prior accounting/COA/permission fixes retained; legacy posted classifications were not rewritten.

## Next step (exact)
1. Review docs/api/rider-statement-simple.md and the branch diff; run the commands below.
2. Run the six SQL Server tests in CI with PAYROLL_TEST_SQL_CONNECTION pointing to an isolated test server; no local SQL connection was configured.
3. Future accounting work remains: journal reversal + period close lock; capped company commission release; Jahez 2140/1240 netting. None implemented by this read-only change.
2. Run the six SQL Server tests in CI with PAYROLL_TEST_SQL_CONNECTION pointing to an isolated test server; no local SQL connection was configured.
3. Future accounting work remains: journal reversal + period close lock; capped company commission release; Jahez 2140/1240 netting. None implemented by this read-only change.

## Open problems / decisions for Ghazal
- Existing Jahez cash opening counterpart is still 3101; owner must decide 2140/1240 or per-row policy. This API does not reclassify it.
- Legacy unverified balances remain warnings outside the posted closing net until the existing reconciliation workflow is approved and used.
- Mixed/unlabelled platform lines are withheld from restricted users when attribution cannot be proved; admins retain access. No metadata was backfilled.
- Existing second-approver work for waivers/transfers, account security hardening, staging approval, off-site backups and journal scan performance remain outside this feature.

## How to verify
```bash
cd ~/Projects/active/gate-payroll
dotnet test tests/Engine.Tests -v minimal
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
git diff --check
```
