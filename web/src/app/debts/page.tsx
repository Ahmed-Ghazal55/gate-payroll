import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function DebtsPage() {
  return (
    <UnderMigration
      sectionTitle="المديونيات والسلف والتحصيل"
      legacyHash="debts"
      description="سجل مديونيات المناديب، التحصيل المجمع، رصيد أول المدة ومطابقة الافتتاح، وسندات الأمر التنفيذية."
      features={[
        'سجل المديونيات والأقساط المستحقة',
        'التحصيل المجمع واستقطاع الرواتب',
        'أرصدة أول المدة والمطابقات',
        'متابعة سندات الأمر التنفيذية',
      ]}
    />
  );
}
