# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor — M2 API for UI (`feat/api-for-ui`) |
| Branch | `feat/api-for-ui` |
| Updated | 2026-11-15 by Cursor (M2 start) |
| Live | `/health` = **0.22.0** (unchanged; no deploy) |
| Tip | working |

## Goal now
M1 complete on branch. Next: Claude reviews M1 (security + data), then Cursor runs `~/.agents/prompts/cursor-M2-api-for-ui.md`.

## Done (M2 so far)
- R1–R3 (M1 leftovers): Ninja RequiredKinds slimmed; rejected server-side only; dual Iqama/unmatched blockers.

## Done (M1)
- Schema probe + `ErpSchemaContractTests`; snapshot committed.
- A1–A4 period columns, Riyadh UTC, `UnifiedNationalNumber`, `ErpPlatformCode`, `GET /api/system/erp-sponsor-map`.
- A5–A7 keyset source Id, capture account holders, rejected + Iqama blocker.
- A9a–c + ERR: fuel by FuelCardId; EventType 5 = PermissionRenewed; §24.3 same-day; ERP → 503.
- A10/A8 capture kinds + HungerStation `finance_read` views; Keeta note.
- Proof: `~/.agents/reviews/M1-live-capture-proof.md` (ninja_daily source **3751** all companies; 4 keyset pages).
- API inventory + OpenAPI regenerated for erp-sponsor-map.

## Open questions
- `ninja_daily_deduction` nearly empty for BW/TR in Oct — confirm with Ghazal whether zero-kind reason is OK for those companies.
- Vehicle/assignment aggregates under unscoped ERP dump (expected with A6 period+platform scope).

## Do not
- No merge/deploy/version bump without Ghazal's yes. ERP read-only only.
