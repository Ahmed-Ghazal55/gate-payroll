'use client';

import React, { Suspense } from 'react';
import { DueCalendarWorkspace } from '../../../features/payments/DueCalendarWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-44 w-full" />}>
      <DueCalendarWorkspace />
    </Suspense>
  );
}
