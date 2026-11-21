# Rider Mobile App Guide (Version 2)

---

## Part 1 — English Version

### 1. What the App Does
- The rider mobile app is an Android application built with Vite, TypeScript, and Capacitor 6, paired with Firebase Cloud Messaging (HTTP v1) and the payroll server API.
- It supports three languages everywhere: Arabic (`ar`, RTL), English (`en`, LTR), and Urdu (`ur`, RTL), with large readable text and clear touch targets.
- It never displays official company documents or company and commercial registration names on rider-facing screens. The statement is a short informational summary and not a legal document.
- It uses the same forms and validation system as `RiderForm` (one shared forms catalog and Saudi IBAN modulo-97 validation). When a rider opens a form inside the app, their identity is taken from the active session without re-typing the Iqama number.

### 2. App Screens
| Screen / Tab | Tab Identifier | What It Shows and Does |
|---|---|---|
| Sign In | `#mob-login-form` | Requires the 10-digit Iqama number, the last 4 digits of the registered phone, and a 64-character one-time access code issued after an identity check. Never allows Iqama-only login. Includes a 3-language switcher (`ar`, `en`, `ur`). |
| Home & Statement | `statement` | Top banner (`#mob-home-summary`) shows the latest approved statement summary with a clear **For you (لك)** or **On you (عليك)** marker. Below it, a month picker (`#mob-period`) loads any approved monthly statement from the same source as the web statement (`PayStatements.Build`), showing financial items one under another (`#mob-statement-lines`) with running balances and a non-legal disclaimer. Unapproved draft cycles are never shown. |
| Notifications | `notifications` | Lists two kinds of notices with read state, stored read receipts, and deep links to related screens or forms: (a) general broadcasts sent to all riders in batches, and (b) individual-only notices (`summons`, `dismissal_warning`, `administrative_deduction`, `appointment_alert`, `administrative_warning`) that can never be broadcast to all riders and always require rider acknowledgement. |
| Requests & Forms | `requests` | Two-way requests with a status timeline and reply thread for seven request types, plus active campaigns from `RiderForm` submitted directly in-app without re-typing the Iqama. |
| Debts Summary | `debts` | Shows total remaining debt, total paid, total waived, and each debt item with its monthly instalment and **On you (عليك)** marker, without company or sponsor names. |
| My Devices | `devices` | Lists active, expired, and revoked sessions for the rider with safe 8-character device fingerprints (no raw tokens) and a server-side sign-out button. |

### 3. How Requests and Notifications Flow
| Request / Notice Type | Technical Key | Routing and Approval Flow |
|---|---|---|
| Advance Request | `advance` | Sent by the rider (`POST /api/rider-mobile/requests`) only to their current supervisor (`RiderAsks`). The supervisor either converts it into a formal advance request for the accountant (`AdvanceRequests`) or declines it with a visible reason. |
| Annual Leave Request | `annual_leave` | New 3-language form (`annual-leave-v1`): leave type (`annual`, `emergency`, `unpaid`), start date, end date, destination (`inside_ksa`, `outside_ksa`), contact phone, and reason. Reviewed and decided by administration with timeline updates and FCM push notice. |
| IBAN Update | `iban_update` | Uses shared Saudi IBAN modulo-97 and salary-bank validation (`iban-v1`). If the submitted IBAN matches the rider's current recorded IBAN, it is auto-approved immediately. If it differs, it requires **two independent approvers** (`first_approved` by reviewer 1, then `approved` by a different reviewer 2) before updating `verifiedIban`. |
| Personal Data Update | `data_update` | Updates phone, city, housing, or emergency contact (`data-update-v1`). Requires **two independent approvers** (`first_approved` then `approved` by a second user). |
| Account Statement Request | `statement_request` | Requests a monthly statement, full account statement, or debt statement for a chosen month range (`periodFrom` to `periodTo`). |
| Confidential Complaint | `complaint` | Marked `confidential = true` (`M-0359`). Strictly hidden from supervisors and regular staff; only the rider and administrators (or holders of `mobile.complaints.confidential`) can view, reply, or decide on it. |
| Appointment Request | `appointment` | Requests an appointment with HR, Finance, Fleet, Operations, or Management on a preferred date and time slot (`morning` or `afternoon`). |
| Outbound Admin Request | `admin_to_rider` | Administration can send a request to a specific rider (`POST /api/rider-mobile/service-requests`) and exchange two-way replies (`POST /api/rider-mobile/requests/{id}/reply` and `POST /api/rider-mobile/service-requests/{id}/reply`). |
| Individual-Only Notices | `individual_notice` | Categories `summons`, `dismissal_warning`, `administrative_deduction`, `appointment_alert`, and `administrative_warning`. The API rejects `iqama = "ALL"` with HTTP 400, forces `requiresAck = true`, and stores a timestamped read and acknowledgement receipt (`readReceipts`). |

### 4. How to Build and Install the Debug APK
1. Ensure Node.js 20+, Java 17 or 21, and the Android SDK are installed on your machine.
2. Place the real Firebase Android configuration file at:
```bash
~/Private/gate-data/firebase/google-services.json
```
3. Run the debug build script from the mobile folder:
```bash
cd /home/ghazal/Projects/active/gate-payroll-rider-app/mobile
./scripts/build-debug.sh
```
4. The script verifies `google-services.json`, copies it into `mobile/android/app/google-services.json` (which is git-ignored), builds the Vite bundle, syncs Capacitor Android, runs `./gradlew assembleDebug`, and copies the output APK to:
```bash
~/Private/gate-data/mobile/gate-rider-debug.apk
```
5. Install the APK on a connected Android phone via USB debugging:
```bash
adb install -r ~/Private/gate-data/mobile/gate-rider-debug.apk
```

### 5. What You Must Finish Before Go-Live
1. **Firebase Project & Android File**:
   - In the Firebase Console, create or open your project and register an Android app with package name:
```text
com.albawaba.gate.rider
```
   - Download `google-services.json` and save it at:
```bash
~/Private/gate-data/firebase/google-services.json
```
2. **Server Environment Variable (Never in DB or Git)**:
   - Generate a Firebase Service Account JSON key from Firebase Console → Project Settings → Service accounts.
   - Set its JSON content in the server environment variable:
```text
Firebase__ServiceAccountJson
```
   - After deployment, save the Firebase settings once in the admin screen (`/app/rider-app`) to clear any legacy key flag.
3. **Synthetic Test Account Setup**:
   - Create a synthetic test rider (for example, Iqama `2000000741`, phone `0501234567`, status `Active`).
   - Assign a test supervisor in Organization Supervision (`/api/org/supervision`) so advance requests can be tested end to end.
   - Issue a one-time mobile login code from the admin panel (`POST /api/inquiry-access` with `scope: "mobile"` and `identityReviewed: true`).

### 6. Load Safety and Known Limits
| Limit / Control | Configured Value | Behavior |
|---|---|---|
| Login Rate Limit | `5` attempts / minute | Sliding window per IP and per hashed Iqama (`InquiryLimits`). Returns HTTP 429 on excess attempts. |
| Rider Read Rate Limit | `60` requests / minute | Fixed 1-minute window per authenticated rider session (`PushNotifications.DefaultReadLimitPerMinute`). Returns HTTP 429 with `Retry-After: 60`. |
| Rider Write Rate Limit | `20` requests / minute | Fixed 1-minute window per authenticated rider session (`PushNotifications.DefaultWriteLimitPerMinute`). Returns HTTP 429 with `Retry-After: 60`. |
| Portal & List Bounds | `50` notices / `100` debts / `100` requests | Caps payload size per request (`MaxPortalBroadcasts`, `MaxPortalDebts`, `MaxRiderListItems`). Supports opt-in `?page=1&pageSize=50`. |
| Outbox Batch Size & Retries | `50` items / batch, max `3` attempts | Background outbox drains pending FCM messages in batches of 50 with a 10-second HTTP timeout and exponential backoff (1 minute, then 5 minutes). `UNREGISTERED` tokens are automatically cleared from the session. |
| Bulk Statement Notification | Idempotent per `(period, iqama)` | `POST /api/rider-mobile/statements/notify-period` notifies riders who have an approved statement in that month via the outbox without putting any salary amounts inside the FCM payload. |
| Missing `google-services.json` | Fails fast in `build-debug.sh` | The build script never fakes `google-services.json`; it stops with a clear message until the real file is placed in `~/Private/gate-data/firebase/google-services.json`. |

---

## الجزء الثاني — النسخة العربية الكاملة

### 1. وظيفة التطبيق وما يقدمه
- تطبيق جوال المناديب هو تطبيق أندرويد مبني لتشغيل خدمات المندوب وربطها مباشرة مع الخادم وخدمة الإشعارات السحابية.
- التطبيق يدعم ثلاث لغات في كل شاشة: العربية باتجاه من اليمين لليسار، والإنجليزية باتجاه من اليسار لليمين، والأردية باتجاه من اليمين لليسار، مع خط كبير واضح وأزرار كبيرة سهلة اللمس.
- التطبيق لا يعرض أبداً أي مستندات رسمية للشركة ولا يذكر اسم الشركة أو السجل التجاري في شاشات المندوب. كشف الحساب المعروض هو ملخص استرشادي مختصر للمندوب وليس مستنداً رسمياً أو قانونياً.
- التطبيق يستخدم نفس نظام النماذج والتحقق الموجود في نموذج المندوب العام:
```text
RiderForm
```
  بما في ذلك نفس فحص رقم الحساب البنكي الدولي وقائمة البنوك المعتمدة. ولما المندوب يفتح أي نموذج من جوه التطبيق، النظام بياخد هويته تلقائياً من جلسته النشطة من غير ما يعيد كتابة رقم الإقامة.

### 2. شاشات التطبيق
| الشاشة أو التبويب | المعرّف | المحتوى والوظيفة |
|---|---|---|
| تسجيل الدخول | `#mob-login-form` | يطلب رقم الإقامة المكون من عشرة أرقام، وآخر أربعة أرقام من الجوال المسجل، ورمز دخول خاص لمرة واحدة مكون من أربعة وستين رمزاً يصدر من الإدارة بعد مراجعة الهوية. لا يسمح أبداً بالدخول برقم الإقامة وحده. ويحتوي على أزرار تبديل اللغات الثلاث. |
| الرئيسية وكشف الحساب | `statement` | البطاقة العلوية في الرئيسية تعرض ملخص آخر كشف راتب معتمد مع شارة واضحة «لك» أو «عليك». وتحتها محدد الشهر يعرض الكشف الشهري المعتمد من نفس مصدر كشف الموقع تماماً، وتظهر فيه البنود المالية بنداً تحت بند مع الرصيد بعد كل بند وتنبيه واضح أن البيان استرشادي وليس مستنداً قانونياً. المسيرات غير المعتمدة لا تظهر أبداً. |
| الإشعارات والتنبيهات | `notifications` | تعرض نوعين من الإشعارات مع حالة القراءة وإيصال استلام محفوظ وروابط تفتح الشاشة أو النموذج المرتبط مباشرة: النوع الأول تعميمات عامة ترسل لكل المناديب على دفعات، والنوع الثاني إشعارات فردية خاصة لا يمكن تعميمها أبداً وتلزم المندوب بفتحها وتأكيد قراءتها. |
| الطلبات والنماذج | `requests` | طلبات متبادلة في الاتجاهين مع سجل مراحل الطلب والردود لسبعة أنواع من الطلبات، بالإضافة إلى الحملات والنماذج المفتوحة التي يرسلها المندوب مباشرة من داخل التطبيق دون إعادة كتابة رقم الإقامة. |
| ملخص المديونيات | `debts` | يعرض إجمالي المديونية المتبقية، وإجمالي المسدد، وإجمالي الإعفاءات، وتفاصيل كل مديونية مع القسط الشهري وشارة «عليك» بنفس التصميم البسيط، وبدون ذكر اسم الشركة أو السجل التجاري. |
| أجهزتي | `devices` | تعرض الجلسات النشطة والمنتهية والملغاة الخاصة بالمندوب مع بصمة آمنة مختصرة للجهاز من ثمانية رموز دون كشف رموز الجلسة السرية، مع زر تسجيل الخروج الذي يلغي الجلسة على الخادم. |

### 3. مسار الطلبات والإشعارات
| نوع الطلب أو الإشعار | المعرّف | مسار التوجيه والاعتماد |
|---|---|---|
| طلب سلفة | `advance` | يرسله المندوب من التطبيق فيذهب فقط إلى مشرفه المباشر الحالي. المشرف إما يحوله إلى طلب سلفة رسمي يذهب لقسم الحسابات، أو يرفضه مع كتابة سبب يظهر للمندوب. |
| طلب إجازة سنوية | `annual_leave` | نموذج جديد بثلاث لغات يشمل نوع الإجازة (سنوية أو اضطرارية أو بدون راتب)، وتاريخ البداية، وتاريخ النهاية، والوجهة (داخل المملكة أو سفر خارج المملكة)، ورقم جوال التواصل، والسبب. تراجعه الإدارة وتبت فيه مع تحديث سجل المراحل وإرسال إشعار فوري للمندوب. |
| تحديث بيانات الآيبان | `iban_update` | يستخدم نفس فحص رقم الحساب البنكي الدولي وقائمة البنوك المعتمدة. إذا كان الآيبان المرسل مطابقاً للآيبان المسجل حالياً للمندوب، يُعتمد تلقائياً فوراً. وإذا كان مختلفاً، يشترط **معتمدين مستقلين** (موافقة أولى من مراجع أول، ثم موافقة نهائية من معتمد ثانٍ مختلف) قبل تحديث الحساب البنكي المعتمد. |
| تحديث البيانات الشخصية | `data_update` | لتحديث رقم الجوال أو المدينة أو السكن أو رقم الطوارئ. يشترط أيضاً **معتمدين مستقلين** (اعتماد أول ثم اعتماد ثانٍ من مستخدم آخر). |
| طلب كشف حساب | `statement_request` | لطلب كشف راتب شهري أو كشف حساب شامل أو بيان مديونيات لفترة يحددها المندوب من شهر بداية إلى شهر نهاية. |
| شكوى سرية | `complaint` | تُحفظ كشكوى سرية تماماً، وتُحجب عن المشرفين والموظفين العاديين؛ لا يطلع عليها ولا يرد عليها إلا المندوب صاحب الشكوى ومسؤول النظام أو من يحمل صلاحية الشكاوى السرية. |
| طلب موعد مراجعة | `appointment` | لحجز موعد مع الموارد البشرية أو المالية أو الأسطول أو التشغيل أو الإدارة في تاريخ مفضل وفترة صباحية أو مسائية. |
| طلب وارد من الإدارة للمندوب | `admin_to_rider` | تقدر الإدارة تبعت طلباً موجهاً لمندوب محدد، ويظهر عند المندوب في تبويب طلباتي، ويقدر الطرفان يتبادلوا الردود عليه خطوة بخطوة. |
| إشعارات فردية لا تُعمّم أبداً | `individual_notice` | تشمل خمسة أنواع: استدعاء إداري، وإنذار فصل، وخصم إداري، وتنبيه موعد، وإنذار إداري. الخادم يرفض إرسال أي نوع منها كتعميم عام للكل، ويجبر تفعيل شرط الإقرار بالقراءة، ويحفظ إيصال القراءة والتأكيد بالتاريخ والوقت. |

### 4. خطوات بناء وتثبيت حزمة تطبيق الأندرويد التجريبية
1. تأكد من وجود بيئة التشغيل المطلوبة لبناء الويب والأندرويد على جهازك.
2. ضع ملف إعدادات مشروع الإشعارات الخاص بتطبيق الأندرويد في المسار التالي:
```bash
~/Private/gate-data/firebase/google-services.json
```
3. شغّل أمر البناء التجريبي من داخل مجلد تطبيق الجوال:
```bash
cd /home/ghazal/Projects/active/gate-payroll-rider-app/mobile
./scripts/build-debug.sh
```
4. الأمر بيتأكد من وجود ملف الإعدادات، وينسخه داخل مجلد مشروع الأندرويد (وهو مستبعد من الحفظ في المستودع)، ويبني ملفات الواجهة، ويزامن المشروع، ويبني حزمة الأندرويد التجريبية، وينسخ الملف النهائي إلى المسار التالي:
```bash
~/Private/gate-data/mobile/gate-rider-debug.apk
```
5. لتثبيت الحزمة على جوال أندرويد متوصل بالكابل:
```bash
adb install -r ~/Private/gate-data/mobile/gate-rider-debug.apk
```

### 5. ما يجب أن تكمله بنفسك قبل الإطلاق الفعلي
1. **ملف مشروع الإشعارات لتطبيق الأندرويد**:
   - افتح لوحة تحكم خدمة الإشعارات السحابية وسجّل تطبيق أندرويد باسم الحزمة التالي:
```text
com.albawaba.gate.rider
```
   - نزّل ملف الإعدادات وضعه في هذا المسار:
```bash
~/Private/gate-data/firebase/google-services.json
```
2. **متغير البيئة لمفتاح حساب الخدمة على الخادم (لا يُحفظ في القاعدة ولا في المستودع)**:
   - ولّد مفتاح حساب خدمة جديد من إعدادات مشروع الإشعارات.
   - اضبط محتوى المفتاح كمتغير بيئة على الخادم بالاسم التالي:
```text
Firebase__ServiceAccountJson
```
   - بعد النشر، احفظ إعدادات الإشعارات مرة واحدة من شاشة الإدارة لمسح أي أثر لمفتاح قديم محفوظ.
3. **تجهيز حسابات الاختبار**:
   - أنشئ مندوب اختبار ببيانات وهمية (مثلاً إقامة رقم `2000000741` وجوال `0501234567` وحالة نشطة).
   - اربط المندوب بمشرف اختبار في جدول الإشراف لتجربة دورة طلب السلفة كاملة من المندوب للمشرف للمحاسب.
   - أصدر رمز دخول جوال لمرة واحدة من شاشة الإدارة بعد التأشير على مراجعة الهوية.

### 6. إدارة التحمّل والحدود المعروفة
| الضابط أو الحد | القيمة المضبوطة | السلوك في النظام |
|---|---|---|
| حد محاولات تسجيل الدخول | `5` محاولات في الدقيقة | نافذة متحركة لكل عنوان اتصال ولكل بصمة إقامة، وترجع رمز خطأ تجاوز الحد عند الزيادة. |
| حد طلبات القراءة للمندوب | `60` طلباً في الدقيقة | نافذة دقيقة واحدة لكل جلسة مندوب نشطة، وترجع تنبيه تجاوز حد الطلبات مع مهلة انتظار ستين ثانية. |
| حد طلبات الكتابة للمندوب | `20` طلباً في الدقيقة | نافذة دقيقة واحدة لكل جلسة مندوب عند إرسال الطلبات أو الردود أو تأكيد الإشعارات. |
| سقف عناصر البوابة والقوائم | `50` إشعاراً و `100` مديونية و `100` طلب | يحد حجم البيانات في كل استجابة، مع دعم تقسيم الصفحات الاختياري بالصفحة وحجم الصفحة. |
| دفعات صندوق الإشعارات وإعادة المحاولة | `50` رسالة لكل دفعة وبحد أقصى `3` محاولات | يُفرغ صندوق الصادر في الخلفية على دفعات من خمسين رسالة بمهلة اتصال عشر ثوانٍ وتأخير تدريجي (دقيقة ثم خمس دقائق)، ويمسح رمز الجهاز تلقائياً إذا رجع أنه غير مسجل. |
| إرسال إشعارات كشوف الرواتب بالجملة | تكرار آمن لكل شهر وإقامة | نقطة إشعار الكشوف المعتمدة ترسل تنبيهاً للمناديب الذين صدرت لهم كشوف معتمدة في الشهر المحدد عبر صندوق الصادر دون وضع أي أرقام رواتب داخل نص الإشعار الخارجي. |
| غياب ملف إعدادات الإشعارات | إيقاف واضح في أمر البناء | أمر البناء لا ينشئ ملفاً مزيفاً أبداً؛ بل يتوقف برسالة إرشادية واضحة حتى تضع الملف الحقيقي في مساره. |
