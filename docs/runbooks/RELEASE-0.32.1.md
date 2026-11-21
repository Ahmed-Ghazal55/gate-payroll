# Release 0.32.1 (2026-11-21)

People section follow-up (DEP11). The branch `feat/people-workforce` (14 commits of 2026-11-19, H1–H35) was already released in full in 0.26.0 (#51): after merging origin/main (0.32.0) into it, its tree was identical to main. This release carries only the fixes from the independent review of the live workforce code (fix round by Cursor, `dd6c12e`, verified by Claude).

## What changed for users
- Workforce → «الإنهاء والغياب» and «القضايا» (live from Omar's ERP): a user limited to some platforms now sees only people who have (or had) an assignment on one of those platforms. Before, these lists used the company scope only, while the directory, cards, export and 360 already used company AND platform. Admins and users without a platform limit see no change.
- Legal cases without a linked employee are hidden for platform-limited users (no platform can be proven).
- Workforce directory: «راتب مدد» amounts show with the shared «ر.س» formatter (two decimals). Text such as «لا يوجد مصدر بعد» stays as is; a hidden value shows «—».

## API
- No new or changed routes. `GET /api/workforce/terminated`, `/api/workforce/absence-cases`, `/api/workforce/legal-cases` add the platform condition to their SQL (parameters only).

## Open (not in this release, for Ghazal / Omar)
- Saudi payroll employees list (`/api/workforce/saudi-payroll-employees`) stays company-scoped only: these employees are not linked to a platform. Decide if platform-limited users should see it.
- Legal case `Status` (int) and absence case `Status` / `CurrentPath` are shown as Omar's raw values: the repo has no meaning for these codes. Arabic labels need Omar's enum list.

## Live safety
- No schema change, no data change, no first-boot migration, no ERP writes. New SQL is SELECT-only and read-only through `WorkforceApi.OpenErp`.

## Ops for Ghazal
- None required. Optional: log in as a platform-limited user and open Workforce → «الإنهاء والغياب».
