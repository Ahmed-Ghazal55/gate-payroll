'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { FileSpreadsheet, RefreshCw, Users, CheckCircle2, Layers } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { usePeriod } from '../../lib/use-period';
import type { CompanySponsor, MudadUploadRow } from '../../lib/types';
import { DataTable, type ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { StatCard } from '../../components/ui/StatCard';
import { ErrorState, EmptyState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';

function mudadFileKindLabel(kind: string): string {
  if (kind === 'salaries') return 'ملف رواتب مدد';
  if (kind === 'bank_accounts') return 'حسابات بنكية مدد';
  return kind || '—';
}

function mudadStatusLabel(status: number): string {
  if (status === 1) return 'محمّل';
  if (status === 2) return 'مستبدل';
  if (status === 3) return 'مرفوض';
  return String(status);
}

export function MudadPanel() {
  const { can, isAdmin } = useAuth();
  const { period, setPeriod } = usePeriod();
  const canView = isAdmin || can('people.mudad.manage') || can('pay.view');

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [companyId, setCompanyId] = useState<string>('all');
  const [rows, setRows] = useState<MudadUploadRow[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .getFinanceSponsors()
      .then((list) => setCompanies(Array.isArray(list) ? list : []))
      .catch(() => setCompanies([]));
  }, []);

  const loadUploads = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const res = await api.getMudadUploads({
        companyId: companyId !== 'all' ? companyId : undefined,
        periodMonth: period || undefined,
      });
      setRows(Array.isArray(res?.rows) ? res.rows : []);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل دفعات رواتب مدد');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [canView, companyId, period]);

  useEffect(() => {
    void loadUploads();
  }, [loadUploads]);

  const companyName = useCallback(
    (cid: string) => {
      const m = companies.find((c) => c.id === cid || c.code === cid);
      return m ? `${m.nameAr || m.shortNameAr || cid} (${m.code})` : cid || '—';
    },
    [companies]
  );

  const summary = useMemo(() => {
    const totalRows = rows.reduce((acc, r) => acc + Number(r.rowCount || 0), 0);
    const salariesCount = rows.filter((r) => r.fileKind === 'salaries').length;
    const bankCount = rows.filter((r) => r.fileKind === 'bank_accounts').length;
    return { batches: rows.length, totalRows, salariesCount, bankCount };
  }, [rows]);

  const columns: ColumnDef<MudadUploadRow>[] = useMemo(
    () => [
      {
        key: 'fileName',
        header: 'اسم الملف',
        type: 'text',
        getValue: (r) => r.fileName || '',
        render: (r) => <span className="font-semibold text-[var(--ink)]">{r.fileName || '—'}</span>,
      },
      {
        key: 'companyId',
        header: 'الشركة',
        type: 'list',
        getValue: (r) => r.companyId || '',
        labelOf: (v) => companyName(String(v || '')),
        render: (r) => <span className="whitespace-nowrap">{companyName(r.companyId)}</span>,
      },
      {
        key: 'periodMonth',
        header: 'الفترة',
        type: 'text',
        getValue: (r) => (r.periodMonth || '').slice(0, 7),
        render: (r) => (
          <span className="num font-mono text-xs">{(r.periodMonth || '').slice(0, 7) || '—'}</span>
        ),
      },
      {
        key: 'fileKind',
        header: 'نوع الدفعة',
        type: 'list',
        getValue: (r) => r.fileKind || '',
        labelOf: (v) => mudadFileKindLabel(String(v || '')),
        render: (r) => <span>{mudadFileKindLabel(r.fileKind)}</span>,
      },
      {
        key: 'rowCount',
        header: 'عدد الموظفين',
        type: 'number',
        numeric: true,
        getValue: (r) => Number(r.rowCount || 0),
        render: (r) => <span className="num font-bold">{Number(r.rowCount || 0)}</span>,
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list',
        getValue: (r) => String(r.status ?? ''),
        labelOf: (v) => mudadStatusLabel(Number(v)),
        toneOf: (v) => defaultStatusTone(Number(v) === 1 ? 'active' : Number(v) === 3 ? 'rejected' : 'neutral'),
        render: (r) => <span>{mudadStatusLabel(Number(r.status))}</span>,
      },
      {
        key: 'uploadedBy',
        header: 'المستخدم',
        type: 'list',
        getValue: (r) => r.uploadedBy || '',
        render: (r) => <span className="text-xs text-[var(--muted)]">{r.uploadedBy || '—'}</span>,
      },
      {
        key: 'uploadedAt',
        header: 'تاريخ الرفع',
        type: 'date',
        getValue: (r) => (r.uploadedAt || '').slice(0, 10),
        render: (r) => (
          <span className="num text-xs text-[var(--muted)]">
            {(r.uploadedAt || '').slice(0, 10) || '—'}
          </span>
        ),
      },
    ],
    [companyName]
  );

  if (!canView) {
    return (
      <EmptyState
        title="صلاحية غير كافية"
        description="عرض دفعات رواتب مدد يتطلب صلاحية إدارة مدد أو عرض الرواتب."
      />
    );
  }

  return (
    <div className="space-y-6" data-testid="mudad-panel">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="دفعات مدد المرفوعة"
          value={summary.batches}
          hint={`للفترة ${period}`}
          icon={FileSpreadsheet}
        />
        <StatCard
          label="إجمالي أسطر مدد"
          value={summary.totalRows}
          hint="موظفون مسجّلون في الدفعات"
          icon={Users}
        />
        <StatCard
          label="ملفات رواتب مدد"
          value={summary.salariesCount}
          hint="مطابقة الالتزام والحد الأدنى"
          icon={CheckCircle2}
        />
        <StatCard
          label="ملفات الحسابات البنكية"
          value={summary.bankCount}
          hint="توثيق الآيبان المعتمد في مدد"
          icon={Layers}
        />
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <div className="flex flex-wrap items-center gap-2">
          <SearchableSelect
            id="mudad-company-filter"
            value={companyId}
            onChange={setCompanyId}
            ariaLabel="تصفية الشركة"
            placeholder="كافة الشركات"
            size="sm"
            className="w-56"
            options={[
              { value: 'all', label: 'كافة الشركات' },
              ...companies.map((c) => ({
                value: c.id,
                label: `${c.nameAr || c.shortNameAr || c.code} (${c.code})`,
              })),
            ]}
          />
          <input
            type="month"
            value={period}
            onChange={(e) => setPeriod(e.target.value)}
            aria-label="فترة مدد"
            className="px-3 py-1.5 text-xs rounded-xl bg-[var(--raised)] border border-[var(--line)] text-[var(--ink)] num"
          />
        </div>
        <button
          type="button"
          onClick={() => void loadUploads()}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--line)] transition"
        >
          <RefreshCw size={14} />
          <span>تحديث</span>
        </button>
      </div>

      {error ? (
        <ErrorState title="تعذر تحميل دفعات مدد" message={error} onRetry={loadUploads} />
      ) : (
        <DataTable
          tableId="payroll-mudad-uploads"
          columns={columns}
          data={rows}
          loading={loading}
          emptyTitle="لا توجد دفعات مدد للفترة المحددة"
          emptyDescription="لم تُرفع ملفات رواتب مدد أو حسابات بنكية لهذه الشركة والشهر بعد."
          exportFilename={`mudad_uploads_${period}`}
          getRowId={(r) => r.id}
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}
