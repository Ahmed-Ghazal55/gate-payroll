# Rider advance asks (Track M R6, BR §25.8)

A rider asks from the app. The ask goes only to the rider's **current** supervisor (`org_supervision`). The supervisor converts it into a formal advance request (the §25 cycle, unchanged) or declines it with a reason the rider sees.

| Method | Route | Who |
|---|---|---|
| GET | `/api/rider-mobile/requests` | Active rider bearer: own asks only |
| POST | `/api/rider-mobile/requests` | Rider bearer: `{type:cash\|bank, amount, reason}` |
| POST | `/api/rider-mobile/requests/{id}/cancel` | Rider bearer: own **open** ask only |
| GET | `/api/advance-requests/rider-asks?status=` | `advances.request` + current supervision |
| POST | `/api/advance-requests/rider-asks/{id}/convert` | Supervisor: optional `{type, amount, reason}` → `AdvanceRequests.Create` |
| POST | `/api/advance-requests/rider-asks/{id}/decline` | Supervisor: `{note}` (3–500 characters, shown to the rider) |

## Rules
- **Statuses:** `open` → `converted` \| `declined` \| `cancelled`.
- **Limits:** one open ask per rider, and no new ask while a formal advance request is not final. Both return 409. No current supervisor also returns 409.
- **What the rider sees:**
  - type, amount, reason, status, and the decline note;
  - after conversion: `advanceStatus` and the final amount and type, taken from the formal request.
  - The rider never sees decisions, reviewer notes or vault data.
- **Supervisor task:**
  - Each ask raises a personal task for its supervisors (`taskKind=rider_ask`).
  - Convert, decline or cancel closes that task: `actionStatus=decided`, and the notification is kept.
- **Not yours = not found:** an ask for a rider you do not supervise returns 404.
- **Audit:** `rider_ask.created`, `.converted`, `.declined`, `.cancelled`.

Verification: `node tests/rider-asks.mjs`.
