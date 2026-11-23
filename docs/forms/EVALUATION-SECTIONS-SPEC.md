# Evaluation by sections — spec (Ghazal, 2026-11-23)

Status: agreed with Ghazal, to build. Branch `feat/evaluation-sections` (from origin/main c23c7e3 = 0.33.4).
Replaces the two-page form (all staff, then all platforms). Base design and rules still apply: `EVALUATION-SPEC.md` (on branch fix/cascading-column-filters) — confidentiality notice, no skip (reason instead), 1–10 staff ratings, notes text or voice, complaint button, 2 images + 1 voice per card, 6-month file retention, edits ≤ 3 until close, nationality, management-only permission, Excel one value per cell.

## Why
One long form is too hard for a rider. Split it into short forms, each with its OWN link, so management shares each form only with the riders it targets.

## Sections (one form + one link each, inside one round)
| key | Form | Staff in it | Extra cards at the end |
|---|---|---|---|
| `dept:movement` | الحركة والمركبات | department «الحركة والمركبات» | — |
| `app:<Platform>` (one per platform in PayPlatforms) | تطبيق <name> | staff whose `apps` contain the platform + staff with `allApps` (operations manager) | the platform card (overall 1–5 + 8 factor stars, as now) + the FINANCE card |
| `dept:finance` | المالية والحسابات | department «المالية والحسابات» (rated one by one) | — |
| `dept:hr` | الموارد البشرية | department «الموارد البشرية» | — |
| `dept:support` | الكول سنتر والدعم الفني | department «الدعم الفني والكول سنتر» (allCities) | — |
- The operations department is NOT a section: its people appear in the app forms through `apps` / `allApps`.
- A person in several sections appears in each section's form and is rated there. In results each person has ONE combined card (all sections together) with a per-section breakdown.
- Manual membership per round section: management can ADD a staff member to a section or REMOVE one from it (overrides on top of the rule above).
- City: every form asks the city first; only staff serving that city appear (allCities staff appear in every city). Work cities = as in 0.33.4 (`WorkCities`).
- App forms are open to anyone with the link; reasons stay («لم أتعامل معه / لا أعرفه / لم أعمل في منصته»; platform: «غير متاح في مكتب المدينة / لم أعمل عليها»).
- No shared start page. The rider app does not list sectioned rounds (links only for now).

## Finance card (inside every app form; the department as a whole, no names)
- Overall 1–5 stars, or a reason «لم أتعامل مع القسم المالي».
- Optional 1–5 stars, focused on the finance role that affects the rider:
  - `responsiveness` تجاوب القسم المالي مع استفساراتك وشكاواك
  - `accuracy` احتساب الراتب بدون أخطاء
  - `correctionSpeed` سرعة تعديل الأخطاء والفروقات بعد الإبلاغ
  - `clarity` وضوح تفاصيل الراتب والخصومات
- Notes (text or voice), attachments (2 images + 1 voice), complaint button (complaint → RiderAsks, targetKind `department`, target «المالية والحسابات», app in payload).

## Suggested answers (make filling easy)
Each notes / complaint box shows tap-to-add chips that DESCRIBE the answer (rider can edit the text after). ar/en/ur.
- Staff (all sections): «متعاون ويرد بسرعة» «يحل المشاكل من أول مرة» «محترم في التعامل» «لا يرد على الاتصالات أو الرسائل» «يتأخر في حل المشاكل» «أسلوب تعامل غير مناسب» «لا يشرح الإجراءات بوضوح» «يفرّق في المعاملة بين المناديب».
- Movement extra: «تأخر صيانة المركبة» «مشاكل في السكن» «تأخر توفير مركبة بديلة».
- HR extra: «تأخر الإقامة أو الأوراق الرسمية» «تأخر الرد على طلبات الإجازة».
- Finance (staff + card): «الراتب ناقص أو فيه خصم غير مفهوم» «تأخر الصرف» «تأخر تعديل الفرق بعد الإبلاغ» «لا يوجد رد على استفسار الراتب» «الحساب واضح وصحيح».
- Support / call centre extra: «طول الانتظار على الخط» «لم تُحل المشكلة بعد المكالمة».
- App card: «الطلبات قليلة» «أوقات الشفتات غير مناسبة» «مشاكل في التطبيق نفسه» «خدمة عملاء المنصة لا ترد» «الشغل يستاهل».

## Replies
- One reply per rider per SECTION per round (id = round + section + iqama). Edits ≤ 3 per section reply, until the round closes; identity/city locked on edit.
- Identity (iqama, name, nationality, phone, work apps) is remembered on the device for the next form; the server still checks.
- Excel and tables: a `section` column; finance card columns (overall, 4 factors, reason, notes, attachments count, complaint).

## Round management (settings panel — full rework, Arabic, professional, simple)
- Staff editor: edit each person directly (name, cities, departments, apps, roles, allCities, allApps, excluded, active) — no JSON pasting (keep the JSON import as an advanced option).
- Round screen: list of sections, each with its link + copy button + reply count + add/remove staff for that section.
- Actions by `allowedActions`: open, pause, close, refresh-staff, DELETE.
- Delete round (management + forms.manage, confirm by typing the round title): deletes the round, all its replies, their attachment files on disk, and the complaints it created in RiderAsks. Audit entry kept (counts only, no names).

## Dashboard
- Section filter on every view; each person = one combined card (all sections), with a breakdown by section; finance card stats per app (overall + factor averages, counts).

## Data changes to apply on live after release (needs Ghazal)
- Close and delete the current round (Ghazal 2026-11-23: «لا هنقفلها و نحذفها من دلوقتي»; complaints too: «امسح»).
- Re-import the private staff file (HR: one person excluded; two operations staff gain apps; operations manager `allApps`).
- Create the new sectioned round, open it, share each link with its target riders.
