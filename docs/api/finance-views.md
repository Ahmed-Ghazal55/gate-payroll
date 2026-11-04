# Finance views API — 0.9.3

واجهات backend فقط. لا تعديل لشجرة الترحيل الحالية أو للقيود أو مخطط القاعدة.
القراءة من `store.Read`؛ تصدير البنك يكتب حدث audit مختصرًا فقط، ولا يسجل دفعًا.
كل الأمثلة مصطنعة، وليست بيانات تشغيل أو تعليمات صرف فعلية.

## الصلاحيات والنطاق

- مصادقة الجلسة مطلوبة. شجرة الهدف تتطلب `treasury.view`؛ الميزان وقائمة الدخل يتطلبان `treasury.journals`.
- التقارير تستخدم شركة **سطر القيد**، لا الشركة الحالية في دليل المندوب. `sponsorId` يقبل الكود أو المعرّف؛ شركة خارج النطاق ترجع 403. غيابه يعرض الشركات المسموح بها فقط، كل شركة منفصلة.
- نطاق الخزائن `accountCodes` محفوظ. إذا حجب سطورًا داخل الشركة المختارة: `scopeLimited=true`, `excludedLineCount` عدد فقط، `isComplete=false`. لا مبالغ أو تفاصيل للسطور المحجوبة. صلاحية Admin / SuperAdmin تتجاوز النطاق كالمعتاد.
- البنك يتطلب `finance.bankfile.export` و`payroll.view` وشركة محددة. الصلاحية الجديدة افتراضية لـ FinancialManager وAdmin / SuperAdmin؛ المحاسب لا يحصل عليها تلقائيًا. دور FinancialManager الجديد يمنح القراءة والتصدير، ولا يمنح اعتماد الرواتب أو تسجيل دفعات.
- نطاق تقارير الأستاذ والبنك هو الشركة وصلاحيات المالية المذكورة؛ هذه ليست واجهة كشف المندوب ذات نطاقات الفئات والمنصات.

## شجرة الحسابات المستهدفة

`GET /api/finance/coa/target`

المورد المضمّن `Payroll.Api/Resources/target-coa.v1.json` مولّد من `gate-finance-spec/docs/02-chart-of-accounts.md` §§1،4،6، بقرارات BUSINESS_RULES §§20،22،23،24. يحتوي 408 حسابًا، منها 232 حساب ترحيل فعال، و75 صف ربط من الشجرة الحالية. المصادر: 294 `excel` و28 `modified` و86 `new`. لا يغير الشجرة الحالية.

```json
{
  "schemaVersion": 1,
  "version": "2026-11-03.1",
  "source": "gate-finance-spec/docs/02-chart-of-accounts.md §§1,4,6; BUSINESS_RULES §§20,22",
  "accounts": [
    {"code":"111101","nameAr":"الصندوق الرئيسي","parent":"1111","level":5,"posting":true,"nature":"debit","type":"cash","direction":"both","company":"الكل","status":"active","statusNote":null,"subledger":"—","source":"excel","sourceNote":"Excel"}
  ],
  "mapping": [
    {"currentCode":"MAIN.BW.DEBT","targetCodes":["111101"],"ruleAr":"111101","noteAr":""}
  ]
}
```

المثال مقتطف توضيحي؛ المورد الفعلي يحتوي الشجرة والربط بالكامل، بنصوص المصدر.
`parent=null` للجذر. `level` يبدأ من 1. `posting` السماح بالترحيل في **الهدف**.
`nature=debit|credit`؛ `type=bank|cash|other`؛ `direction=pay|receive|both` أو null حين لا ينطبق الاتجاه في الجدول.
`company` وصف انطباق الحساب من المصدر، وليس ربطًا فعليًا بحساب بنك شركة.
`status=active|stopped|reserved|display`. نسخة المورد مستقلة عن إصدار التطبيق.
`status` من أول كلمة في عمود الحالة؛ مثل «موقوف — سبب» تصبح `stopped`، و`statusNote="سبب"`؛ بدون ملاحظة يكون الحقل null. «عرض فقط» تصبح `display` دون اعتبار «فقط» ملاحظة.
`subledger` ينقل نص «دفتر مساعد» كما هو، بما فيه «—» عند عدم وجود دفتر. كل حساب يحتوي هذه الحقول.
`source=excel|new|modified`؛ «Excel معدّل» و«منقول (كان …)» كلاهما `modified`. `sourceNote` يحتفظ بالنص الأصلي كاملًا، حتى لا يضيع سبب النقل أو الكود السابق.

إعادة التوليد والتحقق، من جذر gate-payroll:

```bash
python3 tools/generate_target_coa.py
python3 tools/generate_target_coa.py --check
```

يمكن تمرير `--source` و`--output`. السكربت لا يقرأ ملفات تشغيل، ويتحقق من تفرّد الأكواد ووجود الآباء وعدم وجود أبناء لحساب ترحيل، ومن مراجع جدول الربط. يرفض الترحيل على حالة غير فعالة، ويطابق الإجمالي والترحيل الفعال وأعداد المصادر بملخص §1 قبل كتابة المورد؛ عند اختلافها يتوقف دون تعديل المواصفة أو كتابة نتيجة جزئية. `modified` يجمع المعدّل بنفس الكود والمنقول من Excel والمنقول الجديد. لا يضيف تاريخ تشغيل متغيرًا إلى الملف.

## ميزان المراجعة

`GET /api/finance/trial-balance?sponsorId=BW&from=2026-10&to=2026-10&view=target`

`view=current|target`، الافتراضي `current`. الشهور `YYYY-MM`، البداية لا تتجاوز النهاية. النهاية الافتراضية الشهر الحالي؛ البداية الافتراضية النهاية. لا يقرأ مسيرات غير معتمدة أو دفتر ledger القديم؛ مصدره journal بحالة posted (القيود القديمة بلا status تعتبر posted كالسلوك الحالي).

```json
{
  "version":"2026-11-03.1","view":"target","from":"2026-10","to":"2026-10",
  "companies":[{
    "sponsorId":"synthetic-company","companyCode":"BW",
    "rows":[{
      "code":"111101","name":"الصندوق الرئيسي","nameAr":"الصندوق الرئيسي","parent":"1111","level":5,"posting":true,
      "openingDebit":1000,"openingCredit":0,"periodDebit":200,"periodCredit":50,
      "closingDebit":1150,"closingCredit":0
    }],
    "unmapped":[{
      "code":"BANK.BW","nameAr":"بنك افتراضي","reason":"insufficient_mapping_context","candidateCodes":[],
      "openingDebit":0,"openingCredit":0,"periodDebit":100,"periodCredit":0,"closingDebit":100,"closingCredit":0
    }],
    "totals":{"openingDebit":1000,"openingCredit":1000,"periodDebit":300,"periodCredit":300,"closingDebit":1250,"closingCredit":1250},
    "mappedTotals":{"openingDebit":1000,"openingCredit":1000,"periodDebit":200,"periodCredit":300,"closingDebit":1150,"closingCredit":1250},
    "unmappedTotals":{"openingDebit":0,"openingCredit":0,"periodDebit":100,"periodCredit":0,"closingDebit":100,"closingCredit":0},
    "mappingComplete":false,"scopeLimited":false,"excludedLineCount":0,"isComplete":true,"isBalanced":true
  }]
}
```

الصفوف في المثال مقتطفة؛ الإجماليات تخص شركة افتراضية كاملة.

| الحقل | المعنى |
|---|---|
| openingDebit / openingCredit | صافي الرصيد السابق لـ from، في طرفه المناسب لكل حساب مباشر |
| periodDebit / periodCredit | إجمالي الحركات المرحلة من from إلى to شاملًا |
| closingDebit / closingCredit | صافي الافتتاح + مدين الفترة − دائن الفترة، في طرفه المناسب |
| name / nameAr | اسم الحساب بالعربية؛ nameAr متاح للتوافق مع المورد |
| rows | الشجرة كاملة، بما فيها الحسابات الصفرية؛ الحساب التجميعي يجمع أعمدة الحسابات المباشرة تحته، مع أي رصيد تاريخي مباشر عليه |
| totals | مجموع الحسابات المباشرة + غير المربوطة، دون تكرار صفوف الآباء |
| mappedTotals / unmappedTotals | تفصيل totals للحسابات المربوطة وغير المربوطة |
| unmapped | مبالغ الحسابات التي تعذر ربطها، مجمعة بكودها الحالي؛ لا تُسقط من الإجمالي |
| mappingComplete | لا يوجد أي كود غير مربوط في هذا التقرير |
| isComplete | لم يحجب نطاق المستخدم سطورًا داخل الشركة المعروضة؛ اكتمال الربط يُقاس منفصلًا |
| isBalanced | تساوي المدين والدائن في الافتتاح والفترة والإقفال على السطور المرئية؛ لا يثبت اكتمال النطاق |

الربط متعدد الأهداف يستعمل الفئة ودليل الطرف أو المنصة أو الشركة المقابلة المتاح في السطر/الدين/مسير القيد.
ربط عمولة `4102` يستدل من المنصة المثبتة أو منصة وحيدة في مصدر المسير. السطر المختلط بلا دليل يبقى غير مربوط.
`1310` حسب الشركة المقابلة، وليس الشركة صاحبة السطر؛ لا يربط إلى نفس الشركة.
`BANK.*` العام يبقى غير مربوط لحين معرفة البنك الفعلي، لأن `1112xx` ليس كود حساب محددًا.
مركبة المندوب الحالية ليست دليلًا تاريخيًا لتقسيم `5201`؛ يلزم دليل في السطر/الدين.
الحسابات غير المعروفة تبقى في unmapped أيضًا في current. التصنيف التاريخي لا يُصحح بقيد جديد، ولا يُحوّل التعديل السالب تلقائيًا إلى إيراد.

## قائمة الدخل

`GET /api/finance/income-statement?sponsorId=BW&from=2026-10&to=2026-10&view=target`

نفس معلمات الميزان، والافتراضي `target` لشجرة الهدف. `current` يعرض مجموعات الإيرادات والمصروفات الحالية للمقارنة.
الفترة فقط، دون أرصدة الافتتاح. مجموعات الهدف تحت 4 للإيرادات و5 للمصروفات، مع كل مستويات التجميع.

```json
{
  "version":"2026-11-03.1","view":"target","from":"2026-10","to":"2026-10",
  "companies":[{
    "sponsorId":"synthetic-company","companyCode":"BW",
    "revenueGroups":[{"code":"4","nameAr":"الإيرادات","parent":null,"posting":false,"amount":1500}],
    "expenseGroups":[{"code":"5","nameAr":"المصروفات","parent":null,"posting":false,"amount":900}],
    "revenue":1500,"expense":900,"net":600,"netIsComplete":true,
    "scopeLimited":false,"excludedLineCount":0,"unmapped":[]
  }]
}
```

الإيراد = دائن − مدين، والمصروف = مدين − دائن؛ `net=revenue-expense`.
لا تجمع قيم كل مستويات groups مرة ثانية؛ الآباء يتضمنون أبناءهم.
`netIsComplete=false` عند حجب سطر أو تعذر أي ربط، حتى إن كان الكود غير المربوط حساب ميزانية؛ تعمدنا هذا التحفظ لإظهار نقص الدليل.

## معاينة ملف البنك

`POST /api/finance/bank-file/preview`

```json
{"sponsorId":"BW","period":"2026-10"}
```

بديل الشهر الواحد: `from` و`to` بصيغة YYYY-MM؛ أولوية المعلمات الصريحة على period.
المبلغ هو رصيد الالتزام 2110 المُرحّل لكل مندوب وشركة **حتى نهاية to**، شامل المستحقات المعتمدة السابقة وخصوماتها ومدفوعاتها؛ يُختار المندوب إذا له سطر 2110 مُرحّل أو مسير معتمد للشركة داخل from..to؛ لا نسحب مناديب فترات قديمة بلا نشاط أو مسير معتمد في الفترة المطلوبة. الافتتاح يظل داخل رصيد المختارين، ولا يسقط.
لا نطرح ذمم 1210/1211/1230 مرة ثانية، ولا نستخدم payable من معاينة مسير غير معتمد.
الموظفون المصنفون صراحةً في الدليل خارج ملف المناديب. أصحاب الرصيد الصفري أو السالب يظهرون في `excludedRows`، بينما `rows` تحتوي المستحقات الموجبة فقط. المسير المعتمد بلا حركة 2110 يظهر كرصيد صفر، وليس مستحقًا للصرف.

```json
{
  "sponsorId":"synthetic-company","from":"2026-10","to":"2026-10",
  "basis":"posted_2110_closing_payable",
  "rows":[{
    "bank":"RJHI","iban":"SA0380000000608010167519","amount":1000.00,"notes":"",
    "name":"Synthetic Rider","iqama":"2000000111","address":"Synthetic City","errors":[]
  }],
  "excludedRows":[
    {"iqama":"2000000222","name":"Synthetic zero rider","balance":0,"reason":"zero_balance"},
    {"iqama":"2000000333","name":"Synthetic debtor rider","balance":-50,"reason":"rider_owes_company"}
  ],
  "previewToken":"synthetic-signed-token","expiresInSeconds":1200,"canExport":true
}
```

الآيبان المصطنع في المثال صالح حسابيًا فقط. الأفضلية لـ `verifiedIban` غير الفارغ، ثم `iban`. إذا كان المعتمد غير صالح لا نتجاوزه إلى غير المعتمد.
`bank` أول أربعة أحرف SWIFT من المرجع المحلي. تطبيع الآيبان يزيل المسافات ويحوله إلى uppercase.

| رمز الخطأ | المعنى |
|---|---|
| missing_iban | لا يوجد آيبان |
| invalid_iban_mod97 | الشكل السعودي أو فحص mod-97 غير صحيح |
| unknown_bank_code | كود البنك غير موجود في المرجع، ولم يُكتب bank يدويًا (4 حروف كبيرة) |
| bank_mismatch | اختصار البنك لا يطابق الآيبان |
| bank_code_40_manual_confirmation_required | كود 40 محجوب لحين التأكيد اليدوي؛ المتوقع NCBK وفق BR §19 |
| invalid_amount_positive_2dp_required | مبلغ غير رقمي أو ≤ 0 أو بدقة أكثر من منزلتين |
| invalid_iqama / missing_name | هوية غير صالحة أو اسم مفقود في المصدر |

كود غير معروف: bank يُكتب يدويًا (أول 4 حروف من SWIFT) حسب BUSINESS_RULES §19، ويُحسب الصف ضمن editedRowCount. تعديل bank مرفوض لأي كود معروف. الصفوف اللي رصيد 2110 الختامي ليها صفر أو مدين لا تظهر في الملف.
`excludedRows[].balance` هو الرصيد **بإشارته**: صفر = تمت التسوية، وسالب = المندوب مدين للشركة؛ ليس مبلغًا قابلًا للتعديل أو التحويل. سبب الاستبعاد ثابت `zero_balance` أو `rider_owes_company`. وجود المستبعدين لا يمنع تصدير صفوف `rows` الصالحة؛ عدم وجود صف موجب يجعل `canExport=false`. نفس نطاق الشركة والفترة وتصنيف المناديب يطبق على القائمتين.

## تصدير ملف البنك

`POST /api/finance/bank-file/export`

```json
{
  "sponsorId":"BW","period":"2026-10","previewToken":"synthetic-signed-token",
  "confirmBankCode40":false,
  "rows":[{
    "bank":"RJHI","iban":"SA0380000000608010167519","amount":900.00,"notes":"Synthetic adjustment",
    "name":"Synthetic Rider","iqama":"2000000111","address":"Synthetic City","errors":[]
  }]
}
```

- إعادة جميع صفوف المعاينة بنفس ترتيبها وهويتها. التعديل المسموح: `amount` و`notes` فقط؛ المبلغ الرقمي موجب بدقة منزلتين كحد أقصى، والملاحظات نص ≤ 2000 حرف. عدم وجود صفوف يمنع التصدير.
- لا تُرسل `excludedRows` ضمن صفوف التصدير. الخادم يعيد تحديد المستبعدين من المصدر ويمنع أي هوية مستبعدة برسالة عربية و400، حتى لو عدّل العميل مبلغها إلى موجب أو حذف errors أو استبدل بها صفًا مستحقًا؛ لا audit نجاح عند الرفض.
- يُعاد فحص كل صف على الخادم، ولا يُعتمد errors القادمة من العميل. أي خطأ يمنع **الملف كله** بـ400، دون audit نجاح.
- `confirmBankCode40=true` إقرار يدوي صريح للمجموعة ذات الكود 40، وليس تعديلًا لحقل البنك؛ الاختصار يظل NCBK. باقي الاختبارات تظل واجبة.
- التوقيع يرتبط بالمستخدم والشركة والفترة والمصدر المالي لمدة 20 دقيقة. تغيير المصدر، أو المستخدم، أو انتهاء المعاينة/إعادة تشغيل الخادم: 409 وإعادة المعاينة. يجري التحقق والتدقيق في transaction واحدة دون تغيير مستندات التشغيل.
- 200 يرد بملف `text/csv; charset=utf-8`، مع Content-Disposition واسم `<unix-ms>_bulkPayroll.csv`.
- UTF-8 BOM بايتاته EF BB BF، والفاصل comma، ونهاية كل سطر CRLF، مع CRLF أخير. تطبق قواعد CSV للاقتباس عند comma/quote/newline؛ المبلغ منزلتان بفاصلة عشرية نقطة.

ترتيب الرأس حرفيًا:

```text
البنك,رقم الحساب,إجمالي الراتب,ملاحظات,اسم الموظف,رقم الهوية أو الإقامة,عنوان الموظف
```

حدث audit الوحيد يحتوي `id, action, actor, at, rowCount, total, editedRowCount`؛ الأخير يعد الصفوف ذات تعديل المبلغ أو الملاحظات، مرة واحدة للصف. لا تُحفظ محتويات الملف أو الآيبانات أو أسماء المناديب أو الهويات أو نصوص الملاحظات، ولا قيود دفع. `actor` هو المستخدم الذي صدّر الملف.
التصدير لا يثبت أن البنك نفذ التحويل؛ تسجيل الدفع يتم بالإجراء القائم لاحقًا.

## التحقق

```bash
dotnet test tests/Engine.Tests -v minimal
python3 tools/generate_target_coa.py --check
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py'
git diff --check
```

اختبارات C# وHTTP ببيانات مصطنعة: هيكل الشجرة، ربط الحسابات، توازن الشركتين بالعرضين، تجميع الآباء، الدخل دون تكرار، عزل الشركة والخزائن، القراءة دون تعديل، مصدر 2110، الآيبان المعتمد، mod-97، بنك غير معروف وكود 40، منع التلاعب وتغير المصدر، بايتات CSV ورأسه، وخصوصية audit.

## R2 — platform posting gates
`GET/POST /api/platforms/{code}/posting` are administrator-only; POST accepts `{enabled:boolean}` and audits a state change. Missing settings default off. Codes: hs, jahez, keeta, ninja, chefz, amazon. These gates govern the new platform-facts posting pipeline; the established legacy posting flows are retained.

HS target journals use the approved posting accounts through the existing FinancialCore and AccountingPolicy validators. The target path is explicit on each new journal (`chart=target`); direct legacy account resolution and historical records are unchanged. The target trial balance consumes those native account codes directly. The HS path rejects legacy 1241/2141 and cannot post while its gate is off.
