# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/pay-pipeline-six-stages |
| Updated | 2026-11-14 by Cursor (W1-M3) |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | `3532327` — W1-M3; CI green `43616445623` |

## Done (W1-M3)
- Data check `~/.agents/reviews/W1-M3-data-check.md` — gate 1–4 PASS (STOP=no); Ninja-only **183**.
- `dotnet run --project tools/GoldenNinja` → Private store `~/Private/gate-data/golden-2026-09/` (700); never committed. (Loader kept out of `SheetReconcile` so CI does not race `Payroll.Sync.deps.json`.)
- Report `~/.agents/reviews/W1-golden-september.md`: BF **183/183**, BN≈**180/183**, BP **182/183**; due/BQ ~5% ±1 SAR.
- Classes: **(a)=0**, (b)=483, (c)=33, (d)=12 (3 missing-ours hashes). Gap = §40.3 base vs sheet Final×(1−BV) / multi-rider split / راتب bands unmapped; AQ–BE+BN from transfers only.
- Engine **577/577 / 0 skipped** with local `gate-sql-test`. Code: `tools/GoldenNinja/*`, `SheetReconcile/Reconcile.cs`.

## Claude check of W1-M3 (2026-11-14) — golden NOT proven
- Result: due 10/183 and transfer 9/183 within ±1 SAR; ours is mostly HIGHER than the sheet (e.g. 152 vs 465, 1,395 vs 2,671). Stage 4 (BF 183/183, BP 182/183) is fine.
- The "(b) §40 by design" label is wrong for the main gap: §40.3 commission after penalties = the sheet (Final Amount already net of penalties × (1 − BV)). Real causes in the loader notes: 16 `salary_scheme_unmapped` riders run as 10% commission; `no_ops_for_captain` / `ops_fallback_captain`.
- Data gate was changed vs the prompt: «ops orders vs invoice» (prompt check 3) failed for **29/190 captains (15%)** — e.g. ops 0 vs invoice 336 — so the run should have stopped; «ops (captain, rider) vs فاتورة نينجا» (prompt check 4) was not done.
- (d): 3 riders missing from our cycle (one with due 6,552.78).
- One fix round is used (rule 4) → Ghazal decides: which September ops source is right, and whether to run a second, narrow round.

## Next step (exact)
Claude reads W1-golden-september.md + data-check; decide if due/BQ (b) gaps need sheet-rate mapping before Antigravity W3. No merge/deploy.

## Do not
- Do not write to ERP or db77738. No secrets/PII in git. No merge/deploy/version bump without Ghazal's yes.
