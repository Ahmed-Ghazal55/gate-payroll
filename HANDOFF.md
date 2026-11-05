# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex |
| Branch | feat/r6-erp-pulls, based on release/0.14.2 |
| Updated / weekly usage | 2026-11-05; before 86% (latest Ghazal screen) → after unavailable |

## Done
- R6a-1..8 baseline: f86596b; earlier verification 307 .NET passed / 0 failed / 0 skipped on synthetic SQLite + local SQL Server 2022. Counts/history remain in git and private aggregate reports.
- F1 ced524c: proposed_approved debt mapping + 3 new categories/account/permission mappings; no final load approval implied.
- F2 3630d2a: stable vehicle master + immutable append-only vehicle_odometer_reading. Daily values already stored are retained, not refreshed; assignments remain separate. First-kind acceptance is still mandatory.
- F3 d585bf2: approved exact branch aliases as company fallback; conflicts blocked; company_from_branch row-number evidence.
- F4 0277aff: preview-only inactive debt riders; atomic creation on approved load; excluded from payroll/cycles and reactivated with same identity.
- Fix regression tests: full suite 312 passed / 0 failed / 0 skipped on synthetic SQLite + isolated Docker SQL Server 2022; build 0 warnings/errors. F5 keeps normal ERP rider bodies unchanged; clears debtOnly only when reactivating an existing debt-only identity.
- F5 live consistent SELECT-only preview: vehicle 1/558 changed, 557 unchanged; changed field counts plateNumberAr=1, plateNumberEn=1, registrationType=1 (overlap). New readings 13303; REFUSED new_kind_requires_acceptance:vehicle_odometer_reading. No overrides/load.
- Before F2 live drift had moved from old 317/558 to 332/558: currentOdometer315, currentOperationalStatus29, currentAssignmentId27, cityId2, each stable field above1. Private counts: sync-logs/r6a-f2-before.json and r6a-f2-after.json.
- F5 debt preview: 808 rows, 370 valid / 438 flagged; reasons unmatched_company417, negative inputs16, invalid_identity5. Inactive debt-only riders planned0, company_from_branch0. Column D blank707, populated101; no exact approved alias matches. Need verified company evidence, never infer.
- Control totals still blocked (23 control row numbers); mapping has 0 unresolved account columns, but final approved month and reconciliation are still required. Private counts: sync-logs/r6a-fix1-debt-sheet-preview.json.
- No staging writes, push, merge, deploy or remote configuration. Source SELECT-only account enforced. Original files unchanged. Missing sources: sponsorship periods and ADV/payment request tables.

## Next (current authorized task)
- Continue codex-R6a2-hangfire.md R6j-1..4 after the fix commits. Hangfire + ERP sync remain disabled by default. No automatic overrides.
- Latest stable packages are 1.8.25; local cache has 1.8.23. Awaiting Ghazal's answer on package-download exception; do not change NuGet sources.
- Resolve company/branch evidence and reconciliation before Ghazal gives fresh «حمّل». Do not load a refused preview or enable scheduling first. Production requires «حوّل الإنتاج».

## Verify
- dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 (isolated loopback PAYROLL_TEST_SQL_CONNECTION only).
- node tests/erp-sync.mjs; python3 scripts/release_source.py --check; git diff --check.
- CLI examples: docs/data/SYNC.md and DEBT-SHEET.md. Own synthetic Docker test container must be removed after verification; never touch gate-erp-copy-20261103.
