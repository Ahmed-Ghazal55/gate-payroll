'use client';

import React, { Suspense } from 'react';
import { PaymentsWalletWorkspace } from '../../../features/payments/PaymentsWalletWorkspace';
import { Skeleton } from '../../../components/ui/EmptyState';

function MadaPaymentsPage() {
  return (
    <PaymentsWalletWorkspace
      mode="mada"
      title="جهاز مدا والشحن"
      description="تغذية محفظة مدا، المطابقة، وصرف بطاقات شحن الإنترنت عبر محرك المدفوعات."
    />
  );
}

export default function Page() {
  return (
    <Suspense fallback={<Skeleton className="h-40 w-full" />}>
      <MadaPaymentsPage />
    </Suspense>
  );
}
