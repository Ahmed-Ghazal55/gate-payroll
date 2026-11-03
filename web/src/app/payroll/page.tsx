import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function PayrollPage() {
  return (
    <UnderMigration
      sectionTitle="الرواتب والمسيرات الشهرية"
      legacyHash="payroll"
      description="سياسة الاحتساب للتطبيقات (كيتا/جاهز/شفز/نينجا/هنقر/أمازون)، استيراد البيانات، المسير الشهري، وملف البنك CSV المعتمد."
      features={[
        'سياسة الاحتساب بحسب كل تطبيق ونظام عمل',
        'استيراد ومطابقة ملفات التشغيل',
        'المسير الشهري وتدقيق الاستقطاعات والاعتماد',
        'تصدير ملف البنك CSV المعتمد للصرف',
        'كشوف حساب المناديب 360 والمبسطة',
      ]}
    />
  );
}
