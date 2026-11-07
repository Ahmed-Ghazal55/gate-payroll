# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex |
| Branch | feat/r6b-accounting |
| Updated | 2026-11-07 |

## Goal now
- R6b in order C0 → C4 → C6 → C7 → C3 → C5; baseline e75683d (0.15.0).
- No real database writes before Ghazal says «انقل»; no secrets read, push, merge or deploy.
- Ghazal runs credential-dependent inventory/preview/cutover commands. Local synthetic Docker SQL is allowed.
- db77738 is the payroll database; ERP db67927 is always SELECT-only. Invoice is final (BR31).
- Claude owns K4 review-grid API; do not touch other writers' worktrees.

## Done
- Baseline clean; instructions and BR21/24/25.9/26/28–33 read. Release0.15.0 reports331 tests/0skipped.
- Pinned Hangfire1.8.25 is now present in cache; validate current build rather than the obsolete cache note.

## Next step (exact)
- C0 tooling done: read-only inventory/hashed collisions; explicit merge and originals archived atomically; WAL-safe FTPS download; docs/ops/DB-CUTOVER.md has ordered Ghazal commands. Live preview/import/switch NOT RUN (await Ghazal / «انقل»).
- C0 verification:6/6 synthetic SQLite+Docker SQL tests,18/18 FTPS tests; build0warnings/errors. Full rerun pending after fixing reporting fixture (previous335passed/2fixture failures/0skipped).
- Next C4 Ninja invoices; then C6 manual journals; C7 vouchers; C3 Mada/postings; optional C5 FCM.

## How to verify
- Cache-only dotnet restore tests/Engine.Tests/Engine.Tests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1
- dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 (isolated synthetic loopback SQL;0skipped)
- python3 scripts/release_source.py --check; python3 tools/api_inventory.py; git diff --check.

## Capacity
- Weekly % before → after: unavailable from this session; last user screen86% on2026-11-05 is historical.
