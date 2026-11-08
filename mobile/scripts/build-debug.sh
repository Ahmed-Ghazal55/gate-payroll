#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOBILE_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
FIREBASE_DIR="$HOME/Private/gate-data/firebase"
OUTPUT_DIR="$HOME/Private/gate-data/mobile"
SRC_GOOGLE_SERVICES="$FIREBASE_DIR/google-services.json"
TARGET_GOOGLE_SERVICES="$MOBILE_DIR/android/app/google-services.json"
TARGET_APK_DIR="$MOBILE_DIR/android/app/build/outputs/apk/debug"
FINAL_APK="$OUTPUT_DIR/gate-rider-debug.apk"

echo "=== فحص ملف إعدادات Firebase ==="
if [ ! -f "$SRC_GOOGLE_SERVICES" ]; then
  echo "خطأ: ملف google-services.json غير موجود في: $SRC_GOOGLE_SERVICES"
  echo "يرجى تنزيله من لوحة تحكم Firebase (تطبيق com.albawaba.gate.rider) ووضعه في المسار أعلاه ثم إعادة تشغيل هذا السكريبت."
  exit 1
fi

echo "نسخ ملف google-services.json إلى مشروع الأندرويد..."
cp "$SRC_GOOGLE_SERVICES" "$TARGET_GOOGLE_SERVICES"

echo "=== بناء ملفات تطبيق الويب ==="
cd "$MOBILE_DIR"
npm run build
npm run cap:sync

echo "=== بناء حزمة APK (Debug) ==="
cd "$MOBILE_DIR/android"

chmod +x ./gradlew
./gradlew assembleDebug

mkdir -p "$OUTPUT_DIR"
if [ -f "$TARGET_APK_DIR/app-debug.apk" ]; then
  cp "$TARGET_APK_DIR/app-debug.apk" "$FINAL_APK"
  echo "=== تم بناء وتجهيز الحزمة بنجاح! ==="
  echo "حزمة التطبيق جاهزة للتثبيت في: $FINAL_APK"
else
  echo "خطأ: لم يتم العثور على الحزمة الناتجة في: $TARGET_APK_DIR/app-debug.apk"
  exit 1
fi
