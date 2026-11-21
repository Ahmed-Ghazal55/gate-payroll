# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `chore/s1-remove-sync-hangfire` |
| Updated | 2026-11-19 by Cursor (S1F fix round done, not pushed) |
| Live | unchanged (no push/deploy) |
| Next | Planner/Ghazal review S1+S1F; do **not** drop live Hangfire tables until Ghazal says yes |

## Goal now
S1F review fixes on the S1 branch: pay-file imports page back, ImportPlan tests restored, role-editor/sync-baseline cleanup.

## Done (S1F)
- HIGH: `web/src/app/imports/page.tsx` catalog uploads; sidebar «استيراد الملفات»; payroll `import_operations` → `/app/imports`.
- MEDIUM: `tests/Engine.Tests/ImportPlanTests.cs` (6 assertions, no ImportExecutor).
- LOW: `erp.sync.run` hidden/deprecated in editor; sync-baseline out of contract/e2e; `/api/imports/*` keep note.
- Commits: `e407495`, `6da0233`, `ef2a5d2`. Report: `~/.cache/agents-runs/cursor-S1F-report.md`.

## How to verify
```bash
dotnet build Payroll.Api/Payroll.Api.csproj
dotnet test tests/Engine.Tests/Engine.Tests.csproj -p:SkipFrontendBuild=true
npm --prefix web run build
```

## Open for Ghazal
- Drop live `hangfire` schema/tables? (code already gone)
- Later: delete `/api/imports/*` free-upload with old Frontend

## Release 0.25.0 (2026-11-19, Claude planner)
- Rider app v2 (`feat/rider-app-v2`): 3-language short statement, notices with read receipts, two-way requests, annual leave form, push in rider language, rate limits. Rider indexes on live are created ONLY by the admin job `POST /api/system/maintenance/rider-indexes` (run off-hours). Ops: grant the 3 new permissions to two different reviewers (`docs/rider-app/RELEASE-NOTES-OPS.md`).

- 2026-11-19 Release 0.29.1 (Claude pro2 deputy): forms/rider-app security fix (`fix/forms-security-0-28`, S28 by Cursor, verified R28V): IBAN masked without `riders.iban.view`, company scope on form responses/submissions, public edit needs the edit token, sidebar/API permissions aligned, statements and requests panels fixed (`docs/runbooks/RELEASE-0.29.1.md`).
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
