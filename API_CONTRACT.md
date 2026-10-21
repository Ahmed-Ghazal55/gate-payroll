# API v0.4

Same-origin JSON, authenticated HttpOnly cookie for `/api/*` except `/api/auth/*` and `/api/public/forms/*`. Errors use `{error:string}` with 400/401/403/404/429/503. No financial mutation from public routes. Source of truth: `Payroll.Api/Program.cs`, `Engine.cs`, `Imports.cs`; integration tests are executable request examples.

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
| Campaign | GET/POST `/api/campaigns`; POST `/{id}/close` | title,days1..30 → id,path raw token shown once,expiresAt |
| Responses | GET `/api/submissions`; POST `/{id}/review` | status approved/rejected,note,identityReviewed,ibanOwnershipReviewed if IBAN provided |
| Audit | GET `/api/audit` | all stored actor/action/entity/time |
| Public form | GET `/forms/{token}`; GET `/api/public/forms/{token}` | form HTML / title,expiresAt,schema:work-choice-v1; no existing rider information |
| Public submit | POST `/api/public/forms/{token}` | name,iqama,workSystem only → receipt,message; extra fields reject |

Public workSystem values: `tiers` = نظام الراتب الجديد / `commission` = نظام العمولة السابق. No third choice, no default, no consent input. Public iqama is a 10-character ASCII digit string; Arabic/Persian digits reject instead of normalization. Name 3..150 characters, at least two parts, Arabic/English letters and spaces, whitespace normalized. Duplicate `(campaignId, iqama)` rejects. Existing name/phone/IBAN records never exposed by public API. Approval preserves existing private rider data and records workPreference only, without assigning policyId.

Platform values for payroll import: `Keeta/HungerStation/Ninja/Chefz/Jahez/Amazon/Other`. Unmapped financial values remain null. Policy and payroll APIs retain their existing contracts. Legacy submissions retain their historical fields; bank ownership confirmation only applies if an old response includes an IBAN.

Never call the fingerprint endpoint just before approving to replace the displayed preview's fingerprint: doing so would silently approve unseen changes. Rendering already approved periods uses run.result, not recalculation against a newly assigned policy. Never place imported raw rows or private admin responses in public form state or browser storage. Policy assignment and workPreference are separate fields.


Excel endpoints (authenticated): see TABLE_STANDARD_AR.md for exact headers and examples. GET /api/excel/{choices|riders|work}/template; POST /api/excel/export with headers,rows,numericColumns; POST /api/excel/{choices|riders}/preview and /commit (filename,dataBase64,campaignId for choices,fingerprint for commit). Returns row-numbered errors; rejects changed schema, formula input, duplicates, closed campaigns, stale fingerprint or invalid policy; all-or-nothing new records only. Imported choices remain pending.


Release0.4: Production API requires HTTPS. First admin setup requires an explicit, non-placeholder bootstrap secret of at least32 characters; missing configuration returns503 and an incorrect secret rejects. Additional proxy IPs must be explicitly trusted; forwarded Host is not consumed. Public form contracts are unchanged. Administrative rider names use DirectoryProfiles.Name: source names may be mononyms, Unicode letters, dots, hyphens or apostrophes; digits, markup and control characters reject. Excel directory identities remain10 ASCII digits starting1/2. Administrative imports remain needs_review and never assign a policy automatically.
