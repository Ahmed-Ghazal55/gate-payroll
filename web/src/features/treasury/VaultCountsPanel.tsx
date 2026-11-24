'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { formatSar } from '../../lib/money';

type VaultCountRow = {
  id?: string;
  cashBoxId?: string;
  cashBoxCode?: string;
  cashBoxNameAr?: string;
  countedOn?: string;
  bookBalance?: number;
  countedAmount?: number;
  variance?: number;
  status?: string;
  allowedActions?: string[];
};

export function VaultCountsPanel() {
  const { toast } = useToast();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [rows, setRows] = useState<VaultCountRow[]>([]);
  const [listAllowed, setListAllowed] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ cashBoxId: '', countedOn: '', countedAmount: '' });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.listVaultCounts();
      setRows(Array.isArray(res.items) ? (res.items as VaultCountRow[]) : []);
      setListAllowed(Array.isArray(res.allowedActions) ? (res.allowedActions as string[]) : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل جرد الخزن');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const create = async () => {
    if (!listAllowed.includes('create')) {
      toast('صلاحية جرد الخزن مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.createVaultCount({
        cashBoxId: form.cashBoxId.trim(),
        countedOn: form.countedOn.trim(),
        countedAmount: Number(form.countedAmount),
      });
      toast('تم إنشاء جرد الخزنة', 'success');
      setForm({ cashBoxId: '', countedOn: '', countedAmount: '' });
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإنشاء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const act = async (id: string, action: 'submit' | 'approve') => {
    setBusy(true);
    try {
      if (action === 'submit') await api.submitVaultCount(id);
      else await api.approveVaultCount(id);
      toast(action === 'submit' ? 'تم تقديم الجرد للاعتماد' : 'تم اعتماد الجرد وترحيل الفرق', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر تنفيذ الإجراء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <Skeleton className="h-40 w-full" />;
  if (error) return <ErrorState title="جرد الخزن" message={error} onRetry={load} />;

  return (
    <div className="space-y-4" data-testid="vault-counts-panel">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-bold">جرد الخزن</h3>
          <button type="button" onClick={() => void load()} className="inline-flex items-center gap-1 text-xs font-semibold">
            <RefreshCw size={14} /> تحديث
          </button>
        </div>
        {listAllowed.includes('create') && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2">
            {(['cashBoxId', 'countedOn', 'countedAmount'] as const).map((k) => (
              <input
                key={k}
                placeholder={k}
                value={form[k]}
                onChange={(e) => setForm({ ...form, [k]: e.target.value })}
                className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
              />
            ))}
            <button
              type="button"
              disabled={busy}
              onClick={() => void create()}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
            >
              إنشاء جرد
            </button>
          </div>
        )}
      </div>

      {rows.length === 0 ? (
        <EmptyState title="لا جرد مسجّل" description="أنشئ جردًا لخزنة ثم قدّمه لمراجع مستقل." />
      ) : (
        <div className="rounded-2xl border border-[var(--line)] overflow-hidden bg-[var(--surface)]">
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
              <tr>
                <th className="p-2 text-start">الخزنة</th>
                <th className="p-2 text-start">التاريخ</th>
                <th className="p-2 text-start">دفتر</th>
                <th className="p-2 text-start">جرد</th>
                <th className="p-2 text-start">فرق</th>
                <th className="p-2 text-start">الحالة</th>
                <th className="p-2 text-start">إجراءات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {rows.map((r) => (
                <tr key={String(r.id)}>
                  <td className="p-2 font-mono">
                    {r.cashBoxCode} {r.cashBoxNameAr}
                  </td>
                  <td className="p-2">{r.countedOn}</td>
                  <td className="p-2 num">{formatSar(r.bookBalance)}</td>
                  <td className="p-2 num">{formatSar(r.countedAmount)}</td>
                  <td className="p-2 num">{formatSar(r.variance)}</td>
                  <td className="p-2">{r.status}</td>
                  <td className="p-2 space-x-1 space-x-reverse">
                    {r.allowedActions?.includes('submit') && (
                      <button type="button" disabled={busy} onClick={() => void act(String(r.id), 'submit')} className="px-2 py-1 rounded-lg border border-[var(--line)] text-[10px] font-bold">
                        تقديم
                      </button>
                    )}
                    {r.allowedActions?.includes('approve') && (
                      <button type="button" disabled={busy} onClick={() => void act(String(r.id), 'approve')} className="px-2 py-1 rounded-lg border border-[var(--line)] text-[10px] font-bold">
                        اعتماد
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
