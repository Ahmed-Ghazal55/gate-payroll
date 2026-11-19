'use client';

// Section 9 (R3, U-W3-16): «تطبيق المناديب والنماذج» — one admin section for forms, responses,
// the builder, app accounts, notifications, statements, requests and app settings.
// Navigation is the 8 sidebar entries (`?tab=`); rule 3.0: no second horizontal bar of the same tabs here.
import React, { Suspense, useEffect } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { PageHeader } from '../../components/ui/PageHeader';
import { Skeleton, ErrorState } from '../../components/ui/EmptyState';
import { useAuth } from '../../lib/auth-context';
import { RIDER_APP_TAB_LABELS, resolveRiderAppTab, type RiderAppTab } from '../../features/rider-app/tabs';
import { FormsPanel } from '../../features/rider-app/FormsPanel';
import { AccountsPanel } from '../../features/rider-app/AccountsPanel';
import { BroadcastsPanel } from '../../features/rider-app/BroadcastsPanel';
import { StatementsPanel } from '../../features/rider-app/StatementsPanel';
import { RequestsPanel } from '../../features/rider-app/RequestsPanel';
import { SettingsPanel } from '../../features/rider-app/SettingsPanel';

// Same permissions as the sidebar entries and the API route policy (AccessPolicy.cs).
const TAB_PERMISSIONS: Record<RiderAppTab, string[]> = {
  campaigns: ['forms.view'],
  responses: ['forms.view'],
  builder: ['forms.view'],
  accounts: ['mobile.sessions'],
  broadcasts: ['mobile.broadcast'],
  statements: ['mobile.broadcast'],
  requests: ['mobile.broadcast', 'forms.view', 'forms.review'],
  settings: [],
};

function RiderAppSection() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { can, isAdmin } = useAuth();
  const resolved = resolveRiderAppTab(searchParams?.get('tab'));
  const tab = resolved.tab;
  const subtab = searchParams?.get('subtab') || resolved.subtab;

  // Old ids (statement-preview, analytics, unknown) are replaced in the URL so links stay clean.
  useEffect(() => {
    if (!resolved.legacy) return;
    const params = new URLSearchParams(searchParams?.toString() || '');
    params.set('tab', tab);
    if (resolved.subtab) params.set('subtab', resolved.subtab);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }, [resolved.legacy, resolved.subtab, tab, pathname, router, searchParams]);

  const allowed = isAdmin || (tab !== 'settings' && TAB_PERMISSIONS[tab].some((p) => can(p)));

  return (
    <div className="space-y-6">
      <PageHeader
        title="تطبيق المناديب والنماذج"
        eyebrow={RIDER_APP_TAB_LABELS[tab]}
        description="نظام نماذج واحد لروابط الويب وتطبيق المناديب: الحملات والردود والمصمم، حسابات التطبيق، الإشعارات، كشوف الحساب والطلبات."
      />
      {!allowed ? (
        <ErrorState title="لا تملك صلاحية هذا القسم" message="اطلب من مدير النظام منحك الصلاحية المناسبة." />
      ) : tab === 'campaigns' || tab === 'responses' || tab === 'builder' ? (
        <FormsPanel tab={tab} subtab={subtab} />
      ) : tab === 'accounts' ? (
        <AccountsPanel canIssueCode={isAdmin || can('inquiry.issue')} />
      ) : tab === 'broadcasts' ? (
        <BroadcastsPanel subtab={subtab} />
      ) : tab === 'statements' ? (
        <StatementsPanel canSend={isAdmin || can('mobile.broadcast')} />
      ) : tab === 'requests' ? (
        <RequestsPanel subtab={subtab} />
      ) : (
        <SettingsPanel isAdmin={isAdmin} />
      )}
    </div>
  );
}

export default function RiderAppPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <RiderAppSection />
    </Suspense>
  );
}
