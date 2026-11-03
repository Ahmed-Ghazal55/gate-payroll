# ERP data map — 2026-11-03

Source: approved live read-only SQL account; schema metadata exposes 224 tables. Source code: gate-erp sync `d343d63`, verified clone provenance `4eab10c`. Runtime data is newer evidence than historical migration scripts. No source/production writes. No backup restore was needed: the prompt update explicitly replaces it with direct live reads.

Source rows were read once per allowlisted table, ordered by primary key. Read window: 2026-11-03T17:33:48.155657+00:00 to 2026-11-03T17:33:55.9823992+00:00 (UTC); approximately 8 seconds. Between the first and final dry-runs, assignments increased by one and custody events by three; this is live-source movement between runs, not a destination write. Database rowversion was unchanged across the read; this is a read-committed consistency check, not a transactional snapshot or proof against hard deletes. Target schema was empty before dry-run; no Store construction or target DDL/DML occurred.

## Classification rules

- `Entities/Workforce/Employee.cs:26` (`IsEmployee`) and `:27` (`EngagementType`); `Entities/Workforce/RiderProfile.cs:8` links the employee to a rider profile. A rider requires an existing rider profile; administrative employees without one are not fabricated into riders. `IsEmployee` is preserved as a separate flag and never used alone to discard a sponsored rider.
- `Enums/WorkforceEnums.cs:16`: code `1=SponsoredInternal`, `2=OutsideRider`; target maps the latter to `External`. Current SponsorId does not override this enum. Valid candidates include 432 sponsored and 1708 external riders; 14 invalid identities and 16 employees without rider profiles are excluded.
- A platform account owner is `PlatformRiderAccounts.RegisteredEmployeeId`. Operators come from dated `RiderClientAssignments.RiderProfileId`; they are not inferred from the owner. PaymentModel and NinjaRiderType are preserved as codes, not interpreted as payroll rules.
- `EmployeeSponsorshipPeriods` is absent in the visible live schema and absent from the current Application model snapshot. It appears in historical `database/scripts/application.sql:3585`, with an eventual DROP at `:6344`. No historical sponsorship dates are inferred from HireDate or current SponsorId.
- Sponsors match the existing Payroll sponsor catalog only by an unambiguous registration/employer/unified identity. All three match existing keys. All three Sponsor commercial registration numbers are missing: target CR rows are rejected rather than invented; sponsor active dates are not CR issue/expiry dates.

## Schema and source evidence

Counts marked `exact` came from the single-pass reader; other counts are `sys.partitions` metadata estimates. PK/unique/FK definitions below are read from the live SQL catalog. Soft deletion is preserved, not physically applied.

## Riders / employees

- Entity: `src/LogisticsERP.Domain/Entities/Workforce/Employee.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Workforce/RiderProfile.cs:6`.
- Entity: `src/LogisticsERP.Domain/Entities/Workforce/EmployeeWorkHistory.cs:6`.
- Endpoints: `src/LogisticsERP.Api/Controllers/EmployeesController.cs:10` [Route("api/employees")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:21` [HttpGet("{employeeId:guid}")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:29` [HttpGet("{employeeId:guid}/vehicle-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:37` [HttpPut("{employeeId:guid}/vehicle-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:45` [HttpPut("{employeeId:guid}/rider-profile")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:53` [HttpPost], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:63` [HttpPut("{employeeId:guid}")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:71` [HttpPost("{employeeId:guid}/status-transitions")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:79` [HttpPost("{employeeId:guid}/reactivate")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:87` [HttpPost("{employeeId:guid}/role-transitions")], `src/LogisticsERP.Api/Controllers/EmployeesController.cs:95` [HttpGet("{employeeId:guid}/work-history")].
- Endpoints: `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:10` [Route("api/payroll-employees")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:13` [HttpGet], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:23` [HttpGet("{id:guid}")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:31` [HttpPost], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:43` [HttpPut("{id:guid}")], `src/LogisticsERP.Api/Controllers/PayrollEmployeesController.cs:54` [HttpDelete("{id:guid}")].
- Page: `Gate-Front-master/src/app/admin/employees/page.tsx:126`.
- Page: `Gate-Front-master/src/app/admin/employees/[employeeId]/page.tsx:460`.

### app.Employees — 2170 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
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
| IqamaNo | varchar(10) | yes | unique |
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

### app.RiderProfiles — 2154 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | unique; PK |
| EmployeeId | uniqueidentifier | no | unique; unique; FK → app.Employees.Id |
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

### app.EmployeeWorkHistory — 2727 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
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
| Id | uniqueidentifier | no | PK |
| Number | int | no | unique |
| Name | nvarchar(200) | no |  |
| NationalId | varchar(10) | no | unique |
| Country | nvarchar(100) | no |  |
| JoiningDate | date | no |  |
| PersonalIban | varchar(24) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| CompanyProfileId | uniqueidentifier | no | FK → platform.CompanyProfile.Id |
| EmployerIdentityNumber | nvarchar(100) | no | unique |
| RegistryNameAr | nvarchar(200) | no |  |
| RegistryNameEn | nvarchar(200) | yes |  |
| CommercialRegistrationNumber | nvarchar(100) | yes | unique |
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
| Id | uniqueidentifier | no | PK |
| Code | nvarchar(32) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| Code | nvarchar(32) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| GlobalCityId | uniqueidentifier | no | unique; FK → platform.GlobalCities.Id |
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
| Id | uniqueidentifier | no | PK |
| Code | nvarchar(32) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| HousingId | uniqueidentifier | no | unique; FK → app.Housing.Id |
| Name | nvarchar(100) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| HousingId | uniqueidentifier | no | FK → app.Housing.Id |
| Name | nvarchar(100) | no | unique |
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
| FloorId | uniqueidentifier | no | unique; FK → app.HousingFloors.Id |
| Notes | nvarchar(2000) | yes |  |

### app.HousingResidencePeriods — 200 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
| EmployeeId | uniqueidentifier | no | unique; FK → app.Employees.Id |
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
| Id | uniqueidentifier | no | PK |
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
| Id | uniqueidentifier | no | PK |
| RoomId | uniqueidentifier | no | FK → app.HousingRooms.Id |
| IqamaNo | nvarchar(20) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| AssetNumber | nvarchar(64) | no |  |
| NormalizedAssetNumber | nvarchar(64) | no | unique |
| PlateNumberAr | nvarchar(32) | yes |  |
| NormalizedPlateNumberAr | nvarchar(32) | yes | unique |
| PlateNumberEn | nvarchar(32) | yes |  |
| NormalizedPlateNumberEn | nvarchar(32) | yes | unique |
| PlateLettersAr | nvarchar(8) | yes |  |
| PlateLettersEn | nvarchar(8) | yes |  |
| PlateDigits | nvarchar(8) | yes |  |
| Vin | nvarchar(64) | yes | unique |
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
| NormalizedChassisNumber | nvarchar(100) | yes | unique |
| NormalizedSerialNumber | nvarchar(100) | yes | unique |
| PurchasedFromSupplierId | uniqueidentifier | yes | FK → app.VehicleSuppliers.Id |
| RegistrationType | int | yes |  |
| SerialNumber | nvarchar(100) | yes |  |
| TrackedDistanceKm | decimal(18,2) | no |  |
| RegisteredOwnerSupplierId | uniqueidentifier | yes | FK → app.VehicleSuppliers.Id |
| RegisteredOwnerSponsorId | uniqueidentifier | yes | FK → app.Sponsors.Id |

### app.VehicleRegistrations — 2 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
| VehicleId | uniqueidentifier | no | unique; FK → app.Vehicles.Id |
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
| Id | uniqueidentifier | no | PK |
| VehicleId | uniqueidentifier | no | unique; unique; FK → app.Vehicles.Id |
| ProviderName | nvarchar(200) | no |  |
| PolicyNumber | nvarchar(150) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| VehicleId | uniqueidentifier | no | unique; FK → app.Vehicles.Id |
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

### app.VehicleOperationalStatusPeriods — 2785 rows (metadata estimate)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
| VehicleId | uniqueidentifier | no | unique; FK → app.Vehicles.Id |
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

### app.RiderVehicleAssignments — 810 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
| RiderProfileId | uniqueidentifier | no | unique; FK → app.RiderProfiles.Id |
| VehicleId | uniqueidentifier | no | unique; FK → app.Vehicles.Id |
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

### app.RiderVehicleAssignmentEvents — 881 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
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
| Id | uniqueidentifier | no | PK |
| RiderVehicleAssignmentId | uniqueidentifier | no | unique; FK → app.RiderVehicleAssignments.Id |
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
| Id | uniqueidentifier | no | PK |
| Code | nvarchar(32) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| ClientPlatformId | uniqueidentifier | no | unique; unique; FK → platform.ClientPlatforms.Id |
| Code | nvarchar(32) | no | unique |
| ExternalAccountId | nvarchar(150) | no | unique |
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
| OperatingCityId | uniqueidentifier | no | unique; FK → app.OperatingCities.Id |
| RegisteredEmployeeId | uniqueidentifier | yes | unique; FK → app.Employees.Id |
| PaymentModel | int | no |  |
| SponsorId | uniqueidentifier | yes | unique; FK → app.Sponsors.Id |

### app.RiderClientAssignments — 1033 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
| RiderProfileId | uniqueidentifier | no | unique; unique; FK → app.RiderProfiles.Id |
| ClientContractId | uniqueidentifier | no | FK → app.ClientContracts.Id |
| PlatformRiderAccountId | uniqueidentifier | no | unique; FK → app.PlatformRiderAccounts.Id |
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
| PaymentModel | int | no | unique |
| RiderAccountSlot | int | no | unique |
| NinjaRiderType | nvarchar(32) | yes |  |

### app.PlatformAccountRegistrations — 294 rows (exact)

| Column | SQL type | Nullable | Key / relation |
|---|---|---|---|
| Id | uniqueidentifier | no | PK |
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
| PlatformRiderAccountId | uniqueidentifier | yes | unique; FK → app.PlatformRiderAccounts.Id |
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
| Id | uniqueidentifier | no | PK |
| CaseNumber | nvarchar(100) | no | unique |
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
| Id | uniqueidentifier | no | PK |
| LegalCaseId | uniqueidentifier | no | unique; FK → app.HrLegalCases.Id |
| HearingNumber | int | no | unique |
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
| Id | uniqueidentifier | no | PK |
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

A backup-versus-live drift comparison was not run because the authorized live-source update made restore optional. No claim is made that the .bak matches the live source.

Reference-only context: `docs/rider-chefz-jahez-assignment-limits.md:11` distinguishes ownership from dated assignment slots; `tools/LogisticsERP.NinjaPerformance/Program.cs:18` separates inspection/rehearsal from applying changes. These tools were read, never executed.

Additional reference-only schema files: `~/Projects/ref/erp-github/Logistic-ERP/database/scripts/application.sql` (CREATE/ALTER/DROP definitions only). Operational scripts in tools/ and database/scripts/ are not executed or copied. Ninja/Chefz docs are context for account/payment-model codes; no settlement rules or individual amounts are imported.

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
- `identity.UserSessions` (metadata rows: 13422).
- `identity.UserTokens` (metadata rows: 0).
- `migration.__ApplicationMigrationsHistory` (metadata rows: 106).
- `migration.__IdentityMigrationsHistory` (metadata rows: 25).

These are outside the imported areas; historical migrations may describe them. Their presence is not a claim that a pending migration should be applied.

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
