'use client';

import React, { Suspense, useRef, useEffect } from 'react';
import { useRouter, useSearchParams, usePathname } from 'next/navigation';

export interface SectionTabItem {
  id: string;
  label: string;
  icon?: React.ReactNode;
  badge?: number | string;
  badgeVariant?: 'accent' | 'warning' | 'danger' | 'success' | 'muted';
  disabled?: boolean;
}

export interface SectionTabsProps {
  tabs: SectionTabItem[];
  activeTab?: string;
  onChange?: (id: string) => void;
  paramName?: string;
  className?: string;
  variant?: 'underline' | 'pills' | 'segmented';
  ariaLabel?: string;
}

function SectionTabsInner({
  tabs,
  activeTab,
  onChange,
  paramName = 'tab',
  className = '',
  variant = 'underline',
  ariaLabel = 'أقسام الصفحة',
}: SectionTabsProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const pathname = usePathname();
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);

  const paramValue = searchParams?.get(paramName);
  const currentTab = activeTab || paramValue || tabs[0]?.id;

  // Sync tab change
  const handleSelect = (id: string) => {
    if (onChange) {
      onChange(id);
    }
    if (searchParams && router) {
      const params = new URLSearchParams(searchParams.toString());
      if (params.get(paramName) !== id) {
        params.set(paramName, id);
        router.replace(`${pathname}?${params.toString()}`, { scroll: false });
      }
    }
  };

  // Keyboard navigation (ARIA Tabs pattern with RTL support)
  const handleKeyDown = (e: React.KeyboardEvent, index: number) => {
    const total = tabs.length;
    let nextIndex = index;

    if (e.key === 'ArrowRight') {
      // In RTL, ArrowRight moves to the previous visual tab (index - 1)
      nextIndex = (index - 1 + total) % total;
    } else if (e.key === 'ArrowLeft') {
      // In RTL, ArrowLeft moves to the next visual tab (index + 1)
      nextIndex = (index + 1) % total;
    } else if (e.key === 'Home') {
      nextIndex = 0;
    } else if (e.key === 'End') {
      nextIndex = total - 1;
    } else {
      return;
    }

    e.preventDefault();
    const nextTab = tabs[nextIndex];
    if (nextTab && !nextTab.disabled) {
      tabRefs.current[nextIndex]?.focus();
      handleSelect(nextTab.id);
    }
  };

  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={`flex items-center gap-1.5 overflow-x-auto no-scrollbar ${
        variant === 'underline'
          ? 'border-b border-[var(--line)] py-1'
          : variant === 'pills'
          ? 'p-1 rounded-xl bg-[var(--raised)] border border-[var(--line)]'
          : 'p-1 rounded-xl bg-[var(--surface)] border border-[var(--line)]'
      } ${className}`}
    >
      {tabs.map((tab, idx) => {
        const isActive = currentTab === tab.id;
        const isDisabled = tab.disabled;

        const badgeClass =
          tab.badgeVariant === 'danger'
            ? 'bg-[var(--danger)] text-white'
            : tab.badgeVariant === 'warning'
            ? 'bg-[var(--warning)] text-[var(--ink)]'
            : tab.badgeVariant === 'success'
            ? 'bg-[var(--money-pos)] text-white'
            : isActive
            ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
            : 'bg-[var(--raised)] text-[var(--muted)]';

        return (
          <button
            key={tab.id}
            id={`tab-${tab.id}`}
            ref={(el) => {
              tabRefs.current[idx] = el;
            }}
            role="tab"
            aria-selected={isActive}
            aria-controls={`tabpanel-${tab.id}`}
            tabIndex={isActive ? 0 : -1}
            disabled={isDisabled}
            onClick={() => handleSelect(tab.id)}
            onKeyDown={(e) => handleKeyDown(e, idx)}
            className={`inline-flex items-center gap-2 px-3.5 py-2 text-xs sm:text-sm font-semibold transition-all whitespace-nowrap cursor-pointer select-none ${
              variant === 'underline'
                ? `border-b-2 rounded-t-lg -mb-[5px] ${
                    isActive
                      ? 'border-[var(--accent)] text-[var(--accent)] bg-[var(--surface)]'
                      : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--line)]'
                  }`
                : `rounded-lg ${
                    isActive
                      ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs font-bold'
                      : 'text-[var(--muted)] hover:text-[var(--ink)]'
                  }`
            } ${isDisabled ? 'opacity-40 cursor-not-allowed' : ''}`}
          >
            {tab.icon && <span className="shrink-0">{tab.icon}</span>}
            <span>{tab.label}</span>
            {tab.badge !== undefined && (
              <span className={`num text-[10px] px-1.5 py-0.2 rounded-full font-bold ${badgeClass}`}>
                {tab.badge}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

export function SectionTabs(props: SectionTabsProps) {
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
      <SectionTabsInner {...props} />
    </Suspense>
  );
}
