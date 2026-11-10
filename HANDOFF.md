# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-10 19:20 +03 (R12 Part A — clearing after push) |
| Branch | release/0.20.0 |
| Updated | 2026-11-10 by Cursor (R12 Part A) |
| Live | `/health` still 0.19.0 until merge/deploy |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Codex + Antigravity check Part A (`R12-codex`, `R12-antigravity`). Then Claude review / Ghazal merge decision. Part B only after «حمّل S1» etc.

## R12 Part A (done — local only)
- Runbook: `docs/runbooks/RELEASE-0.20.0.md`
- A1: hosted has records+ERP indexes; missing `sync.SourceWatermarks`, `rpt`, HangFire. Startup preservation proven on local copy.
- Hosted blocker: 1 rider `erpSourceId` identity collision (local quarantined only).
- A3: hosted→local copy 233540 rows; first sync commit ~122s; clean no-change 43s (0/0).
- A4: local API 0.20.0 health OK; **0** ninja invoices for 2026-10 blocks Express path.
- A5: focused Engine 20; SQL subset 6; api:check PASS; live still 0.19.0.

## Not done / PENDING
- Part B hosted «حمّل» slices.
- Full browser S1 with real session + October invoice.
- Resolve hosted rider identity collision before hosted sync.
- MonsterASP Jobs / ERP readonly settings (Ghazal).

## Ghazal's steps
1. Wait Codex/Antigravity/Claude checks of Part A.
2. Merge when ready; confirm live 0.20.0.
3. «حمّل S1» (then S2…) one slice per message.
