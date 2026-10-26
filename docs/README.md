# Gate Payroll — دليل المعرفة

آخر مطابقة للمصادر والكود: **2026-10-26**. هذه حزمة معرفة للإصدار `0.7.0`؛ لا تمثل اعتمادًا للأرصدة أو فحصًا جديدًا للإنتاج.

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

كل المصادر الخارجية قُرئت فقط. لم تُنسخ قواعد أو ملفات أشخاص أو مجلدات بيانات خاصة. تُستخدم المسارات التالية للعثور على النص التاريخي، ولا يحتاج التشغيل وجود الأرشيف.

| المرجع | موقع المصدر ودوره |
|---|---|
| S1 | [HANDOFF.md](../HANDOFF.md): آخر حالة، إصلاحات المراجعة وأسئلة المالك |
| S2 | `~/Projects/pro win/Projectes/08_MultiPlatform_Payroll_&_Rider_Ledger_App/02_Architecture_&_System_Blueprints/PROJECT_REVIEW_2026-10-26.md`: المراجعة P1–P3 وترتيب المقترحات |
| S3 | `~/Projects/pro win/Projectes/PROJECT_CONTINUITY.md`: سجل القرارات والتصحيحات حتى نشر 0.7.0 |
| S4 | `~/Projects/pro win/Projectes/.codex-brain-review/continuity/WORKING_KNOWLEDGE_AR.md` و`PAYROLL_HANDOFF_REVIEW_2026-10-21_AR.md`؛ و`~/Projects/pro win/Chat/Codex/continuity/WORKING_KNOWLEDGE_AR.md`: السياق وقيود الحساب وتصحيح التسليمات |
| S5 | مجلد Blueprints في S2: `01_Payroll_Engine_&_Rider_Ledger_Blueprint.md`، `02_Rider_Portal_&_OTP_Microservice_Blueprint.md`، `03_Project_Boundaries_And_Work_Split.md`، `04_Work_Choice_Form_And_Deployment.md` |
| S6 | `~/Projects/_archive/gate-payroll/versions/pro win__Projectes__08_MultiPlatform_Payroll_&_Rider_Ledger_App__04_Payroll_&_Ledger_App_Source/`: `CODEX_GITHUB_DEPLOYMENT_HANDOFF_AR.md`، `CODEX_HANDOFF_REVIEW_AR.md`، `V070_HANDOFF_AR.md`، وتسليمات `artifacts/V05*HANDOFF*.md` و`V061_HANDOFF_AR.md` و`V070_HANDOFF_AR.md` |
| S7 | الأرشيف الشقيق المنتهي بـ`05_Rider_SelfService_Microservice/ANTIGRAVITY_HANDOFF.md`: حدود مصادر الواجهة والفورم |
| S8 | نسخ `Comprress_projects` تحت `~/Projects` ونظائرها في أرشيف Gate Payroll: تأكيد تاريخي؛ التسليمات المتطابقة لم تُحسب كمصادر مستقلة |

المخططات الأولى تضمنت اقتراحات PostgreSQL وOTP وخدمات منفصلة؛ التنفيذ الحالي تطبيق واحد، SQLite أو SQL Server، ورمز وصول مُراجع للبوابة. لا تتحول الاقتراحات إلى ميزات منفذة بمجرد وجودها في المخطط.

المراجع التنفيذية الأساسية: [FinancialCore](../Payroll.Api/FinancialCore.cs)، [AccountingEvents](../Payroll.Api/AccountingEvents.cs)، [AccountingPolicy](../Payroll.Api/AccountingPolicy.cs)، [Store](../Payroll.Api/Store.cs)، [workflow](../.github/workflows/build-and-deploy.yml). تفاصيل API في [API_CONTRACT](../API_CONTRACT.md)، والجداول في [TABLE_STANDARD_AR](../TABLE_STANDARD_AR.md).

## ملاحظة تعلّم محلية

## 2026-10-26 — Gate Payroll — Knowledge pack (Codex)

حُفظت هنا التزامًا بقراءة كل ما خارج المستودع فقط، بدل تحديث سجل التعلّم المشترك.

**Concepts**

- `Accrual` — إثبات الاستحقاق قبل الدفع؛ مستخدم في `Payroll.Api/AccountingEvents.cs:20`.
- `Control account` — حساب رقابي لمطابقة حق أو التزام؛ مستخدم في `Payroll.Api/JahezTreasury.cs:7`.
- `Provenance` — توثيق المصدر وتاريخه يمنع اعتبار الاقتراح تنفيذًا؛ مستخدم في `docs/README.md:19`.

**English**

- `Counterpart` — الطرف المقابل؛ “The opening counterpart still needs an owner decision.”
- `Netting` — مقاصة حق والتزام؛ “Netting does not require a bank movement.”

**Check yourself:** لماذا لا يسجل تسليم حصيلة رسوم جاهز إيرادًا جديدًا؟
