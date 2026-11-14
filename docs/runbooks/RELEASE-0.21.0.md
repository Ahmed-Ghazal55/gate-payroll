# Release 0.21.0 — live ERP reads + frozen run inputs

Branch `release/0.21.0` (from `feat/run-input-snapshots`). Binding rule: BR §39 (`gate-finance-spec/BUSINESS_RULES.md`, approved 2026-11-14).
Hard limits: no merge / deploy without Ghazal's «ادمج». Merge to `main` = production deploy. Never write ERP `db67927`. No secret values in this file.

## What is in it
| Part | Owner | What |
|---|---|---|
| PR #35 (already on main) | Cursor | S1 append-only accept + identity consistent read |
| C0 | Claude | Source switch `erp_live`/`store`; Production is always live; health shows the source |
| L1 + S1 | Cursor + Claude | Paged live reads, scope inside SQL, cross-company leak closed, OPENJSON for large scopes, Arabic search |
| U1 | Antigravity | Operations screens read live, source badge, 503 card |
| L2 (+ fix, fix2) | Cursor | `pay_run_input` table; `capture-inputs`; A0 seal (capture only while stage ≤ 1); required kinds; keyset paging + recount; pre-cycle draft/validate-gaps read live |
| U2 | Antigravity | Cycle «التقاط المدخلات» button from `allowedActions`, inputs card (time, user, rows per kind), contract types |
| Release | Claude | Version 0.21.0, cycle capture fields and typed account assignments in the contract, this runbook |

PR #36 (draft) is fully inside this release. Close it when this release merges.

## 0. Before merge (checked 2026-11-14)
| Check | Expected | Result |
|---|---|---|
| CI `validate` on the release PR | green | see PR |
| `ReleaseSafety.Version` and deploy health pin (`scripts/monsterasp_ftps.py`) | `0.21.0` | ✅ |
| Engine tests with local SQL (`gate-sql-test`) | 0 skipped | ✅ 538/538 on `3c4eaeb` (Cursor); CI runs them on every push |
| `npm --prefix web run api:check`, `node tests/r8-contract.mjs` | pass | ✅ 29 passed |
| Live payroll DB: pay cycles in flight | none past stage 1 | ✅ **0 pay cycles** on hosted (read-only count, 2026-11-14) |
| `ReleaseSafety` counts the new table | `pay_run_input` in counts/hash | ✅ `ReleaseSafety.cs:24-28` |
| Start-up guard | captured cycle without rows = start-up error | ✅ `Store.cs:52-55` |

## 1. Hosting setting (Ghazal, MonsterASP panel; names only)
- `ConnectionStrings__ErpReadonly` = the **read-only** ERP login for `db67927`. Required: Production always reads live.
  Without it, `/health` shows `erpReadonlyConfigured=false`, and the operations screens and the capture button return 503 «مصدر التشغيل غير متاح» (no silent fallback, BR §39.1).
- Keep `Jobs__ErpSync__Enabled=false` (no mirror sync, BR §39.1).
- No other setting changes.

## 2. Merge and deploy (Ghazal)
1. Set the setting in step 1.
2. Write «ادمج». Claude squash-merges the release PR to `main`; CI deploys over FTPS.
3. CI checks live `/health` = `0.21.0` with `dataPreservationVerified=true` and `releaseCheckpointPresent=true`.

## 3. After deploy (read only)
| Check | Expected |
|---|---|
| `/health` | `version=0.21.0`, `operationsSource=erp_live`, `erpReadonlyConfigured=true` |
| Operations tabs (vehicles, housing, accounts, daily) | badge «مباشر من نظام الموارد — آخر قراءة …», page 1 loads, paging and search work |
| Rider 360 → accounts panel | live rows, same badge |
| `pay_run_input` table | exists and is empty (created on start-up) |

If an operations tab shows 503: check the setting name and the read-only login first. Do not turn on the mirror.

## 4. Rollback
- Redeploy the last green 0.20.0 artifact (release checkpoint under `App_Data/release-backups`). Nothing is deleted.
- `pay_run_input` is additive. 0.20.0 ignores it. Leave it in place.

## 5. Next: Ninja 2026-10 parallel run
Follow `docs/runbooks/NINJA-2026-10.md` (BR §38.5): create the cycle → **capture inputs** → check rows per kind → review → second-user approval → bank file marked «لا يُرسل».
