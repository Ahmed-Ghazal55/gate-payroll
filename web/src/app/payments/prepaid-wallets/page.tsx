'use client';

import React, { Suspense } from 'react';
import { PaymentsWalletWorkspace } from '../../../features/payments/PaymentsWalletWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

function PrepaidPaymentsPage() {
  return (
    <PaymentsWalletWorkspace
      mode="prepaid"
      title="المحافظ المدفوعة مقدمًا"
      description="محافظ هيئة النقل وأجير وغيرها مع شحن الحركات عبر محرك المدفوعات."
    />
  );
}

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" />}>
      <PrepaidPaymentsPage />
    </Suspense>
  );
}
