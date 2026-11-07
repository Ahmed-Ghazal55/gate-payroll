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
  Zap,
  UserCheck,
  PlusCircle,
  Smartphone,
  FileSpreadsheet,
  ClipboardCheck,
  Banknote,
  FileUp,
  RefreshCw,
  Volume2,
  VolumeX,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import type { UserProfile } from '../../lib/types';
import { NotificationsPanel } from './NotificationsPanel';
import { GlobalSearchModal } from './GlobalSearchModal';
import { personalSoundEnabled, setPersonalSoundEnabled, unlockNotificationSound, playPersonalChime } from '../../lib/notification-sound';
import { ErpSyncDrawer } from '../system/ErpSyncDrawer';

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
  // Panel and menu open state (strictly one open at a time: R6ui-2)
  const [openPanel, setOpenPanel] = useState<'notif' | 'personal_notif' | 'theme' | 'user' | 'shortcuts' | null>(null);

  // Auth context for permissions and current user
  const { can, isAdmin, user } = useAuth();

  // Theme state
  const [currentTheme, setCurrentTheme] = useState<'sand' | 'light' | 'dim'>('sand');

  // Notifications state
  const [unreadCount, setUnreadCount] = useState<number>(0);
  const [personalUnreadCount, setPersonalUnreadCount] = useState<number>(0);
  const [approvalCount, setApprovalCount] = useState<number>(0);
  const [soundOn, setSoundOn] = useState<boolean>(true);
  // null until the first poll, so existing items never ring on page load.
  const lastPersonalRef = useRef<number | null>(null);

  // ERP Sync Drawer State
  const [erpSyncDrawerOpen, setErpSyncDrawerOpen] = useState(false);

  // Search modal state
  const [searchModalOpen, setSearchModalOpen] = useState(false);

  const notifMenuRef = useRef<HTMLDivElement>(null);
  const personalNotifMenuRef = useRef<HTMLDivElement>(null);
  const shortcutsMenuRef = useRef<HTMLDivElement>(null);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const themeMenuRef = useRef<HTMLDivElement>(null);

  // Initialize theme from document / localStorage
  useEffect(() => {
    const saved = localStorage.getItem('gate_theme') as 'sand' | 'light' | 'dim' | null;
    const initial = saved || (document.documentElement.getAttribute('data-theme') as any) || 'sand';
    setCurrentTheme(initial);
    document.documentElement.setAttribute('data-theme', initial);
  }, []);

  const fetchNotificationSummary = async () => {
    try {
      const summary = await api.getNotificationsSummary();
      if (summary) {
        // The general bell excludes personal items; they have their own icon (A1).
        setUnreadCount(summary.unreadCount - (summary.byCategory?.personal ?? 0));
        const personal = summary.byCategory?.personal ?? 0;
        if (lastPersonalRef.current !== null && personal > lastPersonalRef.current) playPersonalChime();
        lastPersonalRef.current = personal;
        setPersonalUnreadCount(personal);
        setApprovalCount(summary.approvalCount);
      }
    } catch {
      // silent initial poll
    }
  };

  // Poll notification counts every 60 s while the tab is visible; a new personal item rings.
  useEffect(() => {
    setSoundOn(personalSoundEnabled());
    fetchNotificationSummary();
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible') fetchNotificationSummary();
    }, 60_000);
    const onVisible = () => { if (document.visibilityState === 'visible') fetchNotificationSummary(); };
    const unlock = () => unlockNotificationSound();
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('pointerdown', unlock, { once: true });
    window.addEventListener('keydown', unlock, { once: true });
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('pointerdown', unlock);
      window.removeEventListener('keydown', unlock);
    };
  }, []);

  // Listen for Ctrl+K / Cmd+K
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        setSearchModalOpen((prev) => !prev);
      }
      if (e.key === 'Escape') {
        setOpenPanel(null);
      }
    }
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Close menus on outside click
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      const target = e.target as Node;
      if (openPanel === 'user' && userMenuRef.current && !userMenuRef.current.contains(target)) {
        setOpenPanel(null);
      }
      if (openPanel === 'theme' && themeMenuRef.current && !themeMenuRef.current.contains(target)) {
        setOpenPanel(null);
      }
      if (openPanel === 'notif' && notifMenuRef.current && !notifMenuRef.current.contains(target)) {
        setOpenPanel(null);
      }
      if (openPanel === 'personal_notif' && personalNotifMenuRef.current && !personalNotifMenuRef.current.contains(target)) {
        setOpenPanel(null);
      }
      if (openPanel === 'shortcuts' && shortcutsMenuRef.current && !shortcutsMenuRef.current.contains(target)) {
        setOpenPanel(null);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [openPanel]);

  const changeTheme = (theme: 'sand' | 'light' | 'dim') => {
    setCurrentTheme(theme);
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('gate_theme', theme);
    setOpenPanel(null);
  };

  const handleLogout = async () => {
    try {
      await api.logout();
    } catch (_) {}
    window.location.href = '/app/login/';
  };

  // Calculate general (non-personal) unread count
  const generalUnreadCount = Math.max(0, unreadCount - personalUnreadCount);

  // Check wildcard / prefix for advance payouts
  const canPayout =
    can('advances.payout') ||
    can('advances.payout:cash') ||
    can('advances.payout:bank') ||
    can('advances.payout:card') ||
    Boolean(user?.permissions && user.permissions.some((p) => p.startsWith('advances.payout')));

  // Shortcuts list gated by can()
  const shortcutItems = [
    {
      id: 'advance-request',
      label: 'طلب سلفة',
      href: '/debts/?tab=advance_requests&action=new',
      icon: <PlusCircle size={15} className="text-[var(--accent)]" />,
      visible: can('advances.request'),
    },
    {
      id: 'advance-request-net',
      label: 'طلب سلفة نت',
      href: '/debts/?tab=advance_requests&action=new_internet',
      icon: <Smartphone size={15} className="text-[var(--info)]" />,
      visible: can('advances.request'),
    },
    {
      id: 'manual-journal',
      label: 'إنشاء قيد محاسبي',
      href: '/accounting/?action=manual_journal',
      icon: <FileSpreadsheet size={15} className="text-[var(--warning)]" />,
      visible: can('finance.journal.create'),
    },
    {
      id: 'advance-review',
      label: 'مراجعة السلف',
      href: '/debts/?tab=advance_requests',
      icon: <ClipboardCheck size={15} className="text-[var(--money-pos)]" />,
      visible: can('advances.review'),
    },
    {
      id: 'advance-payout',
      label: 'صرف السلف',
      href: '/treasury/?tab=advance_payouts',
      icon: <Banknote size={15} className="text-[var(--accent)]" />,
      visible: canPayout,
    },
    {
      id: 'ninja-upload',
      label: 'رفع فاتورة نينجا',
      href: '/payroll/?tab=ninja_upload',
      icon: <FileUp size={15} className="text-[var(--info)]" />,
      visible: can('pay.view'),
    },
    {
      id: 'import-center',
      label: 'مركز الاستيراد',
      href: '/imports/',
      icon: <FileUp size={15} className="text-[var(--accent)]" />,
      visible: can('imports.view'),
    },
    {
      id: 'erp-sync',
      label: 'تحديث البيانات من ERP',
      onClick: () => {
        setOpenPanel(null);
        setErpSyncDrawerOpen(true);
      },
      icon: <RefreshCw size={15} className="text-[var(--accent)]" />,
      visible: can('erp.sync.run'),
    },
  ];

  const visibleShortcuts = shortcutItems.filter((s) => s.visible);

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
        <div className="flex-1 min-w-0 max-w-xs sm:max-w-md mx-1 sm:mx-2">
          <button
            type="button"
            onClick={() => setSearchModalOpen(true)}
            className="flex items-center justify-between w-full px-3 py-1.5 text-xs rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--muted)] transition"
          >
            <div className="flex items-center gap-2 truncate">
              <Search size={15} />
              <span className="truncate hidden sm:inline">بحث شامل (مندوب، شركة، مديونية)...</span>
            </div>
            <kbd className="hidden sm:inline-block px-1.5 py-0.5 rounded text-[10px] font-mono border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)]">
              Ctrl+K
            </kbd>
          </button>
        </div>

        {/* Right Section: Company Scope, Period, Approvals, Bell, Theme, User */}
        <div className="flex shrink-0 items-center gap-1.5 sm:gap-2.5">
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

          {/* Shortcuts Menu Button */}
          {visibleShortcuts.length > 0 && (
            <div className="relative" ref={shortcutsMenuRef}>
              <button
                type="button"
                onClick={() => setOpenPanel((prev) => (prev === 'shortcuts' ? null : 'shortcuts'))}
                aria-label="قائمة الاختصارات السريعة"
                aria-expanded={openPanel === 'shortcuts'}
                className={`p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition ${
                  openPanel === 'shortcuts' ? 'bg-[var(--raised)] text-[var(--accent)]' : ''
                }`}
              >
                <Zap size={18} />
              </button>

              {openPanel === 'shortcuts' && (
                <div className="absolute end-0 mt-2 w-56 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-2xl py-2 z-40 text-xs font-medium animate-in fade-in zoom-in-95 duration-150">
                  <div className="px-3.5 py-1 text-[11px] font-bold text-[var(--muted)] border-b border-[var(--line)] mb-1 flex items-center justify-between">
                    <span>اختصارات سريعة</span>
                    <kbd className="text-[9px] font-mono bg-[var(--raised)] px-1.5 py-0.5 rounded text-[var(--muted)]">سريع</kbd>
                  </div>
                  <div className="py-1">
                    {visibleShortcuts.map((item) =>
                      item.href ? (
                        <Link
                          key={item.id}
                          href={item.href}
                          onClick={() => setOpenPanel(null)}
                          className="flex items-center gap-2.5 px-3.5 py-2 hover:bg-[var(--row-hover)] transition text-[var(--ink)] font-semibold"
                        >
                          {item.icon}
                          <span>{item.label}</span>
                        </Link>
                      ) : (
                        <button
                          key={item.id}
                          type="button"
                          onClick={item.onClick}
                          className="flex w-full items-center gap-2.5 px-3.5 py-2 hover:bg-[var(--row-hover)] transition text-start text-[var(--ink)] font-semibold"
                        >
                          {item.icon}
                          <span>{item.label}</span>
                        </button>
                      )
                    )}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Personal Notifications Button */}
          <div className="relative" ref={personalNotifMenuRef}>
            <button
              type="button"
              onClick={() => setOpenPanel((prev) => (prev === 'personal_notif' ? null : 'personal_notif'))}
              aria-label="إشعاراتي الشخصية"
              aria-expanded={openPanel === 'personal_notif'}
              className={`relative p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition ${
                openPanel === 'personal_notif' ? 'bg-[var(--raised)] text-[var(--accent)]' : ''
              }`}
            >
              <UserCheck size={18} />
              {personalUnreadCount > 0 && (
                <span className="num absolute top-1 start-1 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-[var(--accent)] text-[9px] font-bold text-[var(--accent-ink)] px-1">
                  {personalUnreadCount}
                </span>
              )}
            </button>

            <NotificationsPanel
              open={openPanel === 'personal_notif'}
              onClose={() => setOpenPanel(null)}
              filterCategory="personal"
              onUnreadCountChange={() => fetchNotificationSummary()}
            />
          </div>

          {/* General Notifications Bell */}
          <div className="relative" ref={notifMenuRef}>
            <button
              type="button"
              onClick={() => setOpenPanel((prev) => (prev === 'notif' ? null : 'notif'))}
              aria-label="الإشعارات العامة"
              aria-expanded={openPanel === 'notif'}
              className={`relative p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition ${
                openPanel === 'notif' ? 'bg-[var(--raised)] text-[var(--accent)]' : ''
              }`}
            >
              <Bell size={18} />
              {generalUnreadCount > 0 && (
                <span className="num absolute top-1 start-1 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-[var(--danger)] text-[9px] font-bold text-white px-1">
                  {generalUnreadCount}
                </span>
              )}
            </button>

            <NotificationsPanel
              open={openPanel === 'notif'}
              onClose={() => setOpenPanel(null)}
              filterCategory="non-personal"
              onUnreadCountChange={() => fetchNotificationSummary()}
            />
          </div>

          {/* Theme Switcher Dropdown */}
          <div className="relative" ref={themeMenuRef}>
            <button
              type="button"
              onClick={() => setOpenPanel((prev) => (prev === 'theme' ? null : 'theme'))}
              aria-label="تبديل المظهر"
              aria-expanded={openPanel === 'theme'}
              className="p-2 rounded-xl text-[var(--ink)] hover:bg-[var(--raised)] transition"
            >
              {currentTheme === 'dim' ? (
                <Moon size={18} className="text-[var(--accent)]" />
              ) : (
                <Sun size={18} className="text-[var(--accent)]" />
              )}
            </button>

            {openPanel === 'theme' && (
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
              onClick={() => setOpenPanel((prev) => (prev === 'user' ? null : 'user'))}
              aria-label="قائمة المستخدم"
              aria-expanded={openPanel === 'user'}
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

            {openPanel === 'user' && (
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
                  onClick={() => setOpenPanel(null)}
                  className="flex items-center gap-2.5 px-3.5 py-2 hover:bg-[var(--row-hover)] text-[var(--ink)]"
                >
                  <span>إعدادات النظام والأدوار</span>
                </Link>

                <button
                  type="button"
                  data-testid="personal-sound-toggle"
                  aria-pressed={soundOn}
                  onClick={() => {
                    const next = !soundOn;
                    setSoundOn(next);
                    setPersonalSoundEnabled(next);
                    if (next) playPersonalChime();
                  }}
                  className="flex w-full items-center gap-2.5 px-3.5 py-2 text-start text-[var(--ink)] hover:bg-[var(--row-hover)]"
                >
                  {soundOn ? <Volume2 size={14} /> : <VolumeX size={14} />}
                  <span>صوت الإشعارات الشخصية: {soundOn ? 'مفعّل' : 'مغلق'}</span>
                </button>

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

      {/* ERP Sync Drawer */}
      <ErpSyncDrawer
        open={erpSyncDrawerOpen}
        onClose={() => setErpSyncDrawerOpen(false)}
      />
    </>
  );
}
