'use client';

import React, { useMemo, useState, Suspense } from 'react';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import Link from 'next/link';
import { PageHeader } from '../../components/ui/PageHeader';
import { SimulationBanner, SIMULATION_BLOCKED_HINT } from '../../components/payroll/SimulationBanner';
import { useAuth } from '../../lib/auth-context';

const APP_LABELS: Record<string, string> = {
  keeta: 'كيتا',
  hungerstation: 'هنقرستيشن',
  ninja: 'نينجا',
  amazon: 'أمازون',
  chefz: 'شيفز',
  jahez: 'جاهز',
  mudad: 'رواتب مدد',
  'bank-runs': 'مسيرات الرواتب والبنك',
  holds: 'طلبات إيقاف والخصم',
  settings: 'إعدادات الرواتب',
};

export function PlatformPayrollPage({ app }: { app: string }) {
  return (
    <Suspense fallback={<div className="p-4 text-sm text-[var(--muted)]">جاري التحميل…</div>}>
      <PlatformPayrollInner app={app} />
    </Suspense>
  );
}

function PlatformPayrollInner({ app }: { app: string }) {
  const { can } = useAuth();
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const title = APP_LABELS[app] ?? app;
  const runMode = searchParams?.get('runMode') || 'live';
  const simulationId = searchParams?.get('simulationId') || '';
  const payoutBlocked = searchParams?.get('payoutBlocked') === '1';
  const [tick, setTick] = useState(0);

  const isSimulation = runMode === 'simulation' && Boolean(simulationId);

  const legacyHref = useMemo(() => {
    if (app === 'settings') return '/payroll/?tab=rules';
    if (app === 'bank-runs') return '/payroll/?tab=bank';
    if (app === 'holds') return '/payroll/?tab=deductions';
    if (app === 'mudad') return '/payroll/?tab=platforms';
    return `/payroll/?tab=platforms&app=${encodeURIComponent(app)}`;
  }, [app]);

  const startSimulation = () => {
    if (!can('salaries.simulation.run')) return;
    const q = new URLSearchParams(searchParams?.toString() || '');
    q.set('runMode', 'simulation');
    if (!q.get('simulationId')) q.set('simulationId', `sim-${app}-${Date.now()}`);
    router.replace(`${pathname}?${q.toString()}`);
  };

  return (
    <div className="space-y-4" key={tick}>
      <PageHeader
        title={title}
        description="شاشة رواتب المنصة — الأزرار الحساسة تتبع وضع التشغيل الفعلي أو المحاكاة."
        primaryAction={
          can('salaries.simulation.run') && app !== 'amazon'
            ? {
                label: 'بدء محاكاة جديدة',
                onClick: startSimulation,
              }
            : undefined
        }
      />

      {isSimulation ? (
        <SimulationBanner
          simulationId={simulationId}
          payoutBlocked={payoutBlocked}
          onChanged={() => setTick((n) => n + 1)}
        />
      ) : null}

      {app === 'amazon' ? (
        <p className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-4 text-sm text-[var(--muted)]">
          أمازون معلّم «لاحقًا» حتى تتوفر فاتورة عيّنة معتمدة.
        </p>
      ) : (
        <div className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3">
          <p className="text-sm text-[var(--ink)]">
            مسار الشاشة الجانبي جاهز. يمكنك فتح مساحة العمل الحالية للمنصة عبر الرابط أدناه إلى أن تُنقل اللوحات إلى هذا المسار بالكامل.
          </p>
          <Link href={legacyHref} className="inline-flex text-sm font-semibold text-[var(--accent)] underline-offset-2 hover:underline">
            فتح مساحة العمل الحالية
          </Link>
          {isSimulation ? (
            <p className="text-xs text-[var(--warning)]" title={SIMULATION_BLOCKED_HINT}>
              تصدير البنك وترحيل القيود وإرسال إشعار المندوب: {SIMULATION_BLOCKED_HINT}
            </p>
          ) : null}
        </div>
      )}
    </div>
  );
}
