# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-15 00:16 M1 |
| Branch | fix/live-erp-reads |
| Updated | 2026-11-15 by Cursor (M1 start) |
| Live | `/health` = **0.22.0** |
| Tip | branched from `f900e8f` (release 0.22.0) |

## Goal now
M1 — live ERP reads that really work (`prompts/cursor-M1-live-erp-reads.md`). Schema probe first, then A1–A10/ERR with failing tests.

## Next step (exact)
Step 1: add `tools/ErpSchemaProbe`, write `docs/data/erp-schema.snapshot.json`, add `ErpSchemaContractTests` (must fail on main defects).

## Do not
- ERP = read-only login only. No write to ERP or `db77738`. No secrets/PII in git. No merge/deploy/version bump without Ghazal's yes.
