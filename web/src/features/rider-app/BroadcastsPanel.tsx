'use client';

// R7: general broadcast to all riders, individual notices that can never be broadcast, and the send log.
import React, { useCallback, useEffect, useState } from 'react';
import { Send, UserRound, History } from 'lucide-react';
import { api, type RiderBroadcast } from '../../lib/api';
import { SectionTabs } from '../../components/ui/SectionTabs';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { Dialog } from '../../components/ui/Dialog';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { useToast } from '../../components/ui/Toast';

export const GENERAL_CATEGORIES: { value: string; label: string }[] = [
  { value: 'operational', label: 'تشغيلي وميداني' },
  { value: 'administrative', label: 'إداري عام' },
  { value: 'form_link', label: 'رابط نموذج' },
  { value: 'financial', label: 'مالي عام (بدون مبالغ)' },
];

// Same list as PushNotifications.IsIndividualOnlyNotice on the server.
export const INDIVIDUAL_KINDS: { value: string; label: string }[] = [
  { value: 'summons', label: 'استدعاء' },
  { value: 'dismissal_warning', label: 'إنذار فصل' },
  { value: 'administrative_deduction', label: 'خصم إداري' },
  { value: 'appointment_alert', label: 'تنبيه موعد' },
  { value: 'administrative_warning', label: 'إنذار إداري' },
];

const CATEGORY_LABEL: Record<string, string> = Object.fromEntries(
  [...GENERAL_CATEGORIES, ...INDIVIDUAL_KINDS].map((c) => [c.value, c.label])
);
const KIND_LABEL: Record<string, string> = {
  broadcast: 'تعميم عام',
  targeted: 'موجّه لمندوب',
  individual_notice: 'إشعار فردي',
  statement_ready: 'كشف حساب جاهز',
};

type Texts = { titleAr: string; bodyAr: string; titleEn: string; bodyEn: string; titleUr: string; bodyUr: string };
const EMPTY: Texts = { titleAr: '', bodyAr: '', titleEn: '', bodyEn: '', titleUr: '', bodyUr: '' };

function TextFields({ value, onChange }: { value: Texts; onChange: (v: Texts) => void }) {
  const field = (key: keyof Texts, label: string, multiline = false, dir: 'rtl' | 'ltr' = 'rtl') => (
    <label className="space-y-1 block">
      <span className="text-xs font-bold text-[var(--ink)]">{label}</span>
      {multiline ? (
        <textarea
          rows={3}
          dir={dir}
          value={value[key]}
          onChange={(e) => onChange({ ...value, [key]: e.target.value })}
          className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
        />
      ) : (
        <input
          dir={dir}
          value={value[key]}
          onChange={(e) => onChange({ ...value, [key]: e.target.value })}
          className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
        />
      )}
    </label>
  );
  return (
    <div className="space-y-3">
      {field('titleAr', 'العنوان بالعربية (مطلوب)')}
      {field('bodyAr', 'النص بالعربية (مطلوب)', true)}
      <details className="rounded-xl border border-[var(--line)] p-3">
        <summary className="text-xs font-bold cursor-pointer">الإنجليزية والأردية (اختياري — يُستخدم العربي إن تُرك فارغًا)</summary>
        <div className="space-y-3 pt-3">
          {field('titleEn', 'العنوان بالإنجليزية', false, 'ltr')}
          {field('bodyEn', 'النص بالإنجليزية', true, 'ltr')}
          {field('titleUr', 'العنوان بالأردية')}
          {field('bodyUr', 'النص بالأردية', true)}
        </div>
      </details>
    </div>
  );
}

function payload(t: Texts) {
  return {
    titleAr: t.titleAr.trim(),
    bodyAr: t.bodyAr.trim(),
    titleEn: t.titleEn.trim() || undefined,
    bodyEn: t.bodyEn.trim() || undefined,
    titleUr: t.titleUr.trim() || undefined,
    bodyUr: t.bodyUr.trim() || undefined,
  };
}

function GeneralForm({ onSent }: { onSent: () => void }) {
  const { toast } = useToast();
  const [category, setCategory] = useState('operational');
  const [texts, setTexts] = useState<Texts>(EMPTY);
  const [requiresAck, setRequiresAck] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [sending, setSending] = useState(false);

  const send = async () => {
    setSending(true);
    try {
      await api.sendRiderBroadcast({ category, iqama: 'ALL', requiresAck, ...payload(texts) });
      toast('تم حفظ التعميم وإرساله لكل المناديب المسجلين في التطبيق', 'success');
      setTexts(EMPTY);
      setConfirm(false);
      onSent();
    } catch (err: any) {
      toast(err?.message || 'تعذر إرسال التعميم', 'danger');
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="max-w-2xl space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <p className="text-xs text-[var(--muted)]">
        التعميم العام يصل لكل المناديب المسجلين. الإشعارات الفردية (استدعاء، إنذار، خصم إداري…) لا تُرسل من هنا أبدًا.
      </p>
      <label className="space-y-1 block">
        <span className="text-xs font-bold">نوع التعميم</span>
        <select
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
        >
          {GENERAL_CATEGORIES.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
      </label>
      <TextFields value={texts} onChange={setTexts} />
      <label className="flex items-center gap-2 text-xs">
        <input type="checkbox" checked={requiresAck} onChange={(e) => setRequiresAck(e.target.checked)} />
        <span>يطلب إقرار المندوب بالاستلام</span>
      </label>
      <button
        type="button"
        disabled={!texts.titleAr.trim() || !texts.bodyAr.trim()}
        onClick={() => setConfirm(true)}
        className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50 inline-flex items-center gap-1.5"
      >
        <Send size={14} /> إرسال التعميم
      </button>
      <Dialog
        open={confirm}
        onClose={() => setConfirm(false)}
        title="تأكيد التعميم العام"
        description="سيصل هذا التعميم لكل المناديب المسجلين في التطبيق. متابعة؟"
        actions={
          <button type="button" disabled={sending} onClick={send} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            {sending ? 'جاري الإرسال…' : 'إرسال'}
          </button>
        }
      >
        <p className="text-sm font-bold">{texts.titleAr}</p>
      </Dialog>
    </div>
  );
}

function IndividualForm({ onSent }: { onSent: () => void }) {
  const { toast } = useToast();
  const [kind, setKind] = useState(INDIVIDUAL_KINDS[0].value);
  const [rider, setRider] = useState<{ iqama: string; name?: string } | null>(null);
  const [texts, setTexts] = useState<Texts>(EMPTY);
  const [confirm, setConfirm] = useState(false);
  const [sending, setSending] = useState(false);
  const kindLabel = CATEGORY_LABEL[kind] || kind;

  const send = async () => {
    if (!rider) return;
    setSending(true);
    try {
      await api.sendRiderBroadcast({ category: kind, iqama: rider.iqama, ...payload(texts) });
      toast('تم إرسال الإشعار الفردي للمندوب فقط', 'success');
      setTexts(EMPTY);
      setConfirm(false);
      onSent();
    } catch (err: any) {
      toast(err?.message || 'تعذر إرسال الإشعار', 'danger');
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="max-w-2xl space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <p className="text-xs text-[var(--muted)]">إشعار رسمي لمندوب واحد فقط، لا يُعمَّم أبدًا، ويطلب إقرار المندوب بالاستلام.</p>
      <label className="space-y-1 block">
        <span className="text-xs font-bold">نوع الإشعار</span>
        <select value={kind} onChange={(e) => setKind(e.target.value)} className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs">
          {INDIVIDUAL_KINDS.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
      </label>
      <div className="space-y-1">
        <span className="text-xs font-bold">المندوب</span>
        <RiderPicker value={rider?.iqama} onChange={(r) => setRider(r ? { iqama: r.iqama ?? r.id, name: r.name } : null)} required />
      </div>
      <TextFields value={texts} onChange={setTexts} />
      <button
        type="button"
        disabled={!rider || !texts.titleAr.trim() || !texts.bodyAr.trim()}
        onClick={() => setConfirm(true)}
        className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50 inline-flex items-center gap-1.5"
      >
        <UserRound size={14} /> إرسال الإشعار الفردي
      </button>
      <Dialog
        open={confirm}
        onClose={() => setConfirm(false)}
        title="تأكيد الإشعار الفردي"
        description={`سيُرسل إشعار فردي رسمي (${kindLabel}) للمندوب فقط بـ 3 لغات مع تتبع القراءة والإقرار. متابعة؟`}
        actions={
          <button type="button" disabled={sending} onClick={send} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            {sending ? 'جاري الإرسال…' : 'إرسال'}
          </button>
        }
      >
        <p className="text-sm">
          {rider?.name || ''} <span className="font-mono" dir="ltr">{rider?.iqama}</span>
        </p>
      </Dialog>
    </div>
  );
}

function HistoryTable({ reloadKey }: { reloadKey: number }) {
  const [rows, setRows] = useState<RiderBroadcast[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [kind, setKind] = useState('');
  const [category, setCategory] = useState('');
  const [iqama, setIqama] = useState('');
  const [serverQuery, setServerQuery] = useState<{ filtersParam?: string; sortParam?: string }>({});
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const filters = useCallback(
    () => ({
      kind: kind || undefined,
      category: category || undefined,
      q: iqama.trim() || undefined,
      filters: serverQuery.filtersParam,
      sort: serverQuery.sortParam,
    }),
    [kind, category, iqama, serverQuery]
  );

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getRiderBroadcastsPage({ page, pageSize, ...filters() });
      setRows(res.items);
      setTotal(res.total);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل سجل الإرسال');
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, filters]);

  useEffect(() => {
    load();
  }, [load, reloadKey]);

  const exportAll = async () => {
    const all: RiderBroadcast[] = [];
    let p = 1;
    const size = 200;
    for (;;) {
      const res = await api.getRiderBroadcastsPage({ page: p, pageSize: size, ...filters() });
      all.push(...res.items);
      if (all.length >= res.total || res.items.length === 0) break;
      p += 1;
      if (p > 100) break;
    }
    const headers = ['العنوان', 'المستهدف', 'النوع', 'التصنيف', 'قراءات', 'إقرارات', 'أرسله', 'وقت الإرسال'];
    const excelRows = all.map((b) => [
      b.titleAr ?? '',
      b.targetIqama === 'ALL' ? 'كل المناديب' : b.targetIqama ?? '',
      KIND_LABEL[b.kind ?? ''] || b.kind || '',
      CATEGORY_LABEL[b.category ?? ''] || b.category || '',
      b.readCount ?? 0,
      b.ackCount ?? 0,
      b.sentBy ?? '',
      b.createdAt ? new Date(b.createdAt).toLocaleString('ar-SA') : '',
    ]);
    const blob = await api.exportExcel({
      headers,
      rows: excelRows,
      numericColumns: [4, 5],
      filename: 'rider-app-notifications.xlsx',
    });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'rider-app-notifications.xlsx';
    a.click();
    window.URL.revokeObjectURL(url);
  };

  const columns: ColumnDef<RiderBroadcast>[] = [
    {
      key: 'titleAr',
      header: 'العنوان',
      type: 'text',
      filterType: 'text',
      getValue: (b) => b.titleAr ?? '',
      render: (b) => <span className="font-bold">{b.titleAr}</span>,
    },
    {
      key: 'targetIqama',
      header: 'المستهدف',
      type: 'text',
      filterType: 'text',
      getValue: (b) => b.targetIqama ?? '',
      labelOf: (v) => (v === 'ALL' ? 'كل المناديب' : String(v ?? '')),
      render: (b) => (b.targetIqama === 'ALL' ? 'كل المناديب' : <span className="font-mono" dir="ltr">{b.targetIqama}</span>),
    },
    {
      key: 'kind',
      header: 'النوع',
      type: 'list',
      filterType: 'list',
      filterOptions: Object.entries(KIND_LABEL).map(([value, label]) => ({ value, label })),
      getValue: (b) => b.kind ?? '',
      labelOf: (v) => KIND_LABEL[String(v ?? '')] || String(v ?? ''),
      render: (b) => KIND_LABEL[b.kind ?? ''] || b.kind || '',
    },
    {
      key: 'category',
      header: 'التصنيف',
      type: 'list',
      filterType: 'list',
      filterOptions: Object.entries(CATEGORY_LABEL).map(([value, label]) => ({ value, label })),
      getValue: (b) => b.category ?? '',
      labelOf: (v) => CATEGORY_LABEL[String(v ?? '')] || String(v ?? ''),
      render: (b) => CATEGORY_LABEL[b.category ?? ''] || b.category || '',
    },
    {
      key: 'readCount',
      header: 'قراءات',
      type: 'number',
      numeric: true,
      filterType: 'number',
      getValue: (b) => b.readCount ?? 0,
    },
    {
      key: 'ackCount',
      header: 'إقرارات',
      type: 'number',
      numeric: true,
      filterType: 'number',
      getValue: (b) => b.ackCount ?? 0,
    },
    {
      key: 'sentBy',
      header: 'أرسله',
      type: 'list',
      filterType: 'list',
      getValue: (b) => b.sentBy ?? '',
    },
    {
      key: 'createdAt',
      header: 'وقت الإرسال',
      type: 'date',
      filterType: 'date',
      getValue: (b) => b.createdAt ?? '',
      render: (b) => (b.createdAt ? new Date(b.createdAt).toLocaleString('ar-SA') : ''),
    },
  ];

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3 text-xs">
        <label className="flex items-center gap-2">
          <span className="font-bold">النوع:</span>
          <div className="w-52">
            <SearchableSelect
              value={kind}
              onChange={(val) => {
                setKind(val);
                setPage(1);
              }}
              clearable
              placeholder="الكل"
              ariaLabel="تصفية بنوع الإشعار"
              options={Object.entries(KIND_LABEL).map(([k, l]) => ({ value: k, label: l }))}
            />
          </div>
        </label>
        <label className="flex items-center gap-2">
          <span className="font-bold">التصنيف:</span>
          <div className="w-52">
            <SearchableSelect
              value={category}
              onChange={(val) => {
                setCategory(val);
                setPage(1);
              }}
              clearable
              placeholder="الكل"
              ariaLabel="تصفية بتصنيف الإشعار"
              options={Object.entries(CATEGORY_LABEL).map(([k, l]) => ({ value: k, label: l }))}
            />
          </div>
        </label>
        <label className="flex items-center gap-2">
          <span className="font-bold">إقامة:</span>
          <input
            value={iqama}
            onChange={(e) => {
              setIqama(e.target.value);
              setPage(1);
            }}
            placeholder="رقم الإقامة"
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono w-36"
            dir="ltr"
          />
        </label>
      </div>
      <DataTable
        tableId="rider-app-broadcasts"
        columns={columns}
        data={rows}
        totalCount={total}
        loading={loading}
        error={error}
        onRetry={load}
        serverSide
        searchable={false}
        distinctEndpoint="/rider-mobile/broadcasts/distinct"
        distinctParams={{ ...filters(), sort: undefined }}
        onServerQueryChange={(sq) => {
          setServerQuery({ filtersParam: sq.filtersParam, sortParam: sq.sortParam });
          setPage(1);
        }}
        page={page}
        pageSize={pageSize}
        onPageChange={setPage}
        onPageSizeChange={(s) => {
          setPageSize(s);
          setPage(1);
        }}
        onExport={exportAll}
        getRowId={(b) => b.id}
        exportFilename="rider-app-notifications"
        emptyTitle="لا توجد إشعارات مرسلة"
      />
    </div>
  );
}

export function BroadcastsPanel({ subtab }: { subtab?: string }) {
  const active = subtab === 'individual' || subtab === 'history' ? subtab : 'broadcast';
  const [reloadKey, setReloadKey] = useState(0);
  const sent = () => setReloadKey((k) => k + 1);
  return (
    <div className="space-y-4">
      <SectionTabs
        paramName="subtab"
        activeTab={active}
        variant="pills"
        ariaLabel="أنواع الإشعارات"
        tabs={[
          { id: 'broadcast', label: 'تعميم عام (للكل)', icon: <Send size={14} /> },
          { id: 'individual', label: 'إشعارات فردية (لا تُعمَّم أبدًا)', icon: <UserRound size={14} /> },
          { id: 'history', label: 'سجل الإرسال والاستلام', icon: <History size={14} /> },
        ]}
      />
      {active === 'broadcast' && <GeneralForm onSent={sent} />}
      {active === 'individual' && <IndividualForm onSent={sent} />}
      {active === 'history' && <HistoryTable reloadKey={reloadKey} />}
    </div>
  );
}
