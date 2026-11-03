import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function TreasuryPage() {
  return (
    <UnderMigration
      sectionTitle="البنوك والصناديق والخزن"
      legacyHash="treasury"
      description="أرصدة الحسابات البنكية والخزن (1110–1140)، التحويلات الداخلية، عهدة جاهز وتسليمها، والقيود اليومية."
      features={[
        'كروت الأرصدة البنكية والخزن (1110-1140)',
        'سجل التحويلات والمطابقات البنكية',
        'متابعة عهدة تطبيق جاهز واستلامها',
        'سجل القيود وتوزيع الخزنة التاريخية',
      ]}
    />
  );
}
