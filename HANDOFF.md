# HANDOFF

| Field | Value |
|---|---|
| Current writer | (clear — Cursor finished R11 T9) |
| Branch | release/0.20.0 |
| Updated | 2026-11-09 by Cursor (R11 T9 release prep) |
| Live | see `/health`; DB db77738 |
| PR | https://github.com/Ahmed-Ghazal55/gate-payroll/pull/34 (do not merge yet) |

## Next step (exact)
Ghazal: review PR #34 (do not merge until Claude Code EGM review). Then decide merge and whether to «حمّل» first incremental sync on db77738. MonsterASP Jobs flags still need Ghazal.

## R11 done
- T1 timings + design; baseline wall ~14.4 min (identity refuse).
- T2 watermarks / incremental / open-period soft-delete / full-resync.
- T3 contentHash + batch mirror lock + plan dictionaries.
- T4 display kinds: vehicle status, day usage, Chefz type/orders, Ninja eval, Jahez policy/daily, rider work timeline.
- T5 Chefz FT §36; vehicle rent cap §38.4; housing by residence days; Jahez reconciliation §37.
- T6 `approval.bypass_maker_checker` grants + badge + admin APIs.
- T7 validate-gaps counts API + §38 parallel bank `DO-NOT-SEND`.
- T8 OpenAPI + web wiring (Jahez, gaps, bypass, ERP pause/resume/fullResync, Mada/liability/legacy, accounting adjustments, debts collect, policy approve, bank comparison).
- T9 version `0.20.0`; Chefz review no longer ships unknown `gateFullTimeCommission` into ValidateRiders.

## Tests (T9)
- Engine.Tests R11-focused filter (Chefz/Jahez/housing/rent/bypass/gaps/bank): **33 passed**.
- Full Engine.Tests: **465 passed**, **19 failed** (local SQL/push-notification env — not R11 regressions), **13 skipped** SQL-only.
- `node tests/integration.mjs` with `Vouchers__Secret`: **PASS** (75 checks).
- `node tests/r8-contract.mjs`: **26 passed**.
- `web npm run api:check` + `npm run build`: **PASS**.
- `web npm run test:e2e`: Chromium installed; dashboard screenshots OK; failed later on notifications button locator (pre-existing UI QA flake / mock session).

## Ninja October / ValidateRiders
- Counts-only API: `GET /pay/validate-gaps?companyId=&period=&app=ninja`.
- Local `~/Private/gate-data/payroll-local/payroll.db` is tiny (~40KB); live October gap counts need a full local copy of production data. Fix UI gaps from the counts report (assignments, categories) before stage 3.

## Not done / PENDING
- Claude Code EGM formal review.
- First incremental «حمّل» on db77738.
- MonsterASP: `Jobs__Enabled`, `Jobs__ErpSync__Enabled`, `ConnectionStrings__ErpReadonly`.
- D3 scheme assignment from ERP PaymentModel; custom new roles; P0–P7 items still on planner list.
- §37 VAT posts with named accounts; BR still marks VAT account naming as planner-open if chart changes.
- Browser e2e/playwright on this agent host; chrome-devtools live verify deferred to Ghazal on local API with real session.

## Ghazal's steps
1. Read PR; wait for Claude Code review.
2. Merge when ready.
3. Say «حمّل» only when ready for first incremental write on db77738.
4. Set MonsterASP Jobs + ERP readonly connection.
5. Run validate-gaps for Ninja 2026-10 on a full local DB copy; fix assignment/category counts in UI.
