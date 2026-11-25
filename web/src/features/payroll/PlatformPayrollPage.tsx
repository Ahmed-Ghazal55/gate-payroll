'use client';

import React, { Suspense } from 'react';
import { PageHeader } from '../../components/ui/PageHeader';

/** Only Amazon stays as an intentional deferred notice; other /payroll/{app} routes redirect. */
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
      <p className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-4 text-sm text-[var(--muted)]">
        هذه الشاشة غير جاهزة بعد. استخدم مساحة عمل الرواتب من القائمة الجانبية.
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <PageHeader title="أمازون" description="منصة مؤجّلة حتى تتوفر فاتورة عيّنة معتمدة." />
      <p className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-4 text-sm text-[var(--muted)]">
        أمازون معلّم «لاحقًا» حتى تتوفر فاتورة عيّنة معتمدة.
      </p>
    </div>
  );
}
