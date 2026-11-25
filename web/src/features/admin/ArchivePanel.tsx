'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';

type PolicyRow = {
  id?: string;
  code?: string;
  nameAr?: string;
  retentionYears?: number;
  hardDeleteAllowed?: boolean;
  restoreViewHours?: number;
};

type RestoreRow = {
  id?: string;
  entityType?: string;
  periodFrom?: string;
  periodTo?: string;
  reasonAr?: string;
  status?: string;
  expiresAt?: string;
  approvalRequestId?: string;
  readOnly?: boolean;
};

const TABS: SectionTabItem[] = [
  { id: 'policies', label: 'سياسات الاحتفاظ' },
  { id: 'requests', label: 'طلبات استرجاع العرض' },
];

export function ArchivePanel() {
  const { toast } = useToast();
  const [subtab, setSubtab] = useState('policies');
  const [policies, setPolicies] = useState<PolicyRow[]>([]);
  const [restores, setRestores] = useState<RestoreRow[]>([]);
  const [messageAr, setMessageAr] = useState('');
  const [allowed, setAllowed] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    entityType: 'audit',
    periodFrom: '',
    periodTo: '',
    reasonAr: '',
  });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getArchivePolicies();
      setPolicies(Array.isArray(res?.items) ? (res.items as PolicyRow[]) : []);
      setRestores(Array.isArray(res?.restoreRequests) ? (res.restoreRequests as RestoreRow[]) : []);
      setMessageAr(typeof res?.messageAr === 'string' ? res.messageAr : '');
      setAllowed(Array.isArray(res?.allowedActions) ? (res.allowedActions as string[]) : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل سياسات الأرشفة');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const submitRestore = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!allowed.includes('request_restore')) {
      toast('لا تملك صلاحية طلب استرجاع الأرشيف', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.requestArchiveRestore({
        entityType: form.entityType.trim(),
        periodFrom: form.periodFrom.trim(),
        periodTo: form.periodTo.trim(),
        reasonAr: form.reasonAr.trim(),
      });
      toast('طُلبت استعادة عرض مؤقت؛ بانتظار اعتماد مستقل', 'success');
      setForm((p) => ({ ...p, reasonAr: '' }));
      await load();
      setSubtab('requests');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر إرسال طلب الاسترجاع', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const policyColumns: ColumnDef<PolicyRow>[] = [
    { key: 'code', header: 'الرمز', filterable: true, render: (r) => r.code || '—' },
    { key: 'nameAr', header: 'السياسة', filterable: true, render: (r) => r.nameAr || '—' },
    {
      key: 'retentionYears',
      header: 'سنوات الاحتفاظ',
      numeric: true,
      render: (r) => String(r.retentionYears ?? 3),
    },
    {
      key: 'restoreViewHours',
      header: 'ساعات عرض الاسترجاع',
      numeric: true,
      render: (r) => String(r.restoreViewHours ?? 72),
    },
    {
      key: 'hardDelete',
      header: 'حذف نهائي',
      render: () => 'ممنوع',
    },
  ];

  const restoreColumns: ColumnDef<RestoreRow>[] = [
    { key: 'entityType', header: 'نوع الكيان', filterable: true, render: (r) => r.entityType || '—' },
    {
      key: 'period',
      header: 'الفترة',
      render: (r) => `${r.periodFrom || '—'} → ${r.periodTo || '—'}`,
    },
    { key: 'status', header: 'الحالة', filterable: true, render: (r) => r.status || '—' },
    { key: 'reasonAr', header: 'السبب', filterable: true, render: (r) => r.reasonAr || '—' },
    { key: 'expiresAt', header: 'ينتهي', render: (r) => r.expiresAt || '—' },
  ];

  if (loading) return <Skeleton rows={5} />;
  if (error) return <ErrorState message={error} onRetry={() => void load()} />;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionTabs tabs={TABS} activeTab={subtab} onChange={setSubtab} paramName="subtab" ariaLabel="تبويبات الأرشفة" />
        <button
          type="button"
          className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2.5 py-1.5 text-xs font-semibold"
          onClick={() => void load()}
        >
          <RefreshCw size={14} /> تحديث
        </button>
      </div>

      {messageAr && <p className="text-xs text-[var(--muted)]">{messageAr}</p>}

      {subtab === 'policies' && (
        policies.length === 0 ? (
          <EmptyState title="لا سياسات أرشفة" description="تُنشأ سياسة الاحتفاظ الافتراضية عند تهيئة النظام." />
        ) : (
          <DataTable columns={policyColumns} data={policies} getRowId={(r) => String(r.id || r.code || '')} />
        )
      )}

      {subtab === 'requests' && (
        <div className="space-y-4">
          {allowed.includes('request_restore') && (
            <form
              onSubmit={submitRestore}
              className="grid gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:grid-cols-2"
            >
              <label className="text-xs font-semibold sm:col-span-2">
                نوع الكيان
                <input
                  className="mt-1 w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm"
                  value={form.entityType}
                  onChange={(e) => setForm((p) => ({ ...p, entityType: e.target.value }))}
                  required
                />
              </label>
              <label className="text-xs font-semibold">
                من تاريخ
                <input
                  type="date"
                  className="mt-1 w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm num"
                  value={form.periodFrom}
                  onChange={(e) => setForm((p) => ({ ...p, periodFrom: e.target.value }))}
                  required
                />
              </label>
              <label className="text-xs font-semibold">
                إلى تاريخ
                <input
                  type="date"
                  className="mt-1 w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm num"
                  value={form.periodTo}
                  onChange={(e) => setForm((p) => ({ ...p, periodTo: e.target.value }))}
                  required
                />
              </label>
              <label className="text-xs font-semibold sm:col-span-2">
                سبب الاسترجاع (عرض قراءة فقط بعد الاعتماد)
                <textarea
                  className="mt-1 w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm"
                  rows={2}
                  value={form.reasonAr}
                  onChange={(e) => setForm((p) => ({ ...p, reasonAr: e.target.value }))}
                  required
                  minLength={3}
                />
              </label>
              <button
                type="submit"
                disabled={busy}
                className="rounded-xl bg-[var(--accent)] px-3 py-2 text-xs font-bold text-[var(--accent-ink)] sm:col-span-2 disabled:opacity-50"
              >
                طلب استرجاع عرض مؤقت
              </button>
            </form>
          )}

          {restores.length === 0 ? (
            <EmptyState
              title="لا طلبات استرجاع"
              description="الطلبات تخص بيانات أقدم من ثلاث سنوات فقط، والعرض مؤقت بعد الاعتماد."
            />
          ) : (
            <DataTable columns={restoreColumns} data={restores} getRowId={(r) => String(r.id || '')} />
          )}
        </div>
      )}
    </div>
  );
}
