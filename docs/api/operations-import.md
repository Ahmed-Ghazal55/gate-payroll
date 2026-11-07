# Per-app operations review

D3: Ninja → Keeta → HungerStation. `ImportCatalog` owns the template columns and URLs. No UI changes.

## Input / output

`POST /api/pay/apps/{ninja|keeta|hs}/operations/import/preview` accepts JSON `{invoiceId,dataBase64}` (one XLSX, ≤8 MiB, ≤24,999 data rows). Use the catalog template, remove its marked example, and enter local `platformAccountId`, actual operator `iqama`, period, orders, baseGross, deduction, days and one of the six pay categories. `car`/`bike` alone do not establish sponsorship. Names are ignored.

Optional from/to dates must both be ISO dates within the invoice month, covered by an actual operator assignment. Without dates, the source assignment must overlap that month. Registered account ownership never establishes an operator. Days and metrics describe the rider's whole app-month: identical across accounts; orders, baseGross and deduction are account shares. Days are actual calendar days; existing payroll billing caps apply later.

Returns `{rows:[review],fingerprint}`. Review contains only the existing `PayCycles` contract:

```json
{"invoiceId":"synthetic-invoice","app":"ninja","accounts":[{"accountId":"synthetic-account","sourceLineId":"synthetic-line","baseGross":100,"incentives":10,"orders":5,"riders":[{"rider":"2000000091","orders":5,"baseGross":100,"deduction":0,"category":"sponsored_private","days":1,"metrics":{"workingHours":8}}]}],"deductions":[]}
```

`POST .../commit` accepts the identical invoiceId/dataBase64 plus returned rows/fingerprint and `confirm:true`. HMAC binds actor, bytes, rows and current source state for 20 minutes. Inside one serialized transaction the server re-parses and validates again, then saves `operations_import` and Audit only. Re-importing the identical file/invoice refuses. No cycle, journal, installment or debt is created. `GET /api/pay/apps/{app}/operations/imports/{id}` returns the saved review; pass its `review` as an entry in the normal cycle `invoices` array. Revalidate through the existing cycle stages and independent approval; saved review is not an authorization bypass.

## Source and reconciliation

Source invoice must be approved, same app/company/month, unposted/unreversed, with explicit deductionsTotal. Every account needs exactly one source line with baseGross, incentives and orders; every account's actual rider sums must match baseGross/orders. Incentives remain at account level for the existing order-proportional cycle allocation. Every account must have operators, including zero lines; extra/duplicate operator-account rows refuse. Negative/unclassified deductions refuse. Company deductions stay company-only; account deductions require explicit rider allocation summing exactly to the approved source components. No expected-performance recalculation replaces final supplier facts (BR31).

**Native HS limitation:** its historical raw import can have platformId/riderNet without normalized account gross, incentives or deductionsTotal. That source blocks with `source_line_missing_invoice_gross` (or missing source total); neither courierBasicPayment/basicPayment nor riderNet is guessed as revenue. Use a reviewed normalized invoice source under the existing cycle contract. This implementation does not silently mutate an approved HS invoice or create a replacement invoice.

## Metric evidence (header names only)

| App | Source headers | Catalog numeric metrics |
|---|---|---|
| Ninja | Total Working Hours; Active Working Hours; Committed Working Hours; Shift Compliance %; Attendance On-Time % | workingHours, activeWorkingHours, committedWorkingHours, shiftCompliancePercent, onTimeAttendancePercent |
| Ninja monthly / invoice | attendance days; days under 8 hours; weekend days | attendanceDays, hours8Days, weekendDays (reviewed monthly counts, never inferred from an average) |
| Keeta | Online Day / أيام الاتصال-صالحة; Shift Online hours; Sum of Valid Shifts; peak-duration controls | validOnlineDays, onlineHours, validShifts, peakHours |
| HS | completed_orders الطلبات المكتملة; عدد الكيلوات | orders (core), distanceKm; hours optional and never invented when absent |

captainId/keetaCourierId/hsRiderId/city/slab remain optional reference columns; they do not become numeric eligibility metrics. Blank metrics are omitted so missing evidence remains `needs_decision` when required by pay rules. No cross-app eligibility assumptions.

## Authorization and failure behavior

Preview/commit require pay.manage; saved read pay.view, with company, platform and actual-rider scopes checked server-side. Financial state changes, actor changes, bytes/row tampering, missing cached formulas, account mismatches, unknown/debt-only riders, absent assignments, invalid dates/categories, and allocation differences refuse before writing. Errors expose row numbers/reasons, not uploaded identities. Unit tests feed all three app outputs through actual cycle stages 0–3; HTTP tests exercise the registered routes with temporary synthetic SQLite only.
