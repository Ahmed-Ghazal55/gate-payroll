# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex |
| Branch | feat/r9-payroll-money |
| Updated | 2026-11-08 by Codex |
| Base | 351ed23; P2 local implementation, no push/merge/deploy or live DB access |

## Goal now
R10 F1–F5 → A1–A2 → D1–D5, then R9 P3 (Ghazal latest instruction). Current item: F5; no live writes authorized.
First real Ninja payroll for 2026-10. Ordered task: `~/.agents/prompts/codex-R9-shared-riders-deductions.md`: P2 → P6 → P0 → P1 → P7 → P3 → P4 → P5.
Ghazal explicitly resolved §22.9.2: preserve its exception and fix it in P2 (prompt: Scope of decision 1).

## R10 done locally
- F4: one cycle scan for bank pending riders and one journal scan for debt-confirmation payables, outside rider loops. Existing money/bank regression: 30 passed, 0 skipped.
- F3: Ninja/Keeta discard routes require pay.manage + reason; preserve rows/audit, hide from active lists/drafts/search, allow reference/file replacement, reject approved/posted/cycle sources. 20 focused tests + 26 HTTP contracts passed, 0 skipped; schema/types/index updated.
- F2: stage 8 includes zero-schedule debts/carries; selection capped by balance and positive net, reason off schedule, schedule unchanged. 19 focused tests passed, 0 skipped; HTTP contracts 22 passed; types regenerated.
- F1: company/month/rider source fingerprint; 17 focused tests passed, 0 skipped. Real October files share one temporary API/store: 3 companies/cycles completed, 209 lines, 9 negative captains, 3 balanced journals; synthetic schemes, actual assignments still unverified.

## P2 implemented locally
- Stages 4–6 remain reachable with decision rows. Stage 7 posts reviewed rows only; `partially_approved` keeps others reviewable. Posted rows are frozen; replays do not repost them. Duplicate legacy runs are blocked for partial approvals too.
- Negative commission/invoice entitlement stays reviewable until explicit row review and independent final salary approval; only then create one `NegativeSalaryCarryover` debt/journal on `112507`. Negative journal amounts switch debit/credit sides.
- Ineligible Keeta salary: recover D/current-month fuel up to earnings; unrecovered cost goes to `512121`, never new rider debt. Full D remains income, fuel settles its existing `112511` receivable. GET `/pay/cycles/{id}/unrecovered-costs` gives the management breakdown.
- Personal debt is proposed at stage 5, confirmed separately after salaries (`stage:8` or `/debt-deductions`); per-rider/all confirmation, zero/skip, positive-net and posted-payable caps, atomic balances/journals and replay protection. Pending salary/debt confirmation is excluded from bank export.
- Signed bonuses/compensation/corrections select approved `config_list` group `pay_adjustment_types` (account + direction); financial list and final financial changes require independent users. Vehicle/platform overrides add compensating entries without changing invoice facts.
- Invoice `DEDUCTIONS` keeps captainId; classification per line/category supports company cost, account, or explicit actual rider. Operation imports now accept signed baseGross; other numeric fields retain their validation.
- OpenAPI, generated TypeScript, endpoint inventory and existing API docs updated. Cycle/review/debt responses expose allowedActions; cycle approvalBlockers explains gating. See `docs/api/pay-review.md` for requests.

## Validation
- Full suite: 444 passed / 0 failed / 0 skipped, including isolated SQL Docker 11433. Final focused run: 20 passed / 0 failed / 0 skipped after the last partial-approval duplicate guard and resolved row-status change.
- HTTP contract checks: 22 passed; generated API types check and git diff --check passed.
- Browser R8 flow passed all four size/theme combinations (16 screenshots), including live API advance to stage 6 with a negative review row. Existing static web export was copied locally for regression; new P2 screens are not implemented here.
- Prior P2 proof (superseded by shared-store F1 above): 209 lines (27 + 76 + 106), 9 negative captains, 3 completed cycles and 3 balanced salary journals, no negative debit/credit values; temporary stores deleted.
- Real-file validation uses synthetic identities and a synthetic zero-commission scheme, with company-level invoice deduction classifications; it is not a real-payroll entitlement calculation. Actual schemes for the 9 negative captains were NOT accessed/verified. Negative Ninja target-salary results remain blocked for Ghazal's policy decision; no extension of the Keeta exception.
- No real rows, identifiers, names or amounts were added to repo/logs. Real input manifest remains private at `/tmp/gate-r9-ninja-files.list` (600); local invocation is via temporary `/tmp/gate-r9-real/Probe.csproj` calling `R9RealFilesEvidence.Run` from repo root.
- Verify: set PAYROLL_TEST_SQL_CONNECTION to isolated Docker loopback 11433, then `dotnet test tests/Engine.Tests/Engine.Tests.csproj -m:1 -p:UseSharedCompilation=false`; `node tests/r8-contract.mjs`; `node web/scripts/gen-api-types.mjs --check`; `node web/tests/r8-ninja-flow.mjs`.

## Requests to Antigravity
- Use generated contract and server allowedActions/approvalBlockers; stage 7 partial approval must leave pending rows reviewable. Wire explicit negative-row approval with reason.
- Add stage 8 debt selection/confirm per rider or all, including zero/skip, and show why bank rows are held. Endpoint: GET/POST `/api/pay/cycles/{id}/debt-deductions` (payloads in pay-review.md).
- Adjustment controls must choose approved pay_adjustment_types and send itemType + signed amount + reason; accountCode, if sent, must match the selected item. Add list management and unrecovered-cost report access.
- Invoice classification must expose per-line and batch company/account/rider choices, with expense account or riderId; captainId remains visible as source.
- Add posting-gate screen for `/api/platforms/{code}/posting`, using actual state and administrator permission. The API work is usable locally; end-to-end P2 browser completion awaits these screens.

## Proposed BR text for Ghazal to send Omar (BR file untouched)
- §24.7: «تصل الحالات المحتاجة قرارًا إلى جدول المراجعة ولا توقف اعتماد الصفوف الجاهزة. تسبق الحوافز والتعويضات والتعديلات الاعتماد النهائي. عند الاعتماد النهائي فقط يُرحّل عجز العمولة أو الفاتورة المستحق على المندوب إلى NegativeSalaryCarryover على 112507 بقيد متوازن، مع بقاء استثناء §22.9.2؛ والموجب مستحق للمندوب. يلي اعتماد الرواتب تأكيد مستقل لاستقطاع الديون ثم ملف البنك.»
- §29: «عجز D والوقود لمندوب راتب كيتا غير المؤهل تكلفة شركة على 512121، ولا ينشئ مديونية. تسترد الخصومات الشخصية بحد الصافي الموجب؛ الباقي يظل ذمة مفتوحة دون صافي سالب أو مصروف شركة. لا يرحّل العجز الآخر قبل انتهاء المراجعة. تؤكد الأقساط بعد الرواتب لكل مندوب أو جماعيًا، من صفر إلى المستحق وبحد الصافي الموجب؛ الصفر تأجيل بلا حركة، وتحدث الذمة والقيد معًا دون تكرار.»
- §32: «تأتي بنود الحوافز والتعويضات والتعديلات من قائمة مالية معتمدة تحدد الحساب والاتجاه. كل تعديل بسبب مكتوب وله قيد متوازن. اعتماد الصفوف الجاهزة لا يعتمد الحالات المعلقة؛ يبقى المعتمد مستقلًا عن التجهيز والمراجعة. بعد اعتماد الراتب يؤكد مراجع مستقل اختيار استقطاع الدين أو تأجيله، ولا يدخل المندوب ملف البنك قبله. التصحيح بعد الاعتماد بالعكس فقط.»

## Next step (exact)
**Continue R10 at F5 in `~/.agents/prompts/codex-R10-review-fixes-access-erp.md`** (Claude's P2 review: F1-F5, then A1-A2, D1-D5), then R9 starting at P3. The text below is superseded where it differs.
P2 backend implementation and validation complete locally; save as `feat(r9): flexible payroll review and debt confirmation (P-2)`. Claude reviews/integrates, Antigravity wires the requests above. Then P6 (account-exception design first; do not implement that exception before Ghazal confirms its design). P6/P0/P1/P7/P3/P4/P5 not started in this P2 continuation.
Before real October payroll: approve global/Ninja settings; supply assignments and operations (`docs/runbooks/NINJA-2026-10.md`). Live writes still require step-specific «حمّل».
Weekly % before → after: unavailable; only Ghazal's usage screen is authoritative.
