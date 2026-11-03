import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function AdminPage() {
  return (
    <UnderMigration
      sectionTitle="إدارة النظام والمستخدمين"
      legacyHash="users"
      description="إدارة المستخدمين والأدوار والصلاحيات، سجل عمليات التدقيق الكامل، وإعدادات الربط والمنظومة."
      features={[
        'إدارة حسابات المستخدمين والأدوار',
        'مصفوفة الصلاحيات وتدقيق الوصول',
        'سجل عمليات التدقيق الشامل (Audit Log)',
        'إعدادات الإشعارات ورسائل الجوال',
      ]}
    />
  );
}
