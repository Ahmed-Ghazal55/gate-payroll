# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/pay-pipeline-six-stages |
| Updated | 2026-11-14 by Cursor (W1-M3b) |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | `138256f` — W1-M3b; CI green `43620810112` |

## Done (W1-M3b)
- Gate restored: orders column «تسليم عدد طلبات» ties 190/191 (0 mismatches). Check 4 fails 3/191 (1.57%). STOP = no.
- Due **147/178 (82.6%)**, transfer **138/178 (77.5%)**. (a)=0. Not 95%, and (d) remains — not relabelled.
- Causes: (c) 19 sheet CJ ≠ BU×0.9; (b) 6 §40.2 order weight; (d) 3 with no ops share. 6 salary riders excluded (negative allowance).
- Fuel once on the main company. Engine tests **580/580 / 0 skipped** with local `gate-sql-test`.
- Reports: `~/.agents/reviews/W1-golden-september.md`, `W1-M3-data-check.md`.

## Next step (exact)
Claude reads both reports. Due is below 95% and three riders stay (d). No merge and no deploy. Antigravity W3 waits for Claude.

## Do not
- Do not write to ERP or db77738. No secrets or PII in git. No merge, deploy, or version bump without Ghazal's yes.
