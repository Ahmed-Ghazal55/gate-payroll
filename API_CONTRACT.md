# API v0.6.1

Same-origin JSON, authenticated HttpOnly cookie for `/api/*` except `/api/auth/*` and `/api/public/*`. Errors use `{error:string}` with 400/401/403/404/409/429/503. No financial mutation from public routes. Source of truth: `Payroll.Api/Program.cs`, `UserAccess.cs`, `DirectoryApi.cs`, `FormBuilder.cs`, `Debts.cs`, `InquiryApi.cs`, `SubmissionChoices.cs`, `Store.cs`, `Engine.cs`, `Imports.cs`; integration tests are executable request examples.

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
| Public submit | POST `/api/public/forms/{token}` | name,iqama,workSystem only → receipt,iqama,name,workSystem,version,status,submittedAt,message; existing choice prompt409; extra fields reject |

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

## Response revisions (0.5.2)

- The user explicitly selected **public Iqama-based editing** over private edit capabilities. Anyone with the open campaign link and a submitted Iqama can see that submission's current name/choice and edit it. This is not identity verification; approved policies, payroll, debts, bank details, directory records and prior review notes remain isolated. Every changed response returns to administrative review.
- POST `/api/public/forms/{token}/lookup` accepts only `{iqama}` (10 ASCII digits). It returns `{alreadySubmitted:false,iqama}` when absent, or `{alreadySubmitted:true,iqama,name,currentChoice,version,message}` when present. It never returns prior versions, review notes, bank data or financial details.
- POST `/api/public/forms/{token}` still accepts only `iqama,name,workSystem`. A duplicate returns HTTP409 with the same current-choice projection; it does not create another record. A first submission returns `receipt,iqama,name,workSystem,version:1,isEdited:false,status,submittedAt,message` and issues no edit token.
- POST `/api/public/forms/{token}/edit` accepts `iqama,name,workSystem` with optional `receipt,version`. It finds the existing record by campaign+Iqama (404 when missing). A supplied receipt must match; it is not an authenticator. A supplied stale version returns409; the current UI always supplies it. Legacy `editToken` input is accepted and ignored, with no code requirement. The old `/resume` route is retained for already-loaded0.5.1 clients with their historical receipt/capability; the new UI never calls it or stores response capabilities.
- Lookup, submit, resume and edit validate the campaign as open/unexpired again inside the transaction and share the public rate limit. A true change updates the same record, appends the previous name, choice, timestamp, version, status, reviewer and note to history, increments version, sets `isEdited:true,status:pending,updatedAt`, removes old current-review metadata, and audits `submission.edited` atomically. Identical retries preserve version/review. No prior versions are discarded.
- `submittedAt` is the response's current updatedAt or original createdAt. The localized `.txt` receipt uses these canonical server values (including actual status and version), not the download time. The UTF-8 BOM file is named `اشعار_تسجيل_الرواتب_[iqama].txt`, supports AR/EN/UR, and contains no edit capabilities. It confirms receipt of the selection, not financial approval. Personal answers remain in page memory, not localStorage/sessionStorage.
- Admin and Reviewer may review; Accountant cannot. Notes are optional. Quick and modal review send the displayed version; stale dialogs refresh instead of approving an unseen edit. Legacy IBAN ownership still requires detail review. `/api/submissions` and review results strip historical edit hashes.
- Pending edits do not recalculate payroll or change a policy; a later approval updates rider preference. The three-field Excel input template is unchanged. The response grid retains status/edited chips, multi-column filters, comparisons, typed version/edit/time/note exports and action exclusions.

## v0.5.3 campaign operations

- `DELETE /api/submissions/{id}`: Admin only, physical deletion and atomic `submission.deleted` audit. Missing ID is 404. Frees `(campaignId, iqama)` and removes reply history; financial ledgers and audit events remain. Clear only an attributable rider work preference, or restore the latest surviving approved preference.
- `POST /api/submissions/bulk-delete`: Admin only, `{campaignId, confirm:true, ids?:string[]}`. Campaign is mandatory; IDs, if supplied, must all exist within it. Without IDs deletes the entire campaign's replies. Transactional and audited, including each deleted ID.
- `POST /api/submissions/bulk-approve`: Admin or Reviewer, `{campaignId?:string, ids?:string[]}`. Approves pending replies only, records identity review and actor, updates preference without assigning payroll policy. Any pending legacy IBAN rejects the whole batch for individual ownership review. Repeating an already-approved batch changes zero replies.
- `PUT /api/campaigns/{id}`: Admin only, partial `{title?, expiresAt?, status?:"open"|"closed", fields?:["city","phone"]}`. No token rotation or reply loss. Unknown/duplicate fields rejected. Dates accept explicit offsets; the UI submits UTC.
- `GET /api/campaigns/{id}/analytics`: Admin or Reviewer. Counts `totalResponses/approved/pending/rejected/edited`, distributions `workSystem/city/engagementType` as `{value,count,percentage}`, and `recent` (latest five by updated/created time). Unknown directory attributes remain explicit. No public analytics route.
- Public campaign metadata includes `fields` (default empty for older campaigns). Enabled fields are required on create/edit, inactive fields rejected. City max100 characters; phone normalized E.164. Lookup returns enabled submitted values only, never values from the rider directory. Historical versions preserve city/phone; changing either increments version and resets review.
- Choices Excel template v3 adds city and phone after the canonical three columns. Older three-column files remain compatible for campaigns with no additional required fields. Preview/commit revalidates active fields and campaign state atomically.
- Campaign analytics denominators are all responses, including unknown classifications. Custom forms have no approval workflow; their KPI cards show that approval/edit counts do not apply.

Bulk requests optionally accept `versions:{id:number}`; the UI supplies each visible row version. Any mismatch aborts the entire operation with409, so a newly edited response cannot be approved or deleted from a stale list. Legacy campaign-only calls remain supported.

## Form deletion (0.5.4)

Admin only: `GET /api/campaigns/{id}/delete-preview` or `GET /api/forms/builder/{id}/delete-preview` returns `{id,title,responseCount,fingerprint}`. `DELETE` to the same parent path (without `/delete-preview`) requires a JSON body `{confirm:true,fingerprint}`. The snapshot covers the form and all its replies; any intervening submission, edit or review returns409 and changes nothing. The UI refreshes the count and requires a new explicit confirmation. Missing confirmation/fingerprint returns400; missing form404; staff403; unauthenticated401.

Deletes only the chosen form and its children atomically, records `campaign.deleted` / `builder.deleted` and per-response audit, and invalidates its public link. Rider directory and financial ledgers remain; attributable work preferences are reconciled via the existing response deletion logic. Draft, open, closed and expired forms use the same deletion path. Response history is removed with its parent response; audit records remain. No production form is deleted automatically during deployment.


## الإصدار 0.6.0 — عقود الحماية والترقية

- `GET /api/system/release-check` للمسؤول فقط: مزود التخزين، سلامة SQLite، أعداد وبصمة الوثائق، دليل النسخة الاحتياطية وحالات العلاقات اليتيمة. لا يُعرض لبوابة المندوب.
- `GET /health` يضيف `dataPreservationVerified` و`releaseCheckpointPresent` دون بيانات شخصية أو مالية. النجاح الصحي وحده لا يثبت صحة أي تكامل خارجي.
- `POST /api/inquiry-access` يقبل `scope=mobile` للمسؤول/المحاسب بعد مراجعة الهوية. الرمز أحادي الاستعمال مدته 10 دقائق، ويُملأ من جزء الرابط الذي لا يُرسل للخادم.
- `POST /api/rider-mobile/login`: `iqamaNo, phoneLast4, code, language`؛ لا يقبل الإقامة وحدها. جلسة 8 ساعات، بصمة الرمز فقط في التخزين، `Authorization: Bearer` حصريًا، مع فحص انتهاء الجلسة وحالة المندوب على كل قراءة أو إقرار إشعار.
- ملفات الوثائق في `App_Data/rider-documents`، وتنزيلها عبر API إداري محمي. تحديث الوصف دون مرفق يبقي الملف السابق، والتحديث يحتفظ بتاريخ الإصدارات.
- معاينتا `/api/debts/import-opening/preview` و`/api/debts/bulk-collect/preview-file` تعيدان `fingerprint`. الاعتماد يتطلب `rows, fingerprint, confirm:true`. أي تعديل للصفوف أو حالة المالية أو اختلاف المستخدم/انتهاء المعاينة يرجع 409 ويطلب معاينة جديدة. يُرفض المرجع المكرر ويُعاد التحقق داخل معاملة واحدة.
- القوالب لها أعمدة ثابتة بنفس ترتيب التنزيل؛ يقبل CSV الإنجليزي الموثق وExcel العربي الصادر من النظام. لا تُستورد الصفوف الصحيحة وحدها من ملف ذي أخطاء.
- الديون الملغاة/المنتظرة لا تدخل كشف المديونية المعتمد، ولا تُفعّل بتجاوز مرحلة الاعتماد. الأرصدة والسداد السابق للديون القديمة محفوظة دون ترحيل قيود قديمة تلقائيًا.
- `deliveryChannel=in_app` يعني إشعارًا محفوظًا داخل البوابة؛ لا يعني تسليم FCM. `fcmRegistered=false` حتى يتوفر تكامل إرسال خارجي فعلي؛ حفظ إعدادات المشروع وحده لا يثبت الاتصال.


تحديث0.6.1: ربط سجل دائن لدين قديم عبر PUT /api/debts/{id} يتطلب Admin وcreditorSponsorId وagreementReviewed وexpectedRemainingBalance مطابقًا؛ لا يعيد إنشاء الدين أو تغيير رصيده. يمنع التحصيل الجديد على دين غير مربوط، مع استمرار دفتر الرواتب القديم دون قيد شركة افتراضي.
