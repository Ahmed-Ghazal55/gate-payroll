'use client';

import React, { useState, useEffect, useRef, Suspense } from 'react';
import Link from 'next/link';
import { usePathname, useSearchParams } from 'next/navigation';
import {
  Menu,
  Search,
  Bell,
  Sun,
  Moon,
  Building2,
  Calendar,
  LogOut,
  ChevronDown,
  Palette,
  Check,
  CheckCircle,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { UserProfile } from '../../lib/types';
import { NotificationsPanel } from './NotificationsPanel';
import { GlobalSearchModal } from './GlobalSearchModal';

function BreadcrumbsNav() {
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const parts = pathname.split('/').filter(Boolean);
  const crumbs = [{ label: 'الرئيسية', href: '/' }];

  if (parts.length > 0) {
    const section = parts[0];
    const sectionLabels: Record<string, string> = {
      riders: 'المناديب',
      companies: 'الشركات والسجلات',
      operations: 'التشغيل والأصول',
      payroll: 'الرواتب',
      debts: 'المديونيات والسلف',
      treasury: 'البنوك والصناديق والخزن',
      accounting: 'المحاسبة والتقارير',
      forms: 'النماذج والردود',
      notifications: 'الإشعارات والتنبيهات',
      admin: 'الإدارة',
    };
    if (sectionLabels[section]) {
      crumbs.push({ label: sectionLabels[section], href: `/${section}/` });
    }
  }

  const tab = searchParams?.get('tab');
  if (tab) {
    const tabLabels: Record<string, string> = {
      campaigns: 'الحملات والروابط',
      builder: 'مصمم النماذج',
      responses: 'مراجعة الردود',
      analytics: 'التحليلات',
    };
    if (tabLabels[tab]) {
      crumbs.push({ label: tabLabels[tab], href: '#' });
    }
  }

  return (
    <nav aria-label="مسار التنقل" className="hidden sm:flex items-center gap-1.5 text-xs text-[var(--muted)]">
      {crumbs.map((c, i) => (
        <React.Fragment key={i}>
          {i > 0 && <span className="text-[var(--line)]">/</span>}
          {i === crumbs.length - 1 ? (
            <span className="font-bold text-[var(--ink)]">{c.label}</span>
          ) : (
            <Link href={c.href} className="hover:text-[var(--ink)] transition">
              {c.label}
            </Link>
          )}
        </React.Fragment>
      ))}
    </nav>
  );
}

export interface HeaderProps {
  onToggleMobileNav: () => void;
  selectedCompany?: string;
  onCompanyChange?: (company: string) => void;
  selectedPeriod?: string;
  onPeriodChange?: (period: string) => void;
}

export function Header({
  onToggleMobileNav,
  selectedCompany = 'all',
  onCompanyChange,
  selectedPeriod = '2026-10',
  onPeriodChange,
}: HeaderProps) {
  // User state
  const [user, setUser] = useState<UserProfile | null>(null);
  const [userMenuOpen, setUserMenuOpen] = useState(false);

  // Theme state
  const [currentTheme, setCurrentTheme] = useState<'sand' | 'light' | 'dim'>('sand');
  const [themeMenuOpen, setThemeMenuOpen] = useState(false);

  // Notifications state
  const [notifOpen, setNotifOpen] = useState(false);
  const [unreadCount, setUnreadCount] = useState<number>(3);
  const [approvalCount, setApprovalCount] = useState<number>(2);

  // Search modal state
  const [searchModalOpen, setSearchModalOpen] = useState(false);

  const userMenuRef = useRef<HTMLDivElement>(null);
  const themeMenuRef = useRef<HTMLDivElement>(null);

  // Initialize theme from document / localStorage
  useEffect(() => {
    const saved = localStorage.getItem('gate_theme') as 'sand' | 'light' | 'dim' | null;
    const initial = saved || (document.documentElement.getAttribute('data-theme') as any) || 'sand';
    setCurrentTheme(initial);
    document.documentElement.setAttribute('data-theme', initial);
  }, []);

  // Fetch current user and notification counts
  useEffect(() => {
    let mounted = true;
    api
      .getMe()
      .then((u) => {
        if (mounted) setUser(u);
      })
      .catch((err) => {
        // If 401, redirect to legacy login
        if (err.status === 401 && typeof window !== 'undefined') {
          window.location.href = `/?returnUrl=${encodeURIComponent(window.location.pathname)}`;
        }
      });

    api
      .getNotificationsSummary()
      .then((summary) => {
        if (mounted && summary) {
          setUnreadCount(summary.unreadCount);
          setApprovalCount(summary.approvalCount);
        }
      })
      .catch(() => {});

    return () => {
      mounted = false;
    };
  }, []);

  // Listen for Ctrl+K / Cmd+K
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        setSearchModalOpen((prev) => !prev);
      }
    }
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Close menus on outside click
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
      if (themeMenuRef.current && !themeMenuRef.current.contains(e.target as Node)) {
        setThemeMenuOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const changeTheme = (theme: 'sand' | 'light' | 'dim') => {
    setCurrentTheme(theme);
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('gate_theme', theme);
    setThemeMenuOpen(false);
  };

  const handleLogout = () => {
    // Redirect to root login
    window.location.href = '/?logout=true';
  };

  return (
    <>
      <header className="sticky top-0 z-30 flex h-16 items-center justify-between gap-2 sm:gap-4 border-b border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] px-3 sm:px-6 shadow-2xs">
        {/* Left Section: Mobile toggle + Breadcrumb */}
        <div className="flex items-center gap-2 sm:gap-3 min-w-0">
          <button
            type="button"
            onClick={onToggleMobileNav}
            aria-label="فتح القائمة الجانبية"
            className="flex lg:hidden p-2 rounded-lg text-[var(--ink)] hover:bg-[var(--raised)] transition"
          >
            <Menu size={20} />
          </button>

          {/* Dynamic Breadcrumbs */}
          <Suspense fallback={<span className="text-xs text-[var(--muted)]">الرئيسية</span>}>
            <BreadcrumbsNav />
          </Suspense>
        </div>

        {/* Center / Global Search Trigger */}
        <div className="flex-1 max-w-xs sm:max-w-md mx-2">
          <button
            type="button"
            onClick={() => setSearchModalOpen(true)}
            className="flex items-center justify-between w-full px-3 py-1.5 text-xs rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--muted)] transition"
          >
            <div className="flex items-center gap-2 truncate">
              <Search size={15} />
              <span className="truncate">بحث شامل (مندوب، شركة، مديونية)...</span>
            </div>
            <kbd className="hidden sm:inline-block px-1.5 py-0.5 rounded text-[10px] font-mono border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)]">
              Ctrl+K
            </kbd>
          </button>
        </div>

        {/* Right Section: Company Scope, Period, Approvals, Bell, Theme, User */}
        <div className="flex items-center gap-1.5 sm:gap-2.5">
          {/* Company Scope Selector */}
          <div className="hidden md:flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-xs">
            <Building2 size={14} className="text-[var(--muted)] shrink-0" />
            <select
              value={selectedCompany}
              onChange={(e) => onCompanyChange && onCompanyChange(e.target.value)}
              aria-label="نطاق الشركة"
              className="bg-transparent font-semibold text-[var(--ink)] focus:outline-hidden cursor-pointer"
            >
              <option value="all">كل الشركات</option>
              <option value="BW">البوابة (BW)</option>
              <option value="EX">إكسبريس (EX)</option>
              <option value="TR">ترانس (TR)</option>
            </select>
          </div>

          {/* Period Picker */}
          <div className="hidden lg:flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-xs">
            <Calendar size={14} className="text-[var(--muted)] shrink-0" />
            <select
              value={selectedPeriod}
              onChange={(e) => onPeriodChange && onPeriodChange(e.target.value)}
              aria-label="فترة العمل"
              className="num bg-transparent font-semibold text-[var(--ink)] focus:outline-hidden cursor-pointer"
            >
              <option value="2026-10">2026-10</option>
              <option value="2026-09">2026-09</option>
              <option value="2026-08">2026-08</option>
            </select>
          </div>

          {/* Approvals Count Link */}
          {approvalCount > 0 && (
            <Link
              href="/debts/"
              title={`${approvalCount} معاملات بانتظار الاعتماد`}
              className="hidden sm:inline-flex items-center gap-1 px-2.5 py-1 text-xs font-semibold rounded-lg bg-[var(--warning)]/15 text-[var(--warning)] hover:bg-[var(--warning)]/25 transition"
            >
              <span className="h-1.5 w-1.5 rounded-full bg-[var(--warning)] animate-pulse" />
              <span>اعتمادات</span>
              <span className="num font-bold">({approvalCount})</span>
            </Link>
          )}

          {/* Notifications Bell */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setNotifOpen(!notifOpen)}
              aria-label="الإشعارات"
              aria-expanded={notifOpen}
              className="relative p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition"
            >
              <Bell size={18} />
              {unreadCount > 0 && (
                <span className="num absolute top-1 start-1 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-[var(--danger)] text-[9px] font-bold text-white px-1">
                  {unreadCount}
                </span>
              )}
            </button>

            <NotificationsPanel
              open={notifOpen}
              onClose={() => setNotifOpen(false)}
              onUnreadCountChange={setUnreadCount}
            />
          </div>

          {/* Theme Switcher Dropdown */}
          <div className="relative" ref={themeMenuRef}>
            <button
              type="button"
              onClick={() => setThemeMenuOpen(!themeMenuOpen)}
              aria-label="تبديل المظهر"
              aria-expanded={themeMenuOpen}
              className="p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition"
            >
              {currentTheme === 'dim' ? (
                <Moon size={18} className="text-[var(--accent)]" />
              ) : (
                <Sun size={18} className="text-[var(--accent)]" />
              )}
            </button>

            {themeMenuOpen && (
              <div className="absolute end-0 mt-2 w-40 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl py-1.5 z-40 text-xs font-medium">
                <div className="px-3 py-1 text-[11px] font-semibold text-[var(--muted)] border-b border-[var(--line)] mb-1">
                  اختر المظهر
                </div>
                <button
                  type="button"
                  onClick={() => changeTheme('sand')}
                  className="flex w-full items-center justify-between px-3 py-1.5 text-start hover:bg-[var(--row-hover)]"
                >
                  <div className="flex items-center gap-2">
                    <span data-theme="sand" className="h-3 w-3 rounded-full bg-[var(--bg)] border border-[var(--line)]" />
                    <span>رملي (افتراضي)</span>
                  </div>
                  {currentTheme === 'sand' && <Check size={14} className="text-[var(--accent)]" />}
                </button>
                <button
                  type="button"
                  onClick={() => changeTheme('light')}
                  className="flex w-full items-center justify-between px-3 py-1.5 text-start hover:bg-[var(--row-hover)]"
                >
                  <div className="flex items-center gap-2">
                    <span data-theme="light" className="h-3 w-3 rounded-full bg-[var(--bg)] border border-[var(--line)]" />
                    <span>فاتح</span>
                  </div>
                  {currentTheme === 'light' && <Check size={14} className="text-[var(--accent)]" />}
                </button>
                <button
                  type="button"
                  onClick={() => changeTheme('dim')}
                  className="flex w-full items-center justify-between px-3 py-1.5 text-start hover:bg-[var(--row-hover)]"
                >
                  <div className="flex items-center gap-2">
                    <span data-theme="dim" className="h-3 w-3 rounded-full bg-[var(--bg)] border border-[var(--line)]" />
                    <span>داكن هادئ</span>
                  </div>
                  {currentTheme === 'dim' && <Check size={14} className="text-[var(--accent)]" />}
                </button>
              </div>
            )}
          </div>

          {/* User Profile Menu */}
          <div className="relative" ref={userMenuRef}>
            <button
              type="button"
              onClick={() => setUserMenuOpen(!userMenuOpen)}
              aria-label="قائمة المستخدم"
              aria-expanded={userMenuOpen}
              className="flex items-center gap-2 p-1 sm:px-2 sm:py-1 rounded-xl hover:bg-[var(--raised)] transition"
            >
              <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs shrink-0">
                {user?.displayNameAr ? user.displayNameAr[0] : 'م'}
              </div>
              <div className="hidden xl:flex flex-col text-start truncate max-w-[120px]">
                <span className="text-xs font-bold leading-tight truncate">
                  {user?.displayNameAr || 'مدير النظام'}
                </span>
                <span className="text-[10px] text-[var(--muted)] leading-tight truncate">
                  {user?.role || 'مسؤول'}
                </span>
              </div>
              <ChevronDown size={14} className="hidden sm:block text-[var(--muted)]" />
            </button>

            {userMenuOpen && (
              <div className="absolute end-0 mt-2 w-52 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl py-1.5 z-40 text-xs font-medium">
                <div className="px-3.5 py-2 border-b border-[var(--line)] mb-1">
                  <div className="font-bold text-sm text-[var(--ink)]">
                    {user?.displayNameAr || 'مدير النظام'}
                  </div>
                  <div className="text-[11px] text-[var(--muted)]">
                    @{user?.username || 'admin'} · {user?.role || 'مسؤول'}
                  </div>
                </div>

                <Link
                  href="/admin/"
                  onClick={() => setUserMenuOpen(false)}
                  className="flex items-center gap-2.5 px-3.5 py-2 hover:bg-[var(--row-hover)] text-[var(--ink)]"
                >
                  <span>إعدادات النظام والأدوار</span>
                </Link>

                <div className="border-t border-[var(--line)] my-1" />

                <button
                  type="button"
                  onClick={handleLogout}
                  className="flex w-full items-center gap-2.5 px-3.5 py-2 text-start text-[var(--danger)] hover:bg-[var(--row-hover)]"
                >
                  <LogOut size={14} />
                  <span>تسجيل الخروج</span>
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      {/* Global Search Modal */}
      <GlobalSearchModal
        open={searchModalOpen}
        onClose={() => setSearchModalOpen(false)}
      />
    </>
  );
}
