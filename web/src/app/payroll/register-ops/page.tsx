'use client';

import React, { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { RefreshCw } from 'lucide-react';
import { api, ApiError, type RegisterOpsLiveRow } from '../../../lib/api';
import { PageHeader } from '../../../components/ui/PageHeader';
import { DataTable, ColumnDef } from '../../../components/ui/DataTable';
import { SearchableSelect } from '../../../components/ui/SearchableSelect';
import { Skeleton, ErrorState } from '../../../components/ui/EmptyState';
import { SourceBadge } from '../../../components/ui/SourceBadge';
import { useToast } from '../../../components/ui/Toast';
import { appendPeriodToHref } from '../../../lib/period';
import { usePeriod } from '../../../lib/use-period';

type AppCode = 'keeta' | 'ninja';
type RegisterCode = 'BW' | 'EX' | 'TR';
type OpsRow = RegisterOpsLiveRow & Record<string, unknown>;

const REGISTERS: { value: RegisterCode; label: string }[] = [
  { value: 'BW', label: 'سجل البوابا' },
  { value: 'EX', label: 'سجل إكسبرس' },
  { value: 'TR', label: 'سجل التجارية' },
];

const APPS: { value: AppCode; label: string }[] = [
  { value: 'ninja', label: 'نينجا' },
  { value: 'keeta', label: 'كيتا' },
];

const NUMERIC = new Set([
  'الأيام الصالحة',
  'الطلبات',
  'المخالفات',
  'المسافة المدفوعة',
  'عدد ايام عمل المندوب',
  'اجمالي المسافه',
  'اجمالي طلبات المعادلة',
  'اجمالي طلبات اليوزر',
  'عدد البدلاء',
  'اجمالي ايام المعادلة',
  'اجمالي ايام اليوزر',
]);

function defaultRange(app: AppCode): { from: string; to: string } {
  if (app === 'ninja') return { from: '2026-10-01', to: '2026-10-31' };
  const today = new Date();
  const y = today.getFullYear();
  const m = String(today.getMonth() + 1).padStart(2, '0');
  const d = String(today.getDate()).padStart(2, '0');
  return { from: '2026-11-01', to: `${y}-${m}-${d}` };
}

function RegisterOpsInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { toast } = useToast();
  const { urlPeriod } = usePeriod();

  const app = (searchParams?.get('app') === 'keeta' ? 'keeta' : 'ninja') as AppCode;
  const register = (['BW', 'EX', 'TR'].includes(searchParams?.get('register') || '')
    ? searchParams!.get('register')!
    : 'BW') as RegisterCode;
  const page = Math.max(1, parseInt(searchParams?.get('page') || '1', 10) || 1);
  const pageSize = [50, 100, 200].includes(parseInt(searchParams?.get('size') || '100', 10))
    ? parseInt(searchParams?.get('size') || '100', 10)
    : 100;
  const defaults = defaultRange(app);
  const from = searchParams?.get('from') || defaults.from;
  const to = searchParams?.get('to') || defaults.to;

  const [rows, setRows] = useState<OpsRow[]>([]);
  const [columns, setColumns] = useState<string[]>([]);
  const [total, setTotal] = useState(0);
  const [counts, setCounts] = useState({ rows: 0, dismissed: 0, duplicateIdRows: 0, uniqueIds: 0 });
  const [missingDays, setMissingDays] = useState<string[]>([]);
  const [source, setSource] = useState('erp_live');
  const [asOf, setAsOf] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [allowedActions, setAllowedActions] = useState<string[]>([]);

  const setQuery = useCallback(
    (patch: Record<string, string | number | undefined>) => {
      const next = new URLSearchParams(searchParams?.toString() || '');
      for (const [k, v] of Object.entries(patch)) {
        if (v === undefined || v === '') next.delete(k);
        else next.set(k, String(v));
      }
      const href = `/payroll/register-ops/?${next.toString()}`;
      router.replace(urlPeriod ? appendPeriodToHref(href, urlPeriod) : href);
    },
    [router, searchParams, urlPeriod],
  );

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    api
      .getRegisterOpsLive({ app, register, from, to, page, pageSize })
      .then((res) => {
        setRows((res.displayRows || res.items || []) as OpsRow[]);
        setColumns(res.columns || []);
        setTotal(res.total || 0);
        setCounts(res.counts || { rows: 0, dismissed: 0, duplicateIdRows: 0, uniqueIds: 0 });
        setMissingDays(res.coverage?.missingDays || []);
        setSource(res.source || 'erp_live');
        setAsOf(res.asOf || '');
        setAllowedActions(res.allowedActions || []);
      })
      .catch((e: unknown) => {
        const msg = e instanceof ApiError ? e.message : 'تعذر قراءة تشغيل السجل من المصدر الحي.';
        setError(msg);
        toast(msg, 'danger');
      })
      .finally(() => setLoading(false));
  }, [app, register, from, to, page, pageSize, toast]);

  useEffect(() => {
    load();
  }, [load]);

  const tableColumns: ColumnDef<OpsRow>[] = useMemo(() => {
    const headers = columns.length > 0 ? columns : [];
    return headers.map((h) => ({
      key: h,
      header: h,
      type: NUMERIC.has(h) ? ('number' as const) : ('text' as const),
      numeric: NUMERIC.has(h),
      filterable: true,
      sortable: true,
      getValue: (row) => row[h],
      render: (row) => {
        const v = row[h];
        if (v === null || v === undefined || v === '') return '—';
        return String(v);
      },
    }));
  }, [columns]);

  return (
    <div className="space-y-4" dir="rtl">
      <PageHeader
        title="تشغيل السجلات (حي من نظام عمر)"
        description="جدول واحد لكل سجل بنفس أعمدة ورقة التشغيل. الصلاحية متوقعة فقط — النهائي من الفاتورة. بدون لقطات مخزّنة عند القراءة."
        primaryAction={
          allowedActions.includes('refresh')
            ? { label: 'تحديث', onClick: load, icon: <RefreshCw size={16} /> }
            : undefined
        }
      />

      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-sm">
          <span>التطبيق</span>
          <SearchableSelect
            value={app}
            onChange={(v) => {
              const nextApp = (v === 'keeta' ? 'keeta' : 'ninja') as AppCode;
              const range = defaultRange(nextApp);
              setQuery({ app: nextApp, from: range.from, to: range.to, page: 1 });
            }}
            options={APPS}
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>السجل</span>
          <SearchableSelect
            value={register}
            onChange={(v) => setQuery({ register: v, page: 1 })}
            options={REGISTERS}
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>من</span>
          <input
            type="date"
            value={from}
            onChange={(e) => setQuery({ from: e.target.value, page: 1 })}
            className="rounded-md border border-[var(--line)] bg-[var(--surface)] px-2 py-2"
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span>إلى</span>
          <input
            type="date"
            value={to}
            onChange={(e) => setQuery({ to: e.target.value, page: 1 })}
            className="rounded-md border border-[var(--line)] bg-[var(--surface)] px-2 py-2"
          />
        </label>
        <SourceBadge source={source} asOf={asOf} />
      </div>

      <div className="flex flex-wrap gap-4 text-sm text-[var(--muted)]">
        <span>الصفوف: {counts.rows}</span>
        <span>المفصول: {counts.dismissed}</span>
        <span>المعرّفات: {counts.uniqueIds}</span>
        <span>تكرار المعرف: {counts.duplicateIdRows}</span>
        <span>
          أيام بلا بيانات: {missingDays.length}
          {missingDays.length > 0 ? ` (${missingDays.slice(0, 8).join('، ')}${missingDays.length > 8 ? '…' : ''})` : ''}
        </span>
      </div>

      {loading && !rows.length ? (
        <Skeleton rows={8} />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={load} />
      ) : (
        <DataTable
          tableId={`register-ops-${app}-${register}`}
          columns={tableColumns}
          data={rows}
          totalCount={total}
          loading={loading}
          page={page}
          pageSize={pageSize}
          onPageChange={(p) => setQuery({ page: p })}
          onPageSizeChange={(s) => setQuery({ size: s, page: 1 })}
          getRowId={(row) =>
            String(
              (row as OpsRow & { assignmentId?: string }).assignmentId ||
                `${row['المعرف']}:${row['رقم الهوية1']}:${row.row}`,
            )
          }
          rowClassName={(row) =>
            row.dismissed ? '!bg-red-700 !text-white hover:!bg-red-800' : undefined
          }
          emptyTitle="لا صفوف تشغيل في الفترة"
          emptyDescription="تحقق من اكتمال بيانات التشغيل في نظام عمر لهذا السجل."
          searchable
          exportable
          exportFilename={`register-ops-${app}-${register}-${from}-${to}`}
        />
      )}
    </div>
  );
}

export default function RegisterOpsPage() {
  return (
    <Suspense fallback={<Skeleton rows={6} />}>
      <RegisterOpsInner />
    </Suspense>
  );
}
