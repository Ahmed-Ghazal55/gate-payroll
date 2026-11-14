# Six-stage pay pipeline (W1)

Binding: `gate-finance-spec/BUSINESS_RULES.md` §40 (+ §22.9, §24.4–5, §29, §32, §34–35, §39). Open decisions: `gate-finance-spec/docs/proposals/OPEN-DECISIONS-2026-11-14.md` — each is a **setting whose default is the ✅ option**, never hard-coded.

## Problem

Invoice classification used `company_level` / `account_level` / `rider_level`. Account-level silent splits and stage-8 mixing this month's items with old debts can mis-charge riders. §40 replaces that with an explicit six-stage path before bank file.

## Goals

1. Per-row **bearer allocations** (company | rider+) summing to the row (halala).
2. Weighted rider share of account gross (orders default).
3. Commission after platform penalties; salary riders show operational costs as **expense lines**, not deductions (personal still deducted; fuel overage default ✅ on rider).
4. Stage-4 sources frozen at capture; stage 8 = **old debts only**.
5. Shared riders held then consolidated once per month.
6. Negative net → debt `112507` for every app; bank from stage-6 remainder + payout journal.

## Non-Goals

- W2 HungerStation import UI; W3 screens (Antigravity).
- Merging PR #39 / deploy / version bump.
- Writing ERP or production payroll DB.

## Stage → cycle code map

Existing cycle `stage` integers **0–8** stay. Pipeline stages 1–6 are **what runs** inside them:

| Pipeline (§40) | Cycle `stage` | Functions / notes |
|---|---|---|
| 1 Invoice + bearer | 0→1 (after capture), classify on invoice | `InvoiceDeductionClassification`, invoice View/Classify; cycle `ReviewInvoices` |
| 2 Rider allocation | 2–3 | `ValidateAccounts` / `ValidateRiders`; `PayReviewCategory.Propose` weights |
| 3 Commission / salary | 4 | `BuildLines` / `PayEvaluator` (commission after rider penalties) |
| 4 Deductions before net | 5 | `BuildDeductions` (month items); fuel/Mudad/Iqama from freeze or approved lists |
| 5 Shared rider | 5–6 | hold tag + consolidation record; `Merge` |
| 6 Old-debt after net | 6→7→8 | `Approve`; `DebtDeductions` old only; bank from remainder |
| (post) Bank / payout | 8+ actions | `BankPayrollFiles` + payout journal |

## Data model changes

- Invoice deduction row: `allocations: [{ bearer: "company"|"rider", riderId?, amount }]`. Drop `account_level` as input. Migrate old levels (see below).
- Per-app setting: allocation weight `orders|days|periods|compliance` (default `orders`).
- Pipeline settings bag for open-decision defaults (Mudad list import, Iqama HR request, housing scope, fuel overage on rider, HS wallet `112506`, …).
- Capture kind `fuel_usage` (+ LiveOperationsQuery allowlist).
- Consolidation record: rider + month → per-app stage-4 nets, release reason.
- ConfigLists: FuelOverage account **112511** (was 112510).

## Migration of existing rows

| Old `level` | New allocations |
|---|---|
| `company_level` | one `{bearer:company, amount}` |
| `rider_level` | one `{bearer:rider, riderId, amount}` |
| `account_level` | single operator → that rider; multi-rider → `needs_review` flag (report counts) |

Read-only `GET .../multi-rider-deductions` (counts/amounts, no names) for Ninja October re-class check.

## Cycle shape

Keep **one cycle per company + month** (`PayCycles` uniqueness). Late invoices join via **consolidation**, never by editing a sealed cycle.

## Risks

| Risk | Mitigation |
|---|---|
| Silent empty live reads | Kind-aware readers (L2 pattern); never `[]` for unknown kind |
| Double rent / fuel on shared riders | Charge once in consolidation |
| Bank ≠ remainder | Tests pin stage-6 remainder; exclude unconfirmed |
| Open decisions change | Settings defaults ✅; flip without code rewrite |

## Alternatives

Hard-code open decisions → rejected (prompt). Multiple cycles per app now → deferred; consolidation covers late invoices.
