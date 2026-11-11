'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw, ShieldAlert, Ban } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import type { ApprovalBypassGrant } from '../../lib/types';

export function ApprovalBypassPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const allowed = isAdmin || can('users.manage');
  const [rows, setRows] = useState<ApprovalBypassGrant[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [username, setUsername] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    if (!allowed) { setLoading(false); return; }
    setLoading(true); setError(null);
    api.listApprovalBypassGrants()
      .then((list) => setRows(Array.isArray(list) ? list : []))
      .catch((err: unknown) => setError(err instanceof ApiError ? err.message : 'تعذر تحميل منح التجاوز'))
      .finally(() => setLoading(false));
  }, [allowed]);

  useEffect(() => { load(); }, [load]);

  if (!allowed) {
    return <EmptyState title="صلاحية غير كافية" description="منح تجاوز الاعتماد متاح لمدير النظام أو من يملك users.manage." />;
  }

  const onGrant = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.grantApprovalBypass({ username: username.trim(), from, to, reason: reason.trim() });
      toast('تم منح تجاوز الاعتماد', 'success');
      setUsername(''); setReason('');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المنح', 'danger');
    } finally { setBusy(false); }
  };

  const onRevoke = async (id: string) => {
    setBusy(true);
    try {
      await api.revokeApprovalBypass(id);
      toast('تم سحب المنح', 'success');
      load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر السحب', 'danger');
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold text-[var(--ink)]">تجاوز اعتماد صانع/مراجع</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">منح مؤرخ مع سبب؛ يظهر شارة على شاشات الاعتماد حتى تاريخ النهاية أو السحب.</p>
        </div>
        <button type="button" onClick={load} className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-semibold">
          <RefreshCw size={14} /> تحديث
        </button>
      </div>

      <form onSubmit={onGrant} className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
        <label className="text-xs font-semibold space-y-1">
          <span>اسم المستخدم</span>
          <input required value={username} onChange={(e) => setUsername(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>من</span>
          <input required type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>إلى</span>
          <input required type="date" value={to} onChange={(e) => setTo(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <label className="text-xs font-semibold space-y-1 sm:col-span-2 lg:col-span-1">
          <span>السبب</span>
          <input required minLength={3} value={reason} onChange={(e) => setReason(e.target.value)} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <div className="flex items-end">
          <button type="submit" disabled={busy} className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            <ShieldAlert size={14} /> منح
          </button>
        </div>
      </form>

      {error ? <ErrorState title="تعذر التحميل" message={error} onRetry={load} /> : loading ? (
        <Skeleton className="h-32 w-full" />
      ) : rows.length === 0 ? (
        <EmptyState title="لا منح حالياً" description="لم يُمنح تجاوز اعتماد بعد." />
      ) : (
        <div className="rounded-2xl border border-[var(--line)] overflow-hidden bg-[var(--surface)]">
          <table className="w-full text-xs sm:text-sm">
            <thead className="bg-[var(--raised)]/50 text-[var(--muted)]">
              <tr>
                <th className="p-3 text-start">المستخدم</th>
                <th className="p-3 text-start">الفترة</th>
                <th className="p-3 text-start">السبب</th>
                <th className="p-3 text-start">الحالة</th>
                <th className="p-3 text-start">إجراء</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {rows.map((g) => (
                <tr key={g.id}>
                  <td className="p-3 font-mono">{g.username}</td>
                  <td className="p-3 font-mono">{g.from} → {g.to}</td>
                  <td className="p-3">{g.reason}</td>
                  <td className="p-3">{g.revokedAt ? `مسحوب (${g.revokedBy || '—'})` : 'ساري'}</td>
                  <td className="p-3">
                    {!g.revokedAt && (
                      <button type="button" disabled={busy} onClick={() => onRevoke(g.id)} className="inline-flex items-center gap-1 text-[var(--danger)] font-semibold disabled:opacity-50">
                        <Ban size={14} /> سحب
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
