# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-15 00:16 M1 |
| Branch | fix/live-erp-reads |
| Updated | 2026-11-15 by Cursor (M1 A1–A4) |
| Live | `/health` = **0.22.0** |
| Tip | branched from `f900e8f` (release 0.22.0) |

## Goal now
M1 — live ERP reads that really work (`prompts/cursor-M1-live-erp-reads.md`).

## Done
- Step 1: `tools/ErpSchemaProbe`, `docs/data/erp-schema.snapshot.json`, `ErpSchemaContractTests` (653cd4a).
- A1–A4: per-kind period columns (`LiveOperationsQuery` KindSpec); `StartedAtUtc`/`EndedAtUtc` + Riyadh day; `SponsorUnifiedNumbers` + `UnifiedNationalNumber` + 503 when unmatched; `ErpPlatformCode` map; `GET /api/system/erp-sponsor-map`; fuel parent load by `FuelCardId` (A9a early). Tests: `ErpSchemaContractTests`, `ErpPlatformCodeTests`, `LiveOperationsQueryTests` (20 pass).

## Next step (exact)
A5–A7: keyset cursor = source row `Id` of last READ; capture scope via `RiderClientAssignments` + sponsored riders; surface malformed Iqama rejections (`rejected` counts + cycle blocker).

## Do not
- ERP = read-only login only. No write to ERP or `db77738`. No secrets/PII in git. No merge/deploy/version bump without Ghazal's yes.
