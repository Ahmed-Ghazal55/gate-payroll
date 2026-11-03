import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function AccountingPage() {
  return (
    <UnderMigration
      sectionTitle="المحاسبة والتقارير المالية"
      legacyHash="ledger"
      description="دليل شجرة الحسابات، ميزان المراجعة، قائمة الدخل، وكشف حساب الشركات والمناديب."
      features={[
        'دليل شجرة الحسابات المحاسبية',
        'ميزان المراجعة التحليلي',
        'قائمة الدخل ومراكز التكلفة',
        'كشوف حسابات الشركات الشريكة',
      ]}
    />
  );
}
