# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-10 14:14 +03 (R11-FIX3 — clearing after CI) |
| Branch | release/0.20.0 |
| Updated | 2026-11-10 by Cursor (R11-FIX3) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
After CI green: Claude Code EGM formal review of PR #34. Do not merge. «حمّل» for hosted db77738 stays Ghazal-only.

## R11-FIX3 (this round — merge blockers from security + Claude R-1)
- **F-01:** Jahez `Build` uses `DemandWrite`; `Allowed` POST no longer accepts `treasury.view`. GET stays view. Test: `ViewOnlyPrincipalCannotBuildJahezReconciliation`.
- **F-08:** One active recon per company+period in `Build`/`Approve`. Tests: `SecondReportForSameCompanyPeriodIsRefusedAndApproveCannotDoublePost`.
- **R-1:** Approver Independent of `createdBy` and `submittedBy`. Test: `ApproverMustDifferFromCreatorAndSubmitterUnlessBypassGrant`.
- **F-04:** New approvals use §35 bypass grants only; historical `approval.self_exception` audits still read; configure refuses new dated exceptions. View/Approve/WithActions pass DB so grants apply. Tests updated in `ApprovalExceptionTests` / `ApprovalBypassTests`.
- **Local:** focused 11/11; Engine+SQL 501 (2 Jobs flaked on local inotify, pass on retry with polling watcher); api:check PASS; r8-contract 26 PASS; r8 stage 5/6 PASS (`Active cycle verified`). Local CSP/`Vouchers__Secret` noise on some browser scripts (same as FIX2).
- **CI:** (filled after push)

## Not done / PENDING
- Claude Code EGM formal review of PR #34.
- First incremental «حمّل» on hosted db77738.
- MonsterASP Jobs flags.
- ERP read-only connection.
- Browser live verify with a real session.

## Ghazal's steps
1. Wait for CI green on FIX3 push.
2. Wait for Claude Code review; then merge decision.
3. Say «حمّل» only when ready for hosted incremental write.
4. Set MonsterASP Jobs + ERP readonly connection.
