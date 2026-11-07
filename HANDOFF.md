# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r6b-accounting |
| Updated | 2026-11-07 |

## Scope and safety
- R6b baseline e75683d (0.15.0); order C0 → C4 → C6 → C7 → C3 → optional C5.
- No real database access/writes, secret reads, push, merge or deploy. ERP db67927 remains SELECT-only; db77738 is the payroll database. No payroll writes before «انقل».
- Only this worktree changed; Claude K2/K4 and Antigravity worktrees untouched. Vouchers__Secret must be provisioned by Ghazal before financial writes.

## Done
- C0 190851c: explicit HR/SQLite-payroll merge, hashed collision inventory, private originals/fingerprints, atomic repeatable import/verification; WAL-safe FTPS snapshot. Live preview/import/switch NOT RUN.
- C4 d42213e: signed43-column Ninja invoice import, classifications/formal disputes, invoice-final differences, scoped approval and posting gates; both payroll/standalone paths prevent duplicate revenue.
- C6 d4302b2: manual drafts without balance effect, independent approval, open-period reversal drafts, attachments and existing-user permission seed with explicit denies preserved.
- C7 032e415: HMAC vouchers, transactional per-company/type/year counters, immutable numbers, Odoo uniqueness/search and public status/type verification30/IP/minute.
- C3 current changes: approved411-account chart/235posting; Mada top-up and internet cash/bank/card payout; company/custodian/balance guards, salary/admin counterparts. K2 approved-request contract tested; no K2 request creation/review implementation here.
- Cutover CLI rehearsal: actual snapshot → preview (no writes) → import → verify → repeat on disposable SQL with synthetic data outside Git; no real private files opened. CLI rejects repository output paths.
- Learning-notes recorded in ~/.agents/learning/log.md; no ~/.agents commit.

## Verification
- Final full .NET365/365 passed,0failed/0skipped on isolated synthetic Docker SQL2022, including CLI rehearsal and company read/payout guards. Own test container removed; old ERP container untouched.
- Build0warnings/errors;18/18 FTPS unit tests;64 combined HTTP checks; source boundary265files; inventory265routes/255API; generator and git diff --check pass. Browser/UI checks NOT RUN (only expected chart counts updated).
- Cache-only NuGet restore (including pinned Hangfire1.8.25); npm offline generated ignored legacy assets for the backend build. No downloads/source changes.

## Next step (exact)
- C3 is ready for local review with the preceding C0/C4/C6/C7 commits. No push/merge/deploy authorization is implied.
- Merge K2 later with review: preserve AdvanceRequests.PayOut internet branch, MadaAccounting.PayInternet, voucher hooks and company read guards. K2 owns catalog/request/review/summary integration; bank fulfilment needs explicit bankAccountCode. K4 review-grid API stays Claude's scope.
- C5 FCM is optional and NOT STARTED after the financial priority work; server delivery/token review remain open. No jobs/FCM activation occurred.
- Ghazal runs the credential-dependent live preview, reviews counts/conflicts, then says «انقل» for that plan. First ERP-kind loads still require fresh «حمّل» afterward.

## Cutover commands — Ghazal only, in order
- Full secure setup/build and secret prompts: docs/ops/DB-CUTOVER.md. No values in chat/history. Keep site and all target writers frozen throughout; Jobs__Enabled=false.
1. Ghazal: cd ~/Projects/active/gate-payroll-r6b; python3 scripts/monsterasp_ftps.py prepare
2. Ghazal: python3 scripts/monsterasp_ftps.py download-data
3. Ghazal: read -r -p 'Printed snapshot path: ' cutover_snapshot; cutover_run="$(dirname "$cutover_snapshot")"
4. Ghazal: dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll snapshot "$cutover_snapshot" "$cutover_run/source.json"
5. Ghazal: dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-preview "$cutover_run/source.json" "$cutover_run/plan.json"
6. STOP unless ready=true, counts reviewed and Ghazal says «انقل»; then: read -r -p 'Reviewed planFingerprint: ' cutover_fingerprint
7. Ghazal: dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-import "$cutover_run/plan.json" "$cutover_fingerprint" --approved
8. Ghazal: dotnet tools/StoreMigration/bin/Debug/net10.0/StoreMigration.dll merge-verify "$cutover_run/plan.json" "$cutover_fingerprint"
9. Ghazal: require verified=true/missing=0/changed=0; set MonsterASP DefaultConnection to db77738, Vouchers__Secret, jobs still OFF; restart with maintenance retained.
10. Ghazal: python3 scripts/monsterasp_ftps.py finish; python3 scripts/monsterasp_ftps.py health
11. Ghazal: authenticated /api/system/release-check must show provider=mssql and expected inventory. Health must show dataPreservationVerified=true and releaseCheckpointPresent=true. Rollback instructions: DB-CUTOVER.md; original SQLite stays untouched.

## Recheck and capacity
- dotnet restore tests/Engine.Tests/Engine.Tests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1; dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 with an isolated synthetic loopback PAYROLL_TEST_SQL_CONNECTION (required0skipped); python3 scripts/release_source.py --check; python3 tools/api_inventory.py; python3 tools/generate_target_coa.py --check; git diff --check.
- Weekly % before → after: unavailable → unavailable; last86% screenshot (2026-11-05) is historical, not current.
