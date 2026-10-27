# Gate Payroll — تعليمات المشروع

- اقرأ `HANDOFF.md` أولًا وحدّثه أخيرًا؛ كاتب واحد لكل فرع/مجلد.
- المعرفة الحالية في `docs/README.md`؛ الأرشيف سياق تاريخي للقراءة فقط.
- رد بالعربي، واترك المصطلحات والأوامر التقنية بالإنجليزي.

## Stack

- ASP.NET Core Minimal API، .NET 10، C#، وMSTest.
- واجهات TypeScript 5.9.3 وCSS؛ Node.js 24 للبناء والاختبار.
- الإدارة: `Frontend/src`؛ الفورم: `RiderForm/src`.
- `dotnet build/publish` يبني الواجهتين إلى `Payroll.Api/wwwroot`.
- عدّل TS الأصلي، لا JS المولد؛ حافظ على ترتيب scripts وescaping.
- SQLite افتراضي وSQL Server بديل؛ JSON documents داخل `records`.
- نشر تطبيق واحد على Windows/IIS في MonsterASP؛ mobile بوابة ويب.

## Commands — من جذر المستودع

```bash
npm ci --no-audit --no-fund --prefix RiderForm
npm run build:all --prefix RiderForm
dotnet build tests/Engine.Tests -v q
dotnet run --no-build --project tests/Engine.Tests -- --accounting-review
dotnet test tests/Engine.Tests --no-build
python3 -m unittest discover -s scripts -p 'test_*.py' -v
```

- اختبار offline لا يغطي HTTP/browser/SQL Server؛ وثّق أي skipped tests.
- CI يفحص المزودين والمتصفح وحزم Linux وWindows؛ التفاصيل في docs.

## Deploy rule

- لا deploy أو publish أو push --force أو API مدفوع دون موافقة Ghazal.
- push إلى main ينشر الإنتاج تلقائيًا؛ لا تعامل الدمج كتغيير محلي فقط.
- اتبع `docs/operations/deploy.md`: FTPS مع TLS وتحقق حفظ البيانات.
- لا ترفع بيانات الجهاز؛ لا clean-slate ولا استبدال بيانات التشغيل.
- مهمة knowledge pack على `docs/knowledge-pack`: commit بـ`[skip ci]`، بلا push.

## Data rules

- لا تقرأ أو تطبع أسرارًا أو ملفات `.env` أو credentials أو قيم اتصال.
- لا تضع بيانات FTPS أو اتصال أو أشخاص أو بنوك أو مبالغ فردية في docs/Git.
- لا تنسخ `App_Data` أو `private-data` إلى المصدر أو الحزمة أو الأصول العامة.
- الاختبارات ببيانات اصطناعية معزولة؛ لا اختبار كتابة على الإنتاج.
- BW وEX وTR دفاتر مستقلة؛ شركة السطر صريحة ومالك الحساب ثابت.
- القيد متوازن لكل شركة، والجاري `1310` صريح؛ لا ترحيل على الأب.
- فصل Accrual/Disbursement/OpeningBalance؛ لا صرف ضمني من طلب HR.
- رسوم جاهز إيراد الشركة `4101` مرة واحدة؛ التسليم لا يكرر الإيراد.
- أموال SET/JAHEZ معزولة؛ رواتب `2110` منها لنفس الشركة فقط.
- لا تحوّل اختيار الفورم إلى policyId أو تعتمد البوستر كمصدر مالي.
- معاينة ثم اعتماد ببصمة وحفظ ذري؛ لا تعديل مباشر للتاريخ المالي.
- افتتاح جاهز: حصته `2140` والعمولة السابقة حقوق ملكية؛ التنفيذ يحتاج فصلًا واعتمادًا مستقلًا.
