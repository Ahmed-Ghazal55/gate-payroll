'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { api, type DebtDeductionsView, type PayCycleItem } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';

type Row = DebtDeductionsView['rows'][number];

const STATUS_AR: Record<string, string> = { pending: 'في انتظار التأكيد', confirmed: 'مؤكد', salary_pending: 'الراتب لم يعتمد بعد' };

// BR §24.7 (P2): personal debts are chosen and confirmed after salaries; the bank file holds these riders until then.
export function DebtDeductionStage({ cycle, onChanged }: { cycle: PayCycleItem; onChanged: () => void }) {
  const { toast } = useToast();
  const [view, setView] = useState<DebtDeductionsView | null>(null);
  const [busy, setBusy] = useState(false);
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [gateEnabled, setGateEnabled] = useState<boolean | null>(null);
  const [gateReason, setGateReason] = useState('');
  const app = cycle.invoices?.[0]?.app ?? 'ninja';

  const load = useCallback(async () => {
    try {
      setView(await api.getDebtDeductions(cycle.id));
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر تحميل مرحلة الديون', 'danger');
    }
    try {
      setGateEnabled((await api.getPostingGate(app)).enabled);
    } catch {
      setGateEnabled(null); // Administrators only; others just don't see the gate.
    }
  }, [cycle.id, app, toast]);
  useEffect(() => { void load(); }, [load]);

  const run = async (work: () => Promise<unknown>, done: string) => {
    setBusy(true);
    try {
      await work();
      toast(done, 'success');
      await load();
      onChanged();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر تنفيذ العملية', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const rows: Row[] = useMemo(() => view?.rows ?? [], [view?.rows]);
  const pending = rows.filter((r) => r.status === 'pending');
  const canApproveMore = (cycle.allowedActions ?? []).includes('approve_salaries') && cycle.stage >= 7;

  const debtStageCols = useMemo(
    () => [
      { key: 'rider', header: 'المندوب', type: 'text' as const, getValue: (r: Row) => r.rider || '' },
      { key: 'salaryNet', header: 'صافي الراتب', type: 'number' as const, getValue: (r: Row) => Number(r.salaryNet ?? 0) },
      {
        key: 'items',
        header: 'الديون',
        type: 'text' as const,
        getValue: (r: Row) =>
          (r.items || []).map((it) => `${it.labelAr || it.category} ${it.amount}`).join(' | ') || 'لا توجد',
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (r: Row) => r.status || '',
        labelOf: (v: unknown) => STATUS_AR[String(v ?? '')] ?? String(v ?? '—'),
        toneOf: (v: unknown) =>
          v === 'confirmed' ? 'success' : v === 'pending' ? 'warning' : 'neutral',
      },
    ],
    []
  );
  const debtStageFilter = useTableAutoFilter<Row>(rows, debtStageCols);

  return (
    <section id="debt-deduction-stage" className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-bold text-[var(--ink)]">مرحلة 8: تأكيد استقطاع الديون</h3>
          <p className="text-xs text-[var(--muted)]">
            حدد مين يتخصم منه دين الشهر ده. الصفر تأجيل بلا حركة. اللي لسه ما اتأكدش ماينزلش في ملف البنك.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {canApproveMore && (
            <button type="button" disabled={busy} onClick={() => run(() => api.advancePayCycle(cycle.id, { stage: 7 }), 'تم اعتماد الصفوف الجاهزة.')} className="px-3 py-1.5 rounded-lg border border-[var(--accent)] text-xs font-bold text-[var(--accent)] disabled:opacity-50">
              اعتماد الصفوف الجاهزة المتبقية
            </button>
          )}
          {pending.length > 0 && (
            <button type="button" id="debt-confirm-all" disabled={busy} onClick={() => run(() => api.actDebtDeductions(cycle.id, { action: 'confirm-all' }), 'تم تأكيد استقطاع الديون.')} className="px-3 py-1.5 rounded-lg bg-[var(--accent)] text-xs font-bold text-white disabled:opacity-50">
              تأكيد الكل ({pending.length})
            </button>
          )}
        </div>
      </div>

      {gateEnabled === false && (
        <div id="posting-gate" className="flex flex-wrap items-center gap-2 rounded-lg border border-[var(--warning)]/40 bg-[var(--warning)]/10 p-2 text-xs">
          <span className="font-bold text-[var(--ink)]">الترحيل المحاسبي لهذا التطبيق مقفول، والاعتماد النهائي هيتوقف بسببه.</span>
          <input value={gateReason} onChange={(e) => setGateReason(e.target.value)} placeholder="سبب الفتح" className="min-w-48 rounded-md border border-[var(--line)] bg-[var(--surface)] p-1 text-[var(--ink)]" />
          <button type="button" disabled={busy || gateReason.trim().length < 3} onClick={() => run(() => api.setPostingGate(app, true, gateReason.trim()), 'تم فتح الترحيل.')} className="rounded-md bg-[var(--accent)] px-2 py-1 font-bold text-white disabled:opacity-50">
            فتح الترحيل
          </button>
        </div>
      )}

      {rows.length === 0 ? (
        <p className="text-xs text-[var(--muted)]">لا يوجد مناديب في المرحلة دي بعد.</p>
      ) : (
        <div className="overflow-x-auto">
          <AutoFilterBar controller={debtStageFilter} totalCount={rows.length} />
          <table className="w-full text-xs">
            <thead className="text-[var(--muted)]">
              <tr>
                <AutoFilterTh controller={debtStageFilter} colKey="rider" className="p-2 text-start" />
                <AutoFilterTh controller={debtStageFilter} colKey="salaryNet" className="p-2 text-start" />
                <AutoFilterTh controller={debtStageFilter} colKey="items" className="p-2 text-start" />
                <AutoFilterTh controller={debtStageFilter} colKey="status" className="p-2 text-start" />
                <th className="p-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {debtStageFilter.rows.map((row) => (
                <tr key={row.rider} data-rider-row>
                  <td className="p-2 font-mono" dir="ltr">{row.rider}</td>
                  <td className="p-2 font-mono">{row.salaryNet.toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
                  <td className="p-2 space-y-1">
                    {row.items.length === 0 && <span className="text-[var(--muted)]">لا توجد</span>}
                    {row.items.map((item) => {
                      const id = `${row.rider}:${item.key}`;
                      const editable = row.allowedActions.includes('edit');
                      return (
                        <div key={item.key} className="flex flex-wrap items-center gap-1">
                          <span className="min-w-28">{item.labelAr || item.category}</span>
                          <span className="text-[var(--muted)]">الرصيد {item.remainingBalance.toLocaleString('en-US')} · القسط {item.due.toLocaleString('en-US')}</span>
                          {editable ? (
                            <>
                              <input aria-label="مبلغ الخصم" inputMode="decimal" value={amounts[id] ?? String(item.amount)} onChange={(e) => setAmounts((p) => ({ ...p, [id]: e.target.value }))} className="w-20 rounded-md border border-[var(--line)] bg-[var(--surface)] p-1 font-mono text-[var(--ink)]" />
                              <input aria-label="السبب" value={reasons[id] ?? ''} onChange={(e) => setReasons((p) => ({ ...p, [id]: e.target.value }))} placeholder="السبب لو غير القسط" className="w-36 rounded-md border border-[var(--line)] bg-[var(--surface)] p-1 text-[var(--ink)]" />
                              <button type="button" disabled={busy} onClick={() => run(() => api.actDebtDeductions(cycle.id, { action: 'edit', rider: row.rider, key: item.key, amount: Number(amounts[id] ?? item.amount), reason: reasons[id] }), 'تم حفظ المبلغ.')} className="rounded-md border border-[var(--line)] px-2 py-1 font-bold disabled:opacity-50">حفظ</button>
                            </>
                          ) : (
                            <span className="font-mono font-bold">{item.amount.toLocaleString('en-US')}</span>
                          )}
                        </div>
                      );
                    })}
                  </td>
                  <td className="p-2">{STATUS_AR[row.status] ?? row.status}</td>
                  <td className="p-2">
                    {row.allowedActions.includes('confirm') && (
                      <button type="button" disabled={busy} onClick={() => run(() => api.actDebtDeductions(cycle.id, { action: 'confirm', rider: row.rider }), 'تم التأكيد.')} className="rounded-md bg-[var(--accent)] px-2 py-1 font-bold text-white disabled:opacity-50">تأكيد</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
