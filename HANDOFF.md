# HANDOFF

| Field | Value |
|---|---|
| Current writer | Claude Code (integration) |
| Branch | release/0.18.0 = fix/0.17.1 + feat/r8-ninja-web (incl. feat/r8-ninja-api) + feat/r8-rider-app |
| Updated | 2026-11-08 by Claude Code |
| Live | 0.17.0 on SQL db77738; 0.17.1 (PR #30) waits for «ادمج» |

## Goal now
Run the first real Ninja payroll for 2026-10 in the browser against db77738 (runbook `docs/runbooks/NINJA-2026-10.md`).

## R8 status
- **API (Codex C1–C5, C8 doc; Claude C6/C7):**
  - OpenAPI contract `/openapi/v1.json` with a snapshot in `docs/api/openapi.v1.json`; `web/src/lib/api-contract.ts` is generated from it, and CI fails on diff.
  - Ninja invoice: per-account upload, approve (maker ≠ checker), `allowedActions`.
  - `GET /pay/review-draft` with blockers.
  - Draft discard for pay settings.
  - `GET /api/riders/lookup` (normalised Arabic search).
  - Manual journal reject/delete.
  - **C8 live dry-run NOT run yet.**
- **Web (Gemini 3.8 A1–A7 + WF-0..WF-4):**
  - Extended DataTable, StatCard filters, RiderPicker (server lookup only), Ninja stepper, review draft, cycle stages, «كيف حُسب», rules editor.
  - R8 calls are typed from the contract.
  - `web/tests/r8-ninja-flow.mjs` drives the real flow on a synthetic month:
    - maker ≠ checker approve → 0 blockers → create cycle → stages 1–4;
    - the explain net equals the API value.
- **Mobile (Argon M1–M7, F1–F4, N1–N4):**
  - `mobile/` Capacitor Android app.
  - `PushNotifications.cs`: FCM v1 from env `Firebase__ServiceAccountJson`, `push_outbox`, background flush, backoff.
  - `POST /api/rider-mobile/device-token`.
  - Vitest 10/10.
  - **Ghazal after deploy:** re-save the Firebase settings once (removes the legacy stored key).
  - Then follow `mobile/README.md` steps 1–7.

## Known gaps (after integration)
1. Contract schemas are loose, so the UI still uses casts:
   - `history` in `/api/pay/cycles`;
   - `lines` in `/api/pay/cycles/{id}/lines`;
   - `approvedBy` / `rejectedBy` / `rejectionReason` on eligibility.
2. The pay-settings lifecycle methods in `api.ts` (terms/schemes/eligibility/settings) still return `any`.
3. The flow test checks the draft total against a fixed `4,500.00` instead of the invoice API value, and stops at stage 4 (statements and posting are not driven in the browser).
4. In the explain drawer, long raw `parameters` JSON overflows the card on the left.

## C8 read-only check on db77738 (2026-11-08, SELECT only)
Full counts are in `~/Private/gate-data/sync-logs/r8-ninja-dryrun.json`.
- **Ninja accounts:** 328 (TR 33, BW 132, EX 163). All are linked to a rider; 213 have assignment history.
- **Blockers before a cycle can be created:**
  1. The global pay setting for 2026-10 is still a draft.
  2. Ninja has no terms, scheme or eligibility for 2026-10.
  3. No Ninja invoice for 2026-10 has been uploaded.

## Next
1. Ghazal: «ادمج» PR #30 (0.17.1), then PR #31 (0.18.0), then deploy.
2. Ghazal, in the browser after deploy:
   - approve the global setting as a second user;
   - enter and approve the Ninja terms, scheme and eligibility;
   - upload the October invoices (per account or combined);
   - follow `docs/runbooks/NINJA-2026-10.md`.
3. Ghazal: «حمّل» per write step in the runbook.
4. Ghazal: fill the decision sheet for the 379 debt rows.
5. Passwords: rotate at the final ERP integration (Ghazal's decision 2026-11-07).

## Capacity reported
- Gemini 3.8: 93% → 87% for fix round 1.
- Argon: 100% → ~88% for M1–N4.
