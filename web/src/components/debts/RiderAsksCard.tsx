'use client';

import React, { useEffect, useState } from 'react';
import { Smartphone } from 'lucide-react';
import { api } from '../../lib/api';
import { useToast } from '../ui/Toast';
import { Dialog } from '../ui/Dialog';

// BR §25.8 (docs/api/rider-asks.md): asks from the rider app reach the current supervisor only.
export interface RiderAsk {
  id: string;
  riderIqama: string;
  riderName: string;
  type: 'cash' | 'bank';
  amount: number;
  reason: string;
  status: 'open' | 'converted' | 'declined' | 'cancelled';
  createdAt: string;
}

type Action = { kind: 'convert' | 'decline'; ask: RiderAsk };

export function RiderAsksCard({ onConverted }: { onConverted: () => void }) {
  const { toast } = useToast();
  const [asks, setAsks] = useState<RiderAsk[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [action, setAction] = useState<Action | null>(null);
  const [type, setType] = useState<'cash' | 'bank'>('cash');
  const [amount, setAmount] = useState('');
  const [text, setText] = useState('');
  const [saving, setSaving] = useState(false);

  const load = async () => {
    setError(null);
    try {
      setAsks(await api.getRiderAsks('open'));
    } catch (err: any) {
      setError(err.message || 'تعذر تحميل طلبات المناديب من التطبيق');
    }
  };

  useEffect(() => {
    load();
  }, []);

  const open = (kind: Action['kind'], ask: RiderAsk) => {
    setAction({ kind, ask });
    setType(ask.type);
    setAmount(String(ask.amount));
    setText('');
  };

  const submit = async () => {
    if (!action) return;
    setSaving(true);
    try {
      if (action.kind === 'convert') {
        const value = Number(amount);
        if (!(value > 0)) {
          toast('المبلغ غير صالح', 'warning');
          return;
        }
        await api.convertRiderAsk(action.ask.id, { type, amount: value, reason: text.trim() || undefined });
        toast('تم رفع طلب السلفة للمحاسب', 'success');
        onConverted();
      } else {
        await api.declineRiderAsk(action.ask.id, text.trim());
        toast('تم رفض الطلب وإبلاغ المندوب بالسبب', 'success');
      }
      setAction(null);
      await load();
    } catch (err: any) {
      toast(err.message || 'تعذر تنفيذ الإجراء', 'danger');
    } finally {
      setSaving(false);
    }
  };

  if (!error && asks.length === 0) return null;

  return (
    <div className="p-4 rounded-2xl border border-[var(--warning)]/40 bg-[var(--warning)]/5 space-y-3" data-testid="rider-asks">
      <div className="flex items-center gap-2 font-bold text-sm text-[var(--ink)]">
        <Smartphone size={16} />
        <span>طلبات المناديب من التطبيق ({asks.length})</span>
      </div>
      {error ? (
        <div role="alert" className="text-xs text-[var(--danger)]">
          {error}{' '}
          <button onClick={load} className="underline">
            إعادة المحاولة
          </button>
        </div>
      ) : (
        <ul className="space-y-2">
          {asks.map((a) => (
            <li key={a.id} data-ask={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
              <div className="min-w-0">
                <div className="font-bold text-sm text-[var(--ink)]">
                  {a.riderName} <span className="num text-xs text-[var(--muted)]">{a.riderIqama}</span>
                </div>
                <div className="text-xs text-[var(--muted)]">
                  <span className="num font-bold text-[var(--ink)]">{a.amount.toLocaleString('en-US')}</span> ر.س · {a.type === 'bank' ? 'بنكي' : 'نقدي'} · {a.reason}
                </div>
              </div>
              <div className="flex gap-2">
                <button onClick={() => open('convert', a)} className="px-3 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold">
                  تحويل لطلب سلفة
                </button>
                <button onClick={() => open('decline', a)} className="px-3 py-1.5 rounded-lg border border-[var(--line)] text-xs font-bold text-[var(--danger)]">
                  رفض
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <Dialog
        open={action !== null}
        onClose={() => !saving && setAction(null)}
        title={action?.kind === 'convert' ? 'تحويل طلب المندوب لطلب سلفة رسمي' : 'رفض طلب المندوب'}
        description={action ? `${action.ask.riderName} — ${action.ask.reason}` : ''}
        actions={
          <>
            <button onClick={() => setAction(null)} disabled={saving} className="h-9 px-3 rounded-lg border border-[var(--line)] text-sm">
              تراجع
            </button>
            <button
              id="rider-ask-submit"
              onClick={submit}
              disabled={saving || (action?.kind === 'decline' && text.trim().length < 3)}
              className="h-9 px-3 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-sm disabled:opacity-50"
            >
              {action?.kind === 'convert' ? 'رفع للمحاسب' : 'تأكيد الرفض'}
            </button>
          </>
        }
      >
        <div className="space-y-3 text-xs text-[var(--muted)]">
          {action?.kind === 'convert' && (
            <div className="grid grid-cols-2 gap-3">
              <label className="space-y-1">
                <span className="block">النوع</span>
                <select id="rider-ask-type" value={type} onChange={(e) => setType(e.target.value as 'cash' | 'bank')} className="w-full h-9 rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2 text-sm text-[var(--ink)]">
                  <option value="cash">نقدي</option>
                  <option value="bank">بنكي</option>
                </select>
              </label>
              <label className="space-y-1">
                <span className="block">المبلغ</span>
                <input id="rider-ask-amount" type="number" min={1} value={amount} onChange={(e) => setAmount(e.target.value)} className="w-full h-9 rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2 text-sm num text-[var(--ink)]" />
              </label>
            </div>
          )}
          <label className="block space-y-1">
            <span className="block">{action?.kind === 'convert' ? 'سبب الطلب للمحاسب (اختياري؛ الافتراضي سبب المندوب)' : 'سبب الرفض (يظهر للمندوب)'}</span>
            <textarea id="rider-ask-text" value={text} onChange={(e) => setText(e.target.value)} maxLength={500} rows={3} className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] p-2 text-sm text-[var(--ink)]" />
          </label>
        </div>
      </Dialog>
    </div>
  );
}
