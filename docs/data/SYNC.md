# ERP sync / mirror — cancelled (S1)

Full ERP sync/copy into payroll documents is **cancelled** (M-0577). Do not run `tools/ErpImport`, systemd `gate-erp-sync.*`, or the removed Hangfire jobs.

## What replaced it

- Live SELECT-only reads via `Payroll.Sync` (`LiveOperationsQuery` / `LiveOperationsSource`) and `ConnectionStrings:ErpReadonly`.
- Per pay-cycle capture snapshots (`pay_run_input`) — see [run-inputs](../design/run-inputs.md).

## Historical notes

Older preview/commit procedures, watermarks, cooldown, and `erp_sync_run` telemetry described in previous versions of this file are historical only. Local `erp_copy` containers and zip/folder sync snapshots are out of scope.

Hangfire tables on live, if any, wait for Ghazal’s yes before drop — see [HANGFIRE](../ops/HANGFIRE.md).
