'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { formatSar } from '../../lib/money';
import type { VaultCashBox } from '../../lib/types';

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
  const [cashBoxes, setCashBoxes] = useState<VaultCashBox[]>([]);
  const [listAllowed, setListAllowed] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ cashBoxId: '', countedOn: '', countedAmount: '' });

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.listVaultCounts();
      setRows(Array.isArray(res.items) ? (res.items as VaultCountRow[]) : []);
      setCashBoxes(Array.isArray(res.cashBoxes) ? res.cashBoxes : []);
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

  const cols = useMemo(
    () => [
      {
        key: 'cashBox',
        header: 'الخزنة',
        type: 'text' as const,
        getValue: (r: VaultCountRow) => `${r.cashBoxCode || ''} ${r.cashBoxNameAr || ''}`.trim(),
      },
      { key: 'countedOn', header: 'التاريخ', type: 'date' as const, getValue: (r: VaultCountRow) => String(r.countedOn || '') },
      { key: 'bookBalance', header: 'دفتر', type: 'number' as const, getValue: (r: VaultCountRow) => Number(r.bookBalance || 0) },
      { key: 'countedAmount', header: 'جرد', type: 'number' as const, getValue: (r: VaultCountRow) => Number(r.countedAmount || 0) },
      { key: 'variance', header: 'فرق', type: 'number' as const, getValue: (r: VaultCountRow) => Number(r.variance || 0) },
      { key: 'status', header: 'الحالة', type: 'text' as const, getValue: (r: VaultCountRow) => String(r.status || '') },
    ],
    []
  );
  const filter = useTableAutoFilter<VaultCountRow>(rows, cols);

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
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
            <label className="text-xs font-semibold space-y-1">
              <span>الخزنة</span>
              {cashBoxes.length > 0 ? (
                <select
                  value={form.cashBoxId}
                  onChange={(e) => setForm({ ...form, cashBoxId: e.target.value })}
                  className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs"
                >
                  <option value="">اختر الخزنة…</option>
                  {cashBoxes.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.code} — {b.nameAr}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  placeholder="معرّف الخزنة"
                  value={form.cashBoxId}
                  onChange={(e) => setForm({ ...form, cashBoxId: e.target.value })}
                  className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
                />
              )}
            </label>
            <label className="text-xs font-semibold space-y-1">
              <span>تاريخ الجرد</span>
              <input
                type="date"
                value={form.countedOn}
                onChange={(e) => setForm({ ...form, countedOn: e.target.value })}
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
              />
            </label>
            <label className="text-xs font-semibold space-y-1">
              <span>المبلغ المعدود فعليًا (ر.س)</span>
              <input
                type="number"
                step="0.01"
                min="0"
                placeholder="0.00"
                value={form.countedAmount}
                onChange={(e) => setForm({ ...form, countedAmount: e.target.value })}
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num"
              />
            </label>
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
          <AutoFilterBar controller={filter} totalCount={rows.length} />
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
              <tr>
                <AutoFilterTh controller={filter} colKey="cashBox" className="p-2 text-start" />
                <AutoFilterTh controller={filter} colKey="countedOn" className="p-2 text-start" />
                <AutoFilterTh controller={filter} colKey="bookBalance" className="p-2 text-start" />
                <AutoFilterTh controller={filter} colKey="countedAmount" className="p-2 text-start" />
                <AutoFilterTh controller={filter} colKey="variance" className="p-2 text-start" />
                <AutoFilterTh controller={filter} colKey="status" className="p-2 text-start" />
                <th className="p-2 text-start">إجراءات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {filter.rows.map((r) => (
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
