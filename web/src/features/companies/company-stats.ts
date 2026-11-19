import type {
  CompaniesStatsTotals,
  CompanyStatsItem,
  CompanyStatsPlatformCount,
} from '../../lib/api';

export type CompaniesTabId = 'companies' | 'cr' | 'expiring' | 'stats';

export const BALANCES_PLACEHOLDER_AR = 'الأرصدة بعد ربط المحاسبة';

export const CANONICAL_PLATFORMS: ReadonlyArray<{ code: string; labelAr: string }> = [
  { code: 'KEETA', labelAr: 'كيتا' },
  { code: 'HUNGER', labelAr: 'هنقرستيشن' },
  { code: 'NINJA', labelAr: 'نينجا' },
  { code: 'JAHEZ', labelAr: 'جاهز' },
  { code: 'SHIFTZ', labelAr: 'ذا شفز' },
  { code: 'AMAZON', labelAr: 'أمازون' },
];

const AUDIT_ACTION_LABELS_AR: Record<string, string> = {
  'company.seeded': 'تهيئة سجل شركة أساسي',
  'company.created': 'إنشاء شركة جديدة',
  'company.updated': 'تحديث بيانات شركة',
  'company.archived': 'أرشفة شركة',
  'company.registration_created': 'إضافة سجل تجاري',
  'company.registration_updated': 'تحديث سجل تجاري',
  'commercial_registration.archived': 'أرشفة سجل تجاري',
  'company.document_created': 'رفع وثيقة شركة',
  'company.document_updated': 'تحديث وثيقة شركة',
  'company_document.archived': 'أرشفة وثيقة شركة',
  'company.import_committed': 'اعتماد استيراد شهادات الشركات',
};

export function auditActionLabelAr(action: string | null | undefined): string {
  const key = (action || '').trim();
  if (!key) return 'عملية مسجلة';
  if (AUDIT_ACTION_LABELS_AR[key]) return AUDIT_ACTION_LABELS_AR[key];
  if (/[\u0600-\u06FF]/.test(key) && !/[A-Za-z]/.test(key)) return key;
  return 'عملية إدارية مسجلة';
}

export function normalizeCompaniesTab(raw: string | null | undefined): CompaniesTabId {
  const value = (raw || '').trim().toLowerCase();
  if (value === 'stats' || value === 'statistics') return 'stats';
  if (value === 'cr' || value === 'registrations') return 'cr';
  if (value === 'expiring' || value === 'documents') return 'expiring';
  return 'companies';
}

export function normalizePlatformCode(code: string | null | undefined): string {
  const value = (code || '').trim().toUpperCase();
  if (!value) return '';
  if (value === 'HUNGERSTATION' || value === 'HUNGER_STATION' || value === 'HUNGER-STATION' || value === 'HS') {
    return 'HUNGER';
  }
  if (value === 'CHEFZ' || value === 'THECHEFZ' || value === 'THE_CHEFZ' || value === 'THE-CHEFZ' || value === 'CHEFSZ') {
    return 'SHIFTZ';
  }
  return value;
}

export function platformLabelAr(code: string | null | undefined, apiNameAr?: string | null): string {
  const normalized = normalizePlatformCode(code);
  const known = CANONICAL_PLATFORMS.find((p) => p.code === normalized);
  if (known) return known.labelAr;
  if (normalized === 'OTHER') return 'أخرى';
  const trimmed = (apiNameAr || '').trim();
  if (trimmed && /[\u0600-\u06FF]/.test(trimmed) && !/[A-Za-z]/.test(trimmed)) {
    return trimmed;
  }
  return 'منصة أخرى';
}

export function buildWorkforceCompanyUrl(companyKey: string, platformCode?: string): string {
  const params = new URLSearchParams();
  if (companyKey.trim()) params.set('company', companyKey.trim());
  if (platformCode && platformCode.trim()) params.set('platform', platformCode.trim());
  const qs = params.toString();
  return qs ? `/workforce/?${qs}` : '/workforce/';
}

export interface PlatformColumn {
  code: string;
  labelAr: string;
}

export function getPlatformColumns(
  items: ReadonlyArray<Pick<CompanyStatsItem, 'platformAccounts'>>,
  extraByPlatform?: ReadonlyArray<Pick<CompanyStatsPlatformCount, 'code' | 'nameAr'>>
): PlatformColumn[] {
  const columns: PlatformColumn[] = CANONICAL_PLATFORMS.map((p) => ({ ...p }));
  const seen = new Set(columns.map((c) => c.code));

  const append = (entry: Pick<CompanyStatsPlatformCount, 'code' | 'nameAr'>) => {
    const code = normalizePlatformCode(entry.code) || 'OTHER';
    if (seen.has(code)) return;
    seen.add(code);
    columns.push({ code, labelAr: platformLabelAr(code, entry.nameAr) });
  };

  for (const item of items) {
    for (const entry of item.platformAccounts.byPlatform) {
      append(entry);
    }
  }
  if (extraByPlatform) {
    for (const entry of extraByPlatform) {
      append(entry);
    }
  }

  return columns;
}

export function filterCompanyStatsItems(
  items: ReadonlyArray<CompanyStatsItem>,
  query: string
): CompanyStatsItem[] {
  const q = query.trim().toLowerCase();
  if (!q) return [...items];
  return items.filter(
    (item) =>
      item.nameAr.toLowerCase().includes(q) ||
      item.shortNameAr.toLowerCase().includes(q) ||
      item.code.toLowerCase().includes(q) ||
      item.unifiedNumber.includes(q) ||
      item.commercialReg.includes(q)
  );
}

export function computeCompanyStatsTotals(
  items: ReadonlyArray<CompanyStatsItem>
): CompaniesStatsTotals {
  let sponsoredTotal = 0;
  let sponsoredRiders = 0;
  let sponsoredAdmins = 0;
  let sponsoredActive = 0;
  let sponsoredOnLeave = 0;
  let platformAccountsTotal = 0;
  let platformAccountsActive = 0;
  let platformAccountsAssigned = 0;
  const byPlatMap = new Map<string, CompanyStatsPlatformCount>();

  for (const item of items) {
    sponsoredTotal += Number(item.sponsorships.total) || 0;
    sponsoredRiders += Number(item.sponsorships.riders) || 0;
    sponsoredAdmins += Number(item.sponsorships.admins) || 0;
    sponsoredActive += Number(item.sponsorships.active) || 0;
    sponsoredOnLeave += Number(item.sponsorships.onLeave) || 0;
    platformAccountsTotal += Number(item.platformAccounts.total) || 0;
    platformAccountsActive += Number(item.platformAccounts.active) || 0;
    platformAccountsAssigned += Number(item.platformAccounts.assigned) || 0;

    for (const entry of item.platformAccounts.byPlatform) {
      const code = normalizePlatformCode(entry.code) || 'OTHER';
      const cur = byPlatMap.get(code);
      if (cur) {
        cur.total += Number(entry.total) || 0;
        cur.active += Number(entry.active) || 0;
        cur.assigned += Number(entry.assigned) || 0;
      } else {
        byPlatMap.set(code, {
          code,
          nameAr: platformLabelAr(code, entry.nameAr),
          total: Number(entry.total) || 0,
          active: Number(entry.active) || 0,
          assigned: Number(entry.assigned) || 0,
        });
      }
    }
  }

  const canonicalIdx = (code: string) => {
    const idx = CANONICAL_PLATFORMS.findIndex((p) => p.code === code);
    return idx >= 0 ? idx : CANONICAL_PLATFORMS.length;
  };
  const byPlatform = Array.from(byPlatMap.values()).sort(
    (a, b) => canonicalIdx(a.code) - canonicalIdx(b.code) || a.code.localeCompare(b.code)
  );

  return {
    companiesCount: items.length,
    sponsoredTotal,
    sponsoredRiders,
    sponsoredAdmins,
    sponsoredActive,
    sponsoredOnLeave,
    platformAccountsTotal,
    platformAccountsActive,
    platformAccountsAssigned,
    byPlatform,
  };
}

export function platformCountFor(
  byPlatform: ReadonlyArray<Pick<CompanyStatsPlatformCount, 'code' | 'total'>>,
  targetCode: string
): number {
  const want = normalizePlatformCode(targetCode) || 'OTHER';
  return byPlatform.reduce((sum, entry) => {
    const code = normalizePlatformCode(entry.code) || 'OTHER';
    return code === want ? sum + (Number(entry.total) || 0) : sum;
  }, 0);
}

export function buildCompanyStatsExport(items: ReadonlyArray<CompanyStatsItem>): {
  headers: string[];
  rows: Array<Array<string | number>>;
  numericColumns: number[];
} {
  const platformColumns = getPlatformColumns(items);
  const headers = [
    'اسم المنشأة',
    'الرقم الموحد',
    'السجل التجاري',
    'حالة الربط',
    'إجمالي المكفولين',
    'المناديب المكفولون',
    'الإداريون المكفولون',
    'المكفولون النشطون',
    'في إجازة',
    'إجمالي حسابات المنصات',
    'الحسابات النشطة',
    'الحسابات المسندة',
    ...platformColumns.map((p) => `حسابات ${p.labelAr}`),
  ];

  const rows = items.map((item) => [
    item.nameAr,
    item.unifiedNumber || '',
    item.commercialReg || '',
    item.erpLinked ? 'مرتبط' : 'غير مرتبط',
    item.sponsorships.total,
    item.sponsorships.riders,
    item.sponsorships.admins,
    item.sponsorships.active,
    item.sponsorships.onLeave,
    item.platformAccounts.total,
    item.platformAccounts.active,
    item.platformAccounts.assigned,
    ...platformColumns.map((p) => platformCountFor(item.platformAccounts.byPlatform, p.code)),
  ]);

  const numericColumns = Array.from({ length: headers.length - 4 }, (_, idx) => idx + 4);
  return {
    headers,
    rows,
    numericColumns,
  };
}
