# Gate Payroll — دليل المعرفة

آخر إعادة تحقق محلية: **2026-10-27**، بعد إنشاء الحزمة في 2026-10-26. هذه حزمة معرفة للإصدار `0.7.0`؛ لا تمثل اعتمادًا للأرصدة أو فحصًا جديدًا للإنتاج.

| الملف | ما ستجده |
|---|---|
| [Architecture](architecture.md) | الوحدات، التخزين، بناء الواجهات وحدود mobile |
| [Chart of accounts](accounting/chart-of-accounts.md) | جميع أكواد الحسابات وأسمائها وغرضها والشركة المالكة |
| [Posting rules](accounting/posting-rules.md) | الحدث المالي → مدين/دائن وسبب القاعدة |
| [Decisions](accounting/decisions.md) | القرارات المؤرخة والأسئلة التي تحتاج حسمًا |
| [Deploy](operations/deploy.md) | CI والنشر والتحقق والتراجع والنسخ الاحتياطي |
| [Roadmap](roadmap.md) | المطلوب بالترتيب بعد استبعاد ما أُصلح |

## ترتيب الاعتماد

قرار المالك الصريح يحكم السياسة. الكود والاختبارات الحالية يثبتان التنفيذ. أحدث [HANDOFF](../HANDOFF.md) يحدد نقطة الاستكمال ويتجاوز حالة المراجعة السابقة له. عند اختلاف السياسة والتنفيذ يُسجل الفرق كسؤال مفتوح؛ لا يُغيّر القيد بالتخمين.

حالة `0.7.0` المنشورة وإصلاحات المحاسبة اللاحقة مأخوذة من HANDOFF الحالي. المراجعة الخارجية في نفس اليوم سبقت بعض هذه الإصلاحات. أعداد الاختبارات في التسليمات القديمة تخص رؤوسها التاريخية ولا تُنقل كدليل نجاح للرأس الحالي.

## مصادر المعرفة

المصادر الخارجية للقراءة فقط. لم تُنسخ قواعد أو ملفات أشخاص أو مجلدات بيانات خاصة. تُستخدم المسارات التالية للعثور على النص التاريخي، ولا يحتاج التشغيل وجود الأرشيف. توضح القائمة المسارات المتاحة عند إعادة التحقق، والمسار التاريخي للمراجعة التي لم تعد موجودة.

| المرجع | موقع المصدر ودوره |
|---|---|
| S1 | [HANDOFF.md](../HANDOFF.md): آخر حالة، إصلاحات المراجعة وأسئلة المالك |
| S2 | المسار التاريخي: `~/Projects/pro win/Projectes/08_MultiPlatform_Payroll_&_Rider_Ledger_App/02_Architecture_&_System_Blueprints/PROJECT_REVIEW_2026-10-26.md`. المراجعة P1–P3 وترتيب المقترحات موثقان في الحزمة الأصلية، commit `06ccd2b`. لم يُعثر على أي `PROJECT_REVIEW_*.md` تحت `~/Projects` في 2026-10-27؛ لم تُعد قراءة أصل المراجعة في هذه الجولة |
| S3 | `~/Projects/Comprress_projects/pro win/Projectes/PROJECT_CONTINUITY.md`: نسخة السياق التاريخي المتاحة. توجد نسخة منقولة في `~/Projects/pro win/Projectes/01 الأنظمة والتطبيقات/01 متعقب المهام وخارطة الطريق/وثائق_التوجيه_والمهام/PROJECT_CONTINUITY.md`؛ تحديثات نشر 0.7.0 تُراجع مع S1/S6، ولا تُفترض من تاريخ الملف المنقول |
| S4 | تحت `~/Projects/Comprress_projects/pro win/`: `Projectes/.codex-brain-review/continuity/WORKING_KNOWLEDGE_AR.md` و`PAYROLL_HANDOFF_REVIEW_2026-10-21_AR.md`، و`Chat/Codex/continuity/WORKING_KNOWLEDGE_AR.md`. النسخة المنقولة: `~/Projects/pro win/Projectes/06 السجلات والذكاء الاصطناعي/02 بيئات ومراجعات الذكاء/.codex-brain-review/continuity/WORKING_KNOWLEDGE_AR.md`. السياق وقيود الحساب وتصحيح التسليمات؛ ليست إثباتًا مستقلًا للإصدار الحالي |
| S5 | المخططات الأربعة تحت `~/Projects/Comprress_projects/pro win/Projectes/08_MultiPlatform_Payroll_&_Rider_Ledger_App/02_Architecture_&_System_Blueprints/`؛ ولها نظائر منقولة تحت `~/Projects/pro win/Projectes/01 الأنظمة والتطبيقات/03 تطبيق الرواتب ودفتر الأستاذ/المخططات المعمارية للنظام/`: `01_Payroll_Engine_&_Rider_Ledger_Blueprint.md`، `02_Rider_Portal_&_OTP_Microservice_Blueprint.md`، `03_Project_Boundaries_And_Work_Split.md`، `04_Work_Choice_Form_And_Deployment.md` |
| S6 | `~/Projects/_archive/gate-payroll/versions/pro win__Projectes__08_MultiPlatform_Payroll_&_Rider_Ledger_App__04_Payroll_&_Ledger_App_Source/`: `CODEX_GITHUB_DEPLOYMENT_HANDOFF_AR.md`، `CODEX_HANDOFF_REVIEW_AR.md`، `V070_HANDOFF_AR.md`، وتسليمات `artifacts/V05*HANDOFF*.md` و`V061_HANDOFF_AR.md` و`V070_HANDOFF_AR.md` |
| S7 | الأرشيف الشقيق المنتهي بـ`05_Rider_SelfService_Microservice/ANTIGRAVITY_HANDOFF.md`: حدود مصادر الواجهة والفورم |
| S8 | نسخ `Comprress_projects` تحت `~/Projects` ونظائرها في أرشيف Gate Payroll: تأكيد تاريخي؛ التسليمات المتطابقة لم تُحسب كمصادر مستقلة |

المخططات الأولى تضمنت اقتراحات PostgreSQL وOTP وخدمات منفصلة؛ التنفيذ الحالي تطبيق واحد، SQLite أو SQL Server، ورمز وصول مُراجع للبوابة. لا تتحول الاقتراحات إلى ميزات منفذة بمجرد وجودها في المخطط.

المراجع التنفيذية الأساسية: [FinancialCore](../Payroll.Api/FinancialCore.cs)، [AccountingEvents](../Payroll.Api/AccountingEvents.cs)، [AccountingPolicy](../Payroll.Api/AccountingPolicy.cs)، [Store](../Payroll.Api/Store.cs)، [workflow](../.github/workflows/build-and-deploy.yml). تفاصيل API في [API_CONTRACT](../API_CONTRACT.md)، والجداول في [TABLE_STANDARD_AR](../TABLE_STANDARD_AR.md).

إعادة التحقق في 2026-10-27: مطابقة أكواد الحسابات الـ75 وأسمائها وملاكها وآبائها وأنواعها وطبيعتها مع الكود، والتحقق من الروابط النسبية وحد 60 سطرًا لـAGENTS. البناء بلا أخطاء أو تحذيرات؛ المحاسبة offline نجحت 44/44 مع اختبار التكلفة المشتركة و52 فحصًا قديمًا؛ MSTest نجحت 90 وتخطت 6 اختبارات SQL Server لعدم تهيئة خدمته؛ أدوات النشر نجحت 21/21 بمحاكاة الاتصال. لم يُنفذ فحص browser أو CI أو إنتاج جديد.

## ملاحظة تعلّم محلية

## 2026-10-27 — Gate Payroll — Knowledge pack verification (Codex)
حُفظت هنا التزامًا بقراءة كل ما خارج المستودع فقط، بدل تحديث سجل التعلّم المشترك.

**Concepts**
- `Accrual` — إثبات الاستحقاق قبل الدفع؛ مستخدم في `Payroll.Api/AccountingEvents.cs:20`.
- `Provenance` — توثيق مصدر المعلومة وحدود توفره؛ مستخدم في `docs/README.md` حتى لو اختفى أصل المراجعة.
- `Skipped test` — اختبار لم يُنفذ لغياب متطلبه؛ نتائج SQL Server هنا ليست نجاحًا للمزود.

**English**
- `Counterpart` — الطرف المقابل؛ “The opening counterpart still needs an owner decision.”
- `Recheck` — إعادة التحقق؛ “Recheck the source path before citing it.”

**Check yourself:** لماذا لا يسجل تسليم حصيلة رسوم جاهز إيرادًا جديدًا؟
