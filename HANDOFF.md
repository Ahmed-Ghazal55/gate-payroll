# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-14 W1 pay-pipeline-six-stages |
| Branch | feat/pay-pipeline-six-stages (creating from origin/main) |
| Updated | 2026-11-14 by Cursor |
| Live | `/health` = **0.21.0** (this branch not deployed) |
| Tip | W1 in progress — step 0 design note |

## In flight
W1 six-stage pay pipeline per `prompts/cursor-W1-pay-pipeline-six-stages.md` and BR §40. Open decisions as settings with ✅ defaults. No ERP/prod writes. Push branch only.

## Do not
- Do not write to ERP or db77738. No secrets in git. No merge/deploy/version bump without Ghazal's yes.
