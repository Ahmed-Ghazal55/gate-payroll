import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function OperationsPage() {
  return (
    <UnderMigration
      sectionTitle="التشغيل والأصول الميدانية"
      legacyHash="dashboard"
      description="متابعة المدن التشغيلية، السكن، أسطول المركبات، وسجلات استلام العهد الشهرية للمناديب."
      features={[
        'توزيع المدن ومواقع التشغيل',
        'إدارة السكن وتكاليف الإيواء',
        'سجلات أسطول المركبات واللوحات',
        'تسليم العهد والأجهزة الشهرية',
      ]}
    />
  );
}
