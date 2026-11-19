# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/rider-app-v2` (worktree `/home/ghazal/Projects/active/gate-payroll-rider-app`) |
| Updated | 2026-11-19 by Antigravity (Argon, Task M2 complete rider mobile app) |
| Live | `/health` = **0.23.0**; no live deploy or setting change from this branch |
| Next | Claude reviews `feat/rider-app-v2`; Ghazal places `google-services.json` and runs `mobile/scripts/build-debug.sh` |

## Goal now
Task M2 (`feat/rider-app-v2`) complete and verified locally. Ready for planner review.

## Done (Task M2 — Rider Mobile App v2, 2026-11-19)
- **Short 3-language statement & Home summary (`Payroll.Api/PayStatements.cs`, `mobile/src/screens/statement.ts`)**: `shortLines` with `ar` / `en` / `ur` labels, running balance, and «لك / عليك» (`For you` / `On you`) markers from `PayStatements.Build` (one source rule, approved payrolls only); `LatestApprovedSummary` on `GET /api/rider-mobile/portal/{iqama}`; no company/organisation names on rider-facing screens; non-legal disclaimer.
- **Debts summary (`mobile/src/screens/debts.ts`, `Payroll.Api/FinancialCore.cs`)**: simple 3-language vertical debt cards with «عليك» markers, totals, and monthly instalments; company/sponsor names stripped from rider-facing views.
- **Notifications (`Payroll.Api/FinancialCore.cs`, `Payroll.Api/PushNotifications.cs`, `mobile/src/screens/notifications.ts`)**: (a) batched general broadcasts to all registered riders; (b) individual-only notices (`summons`, `dismissal_warning`, `administrative_deduction`, `appointment_alert`, `administrative_warning`) that reject `iqama="ALL"` with HTTP 400, force `requiresAck=true`, and store timestamped read and acknowledgement receipts (`readReceipts`) via `POST /api/rider-mobile/broadcasts/{id}/read` and `/ack`; deep links to app tabs and in-app forms.
- **Unified forms & two-way requests (`Payroll.Api/RiderAsks.cs`, `RiderForm/src/rider-form.ts`, `mobile/src/screens/requests.ts`)**: one forms system (`GET /api/rider-mobile/forms`, `POST /api/rider-mobile/forms/{token}/submit`) pre-filling rider identity from the active session without re-typing Iqama; 7 request types (`advance` to current supervisor → accountant, new `annual_leave` form `annual-leave-v1`, `iban_update` with two independent approvers or auto-approve on match, `data_update` with two independent approvers, `statement_request`, confidential `complaint` hidden from supervisors/regular staff, `appointment`), outbound admin-to-rider requests (`POST /api/rider-mobile/service-requests`), status timeline, and two-way replies with FCM push notifications.
- **Language switch & load safety (`mobile/src/i18n.ts`, `Payroll.Api/PushNotifications.cs`)**: `ar` / `en` / `ur` everywhere with RTL/LTR switching and `POST /api/rider-mobile/language`; per-rider rate limit (60 read/min, 20 write/min, HTTP 429 + `Retry-After: 60`), bounded/paged reads, and idempotent bulk statement notify (`POST /api/rider-mobile/statements/notify-period`) via outbox batches without salary figures in FCM payloads.
- **Guide & inventory**: `docs/rider-app/RIDER-APP-GUIDE.md` (English + full Egyptian Arabic); `docs/api/ENDPOINTS.md` regenerated.

## Next step (exact)
1. Review `feat/rider-app-v2` diff and `docs/rider-app/RIDER-APP-GUIDE.md`.
2. Place Firebase `google-services.json` at `~/Private/gate-data/firebase/google-services.json` and run `cd mobile && ./scripts/build-debug.sh` to produce `~/Private/gate-data/mobile/gate-rider-debug.apk`.

## How to verify
```bash
dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true --filter "FullyQualifiedName~PushNotificationsTests|FullyQualifiedName~MobileSessionTests|FullyQualifiedName~PayStatementTests"
(cd mobile && npm test && npm run build)
Vouchers__Secret="synthetic-voucher-secret-for-offline-test-only-0123456789" node tests/mobile-sessions.mjs
Vouchers__Secret="synthetic-voucher-secret-for-offline-test-only-0123456789" node tests/rider-asks.mjs
Vouchers__Secret="synthetic-voucher-secret-for-offline-test-only-0123456789" node tests/api-cleanup.mjs
```

## Do not
- No merge/deploy/push or live setting change without Ghazal's explicit yes. ERP read-only only.
