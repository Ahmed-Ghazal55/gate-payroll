import type { CompanyStatsItem, CompanyStatsPlatformCount } from '../../lib/api';

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

export function platformCountFor(
  byPlatform: ReadonlyArray<Pick<CompanyStatsPlatformCount, 'code' | 'total'>>,
  targetCode: string
): number {
  const want = normalizePlatformCode(targetCode);
  return byPlatform.reduce((sum, entry) => {
    return normalizePlatformCode(entry.code) === want ? sum + (Number(entry.total) || 0) : sum;
  }, 0);
}

export function buildCompanyStatsExport(items: ReadonlyArray<CompanyStatsItem>): {
  headers: string[];
  rows: Array<Array<string | number>>;
  numericColumns: number[];
} {
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
    ...CANONICAL_PLATFORMS.map((p) => `حسابات ${p.labelAr}`),
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
    ...CANONICAL_PLATFORMS.map((p) => platformCountFor(item.platformAccounts.byPlatform, p.code)),
  ]);

  return {
    headers,
    rows,
    numericColumns: [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
  };
}
