# API v0.5.0

Same-origin JSON, authenticated HttpOnly cookie for `/api/*` except `/api/auth/*` and `/api/public/*`. Errors use `{error:string}` with 400/401/403/404/409/429/503. No financial mutation from public routes. Source of truth: `Payroll.Api/Program.cs`, `UserAccess.cs`, `DirectoryApi.cs`, `FormBuilder.cs`, `Debts.cs`, `InquiryApi.cs`, `Store.cs`, `Engine.cs`, `Imports.cs`; integration tests are executable request examples.

| Flow | Endpoint | Main payload/result |
|---|---|---|
| Setup/login | POST `/api/auth/setup`, `/login` | username,password; setup bootstrapKey in Production |
| Admin state | GET `/api/auth/status`, `/api/me`, `/api/dashboard` | setup needed / username / actual counts |
| Riders | GET/POST `/api/riders` | iqama,name,phone,city,policyId; approved policy only |
| Policy | GET/POST `/api/policies`; POST `/{id}/approve` | name,kind:commission/tiers/approved_pay,platform:all or platform name,effectiveFrom:yyyy-MM,config |
| Policy config | commission / tiers | companyFee; tiers:[{minOrders,salary,extraRate,fuelAllowance}],internetAllowance |
| Upload | POST `/api/imports/inspect` | filename,dataBase64 → id,sheets:[{name,rows,columns}] |
| Headers | GET `/api/imports/{id}/sheet?sheet=...&headerRow=1` | indexed headers, sample and row count, admin only |
| Normalize/save | POST `/api/imports/{id}/preview`, `/commit` | sheet,period,platform,headerRow,lastRow?,ignoreRows?,confirmActualRider:true,mapping |
| Mapping | zero-based integer columns | sourceRef,iqama,name,accountId,from,to,orders,platformRevenue,riderPay,isValid,company,city |
| Adjustment | POST `/api/adjustments` | iqama,period,amount signed,category,description,sourceRef |
| Payroll | GET `/api/payroll/preview?period=yyyy-MM` | results incl lines,blockers,openingBalance,periodMovement,closingBalance,payable,debt,orders,**fingerprint at preview time** |
| Approve | POST `/api/payroll/approve` | iqama,period,fingerprint from displayed preview; blockers or stale fingerprint reject |
| Snapshots | GET `/api/payroll/runs` | immutable approved result,policySnapshot,sourceRows |
| Ledger | GET `/api/ledger/{iqama}` | rows,balance |
| Payment | POST `/api/payments` | iqama,period,amount positive,sourceRef unique |
| Export | GET `/api/payroll/export.csv?period=...` | review CSV, not bank file |
| Campaign | GET/POST `/api/campaigns`; POST `/{id}/close` | title,days1..30 → id,path persistent publicUrl + qrCodeUrl; sharing token retained,expiresAt |
| Responses | GET `/api/submissions`; POST `/{id}/review` | status approved/rejected, optional note (empty by default), version (displayed revision), identityReviewed (approval accepts omission, rejects explicit false), ibanOwnershipReviewed required if legacy IBAN provided |
| Audit | GET `/api/audit` | all stored actor/action/entity/time |
| Public form | GET `/forms/{token}`; GET `/api/public/forms/{token}` | form HTML / title,expiresAt,schema:work-choice-v1; no existing rider information |
| Public submit | POST `/api/public/forms/{token}` | name,iqama,workSystem only → receipt,editToken,version,message; private duplicate prompt409; extra fields reject |

Public workSystem values: `tiers` = نظام الراتب الجديد / `commission` = نظام العمولة السابق. No third choice, no default, no consent input. Public iqama is a 10-character ASCII digit string; Arabic/Persian digits reject instead of normalization. Name 3..150 characters, at least two parts, Arabic/English letters and spaces, whitespace normalized. Duplicate `(campaignId, iqama)` rejects. Existing name/phone/IBAN records never exposed by public API. Approval preserves existing private rider data and records workPreference only, without assigning policyId.

Platform values for payroll import: `Keeta/HungerStation/Ninja/Chefz/Jahez/Amazon/Other`. Unmapped financial values remain null. Policy and payroll APIs retain their existing contracts. Legacy submissions retain their historical fields; bank ownership confirmation only applies if an old response includes an IBAN.

Never call the fingerprint endpoint just before approving to replace the displayed preview's fingerprint: doing so would silently approve unseen changes. Rendering already approved periods uses run.result, not recalculation against a newly assigned policy. Never place imported raw rows or private admin responses in public form state or browser storage. Policy assignment and workPreference are separate fields.


Excel endpoints (authenticated): see TABLE_STANDARD_AR.md for exact headers and examples. GET /api/excel/{choices|riders|work}/template; POST /api/excel/export with headers,rows,numericColumns; POST /api/excel/{choices|riders}/preview and /commit (filename,dataBase64,campaignId for choices,fingerprint for commit). Returns row-numbered errors; rejects changed schema, formula input, duplicates, closed campaigns, stale fingerprint or invalid policy; all-or-nothing new records only. Imported choices remain pending.


Release0.4: Production API requires HTTPS. First admin setup requires an explicit, non-placeholder bootstrap secret of at least32 characters; missing configuration returns503 and an incorrect secret rejects. Additional proxy IPs must be explicitly trusted; forwarded Host is not consumed. Public form contracts are unchanged. Administrative rider names use DirectoryProfiles.Name: source names may be mononyms, Unicode letters, dots, hyphens or apostrophes; digits, markup and control characters reject. Excel directory identities remain10 ASCII digits starting1/2. Legacy v1 directory imports retain needs_review. New v2 records retain validated operational statuses; baseline synchronization never assigns a payroll policy automatically.


## Enterprise endpoints (0.5.0)

All administrative endpoints require the live account to be active. Roles are checked on every request; password hashes/salts never leave the user management endpoints. Legacy administrator credentials/cookies remain compatible. New phones are E.164; legacy five-column rider templates remain accepted alongside v2.

| Resource | Endpoint and behavior |
|---|---|
| Users (Admin) | GET/POST /api/users; PUT /api/users/{id}/status with role/status. Password12..200; username unique ignoring case; cannot disable final active Admin. |
| Rider attributes | nationality, phone, engagementType:SponsoredInternal/External, sponsor, city, operationalRole:Car/Motorcycle, status:Active/OnLeave/Archived/NeedsReview; missing source enums remain unassigned. |
| Baseline (Admin) | POST /api/riders/sync-baseline {riders:[...],mode:preview}; then mode:commit + fingerprint. Administrative people excluded, empty existing attributes may be filled, conflicts preserved, no policy assignment. |
| Persistent campaigns | GET /api/campaigns returns publicUrl and qrCodeUrl; GET /api/campaigns/{id}/qr.svg renders authenticated same-site SVG. Old hashed tokens remain valid. |
| Custom builder (Admin) | GET/POST /api/forms/builder; PUT /{id} edits drafts; POST /{id}/publish or /close; GET /{id}/qr.svg. Fields immutable after publishing. |
| Schema | titleAr/titleEn/titleUr, descriptionAr/En/Ur, expiresAt, maxSubmissions, fields:[{id,labelAr,labelEn,labelUr,type,required,options}]. Types text/number/select/radio/iqama/phone/date; max50fields. Options strings or {value,labelAr,labelEn,labelUr}. |
| Questionnaire collection | GET /questionnaires/{token}; GET/POST /api/public/questionnaires/{token}; answers object keys are field IDs. Unknown fields/options reject; required fields, expiry and capacity rechecked transactionally. No financial or rider record writes. |
| Analytics (Admin/Reviewer) | GET /api/forms/catalog, /api/forms/analytics/{id}, /api/forms/responses/{id}; totals, responses/hour since first response, choice counts and percentages of all responses. |
| Debts (Admin/Accountant) | GET/POST /api/debts; PUT /api/debts/{id}. Create iqama, category, totalAmount, monthlyInstallment, sourceRef, agreementReviewed:true, effectiveFrom?, notes?. Categories Advance/TrafficFine/EquipmentLoan/VehicleInstallment/AdministrativeDeduction. |
| Debt changes | PUT monthlyInstallment needs agreementReviewed:true; status active/paused. settled is reached by actual posting. Preview is pure; approval atomically reduces debt and posts an installment once per debt/month. |
| Ledger (Admin/Accountant) | GET /api/ledger lists movements; /{iqama} retains rows/balance. Payroll gross movement + separate debt installments equals net period movement. |
| Access code (Admin/Accountant) | POST /api/inquiry-access {iqama,scope:payslip/debt,identityReviewed:true} returns a 256-bit single use code, expiresAt and inquiry page URL. Manual verified delivery; no SMS provider configured. |
| Private inquiry | POST /api/public/inquiry/payslip or /debt with iqama,phoneLast4,code and period for payslip. Generic401 on invalid verification;429 on IP or identity rate limit. Only approved results or scoped debt totals returned. |
| Cache | Summary TTL15minutes; local revision invalidation on data writes. Successful verified responses: private,max-age=300 and content ETag; every request still verifies a fresh scoped code. |
| Storage | Absent ConnectionStrings:DefaultConnection → SQLite WAL; configured valid string → SQL Server. Invalid configured SQL fails closed, never silently selects another database. Private operator snapshot tool under tools/StoreMigration. |

RBAC matrix, data compatibility, financial posting and operational limits: UPGRADE_V05_AR.md. Public collection form remains the original three-field contract with AR/EN/UR rendering and an informational reference modal following the confirmed existing commission policy.

## Response revisions (0.5.1)

- POST `/api/public/forms/{token}` still accepts **only** `iqama,name,workSystem`. First submission returns `receipt,editToken,version:1`; store the edit capability securely. The server stores only its SHA-256 hash. A duplicate returns HTTP409 `{alreadySubmitted:true,message}` without exposing another rider's receipt, name or choice.
- POST `/api/public/forms/{token}/resume` accepts `receipt,editToken` and returns the owner's current `iqama,name,workSystem,version,isEdited` and capability. No public lookup by Iqama. Receipts issued before0.5.1 are random128-bit capabilities: receipt-only recovery is accepted only while no edit hash exists, then upgraded to a256-bit edit code atomically. Once upgraded, the edit code is required. Lost capabilities require administrative identity review; there is no public bypass.
- POST `/api/public/forms/{token}/edit` accepts `iqama,name,workSystem,receipt,editToken,version`. Iqama cannot change. The authenticated capability must belong to this campaign and response. Both resume and edit reject closed/expired campaigns and are rate limited. A supplied stale version returns409; the current UI always supplies it. Identical retries preserve the existing version and review.
- A changed answer updates the **same** record, increments `version`, sets `isEdited:true,status:pending,updatedAt`, and appends the former name, choice, version, timestamp, status, reviewer and optional note to `history`. Former review metadata leaves the current record; `submission.edited` audit and record changes share one transaction. No revisions are discarded.
- GET `/api/submissions` and review responses omit `editTokenHash`. Admin and Reviewer may review; Accountant cannot. Quick approval includes the displayed version and explicit identity confirmation; legacy IBAN ownership still requires detail review. Optional notes default to empty.
- History is review evidence, not a payroll recalculation. Pending edits do not change the previously accepted rider preference; a subsequent approval updates it. Payroll policy, approved runs and financial ledgers remain untouched.
- Public sessionStorage holds only the receipt/edit capability, scoped by campaign. Personal answers stay in memory and are fetched only after capability verification. Success screen includes the edit button and private code for cross-device recovery. AR/EN/UR edit/duplicate/recovery/error messages are localized.
- Responses grid exports explicit edit flag, numeric version, update time and optional review note. Status chips combine with all column filters; filtered XLSX/CSV excludes action controls. The three-field input template is unchanged; imports create version1 pending replies.
