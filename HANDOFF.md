# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | fix/0.18.1 (PR, waiting for «ادمج») |
| Updated | 2026-11-08 by Claude Code |
| Live | 0.18.0 on SQL db77738 |

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

## 0.18.1: Ninja upload could never reach approval (found from Ghazal's report)
The flow test seeded the invoice and classified deductions directly, so it never hit these:
1. The upload required `ninja_account` rows; db77738 has 0, so every preview failed. Now the captain also matches `platform_account.externalAccountId` (same source as the pull). Unknown or deleted captains and other-company rows are rejected with row numbers.
2. No screen could classify deductions, and approval needs them all classified. Added a category panel and `POST /platforms/ninja/invoices/{id}/classify-batch`.
3. No explanation when approve is hidden. The API now returns `approvalBlockers`; the screen shows them.
4. Per-account picker was always empty: wrong kind (`accounts`) and wrong code case (`Ninja`), plus the 200 page cap.
5. Several selected files: only the first would be saved, silently. Now reconcile only, and it asks for one file at a time. The default reference includes the file name, so a company can hold several invoices a month.
6. `web/tests/r8-ninja-flow.mjs` now uploads a real synthetic xlsx with live-shaped accounts: maker uploads, confirms, classifies; checker approves.

7. Real October files (3 companies) then exposed more: captains with a negative final (9 rows) were rejected, and `-` cells in DEDUCTIONS rows failed the number check. BR §31 says the invoice is final, so they are kept; a negative rider stops at «يحتاج قرار» in the cycle.
8. UI: next-step card with the action button, invoice chips per company, company/month switch resets the screen, reference field removed (generated from the file name), Odoo optional. New `SearchableSelect` (manual journal accounts, cost accounts, sponsors, vaults); riders in payroll dialogs use `RiderPicker`.

**Checked against the 3 real October files** through a local temporary API (counts only): all preview and save. TR 27 lines, BW 76, EX 106 (104 + 2 company). Saved totals differ from the file by about 0.03-0.05 from per-line rounding.

## Next blockers on real data (after 0.18.1)
- `ninja_daily` = 0 on db77738, so the review draft will show `operations_missing` for every account. Needs either the ERP pull (dry-run, then «حمّل») or the template import in «استيراد البيانات» ← «تشغيل نينجا».
- 115 Ninja accounts have no assignments, which gives `unassigned_orders` if they operated in October.

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
1. Ghazal: «ادمج» 0.18.1.
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
