# HANDOFF

| Field | Value |
|---|---|
| Current writer | Antigravity (waiting for Codex M2.6 on feat/r2-m2) |
| Branch | feat/r2-web (rebased on feat/r2-m1 0f86fa4) |
| Live | 0.10.2 (e776f78); local prepared at 0.11.0 |
| Updated | 2026-11-04 by Antigravity |

## Goal now
Release train R2 frontend: W1 (Login/Setup/Logout) and W2 (Companies & Commercial Registrations) are complete on `feat/r2-web`. Checkpoint 0.11.0 reached. Next is W3 (HungerStation review under `/app/payroll/`), which depends on Codex completing M2.6 on `feat/r2-m2`.

## Checkpoint 0.11.0 (W1 + W2 Complete)
- **W1 — Login, setup, logout (commit d7d4d06):**
  - `/app/login/` with username/password, HTTP 423 (locked) and 429 (rate-limited) alerts.
  - Safe `returnUrl` validation (rejects external/malicious URLs with 400 or fallback).
  - `/app/setup/` with bootstrap key check and registration.
  - 401 redirect handling across API and auth context to `/app/login/?returnUrl=...`.
  - Logout calling `POST /api/auth/logout`.
  - Legacy links updated to `/legacy/#/<view>` and sidebar footer to `/legacy/#/dashboard` («الشاشات القديمة (مؤقت)»).
  - Live e2e suite covering safe returnUrl, unauth redirect, 423 lockout on 6th failed attempt, and logout.
- **W2 — Companies & Commercial Registrations (commit 5651a10):**
  - `/app/companies/` with 4 stat cards (companies, active CRs, confirmations due <= 60d, total capital).
  - Sub-tab 1 «الشركات»: table with BW/EX/TR codes, 700 unified number, CR number, entity type, city, capital, riders count, next annual confirmation (Hijri/Gregorian) with <= 60d warning badge.
  - Drawer with company details, ISIC activity chips (with «ناقص» for BW), documents list/upload/expiry, and audit log.
  - Sub-tab 2 «السجلات التجارية» and Sub-tab 3 «الوثائق والانتهاءات».
  - Toolbar with search, status and entity filters, 60-day expiry toggle, Excel/CSV export, and admin-only preview -> commit company import.
  - Static e2e screenshots at 1440 and 360 in `test-results/r2-web/`.
  - Live e2e suite with synthetic data validating import preview/commit, table rows, drawer, ISIC tags, and sub-tabs.

## Tests & Verification Status
- `npm run build`: Clean static build (16/16 routes), no `/app/app/` links, mock API disabled for production.
- `npm run test:e2e`: All 6 test suites passed; 1440 and 360 screenshots generated.
- `node tests/e2e-live.mjs`: Live authenticated session and end-to-end integration passed against real .NET API.
- `node tests/browser-iban.mjs`: Passed.
- `node tests/finance-views-ui.mjs`: Passed.

## Next step (exact)
Wait for Codex to finish M2.6 on `feat/r2-m2`. When Codex's HANDOFF confirms M2.6:
1. `git rebase feat/r2-m2` in `gate-payroll-r2web`.
2. Build backend: `dotnet build Payroll.Api/Payroll.Api.csproj -p:SkipFrontendBuild=true`.
3. Implement W3: HungerStation review workspace under `/app/payroll/` (invoices upload, dispute tracking, TGA distribution, wallet facts, month close, proposed journals, target scheme disabled banner).
4. Run static and live e2e tests, capture 1440/360 screenshots, and update HANDOFF.
