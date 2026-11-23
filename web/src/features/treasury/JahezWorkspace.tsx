'use client';

import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  jahezReconciliationStatusLabelAr,
  jahezTotalsKeyLabelAr,
} from './jahez-labels';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw, FilePlus2, Send, CheckCircle2, Scale } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import type { JahezReconciliationDoc } from '../../lib/types';

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <label className="text-xs font-semibold space-y-1 block">
      <span>{label}</span>
      {children}
    </label>
  );
}

const inputClass =
  'w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs';

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
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);

  const [companyId, setCompanyId] = useState('');
  const { period, setPeriod } = usePeriod();
  const [invoiceNet, setInvoiceNet] = useState('0');
  const [invoiceVat, setInvoiceVat] = useState('0');
  const [lastTransactionDate, setLastTransactionDate] = useState(() => periodDateRange(period).to);
  const [invoiceReference, setInvoiceReference] = useState('');
  const [notes, setNotes] = useState('');

  useEffect(() => {
    setLastTransactionDate(periodDateRange(period).to);
  }, [period]);

  const [transfer, setTransfer] = useState({
    fromVaultCode: '',
    toVaultCode: '',
    amount: '',
    sourceRef: '',
    notes: '',
  });
  const [settlement, setSettlement] = useState({
    sponsorId: '',
    action: 'receive',
    amount: '',
    sourceRef: '',
    notes: '',
  });
  const [feeDeposit, setFeeDeposit] = useState({
    sponsorId: '',
    amount: '',
    sourceRef: '',
    notes: '',
  });
  const [fee, setFee] = useState({ sponsorId: '', iqama: '', amount: '200', notes: '' });

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      const first = list[0]?.code || list[0]?.id || '';
      if (first) {
        setCompanyId((prev) => prev || first);
        setSettlement((s) => ({ ...s, sponsorId: s.sponsorId || first }));
        setFeeDeposit((s) => ({ ...s, sponsorId: s.sponsorId || first }));
        setFee((s) => ({ ...s, sponsorId: s.sponsorId || first }));
      }
    });
  }, []);

  const companyOptions = companies.map((c) => ({
    value: c.code || c.id || '',
    label: companyLabel(c),
  }));

  const load = useCallback(() => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
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

  useEffect(() => {
    load();
  }, [load]);

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="خزينة جاهز تحتاج صلاحية خزينة." />;
  }

  const build = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canWrite) return;
    setBusy(true);
    try {
      const doc = await api.buildJahezReconciliation({
        companyId,
        period,
        invoiceNet: Number(invoiceNet),
        invoiceVat: Number(invoiceVat),
        lastTransactionDate,
        invoiceReference: invoiceReference || undefined,
        notes: notes || undefined,
      });
      toast('تم بناء تقرير المطابقة', 'success');
      setSelected(doc);
      load();
    } catch (err: unknown) {
      const msg =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
            ? err.message
            : 'تعذر بناء تقرير المطابقة';
      toast(msg || 'تعذر بناء تقرير المطابقة', 'danger');
    } finally {
      setBusy(false);
    }
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
    } finally {
      setBusy(false);
    }
  };

  const runOp = async (label: string, fn: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await fn();
      toast(label, 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'فشلت العملية', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">جاهز — مطابقة التسوية وعمليات الخزينة</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">تقارير المطابقة، التحويلات، التسوية، الرسوم، والعهدة.</p>
        </div>
        <button
          type="button"
          onClick={load}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-semibold"
        >
          <RefreshCw size={14} /> تحديث
        </button>
      </div>

      {canWrite && (
        <form
          onSubmit={build}
          className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]"
        >
          <Field label="الشركة">
            <SearchableSelect
              id="jahez-build-company"
              value={companyId}
              onChange={setCompanyId}
              ariaLabel="الشركة"
              placeholder="اختر الشركة"
              options={companyOptions}
              className="w-full"
            />
          </Field>
          <Field label="الشهر">
            <input
              required
              type="month"
              value={period}
              onChange={(e) => setPeriod(e.target.value)}
              className={inputClass}
            />
          </Field>
          <Field label="آخر معاملة">
            <input
              required
              type="date"
              value={lastTransactionDate}
              onChange={(e) => setLastTransactionDate(e.target.value)}
              className={inputClass}
            />
          </Field>
          <Field label="صافي الفاتورة">
            <input
              required
              value={invoiceNet}
              onChange={(e) => setInvoiceNet(e.target.value)}
              placeholder="0.00"
              className={`${inputClass} font-mono`}
            />
          </Field>
          <Field label="ضريبة الفاتورة">
            <input
              required
              value={invoiceVat}
              onChange={(e) => setInvoiceVat(e.target.value)}
              placeholder="0.00"
              className={`${inputClass} font-mono`}
            />
          </Field>
          <Field label="مرجع الفاتورة">
            <input
              value={invoiceReference}
              onChange={(e) => setInvoiceReference(e.target.value)}
              placeholder="رقم أو مرجع الفاتورة"
              className={inputClass}
            />
          </Field>
          <Field label="ملاحظات">
            <input
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="ملاحظات اختيارية"
              className={inputClass}
            />
          </Field>
          <div className="flex items-end">
            <button
              type="submit"
              disabled={busy || !companyId}
              className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
            >
              <FilePlus2 size={14} /> بناء تقرير مطابقة
            </button>
          </div>
        </form>
      )}

      {error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={load} />
      ) : loading ? (
        <Skeleton className="h-40 w-full" />
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
            <div className="p-3 border-b border-[var(--line)] text-xs font-bold">التقارير ({rows.length})</div>
            {rows.length === 0 ? (
              <div className="p-4 text-xs text-[var(--muted)]">لا تقارير بعد.</div>
            ) : (
              <ul className="divide-y divide-[var(--line)] max-h-80 overflow-auto">
                {rows.map((r) => (
                  <li key={r.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setSelected(r);
                        setVariance(r.varianceDecision || '');
                      }}
                      className="w-full text-start p-3 hover:bg-[var(--row-hover)] text-xs"
                    >
                      <div className="font-mono font-bold">
                        {r.period} · {r.companyId}
                      </div>
                      <div className="text-[var(--muted)] mt-0.5">
                        {jahezReconciliationStatusLabelAr(r.status)} · {r.invoiceReference || r.id}
                      </div>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 text-xs">
            {!selected ? (
              <p className="text-[var(--muted)]">اختر تقريرًا للتفاصيل والإجراءات.</p>
            ) : (
              <>
                <div className="font-bold font-mono">{selected.id}</div>
                <div>
                  الحالة: <b>{jahezReconciliationStatusLabelAr(selected.status)}</b>
                </div>
                {selected.totals && (
                  <div className="grid grid-cols-2 gap-2">
                    {Object.entries(selected.totals).map(([k, v]) => (
                      <div key={k} className="p-2 rounded-lg bg-[var(--raised)]/50">
                        <div className="text-[var(--muted)]">{jahezTotalsKeyLabelAr(k)}</div>
                        <div className="font-mono font-bold">{formatSar(Number(v))}</div>
                      </div>
                    ))}
                  </div>
                )}
                {canWrite && selected.status === 'draft' && (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => act(() => api.submitJahezReconciliation(selected.id), 'أُرسل للاعتماد')}
                    className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] font-semibold disabled:opacity-50"
                  >
                    <Send size={14} /> إرسال للاعتماد
                  </button>
                )}
                {canWrite && (selected.status === 'draft' || selected.status === 'pending_approval') && (
                  <div className="flex flex-wrap gap-2 items-end">
                    <label className="flex-1 space-y-1 font-semibold">
                      <span>قرار الفرق</span>
                      <input
                        value={variance}
                        onChange={(e) => setVariance(e.target.value)}
                        className={inputClass}
                      />
                    </label>
                    <button
                      type="button"
                      disabled={busy || variance.trim().length < 3}
                      onClick={() =>
                        act(() => api.decideJahezVariance(selected.id, variance.trim()), 'سُجّل قرار الفرق')
                      }
                      className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] font-semibold disabled:opacity-50"
                    >
                      <Scale size={14} /> حفظ القرار
                    </button>
                  </div>
                )}
                {canWrite && selected.status === 'pending_approval' && (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => act(() => api.approveJahezReconciliation(selected.id), 'اعتُمد التقرير')}
                    className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
                  >
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
          <form
            className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs"
            onSubmit={(e) => {
              e.preventDefault();
              void runOp('تم التحويل', () => api.jahezTransfer({ ...transfer, period, amount: Number(transfer.amount) }));
            }}
          >
            <h4 className="font-bold">نقل عهدة جاهز</h4>
            <Field label="من خزنة">
              <input
                required
                value={transfer.fromVaultCode}
                onChange={(e) => setTransfer({ ...transfer, fromVaultCode: e.target.value })}
                placeholder="رمز الخزنة المصدر"
                className={`${inputClass} font-mono`}
              />
            </Field>
            <Field label="إلى خزنة">
              <input
                required
                value={transfer.toVaultCode}
                onChange={(e) => setTransfer({ ...transfer, toVaultCode: e.target.value })}
                placeholder="رمز الخزنة الهدف"
                className={`${inputClass} font-mono`}
              />
            </Field>
            <Field label="المبلغ">
              <input
                required
                value={transfer.amount}
                onChange={(e) => setTransfer({ ...transfer, amount: e.target.value })}
                placeholder="0.00"
                className={`${inputClass} font-mono`}
              />
            </Field>
            <Field label="مرجع المصدر">
              <input
                required
                value={transfer.sourceRef}
                onChange={(e) => setTransfer({ ...transfer, sourceRef: e.target.value })}
                placeholder="مرجع العملية"
                className={inputClass}
              />
            </Field>
            <Field label="ملاحظات">
              <input
                value={transfer.notes}
                onChange={(e) => setTransfer({ ...transfer, notes: e.target.value })}
                placeholder="ملاحظات اختيارية"
                className={inputClass}
              />
            </Field>
            <Field label="الفترة">
              <input
                required
                type="month"
                value={period}
                onChange={(e) => setPeriod(e.target.value)}
                className={inputClass}
              />
            </Field>
            <button
              type="submit"
              disabled={busy}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
            >
              تنفيذ
            </button>
          </form>

          <form
            className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs"
            onSubmit={(e) => {
              e.preventDefault();
              void runOp('تمت التسوية', () =>
                api.jahezPlatformSettlement({ ...settlement, period, amount: Number(settlement.amount) })
              );
            }}
          >
            <h4 className="font-bold">تسوية منصة جاهز</h4>
            <Field label="نوع الحركة">
              <select
                value={settlement.action}
                onChange={(e) => setSettlement({ ...settlement, action: e.target.value })}
                className={inputClass}
              >
                <option value="receive">تحصيل</option>
                <option value="pay">سداد</option>
              </select>
            </Field>
            <Field label="الشركة الكفيلة">
              <SearchableSelect
                id="jahez-settlement-sponsor"
                value={settlement.sponsorId}
                onChange={(v) => setSettlement({ ...settlement, sponsorId: v })}
                ariaLabel="الشركة الكفيلة"
                placeholder="اختر الشركة"
                options={companyOptions}
                className="w-full"
              />
            </Field>
            <Field label="المبلغ">
              <input
                required
                value={settlement.amount}
                onChange={(e) => setSettlement({ ...settlement, amount: e.target.value })}
                placeholder="0.00"
                className={`${inputClass} font-mono`}
              />
            </Field>
            <Field label="مرجع المصدر">
              <input
                required
                value={settlement.sourceRef}
                onChange={(e) => setSettlement({ ...settlement, sourceRef: e.target.value })}
                placeholder="مرجع العملية"
                className={inputClass}
              />
            </Field>
            <Field label="ملاحظات">
              <input
                value={settlement.notes}
                onChange={(e) => setSettlement({ ...settlement, notes: e.target.value })}
                placeholder="ملاحظات اختيارية"
                className={inputClass}
              />
            </Field>
            <Field label="الفترة">
              <input
                required
                type="month"
                value={period}
                onChange={(e) => setPeriod(e.target.value)}
                className={inputClass}
              />
            </Field>
            <button
              type="submit"
              disabled={busy || !settlement.sponsorId}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
            >
              تنفيذ
            </button>
          </form>

          <form
            className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs"
            onSubmit={(e) => {
              e.preventDefault();
              void runOp('تم الإيداع', () =>
                api.jahezFeeDeposit({ ...feeDeposit, period, amount: Number(feeDeposit.amount) })
              );
            }}
          >
            <h4 className="font-bold">إيداع حصيلة الرسوم</h4>
            <Field label="الشركة الكفيلة">
              <SearchableSelect
                id="jahez-fee-deposit-sponsor"
                value={feeDeposit.sponsorId}
                onChange={(v) => setFeeDeposit({ ...feeDeposit, sponsorId: v })}
                ariaLabel="الشركة الكفيلة"
                placeholder="اختر الشركة"
                options={companyOptions}
                className="w-full"
              />
            </Field>
            <Field label="المبلغ">
              <input
                required
                value={feeDeposit.amount}
                onChange={(e) => setFeeDeposit({ ...feeDeposit, amount: e.target.value })}
                placeholder="0.00"
                className={`${inputClass} font-mono`}
              />
            </Field>
            <Field label="مرجع المصدر">
              <input
                required
                value={feeDeposit.sourceRef}
                onChange={(e) => setFeeDeposit({ ...feeDeposit, sourceRef: e.target.value })}
                placeholder="مرجع العملية"
                className={inputClass}
              />
            </Field>
            <Field label="ملاحظات">
              <input
                value={feeDeposit.notes}
                onChange={(e) => setFeeDeposit({ ...feeDeposit, notes: e.target.value })}
                placeholder="ملاحظات اختيارية"
                className={inputClass}
              />
            </Field>
            <Field label="الفترة">
              <input
                required
                type="month"
                value={period}
                onChange={(e) => setPeriod(e.target.value)}
                className={inputClass}
              />
            </Field>
            <button
              type="submit"
              disabled={busy || !feeDeposit.sponsorId}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
            >
              تنفيذ
            </button>
          </form>

          {canFee && (
            <form
              className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2 text-xs"
              onSubmit={(e) => {
                e.preventDefault();
                void runOp('سُجّل الرسم', () => api.jahezFee({ ...fee, amount: Number(fee.amount) }));
              }}
            >
              <h4 className="font-bold">رسم جاهز</h4>
              <Field label="الشركة الكفيلة">
                <SearchableSelect
                  id="jahez-fee-sponsor"
                  value={fee.sponsorId}
                  onChange={(v) => setFee({ ...fee, sponsorId: v })}
                  ariaLabel="الشركة الكفيلة"
                  placeholder="اختر الشركة"
                  options={companyOptions}
                  className="w-full"
                />
              </Field>
              <Field label="رقم الإقامة">
                <input
                  value={fee.iqama}
                  onChange={(e) => setFee({ ...fee, iqama: e.target.value })}
                  placeholder="رقم إقامة المندوب"
                  className={`${inputClass} font-mono`}
                />
              </Field>
              <Field label="المبلغ">
                <input
                  value={fee.amount}
                  onChange={(e) => setFee({ ...fee, amount: e.target.value })}
                  placeholder="200"
                  className={`${inputClass} font-mono`}
                />
              </Field>
              <Field label="ملاحظات">
                <input
                  value={fee.notes}
                  onChange={(e) => setFee({ ...fee, notes: e.target.value })}
                  placeholder="ملاحظات اختيارية"
                  className={inputClass}
                />
              </Field>
              <button
                type="submit"
                disabled={busy || !fee.sponsorId}
                className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
              >
                تنفيذ
              </button>
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
              const actions = Array.isArray(h.allowedActions) ? (h.allowedActions as string[]) : [];
              return (
                <li key={id} className="p-3 flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-mono font-bold">{id}</div>
                    <div className="text-[var(--muted)]">{status}</div>
                  </div>
                  <div className="flex gap-2">
                    {(actions.includes('confirm') || status === 'pending') && canWrite && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => void runOp('أُكّدت العهدة', () => api.confirmJahezHandover(id, {}))}
                        className="px-2 py-1 rounded-lg border border-[var(--line)] font-semibold disabled:opacity-50"
                      >
                        تأكيد
                      </button>
                    )}
                    {(actions.includes('reject') || status === 'pending') && canWrite && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() =>
                          void runOp('رُفضت العهدة', () =>
                            api.rejectJahezHandover(id, { reason: 'رفض من الواجهة' })
                          )
                        }
                        className="px-2 py-1 rounded-lg border border-[var(--danger)]/40 text-[var(--danger)] font-semibold disabled:opacity-50"
                      >
                        رفض
                      </button>
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
