# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-09 23:35 +03 |
| Branch | release/0.20.0 |
| Updated | 2026-11-09 by Cursor (R11-FIX1) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Ghazal: wait for CI green on R11-FIX1 push, then Claude Code EGM review of PR #34. Do not merge until that review. «حمّل» for first incremental on hosted db77738 stays Ghazal-only.

## R11-FIX1 done
- Bank parallel mark outside CSV body (filename + preview/audit); BOM/CRLF/header audit shape restored (BR §38.5).
- ENDPOINTS inventory regenerated; missing R11 routes added (validate-gaps, approval-bypass, jahez-reconciliations).
- Version pins in HTTP scripts → 0.20.0 (finance-views / rider-statement / campaigns / enterprise).
- Incremental sync: RowVersion only when column exists; open-period modules + workforce stay readable; empty incremental delta ≠ count drop.
- Local sync timings in `docs/data/SYNC.md`: first ~183 s; clean no-change ~55.8 s (&lt; 1 min).
- Engine.Tests vs main `73f6e4b`: shared SQL HTTP env failures only; no R11-only failures.
- e2e bell locator wrong on main too (`الإشعارات` vs `الإشعارات العامة`); locator aligned with Header/ui-qa.

## Not done / PENDING
- Claude Code EGM formal review of PR #34.
- First incremental «حمّل» on hosted db77738.
- MonsterASP Jobs flags.
- Browser live verify with real session.

## Ghazal's steps
1. Confirm CI green after FIX1 push.
2. Wait for Claude Code review; then merge decision.
3. Say «حمّل» only when ready for hosted incremental write.
4. Set MonsterASP Jobs + ERP readonly connection.
