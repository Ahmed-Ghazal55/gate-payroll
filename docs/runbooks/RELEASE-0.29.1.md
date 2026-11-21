# Release 0.29.1 (2026-11-19)

Security fix for the forms and rider-app section released in 0.28.0 (review R28, fixes S28, verification R28V).

## What changed for users
- **IBAN masked by permission.** Form responses (`/api/responses`, `/api/submissions`, custom form responses, request replies and decisions) show only `••••` + the last 4 digits unless the user has `riders.iban.view`. The «مراجعة الردود» Excel/CSV export drops the IBAN column for those users.
- **Company scope on forms.** A user limited to some companies sees, reviews and bulk-approves only the responses of riders in those companies. Others return «الرد غير موجود» (404). Campaign analytics count only in-scope responses. Forms staff with no company assigned keep seeing all responses (as before).
- **Public edit token.** A new response returns a private edit token, saved in the rider's browser session. Editing a response needs that token (checked in constant time); a missing or wrong token returns 404 and shows nothing. The public lookup by iqama no longer returns phone or IBAN.
- **Sidebar and API permissions match.** Builder list opens with `forms.view` (changes still need `forms.manage`); statements preview works for `mobile.broadcast` (approved statements only); rider pickers work for `mobile.broadcast`, `mobile.sessions`, `inquiry.issue`; the requests tab opens for confidential and IBAN approvers; Excel export works for forms and broadcast viewers.
- **Requests.** Decisions are limited by request type (confidential-only or broadcast-only users cannot approve leave or appointments); replies on closed requests are refused; «second approve» is not offered to the first approver. The request drawer shows IBAN (masked by permission), data changes, complaint details, replies and timeline.
- **Statements panel.** No false «not approved» warning; deductions are shown so the cards add up to the net.
- **Other fixes.** Builder cards use the real form title, field count and open status; analytics use real numbers; server-side filters and full export on broadcasts and requests; all 32 Saudi bank codes in exports; IBAN split only on real IBAN columns; Arabic labels for work systems, sessions and settings.

## Ops note for Ghazal
- Responses submitted before 0.29.1 have no edit token, so riders cannot edit those old answers from the public link (they get «الرد غير موجود»); staff can still review those answers. New responses are not affected.
- Optional: grant `riders.iban.view` only to roles that must see full IBANs on forms.

## Live safety
- No schema, seed, data or setting change on first boot. No ERP writes. Edit tokens are stored as a hash on new responses only.
