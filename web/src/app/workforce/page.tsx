'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense } from 'react';
/** U-W3-01 / Claude U3: /workforce/ is the Home label; keep /riders/ until the workforce rebuild. */
function WorkforceRedirect() {
  const router = useRouter();
  const searchParams = useSearchParams();
  useEffect(() => {
    const qs = searchParams?.toString();
    router.replace(qs ? `/riders/?${qs}` : '/riders/');
  }, [router, searchParams]);
  return <div className="p-6 text-sm text-[var(--muted)]">جاري التحويل إلى القوى العاملة…</div>;
}

export default function WorkforcePage() {
  return (
    <Suspense fallback={<div className="p-6 text-sm text-[var(--muted)]">جاري التحميل…</div>}>
      <WorkforceRedirect />
    </Suspense>
  );
}
