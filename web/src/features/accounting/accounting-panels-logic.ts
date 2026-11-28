import type {
  CompanySponsor,
  FinanceJournal,
  IncomeStatementResponse,
  TargetCoaAccount,
  TrialBalanceCompanyRow,
  TrialBalanceResponse,
} from '../../lib/types';

export type LedgerEntryRow = {
  id: string;
  journalId: string;
  journalNumber: string;
  date: string;
  period: string;
  accountCode: string;
  accountNameAr: string;
  companyName: string;
  description: string;
  sourceRef: string;
  debit: number;
  credit: number;
  journal: FinanceJournal;
};

export function lineMatchesSponsor(
  lineSponsorId: string | undefined,
  selectedSponsor: string,
  companies: CompanySponsor[]
): boolean {
  if (selectedSponsor === 'all') return true;
  const lineSponsor = (lineSponsorId || '').trim();
  if (!lineSponsor) return false;
  if (lineSponsor === selectedSponsor) return true;
  return companies.some(
    (c) =>
      (c.code === selectedSponsor || c.id === selectedSponsor) &&
      (lineSponsor === c.id || lineSponsor === c.code)
  );
}

export function buildGeneralLedgerRows({
  journals,
  periodMonth,
  accountCode,
  selectedSponsor,
  companies,
  coaAccounts,
  companyName,
}: {
  journals: FinanceJournal[];
  periodMonth: string;
  accountCode: string;
  selectedSponsor: string;
  companies: CompanySponsor[];
  coaAccounts: TargetCoaAccount[];
  companyName: (...candidates: Array<string | undefined>) => string;
}): LedgerEntryRow[] {
  const out: LedgerEntryRow[] = [];
  for (const j of journals) {
    const jPeriod = (j.period || j.date || j.entryDate || j.createdAt || '').slice(0, 7);
    if (periodMonth && jPeriod && jPeriod !== periodMonth) continue;
    const lines = j.lines || [];
    lines.forEach((l, idx) => {
      if (accountCode !== 'all' && l.accountCode !== accountCode) return;
      if (!lineMatchesSponsor(l.sponsorId, selectedSponsor, companies)) return;
      out.push({
        id: `${j.id}-${idx}`,
        journalId: j.id,
        journalNumber: j.journalNumber || j.id.slice(0, 10),
        date: (j.date || j.entryDate || j.createdAt || '').slice(0, 10),
        period: jPeriod || periodMonth,
        accountCode: l.accountCode || '',
        accountNameAr: l.accountNameAr || coaAccounts.find((a) => a.code === l.accountCode)?.nameAr || '—',
        companyName: companyName(l.sponsorId, j.originSponsorId, j.creditorSponsorId),
        description: l.description || j.description || '—',
        sourceRef: j.sourceRef || '—',
        debit: Number(l.debit || 0),
        credit: Number(l.credit || 0),
        journal: j,
      });
    });
  }
  return out;
}

export type BalanceSheetLine = {
  id: string;
  section: 'assets' | 'liabilities' | 'equity';
  sectionLabel: string;
  code: string;
  nameAr: string;
  level: number;
  posting: boolean;
  isDisplayPlug?: boolean;
  balance: number;
};

export type BalanceSheetSection = {
  key: string;
  companyLabel: string;
  totalAssets: number;
  totalLiabilities: number;
  totalEquity: number;
  diff: number;
  isBalanced: boolean;
  lines: BalanceSheetLine[];
};

const BS_SECTION_LABEL: Record<BalanceSheetLine['section'], string> = {
  assets: 'الأصول',
  liabilities: 'الالتزامات',
  equity: 'حقوق الملكية',
};

export function buildBalanceSheetSections(
  trialData: TrialBalanceResponse | null,
  incomeData: IncomeStatementResponse | null,
  companyName: (...candidates: Array<string | undefined>) => string
): BalanceSheetSection[] {
  const trialCompanies = trialData?.companies || [];
  const incomeCompanies = incomeData?.companies || [];
  return trialCompanies.map((c) => {
    const inc = incomeCompanies.find((ic) => ic.sponsorId === c.sponsorId || ic.companyCode === c.companyCode);
    const netProfit = Number(inc?.net || 0);
    const rows = (c.rows || []) as TrialBalanceCompanyRow[];
    const lines: BalanceSheetLine[] = [];
    let totalAssets = 0;
    let totalLiabilities = 0;
    let totalEquityAccounts = 0;

    for (const r of rows) {
      const cls = (r.code || '')[0];
      if (cls !== '1' && cls !== '2' && cls !== '3') continue;
      const closingDebit = Number(r.closingDebit || 0);
      const closingCredit = Number(r.closingCredit || 0);
      const balance = cls === '1' ? closingDebit - closingCredit : closingCredit - closingDebit;
      if (r.code === '1') totalAssets = balance;
      if (r.code === '2') totalLiabilities = balance;
      if (r.code === '3') totalEquityAccounts = balance;
      if (balance === 0 && (r.level ?? 0) > 2) continue;
      const section: BalanceSheetLine['section'] =
        cls === '1' ? 'assets' : cls === '2' ? 'liabilities' : 'equity';
      lines.push({
        id: `${c.sponsorId || c.companyCode}-${r.code}`,
        section,
        sectionLabel: BS_SECTION_LABEL[section],
        code: r.code,
        nameAr: r.nameAr || r.name || r.code,
        level: Number(r.level || 1),
        posting: Boolean(r.posting),
        balance,
      });
    }
    if (netProfit !== 0) {
      lines.push({
        id: `${c.sponsorId || c.companyCode}-NET`,
        section: 'equity',
        sectionLabel: BS_SECTION_LABEL.equity,
        code: '3999',
        nameAr: 'صافي أرباح / خسائر الفترة الحالية (بند عرض)',
        level: 2,
        posting: true,
        isDisplayPlug: true,
        balance: netProfit,
      });
    }
    const totalEquity = totalEquityAccounts + netProfit;
    const roundedDiff = Math.round((totalAssets - (totalLiabilities + totalEquity)) * 100) / 100;
    const diff = Object.is(roundedDiff, -0) ? 0 : roundedDiff;
    return {
      key: c.sponsorId || c.companyCode || 'company',
      companyLabel: companyName(c.companyCode, c.sponsorId),
      totalAssets,
      totalLiabilities,
      totalEquity,
      diff,
      isBalanced: Math.abs(diff) < 0.01,
      lines,
    };
  });
}

export type AppProfitRow = {
  app: string;
  nameAr: string;
  receivableCode: string;
  revenueCodes: string[];
  receivableClosing: number;
  revenueAmount: number;
};

export const PLATFORM_ACCOUNT_MAP: ReadonlyArray<{
  app: string;
  nameAr: string;
  receivableCode: string;
  revenueCodes: string[];
}> = [
  { app: 'keeta', nameAr: 'كيتا', receivableCode: '112804', revenueCodes: ['411101', '411501'] },
  { app: 'hs', nameAr: 'هنقرستيشن', receivableCode: '112805', revenueCodes: ['411102', '411601'] },
  { app: 'chefz', nameAr: 'ذا شيفز', receivableCode: '112807', revenueCodes: ['411103'] },
  { app: 'ninja', nameAr: 'نينجا', receivableCode: '112806', revenueCodes: ['411104'] },
  { app: 'amazon', nameAr: 'أمازون', receivableCode: '112808', revenueCodes: ['411105'] },
  { app: 'jahez', nameAr: 'جاهز', receivableCode: '112801', revenueCodes: ['411106', '411401'] },
];

export function buildAppProfitabilityRows(trialData: TrialBalanceResponse | null): AppProfitRow[] {
  const byCode = new Map<string, { debit: number; credit: number; closingDebit: number; closingCredit: number }>();
  for (const c of trialData?.companies || []) {
    for (const r of (c.rows || []) as TrialBalanceCompanyRow[]) {
      const prev = byCode.get(r.code) || { debit: 0, credit: 0, closingDebit: 0, closingCredit: 0 };
      byCode.set(r.code, {
        debit: prev.debit + Number(r.periodDebit || 0),
        credit: prev.credit + Number(r.periodCredit || 0),
        closingDebit: prev.closingDebit + Number(r.closingDebit || 0),
        closingCredit: prev.closingCredit + Number(r.closingCredit || 0),
      });
    }
  }
  return PLATFORM_ACCOUNT_MAP.map((p) => {
    const rec = byCode.get(p.receivableCode);
    const receivableClosing = rec ? rec.closingDebit - rec.closingCredit : 0;
    const revenueAmount = p.revenueCodes.reduce((sum, code) => {
      const m = byCode.get(code);
      return sum + (m ? m.credit - m.debit : 0);
    }, 0);
    return {
      app: p.app,
      nameAr: p.nameAr,
      receivableCode: p.receivableCode,
      revenueCodes: p.revenueCodes,
      receivableClosing,
      revenueAmount,
    };
  });
}

export function summarizeAppProfitability(rows: AppProfitRow[]): { revenue: number; rec: number } {
  const revenue = rows.reduce((s, r) => s + r.revenueAmount, 0);
  const rec = rows.reduce((s, r) => s + r.receivableClosing, 0);
  return { revenue, rec };
}
