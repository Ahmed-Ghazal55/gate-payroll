# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex |
| Branch | feat/r6c-debts-ops |
| Updated | 2026-11-07 by Codex — R6c continuation |

## Scope / decisions
- Prompt: ~/.agents/prompts/codex-R6c-continue-D1.md, then original D2→D3→D4. Only this worktree; no frontend source changes, push/merge/deploy or real database commit.
- Ghazal: October included_in_net (394 requests load NOTHING); effectiveFrom2026-10, balance2026-10-31. Per-row partial import only for ready rows; missing riders inactive/debt-only, atomically with their opening documents.
- All credential-dependent live commands are for Ghazal/Claude. C0 database move «انقل» must finish before a fresh «حمّل» and opening load; see docs/ops/DB-CUTOVER.md.

## Done / not done
- D1.1–6:51d033f. Workbook source hashes and creditor precedence retained. Previous K5/source totals remain dated evidence, not a current live preview.
- D1.7–11 tooling implemented: exact allowlisted SELECT-only ERP history across all employee statuses/deletions, payroll employees, profiles and optional sponsorship periods. Latest ended-by-cutoff period, else latest known; absent/ambiguous evidence stays blocked. No active rider creation.
- Shared FIFO, separate rider_credit, approved mapping, row-level financial fingerprints, atomic pending openings+inactive identities, cutover/fingerprint guards, read-only verifier and idempotent replay. Correcting a different blocked row later adds only it. No GL/cash posting.
- Live ERP history and payroll preview NOT RUN by Codex. Actual ready/blocked/category totals require operator run. Claude's prior lookup384 blocked is historical baseline, preserved in r6c-debts-review.json.
- Offline reconciliation:394 approved /112,202.64, 4 rejected-sheet omissions,1 draft,19 carried /4,478,3 duplicate references counted once,16 earlier-period items flagged; per-rider/type detail matches.
- Ghazal approved2026-11-07: company sheets +receipts +per-rider/type detail are authoritative. Formatted-summary cash+115 /internet−115 remains warning-only; no October records load. Live history/payroll preview still operator-only.
- Separate private offline outputs: ~/Private/gate-data/sync-logs/r6c-debts-continuation-offline.{json,xlsx},600/700. October references only in private reconciliation lineage, never debt documents.
- D1 tooling commit467f3b0. D2 Keeta DONE: supplier header map and original-sheet selection, scoped signed preview/confirm, duplicate hash refusal, independent classification/approval, disputes and principal-only gated posting via PayPlatforms. No source/real DB import run.
- D2 verification:22 focused .NET regressions passed (Keeta/Ninja/cycles/catalog),0skipped;15 synthetic HTTP checks passed. Contract:docs/api/keeta-invoices.md. D3 operations DONE:3 apps, catalog-owned columns, signed byte/state/actor-bound confirm, actual dated operators, source/deduction reconciliation, saved review only. D4 Chefz NEXT; still no real commit/load.

- D3 verification:18 focused .NET cases passed (operations/catalog/cycles),0skipped;36 synthetic HTTP checks passed. Native historical HS files without normalized invoice facts stay blocked; contract:docs/api/operations-import.md.

## Operator commands — exact order, not agent-executed
1. **Ghazal/Claude** builds this worktree and previews (SELECT only); same source files/read-only, source credentials never printed:
```bash
cd "$HOME/Projects/active/gate-payroll-r6c"
debt_args=(--debt-sheet "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx" --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" --mapping docs/data/debt-sheet-mapping.v1.json --lookup-riders --lookup-erp-history --report "$HOME/Private/gate-data/sync-logs/r6c-debts-review.json")
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --dry-run
```
2. **Ghazal** reviews canCommit, ready/blocked totals; complete database move and give fresh «حمّل». Otherwise STOP, no load.
3. **Claude only**, after those decisions, commits exact approved fingerprint, verifies then may replay same command (added0):
```bash
read -r -p 'Reviewed previewFingerprint: ' debt_preview_fingerprint
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --commit --after-db-move --preview-fingerprint "$debt_preview_fingerprint"
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll "${debt_args[@]}" --verify --preview-fingerprint "$debt_preview_fingerprint"
```
Require verified=true/missing0/changed0 and counts/totals=preview. Source changes require a fresh preview; previously imported changed rows refuse replacement.

## How to verify / limitations
- D1 .NET387/387 passed,0failed/0skipped with isolated synthetic Docker SQL2022 (own test container currently available for D2–D4 tests). Focused debt tests26/26. Build0warnings/errors; cache-only dependencies.
- dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 (explicit isolated synthetic loopback PAYROLL_TEST_SQL_CONNECTION only).
- At finish: python3 scripts/release_source.py --check; python3 tools/api_inventory.py; python3 tools/generate_target_coa.py --check; git diff --check.
- One mistaken early stale-binary preview attempted the payroll target read connection and failed DNS11002 before a database session; no commit/write. Disclosed to Ghazal. Subsequent offline commands use nonexistent --target-env /tmp/gate-r6c-NO-CREDENTIALS-DO-NOT-CREATE, so stale code cannot reach real settings. No secret values printed.
- Full method/operator commands: docs/data/DEBT-SHEET.md. Weekly% before→after unavailable→unavailable.
