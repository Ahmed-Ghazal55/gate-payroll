'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw, FilePlus2, Send, CheckCircle2, Scale } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import type { JahezReconciliationDoc } from '../../lib/types';

export function JahezWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canView = can('treasury.view') || can('treasury.transfer') || can('treasury.jahez.receive') || isAdmin;
  const canWrite = can('treasury.transfer') || can('treasury.jahez.receive') || isAdmin;
  const canFee = can('treasury.jahez.manage') || isAdmin;
  const canHandover = can('treasury.jahez.handover') || isAdmin;

  const [rows, setRows] = useState<JahezReconciliationDoc[]>([]);
  const [handovers, setHandovers] = useState<Array<Record<string, unknown>>>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<JahezReconciliationDoc | null>(null);
  const [variance, setVariance] = useState('');
  const [busy, setBusy] = useState(false);

  const [companyId, setCompanyId] = useState('BW');
  const [period, setPeriod] = useState('2026-10');
  const [invoiceNet, setInvoiceNet] = useState('0');
  const [invoiceVat, setInvoiceVat] = useState('0');
  const [lastTransactionDate, setLastTransactionDate] = useState('2026-10-31');
  const [invoiceReference, setInvoiceReference] = useState('');
  const [notes, setNotes] = useState('');

  // Ops forms
  const [transfer, setTransfer] = useState({ fromVaultCode: '', toVaultCode: '', amount: '', sourceRef: '', notes: '', period: '2026-10' });
  const [settlement, setSettlement] = useState({ sponsorId: 'BW', action: 'receive', amount: '', sourceRef: '', notes: '', period: '2026-10' });
  const [feeDeposit, setFeeDeposit] = useState({ sponsorId: 'BW', amount: '', sourceRef: '', notes: '', period: '2026-10' });
  const [fee, setFee] = useState({ sponsorId: 'BW', iqama: '', amount: '200', notes: '' });

  const load = useCallback(() => {
    if (!canView) { setLoading(false); return; }
    setLoading(true); setError(null);
    Promise.all([
      api.listJahezReconciliations(),
      canHandover || canWrite ? api.listJahezHandovers().catch(() => []) : Promise.resolve([]),
    ])
      .then(([list, hands]) => {
        setRows(Array.isArray(list) ? list : []);
        setHandovers(Array.isArray(hands) ? hands : []);
      })
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'تعذر التحميل'))
      .finally(() => setLoading(false));
  }, [canView, canHandover, canWrite]);

  useEffect(() => { load(); }, [load]);

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="خزينة جاهز تحتاج صلاحية خزينة." />;
  }

  const build = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canWrite) return;
    setBusy(true);
    try {
      const doc = await api.buildJahezReconciliation({
        companyId, period,
        invoiceNet: Number(invoiceNet), invoiceVat: Number(invoiceVat),
        lastTransactionDate, invoiceReference: invoiceReference || undefined, notes: notes || undefined,
      });
      toast('تم بناء تقرير المطابقة', 'success');
      setSelected(doc);
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر البناء', 'danger');
    } finally { setBusy(false); }
  };

  const act = async (fn: () => Promise<JahezReconciliationDoc>, ok: string) => {
    setBusy(true);
    try {
      const doc = await fn();
      setSelected(doc);
      toast(ok, 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'فشلت العملية', 'danger');
    } finally { setBusy(false); }
  };

  const runOp = async (label: string, fn: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await fn();
      toast(label, 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'فشلت العملية', 'danger');
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">جاهز — مطابقة §37 وعمليات الخزينة</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">تقارير المطابقة، التحويلات، التسوية، الرسوم، والعهدة.</p>
        </div>
        <button type="button" onClick={load} className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-semibold">
          <RefreshCw size={14} /> تحديث
        </button>
      </div>

      {canWrite && (
        <form onSubmit={build} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
          <label className="text-xs font-semibold space-y-1"><span>الشركة</span><input required value={companyId} onChange={(e) => setCompanyId(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          <label className="text-xs font-semibold space-y-1"><span>الشهر</span><input required type="month" value={period} onChange={(e) => setPeriod(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1"><span>آخر معاملة</span><input required type="date" value={lastTransactionDate} onChange={(e) => setLastTransactionDate(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1"><span>صافي الفاتورة</span><input required value={invoiceNet} onChange={(e) => setInvoiceNet(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          <label className="text-xs font-semibold space-y-1"><span>ضريبة الفاتورة</span><input required value={invoiceVat} onChange={(e) => setInvoiceVat(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          <label className="text-xs font-semibold space-y-1"><span>مرجع الفاتورة</span><input value={invoiceReference} onChange={(e) => setInvoiceReference(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1 sm:col-span-2"><span>ملاحظات</span><input value={notes} onChange={(e) => setNotes(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <div className="flex items-end">
            <button type="submit" disabled={busy} className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
              <FilePlus2 size={14} /> بناء تقرير مطابقة
            </button>
          </div>
        </form>
      )}

      {error ? <ErrorState title="تعذر التحميل" message={error} onRetry={load} /> : loading ? <Skeleton className="h-40 w-full" /> : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
            <div className="p-3 border-b border-[var(--line)] text-xs font-bold">التقارير ({rows.length})</div>
            {rows.length === 0 ? <div className="p-4 text-xs text-[var(--muted)]">لا تقارير بعد.</div> : (
              <ul className="divide-y divide-[var(--line)] max-h-80 overflow-auto">
                {rows.map((r) => (
                  <li key={r.id}>
                    <button type="button" onClick={() => { setSelected(r); setVariance(r.varianceDecision || ''); }} className="w-full text-start p-3 hover:bg-[var(--row-hover)] text-xs">
                      <div className="font-mono font-bold">{r.period} · {r.companyId}</div>
                      <div className="text-[var(--muted)] mt-0.5">{r.status} · {r.invoiceReference || r.id}</div>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 text-xs">
            {!selected ? <p className="text-[var(--muted)]">اختر تقريرًا للتفاصيل والإجراءات.</p> : (
              <>
                <div className="font-bold font-mono">{selected.id}</div>
                <div>الحالة: <b>{selected.status}</b></div>
                {selected.totals && (
                  <div className="grid grid-cols-2 gap-2">
                    {Object.entries(selected.totals).map(([k, v]) => (
                      <div key={k} className="p-2 rounded-lg bg-[var(--raised)]/50"><div className="text-[var(--muted)]">{k}</div><div className="font-mono font-bold">{Number(v).toLocaleString('en-US', { minimumFractionDigits: 2 })}</div></div>
                    ))}
                  </div>
                )}
                {canWrite && selected.status === 'draft' && (
                  <button type="button" disabled={busy} onClick={() => act(() => api.submitJahezReconciliation(selected.id), 'أُرسل للاعتماد')} className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] font-semibold disabled:opacity-50">
                    <Send size={14} /> إرسال للاعتماد
                  </button>
                )}
                {canWrite && (selected.status === 'draft' || selected.status === 'pending_approval') && (
                  <div className="flex flex-wrap gap-2 items-end">
                    <label className="flex-1 space-y-1 font-semibold">
                      <span>قرار الفرق</span>
                      <input value={variance} onChange={(e) => setVariance(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
                    </label>
                    <button type="button" disabled={busy || variance.trim().length < 3} onClick={() => act(() => api.decideJahezVariance(selected.id, variance.trim()), 'سُجّل قرار الفرق')} className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] font-semibold disabled:opacity-50">
                      <Scale size={14} /> حفظ القرار
                    </button>
                  </div>
                )}
                {canWrite && selected.status === 'pending_approval' && (
                  <button type="button" disabled={busy} onClick={() => act(() => api.approveJahezReconciliation(selected.id), 'اعتُمد التقرير')} className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50">
                    <CheckCircle2 size={14} /> اعتماد وترحيل
                  </button>
                )}
              </>
            )}
          </div>
        </div>
      )}

      {canWrite && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <form className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs" onSubmit={(e) => { e.preventDefault(); void runOp('تم التحويل', () => api.jahezTransfer({ ...transfer, amount: Number(transfer.amount) })); }}>
            <h4 className="font-bold">نقل عهدة جاهز</h4>
            {(['fromVaultCode', 'toVaultCode', 'amount', 'sourceRef', 'notes', 'period'] as const).map((k) => (
              <input key={k} required placeholder={k} value={transfer[k]} onChange={(e) => setTransfer({ ...transfer, [k]: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
            ))}
            <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50">تنفيذ</button>
          </form>
          <form className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs" onSubmit={(e) => { e.preventDefault(); void runOp('تمت التسوية', () => api.jahezPlatformSettlement({ ...settlement, amount: Number(settlement.amount) })); }}>
            <h4 className="font-bold">تسوية منصة جاهز</h4>
            <select value={settlement.action} onChange={(e) => setSettlement({ ...settlement, action: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]">
              <option value="receive">تحصيل</option>
              <option value="pay">سداد</option>
            </select>
            {(['sponsorId', 'amount', 'sourceRef', 'notes', 'period'] as const).map((k) => (
              <input key={k} required placeholder={k} value={settlement[k]} onChange={(e) => setSettlement({ ...settlement, [k]: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
            ))}
            <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50">تنفيذ</button>
          </form>
          <form className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs" onSubmit={(e) => { e.preventDefault(); void runOp('تم الإيداع', () => api.jahezFeeDeposit({ ...feeDeposit, amount: Number(feeDeposit.amount) })); }}>
            <h4 className="font-bold">إيداع حصيلة الرسوم</h4>
            {(['sponsorId', 'amount', 'sourceRef', 'notes', 'period'] as const).map((k) => (
              <input key={k} required placeholder={k} value={feeDeposit[k]} onChange={(e) => setFeeDeposit({ ...feeDeposit, [k]: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
            ))}
            <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50">تنفيذ</button>
          </form>
          {canFee && (
            <form className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs" onSubmit={(e) => { e.preventDefault(); void runOp('سُجّل الرسم', () => api.jahezFee({ ...fee, amount: Number(fee.amount) })); }}>
              <h4 className="font-bold">رسم جاهز</h4>
              {(['sponsorId', 'iqama', 'amount', 'notes'] as const).map((k) => (
                <input key={k} placeholder={k} value={fee[k]} onChange={(e) => setFee({ ...fee, [k]: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
              ))}
              <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50">تنفيذ</button>
            </form>
          )}
        </div>
      )}

      {(canHandover || canWrite) && handovers.length > 0 && (
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
          <div className="p-3 border-b border-[var(--line)] text-xs font-bold">عهد جاهز ({handovers.length})</div>
          <ul className="divide-y divide-[var(--line)] text-xs">
            {handovers.map((h) => {
              const id = String(h.id || '');
              const status = String(h.status || '');
              const actions = Array.isArray(h.allowedActions) ? h.allowedActions as string[] : [];
              return (
                <li key={id} className="p-3 flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-mono font-bold">{id}</div>
                    <div className="text-[var(--muted)]">{status}</div>
                  </div>
                  <div className="flex gap-2">
                    {(actions.includes('confirm') || status === 'pending') && canWrite && (
                      <button type="button" disabled={busy} onClick={() => void runOp('أُكّدت العهدة', () => api.confirmJahezHandover(id, {}))} className="px-2 py-1 rounded-lg border border-[var(--line)] font-semibold disabled:opacity-50">تأكيد</button>
                    )}
                    {(actions.includes('reject') || status === 'pending') && canWrite && (
                      <button type="button" disabled={busy} onClick={() => void runOp('رُفضت العهدة', () => api.rejectJahezHandover(id, { reason: 'رفض من الواجهة' }))} className="px-2 py-1 rounded-lg border border-[var(--danger)]/40 text-[var(--danger)] font-semibold disabled:opacity-50">رفض</button>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
