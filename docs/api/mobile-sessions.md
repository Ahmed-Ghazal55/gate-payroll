# Rider mobile sessions (R5-C1)

Administrative routes require `mobile.sessions` (included for Admin/SuperAdmin). Other roles receive it only through an explicit custom permission. The live permission catalog is loaded on each authentication; existing user records are preserved. Results and revocations use the rider's current finance sponsor/company scope. An out-of-scope single session or rider returns 404. Sessions are retained indefinitely; revoke never deletes a row.

| Method | Route | Authentication / body |
|---|---|---|
| GET | `/api/rider-mobile/sessions?iqama=&status=active\|expired\|revoked` | Staff cookie + `mobile.sessions`; optional `page`, `pageSize`, `q`, `sort` |
| GET | `/api/rider-mobile/sessions/mine` | Active rider bearer; own sessions, including expired/revoked history |
| POST | `/api/rider-mobile/sessions/{id}/revoke` | Staff permission + scope; `{reason}` |
| POST | `/api/rider-mobile/sessions/revoke-all?iqama=…` | Staff permission + scope; `{reason}`; returns `{revoked}` |
| POST | `/api/rider-mobile/logout` | Active rider bearer; ends only the current session; returns `{ok:true}` |

The list is an array, or `{items,total,page,pageSize}` when `page` is present. Each row contains only `id`, `iqama`, `riderName`, `language`, `createdAt`, `expiresAtUtc`, `lastSeenAt`, `status`, `deviceFingerprint`, `fcmRegistered`.

`id` is the first **16 hex characters** of the stored session hash, without `mobile:`. Pass it unchanged to revoke. A collision is rejected with 409; the full credential hash is never accepted or exposed. `deviceFingerprint` is the first **8 lowercase hex characters** of SHA-256 of the stored FCM token, or an empty string when absent. `fcmRegistered` means an FCM token is stored; it does not prove push delivery or Firebase registration. No bearer token, raw FCM token, or complete session hash is returned, including in audit records.

Status precedence: `revoked` when `revokedAt` exists, otherwise `expired` when expiry is missing/invalid/past, otherwise `active`. Rider authentication also requires an active rider record. Revocation records `revokedAt`, `revokedBy`, and `revokeReason`, with a transactional audit event. Reasons must contain 1–500 non-whitespace characters after trimming. Repeated administrative revocation is idempotent. Revoke-all includes expired sessions not previously revoked.

Every rider bearer route checks the same session guard: own sessions, portal, statement, broadcast list and acknowledgement, logout. Invalid, revoked or expired credentials return **401**, even if a staff cookie accompanies the bearer. Broadcast listing preserves staff cookie access with `mobile.broadcast` and staff scope; rider bearer access returns only `ALL` and own targeted broadcasts.

Login initializes `lastSeenAt`; legacy sessions without it are touched on first use. Reads skip the write transaction until five minutes have passed. When due, authentication is repeated inside the normal serialized Store write before changing `lastSeenAt`; this prevents a concurrent revocation from being overwritten. Touches do not invalidate financial summaries. Failed authentication never touches the row.

Frontend follow-up: replace the unavailable sessions placeholder with the scoped list and reason-required revoke action; use only the returned prefix as the row identifier.

Synthetic verification: `dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true` and `node tests/mobile-sessions.mjs`.
