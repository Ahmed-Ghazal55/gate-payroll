# R4 pay framework

This is an additive path beside the legacy engine. Engine.cs, historical payroll runs and existing journal bodies are unchanged. No actual app rates/bands are invented; all values come from approved versioned records. Jahez remains a separate settlement cycle (BR24.9).

## Versioned settings

`GET /api/pay/catalog` exposes app/account metadata, scheme types, six category keys, operators and an **unapproved** global-default template. `pay.view`, `pay.manage`, `pay.approve` are separate permissions. Existing users receive current role defaults on cookie validation without rewriting their documents. Shared settings writes/approvals additionally require all-company scope and the app platform scope; a FIN checker differs from the preparer, even for administrators.

| Routes | Contract |
|---|---|
| GET/POST `/api/pay/apps/{app}/terms?period=` | Parent billing: per_order, slabs, flat_target; typed model parameters |
| GET/POST `/api/pay/apps/{app}/schemes?period=` | Named internal schemes; explicit accountingRole=principal/agent and six category cells |
| GET/POST `/api/pay/apps/{app}/eligibility?period=` | Rules owned by exactly one app/month |
| POST `…/terms|schemes|eligibility/{id}/approve` | Independently approve latest draft only |
| POST `/api/pay/apps/{app}/copy?from=&to=` | Copy approved prior-month configurations to drafts; preserve linked eligibility snapshots |
| GET/POST `/api/pay/settings` | Global effective-dated version history / new draft |
| POST `/api/pay/settings/{id}/approve` | Independent global approval |

GET lists always return items/total/page/pageSize (max200). App POST body: `{period,code?,data,baseVersionId?}`. A supplied query period must match the body. Terms/eligibility default to code=default; schemes require a code. Every POST appends a new draft; accepted approval metadata is server-owned. Repeating approval is unchanged. An approved version is immutable, and existing cycles will reference exact version IDs. Copy is idempotent for matching drafts, never overwrites approved target versions, and forks an older referenced eligibility snapshot under its own code when necessary. Copied rule references must be approved before their scheme.

`data` for terms: name, billingModel, parameters. Per order requires pricePerOrder; optional distance pricing requires perKmRate/includedKm/maxBillableKm together. Slabs require metric and bands. Flat target requires targetOrders/monthlyAmount/extraRate/shortfallPerOrder/prorate. A configured companyCostAccount must be an active expense account.

Scheme data: name/type/accountingRole/parameters and optional eligibilitySetId. `parameters.categories` explicitly contains sponsored_company_car, sponsored_company_bike, sponsored_private, external_company_car, external_company_bike, external_private. Each cell is `{enabled:false}` or has chargeCompanyVehicle and its formula values: dailyCommission; companyPercent; monthlyCommission plus prorate; or target_salary bands. percent_monthly_with_vehicle explicitly charges a company vehicle. Bands have min, optional max, base/perOrderRate, optional extraRate/fuelCap/internet and optional multiplierMetric/multiplierDivisor. Missing cells/rates or another app/month's eligibility reference are refused.

Eligibility data: name/rules; each rule has metric/operator/threshold/effect. Operators: gt/gte/eq/neq/lt/lte. Effects: `{type:next_band|per_order|zero}`. No shared app rule sets; referenced versions remain exact snapshots.

Global POST body: `{effectiveFrom,fromDefaults:true}` or `{effectiveFrom,data:{values:…}}`. Values include standardDays/februaryDays/billingCutoff/timeZone/roundingDigits/roundingMode/vehicleRent/accounts. The embedded defaults are configuration data (30/28 days,14:00 Riyadh,1800/800 company rents,2dp AwayFromZero), never auto-approved. No rates live in evaluator branches. Ghazal confirmed that billed assignment days are capped at30/28, including31-day/leap-February months.

R4 keeps source data unchanged during previews. Ghazal clarified that actual approved debt installments use existing Debts.Post and update the balance at approval only; this is an authorized business transaction, not a startup migration.

## Approved rider choices (R4.3)
GET `/api/pay/apps/{app}/assignments?period=` returns scoped approved-choice history. POST `/assignments/import/preview|commit` under the same app path is administrator-only: `{period,sourceType:campaign|custom,formId,fieldId,mapping:{choice:approvedSchemeId}}`. Campaign choice field is workSystem; custom forms require a select/radio field and an iqama identity field. When a custom form has several identities, explicitly supply riderFieldId for the actual operator; the owner is never a substitute.

Preview reads current responses and archived approved history, then selects the latest approved choice strictly before Riyadh00:00 on the month's first day. Unapproved/late rows are counted separately; missing approval evidence, an unmapped latest choice or conflicting simultaneous choices block the batch instead of falling back to an older choice. The mapped scheme must already be approved for the exact app and period. Source reviewedAt/approvedAt and reviewer are required; createdAt is not a substitute.

Confirm with period and the returned rows/fingerprint/confirm. The signature binds the actor, current source responses/forms/riders/settings and imported choices. Repeated imports are unchanged. A newly approved source-response version creates another immutable choice; it never rewrites an earlier choice, response, rider or legacy policy. Company visibility uses the assignment's historical company snapshot, not a later rider transfer. Effective resolution uses the latest imported pre-month approval across mapped source forms and rejects equal-time conflicting schemes. No assignment is inferred from an account owner's identity.
