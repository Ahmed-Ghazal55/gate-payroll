# Chefz ERP operations read model

`POST /api/pay/apps/chefz/operations/review` is a read-only query (POST carries structured query selections). Requires pay.view plus company/platform/rider scope. It reads already-pulled `chefz_daily`, `chefz_settlement`, `rider_account_assignment`, platform accounts and riders from the site's Store. It does not connect to ERP, enqueue jobs, seed kinds, save reviews, create invoices, approve, post or load anything. First ERP loading remains behind the existing «حمّل» operator approval.

```json
{"companyId":"BW","period":"2026-10","invoiceId":"synthetic-approved-chefz-invoice","operators":[{"accountId":"synthetic-account","rider":"2000000091","category":"sponsored_private","orders":5}]}
```

companyId resolves to the configured company. invoiceId and operators may be omitted for an incomplete read-only candidate. The response contains `readOnly:true`, `canUseInCycle`, `review`, errors, notes, lineage and aggregate counts. `review` uses exactly the PayCycles invoice/accounts/riders/deductions fields. It can enter normal cycle review only when `canUseInCycle:true` and normal server stage checks pass. No write/commit URL exists for this read model.

## Mapping and evidence

| Already-pulled ERP kind | Projected meaning |
|---|---|
| chefz_daily / chefz.DailyPerformance | Sum explicit totalValidOffers, acceptedOffers, declinedOffers and missedOffers as numeric monthly rider metrics; no invented eligibility threshold. Accepted offers are not delivered orders. |
| chefz_settlement / chefz.WalletSettlements | Actual rider/account/assignment attribution. Use explicit gross plus source deductions once as pre-deduction baseGross. Incentives=0: bonuses/tips already included in source gross. Preserve source gross/VAT/companyAmount/riderAmount and assignment source references as lineage. |
| chargedFrom/chargedTo + assignedDays | Verify inclusive calendar-day counts; union dates across accounts and halves for app-month rider days, never add overlapping rider days twice. Payroll billing caps apply in the existing evaluator. |
| operators query | Explicit delivered orders and one of six pay categories, not guessed from offers or Chefz accountType (freelancer/full-time does not establish sponsorship or vehicle ownership). |
| Approved platform_invoice + lines | Bind sourceLineId and classification from final supplier invoice. Exact account/total/deduction reconciliation and current scopes are rechecked by shared cycle validation. |

Source proof, static code only: ERP `src/LogisticsERP.Domain/Chefz/ChefzOrderSettlementRules.cs` defines GrossEarnings after source deductions and already includes tips/compensation/bonuses; `ChefzRules.cs` also supports wallet-difference gross. No ERP commission percentage, daily charge or expected salary is recalculated here. Payroll pull mapping remains the explicit allowlist in `Payroll.Sync/PullContracts.cs`.

Missing/mismatched dated actual assignments, duplicate daily account/rider/date facts, duplicate settlement source IDs, overlapping account/rider settlement intervals, dates crossing the selected month, inconsistent company+rider financial totals, missing categories/orders or invoice facts produce an incomplete candidate. Deleted sources are excluded and counted. Absent source facts are never synthesized. Unexpected source account/company relations refuse; registered owner is not used.

An ERP/final-invoice difference is displayed as `erp_expected_differs_from_final_invoice` and the candidate requires explicit final-invoice allocation. It does **not** create a dispute, disable posting, mutate the final invoice or block an independently prepared valid payroll cycle (BR31). This projection refuses to label an unbalanced candidate ready. ERP VAT is retained as evidence; final invoice VAT remains authoritative.

Tests use synthetic data, assert the read model leaves source/Audit/invoice/debt/operations records unchanged, and feed a complete candidate through real cycle stages 0–3. No real first load was executed.
