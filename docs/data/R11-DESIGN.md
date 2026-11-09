# R11 design: fast incremental ERP sync, data scope, UI wiring

Owner: Cursor. Planner review: Claude Code (EGM on Cursor is unverified — hand off). Branch: `release/0.20.0`. BR commit: `ca9fdfe`. CR-004: `87b81c3`.

## Risk and review
HIGH: money, schema, sync, permissions, bank file. `design-review` → EGM hands off to Claude Code. This note is the design under review. Build continues unless a BR rule is missing or contradicts code.

## Current pain (measured later in T1)
Today every run:
1. Double full-table read of every allowlisted ERP table (`ConsistentSync.Read` + `SourceReader.Read`).
2. Builds the whole plan in memory (`ImportPlan` / `PullContracts`).
3. Compares each planned doc to target with `JsonNode.DeepEquals` over full bodies.
4. On commit: one `Store.Save` + one full audit row per changed record under a long write; MARS already off for importer connection.
5. Nested child scans in `ImportPlan` / `PullContracts` walk whole child tables per parent (quadratic).

## Audit rule check (BR)
BR does not require one full audit body per ERP mirror row. Maker-checker and reversing entries apply to payroll/posting (§24.7, §32, §35), not to ERP mirror copies. **Decision:** one summary audit per sync run (`erp.import.run`) plus a compact change log (record id, op, old hash, new hash). Keep refusal/threshold audits as today. No personal values in the change log.

## T1 — Timing
Phases on every summary and private run log (ms, integers):
- `readMs` — source read (including consistent double-read when used)
- `planMs` — `ImportPlan.Build`
- `writeMs` — compare + optional commit + same-snapshot verification
- `rptRefreshMs` — reporting refresh for changed kinds (0 until T3 wires it)

Baseline: dry-run `--live --consistent --dry-run` from read-only ERP into a local/copy target only. Numbers go in `SYNC.md`. No production write.

## T2 — Incremental read
New target table `sync.SourceWatermarks` (SQL Server; SQLite equivalent for tests):
| column | meaning |
|---|---|
| tableName | `schema.table` |
| columnKind | `rowversion` \| `id` \| `created_at` \| `dataset_version` |
| lastValue | opaque string (hex for rowversion, Guid/ISO for others) |
| runId | last successful commit run id |

Per ERP table class (CR-004 §3):
- **Auditable / Temporal (`RowVersion`):** `WHERE RowVersion > @last AND RowVersion < @upper` with `upper = MIN_ACTIVE_ROWVERSION()` read once at run start. Replaces double full read + hash for those tables when watermarks exist.
- **History (`CreatedAtUtc` / Id):** `Id` or `CreatedAtUtc` > last.
- **Module skip:** if `app.DatasetVersions` for the module is unchanged vs watermark, skip that module’s tables.
- **Hard-delete / edit-in-place** (chefz performance, settlements, order rows, imports; jahez superseded; housing bulk): re-read open periods only (current + previous calendar month), reconcile by Id, mark missing target rows `sourceDeleted=true` with audit in the compact log — never hard-delete ours.
- **Full resync:** admin-only, reason required, audited; only path that full-reads everything. Consistent double-read kept for full resync until CR-004 change feed lands.

First run without watermarks = full read, then seed watermarks. Missing BR/account: none for this mechanism.

## T3 — Fast write
- Store `contentHash` (SHA-256 of canonical JSON body without volatile import metadata) on each ERP-managed record (column or body field — prefer computed/stored column on `records` for SQL Server path).
- Upsert only hash-mismatched rows in batches (~2000) in short transactions.
- Scope `GatePayroll.DocumentWrite` applock to the batch (or use `GatePayroll.ErpMirrorWrite` for mirror kinds) — do not hold global lock for the whole run.
- One run summary audit + compact change log (see above).
- Refresh `rpt.*` only for kinds that had new/changed rows.
- MARS off for hosted sync connection (already for CLI importer).
- Replace quadratic child scans with pre-built dictionaries keyed by parent Id in `ImportPlan` / `PullContracts`.
- Targets: no-change run &lt; 1 minute; typical daily a few minutes. Report before/after in `SYNC.md`.

## T4 — Display-layer kinds (read-only, BR §38 / CR-004)
Add or complete kinds mapped exactly as ERP (no posting):
- custody: vehicle assignments + events, handovers, inventory issues, vehicle expenses, operational status periods
- performance: per platform account and rider for all apps; Ninja evaluation snapshots for expected eligibility
- Jahez money: operations, money movements, transferred debts, fees/refunds, earnings, commission policies, ledger, cashbox handovers, treasury v3
- housing residence periods; Chefz account details + type changes + settlements + order rows
- HungerStation wallets: **skip** (deferred §38.3)

Update `TARGET-KINDS.md`. HungerStation remains deferred — no new HS kinds.

## T5 — Calculation layer (only stated BR)
- Vehicle rent/deduction cap: never above vehicle allocated value for the period (§38.4 / §24.4).
- Housing deduction by residence days inside the app cycle (§38.4) as a cycle line with BR account; replaces manual-only path when residence facts exist.
- Chefz FT commission (§36): 25/day, max 15 days per half; partial = days×25; percentage unchanged. Tests: 28/29/30/31-day months; rider active only on day 31.
- Jahez reconciliation report (§37): collections + payments on behalf vs summary invoice through last-transaction date; VAT as its own line; proposed balanced journals; detailed report; maker-checker. **Do not post the VAT line** until BR names the VAT account (⏳ in §37). List in questions.

## T6 — Approval bypass (§35)
Permission `approval.bypass_maker_checker`: grant per user with from/to + reason; revoke anytime; audit every use; badge on approval screens; admin grant/revoke screen. Replaces dated self-approval exception without breaking existing audit rows.

## T7 — Ninja October past stage 2
`ValidateRiders` (`PayCycles.cs` ~122–148) fails when: missing assignment overlap, missing/invalid category, operation proof mismatch, share totals mismatch. Using a **local copy** of data only: fix code bugs; for data gaps produce counts-only list for Ghazal (accounts with no assignment, missing category). Parallel run (§38.5): post normally; bank file generated for comparison and marked «not to be sent».

## T8 — Frontend wiring
Regenerate contract (`npm run api:generate`). Wire every endpoint with no web caller (enumerated below as work proceeds). Follow `erp-table-workspaces`. Browser-check with chrome-devtools against local API; hide buttons when `allowedActions` says no. Remove or wire "coming soon" upload stubs.

### Endpoints to wire (seed list; expand while coding)
- Jahez: all `/finance/jahez-*` + new reconciliation report
- Chefz review/commission
- Treasury: opening, liability payment, legacy cash allocation, mada balance, accounting adjustments, bank file export
- Debts: bulk collect, opening import, settlements
- Admin: approval bypass grants, policy approve
- ERP sync: pause/resume/full-resync, run timing report
- Imports templates

## T9 — Release
Version `0.20.0` in `ReleaseSafety.cs` and `docs/api/ENDPOINTS.md`. Tests listed in the prompt. One PR, no merge. HANDOFF updated; writer lock cleared.

## Open BR / planner items (do not invent)
1. §37 VAT account named (`213201` / Dr `112801`) — report posts VAT with maker-checker.
2. CR-004 change feed not yet in ERP — watermarks + open-period reconcile are the workaround; full change feed is Omar’s side.
3. EGM formal review: Claude Code.

## Skills used
backend-design (query-discipline, migration-safety, idempotency, performance, observability), hunt-n-plus-one pattern via dictionaries, TDD for new calc rules, verification-before-completion, frontend-design + chrome-devtools for T8.
