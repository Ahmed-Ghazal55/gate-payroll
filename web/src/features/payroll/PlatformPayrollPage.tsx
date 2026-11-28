'use client';

import React, { Suspense } from 'react';
import Link from 'next/link';
import { PageHeader } from '../../components/ui/PageHeader';
import { EmptyState } from '../../components/ui/EmptyState';

/** Redirected / direct platform notice without stub copy. */
export function PlatformPayrollPage({ app }: { app: string }) {
  return (
    <Suspense fallback={<div className="p-4 text-sm text-[var(--muted)]">جاري التحميل…</div>}>
      <PlatformPayrollInner app={app} />
    </Suspense>
  );
}

function PlatformPayrollInner({ app }: { app: string }) {
  if (app !== 'amazon') {
    return (
      <EmptyState
        title="مساحة عمل المنصة في صفحة الرواتب"
        description="افتح تبويب المنصات في صفحة الرواتب لإدارة الفواتير والتسويات والشروط."
      />
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader title="أمازون" description="إدارة فواتير وشروط منصة أمازون من مساحة عمل الرواتب." />
      <EmptyState
        title="لا توجد فواتير أو تسويات لمنصة أمازون"
        description="لم تُرفع فواتير أو تسويات لمنصة أمازون بعد. يمكنك إعداد الشروط الأصلية والأنظمة الداخلية من مساحة عمل الرواتب."
      />
      <div>
        <Link
          href="/payroll/?tab=platforms&app=amazon"
          className="inline-flex px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
        >
          فتح مساحة عمل أمازون
        </Link>
      </div>
    </div>
  );
}
