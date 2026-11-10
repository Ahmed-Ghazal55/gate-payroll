# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) |
| Branch | release/0.20.0 |
| Updated | 2026-11-10 by Cursor (R11-FIX3 done) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Claude Code EGM formal review of PR #34. Do not merge. «حمّل» for hosted db77738 stays Ghazal-only.

## R11-FIX3 (done — merge blockers from security + Claude R-1)
- Commit: `fbe0933` — `fix(access): R11-FIX3 Jahez write gate, unique recon, §35 grants`
- **CI green:** https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/42816744969 (`validate` pass 11m5s)
- **F-01:** `JahezReconciliation.DemandWrite` + `AccessPolicy` POST Allowed write-only; GET stays `treasury.view`. Test: `ViewOnlyPrincipalCannotBuildJahezReconciliation`.
- **F-08:** `RefuseDuplicateActive` in Build + Approve. Test: `SecondReportForSameCompanyPeriodIsRefusedAndApproveCannotDoublePost`.
- **R-1:** Approve Independent of `createdBy` and `submittedBy`. Test: `ApproverMustDifferFromCreatorAndSubmitterUnlessBypassGrant`.
- **F-04:** New approvals via §35 bypass grants only; Configure clears/refuses new dated exceptions; View/Approve/WithActions pass DB. Tests: `ApprovalExceptionTests` / `ApprovalBypassTests`.
- **Local:** focused 11/11; Engine+SQL 501 (2 Jobs inotify flakes, retry OK); api:check; r8-contract 26; r8 stage 5/6 Active verified. Local CSP/`Vouchers__Secret` noise only (same as FIX2).

## Not done / PENDING
- Claude Code EGM formal review of PR #34.
- First incremental «حمّل» on hosted db77738.
- MonsterASP Jobs flags.
- ERP read-only connection.
- Browser live verify with a real session.

## Ghazal's steps
1. Wait for Claude Code review; then merge decision.
2. Say «حمّل» only when ready for hosted incremental write.
3. Set MonsterASP Jobs + ERP readonly connection.
