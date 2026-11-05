# ERP target document kinds

These are additive `records` documents. Import logic is `tools/ErpImport/ImportPlan.cs`; source columns are explicitly allowlisted in `SourceReader.Catalog`. No relational redesign, accounting posting, deletion or payment-model calculation is included.

Shared provenance: `erpSourceId` = ERP primary key, `erpSource` = target kind, `erpStatusCode` = original numeric status, `sourceDeleted` = source soft deletion flag. Stable non-rider keys are `erp:<kind>:<source PK>`. Rider keys remain the existing `iqama` contract. Source-key changes that would create a second destination identity stop the import for review. No personal keys are logged.

| Kind | Natural key / destination key | Fields and ERP source | Relations / nesting |
|---|---|---|---|
| company | Sponsors.Id; reuse the exact existing Payroll sponsor key when registration/employer/unified identity matches unambiguously | nameAr/nameEn ← RegistryNameAr/En; shortNameAr/code ← existing sponsor catalog; commercialReg ← CommercialRegistrationNumber; unifiedNumber ← UnifiedNationalNumber; employerIdentityNumber ← EmployerIdentityNumber; city ← AddressCity; status ← Status/IsDeleted; companyProfileId ← CompanyProfileId; companyProfile ← referenced group profile metadata | Existing Prompt A company kind and sponsorId retained. CompanyProfile is the group/company profile; Sponsors are distinct legal employers. Three existing keys match, without name-based guesses. |
| commercial_registration | Sponsors.Id, stable prefixed key; actual number is required | number ← CommercialRegistrationNumber; company/companyId ← mapped SponsorId; city ← AddressCity; issueDate/expiryDate remain unknown; activeFrom/activeTo ← sponsor activity dates | No source Sponsor rows currently have a CR number. A group-level CompanyProfile registration, if present, is kept as nested profile metadata and requires an explicit legal-entity mapping before creating a normalized CR. Do not substitute an employer identity number or sponsor activity dates for legal registration information. |
| city | GlobalCities.Id | code/nameAr/nameEn/regionAr/regionEn/countryCode ← same source fields; operatingCities[] ← OperatingCities | OperatingCity IDs resolve through GlobalCityId. Housing.CityId already points to GlobalCities; employee/vehicle operating cities point to OperatingCities. |
| rider | Employees.IqamaNo after valid-identity/duplicate validation | name/nameEn ← FullNameAr/En; phone ← PrimaryPhone; iban ← Iban; nationality ← Nationality; engagementType ← EngagementType; isEmployee ← IsEmployee; personRole/isDeliveryRider ← administrative versus delivery role; status ← EmployeeStatus/IsDeleted; hireDate ← HireDate; erpRiderProfileIds[] ← RiderProfiles.Id | Requires a RiderProfile to preserve the person/account/vehicle reference. Administrative profiles remain in the existing storage kind but are excluded from the delivery-rider directory/search/stats/quality views. SponsorId maps to the existing company key, city resolves via OperatingCities. `1 → SponsoredInternal`, `2 → External`. Keep administrative classification separate from sponsorship: 2092 delivery riders plus 38 administrative profiles, not 2130 delivery riders. Financial policy, bank-verification state and other existing target fields are preserved by merge. |
| housing | Housing.Id | code/nameAr/nameEn; cityId; openedDate/closedDate ← OpenedDate/ClosedDate | floors[] ← HousingFloors; rooms[] ← HousingRooms; externalOccupants[] ← HousingExternalOccupants. Nested IDs remain source IDs, room references are explicit. |
| housing_residence | HousingResidencePeriods.Id | housingId via RoomId → HousingRooms.HousingId; roomSourceId ← RoomId; employeeSourceId ← EmployeeId; riderId where mapped; effectiveFrom/effectiveTo ← same fields | Employee relation is retained even when no valid target rider exists. An empty riderId does not invent a rider. End date is nullable; reject reversed ranges. |
| vehicle | Vehicles.Id | assetNumber, serialNumber, plates, vin, chassisNumber, modelYear, vehicleType/fuelType/ownershipType, currentOdometer/currentOperationalStatus, registrationType ← matching fields | companyId ← SponsorId; registeredOwnerCompanyId ← RegisteredOwnerSponsorId; separate registeredOwnerSupplierSourceId; cityId; manufacturer/model source IDs; currentAssignmentId; registrations[] and insurancePolicies[] preserve date/status fields. No financial amounts imported. |
| vehicle_assignment | RiderVehicleAssignments.Id | riderId via RiderProfileId; vehicleId; startedAt/endedAt; odometers; isRealRider; permissionStartsOn/EndsOn; previousAssignmentSourceId | realRiders[] ← RealRiders snapshots; the actual alternate rider is not silently equated with the registered profile. Preserve all history, including October. Reject unmapped required rider/vehicle or reversed dates. |
| custody_handover | RiderVehicleAssignmentEvents.Id | assignmentId; occurredAt ← OccurredAtUtc; eventTypeCode ← EventType; direction | Codes 1/4 = handover, 2/3 = return. Permission-renewal/cancel/correction events remain source events rather than invented custody movements. October overlap/counts are reported separately. |
| platform_account | PlatformRiderAccounts.Id | code/externalAccountId; platformCode from ClientPlatforms; registeredEmployeeSourceId/registeredRiderId; paymentModelCode; startDate/endDate; companyId/cityId | assignments[] ← RiderClientAssignments includes source profile + mapped riderId and date range/payment model/slot/NinjaRiderType; registrations[] ← PlatformAccountRegistrations. Owner and dated operator are distinct. No credentials or usernames are selected. |
| sponsorship_period | Intended EmployeeSponsorshipPeriods.Id | employee/rider, sponsor/company, EffectiveFrom/EffectiveTo, Status when an authoritative source becomes available | Unavailable in current source and current model; do not derive historical dates from the present sponsor. No rows generated. |
| legal_case | HrLegalCases.Id | number, personName/personTypeCode; employeeSourceId/riderProfileSourceId; riderId where mapped; companyId; partyRoleCode; caseDate/caseTime; details | hearings[] ← HrLegalCaseHearings (date/time/status/details/location). Person-only cases are retained without fabricating an employee. Hearing file contents are not downloaded. |

## Indexes and existing-database upgrade

`Store.cs` calls `ErpDataIndexes.Ensure` after the existing schema setup. It adds idempotent indexes for `(kind, erpSourceId)`, companyId/sponsorId, cityId, housingId, riderId/registeredRiderId, vehicleId, assignmentId, and the effectiveFrom/effectiveTo range. SQLite uses JSON expression indexes; SQL Server uses bounded JSON computed columns plus indexes, with compatible session options. Existing record bodies and financial indexes are untouched. Primary record IDs and importer collision checks enforce the natural-key strategy; the source index is not an invitation to create a second ID for one source row.

Dry-run never constructs Store: it uses SELECT-only target reads, even if `records` does not yet exist. Only `--commit` constructs the target Store, runs schema initialization/checkpointing under the private runtime directory, and calls the normal Save/Audit path in a transaction with actor `erp-import`. Its opt-in SQL Server buffer bulk-copies those Save/Audit documents to a temporary table and applies UPDATE/INSERT in the same transaction; application transactions remain immediate. Current target state is rechecked once inside the transaction before saving. A second synthetic commit is tested to produce zero row changes and zero new audit rows. Approved staging verification completed: 6451 domain rows, 8581 import audit rows after the explicit role-metadata correction, eight ERP indexes; a separate second CLI commit run produced zero new/changed rows. After adding explicit role metadata, both its automatic verification and a fresh independent dry-run also produced zero new/changed rows. The stable local backup is older than the earlier live dry-run; see the reconciliation drift table.

## R6a-1 fuel facts

| Kind | Source | Contract |
|---|---|---|
| fuel_card | app.FuelCards | card number/provider/type, company and city; no invented current rider |
| fuel_card_assignment | app.FuelCardRiderAssignments | dated rider/card link; EmployeeId and RiderProfileId must agree |
| fuel_usage_month | app.FuelCardMonthlyUsages | report month, liters, total/before-tax/VAT, transaction count, fuel type; explicit rider relation required |

New source tables are optional only when absent. A present table with missing allowlisted columns refuses the pull. Empty tables remain distinguishable from absent tables. Unmatched/ambiguous riders are rejected with counts, never guessed from the current card owner. Counts-only summaries include numeric totals for accepted documents, separately from rejected counts. Stable keys and merge/idempotency rules above apply.

## R6a-2 custody and vehicle additions
- `custody_equipment`: maintenance.RiderInventoryIssues + explicit issue lines; rider, issue date, item quantities, expected return, returned quantity and total cost. Invalid returns are rejected.
- `custody_sim`: app.RiderPhoneSimAssignments with the actual dated profile link.
- `vehicle_accident`: app.VehicleAccidents, nested VehicleAccidentCases and VehicleAccidentInstallments, preserving source status, repair/claim/refund amounts and installment dates. No attachments or free-form person details selected.
- `vehicle_expense`: maintenance.VehicleExpenses, signed before-tax/VAT/total, source event and reversal linkage, company via vehicle; a missing optional rider is retained as vehicle-only, an invalid explicit rider is rejected.
These are source facts only: no debt or journal is posted by the pull.

## R6a-3 Chefz / The Chefz
- `chefz_account` → chefz.AccountDetails (ERP class ChefzAccountDetails): report ID/type/vehicle/status and registered owner.
- `chefz_daily` → chefz.DailyPerformance (ChefzDailyPerformance): actual assigned rider, date, offers/acceptance, explicit source ID/row.
- `chefz_settlement` → chefz.WalletSettlements (ChefzWalletSettlement): full delivery-fee/VAT/tips/compensation/bonus/deduction/gross/commission/company/rider breakdown and period. No recalculation.
Actual rider and assignment must agree with the account; never substitute its registered owner. Only final tables are pulled; DailyPerformanceSources and ImportedOrderRows are excluded. Monetary totals exclude rates and include only accepted rows.

## R6a-4 Jahez final financial facts
| Kind | Actual final ERP table (class) |
|---|---|
| jahez_earnings_statement | jahez_v2.EarningsStatements (JahezEarningsStatement) |
| jahez_transaction | jahez_v2.PlatformTransactions (JahezTransaction) |
| jahez_ledger_entry | jahez_v2.AccountingEntries (JahezLedgerEntry) |
| jahez_account_fee | jahez_v2.AccountFees (JahezAccountFee) |
| jahez_account_fee_refund | jahez_v2.AccountFeeRefunds (JahezAccountFeeRefund) |
| jahez_transferred_debt | jahez_v2.TransferredDebts (JahezTransferredDebt) |
| jahez_account_session | jahez_v2.AccountSessions (JahezAccountSession) |

HandoverId is a foreign key to AccountSessions; transferred debt resolves through Operations. Preserve signed amounts, source statuses, superseding/reversal links and periods. Account-only financial entries remain explicitly account-only, never attributed to today's owner. Company attribution is the linked account sponsor, not proof of historical legal ownership. Final rows retain ImportRowId as lineage only; no ImportRows, Sources, import files, credentials or CalculationJson are read. These are imported source facts, not new Gate debts/payments/journals.
