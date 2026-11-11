'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { Banknote, Plus, RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, Skeleton } from '../../components/ui/EmptyState';

export function DebtActionsPanel({ selectedDebtId = '' }: { selectedDebtId?: string }) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canCollect = can('debts.collect') || can('debts.manage') || isAdmin;
  const canCreate = can('debts.create') || can('debts.manage') || isAdmin;
  const canView = can('debts.view') || canCollect || canCreate || isAdmin;
  const [settlements, setSettlements] = useState<Array<Record<string, unknown>>>([]);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [debtId, setDebtId] = useState(selectedDebtId);
  const [collect, setCollect] = useState({ collectedAmount: '', waivedAmount: '0', receiptRef: '', targetVaultCode: '', period: '2026-10', notes: '', waiverReason: '' });
  const [createForm, setCreateForm] = useState({ iqama: '', category: 'CashAdvance', totalAmount: '', monthlyInstallment: '', creditorSponsorId: 'BW', notes: '' });

  useEffect(() => { if (selectedDebtId) setDebtId(selectedDebtId); }, [selectedDebtId]);

  const loadSettlements = useCallback(() => {
    if (!canView) return;
    setLoading(true);
    api.getDebtSettlements(debtId ? { debtId } : undefined)
      .then((rows) => setSettlements(Array.isArray(rows) ? rows : []))
      .catch(() => setSettlements([]))
      .finally(() => setLoading(false));
  }, [canView, debtId]);

  useEffect(() => { loadSettlements(); }, [loadSettlements]);

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض التحصيلات يحتاج صلاحية ديون." />;
  }

  const onCollect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canCollect) return;
    setBusy(true);
    try {
      const body: Record<string, unknown> = {
        collectedAmount: Number(collect.collectedAmount || 0),
        waivedAmount: Number(collect.waivedAmount || 0),
        receiptRef: collect.receiptRef,
        period: collect.period,
        notes: collect.notes || undefined,
      };
      if (collect.targetVaultCode) body.targetVaultCode = collect.targetVaultCode;
      if (Number(collect.waivedAmount || 0) > 0) body.waiverReason = collect.waiverReason;
      await api.collectDebt(debtId, body);
      toast('تم التحصيل', 'success');
      loadSettlements();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التحصيل', 'danger');
    } finally { setBusy(false); }
  };

  const onCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canCreate) return;
    setBusy(true);
    try {
      await api.createDebt({
        ...createForm,
        totalAmount: Number(createForm.totalAmount),
        monthlyInstallment: Number(createForm.monthlyInstallment),
      });
      toast('أُنشئ الدين', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإنشاء', 'danger');
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-4">
      {canCreate && (
        <form onSubmit={onCreate} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          <h3 className="sm:col-span-2 lg:col-span-3 text-sm font-bold inline-flex items-center gap-2"><Plus size={16} /> دين جديد</h3>
          {(['iqama', 'category', 'totalAmount', 'monthlyInstallment', 'creditorSponsorId', 'notes'] as const).map((k) => (
            <input key={k} required={k !== 'notes'} placeholder={k} value={createForm[k]} onChange={(e) => setCreateForm({ ...createForm, [k]: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          ))}
          <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">إنشاء</button>
        </form>
      )}

      {canCollect && (
        <form onSubmit={onCollect} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          <h3 className="sm:col-span-2 lg:col-span-3 text-sm font-bold inline-flex items-center gap-2"><Banknote size={16} /> تحصيل دين</h3>
          <input required placeholder="debtId" value={debtId} onChange={(e) => setDebtId(e.target.value)} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          {(['collectedAmount', 'waivedAmount', 'receiptRef', 'targetVaultCode', 'period', 'notes', 'waiverReason'] as const).map((k) => (
            <input key={k} required={k === 'receiptRef' || k === 'collectedAmount'} placeholder={k} value={collect[k]} onChange={(e) => setCollect({ ...collect, [k]: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          ))}
          <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">تحصيل</button>
        </form>
      )}

      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
        <div className="p-3 border-b border-[var(--line)] flex items-center justify-between">
          <span className="text-xs font-bold">التحصيلات ({settlements.length})</span>
          <button type="button" onClick={loadSettlements} className="inline-flex items-center gap-1 text-xs font-semibold"><RefreshCw size={14} /> تحديث</button>
        </div>
        {loading ? <Skeleton className="h-24 w-full" /> : settlements.length === 0 ? (
          <div className="p-4 text-xs text-[var(--muted)]">لا تحصيلات معروضة.</div>
        ) : (
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
              <tr>
                <th className="p-2 text-start">السند</th>
                <th className="p-2 text-start">الدين</th>
                <th className="p-2 text-start">محصّل</th>
                <th className="p-2 text-start">إعفاء</th>
                <th className="p-2 text-start">التاريخ</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {settlements.slice(0, 100).map((s) => (
                <tr key={String(s.id)}>
                  <td className="p-2 font-mono">{String(s.receiptRef || '')}</td>
                  <td className="p-2 font-mono">{String(s.debtId || '')}</td>
                  <td className="p-2 font-mono">{Number(s.collectedAmount || 0).toFixed(2)}</td>
                  <td className="p-2 font-mono">{Number(s.waivedAmount || 0).toFixed(2)}</td>
                  <td className="p-2">{String(s.collectionDate || s.createdAt || '')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
