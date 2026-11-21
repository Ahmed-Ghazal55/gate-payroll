# Release 0.28.0 (2026-11-19)

## What changed for users
- One main section «تطبيق المناديب والنماذج» replaces «النماذج والردود» and «تطبيق المناديب». Its 8 sidebar entries: الحملات والروابط · مراجعة الردود · مصمم النماذج · الحسابات والأجهزة · الإشعارات والإرسال · إرسال كشوف الحساب · الطلبات والمواعيد · إعدادات التطبيق. Old `/app/forms/?tab=…` and `?tab=statement-preview` links redirect. Public form links `/forms/<token>` are unchanged.
- Excel exports (every table): an IBAN column is split into الآيبان / كود البنك / اسم البنك; iqama, phone and IBAN stay text; empty values stay empty. Screens are unchanged. The workforce export gets the two bank columns too.
- Campaigns: KPI cards, analytics as an inner view, «معاينة على عرض الجوال» opens the real form in a phone-size window.
- Notifications: general broadcast to all; individual notices (استدعاء، إنذار فصل، خصم إداري، تنبيه موعد، إنذار إداري) go to one rider only — the server still rejects them for ALL; paged send log with read/ack counts; 3 languages.
- Statements: preview of a rider's approved statement at phone width; «إرسال إشعار الكشوف المعتمدة للفترة» shows how many riders were notified (no amounts in the notice).
- Requests: real queue of rider requests (leave, appointments, IBAN/data changes, statements, complaints) with server paging, counts and buttons from the server; the existing approval rules are unchanged. Advance asks stay in «المديونيات والسلف».
- Accounts: staff can issue a one-time app login link after confirming the rider's identity (permission `inquiry.issue`).
- Settings: real counts from the server; «غير مهيأ» instead of fake Firebase values.
- New endpoint `GET /api/rider-mobile/service-requests/summary`; the queue hides the full new IBAN of an IBAN-change request unless the user has `riders.iban.view` or the IBAN approver permissions.

## Ops step for Ghazal (after deploy)
- Check who should see the section: entries follow `forms.view`, `mobile.sessions`, `mobile.broadcast`, `forms.review`; settings are Admin only; the login-link button needs `inquiry.issue` (plus the server's full-scope rule).

## Not in this release (waiting for Ghazal / other owners)
- Bank payroll file shape and first column = bank short code, CSV/Excel choice (R5/R7 bank part); Mudad file editing (R5) — money.
- Who approves rider requests (open questions 4 and 5) — current rules kept.
- Form-builder texts in 3 languages instead of fixed texts (X11); reply deadline per request type (X8); broadcast filtered by company/platform; the rider phone app itself (R6, Antigravity).

## Live safety
- No schema, seed, data or setting change on first boot. New reads are SELECTs on our own records; no ERP writes.
