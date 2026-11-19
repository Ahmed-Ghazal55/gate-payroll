# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | `feat/rider-app-v2` (worktree `/home/ghazal/Projects/active/gate-payroll-rider-app`) |
| Updated | 2026-11-19 by Antigravity (Task M3 fix round from Review RA1) |
| Live | `/health` = **0.23.0**; no live deploy or setting change from this branch |
| Next | Planner re-reviews `feat/rider-app-v2` M3 fixes and maps §12.2 DB design note; Ghazal places `google-services.json` and runs `mobile/scripts/build-debug.sh` |

## Goal now
Task M3 (all 13 findings from Review RA1: H1–H3, M1–M6, L1–L4) fixed with one commit per fix and verified locally. Ready for planner re-review.

## Done (Task M3 — Review RA1 Fix Round, 2026-11-19)
- **H1 (`03cd4e3`)**: Added dedicated permissions `riders.iban_data.approve_first`, `riders.iban_data.approve_second`, and `mobile.complaints.confidential` in `UserAccess.cs` / `AccessPolicy.cs`; enforced per-step permissions and user-ID (`NameIdentifier`) independence check in `RiderAsks.DecideServiceRequest`.
- **H2 (`3e9afd8`)**: Preserved stored deduction/adjustment sign in `PayStatements.cs` (`BuildShortLines`) so credit adjustments/refunds stay positive («لك»).
- **H3 (`a214409`)**: Reconciled `BuildShortLines` for both `pay_cycle` (salary/commission/penalties/operating cost) and `legacy_run` (keeping negative `platform_settlement` lines); added `shortLinesBalanced` / `shortLinesMismatch` mismatch guard and tests asserting last `runningBalance == closing`.
- **M1 (`d45a2c8`)**: Removed phone-side `buildFallbackShortLines` in `mobile/src/screens/statement.ts`; shows localized "not available" when `shortLines` is missing.
- **M2 (`6a90bb9`)**: Stored 3-language (`ar`/`en`/`ur`) push title/body across all enqueue sites (`FinancialCore.cs`, `RiderAsks.cs`, `PushNotifications.cs`) and resolved payload text by active session `language`.
- **M3 (`6271398`)**: Evicted stale rate-limit windows in `PushNotifications.cs` and enforced per-IP check before auth (`EnforceRiderIpRateLimit`) followed by per-session check after auth (`EnforceRiderSessionRateLimit`).
- **M4 (`627bde5`)**: Added filtered SQL queries (`Store.ListWhere`, `ListByIds`, `ListBroadcastsForRider`, `ListIdsByPrefix`) + SQLite partial indexes and switched rider read paths off full `Store.List` scans.
- **M5 (`a4ebe4a`)**: Removed placeholder `0500000000` / `"الرياض"` defaults on in-app IBAN campaign submit (`RiderAsks.cs`) and added required phone/city inputs in `mobile/src/screens/requests.ts`.
- **M6 (`22ace78`)**: Added planner note `docs/rider-app/DB-DESIGN-MAPPING-NOTE.md` listing the §12.2 DB-design mappings (`app.broadcast_ack`, `rider_service_request`, `hr.iban_change`) without editing main repo design files.
- **L1 (`48d4a0a`)**: Always take submitted in-app form name strictly from the rider record.
- **L2 (`2c1a8de`)**: `CanStaffSeeServiceRequest` denies access when the rider record is missing.
- **L3 (`f727873`)**: Filtered `FormSchemasCatalog` and enforced `SubmitInAppForm` by rider company and form target.
- **L4 (`8f1f8f9`)**: Added 1:1 `ar`/`en`/`ur` i18n key-parity and non-empty value test in `mobile/src/__tests__/api-and-routing.test.ts`.

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
