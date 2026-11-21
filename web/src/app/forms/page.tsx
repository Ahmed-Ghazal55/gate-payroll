'use client';

// R3: the old admin page /forms/ now lives in «تطبيق المناديب والنماذج» (/rider-app/?tab=…).
// Redirect only; the public form links /forms/<token> are served by the API and are not touched.
import React, { Suspense, useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Skeleton } from '../../components/ui/EmptyState';
import { formsRedirectTarget } from '../../features/rider-app/tabs';

function FormsRedirect() {
  const router = useRouter();
  const searchParams = useSearchParams();
  useEffect(() => {
    router.replace(formsRedirectTarget(searchParams?.toString() || ''));
  }, [router, searchParams]);
  return <Skeleton rows={4} className="h-16" />;
}

export default function FormsPage() {
  return (
    <Suspense fallback={<Skeleton rows={4} className="h-16" />}>
      <FormsRedirect />
    </Suspense>
  );
}
