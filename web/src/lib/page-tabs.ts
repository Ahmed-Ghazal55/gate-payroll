// Page-tabs-rule 3.0: single navigation level for sidebar-split sections.
// Each sidebar-split page renders the section named by `?tab=` without a duplicate top tab bar.
// Legacy and unknown `?tab=` values redirect/map to their canonical target while preserving query params.

export const PAYROLL_TABS = [
  'platforms',
  'cycle',
  'current-cycle',
  'ninja_upload',
  'rules',
  'deductions',
  'bank',
  'policies',
] as const;
export type PayrollTab = (typeof PAYROLL_TABS)[number];

export const PAYROLL_TAB_LABELS: Record<PayrollTab, string> = {
  platforms: 'فواتير وتسويات المنصات',
  cycle: 'دورة الرواتب والمسير',
  'current-cycle': 'الدورة الحالية (النظام القائم)',
  ninja_upload: 'رفع فاتورة نينجا',
  rules: 'الأنظمة ومحرر القواعد',
  deductions: 'الاستقطاعات الشهرية',
  bank: 'التحويل والبنوك',
  policies: 'السياسات السابقة',
};

export const DEBTS_TABS = [
  'debts',
  'advance_requests',
  'actions',
  'statement',
  'approvals',
  'promissory',
] as const;
export type DebtsTab = (typeof DEBTS_TABS)[number];

export const DEBTS_TAB_LABELS: Record<DebtsTab, string> = {
  debts: 'سجل المديونيات',
  advance_requests: 'طلبات السلف',
  actions: 'تحصيل وإنشاء',
  statement: 'كشف حساب المندوب',
  approvals: 'منظومة الاعتمادات',
  promissory: 'أرشيف سندات الأمر',
};

export const TREASURY_TABS = [
  'vaults',
  'advance_payouts',
  'bank_file',
  'jahez',
  'movements',
] as const;
export type TreasuryTab = (typeof TREASURY_TABS)[number];

export const TREASURY_TAB_LABELS: Record<TreasuryTab, string> = {
  vaults: 'أرصدة الخزينة (1110-1140)',
  advance_payouts: 'صرف السلف المعتمدة',
  bank_file: 'ملف صرف الرواتب البنكي',
  jahez: 'جاهز — مطابقة وخزينة',
  movements: 'سجل القيود والحركات',
};

export const ACCOUNTING_TABS = [
  'coa',
  'journals',
  'manual_journals',
  'trial_balance',
  'income_statement',
] as const;
export type AccountingTab = (typeof ACCOUNTING_TABS)[number];

export const ACCOUNTING_TAB_LABELS: Record<AccountingTab, string> = {
  coa: 'دليل الحسابات',
  journals: 'سجل القيود اليومية',
  manual_journals: 'القيود اليدوية',
  trial_balance: 'ميزان المراجعة',
  income_statement: 'قائمة الدخل',
};

/** One-time toast when a removed tab redirects (U-W3-27 / U-W3-28). */
export const REMOVED_TAB_TOASTS = {
  adjustments:
    'تم إيقاف شاشة التسويات: مدا انتقل إلى المدفوعات، تعديلات الرواتب إلى بنود التعديل في الرواتب، والرسوم الإدارية إلى طلبات الخصم',
  extras:
    'تم نقل إدارة مدا إلى المدفوعات ← جهاز مدا والشحن، وسداد الالتزامات إلى الموردون والدفعات',
} as const;

export const ADMIN_TABS = [
  'users',
  'org',
  'lists',
  'bypass',
  'audit',
  'settings',
] as const;
export type AdminTab = (typeof ADMIN_TABS)[number];

export const ADMIN_TAB_LABELS: Record<AdminTab, string> = {
  users: 'المستخدمون والصلاحيات',
  org: 'الأقسام والرتب والتفويضات',
  lists: 'القوائم والإعدادات',
  bypass: 'تجاوز الاعتماد',
  audit: 'سجل التدقيق والعمليات',
  settings: 'إعدادات النظام',
};

export const OPERATIONS_TABS = ['cities', 'housing', 'vehicles', 'fuel'] as const;
export type OperationsTab = (typeof OPERATIONS_TABS)[number];

export const OPERATIONS_TAB_LABELS: Record<OperationsTab, string> = {
  cities: 'المدن والمناطق',
  housing: 'السكن والإسكان',
  vehicles: 'المركبات والتسليمات',
  fuel: 'الوقود',
};

export interface TabResolution<T extends string> {
  tab: T;
  legacy: boolean;
  /** Optional cross-page redirect pathname (e.g. operations?tab=cases -> /workforce/). */
  targetPathname?: string;
  /** Optional extra query params to apply on redirect (e.g. keeta_upload -> app=keeta&sub=settlements). */
  extraParams?: Record<string, string>;
  /** When true on a cross-page redirect, omit `?tab=` from the destination URL. */
  dropTab?: boolean;
}

const PAYROLL_LEGACY: Record<string, { tab: PayrollTab; extraParams?: Record<string, string> }> = {
  current_cycle: { tab: 'current-cycle' },
  apps: { tab: 'platforms' },
  keeta_upload: { tab: 'platforms', extraParams: { app: 'keeta', sub: 'settlements' } },
  ninja: { tab: 'platforms', extraParams: { app: 'ninja', sub: 'settlements' } },
};

const DEBTS_LEGACY: Record<string, { tab: DebtsTab }> = {
  statements: { tab: 'statement' },
  list: { tab: 'debts' },
  advances: { tab: 'advance_requests' },
};

const TREASURY_LEGACY: Record<string, { tab: TreasuryTab; targetPathname?: string; dropTab?: boolean }> = {
  payouts: { tab: 'advance_payouts' },
  bank: { tab: 'bank_file' },
  journals: { tab: 'movements' },
  extras: { tab: 'vaults', targetPathname: '/payments/mada', dropTab: true },
};

const ACCOUNTING_LEGACY: Record<string, { tab: AccountingTab }> = {
  trial: { tab: 'trial_balance' },
  income: { tab: 'income_statement' },
  manual: { tab: 'manual_journals' },
  adjustments: { tab: 'manual_journals' },
};

const ADMIN_LEGACY: Record<string, { tab: AdminTab }> = {
  roles: { tab: 'users' },
  audit_log: { tab: 'audit' },
};

export function resolvePayrollTab(raw: string | null | undefined): TabResolution<PayrollTab> {
  if (raw && (PAYROLL_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as PayrollTab, legacy: false };
  }
  const mapped = raw ? PAYROLL_LEGACY[raw] : undefined;
  if (mapped) return { tab: mapped.tab, extraParams: mapped.extraParams, legacy: true };
  return { tab: 'platforms', legacy: Boolean(raw) };
}

export function resolveDebtsTab(raw: string | null | undefined): TabResolution<DebtsTab> {
  if (raw && (DEBTS_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as DebtsTab, legacy: false };
  }
  const mapped = raw ? DEBTS_LEGACY[raw] : undefined;
  if (mapped) return { tab: mapped.tab, legacy: true };
  return { tab: 'debts', legacy: Boolean(raw) };
}

export function resolveTreasuryTab(raw: string | null | undefined): TabResolution<TreasuryTab> {
  if (raw && (TREASURY_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as TreasuryTab, legacy: false };
  }
  const mapped = raw ? TREASURY_LEGACY[raw] : undefined;
  if (mapped) {
    return {
      tab: mapped.tab,
      legacy: true,
      targetPathname: mapped.targetPathname,
      dropTab: mapped.dropTab,
    };
  }
  return { tab: 'vaults', legacy: Boolean(raw) };
}

export function resolveAccountingTab(raw: string | null | undefined): TabResolution<AccountingTab> {
  if (raw && (ACCOUNTING_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as AccountingTab, legacy: false };
  }
  const mapped = raw ? ACCOUNTING_LEGACY[raw] : undefined;
  if (mapped) return { tab: mapped.tab, legacy: true };
  return { tab: 'coa', legacy: Boolean(raw) };
}

export function resolveAdminTab(raw: string | null | undefined): TabResolution<AdminTab> {
  if (raw && (ADMIN_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as AdminTab, legacy: false };
  }
  const mapped = raw ? ADMIN_LEGACY[raw] : undefined;
  if (mapped) return { tab: mapped.tab, legacy: true };
  return { tab: 'users', legacy: Boolean(raw) };
}

export function resolveOperationsTab(raw: string | null | undefined): TabResolution<OperationsTab> {
  if (raw && (OPERATIONS_TABS as readonly string[]).includes(raw)) {
    return { tab: raw as OperationsTab, legacy: false };
  }
  if (raw === 'cases') {
    return { tab: 'cities', legacy: true, targetPathname: '/workforce/', extraParams: { tab: 'cases' }, dropTab: true };
  }
  if (raw === 'accounts') {
    return { tab: 'cities', legacy: true, targetPathname: '/workforce/', dropTab: true };
  }
  if (raw === 'custody' || raw === 'guarantees') {
    return { tab: 'vehicles', legacy: true };
  }
  return { tab: 'cities', legacy: Boolean(raw) };
}

/**
 * Builds the canonical URL for a resolved tab while preserving unrelated query params
 * (such as `period`, `id`, `action`, `voucherNo`, `app`, `sub`).
 */
export function buildTabRedirectUrl<T extends string>(
  pathname: string,
  currentSearch: string | URLSearchParams | null | undefined,
  resolution: TabResolution<T>
): string {
  const params = new URLSearchParams(
    typeof currentSearch === 'string' ? currentSearch : currentSearch?.toString() || ''
  );
  const targetPath = resolution.targetPathname || pathname;
  if (resolution.dropTab) {
    params.delete('tab');
  } else {
    params.set('tab', resolution.tab);
  }
  if (resolution.extraParams) {
    for (const [k, v] of Object.entries(resolution.extraParams)) {
      params.set(k, v);
    }
  }
  const qs = params.toString();
  return qs ? `${targetPath}?${qs}` : targetPath;
}

/** Breadcrumb Arabic label for a section + `?tab=` query param. */
export function getBreadcrumbTabLabel(section: string, rawTab: string | null | undefined): string | null {
  if (!rawTab) return null;
  switch (section) {
    case 'operations':
      return OPERATIONS_TAB_LABELS[resolveOperationsTab(rawTab).tab] ?? null;
    case 'payroll':
      return PAYROLL_TAB_LABELS[resolvePayrollTab(rawTab).tab] ?? null;
    case 'debts':
      return DEBTS_TAB_LABELS[resolveDebtsTab(rawTab).tab] ?? null;
    case 'treasury':
      return TREASURY_TAB_LABELS[resolveTreasuryTab(rawTab).tab] ?? null;
    case 'accounting':
      return ACCOUNTING_TAB_LABELS[resolveAccountingTab(rawTab).tab] ?? null;
    case 'admin':
      return ADMIN_TAB_LABELS[resolveAdminTab(rawTab).tab] ?? null;
    case 'rider-app': {
      const map: Record<string, string> = {
        campaigns: 'الحملات والروابط',
        builder: 'مصمم النماذج',
        responses: 'مراجعة الردود',
        accounts: 'الحسابات والأجهزة',
        broadcasts: 'الإشعارات والإرسال',
        statements: 'إرسال كشوف الحساب',
        requests: 'الطلبات والمواعيد',
        settings: 'إعدادات التطبيق',
        analytics: 'التحليلات',
      };
      return map[rawTab] ?? null;
    }
    default:
      return null;
  }
}

/** Consolidated legacy redirect table for documentation and unit tests. */
export const TAB_REDIRECT_MAP = {
  payroll: PAYROLL_LEGACY,
  debts: DEBTS_LEGACY,
  treasury: TREASURY_LEGACY,
  accounting: ACCOUNTING_LEGACY,
  admin: ADMIN_LEGACY,
  operations: {
    cases: '/workforce/?tab=cases',
    accounts: '/workforce/',
    custody: '/operations/?tab=vehicles',
    guarantees: '/operations/?tab=vehicles',
  },
  removed: {
    'treasury?tab=extras': '/payments/mada',
    'accounting?tab=adjustments': '/accounting/?tab=manual_journals',
  },
} as const;
