# Hangfire / ERP sync jobs — removed (S1)

Code path removed on branch `chore/s1-remove-sync-hangfire` (M-0577, M-0590). The API no longer references Hangfire packages, registers workers, or exposes `/api/system/jobs/erp-sync/*`.

Live ERP access is **read-only** through `ConnectionStrings:ErpReadonly` / live readers in `Payroll.Sync`. Pay cycles snapshot the inputs they use; there is no ERP mirror or `erp_copy` load path.

## Live database tables

If MonsterASP still has a `hangfire` schema / tables from earlier releases, **do not drop them** until Ghazal explicitly says yes. Code removal alone is enough for S1.

## Settings (names only)

Old hosted keys such as `Jobs__Enabled`, `Jobs__ErpSync__*`, and `ConnectionStrings__Jobs` are unused by current code. Leave live values alone unless Ghazal asks to clean them.

Permission catalog alias `erp.sync.run` may still exist on stored users; it grants nothing useful without the removed endpoints.
