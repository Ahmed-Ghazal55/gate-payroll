'use client';

import React, { useState } from 'react';
import { Sidebar } from './Sidebar';
import { Header } from './Header';
import { ToastProvider } from '../ui/Toast';

export interface AppShellProps {
  children: React.ReactNode;
}

export function AppShell({ children }: AppShellProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [company, setCompany] = useState('all');
  const [period, setPeriod] = useState('2026-10');

  return (
    <ToastProvider>
      <div className="flex min-h-screen bg-[var(--bg)] text-[var(--ink)] antialiased">
        {/* Skip to main content for accessibility */}
        <a
          href="#main-content"
          className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:start-3 focus:z-50 focus:px-4 focus:py-2 focus:bg-[var(--accent)] focus:text-[var(--accent-ink)] focus:rounded-lg focus:font-bold"
        >
          الانتقال مباشرة للمحتوى الأساسي
        </a>

        {/* Sidebar */}
        <Sidebar
          collapsed={collapsed}
          onToggleCollapse={() => setCollapsed(!collapsed)}
          mobileOpen={mobileOpen}
          onCloseMobile={() => setMobileOpen(false)}
        />

        {/* Content Area */}
        <div className="flex-1 flex flex-col min-w-0 min-h-screen">
          <Header
            onToggleMobileNav={() => setMobileOpen(true)}
            selectedCompany={company}
            onCompanyChange={setCompany}
            selectedPeriod={period}
            onPeriodChange={setPeriod}
          />

          <main
            id="main-content"
            className="flex-1 p-3 sm:p-6 lg:p-8 max-w-7xl w-full mx-auto"
          >
            {children}
          </main>
        </div>
      </div>
    </ToastProvider>
  );
}
