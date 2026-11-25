'use client';

import React, { Suspense } from 'react';
import { RecurringPayeesWorkspace } from '../../../features/payments/RecurringPayeesWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

function RecurringPayeesPage() {
  return <RecurringPayeesWorkspace mode="payees" />;
}

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" />}>
      <RecurringPayeesPage />
    </Suspense>
  );
}
