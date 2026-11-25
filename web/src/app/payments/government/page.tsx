'use client';

import React, { Suspense } from 'react';
import { GovernmentPaymentsWorkspace } from '../../../features/payments/GovernmentPaymentsWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <GovernmentPaymentsWorkspace />
    </Suspense>
  );
}
