# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | feat/r6-erp-pulls, based on release/0.14.2 |
| Updated / weekly usage | 2026-11-05 by Codex; before unavailable → after unavailable |

R6a-1..8 implemented locally: allowlisted ERP facts, FIFO debt-sheet preview/staging, admin-only sync runs/API and disabled three-hour units. No push/merge/deploy, VPS actions, staging writes or changes to other worktrees. ERP access was SELECT-only with the existing read-only account; no secrets or personal rows were printed.
Verified: Debug tests + Release importer build, 0 warnings/errors; .NET 307 passed / 0 failed / 0 skipped on synthetic SQLite + local Docker SQL Server 2022; ERP-sync HTTP 14 passed (also in dotnet test); source boundary 230; inventory 239 routes / 229 API; systemd calendar parsed. Browser/UI checks were not run.
Hostinger VPS list: 0 instances visible, no name/OS. Units not installed/enabled; temporary SQL test container stopped and removed after verification. See docs/data/VPS-SYNC.md and docs/data/SYNC.md.
Latest consistent SELECT-only pull snapshot: 2026-11-05T13:37:22.6964492+00:00 → 2026-11-05T13:38:00.0204745+00:00. REFUSED kind_changes_over_threshold:vehicle (317/558 changed). No large-change override was used; the counts below are preview results, not loaded records.
Private counts-only results: ~/Private/gate-data/sync-logs/r6a-pulls-preview.json and r6a-debt-sheet-preview.json. Missing authoritative sources: sponsorship periods, ADV/payment-request tables; work permits only through actual employee-document types. Never infer their rows.

| Kind | Accepted/source | Rejected | Accepted totals |
|---|---:|---:|---|
| fuel_card | 998/998 | 0 | — |
| fuel_card_assignment | 680/680 | 0 | — |
| fuel_usage_month | 973/986 | 13 | liters=241353.246, amount=526149.42, amountBeforeTax=457521.6, vat=68628.35, transactionCount=11736 |
| custody_equipment | 0/0 | 0 | 0 |
| custody_sim | 0/0 | 0 | — |
| vehicle_accident | 0/0 | 0 | — |
| vehicle_expense | 1395/1395 | 0 | amountBeforeTax=20303.98, vat=0.0, amount=20303.98 |
| chefz_account | 464/464 | 0 | — |
| chefz_daily | 5309/5309 | 0 | totalValidOffers=30065, acceptedOffers=21019, declinedOffers=364, missedOffers=8682 |
| chefz_settlement | 496/496 | 0 | deliveryFees=437461.0, vat=57060.12, tips=4373.0, compensation=3027.0, bonuses=0.0, deductions=13824.25, gross=373976.63, companyAmount=63270.83, riderAmount=310705.8 |
| jahez_account_session | 410/410 | 0 | — |
| jahez_earnings_statement | 0/0 | 0 | 0 |
| jahez_transaction | 18347/18347 | 0 | deliveryPrice=-1601787.436, cashAmount=742765.01, net=-835886.535, driverAdjustment=23135.891 |
| jahez_ledger_entry | 36816/36816 | 0 | amount=84211.796 |
| jahez_account_fee | 97/97 | 0 | amount=19400.0, waivedAmount=0.0 |
| jahez_account_fee_refund | 0/0 | 0 | 0 |
| jahez_transferred_debt | 25/25 | 0 | amount=3675.39, paidAmount=711.19 |
| erp_jahez_operation | 647/647 | 0 | net=-270165.16, commission=87045.0, calculatedCommission=87300.0, remaining=-183120.16 |
| ninja_account | 351/351 | 0 | — |
| ninja_daily | 3835/4135 | 300 | orders=53305, hours=44435.80138882 |
| ninja_daily_deduction | 10/10 | 0 | collectedAmount=0.0, amount=-81.0 |
| employee | 2196/2196 | 0 | — |
| employee_document | 2630/2630 | 0 | — |
| driver_license | 538/538 | 0 | — |
| rider_account_assignment | 1084/1099 | 15 | — |
| erp_jahez_request | 31/31 | 0 | waiverAmount=0.0, debtAdjustment=0, proposedCommission=465.0, proposedNet=-395.01 |
| erp_money_movement | 634/634 | 0 | amount=213701.35, commissionPaid=543.22, dashboardDebtPaid=167.97 |
| erp_treasury_account | 23/23 | 0 | — |
| erp_treasury_posting | 51/51 | 0 | amount=-16079.35 |
| erp_cashbox_handover | 16/16 | 0 | cashboxAmount=14502.4, petroAppAmount=3049.75, bankAmount=-203320.87, movementCount=663 |

Debt XLSX: 808 nonblank-ID rows; 370 valid / 438 flagged; accepted opening debt 806061.61, separate rider credit 2443.70. Flags: 252 company missing, 165 rider missing, 16 negative inputs, 5 invalid IDs. Only Excel row numbers are recorded privately.
Deductions: 393 final payment references; grand total 111952.64 matches detail. Detail/control cash 52093 vs 52458; internet 12954 vs 12839. Per-company/detail/receipt issues are flagged by row number; do not force reconciliation.
Ghazal decided FIFO in debt-column order, net tolerance ≤0.01, zero categories skipped, excess as separate rider_credit, refs DEBT-SHEET:<row>:<category>. Implemented and tested. Mapping stays DRAFT: docs/data/debt-sheet-mapping.v1.json; review unresolved categories/accounts (especially wallet 112506 vs target HR), exact company aliases and effective month. Opening imports remain pending_approval; no posting counterpart, active deductions or cash/journal is invented.
Next: Claude reviews the eight local task commits. Ghazal resolves the flagged source/sheet rows and mapping decisions, reviews vehicle drift, then gives fresh «حمّل» for the approved manual load. Do not load a refused preview or enable scheduling first. No server purchase/creation is authorized.

## Verify
`dotnet build tests/Engine.Tests/Engine.Tests.csproj --no-restore -p:SkipFrontendBuild=true -m:1`
`dotnet test tests/Engine.Tests/Engine.Tests.csproj --no-build --no-restore -m:1` (use only an isolated loopback PAYROLL_TEST_SQL_CONNECTION for SQL tests).
`node tests/erp-sync.mjs`; `python3 scripts/release_source.py --check`; `git diff --check`. CLI/debt dry-run examples: docs/data/SYNC.md and DEBT-SHEET.md. Learning log appended.
