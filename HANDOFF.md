# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-23 13:44 |
| Branch | `trial/local-2026-11-23` |
| Updated | 2026-11-23 by Cursor (TRIAL1 integration merges) |
| Live | unchanged (no push/deploy; local trial only) |
| Next | Merge remaining W2/W3 into trial branch; regenerate contracts; sqltest |

## Goal now
TRIAL1: local integration of W2/W3 branches for Ghazal to try invoices, pay cycle, balances. Never push / never main / never live DB.

## Done (W3AC)
- `POST /api/sal/simulations` create (AccessPolicy + OpenAPI + contract) with create/duplicate tests.
- Payroll shells redirect to `/payroll/?tab=…`; sidebar no longer uses `/payroll/{app}` for active apps.
- SimulationBanner binds `rows`/`simNet`/`liveNet` and journal `side`/`amount` shapes; client `createSimulation`.
- Approvals maker-checker empty copy; CoA column filters; Jahez error message polish.
- Verify: web 143/143; `api:check` OK; `npm build` OK; sqltest Failed:0 Passed:955.
- Commits on branch (local only). Report: `~/.cache/agents-runs/cursor-W3AC-report.md`.

## How to verify
```bash
npm --prefix web test
npm --prefix web run api:check
npm --prefix web run build
~/.cache/agents-runs/sqltest.sh /home/ghazal/Projects/active/gate-payroll-w3a
```

## Open for Ghazal
- Drop live `hangfire` schema/tables? (code already gone)
- Later: delete `/api/imports/*` free-upload with old Frontend

## Release 0.25.0 (2026-11-19, Claude planner)
- Rider app v2 (`feat/rider-app-v2`): 3-language short statement, notices with read receipts, two-way requests, annual leave form, push in rider language, rate limits. Rider indexes on live are created ONLY by the admin job `POST /api/system/maintenance/rider-indexes` (run off-hours). Ops: grant the 3 new permissions to two different reviewers (`docs/rider-app/RELEASE-NOTES-OPS.md`).

- 2026-11-19 Release 0.29.1 (Claude pro2 deputy): forms/rider-app security fix (`fix/forms-security-0-28`, S28 by Cursor, verified R28V): IBAN masked without `riders.iban.view`, company scope on form responses/submissions, public edit needs the edit token, sidebar/API permissions aligned, statements and requests panels fixed (`docs/runbooks/RELEASE-0.29.1.md`).
- 2026-11-21 Release 0.33.0 PREPARED, NOT MERGED (Claude pro2, DBS2): additive master schemas only (`feat/schema-release`: W1 masters + M4f vault/settlement guards; HS wallet money change parked on `feat/hs-wallet-account-move`). First boot creates 67 new tables + seeds in one transaction; no existing table or row changes. Merge only after Ghazal's full DB backup + download and `sp_spaceused` check. Notes: `docs/runbooks/RELEASE-0.33.0.md`.
- 2026-11-21 Release 0.32.3 (Claude pro2 deputy, DEP13): `fix/tabs-period` (UT1 by Antigravity, UT1F by Cursor, reviewed by Claude in the built app): no duplicate top tab bars (sidebar holds the sections), old `?tab=` links redirect to the right section, one URL period `?period=YYYY-MM` shared by header and page pickers and kept on navigation, remaining English UI labels in Arabic (M-0709, M-0710). Notes: `docs/runbooks/RELEASE-0.32.3.md`.
- 2026-11-22 Release 0.32.4 (Claude pro2 deputy, DEP14): `fix/cascading-column-filters` (forms session, reviewed by Claude): Excel-style cascading column value lists in every client table; rider-app responses show one campaign/builder form at a time with that form's question columns and same-column export; new read-only `GET /api/responses/forms`. Notes: `docs/runbooks/RELEASE-0.32.4.md`.
- 2026-11-21 Release 0.32.2 (Claude pro2 deputy, DEP12): `fix/qa-screens` screen QA fixes (AQ1 by Antigravity, AQ1F + AQ1G by Cursor, reviewed by Claude): table alignment, Arabic labels/statuses everywhere, «ر.س» only on money footers, treasury vault types, audit labels + real Excel exports, trial balance paging, companies file import restored. Open decisions: duplicate top tab bars, header vs page period. Notes: `docs/runbooks/RELEASE-0.32.2.md`.
- 2026-11-21 Release 0.32.1 (Claude pro2 deputy, DEP11): `feat/people-workforce` was already fully released in 0.26.0; review fixes only: live terminated / absence / legal lists now also apply the user's platform scope (any current or ended assignment), «راتب مدد» in the workforce list uses the «ر.س» formatter. Notes: `docs/runbooks/RELEASE-0.32.1.md`.
- 2026-11-21 Release 0.32.0 (Claude pro2 deputy, DEP10): `fix/ops-ui-and-filters` (OF1/FL2 + OF1R fix round): Excel-style column filter on every table (sort, colour, number/date/text conditions, value list with counts and blanks), server filter/sort/distinct contract with column whitelists and bounds, operations fixes (all purchase invoices of the period, Arabic assignment table, fuel card name + iqama under the name + Arabic statuses, fuel-log/oil/purchases exports). Import-centre branch was already in 0.30.0. Notes: `docs/runbooks/RELEASE-0.32.0.md`.
- 2026-11-21 Release 0.31.0 (Claude pro2 deputy, DEP9): 9 read-only live ERP endpoints for the capture stage: Ninja daily performance/deductions with coverage, payment methods, account bearers (L1); fuel, vehicle periods, housing periods, Keeta expected eligibility, HS stamp queue (L2). No screens yet. Notes: `docs/runbooks/RELEASE-0.31.0.md`.
- 2026-11-21 Release 0.30.2 (Claude pro2 deputy, DEP8): FU1 follow-ups: riders/profile-360 platform scope + 404 with HTTP masking tests, audit paged and filtered in SQL with admin server paging, template buttons via `allowedActions` `download_template`, riders-file upload removed from `/workforce`. Notes: `docs/runbooks/RELEASE-0.30.2.md`.
- 2026-11-21 Release 0.30.1 (Claude pro2 deputy, DEP7): old `/legacy/` interface + free-upload `/api/imports/inspect|sheet|preview|commit` removed (CL1); SystemAdmin full permissions + self-approval audit report; `users.manage` can no longer grant Admin/SystemAdmin (CL1F). Notes: `docs/runbooks/RELEASE-0.30.1.md`.
- 2026-11-21 Release 0.30.0 (Claude pro2 deputy, DEP6): import centre removed → upload buttons per page (IM1/H2), INT1F + A2 integrity fixes (rider IBAN/muda masking + scope, sidebar permissions, `audit.view`, links, `formatSar`, invoice company must be chosen), forms `forms.responses.allCompanies` (FP1F) — ops: grant it to unscoped reviewers BEFORE deploy (`docs/runbooks/RELEASE-0.30.0.md`).

## Release 0.29.0 (2026-11-19, Claude pro2 deputy)
- Companies section (`feat/companies-stats`, U-W2-06 / U-W3-03, C1R + C1F): «الإحصائيات والمؤشرات» tab with live per-company sponsorship and platform-account counts (company + platform scope), filter cards that drill to the workforce list, CR drawer tab (`docs/runbooks/RELEASE-0.29.0.md`). Balances per company come later with accounting.

## Release 0.28.0 (2026-11-19, Claude pro2 deputy)
- Forms + rider-app admin as one section «تطبيق المناديب والنماذج» (`feat/forms-app-admin`, R1–R4, R7 notices, U-W3-16/17, X12): 8 sidebar entries, Excel IBAN split, individual-only notices, requests queue, statements notify (`docs/runbooks/RELEASE-0.28.0.md`). Built by Claude (Cursor CLI was not permitted in that session). Bank/Mudad files and request-approval policy wait for Ghazal.

## Release 0.27.0 (2026-11-19, Claude pro2 deputy)
- Operations and fleet (`feat/ops-fleet`, O1–O7 + O1F): live view-only cities, housing, vehicles period report, oil, purchases, fuel; custody / guarantees / cases / accounts tabs removed with redirects (`docs/runbooks/RELEASE-0.27.0.md`). Money items (payments, rents, fuel wallets) wait for Ghazal.

## Release 0.26.0 (2026-11-19, Claude pro2 deputy)
- People section (`feat/people-workforce`, H1–H35 + P1F/P1G): full iqama, live workforce list (filters, cards, one-query export), reduced 360, HR catalogs; document upload checklist removed. Ops: grant `riders.iban.view`, `riders.saudi_salary.view`, `riders.legal_cases.view`, `riders.unified_number.view` to the right roles (`docs/runbooks/RELEASE-0.26.0.md`).
