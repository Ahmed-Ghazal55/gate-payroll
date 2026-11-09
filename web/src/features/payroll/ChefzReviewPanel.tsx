'use client';

import React, { useState } from 'react';
import { ClipboardCheck } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState } from '../../components/ui/EmptyState';

export function ChefzReviewPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const [companyId, setCompanyId] = useState('BW');
  const [period, setPeriod] = useState('2026-10');
  const [invoiceId, setInvoiceId] = useState('');
  const [operatorsJson, setOperatorsJson] = useState('[]');
  const [result, setResult] = useState<Record<string, unknown> | null>(null);
  const [busy, setBusy] = useState(false);

  if (!can('pay.view') && !isAdmin) {
    return <EmptyState title="صلاحية غير كافية" description="مراجعة شيفز تحتاج pay.view." />;
  }

  const run = async () => {
    setBusy(true);
    try {
      let operators: unknown = [];
      try { operators = JSON.parse(operatorsJson || '[]'); }
      catch { throw new ApiError('صيغة مشغّلي الحسابات JSON غير صالحة', 400); }
      const res = await api.reviewChefzOperations({
        companyId, period, invoiceId: invoiceId.trim() || undefined, operators,
      });
      setResult(res);
      toast(res.canUseInCycle ? 'جاهز للاستخدام في الدورة' : 'يوجد أخطاء في المراجعة', res.canUseInCycle ? 'success' : 'warning');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المراجعة', 'danger');
      setResult(null);
    } finally { setBusy(false); }
  };

  const errors = Array.isArray(result?.errors) ? result!.errors as unknown[] : [];
  const notes = Array.isArray(result?.notes) ? result!.notes as unknown[] : [];
  const counts = (result?.counts && typeof result.counts === 'object') ? result.counts as Record<string, number> : {};

  return (
    <div className="space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <div>
        <h3 className="text-sm font-bold">مراجعة تسويات شيفز (قراءة فقط)</h3>
        <p className="text-xs text-[var(--muted)] mt-0.5">يعرض الأداء والمحافظ المسحوبة من ERP دون كتابة. مرّر الطلبات والفئة لكل مشغّل.</p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <label className="text-xs font-semibold space-y-1">
          <span>الشركة</span>
          <input value={companyId} onChange={(e) => setCompanyId(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>الشهر</span>
          <input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>معرّف الفاتورة</span>
          <input value={invoiceId} onChange={(e) => setInvoiceId(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
        </label>
      </div>
      <label className="block text-xs font-semibold space-y-1">
        <span>مشغّلو الحسابات (JSON)</span>
        <textarea value={operatorsJson} onChange={(e) => setOperatorsJson(e.target.value)} rows={4} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono text-[11px]" placeholder='[{"accountId":"...","rider":"1...","category":"FT","orders":10}]' />
      </label>
      <button type="button" disabled={busy} onClick={run} className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
        <ClipboardCheck size={14} /> {busy ? 'جاري…' : 'مراجعة'}
      </button>
      {result && (
        <div className="space-y-2 text-xs">
          <div className="flex flex-wrap gap-3">
            <span>يمكن استخدامها في الدورة: <b>{String(!!result.canUseInCycle)}</b></span>
            {Object.entries(counts).map(([k, v]) => <span key={k} className="font-mono">{k}={v}</span>)}
          </div>
          {errors.length > 0 && (
            <div className="p-3 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10">
              <p className="font-bold text-[var(--danger)] mb-1">أخطاء ({errors.length})</p>
              <pre className="whitespace-pre-wrap font-mono text-[11px]">{JSON.stringify(errors, null, 2)}</pre>
            </div>
          )}
          {notes.length > 0 && (
            <div className="p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40">
              <p className="font-bold mb-1">ملاحظات</p>
              <pre className="whitespace-pre-wrap font-mono text-[11px]">{JSON.stringify(notes, null, 2)}</pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
