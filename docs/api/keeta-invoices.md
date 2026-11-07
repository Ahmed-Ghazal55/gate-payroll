# Keeta supplier invoice import (R6c D2)

## Source column map, written before the importer

Arabic supplier-invoice sheets observed under the approved monthly-invoice and slab-settlement folders. Header matching uses the supplier prefix ending at the first `إجمالي المبلغ المستحق`; appended local payroll/debt/IBAN columns are ignored. Select sheetName explicitly when several supplier sheets exist; do not import both detail and summary sheets.

| Header | Meaning / sign | Level |
|---|---|---|
| المعرف | Exact platform account external ID; never an actual-rider identity | account |
| الطلبات المُسلمة | Delivered orders, nonnegative integer | account |
| أيام الاتصال-صالحة | Valid online days, source metric only | account |
| ساعات الاتصال اليومي-صالحة | Online duration, source metric only | account |
| ساعات الاتصال اليومي خلال وقت الذروة-صالحة | Peak duration, source metric only | account |
| صالح | Invoice eligibility label, retained for comparison | account |
| المركبة | Source vehicle capability label | account |
| الإجمالي (or final الاجمالي before amount due) | Final invoice amount excluding VAT, including supplier adjustments | account |
| إجمالي المبلغ المستحق | Final amount including VAT; VAT is the explicit difference, never guessed from an absent column | account |
| الخصم / خصومات أخرى / غرامة مُخالفة | Deduction magnitude, company/account classification required | classified |
| تعويض عن تلف الطعام / تعويض طعام | Food-damage deduction magnitude | classified |
| رسوم خدمة التسجيل | Registration deduction magnitude | classified |
| خصم TGA (بدون ضريبة القيمة المضافة). | TGA deduction magnitude; no second local deduction | classified |
| خصم رسوم الخدمة / خصم مكافأة التوصيل على الموعد / خصم إعانة التاجر | Declared supplier deductions | classified |
| تعديل آخر / تعديلات أخرى | Negative adjustment is a deduction; positive value stays inside final invoice revenue | classified |
| Names, account-holder identities, phones, local substitute/payroll/debt/fuel/bank columns | Never used to infer the actual worker or recalculate salary | ignored |

Declared deductions are added back to final net to expose gross invoice revenue; positive bonuses are already included in that revenue and are not added a second time. BaseGross is that invoice gross; incentives=0 in the normalized line to avoid double counting. The original final net, inclusive total, VAT and individual deduction categories stay separate. Expected calculation differences are informational only (BR31). Missing distinct ex-VAT/inclusive fields, invalid caches, conflicting headers and unknown account links are refused rather than inventing a VAT or worker split.

Company rows require explicit row selection in companyRows and no account ID; totals/footer rows are not company deductions. Adjustments stay company-level and require independent expense classification. For an agent or mixed payroll use the pay cycle; standalone posting is allowed only for explicit accountingRole=principal. It uses PayPlatforms.For(keeta) and explicit invoice VAT.

## HTTP contract

- `POST /api/platforms/keeta/invoices` multipart: exactly one file (xlsx, max8MiB), companyId, period, reference, optional sheetName, accountingRole (principal|cycle_only, default cycle_only), optional companyRows JSON array and odooVoucherNo. Preview returns rows and fingerprint. Nothing is saved.
- Same endpoint `?commit=1`: repeat identical file/options with rows, fingerprint and confirm=true. Signature binds actor, file bytes and current invoice/account/company/period state. Duplicate bytes or reference are refused.
- `GET /api/platforms/keeta/invoices`; `GET /api/platforms/keeta/invoices/{id}/lines`: company/platform scoped.
- `POST .../{id}/classify`: lineId, category, level (company_level|account_level), expenseAccount for company-level. Importer/classifier cannot approve their own facts.
- `POST .../{id}/approve`: pay.approve, independent checker; final source values are retained.
- `POST .../{id}/dispute`: reason; `POST .../{id}/resolve`: disputeId and reason, independent checker.
- `POST .../{id}/post`: pay.approve, explicit principal role, open period, platform posting gate and resolved disputes. It refuses account-level deductions until actual-worker allocation through the cycle, and refuses invoices reserved by any active cycle. Replay never posts a second journal.

Permission policy: pay.view for reads, pay.manage for preview/confirm/classification/dispute, pay.approve for approval/resolve/post. HMAC previews and all business writes use existing Store transactions. Keeta deductions carry Keeta provenance, with no invented rider debt. Source definitions: real file headers only; tests use synthetic values and loopback APIs exclusively.
