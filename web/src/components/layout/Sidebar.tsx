'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import {
  LayoutDashboard,
  Users,
  Building2,
  Truck,
  Calculator,
  Receipt,
  Landmark,
  FileSpreadsheet,
  ClipboardList,
  ShieldCheck,
  ChevronRight,
  ChevronLeft,
  ExternalLink,
  Sparkles,
  X,
} from 'lucide-react';
import { useAuth } from '../../lib/auth-context';

export interface NavItem {
  id: string;
  href: string;
  label: string;
  icon: React.ReactNode;
  badge?: number | string;
  isNew?: boolean;
  permission?: string;
}

export interface SidebarProps {
  collapsed: boolean;
  onToggleCollapse: () => void;
  mobileOpen: boolean;
  onCloseMobile: () => void;
  pendingApprovalsCount?: number;
  pendingResponsesCount?: number;
}

export function Sidebar({
  collapsed,
  onToggleCollapse,
  mobileOpen,
  onCloseMobile,
  pendingApprovalsCount = 0,
  pendingResponsesCount = 0,
}: SidebarProps) {
  const pathname = usePathname();
  const { can, isAdmin, user } = useAuth();

  const navItems: NavItem[] = [
    {
      id: 'dashboard',
      href: '/',
      label: 'الرئيسية',
      icon: <LayoutDashboard size={19} />,
      permission: 'payroll.view',
    },
    {
      id: 'riders',
      href: '/riders/',
      label: 'المناديب',
      icon: <Users size={19} />,
      permission: 'riders.view',
    },
    {
      id: 'companies',
      href: '/companies/',
      label: 'الشركات والسجلات',
      icon: <Building2 size={19} />,
      isNew: true,
      permission: 'companies.view',
    },
    {
      id: 'operations',
      href: '/operations/',
      label: 'التشغيل والأصول',
      icon: <Truck size={19} />,
      isNew: true,
      permission: 'operations.view',
    },
    {
      id: 'payroll',
      href: '/payroll/',
      label: 'الرواتب',
      icon: <Calculator size={19} />,
      permission: 'payroll.view',
    },
    {
      id: 'debts',
      href: '/debts/',
      label: 'المديونيات والسلف',
      icon: <Receipt size={19} />,
      badge: pendingApprovalsCount > 0 ? pendingApprovalsCount : undefined,
      permission: 'debts.view',
    },
    {
      id: 'treasury',
      href: '/treasury/',
      label: 'البنوك والصناديق والخزن',
      icon: <Landmark size={19} />,
      permission: 'treasury.view',
    },
    {
      id: 'accounting',
      href: '/accounting/',
      label: 'المحاسبة والتقارير',
      icon: <FileSpreadsheet size={19} />,
      permission: 'payroll.view',
    },
    {
      id: 'forms',
      href: '/forms/',
      label: 'النماذج والردود',
      icon: <ClipboardList size={19} />,
      badge: pendingResponsesCount > 0 ? pendingResponsesCount : undefined,
      permission: 'forms.view',
    },
    {
      id: 'admin',
      href: '/admin/',
      label: 'الإدارة',
      icon: <ShieldCheck size={19} />,
      permission: 'users.manage',
    },
  ];

  const visibleNavItems = navItems.filter((item) => {
    if (!user) return true; // optimistic while auth loading
    if (isAdmin) return true;
    if (!item.permission) return true;
    return can(item.permission);
  });

  const sidebarContent = (
    <div className="flex flex-col h-full bg-[var(--surface)] text-[var(--ink)] border-e border-[var(--line)]">
      {/* Brand Header */}
      <div className="flex items-center justify-between p-4 border-b border-[var(--line)] min-h-[64px]">
        <Link href="/" className="flex items-center gap-3 overflow-hidden">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xl shadow-xs shrink-0">
            ب
          </div>
          {!collapsed && (
            <div className="flex flex-col truncate">
              <span className="font-black text-base tracking-tight text-[var(--ink)]">
                البوابة
              </span>
              <span className="text-[11px] text-[var(--muted)] leading-tight">
                الرواتب · الحسابات · النماذج
              </span>
            </div>
          )}
        </Link>

        {/* Mobile close button */}
        <button
          type="button"
          onClick={onCloseMobile}
          aria-label="إغلاق القائمة"
          className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] lg:hidden"
        >
          <X size={20} />
        </button>
      </div>

      {/* Navigation items list */}
      <nav className="flex-1 overflow-y-auto p-3 space-y-1.5 no-scrollbar">
        {!collapsed && (
          <div className="px-3 pt-2 pb-1 text-[11px] font-bold text-[var(--muted)] uppercase tracking-wider">
            مساحة الإدارة 1.0
          </div>
        )}

        {visibleNavItems.map((item) => {
          const isActive =
            item.href === '/'
              ? pathname === '/'
              : pathname.startsWith(item.href.replace(/\/$/, ''));

          return (
            <Link
              key={item.id}
              href={item.href}
              onClick={onCloseMobile}
              title={collapsed ? item.label : undefined}
              className={`flex items-center gap-3 px-3 py-2.5 rounded-xl font-medium text-xs sm:text-sm transition-colors group relative ${
                isActive
                  ? 'bg-[var(--accent)] text-[var(--accent-ink)] font-semibold shadow-xs'
                  : 'text-[var(--ink)] hover:bg-[var(--raised)]'
              } ${collapsed ? 'justify-center px-2' : ''}`}
            >
              <span className={`shrink-0 ${isActive ? 'text-[var(--accent-ink)]' : 'text-[var(--accent)]'}`}>
                {item.icon}
              </span>

              {!collapsed && (
                <div className="flex flex-1 items-center justify-between gap-1 overflow-hidden">
                  <span className="truncate">{item.label}</span>
                  <div className="flex items-center gap-1.5 shrink-0">
                    {item.isNew && (
                      <span className={`text-[10px] font-bold px-1.5 py-0.2 rounded-full ${
                        isActive ? 'bg-white/25 text-white' : 'bg-[var(--raised)] text-[var(--muted)]'
                      }`}>
                        جديد
                      </span>
                    )}
                    {item.badge !== undefined && (
                      <span className={`num text-[11px] font-bold px-1.5 py-0.2 rounded-full ${
                        isActive
                          ? 'bg-white text-[var(--accent)]'
                          : 'bg-[var(--accent)] text-[var(--accent-ink)]'
                      }`}>
                        {item.badge}
                      </span>
                    )}
                  </div>
                </div>
              )}
            </Link>
          );
        })}
      </nav>

      {/* Footer / Switch back to old UI */}
      <div className="p-3 border-t border-[var(--line)] bg-[var(--surface)] shrink-0 space-y-2">
        <a
          href="/#/dashboard"
          title={collapsed ? 'الواجهة السابقة' : undefined}
          className={`flex items-center gap-2 px-3 py-2 rounded-xl text-xs font-semibold text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] border border-[var(--line)] transition ${
            collapsed ? 'justify-center px-2' : ''
          }`}
        >
          <ExternalLink size={15} className="shrink-0" />
          {!collapsed && <span>الواجهة السابقة</span>}
        </a>

        {/* Collapse toggle button for desktop */}
        <button
          type="button"
          onClick={onToggleCollapse}
          aria-label={collapsed ? 'توسيع القائمة' : 'تصغير القائمة'}
          className="hidden lg:flex w-full items-center justify-center gap-2 py-1.5 rounded-lg text-xs font-medium text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
        >
          {collapsed ? <ChevronLeft size={16} /> : <ChevronRight size={16} />}
          {!collapsed && <span>تصغير القائمة</span>}
        </button>
      </div>
    </div>
  );

  return (
    <>
      {/* Desktop sidebar */}
      <aside
        className={`hidden lg:block shrink-0 transition-all duration-200 sticky top-0 h-screen z-30 ${
          collapsed ? 'w-18' : 'w-64'
        }`}
      >
        {sidebarContent}
      </aside>

      {/* Mobile drawer overlay */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-xs animate-in fade-in"
            onClick={onCloseMobile}
            aria-hidden="true"
          />
          <div className="relative w-72 max-w-[85vw] h-full shadow-2xl animate-in slide-in-from-right duration-200">
            {sidebarContent}
          </div>
        </div>
      )}
    </>
  );
}
