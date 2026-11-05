# Organization: departments, ranks, delegations, supervision, routing (BR §25.1)

All rows are dated and append-only. `POST …/{id}/end` sets `validTo` (or ends a delegation) and never deletes. Admin or `org.manage` only, except `GET /api/org/catalog` (any signed-in user) and `GET /api/org/supervision?mine=1` (a supervisor's own active riders).

| Route | Body | Notes |
|---|---|---|
| GET `/api/org/catalog` | — | `departments` (FIN, HR, FLT, OPS, MGT), `ranks` [{rank:"C",level:1}…{rank:"A+",level:4}], `operations` |
| GET/POST `/api/org/memberships` | `{userId, department, rank, validFrom?, validTo?}` | No overlapping membership in the same department |
| GET/POST `/api/org/delegations` | `{fromUser, toUser, operation, validFrom?, until, reason?}` | `until` is required; expired delegations are ignored |
| GET/POST `/api/org/supervision` | `{supervisorUserId, riderIqama, validFrom?, validTo?}` | `?mine=1` → caller's active riders with `riderName` |
| GET/POST `/api/org/routing` | `{operation, department, minRank, users[], validFrom?, validTo?}` | The newest active row per operation wins |
| POST `/api/org/{memberships|delegations|supervision|routing}/{id}/end` | `{validTo?}` | |
| GET `/api/org/responsible?operation=` | — | Resolved users with `via` = assignment / delegation |

**Operations:** `advance.review`, and `advance.payout:<vaultCode>` (for example `advance.payout:MAIN.BW.DEBT`, `advance.payout:BANK.BW`). The payout routing users are the custodians of that cashbox or bank.

**Responsibility rule:** a user is responsible only if they are named in the active routing **and** hold an active membership in its department with rank ≥ `minRank`, or they hold a valid delegation from such a user. A permission alone never qualifies.
