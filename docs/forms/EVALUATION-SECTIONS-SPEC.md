# Evaluation by sections — spec (Ghazal, 2026-11-23; EV6 2026-11-24)

Status: agreed with Ghazal, to build. Branch `feat/evaluation-ev6` (from origin/main 5185b32 = 0.33.5).
Replaces the two-page form (all staff, then all platforms). Base design and rules still apply: `EVALUATION-SPEC.md` (on branch fix/cascading-column-filters) — confidentiality notice, no skip (reason instead), 1–10 staff ratings, notes text or voice, complaint button, 2 images + 1 voice per card, 6-month file retention, edits ≤ 3 until close, nationality, management-only permission, Excel one value per cell.

## Why
One long form is too hard for a rider. Split it into short forms, each with its OWN link, so management shares each form only with the riders it targets.

## Sections (one form + one link each, inside one round)
| key | Form | Staff in it | Extra cards at the end |
|---|---|---|---|
| `dept:movement` | الحركة والمركبات | department «الحركة والمركبات» | — |
| `app:<Platform>` (one per platform in PayPlatforms) | تطبيق <name> | staff whose `apps` contain the platform + staff with `allApps` (operations manager); city filter uses optional `appCities[app]` when set | the platform card + FINANCE card + SYSTEMS card |
| `dept:finance` | المالية والحسابات | department «المالية والحسابات» (rated one by one) | — |
| `dept:hr` | الإدارة والموارد البشرية | department «الموارد البشرية» OR «الإدارة» | — |
| `dept:support` | الكول سنتر والدعم الفني | department «الدعم الفني والكول سنتر» (allCities) | — |
- The operations department is NOT a section: its people appear in the app forms through `apps` / `allApps`.
- A person in several sections appears in each section's form and is rated there. In results each person has ONE combined card (all sections together) with a per-section breakdown.
- Manual membership per round section: management can ADD a staff member to a section or REMOVE one from it (overrides on top of the rule above).
- City: every form asks the city first; only staff serving that city appear (allCities staff appear in every city). Work cities = cities of city-bound staff **plus** cities listed in any `appCities` (so a prior city stays pickable). App section city filter: `appCities[app]` when present and non-empty, else `cities` / `allCities`. Department sections ignore `appCities`.
- App forms are open to anyone with the link; reasons stay («لم أتعامل معه / لا أعرفه / لم أعمل في منصته»; platform: «غير متاح في مكتب المدينة / لم أعمل عليها»).
- No shared start page. The rider app does not list sectioned rounds (links only for now).

## Confidentiality notice (EV6 R1)
First step of every form: highlighted box (icon + 3 lines), not small grey text:
1. تقييمك وشكواك سرية تمامًا.
2. لا يرى المشرف أو الموظف الذي تقيّمه اسمك ولا إجابتك؛ تراجعها الإدارة المخوّلة فقط لتحسين العمل وحل المشاكل.
3. لن يُستخدم أي تقييم أو شكوى ضدك بأي شكل. تكلّم بحرية واشرح مشكلتك بالتفصيل.
(en + ur too.) Reminder under every complaint details box and on the review step: «شكواك سرية ولن تُستخدم ضدك.» Access rules unchanged (CanView = management with view permission; supervisors never).

## Finance card (inside every app form; the department as a whole, no names)
- Overall 1–5 stars, or a reason «لم أتعامل مع القسم المالي».
- Optional 1–5 stars, focused on the finance role that affects the rider:
  - `responsiveness` تجاوب القسم المالي مع استفساراتك وشكاواك
  - `accuracy` احتساب الراتب بدون أخطاء
  - `correctionSpeed` سرعة تعديل الأخطاء والفروقات بعد الإبلاغ
  - `clarity` وضوح تفاصيل الراتب والخصومات
- Notes (text or voice), attachments (2 images + 1 voice), complaint button (complaint → RiderAsks, targetKind `department`, target «المالية والحسابات», app in payload).

## Systems and data card (EV6 R2 — inside every app form; department card, no names)
Order in an app form: staff cards → platform card → finance card → systems card.
- Title «تقييم الأنظمة والبيانات» (en "Systems and data", ur).
- Overall 1–5 stars, or reason «لم أستخدم نماذج أو بيانات الشركة» (`not_used_forms_data`).
- Optional 1–5 factors: `formsEase`, `dataClarity`, `helpfulness`, `responsiveness` (labels in schema ar/en/ur).
- Notes / attachments / complaint like finance; complaint target «الأنظمة والبيانات», visibility like finance department complaints.
- Chips (ar/en/ur): «النماذج سهلة وواضحة» «البيانات اللي بتوصلني فيها أخطاء» «الجداول غير واضحة» «لا يوجد رد على مقترحاتي أو طلباتي» «الروابط لا تعمل أو بطيئة».
- Stored as `systemsAnswer` (finance stays `financeAnswer` for backward compatibility). Excel: systems columns once per reply. Scorecards: systems stats per app. Old replies with only financeAnswer still export.

## Suggested answers (make filling easy)
Each notes / complaint box shows tap-to-add chips that DESCRIBE the answer (rider can edit the text after). ar/en/ur.
- Staff (all sections): «متعاون ويرد بسرعة» «يحل المشاكل من أول مرة» «محترم في التعامل» «لا يرد على الاتصالات أو الرسائل» «يتأخر في حل المشاكل» «أسلوب تعامل غير مناسب» «لا يشرح الإجراءات بوضوح» «يفرّق في المعاملة بين المناديب».
- Movement extra: «تأخر صيانة المركبة» «مشاكل في السكن» «تأخر توفير مركبة بديلة».
- HR extra: «تأخر الإقامة أو الأوراق الرسمية» «تأخر الرد على طلبات الإجازة».
- Finance (staff + card): «الراتب ناقص أو فيه خصم غير مفهوم» «تأخر الصرف» «تأخر تعديل الفرق بعد الإبلاغ» «لا يوجد رد على استفسار الراتب» «الحساب واضح وصحيح».
- Systems card: see chips above.
- Support / call centre extra: «طول الانتظار على الخط» «لم تُحل المشكلة بعد المكالمة».
- App card: «الطلبات قليلة» «أوقات الشفتات غير مناسبة» «مشاكل في التطبيق نفسه» «خدمة عملاء المنصة لا ترد» «الشغل يستاهل».

## Replies
- One reply per rider per SECTION per round (id = round + section + iqama). Edits ≤ 3 per section reply, until the round closes; identity/city locked on edit.
- Identity (iqama, name, nationality, phone, work apps) is remembered on the device for the next form; the server still checks.
- Excel and tables: a `section` column; finance card columns; systems card columns (overall, 4 factors, reason, notes, attachments count, complaint).

## Round management (settings panel — full rework, Arabic, professional, simple)
- Staff editor: edit each person directly (name, cities, departments, apps, roles, allCities, allApps, excluded, active, optional `appCities` per app «مدن هذا التطبيق») — no JSON pasting (keep the JSON import as an advanced option). Department chips include «الإدارة».
- Round screen: list of sections, each with its link + copy button + reply count + add/remove staff for that section.
- Actions by `allowedActions`: open, pause, close, refresh-staff, DELETE.
- Delete round (management + forms.manage, confirm by typing the round title): deletes the round, all its replies, their attachment files on disk, and the complaints it created in RiderAsks. Audit entry kept (counts only, no names).

## Dashboard
- Section filter on every view; each person = one combined card (all sections), with a breakdown by section; finance + systems card stats per app (overall + factor averages, counts).

## Data changes to apply on live after release (needs Ghazal)
- Close and delete the current round (Ghazal 2026-11-23: «لا هنقفلها و نحذفها من دلوقتي»; complaints too: «امسح»).
- Re-import the private staff file (HR: one person excluded; two operations staff gain apps; operations manager `allApps`).
- Create the new sectioned round, open it, share each link with its target riders.

## Staff import and delete (Ghazal, 2026-11-24)
- Import: a NON-EMPTY list in the file REPLACES the stored one (cities, departments, roles, apps, appCities); it is not merged. A missing, null or EMPTY list keeps the stored one, and the result lists those cards as kept. Clearing a whole list is done in the staff editor only. An app written with no cities keeps its stored cities, even when a sibling app has cities; an app omitted from a non-empty appCities map is removed. Blank text is rejected. allCities / allApps stay as stored when absent. People not in the file are untouched. Several rows of one person in the same file still combine.
- Delete card: management + forms.manage, confirm by typing the name. Existing rounds keep their copy until «تحديث الموظفين». If the name stays in the import file, the next import creates the card again.
