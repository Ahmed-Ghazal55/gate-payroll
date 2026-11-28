'use client';

import React, { useEffect, useMemo, useState } from 'react';
import { Eye, RefreshCw, Scale } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { ErrorState, Skeleton } from '../../components/ui/EmptyState';

type ComparisonRow = {
  invoiceId?: string;
  companyId?: string;
  accountId?: string;
  riderKey?: string | null;
  invoiceOrders?: number;
  invoiceBaseGross?: number | null;
  invoiceStatus?: string;
  expectedStatus?: string | null;
  expectedTier?: string | null;
  expectedCapacityTier?: string | null;
  deltaNoteAr?: string;
  match?: string;
};

type DisplayPayload = {
  app: string;
  period: string;
  displayOnly: boolean;
  affectsCalculation: boolean;
  acceptanceIsNorm: boolean;
  noteAr: string;
  erpNoteAr?: string;
  source?: string;
  erpExpected?: unknown[];
  approvedInvoices?: Array<{ id: string; companyId?: string; period?: string; approvalStatus?: string }>;
  comparison?: ComparisonRow[];
  localTermsArchive?: unknown[];
};

export function ParentTermsDisplayPanel({
  appSlug,
  appNameAr,
  period,
}: {
  appSlug: string;
  appNameAr: string;
  period: string;
}) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<DisplayPayload | null>(null);
  const [keetaExpected, setKeetaExpected] = useState<Array<Record<string, unknown>>>([]);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const display = await api.getParentTermsDisplay(appSlug, period);
      setData(display as DisplayPayload);
      if (appSlug === 'keeta') {
        try {
          const live = await api.getKeetaEligibility({ month: period, page: 1, pageSize: 200 });
          const items = (live as { items?: Array<Record<string, unknown>> })?.items ?? [];
          setKeetaExpected(items);
        } catch {
          setKeetaExpected([]);
        }
      } else {
        setKeetaExpected([]);
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل عرض الشروط الأصلية.');
      setData(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [appSlug, period]);

  const comparisonRows = useMemo(() => {
    const base = (data?.comparison ?? []).map((r, i) => ({
      id: `${r.invoiceId ?? 'inv'}-${r.accountId ?? i}`,
      invoiceId: r.invoiceId ?? '—',
      companyId: r.companyId ?? '—',
      accountId: r.accountId ?? '—',
      riderKey: r.riderKey ?? '—',
      invoiceOrders: r.invoiceOrders ?? 0,
      expectedStatus: r.expectedStatus ?? '—',
      expectedTier: r.expectedTier ?? '—',
      note: r.deltaNoteAr ?? 'مراجعة فقط',
    }));
    // Enrich from live Keeta expected rows by account when available (display only).
    if (keetaExpected.length === 0) return base;
    return base.map((row) => {
      const hit = keetaExpected.find(
        (e) =>
          String(e.platformAccountId ?? e.accountId ?? '') === row.accountId ||
          String(e.reportIdNumber ?? '') === row.riderKey
      );
      if (!hit) return row;
      return {
        ...row,
        expectedStatus: String(hit.expectedStatus ?? hit.status ?? row.expectedStatus),
        expectedTier: String(hit.expectedExperienceTier ?? hit.expectedCapacityTier ?? row.expectedTier),
      };
    });
  }, [data, keetaExpected]);

  const filter = useTableAutoFilter(comparisonRows, [
    { key: 'accountId', header: 'اليوزر / الحساب', getValue: (r) => r.accountId },
    { key: 'riderKey', header: 'المندوب / الهوية', getValue: (r) => r.riderKey },
    { key: 'invoiceOrders', header: 'طلبات الفاتورة', getValue: (r) => r.invoiceOrders, type: 'number' },
    { key: 'expectedStatus', header: 'الصلاحية المتوقعة', getValue: (r) => r.expectedStatus },
    { key: 'expectedTier', header: 'الشريحة المتوقعة', getValue: (r) => r.expectedTier },
    { key: 'invoiceId', header: 'الفاتورة', getValue: (r) => r.invoiceId },
    { key: 'note', header: 'ملاحظة', getValue: (r) => r.note },
  ]);

  if (loading) {
    return (
      <div className="p-6 space-y-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (error) {
    return <ErrorState title="تعذر تحميل الشروط الأصلية" message={error} onRetry={load} />;
  }

  return (
    <div className="space-y-5">
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-2 max-w-3xl">
          <h3 className="text-base sm:text-lg font-bold text-[var(--ink)] inline-flex items-center gap-2">
            <Eye size={18} />
            الشروط الأصلية · {appNameAr} · {period}
          </h3>
          <p className="text-xs text-[var(--muted)] leading-relaxed">
            {data?.noteAr ||
              'عرض فقط من نظام عمر: الصلاحية المتوقعة والمستويات والشروط. لا يؤثر على الاحتساب. الفاتورة المعتمدة هي المرجع؛ القبول هو الأصل.'}
          </p>
          {data?.erpNoteAr && <p className="text-[11px] text-[var(--info)]">{data.erpNoteAr}</p>}
          <div className="flex flex-wrap gap-2 text-[11px]">
            <span className="px-2 py-0.5 rounded-full bg-[var(--raised)] font-bold">عرض فقط</span>
            <span className="px-2 py-0.5 rounded-full bg-[var(--money-pos)]/15 text-[var(--money-pos)] font-bold">
              لا يؤثر على الاحتساب
            </span>
            <span className="px-2 py-0.5 rounded-full bg-[var(--accent)]/15 text-[var(--accent)] font-bold inline-flex items-center gap-1">
              <Scale size={12} />
              القبول هو الأصل
            </span>
          </div>
        </div>
        <button
          type="button"
          onClick={() => void load()}
          className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer"
        >
          <RefreshCw size={14} />
          تحديث
        </button>
      </div>

      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <h4 className="text-sm font-bold text-[var(--ink)]">مقارنة الصلاحية المتوقعة × الفاتورة المعتمدة</h4>
        <p className="text-[11px] text-[var(--muted)]">
          الفروقات للمراجعة وتحسين التوقع فقط. لا اعتراض تلقائي — القبول هو الأصل.
        </p>
        <AutoFilterBar controller={filter} />
        <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
          <table className="min-w-full text-xs">
            <thead className="bg-[var(--raised)]">
              <tr>
                <AutoFilterTh controller={filter} colKey="accountId" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="riderKey" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="invoiceOrders" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="expectedStatus" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="expectedTier" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="invoiceId" className="py-2.5 px-3 text-start" />
                <AutoFilterTh controller={filter} colKey="note" className="py-2.5 px-3 text-start" />
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {filter.rows.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-10 text-center text-[var(--muted)]">
                    لا توجد فاتورة معتمدة لهذا الشهر للمقارنة بعد. عند توفر الفاتورة والصلاحية المتوقعة من نظام عمر
                    تظهر هنا للمراجعة فقط — بلا مانع على الاحتساب.
                  </td>
                </tr>
              ) : (
                filter.rows.map((row) => (
                  <tr key={row.id} className="hover:bg-[var(--row-hover)]">
                    <td className="py-2 px-3 font-mono">{row.accountId}</td>
                    <td className="py-2 px-3 font-mono">{row.riderKey}</td>
                    <td className="py-2 px-3 font-mono num">{row.invoiceOrders}</td>
                    <td className="py-2 px-3">{row.expectedStatus}</td>
                    <td className="py-2 px-3">{row.expectedTier}</td>
                    <td className="py-2 px-3 font-mono text-[10px]">{row.invoiceId}</td>
                    <td className="py-2 px-3 text-[var(--muted)]">{row.note}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {appSlug === 'keeta' && keetaExpected.length > 0 && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
          <h4 className="text-sm font-bold">صلاحية كيتا المتوقعة من نظام عمر ({keetaExpected.length} صف)</h4>
          <p className="text-[11px] text-[var(--muted)]">متوقعة فقط — ليست اعتماداً نهائياً.</p>
        </div>
      )}
    </div>
  );
}
