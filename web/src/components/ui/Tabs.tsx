'use client';

import React, { Suspense } from 'react';
import { useRouter, useSearchParams, usePathname } from 'next/navigation';

export interface TabItem {
  id: string;
  label: string;
  icon?: React.ReactNode;
  badge?: number | string;
}

export interface TabsProps {
  tabs: TabItem[];
  activeTab?: string;
  onChange?: (id: string) => void;
  paramName?: string;
  className?: string;
}

function TabsInner({
  tabs,
  activeTab,
  onChange,
  paramName = 'tab',
  className = '',
}: TabsProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const pathname = usePathname();

  const currentTab = activeTab || searchParams?.get(paramName) || tabs[0]?.id;

  const handleSelect = (id: string) => {
    if (onChange) {
      onChange(id);
    } else if (searchParams && router) {
      const params = new URLSearchParams(searchParams.toString());
      params.set(paramName, id);
      router.push(`${pathname}?${params.toString()}`, { scroll: false });
    }
  };

  return (
    <div
      role="tablist"
      aria-label="علامات التبويب"
      className={`flex items-center gap-1 border-b border-[var(--line)] overflow-x-auto no-scrollbar py-1 ${className}`}
    >
      {tabs.map((tab) => {
        const isActive = currentTab === tab.id;
        return (
          <button
            key={tab.id}
            role="tab"
            aria-selected={isActive}
            tabIndex={isActive ? 0 : -1}
            onClick={() => handleSelect(tab.id)}
            className={`inline-flex items-center gap-2 px-3.5 py-2 text-xs sm:text-sm font-semibold rounded-t-lg border-b-2 transition-all whitespace-nowrap -mb-[5px] ${
              isActive
                ? 'border-[var(--accent)] text-[var(--accent)] bg-[var(--surface)]'
                : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--line)]'
            }`}
          >
            {tab.icon}
            <span>{tab.label}</span>
            {tab.badge !== undefined && (
              <span
                className={`num text-[10px] px-1.5 py-0.5 rounded-full font-bold ${
                  isActive
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                    : 'bg-[var(--raised)] text-[var(--muted)]'
                }`}
              >
                {tab.badge}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

export function Tabs(props: TabsProps) {
  return (
    <Suspense
      fallback={
        <div className="flex items-center gap-2 border-b border-[var(--line)] py-2">
          {props.tabs.map((t) => (
            <span key={t.id} className="text-xs text-[var(--muted)] px-3 py-1">
              {t.label}
            </span>
          ))}
        </div>
      }
    >
      <TabsInner {...props} />
    </Suspense>
  );
}
