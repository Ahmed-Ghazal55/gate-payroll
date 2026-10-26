# HANDOFF

| Field | Value |
|---|---|
| Current writer | none |
| Branch | main (project: ~/Projects/active/gate-payroll) |
| Updated | 2026-10-26 19:45 by Claude Code |

## Goal now
Accounting/COA fixes from the 2026-10-26 review are LIVE (3b8ac0e, run 40358400860 success, health 0.7.0 dataPreservationVerified).

## Done (latest first)
- Salary payment dialog lists Jahez settlement accounts (labelled "same company payroll only").
- Login: 5 wrong passwords lock the account for 15 min (423), audit `admin.login_locked`.
- toggle-todo checks department scope; campaign admin actions accept SuperAdmin.
- Opening-debt import commit now needs `treasury.opening` as well as `debts.import_opening`.
- Payroll payment (2110) allowed from BANK.*.JAHEZ / MAIN.*.SET of the same company only (Jahez money was trapped).
- Opening balances reject company share (was Dr 5xxx / Cr 3101 = prior-year cost booked as current expense). Template sample fixed.
- No silent BW default: `ResolveSponsor("")` throws; jahez-fee and opening import need explicit company; journal header takes first line's company.
- COA: new 5205 (company-borne advances/custody, was 5109 gov fees), 5206 (company-borne platform debt, was 5203 fines), 5300 group for 5301; 2140/2141 renamed to "due to Jahez / other platforms"; MAIN.TR wording = group; SUP.TR.FEE -> MAIN.TR.FEE.
- Tests: +4 accounting tests (44/44), full dotnet test 90 passed / 6 skipped (SQL Server/browser, CI only).

## Next step (exact)
1. Ghazal: decide the Jahez opening counter-account and who gets `treasury.opening` (see below).
2. Old local main kept as branch `backup/local-main-a6558d7`; old non-git source archived in ~/Projects/_archive/gate-payroll/versions/.
3. Then build in this order: journal reversal (`POST /api/finance/journals/{id}/reverse`, mirrored lines, second approver) + period close lock in `FinancialCore.PostJournal`.
4. Commission release: BANK.X.JAHEZ -> BANK.X capped at earned 4102 minus prior releases (new route in `JahezTreasury.cs`).
5. Jahez netting entry 2140 vs 1240 per company (`JahezTreasury.cs`).
6. Second approver for waivers above a threshold and for debt transfers (`Debts.cs` collect, `DebtTransfers.cs`).
7. Password change/reset + MFA for Admin/Finance; encrypt Firebase serviceAccountJson.
8. Ops: staging slot + manual approval gate in `build-and-deploy.yml`; off-site daily backup.
9. Performance: stop full journal scans in `AccountingPolicy.Balance` (balances table or line index).
10. Bulk payroll approval + guided opening-reconciliation screen.

## Open problems / decisions for Ghazal
- Opening counter-account for Jahez cash (SUP.*.SET, MAIN.*.SET, BANK.*.JAHEZ): still 3101 equity. Options: 2140 (owed to Jahez), 1240, or chosen per row. Accounting policy decision.
- Accountant role no longer commits opening-debt imports alone; give `treasury.opening` to the approver (FinancialManager/Admin).
- Old posted lines on 5109/5203 for company share stay as history; reclassify by journal only after reversal exists.

## How to verify
```bash
cd ~/Projects/active/gate-payroll
dotnet build tests/Engine.Tests -v q
dotnet run --no-build --project tests/Engine.Tests -- --accounting-review   # 44/44 + 52 legacy
dotnet test tests/Engine.Tests --no-build                                    # 90 passed, 6 skipped
curl -s https://payroll.marketingalbawaba.com/health                        # ok=true, 0.7.0
```
