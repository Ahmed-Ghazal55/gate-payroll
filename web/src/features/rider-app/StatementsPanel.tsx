'use client';

// Statements to riders: preview one rider's approved statement at phone width, then notify the period.
// No amounts are sent in notifications (the server sends only "your statement is ready").
import React, { useState } from 'react';
import Link from 'next/link';
import { Bell, Eye } from 'lucide-react';
import { api, type RiderMonthlyStatement } from '../../lib/api';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { shouldWarnUnapproved } from './statement-preview';
import { formatSar } from '../../lib/money';

const money = (n?: number | null) => formatSar((n ?? 0));

function platformLabelAr(raw?: string | null): string {
  const key = (raw || '').trim().toLowerCase();
  if (key === 'keeta') return 'كيتا';
  if (key === 'hungerstation' || key === 'hs') return 'هنقرستيشن';
  if (key === 'ninja') return 'نينجا';
  if (key === 'jahez') return 'جاهز';
  if (key === 'chefz' || key === 'thechefz') return 'ذا شفز';
  if (key === 'amazon') return 'أمازون';
  return raw || '—';
}

function previousMonth(): string {
  const d = new Date();
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() - 1);
  return d.toISOString().slice(0, 7);
}

export function StatementsPanel({ canSend }: { canSend: boolean }) {
  const { toast } = useToast();
  const [period, setPeriod] = useState(previousMonth());
  const [iqama, setIqama] = useState('');
  const [statement, setStatement] = useState<RiderMonthlyStatement | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [sending, setSending] = useState(false);

  const load = async (forIqama: string, forPeriod: string) => {
    if (!forIqama) return;
    setLoading(true);
    setError(null);
    try {
      setStatement(await api.getRiderStatementPreview(forIqama, forPeriod));
    } catch (err: any) {
      setStatement(null);
      setError(err?.message || 'تعذر تحميل كشف الحساب');
    } finally {
      setLoading(false);
    }
  };

  const notify = async () => {
    setSending(true);
    try {
      const res = await api.notifyStatementsForPeriod(period);
      toast(
        `تم إشعار ${res.notifiedRidersCount} مندوب بكشوفهم المعتمدة` +
          (res.skippedAlreadyNotifiedCount > 0 ? ` (${res.skippedAlreadyNotifiedCount} سبق إشعارهم)` : ''),
        'success'
      );
      setConfirm(false);
    } catch (err: any) {
      toast(err?.message || 'تعذر إرسال الإشعارات', 'danger');
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="space-y-4">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-end gap-3">
        <label className="space-y-1">
          <span className="text-xs font-bold block">الفترة</span>
          <input
            type="month"
            value={period}
            onChange={(e) => {
              setPeriod(e.target.value);
              load(iqama, e.target.value);
            }}
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
          />
        </label>
        <div className="min-w-64 flex-1 space-y-1">
          <span className="text-xs font-bold block">المندوب (للمعاينة)</span>
          <RiderPicker
            value={iqama}
            onChange={(r) => {
              const id = r ? r.iqama ?? r.id : '';
              setIqama(id);
              if (id) load(id, period);
              else setStatement(null);
            }}
          />
        </div>
        {iqama ? (
          <Link
            href={`/riders/statement/?iqama=${encodeURIComponent(iqama)}`}
            className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold inline-flex items-center gap-1.5"
          >
            <Eye size={14} /> كشف الحساب الكامل
          </Link>
        ) : null}
        {canSend ? (
          <button
            type="button"
            onClick={() => setConfirm(true)}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold inline-flex items-center gap-1.5"
          >
            <Bell size={14} /> إرسال إشعار الكشوف المعتمدة للفترة
          </button>
        ) : null}
      </div>

      {!iqama ? (
        <p className="text-xs text-[var(--muted)] p-4">اختر مندوبًا لمعاينة كشفه كما يظهر في التطبيق.</p>
      ) : loading ? (
        <div className="max-w-xs mx-auto space-y-3">
          <Skeleton className="h-40 w-full rounded-2xl" />
        </div>
      ) : error ? (
        <p className="text-xs text-[var(--danger)] p-4">{error}</p>
      ) : statement ? (
        <div className="w-[360px] max-w-full mx-auto border-4 border-[var(--ink)] rounded-3xl p-4 bg-[var(--surface)] space-y-3" data-testid="statement-phone-preview">
          {(() => {
            const s = statement as RiderMonthlyStatement & {
              shortLines?: Array<{ labelAr?: string; label?: string; key?: string; amount?: number }>;
              closingMarkerAr?: string;
              disclaimerAr?: string;
              deductions?: Array<{ labelAr?: string; label?: string; category?: string; amount?: number }>;
            };
            return (
              <>
          <div className="p-3 rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] text-center space-y-1">
            <div className="text-[11px]">{s.header?.period || period}</div>
            <div className="text-xl font-black num">{money(s.closing)}</div>
            {/* Same marker as PayStatements.MarkerAr on the server. */}
            <div className="text-[10px]">{s.closingMarkerAr || (s.closing > 0 ? 'لك' : s.closing < 0 ? 'عليك' : 'متوازن')}</div>
          </div>
          {shouldWarnUnapproved(s.status) ? (
            <p className="text-[11px] text-[var(--warning)] text-center">لا يوجد كشف معتمد لهذه الفترة بعد؛ لن يصل المندوب إشعار له.</p>
          ) : null}
          <div className="space-y-1.5 text-xs">
            {s.shortLines && s.shortLines.length > 0
              ? s.shortLines.map((line, i) => (
                  <div key={i} className="flex justify-between p-2 rounded-xl bg-[var(--raised)]">
                    <span>{line.labelAr || line.label || line.key}</span>
                    <span className="num font-bold">{money(line.amount)}</span>
                  </div>
                ))
              : s.cards.map((c, i) => (
                  <div key={i} className="flex justify-between p-2 rounded-xl bg-[var(--raised)]">
                    <span>{platformLabelAr(c.platform || c.app)}</span>
                    <span className="num font-bold">{money(c.gross)}</span>
                  </div>
                ))}
            {(s.deductions || []).map((d, i) => (
              <div key={`d-${i}`} className="flex justify-between p-2 rounded-xl bg-[var(--raised)] text-[var(--danger)]">
                <span>{d.labelAr || d.label || d.category || 'خصم'}</span>
                <span className="num font-bold">{money(d.amount)}</span>
              </div>
            ))}
            <div className="flex justify-between py-1 border-t border-[var(--line)]">
              <span className="text-[var(--muted)]">الرصيد المرحل</span>
              <span className="num">{money(s.carried)}</span>
            </div>
            <div className="flex justify-between py-1">
              <span className="text-[var(--muted)]">صافي الشهر</span>
              <span className="num font-bold">{money(s.net)}</span>
            </div>
            <div className="flex justify-between py-1 font-bold">
              <span>{s.closingMarkerAr || (s.closing > 0 ? 'لك' : s.closing < 0 ? 'عليك' : 'متوازن')}</span>
              <span className="num">{money(s.closing)}</span>
            </div>
          </div>
          <p className="text-[10px] text-[var(--muted)] text-center">{s.disclaimerAr || 'للاطلاع فقط — ليست مستندًا قانونيًا'}</p>
              </>
            );
          })()}
        </div>
      ) : null}

      <Dialog
        open={confirm}
        onClose={() => setConfirm(false)}
        title="إشعار الكشوف المعتمدة"
        description={`سيتم إشعار كل مندوب له كشف معتمد في الفترة ${period} بأن كشفه جاهز في التطبيق، بدون أي مبالغ في الإشعار. متابعة؟`}
        actions={
          <button type="button" disabled={sending} onClick={notify} className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            {sending ? 'جاري الإرسال…' : 'إرسال'}
          </button>
        }
      >
        <span />
      </Dialog>
    </div>
  );
}
