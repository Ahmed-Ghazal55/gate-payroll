'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
/** U-W3-01: promissory moves to its own sidebar route; reuse debts tab until U-W3-11. */
export default function DebtsPromissoryPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/debts/?tab=promissory');
  }, [router]);
  return <div className="p-6 text-sm text-[var(--muted)]">جاري التحويل إلى أرشيف سندات الأمر…</div>;
}
