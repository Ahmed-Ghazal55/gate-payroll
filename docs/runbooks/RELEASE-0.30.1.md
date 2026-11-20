# Release 0.30.1 (2026-11-21)

Clean-up release on top of 0.30.0: the old interface and the free-upload import endpoints are removed; the System Administrator role gets its full permissions and a self-approval audit report (task CL1 + fix round CL1F, decisions M-0621 and M-0667, BUSINESS_RULES §35).

## What changed for users
- **Old interface removed.** The pre-Next.js admin screens (`/legacy/`) are gone. `/legacy/`, `/legacy` and `/index.html` now redirect to `/app/`. The Next.js app under `/app/` is the only admin interface. The login page accepts a return address under `/app/` only. Public forms, inquiry and the rider pages are unchanged.
- **Free-upload endpoints removed.** `POST /api/imports/inspect`, `GET /api/imports/{id}/sheet`, `POST /api/imports/{id}/preview` and `POST /api/imports/{id}/commit` no longer exist (404). Only the old interface used them. Imports run from the page buttons added in 0.30.0. `GET /api/imports` (batch list) and `GET /api/imports/templates/*` stay.
- **System Administrator = full permissions.** The role «مسؤول النظام» (`SystemAdmin`, rank 5) now holds every permission in the catalog and counts as an administrator. Like BUSINESS_RULES §35, it holds the maker-checker exception by default: when it approves its own work, the approval is recorded as `approval.bypass_maker_checker` with badge `system_admin` and reason `system-admin-default`. Its role template is fixed (cannot be edited from the roles screen).
- **Self-approval audit report.** `GET /api/admin/approval-exceptions` now lists every self-approval: old dated exceptions, bypass-grant uses and System Administrator self-approvals (for the monthly review).
- **Security fix (user management).** A user with only `users.manage` can no longer create or edit a user with the Admin or SystemAdmin role, edit an administrator, or grant permissions they do not hold. Before, `POST /api/users` and `PUT /api/users/{id}` skipped this check. The last active administrator (now including SystemAdmin) cannot be suspended.

## Ops note for Ghazal
- Give the `SystemAdmin` role only to the system administrator account. Anyone with this role can manage users and approve their own work (audited).
- Review the self-approval report monthly (§35, M-0673).
- Old files of the removed interface (`index.html`, `app.js`, `enterprise.js`, `table-tools.js`, `bootstrap.js`) may stay on the server, because the deploy does not delete files. `/index.html` redirects; the scripts hold no data. They can be deleted by hand later.

## Live safety
- No schema change, no data wipe, no ERP writes.
- First boot runs one marker-guarded migration (`migration:system-admin-permissions:v1`, runs once): it saves the `SystemAdmin` role template with the full catalog, writes one audit row per System Administrator user and one for the template, and saves the marker setting. Nothing else changes.
- The test-only endpoint `POST /api/test/seed-work` is mapped only when the environment is Development. Live runs as Production (`/api/auth/status` → `"environment":"Production"`), so it does not exist there.
