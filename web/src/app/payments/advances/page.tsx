'use client';

import React, { Suspense } from 'react';
import { AdvancesWorkspace } from '../../../features/payments/AdvancesWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <AdvancesWorkspace />
    </Suspense>
  );
}
