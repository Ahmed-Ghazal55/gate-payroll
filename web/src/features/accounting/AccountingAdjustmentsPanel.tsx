'use client';

import React from 'react';
import { useRouter } from 'next/navigation';
import { EmptyState } from '../../components/ui/EmptyState';

/** Adjustments UI retired with payments engine (410 on old routes). Keep the tab shell for redirects. */
export function AccountingAdjustmentsPanel(_props: {
  companies: Array<{ id: string; code?: string; nameAr?: string }>;
}) {
  const router = useRouter();
  return (
    <EmptyState
      title="شاشة التسويات موقوفة"
      description="مدا في المدفوعات ← جهاز مدا والشحن، وتعديلات الرواتب في بنود التعديل، والرسوم الإدارية في طلبات الخصم."
      action={{
        label: 'جهاز مدا والشحن',
        onClick: () => router.push('/payments/mada'),
      }}
    />
  );
}
