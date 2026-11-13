# Run input snapshots (L2)

Money path: freeze ERP-sourced ops rows for one payroll cycle (one company, one app, one month) before calculation/approval. Binding rules: `~/Projects/active/gate-finance-spec/BUSINESS_RULES.md`. Plan: `.cursor/plans/live_ops_erp_reads_891ef52a.plan.md`. Branch: `feat/run-input-snapshots` from `feat/live-ops-erp-reads`.

EGM Stage 5 (2026-11-13): artifacts in `~/.agents/work/egm/gate-payroll/`. Stages 6–9 deferred to Claude step R (Cursor EGM unverified; host RAM &lt; 5 GiB). One fix round below.

## Goal

Screens keep reading logistics ERP live. A payroll run must not depend on a changing ERP mirror. Maker runs `capture_inputs` (recapture allowed until sealed). Draft, calculation, review, **and cycle fingerprint** read only those frozen rows for the swapped kinds. Seal (no further capture writes): cycle status is `approved` or `partially_approved`, or any rider has `salaryApprovedAt`. Response 409, no write. Invoice/terms binding is an explicit non-goal for L2 (sibling seal later).

## Feature

### Table `pay_run_input`

Dedicated table (dual-engine like `SourceWatermarks`: SQL Server `pay.PayRunInput` + SQLite `pay_run_input`). Additive only. Recapture deletes only rows for that same unapproved cycle inside one publish transaction; never delete approved-cycle rows from product code.

| Column | Role |
|---|---|
| cycle_id | Pay cycle id |
| platform | Cycle app (e.g. ninja) — denormalized per planner L2 |
| period_from / period_to | Inclusive calendar bounds for the cycle month |
| kind | Ops kind |
| doc_id | Source document id |
| body | JSON: calc-required fields ⊇ `Operations.Project` list for that kind |
| source_rowversion | Opaque ERP rowversion when present; null if absent (not empty string) |
| captured_at / captured_by | Capture stamp |
| body_hash | SHA-256 of canonical body (planner L2; used in tests/audit) |

Constraints: unique `(cycle_id, kind, doc_id)`; index `(cycle_id, kind)`. Module name: **`PayRunInputs`** (API surface may expose `RunInputs.List` as a thin alias).

### Capture kinds (closed set for Ninja first payroll)

Only kinds that have a named reader or fingerprint dependency:

| Kind | Scope in ERP read | Readers |
|---|---|---|
| `ninja_daily` | platform + period (date in month) | ValidateGaps, PayReviewDraft, Fingerprint |
| `ninja_daily_deduction` | platform + period | Fingerprint / future review; capture so calc is not silent if wired |
| `platform_account` | platform (+ company) | ValidateGaps, NinjaInvoices.Actions, Fingerprint |
| `vehicle_assignment` | company + period overlap (not platform filter) | Calculation, PayReviewCategory, Fingerprint |
| `housing_residence` | company riders + period overlap | Calculation |
| `vehicle` | ids referenced by captured assignments | Fingerprint |
| `rider_work_timeline` | **not captured in L2** (no reader yet) | — |

Chefz/Jahez: same pattern when that cycle app is used (daily + accounts + vehicle/housing). `rider` stays payroll master (not captured; drift noted for R). `ninja_account` is not a live ops kind — Actions uses `platform_account` only after L2.

### Capture action

`POST /api/pay/cycles/{id}/capture-inputs`

- `allowedActions` includes `capture_inputs` only when not sealed (see Goal) and actor has `pay.manage` with company scope.
- Live ERP via `LiveOperationsQuery` with **period (+ platform where kind is platform-scoped) in SQL `BuildWhere`**, not in-memory. Stream pages of 500 (capture-only constant; UI max stays 200). Prefer: stage pages outside the final TX, then one short TX delete+insert (or replace) for that cycle_id; if staging is too large for L2, document timeout risk and keep single TX with clear failure = no partial bag.
- Audit `pay.inputs_captured` with counts per kind. Response: counts per kind + `captured_at`.

### Readers

`PayRunInputs.List(c, cycleId, kind)` returns frozen bodies.

Swap cycle-scoped mirrored reads:

- `PayCycles.Calculation.cs` — vehicle_assignment, housing_residence
- `PayCycles.ValidateGaps.cs` — platform_account, ninja_daily
- `PayCycles.Sources.cs` Fingerprint — vehicle_assignment, platform_account, ninja_daily, vehicle (from captured set)
- `PayReviewDraft.cs` — ninja_daily
- `PayReviewCategory.cs` — vehicle_assignment
- `NinjaInvoices.Actions.cs` — platform_account for cycle-bound paths

If a required kind has zero captured rows → review blocker «التقط المدخلات أولًا» (never silent zero; never fall back to Store mirror for those paths).

Non-cycle (HANDOFF will list): `ChefzOperations.cs`, `KeetaInvoices.cs`, `HungerStationInvoices.Parse.cs`, `InvoiceDeductionClassification.cs` — not cycle-bound in L2.

### Mirror off

S1–S4 and `ErpSyncJob` stay disabled. TODO dated 2026-11-13: remove after first payroll. Do not touch old mirrored rows.

### Contract gaps (prompt 4b — same PR, not money logic)

OpenAPI row schemas per kind from `Operations.Fields`; regenerate `api-contract.ts`. Add `web/tests/ops-live-ui.mjs` and `tests/r8-contract.mjs` to browser CI list.

## Open for Claude R / Ghazal (after one fix round)

- Seal at any `salaryApprovedAt` vs only final `pay.approve` (BR §32).
- Capture generations instead of delete+insert.
- Rider stub in the bag; invoice/terms sibling seal.
- Whether `body_hash` / per-row period stay long-term.

## Detailed Implementation Plan

1. `PayRunInputs.Ensure` + ReplaceCycle/List/hash; call Ensure from Store open beside other Ensures.
2. Extend `LiveOperationsQuery` with optional period-from/to and capture page size 500; platform filter only for platform-scoped kinds.
3. Capture endpoint + View action + audit; tests: capture, recapture, sealed → 409.
4. Swap listed readers + Fingerprint; blocker when required kinds empty.
5. Synthetic Ninja 2026-10: capture → draft/fingerprint use only `pay_run_input`; change fixture ERP after seal → approved run unchanged.
6. OpenAPI + CI browser list; `gen-api-types --check`, contracts, browser list, 0 skipped.
7. HANDOFF file:line + cycle-bound matrix; clear writer lock; stop for Claude migration/money review.
