import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function RidersPage() {
  return (
    <UnderMigration
      sectionTitle="المناديب ودليل السجلات"
      legacyHash="riders"
      description="دليل المناديب الموحد، ملف المندوب 360، جودة البيانات والمطابقة مع المنصات."
      features={[
        'دليل المناديب وبحث الإقامات',
        'ملف المندوب 360 ومستنداته',
        'فحص جودة البيانات وتكرار الهويات',
        'مزامنة الحسابات مع التطبيقات',
      ]}
    />
  );
}
