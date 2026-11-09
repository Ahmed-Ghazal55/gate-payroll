# HANDOFF

| Field | Value |
|---|---|
| Current writer | Cursor 2026-11-09 20:56 +03 |
| Branch | release/0.20.0 (pending T5 commit) |
| Updated | 2026-11-09 by Cursor (R11 mid) |
| Live | see `/health`; DB db77738 |

## Next step (exact)
Continue R11: T4 kinds → T7 ValidateRiders → T8 UI → T9 PR `release: 0.20.0`.

## R11 done so far
- T1 timings + design; baseline wall ~14.4 min (identity refuse).
- T2 watermarks / incremental / open-period soft-delete / full-resync.
- T3 contentHash + batch mirror lock + plan dictionaries.
- T5: Chefz FT §36; vehicle rent cap §38.4 (shared vehicle); housing by residence days; Jahez reconciliation §37.
- T6 `approval.bypass_maker_checker` grants + badge + admin APIs; legacy ghazal evidence kept.

## Goal now
First real Ninja payroll for 2026-10 in the browser. Then Keeta September. Open items audit: `~/.agents/NEXT.md`.

## 0.19.0 (R10 + P2 + R9 web)
- **ERP load done on db77738** (2026-11-08): 106,752 new, 382 changed, verified on the same snapshot. Audit grew to about 117k rows, one per loaded record.
- **The importer hung on Linux** in `SqlBulkCopy` over a MARS session (seen in a stack trace); it now turns MARS off for its own connection only.
- **Assignment bug found by the real-data rehearsal:** ERP sends `EffectiveFrom/To`, the code read `RawEffectiveFrom/To`, and every test seeded the wrong name. `AssignmentSpan` reads both.
- **Rehearsal on a read-only copy of live data, October:** 191 of 207 accounts resolve operator and category automatically. Left: settings, scheme assignment for everyone, 13 accounts with no daily and not exactly one assignment, 2 multi-operator accounts with a proposal, 1 category. ERP data ends 24 Oct (TR) and 29 Oct (BW, EX); the invoice stays final.
- **Payroll API:** partial approval, negative carry on `112507`, §22.9.2 floor, debt stage 8 (any debt, up to balance and net), discard unapproved invoices, fingerprint limited to the cycle's own facts.
- **Access:** user edit dialog, role permission editor, temporary passwords, delegated `users.manage` with an escalation guard, dated self-approval exception on `ghazal` only, Supervisor role.
- **Sync button:** administrators can accept named kinds per run with a reason; the report lists changed fields.
- **Web:** Keeta screen, searchable selects, next-step cards, pay settings screens, stage 8, negative row decision, adjustment list, posting gate, invoice discard.

## Not done
- D3 scheme assignment proposal from ERP `PaymentModel` (1 PayPerOrder, 2 Salary): use the assignments screen.
- Custom new roles (editing existing roles works). Inline category choice and accepting a multi-operator proposal on screen: use the operations import.
- R9 P3 ERP deductions into payroll, P6 settings gaps, P0 Keeta real parsing, P1 shared riders, P7 deduction requests, P4 Chefz, P5 Jahez. Codex resumes at P3.
- Enabling the hosted sync on MonsterASP (`Jobs__Enabled`, `Jobs__ErpSync__Enabled`, `ConnectionStrings__ErpReadonly`): Ghazal sets them.

## Ghazal's decision pending
2 negative Ninja captains are on Salary, 2 are not in ERP daily. They stay blocked (`negative_ninja_salary_requires_policy`) without stopping the others.

## Proposed BR text for Ghazal to send Omar (BR file untouched)
- §24.7: «تصل الحالات المحتاجة قرارًا إلى جدول المراجعة ولا توقف اعتماد الصفوف الجاهزة. تسبق الحوافز والتعويضات والتعديلات الاعتماد النهائي. عند الاعتماد النهائي فقط يُرحّل عجز العمولة أو الفاتورة المستحق على المندوب إلى NegativeSalaryCarryover على 112507 بقيد متوازن، مع بقاء استثناء §22.9.2؛ والموجب مستحق للمندوب. يلي اعتماد الرواتب تأكيد مستقل لاستقطاع الديون ثم ملف البنك.»
- §29: «عجز D والوقود لمندوب راتب كيتا غير المؤهل تكلفة شركة على 512121، ولا ينشئ مديونية. تسترد الخصومات الشخصية بحد الصافي الموجب؛ الباقي يظل ذمة مفتوحة دون صافي سالب أو مصروف شركة. لا يرحّل العجز الآخر قبل انتهاء المراجعة. تؤكد الأقساط بعد الرواتب لكل مندوب أو جماعيًا، من صفر إلى المستحق وبحد الصافي الموجب؛ الصفر تأجيل بلا حركة، وتحدث الذمة والقيد معًا دون تكرار.»
- §32: «تأتي بنود الحوافز والتعويضات والتعديلات من قائمة مالية معتمدة تحدد الحساب والاتجاه. كل تعديل بسبب مكتوب وله قيد متوازن. اعتماد الصفوف الجاهزة لا يعتمد الحالات المعلقة؛ يبقى المعتمد مستقلًا عن التجهيز والمراجعة. بعد اعتماد الراتب يؤكد مراجع مستقل اختيار استقطاع الدين أو تأجيله، ولا يدخل المندوب ملف البنك قبله. التصحيح بعد الاعتماد بالعكس فقط.»

