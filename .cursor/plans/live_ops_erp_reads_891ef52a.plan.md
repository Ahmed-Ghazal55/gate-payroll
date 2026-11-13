---
name: Live ops ERP reads + frozen run inputs
overview: Stop mirroring ERP into payroll db77738. Operations screens read logistics ERP db67927 live, read-only, one page at a time, with scope in SQL. A payroll run captures only its own inputs (one app + one month) into pay_run_input and freezes them at approval. This lands BEFORE the first payroll (Ghazal 2026-11-12). Master plan - ~/.claude/plans/sharded-yawning-kettle.md
todos:
  - id: c0-source-switch
    content: "C0 (Claude): Operations:Source switch, store fallback in Development/CI, auth+kind before ERP, 20 s timeout, read-only check cache, read audit, health flags, response source/asOf/allowedActions"
    status: completed
  - id: l1-paged-query
    content: "L1 (Cursor): one-table paged SQL per kind (ORDER BY Id, OFFSET/FETCH, COUNT), parents by page keys, reuse ImportPlan.Build on the small snapshot"
    status: completed
  - id: l1-scope-in-sql
    content: "L1 (Cursor): company scope + rider-level keys in WHERE; no per-row Store.Get; correct totals"
    status: completed
  - id: l1-search-contract
    content: "L1 (Cursor): q search in SQL (case-insensitive, Arabic normaliser parity), pageSize max 200, regenerate web/src/lib/api-contract.ts"
    status: completed
  - id: l1-rider-accounts
    content: "L1 (Cursor): rider accounts query filtered by that rider's ERP key only"
    status: completed
  - id: u1-ops-ui
    content: "U1 (Antigravity Flash 3.8): server paging/search, live badge + asOf, 503 state, allowedActions buttons, rider accounts panel"
    status: completed
  - id: s1-security
    content: "S1 (Claude): security pass on PR #36 (IDOR, SQL injection in dynamic SQL, scope) + one fix round"
    status: completed
  - id: l2-run-inputs
    content: "L2 (Cursor): pay_run_input table + capture_inputs action + RunInputs.List readers swap; design-review (EGM) first"
    status: pending
  - id: u2-cycle-ui
    content: "U2 (Antigravity Flash 3.8): cycle screen capture inputs button, captured-at, rows per kind"
    status: pending
  - id: br-text
    content: "R (Claude): BR section draft, migration review, release notes; Ghazal approves BR and deploy"
    status: pending
isProject: false
---

# Live ops reads from logistics ERP + frozen run inputs

## Decisions (Ghazal 2026-11-12)

- No full ERP mirror in db77738. Screens read ERP live. The S1 full load is cancelled.
- A payroll run copies **only its own inputs** and freezes them at approval (audit trail; split by app + month).
- This lands before the first payroll (Ninja 2026-10).
- Codex does nothing for now. Claude takes security, migrations and bigger builds. Cursor is the main builder.

## What was wrong in the first version (review 2026-11-12)

1. CI broke: with no ERP login, `/api/operations/*` returned 503, so `web/tests/r8-ninja-flow.mjs:397` got no `items`. **Fixed in C0.**
2. No SQL paging: `LiveOperationsSource.Read` → `SourceReader.ReadTables` reads every related table in full (`platform_account` pulls Employees, Sponsors, Cities, assignments) on each request. Then it pages in memory.
3. Scope was checked after the read (`Operations.Visible` + `Store.Get` per row = N+1) → wrong totals, short pages.
4. No `q` in SQL, no stable order, no `allowedActions`, no source/as-of, no timeout, no read audit, no rollback switch. **Partly fixed in C0** (switch, timeout, audit, envelope).
5. The ERP was read before the permission check and before the kind was validated. **Fixed in C0.**
6. Payroll still reads mirrored kinds via `Store.List` (`PayCycles.Calculation.cs`, `PayCycles.ValidateGaps.cs`, `PayReviewDraft.cs`, `PayReviewCategory.cs`, `NinjaInvoices.Actions.cs`, `ChefzOperations.cs`, `KeetaInvoices.cs`, `HungerStationInvoices.Parse.cs`, `InvoiceDeductionClassification.cs`) → Phase B.

## Phase A — live reads (PR #36, branch `feat/live-ops-erp-reads`)

- C0 done (`0991530`): `Operations.SourceMode` (`erp_live` | `store`; Production is always live), `Operations.ErpConfigured`, `Envelope` (source, asOf, allowedActions), `LiveRead` (20 s), read-only check cache, `operations.rider_accounts_read` audit, `/health` flags.
- L1: new `Payroll.Sync/LiveOperationsQuery.cs`. Per kind: main table + allowlisted columns + key column + company column + search columns.
  `SELECT … WHERE scope AND q ORDER BY <key> OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY` and `SELECT COUNT_BIG(*)` with the same WHERE. All values are parameters; table and column names come only from the code allowlist.
  Parents are loaded by `IN (@k0..@kn)` for the page keys (batches ≤ 500). Then build a `SourceSnapshot` from those rows and reuse `ImportPlan.Build` → same JSON as `Operations.Fields`.
- Scope: company ids from `FinancialScopes` go into WHERE. Rider-level actors: their allowed ERP employee keys from the payroll `rider` rows, as parameters.

## Phase B — frozen run inputs (branch `feat/run-input-snapshots`)

- Table `pay_run_input(cycle_id, platform, period_from, period_to, kind, doc_id, body, source_rowversion, captured_at, captured_by, body_hash)`, unique (cycle_id, kind, doc_id), index (cycle_id, kind).
- `capture_inputs` cycle action (in `allowedActions` until approval). Reads live ERP for that platform + period only. A recapture replaces the rows until approval. After approval: no writes (code + test).
- `RunInputs.List(c, cycleId, kind)` replaces the `Store.List` reads above. `rider` stays payroll master data.
- Mirror jobs (S1–S4, `ErpSyncJob`) stay off. The code is removed after the first payroll. Old mirrored rows are kept until Ghazal decides.

## Out of scope

- Writes to db67927. ERP changes other than CR-004. Production DB. Secret values in git, docs or chat.

## Checks

- CI green; browser tests run on `store` in Development.
- Live read-only ERP (local `ERP_READONLY_DB`): page 1 < 2 s per kind, `total` correct, no overlap between pages 1 and 2.
- A company-scoped user never sees another company's rows.
- Phase B: Ninja 2026-10 capture → the draft reads only `pay_run_input`. An approved run doesn't change after ERP changes.
