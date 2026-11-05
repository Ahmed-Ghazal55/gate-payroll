'use client';

import React, { useEffect, useState } from 'react';
import { RefreshCw, ShieldOff, Smartphone } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../ui/Toast';
import { Dialog } from '../ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../ui/EmptyState';

// R5-C1 (docs/api/mobile-sessions.md): the row id is the 16-hex prefix the server returns; nothing else identifies a session.
export interface RiderSession {
  id: string;
  iqama: string;
  riderName: string;
  language: string;
  createdAt: string;
  expiresAtUtc: string;
  lastSeenAt: string;
  status: 'active' | 'expired' | 'revoked';
  deviceFingerprint: string;
  fcmRegistered: boolean;
}

type Target = { kind: 'one'; session: RiderSession } | { kind: 'all'; iqama: string; name: string };

const STATUS_LABEL: Record<RiderSession['status'], string> = { active: 'نشطة', expired: 'منتهية', revoked: 'ملغاة' };
const STATUS_CLASS: Record<RiderSession['status'], string> = {
  active: 'bg-[var(--success)]/15 text-[var(--success)]',
  expired: 'bg-[var(--muted)]/15 text-[var(--muted)]',
  revoked: 'bg-[var(--danger)]/15 text-[var(--danger)]',
};

const when = (v: string) => (v ? v.replace('T', ' ').slice(0, 16) : '—');

export function RiderSessionsPanel() {
  const { isAdmin, can } = useAuth();
  const { toast } = useToast();
  const allowed = isAdmin || can('mobile.sessions');
  const [status, setStatus] = useState<'' | RiderSession['status']>('active');
  const [iqama, setIqama] = useState('');
  const [rows, setRows] = useState<RiderSession[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [target, setTarget] = useState<Target | null>(null);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getRiderSessions({ status: status || undefined, iqama: iqama.trim() || undefined });
      setRows(Array.isArray(res) ? res : res.items);
    } catch (err: any) {
      setError(err.message || 'تعذر تحميل جلسات المناديب');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (allowed) load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [allowed, status]);

  const submitRevoke = async () => {
    if (!target) return;
    const why = reason.trim();
    if (!why) {
      toast('سبب الإلغاء مطلوب', 'warning');
      return;
    }
    setSaving(true);
    try {
      if (target.kind === 'one') {
        await api.revokeRiderSession(target.session.id, why);
        toast('تم إلغاء الجلسة', 'success');
      } else {
        const res = await api.revokeAllRiderSessions(target.iqama, why);
        toast(`تم إلغاء ${res.revoked} جلسة`, 'success');
      }
      setTarget(null);
      setReason('');
      await load();
    } catch (err: any) {
      toast(err.message || 'تعذر إلغاء الجلسة', 'danger');
    } finally {
      setSaving(false);
    }
  };

  if (!allowed) {
    return <EmptyState title="لا تملك صلاحية جلسات المناديب" description="تحتاج صلاحية mobile.sessions لعرض الجلسات وإلغائها." />;
  }

  const activeForIqama = iqama.trim() ? rows.filter((r) => r.iqama === iqama.trim() && r.status !== 'revoked') : [];

  return (
    <div className="space-y-4" data-testid="rider-sessions">
      <div className="flex flex-wrap items-end gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <label className="text-xs text-[var(--muted)] space-y-1">
          <span className="block">الحالة</span>
          <select
            id="sessions-status"
            value={status}
            onChange={(e) => setStatus(e.target.value as typeof status)}
            className="h-9 rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2 text-sm text-[var(--ink)]"
          >
            <option value="active">نشطة</option>
            <option value="expired">منتهية</option>
            <option value="revoked">ملغاة</option>
            <option value="">الكل</option>
          </select>
        </label>
        <label className="text-xs text-[var(--muted)] space-y-1">
          <span className="block">رقم الإقامة</span>
          <input
            id="sessions-iqama"
            value={iqama}
            onChange={(e) => setIqama(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && load()}
            inputMode="numeric"
            className="h-9 w-40 rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2 text-sm num text-[var(--ink)]"
          />
        </label>
        <button onClick={load} className="h-9 px-3 rounded-lg border border-[var(--line)] text-sm inline-flex items-center gap-1">
          <RefreshCw size={14} /> تحديث
        </button>
        {activeForIqama.length > 0 && (
          <button
            id="sessions-revoke-all"
            onClick={() => setTarget({ kind: 'all', iqama: iqama.trim(), name: activeForIqama[0].riderName })}
            className="h-9 px-3 rounded-lg bg-[var(--danger)] text-white text-sm inline-flex items-center gap-1"
          >
            <ShieldOff size={14} /> إلغاء كل جلسات المندوب
          </button>
        )}
      </div>

      {loading ? (
        <Skeleton />
      ) : error ? (
        <ErrorState message={error} onRetry={load} />
      ) : rows.length === 0 ? (
        <EmptyState title="لا توجد جلسات" description="لا توجد جلسات تطابق الفلتر الحالي." icon={<Smartphone size={20} />} />
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
          <table className="w-full text-sm">
            <thead className="text-xs text-[var(--muted)] bg-[var(--bg)]">
              <tr>
                <th className="p-3 text-start">المندوب</th>
                <th className="p-3 text-start">الجهاز</th>
                <th className="p-3 text-start">اللغة</th>
                <th className="p-3 text-start">بداية الجلسة</th>
                <th className="p-3 text-start">آخر استخدام</th>
                <th className="p-3 text-start">تنتهي</th>
                <th className="p-3 text-start">الحالة</th>
                <th className="p-3" />
              </tr>
            </thead>
            <tbody>
              {rows.map((s) => (
                <tr key={s.id} className="border-t border-[var(--line)]">
                  <td className="p-3">
                    <div className="font-bold text-[var(--ink)]">{s.riderName || '—'}</div>
                    <div className="num text-xs text-[var(--muted)]">{s.iqama}</div>
                  </td>
                  <td className="p-3 font-mono text-xs">
                    {s.deviceFingerprint || '—'}
                    {s.fcmRegistered && <span className="ms-1 text-[var(--muted)]">(FCM)</span>}
                  </td>
                  <td className="p-3 text-xs">{s.language}</td>
                  <td className="p-3 num text-xs whitespace-nowrap">{when(s.createdAt)}</td>
                  <td className="p-3 num text-xs whitespace-nowrap">{when(s.lastSeenAt)}</td>
                  <td className="p-3 num text-xs whitespace-nowrap">{when(s.expiresAtUtc)}</td>
                  <td className="p-3">
                    <span className={`px-2 py-0.5 rounded-full text-xs font-bold ${STATUS_CLASS[s.status]}`}>{STATUS_LABEL[s.status]}</span>
                  </td>
                  <td className="p-3">
                    {s.status !== 'revoked' && (
                      <button
                        data-session={s.id}
                        onClick={() => setTarget({ kind: 'one', session: s })}
                        className="text-xs text-[var(--danger)] font-bold"
                      >
                        إلغاء
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Dialog
        open={target !== null}
        onClose={() => !saving && setTarget(null)}
        title={target?.kind === 'all' ? 'إلغاء كل جلسات المندوب' : 'إلغاء جلسة'}
        description={
          target?.kind === 'all'
            ? `سيُطلب من ${target.name || target.iqama} تسجيل الدخول من جديد على كل الأجهزة.`
            : target
            ? `جهاز ${target.session.deviceFingerprint || 'غير معروف'} — ${target.session.riderName}`
            : ''
        }
        actions={
          <>
            <button onClick={() => setTarget(null)} disabled={saving} className="h-9 px-3 rounded-lg border border-[var(--line)] text-sm">
              تراجع
            </button>
            <button
              id="sessions-revoke-confirm"
              onClick={submitRevoke}
              disabled={saving || !reason.trim()}
              className="h-9 px-3 rounded-lg bg-[var(--danger)] text-white text-sm disabled:opacity-50"
            >
              تأكيد الإلغاء
            </button>
          </>
        }
      >
        <label className="block text-xs text-[var(--muted)] space-y-1">
          <span className="block">سبب الإلغاء (مطلوب، يُحفظ في سجل التدقيق)</span>
          <textarea
            id="sessions-revoke-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={500}
            rows={3}
            className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] p-2 text-sm text-[var(--ink)]"
          />
        </label>
      </Dialog>
    </div>
  );
}
