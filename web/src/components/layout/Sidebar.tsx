'use client';

import React, { useState, useEffect, Suspense } from 'react';
import Link from 'next/link';
import { usePathname, useSearchParams } from 'next/navigation';
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
  Smartphone,
  ChevronDown,
  ChevronRight,
  ChevronLeft,
  X,
  FileUp,
} from 'lucide-react';
import { useAuth } from '../../lib/auth-context';

export interface NavSubItem {
  id: string;
  href: string;
  label: string;
  badge?: number | string;
  isNew?: boolean;
  permission?: string | string[];
}

export interface NavGroup {
  id: string;
  label: string;
  icon: React.ReactNode;
  defaultHref: string;
  permission?: string;
  badge?: number | string;
  isNew?: boolean;
  items: NavSubItem[];
}

export interface SidebarProps {
  collapsed: boolean;
  onToggleCollapse: () => void;
  mobileOpen: boolean;
  onCloseMobile: () => void;
  pendingApprovalsCount?: number;
  pendingResponsesCount?: number;
}

function SidebarInner({
  collapsed,
  onToggleCollapse,
  mobileOpen,
  onCloseMobile,
  pendingApprovalsCount = 0,
  pendingResponsesCount = 0,
}: SidebarProps) {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { can, isAdmin, user } = useAuth();

  // Definition of IA Groups matching Spec 09 lines 37-50
  const navGroups: NavGroup[] = [
    {
      id: 'dashboard',
      label: 'الرئيسية',
      icon: <LayoutDashboard size={18} />,
      defaultHref: '/',
      permission: 'payroll.view',
      items: [
        { id: 'dash-kpi', href: '/', label: 'لوحة المؤشرات والمهام' },
        { id: 'dash-import', href: '/imports/', label: 'الاستيراد التشغيلي', isNew: true },
      ],
    },
    {
      id: 'riders',
      label: 'المناديب',
      icon: <Users size={18} />,
      defaultHref: '/riders/',
      permission: 'riders.view',
      items: [
        { id: 'riders-list', href: '/riders/?tab=directory', label: 'دليل المناديب' },
        { id: 'riders-360', href: '/riders/?tab=360', label: 'ملف المندوب 360' },
        { id: 'riders-statement', href: '/riders/statement/', label: 'كشف حساب المندوب', isNew: true },
        { id: 'riders-quality', href: '/riders/?tab=quality', label: 'جودة البيانات' },
        { id: 'riders-sync', href: '/riders/?tab=sync', label: 'المزامنة' },
      ],
    },
    {
      id: 'companies',
      label: 'الشركات والسجلات',
      icon: <Building2 size={18} />,
      defaultHref: '/companies/',
      permission: 'companies.view',
      items: [
        { id: 'companies-sponsors', href: '/companies/?tab=companies', label: 'الشركات والكفلاء' },
        { id: 'companies-cr', href: '/companies/?tab=cr', label: 'السجلات التجارية' },
        { id: 'companies-docs', href: '/companies/?tab=expiring', label: 'الوثائق والانتهاءات' },
      ],
    },
    {
      id: 'operations',
      label: 'التشغيل والأصول',
      icon: <Truck size={18} />,
      defaultHref: '/operations/',
      permission: 'operations.view',
      items: [
        { id: 'ops-cities', href: '/operations/?tab=cities', label: 'المدن والمناطق' },
        { id: 'ops-housing', href: '/operations/?tab=housing', label: 'السكن والإسكان' },
        { id: 'ops-vehicles', href: '/operations/?tab=vehicles', label: 'المركبات والتسليمات' },
        { id: 'ops-custody', href: '/operations/?tab=custody', label: 'العهد العينية' },
        { id: 'ops-guarantees', href: '/operations/?tab=guarantees', label: 'الكفالات' },
        { id: 'ops-cases', href: '/operations/?tab=cases', label: 'القضايا والمنازعات' },
        { id: 'ops-accounts', href: '/operations/?tab=accounts', label: 'حسابات المنصات' },
      ],
    },
    {
      id: 'payroll',
      label: 'الرواتب',
      icon: <Calculator size={18} />,
      defaultHref: '/payroll/',
      permission: 'payroll.view',
      items: [
        { id: 'payroll-apps', href: '/payroll/?tab=platforms', label: 'فواتير وتسويات المنصات' },
        { id: 'payroll-cycle', href: '/payroll/?tab=cycle', label: 'دورة الرواتب والمسير' },
        { id: 'payroll-rules', href: '/payroll/?tab=rules', label: 'الأنظمة ومحرر القواعد' },
        { id: 'payroll-deductions', href: '/payroll/?tab=deductions', label: 'الاستقطاعات الشهرية' },
        { id: 'payroll-bank', href: '/payroll/?tab=bank', label: 'التحويل والبنوك' },
        { id: 'payroll-policies', href: '/payroll/?tab=policies', label: 'السياسات السابقة' },
      ],
    },
    {
      id: 'debts',
      label: 'المديونيات والسلف',
      icon: <Receipt size={18} />,
      defaultHref: '/debts/',
      badge: pendingApprovalsCount > 0 ? pendingApprovalsCount : undefined,
      permission: 'debts.view',
      items: [
        { id: 'debts-list', href: '/debts/?tab=debts', label: 'سجل المديونيات', permission: 'debts.view' },
        { id: 'debts-advance-requests', href: '/debts/?tab=advance_requests', label: 'طلبات السلف', permission: ['advances.request', 'advances.review'] },
        { id: 'debts-statement', href: '/debts/?tab=statement', label: 'كشف حساب المندوب', permission: 'debts.view' },
        { id: 'debts-approvals', href: '/debts/?tab=approvals', label: 'منظومة الاعتمادات', permission: 'debts.approve' },
        { id: 'debts-promissory', href: '/debts/?tab=promissory', label: 'أرشيف سندات الأمر', permission: 'debts.view' },
      ],
    },
    {
      id: 'treasury',
      label: 'البنوك والصناديق والخزن',
      icon: <Landmark size={18} />,
      defaultHref: '/treasury/',
      permission: 'treasury.view',
      items: [
        { id: 'treasury-vaults', href: '/treasury/?tab=vaults', label: 'أرصدة الخزينة (1110-1140)', permission: 'treasury.view' },
        { id: 'treasury-advance-payouts', href: '/treasury/?tab=advance_payouts', label: 'صرف السلف المعتمدة', permission: 'advances.payout' },
        { id: 'treasury-bankfile', href: '/treasury/?tab=bank_file', label: 'ملف صرف الرواتب البنكي', permission: 'treasury.view' },
        { id: 'treasury-movements', href: '/treasury/?tab=movements', label: 'سجل القيود والحركات', permission: 'treasury.view' },
      ],
    },
    {
      id: 'accounting',
      label: 'المحاسبة والتقارير',
      icon: <FileSpreadsheet size={18} />,
      defaultHref: '/accounting/',
      permission: 'payroll.view',
      items: [
        { id: 'acc-coa', href: '/accounting/?tab=coa', label: 'دليل الحسابات' },
        { id: 'acc-journals', href: '/accounting/?tab=journals', label: 'سجل القيود اليومية' },
        { id: 'acc-trial', href: '/accounting/?tab=trial_balance', label: 'ميزان المراجعة' },
        { id: 'acc-income', href: '/accounting/?tab=income_statement', label: 'قائمة الدخل' },
      ],
    },
    {
      id: 'forms',
      label: 'النماذج والردود',
      icon: <ClipboardList size={18} />,
      defaultHref: '/forms/',
      badge: pendingResponsesCount > 0 ? pendingResponsesCount : undefined,
      permission: 'forms.view',
      items: [
        { id: 'forms-campaigns', href: '/forms/?tab=campaigns', label: 'الحملات والروابط' },
        { id: 'forms-responses', href: '/forms/?tab=responses', label: 'مراجعة الردود' },
        { id: 'forms-builder', href: '/forms/?tab=builder', label: 'مصمم النماذج' },
      ],
    },
    {
      id: 'rider-app',
      label: 'تطبيق المناديب',
      icon: <Smartphone size={18} />,
      defaultHref: '/rider-app/',
      isNew: true,
      permission: 'operations.view',
      items: [
        { id: 'app-accounts', href: '/rider-app/?tab=accounts', label: 'الحسابات والأجهزة' },
        { id: 'app-broadcasts', href: '/rider-app/?tab=broadcasts', label: 'الإشعارات والإرسال' },
        { id: 'app-settings', href: '/rider-app/?tab=settings', label: 'إعدادات Firebase' },
        { id: 'app-statement', href: '/rider-app/?tab=statement-preview', label: 'معاينة كشف الحساب' },
        { id: 'app-requests', href: '/rider-app/?tab=requests', label: 'الطلبات والمواعيد' },
      ],
    },
    {
      id: 'admin',
      label: 'الإدارة',
      icon: <ShieldCheck size={18} />,
      defaultHref: '/admin/',
      permission: 'users.manage',
      items: [
        { id: 'admin-users', href: '/admin/?tab=users', label: 'المستخدمون والصلاحيات', permission: 'users.manage' },
        { id: 'admin-org', href: '/admin/?tab=org', label: 'الأقسام والرتب والتفويضات', permission: 'org.manage' },
        { id: 'admin-audit', href: '/admin/?tab=audit', label: 'سجل التدقيق والعمليات', permission: 'users.manage' },
        { id: 'admin-settings', href: '/admin/?tab=settings', label: 'إعدادات النظام', permission: 'users.manage' },
      ],
    },
  ];

  // Helper to determine which group matches the current route
  const getActiveGroupId = () => {
    if (pathname === '/') return 'dashboard';
    const clean = pathname.replace(/\/$/, '');
    const found = navGroups.find((g) => {
      const gPath = g.defaultHref.replace(/\/$/, '');
      if (gPath === '') return clean === '';
      return clean === gPath || clean.startsWith(gPath + '/');
    });
    return found ? found.id : 'dashboard';
  };

  const activeGroupId = getActiveGroupId();

  // State of open group (only one group open at a time: R6ui-2)
  const [openGroupId, setOpenGroupId] = useState<string | null>(activeGroupId);

  // Ensure active group stays open on route change
  useEffect(() => {
    setOpenGroupId(activeGroupId);
  }, [activeGroupId]);

  const toggleGroup = (groupId: string) => {
    setOpenGroupId((prev) => (prev === groupId ? null : groupId));
  };

  const isItemVisible = (item: NavSubItem) => {
    if (!user || isAdmin || !item.permission) return true;
    if (Array.isArray(item.permission)) {
      return item.permission.some((p) => can(p));
    }
    return can(item.permission);
  };

  const visibleGroups = navGroups
    .map((group) => ({
      ...group,
      items: group.items.filter(isItemVisible),
    }))
    .filter((group) => {
      if (!user) return true;
      if (isAdmin) return true;
      const hasGroupPerm = !group.permission || can(group.permission);
      return hasGroupPerm || group.items.length > 0;
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
          className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] lg:hidden cursor-pointer"
        >
          <X size={20} />
        </button>
      </div>

      {/* Navigation Groups List */}
      <nav className="flex-1 overflow-y-auto p-2.5 space-y-1.5 no-scrollbar">
        {!collapsed && (
          <div className="px-2.5 pt-1.5 pb-1 text-[11px] font-bold text-[var(--muted)] uppercase tracking-wider">
            منظومة البوابة 1.0
          </div>
        )}

        {visibleGroups.map((group) => {
          const isGroupActive = activeGroupId === group.id;
          const isOpen = openGroupId === group.id;

          return (
            <div key={group.id} className="space-y-1">
              {/* Group Header Button */}
              <div
                className={`flex items-center justify-between rounded-xl transition-all ${
                  isGroupActive
                    ? 'bg-[var(--raised)] text-[var(--ink)] font-bold'
                    : 'text-[var(--ink)] hover:bg-[var(--raised)]/70'
                } ${collapsed ? 'p-2 justify-center' : 'px-2.5 py-2'}`}
              >
                <Link
                  href={group.defaultHref}
                  onClick={onCloseMobile}
                  title={collapsed ? group.label : undefined}
                  className={`flex items-center gap-2.5 flex-1 min-w-0 ${
                    collapsed ? 'justify-center' : ''
                  }`}
                >
                  <span
                    className={`shrink-0 ${
                      isGroupActive ? 'text-[var(--accent)]' : 'text-[var(--muted)]'
                    }`}
                  >
                    {group.icon}
                  </span>
                  {!collapsed && (
                    <span className="text-xs sm:text-sm font-semibold truncate">
                      {group.label}
                    </span>
                  )}
                </Link>

                {!collapsed && (
                  <div className="flex items-center gap-1 shrink-0">
                    {group.badge !== undefined && (
                      <span className="num text-[10px] font-bold px-1.5 py-0.2 rounded-full bg-[var(--accent)] text-[var(--accent-ink)]">
                        {group.badge}
                      </span>
                    )}
                    <button
                      type="button"
                      onClick={() => toggleGroup(group.id)}
                      aria-label={isOpen ? 'طي المجموعة' : 'توسيع المجموعة'}
                      aria-expanded={isOpen}
                      className="p-1 rounded-md text-[var(--muted)] hover:text-[var(--ink)] transition cursor-pointer"
                    >
                      <ChevronDown
                        size={14}
                        className={`transition-transform duration-200 ${
                          isOpen ? 'rotate-180' : ''
                        }`}
                      />
                    </button>
                  </div>
                )}
              </div>

              {/* Sub-items list (collapsible) */}
              {!collapsed && isOpen && group.items.length > 0 && (
                <div className="ms-6 me-1 my-1 space-y-0.5 border-s-2 border-[var(--line)] ps-2.5 animate-in slide-in-from-top-1 duration-150">
                  {group.items.map((subItem) => {
                    const [subPath, subQuery] = subItem.href.split('?');
                    const cleanSubPath = subPath.replace(/\/$/, '') || '/';
                    const cleanCurrentPath = pathname.replace(/\/$/, '') || '/';
                    const pathMatch = cleanCurrentPath === cleanSubPath;

                    let isSubActive = false;
                    if (pathMatch) {
                      if (subQuery) {
                        const targetParam = new URLSearchParams(subQuery).get('tab');
                        const currentParam = searchParams?.get('tab');
                        isSubActive = currentParam === targetParam;
                      } else {
                        isSubActive = !searchParams?.get('tab');
                      }
                    }

                    return (
                      <Link
                        key={subItem.id}
                        href={subItem.href}
                        onClick={onCloseMobile}
                        className={`flex items-center justify-between py-1.5 px-2 rounded-lg text-xs transition-colors ${
                          isSubActive
                            ? 'bg-[var(--accent)] text-[var(--accent-ink)] font-bold shadow-2xs'
                            : 'text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]'
                        }`}
                      >
                        <span className="truncate">{subItem.label}</span>
                        {subItem.isNew && (
                          <span
                            className={`text-[9px] font-bold px-1 rounded-sm ${
                              isSubActive
                                ? 'bg-white/30 text-white'
                                : 'bg-[var(--accent)]/15 text-[var(--accent)]'
                            }`}
                          >
                            جديد
                          </span>
                        )}
                      </Link>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </nav>

      {/* Footer / Collapse Toggle */}
      <div className="p-3 border-t border-[var(--line)] bg-[var(--surface)] shrink-0">
        <button
          type="button"
          onClick={onToggleCollapse}
          aria-label={collapsed ? 'توسيع القائمة' : 'تصغير القائمة'}
          className="hidden lg:flex w-full items-center justify-center gap-2 py-1.5 rounded-lg text-xs font-semibold text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition cursor-pointer"
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

      {/* Mobile drawer overlay (drawer at 360px) */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-xs animate-in fade-in"
            onClick={onCloseMobile}
            aria-hidden="true"
          />
          <div className="relative w-80 max-w-[85vw] h-full shadow-2xl animate-in slide-in-from-right duration-200">
            {sidebarContent}
          </div>
        </div>
      )}
    </>
  );
}

export function Sidebar(props: SidebarProps) {
  return (
    <Suspense
      fallback={
        <aside
          className={`hidden lg:block shrink-0 sticky top-0 h-screen z-30 bg-[var(--surface)] border-e border-[var(--line)] ${
            props.collapsed ? 'w-18' : 'w-64'
          }`}
        />
      }
    >
      <SidebarInner {...props} />
    </Suspense>
  );
}
