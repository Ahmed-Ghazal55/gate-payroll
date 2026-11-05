# Daily ERP → staging sync

The importer accepts `--live --consistent --dry-run` and `--live --consistent --commit`. Source and target credentials are read internally from their private env files; the only accepted target remains staging db77738. Production is never accepted. A live commit without `--consistent` is refused. A new staging load requires Ghazal's «حمّل» after its dry-run; the certificate-load approval does not enable this timer.

Each attempt reads the explicit source-table allowlist twice, ordered by primary key. Per-table canonical fingerprints are compared in memory, covering every table that contributes to a kind, including nested history. At most three pairs are attempted. Any mismatch retries the complete pair; three mismatches refuse the run before Store creation or writes. No snapshot-isolation setting or ERP record is changed.

The accepted plan is compared with the ERP-managed target baseline. For an existing kind, the run is refused only when new + changed records exceed max(10% of its baseline, 5 rows), or when the count drop exceeds max(2% of its baseline, 1 row). Equality is allowed. Thus one changed row in a three-row company/city kind passes; eleven changes in a hundred-row kind refuse. A previously empty kind with proposed new rows is refused unless that exact kind is named in `--accept-new-kind <kind>`. Manual/certificate records are excluded from the ERP baseline; certificate-verified registry fields survive subsequent ERP merges. Missing source records are retained; there is no DELETE path. The Store transaction checks concurrent target changes and writes records/audits atomically, then verifies the same source snapshot without rereading it.

Every `--live` run writes a uniquely named JSON log under `~/Private/gate-data/sync-logs/`, directories 700 and files 600. Its typed schema contains timestamps, attempts, fixed error codes, aggregate import counts/reasons, and the validated kind lists `acceptNewKinds` / `acceptLargeChanges` (empty lists for the normal timer). Fingerprints, connection strings and record bodies are excluded. An inconsistent/refused run is logged with its available counts and refusal code. Inspect that result before replaying a failed commit; the `committed` flag and next dry-run establish the actual state.

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

Both flags are repeatable and apply to exact allowlisted kind names. Unknown or missing names fail argument validation before connections are opened. They do not bypass the consistent source reads, target checks, immutable-fact checks, concurrent-write checks, audit, or the requirement for approval before staging writes.

- `--accept-new-kind <kind>` permits initial rows for a previously empty ERP-managed kind. Acceptance applies only to that named kind and is required even if `--accept-large-change` names it too. Once loaded, later daily runs have a nonzero baseline.
- `--accept-large-change <kind>` permits a reviewed change or count-drop threshold exceedance for an existing kind in that manual run. It does not remove any target records. Do not add this flag to the timer/service.

For example, after inspecting the counts, a manual SELECT-only review can name multiple kinds:
```bash
dotnet tools/ErpImport/bin/Release/net10.0/ErpImport.dll --live --consistent --dry-run \
  --accept-new-kind legal_case --accept-new-kind city --accept-large-change rider
```
These names are examples, not standing approval. New HS kinds become available with the M2 importer; accept them individually only after reviewing their source contract and proposed counts. A commit uses the same reviewed flags and additionally `--commit` instead of `--dry-run`, only after Ghazal's explicit staging-write approval. The run log records both flag lists for completed, refused and failed live runs. Normal unattended runs pass neither flag.

## When a log says `refused`

1. Read the private counts-only log's `errorCode`, affected kind, baseline/proposed counts and override lists. Do not blindly repeat a commit or enable the timer. If writes might already have committed before a verification failure, use `summary.committed` plus a new SELECT-only dry-run to establish the actual state first.
2. For `source_inconsistent_after_three_attempts`, wait for an approved quieter read window and retry a dry-run. The overrides do not bypass inconsistent reads.
3. For `new_kind_requires_acceptance`, review the new source contract and every proposed kind count, then dry-run with that exact `--accept-new-kind`. Other new kinds remain refused.
4. For `kind_changes_over_threshold` or `source_count_drop_over_threshold`, check the cause with the source owner. Correct unexpected data/source problems. Only a confirmed legitimate change warrants a manual dry-run with `--accept-large-change` for that exact kind; missing target rows are retained even then.
5. Review the successful dry-run and get approval for the manual staging load. Rebuild after code changes. Leave unattended units without overrides; if an anomaly recurs, stop the schedule and investigate rather than making an override permanent.

The user units in `tools/ErpImport/systemd/` are delivered **disabled and uninstalled**. After merge/review, a successful dry-run and explicit approval for scheduled staging writes, Ghazal can build the Release importer, copy the two units to his user systemd directory and enable the timer himself:
```bash
dotnet publish -c Release tools/ErpImport -p:SkipFrontendBuild=true --no-self-contained -o /tmp/gate-sync-release
mkdir -p ~/gate-sync ~/.config/systemd/user
cp -a /tmp/gate-sync-release/. ~/gate-sync/
cp tools/ErpImport/systemd/gate-erp-sync.{service,timer} ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now gate-erp-sync.timer
systemctl --user list-timers gate-erp-sync.timer
```
`OnCalendar` explicitly uses Asia/Riyadh every three hours at :15; `Persistent=true` catches a missed run when the user manager starts. A catch-up run may therefore happen during working hours: disable the timer if that read window is not approved. Codex has not installed or enabled it. No env values belong in unit files or the journal.

## R6a additions

The explicit allowlist now includes fuel, custody, vehicles, Chefz, Jahez final financial rows, Ninja operations and additional HR/treasury sources. See TARGET-KINDS.md. Dry-run summaries include accepted numeric totals separately from rejection counts. `debt-sheet-mapping.v1.json` and DEBT-SHEET.md define the separate, draft-approved FIFO opening workbook adapter; it is never scheduled.

The timer now runs every three hours at :15 Riyadh (`00/3:15:00`), with Persistent=true. Units assume a dedicated non-root sync user's `~/gate-sync/ErpImport.dll`; use the complete published directory. VPS setup/network/first-load/stop instructions and the verified empty Hostinger VPS list are in VPS-SYNC.md. The units remain uninstalled and disabled.

After initial manual acceptance and explicit «حمّل», committed-mode live runs append counts-only `erp_sync_run` records and administrator-only refusal notifications. Refusals never write source-domain documents. Before approval, and for all dry-runs, only private JSON logs are written; no staging telemetry is inserted. Admin/SuperAdmin endpoint: `GET /api/system/erp-sync?limit=20` (maximum 100).
