'use client';

import React, { Suspense } from 'react';
import { HsCustodyWorkspace } from '../../../features/payments/HsCustodyWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <HsCustodyWorkspace />
    </Suspense>
  );
}
