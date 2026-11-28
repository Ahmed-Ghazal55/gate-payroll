'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  BookOpen,
  Scale,
  Building2,
  PieChart,
  FileSpreadsheet,
  Download,
  RefreshCw,
  TrendingUp,
  TrendingDown,
  Eye,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { formatSar } from '../../lib/money';
import type { AccountingTab } from '../../lib/page-tabs';
import type {
  CompanySponsor,
  CostCentreRow,
  FinanceJournal,
  IncomeStatementResponse,
  TargetCoaAccount,
  TrialBalanceCompanyRow,
  TrialBalanceResponse,
} from '../../lib/types';
import { DataTable, type ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { StatCard } from '../../components/ui/StatCard';
import { EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';

type ExportFn = (title: string, headers: string[], rows: unknown[][], numericCols?: number[]) => void;

type SharedProps = {
  companies: CompanySponsor[];
  selectedSponsor: string;
  onSponsorChange: (v: string) => void;
  periodMonth: string;
  onPeriodChange: (v: string) => void;
  companyName: (...candidates: Array<string | undefined>) => string;
  onExportExcel: ExportFn;
};

type LedgerEntryRow = {
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

export function GeneralLedgerPanel({
  coaAccounts,
  journals,
  loadingJournals,
  journalsError,
  onReloadJournals,
  onSelectJournal,
  companies,
  selectedSponsor,
  onSponsorChange,
  periodMonth,
  onPeriodChange,
  companyName,
  onExportExcel,
}: SharedProps & {
  coaAccounts: TargetCoaAccount[];
  journals: FinanceJournal[];
  loadingJournals: boolean;
  journalsError: string | null;
  onReloadJournals: () => void;
  onSelectJournal: (j: FinanceJournal) => void;
}) {
  const [accountCode, setAccountCode] = useState<string>('all');

  const accountOptions = useMemo(
    () => [
      { value: 'all', label: 'كافة الحسابات' },
      ...coaAccounts
        .filter((a) => a.posting ?? a.isPosting)
        .map((a) => ({ value: a.code, label: `${a.code} — ${a.nameAr}` })),
    ],
    [coaAccounts]
  );

  const ledgerRows = useMemo<LedgerEntryRow[]>(() => {
    const out: LedgerEntryRow[] = [];
    for (const j of journals) {
      const jPeriod = (j.period || j.date || j.entryDate || j.createdAt || '').slice(0, 7);
      if (periodMonth && jPeriod && jPeriod !== periodMonth) continue;
      const lines = j.lines || [];
      lines.forEach((l, idx) => {
        if (accountCode !== 'all' && l.accountCode !== accountCode) return;
        const sponsorMatch =
          selectedSponsor === 'all' ||
          l.sponsorId === selectedSponsor ||
          j.originSponsorId === selectedSponsor ||
          j.creditorSponsorId === selectedSponsor ||
          companies.some(
            (c) =>
              (c.code === selectedSponsor || c.id === selectedSponsor) &&
              (l.sponsorId === c.id || l.sponsorId === c.code || j.originSponsorId === c.id)
          );
        if (!sponsorMatch) return;
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
  }, [journals, periodMonth, accountCode, selectedSponsor, companies, coaAccounts, companyName]);

  const totals = useMemo(() => {
    const debit = ledgerRows.reduce((s, r) => s + r.debit, 0);
    const credit = ledgerRows.reduce((s, r) => s + r.credit, 0);
    return { count: ledgerRows.length, debit, credit, net: debit - credit };
  }, [ledgerRows]);

  const columns: ColumnDef<LedgerEntryRow>[] = [
    {
      key: 'date',
      header: 'التاريخ',
      type: 'date',
      getValue: (r) => r.date,
      render: (r) => <span className="num text-xs text-[var(--muted)]">{r.date || '—'}</span>,
    },
    {
      key: 'journalNumber',
      header: 'رقم القيد',
      type: 'text',
      getValue: (r) => r.journalNumber,
      render: (r) => (
        <button
          type="button"
          onClick={() => onSelectJournal(r.journal)}
          className="inline-flex items-center gap-1 font-mono font-bold text-xs text-[var(--accent)] hover:underline"
        >
          <Eye size={12} />
          <span>{r.journalNumber}</span>
        </button>
      ),
    },
    {
      key: 'accountCode',
      header: 'الحساب',
      type: 'list',
      getValue: (r) => r.accountCode,
      labelOf: (v) => {
        const match = coaAccounts.find((a) => a.code === String(v));
        return match ? `${match.code} — ${match.nameAr}` : String(v || '');
      },
      render: (r) => (
        <span className="text-xs">
          <span className="num font-mono font-bold text-[var(--accent)] me-1">{r.accountCode}</span>
          <span>{r.accountNameAr}</span>
        </span>
      ),
    },
    {
      key: 'companyName',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => r.companyName,
    },
    {
      key: 'description',
      header: 'البيان',
      type: 'text',
      getValue: (r) => r.description,
    },
    {
      key: 'debit',
      header: 'مدين',
      type: 'number',
      numeric: true,
      getValue: (r) => r.debit,
      render: (r) => (
        <span className="num font-mono text-xs font-bold text-blue-600 dark:text-blue-400">
          {r.debit > 0 ? formatSar(r.debit) : '—'}
        </span>
      ),
    },
    {
      key: 'credit',
      header: 'دائن',
      type: 'number',
      numeric: true,
      getValue: (r) => r.credit,
      render: (r) => (
        <span className="num font-mono text-xs font-bold text-purple-600 dark:text-purple-400">
          {r.credit > 0 ? formatSar(r.credit) : '—'}
        </span>
      ),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard label="الحركات المطابقة" value={totals.count} hint={`فترة ${periodMonth}`} icon={BookOpen} />
        <StatCard label="إجمالي المدين" value={formatSar(totals.debit)} hint="مجموع أسطر المدين" icon={TrendingUp} />
        <StatCard label="إجمالي الدائن" value={formatSar(totals.credit)} hint="مجموع أسطر الدائن" icon={TrendingDown} />
        <StatCard label="صافي الحركة" value={formatSar(totals.net)} hint="المدين ناقص الدائن" icon={Scale} />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex flex-wrap items-center gap-2">
          <SearchableSelect
            id="gl-account-filter"
            value={accountCode}
            onChange={setAccountCode}
            ariaLabel="اختيار الحساب"
            placeholder="كافة الحسابات"
            size="sm"
            className="w-64"
            options={accountOptions}
          />
          <SearchableSelect
            id="gl-sponsor-filter"
            value={selectedSponsor}
            onChange={onSponsorChange}
            ariaLabel="تصفية الشركة"
            placeholder="كافة المنشآت"
            size="sm"
            className="w-52"
            options={[
              { value: 'all', label: 'كافة المنشآت والسجلات' },
              ...companies.map((c) => ({
                value: c.code || c.id,
                label: `${c.nameAr || c.shortNameAr || c.code || c.id}${c.code ? ` (${c.code})` : ''}`,
              })),
            ]}
          />
          <input
            type="month"
            value={periodMonth}
            onChange={(e) => onPeriodChange(e.target.value)}
            aria-label="الفترة"
            className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
          />
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onReloadJournals}
            className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
            title="تحديث القيود"
          >
            <RefreshCw size={15} />
          </button>
          <button
            type="button"
            onClick={() =>
              onExportExcel(
                'دفتر_الأستاذ',
                ['التاريخ', 'رقم القيد', 'رمز الحساب', 'اسم الحساب', 'الشركة', 'البيان', 'مدين', 'دائن'],
                ledgerRows.map((r) => [r.date, r.journalNumber, r.accountCode, r.accountNameAr, r.companyName, r.description, r.debit, r.credit]),
                [2, 6, 7]
              )
            }
            className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
          >
            <Download size={14} />
            <span>تصدير إكسل</span>
          </button>
        </div>
      </div>

      {journalsError ? (
        <ErrorState title="تعذر تحميل دفتر الأستاذ" message={journalsError} onRetry={onReloadJournals} />
      ) : (
        <DataTable
          tableId="accounting-general-ledger"
          columns={columns}
          data={ledgerRows}
          loading={loadingJournals}
          emptyTitle="لا توجد حركات في دفتر الأستاذ"
          emptyDescription="لا توجد أسطر قيود مطابقة للحساب والشركة والفترة المختارة."
          exportFilename={`general_ledger_${periodMonth}`}
          getRowId={(r) => r.id}
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}

type BalanceSheetLine = {
  id: string;
  section: 'assets' | 'liabilities' | 'equity';
  sectionLabel: string;
  code: string;
  nameAr: string;
  level: number;
  posting: boolean;
  balance: number;
};

const BS_SECTION_LABEL: Record<BalanceSheetLine['section'], string> = {
  assets: 'الأصول',
  liabilities: 'الالتزامات',
  equity: 'حقوق الملكية',
};

export function BalanceSheetPanel({
  trialData,
  incomeData,
  loading,
  error,
  onReload,
  companies,
  selectedSponsor,
  onSponsorChange,
  periodMonth,
  onPeriodChange,
  companyName,
  onExportExcel,
}: SharedProps & {
  trialData: TrialBalanceResponse | null;
  incomeData: IncomeStatementResponse | null;
  loading: boolean;
  error: string | null;
  onReload: () => void;
}) {
  const sections = useMemo(() => {
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
          nameAr: 'صافي أرباح / خسائر الفترة الحالية',
          level: 2,
          posting: true,
          balance: netProfit,
        });
      }
      const totalEquity = totalEquityAccounts + netProfit;
      return {
        key: c.sponsorId || c.companyCode || 'company',
        companyLabel: companyName(c.companyCode, c.sponsorId),
        totalAssets,
        totalLiabilities,
        totalEquity,
        diff: totalAssets - (totalLiabilities + totalEquity),
        lines,
      };
    });
  }, [trialData, incomeData, companyName]);

  const columns: ColumnDef<BalanceSheetLine>[] = [
    {
      key: 'sectionLabel',
      header: 'القسم',
      type: 'list',
      getValue: (r) => r.sectionLabel,
      toneOf: (v) => (v === 'الأصول' ? 'info' : v === 'الالتزامات' ? 'warning' : 'success'),
    },
    {
      key: 'code',
      header: 'رمز الحساب',
      type: 'text',
      getValue: (r) => r.code,
      render: (r) => <span className="num font-mono font-bold text-[var(--accent)]">{r.code}</span>,
    },
    {
      key: 'nameAr',
      header: 'اسم البند',
      type: 'text',
      getValue: (r) => r.nameAr,
      render: (r) => <span className={r.posting ? 'text-[var(--ink)]' : 'font-bold text-[var(--ink)]'}>{r.nameAr}</span>,
    },
    {
      key: 'balance',
      header: 'الرصيد (ر.س)',
      type: 'number',
      numeric: true,
      getValue: (r) => r.balance,
      render: (r) => <span className="num font-bold">{formatSar(r.balance)}</span>,
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex flex-wrap items-center gap-2">
          <SearchableSelect
            id="bs-sponsor-filter"
            value={selectedSponsor}
            onChange={onSponsorChange}
            ariaLabel="تصفية المنشأة"
            placeholder="كافة المنشآت"
            size="sm"
            className="w-52"
            options={[
              { value: 'all', label: 'كافة المنشآت والسجلات' },
              ...companies.map((c) => ({
                value: c.code || c.id,
                label: `${c.nameAr || c.shortNameAr || c.code || c.id}${c.code ? ` (${c.code})` : ''}`,
              })),
            ]}
          />
          <input
            type="month"
            value={periodMonth}
            onChange={(e) => onPeriodChange(e.target.value)}
            aria-label="الفترة"
            className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
          />
        </div>
        <button
          type="button"
          onClick={onReload}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-semibold text-[var(--ink)] border border-[var(--line)] hover:bg-[var(--line)] transition"
        >
          <RefreshCw size={14} />
          <span>تحديث</span>
        </button>
      </div>

      {error ? (
        <ErrorState title="تعذر تحميل الميزانية العمومية" message={error} onRetry={onReload} />
      ) : sections.length === 0 && !loading ? (
        <EmptyState title="لا توجد بيانات للميزانية العمومية" description="اختر الفترة والمنشأة لعرض الأصول والالتزامات وحقوق الملكية." />
      ) : (
        <div className="space-y-6">
          {sections.map((sec) => (
            <div key={sec.key} className="space-y-4">
              <div className="flex items-center justify-between px-4 py-2.5 rounded-xl bg-[var(--surface)] border border-[var(--line)] text-xs font-bold">
                <span>{sec.companyLabel}</span>
                <button
                  type="button"
                  onClick={() =>
                    onExportExcel(
                      `الميزانية_العمومية_${sec.key}`,
                      ['القسم', 'رمز الحساب', 'اسم البند', 'الرصيد (ر.س)'],
                      sec.lines.map((l) => [l.sectionLabel, l.code, l.nameAr, l.balance]),
                      [1, 3]
                    )
                  }
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold"
                >
                  <Download size={13} />
                  <span>تصدير إكسل</span>
                </button>
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <StatCard label="إجمالي الأصول (1)" value={formatSar(sec.totalAssets)} icon={Scale} />
                <StatCard label="إجمالي الالتزامات (2)" value={formatSar(sec.totalLiabilities)} icon={TrendingDown} />
                <StatCard label="حقوق الملكية وصافي الفترة (3)" value={formatSar(sec.totalEquity)} icon={TrendingUp} />
              </div>
              <DataTable
                tableId={`accounting-balance-sheet-${sec.key}`}
                columns={columns}
                data={sec.lines}
                loading={loading}
                emptyTitle="لا توجد بنود ميزانية"
                emptyDescription="لم تُسجّل حركات على حسابات الميزانية في هذه الفترة."
                exportFilename={`balance_sheet_${sec.key}_${periodMonth}`}
                getRowId={(r) => r.id}
                searchable
                syncUrl={false}
              />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const CC_KIND_AR: Record<string, string> = {
  none: 'عام / بدون مركز',
  city: 'مدينة',
  housing: 'سكن',
  office: 'مكتب',
  vehicle: 'مركبة',
};

export function CostCentresPanel({
  companies,
  selectedSponsor,
  onSponsorChange,
  periodMonth,
  onPeriodChange,
  onExportExcel,
}: SharedProps) {
  const [rows, setRows] = useState<CostCentreRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getCostCentres({
        sponsorId: selectedSponsor !== 'all' ? selectedSponsor : undefined,
        from: periodMonth,
        to: periodMonth,
      });
      setRows(Array.isArray(res?.items) ? res.items : []);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل مراكز التكلفة');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [selectedSponsor, periodMonth]);

  useEffect(() => {
    void load();
  }, [load]);

  const summary = useMemo(() => {
    const debit = rows.reduce((s, r) => s + Number(r.debit || 0), 0);
    const credit = rows.reduce((s, r) => s + Number(r.credit || 0), 0);
    const closing = rows.reduce((s, r) => s + Number(r.closing || 0), 0);
    return { count: rows.length, debit, credit, closing };
  }, [rows]);

  const columns: ColumnDef<CostCentreRow>[] = [
    {
      key: 'code',
      header: 'الرمز',
      type: 'text',
      getValue: (r) => r.code,
      render: (r) => <span className="num font-mono font-bold text-[var(--accent)]">{r.code}</span>,
    },
    {
      key: 'nameAr',
      header: 'مركز التكلفة / المدينة',
      type: 'text',
      getValue: (r) => r.nameAr,
      render: (r) => <span className="font-semibold">{r.nameAr}</span>,
    },
    {
      key: 'kind',
      header: 'النوع',
      type: 'list',
      getValue: (r) => r.kind,
      labelOf: (v) => CC_KIND_AR[String(v)] || String(v),
    },
    {
      key: 'companyNameAr',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => r.companyNameAr || r.companyCode || 'عام',
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => (r.status === 1 ? 'نشط' : 'موقوف'),
      toneOf: (v) => defaultStatusTone(v === 'نشط' ? 'active' : 'inactive'),
    },
    {
      key: 'debit',
      header: 'مدين الفترة',
      type: 'number',
      numeric: true,
      getValue: (r) => Number(r.debit || 0),
      render: (r) => <span className="num font-mono">{formatSar(r.debit)}</span>,
    },
    {
      key: 'credit',
      header: 'دائن الفترة',
      type: 'number',
      numeric: true,
      getValue: (r) => Number(r.credit || 0),
      render: (r) => <span className="num font-mono">{formatSar(r.credit)}</span>,
    },
    {
      key: 'closing',
      header: 'الرصيد الختامي',
      type: 'number',
      numeric: true,
      getValue: (r) => Number(r.closing || 0),
      render: (r) => <span className="num font-bold">{formatSar(r.closing)}</span>,
    },
  ];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard label="مراكز التكلفة" value={summary.count} hint={`فترة ${periodMonth}`} icon={Building2} />
        <StatCard label="إجمالي المدين" value={formatSar(summary.debit)} icon={TrendingUp} />
        <StatCard label="إجمالي الدائن" value={formatSar(summary.credit)} icon={TrendingDown} />
        <StatCard label="صافي الأرصدة" value={formatSar(summary.closing)} icon={Scale} />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex flex-wrap items-center gap-2">
          <SearchableSelect
            id="cc-sponsor-filter"
            value={selectedSponsor}
            onChange={onSponsorChange}
            ariaLabel="تصفية الشركة"
            placeholder="كافة المنشآت"
            size="sm"
            className="w-52"
            options={[
              { value: 'all', label: 'كافة المنشآت والسجلات' },
              ...companies.map((c) => ({
                value: c.code || c.id,
                label: `${c.nameAr || c.shortNameAr || c.code || c.id}${c.code ? ` (${c.code})` : ''}`,
              })),
            ]}
          />
          <input
            type="month"
            value={periodMonth}
            onChange={(e) => onPeriodChange(e.target.value)}
            aria-label="الفترة"
            className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
          />
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => void load()}
            className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
            title="تحديث"
          >
            <RefreshCw size={15} />
          </button>
          <button
            type="button"
            onClick={() =>
              onExportExcel(
                'مراكز_التكلفة',
                ['الرمز', 'الاسم', 'النوع', 'الشركة', 'مدين', 'دائن', 'الختامي'],
                rows.map((r) => [r.code, r.nameAr, CC_KIND_AR[r.kind] || r.kind, r.companyNameAr || r.companyCode || 'عام', r.debit, r.credit, r.closing]),
                [4, 5, 6]
              )
            }
            className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold"
          >
            <Download size={14} />
            <span>تصدير إكسل</span>
          </button>
        </div>
      </div>

      {error ? (
        <ErrorState title="تعذر تحميل مراكز التكلفة" message={error} onRetry={load} />
      ) : (
        <DataTable
          tableId="accounting-cost-centres"
          columns={columns}
          data={rows}
          loading={loading}
          emptyTitle="لا توجد مراكز تكلفة"
          emptyDescription="لم تُسجّل مراكز تكلفة أو أرصدة في الفترة المختارة."
          exportFilename={`cost_centres_${periodMonth}`}
          getRowId={(r) => r.id}
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}

type AppProfitRow = {
  app: string;
  nameAr: string;
  receivableCode: string;
  revenueCodes: string[];
  receivableClosing: number;
  revenueAmount: number;
  directCostAmount: number;
  netMargin: number;
};

const PLATFORM_ACCOUNT_MAP: Array<{
  app: string;
  nameAr: string;
  receivableCode: string;
  revenueCodes: string[];
  costCodes: string[];
}> = [
  { app: 'keeta', nameAr: 'كيتا', receivableCode: '112804', revenueCodes: ['411101', '411501'], costCodes: ['512201'] },
  { app: 'hs', nameAr: 'هنقرستيشن', receivableCode: '112805', revenueCodes: ['411102', '411601'], costCodes: ['512202'] },
  { app: 'chefz', nameAr: 'ذا شيفز', receivableCode: '112807', revenueCodes: ['411103'], costCodes: ['512203'] },
  { app: 'ninja', nameAr: 'نينجا', receivableCode: '112806', revenueCodes: ['411104'], costCodes: ['512204'] },
  { app: 'amazon', nameAr: 'أمازون', receivableCode: '112808', revenueCodes: ['411105'], costCodes: ['512205'] },
  { app: 'jahez', nameAr: 'جاهز', receivableCode: '112801', revenueCodes: ['411106', '411401'], costCodes: ['512206'] },
];

export function AppProfitabilityPanel({
  trialData,
  loading,
  error,
  onReload,
  companies,
  selectedSponsor,
  onSponsorChange,
  periodMonth,
  onPeriodChange,
  onExportExcel,
}: SharedProps & {
  trialData: TrialBalanceResponse | null;
  loading: boolean;
  error: string | null;
  onReload: () => void;
}) {
  const rows = useMemo<AppProfitRow[]>(() => {
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
      const directCostAmount = p.costCodes.reduce((sum, code) => {
        const m = byCode.get(code);
        return sum + (m ? m.debit - m.credit : 0);
      }, 0);
      return {
        app: p.app,
        nameAr: p.nameAr,
        receivableCode: p.receivableCode,
        revenueCodes: p.revenueCodes,
        receivableClosing,
        revenueAmount,
        directCostAmount,
        netMargin: revenueAmount - directCostAmount,
      };
    });
  }, [trialData]);

  const totals = useMemo(() => {
    const revenue = rows.reduce((s, r) => s + r.revenueAmount, 0);
    const cost = rows.reduce((s, r) => s + r.directCostAmount, 0);
    const rec = rows.reduce((s, r) => s + r.receivableClosing, 0);
    return { revenue, cost, net: revenue - cost, rec };
  }, [rows]);

  const columns: ColumnDef<AppProfitRow>[] = [
    {
      key: 'nameAr',
      header: 'التطبيق',
      type: 'list',
      getValue: (r) => r.nameAr,
      render: (r) => <span className="font-bold text-[var(--ink)]">{r.nameAr}</span>,
    },
    {
      key: 'receivableCode',
      header: 'حساب الذمم',
      type: 'text',
      getValue: (r) => r.receivableCode,
      render: (r) => <span className="num font-mono text-xs text-[var(--muted)]">{r.receivableCode}</span>,
    },
    {
      key: 'receivableClosing',
      header: 'رصيد ذمم المنصة',
      type: 'number',
      numeric: true,
      getValue: (r) => r.receivableClosing,
      render: (r) => <span className="num">{formatSar(r.receivableClosing)}</span>,
    },
    {
      key: 'revenueAmount',
      header: 'إيرادات الفترة',
      type: 'number',
      numeric: true,
      getValue: (r) => r.revenueAmount,
      render: (r) => <span className="num font-bold text-emerald-600 dark:text-emerald-400">{formatSar(r.revenueAmount)}</span>,
    },
    {
      key: 'directCostAmount',
      header: 'التكاليف المباشرة',
      type: 'number',
      numeric: true,
      getValue: (r) => r.directCostAmount,
      render: (r) => <span className="num font-bold text-rose-600 dark:text-rose-400">{formatSar(r.directCostAmount)}</span>,
    },
    {
      key: 'netMargin',
      header: 'صافي الهامش',
      type: 'number',
      numeric: true,
      getValue: (r) => r.netMargin,
      render: (r) => <span className="num font-black">{formatSar(r.netMargin)}</span>,
    },
  ];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard label="إيرادات المنصات" value={formatSar(totals.revenue)} hint={`فترة ${periodMonth}`} icon={TrendingUp} />
        <StatCard label="التكاليف المباشرة" value={formatSar(totals.cost)} icon={TrendingDown} />
        <StatCard label="صافي هامش التطبيقات" value={formatSar(totals.net)} icon={PieChart} />
        <StatCard label="ذمم المنصات القائمة" value={formatSar(totals.rec)} icon={Scale} />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex flex-wrap items-center gap-2">
          <SearchableSelect
            id="app-prof-sponsor-filter"
            value={selectedSponsor}
            onChange={onSponsorChange}
            ariaLabel="تصفية الشركة"
            placeholder="كافة المنشآت"
            size="sm"
            className="w-52"
            options={[
              { value: 'all', label: 'كافة المنشآت والسجلات' },
              ...companies.map((c) => ({
                value: c.code || c.id,
                label: `${c.nameAr || c.shortNameAr || c.code || c.id}${c.code ? ` (${c.code})` : ''}`,
              })),
            ]}
          />
          <input
            type="month"
            value={periodMonth}
            onChange={(e) => onPeriodChange(e.target.value)}
            aria-label="الفترة"
            className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
          />
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onReload}
            className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
            title="تحديث"
          >
            <RefreshCw size={15} />
          </button>
          <button
            type="button"
            onClick={() =>
              onExportExcel(
                'ربحية_التطبيقات',
                ['التطبيق', 'حساب الذمم', 'ذمم المنصة', 'الإيرادات', 'التكاليف المباشرة', 'صافي الهامش'],
                rows.map((r) => [r.nameAr, r.receivableCode, r.receivableClosing, r.revenueAmount, r.directCostAmount, r.netMargin]),
                [1, 2, 3, 4, 5]
              )
            }
            className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold"
          >
            <Download size={14} />
            <span>تصدير إكسل</span>
          </button>
        </div>
      </div>

      {error ? (
        <ErrorState title="تعذر تحميل ربحية التطبيقات" message={error} onRetry={onReload} />
      ) : (
        <DataTable
          tableId="accounting-app-profitability"
          columns={columns}
          data={rows}
          loading={loading}
          emptyTitle="لا توجد بيانات ربحية"
          emptyDescription="لم تُرحّل قيود منصات في الفترة المختارة."
          exportFilename={`app_profitability_${periodMonth}`}
          getRowId={(r) => r.app}
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}

export function AccountingReportsPanel({
  trialData,
  incomeData,
  journals,
  periodMonth,
  onPeriodChange,
  onNavigateTab,
  onExportExcel,
}: {
  trialData: TrialBalanceResponse | null;
  incomeData: IncomeStatementResponse | null;
  journals: FinanceJournal[];
  periodMonth: string;
  onPeriodChange: (v: string) => void;
  onNavigateTab: (tab: AccountingTab) => void;
  onExportExcel: ExportFn;
}) {
  const reports = [
    {
      id: 'trial_balance' as const,
      title: 'ميزان المراجعة',
      desc: 'الأرصدة الافتتاحية وحركات الفترة والأرصدة الختامية على الدليل المستهدف.',
      metric: `${trialData?.companies?.length || 0} منشآت`,
      onExport: () => {
        const company = trialData?.companies?.[0];
        if (!company) return;
        onExportExcel(
          'ميزان_المراجعة',
          ['رمز الحساب', 'اسم الحساب', 'افتتاح مدين', 'افتتاح دائن', 'فترة مدين', 'فترة دائن', 'إغلاق مدين', 'إغلاق دائن'],
          (company.rows || []).map((r) => [r.code, r.nameAr || r.name || '', r.openingDebit || 0, r.openingCredit || 0, r.periodDebit || 0, r.periodCredit || 0, r.closingDebit || 0, r.closingCredit || 0]),
          [0, 2, 3, 4, 5, 6, 7]
        );
      },
    },
    {
      id: 'income_statement' as const,
      title: 'قائمة الدخل',
      desc: 'إجمالي الإيرادات والمصروفات وصافي النتيجة التشغيلية للفترة.',
      metric: formatSar((incomeData?.companies || []).reduce((s, c) => s + Number(c.net || 0), 0)),
      onExport: () => {
        const company = incomeData?.companies?.[0];
        if (!company) return;
        onExportExcel(
          'قائمة_الدخل',
          ['النوع', 'رمز الحساب', 'اسم البند', 'المبلغ (ر.س)'],
          [
            ...(company.revenueGroups || []).map((r) => ['إيرادات', r.code, r.nameAr, r.amount]),
            ...(company.expenseGroups || []).map((e) => ['مصروفات', e.code, e.nameAr, e.amount]),
          ],
          [1, 3]
        );
      },
    },
    {
      id: 'balance_sheet' as const,
      title: 'الميزانية العمومية',
      desc: 'ملخص الأصول والالتزامات وحقوق الملكية بنهاية الفترة.',
      metric: `نهاية ${periodMonth}`,
      onExport: () => onNavigateTab('balance_sheet'),
    },
    {
      id: 'general_ledger' as const,
      title: 'دفتر الأستاذ العام',
      desc: 'سجل تفصيلي لحركات الحسابات والقيود المرحلة.',
      metric: `${journals.length} قيد`,
      onExport: () =>
        onExportExcel(
          'سجل_القيود',
          ['رقم القيد', 'التاريخ', 'الفترة', 'البيان', 'المرجع'],
          journals.map((j) => [j.journalNumber || j.id, (j.date || j.entryDate || '').slice(0, 10), j.period || '', j.description || '', j.sourceRef || '—']),
          []
        ),
    },
    {
      id: 'cost_centres' as const,
      title: 'مراكز التكلفة والمدن',
      desc: 'متابعة أرصدة وحركات المدن ومراكز التكلفة.',
      metric: periodMonth,
      onExport: () => onNavigateTab('cost_centres'),
    },
    {
      id: 'app_profitability' as const,
      title: 'ربحية التطبيقات',
      desc: 'مقارنة إيرادات وتكاليف وذمم كل منصة تشغيلية.',
      metric: '6 تطبيقات',
      onExport: () => onNavigateTab('app_profitability'),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex items-center gap-2 text-xs font-bold">
          <FileSpreadsheet size={16} className="text-[var(--accent)]" />
          <span>مركز التقارير المالية والتصدير</span>
        </div>
        <input
          type="month"
          value={periodMonth}
          onChange={(e) => onPeriodChange(e.target.value)}
          aria-label="فترة التقارير"
          className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {reports.map((rep) => (
          <div
            key={rep.id}
            className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-col justify-between gap-4 shadow-xs"
          >
            <div className="space-y-1.5">
              <div className="flex items-center justify-between gap-2">
                <h3 className="font-bold text-sm text-[var(--ink)]">{rep.title}</h3>
                <span className="num text-[11px] font-bold px-2 py-0.5 rounded-full bg-[var(--raised)] text-[var(--accent)]">
                  {rep.metric}
                </span>
              </div>
              <p className="text-xs text-[var(--muted)] leading-relaxed">{rep.desc}</p>
            </div>
            <div className="flex items-center justify-between gap-2 pt-2 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => onNavigateTab(rep.id)}
                className="px-3 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition"
              >
                عرض التقرير
              </button>
              <button
                type="button"
                onClick={rep.onExport}
                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
              >
                <Download size={13} />
                <span>تصدير / فتح</span>
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
