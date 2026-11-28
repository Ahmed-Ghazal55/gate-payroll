'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Camera } from 'lucide-react';
import { api, ApiError, type ApiResp } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { usePeriod } from '../../lib/use-period';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import type { CompanySponsor } from '../../lib/types';
import { formatSar } from '../../lib/money';

type Live = ApiResp<'/api/sal/erp-settlements/{app}/live'>;
type SettlementRow = NonNullable<Live['items']>[number];

export function ChefzSettlementsPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canView = can('pay.view') || isAdmin;
  const { period, setPeriod } = usePeriod();
  const [half, setHalf] = useState('all');
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [companyId, setCompanyId] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rows, setRows] = useState<SettlementRow[]>([]);
  const [allowed, setAllowed] = useState<string[]>([]);
  const [snapshotSafe, setSnapshotSafe] = useState(false);
  const [ruleAr, setRuleAr] = useState('');
  const [noteAr, setNoteAr] = useState('');
  const [totals, setTotals] = useState<Record<string, number>>({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      setCompanyId((prev) => prev || list[0]?.code || list[0]?.id || '');
    });
  }, []);

  const load = useCallback(async () => {
    if (!canView || !companyId) return;
    setLoading(true);
    setError(null);
    try {
      const live = await api.getErpSettlementsLive('chefz', { companyId, period, half });
      setRows(Array.isArray(live.items) ? (live.items as SettlementRow[]) : []);
      setAllowed(Array.isArray(live.allowedActions) ? live.allowedActions.map(String) : []);
      setSnapshotSafe(!!live.snapshotSafe);
      setRuleAr(String(live.snapshotSafetyRuleAr || ''));
      setNoteAr(String(live.noteAr || ''));
      const t = live.totals && typeof live.totals === 'object' ? live.totals : {};
      setTotals(Object.fromEntries(Object.entries(t).filter(([, v]) => typeof v === 'number')) as Record<string, number>);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل تصفيات شيفز');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [canView, companyId, period, half]);

  useEffect(() => {
    void load();
  }, [load]);

  const cols = useMemo(
    () => [
      { key: 'periodFrom', header: 'من', type: 'date' as const, getValue: (r: SettlementRow) => String(r.periodFrom || '').slice(0, 10) },
      { key: 'periodTo', header: 'إلى', type: 'date' as const, getValue: (r: SettlementRow) => String(r.periodTo || '').slice(0, 10) },
      { key: 'accountType', header: 'النوع', type: 'text' as const, getValue: (r: SettlementRow) => String(r.accountType || '') },
      { key: 'gross', header: 'الإجمالي', type: 'number' as const, getValue: (r: SettlementRow) => Number(r.gross || 0) },
      { key: 'companyAmount', header: 'حصة الشركة', type: 'number' as const, getValue: (r: SettlementRow) => Number(r.companyAmount || 0) },
      { key: 'riderAmount', header: 'حصة المندوب', type: 'number' as const, getValue: (r: SettlementRow) => Number(r.riderAmount || 0) },
      { key: 'completion', header: 'الاكتمال', type: 'text' as const, getValue: (r: SettlementRow) => String(r.completionLabelAr || '') },
    ],
    []
  );
  const filter = useTableAutoFilter(rows, cols);

  const takeSnapshot = async () => {
    if (!allowed.includes('snapshot')) {
      toast('اللقطة غير متاحة — يلزم تصفيات مكتملة ومقفلة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.takeErpSettlementSnapshot('chefz', { companyId, period });
      toast('تم أخذ لقطة عرض', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر أخذ اللقطة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض تصفيات شيفز يحتاج pay.view." />;
  }

  return (
    <div className="space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">تصفيات شيفز المكتملة (قراءة من نظام عمر)</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">{noteAr || 'عرض مباشر للتصفيات دون كتابة على نظام عمر.'}</p>
        </div>
        <div className="flex gap-2">
          <button type="button" onClick={() => void load()} className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs">
            <RefreshCw size={12} /> تحديث
          </button>
          <button type="button" disabled={busy || !allowed.includes('snapshot')} onClick={() => void takeSnapshot()} className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            <Camera size={12} /> لقطة عرض
          </button>
        </div>
      </div>

      <div className={`p-3 rounded-xl border text-xs ${snapshotSafe ? 'border-[var(--accent)]/40 bg-[var(--accent)]/10' : 'border-[var(--warning)]/40 bg-[var(--warning)]/10'}`}>
        <p className="font-bold mb-1">{snapshotSafe ? 'اللقطة آمنة لهذا النطاق' : 'اللقطة غير آمنة لهذا النطاق'}</p>
        <p>{ruleAr}</p>
        <p className="mt-1 text-[var(--muted)]">مكتملة ومقفلة: {Number(totals.completedClosed || 0)} — مفتوحة/ناقصة: {Number(totals.openOrIncomplete || 0)}</p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
        <SearchableSelect
          value={companyId}
          onChange={setCompanyId}
          options={companies.map((c) => ({ value: c.code || c.id || '', label: companyLabel(c) }))}
          placeholder="الشركة"
        />
        <label className="text-[11px] font-semibold space-y-1">
          <span>الشهر</span>
          <input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
        </label>
        <label className="text-[11px] font-semibold space-y-1">
          <span>النصف</span>
          <select value={half} onChange={(e) => setHalf(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
            <option value="all">الكل</option>
            <option value="1">الأول</option>
            <option value="2">الثاني</option>
          </select>
        </label>
      </div>

      {loading ? <Skeleton className="h-28 rounded-2xl" /> : null}
      {error ? <ErrorState title="تعذر التحميل" message={error} onRetry={load} /> : null}

      {!loading && !error ? (
        <>
          <div className="flex flex-wrap gap-3 text-xs">
            <span>إجمالي: <b>{formatSar(Number(totals.gross || 0))}</b></span>
            <span>شركة: <b>{formatSar(Number(totals.companyAmount || 0))}</b></span>
            <span>مندوب: <b>{formatSar(Number(totals.riderAmount || 0))}</b></span>
          </div>
          <AutoFilterBar controller={filter} totalCount={rows.length} />
          <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
            <table className="w-full text-xs">
              <thead className="bg-[var(--raised)]">
                <tr>
                  <AutoFilterTh controller={filter} colKey="periodFrom" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="periodTo" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="accountType" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="gross" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="companyAmount" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="riderAmount" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="completion" className="p-2 text-start" />
                </tr>
              </thead>
              <tbody>
                {filter.rows.map((row) => (
                  <tr key={String(row.id || row.erpRowId)} className="border-t border-[var(--line)]">
                    <td className="p-2">{String(row.periodFrom || '').slice(0, 10)}</td>
                    <td className="p-2">{String(row.periodTo || '').slice(0, 10)}</td>
                    <td className="p-2">{row.accountType}</td>
                    <td className="p-2">{formatSar(Number(row.gross || 0))}</td>
                    <td className="p-2">{formatSar(Number(row.companyAmount || 0))}</td>
                    <td className="p-2">{formatSar(Number(row.riderAmount || 0))}</td>
                    <td className="p-2">{row.completionLabelAr}</td>
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
