# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | release/0.17.0 (main 0.16.0 + feat/import-center + feat/r6c-ui + feat/r6c-debts-ops) |
| Live | 0.16.0 on **db77738 (SQL Server)** since 2026-11-07 |
| Updated | 2026-11-07 by Claude |

## Project folder
- One working folder: `~/Projects/active/gate-payroll`. Agents work in branches here (single writer).
- A separate worktree is used only when two agents must write at the same time, and it is removed after merge.

## Done 2026-11-07
- 0.16.0 merged (#28) and deployed.
- **DB move (C0) done.** SQLite snapshot → db77738:
  - merge plan `ready=true`, 833 added, 862 merged, 0 conflicts;
  - verified: `missing=0`, `changed=0`, receipt present;
  - results: rider 2151, submission 224, user 4, company 3.
  - Fix: company legal descriptors (`crType`, `characteristic`, `entityType`, `nameOnCertificateAr`) are ERP-owned in the merge policy.
  - The import needs a long command timeout on the remote server (the first run timed out and rolled back).
  - `ConnectionStrings__DefaultConnection` set by Ghazal. The original SQLite file stays on the site as rollback.
- **Opening debts (D1) loaded** as `pending_approval` / `OpeningBalance`, effectiveFrom 2026-10. See «Debt load» below.
- Review fixes on R6c:
  - the commit kept the target password (captured before `Open`);
  - the ERP-history creditor = the sponsor on the balance date, else the latest ended period.
- 0.17.0: Import Center (catalog + templates + UI with the free importer tab), Keeta invoices (D2), per-app operations import (D3), Chefz read model (D4). CI now runs advances, finance-views, r6b-accounting, r6c-imports and import-center.

## Debt load (counts only)
- Ready 407 rows: 419 opening debts / 953,933.71, 6 rider credits / 2,443.70, 2 inactive debt-only riders.
- Blocked 401 rows:
  - 379 creditor unknown. ERP has no sponsor for them: 231 exist in ERP with an empty SponsorId, 148 not in ERP.
  - 22 other reasons: invalid or duplicate iqama, negative values, creditor conflict.
- Sheet row 809 has no name and a net of 1,350,402.83 (≈ the sheet total). It is a totals row. The real rider net (807 rows) is 1,351,028.83. Blocked, not loaded.
- Ghazal's decision sheet (private): `~/Private/gate-data/review/debts-blocked-creditor-2026-10.xlsx`.
  - A suggested company from ERP platform accounts: EX 62, BW 47, TR 1, conflict 6, none 263.
  - After Ghazal fills «قرار غزال», add a decisions overlay and run a new preview + commit. Only the fixed rows are added (idempotent by `DEBT-SHEET:<row>:<category>`).

## Next
1. Ghazal: fill the decision sheet for the 379 rows.
2. Ghazal: approve CR-003 (`gate-finance-spec/docs/change-requests/CR-003-hungerstation-erp-review.md`), then send it to Omar.
3. Passwords: rotate at the final ERP integration (Ghazal's decision 2026-11-07).
