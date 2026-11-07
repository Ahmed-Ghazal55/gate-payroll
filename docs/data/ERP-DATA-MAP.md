# ERP data map — 2026-11-03

Source loaded into payroll database: local READ_ONLY `erp_copy`, restored from the approved 2026-11-03 backup in SQL Server 2025 (major 17). Backup header: started 2026-11-03 11:05:49, finished 11:05:55 (server-clock timezone is not recorded). Both local and prior live schema expose 224 tables. Source code: gate-erp sync `d343d63`, verified clone provenance `4eab10c`. Runtime data is newer evidence than historical migration scripts. No source/production writes. The live commit attempt was safely refused after a changing read; snapshot isolation was already disabled and was not altered. The prompt explicitly permits a local backup fallback in this case. The original backup remained unchanged; docker cp bytes were verified by SHA-256. Local volume and SA env remain only beneath Private/gate-data/mssql.

Source rows were read once per allowlisted table, ordered by primary key. Read window: 2026-11-03T17:58:31.0969517+00:00 to 2026-11-03T17:58:31.2474698+00:00 (UTC); approximately 0.15 seconds. The local database was READ_ONLY before the read, so the loaded dataset is stable. Under Ghazal’s prior explicit load approval, the first transaction saved 6451 domain documents and 6451 audit rows. Earlier live dry-runs used a database-rowversion stability check; a later unstable read was refused before any payroll database writes.

## Classification rules

- `src/LogisticsERP.Domain/Entities/Workforce/Employee.cs:26` defines `IsEmployee`; it means administrative staff, not sponsorship. `WorkforceService.cs:180` records Administrative/Rider roles; `:347` excludes staff from the dedicated rider list. `Gate-Front-master/src/app/admin/employees/[employeeId]/page.tsx:680` requires both a rider profile and `!isEmployee` for rider UI.
- A profile is not proof of delivery work: `src/LogisticsERP.Infrastructure/Hr/WorkforceService.cs:128-158` allows opening vehicle/account profiles for administrative employees. `Gate-Front-master/src/components/employees/EmployeePlatformAccounts.tsx:569` explicitly explains that staff keep `isEmployee: true` while owning/using platform accounts and remain outside the dedicated rider list.
- Source aggregates: 394 sponsored delivery-rider profiles; 1712 external delivery-rider profiles (14 invalid identities excluded, leaving 1698); 38 administrative profiles; 15 external employees and one administrator have no profile. Loaded people/profile records therefore total 2130: **2092 delivery riders and 38 administrative profiles**. They remain separate roles; administrative references are retained rather than deleted.
- `Enums/WorkforceEnums.cs:16`: relationship code `1=SponsoredInternal`, `2=OutsideRider` (target `External`) is independent of the staff/delivery role. Current SponsorId does not override either field. Source `IsEmployee` remains intact; `personRole` and `isDeliveryRider` make it explicit. The rider directory/search/dashboard/data-quality views exclude only imported administrative profiles; legacy records keep their behavior.
- Platform account ownership is `PlatformRiderAccounts.RegisteredEmployeeId`; dated operators come from `RiderClientAssignments.RiderProfileId`. Ownership does not identify the working rider or imply payroll eligibility. PaymentModel and NinjaRiderType remain codes, not assumed payroll policies.
- `EmployeeSponsorshipPeriods` is absent in the live/restored schema and current model. Historical `database/scripts/application.sql:3585` creates it and `:6344` drops it. No historical dates are inferred from HireDate/current SponsorId.
- Sponsors match existing Payroll company keys only through unambiguous registration/employer/unified identities. All three keys match. All Sponsor CR numbers are missing: no CR rows or issue/expiry dates are invented; sponsor activity dates are not CR dates.

## Schema and source evidence

Counts marked `exact` came from the single-pass reader; other counts are `sys.partitions` metadata estimates. PK/unique-index key membership and FKs below come from the restored SQL catalog, compared with the prior live catalog. A unique label identifies an index component, not unconditional uniqueness of that column. Filter predicates were not exported; in particular, ClientConfigurations.cs:122-135 limits some RiderClientAssignments indexes to current, non-deleted rows. The importer preserves dated history rather than assuming one account/operator for all time. Soft deletion is preserved, not physically applied.

## Riders / employees

- Entity: `src/LogisticsERP.Domain/Entities/Workforce/Employee.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Workforce/RiderProfile.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Workforce/EmployeeWorkHistory.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/EmployeesController.cs:10` [Route("api/employees")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:21` [HttpGet("{employeeId:guid}")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:29` [HttpGet("{employeeId:guid}/vehicle-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:37` [HttpPut("{employeeId:guid}/vehicle-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:45` [HttpPut("{employeeId:guid}/rider-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:53` [HttpPost], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:63` [HttpPut("{employeeId:guid}")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:71` [HttpPost("{employeeId:guid}/status-transitions")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:79` [HttpPost("{employeeId:guid}/reactivate")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:87` [HttpPost("{employeeId:guid}/role-transitions")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:95` [HttpGet("{employeeId:guid}/work-history")].
- Endpoints: `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:10` [Route("api/payroll-employees")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:23` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:31` [HttpPost], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:43` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:54` [HttpDelete("{id:guid}")].
- Page: `Gate-Front-master/src/app/admin/employees/page.tsx:126`.
- Page: `Gate-Front-master/src/app/admin/employees/[employeeId]/page.tsx:460`.

### app.Employees — 2160 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_Employees #1 |
| FullNameAr | nvarchar(200) | no |  |
| FullNameEn | nvarchar(200) | yes |  |
| WorkingForMeAs | nvarchar(200) | yes |  |
| PrimaryPhone | nvarchar(32) | yes |  |
| Status | int | no |  |
| MaritalStatus | int | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| HireDate | date | yes |  |
| AlternateContactName | nvarchar(200) | yes |  |
| AlternateContactPhone | nvarchar(32) | yes |  |
| BirthDate | date | yes |  |
| ContractEndDate | date | yes |  |
| ContractStartDate | date | yes |  |
| Email | nvarchar(320) | yes |  |
| EmergencyContactName | nvarchar(200) | yes |  |
| EmergencyContactPhone | nvarchar(32) | yes |  |
| EmergencyContactRelationship | nvarchar(100) | yes |  |
| EngagementType | int | no |  |
| Gender | int | yes |  |
| IqamaNo | varchar(10) | yes | unique-index IX_Employees_IqamaNo #1 |
| IsEmployee | bit | no |  |
| Nationality | nvarchar(100) | yes |  |
| OperatingCityId | uniqueidentifier | yes | FK → app.OperatingCities.Id |
| OperationalWorkTypeId | uniqueidentifier | yes | FK → app.OperationalWorkTypes.Id |
| ProbationEndDate | date | yes |  |
| ProfilePhotoDocumentId | uniqueidentifier | yes | FK → app.EmployeeDocuments.Id |
| ResidencyProfession | nvarchar(200) | yes |  |
| SecondaryPhone | nvarchar(32) | yes |  |
| SponsorId | uniqueidentifier | yes | FK → app.Sponsors.Id |
| StatusReason | nvarchar(500) | yes |  |
| TerminationDate | date | yes |  |
| Iban | varchar(34) | yes |  |
| AddressAdditionalNumber | nvarchar(32) | yes |  |
| AddressBuildingNumber | nvarchar(32) | yes |  |
| AddressCity | nvarchar(200) | yes |  |
| AddressDistrict | nvarchar(200) | yes |  |
| AddressPostalCode | nvarchar(32) | yes |  |
| AddressStreet | nvarchar(200) | yes |  |

### app.RiderProfiles — 2144 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | unique-index AK_RiderProfiles_Id_EmployeeId #1; PK PK_RiderProfiles #1 |
| EmployeeId | uniqueidentifier | no | unique-index AK_RiderProfiles_Id_EmployeeId #2; unique-index IX_RiderProfiles_EmployeeId #1; FK → app.Employees.Id |
| OperationalNotes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| TShirtSize | int | yes |  |

### app.EmployeeWorkHistory — 2677 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_EmployeeWorkHistory #1 |
| EmployeeId | uniqueidentifier | no | FK → app.Employees.Id |
| ChangeType | int | no |  |
| OldValue | nvarchar(1000) | yes |  |
| NewValue | nvarchar(1000) | yes |  |
| EffectiveDate | date | no |  |
| Reason | nvarchar(1000) | no |  |
| ChangedByUserId | uniqueidentifier | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |

### app.PayrollEmployees — 9 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_PayrollEmployees #1 |
| Number | int | no | unique-index IX_PayrollEmployees_Number #1 |
| Name | nvarchar(200) | no |  |
| NationalId | varchar(10) | no | unique-index IX_PayrollEmployees_NationalId #1 |
| Country | nvarchar(100) | no |  |
| JoiningDate | date | no |  |
| PersonalIban | varchar(24) | no | unique-index IX_PayrollEmployees_PersonalIban #1 |
| Salary | decimal(18,2) | no |  |
| Status | nvarchar(100) | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| SponsorId | uniqueidentifier | no | FK → app.Sponsors.Id |


## Companies / CR

- Entity: `src/LogisticsERP.Domain/Entities/Workforce/Sponsor.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Platform/CompanyProfile.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/SponsorsController.cs:10` [Route("api/sponsors")], `src/LogisticsERP.Api/Controllers/SponsorsController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/SponsorsController.cs:21` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/SponsorsController.cs:29` [HttpPost], `src/LogisticsERP.Api/Controllers/SponsorsController.cs:33` [HttpPut("{id:guid}")].
- Page: `Gate-Front-master/src/app/admin/employees/sponsors/page.tsx:3`.

### app.Sponsors — 3 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_Sponsors #1 |
| CompanyProfileId | uniqueidentifier | no | FK → platform.CompanyProfile.Id |
| EmployerIdentityNumber | nvarchar(100) | no | unique-index IX_Sponsors_EmployerIdentityNumber #1 |
| RegistryNameAr | nvarchar(200) | no |  |
| RegistryNameEn | nvarchar(200) | yes |  |
| CommercialRegistrationNumber | nvarchar(100) | yes | unique-index IX_Sponsors_CommercialRegistrationNumber #1 |
| UnifiedNationalNumber | nvarchar(100) | yes |  |
| SponsorType | int | no |  |
| Status | int | no |  |
| ActiveFrom | date | yes |  |
| ActiveTo | date | yes |  |
| ContactName | nvarchar(200) | yes |  |
| ContactPhone | nvarchar(32) | yes |  |
| ContactEmail | nvarchar(320) | yes |  |
| AddressBuildingNumber | nvarchar(32) | yes |  |
| AddressStreet | nvarchar(200) | yes |  |
| AddressDistrict | nvarchar(200) | yes |  |
| AddressCity | nvarchar(200) | yes |  |
| AddressPostalCode | nvarchar(32) | yes |  |
| AddressAdditionalNumber | nvarchar(32) | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### platform.CompanyProfile — 1 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_CompanyProfile #1 |
| Code | nvarchar(32) | no | unique-index IX_CompanyProfile_Code #1 |
| LegalNameAr | nvarchar(200) | no |  |
| LegalNameEn | nvarchar(200) | no |  |
| DisplayNameAr | nvarchar(200) | no |  |
| DisplayNameEn | nvarchar(200) | no |  |
| CommercialRegistrationNumber | nvarchar(100) | yes |  |
| UnifiedNationalNumber | nvarchar(100) | yes |  |
| VatNumber | nvarchar(100) | yes |  |
| ContactPhone | nvarchar(32) | no |  |
| ContactEmail | nvarchar(320) | yes |  |
| AddressBuildingNumber | nvarchar(32) | yes |  |
| AddressStreet | nvarchar(200) | yes |  |
| AddressDistrict | nvarchar(200) | yes |  |
| AddressCity | nvarchar(200) | yes |  |
| AddressPostalCode | nvarchar(32) | yes |  |
| AddressAdditionalNumber | nvarchar(32) | yes |  |
| LogoAssetKey | nvarchar(500) | yes |  |
| DefaultLocale | nvarchar(10) | no |  |
| TimeZoneId | nvarchar(100) | no |  |
| NextEmployeeSequence | bigint | no |  |
| Status | int | no |  |
| SuspensionReason | nvarchar(500) | yes |  |
| SuspendedAtUtc | datetimeoffset | yes |  |
| SuspendedByUserId | uniqueidentifier | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |


## Cities

- Entity: `src/LogisticsERP.Domain/Entities/Platform/GlobalCity.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Platform/OperatingCity.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:10` [Route("api/hr-catalogs")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:13` [HttpGet("global-cities")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:17` [HttpPost("global-cities")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:22` [HttpPut("global-cities/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:27` [HttpGet("job-titles")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:31` [HttpPost("job-titles")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:35` [HttpPut("job-titles/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:39` [HttpPut("job-titles/{id:guid}/operational-work-types")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:47` [HttpGet("residency-professions")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:51` [HttpPost("residency-professions")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:55` [HttpPut("residency-professions/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:59` [HttpGet("operational-work-types")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:63` [HttpPost("operational-work-types")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:68` [HttpPut("operational-work-types/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:73` [HttpGet("driver-license-categories")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:77` [HttpPost("driver-license-categories")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:82` [HttpPut("driver-license-categories/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:87` [HttpGet("document-types")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:91` [HttpPost("document-types")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:96` [HttpPut("document-types/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:101` [HttpGet("document-requirements")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:106` [HttpPost("document-requirements")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:111` [HttpPut("document-requirements/{id:guid}")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:116` [HttpGet("operating-cities")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:120` [HttpPost("operating-cities")], `src/LogisticsERP.Api/Controllers/HrCatalogsController.cs:125` [HttpPut("operating-cities/{id:guid}")].
- Page: `Gate-Front-master/src/app/admin/employees/cities/page.tsx:3`.

### platform.GlobalCities — 5 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_GlobalCities #1 |
| Code | nvarchar(32) | no | unique-index IX_GlobalCities_Code #1 |
| NameAr | nvarchar(200) | no |  |
| NameEn | nvarchar(200) | no |  |
| RegionAr | nvarchar(200) | no |  |
| RegionEn | nvarchar(200) | no |  |
| CountryCode | nchar(2) | no |  |
| Latitude | decimal(9,6) | yes |  |
| Longitude | decimal(9,6) | yes |  |
| DisplayOrder | int | no |  |
| Status | int | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.OperatingCities — 5 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_OperatingCities #1 |
| GlobalCityId | uniqueidentifier | no | unique-index IX_OperatingCities_GlobalCityId #1; FK → platform.GlobalCities.Id |
| EnabledFrom | date | no |  |
| DisabledAt | date | yes |  |
| Status | int | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |


## Housing / rooms / residence

- Entity: `src/LogisticsERP.Domain/Entities/Housing/Housing.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Housing/HousingRoom.cs:5`.
- Entity: `src/LogisticsERP.Domain/Entities/Housing/HousingResidencePeriod.cs:5`.
- Endpoints: `src/LogisticsERP.Api/Controllers/HousingController.cs:10` [Route("api/housing")], `src/LogisticsERP.Api/Controllers/HousingController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/HousingController.cs:21` [HttpGet("stay-report")], `src/LogisticsERP.Api/Controllers/HousingController.cs:36` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:44` [HttpPost], `src/LogisticsERP.Api/Controllers/HousingController.cs:48` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:60` [HttpGet("{id:guid}/rooms")], `src/LogisticsERP.Api/Controllers/HousingController.cs:68` [HttpPost("{id:guid}/rooms")], `src/LogisticsERP.Api/Controllers/HousingController.cs:81` [HttpGet("{id:guid}/floors")], `src/LogisticsERP.Api/Controllers/HousingController.cs:89` [HttpPost("{id:guid}/floors")], `src/LogisticsERP.Api/Controllers/HousingController.cs:97` [HttpPut("{id:guid}/floors/{floorId:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:105` [HttpDelete("floors/{floorId:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:113` [HttpPost("floors/{floorId:guid}/equipment")], `src/LogisticsERP.Api/Controllers/HousingController.cs:121` [HttpPut("floors/{floorId:guid}/equipment/{equipmentId:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:129` [HttpDelete("equipment/{equipmentId:guid}")], `src/LogisticsERP.Api/Controllers/HousingController.cs:137` [HttpGet("{id:guid}/residents")], `src/LogisticsERP.Api/Controllers/HousingController.cs:145` [HttpPost("{id:guid}/residents")], `src/LogisticsERP.Api/Controllers/HousingController.cs:153` [HttpPost("residence-periods/{periodId:guid}/close")], `src/LogisticsERP.Api/Controllers/HousingController.cs:161` [HttpGet("{id:guid}/supervisors")], `src/LogisticsERP.Api/Controllers/HousingController.cs:169` [HttpPost("{id:guid}/supervisors")], `src/LogisticsERP.Api/Controllers/HousingController.cs:177` [HttpPost("supervisor-periods/{periodId:guid}/close")].
- Page: `Gate-Front-master/src/app/admin/housing/page.tsx:95`.
- Page: `Gate-Front-master/src/app/admin/housing/[housingId]/page.tsx:82`.

### app.Housing — 7 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_Housing #1 |
| Code | nvarchar(32) | no | unique-index IX_Housing_Code #1 |
| NameAr | nvarchar(200) | no |  |
| NameEn | nvarchar(200) | no |  |
| CityId | uniqueidentifier | no | FK → platform.GlobalCities.Id |
| AddressBuildingNumber | nvarchar(32) | yes |  |
| AddressStreet | nvarchar(200) | yes |  |
| AddressDistrict | nvarchar(200) | yes |  |
| AddressCity | nvarchar(200) | yes |  |
| AddressPostalCode | nvarchar(32) | yes |  |
| AddressAdditionalNumber | nvarchar(32) | yes |  |
| Latitude | decimal(9,6) | yes |  |
| Longitude | decimal(9,6) | yes |  |
| ContactPhone | nvarchar(32) | yes |  |
| OpenedDate | date | yes |  |
| ClosedDate | date | yes |  |
| Status | int | no |  |
| StatusReason | nvarchar(500) | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.HousingFloors — 12 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HousingFloors #1 |
| HousingId | uniqueidentifier | no | unique-index IX_HousingFloors_HousingId_Name #1; FK → app.Housing.Id |
| Name | nvarchar(100) | no | unique-index IX_HousingFloors_HousingId_Name #2 |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.HousingRooms — 42 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HousingRooms #1 |
| HousingId | uniqueidentifier | no | FK → app.Housing.Id |
| Name | nvarchar(100) | no | unique-index IX_HousingRooms_FloorId_Name #2 |
| Capacity | int | no |  |
| CurrentOccupancy | int | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| FloorId | uniqueidentifier | no | unique-index IX_HousingRooms_FloorId_Name #1; FK → app.HousingFloors.Id |
| Notes | nvarchar(2000) | yes |  |

### app.HousingResidencePeriods — 200 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HousingResidencePeriods #1 |
| EmployeeId | uniqueidentifier | no | unique-index IX_HousingResidencePeriods_EmployeeId #1; FK → app.Employees.Id |
| EffectiveFrom | date | no |  |
| EffectiveTo | date | yes |  |
| MoveInReason | nvarchar(1000) | yes |  |
| MoveOutReason | nvarchar(1000) | yes |  |
| SourceReference | nvarchar(200) | yes |  |
| DestinationReference | nvarchar(200) | yes |  |
| CapacityOverrideUsed | bit | no |  |
| CapacityOverrideReason | nvarchar(1000) | yes |  |
| AssignedByUserId | uniqueidentifier | no |  |
| ClosedByUserId | uniqueidentifier | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| ClosedAtUtc | datetimeoffset | yes |  |
| RowVersion | timestamp | no |  |
| RoomId | uniqueidentifier | no | FK → app.HousingRooms.Id |

### app.HousingExternalOccupants — 22 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HousingExternalOccupants #1 |
| RoomId | uniqueidentifier | no | FK → app.HousingRooms.Id |
| Name | nvarchar(200) | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.HousingPendingOccupants — 8 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HousingPendingOccupants #1 |
| RoomId | uniqueidentifier | no | FK → app.HousingRooms.Id |
| IqamaNo | nvarchar(20) | no | unique-index IX_HousingPendingOccupants_IqamaNo #1 |
| Name | nvarchar(200) | no |  |
| SourceRow | int | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |


## Vehicles / registrations / insurance

- Entity: `src/LogisticsERP.Domain/Entities/Fleet/VehicleCatalog.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Fleet/VehicleCompliance.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/VehiclesController.cs:14` [Route("api/vehicles")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:17` [HttpGet("{id:guid}/complete-history")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:29` [HttpGet], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:37` [HttpGet("lookup")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:45` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:53` [HttpPost], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:61` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:85` [HttpPost("{id:guid}/{statusAction:regex(^(stolen|recover|out-of-service|restore|decommission|under-movement-responsibility)$)}")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:93` [HttpGet("{id:guid}/status-history")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:101` [HttpGet("{id:guid}/odometer")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:109` [HttpPost("{id:guid}/odometer")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:117` [HttpGet("{id:guid}/rider-timeline")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:125` [HttpGet("{id:guid}/readiness")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:133` [HttpPost("{id:guid}/identity-corrections")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:141` [HttpGet("{id:guid}/identity-corrections")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:149` [HttpPost("{id:guid}/registration-transitions/private-to-public")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:171` [HttpGet("{id:guid}/registration-transitions")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:183` [Route("api/vehicles/{vehicleId:guid}/files")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:187` [HttpGet], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:195` [HttpPut("{kind}")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:205` [HttpGet("{attachmentId:guid}/versions")], `src/LogisticsERP.Api/Controllers/VehiclesController.cs:213` [HttpGet("{attachmentId:guid}/download")].
- Endpoints: `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:10` [Route("api/vehicles/{vehicleId:guid}")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:13` [HttpGet("{type:regex(^(registrations|insurance-policies|inspections|operation-cards)$)}")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:21` [HttpPost("registrations")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:29` [HttpPost("insurance-policies")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:37` [HttpPost("inspections")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:45` [HttpPost("operation-cards")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:55` [Route("api/vehicle-compliance")], `src/LogisticsERP.Api/Controllers/VehicleComplianceController.cs:58` [HttpGet("due")].
- Page: `Gate-Front-master/src/app/admin/fleet/vehicles/page.tsx:115`.
- Page: `Gate-Front-master/src/app/admin/fleet/vehicles/[id]/page.tsx:75`.

### app.Vehicles — 558 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_Vehicles #1 |
| AssetNumber | nvarchar(64) | no |  |
| NormalizedAssetNumber | nvarchar(64) | no | unique-index IX_Vehicles_NormalizedAssetNumber #1 |
| PlateNumberAr | nvarchar(32) | yes |  |
| NormalizedPlateNumberAr | nvarchar(32) | yes | unique-index IX_Vehicles_NormalizedPlateNumberAr #1 |
| PlateNumberEn | nvarchar(32) | yes |  |
| NormalizedPlateNumberEn | nvarchar(32) | yes | unique-index IX_Vehicles_NormalizedPlateNumberEn #1 |
| PlateLettersAr | nvarchar(8) | yes |  |
| PlateLettersEn | nvarchar(8) | yes |  |
| PlateDigits | nvarchar(8) | yes |  |
| Vin | nvarchar(64) | yes | unique-index IX_Vehicles_Vin #1 |
| ChassisNumber | nvarchar(100) | yes |  |
| EngineNumber | nvarchar(100) | yes |  |
| VehicleManufacturerId | uniqueidentifier | no | FK → app.VehicleManufacturers.Id; FK → app.VehicleModels.VehicleManufacturerId |
| VehicleModelId | uniqueidentifier | no | FK → app.VehicleModels.Id |
| ModelYear | int | yes |  |
| VehicleType | int | no |  |
| FuelType | int | no |  |
| TransmissionType | int | no |  |
| ColorAr | nvarchar(100) | yes |  |
| ColorEn | nvarchar(100) | yes |  |
| OwnershipType | int | no |  |
| OwnerName | nvarchar(200) | yes |  |
| AcquisitionDate | date | yes |  |
| LeaseReference | nvarchar(200) | yes |  |
| CurrentOdometer | bigint | no |  |
| LastOdometerAtUtc | datetimeoffset | yes |  |
| CurrentOperationalStatus | int | no |  |
| CurrentAssignmentId | uniqueidentifier | yes |  |
| DecommissionedAtUtc | datetimeoffset | yes |  |
| DecommissionReason | nvarchar(1000) | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| OperatingCityId | uniqueidentifier | yes | FK → app.OperatingCities.Id |
| SponsorId | uniqueidentifier | yes | FK → app.Sponsors.Id |
| NormalizedChassisNumber | nvarchar(100) | yes | unique-index IX_Vehicles_NormalizedChassisNumber #1 |
| NormalizedSerialNumber | nvarchar(100) | yes | unique-index IX_Vehicles_NormalizedSerialNumber #1 |
| PurchasedFromSupplierId | uniqueidentifier | yes | FK → app.VehicleSuppliers.Id |
| RegistrationType | int | yes |  |
| SerialNumber | nvarchar(100) | yes |  |
| TrackedDistanceKm | decimal(18,2) | no |  |
| RegisteredOwnerSupplierId | uniqueidentifier | yes | FK → app.VehicleSuppliers.Id |
| RegisteredOwnerSponsorId | uniqueidentifier | yes | FK → app.Sponsors.Id |

### app.VehicleRegistrations — 2 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_VehicleRegistrations #1 |
| VehicleId | uniqueidentifier | no | unique-index IX_VehicleRegistrations_VehicleId #1; FK → app.Vehicles.Id |
| RegistrationNumber | nvarchar(150) | no |  |
| IssuingAuthority | nvarchar(200) | no |  |
| IssueDate | date | no |  |
| ExpiryDate | date | no |  |
| Status | int | no |  |
| IsCurrent | bit | no |  |
| PreviousRecordId | uniqueidentifier | yes | FK → app.VehicleRegistrations.Id |
| ProofAttachmentId | uniqueidentifier | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.VehicleInsurancePolicies — 0 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_VehicleInsurancePolicies #1 |
| VehicleId | uniqueidentifier | no | unique-index IX_VehicleInsurancePolicies_VehicleId #1; unique-index IX_VehicleInsurancePolicies_VehicleId_PolicyNumber #1; FK → app.Vehicles.Id |
| ProviderName | nvarchar(200) | no |  |
| PolicyNumber | nvarchar(150) | no | unique-index IX_VehicleInsurancePolicies_VehicleId_PolicyNumber #2 |
| CoverageType | nvarchar(200) | yes |  |
| EffectiveFrom | date | no |  |
| ExpiryDate | date | no |  |
| ClaimReference | nvarchar(200) | yes |  |
| ClaimContact | nvarchar(200) | yes |  |
| Status | int | no |  |
| IsCurrent | bit | no |  |
| PreviousRecordId | uniqueidentifier | yes | FK → app.VehicleInsurancePolicies.Id |
| ProofAttachmentId | uniqueidentifier | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.VehicleOperationCards — 304 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_VehicleOperationCards #1 |
| VehicleId | uniqueidentifier | no | unique-index IX_VehicleOperationCards_VehicleId #1; FK → app.Vehicles.Id |
| CardNumber | nvarchar(150) | no |  |
| IssuingAuthority | nvarchar(200) | no |  |
| IssueDate | date | no |  |
| ExpiryDate | date | no |  |
| Status | int | no |  |
| IsCurrent | bit | no |  |
| PreviousRecordId | uniqueidentifier | yes | FK → app.VehicleOperationCards.Id |
| ProofAttachmentId | uniqueidentifier | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.VehicleOperationalStatusPeriods — 2768 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_VehicleOperationalStatusPeriods #1 |
| VehicleId | uniqueidentifier | no | unique-index IX_VehicleOperationalStatusPeriods_VehicleId #1; FK → app.Vehicles.Id |
| Status | int | no |  |
| EffectiveFromUtc | datetimeoffset | no |  |
| EffectiveToUtc | datetimeoffset | yes |  |
| ReasonCode | nvarchar(100) | yes |  |
| Reason | nvarchar(1000) | no |  |
| SourceType | int | no |  |
| SourceEntityId | uniqueidentifier | yes |  |
| ChangedByUserId | uniqueidentifier | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |


## Vehicle assignments / custody

- Entity: `src/LogisticsERP.Domain/Entities/Fleet/VehicleOperations.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:13` [Route("api/vehicle-assignments")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:25` [HttpGet], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:37` [HttpGet("{assignmentId:guid}")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:45` [HttpPost("{assignmentId:guid}/promissory-files")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:58` [HttpPost("take")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:71` [HttpPost("return")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:83` [HttpPost("return-with-condition-report")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:98` [HttpPost("switch")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:116` [HttpPost("{assignmentId:guid}/renew-permission")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:227` [Route("api/riders/{riderProfileId:guid}/vehicle-timeline")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:230` [HttpGet("/api/riders/{riderProfileId:guid}/complete-history")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:241` [HttpGet], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:251` [Route("api/riders/{riderProfileId:guid}/promissory-files")], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:254` [HttpGet], `src/LogisticsERP.Api/Controllers/VehicleAssignmentsController.cs:262` [HttpGet("{fileId:guid}/download")].
- Page: `Gate-Front-master/src/app/admin/fleet/vehicles/[id]/history/page.tsx:1`.
- Page: `Gate-Front-master/src/app/admin/fleet/vehicles/[id]/events/page.tsx:11`.

### app.RiderVehicleAssignments — 800 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_RiderVehicleAssignments #1 |
| RiderProfileId | uniqueidentifier | no | unique-index IX_RiderVehicleAssignments_RiderProfileId #1; FK → app.RiderProfiles.Id |
| VehicleId | uniqueidentifier | no | unique-index IX_RiderVehicleAssignments_VehicleId #1; FK → app.Vehicles.Id |
| OperationId | uniqueidentifier | no |  |
| PreviousAssignmentId | uniqueidentifier | yes | FK → app.RiderVehicleAssignments.Id |
| StartedAtUtc | datetimeoffset | no |  |
| StartOdometer | bigint | no |  |
| StartVehicleCondition | int | no |  |
| StartFuelLevelPercentage | tinyint | yes |  |
| EndedAtUtc | datetimeoffset | yes |  |
| EndOdometer | bigint | yes |  |
| EndVehicleCondition | int | yes |  |
| EndFuelLevelPercentage | tinyint | yes |  |
| PermissionReference | nvarchar(200) | yes |  |
| PermissionStartsOn | date | yes |  |
| PermissionEndsOn | date | yes |  |
| Status | int | no |  |
| AssignmentReason | nvarchar(1000) | no |  |
| CompletionReason | nvarchar(1000) | yes |  |
| AssignedByUserId | uniqueidentifier | no |  |
| EndedByUserId | uniqueidentifier | yes |  |
| WasBackdated | bit | no |  |
| BackdatedReason | nvarchar(1000) | yes |  |
| CorrectionOfAssignmentId | uniqueidentifier | yes | FK → app.RiderVehicleAssignments.Id |
| CorrectionReason | nvarchar(1000) | yes |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| EndLocationSnapshot | nvarchar(400) | yes |  |
| StartLocationSnapshot | nvarchar(400) | yes |  |
| IsRealRider | bit | no |  |

### app.RiderVehicleAssignmentEvents — 863 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_RiderVehicleAssignmentEvents #1 |
| RiderVehicleAssignmentId | uniqueidentifier | no | FK → app.RiderVehicleAssignments.Id |
| OperationId | uniqueidentifier | no |  |
| EventType | int | no |  |
| OccurredAtUtc | datetimeoffset | no |  |
| ActorUserId | uniqueidentifier | no |  |
| Reason | nvarchar(1000) | no |  |
| ChangeSnapshotJson | nvarchar(max) | yes |  |
| CorrelationId | nvarchar(100) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |

### app.RealRiders — 6 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_RealRiders #1 |
| RiderVehicleAssignmentId | uniqueidentifier | no | unique-index IX_RealRiders_RiderVehicleAssignmentId #1; FK → app.RiderVehicleAssignments.Id |
| Name | nvarchar(200) | no |  |
| IqamaNo | varchar(10) | no |  |
| RelationshipToAssignedRider | nvarchar(200) | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |


## Platform account ownership / operation

- Entity: `src/LogisticsERP.Domain/Entities/Clients/PlatformRiderAccount.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Clients/RiderClientAssignment.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Clients/PlatformAccountRegistration.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:10` [Route("api/platform-accounts")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:40` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:45` [HttpPost], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:52` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:60` [HttpPost("{id:guid}/assign")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:68` [HttpPost("{id:guid}/release")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:76` [HttpGet("{id:guid}/assignment-history")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:81` [HttpGet("{id:guid}/credential-history")], `src/LogisticsERP.Api/Controllers/PlatformAccountsController.cs:86` [HttpPost("{id:guid}/rotate-credential")].
- Page: `Gate-Front-master/src/app/admin/platforms/accounts/page.tsx:67`.
- Page: `Gate-Front-master/src/app/admin/platforms/accounts/[id]/page.tsx:29`.

### platform.ClientPlatforms — 6 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_ClientPlatforms #1 |
| Code | nvarchar(32) | no | unique-index IX_ClientPlatforms_Code #1 |
| NameAr | nvarchar(200) | no |  |
| NameEn | nvarchar(200) | no |  |
| DescriptionAr | nvarchar(500) | yes |  |
| DescriptionEn | nvarchar(500) | yes |  |
| LogoAssetKey | nvarchar(500) | yes |  |
| Status | int | no |  |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| SupportedPaymentModels | int | no |  |

### app.PlatformRiderAccounts — 1874 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_PlatformRiderAccounts #1 |
| ClientPlatformId | uniqueidentifier | no | unique-index IX_PlatformRiderAccounts_ClientPlatformId_ExternalAccountId #1; unique-index IX_PlatformRiderAccounts_RegisteredEmployeeId_ClientPlatformId_OperatingCityId_SponsorId #2; FK → platform.ClientPlatforms.Id |
| Code | nvarchar(32) | no | unique-index IX_PlatformRiderAccounts_Code #1 |
| ExternalAccountId | nvarchar(150) | no | unique-index IX_PlatformRiderAccounts_ClientPlatformId_ExternalAccountId #2 |
| UserName | nvarchar(150) | yes |  |
| Status | int | no |  |
| StatusReason | nvarchar(500) | yes |  |
| AcquisitionDate | date | yes |  |
| StartDate | date | yes |  |
| EndDate | date | yes |  |
| OwnershipNotes | nvarchar(4000) | yes |  |
| OperationalNotes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| OperatingCityId | uniqueidentifier | no | unique-index IX_PlatformRiderAccounts_RegisteredEmployeeId_ClientPlatformId_OperatingCityId_SponsorId #3; FK → app.OperatingCities.Id |
| RegisteredEmployeeId | uniqueidentifier | yes | unique-index IX_PlatformRiderAccounts_RegisteredEmployeeId_ClientPlatformId_OperatingCityId_SponsorId #1; FK → app.Employees.Id |
| PaymentModel | int | no |  |
| SponsorId | uniqueidentifier | yes | unique-index IX_PlatformRiderAccounts_RegisteredEmployeeId_ClientPlatformId_OperatingCityId_SponsorId #4; FK → app.Sponsors.Id |

### app.RiderClientAssignments — 1024 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_RiderClientAssignments #1 |
| RiderProfileId | uniqueidentifier | no | unique-index IX_RiderClientAssignments_RiderProfileId_PaymentModel #1; unique-index IX_RiderClientAssignments_RiderProfileId_RiderAccountSlot #1; FK → app.RiderProfiles.Id |
| ClientContractId | uniqueidentifier | no | FK → app.ClientContracts.Id |
| PlatformRiderAccountId | uniqueidentifier | no | unique-index IX_RiderClientAssignments_PlatformRiderAccountId #1; FK → app.PlatformRiderAccounts.Id |
| EffectiveFrom | date | no |  |
| EffectiveTo | date | yes |  |
| Status | int | no |  |
| StartReason | nvarchar(1000) | yes |  |
| EndReason | nvarchar(1000) | yes |  |
| OperationalAgreementReference | nvarchar(200) | yes |  |
| OperationalAgreementNotes | nvarchar(4000) | yes |  |
| AssignedByUserId | uniqueidentifier | no |  |
| EndedByUserId | uniqueidentifier | yes |  |
| WasBackdated | bit | no |  |
| BackdatedReason | nvarchar(1000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |
| PaymentModel | int | no | unique-index IX_RiderClientAssignments_RiderProfileId_PaymentModel #2 |
| RiderAccountSlot | int | no | unique-index IX_RiderClientAssignments_RiderProfileId_RiderAccountSlot #2 |
| NinjaRiderType | nvarchar(32) | yes |  |

### app.PlatformAccountRegistrations — 290 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_PlatformAccountRegistrations #1 |
| RegisteredEmployeeId | uniqueidentifier | no | FK → app.Employees.Id; FK → app.RiderProfiles.EmployeeId |
| RiderProfileId | uniqueidentifier | no | FK → app.RiderProfiles.Id |
| ClientPlatformId | uniqueidentifier | no | FK → app.ClientContracts.ClientPlatformId; FK → platform.ClientPlatforms.Id |
| ClientContractId | uniqueidentifier | no | FK → app.ClientContracts.Id |
| SponsorId | uniqueidentifier | yes | FK → app.Sponsors.Id |
| OperatingCityId | uniqueidentifier | no | FK → app.OperatingCities.Id |
| RegistrationType | int | no |  |
| Status | int | no |  |
| StatusReason | nvarchar(1000) | yes |  |
| RequestedAtUtc | datetimeoffset | yes |  |
| ActivatedAtUtc | datetimeoffset | yes |  |
| PlatformRiderAccountId | uniqueidentifier | yes | unique-index IX_PlatformAccountRegistrations_PlatformRiderAccountId #1; FK → app.PlatformRiderAccounts.Id |
| Notes | nvarchar(4000) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |


## Sponsorship periods

- Page: `Gate-Front-master/src/app/admin/employees/[employeeId]/history/page.tsx:1`.

### EmployeeSponsorshipPeriods — unavailable

No visible source table; no fabricated import.


## Legal cases / hearings

- Entity: `src/LogisticsERP.Domain/Entities/Workforce/HrLegalCase.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:11` [Route("api/hr/legal-cases")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:14` [HttpGet], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:32` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:40` [HttpPost], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:48` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:56` [HttpDelete("{id:guid}")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:64` [HttpGet("{id:guid}/history")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:72` [HttpPost("{caseId:guid}/hearings")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:80` [HttpPut("{caseId:guid}/hearings/{hearingId:guid}")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:88` [HttpDelete("{caseId:guid}/hearings/{hearingId:guid}")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:96` [HttpPost("{caseId:guid}/hearings/{hearingId:guid}/files")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:108` [HttpGet("{caseId:guid}/hearings/{hearingId:guid}/files/{fileId:guid}/download")], `src/LogisticsERP.Api/Controllers/LegalCasesController.cs:118` [HttpDelete("{caseId:guid}/hearings/{hearingId:guid}/files/{fileId:guid}")].
- Page: `Gate-Front-master/src/app/admin/hr/legal-cases/page.tsx:42`.

### app.HrLegalCases — 13 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HrLegalCases #1 |
| CaseNumber | nvarchar(100) | no | unique-index IX_HrLegalCases_CaseNumber #1 |
| PersonName | nvarchar(250) | no |  |
| PersonType | int | no |  |
| EmployeeId | uniqueidentifier | yes | FK → app.Employees.Id |
| RiderProfileId | uniqueidentifier | yes | FK → app.RiderProfiles.Id |
| SponsorId | uniqueidentifier | no | FK → app.Sponsors.Id |
| SponsorPartyRole | int | no |  |
| CaseDate | date | no |  |
| CaseTime | time | no |  |
| Status | int | no |  |
| Details | nvarchar(max) | no |  |
| Notes | nvarchar(4000) | yes |  |
| ResponsibleUserId | uniqueidentifier | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.HrLegalCaseHearings — 2 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HrLegalCaseHearings #1 |
| LegalCaseId | uniqueidentifier | no | unique-index IX_HrLegalCaseHearings_LegalCaseId_HearingNumber #1; FK → app.HrLegalCases.Id |
| HearingNumber | int | no | unique-index IX_HrLegalCaseHearings_LegalCaseId_HearingNumber #2 |
| HearingDate | date | no |  |
| HearingTime | time | no |  |
| Status | int | no |  |
| Details | nvarchar(max) | no |  |
| Notes | nvarchar(4000) | yes |  |
| Location | nvarchar(500) | yes |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

### app.HrLegalCaseHearingFiles — 0 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK PK_HrLegalCaseHearingFiles #1 |
| HearingId | uniqueidentifier | no | FK → app.HrLegalCaseHearings.Id |
| OriginalFileName | nvarchar(255) | no |  |
| StoredFileName | nvarchar(255) | no |  |
| StoragePath | nvarchar(1000) | no |  |
| ContentType | nvarchar(100) | no |  |
| FileSizeBytes | bigint | no |  |
| Sha256Checksum | nchar(64) | no |  |
| Description | nvarchar(1000) | yes |  |
| UploadedByUserId | uniqueidentifier | no |  |
| UploadedAtUtc | datetimeoffset | no |  |
| CreatedAtUtc | datetimeoffset | no |  |
| CreatedByUserId | uniqueidentifier | yes |  |
| UpdatedAtUtc | datetimeoffset | yes |  |
| UpdatedByUserId | uniqueidentifier | yes |  |
| RowVersion | timestamp | no |  |
| IsDeleted | bit | no |  |
| DeletedAtUtc | datetimeoffset | yes |  |
| DeletedByUserId | uniqueidentifier | yes |  |
| DeletionReason | nvarchar(500) | yes |  |

## Drift and limits

| Live table | Columns not represented by the parsed current model snapshot |
|---|---|
| scoped tables | none found |

Snapshot comparison is structural and includes owned-property column names; it does not prove migration history/application order. The live metadata is authoritative for the fields actually read. Compare the table/column lists above with the cited EF configurations when evolving the adapter.

Backup versus prior live metadata: same 224 table names, zero column-definition differences, zero PK/unique-index key-column differences, and zero FK-definition differences. Metadata row counts differ for 55 tables. The counts below are snapshot observations, not claims about the current live state or individual field equality.

Reference-only context: `docs/rider-chefz-jahez-assignment-limits.md:11` distinguishes ownership from dated assignment slots; `tools/LogisticsERP.NinjaPerformance/Program.cs:18` separates inspect/rehearse/apply. Those tools were read, never executed.

Additional reference-only schema files: `~/Projects/ref/erp-github/Logistic-ERP/database/scripts/application.sql` (CREATE/ALTER/DROP definitions only). Operational scripts in tools/ and database/scripts/ are not executed or copied. Ninja/Chefz docs are context for account/payment-model codes; no settlement rules or individual amounts are imported.

### Live versus backup metadata row-count drift

| Table | Earlier live metadata | Restored backup metadata |
|---|---:|---:|
| app.EmployeeDocuments | 2564 | 2540 |
| app.EmployeeDocumentVersions | 2048 | 2024 |
| app.Employees | 2170 | 2160 |
| app.EmployeeWorkHistory | 2727 | 2677 |
| app.FleetCommandReceipts | 537 | 522 |
| app.FuelCardImports | 29 | 27 |
| app.FuelCardRiderAssignments | 666 | 660 |
| app.Notifications | 39074 | 38008 |
| app.PlatformAccountCredentialVersions | 269 | 266 |
| app.PlatformAccountDayUsages | 1360 | 1343 |
| app.PlatformAccountRegistrations | 294 | 290 |
| app.RiderAssignmentEvents | 1625 | 1607 |
| app.RiderClientAssignments | 1033 | 1024 |
| app.RiderProfiles | 2154 | 2144 |
| app.RiderVehicleAssignmentEvents | 878 | 863 |
| app.RiderVehicleAssignments | 809 | 800 |
| app.VehicleDailyDistances | 9770 | 9757 |
| app.VehicleOdometerReadings | 12620 | 12583 |
| app.VehicleOperationalStatusPeriods | 2785 | 2768 |
| audit.AuditEntries | 349673 | 331025 |
| chefz.DailyPerformance | 5035 | 4912 |
| chefz.DailyPerformanceSources | 5029 | 4906 |
| chefz.ImportedOrderRows | 9574 | 0 |
| chefz.OrderSettlementImports | 6 | 3 |
| chefz.PerformanceImports | 33 | 32 |
| chefz.WalletSettlements | 496 | 241 |
| identity.UserSessions | 13422 | 13138 |
| jahez_v2.AccountFees | 87 | 83 |
| jahez_v2.AccountingEntries | 30010 | 27619 |
| jahez_v2.AccountSessions | 400 | 396 |
| jahez_v2.CashboxHandoverMovements | 617 | 568 |
| jahez_v2.CashboxHandovers | 15 | 14 |
| jahez_v2.CommandReceipts | 1770 | 1690 |
| jahez_v2.ImportBatches | 72 | 71 |
| jahez_v2.ImportFiles | 72 | 71 |
| jahez_v2.ImportRows | 47459 | 46405 |
| jahez_v2.MoneyMovements | 563 | 532 |
| jahez_v2.Operations | 577 | 543 |
| jahez_v2.PlatformTransactions | 15076 | 14022 |
| jahez_v2.RequestDecisions | 27 | 24 |
| jahez_v2.Requests | 29 | 26 |
| jahez_v2.TransferredDebts | 24 | 21 |
| jahez_v3.Ownership | 1250 | 1178 |
| maintenance.MaterialUsages | 1353 | 1341 |
| maintenance.OilBarrelLosses | 14 | 13 |
| maintenance.OilBarrelUsageAllocations | 1353 | 1341 |
| maintenance.OilChangeOperations | 1324 | 1313 |
| maintenance.OilChangeRevisions | 10 | 9 |
| maintenance.StockCostAllocations | 1367 | 1354 |
| maintenance.StockMovementLines | 1386 | 1373 |
| maintenance.StockMovements | 1375 | 1362 |
| maintenance.VehicleExpenses | 1355 | 1343 |
| maintenance.VehicleSchedules | 370 | 367 |
| ninja.NinjaAccountUpdateRows | 481 | 293 |
| ninja.NinjaAccountUpdateUploads | 3 | 2 |

### Other visible tables absent from current snapshot models

- `identity.AccessScopes` (metadata rows: 0).
- `identity.RoleClaims` (metadata rows: 0).
- `identity.RolePermissions` (metadata rows: 1573).
- `identity.Roles` (metadata rows: 12).
- `identity.SupportAccessGrants` (metadata rows: 0).
- `identity.TemporaryCredentials` (metadata rows: 58).
- `identity.UserClaims` (metadata rows: 0).
- `identity.UserDirectPermissionAssignments` (metadata rows: 23147).
- `identity.UserLogins` (metadata rows: 0).
- `identity.UserRoleAssignments` (metadata rows: 144).
- `identity.UserRoles` (metadata rows: 0).
- `identity.Users` (metadata rows: 36).
- `identity.UserSessions` (metadata rows: 13138).
- `identity.UserTokens` (metadata rows: 0).
- `migration.__ApplicationMigrationsHistory` (metadata rows: 106).
- `migration.__IdentityMigrationsHistory` (metadata rows: 25).

These are outside the imported areas; historical migrations may describe them. No migration was applied to ERP.

## Enum code definitions (from source, no row values)

- `EmployeeStatus`: `Draft = 1, Onboarding = 2, Active = 3, Suspended = 4, OnLeave = 5, Terminated = 6, Archived = 7, Fleeing = 8, Accident = 9, Sick = 10` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:3`.
- `EmployeeRelationshipType`: `SponsoredInternal = 1, OutsideRider = 2` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:16`.
- `MaritalStatus`: `Unmarried = 1, Married = 2, Divorced = 3, Widowed = 4` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:20`.
- `HousingStatus`: `Draft = 1, Active = 2, Inactive = 3, Closed = 4, Archived = 5` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:21`.
- `DocumentStatus`: `Active = 1, Expired = 2, Superseded = 3, Archived = 4` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:22`.
- `SponsorshipStatus`: `Pending = 1, Active = 2, TransferInProgress = 3, Ended = 4, Cancelled = 5` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:24`.
- `DriverLicenseBookingStatus`: `NotApplicable = 1, NotBooked = 2, WaitingForAppointment = 3, Booked = 4, Cancelled = 5, Unknown = 6` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:25`.
- `DriverLicenseIssuanceStatus`: `NotStarted = 1, InProgress = 2, Issued = 3, Rejected = 4, Cancelled = 5` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:26`.
- `DriverLicenseStatus`: `Application = 1, Active = 2, Expired = 3, Suspended = 4, Revoked = 5, Rejected = 6, Superseded = 7, Cancelled = 8` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:27`.
- `RiderCardType`: `Car = 1, Motorcycle = 2` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:28`.
- `RiderCardStatus`: `Draft = 1, Active = 2, Expired = 3, Suspended = 4, Superseded = 5, Cancelled = 6` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:30`.
- `RiderHealthCardStatus`: `Draft = 1, Active = 2, Expired = 3, Suspended = 4, Superseded = 5, Cancelled = 6` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:31`.
- `PromissoryNoteStatus`: `Draft = 1, Active = 2, Settled = 3, Expired = 4, Cancelled = 5, Disputed = 6` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:32`.
- `InsuranceCompanyStatus`: `Draft = 1, Active = 2, Suspended = 3, Inactive = 4, Archived = 5` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:33`.
- `InsurancePlanStatus`: `Draft = 1, Active = 2, Inactive = 3, Archived = 4` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:34`.
- `MedicalInsurancePolicyStatus`: `Pending = 1, Active = 2, Expired = 3, Cancelled = 4, Superseded = 5` — `src/LogisticsERP.Domain/Enums/WorkforceEnums.cs:35`.
- `VehicleCatalogStatus`: `Active = 1, Disabled = 2, Archived = 3` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:3`.
- `VehicleType`: `Motorcycle = 1, Car = 2, Van = 3, Truck = 4, Other = 5` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:4`.
- `VehicleOwnershipType`: `Owned = 1, Leased = 2, ThirdParty = 3` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:7`.
- `VehicleRegistrationType`: `Private = 1, PrivateTransport = 2, SmallBus = 3, Taxi = 4, PublicTransport = 5, PublicBus = 6, Motorcycle = 7, PublicWorks = 8` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:8`.
- `VehicleOperationalStatus`: `Available = 1, Assigned = 2, ProblemHold = 3, AccidentHold = 4, Stolen = 5, OutOfService = 6, Decommissioned = 7, UnderMovementResponsibility = 8` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:19`.
- `VehicleStatusSourceType`: `Vehicle = 1, Assignment = 2, Issue = 3, Accident = 4, Administrative = 5` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:30`.
- `RiderVehicleAssignmentStatus`: `Active = 1, Completed = 2, Cancelled = 3, Corrected = 4` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:33`.
- `VehiclePlatformAccountAssignmentStatus`: `Active = 1, Ended = 2` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:34`.
- `VehiclePlatformAccountApprovalStatus`: `Approved = 1` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:35`.
- `VehiclePlatformAccountSwitchStatus`: `Pending = 1, Accepted = 2` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:37`.
- `RiderVehicleAssignmentEventType`: `Taken = 1, Returned = 2, SwitchedOut = 3, SwitchedIn = 4, PermissionRenewed = 5, Cancelled = 6, Corrected = 7` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:38`.
- `VehicleCondition`: `Unknown = 1, Good = 2, Fair = 3, Damaged = 4, Unsafe = 5` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:48`.
- `ComplianceRecordStatus`: `Active = 1, Superseded = 2, Cancelled = 3` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:49`.
- `VehicleComplianceDueStatus`: `Valid = 1, Upcoming = 2, DueToday = 3, Expired = 4, Missing = 5, UploadedWithoutDates = 6` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:51`.
- `VehicleIssueStatus`: `Open = 1, UnderReview = 2, Resolved = 3, Closed = 4, Rejected = 5` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:72`.
- `VehicleAccidentStatus`: `Reported = 1, Finalized = 2, Closed = 3` — `src/LogisticsERP.Domain/Enums/FleetEnums.cs:74`.
- `ClientContractStatus`: `Draft = 1, Active = 2, Suspended = 3, Ended = 4, Archived = 5` — `src/LogisticsERP.Domain/Enums/ClientEnums.cs:3`.
- `PlatformRiderAccountStatus`: `Available = 1, Assigned = 2, Suspended = 3, Retired = 4, Archived = 5` — `src/LogisticsERP.Domain/Enums/ClientEnums.cs:4`.
- `RiderAssignmentStatus`: `Planned = 1, Active = 2, Ended = 3, Cancelled = 4` — `src/LogisticsERP.Domain/Enums/ClientEnums.cs:5`.
- `PlatformRegistrationType`: `Sponsored = 1, Freelancer = 2` — `src/LogisticsERP.Domain/Enums/ClientEnums.cs:6`.
- `PlatformAccountRegistrationStatus`: `NotRequired = 1, Requested = 2, ActivationInProgress = 3, Activated = 4, Suspended = 5, Rejected = 6, MissingData = 7, Cancelled = 8` — `src/LogisticsERP.Domain/Enums/ClientEnums.cs:7`.
- `ExportStatus`: `Pending = 1, Running = 2, Completed = 3, Failed = 4, Expired = 5, Cancelled = 6` — `src/LogisticsERP.Domain/Enums/SystemEnums.cs:5`.
- `NinjaRiderType`: `InternalCar = 1, ExternalCar = 2, InternalMotorcycle = 3, ExternalMotorcycle = 4` — `src/LogisticsERP.Domain/Enums/NinjaRiderType.cs:3`.
