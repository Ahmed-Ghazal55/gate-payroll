# Reconciliation — 2026-11-03

Status: **dry-run only; awaiting Ghazal’s «حمّل»**. Staging has not been loaded. A second post-load idempotency run is pending this approval.

Read window (UTC): 2026-11-03T17:33:48.155657+00:00 → 2026-11-03T17:33:55.9823992+00:00. Consistency: `read_committed_database_rowversion_stable`. All selected tables were read once in primary-key order.

| Kind | ERP source rows | New | Changed | Unchanged | Rejected | Staging before |
|---|---:|---:|---:|---:|---:|---:|
| company | 3 | 3 | 0 | 0 | 0 | 0 |
| commercial_registration | 3 | 0 | 0 | 0 | 3 | 0 |
| city | 5 | 5 | 0 | 0 | 0 | 0 |
| rider | 2170 | 2140 | 0 | 0 | 30 | 0 |
| housing | 7 | 7 | 0 | 0 | 0 | 0 |
| housing_residence | 200 | 200 | 0 | 0 | 0 | 0 |
| vehicle | 558 | 558 | 0 | 0 | 0 | 0 |
| vehicle_assignment | 810 | 810 | 0 | 0 | 0 | 0 |
| custody_handover | 881 | 879 | 0 | 0 | 2 | 0 |
| platform_account | 1874 | 1874 | 0 | 0 | 0 | 0 |
| sponsorship_period | 0 | 0 | 0 | 0 | 0 | 0 |
| legal_case | 13 | 13 | 0 | 0 | 0 | 0 |

Counts refer to source candidate rows, not duplicate destination kinds. Companies and CRs share the Sponsors source. Custody contains only Taken/Returned/SwitchedIn/SwitchedOut event codes; other events are retained in source and excluded with a reason.

| Kind | Reason | Count |
|---|---|---:|
| commercial_registration | missing_registration_number | 3 |
| rider | invalid_identity | 14 |
| rider | employee_without_rider_profile | 16 |
| custody_handover | non_handover_event | 2 |

Unavailable: `sponsorship_period` — source table absent; no historical periods inferred.

## Aggregate observations

- company_reuses_payroll_sponsor_key: 3.
- external_riders: 1708.
- sponsored_riders: 432.
- october_custody_return: 193.
- october_custody_handover: 184.

No record bodies, keys, names, identities, contact data, account values or individual amounts are included in this report.
