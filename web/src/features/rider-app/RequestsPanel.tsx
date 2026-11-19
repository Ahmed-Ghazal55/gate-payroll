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
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { IbanDisplay } from '../../components/ui/IbanDisplay';
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
  if (r.requestType === 'iban_update') {
    const iban = String(p.iban ?? '');
    return iban ? `آيبان: ${iban}` : r.reason ?? '';
  }
  if (r.requestType === 'data_update') {
    return `${p.fieldToUpdate ?? ''} : ${p.previousValue ?? '—'} ← ${p.newValue ?? '—'}`;
  }
  if (r.requestType === 'complaint') return String(p.details ?? r.reason ?? '');
  if (r.requestType === 'statement_request') return String(p.scope ?? p.period ?? r.reason ?? '');
  return r.reason ?? '';
}

function RequestDetailBody({ row }: { row: ServiceRequest }) {
  const p = (row.payload ?? {}) as Record<string, unknown>;
  const replies = (row.replies ?? []) as Array<{ senderRole?: string; senderName?: string; message?: string; createdAt?: string }>;
  const timeline = (row.timeline ?? []) as Array<{ at?: string; status?: string; labelAr?: string; note?: string }>;
  return (
    <div className="space-y-3 text-xs">
      <p className="text-[var(--muted)]">{details(row)}</p>
      {row.requestType === 'iban_update' && p.iban ? (
        <IbanDisplay iban={String(p.iban)} bankCode={typeof p.bankCode === 'string' ? p.bankCode : undefined} />
      ) : null}
      {row.requestType === 'data_update' ? (
        <dl className="grid grid-cols-2 gap-1">
          <dt className="text-[var(--muted)]">الحقل</dt><dd>{String(p.fieldToUpdate ?? '—')}</dd>
          <dt className="text-[var(--muted)]">القيمة السابقة</dt><dd>{String(p.previousValue ?? '—')}</dd>
          <dt className="text-[var(--muted)]">القيمة الجديدة</dt><dd>{String(p.newValue ?? '—')}</dd>
        </dl>
      ) : null}
      {row.requestType === 'complaint' && p.details ? <p className="whitespace-pre-wrap">{String(p.details)}</p> : null}
      {row.reason ? <p><span className="font-bold">السبب: </span>{row.reason}</p> : null}
      {replies.length > 0 ? (
        <div className="space-y-1">
          <p className="font-bold">الردود</p>
          {replies.map((rep, i) => (
            <div key={i} className="p-2 rounded-lg bg-[var(--raised)]">
              <div className="text-[10px] text-[var(--muted)]">{rep.senderName || rep.senderRole || '—'} · {rep.createdAt ? new Date(rep.createdAt).toLocaleString('ar-SA') : ''}</div>
              <div>{rep.message}</div>
            </div>
          ))}
        </div>
      ) : null}
      {timeline.length > 0 ? (
        <div className="space-y-1">
          <p className="font-bold">سجل الحالة</p>
          {timeline.map((t, i) => (
            <div key={i} className="flex justify-between gap-2 border-b border-[var(--line)] py-1">
              <span>{t.labelAr || t.status}</span>
              <span className="text-[var(--muted)] shrink-0">{t.at ? new Date(t.at).toLocaleString('ar-SA') : ''}</span>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
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
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [target, setTarget] = useState<{ row: ServiceRequest; action: string } | null>(null);
  const [detail, setDetail] = useState<ServiceRequest | null>(null);
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
  }, [active, page, pageSize, status]);

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
    { key: 'id', header: 'رقم الطلب', getValue: (r) => r.id.replace(/^sreq:/, '').slice(0, 8), render: (r) => <span className="font-mono text-xs" dir="ltr">{r.id.replace(/^sreq:/, '').slice(0, 8)}</span> },
    {
      key: 'rider',
      header: 'المندوب والإقامة',
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
    { key: 'requestType', header: 'النوع', getValue: (r) => TYPE_LABEL[r.requestType] || r.requestType },
    {
      key: 'details',
      header: 'التفاصيل',
      getValue: details,
      render: (r) => (
        <button type="button" onClick={() => setDetail(r)} className="text-start text-xs text-[var(--accent)] font-semibold hover:underline max-w-[220px]">
          {details(r) || 'عرض التفاصيل'}
        </button>
      ),
    },
    { key: 'direction', header: 'الاتجاه', getValue: (r) => (r.direction === 'admin_to_rider' ? 'من الإدارة للمندوب' : 'من المندوب') },
    { key: 'createdAt', header: 'تاريخ الإرسال', getValue: (r) => (r.createdAt ? new Date(r.createdAt).toLocaleString('ar-SA') : '') },
    { key: 'status', header: 'الحالة', getValue: (r) => STATUS_LABEL[r.status] || r.status },
    {
      key: 'actions',
      header: 'الإجراء',
      render: (r) => (
        <div className="flex flex-wrap gap-1">
          <button type="button" onClick={() => setDetail(r)} className="px-2 py-1 rounded-lg text-[11px] font-bold border border-[var(--line)]">
            تفاصيل
          </button>
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
        open={!!detail}
        onClose={() => setDetail(null)}
        title={detail ? `${TYPE_LABEL[detail.requestType] || detail.requestType} — ${STATUS_LABEL[detail.status] || detail.status}` : ''}
        description={detail ? `${detail.riderName ?? ''} — ${detail.riderIqama}` : undefined}
      >
        {detail ? <RequestDetailBody row={detail} /> : null}
      </Dialog>
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
          {target ? <RequestDetailBody row={target.row} /> : null}
          <label className="space-y-1 block">
            <span className="text-xs font-bold">{needsNote ? 'النص الذي يصل للمندوب (مطلوب)' : 'ملاحظة (اختياري)'}</span>
            <textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs" />
          </label>
        </div>
      </Dialog>
    </div>
  );
}
