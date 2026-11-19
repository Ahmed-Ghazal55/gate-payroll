# S1 removal list — ERP sync/copy + Hangfire

Branch: `chore/s1-remove-sync-hangfire`. Decisions: M-0577, M-0590, Ledger H2 (M-0621). Live Hangfire tables stay until Ghazal says yes (code only).

## Hangfire

| Item | Action | Why |
|---|---|---|
| Hangfire packages in `Payroll.Api/Payroll.Api.csproj` | **remove** | Only used for ERP sync |
| `GateJobs` / `HangfireJobsSchedule` / `JobsStartup` / `AddHangfire*` in `JobsSchedule.cs` | **remove** | Only sync jobs registered |
| `ErpSyncJob`, `ErpSyncDailyDispatcher` | **remove** | Sync jobs |
| `JobsOptions`, `JobRunGate`, `JobsControl`, `IJobsSchedule`, `DisabledJobsSchedule`, `JobsHealth` | **remove** | Sync scheduling/control |
| `Program.cs` `GateJobs.Add`, `JobsControl.Map`, health `jobs` field | **remove / simplify** | No non-sync Hangfire job exists (`FutureIds` are stubs only) |
| Live DB schema `hangfire.*` | **keep (DB)** | Do not drop tables |
| Docs `docs/ops/HANGFIRE.md` | **update/retire** | Describe removal; tables wait for yes |

## ERP sync API / runner

| Item | Action | Why |
|---|---|---|
| `ErpSyncRunner.cs`, `ErpSyncRequests.cs`, `ErpSyncRuns.cs`, `ErpImportExecutor.cs` | **remove** | Sync/copy write path |
| Routes `/api/system/jobs/erp-sync/*`, `/api/system/erp-sync`, `/api/system/jobs` | **remove** | Manual sync + status |
| AccessPolicy sync path checks | **remove** | Endpoints gone |
| Permission catalog entry `erp.sync.run` in `UserAccess.cs` | **keep alias** | Other stored grants may reference it |
| Seed `ErpSyncRequests.SeedPermission` | **remove** | No longer needed for new installs |
| `tests/erp-sync.mjs`, `Jobs*.cs` / `ErpSyncRunTests.cs` / `ErpImportTests.cs` (sync parts) | **remove** | Cover removed code |

## `Payroll.Sync/` (namespace `ErpImport`)

| File / symbol | Action | Why |
|---|---|---|
| `LiveOperationsQuery.cs`, `LiveOperationsSource.cs` | **keep** | Live ops + capture reads |
| `Connections.Source` (live readonly) | **keep** | Live ERP reads |
| `Connections.LocalCopy` / `erp_copy` allowlist + `Target` staging import | **remove** | Cancelled local copy / staging sync |
| `SourceReader.cs`, `SourceReadProfiles.cs`, `PullContracts.cs` | **keep** | Live table catalog + capture |
| `ImportPlan.cs` (+ `PlannedDocument`) | **keep** | Shared projection used by live `LiveOperationsSource.Project` |
| `ImportModels.cs`, `SyncContracts.cs` (`ISyncSponsor`), `ErpPlatformCode.cs` | **keep** | Live + finance sponsors |
| `SyncCancellation.cs` | **keep** | Used by live capture |
| `DebtCreditorHistory.cs` | **keep** | Debt opening / creditor resolve (not ERP mirror sync) |
| `ContentHash.cs` | **keep for now** | Used by projection/hash helpers; list if only sync after cleanup |
| `HsFactsSource.cs` | **keep** | HS fact table map; live/facts may still need it |
| `ConsistentSync.cs`, `SourceWatermarks.cs`, `OpenPeriodReconcile.cs`, `SyncRunLog.cs`, `SchemaInspector.cs` | **remove** | Sync consistency / watermarks / CLI schema dump |

## Tools

| Item | Action | Why |
|---|---|---|
| `tools/ErpImport/Program.cs` + systemd `gate-erp-sync.*` | **remove** | Sync CLI |
| `tools/ErpImport/DebtSheet*.cs` | **move** into `Payroll.Api` (or sibling lib) then drop tool project | Debt-sheet / opening is pay-pipeline import, not ERP sync; keep behavior |
| `Engine.Tests` project ref to `ErpImport.csproj` | **fix** | Point at Api/Sync only |
| `ErpSchemaProbe`, `ErpCaptureProbe`, `RiderIdentityRepair`, `ReportRebuild` | **keep** (fix if they call `LocalCopy` / sync-only APIs) | Probes/repair for live reads; not Hangfire |

## Web UI

| Item | Action | Why |
|---|---|---|
| `web/src/components/system/ErpSyncDrawer.tsx` | **remove** | Sync UI |
| Header / Sidebar import-centre + ERP sync actions | **remove** | Ledger H2 |
| `web/src/app/imports/` + `features/imports/FreeImportTab.tsx` | **remove** | Import centre UI |
| Links to `/imports/` from home / operations / payroll | **remove or retarget** | Dead after page removal |
| `api.runErpSync*` / types `ErpSync*` | **remove** | Sync client |
| Platform invoice / ops import API helpers (Ninja, Keeta, HS, operations import, bank/pay files) | **keep** | Pay pipeline |
| Riders baseline sync UI + `POST /riders/sync-baseline` | **remove** | Second COPY path (not live ERP) |
| Regenerate `web/src/lib/api-contract.ts` via `npm run api:generate` after OpenAPI snapshot update | **yes** | Contract must match removed routes |

## Backend keep (pay-file / non-ERP-sync imports)

| Item | Action | Why |
|---|---|---|
| `NinjaInvoices*`, `KeetaInvoices*`, `HungerStationInvoices*`, `OperationsImports.cs`, `BankPayrollFiles.cs`, `PayAssignments` import, `FinancialImports` (non-ERP) | **keep** | Pay pipeline file imports |
| `CompanyRegistryImport.cs`, `ImportCatalog.cs`, free-form `/api/imports/*` | **keep (API)** | Old `Frontend/` + test scripts (`tests/integration.mjs`, `enterprise.mjs`, `finance-views*.mjs`, `r6c-imports.mjs`) still call `/api/imports/*`. Catalog page restored in S1F uses `/imports/catalog` + platform/debt/finance upload URLs. Remove free-upload later with the old Frontend. |
| Permission `imports.view` / `imports.commit` | **keep** | Still gates excel/work imports |
| Permission alias `erp.sync.run` | **keep alias, hide in editor** | Stored users may still list it; role editor catalog omits it (Deprecated) |

## Docs / config / scripts

| Item | Action | Why |
|---|---|---|
| `docs/data/SYNC.md`, `VPS-SYNC.md`, `docs/ops/HANGFIRE.md`, ERP sync sections in README / ERP-DATA-MAP / runbooks | **update** | Sync cancelled |
| `docs/design/run-inputs.md` TODO about ErpSyncJob | **update** | Mirror removed |
| `docs/api/ENDPOINTS.md` + `openapi.v1.json` | **regenerate/update** | Drop sync routes |
| `scripts/release_source.py` ErpImport / Hangfire paths | **update** | Release inventory |
| `tools/api_inventory.py` sync permission notes | **update** | Match removed routes |
| Repo `appsettings*.json` Jobs keys | **none present** | Only live env; do not touch live |

## Doubt (record, do not block)

1. Whether `/api/imports/*` free upload should be deleted later with the old `Frontend/` once test scripts move to platform import endpoints (kept for now; see keep table).
2. Whether `ContentHash` / `HsFactsSource` can shrink after sync executor is gone (keep unless build proves unused).
3. `RiderIdentityRepair` still builds a full `ImportPlan` for identity repair — kept as a tool, not a Hangfire job.
