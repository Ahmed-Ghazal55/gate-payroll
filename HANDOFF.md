# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-10 12:00 +03 (R11-FIX2 in progress — clearing after push/CI) |
| Branch | release/0.20.0 |
| Updated | 2026-11-10 by Cursor (R11-FIX2) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
After CI green on this push: Claude Code EGM review of PR #34. Do not merge. «حمّل» for hosted db77738 stays Ghazal-only.

## R11-FIX2 (this round)
- **Failed:** CI run 42698743152 Browser at `web/tests/r8-ninja-flow.mjs` stage 5 advance → 400.
- **400 body (branch, before fix):** `{"error":"قيمة رقمية مطلوبة: housingMonthly","code":"invalid_request"}`
- **Main `73f6e4b`:** stage 5/6 advance OK (`✓ Active cycle id verified`); later local CSP `waitForFunction` noise only.
- **Cause:** `PayEvaluator.HousingCharge` (`Payroll.Api/PayEvaluator.cs:55`) called `PayValues.Number(...,"housingMonthly")` before the zero-days early return. Stage 5 `BuildDeductions` → `HousingForRider` (`PayCycles.Calculation.cs:86`) calls it for every rider; r8 seed has no residences and omits `housingMonthly`. Not fingerprint, not rent-cap `CapSharedVehicleRent`/`VehicleIdOf`, not approval bypass.
- **Fix:** return 0 when `days==0` before requiring `housingMonthly` (BR §38.4: no residence days → no housing charge).
- **Engine test:** `NegativeCaptainReachesStage5WhenPaySettingOmitsHousingMonthly`
- **Local:** Engine with SQL `11433` → 498/498, 0 skipped; `gen-api-types --check` PASS; `r8-contract` PASS; r8 stage 5/6 PASS after fix. Local CSP/`Vouchers__Secret` failures on some browser scripts are environment noise (same late CSP on main r8); not the stage-5 path.
- **CI:** (filled after push)

## Not done / PENDING
- Claude Code EGM formal review of PR #34.
- First incremental «حمّل» on hosted db77738.
- MonsterASP Jobs flags.
- ERP read-only connection.
- Browser live verify with a real session.

## Ghazal's steps
1. Wait for CI green on FIX2 push.
2. Wait for Claude Code review; then merge decision.
3. Say «حمّل» only when ready for hosted incremental write.
4. Set MonsterASP Jobs + ERP readonly connection.
