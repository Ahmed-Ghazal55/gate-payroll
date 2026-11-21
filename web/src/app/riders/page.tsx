'use client';

import React, { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Skeleton } from '../../components/ui/EmptyState';

function RidersRedirectInner() {
  const router = useRouter();
  const searchParams = useSearchParams();

  useEffect(() => {
    const qs = searchParams?.toString();
    router.replace(qs ? `/workforce/?${qs}` : '/workforce/');
  }, [router, searchParams]);

  return (
    <div className="p-8 space-y-4">
      <Skeleton className="h-10 w-64" />
      <p className="text-sm text-[var(--muted)]">جاري التحويل إلى صفحة القوى العاملة...</p>
    </div>
  );
}

export default function RidersRedirectPage() {
  return (
    <Suspense
      fallback={
        <div className="p-8 space-y-4">
          <Skeleton className="h-10 w-64" />
        </div>
      }
    >
      <RidersRedirectInner />
    </Suspense>
  );
}
