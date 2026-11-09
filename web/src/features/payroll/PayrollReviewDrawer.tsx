'use client';

import React, { useEffect, useState } from 'react';
import {
  CheckCircle2,
  AlertTriangle,
  Clock,
  User,
  Plus,
  Edit2,
  History,
  Calculator,
  ShieldAlert,
  Loader2,
  DollarSign,
  ArrowRight,
} from 'lucide-react';
import { Drawer } from '../../components/ui/Drawer';
import { api, ApiError } from '../../lib/api';
import type { PayReviewRow, PayReviewItem, PayReviewEdit } from '../../lib/types';
import { useToast } from '../../components/ui/Toast';
import { SearchableSelect } from '../../components/ui/SearchableSelect';

export interface PayrollReviewDrawerProps {
  open: boolean;
  onClose: () => void;
  row: PayReviewRow | null;
  cycleId: string;
  onRowUpdated: (updated: PayReviewRow) => void;
}

export function PayrollReviewDrawer({
  open,
  onClose,
  row,
  cycleId,
  onRowUpdated,
}: PayrollReviewDrawerProps) {
  const { toast } = useToast();

  const [submitting, setSubmitting] = useState(false);
  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [editAmount, setEditAmount] = useState<string>('');
  const [editReason, setEditReason] = useState<string>('');

  const [adjustOpen, setAdjustOpen] = useState(false);
  const [adjustAmount, setAdjustAmount] = useState<string>('');
  const [adjustReason, setAdjustReason] = useState<string>('');

  const [itemType, setItemType] = useState('');
  const [itemTypes, setItemTypes] = useState<Array<{ value: string; label: string }>>([]);
  const [decisionReason, setDecisionReason] = useState('');
  useEffect(() => {
    if (!open) return;
    api.getConfigListGroup('pay_adjustment_types')
      .then((g) => setItemTypes(g.items.filter((i) => i.active && i.status !== 'pending').map((i) => ({ value: i.code, label: `${i.nameAr} (${i.values?.account ?? ''})` }))))
      .catch(() => setItemTypes([]));
  }, [open]);

  if (!row) return null;

  const maskIqama = (iqama: string) => {
    if (!iqama || iqama.length < 6) return iqama;
    return `${iqama.slice(0, 4)}****${iqama.slice(-2)}`;
  };

  const handleApprove = async () => {
    setSubmitting(true);
    try {
      if (row.net < 0 && decisionReason.trim().length < 3) {
        toast('الصافي سالب: اكتب سبب القرار. سيتحول العجز إلى مديونية عند الاعتماد النهائي.', 'warning');
        return;
      }
      const updated = await api.reviewPayCycleRider(cycleId, row.rider, row.net < 0 ? { action: 'approve', reason: decisionReason.trim() } : { action: 'approve' });
      toast('تم اعتماد بيانات واستحقاق المندوب بنجاح.', 'success');
      onRowUpdated(updated);
    } catch (err: any) {
      toast(err.message || 'تعذر اعتماد المندوب.', 'danger');
    } finally {
      setSubmitting(false);
    }
  };

  const handleSaveEdit = async (item: PayReviewItem) => {
    const val = parseFloat(editAmount);
    if (isNaN(val) || val < 0 || (item.max !== null && val > item.max)) {
      toast(`المبلغ يجب أن يكون بين 0 و ${item.max ?? 0}.`, 'danger');
      return;
    }
    if (!editReason || editReason.trim().length < 3) {
      toast('يرجى كتابة سبب واضح للتعديل (3 أحرف على الأقل).', 'danger');
      return;
    }

    setSubmitting(true);
    try {
      const updated = await api.reviewPayCycleRider(cycleId, row.rider, {
        action: 'edit',
        ...(item.key.startsWith('debt:') ? {} : { itemType }),
        key: item.key,
        amount: val,
        reason: editReason.trim(),
      });
      toast('تم تعديل قسط المديونية وتحديث المسير.', 'success');
      setEditingKey(null);
      setEditAmount('');
      setEditReason('');
      onRowUpdated(updated);
    } catch (err: any) {
      toast(err.message || 'تعذر حفظ التعديل.', 'danger');
    } finally {
      setSubmitting(false);
    }
  };

  const handleAddAdjustment = async () => {
    const val = parseFloat(adjustAmount);
    if (isNaN(val) || val === 0 || Math.abs(val) > 100000) {
      toast('مبلغ التعديل غير صالح (يجب أن لا يتجاوز 100,000 ر.س).', 'danger');
      return;
    }
    if (!adjustReason || adjustReason.trim().length < 3) {
      toast('يرجى كتابة سبب واضح لتعديل الراتب.', 'danger');
      return;
    }

    setSubmitting(true);
    try {
      const updated = await api.reviewPayCycleRider(cycleId, row.rider, {
        action: 'adjust',
        itemType,
        amount: val,
        reason: adjustReason.trim(),
      });
      toast('تمت إضافة تسوية الراتب للمندوب بنجاح.', 'success');
      setAdjustOpen(false);
      setAdjustAmount('');
      setAdjustReason('');
      onRowUpdated(updated);
    } catch (err: any) {
      toast(err.message || 'تعذر إضافة تعديل الراتب.', 'danger');
    } finally {
      setSubmitting(false);
    }
  };

  const phase1Items = (row.items || []).filter((i) => i.phase === 1);
  const phase2Items = (row.items || []).filter((i) => i.phase === 2);
  const phase3Items = (row.items || []).filter((i) => i.phase === 3);

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={row.name || 'مراجعة استحقاق المندوب'}
      subtitle={`رقم الهوية / الإقامة: ${maskIqama(row.rider)}`}
      width="max-w-md sm:max-w-2xl"
      footer={
        <div className="flex items-center justify-between w-full">
          <div className="text-xs">
            <span className="text-[var(--muted)]">الصافي النهائي: </span>
            <span
              className={`font-mono font-bold num text-sm ${
                row.net >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--danger)]'
              }`}
            >
              {row.net.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
            </span>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-3.5 py-1.5 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)]"
            >
              إغلاق
            </button>

            {row.review !== 'approved' && (<>
              {row.net < 0 && (
                <input
                  id="review-negative-reason"
                  value={decisionReason}
                  onChange={(e) => setDecisionReason(e.target.value)}
                  placeholder="سبب اعتماد الصف السالب (يتحول مديونية عند الاعتماد النهائي)"
                  className="w-full rounded-lg border border-[var(--line)] bg-[var(--surface)] p-2 text-xs text-[var(--ink)]"
                />
              )}
              <button
                type="button"
                disabled={submitting || !(row.allowedActions ?? []).includes('approve')}
                onClick={handleApprove}
                className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed shadow-xs"
              >
                {submitting ? <Loader2 size={14} className="animate-spin" /> : <CheckCircle2 size={14} />}
                <span>اعتماد المندوب</span>
              </button>
            </>)}
          </div>
        </div>
      }
    >
      <div className="space-y-6">
        {/* Status Pills */}
        <div className="flex flex-wrap items-center gap-2">
          <span
            className={`text-xs font-bold px-2.5 py-1 rounded-full flex items-center gap-1 ${
              row.status === 'ready'
                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
            }`}
          >
            {row.status === 'ready' ? <CheckCircle2 size={13} /> : <AlertTriangle size={13} />}
            <span>{row.status === 'ready' ? 'جاهز للاعتماد' : 'يحتاج قرار (موانع استحقاق)'}</span>
          </span>

          <span
            className={`text-xs font-bold px-2.5 py-1 rounded-full ${
              row.review === 'approved'
                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                : 'bg-[var(--warning)]/15 text-[var(--warning)]'
            }`}
          >
            {row.review === 'approved'
              ? `معتمد بواسطة @${row.reviewedBy || 'reviewer'}`
              : 'بانتظار المراجعة والاعتماد'}
          </span>
        </div>

        {/* Financial Flow Cards (BR §29: Phases Flow) */}
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5 p-3 rounded-2xl border border-[var(--line)] bg-[var(--raised)]/40 text-xs">
          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">الإجمالي (Gross)</span>
            <span className="num font-mono font-bold text-[var(--ink)]">
              {row.gross.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">العمولة (Commission)</span>
            <span className="num font-mono font-bold text-[var(--warning)]">
              {row.commission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">بعد العمولة</span>
            <span className="num font-mono font-bold text-[var(--ink)]">
              {row.afterCommission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">المرحلة 1 (تشغيلي)</span>
            <span className="num font-mono font-bold text-[var(--danger)]">
              {row.phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--accent)]/30 bg-[var(--accent)]/5 col-span-2 sm:col-span-1">
            <span className="text-[10px] font-bold text-[var(--accent)] block">المستحق الأولي</span>
            <span className="num font-mono font-black text-[var(--accent)]">
              {row.preliminaryDue.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">المرحلة 2 (مديونيات)</span>
            <span className="num font-mono font-bold text-[var(--danger)]">
              {row.phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
            <span className="text-[10px] text-[var(--muted)] block">المرحلة 3 (تسويات)</span>
            <span className="num font-mono font-bold text-[var(--ink)]">
              {row.phase3.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>

          <div className="p-2 rounded-xl bg-[var(--surface)] border border-[var(--money-pos)]/40 bg-[var(--money-pos)]/5 col-span-2 sm:col-span-1">
            <span className="text-[10px] font-bold text-[var(--money-pos)] block">الصافي النهائي</span>
            <span
              className={`num font-mono font-black ${
                row.net >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--danger)]'
              }`}
            >
              {row.net.toLocaleString('en-US', { minimumFractionDigits: 2 })}
            </span>
          </div>
        </div>

        {/* Phase 1: Operational Deductions */}
        <div className="space-y-2">
          <div className="flex items-center justify-between border-b border-[var(--line)] pb-1.5">
            <h4 className="text-xs font-bold text-[var(--ink)]">المرحلة 1: الخصومات التشغيلية وبدل المركبة</h4>
            <span className="num font-mono text-xs text-[var(--muted)]">
              إجمالي: {row.phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
            </span>
          </div>

          {phase1Items.length === 0 ? (
            <p className="text-[11px] text-[var(--muted)] py-1">لا توجد خصومات تشغيلية مسجلة لهذه الدورة.</p>
          ) : (
            <div className="divide-y divide-[var(--line)] rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs">
              {phase1Items.map((item) => (
                <div key={item.key} className="flex items-center justify-between p-2.5">
                  <div>
                    <span className="font-semibold text-[var(--ink)]">{item.labelAr}</span>
                    <span className="text-[10px] text-[var(--muted)] block">المصدر: {item.source}</span>
                  </div>
                  <span className="num font-mono font-bold text-[var(--danger)]">
                    {item.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Phase 2: Debt Installments (Editable) */}
        <div className="space-y-2">
          <div className="flex items-center justify-between border-b border-[var(--line)] pb-1.5">
            <div>
              <h4 className="text-xs font-bold text-[var(--ink)]">المرحلة 2: أقساط المديونيات والسلف (BR §32)</h4>
              <p className="text-[10px] text-[var(--muted)]">يمكن للمراجع تعديل القسط بين 0 والحد الأقصى مع ذكر السبب.</p>
            </div>
            <span className="num font-mono text-xs text-[var(--muted)]">
              إجمالي: {row.phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
            </span>
          </div>

          {phase2Items.length === 0 ? (
            <p className="text-[11px] text-[var(--muted)] py-1">لا توجد أقساط مديونية مستقطعة في هذه المرحلة.</p>
          ) : (
            <div className="divide-y divide-[var(--line)] rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs">
              {phase2Items.map((item) => (
                <div key={item.key} className="p-2.5 space-y-2">
                  <div className="flex items-center justify-between">
                    <div>
                      <span className="font-semibold text-[var(--ink)]">{item.labelAr}</span>
                      <span className="text-[10px] text-[var(--muted)] block">
                        الحد الأقصى المحسوب: {item.max ?? item.amount} ر.س · المصدر: {item.source}
                      </span>
                    </div>

                    <div className="flex items-center gap-2">
                      <span className="num font-mono font-bold text-[var(--danger)]">
                        {item.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                      </span>

                      {item.editable && editingKey !== item.key && (
                        <button
                          type="button"
                          onClick={() => {
                            setEditingKey(item.key);
                            setEditAmount(String(Math.abs(item.amount)));
                            setEditReason('');
                          }}
                          className="p-1 rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--accent)]"
                          title="تعديل قيمة القسط"
                        >
                          <Edit2 size={13} />
                        </button>
                      )}
                    </div>
                  </div>

                  {editingKey === item.key && (
                    <div className="rounded-lg bg-[var(--raised)]/60 p-2.5 space-y-2 border border-[var(--line)]">
                      <div className="flex items-center gap-2">
                        <label className="text-[11px] font-bold text-[var(--ink)] shrink-0">القسط الجديد:</label>
                        <input
                          type="number"
                          step="0.01"
                          min="0"
                          max={item.max ?? undefined}
                          value={editAmount}
                          onChange={(e) => setEditAmount(e.target.value)}
                          className="w-28 p-1 rounded-md border border-[var(--line)] bg-[var(--surface)] text-xs font-mono font-bold"
                        />
                        <span className="text-[10px] text-[var(--muted)]">ر.س (بين 0 و {item.max})</span>
                      </div>

                      <div className="space-y-1">
                        <label className="text-[11px] font-bold text-[var(--ink)] block">سبب التعديل (إلزامي):</label>
                        <input
                          type="text"
                          placeholder="مثلاً: تأجيل قسط السلفة مراعاة لظروف المندوب..."
                          value={editReason}
                          onChange={(e) => setEditReason(e.target.value)}
                          className="w-full p-1.5 rounded-md border border-[var(--line)] bg-[var(--surface)] text-xs"
                        />
                      </div>

                      <div className="flex items-center justify-end gap-2 pt-1">
                        <button
                          type="button"
                          onClick={() => setEditingKey(null)}
                          className="px-2.5 py-1 text-xs rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]"
                        >
                          إلغاء
                        </button>
                        {!item.key.startsWith('debt:') && (
                          <SearchableSelect ariaLabel="بند التعديل" value={itemType} onChange={setItemType} placeholder="اكتب بند التعديل..." options={itemTypes} />
                        )}
                        <button
                          type="button"
                          disabled={submitting}
                          onClick={() => handleSaveEdit(item)}
                          className="px-3 py-1 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90"
                        >
                          حفظ واعتماد التعديل
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Phase 3: Salary Adjustments (+ / -) */}
        <div className="space-y-2">
          <div className="flex items-center justify-between border-b border-[var(--line)] pb-1.5">
            <div>
              <h4 className="text-xs font-bold text-[var(--ink)]">المرحلة 3: تسويات وتعديلات الراتب</h4>
              <p className="text-[10px] text-[var(--muted)]">زيادة (Dr 512122 / Cr 212103) أو تصحيح زيادة سابقة.</p>
            </div>

            <button
              type="button"
              onClick={() => setAdjustOpen((prev) => !prev)}
              className="inline-flex items-center gap-1 text-[11px] font-bold text-[var(--accent)] hover:underline"
            >
              <Plus size={13} />
              <span>إضافة تسوية راتب</span>
            </button>
          </div>

          {adjustOpen && (
            <div className="rounded-xl border border-[var(--accent)]/30 bg-[var(--accent)]/5 p-3 space-y-2.5 text-xs">
              <h5 className="font-bold text-[var(--accent)]">إضافة تسوية على الراتب</h5>
              <div className="flex items-center gap-2">
                <label className="text-[11px] font-bold text-[var(--ink)] shrink-0">المبلغ (+ / -):</label>
                <SearchableSelect
                  ariaLabel="بند التعديل"
                  value={itemType}
                  onChange={setItemType}
                  placeholder="اكتب بند التعديل..."
                  options={itemTypes}
                />
                <input
                  type="number"
                  step="0.01"
                  placeholder="مثال: 150 أو -50"
                  value={adjustAmount}
                  onChange={(e) => setAdjustAmount(e.target.value)}
                  className="w-32 p-1.5 rounded-md border border-[var(--line)] bg-[var(--surface)] text-xs font-mono font-bold"
                />
                <span className="text-[10px] text-[var(--muted)]">موجب للزيادة، سالب للاستقطاع</span>
              </div>

              <div className="space-y-1">
                <label className="text-[11px] font-bold text-[var(--ink)] block">السبب والبيان (إلزامي):</label>
                <input
                  type="text"
                  placeholder="مثلاً: مكافأة تميز تشغيلي أو تسوية بدل سكن..."
                  value={adjustReason}
                  onChange={(e) => setAdjustReason(e.target.value)}
                  className="w-full p-1.5 rounded-md border border-[var(--line)] bg-[var(--surface)] text-xs"
                />
              </div>

              <div className="flex items-center justify-end gap-2 pt-1">
                <button
                  type="button"
                  onClick={() => setAdjustOpen(false)}
                  className="px-2.5 py-1 text-xs rounded-lg border border-[var(--line)] bg-[var(--surface)]"
                >
                  إلغاء
                </button>
                <button
                  type="button"
                  disabled={submitting}
                  onClick={handleAddAdjustment}
                  className="px-3.5 py-1 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)]"
                >
                  تأكيد وحفظ التسوية
                </button>
              </div>
            </div>
          )}

          {phase3Items.length === 0 ? (
            <p className="text-[11px] text-[var(--muted)] py-1">لا توجد تسويات مسجلة في المرحلة 3.</p>
          ) : (
            <div className="divide-y divide-[var(--line)] rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs">
              {phase3Items.map((item) => (
                <div key={item.key} className="flex items-center justify-between p-2.5">
                  <div>
                    <span className="font-semibold text-[var(--ink)]">{item.labelAr}</span>
                    <span className="text-[10px] text-[var(--muted)] block">المصدر: {item.source}</span>
                  </div>
                  <span
                    className={`num font-mono font-bold ${
                      item.amount >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--danger)]'
                    }`}
                  >
                    {item.amount > 0 ? `+${item.amount}` : item.amount} ر.س
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Edit History Timeline */}
        {row.edits && row.edits.length > 0 && (
          <div className="space-y-2 pt-2 border-t border-[var(--line)]">
            <div className="flex items-center gap-1.5 text-xs font-bold text-[var(--ink)]">
              <History size={14} className="text-[var(--accent)]" />
              <span>سجل التعديلات والقرارات السابقة ({row.edits.length})</span>
            </div>

            <div className="space-y-1.5">
              {row.edits.map((e, idx) => (
                <div
                  key={idx}
                  className="p-2 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-1"
                >
                  <div className="flex items-center justify-between text-[11px]">
                    <span className="font-bold text-[var(--ink)]">@{e.by}</span>
                    <span className="num text-[10px] text-[var(--muted)]">
                      {new Date(e.at).toLocaleString('ar-SA')}
                    </span>
                  </div>
                  <p className="text-[11px] text-[var(--muted)]">{e.reason}</p>
                  <div className="text-[10px] font-mono flex items-center gap-2 pt-0.5">
                    <span className="num text-[var(--muted)]">{e.from} ر.س</span>
                    <ArrowRight size={12} className="text-[var(--muted)]" />
                    <span className="num font-bold text-[var(--accent)]">{e.to} ر.س</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </Drawer>
  );
}
