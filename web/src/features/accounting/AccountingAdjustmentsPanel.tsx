'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw, Plus, CheckCircle2 } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';

type Adj = Record<string, unknown>;

export function AccountingAdjustmentsPanel({ companies }: { companies: Array<{ id: string; code?: string; nameAr?: string }> }) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canCreate = can('finance.journal.create') || isAdmin;
  const canApprove = can('finance.journal.approve') || isAdmin;
  const [rows, setRows] = useState<Adj[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({
    type: 'mada_topup',
    companyId: companies[0]?.id || 'BW',
    date: new Date().toISOString().slice(0, 10),
    reference: '',
    description: '',
    amount: '',
    riderIqama: '',
    bankAccountCode: '',
  });

  const load = useCallback(() => {
    if (!canCreate && !canApprove) { setLoading(false); return; }
    setLoading(true); setError(null);
    api.listAccountingAdjustments({ pageSize: 100 })
      .then((res) => {
        const items = Array.isArray(res) ? res : Array.isArray(res?.items) ? res.items : [];
        setRows(items as Adj[]);
      })
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'تعذر التحميل'))
      .finally(() => setLoading(false));
  }, [canCreate, canApprove]);

  useEffect(() => { load(); }, [load]);

  if (!canCreate && !canApprove) {
    return <EmptyState title="صلاحية غير كافية" description="تسويات مدا/الراتب/الرسوم الإدارية تحتاج صلاحية قيود." />;
  }

  const draft = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.draftAccountingAdjustment({
        ...form,
        amount: Number(form.amount),
        riderIqama: form.type === 'mada_topup' ? undefined : form.riderIqama,
        bankAccountCode: form.type === 'mada_topup' ? form.bankAccountCode : undefined,
      });
      toast('مسودة التسوية جاهزة', 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإنشاء', 'danger');
    } finally { setBusy(false); }
  };

  const approve = async (id: string) => {
    setBusy(true);
    try {
      await api.approveAccountingAdjustment(id);
      toast('اعتُمدت التسوية', 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الاعتماد', 'danger');
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-bold">تسويات مدا والراتب والرسوم الإدارية</h3>
          <p className="text-xs text-[var(--muted)]">مسودة ثم اعتماد مستقل.</p>
        </div>
        <button type="button" onClick={load} className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-semibold">
          <RefreshCw size={14} /> تحديث
        </button>
      </div>

      {canCreate && (
        <form onSubmit={draft} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
          <label className="text-xs font-semibold space-y-1">
            <span>النوع</span>
            <select value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]">
              <option value="mada_topup">شحن مدا</option>
              <option value="salary_increase">زيادة راتب</option>
              <option value="salary_decrease">نقص راتب</option>
              <option value="administrative_charge">رسم إداري</option>
            </select>
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>الشركة</span>
            <select value={form.companyId} onChange={(e) => setForm({ ...form, companyId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]">
              {companies.map((c) => <option key={c.id} value={c.id}>{c.nameAr || c.code || c.id}</option>)}
            </select>
          </label>
          <label className="text-xs font-semibold space-y-1"><span>التاريخ</span><input type="date" required value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1"><span>المرجع</span><input required value={form.reference} onChange={(e) => setForm({ ...form, reference: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1"><span>البيان</span><input required value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" /></label>
          <label className="text-xs font-semibold space-y-1"><span>المبلغ</span><input required value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          {form.type === 'mada_topup' ? (
            <label className="text-xs font-semibold space-y-1"><span>حساب البنك</span><input required value={form.bankAccountCode} onChange={(e) => setForm({ ...form, bankAccountCode: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          ) : (
            <label className="text-xs font-semibold space-y-1"><span>إقامة المندوب</span><input required value={form.riderIqama} onChange={(e) => setForm({ ...form, riderIqama: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" /></label>
          )}
          <div className="flex items-end">
            <button type="submit" disabled={busy} className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
              <Plus size={14} /> مسودة
            </button>
          </div>
        </form>
      )}

      {error ? <ErrorState title="تعذر التحميل" message={error} onRetry={load} /> : loading ? <Skeleton className="h-32 w-full" /> : rows.length === 0 ? (
        <EmptyState title="لا تسويات" description="أنشئ مسودة أولاً." />
      ) : (
        <div className="rounded-2xl border border-[var(--line)] overflow-hidden bg-[var(--surface)]">
          <table className="w-full text-xs sm:text-sm">
            <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
              <tr>
                <th className="p-3 text-start">النوع</th>
                <th className="p-3 text-start">المرجع</th>
                <th className="p-3 text-start">المبلغ</th>
                <th className="p-3 text-start">الحالة</th>
                <th className="p-3 text-start">إجراء</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {rows.map((r) => {
                const id = String(r.id || '');
                const status = String(r.status || '');
                return (
                  <tr key={id}>
                    <td className="p-3">{String(r.type || '')}</td>
                    <td className="p-3 font-mono">{String(r.reference || '')}</td>
                    <td className="p-3 font-mono">{Number(r.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
                    <td className="p-3">{status}</td>
                    <td className="p-3">
                      {canApprove && status === 'draft' && (
                        <button type="button" disabled={busy} onClick={() => approve(id)} className="inline-flex items-center gap-1 font-semibold text-[var(--accent)] disabled:opacity-50">
                          <CheckCircle2 size={14} /> اعتماد
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
