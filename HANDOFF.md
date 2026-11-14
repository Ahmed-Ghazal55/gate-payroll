# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-15 00:16 M1 |
| Branch | fix/live-erp-reads |
| Updated | 2026-11-15 by Cursor (M1 A9+ERR) |
| Live | `/health` = **0.22.0** |
| Tip | branched from `f900e8f` (release 0.22.0); tip includes A1–A9/ERR |

## Goal now
M1 — live ERP reads that really work (`prompts/cursor-M1-live-erp-reads.md`).

## Done
- Step 1 schema probe + `ErpSchemaContractTests` (653cd4a).
- A1–A4: period columns, Riyadh UTC, unified sponsor + erp-sponsor-map, ErpPlatformCode (988e0a1).
- A5–A7: keyset source Id, capture account holders, rejected + Iqama blocker (32f117a).
- A9a fuel by FuelCardId; A9b EventType 5 = PermissionRenewed; A9c §24.3 same-day + day contributions; ERR → 503 (`ErpLiveErrors`).

## Next step (exact)
A10 + A8: extend capture kinds (`ninja_daily_deduction`, `rider_work_timeline` fields, `platform_day_usage`, `vehicle_expense`); HungerStation views; Keeta “no ERP performance source”; then ErpCaptureProbe + proof file.

## Do not
- ERP = read-only login only. No write to ERP or `db77738`. No secrets/PII in git. No merge/deploy/version bump without Ghazal's yes.
