# تطبيق مندوب البوابة — Capacitor Android

تطبيق الجوال الرسمي لمناديب شركة البوابة للخدمات اللوجستية، مبني باستخدام **Vite + TypeScript + Capacitor 6**.

## متطلبات التشغيل
- Node.js 20+
- Android Studio / Android SDK (لبناء حزمة APK)
- Java 17/21

## أوامر التطوير والبناء
```bash
# تثبيت الاعتماديات
npm install

# تشغيل خادم التطوير المحلي
npm run dev

# بناء ملفات الويب
npm run build

# مزامنة ملفات الويب مع مشروع أندرويد
npm run cap:sync

# فتح المشروع في Android Studio
npm run cap:android
```

## خطوات غزال لتفعيل الإشعارات وتوليد نسخة التجربة (Debug APK)
1. إنشاء مشروع Firebase مجاني (Spark Plan).
2. إضافة تطبيق Android بالحزمة: `com.albawaba.gate.rider`.
3. تنزيل ملف `google-services.json` ووضعه في:
   `~/Private/gate-data/firebase/google-services.json`
4. إنشاء مفتاح حساب خدمة (Service Account Key) وتعيينه كمتغير بيئة في سيرفر MonsterASP:
   `Firebase__ServiceAccountJson` ثم إعادة تشغيل الموقع.
5. تشغيل سكريبت البناء:
   `./scripts/build-debug.sh`
6. تثبيت ملف `~/Private/gate-data/mobile/gate-rider-debug.apk` على الجوال وتسجيل الدخول برمز المندوب.
7. إرسال تعميم تجريبي من لوحة التحكم `/app/rider-app` للتأكد من وصول الإشعار الفوري.
