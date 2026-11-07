# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r6c-debts-ops (from feat/import-center e0fcaee / release 0.16.0) |
| Updated | 2026-11-07 by Codex |

## Goal / explicit stop
- Ghazal: D1 → D2 → D3 → D4, **stop after D1 step 6** pending his October decision. No approval for further steps or a real load is implied.
- Only this worktree changed. Claude gate-payroll-ic and Antigravity gate-payroll-r6c-ui untouched. No push/merge/deploy, secrets read, real DB connection/write or source-file modification.

## Done / pending
- D1 steps 1–6: existing DebtSheetImporter extended via partial files; frozen SHA-256/sheet manifests, cached formulas, identity/exponent normalization, three equations, archive comparisons, creditor precedence and conservative October classes. --review-through-six has no Store/write path; --commit is refused; mapping octoberLoadMode=pending_decision also blocks legacy commits.
- Offline corrected source: 808 rows / 807 identities / 2,701,431.66 net; 93 flagged rows. Invalid IDs5, duplicate rows2, negative payments16, negative buckets2, creditor conflicts2. Archive differences =74 main rows /76 comparisons (two rows differ against both archives). No unexplained equation mismatch.
- 158 subcent cached nets explain 0.01 between aggregate rounding and row-rounded sum (2,701,431.65). Both totals retained; no values silently adjusted.
- October394 requests /112,202.64: strict separate255 riders /99,925.14; ambiguous20 /12,277.50; proven included0. K5 heuristic reproduced266 /102,857.64 versus9 /9,345.00; extra11 ambiguous =ten negative-input cases plus one invalid identity, one negative case also mentions October.
- October separate / ambiguous totals by creditor: BW34,321.59 /7,195.00; EX45,679.10 /3,394.50; TR19,924.45 /1,688.00. Rider counts may overlap across creditors. Category totals belong to step8, not run.
- Creditor rule: archive sheet first, exact rider financeSponsorId then sponsorId, else blocked; cross-company keeps sheet. Offline EX66 rows /213,790.53; TR55 /231,741.45; blocked687 /2,255,899.67. **455 nonzero rows need payroll rider lookup; it has NOT run.** Cross-company check remains incomplete until then.
- Private JSON/XLSX: ~/Private/gate-data/sync-logs/r6c-debts-review.{json,xlsx}, files600/dir700. Original hashes/sheet lists rechecked unchanged; no names/identities/raw notes/request numbers copied into reports. Debt export date unavailable; balance cutoff2026-10-31 is not an export date.
- D1 steps7–11 NOT RUN; D2 Keeta / D3 operations / D4 Chefz NOT STARTED. No steps skipped ahead. Full method/results: docs/data/DEBT-SHEET.md.

## Next — exact order / who runs
1. **Ghazal** runs credential-dependent SELECT-only lookup (never ERP, never Store initialization); refreshes the same private reports:
```bash
cd "$HOME/Projects/active/gate-payroll-r6c"
dotnet tools/ErpImport/bin/Debug/net10.0/ErpImport.dll --dry-run --review-through-six --lookup-riders \
  --debt-sheet "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/المديونيات اجمالي - 2026.xlsx" \
  --deductions "$HOME/Projects/pro win/02 بيانات وتشغيل المنصات/استقطاعات المناديب.xlsx" \
  --mapping docs/data/debt-sheet-mapping.v1.json \
  --report "$HOME/Private/gate-data/sync-logs/r6c-debts-review.json"
```
2. **Ghazal** decides October: (a) separate October items on top of openings, phase by type; (b) already in net, load nothing. No-trace is a heuristic, and the20 ambiguous groups remain unresolved. Also choose effective opening month before loading.
3. **Codex waits here.** After that decision, resume D1 step7→8→9→10, preview/review; step11 only after completed «انقل» then fresh «حمّل». Follow docs/ops/DB-CUTOVER.md for the separate database move; Vouchers__Secret is Ghazal's provisioning task.
4. Then D2→D3→D4; leave catalog/other worktrees unchanged until their tasks start.

## How to verify / evidence
- Cache-only restore and build succeeded (0warnings/0errors); generated ignored legacy assets offline, no frontend source edits.
- Final full .NET382/382 passed,0failed/0skipped, on isolated synthetic Docker SQL2022; own container removed. Prior four transient HTTP failures cleared in isolated17/17 rerun and the final full run. Eleven new behavior tests include legacy-commit refusal.
- dotnet restore tests/Engine.Tests/Engine.Tests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false --disable-parallel -m:1
- dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
- dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 (explicit isolated synthetic loopback PAYROLL_TEST_SQL_CONNECTION; never a real DB).
- python3 scripts/release_source.py --check:272files; python3 tools/api_inventory.py:275routes/265API, no inventory diff; python3 tools/generate_target_coa.py --check and git diff --check passed.
- Three CLI refusal cases passed before any DB/credential access; JSON/XLSX readable, private permissions and totals verified. Learning-notes recorded. Weekly% before→after: unavailable→unavailable; no current meter supplied.
