# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | release/0.16.0 (integration of feat/k-review + feat/r6b-ui + feat/r6b-accounting) |
| Updated | 2026-11-07 by Claude |

## Release 0.16.0 (2026-11-07, Claude), branch `release/0.16.0`
- Integrates `feat/k-review` (K1–K4) + `feat/r6b-ui` (A0–A8) + `feat/r6b-accounting` (C0, C3, C4, C6, C7). C5 FCM and A9 QR not started.
- Review fixes: K2 card-payout 409 removed (C3 posts it via `MadaAccounting.PayInternet`); internet-advance test uses scoped principals (Codex's company scope kept); general bell excludes personal count; CI gets a synthetic `Vouchers__Secret`.
- Verified locally: .NET 367/367 (SQL Server in Docker, 0 skipped); 20 node/browser suites incl. published-app, `r6b-accounting`, `r4-backend`, `ui-qa`; `release_source --check`.
- **Before «ادمج» (Ghazal):** set `Vouchers__Secret` (random 32–256 chars) in MonsterASP → Scripting → Environment variables. Without it every new financial document (journal, payout, settlement, cycle posting) returns 503.
- DB move (C0) is a separate step after «انقل»: follow `docs/ops/DB-CUTOVER.md` and the numbered commands below.

## Wave 2 — Antigravity R6b UI (2026-11-07), branch `feat/r6b-ui`
All tasks A0 through A8 from `~/.agents/prompts/antigravity-R6b-ui.md` completed, tested, and verified on 1440px and 360px:
- **A0 (Page Sweep & Visual QA):** Completed (`765b24a`). Fixed numeric and text table alignment using logical classes (`text-start`/`text-end`), resolved RTL contrast issues, eliminated horizontal page scroll across all 16 views.
- **A1 (Header Shortcuts & Personal Notifications):** Completed (`d14bde3`). Added `Zap` shortcuts menu strictly filtered by `can()` (Supervisors see only the two advance forms; omitted if 0 permissions). Added `UserCheck` personal notifications icon next to the bell, reading from `summary.byCategory.personal` while general bell excludes personal.
- **A2 (ERP Sync Drawer Panel):** Completed (`b432cbb`). Built `ErpSyncDrawer.tsx` with live 3s polling, dry-run & commit actions, refusal code mapping to friendly Arabic, counts table, and last 5 runs history.
- **Prerequisite Merge:** Completed (`b15cb09`). Clean merge of `feat/k-review` into `feat/r6b-ui` with 0 conflicts and 0 build errors.
- **A3 (Payroll Review Grid - BR §32):** Completed (`703cf77`). Built `PayrollReviewGrid.tsx` and `PayrollReviewDrawer.tsx` (ResponsesTab pattern) with phase groupings, debt installment edit (0..max), salary adjustments (+/−), single-row approval, bulk approve dialog with skipped count, sticky totals bar, and Stage 6 integration.
- **A4 (Config Lists Panels - BR §27):** Completed (`a2fd327`). Built `ConfigListsPanel.tsx` in `/app/admin` with tabs for all 6 groups, custom field schema builder, item add/edit dialog, active/inactive toggle, and maker-checker approval badge for financial groups (`pay.approve`).
- **A5 (Statement Phases - BR §29):** Completed (`a80825a`). Added phased subtotals bar (Gross → Commission → Phase 1 → Preliminary Due → Phase 2 → Phase 3 → Net) in rider monthly statement (`/app/riders/statement`) and cycle stepper (`/app/payroll`).
- **A6 (Internet Advance Form - BR §25.9 / K2):** Completed (`18cb691`). Added «سلفة نت» with fulfilment options (نقدي / بنكي / كارت شحن مكنة مدى). Card requests auto-populate package price as read-only from approved config lists (`recharge_networks` / `recharge_packages`). Treasury payout queue displays network and package.
- **A7 (Manual Journal Screen - BR §28 / C6):** Completed (`701c7a9`). Built `ManualJournalsPanel.tsx` in `/app/accounting` (also opened via shortcut `?action=manual_journal`), lines grid (min 2 lines), COA posting accounts selector, live debit/credit balance indicator (green when balanced, red when diff), 2MB attachment uploader (base64), draft list, maker-checker approval, and reversal modal with mandatory reason.
- **A8 (Ninja Invoice Reconciliation - BR §26, §31):** Completed (`e438b38`). Built `NinjaInvoiceUploadScreen.tsx` supporting 1–5 supplier `.xlsx` files against `POST /api/platforms/ninja/invoices/reconcile`. Prominent BR §31 banner: «المعتمد هو الفاتورة — الفرق للعلم», totals comparison, invoice adjustments table, and captain-by-captain comparison grid highlighting differences and reasons. Accessible via `/app/payroll?tab=ninja_upload` and platforms tab.
- **A9 (Voucher QR & Search):** Waiting for Codex C7.

### Verification Summary
- `npm run build`: PASS (static export generated with 0 errors)
- `python3 scripts/package_web.py`: PASS
- `dotnet build tests/Engine.Tests/Engine.Tests.csproj`: PASS (0 warnings, 0 errors)
- `node web/tests/ui-qa.mjs`: PASS (all Playwright checks passed with zero errors; 32 screenshots generated at 360 and 1440 in `test-results/r6b-ui/`)
- Browser test suites: PASS (`sessions-ui.mjs`, `rider-app-ui.mjs`, `rider-asks-ui.mjs`, `finance-views-ui.mjs`, `browser.mjs`)

## Wave 2 — Claude K1–K4 (2026-11-07), branch `feat/k-review`
- K1: config lists API (`ConfigLists.cs`), 6 groups, custom fields, versions, maker-checker for financial groups, seeded defaults (BR §27/§29).
- K2: internet advance (`type=internet`, fulfilment cash|bank|recharge_card, package price from lists, custody `MADA`). Card payout is 409 until Codex C3.
- K3: every cycle deduction has phase/order/label; installments taken in phase order; rider summary + statement show gross → commission → phase1 → المستحق الأولي → phase2 → phase3 → net.
- K4: review grid `GET/POST /api/pay/cycles/{id}/review…` (BR §32); stage 7 needs all rows approved.
- COA regenerated from the spec: +112404, 421302, 512122 (411 total / 235 posting / new 89). Generator accepts «جديد (BR …)».
- Verified: .NET 323 passed / 10 skipped (SQL needs Docker). Docs: `docs/api/pay-review.md`.
- Parallel: Codex `feat/r6b-accounting` (prompt `~/.agents/prompts/codex-R6b-accounting.md`), Antigravity `feat/r6b-ui` (prompt `antigravity-R6b-ui.md`). Antigravity merges `feat/k-review` for A3–A6.

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
