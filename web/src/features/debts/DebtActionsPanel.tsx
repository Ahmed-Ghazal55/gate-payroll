'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Banknote, Plus, RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { formatSar } from '../../lib/money';

export function DebtActionsPanel({ selectedDebtId = '' }: { selectedDebtId?: string }) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canCollect = can('debts.collect') || can('debts.manage') || isAdmin;
  const canCreate = can('debts.create') || can('debts.manage') || isAdmin;
  const canLedgerAct = can('debts.waive') || can('debts.collect') || can('debts.manage') || isAdmin;
  const canView = can('debts.view') || canCollect || canCreate || canLedgerAct || isAdmin;
  const [settlements, setSettlements] = useState<Array<Record<string, unknown>>>([]);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [debtId, setDebtId] = useState(selectedDebtId);
  const [collect, setCollect] = useState({ collectedAmount: '', waivedAmount: '0', receiptRef: '', targetVaultCode: '', period: '2026-10', notes: '', waiverReason: '' });
  const [ledger, setLedger] = useState({
    amount: '',
    reason: '',
    approvalRequestId: '',
    period: '2026-10',
    cashAccountCode: '111101',
    companyId: '',
  });
  const [createForm, setCreateForm] = useState({ iqama: '', category: 'CashAdvance', totalAmount: '', monthlyInstallment: '', creditorSponsorId: 'BW', notes: '' });
  const [categories, setCategories] = useState<Array<{ code: string; nameAr: string }>>([]);
  const [sponsors, setSponsors] = useState<Array<{ id: string; code: string; nameAr: string }>>([]);

  useEffect(() => { if (selectedDebtId) setDebtId(selectedDebtId); }, [selectedDebtId]);

  useEffect(() => {
    let cancelled = false;
    api.getFinanceCatalog()
      .then((cat) => {
        if (cancelled) return;
        const list = (cat?.categories || [])
          .filter((c) => c.code)
          .map((c) => ({ code: String(c.code), nameAr: String(c.nameAr || c.code) }));
        setCategories(list);
      })
      .catch(() => {});
    api.getFinanceSponsors()
      .then((list) => {
        if (cancelled) return;
        setSponsors(
          (Array.isArray(list) ? list : []).map((s) => ({
            id: s.id,
            code: s.code || s.id,
            nameAr: s.nameAr || s.shortNameAr || s.code || s.id,
          }))
        );
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, []);

  const loadSettlements = useCallback(() => {
    if (!canView) return;
    setLoading(true);
    api.getDebtSettlements(debtId ? { debtId } : undefined)
      .then((rows) => setSettlements(Array.isArray(rows) ? rows : []))
      .catch(() => setSettlements([]))
      .finally(() => setLoading(false));
  }, [canView, debtId]);

  useEffect(() => { loadSettlements(); }, [loadSettlements]);

  const settlementCols = useMemo(
    () => [
      { key: 'receiptRef', header: 'السند', type: 'text' as const, getValue: (s: Record<string, unknown>) => String(s.receiptRef || '') },
      { key: 'debtId', header: 'الدين', type: 'text' as const, getValue: (s: Record<string, unknown>) => String(s.debtId || '') },
      { key: 'collectedAmount', header: 'محصّل', type: 'number' as const, getValue: (s: Record<string, unknown>) => Number(s.collectedAmount || 0) },
      { key: 'waivedAmount', header: 'إعفاء', type: 'number' as const, getValue: (s: Record<string, unknown>) => Number(s.waivedAmount || 0) },
      { key: 'date', header: 'التاريخ', type: 'date' as const, getValue: (s: Record<string, unknown>) => String(s.collectionDate || s.createdAt || '') },
    ],
    []
  );
  const settlementFilter = useTableAutoFilter<Record<string, unknown>>(settlements, settlementCols);

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
          <label className="text-xs font-semibold space-y-1">
            <span>رقم الإقامة</span>
            <input required placeholder="مثال: 2000000101" value={createForm.iqama} onChange={(e) => setCreateForm({ ...createForm, iqama: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>فئة المديونية</span>
            {categories.length > 0 ? (
              <select required value={createForm.category} onChange={(e) => setCreateForm({ ...createForm, category: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
                {categories.map((c) => (
                  <option key={c.code} value={c.code}>{c.nameAr}</option>
                ))}
              </select>
            ) : (
              <input required placeholder="فئة المديونية" value={createForm.category} onChange={(e) => setCreateForm({ ...createForm, category: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
            )}
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>المبلغ الإجمالي (ر.س)</span>
            <input required type="number" step="0.01" min="0" placeholder="0.00" value={createForm.totalAmount} onChange={(e) => setCreateForm({ ...createForm, totalAmount: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>القسط الشهري (ر.س)</span>
            <input required type="number" step="0.01" min="0" placeholder="0.00" value={createForm.monthlyInstallment} onChange={(e) => setCreateForm({ ...createForm, monthlyInstallment: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>الشركة الدائنة</span>
            {sponsors.length > 0 ? (
              <select required value={createForm.creditorSponsorId} onChange={(e) => setCreateForm({ ...createForm, creditorSponsorId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
                {sponsors.map((s) => (
                  <option key={s.id} value={s.code}>{s.nameAr} ({s.code})</option>
                ))}
              </select>
            ) : (
              <input required placeholder="رمز الشركة الدائنة" value={createForm.creditorSponsorId} onChange={(e) => setCreateForm({ ...createForm, creditorSponsorId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
            )}
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>ملاحظات</span>
            <input placeholder="ملاحظات اختيارية" value={createForm.notes} onChange={(e) => setCreateForm({ ...createForm, notes: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          </label>
          <div className="sm:col-span-2 lg:col-span-3 flex justify-end">
            <button type="submit" disabled={busy} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">إنشاء</button>
          </div>
        </form>
      )}

      {canCollect && (
        <form onSubmit={onCollect} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          <h3 className="sm:col-span-2 lg:col-span-3 text-sm font-bold inline-flex items-center gap-2"><Banknote size={16} /> تحصيل دين</h3>
          <label className="text-xs font-semibold space-y-1">
            <span>معرّف المديونية</span>
            <input required placeholder="أدخل معرّف المديونية" value={debtId} onChange={(e) => setDebtId(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>المبلغ المحصّل (ر.س)</span>
            <input required type="number" step="0.01" min="0" placeholder="0.00" value={collect.collectedAmount} onChange={(e) => setCollect({ ...collect, collectedAmount: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>مبلغ الإعفاء (ر.س)</span>
            <input type="number" step="0.01" min="0" placeholder="0.00" value={collect.waivedAmount} onChange={(e) => setCollect({ ...collect, waivedAmount: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>رقم سند التحصيل</span>
            <input required placeholder="مثال: REC-1001" value={collect.receiptRef} onChange={(e) => setCollect({ ...collect, receiptRef: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>حساب الخزنة المستلمة</span>
            <input placeholder="رمز الخزنة (اختياري)" value={collect.targetVaultCode} onChange={(e) => setCollect({ ...collect, targetVaultCode: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>الفترة المالية</span>
            <input type="month" value={collect.period} onChange={(e) => setCollect({ ...collect, period: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>ملاحظات</span>
            <input placeholder="ملاحظات اختيارية" value={collect.notes} onChange={(e) => setCollect({ ...collect, notes: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>سبب الإعفاء</span>
            <input placeholder="سبب الإعفاء عند وجود إعفاء" value={collect.waiverReason} onChange={(e) => setCollect({ ...collect, waiverReason: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          </label>
          <div className="sm:col-span-2 lg:col-span-3 flex justify-end">
            <button type="submit" disabled={busy} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">تحصيل</button>
          </div>
        </form>
      )}

      {canLedgerAct && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
          <h3 className="sm:col-span-2 lg:col-span-3 text-sm font-bold">إعفاء / تحمّل / تحصيل دفتري (بعد اعتماد)</h3>
          <label className="text-xs font-semibold space-y-1">
            <span>معرّف المديونية</span>
            <input placeholder="أدخل معرّف المديونية" value={debtId} onChange={(e) => setDebtId(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>المبلغ (ر.س)</span>
            <input type="number" step="0.01" min="0" placeholder="0.00" value={ledger.amount} onChange={(e) => setLedger({ ...ledger, amount: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>السبب / البيان</span>
            <input placeholder="سبب الإجراء أو البيان" value={ledger.reason} onChange={(e) => setLedger({ ...ledger, reason: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>رقم طلب الاعتماد</span>
            <input placeholder="معرّف طلب الاعتماد" value={ledger.approvalRequestId} onChange={(e) => setLedger({ ...ledger, approvalRequestId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>الفترة المالية</span>
            <input type="month" value={ledger.period} onChange={(e) => setLedger({ ...ledger, period: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs num" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>حساب النقدية</span>
            <input placeholder="مثال: 111101" value={ledger.cashAccountCode} onChange={(e) => setLedger({ ...ledger, cashAccountCode: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          </label>
          <label className="text-xs font-semibold space-y-1">
            <span>الشركة (للطباعة)</span>
            {sponsors.length > 0 ? (
              <select value={ledger.companyId} onChange={(e) => setLedger({ ...ledger, companyId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
                <option value="">اختر الشركة…</option>
                {sponsors.map((s) => (
                  <option key={s.id} value={s.code}>{s.nameAr} ({s.code})</option>
                ))}
              </select>
            ) : (
              <input placeholder="رمز الشركة" value={ledger.companyId} onChange={(e) => setLedger({ ...ledger, companyId: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
            )}
          </label>
          <div className="sm:col-span-2 lg:col-span-3 flex flex-wrap gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                void (async () => {
                  setBusy(true);
                  try {
                    await api.waiveDebtLedger(debtId, {
                      amount: Number(ledger.amount),
                      reason: ledger.reason,
                      approvalRequestId: ledger.approvalRequestId,
                      period: ledger.period,
                    });
                    toast('تم الإعفاء على دفتر W2', 'success');
                    loadSettlements();
                  } catch (err: unknown) {
                    toast(err instanceof ApiError ? err.message : 'تعذر الإعفاء', 'danger');
                  } finally {
                    setBusy(false);
                  }
                })();
              }}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50"
            >
              إعفاء
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                void (async () => {
                  setBusy(true);
                  try {
                    await api.absorbDebtLedger(debtId, {
                      amount: Number(ledger.amount),
                      reason: ledger.reason,
                      approvalRequestId: ledger.approvalRequestId,
                      period: ledger.period,
                    });
                    toast('تم تحمّل الشركة للدين', 'success');
                    loadSettlements();
                  } catch (err: unknown) {
                    toast(err instanceof ApiError ? err.message : 'تعذر التحمّل', 'danger');
                  } finally {
                    setBusy(false);
                  }
                })();
              }}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50"
            >
              تحمّل الشركة
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                void (async () => {
                  setBusy(true);
                  try {
                    await api.collectDebtLedger(debtId, {
                      collectedAmount: Number(ledger.amount),
                      period: ledger.period,
                      cashAccountCode: ledger.cashAccountCode || '111101',
                      notes: ledger.reason,
                    });
                    toast('تم التحصيل على دفتر W2', 'success');
                    loadSettlements();
                  } catch (err: unknown) {
                    toast(err instanceof ApiError ? err.message : 'تعذر التحصيل الدفتري', 'danger');
                  } finally {
                    setBusy(false);
                  }
                })();
              }}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50"
            >
              تحصيل دفتري
            </button>
            <button
              type="button"
              disabled={busy || !ledger.companyId}
              onClick={() => {
                void (async () => {
                  setBusy(true);
                  try {
                    const res = await api.printDebtsRegister(ledger.companyId);
                    toast(`جاهز للطباعة (${Array.isArray((res as { items?: unknown[] }).items) ? (res as { items: unknown[] }).items.length : '—'} صف)`, 'success');
                  } catch (err: unknown) {
                    toast(err instanceof ApiError ? err.message : 'تعذر تحميل سجل الطباعة', 'danger');
                  } finally {
                    setBusy(false);
                  }
                })();
              }}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50"
            >
              طباعة السجل
            </button>
          </div>
        </div>
      )}

      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
        <div className="p-3 border-b border-[var(--line)] flex items-center justify-between">
          <span className="text-xs font-bold">التحصيلات ({settlements.length})</span>
          <button type="button" onClick={loadSettlements} className="inline-flex items-center gap-1 text-xs font-semibold"><RefreshCw size={14} /> تحديث</button>
        </div>
        {loading ? <Skeleton className="h-24 w-full" /> : settlements.length === 0 ? (
          <div className="p-4 text-xs text-[var(--muted)]">لا تحصيلات معروضة.</div>
        ) : (
          <div>
            <AutoFilterBar controller={settlementFilter} totalCount={settlements.length} />
            <table className="w-full text-xs">
              <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
                <tr>
                  <AutoFilterTh controller={settlementFilter} colKey="receiptRef" className="p-2 text-start" />
                  <AutoFilterTh controller={settlementFilter} colKey="debtId" className="p-2 text-start" />
                  <AutoFilterTh controller={settlementFilter} colKey="collectedAmount" className="p-2 text-start" />
                  <AutoFilterTh controller={settlementFilter} colKey="waivedAmount" className="p-2 text-start" />
                  <AutoFilterTh controller={settlementFilter} colKey="date" className="p-2 text-start" />
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]">
                {settlementFilter.rows.slice(0, 100).map((s) => (
                  <tr key={String(s.id)}>
                    <td className="p-2 font-mono">{String(s.receiptRef || '')}</td>
                    <td className="p-2 font-mono">{String(s.debtId || '')}</td>
                    <td className="p-2 num whitespace-nowrap">{formatSar(s.collectedAmount as number | string | null | undefined)}</td>
                    <td className="p-2 num whitespace-nowrap">{formatSar(s.waivedAmount as number | string | null | undefined)}</td>
                    <td className="p-2">{String(s.collectionDate || s.createdAt || '')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
