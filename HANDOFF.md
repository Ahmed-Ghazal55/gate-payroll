# HANDOFF

| Field | Value |
|---|---|
| Current writer | Antigravity (Gemini 3.8) |
| Branch | feat/r8-ninja-web; merged feat/r8-ninja-api (d7e2feb) |
| Updated | 2026-11-07 by Antigravity (Gemini 3.8) |
| Live | Last handoff: 0.17.0 on SQL db77738; not rechecked this run |

## Goal now
First real Ninja payroll, 2026-10, in the browser against db77738. Completed fix round 1 (WF-0 to WF-4).

## R8 Web Fix Round 1 (WF-0 to WF-4 Completed)
- **WF-0 (Done - commit `217559e`):** Merged `feat/r8-ninja-api` (`d7e2feb`). Ran `node scripts/gen-api-types.mjs --check` with 0 diff.
- **WF-1 (Done - commit `c8ba478`):** Bound all R8 endpoints in `web/src/lib/api.ts` and screens to contract types. Verified intentional typo causes `npm run build` failure, then reverted. Remaining `any` in R8 components is 0.
- **WF-2 (Done - commit `d3350a6`):** `RiderPicker` uses server lookup only (`GET /api/riders/lookup`), displays server chips, handles Arabic error string, and shows «غير موجود — أضف المندوب أولاً». `ManualJournalsPanel` respects `allowedActions` for approve/reject/delete/reverse with a Reject Reason Dialog modal.
- **WF-3 (Done - commit `b2981e9`):** `uploadNinjaInvoice` in `api.ts` parses `data.error` (string) and `data.code` identical to `handleRequest`.
- **WF-4 (Done - commit `4659498`):** Rewrote `web/tests/r8-ninja-flow.mjs` to actively drive the end-to-end flow in the browser:
  1. Starts live dotnet server and seeds only start-of-month baseline records.
  2. Verifies User A (maker) sees no «اعتماد» button on their own invoice (`maker ≠ checker`).
  3. User B (checker) approves invoice in browser (`#btn-approve-ninja-invoice`).
  4. User B checks Review Draft: asserts 0 blockers (`0 Blockers`) and per-account totals equal invoice line total (4,500.00 SAR).
  5. User B clicks «إنشاء دورة الرواتب».
  6. User B advances stages with real buttons until stage 4 lines are generated.
  7. Opens «كيف حُسب؟» Explain Drawer and asserts net entitlement equals API value from `GET /api/pay/cycles/{id}/lines` (4,050.00 SAR).
  8. All 16 screenshots saved across 1440 & 360 in light & dim modes in `test-results/r8-web/`.

## Requests to Codex
1. In `docs/api/openapi.v1.json`, expose `approvedBy`, `rejectedBy`, and `rejectionReason` on `/api/pay/apps/{app}/eligibility` so the UI can type them directly from the contract.
2. In `docs/api/openapi.v1.json`, provide concrete schemas for `history` in `/api/pay/cycles` and `lines` in `/api/pay/cycles/{id}/lines` instead of `Record<string, never>[]`.

## Test Results
- `npm run build`: PASS (TypeScript exit 0, static export clean).
- `node web/tests/ui-qa.mjs`: PASS (all tests a, b, c, d with 0 failures).
- `node web/tests/r8-ninja-flow.mjs`: PASS (WF-4 complete, 16 screenshots generated).
- Remaining `any` count in R8 files: 0.

## Capacity / Usage Screen %
93% remaining before → 87% remaining after (Gemini Models weekly pool).
