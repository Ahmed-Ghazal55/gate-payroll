'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';

export function NotifySettingsPanel() {
  const { toast } = useToast();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [allowed, setAllowed] = useState<string[]>([]);
  const [replyWorkingDays, setReplyWorkingDays] = useState('3');
  const [channels, setChannels] = useState('in_app,push');
  const [matrixJson, setMatrixJson] = useState('[]');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getAdminNotifySettings();
      setAllowed(Array.isArray(res.allowedActions) ? (res.allowedActions as string[]) : []);
      setReplyWorkingDays(String(res.replyWorkingDays ?? '3'));
      const ch = Array.isArray(res.channels) ? (res.channels as string[]) : ['in_app', 'push'];
      setChannels(ch.join(','));
      setMatrixJson(JSON.stringify(res.matrix ?? [], null, 2));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل إعدادات الإشعارات');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    if (!allowed.includes('save_notify_settings')) {
      toast('لا تملك صلاحية حفظ إعدادات الإشعارات', 'warning');
      return;
    }
    let matrix: unknown = [];
    try {
      matrix = JSON.parse(matrixJson || '[]');
    } catch {
      toast('مصفوفة التنبيهات يجب أن تكون JSON صالح', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.putAdminNotifySettings({
        replyWorkingDays: Number(replyWorkingDays),
        channels: channels
          .split(',')
          .map((s) => s.trim())
          .filter(Boolean),
        matrix,
      });
      toast('تم حفظ إعدادات الإشعارات', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الحفظ', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <Skeleton className="h-40 w-full" />;
  if (error) return <ErrorState title="إعدادات الإشعارات" message={error} onRetry={load} />;

  return (
    <div className="space-y-4" data-testid="notify-settings-panel">
      <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
        <div className="flex items-center justify-between gap-2">
          <h3 className="text-sm font-bold text-[var(--ink)]">إعدادات الإشعارات والمهام</h3>
          <button type="button" onClick={() => void load()} className="inline-flex items-center gap-1 text-xs font-semibold">
            <RefreshCw size={14} /> تحديث
          </button>
        </div>
        <p className="text-xs text-[var(--muted)]">
          المحادثة لا تنشئ إشعارات. مهلة الرد تُحسب بأيام العمل فقط.
        </p>
        <label className="block text-xs font-bold">
          مهلة الرد (أيام عمل)
          <input
            type="number"
            min={1}
            max={30}
            value={replyWorkingDays}
            onChange={(e) => setReplyWorkingDays(e.target.value)}
            className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
        </label>
        <label className="block text-xs font-bold">
          القنوات (مفصولة بفاصلة)
          <input
            value={channels}
            onChange={(e) => setChannels(e.target.value)}
            className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
        </label>
        <label className="block text-xs font-bold">
          مصفوفة التنبيهات (JSON)
          <textarea
            value={matrixJson}
            onChange={(e) => setMatrixJson(e.target.value)}
            rows={8}
            className="mt-1 w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono"
          />
        </label>
        {allowed.includes('save_notify_settings') ? (
          <button
            type="button"
            disabled={busy}
            onClick={() => void save()}
            className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
          >
            حفظ الإعدادات
          </button>
        ) : (
          <EmptyState title="عرض فقط" description="لا توجد صلاحية save_notify_settings." />
        )}
      </div>
    </div>
  );
}
