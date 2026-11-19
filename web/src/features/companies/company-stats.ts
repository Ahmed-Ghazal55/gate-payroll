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
