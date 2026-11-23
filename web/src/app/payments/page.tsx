'use client';

import React, { useEffect, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { appendPeriodToHref, readUrlPeriod } from '../../lib/period';
import { Skeleton } from '../../components/ui/EmptyState';

function PaymentsIndexRedirect() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const urlPeriod = readUrlPeriod(searchParams);

  useEffect(() => {
    router.replace(appendPeriodToHref('/payments/due-calendar', urlPeriod));
  }, [router, urlPeriod]);

  return <Skeleton className="h-40 w-full" />;
}

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" />}>
      <PaymentsIndexRedirect />
    </Suspense>
  );
}
