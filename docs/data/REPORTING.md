# SQL reporting projections

Current target: **قاعدة الرواتب db77738** (BR33). Ghazal alone runs credential-dependent commands. No payroll database writes before the reviewed **«انقل»** cutover; ERP first-kind loads still need fresh **«حمّل»** afterward. Follow [DB-CUTOVER](../ops/DB-CUTOVER.md). Legacy identifiers `staging.env` / `STAGING_DB` are retained for compatibility. Historical counts below are dated evidence, not a fresh preview.

`records` remains the source of truth. `rpt` is a rebuildable typed projection, not another writable business store. No existing document body is migrated or rewritten. SQLite explicitly skips this path with a fixed diagnostic; it never creates imitation rpt tables.

Every table has RowKey(char64 primary key),SourceRecordId(nvarchar128),CompanyId(nvarchar128),RiderId(nvarchar64),Platform(nvarchar40),Period(char7),SourceId(nvarchar512),SourceSha(char64). Row keys are deterministic hashes of the source id/child position. SourceRecordId and each reference ending in Id have indexes; account code and period are indexed. Optional relationships remain nullable for unmapped historical records. There are no cascading foreign-key constraints on incomplete old data.

| Table | Source kind | Additional typed columns |
|---|---|---|
| Riders | rider | Name,Status,EngagementType,IsDeliveryRider(bit),CityId |
| Companies | company | Code,Name |
| PlatformAccounts | platform_account | ExternalAccountId,RegisteredRiderId,CityId,StartAt,EndAt |
| AccountAssignments | platform_account.assignments | AccountId,FromAt,ToAt,Status; RiderId is the actual operator |
| Vehicles | vehicle | AssetNumber,PlateNumber,VehicleType,OwnershipType,CityId |
| VehicleAssignments | vehicle_assignment | VehicleId,FromAt,ToAt,StartOdometer,EndOdometer |
| Debts | debt | Category,Status,TotalAmount,PaidAmount,RemainingBalance,MonthlyInstallment |
| DebtSettlements | debt_settlement | DebtId,JournalId,Method,CollectedAmount,WaivedAmount,RemainingAfter |
| JournalLines | journal.lines | JournalId,AccountCode,PlatformAccountId,DebtId,Debit,Credit,Chart,SourceType |
| PayCycles | pay_cycle | Stage(int),Status,PostingPeriod,JournalId; extra header for joining approval status |
| PayCycleLines | pay_cycle_line | CycleId,SchemeId,Orders(int),Gross,PlatformGross,AccountDeduction,Incentives,CompanyCommission,VehicleCharge,OperatingCost,Entitlement,NetBeforePersonal,Status |
| PlatformInvoices | platform_invoice | ApprovalStatus,Reference,AmountExclVat,Vat,DeductionsTotal,JournalId |
| PlatformInvoiceLines | platform_invoice_line | InvoiceId,PlatformAccountId,LineType,Orders(int),BaseGross,Incentives,DeductionAmount |
| Accounts | approved embedded COA | Code,Name,ParentCode,Nature,Status,Posting(bit) |

Amounts/odometers are decimal(19,4); dates datetimeoffset(7), normalized to UTC. Text columns use bounded Unicode. Period remains yyyy-MM. Projection does not invent absent company/platform/period evidence; join source references where needed. Journal company comes from the line, not another creditor in the header. No credentials, tokens, IBAN, phone, free-form histories or extra source payload enter the projection. Names/asset references are limited to existing admin fields. Protect database report-reader access with the same organizational rules as the admin UI; these tables are not public API responses.

`rpt.SourceState` records one source SHA,row count,projection SHA per source id/kind; `rpt.Meta` identifies schema version1. Account resource uses reserved source id/kind @coa. A consistency check recomputes expected rows from original raw bodies and the current chart, checks source counts/SHA, and hashes actual typed table rows. Changed cells, missing/extra rows, stale source state and stale chart all fail. Output contains aggregate counts/booleans only, never source rows or connection values.

Normal Store.Save refreshes affected ids inside the caller's existing SQL transaction. Buffered import flush calls the same batched refresh after source upserts, before commit. A projection error rolls back both source and report writes. No report schema is created at application startup. Until explicit initialization, refresh skips absent rpt.Meta. A full rebuild replaces derived rows only, leaves records byte-identical, and must pass consistency before commit. After changing the embedded COA, run an approved rebuild/check so rpt.Accounts matches the new resource.

```bash
# Always run from the task worktree; restore only from the local NuGet cache.
dotnet build tools/ReportRebuild/ReportRebuild.csproj --no-restore -p:SkipFrontendBuild=true -m:1
# Read-only: no Store startup/checkpoint, no schema writes.
dotnet tools/ReportRebuild/bin/Debug/net10.0/ReportRebuild.dll --dry-run
# Read-only consistency (exit2 if absent/inconsistent).
dotnet tools/ReportRebuild/bin/Debug/net10.0/ReportRebuild.dll --check
# Run ONLY after Ghazal's fresh «حمّل» for this reporting build.
dotnet tools/ReportRebuild/bin/Debug/net10.0/ReportRebuild.dll --commit
```

The tool reads staging.env internally and refuses any catalog except the approved payroll database catalog. It never logs a connection value, source id or row. It takes the same document-write lock for rebuild, serializable transaction, and checks existing records before/after. The guard still accepts only db77738, now the payroll database. Rebuild is an operator write requiring explicit approval after the reviewed cutover; it does not select another database. SQL integration must be reported as skipped until a permitted SQL execution actually passes; pure projection and SQLite no-op tests do not prove SQL DDL/bulk behavior.

R4.7 payroll database dry-run (2026-11-04): rpt absent; expected6797 rows: Riders2130,Companies3,PlatformAccounts1874,AccountAssignments1024,Vehicles558,VehicleAssignments800,Accounts408; remaining tables0. Read-only preview passed; initialization/rebuild awaits a fresh «حمّل». Counts can change before commit.
