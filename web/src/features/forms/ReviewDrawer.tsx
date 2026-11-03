'use client';

import React, { useState, useEffect } from 'react';
import { Drawer } from '../../components/ui/Dialog';
import { api, ApiError } from '../../lib/api';
import type { Submission } from '../../lib/types';
import { Check, X, Copy, ShieldCheck, AlertCircle, Clock, CheckSquare } from 'lucide-react';
import { useToast } from '../../components/ui/Toast';

export interface ReviewDrawerProps {
  submission: Submission | null;
  onClose: () => void;
  onSuccess: () => void;
}

export function ReviewDrawer({ submission, onClose, onSuccess }: ReviewDrawerProps) {
  const { toast } = useToast();
  const [identityReviewed, setIdentityReviewed] = useState(false);
  const [ibanOwnershipReviewed, setIbanOwnershipReviewed] = useState(false);
  const [reviewNote, setReviewNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (submission) {
      setIdentityReviewed(false);
      setIbanOwnershipReviewed(false);
      setReviewNote(submission.reviewNote || '');
    }
  }, [submission]);

  if (!submission) return null;

  const hasIban = !!submission.iban;
  const isPending = submission.status === 'pending';

  const handleReview = async (status: 'approved' | 'rejected') => {
    if (status === 'approved') {
      if (!identityReviewed) {
        toast('يرجى تأكيد مراجعة هوية المندوب والاسم خارج النموذج أولاً.', 'warning');
        return;
      }
      if (hasIban && !ibanOwnershipReviewed) {
        toast('يرجى تأكيد مراجعة ملكية الآيبان؛ فحص الصيغة لا يثبت صاحب الحساب.', 'warning');
        return;
      }
    }

    setSubmitting(true);
    try {
      await api.reviewSubmission(submission.id, {
        status,
        note: reviewNote.trim() || undefined,
        version: submission.version || 1,
        identityReviewed: status === 'approved' ? true : undefined,
        ibanOwnershipReviewed: status === 'approved' && hasIban ? true : undefined,
      });

      toast(status === 'approved' ? 'تم اعتماد الرد وتوثيق المراجعة بنجاح' : 'تم رفض الرد', 'success');
      onSuccess();
      onClose();
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        toast('تم تعديل الرد من قبل مستخدم آخر؛ أعد فتح المراجعة.', 'danger');
        onSuccess();
        onClose();
      } else {
        toast(err?.message || 'تعذر إتمام عملية المراجعة', 'danger');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const copyIban = () => {
    if (submission.iban && navigator.clipboard) {
      navigator.clipboard.writeText(submission.iban);
      toast('تم نسخ الآيبان إلى الحافظة', 'success');
    }
  };

  return (
    <Drawer
      open={!!submission}
      onClose={onClose}
      title={hasIban ? 'مراجعة وتدقيق بيانات الآيبان' : 'مراجعة اختيار نظام العمل'}
      subtitle={`المندوب: ${submission.name} — إقامة: ${submission.iqama}`}
      footer={
        isPending ? (
          <div className="flex items-center justify-between gap-3">
            <button
              type="button"
              disabled={submitting}
              onClick={() => handleReview('rejected')}
              className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-xs font-bold rounded-xl border border-[var(--danger)] text-[var(--danger)] hover:bg-[var(--danger)]/10 transition disabled:opacity-50"
            >
              <X size={15} />
              <span>رفض الرد</span>
            </button>
            <button
              type="button"
              disabled={submitting}
              onClick={() => handleReview('approved')}
              className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-95 active:scale-95 transition shadow-xs disabled:opacity-50"
            >
              <Check size={15} />
              <span>اعتماد رسمي</span>
            </button>
          </div>
        ) : (
          <div className="text-center text-xs text-[var(--muted)]">
            حالة الرد: <strong className="text-[var(--ink)]">{submission.status === 'approved' ? 'معتمد' : 'مرفوض'}</strong>
          </div>
        )
      }
    >
      <div className="space-y-5 text-xs sm:text-sm">
        {/* Rider Identity card */}
        <div className="p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)] space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-[var(--muted)]">اسم المندوب:</span>
            <strong className="text-[var(--ink)]">{submission.name}</strong>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-[var(--muted)]">رقم الإقامة:</span>
            <strong className="num font-mono text-[var(--ink)]">{submission.iqama}</strong>
          </div>
          {submission.phone && (
            <div className="flex items-center justify-between">
              <span className="text-[var(--muted)]">رقم الجوال:</span>
              <strong className="num text-[var(--ink)]">{submission.phone}</strong>
            </div>
          )}
          {submission.city && (
            <div className="flex items-center justify-between">
              <span className="text-[var(--muted)]">المدينة:</span>
              <strong className="text-[var(--ink)]">{submission.city}</strong>
            </div>
          )}
          {submission.campaignTitle && (
            <div className="flex items-center justify-between text-xs pt-1 border-t border-[var(--line)]">
              <span className="text-[var(--muted)]">الحملة:</span>
              <span className="text-[var(--ink)] font-medium">{submission.campaignTitle}</span>
            </div>
          )}
        </div>

        {/* Full Unmasked IBAN (Inspection only in review) */}
        {hasIban && (
          <div className="p-4 rounded-xl border border-[var(--accent)]/40 bg-[var(--accent)]/5 space-y-2.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-[var(--accent)] flex items-center gap-1.5">
                <ShieldCheck size={16} />
                الآيبان البنكي (معروض بالكامل للمراجعة فقط):
              </span>
              <button
                type="button"
                onClick={copyIban}
                className="inline-flex items-center gap-1 text-[11px] font-semibold text-[var(--accent)] hover:underline"
              >
                <Copy size={12} />
                <span>نسخ</span>
              </button>
            </div>
            <div className="num font-mono font-bold text-sm tracking-wider text-[var(--ink)] select-all bg-[var(--surface)] p-2.5 rounded-lg border border-[var(--line)] text-center shadow-2xs">
              {submission.iban}
            </div>
            <div className="flex items-center justify-between text-xs text-[var(--muted)] pt-0.5">
              <span>البنك المستخرج:</span>
              <strong className="text-[var(--ink)]">{submission.bank || 'مصرف الراجحي'}</strong>
            </div>
          </div>
        )}

        {/* Work preference info */}
        {submission.workSystem && (
          <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] space-y-1.5">
            <span className="text-xs font-bold text-[var(--muted)]">نظام العمل المختار:</span>
            <div className="text-sm font-bold text-[var(--accent)]">
              {submission.workSystem === 'monthly' ? 'راتب شهري ثابت' : 'محاسبة بالطلب (عمولة)'}
            </div>
          </div>
        )}

        {/* Audit Review Checkboxes Form */}
        {isPending ? (
          <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] space-y-3.5">
            <h4 className="text-xs font-bold text-[var(--ink)] flex items-center gap-1.5">
              <CheckSquare size={14} className="text-[var(--accent)]" />
              تأكيدات المراجعة الإلزامية قبل الاعتماد
            </h4>

            {/* Checkbox 1: Identity */}
            <label className="flex items-start gap-2.5 cursor-pointer">
              <input
                type="checkbox"
                checked={identityReviewed}
                onChange={(e) => setIdentityReviewed(e.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)] cursor-pointer"
              />
              <span className="text-xs text-[var(--ink)] leading-relaxed">
                راجعت مطابقة الاسم ورقم الإقامة خارج النموذج وتأكدت من هوية المندوب.
              </span>
            </label>

            {/* Checkbox 2: IBAN Ownership */}
            {hasIban && (
              <label className="flex items-start gap-2.5 cursor-pointer">
                <input
                  type="checkbox"
                  checked={ibanOwnershipReviewed}
                  onChange={(e) => setIbanOwnershipReviewed(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)] cursor-pointer"
                />
                <span className="text-xs text-[var(--ink)] leading-relaxed">
                  راجعت أن الآيبان باسم المندوب نفسه؛ فحص الصيغة لا يثبت صاحب الحساب.
                </span>
              </label>
            )}

            {/* Review Note */}
            <div>
              <label className="block text-xs font-bold text-[var(--muted)] mb-1">
                ملاحظة المراجعة (اختيارية):
              </label>
              <textarea
                rows={2}
                value={reviewNote}
                onChange={(e) => setReviewNote(e.target.value)}
                placeholder="أضف أي ملاحظات تود توثيقها في سجل التدقيق..."
                className="w-full p-2.5 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
              />
            </div>

            {/* Notice matching old UI */}
            <div className="p-2.5 rounded-lg bg-[var(--raised)] border border-[var(--line)] text-[11px] text-[var(--muted)] leading-relaxed">
              {hasIban
                ? 'الاعتماد يسجل مراجعة الرد فقط، ولا يغيّر آيبان المندوب في ملف البنك. المقارنة بالآيبانات المعتمدة السابقة مرحلة لاحقة.'
                : 'اعتماد الاختيار لا يعيّن سياسة حساب ولا يعيد حساب راتب مقفل.'}
            </div>
          </div>
        ) : (
          <div className="p-3.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs">
            <span className="text-[var(--muted)]">ملاحظة المراجعة المسجلة: </span>
            <span className="text-[var(--ink)] font-medium">{submission.reviewNote || '—'}</span>
          </div>
        )}

        {/* History Box */}
        {submission.history && submission.history.length > 0 && (
          <div>
            <h4 className="text-xs font-bold text-[var(--ink)] mb-2.5 flex items-center gap-1.5">
              <Clock size={14} className="text-[var(--muted)]" />
              سجل التعديلات السابقة ({submission.history.length})
            </h4>
            <div className="space-y-2 border-s-2 border-[var(--line)] ms-2 ps-3.5">
              {submission.history.map((h, i) => (
                <div key={i} className="text-xs space-y-1 bg-[var(--raised)]/50 p-2 rounded-lg">
                  <div className="flex items-center justify-between text-[11px] text-[var(--muted)]">
                    <span>
                      نسخة {h.version} ({h.status === 'approved' ? 'معتمد' : h.status === 'rejected' ? 'مرفوض' : 'قيد المراجعة'})
                    </span>
                    <span className="num">{new Date(h.submittedAt).toLocaleDateString('ar-SA')}</span>
                  </div>
                  <div className="font-semibold text-[var(--ink)]">
                    {h.iban || h.workSystem}
                  </div>
                  {h.reviewNote && <p className="text-[11px] text-[var(--muted)]">{h.reviewNote}</p>}
                  {h.reviewedBy && (
                    <div className="text-[10px] text-[var(--muted)]">المراجع: {h.reviewedBy}</div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Drawer>
  );
}
