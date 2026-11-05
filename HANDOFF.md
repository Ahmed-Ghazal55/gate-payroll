# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch / worktree | feat/mobile-sessions / gate-payroll-mob; based on feat/advances-api 565885d |
| Updated | 2026-11-05 by Codex |
| Weekly usage | Before unavailable → after unavailable; usage screen not exposed |

## Goal completed
R5-C1 and R5-C2 completed locally. Next: Claude merges feat/mobile-sessions into the 0.14.0 release. No push, merge, deploy, production/real DB access, web/ edits or access to the concurrent gate-payroll-r4api worktree.

## Done
- R5-C1 commit 6971a0e: mobile.sessions permission/catalog; company-scoped list, own list, audited reason-required revoke/revoke-all and own logout; safe 16-hex session prefix and 8-hex FCM fingerprint; retained history; shared revoked/expired checks for portal/statement/broadcast/ack; five-minute lastSeen throttle with serialized revalidation. Contract: docs/api/mobile-sessions.md.
- R5-C2: fixed the two HIGH findings below; 6 additional Engine behavior tests cover supervision, personal privacy, payout replay/concurrency/overdraft, mass assignment and org write permissions. Existing maker-checker and routed vault custody remain enforced.
- Final verification: clean build (0 warnings/errors); .NET 280 passed / 0 failed / 7 isolated SQL checks skipped; HTTP advances 57, mobile-sessions 77, R4 backend 56 all passed. Source boundary 215 and git diff --check passed; inventory regenerated (232 total / 222 API). .NET also ran its existing SQLite HTTP contracts. No SQL Server or browser checks executed.
- C1 checkpoint: .NET 274/0/7 and HTTP 77/37/56 passed. Cached NuGet restore and cached TypeScript compiler generated Payroll.Api assets; no frontend source edits. The new HTTP suite uses an automatic local port after a fixed-port collision with R4 was corrected. Learning log appended.
- Inherited advances: dated org memberships/ranks/delegations/supervision/routing, personal tasks, request → review/propose → respond → payout/debt/journal (BR §25). See docs/api/advances.md and org.md.
- Inherited R4 integration: local 0.14.0. Reporting staging commit still needs fresh «حمّل»; production switch needs «حوّل الإنتاج». Neither action occurred here.

## Codex security review R5
Scoped review of AdvanceRequests.cs, OrgDirectory.cs, Notifications.RaiseFor/CloseTasks, new AccessPolicy lines and their reachable legacy notification paths; one fix round. No CRITICAL finding confirmed.
- HIGH fixed — FinancialCore.cs:714 and :754, Notifications.cs:26: legacy center/toggle exposed personal tasks to other approvers, creators and administrators. Fix: personal-recipient visibility on both paths. The HTTP regression failed against the original implementation, then passed; Engine and HTTP assert no cross-user disclosure and read ≠ decision.
- HIGH fixed — AdvanceRequests.cs:117 and :157: the original requester retained read/reply access after supervision ended. Fix: require current supervision plus requester identity/permission; routed reviewers/custodians also require their operation permission, summary requires advances.review. Tests deny the former and replacement supervisor; state remains unchanged.
- MEDIUM open — OrgDirectory.cs:71, :81, :87: supervision/routing accept end-before-start, and delegation ending lacks the same interval check. Fix proposed: require a nonempty start and end ≥ start, and validate delegation end against its start. Synthetic HTTP probe confirmed reversed routing dates are accepted. Not fixed per requested severity scope.
- MEDIUM open — AdvanceRequests.cs:60: last settlement is selected globally, including unrelated riders and unapproved legacy rows. It can hide this rider's advances from the reviewer. Fix proposed: select only this rider's approved run/cycle and its approval timestamp. Synthetic probe confirmed another rider's future run becomes the cutoff. Not fixed.
- MEDIUM open — AdvanceRequests.cs:66: Summary indexes scalar closing as closing.net; a routed reviewer with normal financial permissions receives 500. Fix proposed: read the numeric contract explicitly and distinguish an absent statement; review the forecast against already-applied deductions. Synthetic HTTP probe confirmed 500 for a FinancialAccountant reviewer. Not fixed.
- Verified: requester cannot review; payout needs the selected vault's current routing; client workflow/debt/revision fields are ignored or checked; org writes demand org.manage; personal tasks are absent from broadcasts. Independent SQLite Store instances prove one payout on replay and no overdraft across competing approved requests. Store.Write serializes balance/sourceRef/state/journal/audit together; SQL Server application-lock behavior was inspected only, not executed.

## Next step (exact)
Claude: merge feat/mobile-sessions into the 0.14.0 release in the integration worktree when authorized; decide the three MEDIUM items above with Ghazal. No further HIGH/CRITICAL fixes remain from this scoped review.
Antigravity: replace «غير متاح بعد» with the sessions list and reason-required revoke action. Use docs/api/mobile-sessions.md and only the returned prefix as the row identifier.

## How to verify
```bash
# Clean worktrees first need cached NuGet restore and generated frontend assets.
dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1
env -u PAYROLL_TEST_SQL_CONNECTION ConnectionStrings__DefaultConnection= dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1 -v minimal
node tests/advances.mjs
node tests/mobile-sessions.mjs
node tests/r4-backend.mjs
python3 tools/api_inventory.py
python3 scripts/release_source.py --check
git diff --check
```
