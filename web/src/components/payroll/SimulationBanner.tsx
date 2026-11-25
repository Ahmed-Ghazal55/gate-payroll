'use client';

import React, { useEffect, useState } from 'react';
import { AlertTriangle, Trash2, ArrowRightCircle, Eye } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../ui/Toast';
import { Dialog } from '../ui/Dialog';
import { formatSar } from '../../lib/money';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../ui/ColumnFilterMenu';

export interface SimulationBannerProps {
  simulationId: string;
  payoutBlocked?: boolean;
  onChanged?: () => void;
}

type PreviewBody = {
  allowedActions?: string[];
  lines?: Array<{ accountCode?: string; accountNameAr?: string; debit?: number; credit?: number }>;
};

export function SimulationBanner({ simulationId, payoutBlocked, onChanged }: SimulationBannerProps) {
  const { can } = useAuth();
  const { toast } = useToast();
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const [confirmPromote, setConfirmPromote] = useState(false);
  const [diffRows, setDiffRows] = useState<Array<Record<string, unknown>>>([]);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [preview, setPreview] = useState<PreviewBody | null>(null);
  const [actions, setActions] = useState<string[]>([
    ...(can('salaries.simulation.run') ? ['discard_simulation'] : []),
    ...(can('pay.manage') ? ['promote_simulation'] : []),
  ]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const body = (await api.getSimulationJournalPreview(simulationId)) as PreviewBody;
        if (cancelled) return;
        if (Array.isArray(body.allowedActions)) setActions(body.allowedActions);
        setPreview(body);
      } catch {
        /* keep permission-based defaults */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [simulationId]);

  const discard = async () => {
    if (!actions.includes('discard_simulation')) {
      toast('لا تملك صلاحية إلغاء المحاكاة', 'warning');
      return;
    }
    setBusy('discard');
    try {
      await api.discardSimulation(simulationId);
      toast('تم إلغاء المحاكاة', 'success');
      setConfirmDiscard(false);
      onChanged?.();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر إلغاء المحاكاة', 'danger');
    } finally {
      setBusy(null);
    }
  };

  const openPromote = async () => {
    if (!actions.includes('promote_simulation')) {
      toast('لا تملك صلاحية التحويل لدورة حقيقية', 'warning');
      return;
    }
    setBusy('diff');
    try {
      const diff = await api.getSimulationPromoteDiff(simulationId);
      const body = diff as { rows?: unknown[]; items?: unknown[] };
      const rows = Array.isArray(body.rows)
        ? (body.rows as Array<Record<string, unknown>>)
        : Array.isArray(body.items)
          ? (body.items as Array<Record<string, unknown>>)
          : Array.isArray(diff)
            ? (diff as Array<Record<string, unknown>>)
            : [];
      setDiffRows(rows);
      setConfirmPromote(true);
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر جلب فروقات التحويل', 'danger');
    } finally {
      setBusy(null);
    }
  };

  const promote = async () => {
    setBusy('promote');
    try {
      await api.promoteSimulation(simulationId);
      toast('تم إنشاء مسودة دورة حقيقية — ما زالت تحتاج الاعتمادات المعتادة', 'success');
      setConfirmPromote(false);
      onChanged?.();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر التحويل لدورة حقيقية', 'danger');
    } finally {
      setBusy(null);
    }
  };


  const diffCols = [
    {
      key: 'label',
      header: 'المندوب / التغيير',
      type: 'text' as const,
      getValue: (row: Record<string, unknown>) =>
        String(row.erpRiderId ?? row.labelAr ?? row.field ?? row.key ?? '—') +
        (row.changeAr || row.change ? ` · ${String(row.changeAr ?? row.change)}` : ''),
    },
    {
      key: 'simulation',
      header: 'محاكاة',
      type: 'number' as const,
      getValue: (row: Record<string, unknown>) =>
        Number(row.simNet ?? row.simulation ?? 0),
    },
    {
      key: 'live',
      header: 'حي',
      type: 'number' as const,
      getValue: (row: Record<string, unknown>) => Number(row.liveNet ?? row.live ?? 0),
    },
  ];
  const diffFilter = useTableAutoFilter(diffRows, diffCols);
  const previewLines = (preview?.lines ?? []).map((line) => ({
    ...line,
    accountCode: line.accountCode ?? (line as { accountHint?: string }).accountHint,
    accountNameAr: line.accountNameAr ?? (line as { labelAr?: string }).labelAr,
    debit: line.debit ?? ((line as { side?: string }).side === 'debit' ? Number((line as { amount?: number }).amount ?? 0) : 0),
    credit: line.credit ?? ((line as { side?: string }).side === 'credit' ? Number((line as { amount?: number }).amount ?? 0) : 0),
  }));
  const previewCols = [
    { key: 'account', header: 'الحساب', type: 'text' as const, getValue: (line: NonNullable<PreviewBody['lines']>[number]) => String(line.accountNameAr ?? line.accountCode ?? '—') },
    { key: 'debit', header: 'مدين', type: 'number' as const, getValue: (line: NonNullable<PreviewBody['lines']>[number]) => Number(line.debit ?? 0) },
    { key: 'credit', header: 'دائن', type: 'number' as const, getValue: (line: NonNullable<PreviewBody['lines']>[number]) => Number(line.credit ?? 0) },
  ];
  const previewFilter = useTableAutoFilter(previewLines, previewCols);

  return (
    <>
      <div
        role="status"
        className="sticky top-16 z-20 mb-4 rounded-xl border-2 border-dashed border-amber-500/60 bg-gradient-to-l from-amber-500/15 via-violet-500/10 to-amber-500/15 px-3 py-3 sm:px-4"
      >
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="space-y-1 min-w-0">
            <div className="inline-flex items-center gap-1.5 rounded-md bg-amber-600/90 px-2 py-0.5 text-[11px] font-bold text-white">
              <AlertTriangle size={12} />
              وضع المحاكاة (بيانات حية — بدون ترحيل)
            </div>
            <p className="text-xs sm:text-sm text-[var(--ink)] leading-relaxed">
              هذه الدورة في وضع المحاكاة: لا قيود محاسبية، لا مديونيات، لا ملفات بنكية، ولا إشعارات للمناديب، ومستبعدة تلقائيًا من كل التقارير والأرصدة.
            </p>
            {payoutBlocked ? (
              <p className="text-xs font-semibold text-[var(--warning)]">
                الصرف موقوف للمقارنة فقط — لا يُرسل ملف بنك من هذه الدورة.
              </p>
            ) : null}
          </div>
          <div className="flex flex-wrap gap-2 shrink-0">
            <button
              type="button"
              className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] px-2.5 py-1.5 text-xs font-semibold"
              onClick={async () => {
                setBusy('preview');
                try {
                  const body = (await api.getSimulationJournalPreview(simulationId)) as PreviewBody;
                  setPreview(body);
                  setPreviewOpen(true);
                } catch (err: unknown) {
                  toast(err instanceof Error ? err.message : 'تعذر معاينة القيود', 'danger');
                } finally {
                  setBusy(null);
                }
              }}
              disabled={busy !== null}
            >
              <Eye size={14} />
              معاينة القيود المتوقعة
            </button>
            {actions.includes('discard_simulation') && (
              <button
                type="button"
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--danger)]/15 text-[var(--danger)] px-2.5 py-1.5 text-xs font-semibold"
                onClick={() => setConfirmDiscard(true)}
                disabled={busy !== null}
              >
                <Trash2 size={14} />
                إلغاء وحذف المحاكاة
              </button>
            )}
            {actions.includes('promote_simulation') && (
              <button
                type="button"
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] px-2.5 py-1.5 text-xs font-semibold"
                onClick={() => void openPromote()}
                disabled={busy !== null}
              >
                <ArrowRightCircle size={14} />
                تحويل إلى دورة حقيقية
              </button>
            )}
          </div>
        </div>
      </div>

      <Dialog
        open={confirmDiscard}
        onClose={() => setConfirmDiscard(false)}
        title="إلغاء المحاكاة"
        description="هل تريد إلغاء هذه المحاكاة؟ لن يتأثر أي سجل حقيقي."
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setConfirmDiscard(false)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--danger)] text-white font-semibold disabled:opacity-50"
              onClick={() => void discard()}
              disabled={busy === 'discard'}
            >
              إلغاء وحذف
            </button>
          </>
        }
      >
        <p className="text-sm text-[var(--muted)]">لن يُحذف أي سجل حقيقي أو قيد مرحّل.</p>
      </Dialog>

      <Dialog
        open={confirmPromote}
        onClose={() => setConfirmPromote(false)}
        title="تحويل المحاكاة إلى دورة حقيقية"
        description="المسودة الحقيقية تمر بكل الاعتمادات المعتادة بعد التحويل."
        size="lg"
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setConfirmPromote(false)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-semibold disabled:opacity-50"
              onClick={() => void promote()}
              disabled={busy === 'promote'}
            >
              تحويل لحقيقي
            </button>
          </>
        }
      >
        <div className="max-h-64 overflow-auto rounded-lg border border-[var(--line)]">
          <AutoFilterBar controller={diffFilter} totalCount={diffRows.length} />
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)] sticky top-0">
              <tr>
                <AutoFilterTh controller={diffFilter} colKey="label" className="p-2 text-start" />
                <AutoFilterTh controller={diffFilter} colKey="simulation" className="p-2 text-start" />
                <AutoFilterTh controller={diffFilter} colKey="live" className="p-2 text-start" />
              </tr>
            </thead>
            <tbody>
              {diffFilter.rows.length === 0 ? (
                <tr>
                  <td colSpan={3} className="p-3 text-[var(--muted)]">
                    لا فروقات ظاهرة بين المحاكاة والوضع الحي.
                  </td>
                </tr>
              ) : (
                diffFilter.rows.map((row, idx) => (
                  <tr key={idx} className="border-t border-[var(--line)]">
                    <td className="p-2">
                      {String(row.erpRiderId ?? row.labelAr ?? row.field ?? row.key ?? '—')}
                      {row.changeAr || row.change ? (
                        <span className="block text-[10px] text-[var(--muted)]">{String(row.changeAr ?? row.change)}</span>
                      ) : null}
                    </td>
                    <td className="p-2 num">{formatSar(Number(row.simNet ?? row.simulation ?? 0))}</td>
                    <td className="p-2 num">{formatSar(Number(row.liveNet ?? row.live ?? 0))}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Dialog>

      <Dialog open={previewOpen} onClose={() => setPreviewOpen(false)} title="معاينة القيود المتوقعة" description="عرض فقط — بلا ترحيل في وضع المحاكاة.">
        <div className="max-h-72 overflow-auto rounded-lg border border-[var(--line)]">
          <AutoFilterBar controller={previewFilter} totalCount={previewLines.length} />
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)] sticky top-0">
              <tr>
                <AutoFilterTh controller={previewFilter} colKey="account" className="p-2 text-start" />
                <AutoFilterTh controller={previewFilter} colKey="debit" className="p-2 text-start" />
                <AutoFilterTh controller={previewFilter} colKey="credit" className="p-2 text-start" />
              </tr>
            </thead>
            <tbody>
              {previewFilter.rows.length === 0 ? (
                <tr>
                  <td colSpan={3} className="p-3 text-[var(--muted)]">
                    لا قيود متوقعة لهذه المحاكاة.
                  </td>
                </tr>
              ) : (
                previewFilter.rows.map((line, idx) => (
                  <tr key={idx} className="border-t border-[var(--line)]">
                    <td className="p-2">{String(line.accountNameAr ?? line.accountCode ?? '—')}</td>
                    <td className="p-2 num">{formatSar(Number(line.debit ?? 0))}</td>
                    <td className="p-2 num">{formatSar(Number(line.credit ?? 0))}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Dialog>
    </>
  );
}

/** Disabled production controls tooltip in simulation mode. */
export const SIMULATION_BLOCKED_HINT = 'غير متاح في وضع المحاكاة';
