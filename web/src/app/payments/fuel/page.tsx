'use client';

import React, { Suspense } from 'react';
import { PaymentsWalletWorkspace } from '../../../features/payments/PaymentsWalletWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

function FuelPaymentsPage() {
  return (
    <PaymentsWalletWorkspace
      mode="fuel"
      title="الوقود والمحطات"
      description="محافظ الوقود والتمويل والمطابقة الشهرية والاستهلاك المباشر من نظام الموارد."
    />
  );
}

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" />}>
      <FuelPaymentsPage />
    </Suspense>
  );
}
