import React from 'react';
import { UnderMigration } from '../../components/common/UnderMigration';

export default function CompaniesPage() {
  return (
    <UnderMigration
      sectionTitle="الشركات والسجلات التجارية"
      legacyHash="sponsors"
      description="إدارة الشركات والكفلاء، السجلات التجارية، وثائق الشركة وتواريخ انتهائها، والكفالات والقضايا القانونية."
      features={[
        'دليل الشركات والكفلاء (Sponsors)',
        'السجلات التجارية والفرعية',
        'متابعة انتهاء الوثائق والتراخيص',
        'الكفالات والغرامات والقضايا',
      ]}
    />
  );
}
