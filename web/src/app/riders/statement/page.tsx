'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  FileText,
  Printer,
  Download,
  Calendar,
  Search,
  ArrowRight,
  Building2,
  Phone,
  CreditCard,
  Car,
  AlertCircle,
  CheckCircle2,
  DollarSign,
  TrendingDown,
  TrendingUp,
  Clock,
  Layers,
  ArrowDownLeft,
  ArrowUpRight,
} from 'lucide-react';
import { api } from '../../../lib/api';
import { useAuth } from '../../../lib/auth-context';
import { useToast } from '../../../components/ui/Toast';
import { PageHeader } from '../../../components/ui/PageHeader';
import { Skeleton, EmptyState, ErrorState } from '../../../components/ui/EmptyState';
import { RiderPicker } from '../../../components/ui/RiderPicker';
import type { RiderStatement, RiderProfile360, MonthlyRiderStatement } from '../../../lib/types';
import { RiderAccountsPanel } from '../../../components/riders/RiderAccountsPanel';

function RiderStatementPageInner() {
  const { user } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const [iqama, setIqama] = useState<string>(searchParams?.get('iqama') || '');
  const [fromDate, setFromDate] = useState<string>('2026-01-01');
  const [toDate, setToDate] = useState<string>('2026-10-31');
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const [statement, setStatement] = useState<RiderStatement | null>(null);
  const [monthlyStatement, setMonthlyStatement] = useState<MonthlyRiderStatement | null>(null);
  const [profile, setProfile] = useState<RiderProfile360 | null>(null);

  const fetchStatement = async (targetIqama?: string) => {
    const q = (targetIqama || iqama).trim();
    if (!q) {
      toast('يرجى إدخال رقم إقامة المندوب', 'warning');
      return;
    }
    setLoading(true);
    setError(null);
    const period = toDate ? toDate.slice(0, 7) : '2026-10';
    try {
      // 1. Try 0.14.0 monthly statement endpoint first (R4.8)
      let monthlyData: MonthlyRiderStatement | null = null;
      try {
        const res = await api.getRiderMonthlyStatement(q, period);
        if (res && (res.source !== 'none' || (res.cards && res.cards.length > 0))) {
          monthlyData = res;
        }
      } catch (_) {
        monthlyData = null;
      }
      setMonthlyStatement(monthlyData);

      // 2. Fetch detailed or simple statement fallback + profile
      const [stmtRes, profRes] = await Promise.allSettled([
        (async () => {
          try {
            return await api.getRiderStatement(q, fromDate, toDate);
          } catch {
            const simple = await api.getRiderStatementSimple(q, fromDate, toDate);
            return {
              iqama: q,
              riderName: simple.riderName || monthlyData?.header?.name || 'مندوب تجريبي',
              period: { from: fromDate, to: toDate },
              openingBalance: simple.openingBalance || monthlyData?.carried || 0,
              closingBalance: simple.closingBalance || monthlyData?.closing || 0,
              entries: simple.entries || [],
            } as RiderStatement;
          }
        })(),
        api.getRiderProfile360(q),
      ]);

      if (stmtRes.status === 'fulfilled') {
        setStatement(stmtRes.value);
      } else {
        throw stmtRes.reason;
      }

      if (profRes.status === 'fulfilled') {
        setProfile(profRes.value);
      } else {
        setProfile(null);
      }
    } catch (err: any) {
      const msg = err.message || 'تعذر استرجاع كشف الحساب المالي';
      setError(msg);
      toast(msg, 'danger');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    const queryIqama = searchParams?.get('iqama');
    if (queryIqama) {
      setIqama(queryIqama);
      fetchStatement(queryIqama);
    }
  }, []);

  // Total debits and credits
  const totals = useMemo(() => {
    if (!statement?.entries) return { debit: 0, credit: 0 };
    return statement.entries.reduce(
      (acc, e) => ({
        debit: acc.debit + (e.debit || 0),
        credit: acc.credit + (e.credit || 0),
      }),
      { debit: 0, credit: 0 }
    );
  }, [statement]);

  // Mask IBAN
  const maskIban = (iban?: string) => {
    if (!iban || iban.length < 10) return iban || 'غير مسجل';
    return `${iban.slice(0, 4)} **** **** ${iban.slice(-4)}`;
  };

  // Mask Iqama
  const maskIqama = (val: string) => {
    if (!val || val.length < 8) return val;
    return `${val.slice(0, 2)}****${val.slice(-4)}`;
  };

  // Print handler
  const handlePrint = () => {
    if (typeof window !== 'undefined') {
      window.print();
    }
  };

  // Export CSV
  const handleExportCsv = () => {
    if (!statement?.entries || statement.entries.length === 0) {
      toast('لا توجد حركات للتصدير', 'warning');
      return;
    }

    const headers = ['التاريخ', 'نوع الحركة', 'البيان', 'المرجع', 'مدين (ر.س)', 'دائن (ر.س)', 'الرصيد التراكمي (ر.س)'];
    const rows = (statement.entries || []).map((e) => [
      e.date,
      e.entryType,
      `"${(e.description || '').replace(/"/g, '""')}"`,
      e.reference || '',
      e.debit || 0,
      e.credit || 0,
      e.balance || 0,
    ]);

    const csvContent = '\uFEFF' + [headers.join(','), ...rows.map((r) => r.join(','))].join('\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const pFrom = statement.period?.from || fromDate;
    const pTo = statement.period?.to || toDate;
    a.download = `statement-${statement.iqama}-${pFrom}-to-${pTo}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    toast('تم تصدير كشف الحساب بصيغة CSV بنجاح', 'success');
  };

  const isDueToCompany = (statement?.closingBalance || 0) < 0; // عليه
  const isDueToRider = (statement?.closingBalance || 0) > 0; // له

  return (
    <div className="space-y-6 print:m-0 print:p-0">
      {/* Top action bar (hidden during print) */}
      <div className="print:hidden space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Link
              href="/riders/"
              className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
              title="العودة لدليل المناديب"
            >
              <ArrowRight size={16} />
            </Link>
            <div>
              <h1 className="text-lg sm:text-xl font-bold text-[var(--ink)]">
                كشف حساب المندوب المالي
              </h1>
              <p className="text-xs text-[var(--muted)]">
                الرصيد الافتتاحي، الحركات اليومية، استقطاعات التشغيل، والرصيد الختامي المعتمد.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handlePrint}
              className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] text-xs font-bold transition inline-flex items-center gap-1.5 cursor-pointer"
            >
              <Printer size={15} />
              <span>طباعة / PDF</span>
            </button>
            <button
              type="button"
              onClick={handleExportCsv}
              className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <Download size={15} />
              <span>تصدير Excel (CSV)</span>
            </button>
          </div>
        </div>

        {/* Search & Filter Toolbar */}
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center gap-3">
          <div className="flex-1 min-w-[240px] max-w-sm">
            <RiderPicker
              value={iqama}
              onChange={(rider) => {
                if (rider?.iqama) {
                  setIqama(rider.iqama);
                  fetchStatement(rider.iqama);
                }
              }}
              placeholder="ابحث باسم المندوب أو رقم الإقامة..."
            />
          </div>

          <div className="flex items-center gap-2 text-xs">
            <span className="text-[var(--muted)]">من:</span>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
            <span className="text-[var(--muted)]">إلى:</span>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <button
            type="button"
            disabled={loading}
            onClick={() => fetchStatement()}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
          >
            {loading ? 'جاري الاستعلام...' : 'استعراض كشف الحساب'}
          </button>
        </div>
      </div>

      {error ? (
        <ErrorState title="تعذر استرجاع كشف الحساب" message={error} onRetry={() => fetchStatement()} />
      ) : loading ? (
        <div className="p-8 space-y-4 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : !statement ? (
        <EmptyState
          title="أدخل رقم الإقامة واستعرض كشف الحساب"
          description="يعرض كشف الحساب تفاصيل المستحقات، استقطاعات المنصات، السلف، والرصيد الختامي."
        />
      ) : (
        <div className="space-y-6 print:space-y-4">
          {/* Section 1: Header (Plan §3.1) */}
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs print:border-black print:shadow-none">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <div className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-[var(--raised)] text-xs font-bold text-[var(--accent)] mb-2 print:border">
                  <span>شركة البوابة المقبلة للتجارة · كشف حساب مندوب معتمد</span>
                </div>
                <h2 className="text-xl sm:text-2xl font-black text-[var(--ink)]">
                  {statement.riderName}
                </h2>
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-1.5 text-xs text-[var(--muted)] mt-2">
                  <div>
                    <span className="font-semibold text-[var(--ink)]">رقم الإقامة: </span>
                    <span className="font-mono">{statement.iqama}</span>
                  </div>
                  <div>
                    <span className="font-semibold text-[var(--ink)]">الكفيل / الشركة: </span>
                    <span>{profile?.rider?.sponsor || 'شركة البوابة المقبلة'}</span>
                  </div>
                  <div>
                    <span className="font-semibold text-[var(--ink)]">الفئة التشغيلية: </span>
                    <span>
                      {profile?.rider?.engagementType === 'SponsoredInternal' ? 'كفالة الشركة' : 'خارجي'}
                      {' × '}
                      {profile?.rider?.operationalRole === 'Car' ? 'سيارة' : 'دباب'}
                    </span>
                  </div>
                  <div>
                    <span className="font-semibold text-[var(--ink)]">الآيبان البنكي: </span>
                    <span className="font-mono">{maskIban(profile?.rider?.iban)}</span>
                  </div>
                </div>
              </div>

              <div className="text-start sm:text-end shrink-0">
                <div className="text-xs text-[var(--muted)] font-medium">فترة كشف الحساب</div>
                <div className="text-sm font-bold num text-[var(--ink)] mt-0.5">
                  {(statement.period?.from || fromDate)} ➔ {(statement.period?.to || toDate)}
                </div>
              </div>
            </div>

            {/* Balances Summary Cards */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 mt-6 pt-5 border-t border-[var(--line)]">
              <div className="p-3.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] print:bg-white">
                <div className="text-xs text-[var(--muted)] font-medium">الرصيد الافتتاحي</div>
                <div className="text-lg font-bold num text-[var(--ink)] mt-1">
                  {(statement.openingBalance || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </div>
                <div className="text-[10px] text-[var(--muted)] mt-0.5">الرصيد المرحل قبل هذه الفترة</div>
              </div>

              <div className="p-3.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] print:bg-white">
                <div className="text-xs text-[var(--muted)] font-medium">إجمالي الحركات (مدين / دائن)</div>
                <div className="text-xs font-bold num text-[var(--ink)] mt-1.5 flex justify-between">
                  <span>مدين: {totals.debit.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                  <span>دائن: {totals.credit.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                </div>
                <div className="text-[10px] text-[var(--muted)] mt-0.5">{(statement.entries || []).length} حركات مالية مسجلة</div>
              </div>

              <div className={`p-3.5 rounded-xl border print:bg-white ${
                isDueToRider
                  ? 'bg-[var(--money-pos)]/10 border-[var(--money-pos)]/40 text-[var(--money-pos)]'
                  : isDueToCompany
                  ? 'bg-[var(--danger)]/10 border-[var(--danger)]/40 text-[var(--danger)]'
                  : 'bg-[var(--raised)]/60 border-[var(--line)] text-[var(--ink)]'
              }`}>
                <div className="text-xs font-medium">الرصيد الختامي المعتمد</div>
                <div className="text-xl font-black num mt-1">
                  {Math.abs(statement.closingBalance || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </div>
                <div className="text-xs font-bold mt-0.5">
                  {isDueToRider ? 'مستحق للمندوب (له)' : isDueToCompany ? 'مستحق على المندوب (عليه)' : 'حساب مطابق (صفر)'}
                </div>
              </div>
            </div>
          </div>

          {/* Monthly App Cards & Explain Lines (0.14.0 / R4.8) */}
          {monthlyStatement && monthlyStatement.cards && monthlyStatement.cards.length > 0 && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                    كروت استحقاق المنصات الشهرية وتفاصيل الاحتساب (R4.8)
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-0.5">
                    مصدر الاحتساب: {monthlyStatement.source === 'pay_cycle' ? 'دورة الرواتب المعتمدة' : 'مسير الرواتب القائم'} · الحالة: {monthlyStatement.status === 'approved' ? 'معتمد' : 'معاينة'}
                  </p>
                </div>
                <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/15 text-[var(--accent)]">
                  صافي الشهر: {monthlyStatement.net.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>

              {/* Statement Deduction Phases & Subtotals (BR §29) */}
              {monthlyStatement.phases && (
                <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                  <div className="flex items-center justify-between border-b border-[var(--line)] pb-2">
                    <div>
                      <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                        مراحل احتساب الاستقطاعات وصافي الاستحقاق (BR §29)
                      </h4>
                      <p className="text-[11px] text-[var(--muted)] mt-0.5">
                        التدرج المالي المعتمد لحسم المستحقات من الإجمالي حتى الصافي النهائي.
                      </p>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2 text-xs">
                    <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                      <span className="text-[10px] text-[var(--muted)] block">الإجمالي (Gross)</span>
                      <span className="num font-mono font-bold text-[var(--ink)]">
                        {monthlyStatement.phases.gross.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                      <span className="text-[10px] text-[var(--muted)] block">بعد العمولة</span>
                      <span className="num font-mono font-bold text-[var(--ink)]">
                        {monthlyStatement.phases.afterCommission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                      <span className="text-[10px] text-[var(--muted)] block">المرحلة 1 (تشغيلي)</span>
                      <span className="num font-mono font-bold text-[var(--danger)]">
                        {monthlyStatement.phases.phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/40">
                      <span className="text-[10px] font-bold text-[var(--accent)] block">المستحق الأولي</span>
                      <span className="num font-mono font-black text-[var(--accent)]">
                        {monthlyStatement.phases.preliminaryDue.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                      <span className="text-[10px] text-[var(--muted)] block">المرحلة 2 (مديونيات)</span>
                      <span className="num font-mono font-bold text-[var(--danger)]">
                        {monthlyStatement.phases.phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                      <span className="text-[10px] text-[var(--muted)] block">المرحلة 3 (تسويات)</span>
                      <span className="num font-mono font-bold text-[var(--ink)]">
                        {monthlyStatement.phases.phase3.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>

                    <div className="p-2.5 rounded-xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/40">
                      <span className="text-[10px] font-bold text-[var(--money-pos)] block">الصافي النهائي</span>
                      <span className="num font-mono font-black text-[var(--money-pos)]">
                        {monthlyStatement.net.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </div>
                  </div>
                </div>
              )}

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {monthlyStatement.cards.map((card, idx) => (
                  <div key={idx} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                    <div className="flex justify-between items-start">
                      <div>
                        <span className="text-[10px] px-2 py-0.5 rounded-full bg-[var(--raised)] font-mono text-[var(--muted)]">
                          {card.app}
                        </span>
                        <h4 className="font-bold text-base text-[var(--ink)] mt-1">{card.platform}</h4>
                        <div className="text-xs text-[var(--muted)]">{card.schemeName || 'النظام المعتمد'}</div>
                      </div>
                      <div className="text-end">
                        <span className="text-xs text-[var(--muted)] block">الصافي قبل التحملات:</span>
                        <span className="text-base font-bold num text-[var(--money-pos)]">
                          {(card.netBeforePersonal ?? card.earnedGross ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </span>
                      </div>
                    </div>

                    <div className="grid grid-cols-3 gap-2 p-2.5 rounded-xl bg-[var(--raised)]/50 text-xs">
                      <div>
                        <span className="text-[10px] text-[var(--muted)] block">إجمالي المنصة:</span>
                        <span className="font-bold num">{(card.gross || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س</span>
                      </div>
                      <div>
                        <span className="text-[10px] text-[var(--muted)] block">إيجار المركبة:</span>
                        <span className="font-bold num text-[var(--danger)]">{(card.vehicleCharge || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س</span>
                      </div>
                      <div>
                        <span className="text-[10px] text-[var(--muted)] block">الحوافز:</span>
                        <span className="font-bold num text-[var(--accent)]">{(card.incentives || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س</span>
                      </div>
                    </div>

                    {card.explain && card.explain.length > 0 && (
                      <div className="space-y-1.5 pt-1">
                        <span className="text-[11px] font-bold text-[var(--muted)] block">شرح الاحتساب (Explain):</span>
                        <div className="space-y-1">
                          {card.explain.map((exp, eIdx) => (
                            <div key={eIdx} className="p-2 rounded-lg bg-[var(--surface)] border border-[var(--line)] text-xs flex justify-between items-center">
                              <div>
                                <span className="font-semibold text-[var(--ink)]">{exp.label}</span>
                                {exp.formula && (
                                  <span className="text-[10px] text-[var(--muted)] font-mono block text-left" dir="ltr">{exp.formula}</span>
                                )}
                              </div>
                              <span className={`num font-bold ${exp.amount < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'}`}>
                                {(exp.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                ))}
              </div>

              {monthlyStatement.deductions && monthlyStatement.deductions.length > 0 && (
                <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-2">
                  <h4 className="font-bold text-xs text-[var(--ink)]">الاستقطاعات والتحملات المباشرة (Stage 2 Deductions):</h4>
                  <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-2">
                    {monthlyStatement.deductions.map((d, dIdx) => (
                      <div key={dIdx} className="p-2.5 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs flex justify-between items-center">
                        <div>
                          <span className="font-semibold text-[var(--ink)]">{d.description || d.category}</span>
                          <span className="text-[10px] text-[var(--muted)] block font-mono">{d.source}</span>
                        </div>
                        <span className="num font-bold text-[var(--danger)]">
                          -{(d.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Section 2: Timeline Table (Plan §3.3) */}
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs print:border-black print:shadow-none">
            <div className="p-4 border-b border-[var(--line)]">
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                سجل حركات كشف الحساب الزمني (Timeline Table)
              </h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                كافة قيود الاستحقاق والخصم وحركات السلف والتسويات المحاسبية مرتبة زمنياً.
              </p>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-start text-xs sm:text-sm">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold print:bg-gray-100">
                  <tr>
                    <th className="p-3 text-start">التاريخ</th>
                    <th className="p-3 text-start">نوع الحركة</th>
                    <th className="p-3 text-start">البيان والتفاصيل</th>
                    <th className="p-3 text-start">رقم المرجع</th>
                    <th className="p-3 text-start">مدين (له)</th>
                    <th className="p-3 text-start">دائن (عليه)</th>
                    <th className="p-3 text-start">الرصيد التراكمي</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)] print:divide-gray-300">
                  {(statement.entries || []).length === 0 ? (
                    <tr>
                      <td colSpan={7} className="p-6 text-center text-xs text-[var(--muted)]">
                        لا توجد حركات مالية مسجلة خلال الفترة المحددة.
                      </td>
                    </tr>
                  ) : (
                    (statement.entries || []).map((entry, idx) => (
                      <tr key={idx} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs text-[var(--muted)] whitespace-nowrap">
                          {entry.date}
                        </td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--ink)]">
                            {entry.entryType}
                          </span>
                        </td>
                        <td className="p-3 font-medium max-w-sm">{entry.description}</td>
                        <td className="p-3 font-mono text-xs text-[var(--muted)]">{entry.reference || '-'}</td>
                        <td className="p-3 num font-bold text-[var(--money-pos)]">
                          {entry.debit > 0 ? `${entry.debit.toLocaleString('en-US', { minimumFractionDigits: 2 })}` : '-'}
                        </td>
                        <td className="p-3 num font-bold text-[var(--danger)]">
                          {entry.credit > 0 ? `${entry.credit.toLocaleString('en-US', { minimumFractionDigits: 2 })}` : '-'}
                        </td>
                        <td className="p-3 num font-mono font-bold text-[var(--ink)]">
                          {entry.balance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {/* Section 3: Footer with Closing Label (Plan §3.4) */}
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4 print:border-black print:shadow-none">
            <div>
              <div className="text-xs text-[var(--muted)] font-medium">الخلاصة المالية النهائية</div>
              <div className="flex items-center gap-2 mt-1">
                <span className="text-base sm:text-lg font-black text-[var(--ink)]">
                  الرصيد النهائي المستحق:
                </span>
                <span className={`text-xl sm:text-2xl font-black num ${isDueToRider ? 'text-[var(--money-pos)]' : isDueToCompany ? 'text-[var(--danger)]' : 'text-[var(--ink)]'}`}>
                  {Math.abs(statement.closingBalance).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
                <span className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                  isDueToRider
                    ? 'bg-[var(--money-pos)] text-white'
                    : isDueToCompany
                    ? 'bg-[var(--danger)] text-white'
                    : 'bg-[var(--raised)] text-[var(--ink)]'
                }`}>
                  {isDueToRider ? 'مستحق للمندوب (له)' : isDueToCompany ? 'مستحق على المندوب (عليه)' : 'حساب مطابق'}
                </span>
              </div>
            </div>

            <div className="flex items-center gap-6 text-xs text-[var(--muted)] print:text-black">
              <div className="text-center">
                <div className="font-semibold text-[var(--ink)]">إعداد المحاسب</div>
                <div className="mt-4 pt-1 border-t border-[var(--line)] w-28">التوقيع</div>
              </div>
              <div className="text-center">
                <div className="font-semibold text-[var(--ink)]">اعتماد المدير المالي</div>
                <div className="mt-4 pt-1 border-t border-[var(--line)] w-28">الختم والاعتماد</div>
              </div>
            </div>
          </div>

          {/* Rider Platform Accounts Panel (0.20.0 Live ERP Reads) */}
          <div className="print:hidden">
            <RiderAccountsPanel iqama={statement.iqama} />
          </div>
        </div>
      )}
    </div>
  );
}

export default function RiderStatementPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-48 w-full rounded-2xl" />
        </div>
      }
    >
      <RiderStatementPageInner />
    </Suspense>
  );
}
