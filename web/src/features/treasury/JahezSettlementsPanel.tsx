'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError, type ApiResp } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import type { CompanySponsor } from '../../lib/types';
import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';

type PeriodReport = ApiResp<'/api/sal/erp-settlements/jahez/period-report'>;
type LiveRow = NonNullable<NonNullable<PeriodReport['live']>['items']>[number];

export function JahezSettlementsPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canView = can('pay.view') || can('treasury.view') || isAdmin;
  const { period } = usePeriod();
  const range = periodDateRange(period);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [companyId, setCompanyId] = useState('');
  const [from, setFrom] = useState(range.from);
  const [to, setTo] = useState(range.to);
  const [invoiceNet, setInvoiceNet] = useState('');
  const [invoiceVat, setInvoiceVat] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rows, setRows] = useState<LiveRow[]>([]);
  const [totals, setTotals] = useState<Record<string, number>>({});
  const [noteAr, setNoteAr] = useState('');
  const [rates, setRates] = useState<Array<Record<string, unknown>>>([]);
  const [allowed, setAllowed] = useState<string[]>([]);

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      setCompanyId((prev) => prev || list[0]?.code || list[0]?.id || '');
    });
  }, []);

  useEffect(() => {
    const r = periodDateRange(period);
    setFrom(r.from);
    setTo(r.to);
  }, [period]);

  const load = useCallback(async () => {
    if (!canView || !companyId) return;
    setLoading(true);
    setError(null);
    try {
      const report = await api.getJahezPeriodReport({
        companyId,
        from,
        to,
        invoiceNet: invoiceNet.trim() === '' ? undefined : Number(invoiceNet),
        invoiceVat: invoiceVat.trim() === '' ? undefined : Number(invoiceVat),
      });
      const live = report.live;
      setRows(Array.isArray(live?.items) ? live.items : []);
      const t = report.totals ?? {};
      setTotals(Object.fromEntries(Object.entries(t).filter(([, v]) => typeof v === 'number')) as Record<string, number>);
      setRates(Array.isArray(report.commissionRates) ? report.commissionRates.map((r) => ({ ...r })) : []);
      setNoteAr(String(report.noteAr || ''));
      setAllowed(Array.isArray(report.allowedActions) ? [...report.allowedActions] : []);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل تصفيات جاهز');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [canView, companyId, from, to, invoiceNet, invoiceVat]);

  useEffect(() => {
    void load();
  }, [load]);

  const cols = useMemo(
    () => [
      { key: 'executedAt', header: 'التنفيذ', type: 'date' as const, getValue: (r: LiveRow) => String(r.executedAt || '').slice(0, 10) },
      { key: 'net', header: 'الصافي', type: 'number' as const, getValue: (r: LiveRow) => Number(r.net || 0) },
      { key: 'commission', header: 'العمولة', type: 'number' as const, getValue: (r: LiveRow) => Number(r.commission || 0) },
      { key: 'transferToRider', header: 'تحويل للمندوب نيابة عن جاهز', type: 'number' as const, getValue: (r: LiveRow) => Number(r.transferToRider || 0) },
      { key: 'fund', header: 'الصندوق', type: 'text' as const, getValue: (r: LiveRow) => String(r.fund || '') },
      { key: 'purpose', header: 'الغرض', type: 'text' as const, getValue: (r: LiveRow) => String(r.purpose || '') },
      { key: 'status', header: 'الحالة', type: 'text' as const, getValue: (r: LiveRow) => String(r.status || '') },
    ],
    []
  );
  const filter = useTableAutoFilter(rows, cols);

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض تصفيات جاهز يحتاج pay.view." />;
  }

  return (
    <div className="space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">تصفيات جاهز للفترة (قراءة من نظام عمر)</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">بلا دورة راتب — تصفيات متفرقة. اختر فترة الفاتورة وقارن ما دفعناه/حصّلناه بالفاتورة.</p>
          {noteAr ? <p className="text-[11px] text-[var(--muted)] mt-1">{noteAr}</p> : null}
        </div>
        <button type="button" onClick={() => void load()} className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs">
          <RefreshCw size={12} /> تحديث
        </button>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 lg:grid-cols-6 gap-2">
        <SearchableSelect
          value={companyId}
          onChange={setCompanyId}
          options={companies.map((c) => ({ value: c.code || c.id || '', label: companyLabel(c) }))}
          placeholder="الشركة"
        />
        <label className="text-[11px] font-semibold space-y-1">
          <span>من</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
        </label>
        <label className="text-[11px] font-semibold space-y-1">
          <span>إلى</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
        </label>
        <label className="text-[11px] font-semibold space-y-1">
          <span>صافي الفاتورة</span>
          <input value={invoiceNet} onChange={(e) => setInvoiceNet(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
        </label>
        <label className="text-[11px] font-semibold space-y-1">
          <span>ضريبة الفاتورة</span>
          <input value={invoiceVat} onChange={(e) => setInvoiceVat(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
        </label>
        <button type="button" disabled={loading || !allowed.includes('compare_invoice') && allowed.length === 0} onClick={() => void load()} className="self-end px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
          احسب الفترة
        </button>
      </div>

      {loading ? <Skeleton className="h-28 rounded-2xl" /> : null}
      {error ? <ErrorState title="تعذر التحميل" message={error} onRetry={load} /> : null}

      {!loading && !error ? (
        <>
          <div className="flex flex-wrap gap-3 text-xs">
            <span>عمولة: <b>{formatSar(Number(totals.commission || 0))}</b></span>
            <span>تحويل للمناديب: <b>{formatSar(Number(totals.transferToRider || 0))}</b></span>
            <span>تحصيلات نقدية: <b>{formatSar(Number(totals.cashCollections || 0))}</b></span>
            <span>مدفوع نيابة: <b>{formatSar(Number(totals.paidOnBehalf || 0))}</b></span>
            <span>المتوقع: <b>{formatSar(Number(totals.expectedNet || 0))}</b></span>
            {totals.hasInvoice ? <span>فرق الفاتورة: <b>{formatSar(Number(totals.variance || 0))}</b></span> : null}
          </div>
          {rates.length > 0 ? (
            <p className="text-[11px] text-[var(--muted)]">أسعار عمولة معروضة: {rates.length} (من المصدر المحلي إن وُجد).</p>
          ) : null}
          <AutoFilterBar controller={filter} totalCount={rows.length} />
          <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
            <table className="w-full text-xs">
              <thead className="bg-[var(--raised)]">
                <tr>
                  <AutoFilterTh controller={filter} colKey="executedAt" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="net" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="commission" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="transferToRider" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="fund" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="purpose" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="status" className="p-2 text-start" />
                </tr>
              </thead>
              <tbody>
                {filter.rows.map((row) => (
                  <tr key={String(row.id)} className="border-t border-[var(--line)]">
                    <td className="p-2">{String(row.executedAt || '').slice(0, 10)}</td>
                    <td className="p-2">{formatSar(Number(row.net || 0))}</td>
                    <td className="p-2">{formatSar(Number(row.commission || 0))}</td>
                    <td className="p-2">{formatSar(Number(row.transferToRider || 0))}</td>
                    <td className="p-2">{row.fund}</td>
                    <td className="p-2">{row.purpose}</td>
                    <td className="p-2">{row.status}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </div>
  );
}
