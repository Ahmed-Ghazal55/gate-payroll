'use client';

import React, { useEffect, useState } from 'react';
import { AlertTriangle, Trash2, ArrowRightCircle, Eye } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../ui/Toast';
import { Dialog } from '../ui/Dialog';
import { formatSar } from '../../lib/money';

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
      const rows = Array.isArray((diff as { items?: unknown[] }).items)
        ? ((diff as { items: Array<Record<string, unknown>> }).items)
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
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)] sticky top-0">
              <tr>
                <th className="p-2 text-start">البند</th>
                <th className="p-2 text-start">محاكاة</th>
                <th className="p-2 text-start">حي</th>
              </tr>
            </thead>
            <tbody>
              {diffRows.length === 0 ? (
                <tr>
                  <td colSpan={3} className="p-3 text-[var(--muted)]">
                    لا فروقات ظاهرة بين المحاكاة والوضع الحي.
                  </td>
                </tr>
              ) : (
                diffRows.map((row, idx) => (
                  <tr key={idx} className="border-t border-[var(--line)]">
                    <td className="p-2">{String(row.labelAr ?? row.field ?? row.key ?? '—')}</td>
                    <td className="p-2 num">
                      {typeof row.simulation === 'number' ? formatSar(row.simulation) : String(row.simulation ?? '—')}
                    </td>
                    <td className="p-2 num">
                      {typeof row.live === 'number' ? formatSar(row.live) : String(row.live ?? '—')}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Dialog>

      <Dialog open={previewOpen} onClose={() => setPreviewOpen(false)} title="معاينة القيود المتوقعة" description="عرض فقط — بلا ترحيل في وضع المحاكاة.">
        <div className="max-h-72 overflow-auto rounded-lg border border-[var(--line)]">
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)] sticky top-0">
              <tr>
                <th className="p-2 text-start">الحساب</th>
                <th className="p-2 text-start">مدين</th>
                <th className="p-2 text-start">دائن</th>
              </tr>
            </thead>
            <tbody>
              {(preview?.lines ?? []).length === 0 ? (
                <tr>
                  <td colSpan={3} className="p-3 text-[var(--muted)]">
                    لا قيود متوقعة لهذه المحاكاة.
                  </td>
                </tr>
              ) : (
                (preview?.lines ?? []).map((line, idx) => (
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
