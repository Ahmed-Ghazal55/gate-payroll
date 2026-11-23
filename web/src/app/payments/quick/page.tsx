'use client';

import React, { Suspense } from 'react';
import { QuickPaymentWorkspace } from '../../../features/payments/QuickPaymentWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <QuickPaymentWorkspace />
    </Suspense>
  );
}
