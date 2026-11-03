# Reconciliation — 2026-11-03

Status: **loaded to staging after Ghazal’s explicit «حمّل»**. Source is the restored local READ_ONLY backup. The live attempt was refused before writes because its read changed; SQL snapshot isolation was not enabled/changed.

Backup header finish: 2026-11-03 11:05:55 (server-clock timezone unspecified). SQL Server major 17. Original bytes verified after docker cp; source size/mtime unchanged.

Load read window (UTC): 2026-11-03T17:58:31.0969517+00:00 → 2026-11-03T17:58:31.2474698+00:00. Mode: `local_read_only_copy`.

| Kind | Backup source rows | New loaded | Changed | Rejected | Actual staging | Same-snapshot new/changed |
|---|---:|---:|---:|---:|---:|---|
| company | 3 | 3 | 0 | 0 | 3 | 0/0 |
| commercial_registration | 3 | 0 | 0 | 3 | 0 | 0/0 |
| city | 5 | 5 | 0 | 0 | 5 | 0/0 |
| rider | 2160 | 2130 | 0 | 30 | 2130 | 0/0 |
| housing | 7 | 7 | 0 | 0 | 7 | 0/0 |
| housing_residence | 200 | 200 | 0 | 0 | 200 | 0/0 |
| vehicle | 558 | 558 | 0 | 0 | 558 | 0/0 |
| vehicle_assignment | 800 | 800 | 0 | 0 | 800 | 0/0 |
| custody_handover | 863 | 861 | 0 | 2 | 861 | 0/0 |
| platform_account | 1874 | 1874 | 0 | 0 | 1874 | 0/0 |
| sponsorship_period | 0 | 0 | 0 | 0 | 0 | 0/0 |
| legal_case | 13 | 13 | 0 | 0 | 13 | 0/0 |

Initial load proof: 6451 domain documents, 6451 `erp-import` audit rows, 8 ERP indexes. Every kind matched the actual SQL aggregate count. The automatic second comparison against the exact in-memory source snapshot had zero new/changed rows.

A separate second `--local-copy --commit` CLI run also completed successfully: **0 new, 0 changed, 6451 unchanged**, with the same exclusions. Its source read window (UTC) was 2026-11-03T18:03:37.7061583+00:00 → 2026-11-03T18:03:37.8407928+00:00. The target had 6451 domain rows before that run.

## Rejections and unavailable data

| Kind | Reason | Count |
|---|---|---:|
| commercial_registration | missing_registration_number | 3 |
| rider | invalid_identity | 14 |
| rider | employee_without_rider_profile | 16 |
| custody_handover | non_handover_event | 2 |

Unavailable: sponsorship-period source table. No historical dates or missing CR numbers were invented.

## Prior live dry-run versus loaded backup

| Kind | Prior live eligible | Loaded backup | Difference |
|---|---:|---:|---:|
| company | 3 | 3 | 0 |
| commercial_registration | 0 | 0 | 0 |
| city | 5 | 5 | 0 |
| rider | 2140 | 2130 | 10 |
| housing | 7 | 7 | 0 |
| housing_residence | 200 | 200 | 0 |
| vehicle | 558 | 558 | 0 |
| vehicle_assignment | 810 | 800 | 10 |
| custody_handover | 879 | 861 | 18 |
| platform_account | 1874 | 1874 | 0 |
| sponsorship_period | 0 | 0 | 0 |
| legal_case | 13 | 13 | 0 |

The live dry-run had 6489 eligible documents; the stable backup loaded 6451. The 38-document difference is 10 riders, 10 vehicle assignments and 18 custody events. There are also nested-source count differences (for example platform assignment/registration history); see ERP-DATA-MAP.md. These later live changes have not been loaded.

Classification in the loaded backup: 394 sponsored delivery riders + 1698 external delivery riders = 2092 delivery riders; 38 additional administrative account/vehicle profiles are retained. The `rider` storage-kind count is 2130 people/profile records. `personRole` / `isDeliveryRider` metadata was explicitly added so staff cannot be mistaken for delivery riders. October custody: handover=184, return=193.

A subsequent role-metadata correction updated the 2130 people/profile documents through the same Store transaction and Audit path. Final direct SQL aggregates verified 6451 domain documents, 8581 `erp-import` audit rows, eight ERP indexes, 38 administrative profiles and 2092 delivery riders. The earlier short-timeout correction attempt left zero role updates and no new audit rows; retry used a bounded 120-second import-target command timeout. No records were deleted. The correction’s automatic same-snapshot comparison returned zero new/changed rows. Its source read window (UTC): 2026-11-03T18:38:35.8082936+00:00 → 2026-11-03T18:38:35.9387705+00:00.

Final independent SELECT-only dry-run after the correction: **0 new, 0 changed, 6451 unchanged**. Read window (UTC): 2026-11-03T18:46:05.3913976+00:00 → 2026-11-03T18:46:05.518964+00:00. All 12 kinds retain the same rejection reasons. This checks the current importer plan against staging without creating a Store or writing data.

Validation: build succeeded with zero warnings/errors; .NET tests 149 passed / 0 failed / 6 optional SQL-backed cases skipped, including nine synthetic importer behavior tests; standalone synthetic HTTP checks 255 passed; release-source check 149 allowlisted files; whitespace and new-file/added-line sensitive-literal checks passed. The approved staging import itself exercised SQL Server transactions, audits and all eight ERP indexes.

No row values, names, identities, phones, IBANs or individual amounts are included. Production SQLite and ERP production data were not written.
