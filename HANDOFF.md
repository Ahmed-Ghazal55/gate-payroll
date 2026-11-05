# HANDOFF

| Field | Value |
|---|---|
| Current writer | Codex — R5-C2 advances/org security review |
| Branch / worktree | feat/mobile-sessions / gate-payroll-mob; based on feat/advances-api 565885d |
| Updated | 2026-11-05 by Codex |
| Weekly usage | Before unavailable → after unavailable; usage screen not exposed |

## Goal now
R5-C1 implemented and verified; finish the scoped R5-C2 security review, fix HIGH/CRITICAL only, then hand to Claude. No push, merge, deploy, production/real DB access or changes to web/ or the concurrent gate-payroll-r4api worktree.

## Done
- R5-C1: scoped mobile.sessions catalog/policy; admin list, own list, reason-required revoke/revoke-all and own logout; safe 16-hex session prefix and 8-hex FCM fingerprint; retained revocation history and transactional audit; shared revoked/expired guard for portal/statement/broadcast/ack; five-minute lastSeen throttle with serialized revalidation.
- C1 verification: .NET 274 passed / 0 failed / 7 isolated SQL checks skipped; mobile-sessions HTTP 77, advances HTTP 37, R4 HTTP 56 passed. Source boundary 215 passed; inventory 232 total / 222 API. Cached restore and cached TypeScript compiler used for generated Payroll.Api assets; no frontend source edits.
- Inherited advances backend: OrgDirectory dated memberships/ranks/delegations/supervision/routing; targeted personal tasks; AdvanceRequests request → review/propose → respond → payout with debt and journal, BR §25. See docs/api/advances.md and org.md.
- Inherited R4 backend/web integration is 0.14.0 locally. Reporting staging commit still needs fresh «حمّل»; production switch needs «حوّل الإنتاج»; neither is authorized here.

## Next step (exact)
Codex: review OrgDirectory.cs, AdvanceRequests.cs, Notifications.RaiseFor/CloseTasks and AccessPolicy. After R5-C2, Claude merges feat/mobile-sessions into the 0.14.0 release (not performed here).
Antigravity follow-up: replace «غير متاح بعد» for mobile sessions with the list and reason-required revoke action. Contract: docs/api/mobile-sessions.md. Do not expose full hashes or device tokens.

## How to verify
```bash
# Restore from the existing local package cache if this is a clean worktree.
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 -v minimal
node tests/advances.mjs
node tests/mobile-sessions.mjs
node tests/r4-backend.mjs
python3 tools/api_inventory.py
python3 scripts/release_source.py --check
git diff --check
```
