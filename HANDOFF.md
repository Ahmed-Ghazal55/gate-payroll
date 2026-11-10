# HANDOFF

| Field | Value |
|---|---|
| Current writer | (none) |
| Branch | release/0.20.0 |
| Updated | 2026-11-10 by Cursor (R11-FIX2 done, CI green) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Claude Code: EGM formal review of PR #34. Do not merge. «حمّل» for hosted db77738 stays Ghazal-only.

## R11-FIX2 done
- **Failed:** CI run 42698743152 Browser at `web/tests/r8-ninja-flow.mjs` stage 5 advance → 400.
- **400 body (branch, before fix):** `{"error":"قيمة رقمية مطلوبة: housingMonthly","code":"invalid_request"}`
- **Main `73f6e4b`:** stage 5/6 advance OK (`✓ Active cycle id verified`).
- **Cause:** `PayEvaluator.HousingCharge` (`Payroll.Api/PayEvaluator.cs:55`) required `housingMonthly` before the zero-days return. Called from stage 5 `BuildDeductions` → `HousingForRider` (`PayCycles.Calculation.cs:86`) for every rider; r8 seed has no residences and omits `housingMonthly`. Ruled out: fingerprint («تغير المصدر»), `CapSharedVehicleRent`/`VehicleIdOf`, approval bypass.
- **Fix:** `HousingCharge` returns 0 when `days==0` before reading `housingMonthly` (BR §38.4).
- **Engine test:** `NegativeCaptainReachesStage5WhenPaySettingOmitsHousingMonthly`
- **Assert:** r8 advance failure message now includes response body.
- **Local:** Engine + SQL `11433` → 498/498, 0 skipped; `gen-api-types --check` PASS; `r8-contract` 26 PASS; r8 stage 5/6 PASS after fix.
- **Push:** `52cb9c7`
- **CI:** https://github.com/Ahmed-Ghazal55/gate-payroll/actions/runs/42801562070 — **success** (Browser green, including r8-ninja-flow).

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
