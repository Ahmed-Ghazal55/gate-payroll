'use client';

import React, { useState } from 'react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState } from '../../components/ui/EmptyState';

/** Minimal bank recon UI on existing treasury bank-statement routes (FLOW1 / TRACE U-W3-13). */
export function BankReconPanel() {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  const [statementId, setStatementId] = useState('');
  const [view, setView] = useState<Record<string, unknown> | null>(null);
  const [upload, setUpload] = useState({
    bankAccountId: '',
    periodMonth: '2026-10',
    openingBalance: '0',
    closingBalance: '0',
    linesJson: '[{"occurredOn":"2026-10-01","amount":100,"reference":"SYN"}]',
  });
  const [adjust, setAdjust] = useState({ amount: '', reason: '' });
  const [rejectReason, setRejectReason] = useState('');

  const doUpload = async () => {
    setBusy(true);
    try {
      let lines: unknown = [];
      try {
        lines = JSON.parse(upload.linesJson || '[]');
      } catch {
        toast('سطور الكشف يجب أن تكون JSON صالح', 'danger');
        return;
      }
      const res = await api.uploadBankStatement({
        bankAccountId: upload.bankAccountId.trim(),
        periodMonth: upload.periodMonth.trim(),
        openingBalance: Number(upload.openingBalance),
        closingBalance: Number(upload.closingBalance),
        lines,
      });
      setView(res);
      setStatementId(String(res.id || ''));
      toast('تم رفع كشف البنك', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الرفع', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const load = async () => {
    if (!statementId.trim()) return;
    setBusy(true);
    try {
      const res = await api.getBankStatement(statementId.trim());
      setView(res);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التحميل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const autoMatch = async () => {
    if (!statementId.trim()) return;
    setBusy(true);
    try {
      const res = await api.autoMatchBankStatement(statementId.trim());
      setView(res);
      toast('تم تشغيل المطابقة التلقائية', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المطابقة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const requestAdjust = async () => {
    if (!statementId.trim()) return;
    setBusy(true);
    try {
      const res = await api.requestBankAdjust(statementId.trim(), {
        amount: Number(adjust.amount),
        reason: adjust.reason.trim(),
      });
      toast(`طُلبت تسوية (${String(res.status || 'pending_approval')}) — يمكن رفضها لإغلاق الحالة`, 'success');
      setView(res);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر طلب التسوية', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const rejectAdjust = async () => {
    const id = String(view?.id || '');
    if (!id || view?.kind !== 'bank_statement_adjust') {
      toast('افتح طلب تسوية معلقًا أولًا', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.rejectBankAdjust(id, { reason: rejectReason.trim() });
      toast('رُفضت التسوية وأُغلقت الحالة', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الرفض', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4" data-testid="bank-recon-panel">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <h3 className="text-sm font-bold">مطابقة البنوك</h3>
        <p className="text-xs text-[var(--muted)]">
          رفع كشف، مطابقة تلقائية، وطلب تسوية بانتظار اعتماد. اعتماد الترحيل موقوف حتى يُحدد حساب فرق التوقيت البنكي.
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
          {(['bankAccountId', 'periodMonth', 'openingBalance', 'closingBalance'] as const).map((k) => (
            <input
              key={k}
              placeholder={k}
              value={upload[k]}
              onChange={(e) => setUpload({ ...upload, [k]: e.target.value })}
              className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
            />
          ))}
        </div>
        <textarea
          value={upload.linesJson}
          onChange={(e) => setUpload({ ...upload, linesJson: e.target.value })}
          rows={4}
          className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
        />
        <button type="button" disabled={busy} onClick={() => void doUpload()} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
          رفع كشف
        </button>
      </div>

      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <div className="flex flex-wrap gap-2 items-end">
          <label className="text-xs font-bold flex-1 min-w-[180px]">
            معرّف الكشف
            <input
              value={statementId}
              onChange={(e) => setStatementId(e.target.value)}
              className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
            />
          </label>
          <button type="button" disabled={busy} onClick={() => void load()} className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold">
            عرض
          </button>
          <button type="button" disabled={busy} onClick={() => void autoMatch()} className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold">
            مطابقة تلقائية
          </button>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-2">
          <input
            placeholder="مبلغ التسوية"
            value={adjust.amount}
            onChange={(e) => setAdjust({ ...adjust, amount: e.target.value })}
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
          <input
            placeholder="سبب التسوية"
            value={adjust.reason}
            onChange={(e) => setAdjust({ ...adjust, reason: e.target.value })}
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
          <button type="button" disabled={busy} onClick={() => void requestAdjust()} className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold">
            طلب تسوية
          </button>
        </div>
        <div className="flex gap-2">
          <input
            placeholder="سبب رفض التسوية المعلقة"
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            className="flex-1 p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
          <button type="button" disabled={busy} onClick={() => void rejectAdjust()} className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold">
            رفض التسوية
          </button>
        </div>
        {view ? (
          <pre className="text-[10px] font-mono overflow-auto max-h-64 p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
            {JSON.stringify(view, null, 2)}
          </pre>
        ) : (
          <EmptyState title="لا كشف معروض" description="ارفع كشفًا أو أدخل معرّفه." />
        )}
      </div>
    </div>
  );
}
