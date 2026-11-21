'use client';

import React, { Suspense, useEffect, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Sidebar } from './Sidebar';
import { Header } from './Header';
import { ToastProvider } from '../ui/Toast';
import { AuthProvider } from '../../lib/auth-context';

export interface AppShellProps {
  children: React.ReactNode;
}

function currentMonthPeriod(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

function AppShellInner({ children }: AppShellProps) {
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [company, setCompany] = useState('all');

  const urlPeriod = searchParams?.get('period') || '';
  const [period, setPeriod] = useState(urlPeriod || currentMonthPeriod());

  useEffect(() => {
    if (urlPeriod && urlPeriod !== period) setPeriod(urlPeriod);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlPeriod]);

  const changePeriod = (next: string) => {
    setPeriod(next);
    const q = new URLSearchParams(searchParams?.toString() || '');
    q.set('period', next);
    const qs = q.toString();
    router.replace(`${pathname}${qs ? `?${qs}` : ''}`);
  };

  const isAuthPage =
    Boolean(pathname && (pathname.includes('/login') || pathname.includes('/setup')));

  if (isAuthPage) {
    return (
      <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)] antialiased flex flex-col justify-center items-center p-4">
        <main id="main-content" className="w-full max-w-md">
          {children}
        </main>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen bg-[var(--bg)] text-[var(--ink)] antialiased">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:start-3 focus:z-50 focus:px-4 focus:py-2 focus:bg-[var(--accent)] focus:text-[var(--accent-ink)] focus:rounded-lg focus:font-bold"
      >
        الانتقال مباشرة للمحتوى الأساسي
      </a>

      <Sidebar
        collapsed={collapsed}
        onToggleCollapse={() => setCollapsed(!collapsed)}
        mobileOpen={mobileOpen}
        onCloseMobile={() => setMobileOpen(false)}
      />

      <div className="flex-1 flex flex-col min-w-0 min-h-screen">
        <Header
          onToggleMobileNav={() => setMobileOpen(true)}
          selectedCompany={company}
          onCompanyChange={setCompany}
          selectedPeriod={period}
          onPeriodChange={changePeriod}
        />

        <main id="main-content" className="flex-1 p-3 sm:p-6 lg:p-8 max-w-7xl w-full mx-auto">
          {children}
        </main>
      </div>
    </div>
  );
}

export function AppShell({ children }: AppShellProps) {
  return (
    <AuthProvider>
      <ToastProvider>
        <Suspense
          fallback={
            <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)] p-6 text-sm text-[var(--muted)]">
              جاري التحميل…
            </div>
          }
        >
          <AppShellInner>{children}</AppShellInner>
        </Suspense>
      </ToastProvider>
    </AuthProvider>
  );
}
