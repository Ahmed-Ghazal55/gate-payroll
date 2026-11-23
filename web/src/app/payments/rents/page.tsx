'use client';

import React, { Suspense } from 'react';
import { RentsWorkspace } from '../../../features/payments/RentsWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <RentsWorkspace />
    </Suspense>
  );
}
