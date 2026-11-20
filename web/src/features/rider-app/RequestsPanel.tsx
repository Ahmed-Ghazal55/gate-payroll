'use client';

// U-W3-17: rider requests queue. Buttons come from the server's allowedActions; decisions use the
// existing decide/reply endpoints (their approval rules are unchanged). Advance asks are money:
// shown read-only here and handled in the debts section.
import React, { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ExternalLink } from 'lucide-react';
import { api, type ServiceRequest, type ServiceRequestsSummary } from '../../lib/api';
import { SectionTabs } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, type ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { useToast } from '../../components/ui/Toast';

const TYPE_LABEL: Record<string, string> = {
  annual_leave: 'إجازة سنوية',
  appointment: 'موعد',
  iban_update: 'تحديث آيبان',
  data_update: 'تحديث بيانات',
  statement_request: 'طلب كشف حساب',
  complaint: 'شكوى سرية',
};
const STATUS_LABEL: Record<string, string> = {
  open: 'جديد',
  need_info: 'بانتظار توضيح المندوب',
  awaiting_rider: 'بانتظار رد المندوب',
  first_approved: 'اعتماد أول — بانتظار الثاني',
  approved: 'مقبول',
  rejected: 'مرفوض',
  cancelled: 'ملغى من المندوب',
};
const ACTION_LABEL: Record<string, string> = {
  approve: 'قبول',
  first_approve: 'اعتماد أول',
  second_approve: 'اعتماد ثانٍ',
  reject: 'رفض',
  need_info: 'طلب توضيح',
  reply: 'رد',
};

// Sub-tab → requestType filter (`other` = everything that is not leave or appointment).
const SUBTAB_TYPE: Record<string, string | undefined> = { all: undefined, annual_leave: 'annual_leave', appointments: 'appointment' };

function details(r: ServiceRequest): string {
  const p = (r.payload ?? {}) as Record<string, unknown>;
  if (r.requestType === 'annual_leave') return `${p.startDate ?? ''} → ${p.endDate ?? ''} (${p.days ?? '—'} يوم)`;
  if (r.requestType === 'appointment') return `${p.preferredDate ?? ''} ${p.preferredSlot === 'afternoon' ? 'مساءً' : 'صباحًا'}`;
  return r.reason ?? '';
}

function AdvanceAsks() {
  return (
    <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
      <p className="text-sm font-bold">طلبات السلف من التطبيق</p>
      <p className="text-xs text-[var(--muted)]">
        طلب السلفة يصل لمشرف المندوب، ويُحوَّل إلى طلب سلفة رسمي بمسار الاعتماد المعتاد في قسم المديونيات والسلف. لا قرار مالي من هذه الشاشة.
      </p>
      <Link href="/debts/?tab=advance_requests" className="inline-flex items-center gap-1.5 text-xs font-bold text-[var(--accent)]">
        <ExternalLink size={14} /> فتح في طلبات السلف
      </Link>
    </div>
  );
}

export function RequestsPanel({ subtab }: { subtab?: string }) {
  const { toast } = useToast();
  const active = subtab && ['all', 'annual_leave', 'appointments', 'other', 'advances'].includes(subtab) ? subtab : 'all';
  const [summary, setSummary] = useState<ServiceRequestsSummary | null>(null);
  const [rows, setRows] = useState<ServiceRequest[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [status, setStatus] = useState('');
  const [serverQuery, setServerQuery] = useState<{ filtersParam?: string; sortParam?: string }>({});
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [target, setTarget] = useState<{ row: ServiceRequest; action: string } | null>(null);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    if (active === 'advances') return;
    setLoading(true);
    setError(null);
    try {
      const [res, sum] = await Promise.all([
        api.getServiceRequestsPage({
          page,
          pageSize,
          status: status || undefined,
          requestType: SUBTAB_TYPE[active],
          group: active === 'other' ? 'other' : undefined,
          filters: serverQuery.filtersParam,
          sort: serverQuery.sortParam,
        }),
        api.getServiceRequestsSummary(),
      ]);
      setRows(res.items);
      setTotal(res.total);
      setSummary(sum);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل الطلبات');
    } finally {
      setLoading(false);
    }
  }, [active, page, pageSize, status, serverQuery]);

  useEffect(() => {
    load();
  }, [load]);

  const submit = async () => {
    if (!target) return;
    setSaving(true);
    try {
      if (target.action === 'reply') await api.replyServiceRequest(target.row.id, note.trim());
      else await api.decideServiceRequest(target.row.id, { action: target.action, note: note.trim() || undefined });
      toast('تم حفظ القرار وإشعار المندوب في التطبيق', 'success');
      setTarget(null);
      setNote('');
      load();
    } catch (err: any) {
      toast(err?.message || 'تعذر حفظ القرار', 'danger');
    } finally {
      setSaving(false);
    }
  };

  const columns: ColumnDef<ServiceRequest>[] = [
    {
      key: 'id',
      header: 'رقم الطلب',
      type: 'text',
      filterType: 'text',
      getValue: (r) => r.id.replace(/^sreq:/, '').slice(0, 8),
      render: (r) => <span className="font-mono text-xs" dir="ltr">{r.id.replace(/^sreq:/, '').slice(0, 8)}</span>,
    },
    {
      key: 'rider',
      header: 'المندوب والإقامة',
      type: 'text',
      filterType: 'text',
      filterColumn: 'riderName',
      getValue: (r) => `${r.riderName ?? ''} ${r.riderIqama}`,
      exportColumns: [
        { header: 'المندوب', value: (r) => r.riderName ?? '' },
        { header: 'رقم الإقامة', value: (r) => r.riderIqama },
      ],
      render: (r) => (
        <div className="flex flex-col">
          <span className="font-bold text-xs">{r.riderName || '—'}</span>
          <span className="font-mono text-[11px] text-[var(--muted)]" dir="ltr">{r.riderIqama}</span>
        </div>
      ),
    },
    {
      key: 'requestType',
      header: 'النوع',
      type: 'list',
      filterType: 'list',
      filterOptions: Object.entries(TYPE_LABEL).map(([value, label]) => ({ value, label })),
      getValue: (r) => r.requestType,
      labelOf: (v) => TYPE_LABEL[String(v ?? '')] || String(v ?? ''),
      render: (r) => TYPE_LABEL[r.requestType] || r.requestType,
    },
    {
      key: 'details',
      header: 'التفاصيل',
      type: 'text',
      filterType: 'text',
      filterColumn: 'reason',
      getValue: details,
    },
    {
      key: 'direction',
      header: 'الاتجاه',
      type: 'list',
      filterType: 'list',
      filterOptions: [
        { value: 'admin_to_rider', label: 'من الإدارة للمندوب' },
        { value: 'rider_to_admin', label: 'من المندوب' },
      ],
      getValue: (r) => r.direction || 'rider_to_admin',
      labelOf: (v) => (v === 'admin_to_rider' ? 'من الإدارة للمندوب' : 'من المندوب'),
      render: (r) => (r.direction === 'admin_to_rider' ? 'من الإدارة للمندوب' : 'من المندوب'),
    },
    {
      key: 'createdAt',
      header: 'تاريخ الإرسال',
      type: 'date',
      filterType: 'date',
      getValue: (r) => r.createdAt ?? '',
      render: (r) => (r.createdAt ? new Date(r.createdAt).toLocaleString('ar-SA') : ''),
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      filterType: 'list',
      filterOptions: Object.entries(STATUS_LABEL).map(([value, label]) => ({ value, label })),
      getValue: (r) => r.status,
      labelOf: (v) => STATUS_LABEL[String(v ?? '')] || String(v ?? ''),
      toneOf: defaultStatusTone,
      render: (r) => STATUS_LABEL[r.status] || r.status,
    },
    {
      key: 'actions',
      header: 'الإجراء',
      filterable: false,
      sortable: false,
      render: (r) => (
        <div className="flex flex-wrap gap-1">
          {(r.allowedActions ?? []).map((a) => (
            <button
              key={a}
              type="button"
              onClick={() => {
                setTarget({ row: r, action: a });
                setNote('');
              }}
              className={`px-2 py-1 rounded-lg text-[11px] font-bold border border-[var(--line)] ${a === 'reject' ? 'text-[var(--danger)]' : ''}`}
            >
              {ACTION_LABEL[a] || a}
            </button>
          ))}
        </div>
      ),
    },
  ];

  const needsNote = target && (target.action === 'reject' || target.action === 'need_info' || target.action === 'reply');

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatCard label="طلبات جديدة بانتظار المراجعة" value={summary?.open ?? '…'} loading={!summary && loading} />
        <StatCard label="إجازات سنوية مفتوحة" value={summary?.annualLeaveOpen ?? '…'} loading={!summary && loading} />
        <StatCard label="مواعيد مفتوحة" value={summary?.appointmentsOpen ?? '…'} loading={!summary && loading} />
        <StatCard label="منجزة هذا الشهر" value={summary?.doneThisMonth ?? '…'} loading={!summary && loading} />
      </div>
      <SectionTabs
        paramName="subtab"
        activeTab={active}
        variant="pills"
        ariaLabel="أنواع الطلبات"
        onChange={() => setPage(1)}
        tabs={[
          { id: 'all', label: 'الكل' },
          { id: 'annual_leave', label: 'الإجازات السنوية' },
          { id: 'appointments', label: 'المواعيد' },
          { id: 'other', label: 'طلبات أخرى' },
          { id: 'advances', label: 'طلبات السلف' },
        ]}
      />
      {active === 'advances' ? (
        <AdvanceAsks />
      ) : (
        <>
          <label className="flex items-center gap-2 text-xs">
            <span className="font-bold">الحالة:</span>
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(1);
              }}
              className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)]"
            >
              <option value="">الكل</option>
              {Object.entries(STATUS_LABEL).map(([k, l]) => (
                <option key={k} value={k}>
                  {l}
                </option>
              ))}
            </select>
          </label>
          <DataTable
            tableId="rider-app-requests"
            columns={columns}
            data={rows}
            totalCount={total}
            loading={loading}
            error={error}
            onRetry={load}
            serverSide
            searchable={false}
            distinctEndpoint="/rider-mobile/service-requests/distinct"
            distinctParams={{
              status: status || undefined,
              requestType: SUBTAB_TYPE[active],
              group: active === 'other' ? 'other' : undefined,
              filters: serverQuery.filtersParam,
            }}
            onServerQueryChange={(sq) => {
              setServerQuery({ filtersParam: sq.filtersParam, sortParam: sq.sortParam });
              setPage(1);
            }}
            onExport={async () => {
              const allRes = await api.getServiceRequestsPage({
                page: 1,
                pageSize: 200,
                status: status || undefined,
                requestType: SUBTAB_TYPE[active],
                group: active === 'other' ? 'other' : undefined,
                filters: serverQuery.filtersParam,
                sort: serverQuery.sortParam,
              });
              const headers = ['رقم الطلب', 'المندوب', 'رقم الإقامة', 'النوع', 'التفاصيل', 'الاتجاه', 'تاريخ الإرسال', 'الحالة'];
              const excelRows = allRes.items.map((r) => [
                r.id.replace(/^sreq:/, '').slice(0, 8),
                r.riderName ?? '',
                r.riderIqama ?? '',
                TYPE_LABEL[r.requestType] || r.requestType,
                details(r),
                r.direction === 'admin_to_rider' ? 'من الإدارة للمندوب' : 'من المندوب',
                r.createdAt ?? '',
                STATUS_LABEL[r.status] || r.status,
              ]);
              const blob = await api.exportExcel({ headers, rows: excelRows, numericColumns: [], filename: 'rider-app-requests.xlsx' });
              const url = window.URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url;
              a.download = 'rider-app-requests.xlsx';
              document.body.appendChild(a);
              a.click();
              window.URL.revokeObjectURL(url);
              document.body.removeChild(a);
            }}
            page={page}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={(s) => {
              setPageSize(s);
              setPage(1);
            }}
            getRowId={(r) => r.id}
            exportFilename="rider-app-requests"
            emptyTitle="لا توجد طلبات"
          />
        </>
      )}
      <Dialog
        open={!!target}
        onClose={() => setTarget(null)}
        title={target ? `${ACTION_LABEL[target.action] || target.action}: ${TYPE_LABEL[target.row.requestType] || target.row.requestType}` : ''}
        description={target ? `${target.row.riderName ?? ''} — ${target.row.riderIqama}` : undefined}
        actions={
          <button
            type="button"
            disabled={saving || (!!needsNote && note.trim().length < 3)}
            onClick={submit}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
          >
            {saving ? 'جاري الحفظ…' : 'حفظ'}
          </button>
        }
      >
        <div className="space-y-2">
          {target ? <p className="text-xs text-[var(--muted)]">{details(target.row)}</p> : null}
          <label className="space-y-1 block">
            <span className="text-xs font-bold">{needsNote ? 'النص الذي يصل للمندوب (مطلوب)' : 'ملاحظة (اختياري)'}</span>
            <textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs" />
          </label>
        </div>
      </Dialog>
    </div>
  );
}
