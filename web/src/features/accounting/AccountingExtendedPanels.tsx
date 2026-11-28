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
  AlertTriangle,
  CheckCircle2,
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
  TrialBalanceResponse,
} from '../../lib/types';
import { DataTable, type ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { StatCard } from '../../components/ui/StatCard';
import { EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import {
  buildGeneralLedgerRows,
  buildBalanceSheetSections,
  buildAppProfitabilityRows,
  summarizeAppProfitability,
  type LedgerEntryRow,
  type BalanceSheetLine,
  type AppProfitRow,
} from './accounting-panels-logic';

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

  const ledgerRows = useMemo<LedgerEntryRow[]>(
    () =>
      buildGeneralLedgerRows({
        journals,
        periodMonth,
        accountCode,
        selectedSponsor,
        companies,
        coaAccounts,
        companyName,
      }),
    [journals, periodMonth, accountCode, selectedSponsor, companies, coaAccounts, companyName]
  );

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
  const sections = useMemo(
    () => buildBalanceSheetSections(trialData, incomeData, companyName),
    [trialData, incomeData, companyName]
  );

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
              <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 rounded-xl bg-[var(--surface)] border border-[var(--line)] text-xs font-bold">
                <div className="flex items-center gap-2">
                  <span>{sec.companyLabel}</span>
                  <span
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold ${
                      sec.isBalanced
                        ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
                        : 'bg-amber-500/15 text-amber-700 dark:text-amber-300'
                    }`}
                  >
                    {sec.isBalanced ? <CheckCircle2 size={12} /> : <AlertTriangle size={12} />}
                    <span>{sec.isBalanced ? 'متوازنة' : `غير متوازنة · فرق ${formatSar(sec.diff)}`}</span>
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() =>
                    onExportExcel(
                      `الميزانية_العمومية_${sec.key}`,
                      ['القسم', 'رمز الحساب', 'اسم البند', 'الرصيد (ر.س)'],
                      [
                        ...sec.lines.map((l) => [l.sectionLabel, l.code, l.nameAr, l.balance]),
                        [
                          'المطابقة',
                          '—',
                          sec.isBalanced
                            ? 'فرق الميزانية (متوازنة)'
                            : 'تحذير: الميزانية غير متوازنة — فرق الأصول عن الالتزامات وحقوق الملكية',
                          sec.diff,
                        ],
                      ],
                      [1, 3]
                    )
                  }
                  className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold"
                >
                  <Download size={13} />
                  <span>تصدير إكسل</span>
                </button>
              </div>
              {!sec.isBalanced && (
                <div
                  role="alert"
                  className="flex items-start gap-2.5 p-3.5 rounded-2xl border border-amber-500/30 bg-amber-500/10 text-amber-800 dark:text-amber-200 text-xs"
                >
                  <AlertTriangle size={16} className="shrink-0 mt-0.5" />
                  <div className="space-y-0.5">
                    <p className="font-bold">تحذير: الميزانية العمومية غير متوازنة</p>
                    <p>
                      يوجد فرق قدره <span className="num font-bold">{formatSar(sec.diff)}</span> بين إجمالي الأصول وإجمالي الالتزامات وحقوق الملكية؛ راجع القيود أو التعيين قبل الاعتماد أو التصدير.
                    </p>
                  </div>
                </div>
              )}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <StatCard label="إجمالي الأصول (1)" value={formatSar(sec.totalAssets)} icon={Scale} />
                <StatCard label="إجمالي الالتزامات (2)" value={formatSar(sec.totalLiabilities)} icon={TrendingDown} />
                <StatCard label="حقوق الملكية وصافي الفترة (3)" value={formatSar(sec.totalEquity)} icon={TrendingUp} />
                <StatCard
                  label="فرق الميزانية"
                  value={formatSar(sec.diff)}
                  hint={sec.isBalanced ? 'الأصول تساوي الالتزامات وحقوق الملكية' : 'تحذير: يوجد فرق عدم توازن'}
                  icon={sec.isBalanced ? CheckCircle2 : AlertTriangle}
                  trend={{ value: sec.isBalanced ? 'متوازنة' : 'غير متوازنة', isPositive: sec.isBalanced }}
                />
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
          getRowId={(r) => `${r.id}:${r.companyId || 'shared'}`}
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}

export function AppProfitabilityPanel({
  companies,
  selectedSponsor,
  onSponsorChange,
  periodMonth,
  onPeriodChange,
  onExportExcel,
}: SharedProps) {
  const [trialData, setTrialData] = useState<TrialBalanceResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getTrialBalance({
        view: 'target',
        from: periodMonth,
        to: periodMonth,
        sponsorId: selectedSponsor !== 'all' ? selectedSponsor : undefined,
      });
      setTrialData(res);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل إيرادات وذمم التطبيقات');
      setTrialData(null);
    } finally {
      setLoading(false);
    }
  }, [selectedSponsor, periodMonth]);

  useEffect(() => {
    void load();
  }, [load]);

  const rows = useMemo<AppProfitRow[]>(() => buildAppProfitabilityRows(trialData), [trialData]);

  const totals = useMemo(() => summarizeAppProfitability(rows), [rows]);

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
  ];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <StatCard label="إيرادات المنصات" value={formatSar(totals.revenue)} hint={`فترة ${periodMonth} · الدليل المستهدف`} icon={TrendingUp} />
        <StatCard label="ذمم المنصات القائمة" value={formatSar(totals.rec)} hint="أرصدة ذمم المنصات على الدليل المستهدف" icon={Scale} />
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
                'إيرادات_وذمم_التطبيقات',
                ['التطبيق', 'حساب الذمم', 'ذمم المنصة', 'الإيرادات'],
                rows.map((r) => [r.nameAr, r.receivableCode, r.receivableClosing, r.revenueAmount]),
                [1, 2, 3]
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
        <ErrorState title="تعذر تحميل إيرادات وذمم التطبيقات" message={error} onRetry={load} />
      ) : (
        <DataTable
          tableId="accounting-app-profitability"
          columns={columns}
          data={rows}
          loading={loading}
          emptyTitle="لا توجد بيانات للمنصات"
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
      desc: 'مقارنة إيرادات وذمم كل منصة تشغيلية على الدليل المستهدف.',
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
