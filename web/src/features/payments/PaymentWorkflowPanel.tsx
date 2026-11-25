'use client';

import React, { useMemo, useState } from 'react';
import {
  CheckCircle2,
  XCircle,
  Send,
  Pencil,
  Banknote,
  ShieldCheck,
  Plus,
  Ban,
} from 'lucide-react';
import {
  api,
  ApiError,
  type PaymentDocumentItem,
  type PaymentVoucherItem,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { formatSar } from '../../lib/money';
import {
  accountOptions,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentMeta,
  documentStatusLabel,
  hasAllowedAction,
  isBankOrCashCode,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
  VOUCHER_STATUS_LABELS,
} from './payment-engine';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)] focus:outline-hidden focus:border-[var(--accent)]';

export interface PaymentDocumentActionsProps {
  doc: PaymentDocumentItem;
  period: string;
  accounts: CoaAccountOption[];
  onUpdated: () => Promise<void> | void;
  onVoucherCreated?: (voucher: PaymentVoucherItem) => void;
  customPayConfirmText?: (doc: PaymentDocumentItem) => string;
}

/**
 * Unified payment-engine row actions strictly driven by server `allowedActions`:
 * edit_draft · submit · cancel_draft · approve · create_payment_request (+ voucher dual-approve/pay/cancel).
 */
export function PaymentDocumentActions({
  doc,
  period,
  accounts,
  onUpdated,
  onVoucherCreated,
  customPayConfirmText,
}: PaymentDocumentActionsProps) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canDecideApproval = can('approvals.decide') || isAdmin;

  const [busy, setBusy] = useState(false);
  const [confirmKind, setConfirmKind] = useState<'submit' | 'cancel' | 'approve' | null>(null);
  const [approveNote, setApproveNote] = useState('');
  const [coverageReason, setCoverageReason] = useState('');

  const [editOpen, setEditOpen] = useState(false);
  const [editRef, setEditRef] = useState('');
  const [editDueOn, setEditDueOn] = useState('');
  const [editAccountCode, setEditAccountCode] = useState('');
  const [editAmount, setEditAmount] = useState('');

  const [reserveOpen, setReserveOpen] = useState(false);
  const [reserveAmount, setReserveAmount] = useState('');
  const [reserveBankCode, setReserveBankCode] = useState('');
  const [reservePaidOn, setReservePaidOn] = useState('');
  const [reserveRef, setReserveRef] = useState('');
  const [reserveKey, setReserveKey] = useState('');
  const [activeVoucher, setActiveVoucher] = useState<PaymentVoucherItem | null>(null);

  const meta = useMemo(() => documentMeta(doc), [doc]);
  const isEditableType = ['quick', 'expense', 'purchase', 'government'].includes(doc.type);
  const isAdvanceType = doc.type === 'advance' || doc.type === 'hs_topup';
  const isCardAdvance = doc.type === 'advance' && String(meta.fulfilment || '') === 'recharge_card';

  const bankOptions = useMemo(() => {
    const filtered = accountOptions(accounts, (a) =>
      isBankOrCashCode(a.code) || (isCardAdvance && a.code === '112404')
    );
    if (filtered.length > 0) return filtered;
    const fallback = [
      { value: '111101', label: '111101 — الصندوق الرئيسي' },
      { value: '111102', label: '111102 — العهد النثرية' },
      { value: '113101', label: '113101 — البنك الرئيسي' },
    ];
    if (isCardAdvance) fallback.push({ value: '112404', label: '112404 — رصيد جهاز مدا' });
    return fallback;
  }, [accounts, isCardAdvance]);

  const allAccountOptions = useMemo(() => accountOptions(accounts), [accounts]);

  const openEdit = () => {
    const firstLine = (doc.lines || [])[0];
    const matched = accounts.find((a) => a.id === firstLine?.accountId);
    setEditRef(String(doc.reference || ''));
    setEditDueOn(String(doc.dueOn || defaultDateInPeriod(period)).slice(0, 10));
    setEditAccountCode(matched?.code || '');
    setEditAmount(String(doc.amount || ''));
    setEditOpen(true);
  };

  const openReserve = () => {
    const payoutCode = String(meta.payoutAccountCode || '');
    const avail = Number(doc.availableBalance ?? doc.amount ?? 0);
    setReserveAmount(avail > 0 ? avail.toFixed(2) : String(doc.amount || ''));
    setReserveBankCode(payoutCode || bankOptions[0]?.value || '111101');
    setReservePaidOn(defaultDateInPeriod(period));
    setReserveRef(String(doc.reference || doc.docNo || ''));
    setReserveKey(newIdempotencyKey(`pv-${doc.id.slice(0, 8)}`));
    setActiveVoucher(null);
    setReserveOpen(true);
  };

  const runTransition = async () => {
    if (!confirmKind) return;
    setBusy(true);
    try {
      if (confirmKind === 'submit') {
        await api.submitPaymentDocument(doc.id);
        toast('تم الإرسال للاعتماد — بانتظار الاعتماد المالي', 'success');
      } else if (confirmKind === 'cancel') {
        await api.cancelPaymentDocument(doc.id);
        toast('تم إلغاء المسودة مع بقائها في السجل', 'success');
      } else if (confirmKind === 'approve') {
        const body: Record<string, unknown> = {};
        if (approveNote.trim()) body.note = approveNote.trim();
        if (coverageReason.trim()) body.coverageReason = coverageReason.trim();
        await api.approvePaymentDocument(doc.id, body);
        toast('تم الاعتماد وتسجيله في سجل الاعتمادات', 'success');
      }
      setConfirmKind(null);
      setApproveNote('');
      setCoverageReason('');
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تنفيذ الإجراء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      const detail = await api.getPaymentDocument(doc.id);
      const expectedHash = String((detail as Record<string, unknown>).contentHash || '');
      const amountNum = Number(editAmount);
      if (!Number.isFinite(amountNum) || amountNum <= 0) {
        toast('أدخل مبلغًا صحيحًا أكبر من صفر', 'warning');
        return;
      }
      const lineCode =
        editAccountCode ||
        accounts.find((a) => a.id === (detail.lines || [])[0]?.accountId)?.code ||
        '';
      if (!lineCode) {
        toast('اختر الحساب المحاسبي للبند', 'warning');
        return;
      }
      await api.editPaymentDocument(doc.id, {
        expectedHash,
        reference: editRef.trim(),
        dueOn: editDueOn,
        vatAmount: 0,
        validTaxInvoice: false,
        lines: [
          {
            accountCode: lineCode,
            amount: amountNum,
            costCentreId: (detail.lines || [])[0]?.costCentreId || NONE_COST_CENTRE_ID,
          },
        ],
      });
      toast('تم حفظ تعديل المسودة', 'success');
      setEditOpen(false);
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر حفظ التعديل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runReserve = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      const amountNum = Number(reserveAmount);
      if (!Number.isFinite(amountNum) || amountNum <= 0) {
        toast('أدخل مبلغ صرف صالحًا', 'warning');
        return;
      }
      const voucher = await api.createPaymentRequest(doc.id, {
        amount: amountNum,
        paidOn: reservePaidOn,
        bankAccountCode: reserveBankCode,
        reference: reserveRef.trim() || String(doc.reference || doc.docNo || doc.id.slice(0, 8)),
        idempotencyKey: reserveKey || newIdempotencyKey(`pv-${doc.id.slice(0, 8)}`),
      });
      setActiveVoucher(voucher);
      onVoucherCreated?.(voucher);
      toast(`تم حجز طلب الصرف ${voucher.paymentNo || ''}`, 'success');
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء طلب الصرف', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runDualApproveVoucher = async () => {
    if (!activeVoucher?.approvalRequestId) return;
    setBusy(true);
    try {
      await api.decideApproval(activeVoucher.approvalRequestId, {
        decision: 'approved',
        note: 'اعتماد ثنائي لسند الصرف',
      });
      const refreshed = await api.getPaymentVoucher(activeVoucher.id);
      setActiveVoucher(refreshed);
      toast('تم الاعتماد الثنائي للسند', 'success');
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر الاعتماد الثنائي', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runPayVoucher = async () => {
    if (!activeVoucher) return;
    setBusy(true);
    try {
      const paid = isCardAdvance
        ? await api.issueMadaCard({ paymentId: activeVoucher.id })
        : await api.payPaymentVoucher(activeVoucher.id);
      setActiveVoucher(paid);
      toast(`تم إصدار سند الصرف ${paid.paymentNo || ''} وترحيل القيد`, 'success');
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر صرف السند وترحيله', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runCancelVoucher = async () => {
    if (!activeVoucher) return;
    setBusy(true);
    try {
      const cancelled = await api.cancelPaymentVoucher(activeVoucher.id);
      setActiveVoucher(cancelled);
      toast('تم إلغاء السند وتحرير الحجز', 'success');
      await onUpdated();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إلغاء السند', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const canEdit = hasAllowedAction(doc, 'edit_draft') && isEditableType;
  const canSubmit = hasAllowedAction(doc, 'submit');
  const canCancel = hasAllowedAction(doc, 'cancel_draft');
  const canApprove = hasAllowedAction(doc, 'approve');
  const canReserve = hasAllowedAction(doc, 'create_payment_request');

  if (!canEdit && !canSubmit && !canCancel && !canApprove && !canReserve) {
    return <span className="text-[11px] text-[var(--muted)]">{documentStatusLabel(doc)}</span>;
  }

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {canEdit && (
        <button
          type="button"
          disabled={busy}
          onClick={openEdit}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[11px] font-semibold text-[var(--ink)] hover:bg-[var(--raised)] cursor-pointer"
        >
          <Pencil size={12} aria-hidden="true" />
          <span>تعديل</span>
        </button>
      )}
      {canSubmit && (
        <button
          type="button"
          disabled={busy}
          onClick={() => setConfirmKind('submit')}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-[11px] font-bold hover:opacity-95 cursor-pointer"
        >
          <Send size={12} aria-hidden="true" />
          <span>إرسال للاعتماد</span>
        </button>
      )}
      {canApprove && (
        <button
          type="button"
          disabled={busy}
          onClick={() => setConfirmKind('approve')}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-[var(--money-pos)] text-white text-[11px] font-bold hover:opacity-95 cursor-pointer"
        >
          <CheckCircle2 size={12} aria-hidden="true" />
          <span>اعتماد</span>
        </button>
      )}
      {canReserve && (
        <button
          type="button"
          disabled={busy}
          onClick={openReserve}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-[11px] font-bold hover:opacity-95 cursor-pointer"
        >
          <Banknote size={12} aria-hidden="true" />
          <span>إصدار سند صرف</span>
        </button>
      )}
      {canCancel && (
        <button
          type="button"
          disabled={busy}
          onClick={() => setConfirmKind('cancel')}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--danger)]/40 text-[var(--danger)] text-[11px] font-semibold hover:bg-[var(--danger)]/10 cursor-pointer"
        >
          <XCircle size={12} aria-hidden="true" />
          <span>إلغاء المسودة</span>
        </button>
      )}

      {/* Confirm Submit / Cancel / Approve */}
      <Dialog
        open={confirmKind !== null}
        onClose={() => !busy && setConfirmKind(null)}
        title={
          confirmKind === 'submit'
            ? 'إرسال المستند للاعتماد المالي'
            : confirmKind === 'cancel'
              ? 'إلغاء المسودة'
              : 'تأكيد اعتماد المستند'
        }
        size="sm"
        actions={
          <>
            <button
              type="button"
              disabled={busy}
              onClick={() => setConfirmKind(null)}
              className="px-3 py-1.5 rounded-lg border border-[var(--line)] text-xs font-semibold text-[var(--ink)]"
            >
              تراجع
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => void runTransition()}
              className={`px-3.5 py-1.5 rounded-lg text-xs font-bold text-white ${
                confirmKind === 'cancel' ? 'bg-[var(--danger)]' : 'bg-[var(--accent)]'
              }`}
            >
              {confirmKind === 'submit'
                ? 'تأكيد الإرسال'
                : confirmKind === 'cancel'
                  ? 'تأكيد الإلغاء'
                  : 'تأكيد الاعتماد'}
            </button>
          </>
        }
      >
        <div className="space-y-3 text-xs text-[var(--ink)]">
          {confirmKind === 'submit' && (
            <p>إرسال المستند رقم <bdi className="font-bold">{doc.docNo || doc.reference}</bdi> بمبلغ <span className="num font-bold">{formatSar(doc.amount)}</span> للاعتماد المالي؟</p>
          )}
          {confirmKind === 'cancel' && (
            <p>سيتم إلغاء المسودة مع بقائها في السجل (لا تُحذف). هل تريد المتابعة؟</p>
          )}
          {confirmKind === 'approve' && (
            <>
              <p>
                تأكيد اعتماد المستند بمبلغ <span className="num font-bold">{formatSar(doc.amount)}</span>؟ سيُضاف تلقائيًا إلى قائمة طلبات الدفع.
              </p>
              <label className="block space-y-1">
                <span className="text-[var(--muted)]">ملاحظة الاعتماد (اختياري)</span>
                <input
                  className={inputClass}
                  value={approveNote}
                  onChange={(e) => setApproveNote(e.target.value)}
                  placeholder="ملاحظة للمراجع أو الخزينة"
                />
              </label>
              {isAdvanceType && (
                <label className="block space-y-1">
                  <span className="text-[var(--muted)]">سبب التغطية (مطلوب إذا لم يغطِّ الصافي المتوقع السلفة)</span>
                  <textarea
                    rows={2}
                    className={inputClass}
                    value={coverageReason}
                    onChange={(e) => setCoverageReason(e.target.value)}
                    placeholder="اكتب مبرر الاعتماد عند عدم التغطية"
                  />
                </label>
              )}
            </>
          )}
        </div>
      </Dialog>

      {/* Edit Draft Dialog */}
      <Dialog
        open={editOpen}
        onClose={() => !busy && setEditOpen(false)}
        title="تعديل المسودة"
        description={`المستند ${doc.docNo || doc.id.slice(0, 8)}`}
        size="md"
      >
        <form onSubmit={runEdit} className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="block space-y-1 text-xs">
              <span className="text-[var(--muted)]">المرجع / البيان</span>
              <input
                className={inputClass}
                value={editRef}
                onChange={(e) => setEditRef(e.target.value)}
                required
              />
            </label>
            <label className="block space-y-1 text-xs">
              <span className="text-[var(--muted)]">تاريخ الاستحقاق</span>
              <input
                type="date"
                className={inputClass}
                value={editDueOn}
                onChange={(e) => setEditDueOn(e.target.value)}
                required
              />
            </label>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1 text-xs">
              <span className="block text-[var(--muted)]">الحساب المحاسبي</span>
              <SearchableSelect
                value={editAccountCode}
                onChange={setEditAccountCode}
                options={allAccountOptions}
                placeholder="اختر الحساب..."
              />
            </div>
            <label className="block space-y-1 text-xs">
              <span className="text-[var(--muted)]">المبلغ</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={editAmount}
                onChange={(e) => setEditAmount(e.target.value)}
                required
              />
            </label>
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={() => setEditOpen(false)}
              className="px-3 py-1.5 rounded-lg border border-[var(--line)] text-xs font-semibold"
            >
              إغلاق
            </button>
            <button
              type="submit"
              disabled={busy}
              className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
            >
              حفظ المسودة
            </button>
          </div>
        </form>
      </Dialog>

      {/* Reserve + Dual Approve + Pay Voucher Dialog */}
      <Dialog
        open={reserveOpen}
        onClose={() => !busy && setReserveOpen(false)}
        title="إصدار سند صرف وسداد المستند"
        description={`المستند ${doc.docNo || doc.reference || ''} — المتاح: ${formatSar(doc.availableBalance ?? doc.amount)}`}
        size="md"
      >
        {!activeVoucher ? (
          <form onSubmit={runReserve} className="space-y-3">
            {customPayConfirmText && (
              <div className="rounded-xl border border-[var(--warning)]/40 bg-[var(--warning)]/10 p-3 text-xs text-[var(--ink)]">
                {customPayConfirmText(doc)}
              </div>
            )}
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1 text-xs">
                <span className="block text-[var(--muted)]">حساب البنك / الخزينة</span>
                <SearchableSelect
                  value={reserveBankCode}
                  onChange={setReserveBankCode}
                  options={bankOptions}
                  placeholder="اختر حساب الصرف..."
                />
              </div>
              <label className="block space-y-1 text-xs">
                <span className="text-[var(--muted)]">المبلغ المطلوب صرفه</span>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  className={inputClass}
                  value={reserveAmount}
                  onChange={(e) => setReserveAmount(e.target.value)}
                  required
                />
              </label>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="block space-y-1 text-xs">
                <span className="text-[var(--muted)]">تاريخ الصرف</span>
                <input
                  type="date"
                  className={inputClass}
                  value={reservePaidOn}
                  onChange={(e) => setReservePaidOn(e.target.value)}
                  required
                />
              </label>
              <label className="block space-y-1 text-xs">
                <span className="text-[var(--muted)]">مرجع السند / التحويل</span>
                <input
                  className={inputClass}
                  value={reserveRef}
                  onChange={(e) => setReserveRef(e.target.value)}
                  required
                />
              </label>
            </div>
            <p className="text-[11px] text-[var(--muted)]">
              سيُحجز المبلغ ويُفتح طلب اعتماد ثنائي مستقل قبل ترحيل قيد الصرف.
            </p>
            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setReserveOpen(false)}
                className="px-3 py-1.5 rounded-lg border border-[var(--line)] text-xs font-semibold"
              >
                إلغاء
              </button>
              <button
                type="submit"
                disabled={busy}
                className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
              >
                حجز طلب الصرف
              </button>
            </div>
          </form>
        ) : (
          <div className="space-y-4 text-xs">
            <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3 space-y-1.5">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">رقم السند</span>
                <bdi className="font-bold text-[var(--ink)]">{activeVoucher.paymentNo || activeVoucher.id}</bdi>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">المبلغ</span>
                <span className="num font-bold">{formatSar(activeVoucher.amount)}</span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">الحالة</span>
                <span className="font-semibold">
                  {VOUCHER_STATUS_LABELS[Number(activeVoucher.status)] || String(activeVoucher.status)}
                </span>
              </div>
              {activeVoucher.journalId && (
                <div className="flex items-center justify-between">
                  <span className="text-[var(--muted)]">رقم القيد المرحّل</span>
                  <bdi className="font-mono text-[11px]">{activeVoucher.journalId}</bdi>
                </div>
              )}
            </div>

            <div className="flex flex-wrap items-center justify-end gap-2">
              {canDecideApproval && activeVoucher.status === 1 && activeVoucher.approvalRequestId && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void runDualApproveVoucher()}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-[var(--accent)] text-[var(--accent)] font-bold"
                >
                  <ShieldCheck size={14} aria-hidden="true" />
                  <span>اعتماد ثنائي للسند</span>
                </button>
              )}
              {hasAllowedAction(activeVoucher, 'pay_voucher') && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void runPayVoucher()}
                  className="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-lg bg-[var(--money-pos)] text-white font-bold"
                >
                  <Banknote size={14} aria-hidden="true" />
                  <span>{isCardAdvance ? 'صرف بطاقة مدا وترحيل القيد' : 'تأكيد الصرف وترحيل القيد'}</span>
                </button>
              )}
              {hasAllowedAction(activeVoucher, 'cancel_draft') && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void runCancelVoucher()}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-[var(--danger)]/40 text-[var(--danger)] font-semibold"
                >
                  <Ban size={14} aria-hidden="true" />
                  <span>إلغاء السند</span>
                </button>
              )}
              <button
                type="button"
                onClick={() => setReserveOpen(false)}
                className="px-3 py-1.5 rounded-lg border border-[var(--line)] font-semibold"
              >
                إغلاق
              </button>
            </div>
          </div>
        )}
      </Dialog>
    </div>
  );
}

/** Inline "+ إضافة حساب جديد" modal backed by POST /api/finance/coa. */
export function AddCoaAccountDialog({
  open,
  onClose,
  defaultParentCode = '5210',
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  defaultParentCode?: string;
  onCreated: (code: string) => Promise<void> | void;
}) {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  const [parentCode, setParentCode] = useState(defaultParentCode);
  const [code, setCode] = useState('');
  const [nameAr, setNameAr] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.createFinanceCoa({
        parentCode: parentCode.trim(),
        code: code.trim(),
        nameAr: nameAr.trim(),
        isPostable: true,
      });
      toast(`تم إنشاء الحساب ${code.trim()}`, 'success');
      await onCreated(code.trim());
      setCode('');
      setNameAr('');
      onClose();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء الحساب', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} title="إضافة حساب جديد في دليل الحسابات" size="sm">
      <form onSubmit={submit} className="space-y-3 text-xs">
        <label className="block space-y-1">
          <span className="text-[var(--muted)]">رمز الحساب الأب</span>
          <input
            className={inputClass}
            value={parentCode}
            onChange={(e) => setParentCode(e.target.value)}
            required
          />
        </label>
        <label className="block space-y-1">
          <span className="text-[var(--muted)]">رمز الحساب الجديد</span>
          <input
            className={inputClass}
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="مثال: 521199"
            required
          />
        </label>
        <label className="block space-y-1">
          <span className="text-[var(--muted)]">اسم الحساب بالعربية</span>
          <input
            className={inputClass}
            value={nameAr}
            onChange={(e) => setNameAr(e.target.value)}
            placeholder="اسم بند الصرف أو المحفظة"
            required
          />
        </label>
        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            onClick={onClose}
            className="px-3 py-1.5 rounded-lg border border-[var(--line)] font-semibold"
          >
            تراجع
          </button>
          <button
            type="submit"
            disabled={busy}
            className="inline-flex items-center gap-1 px-3.5 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold"
          >
            <Plus size={13} aria-hidden="true" />
            <span>حفظ الحساب</span>
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/** Inline "+ إضافة مورد جديد" modal backed by POST /api/payments/suppliers. */
export function AddSupplierDialog({
  open,
  onClose,
  companyOptions,
  defaultCompanyId,
  onCreated,
}: {
  open: boolean;
  onClose: () => void;
  companyOptions: Array<{ value: string; label: string }>;
  defaultCompanyId?: string;
  onCreated: (id: string) => Promise<void> | void;
}) {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  const [companyId, setCompanyId] = useState(defaultCompanyId || '');
  const [nameAr, setNameAr] = useState('');
  const [vatNo, setVatNo] = useState('');

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const targetCompany = companyId || defaultCompanyId || companyOptions[0]?.value || '';
    if (!targetCompany) {
      toast('اختر الشركة أولًا', 'warning');
      return;
    }
    setBusy(true);
    try {
      const created = await api.createPaymentSupplier({
        companyId: targetCompany,
        nameAr: nameAr.trim(),
        vatNo: vatNo.trim(),
      });
      toast('تم تسجيل المورد الجديد', 'success');
      await onCreated(String(created.id || ''));
      setNameAr('');
      setVatNo('');
      onClose();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل المورد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={() => !busy && onClose()} title="إضافة مورد جديد" size="sm">
      <form onSubmit={submit} className="space-y-3 text-xs">
        <div className="space-y-1">
          <span className="block text-[var(--muted)]">الشركة</span>
          <SearchableSelect
            value={companyId || defaultCompanyId || ''}
            onChange={setCompanyId}
            options={companyOptions}
            placeholder="اختر الشركة..."
          />
        </div>
        <label className="block space-y-1">
          <span className="text-[var(--muted)]">اسم المورد</span>
          <input
            className={inputClass}
            value={nameAr}
            onChange={(e) => setNameAr(e.target.value)}
            required
          />
        </label>
        <label className="block space-y-1">
          <span className="text-[var(--muted)]">الرقم الضريبي (اختياري)</span>
          <input
            className={inputClass}
            value={vatNo}
            onChange={(e) => setVatNo(e.target.value)}
            placeholder="300000000000003"
          />
        </label>
        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            onClick={onClose}
            className="px-3 py-1.5 rounded-lg border border-[var(--line)] font-semibold"
          >
            تراجع
          </button>
          <button
            type="submit"
            disabled={busy}
            className="inline-flex items-center gap-1 px-3.5 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold"
          >
            <Plus size={13} aria-hidden="true" />
            <span>حفظ المورد</span>
          </button>
        </div>
      </form>
    </Dialog>
  );
}
