# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/pay-pipeline-six-stages |
| Updated | 2026-11-14 by Cursor (W1-M3) |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | after W1-M3 push — see Done |

## Done (W1-M3)
- Data check `~/.agents/reviews/W1-M3-data-check.md` — gate 1–4 PASS (STOP=no); Ninja-only **183**.
- `dotnet run --project tools/GoldenNinja` → Private store `~/Private/gate-data/golden-2026-09/` (700); never committed. (Loader kept out of `SheetReconcile` so CI does not race `Payroll.Sync.deps.json`.)
- Report `~/.agents/reviews/W1-golden-september.md`: BF **183/183**, BN≈**180/183**, BP **182/183**; due/BQ ~5% ±1 SAR.
- Classes: **(a)=0**, (b)=483, (c)=33, (d)=12 (3 missing-ours hashes). Gap = §40.3 base vs sheet Final×(1−BV) / multi-rider split / راتب bands unmapped; AQ–BE+BN from transfers only.
- Engine **577/577 / 0 skipped** with local `gate-sql-test`. Code: `tools/GoldenNinja/*`, `SheetReconcile/Reconcile.cs`.

## Next step (exact)
Claude reads W1-golden-september.md + data-check; decide if due/BQ (b) gaps need sheet-rate mapping before Antigravity W3. No merge/deploy.

## Do not
- Do not write to ERP or db77738. No secrets/PII in git. No merge/deploy/version bump without Ghazal's yes.
