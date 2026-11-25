'use client';

import Link from 'next/link';
import { EmptyState } from '../../components/ui/EmptyState';

/**
 * FLOW1: POST /api/treasury/transfers always returns 409 (RefuseTransfer) until payment-engine merge.
 * Show an honest blocked state instead of an empty shell.
 */
export function TreasuryTransfersPanel() {
  return (
    <div className="space-y-4" data-testid="treasury-transfers-panel">
      <EmptyState
        title="تحويلات الخزينة موقوفة مؤقتًا"
        description="نقطة التحويل ترجع رفضًا تعاقديًا حتى تُدمج مع محرك المدفوعات المعتمدة. استخدم تسجيل المدفوعات أو التحويل عبر مستند دفع معتمد."
      />
      <div className="flex flex-wrap gap-2">
        <Link
          href="/payments/quick"
          className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
        >
          مدفوعات سريعة
        </Link>
        <Link
          href="/approvals/"
          className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold"
        >
          صندوق الاعتمادات
        </Link>
      </div>
    </div>
  );
}
