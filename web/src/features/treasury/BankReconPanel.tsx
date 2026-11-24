'use client';

import React, { useCallback, useMemo, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { formatSar } from '../../lib/money';

type LineRow = {
  id?: string;
  lineNo?: number;
  occurredOn?: string;
  amount?: number;
  reference?: string;
  matched?: boolean;
  disposition?: string;
  timing?: boolean;
  documentId?: string;
  reconKind?: string;
  allowedActions?: string[];
};

type StatementView = {
  id?: string;
  periodMonth?: string;
  openingBalance?: number;
  closingBalance?: number;
  bankClosingBalance?: number;
  reconciledBalance?: number;
  timingCarriedAmount?: number;
  matchedCount?: number;
  unmatchedCount?: number;
  timingCount?: number;
  status?: string;
  allowedActions?: string[];
  bankFeeAccountCode?: string;
  bankIncomeAccountCode?: string;
  bankSuspenseAccountCode?: string;
  lines?: LineRow[];
};

const ACTION_LABELS: Record<string, string> = {
  create_payment: 'إنشاء سند صرف',
  create_receipt: 'إنشاء سند قبض',
  create_journal: 'تسجيل قيد',
  mark_timing: 'فرق توقيت',
  open_original: 'فتح القيد الأصلي للتصحيح',
  clear_timing: 'إلغاء فرق التوقيت',
};

const KIND_BY_ACTION: Record<string, string> = {
  create_payment: 'payment',
  create_receipt: 'receipt',
  create_journal: 'journal',
  mark_timing: 'timing',
  open_original: 'open_original',
  clear_timing: 'clear_timing',
};

/** Bank recon: line actions replace generic adjust (TRIAL6). */
export function BankReconPanel() {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statementId, setStatementId] = useState('');
  const [view, setView] = useState<StatementView | null>(null);
  const [noteDraft, setNoteDraft] = useState('');
  const [upload, setUpload] = useState({
    bankAccountId: '',
    periodMonth: '2026-10',
    openingBalance: '0',
    closingBalance: '0',
    linesJson: '[{"occurredOn":"2026-10-01","amount":100,"reference":"SYN"}]',
  });

  const applyView = (res: Record<string, unknown>) => {
    setView(res as StatementView);
    if (res.id) setStatementId(String(res.id));
  };

  const load = useCallback(async (id?: string) => {
    const sid = (id ?? statementId).trim();
    if (!sid) return;
    setLoading(true);
    setError(null);
    try {
      const res = await api.getBankStatement(sid);
      applyView(res);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل كشف البنك');
    } finally {
      setLoading(false);
    }
  }, [statementId]);

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
      applyView(res);
      toast('تم رفع كشف البنك', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الرفع', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const autoMatch = async () => {
    if (!statementId.trim()) return;
    if (!view?.allowedActions?.includes('auto_match')) {
      toast('صلاحية المطابقة غير متاحة لهذا الكشف', 'warning');
      return;
    }
    setBusy(true);
    try {
      const res = await api.autoMatchBankStatement(statementId.trim());
      applyView(res);
      toast('تم تشغيل المطابقة التلقائية', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المطابقة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const lineAction = async (line: LineRow, action: string) => {
    if (!statementId.trim() || !line.id) return;
    if (!line.allowedActions?.includes(action)) {
      toast('هذا الإجراء غير متاح لهذا السطر', 'warning');
      return;
    }
    const kind = KIND_BY_ACTION[action];
    if (!kind) return;
    const needsNote = kind === 'timing' || kind === 'payment' || kind === 'receipt' || kind === 'journal';
    const note = noteDraft.trim() || (needsNote ? 'إجراء مطابقة بنكية' : '');
    if (needsNote && note.length < 3) {
      toast('أدخل بيانًا من 3 أحرف على الأقل قبل الإجراء', 'warning');
      return;
    }
    setBusy(true);
    try {
      const res = await api.bankStatementLineAction(statementId.trim(), line.id, {
        kind,
        note,
      });
      if (kind === 'open_original') {
        const route = String((res as { formRoute?: string }).formRoute || '/app/accounting?tab=manual_journals');
        toast('افتح العكس والتصحيح من مسار القيد — لا يُعدَّل قيد مرحّل', 'info');
        window.location.assign(route);
        return;
      }
      if (kind === 'payment' || kind === 'receipt' || kind === 'journal') {
        const docId = String((res as { documentId?: string }).documentId || '');
        toast(
          docId
            ? `مُسودة ${ACTION_LABELS[action] || action} جاهزة للاعتماد (${docId})`
            : 'أُنشئت المسودة — اعتمدها من القيود اليدوية ثم يُطابق السطر تلقائيًا',
          'success'
        );
      } else {
        toast(kind === 'timing' ? 'سُجّل فرق التوقيت ويُرحَّل للشهر التالي' : 'تم تحديث السطر', 'success');
      }
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر تنفيذ إجراء السطر', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const lines = Array.isArray(view?.lines) ? view!.lines! : [];
  const cols = useMemo(
    () => [
      { key: 'lineNo', header: 'م', type: 'number' as const, getValue: (r: LineRow) => Number(r.lineNo || 0) },
      { key: 'occurredOn', header: 'التاريخ', type: 'date' as const, getValue: (r: LineRow) => String(r.occurredOn || '') },
      { key: 'reference', header: 'المرجع', type: 'text' as const, getValue: (r: LineRow) => String(r.reference || '') },
      { key: 'amount', header: 'المبلغ', type: 'number' as const, getValue: (r: LineRow) => Number(r.amount || 0) },
      {
        key: 'disposition',
        header: 'الحالة',
        type: 'text' as const,
        getValue: (r: LineRow) => {
          if (r.matched || r.disposition === 'matched') return 'مطابق';
          if (r.disposition === 'timing' || r.timing) return 'فرق توقيت';
          if (r.disposition === 'pending_document') return 'بانتظار مستند';
          return 'غير مطابق';
        },
      },
    ],
    []
  );
  const filter = useTableAutoFilter<LineRow>(lines, cols);

  return (
    <div className="space-y-4" data-testid="bank-recon-panel">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <h3 className="text-sm font-bold">مطابقة البنوك</h3>
        <p className="text-xs text-[var(--muted)]">
          لكل سطر غير مطابق: سند صرف (عمولات بنكية)، سند قبض (إيرادات بنكية)، قيد تحت التسوية، فرق توقيت، أو فتح القيد الأصلي للعكس والتصحيح. لا تسوية عامة ولا ترحيل على فروق الخزنة.
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
          {(
            [
              ['bankAccountId', 'معرّف الحساب البنكي'],
              ['periodMonth', 'شهر الكشف'],
              ['openingBalance', 'رصيد الافتتاح'],
              ['closingBalance', 'رصيد الإقفال'],
            ] as const
          ).map(([k, label]) => (
            <label key={k} className="text-xs font-bold">
              {label}
              <input
                value={upload[k]}
                onChange={(e) => setUpload({ ...upload, [k]: e.target.value })}
                className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
              />
            </label>
          ))}
        </div>
        <textarea
          value={upload.linesJson}
          onChange={(e) => setUpload({ ...upload, linesJson: e.target.value })}
          rows={4}
          aria-label="سطور الكشف"
          className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
        />
        <button
          type="button"
          disabled={busy}
          onClick={() => void doUpload()}
          className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
        >
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
          <button
            type="button"
            disabled={busy || loading}
            onClick={() => void load()}
            className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold inline-flex items-center gap-1"
          >
            <RefreshCw className="w-3.5 h-3.5" aria-hidden />
            عرض
          </button>
          {view?.allowedActions?.includes('auto_match') && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void autoMatch()}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold"
            >
              مطابقة تلقائية
            </button>
          )}
        </div>

        {view && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs" data-testid="bank-recon-stats">
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
              <div className="text-[var(--muted)]">مطابق</div>
              <div className="font-bold">{Number(view.matchedCount || 0)}</div>
            </div>
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
              <div className="text-[var(--muted)]">غير مطابق</div>
              <div className="font-bold">{Number(view.unmatchedCount || 0)}</div>
            </div>
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
              <div className="text-[var(--muted)]">فرق توقيت</div>
              <div className="font-bold">{Number(view.timingCount || 0)}</div>
            </div>
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
              <div className="text-[var(--muted)]">رصيد بعد المطابقة</div>
              <div className="font-bold font-mono">{formatSar(Number(view.reconciledBalance ?? 0))}</div>
            </div>
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)] col-span-2">
              <div className="text-[var(--muted)]">رصيد إقفال البنك</div>
              <div className="font-bold font-mono">{formatSar(Number(view.bankClosingBalance ?? view.closingBalance ?? 0))}</div>
            </div>
            <div className="p-2 rounded-xl bg-[var(--raised)] border border-[var(--line)] col-span-2 text-[10px] text-[var(--muted)]">
              حسابات التسوية: عمولات {view.bankFeeAccountCode || '541401'} · إيرادات{' '}
              {view.bankIncomeAccountCode || '422103'} · تحت التسوية {view.bankSuspenseAccountCode || '114108'}
            </div>
          </div>
        )}

        <label className="text-xs font-bold block">
          بيان الإجراء (سند/قيد/فرق توقيت)
          <input
            value={noteDraft}
            onChange={(e) => setNoteDraft(e.target.value)}
            placeholder="مثال: عمولة بنكية شهرية"
            className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs"
          />
        </label>

        {loading ? (
          <Skeleton className="h-40 w-full" />
        ) : error ? (
          <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
        ) : !view ? (
          <EmptyState title="لا كشف معروض" description="ارفع كشفًا أو أدخل معرّفه." />
        ) : lines.length === 0 ? (
          <EmptyState title="لا سطور" description="الكشف بلا سطور." />
        ) : (
          <div className="overflow-auto border border-[var(--line)] rounded-2xl">
            <AutoFilterBar controller={filter} totalCount={lines.length} />
            <table className="w-full text-xs">
              <thead className="sticky top-0 bg-[var(--raised)]">
                <tr>
                  <AutoFilterTh controller={filter} colKey="lineNo" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="occurredOn" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="reference" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="amount" className="p-2 text-start" />
                  <AutoFilterTh controller={filter} colKey="disposition" className="p-2 text-start" />
                  <th className="p-2 text-start font-bold">إجراءات</th>
                </tr>
              </thead>
              <tbody>
                {filter.rows.map((line) => (
                  <tr key={String(line.id)} className="border-t border-[var(--line)]">
                    <td className="p-2 font-mono">{line.lineNo}</td>
                    <td className="p-2 font-mono">{line.occurredOn}</td>
                    <td className="p-2">
                      <bdi>{line.reference}</bdi>
                    </td>
                    <td className="p-2 font-mono">{formatSar(Number(line.amount || 0))}</td>
                    <td className="p-2">
                      {line.matched || line.disposition === 'matched'
                        ? 'مطابق'
                        : line.disposition === 'timing' || line.timing
                          ? 'فرق توقيت'
                          : line.disposition === 'pending_document'
                            ? 'بانتظار مستند'
                            : 'غير مطابق'}
                    </td>
                    <td className="p-2">
                      <div className="flex flex-wrap gap-1">
                        {(line.allowedActions || []).map((action) => (
                          <button
                            key={action}
                            type="button"
                            disabled={busy}
                            onClick={() => void lineAction(line, action)}
                            className="px-2 py-1 rounded-lg border border-[var(--line)] text-[10px] font-bold disabled:opacity-50"
                          >
                            {ACTION_LABELS[action] || action}
                          </button>
                        ))}
                        {line.documentId ? (
                          <span className="text-[10px] text-[var(--muted)] font-mono">{line.documentId}</span>
                        ) : null}
                      </div>
                    </td>
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
