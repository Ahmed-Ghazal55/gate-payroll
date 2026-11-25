'use client';

import React, { Suspense } from 'react';
import { SuppliersWorkspace } from '../../../features/payments/SuppliersWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <SuppliersWorkspace />
    </Suspense>
  );
}
