# Release 0.20.0 — merge, deploy check, staged ERP «حمّل»

Owner path for R12. Branch `release/0.20.0`, PR #34. HungerStation stays deferred (BR §38.3).  
Hard limits: no merge / deploy / hosted write until Ghazal says so. Never write ERP `db67927`. No secret values in this file.

Evidence base (Part A, 2026-11-10): hosted schema read-only; local SQL `db77738` on `127.0.0.1,11433` restored from hosted then sync-rehearsed; live ERP schema read-only; local API `/health` on `0.20.0` with `dataPreservationVerified=true`.

---

## 0. Before merge

| Check | Expected |
|---|---|
| PR #34 CI `validate` | green on tip |
| `ReleaseSafety.Version` | `0.20.0` |
| `docs/api/ENDPOINTS.md` title | 0.20.0 |
| Live `/health` today | still `0.19.0` until deploy after merge |
| Engine focused (ReleaseSafety / watermarks / Jahez / approval) | 20/20 |
| Engine SQL subset on local 11433 | 6/6 |
| `npm --prefix web run api:check` | PASS |
| Rollback checkpoint | previous live release `0.19.0` + private `release-backups` under App_Data |

MonsterASP setting **names only** (Ghazal sets values): `Jobs__Enabled`, `Jobs__ErpSync__Enabled`, `Jobs__ErpSync__DailyEnabled`, `Jobs__ErpSync__CatchUpEnabled`, `ConnectionStrings__DefaultConnection`, `ConnectionStrings__Jobs`, `ConnectionStrings__ErpReadonly` (or `ERP_READONLY_DB`), `Vouchers__Secret`.

---

## 1. Merge steps (Ghazal)

1. Codex review (`prompts/R12-codex`) + Antigravity check (`prompts/R12-antigravity`) of Part A evidence.
2. Claude EGM / formal PR review if still open.
3. Squash-merge PR #34 to `main` only after explicit «ادمج» (merge deploys).
4. Confirm live `/health` → `version=0.20.0`, `dataPreservationVerified=true`, `releaseCheckpointPresent=true`.
5. Keep `Jobs__ErpSync__Enabled=false` until first «حمّل» slices finish.

### Rollback

- IIS/App_Data previous release checkpoint under `release-backups/0.19.0` (private host).
- Redeploy last green `0.19.0` artifact; do not delete `dbo.records`.
- ERP mirror rows are additive; corrections use new sync / reversing entries, never DELETE.

---

## 2. Database readiness (A1 summary)

| Object | Hosted db77738 | Created how | Notes |
|---|---|---|---|
| `dbo.records` | exists | prior cutover | 233 540 rows at Part A copy |
| ERP JSON computed cols + `ix_erp_*` | exists | Store / `ErpDataIndexes.Ensure` at startup | non-destructive |
| `sync.SourceWatermarks` | **missing** until first successful commit | `SourceWatermarks.Ensure` on sync/commit | startup of API alone does not require it |
| `rpt.*` | missing | first reporting rebuild | optional for payroll path |
| `HangFire.*` | missing | when `Jobs__Enabled=true` | keep off until ready |
| Startup preservation | proven on local copy | `ReleaseSafety` / `dataPreservationVerified` | local open: unchanged capture |

**Hosted blocker before incremental sync:** read-only R12-fix1 inspection found one `rider` source collision. The stored rider document exists, while the ERP-planned identity document does not. The stored profile differs from ERP in 2 fields (`iqama`, `nationality`); the old identity belongs to 0 ERP employees and the current identity belongs to 1. There are 173 top-level references: 171 active references must follow the corrected identity, while 2 audit references remain immutable. ERP is authoritative, so quarantining only the stored rider is insufficient because it detaches historical Jahez and assignment records.

The guarded one-time tool derives both identities in memory from the approved read-only ERP and payroll connections; it never prints either identity. It re-keys the rider and active identity references atomically, preserves audit history, refuses any count/state drift, and requires the exact dry-run fingerprint. **It has not been run against hosted in either mode.** Run the first command only after reviewing this block. Run the second command only after Ghazal writes «حمّل S1» and the dry-run reports exactly one collision:

```bash
dotnet run --project tools/RiderIdentityRepair/RiderIdentityRepair.csproj -- --dry-run
dotnet run --project tools/RiderIdentityRepair/RiderIdentityRepair.csproj -- --commit --preview-fingerprint <SHA256_FROM_SAME_DRY_RUN>
```

After the repair, repeat the normal S1 ERP dry-run. It must show no `source_identity_changed_requires_review` before any S1 commit.

---

## 3. Slice map (A2 — live ERP is source of truth)

| Slice | ERP tables (live) | Target kinds | Screen / tab | Payroll use |
|---|---|---|---|---|
| S1 | `app.Employees`, RiderProfiles, PlatformRiderAccounts, RiderClientAssignments, `ninja.*`, Vehicles/assignments, Housing*, Fuel*, VehicleExpenses, custody events | `employee`, `rider`, `platform_account`, `rider_account_assignment`, `ninja_*`, `vehicle*`, `housing*`, `fuel_*`, `custody_*`, … | Operations (vehicles/housing/custody), Payroll Ninja tabs, Import center | October cycle validate/create/stages, housing stage 5, fuel debts |
| S2 | `jahez_v2.*`, `jahez_v3.Treasury*` | `jahez_*`, `erp_jahez_*`, `erp_money_movement`, `erp_treasury_*`, `erp_cashbox_handover` | Treasury → Jahez | Jahez reconciliation §37, treasury postings |
| S3 | `chefz.AccountDetails`, DailyPerformance, WalletSettlements, AccountTypeChanges, ImportedOrderRows | `chefz_*` | Payroll / operations Chefz panels | Chefz settlement review (not first Ninja path) |
| S4 | VehicleOperationalStatusPeriods, PlatformAccountDayUsages, EvaluationSnapshots, derived timeline | `vehicle_operational_status`, `platform_day_usage`, `ninja_evaluation_snapshot`, `rider_work_timeline` | Validate-gaps / ops display | Display + gap counts; HungerStation excluded |

Money-critical unmapped / empty on live ERP (flag): `jahez_v2.EarningsStatements=0`, `CommissionPolicies=0`, `ninja.EvaluationSnapshots=0`. Live-only HungerStation tables are deferred. Live has 15 tables not on the Nov-3 local ERP copy (mostly `hunger.*` + a few `app.*` business revisions).

---

## 4. Local rehearsal results (A3)

Local target: private `--target-env` → catalog `db77738` on port 11433 (not MonsterASP).  
Copied hosted → local (233 540 rows, ~22 min). Then live ERP sync after quarantining 1 rider collision.

| Run | Wall | read/plan/write ms | Result |
|---|---:|---:|---|
| Live dry-run ×3 (before quarantine) | ~50–69 s | — | refused `source_identity_changed_requires_review` |
| Live dry-run (after quarantine + accepts) | 62 s | 48151 / 1426 / 9463 | completed |
| First commit (accept-new ×8 + accept-large × many) | 122 s | 41901 / 1474 / 74214 | completed |
| No-change commit #1 | 45 s | 19100 / 1338 / 18721 | 0 new / 27 changed |
| No-change commit #2 | **43 s** | 18343 / 1280 / 17559 | **0 new / 0 changed** (under 1 min) |

### Slice totals on first commit (from sync log)

| Slice | new | changed | unchanged | rejected |
|---|---:|---:|---:|---:|
| S1 | 1449 | 565 | 35983 | 404 |
| S2 | 17337 | 1560 | 68387 | 0 |
| S3 | 9856 | 0 | 6708 | 0 |
| S4 | 6099 | 0 | 0 | 34 |

Screens: local API `/health` shows `0.20.0` + preservation flags. Full UI verify of each tab after sync is still pending a real browser session (A4).

---

## 5. S1 vertical slice (A4) — blockers

Local inventory after copy+sync:

| Prerequisite | Count / note |
|---|---|
| users | 5 |
| `pay_setting` period 2026-10 | 1 |
| ninja `pay_terms` 2026-10 | 1 |
| ninja `platform_invoice` 2026-10 | **0** — blocks discard/re-upload Express path |
| ninja_account / ninja_daily | 365 / 4457 |

Recorded blockers (do not invent settings):

1. **No October Ninja invoice** on hosted/local copy → upload/discard/re-upload cannot be walked until a synthetic or approved invoice file is provided.
2. **Hosted rider identity collision** must be resolved before hosted «حمّل» (see §2).
3. **§35 bypass grant** for `ghazal`: use Admin → approval bypass (not dated self-exception). Synthetic reason only in tests, e.g. `R12 local rehearsal bypass`.
4. Bank file: enable **comparisonOnly** / `DO-NOT-SEND` (§38.5) on Treasury → bank file; never send.
5. Stages 1–8 / review-draft: require auth session + invoice + assignments; unauthenticated local call returned 403 as expected.

---

## 6. «حمّل» blocks (Part B — one slice per message)

Build Release importer first:

```bash
cd ~/Projects/active/gate-payroll
dotnet publish -c Release tools/ErpImport -p:SkipFrontendBuild=true --no-self-contained -o /tmp/gate-sync-release
```

Default target env is hosted staging file (Ghazal’s shell only). Always dry-run first. Stop on any surprise vs A3.

### Shared stop conditions

- `source_identity_changed_requires_review` / `append_only_fact_changed_*` / `source_inconsistent_after_three_attempts`
- Any kind over threshold without an explicit `--accept-*` reviewed in that message
- Counts diverge wildly from A3 table above
- `/health` not `0.20.0` with preservation true after deploy

### Verify query (counts only, hosted or local)

```sql
SELECT kind, COUNT(*) AS c FROM dbo.records GROUP BY kind ORDER BY kind;
SELECT COUNT(*) AS watermarks FROM sync.SourceWatermarks; -- after first successful commit
```

### S1 — Ninja October workforce / ops

```bash
# DRY-RUN first
dotnet /tmp/gate-sync-release/ErpImport.dll --live --consistent --dry-run --counts-only \
  --accept-new-kind vehicle_operational_status \
  --accept-new-kind platform_day_usage \
  --accept-new-kind rider_work_timeline \
  --accept-new-kind ninja_evaluation_snapshot \
  --accept-append-only-change vehicle_odometer_reading
# After Ghazal «حمّل S1» and matching dry-run:
dotnet /tmp/gate-sync-release/ErpImport.dll --live --consistent --commit --counts-only \
  # same accept flags reviewed in the dry-run (include append-only only when dry-run required it)
```

Expected order of magnitude (from A3 first commit S1): ~1.4k new, ~0.6k changed, hundreds rejected (assignment/rider gaps). Then no-change commit &lt; 1 min.

### S2 — Jahez

```bash
dotnet /tmp/gate-sync-release/ErpImport.dll --live --consistent --dry-run --counts-only \
  --accept-new-kind jahez_daily_order \
  --accept-new-kind jahez_commission_policy
# «حمّل S2» → same with --commit
```

Expected: large `jahez_ledger_entry` / `jahez_transaction` deltas if hosted still behind live (A3 S2 ~17k new).

### S3 — Chefz

```bash
dotnet /tmp/gate-sync-release/ErpImport.dll --live --consistent --dry-run --counts-only \
  --accept-new-kind chefz_account_type_change \
  --accept-new-kind chefz_order_row
# «حمّل S3» → --commit
```

Expected: ~9.8k new on S3 kinds when those tables were empty on hosted.

### S4 — remaining money/display (not HungerStation)

```bash
dotnet /tmp/gate-sync-release/ErpImport.dll --live --consistent --dry-run --counts-only
# add only reviewed --accept-new-kind / --accept-large-change from that dry-run
# «حمّل S4» → --commit
```

List leftovers after S1–S3: any ERP-managed kind still empty that screens read (except `hunger.*`).

---

## 7. After S1 on hosted

Tell Ghazal the browser steps left for the first real Ninja payroll (see `docs/runbooks/NINJA-2026-10.md`): settings approve → invoice upload → classify/approve → operations import → review-draft → cycle stages 1–8 → bank file **DO-NOT-SEND**.
