'use client';

import React, { Suspense } from 'react';
import { HrRequestsWorkspace } from '../../../features/payments/HrRequestsWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <HrRequestsWorkspace />
    </Suspense>
  );
}
