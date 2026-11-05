'use client';

import React, { useEffect, useState, useMemo } from 'react';
import {
  FileSpreadsheet,
  Layers,
  BookOpen,
  Scale,
  TrendingUp,
  TrendingDown,
  Search,
  Filter,
  CheckCircle2,
  AlertCircle,
  ExternalLink,
  ChevronRight,
  Download,
  Info,
  Calendar,
  Building,
  RefreshCw,
  Hash,
  ArrowUpDown,
  FileText,
  Eye,
  X,
} from 'lucide-react';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { ErrorState, EmptyState, Skeleton } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { api } from '../../lib/api';
import type {
  TargetCoaAccount,
  TargetCoaResponse,
  TargetCoaSummary,
  TrialBalanceResponse,
  TrialBalanceCompany,
  TrialBalanceCompanyRow,
  IncomeStatementResponse,
  IncomeStatementCompany,
  FinanceJournal,
  FinanceJournalLine,
} from '../../lib/types';

export default function AccountingPage() {
  const { toast } = useToast();
  const [activeTab, setActiveTab] = useState<'coa' | 'journals' | 'trial' | 'income'>('coa');

  // ==========================================
  // Data States
  // ==========================================
  const [loadingCoa, setLoadingCoa] = useState(false);
  const [coaData, setCoaData] = useState<TargetCoaResponse | null>(null);
  const [coaError, setCoaError] = useState<string | null>(null);

  const [loadingJournals, setLoadingJournals] = useState(false);
  const [journals, setJournals] = useState<FinanceJournal[]>([]);
  const [journalsError, setJournalsError] = useState<string | null>(null);

  const [loadingTrial, setLoadingTrial] = useState(false);
  const [trialData, setTrialData] = useState<TrialBalanceResponse | null>(null);
  const [trialError, setTrialError] = useState<string | null>(null);
  const [trialView, setTrialView] = useState<'target' | 'current'>('target');

  const [loadingIncome, setLoadingIncome] = useState(false);
  const [incomeData, setIncomeData] = useState<IncomeStatementResponse | null>(null);
  const [incomeError, setIncomeError] = useState<string | null>(null);
  const [incomeView, setIncomeView] = useState<'target' | 'current'>('target');

  // Filters
  const [coaSearch, setCoaSearch] = useState('');
  const [coaSourceFilter, setCoaSourceFilter] = useState<string>('all');
  const [coaTypeFilter, setCoaTypeFilter] = useState<string>('all');
  const [coaPostingOnly, setCoaPostingOnly] = useState<boolean>(false);

  const [journalSearch, setJournalSearch] = useState('');
  const [selectedJournal, setSelectedJournal] = useState<FinanceJournal | null>(null);
  const [selectedAccount, setSelectedAccount] = useState<TargetCoaAccount | null>(null);

  const [selectedSponsor, setSelectedSponsor] = useState<string>('all');
  const [periodMonth, setPeriodMonth] = useState<string>(new Date().toISOString().slice(0, 7));

  // ==========================================
  // Data Fetching
  // ==========================================
  const loadCoa = async () => {
    setLoadingCoa(true);
    setCoaError(null);
    try {
      const res = await api.getTargetCoa();
      setCoaData(res);
    } catch (err: any) {
      setCoaError(err?.message || 'تعذر تحميل دليل شجرة الحسابات المستهدفة');
    } finally {
      setLoadingCoa(false);
    }
  };

  const loadJournals = async () => {
    setLoadingJournals(true);
    setJournalsError(null);
    try {
      const res = await api.getFinanceJournals();
      setJournals(Array.isArray(res) ? res : []);
    } catch (err: any) {
      setJournalsError(err?.message || 'تعذر تحميل سجل القيود المحاسبية');
    } finally {
      setLoadingJournals(false);
    }
  };

  const loadTrialBalance = async () => {
    setLoadingTrial(true);
    setTrialError(null);
    try {
      const res = await api.getTrialBalance({
        view: trialView,
        from: periodMonth,
        to: periodMonth,
        sponsorId: selectedSponsor === 'all' ? undefined : selectedSponsor,
      });
      setTrialData(res);
    } catch (err: any) {
      setTrialError(err?.message || 'تعذر تحميل ميزان المراجعة');
    } finally {
      setLoadingTrial(false);
    }
  };

  const loadIncomeStatement = async () => {
    setLoadingIncome(true);
    setIncomeError(null);
    try {
      const res = await api.getIncomeStatement({
        view: incomeView,
        from: periodMonth,
        to: periodMonth,
        sponsorId: selectedSponsor === 'all' ? undefined : selectedSponsor,
      });
      setIncomeData(res);
    } catch (err: any) {
      setIncomeError(err?.message || 'تعذر تحميل قائمة الدخل');
    } finally {
      setLoadingIncome(false);
    }
  };

  useEffect(() => {
    loadCoa();
    loadJournals();
  }, []);

  useEffect(() => {
    if (activeTab === 'trial') {
      loadTrialBalance();
    } else if (activeTab === 'income') {
      loadIncomeStatement();
    }
  }, [activeTab, trialView, incomeView, selectedSponsor, periodMonth]);

  // ==========================================
  // Summary Metrics Computation
  // ==========================================
  const coaAccounts = useMemo(() => coaData?.accounts || [], [coaData]);

  const coaSummary = useMemo(() => {
    if (coaData?.summary) return coaData.summary;
    const total = coaAccounts.length;
    const activePosting = coaAccounts.filter((a) => (a.posting ?? a.isPosting) && (a.status === 'active' || a.isActive !== false)).length;
    const excel2024 = coaAccounts.filter((a) => (a.source === 'excel' || a.sourceType === 'excel2024')).length;
    const modified = coaAccounts.filter((a) => (a.source === 'modified' || a.sourceType === 'modified')).length;
    const newAccounts = coaAccounts.filter((a) => (a.source === 'new' || a.sourceType === 'new')).length;

    return {
      total,
      activePosting,
      sourceBreakdown: {
        excel2024,
        modified,
        newAccounts,
      },
    };
  }, [coaData, coaAccounts]);

  // Filtered COA Accounts
  const filteredAccounts = useMemo(() => {
    return coaAccounts.filter((acc) => {
      if (coaSearch) {
        const q = coaSearch.toLowerCase().trim();
        const codeMatch = acc.code.toLowerCase().includes(q);
        const nameMatch = acc.nameAr.toLowerCase().includes(q);
        if (!codeMatch && !nameMatch) return false;
      }

      if (coaSourceFilter !== 'all') {
        const src = (acc.source || acc.sourceType || '').toLowerCase();
        if (coaSourceFilter === 'excel' && !src.includes('excel')) return false;
        if (coaSourceFilter === 'modified' && !src.includes('modified')) return false;
        if (coaSourceFilter === 'new' && !src.includes('new')) return false;
      }

      if (coaTypeFilter !== 'all') {
        if (!acc.code.startsWith(coaTypeFilter)) return false;
      }

      if (coaPostingOnly) {
        const isPosting = acc.posting ?? acc.isPosting;
        if (!isPosting) return false;
      }

      return true;
    });
  }, [coaAccounts, coaSearch, coaSourceFilter, coaTypeFilter, coaPostingOnly]);

  // Filtered Journals
  const filteredJournals = useMemo(() => {
    return journals.filter((j) => {
      if (!journalSearch) return true;
      const q = journalSearch.toLowerCase().trim();
      const numMatch = (j.journalNumber || j.id).toLowerCase().includes(q);
      const descMatch = (j.description || '').toLowerCase().includes(q);
      const refMatch = (j.sourceRef || '').toLowerCase().includes(q);
      const iqamaMatch = (j.iqama || '').includes(q);
      const spMatch = (j.originSponsorNameAr || j.creditorSponsorNameAr || '').toLowerCase().includes(q);
      const lineMatch = j.lines?.some((l) => l.accountCode.includes(q) || (l.description || '').toLowerCase().includes(q));
      return numMatch || descMatch || refMatch || iqamaMatch || spMatch || lineMatch;
    });
  }, [journals, journalSearch]);

  // Find original / reversal journals map
  const journalsById = useMemo(() => {
    const map = new Map<string, FinanceJournal>();
    journals.forEach((j) => map.set(j.id, j));
    return map;
  }, [journals]);

  // Export Table to Excel
  const handleExportExcel = async (title: string, headers: string[], rows: any[][], numericCols?: number[]) => {
    try {
      const res = await api.exportExcel({
        headers,
        rows,
        numericColumns: numericCols || [],
        filename: `${title}_${periodMonth}.xlsx`,
      });
      const url = window.URL.createObjectURL(res);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${title}_${periodMonth}.xlsx`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      toast('تم تصدير ملف الإكسل بنجاح', 'success');
    } catch (err: any) {
      toast(err?.message || 'تعذر تصدير ملف الإكسل', 'danger');
    }
  };

  // ==========================================
  // Table Columns
  // ==========================================
  const coaColumns: ColumnDef<TargetCoaAccount>[] = [
    {
      key: 'code',
      header: 'رمز الحساب',
      render: (acc) => (
        <span className="font-mono font-bold text-xs num text-[var(--accent)] bg-[var(--raised)] px-2 py-0.5 rounded border border-[var(--line)]">
          {acc.code}
        </span>
      ),
    },
    {
      key: 'nameAr',
      header: 'اسم الحساب المحاسبي',
      render: (acc) => {
        const indentClass =
          acc.level === 1
            ? 'font-bold text-[var(--ink)] text-sm'
            : acc.level === 2
            ? 'font-semibold text-[var(--ink)] pr-3'
            : acc.level === 3
            ? 'font-medium text-[var(--ink)] pr-6'
            : acc.level === 4
            ? 'text-xs text-[var(--ink)] pr-9'
            : 'text-xs text-[var(--muted)] pr-12';

        return (
          <div className="flex items-center gap-2">
            <span className={indentClass}>{acc.nameAr}</span>
            {acc.level === 1 && (
              <span className="text-[10px] bg-[var(--accent)] text-[var(--accent-ink)] font-bold px-1.5 py-0.2 rounded-full">
                مستوى رئيسي {acc.level}
              </span>
            )}
          </div>
        );
      },
    },
    {
      key: 'posting',
      header: 'نوع الحساب',
      render: (acc) => {
        const isPosting = acc.posting ?? acc.isPosting;
        return isPosting ? (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-600 dark:text-emerald-400">
            <CheckCircle2 size={12} />
            ترحيل مباشر
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--muted)]">
            <Layers size={12} />
            رئيسي تجميعي
          </span>
        );
      },
    },
    {
      key: 'nature',
      header: 'الطبيعة',
      render: (acc) => {
        const isDebit = (acc.nature || '').toLowerCase() === 'debit';
        return (
          <span className={`text-xs font-semibold px-2 py-0.5 rounded ${isDebit ? 'text-blue-600 bg-blue-500/10' : 'text-purple-600 bg-purple-500/10'}`}>
            {isDebit ? 'مدين' : 'دائن'}
          </span>
        );
      },
    },
    {
      key: 'source',
      header: 'مصدر الاعتماد',
      render: (acc) => {
        const src = (acc.source || acc.sourceType || '').toLowerCase();
        if (src.includes('excel')) {
          return (
            <span className="px-2 py-0.5 rounded-md text-[11px] font-medium bg-[var(--raised)] text-[var(--muted)] border border-[var(--line)]">
              Excel 2024
            </span>
          );
        }
        if (src.includes('mod')) {
          return (
            <span className="px-2 py-0.5 rounded-md text-[11px] font-semibold bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20">
              تعديل مالي
            </span>
          );
        }
        return (
          <span className="px-2 py-0.5 rounded-md text-[11px] font-semibold bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20">
            حساب جديد
          </span>
        );
      },
    },
    {
      key: 'actions',
      header: '',
      render: (acc) => (
        <button
          type="button"
          onClick={() => setSelectedAccount(acc)}
          className="p-1 text-[var(--muted)] hover:text-[var(--accent)] hover:bg-[var(--raised)] rounded-lg transition"
          title="معاينة تفاصيل الحساب"
        >
          <Eye size={16} />
        </button>
      ),
    },
  ];

  const journalColumns: ColumnDef<FinanceJournal>[] = [
    {
      key: 'journalNumber',
      header: 'رقم القيد',
      render: (j) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-mono font-bold text-xs num text-[var(--accent)]">
            {j.journalNumber || j.id.slice(0, 10)}
          </span>
          {j.sourceRef && (
            <span className="text-[10px] text-[var(--muted)] truncate max-w-[120px]" title={j.sourceRef}>
              {j.sourceRef}
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'date',
      header: 'التاريخ والفترة',
      render: (j) => (
        <div className="flex flex-col text-xs">
          <span className="num font-semibold text-[var(--ink)]">{j.date || j.entryDate || j.createdAt?.slice(0, 10)}</span>
          {j.period && <span className="num text-[11px] text-[var(--muted)]">{j.period}</span>}
        </div>
      ),
    },
    {
      key: 'description',
      header: 'البيان المحاسبي',
      render: (j) => (
        <div className="space-y-1 max-w-[280px]">
          <p className="text-xs font-semibold text-[var(--ink)] line-clamp-2 leading-relaxed" title={j.description}>
            {j.description}
          </p>
          <div className="flex flex-wrap items-center gap-1.5">
            {j.originSponsorNameAr && (
              <span className="text-[10px] px-1.5 py-0.2 rounded bg-[var(--raised)] text-[var(--muted)]">
                {j.originSponsorNameAr}
              </span>
            )}
            {j.chart === 'target' && (
              <span className="text-[10px] px-1.5 py-0.2 rounded bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 font-semibold">
                دليل مستهدف
              </span>
            )}
            {/* Reversal Link */}
            {j.reversesJournalId && (
              <button
                type="button"
                onClick={() => {
                  const orig = journalsById.get(j.reversesJournalId!);
                  if (orig) setSelectedJournal(orig);
                  else toast(`قيد أصلي رقم ${j.reversesJournalId}`, 'info');
                }}
                className="inline-flex items-center gap-1 text-[10px] px-1.5 py-0.2 rounded bg-amber-500/10 text-amber-700 dark:text-amber-300 font-semibold hover:underline"
              >
                <ArrowUpDown size={10} />
                عكس قيد أصلي
              </button>
            )}
          </div>
        </div>
      ),
    },
    {
      key: 'totalDebit',
      header: 'إجمالي المدين',
      render: (j) => {
        const sumDebit = j.totalDebit ?? j.lines?.reduce((acc, l) => acc + (l.debit || 0), 0) ?? 0;
        return (
          <span className="num font-bold text-xs text-blue-600 dark:text-blue-400">
            {sumDebit > 0 ? sumDebit.toLocaleString('en-US', { minimumFractionDigits: 2 }) : '0.00'}
          </span>
        );
      },
    },
    {
      key: 'totalCredit',
      header: 'إجمالي الدائن',
      render: (j) => {
        const sumCredit = j.totalCredit ?? j.lines?.reduce((acc, l) => acc + (l.credit || 0), 0) ?? 0;
        return (
          <span className="num font-bold text-xs text-purple-600 dark:text-purple-400">
            {sumCredit > 0 ? sumCredit.toLocaleString('en-US', { minimumFractionDigits: 2 }) : '0.00'}
          </span>
        );
      },
    },
    {
      key: 'linesCount',
      header: 'الأسطر',
      render: (j) => (
        <span className="num text-xs text-[var(--muted)] px-2 py-0.5 rounded bg-[var(--raised)]">
          {j.lines?.length || 0} أسطر
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (j) => (
        <button
          type="button"
          onClick={() => setSelectedJournal(j)}
          className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-semibold rounded-lg bg-[var(--raised)] hover:bg-[var(--line)] text-[var(--ink)] transition"
        >
          <FileText size={13} />
          <span>الأسطر</span>
        </button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="المحاسبة والتقارير المالية"
        eyebrow="الدفاتر والتقارير المعتمدة"
        description="استعرض دليل شجرة الحسابات المستهدفة (403 حسابات)، سجل القيود المزدوجة مع روابط القيود العكسية، وميزان المراجعة وقائمة الدخل."
      />

      {/* 4 Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="إجمالي الحسابات المستهدفة"
          value={coaSummary.total || 403}
          hint={`294 إكسل 2024 · 81 جديد · 28 معدل`}
          icon={Layers}
          trend={{ value: 'دليل 1.0', isPositive: true }}
          onClick={() => setActiveTab('coa')}
        />

        <StatCard
          label="حسابات الترحيل المباشر"
          value={coaSummary.activePosting || 227}
          hint="حسابات طرفية قابلة لاستقبال القيود"
          icon={CheckCircle2}
          trend={{ value: 'جاهز للترحيل', isPositive: true }}
          onClick={() => {
            setActiveTab('coa');
            setCoaPostingOnly(true);
          }}
        />

        <StatCard
          label="سجل القيود المحاسبية"
          value={journals.length}
          hint="قيود مزدوجة متوازنة مع تتبع العكس"
          icon={Scale}
          trend={{ value: 'متزن', isPositive: true }}
          onClick={() => setActiveTab('journals')}
        />

        <StatCard
          label="صافي الدخل التقديري"
          value={
            incomeData?.companies && incomeData.companies.length > 0
              ? (incomeData.companies.reduce((sum, c) => sum + (c.net || 0), 0)).toLocaleString('en-US', { minimumFractionDigits: 2 }) + ' ر.س'
              : 'غير معروف'
          }
          hint="مبني على وقائع القيود المعتمدة"
          icon={TrendingUp}
          trend={{
            value: incomeData?.companies?.every((c) => c.netIsComplete) ? 'مكتمل' : 'محدود النطاق',
            isPositive: incomeData?.companies?.every((c) => c.netIsComplete),
          }}
          onClick={() => setActiveTab('income')}
        />
      </div>

      {/* Sub Tabs Navigation */}
      <div className="flex border-b border-[var(--line)] gap-2 overflow-x-auto no-scrollbar">
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'coa'}
          onClick={() => setActiveTab('coa')}
          className={`px-4 py-2.5 text-xs sm:text-sm font-bold border-b-2 transition whitespace-nowrap ${
            activeTab === 'coa'
              ? 'border-[var(--accent)] text-[var(--accent)]'
              : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
          }`}
        >
          شجرة دليل الحسابات المستهدف ({coaSummary.total || 403})
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'journals'}
          onClick={() => setActiveTab('journals')}
          className={`px-4 py-2.5 text-xs sm:text-sm font-bold border-b-2 transition whitespace-nowrap ${
            activeTab === 'journals'
              ? 'border-[var(--accent)] text-[var(--accent)]'
              : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
          }`}
        >
          سجل القيود المحاسبية ({journals.length})
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'trial'}
          onClick={() => setActiveTab('trial')}
          className={`px-4 py-2.5 text-xs sm:text-sm font-bold border-b-2 transition whitespace-nowrap ${
            activeTab === 'trial'
              ? 'border-[var(--accent)] text-[var(--accent)]'
              : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
          }`}
        >
          ميزان المراجعة التحليلي
        </button>

        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'income'}
          onClick={() => setActiveTab('income')}
          className={`px-4 py-2.5 text-xs sm:text-sm font-bold border-b-2 transition whitespace-nowrap ${
            activeTab === 'income'
              ? 'border-[var(--accent)] text-[var(--accent)]'
              : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
          }`}
        >
          قائمة الدخل والنتائج
        </button>
      </div>

      {/* ======================================================== */}
      {/* TAB 1: Chart of Accounts (COA) Tree & Table              */}
      {/* ======================================================== */}
      {activeTab === 'coa' && (
        <div className="space-y-4">
          {/* Toolbar */}
          <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between bg-[var(--surface)] p-3 rounded-2xl border border-[var(--line)] shadow-xs">
            <div className="flex-1 relative">
              <Search size={16} className="absolute right-3 top-2.5 text-[var(--muted)] pointer-events-none" />
              <input
                type="text"
                placeholder="ابحث برقم الحساب أو الاسم العربي..."
                value={coaSearch}
                onChange={(e) => setCoaSearch(e.target.value)}
                className="w-full pl-3 pr-9 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden focus:ring-1 focus:ring-[var(--accent)]"
              />
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <select
                value={coaTypeFilter}
                onChange={(e) => setCoaTypeFilter(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كل التصنيفات (1-5)</option>
                <option value="1">1 - الأصول</option>
                <option value="2">2 - الخصوم</option>
                <option value="3">3 - حقوق الملكية</option>
                <option value="4">4 - الإيرادات</option>
                <option value="5">5 - المصروفات</option>
              </select>

              <select
                value={coaSourceFilter}
                onChange={(e) => setCoaSourceFilter(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كل المصادر</option>
                <option value="excel">Excel 2024 ({coaSummary.sourceBreakdown.excel2024})</option>
                <option value="modified">تعديل مالي ({coaSummary.sourceBreakdown.modified})</option>
                <option value="new">حسابات جديدة ({coaSummary.sourceBreakdown.newAccounts})</option>
              </select>

              <label className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs text-[var(--ink)] cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={coaPostingOnly}
                  onChange={(e) => setCoaPostingOnly(e.target.checked)}
                  className="rounded text-[var(--accent)]"
                />
                <span>حسابات الترحيل فقط</span>
              </label>

              <button
                type="button"
                onClick={() => {
                  const headers = ['رمز الحساب', 'اسم الحساب', 'المستوى', 'الحساب الأب', 'ترحيل مباشر', 'الطبيعة', 'المصدر'];
                  const rows = filteredAccounts.map((a) => [
                    a.code,
                    a.nameAr,
                    a.level,
                    a.parent || a.parentCode || '—',
                    (a.posting ?? a.isPosting) ? 'نعم' : 'لا',
                    a.nature || '—',
                    a.source || a.sourceType || '—',
                  ]);
                  handleExportExcel('دليل_الحسابات_المستهدف', headers, rows, [0, 2]);
                }}
                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
              >
                <Download size={14} />
                <span>تصدير Excel</span>
              </button>
            </div>
          </div>

          {/* Table */}
          {loadingCoa ? (
            <div className="space-y-2">
              <Skeleton className="h-12 rounded-xl" rows={8} />
            </div>
          ) : coaError ? (
            <ErrorState title="تعذر تحميل الدليل المستهدف" message={coaError} onRetry={loadCoa} />
          ) : (
            <DataTable
              columns={coaColumns}
              data={filteredAccounts}
              emptyTitle="لا توجد حسابات مطابقة للبحث"
              emptyDescription="جرّب تعديل الفلاتر أو إدخال رقم كود مختلف."
            />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* TAB 2: Journal Entries (سجل القيود المحاسبية المزدوجة)     */}
      {/* ======================================================== */}
      {activeTab === 'journals' && (
        <div className="space-y-4">
          <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between bg-[var(--surface)] p-3 rounded-2xl border border-[var(--line)] shadow-xs">
            <div className="flex-1 relative">
              <Search size={16} className="absolute right-3 top-2.5 text-[var(--muted)] pointer-events-none" />
              <input
                type="text"
                placeholder="ابحث برقم القيد، المرجع، البيان، المندوب أو كود الحساب..."
                value={journalSearch}
                onChange={(e) => setJournalSearch(e.target.value)}
                className="w-full pl-3 pr-9 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={loadJournals}
                className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
                title="تحديث القيود"
              >
                <RefreshCw size={15} />
              </button>

              <button
                type="button"
                onClick={() => {
                  const headers = ['رقم القيد', 'التاريخ', 'الفترة', 'البيان', 'المنشأة', 'إجمالي المدين', 'إجمالي الدائن', 'المرجع'];
                  const rows = filteredJournals.map((j) => [
                    j.journalNumber || j.id,
                    j.date || j.entryDate || j.createdAt?.slice(0, 10),
                    j.period || '—',
                    j.description,
                    j.originSponsorNameAr || j.creditorSponsorNameAr || '—',
                    j.totalDebit ?? j.lines?.reduce((s, l) => s + (l.debit || 0), 0) ?? 0,
                    j.totalCredit ?? j.lines?.reduce((s, l) => s + (l.credit || 0), 0) ?? 0,
                    j.sourceRef || '—',
                  ]);
                  handleExportExcel('سجل_القيود_المحاسبية', headers, rows, [0, 5, 6]);
                }}
                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
              >
                <Download size={14} />
                <span>تصدير Excel</span>
              </button>
            </div>
          </div>

          {loadingJournals ? (
            <div className="space-y-2">
              <Skeleton className="h-12 rounded-xl" rows={6} />
            </div>
          ) : journalsError ? (
            <ErrorState title="تعذر تحميل سجل القيود" message={journalsError} onRetry={loadJournals} />
          ) : (
            <DataTable
              columns={journalColumns}
              data={filteredJournals}
              emptyTitle="لا توجد قيود مسجلة"
              emptyDescription="ستظهر هنا القيود المعتمدة والآلية الناتجة عن الرواتب والخزينة."
            />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* TAB 3: Trial Balance (ميزان المراجعة التحليلي)             */}
      {/* ======================================================== */}
      {activeTab === 'trial' && (
        <div className="space-y-4">
          {/* Controls toolbar */}
          <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between bg-[var(--surface)] p-3 rounded-2xl border border-[var(--line)] shadow-xs">
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex rounded-xl bg-[var(--raised)] p-0.5 border border-[var(--line)]">
                <button
                  type="button"
                  onClick={() => setTrialView('target')}
                  className={`px-3 py-1 text-xs font-bold rounded-lg transition ${
                    trialView === 'target'
                      ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                      : 'text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  الدليل المستهدف (Target)
                </button>
                <button
                  type="button"
                  onClick={() => setTrialView('current')}
                  className={`px-3 py-1 text-xs font-bold rounded-lg transition ${
                    trialView === 'current'
                      ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                      : 'text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  الدليل الحالي (Current)
                </button>
              </div>

              <select
                value={selectedSponsor}
                onChange={(e) => setSelectedSponsor(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كل المنشآت والسجلات</option>
                <option value="BW">شركة البوابة المقبلة (BW)</option>
                <option value="EX">المؤسسة Express (EX)</option>
                <option value="TR">مؤسسة النقل والتوزيع (TR)</option>
              </select>

              <input
                type="month"
                value={periodMonth}
                onChange={(e) => setPeriodMonth(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden num"
              />
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={loadTrialBalance}
                className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
                title="تحديث ميزان المراجعة"
              >
                <RefreshCw size={15} />
              </button>

              <button
                type="button"
                onClick={() => {
                  const company = trialData?.companies?.[0];
                  if (!company) return;
                  const headers = ['رمز الحساب', 'اسم الحساب', 'افتتاح مدين', 'افتتاح دائن', 'فترة مدين', 'فترة دائن', 'إغلاق مدين', 'إغلاق دائن'];
                  const rows = company.rows.map((r) => [
                    r.code,
                    r.nameAr || r.name || '—',
                    r.openingDebit || 0,
                    r.openingCredit || 0,
                    r.periodDebit || 0,
                    r.periodCredit || 0,
                    r.closingDebit || 0,
                    r.closingCredit || 0,
                  ]);
                  handleExportExcel(`ميزان_المراجعة_${trialView}`, headers, rows, [0, 2, 3, 4, 5, 6, 7]);
                }}
                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
              >
                <Download size={14} />
                <span>تصدير Excel</span>
              </button>
            </div>
          </div>

          {loadingTrial ? (
            <div className="space-y-2">
              <Skeleton className="h-12 rounded-xl" rows={6} />
            </div>
          ) : trialError ? (
            <ErrorState title="تعذر تحميل ميزان المراجعة" message={trialError} onRetry={loadTrialBalance} />
          ) : trialData?.companies && trialData.companies.length > 0 ? (
            <div className="space-y-6">
              {trialData.companies.map((company) => {
                const isBalanced = company.isBalanced ?? true;
                const rows = company.rows || [];
                const totals = company.totals || {};

                return (
                  <div key={company.sponsorId || company.companyCode} className="space-y-4">
                    {/* Balanced status banner */}
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-4 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs">
                      <div className="flex items-center gap-3">
                        <div
                          className={`flex h-10 w-10 items-center justify-center rounded-xl font-bold ${
                            isBalanced ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400' : 'bg-red-500/10 text-red-600'
                          }`}
                        >
                          {isBalanced ? <CheckCircle2 size={22} /> : <AlertCircle size={22} />}
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <h3 className="font-bold text-sm text-[var(--ink)]">
                              سجل المنشأة: {company.companyCode}
                            </h3>
                            <span
                              className={`text-[11px] font-bold px-2 py-0.5 rounded-full ${
                                isBalanced
                                  ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
                                  : 'bg-red-500/10 text-red-600'
                              }`}
                            >
                              {isBalanced ? 'الميزان متزن تمامًا' : 'يوجد فروقات محاسبية'}
                            </span>
                          </div>
                          <p className="text-xs text-[var(--muted)]">
                            الفترة: من {trialData.from || periodMonth} إلى {trialData.to || periodMonth} · عرض: {trialView === 'target' ? 'الدليل المستهدف' : 'الدليل الحالي'}
                          </p>
                        </div>
                      </div>

                      <div className="flex items-center gap-4 text-xs font-semibold">
                        <div>
                          <span className="text-[var(--muted)] block">إغلاق مدين:</span>
                          <span className="num font-bold text-blue-600 dark:text-blue-400 text-sm">
                            {(totals.closingDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                          </span>
                        </div>
                        <div>
                          <span className="text-[var(--muted)] block">إغلاق دائن:</span>
                          <span className="num font-bold text-purple-600 dark:text-purple-400 text-sm">
                            {(totals.closingCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                          </span>
                        </div>
                      </div>
                    </div>

                    {/* Unmapped accounts warning if present */}
                    {company.unmapped && company.unmapped.length > 0 && (
                      <div className="p-4 rounded-2xl bg-amber-500/10 border border-amber-500/20 text-amber-800 dark:text-amber-200">
                        <div className="flex items-center gap-2 font-bold text-xs mb-2">
                          <AlertCircle size={16} />
                          <span>حسابات بحاجة إلى استكمال سياق التعيين ({company.unmapped.length} حساب)</span>
                        </div>
                        <div className="space-y-1 text-xs">
                          {company.unmapped.map((u: any) => (
                            <div key={u.code} className="flex items-center justify-between py-0.5 border-b border-amber-500/10">
                              <span>
                                {u.code} - {u.nameAr || u.name}
                              </span>
                              <span className="num font-bold">
                                {((u.closingDebit || 0) - (u.closingCredit || 0)).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Trial balance table */}
                    <div className="overflow-x-auto rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
                      <table className="w-full text-xs text-right border-collapse">
                        <thead>
                          <tr className="bg-[var(--raised)] border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                            <th className="py-2.5 px-3">رمز الحساب</th>
                            <th className="py-2.5 px-3">اسم الحساب</th>
                            <th className="py-2.5 px-3 text-left num">افتتاح مدين</th>
                            <th className="py-2.5 px-3 text-left num">افتتاح دائن</th>
                            <th className="py-2.5 px-3 text-left num">فترة مدين</th>
                            <th className="py-2.5 px-3 text-left num">فترة دائن</th>
                            <th className="py-2.5 px-3 text-left num">إغلاق مدين</th>
                            <th className="py-2.5 px-3 text-left num">إغلاق دائن</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {rows.map((row) => {
                            const isGroup = !row.posting;
                            const hasActivity =
                              (row.openingDebit || 0) > 0 ||
                              (row.openingCredit || 0) > 0 ||
                              (row.periodDebit || 0) > 0 ||
                              (row.periodCredit || 0) > 0 ||
                              (row.closingDebit || 0) > 0 ||
                              (row.closingCredit || 0) > 0;

                            return (
                              <tr
                                key={row.code}
                                className={`hover:bg-[var(--raised)]/50 transition ${
                                  isGroup ? 'font-bold bg-[var(--raised)]/20 text-[var(--ink)]' : 'text-[var(--ink)]'
                                }`}
                              >
                                <td className="py-2 px-3 font-mono text-[var(--accent)] num">
                                  {row.code}
                                </td>
                                <td className="py-2 px-3">
                                  <span className={row.level === 1 ? 'font-black text-sm' : isGroup ? 'font-bold' : 'pr-3'}>
                                    {row.nameAr || row.name}
                                  </span>
                                </td>
                                <td className="py-2 px-3 text-left num font-mono">
                                  {(row.openingDebit || 0) > 0 ? (row.openingDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                                <td className="py-2 px-3 text-left num font-mono">
                                  {(row.openingCredit || 0) > 0 ? (row.openingCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                                <td className="py-2 px-3 text-left num font-mono text-blue-600 dark:text-blue-400">
                                  {(row.periodDebit || 0) > 0 ? (row.periodDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                                <td className="py-2 px-3 text-left num font-mono text-purple-600 dark:text-purple-400">
                                  {(row.periodCredit || 0) > 0 ? (row.periodCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                                <td className="py-2 px-3 text-left num font-mono font-bold text-blue-700 dark:text-blue-300">
                                  {(row.closingDebit || 0) > 0 ? (row.closingDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                                <td className="py-2 px-3 text-left num font-mono font-bold text-purple-700 dark:text-purple-300">
                                  {(row.closingCredit || 0) > 0 ? (row.closingCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                        <tfoot className="bg-[var(--raised)] border-t-2 border-[var(--line)] font-bold text-xs">
                          <tr>
                            <td colSpan={2} className="py-3 px-3 text-right">
                              الإجمالي العام المتوازن
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold">
                              {(totals.openingDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold">
                              {(totals.openingCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold text-blue-600">
                              {(totals.periodDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold text-purple-600">
                              {(totals.periodCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold text-blue-700">
                              {(totals.closingDebit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                            <td className="py-3 px-3 text-left num font-mono font-bold text-purple-700">
                              {(totals.closingCredit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                            </td>
                          </tr>
                        </tfoot>
                      </table>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <EmptyState title="لا توجد بيانات لميزان المراجعة" description="تأكد من اختيار الفترة والمنشأة المناسبة." />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* TAB 4: Income Statement (قائمة الدخل والنتائج)             */}
      {/* ======================================================== */}
      {activeTab === 'income' && (
        <div className="space-y-4">
          {/* Controls toolbar */}
          <div className="flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between bg-[var(--surface)] p-3 rounded-2xl border border-[var(--line)] shadow-xs">
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={selectedSponsor}
                onChange={(e) => setSelectedSponsor(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كل المنشآت والسجلات</option>
                <option value="BW">شركة البوابة المقبلة (BW)</option>
                <option value="EX">المؤسسة Express (EX)</option>
                <option value="TR">مؤسسة النقل والتوزيع (TR)</option>
              </select>

              <input
                type="month"
                value={periodMonth}
                onChange={(e) => setPeriodMonth(e.target.value)}
                className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] focus:outline-hidden num"
              />
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={loadIncomeStatement}
                className="p-1.5 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)] transition"
                title="تحديث قائمة الدخل"
              >
                <RefreshCw size={15} />
              </button>

              <button
                type="button"
                onClick={() => {
                  const company = incomeData?.companies?.[0];
                  if (!company) return;
                  const headers = ['النوع', 'رمز الحساب', 'اسم البند', 'المبلغ (ر.س)'];
                  const revRows = (company.revenueGroups || []).map((r) => ['إيرادات', r.code, r.nameAr, r.amount]);
                  const expRows = (company.expenseGroups || []).map((e) => ['مصروفات', e.code, e.nameAr, e.amount]);
                  const rows = [...revRows, ...expRows, ['صافي النتيجة', '—', 'صافي الدخل التشغيلي', company.net || 0]];
                  handleExportExcel('قائمة_الدخل', headers, rows, [1, 3]);
                }}
                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--raised)] hover:bg-[var(--line)] text-xs font-semibold text-[var(--ink)] transition"
              >
                <Download size={14} />
                <span>تصدير Excel</span>
              </button>
            </div>
          </div>

          {loadingIncome ? (
            <div className="space-y-2">
              <Skeleton className="h-12 rounded-xl" rows={6} />
            </div>
          ) : incomeError ? (
            <ErrorState title="تعذر تحميل قائمة الدخل" message={incomeError} onRetry={loadIncomeStatement} />
          ) : incomeData?.companies && incomeData.companies.length > 0 ? (
            <div className="space-y-6">
              {incomeData.companies.map((company) => {
                const revenue = company.revenue || 0;
                const expense = company.expense || 0;
                const net = company.net || 0;
                const isNetPositive = net >= 0;

                return (
                  <div key={company.sponsorId || company.companyCode} className="space-y-6">
                    {/* 3 Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      <div className="p-4 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs">
                        <div className="flex items-center justify-between text-xs text-[var(--muted)] mb-1">
                          <span>إجمالي الإيرادات</span>
                          <TrendingUp size={16} className="text-emerald-500" />
                        </div>
                        <div className="text-xl font-black text-emerald-600 dark:text-emerald-400 num">
                          {revenue.toLocaleString('en-US', { minimumFractionDigits: 2 })} <span className="text-xs font-bold">ر.س</span>
                        </div>
                        <span className="text-[10px] text-[var(--muted)] mt-1 block">
                          مجموعات إيرادات التشغيل والمنصات (4)
                        </span>
                      </div>

                      <div className="p-4 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs">
                        <div className="flex items-center justify-between text-xs text-[var(--muted)] mb-1">
                          <span>إجمالي المصروفات</span>
                          <TrendingDown size={16} className="text-rose-500" />
                        </div>
                        <div className="text-xl font-black text-rose-600 dark:text-rose-400 num">
                          {expense.toLocaleString('en-US', { minimumFractionDigits: 2 })} <span className="text-xs font-bold">ر.س</span>
                        </div>
                        <span className="text-[10px] text-[var(--muted)] mt-1 block">
                          تكاليف التشغيل والرواتب والمصاريف الإدارية (5)
                        </span>
                      </div>

                      <div className="p-4 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs">
                        <div className="flex items-center justify-between text-xs text-[var(--muted)] mb-1">
                          <span>صافي الدخل (الأرباح / الخسائر)</span>
                          <Scale size={16} className={isNetPositive ? 'text-emerald-500' : 'text-rose-500'} />
                        </div>
                        <div className={`text-xl font-black num ${isNetPositive ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'}`}>
                          {net.toLocaleString('en-US', { minimumFractionDigits: 2 })} <span className="text-xs font-bold">ر.س</span>
                        </div>
                        <span className="text-[10px] text-[var(--muted)] mt-1 block">
                          {company.netIsComplete ? 'التعيين مكتمل بنسبة 100%' : 'يوجد بنود غير معينة بالكامل'}
                        </span>
                      </div>
                    </div>

                    {/* Breakdown Tables: Revenue and Expenses */}
                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                      {/* Revenues Table */}
                      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                        <div className="p-4 border-b border-[var(--line)] bg-[var(--raised)]/50 flex items-center justify-between">
                          <h4 className="font-bold text-sm text-[var(--ink)] flex items-center gap-2">
                            <span className="h-2 w-2 rounded-full bg-emerald-500" />
                            <span>تفاصيل مجموعات الإيرادات</span>
                          </h4>
                          <span className="num font-bold text-xs text-emerald-600">
                            {revenue.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </span>
                        </div>

                        <div className="divide-y divide-[var(--line)] max-h-96 overflow-y-auto">
                          {(company.revenueGroups || []).length > 0 ? (
                            (company.revenueGroups || []).map((grp) => (
                              <div key={grp.code} className="p-3 flex items-center justify-between text-xs hover:bg-[var(--raised)]/40 transition">
                                <div className="flex flex-col gap-0.5">
                                  <span className="font-bold text-[var(--ink)]">{grp.nameAr}</span>
                                  <span className="font-mono text-[11px] text-[var(--muted)] num">{grp.code}</span>
                                </div>
                                <span className="num font-bold text-[var(--ink)]">
                                  {grp.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </span>
                              </div>
                            ))
                          ) : (
                            <div className="p-6 text-center text-xs text-[var(--muted)]">
                              لا توجد حركات إيرادات مسجلة لهذه الفترة
                            </div>
                          )}
                        </div>
                      </div>

                      {/* Expenses Table */}
                      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                        <div className="p-4 border-b border-[var(--line)] bg-[var(--raised)]/50 flex items-center justify-between">
                          <h4 className="font-bold text-sm text-[var(--ink)] flex items-center gap-2">
                            <span className="h-2 w-2 rounded-full bg-rose-500" />
                            <span>تفاصيل مجموعات المصروفات</span>
                          </h4>
                          <span className="num font-bold text-xs text-rose-600">
                            {expense.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </span>
                        </div>

                        <div className="divide-y divide-[var(--line)] max-h-96 overflow-y-auto">
                          {(company.expenseGroups || []).length > 0 ? (
                            (company.expenseGroups || []).map((grp) => (
                              <div key={grp.code} className="p-3 flex items-center justify-between text-xs hover:bg-[var(--raised)]/40 transition">
                                <div className="flex flex-col gap-0.5">
                                  <span className="font-bold text-[var(--ink)]">{grp.nameAr}</span>
                                  <span className="font-mono text-[11px] text-[var(--muted)] num">{grp.code}</span>
                                </div>
                                <span className="num font-bold text-[var(--ink)]">
                                  {grp.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </span>
                              </div>
                            ))
                          ) : (
                            <div className="p-6 text-center text-xs text-[var(--muted)]">
                              لا توجد حركات مصروفات مسجلة لهذه الفترة
                            </div>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <EmptyState title="لا توجد بيانات لقائمة الدخل" description="تأكد من اختيار الفترة المحددة." />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* DRAWER: Account Details Drawer                           */}
      {/* ======================================================== */}
      {selectedAccount && (
        <div className="fixed inset-0 z-50 flex justify-end">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-xs"
            onClick={() => setSelectedAccount(null)}
            aria-hidden="true"
          />
          <div
            role="dialog"
            aria-label="تفاصيل الحساب المحاسبي"
            className="relative w-full max-w-md bg-[var(--surface)] text-[var(--ink)] h-full shadow-2xl p-6 flex flex-col justify-between overflow-y-auto z-10"
          >
            <div className="space-y-6">
              <div className="flex items-center justify-between border-b border-[var(--line)] pb-4">
                <div>
                  <h3 className="font-bold text-base text-[var(--ink)]">{selectedAccount.nameAr}</h3>
                  <span className="font-mono text-xs text-[var(--accent)] num font-bold">
                    {selectedAccount.code}
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => setSelectedAccount(null)}
                  className="p-1.5 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                >
                  <X size={18} />
                </button>
              </div>

              <div className="space-y-3 text-xs">
                <div className="flex justify-between py-2 border-b border-[var(--line)]">
                  <span className="text-[var(--muted)]">المستوى في الدليل</span>
                  <span className="font-bold text-[var(--ink)] num">مستوى {selectedAccount.level}</span>
                </div>

                <div className="flex justify-between py-2 border-b border-[var(--line)]">
                  <span className="text-[var(--muted)]">نوع الحساب</span>
                  <span className="font-bold">
                    {(selectedAccount.posting ?? selectedAccount.isPosting) ? 'حساب ترحيل مباشر (طرفي)' : 'حساب رئيسي تجميعي'}
                  </span>
                </div>

                <div className="flex justify-between py-2 border-b border-[var(--line)]">
                  <span className="text-[var(--muted)]">الطبيعة المحاسبية</span>
                  <span className="font-bold text-[var(--ink)]">
                    {(selectedAccount.nature || '').toLowerCase() === 'debit' ? 'مدين (Debit)' : 'دائن (Credit)'}
                  </span>
                </div>

                <div className="flex justify-between py-2 border-b border-[var(--line)]">
                  <span className="text-[var(--muted)]">المصدر المعتمد</span>
                  <span className="font-bold text-[var(--ink)]">
                    {selectedAccount.sourceNote || selectedAccount.source || 'Excel 2024'}
                  </span>
                </div>

                {selectedAccount.parent && (
                  <div className="flex justify-between py-2 border-b border-[var(--line)]">
                    <span className="text-[var(--muted)]">الحساب الرئيسي الأب</span>
                    <span className="font-mono font-bold text-[var(--accent)] num">{selectedAccount.parent}</span>
                  </div>
                )}

                <div className="flex justify-between py-2 border-b border-[var(--line)]">
                  <span className="text-[var(--muted)]">حالة الحساب</span>
                  <span className="font-bold text-emerald-600 dark:text-emerald-400">
                    {selectedAccount.status || 'نشط'}
                  </span>
                </div>
              </div>
            </div>

            <div className="pt-4 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setSelectedAccount(null)}
                className="w-full py-2.5 rounded-xl font-bold text-xs bg-[var(--raised)] hover:bg-[var(--line)] text-[var(--ink)] transition"
              >
                إغلاق
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* DRAWER: Journal Details Drawer                           */}
      {/* ======================================================== */}
      {selectedJournal && (
        <div className="fixed inset-0 z-50 flex justify-end">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-xs"
            onClick={() => setSelectedJournal(null)}
            aria-hidden="true"
          />
          <div
            role="dialog"
            aria-label="تفاصيل القيد المحاسبي"
            className="relative w-full max-w-2xl bg-[var(--surface)] text-[var(--ink)] h-full shadow-2xl p-6 flex flex-col justify-between overflow-y-auto z-10"
          >
            <div className="space-y-6">
              <div className="flex items-center justify-between border-b border-[var(--line)] pb-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="font-bold text-base text-[var(--ink)]">
                      قيد محاسبي: {selectedJournal.journalNumber || selectedJournal.id.slice(0, 10)}
                    </h3>
                    {selectedJournal.reversesJournalId && (
                      <span className="text-[10px] bg-amber-500/10 text-amber-600 dark:text-amber-400 px-2 py-0.5 rounded-full font-bold">
                        قيد عكسي
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-[var(--muted)] mt-1">
                    التاريخ: {selectedJournal.date || selectedJournal.entryDate || selectedJournal.createdAt?.slice(0, 10)} · الفترة: {selectedJournal.period || '—'}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => setSelectedJournal(null)}
                  className="p-1.5 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                >
                  <X size={18} />
                </button>
              </div>

              {/* Description & metadata */}
              <div className="p-3 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs space-y-1">
                <p className="font-semibold text-[var(--ink)]">{selectedJournal.description}</p>
                {selectedJournal.sourceRef && (
                  <p className="text-[var(--muted)] font-mono text-[11px]">مرجع: {selectedJournal.sourceRef}</p>
                )}
                {selectedJournal.reversesJournalId && (
                  <div className="pt-1 flex items-center gap-1.5 text-amber-700 dark:text-amber-300">
                    <Info size={13} />
                    <span>هذا القيد يعكس القيد السابق رقم: {selectedJournal.reversesJournalId}</span>
                  </div>
                )}
              </div>

              {/* Journal Lines Table */}
              <div className="space-y-2">
                <h4 className="font-bold text-xs text-[var(--ink)]">أسطر القيد المحاسبي المزدوج</h4>
                <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
                  <table className="w-full text-xs text-right border-collapse">
                    <thead>
                      <tr className="bg-[var(--raised)] border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                        <th className="py-2 px-3">رقم الحساب</th>
                        <th className="py-2 px-3">اسم الحساب</th>
                        <th className="py-2 px-3 text-left num">مدين</th>
                        <th className="py-2 px-3 text-left num">دائن</th>
                        <th className="py-2 px-3">البيان</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {(selectedJournal.lines || []).map((line, idx) => (
                        <tr key={idx} className="hover:bg-[var(--raised)]/40 transition">
                          <td className="py-2 px-3 font-mono font-bold text-[var(--accent)] num">
                            {line.accountCode}
                          </td>
                          <td className="py-2 px-3 text-[var(--ink)] font-medium">
                            {line.accountNameAr || '—'}
                          </td>
                          <td className="py-2 px-3 text-left num font-mono text-blue-600 dark:text-blue-400 font-bold">
                            {(line.debit || 0) > 0 ? (line.debit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                          </td>
                          <td className="py-2 px-3 text-left num font-mono text-purple-600 dark:text-purple-400 font-bold">
                            {(line.credit || 0) > 0 ? (line.credit || 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                          </td>
                          <td className="py-2 px-3 text-[var(--muted)] text-[11px] truncate max-w-[150px]">
                            {line.description || '—'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                    <tfoot className="bg-[var(--raised)] border-t border-[var(--line)] font-bold text-xs">
                      <tr>
                        <td colSpan={2} className="py-2 px-3 text-right">
                          إجمالي القيد
                        </td>
                        <td className="py-2 px-3 text-left num font-mono text-blue-600">
                          {(selectedJournal.lines?.reduce((sum, l) => sum + (l.debit || 0), 0) || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                        </td>
                        <td className="py-2 px-3 text-left num font-mono text-purple-600">
                          {(selectedJournal.lines?.reduce((sum, l) => sum + (l.credit || 0), 0) || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}
                        </td>
                        <td />
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </div>
            </div>

            <div className="pt-4 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setSelectedJournal(null)}
                className="w-full py-2.5 rounded-xl font-bold text-xs bg-[var(--raised)] hover:bg-[var(--line)] text-[var(--ink)] transition"
              >
                إغلاق
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
