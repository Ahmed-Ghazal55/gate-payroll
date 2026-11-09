# ERP → payroll database sync

## R11 timing (T1 baseline, 2026-11-09)

Every summary and private live run log now carries `phases`: `readMs`, `planMs`, `writeMs`, `rptRefreshMs`, `totalMs` (integers, counts only).

Baseline dry-run against read-only ERP → staging target (`--live --consistent --dry-run --counts-only`), no commit:

| metric | value |
|---|---|
| wall clock | 865 328 ms (~14.4 min) |
| status | refused (`source_identity_changed_requires_review`) during compare |
| consistent attempts | 1 (double-read matched) |
| notes | First instrumented run exited before phases were attached to `lastSummary`; refusal path now captures phases. Re-measure after T2/T3. Target: no-change &lt; 1 min; typical daily a few minutes. |

Design: [R11-DESIGN.md](R11-DESIGN.md).

### R11 incremental reads (T2)

After the first successful commit, the target stores watermarks in `sync.SourceWatermarks` (SQLite: `sync_source_watermarks`). Later runs:

- skip modules whose `app.DatasetVersions` row is unchanged;
- read Auditable/Temporal tables with `RowVersion > last AND RowVersion < MIN_ACTIVE_ROWVERSION()` (one upper bound per run; no double full read);
- re-read open periods (current + previous Riyadh month) for hard-delete/edit-in-place tables and soft-mark missing target rows (`sourceDeleted`, never hard delete);
- accept `--full-resync <reason>` (admin API: `fullResync` + `reason`) as the only full-table read path once watermarks exist.

The selected workflow is [manual ERP sync with startup catch-up and optional daily Hangfire execution](../ops/HANGFIRE.md), disabled by default. There is no three-hour recurrence or active VPS. The old systemd files remain disabled; VPS guidance is historical/optional and requires a new explicit decision. Manual CLI acceptance remains unchanged. Hosted dry-runs record counts-only payroll database telemetry; CLI dry-runs remain private-file-only.

The importer accepts `--live --consistent --dry-run` and `--live --consistent --commit`. Source and target credentials are read internally from their private env files; the only accepted target remains payroll database db77738. Any other target database is rejected. A live commit without `--consistent` is refused. A new ERP load requires Ghazal's «حمّل» after its dry-run; the certificate-load approval does not enable scheduling.

Each attempt reads the explicit source-table allowlist twice, ordered by primary key. Per-table canonical fingerprints are compared in memory, covering every table that contributes to a kind, including nested history. At most three pairs are attempted. Any mismatch retries the complete pair; three mismatches refuse the run before Store creation or writes. No snapshot-isolation setting or ERP record is changed.

The accepted plan is compared with the ERP-managed target baseline. For an existing kind, the run is refused only when new + changed records exceed max(10% of its baseline, 5 rows), or when the count drop exceeds max(2% of its baseline, 1 row). Equality is allowed. Thus one changed row in a three-row company/city kind passes; eleven changes in a hundred-row kind refuse. A previously empty kind with proposed new rows is refused unless that exact kind is named in `--accept-new-kind <kind>`. Manual/certificate records are excluded from the ERP baseline; certificate-verified registry fields survive subsequent ERP merges. Missing source records are retained; there is no DELETE path. The Store transaction checks concurrent target changes and writes records/audits atomically, then verifies the same source snapshot without rereading it.

Every CLI `--live` run writes a uniquely named JSON log under `~/Private/gate-data/sync-logs/`, directories 700 and files 600. Its typed schema contains timestamps, attempts, fixed error codes, aggregate import counts/reasons, and the validated kind lists `acceptNewKinds` / `acceptLargeChanges` (empty lists for the normal timer). Fingerprints, connection strings and record bodies are excluded. An inconsistent/refused run is logged with its available counts and refusal code. Inspect that result before replaying a failed commit; the `committed` flag and next dry-run establish the actual state.

Build the Release importer before the first dry-run and after every code update; the unit executes this build output from `~/Projects/active/gate-payroll` after merge/review:
```bash
cd ~/Projects/active/gate-payroll
dotnet publish -c Release tools/ErpImport -p:SkipFrontendBuild=true --no-self-contained -o /tmp/gate-sync-release
```
A Debug build does not create the binary required by the service.

Review commands (SELECT-only):
```bash
dotnet tools/ErpImport/bin/Release/net10.0/ErpImport.dll --live --consistent --dry-run
```

## Reviewed manual overrides

Both flags are repeatable and apply to exact allowlisted kind names. Unknown or missing names fail argument validation before connections are opened. They do not bypass the consistent source reads, target checks, immutable-fact checks, concurrent-write checks, audit, or the requirement for approval before payroll database writes.

- `--accept-new-kind <kind>` permits initial rows for a previously empty ERP-managed kind. Acceptance applies only to that named kind and is required even if `--accept-large-change` names it too. Once loaded, later daily runs have a nonzero baseline.
- `--accept-large-change <kind>` permits a reviewed change or count-drop threshold exceedance for an existing kind in that manual run. It does not remove any target records. Do not add this flag to the timer/service.

For example, after inspecting the counts, a manual SELECT-only review can name multiple kinds:
```bash
dotnet tools/ErpImport/bin/Release/net10.0/ErpImport.dll --live --consistent --dry-run \
  --accept-new-kind legal_case --accept-new-kind city --accept-large-change rider
```
These names are examples, not standing approval. New HS kinds become available with the M2 importer; accept them individually only after reviewing their source contract and proposed counts. A commit uses the same reviewed flags and additionally `--commit` instead of `--dry-run`, only after Ghazal's explicit payroll database-write approval. The run log records both flag lists for completed, refused and failed live runs. Normal unattended runs pass neither flag.

## When a log says `refused`

1. Read the private counts-only log's `errorCode`, affected kind, baseline/proposed counts and override lists. Do not blindly repeat a commit or enable the timer. If writes might already have committed before a verification failure, use `summary.committed` plus a new SELECT-only dry-run to establish the actual state first.
2. For `source_inconsistent_after_three_attempts`, wait for an approved quieter read window and retry a dry-run. The overrides do not bypass inconsistent reads.
3. For `new_kind_requires_acceptance`, review the new source contract and every proposed kind count, then dry-run with that exact `--accept-new-kind`. Other new kinds remain refused.
4. For `kind_changes_over_threshold` or `source_count_drop_over_threshold`, check the cause with the source owner. Correct unexpected data/source problems. Only a confirmed legitimate change warrants a manual dry-run with `--accept-large-change` for that exact kind; missing target rows are retained even then.
5. Review the successful dry-run and get approval for the manual payroll database load. Rebuild after code changes. Leave unattended units without overrides; if an anomaly recurs, stop the schedule and investigate rather than making an override permanent.

Use [Hangfire operations](../ops/HANGFIRE.md) for manual run/status permission, shared15-minute cooldown,24-hour startup catch-up and the optional10:00 Riyadh daily schedule. The old systemd files remain disabled and uninstalled; their optional alternative setup is isolated in [VPS-SYNC.md](VPS-SYNC.md). Do not enable both schedulers.

## R6a additions

The explicit allowlist now includes fuel, custody, vehicles, Chefz, Jahez final financial rows, Ninja operations and additional HR/treasury sources. See TARGET-KINDS.md. Dry-run summaries include accepted numeric totals separately from rejection counts. `debt-sheet-mapping.v1.json` and DEBT-SHEET.md define the separate, draft-approved FIFO opening workbook adapter; it is never scheduled.

After initial manual acceptance and explicit «حمّل», committed-mode live runs append counts-only `erp_sync_run` records and administrator-only refusal notifications. Refusals never write source-domain documents. CLI dry-runs write only private JSON logs. Hosted dry-runs additionally record counts-only telemetry after Jobs is deliberately enabled; no hosted job was enqueued on real payroll database in this task. Admin/SuperAdmin endpoint: `GET /api/system/erp-sync?limit=20` (maximum 100).

### Vehicle drift correction (R6a-fix1-2)
The live source moved since the 317/558 preview. Before correction: 332/558 changed; field counts: currentOdometer 315, currentOperationalStatus 29, currentAssignmentId 27, cityId 2, plateNumberAr 1, plateNumberEn 1, registrationType 1. Counts overlap. Daily operational pointers are no longer refreshed in vehicle; existing values are retained, without deleting records. Assignments remain separate facts.
After correction: vehicle 1 changed / 557 unchanged out of 558; 13303 new vehicle_odometer_reading facts. Stable changed fields: plateNumberAr=1, plateNumberEn=1, registrationType=1 (overlap). No accept override was used: the new reading kind still requires explicit first acceptance after approval. Existing readings are immutable; corrections/voiding require manual review. Missing readings are retained and source-count drop protection remains. Only additions to an already accepted reading kind bypass the change-percentage threshold. Private aggregate evidence: sync-logs/r6a-f2-before.json and r6a-f2-after.json.
