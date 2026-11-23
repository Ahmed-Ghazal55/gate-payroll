'use client';

import React, { Suspense } from 'react';
import { TrafficViolationsWorkspace } from '../../../features/payments/TrafficViolationsWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <TrafficViolationsWorkspace />
    </Suspense>
  );
}
