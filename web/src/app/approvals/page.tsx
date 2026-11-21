'use client';

import React, { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { CheckCircle2, XCircle, PauseCircle, RefreshCw, ExternalLink } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { StatCard } from '../../components/ui/StatCard';
import { formatSar } from '../../lib/money';
import type { ApprovalRequest } from '../../lib/types';

const TABS: SectionTabItem[] = [
  { id: 'all', label: 'الكل' },
  { id: 'payments', label: 'السلف والمدفوعات' },
  { id: 'salaries', label: 'إيقاف وخصم الرواتب' },
  { id: 'debts', label: 'المديونيات والإعفاءات' },
  { id: 'erp_requests', label: 'طلبات نظام الموارد' },
  { id: 'other', label: 'أخرى' },
];

const STATUS_AR: Record<string, string> = {
  pending: 'بانتظار الاعتماد',
  escalated: 'مُصعَّد',
  approved: 'معتمد',
  rejected: 'مرفوض',
  suspended: 'معلّق',
  cancelled: 'ملغى',
};

const MAKER_CHECKER_AR =
  'شرط الشخصين: لا يعتمد مقدّم الطلب نفسُه؛ يلزم مراجع مستقل أو منح تجاوز مؤرخ.';

function ApprovalsInner() {
  const { can } = useAuth();
  const { toast } = useToast();
  const router = useRouter();
  const searchParams = useSearchParams();
  const tab = searchParams?.get('tab') || 'all';

  const [rows, setRows] = useState<ApprovalRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState('pending');
  const [mineOnly, setMineOnly] = useState(true);
  const [decideRow, setDecideRow] = useState<ApprovalRequest | null>(null);
  const [decision, setDecision] = useState<'approve' | 'reject' | 'suspended'>('approve');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);

  const setTab = (id: string) => {
    const q = new URLSearchParams(searchParams?.toString() || '');
    q.set('tab', id);
    router.replace(`/approvals/?${q.toString()}`);
  };

  const load = useCallback(async () => {
    if (!can('approvals.view')) {
      setError('لا تملك صلاحية عرض الاعتمادات');
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const module = tab === 'all' ? undefined : tab;
      const list = await api.getApprovals({
        module,
        status: statusFilter === 'all' ? undefined : statusFilter,
        mineOnly,
      });
      setRows(Array.isArray(list) ? list : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل الاعتمادات');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [can, tab, statusFilter, mineOnly]);

  useEffect(() => {
    void load();
  }, [load]);

  const kpis = useMemo(() => {
    const pendingMine = rows.filter((r) => r.status === 'pending' || r.status === 'escalated').length;
    const escalated = rows.filter((r) => r.status === 'escalated' || r.escalated).length;
    const approvedToday = rows.filter((r) => r.status === 'approved').length;
    const rejectedToday = rows.filter((r) => r.status === 'rejected').length;
    return { pendingMine, escalated, approvedToday, rejectedToday };
  }, [rows]);

  const openDecide = (row: ApprovalRequest, next: 'approve' | 'reject' | 'suspended') => {
    const actions = row.allowedActions ?? [];
    const needed = next === 'suspended' ? 'suspend' : next;
    if (!actions.includes(needed) && !actions.includes(next)) {
      const blocker = row.approvalBlockers?.[0] || MAKER_CHECKER_AR;
      toast(blocker, 'warning');
      return;
    }
    setDecideRow(row);
    setDecision(next);
    setNote('');
  };

  const submitDecision = async () => {
    if (!decideRow) return;
    if ((decision === 'reject' || decision === 'suspended') && note.trim().length < 3) {
      toast('سبب الرفض أو التعليق مطلوب (٣ أحرف على الأقل)', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.decideApproval(decideRow.id, {
        decision,
        note: note.trim() || undefined,
        reason: note.trim() || undefined,
      });
      toast(
        decision === 'approve' ? 'تم الاعتماد وتسجيل القرار في التدقيق' : decision === 'reject' ? 'تم الرفض' : 'تم التعليق',
        'success'
      );
      setDecideRow(null);
      await load();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'تعذر تسجيل القرار';
      toast(msg.includes('شخص') || msg.includes('مستقل') ? MAKER_CHECKER_AR : msg, 'danger');
    } finally {
      setBusy(false);
    }
  };

  const columns: ColumnDef<ApprovalRequest>[] = [
    {
      key: 'requestNo',
      header: 'رقم الطلب',
      render: (r) => <span className="num font-semibold">{r.requestNo || r.id.slice(0, 8)}</span>,
    },
    {
      key: 'kind',
      header: 'الوحدة والنوع',
      filterable: true,
      render: (r) => (
        <div className="space-y-0.5">
          <div className="font-semibold">{r.requestTypeAr || r.requestType || r.kind || '—'}</div>
          <div className="text-[11px] text-[var(--muted)]">{r.module || r.targetDepartment || '—'}</div>
        </div>
      ),
    },
    {
      key: 'description',
      header: 'الوصف والمرجع',
      render: (r) => (
        <div className="space-y-0.5 max-w-xs">
          <div className="truncate">{r.descriptionAr || r.reason || '—'}</div>
          {r.sourceHref ? (
            <a href={r.sourceHref} className="inline-flex items-center gap-1 text-[11px] text-[var(--accent)]">
              فتح السجل الأصلي <ExternalLink size={12} />
            </a>
          ) : r.referenceLabel ? (
            <span className="text-[11px] text-[var(--muted)]">{r.referenceLabel}</span>
          ) : null}
        </div>
      ),
    },
    {
      key: 'amount',
      header: 'المبلغ',
      money: true,
      numeric: true,
      render: (r) => (r.amount != null ? formatSar(r.amount) : '—'),
    },
    {
      key: 'rank',
      header: 'القسم والرتبة',
      render: (r) => (
        <span className="text-xs">
          {r.targetDepartment || '—'}
          {r.requiredRankLevel != null ? ` · رتبة ${r.requiredRankLevel}` : ''}
        </span>
      ),
    },
    {
      key: 'requestedBy',
      header: 'مقدم الطلب',
      filterable: true,
      render: (r) => (
        <div className="space-y-0.5">
          <div>{r.requestedBy}</div>
          <div className="num text-[11px] text-[var(--muted)]">
            {r.requestedAt ? new Date(r.requestedAt).toLocaleString('ar-SA') : '—'}
          </div>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'الحالة',
      filterable: true,
      render: (r) => (
        <span className="text-xs font-semibold">{STATUS_AR[r.status] || r.status}</span>
      ),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      render: (r) => {
        const actions = r.allowedActions ?? [];
        return (
          <div className="flex flex-wrap gap-1">
            {actions.includes('approve') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md bg-[var(--money-pos)]/15 text-[var(--money-pos)] px-2 py-1 text-[11px] font-bold"
                onClick={() => openDecide(r, 'approve')}
              >
                <CheckCircle2 size={12} /> اعتماد
              </button>
            )}
            {actions.includes('reject') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md bg-[var(--danger)]/15 text-[var(--danger)] px-2 py-1 text-[11px] font-bold"
                onClick={() => openDecide(r, 'reject')}
              >
                <XCircle size={12} /> رفض
              </button>
            )}
            {actions.includes('suspend') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md bg-[var(--warning)]/15 text-[var(--warning)] px-2 py-1 text-[11px] font-bold"
                onClick={() => openDecide(r, 'suspended')}
              >
                <PauseCircle size={12} /> تعليق
              </button>
            )}
            {!actions.length && (r.approvalBlockers?.[0] || MAKER_CHECKER_AR) ? (
              <span className="text-[10px] text-[var(--muted)] max-w-[12rem] leading-snug">
                {r.approvalBlockers?.[0] || MAKER_CHECKER_AR}
              </span>
            ) : null}
          </div>
        );
      },
    },
  ];

  if (!can('approvals.view')) {
    return <EmptyState title="لا صلاحية" description="عرض الاعتمادات يتطلب صلاحية الاعتمادات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="الاعتمادات"
        description="صندوق اعتماد موحّد للسلف والإيقاف والخصم وتخفيض المديونية وطلبات صرف الأموال بشخصين."
        moreActions={[{ label: 'تحديث', onClick: () => void load(), icon: <RefreshCw size={14} /> }]}
      />

      <SectionTabs tabs={TABS} activeTab={tab} onChange={setTab} ariaLabel="تبويبات الاعتمادات" />

      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatCard label="بانتظار اعتمادي الآن" value={String(kpis.pendingMine)} />
        <StatCard label="متأخرة / مُصعَّدة" value={String(kpis.escalated)} />
        <StatCard label="معتمدة (في القائمة)" value={String(kpis.approvedToday)} />
        <StatCard label="مرفوضة (في القائمة)" value={String(kpis.rejectedToday)} />
      </div>

      <div className="flex flex-wrap gap-3 items-end">
        <div className="min-w-48">
          <SearchableSelect
            ariaLabel="حالة الاعتماد"
            value={statusFilter}
            onChange={setStatusFilter}
            options={[
              { value: 'pending', label: 'بانتظار الاعتماد' },
              { value: 'escalated', label: 'مُصعَّد' },
              { value: 'approved', label: 'معتمد' },
              { value: 'rejected', label: 'مرفوض' },
              { value: 'suspended', label: 'معلّق' },
              { value: 'all', label: 'كل الحالات' },
            ]}
          />
        </div>
        <label className="inline-flex items-center gap-2 text-xs font-semibold text-[var(--ink)]">
          <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} />
          طلبات تخصني / قسمي
        </label>
      </div>

      {loading ? (
        <Skeleton rows={6} />
      ) : error ? (
        <ErrorState message={error} onRetry={() => void load()} />
      ) : rows.length === 0 ? (
        <EmptyState title="لا توجد طلبات بانتظار اعتمادك حاليًا" />
      ) : (
        <DataTable columns={columns} data={rows} getRowId={(r) => r.id} />
      )}

      <Dialog
        open={Boolean(decideRow)}
        onClose={() => setDecideRow(null)}
        title={
          decision === 'approve'
            ? `تأكيد اعتماد الطلب`
            : decision === 'reject'
              ? 'رفض الطلب'
              : 'تعليق الطلب'
        }
        description={
          decision === 'approve'
            ? `تأكيد اعتماد الطلب #${decideRow?.requestNo || decideRow?.id.slice(0, 8)}؟ لا يوجد اعتماد تلقائي عند التصعيد؛ قرارك نهائي ومُسجل في التدقيق.`
            : 'سبب القرار إلزامي عند الرفض أو التعليق.'
        }
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setDecideRow(null)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-semibold disabled:opacity-50"
              disabled={busy}
              onClick={() => void submitDecision()}
            >
              تأكيد
            </button>
          </>
        }
      >
        <label className="block text-xs font-semibold mb-1">ملاحظة / سبب</label>
        <textarea
          className="w-full min-h-24 rounded-lg border border-[var(--line)] bg-[var(--raised)] p-2 text-sm"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder={decision === 'approve' ? 'اختياري' : 'سبب إلزامي'}
        />
        <p className="text-[11px] text-[var(--muted)] mt-2">{MAKER_CHECKER_AR}</p>
      </Dialog>
    </div>
  );
}

export default function ApprovalsPage() {
  return (
    <Suspense fallback={<Skeleton rows={4} />}>
      <ApprovalsInner />
    </Suspense>
  );
}
