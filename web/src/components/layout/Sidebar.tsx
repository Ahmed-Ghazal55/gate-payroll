'use client';

import React, { useState, useEffect, Suspense } from 'react';
import Link from 'next/link';
import { usePathname, useSearchParams } from 'next/navigation';
import {
  LayoutDashboard,
  Wallet,
  Truck,
  Calculator,
  Receipt,
  ClipboardCheck,
  Landmark,
  FileSpreadsheet,
  ShieldCheck,
  Smartphone,
  ChevronDown,
  ChevronRight,
  ChevronLeft,
  X,
} from 'lucide-react';
import { useAuth } from '../../lib/auth-context';
import { ApprovalExceptionChip } from '../../features/admin/ApprovalExceptionChip';
import { appendPeriodToHref, readUrlPeriod } from '../../lib/period';

export interface NavSubItem {
  id: string;
  href: string;
  label: string;
  badge?: number | string;
  isNew?: boolean;
  permission?: string | string[];
  disabled?: boolean;
  laterBadge?: boolean;
  /** When true, every listed permission is required (default: any one). */
  requireAll?: boolean;
}

export interface NavGroup {
  id: string;
  label: string;
  icon: React.ReactNode;
  defaultHref: string;
  permission?: string | string[];
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

/** U-W3-01 — Home sidebar tree (H1/H2/O1). No import/sync entries. */
export function buildNavGroups(
  pendingApprovalsCount = 0,
  pendingResponsesCount = 0
): NavGroup[] {
  return [
    {
      id: 'home',
      label: 'الرئيسية',
      icon: <LayoutDashboard size={18} />,
      defaultHref: '/',
      permission: 'payroll.view',
      items: [
        { id: 'dash-kpi', href: '/', label: 'لوحة المؤشرات والمهام' },
        {
          id: 'workforce',
          href: '/workforce/',
          label: 'القوى العاملة',
          permission: ['people.workforce.view', 'riders.view'],
        },
        {
          id: 'companies',
          href: '/companies/',
          label: 'الشركات والسجلات',
          permission: 'companies.view',
        },
        {
          id: 'hr-catalogs',
          href: '/hr/catalogs/',
          label: 'كتالوجات الموظفين',
          permission: 'hr.catalogs.view',
        },
        {
          id: 'rider-statement',
          href: '/riders/statement/',
          label: 'كشف حساب المندوب',
          permission: ['pay.view', 'payroll.view', 'debts.view'],
        },
      ],
    },
    {
      id: 'payments',
      label: 'المدفوعات والمشتريات',
      icon: <Wallet size={18} />,
      defaultHref: '/payments/rents',
      permission: 'payments.view',
      items: [
        { id: 'pay-rents', href: '/payments/rents', label: 'الإيجارات والسكنات' },
        { id: 'pay-installments', href: '/payments/installments', label: 'الأقساط والمدفوعات البنكية' },
        { id: 'pay-purchases', href: '/payments/purchases', label: 'فواتير المشتريات والمصروفات' },
        { id: 'pay-fuel', href: '/payments/fuel', label: 'الوقود والمحطات' },
        { id: 'pay-hr', href: '/payments/hr-requests', label: 'طلبات الموارد البشرية والخصومات' },
        { id: 'pay-advances', href: '/payments/advances', label: 'السلف والشحن' },
        { id: 'pay-mada', href: '/payments/mada', label: 'جهاز مدا والشحن' },
        { id: 'pay-hs', href: '/payments/hs-custody', label: 'عهد هنقرستيشن' },
        { id: 'pay-traffic', href: '/payments/traffic-violations', label: 'سداد المخالفات المرورية' },
        { id: 'pay-suppliers', href: '/payments/suppliers', label: 'الموردون والدفعات' },
        { id: 'pay-due', href: '/payments/due-calendar', label: 'لوحة المستحقات' },
        { id: 'pay-gov', href: '/payments/government', label: 'المدفوعات الحكومية' },
        { id: 'pay-prepaid', href: '/payments/prepaid-wallets', label: 'المحافظ المدفوعة مقدمًا' },
        { id: 'pay-quick', href: '/payments/quick', label: 'تسجيل مدفوعات سريعة' },
      ],
    },
    {
      id: 'operations',
      label: 'التشغيل والأسطول',
      icon: <Truck size={18} />,
      defaultHref: '/operations/?tab=cities',
      permission: 'operations.view',
      items: [
        { id: 'ops-cities', href: '/operations/?tab=cities', label: 'المدن والمناطق' },
        { id: 'ops-housing', href: '/operations/?tab=housing', label: 'السكن والإسكان' },
        { id: 'ops-vehicles', href: '/operations/?tab=vehicles', label: 'المركبات والتسليمات' },
        { id: 'ops-fuel', href: '/operations/?tab=fuel', label: 'الوقود' },
      ],
    },
    {
      id: 'payroll',
      label: 'الرواتب',
      icon: <Calculator size={18} />,
      defaultHref: '/payroll/keeta',
      permission: ['payroll.view', 'pay.view'],
      items: [
        { id: 'pr-keeta', href: '/payroll/keeta', label: 'كيتا' },
        { id: 'pr-hs', href: '/payroll/hungerstation', label: 'هنقرستيشن' },
        { id: 'pr-ninja', href: '/payroll/ninja', label: 'نينجا' },
        { id: 'pr-amazon', href: '/payroll/amazon', label: 'أمازون', laterBadge: true, disabled: true },
        { id: 'pr-chefz', href: '/payroll/chefz', label: 'شيفز' },
        { id: 'pr-jahez', href: '/payroll/jahez', label: 'جاهز' },
        { id: 'pr-mudad', href: '/payroll/mudad', label: 'رواتب مدد' },
        { id: 'pr-bank', href: '/payroll/bank-runs', label: 'مسيرات الرواتب والبنك' },
        { id: 'pr-holds', href: '/payroll/holds', label: 'طلبات إيقاف والخصم' },
        { id: 'pr-settings', href: '/payroll/settings', label: 'إعدادات الرواتب' },
      ],
    },
    {
      id: 'debts',
      label: 'المديونيات والتحصيل',
      icon: <Receipt size={18} />,
      defaultHref: '/debts/',
      permission: 'debts.view',
      items: [
        { id: 'debts-list', href: '/debts/', label: 'سجل المديونيات', permission: 'debts.view' },
        {
          id: 'debts-promissory',
          href: '/debts/promissory',
          label: 'أرشيف سندات الأمر',
          permission: 'debts.promissory.view',
        },
      ],
    },
    {
      id: 'approvals',
      label: 'الاعتمادات',
      icon: <ClipboardCheck size={18} />,
      defaultHref: '/approvals/',
      permission: 'approvals.view',
      badge: pendingApprovalsCount > 0 ? pendingApprovalsCount : undefined,
      items: [{ id: 'approvals-main', href: '/approvals/', label: 'صندوق الاعتمادات' }],
    },
    {
      id: 'treasury',
      label: 'البنوك والصناديق والخزن',
      icon: <Landmark size={18} />,
      defaultHref: '/treasury/?tab=balances',
      permission: 'treasury.view',
      items: [
        { id: 'tr-balances', href: '/treasury/?tab=balances', label: 'الأرصدة' },
        { id: 'tr-transfers', href: '/treasury/?tab=transfers', label: 'التحويلات' },
        { id: 'tr-recon', href: '/treasury/?tab=bank_recon', label: 'مطابقة البنوك' },
        { id: 'tr-vault', href: '/treasury/?tab=vault_counts', label: 'جرد الخزن' },
        { id: 'tr-jahez', href: '/treasury/?tab=jahez', label: 'جاهز (مطابقة وخزينة)' },
        { id: 'tr-movements', href: '/treasury/?tab=movements', label: 'سجل الحركات' },
      ],
    },
    {
      id: 'accounting',
      label: 'المحاسبة والتقارير',
      icon: <FileSpreadsheet size={18} />,
      defaultHref: '/accounting/?tab=coa',
      permission: 'accounting.chart.view',
      items: [
        { id: 'acc-coa', href: '/accounting/?tab=coa', label: 'دليل الحسابات' },
        { id: 'acc-manual', href: '/accounting/?tab=manual_journals', label: 'القيود اليدوية' },
        { id: 'acc-journals', href: '/accounting/?tab=journals', label: 'سجل القيود اليومية' },
        { id: 'acc-gl', href: '/accounting/?tab=general_ledger', label: 'دفتر الأستاذ / كشف حساب' },
        { id: 'acc-tb', href: '/accounting/?tab=trial_balance', label: 'ميزان المراجعة' },
        { id: 'acc-pl', href: '/accounting/?tab=income_statement', label: 'قائمة الدخل' },
        { id: 'acc-bs', href: '/accounting/?tab=balance_sheet', label: 'الميزانية العمومية' },
        { id: 'acc-cc', href: '/accounting/?tab=cost_centres', label: 'مراكز التكلفة والمدن' },
        { id: 'acc-app', href: '/accounting/?tab=app_profitability', label: 'ربحية التطبيقات' },
        { id: 'acc-reports', href: '/accounting/?tab=reports', label: 'التقارير' },
      ],
    },
    {
      id: 'rider_app',
      label: 'تطبيق المناديب والنماذج',
      icon: <Smartphone size={18} />,
      defaultHref: '/rider-app/?tab=campaigns',
      permission: ['forms.view', 'mobile.sessions'],
      badge: pendingResponsesCount > 0 ? pendingResponsesCount : undefined,
      items: [
        { id: 'ra-campaigns', href: '/rider-app/?tab=campaigns', label: 'الحملات والروابط' },
        { id: 'ra-responses', href: '/rider-app/?tab=responses', label: 'مراجعة الردود' },
        { id: 'ra-builder', href: '/rider-app/?tab=builder', label: 'مصمم النماذج' },
        { id: 'ra-accounts', href: '/rider-app/?tab=accounts', label: 'الحسابات والأجهزة' },
        { id: 'ra-broadcasts', href: '/rider-app/?tab=broadcasts', label: 'الإشعارات والإرسال' },
        { id: 'ra-statements', href: '/rider-app/?tab=statements', label: 'إرسال كشوف الحساب' },
        { id: 'ra-requests', href: '/rider-app/?tab=requests', label: 'الطلبات والمواعيد' },
        { id: 'ra-settings', href: '/rider-app/?tab=settings', label: 'إعدادات التطبيق' },
      ],
    },
    {
      id: 'admin',
      label: 'الإدارة',
      icon: <ShieldCheck size={18} />,
      defaultHref: '/admin/?tab=users',
      permission: 'users.manage',
      items: [
        { id: 'ad-users', href: '/admin/?tab=users', label: 'المستخدمون والصلاحيات', permission: 'users.manage' },
        { id: 'ad-org', href: '/admin/?tab=org', label: 'الأقسام والرتب والتفويضات', permission: 'org.manage' },
        { id: 'ad-bypass', href: '/admin/?tab=bypass', label: 'تجاوز الاعتماد', permission: 'approval.bypass_maker_checker' },
        { id: 'ad-lists', href: '/admin/?tab=lists', label: 'القوائم العامة', permission: 'admin.lists.manage' },
        { id: 'ad-audit', href: '/admin/?tab=audit', label: 'سجل التدقيق', permission: 'admin.audit.view' },
        { id: 'ad-read', href: '/admin/?tab=read_summary', label: 'ملخص القراءة اليومي', permission: 'admin.read_summary.view' },
        { id: 'ad-settings', href: '/admin/?tab=settings', label: 'إعدادات النظام', permission: 'admin.settings.manage' },
        { id: 'ad-notify', href: '/admin/?tab=notify_settings', label: 'إعدادات الإشعارات', permission: 'users.manage' },
        { id: 'ad-archive', href: '/admin/?tab=archive', label: 'الأرشفة', permission: 'admin.archive.manage' },
        { id: 'ad-chat', href: '/admin/?tab=staff_chat', label: 'محادثة الموظفين', permission: 'chat.view' },
      ],
    },
  ];
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
  const urlPeriod = readUrlPeriod(searchParams);
  const withPeriod = (href: string) => appendPeriodToHref(href, urlPeriod);
  const { can, isAdmin, user } = useAuth();

  const navGroups = buildNavGroups(pendingApprovalsCount, pendingResponsesCount);

  const getActiveGroupId = () => {
    if (pathname === '/' || pathname === '') return 'home';
    const clean = pathname.replace(/\/$/, '');
    if (clean.startsWith('/workforce') || clean.startsWith('/riders') || clean.startsWith('/companies') || clean.startsWith('/hr')) {
      return 'home';
    }
    const found = navGroups.find((g) => {
      if (g.id === 'home') return false;
      const gPath = g.defaultHref.split('?')[0].replace(/\/$/, '');
      if (gPath === '') return clean === '';
      return clean === gPath || clean.startsWith(gPath + '/') || clean.startsWith(gPath);
    });
    return found ? found.id : 'home';
  };

  const activeGroupId = getActiveGroupId();
  const [openGroupId, setOpenGroupId] = useState<string | null>(activeGroupId);

  useEffect(() => {
    setOpenGroupId(activeGroupId);
  }, [activeGroupId]);

  const toggleGroup = (groupId: string) => {
    setOpenGroupId((prev) => (prev === groupId ? null : groupId));
  };

  const matchesPermission = (permission?: string | string[], requireAll = false) => {
    if (!permission) return true;
    if (Array.isArray(permission)) {
      return requireAll ? permission.every((p) => can(p)) : permission.some((p) => can(p));
    }
    return can(permission);
  };

  const isItemVisible = (item: NavSubItem, group: NavGroup) => {
    if (!user || isAdmin) return true;
    return matchesPermission(item.permission ?? group.permission, item.requireAll);
  };

  const visibleGroups = navGroups
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => isItemVisible(item, group)),
    }))
    .filter((group) => {
      if (!user) return true;
      if (isAdmin) return true;
      const hasGroupPerm =
        !group.permission ||
        (Array.isArray(group.permission)
          ? group.permission.some((p) => can(p))
          : can(group.permission));
      return hasGroupPerm || group.items.length > 0;
    });

  const emptyPerms = Boolean(user) && !isAdmin && visibleGroups.length === 0;

  const sidebarContent = (
    <div className="flex flex-col h-full bg-[var(--surface)] text-[var(--ink)] border-e border-[var(--line)]">
      <div className="flex items-center justify-between p-4 border-b border-[var(--line)] min-h-[64px]">
        <Link href={withPeriod('/')} className="flex items-center gap-3 overflow-hidden">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xl shadow-xs shrink-0">
            ب
          </div>
          {!collapsed && (
            <div className="flex flex-col truncate">
              <span className="font-black text-base tracking-tight text-[var(--ink)]">البوابة</span>
              <span className="text-[11px] text-[var(--muted)] leading-tight">الرواتب · الحسابات · النماذج</span>
            </div>
          )}
        </Link>
        <button
          type="button"
          onClick={onCloseMobile}
          aria-label="إغلاق القائمة"
          className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] lg:hidden cursor-pointer"
        >
          <X size={20} />
        </button>
      </div>

      <ApprovalExceptionChip collapsed={collapsed} />
      <nav className="flex-1 overflow-y-auto p-2.5 space-y-1.5 no-scrollbar" aria-label="القائمة الرئيسية">
        {!collapsed && (
          <div className="px-2.5 pt-1.5 pb-1 text-[11px] font-bold text-[var(--muted)] uppercase tracking-wider">
            منظومة البوابة
          </div>
        )}

        {emptyPerms && !collapsed && (
          <p className="px-2.5 py-3 text-xs text-[var(--muted)] leading-relaxed">
            لا توجد أقسام متاحة لصلاحيتك — تواصل مع مسؤول النظام
          </p>
        )}

        {visibleGroups.map((group) => {
          const isGroupActive = activeGroupId === group.id;
          const isOpen = openGroupId === group.id;

          return (
            <div key={group.id} className="space-y-1">
              <div
                className={`flex items-center justify-between rounded-xl transition-all ${
                  isGroupActive
                    ? 'bg-[var(--raised)] text-[var(--ink)] font-bold'
                    : 'text-[var(--ink)] hover:bg-[var(--raised)]/70'
                } ${collapsed ? 'p-2 justify-center' : 'px-2.5 py-2'}`}
              >
                <Link
                  href={withPeriod(group.items[0]?.href ?? group.defaultHref)}
                  onClick={onCloseMobile}
                  title={collapsed ? group.label : undefined}
                  className={`flex items-center gap-2.5 flex-1 min-w-0 ${collapsed ? 'justify-center' : ''}`}
                >
                  <span className={`shrink-0 ${isGroupActive ? 'text-[var(--accent)]' : 'text-[var(--muted)]'}`}>
                    {group.icon}
                  </span>
                  {!collapsed && <span className="text-xs sm:text-sm font-semibold truncate">{group.label}</span>}
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
                        className={`transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`}
                      />
                    </button>
                  </div>
                )}
              </div>

              {!collapsed && isOpen && group.items.length > 0 && (
                <div className="ms-6 me-1 my-1 space-y-0.5 border-s-2 border-[var(--line)] ps-2.5 animate-in slide-in-from-top-1 duration-150">
                  {group.items.map((subItem) => {
                    const [subPath, subQuery] = subItem.href.split('?');
                    const cleanSubPath = subPath.replace(/\/$/, '') || '/';
                    const cleanCurrentPath = pathname.replace(/\/$/, '') || '/';
                    const pathMatch =
                      cleanCurrentPath === cleanSubPath ||
                      (group.id === 'approvals' && cleanCurrentPath.startsWith('/approvals')) ||
                      (cleanSubPath !== '/' && cleanCurrentPath.startsWith(cleanSubPath + '/'));

                    const hasTabbedSibling = group.items.some((sibling) => {
                      const [sibPath, sibQuery] = sibling.href.split('?');
                      const cleanSibPath = sibPath.replace(/\/$/, '') || '/';
                      return (
                        cleanSibPath === cleanSubPath &&
                        Boolean(sibQuery && new URLSearchParams(sibQuery).get('tab'))
                      );
                    });

                    let isSubActive = false;
                    if (pathMatch) {
                      if (subQuery) {
                        const targetParam = new URLSearchParams(subQuery).get('tab');
                        const defaultTab = new URLSearchParams(group.defaultHref.split('?')[1] || '').get('tab');
                        const currentParam = searchParams?.get('tab') ?? defaultTab;
                        isSubActive = currentParam === targetParam;
                      } else {
                        isSubActive = hasTabbedSibling
                          ? !searchParams?.get('tab')
                          : cleanCurrentPath === cleanSubPath ||
                            (group.id === 'approvals' && cleanCurrentPath.startsWith('/approvals'));
                      }
                    }

                    if (subItem.disabled) {
                      return (
                        <span
                          key={subItem.id}
                          className="flex items-center justify-between py-1.5 px-2 rounded-lg text-xs text-[var(--muted)] opacity-60 cursor-not-allowed"
                          title="لاحقًا"
                        >
                          <span className="truncate">{subItem.label}</span>
                          {subItem.laterBadge && (
                            <span className="text-[9px] font-bold px-1 rounded-sm bg-[var(--raised)] text-[var(--muted)]">
                              لاحقًا
                            </span>
                          )}
                        </span>
                      );
                    }

                    return (
                      <Link
                        key={subItem.id}
                        href={withPeriod(subItem.href)}
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
      <aside
        className={`hidden lg:block shrink-0 transition-all duration-200 sticky top-0 h-screen z-30 ${
          collapsed ? 'w-18' : 'w-64'
        }`}
      >
        {sidebarContent}
      </aside>

      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-xs animate-in fade-in"
            onClick={onCloseMobile}
            aria-hidden="true"
          />
          <div className="relative w-80 max-w-[85vw] h-full shadow-2xl animate-in slide-in-from-right duration-200 ms-auto">
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
