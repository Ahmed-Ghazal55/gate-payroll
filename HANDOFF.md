# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | fix/0.9.3-coa-source-bank-excluded from main 231fc43 |
| Live | 0.9.2 (231fc43, 2026-11-02); no push/deployment for this task |
| Updated | 2026-11-03 by Antigravity |

## Goal now
0.9.3 frontend changes completed by Antigravity; ready for Claude review. One local commit only; no push, PR, merge, deployment or real-data access.

## Done (latest first)
- Antigravity updated COA stats cards in Frontend/src/finance-views.ts: replaced mapping-based 4th card with source breakdown (294 Excel, 28 modified, 74 new). Added source badges per account in tree/table and statusNote tooltips/hints (verified on stopped accounts such as 112510).
- Bank file preview updated with collapsible panel «مستبعدين من الملف (N)» closed by default and a 4th stats card. Excluded reasons formatted in Arabic (zero_balance in grey, rider_owes_company in red with signed balance). Excluded rows are strictly non-selectable and omitted from export payload.
- tests/finance-views-ui.mjs updated with synthetic zero/negative balance test cases, asserting source counts (294/28/74), badges, statusNote, excluded panel collapsible behavior, and export restrictions. Saved visual screenshots (desktop & 360px mobile) to test-results/.
- Generator parses the first status word, keeps statusNote/sourceNote, classifies source and exports subledger on every account; validates totals against spec §1 before writing. Synthetic Markdown behavior tests cover parsing and mismatch refusal.
- Regenerated target-coa.v1.json: 396 accounts, 221 active posting, sources excel=294/modified=28/new=74; 75 mapping rows. finance-spec remains read-only; API returns the new fields through its existing JSON resource.
- Bank preview now returns excludedRows with iqama/name/signed balance/reason (zero_balance or rider_owes_company). Positive rows remain exportable; authoritative export rejects excluded identities even after client edits. Synthetic tests cover zero/negative/mixed/no-journal approved runs and refusal without audit writes.
- 0.9.2 SARIE map and code-40 NCBK confirmation unchanged. UI screen logic untouched; only the prompt-listed release/footer/package metadata changed to 0.9.3. API contract documented in docs/api/finance-views.md.
- Checks: .NET 128 passed/0 failed/6 SQL skipped; Python 21 passed with mocked network; standalone finance-views HTTP passed on temporary SQLite. Build: 0 warnings/errors. Generator --check, source boundary (114 files) and whitespace checks passed.
- NuGet restore/network was unnecessary (--no-restore). VSTest/HTTP required sandbox approval for local sockets; SQL connection disabled and only synthetic local data used. No production connection or deploy.
- Shared learning note recorded. The old branch fix/v0.9.1-coa-source-bank-excluded remains at eb8ee0c; no history cleanup or deletion.

## Next step (exact)
1. Claude reviews 0.9.3 after the frontend changes; no push/deployment without Ghazal's explicit approval.
2. SQL Server test coverage remains for CI with an explicitly isolated synthetic database; the six skipped tests are not provider verification.

## Open problems / decisions for Ghazal
- No accounting mapping/posting redesign was included; unresolved historical mappings stay explicit as before.

## How to verify
```bash
python3 tools/generate_target_coa.py --check
python3 scripts/release_source.py --check
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-restore -v minimal
dotnet build Payroll.Api/Payroll.Api.csproj --no-restore -v minimal
env ConnectionStrings__DefaultConnection= node tests/finance-views.mjs
node tests/finance-views-ui.mjs
git diff --check
```
