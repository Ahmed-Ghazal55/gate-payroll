'use client';

// App settings (Admin only, like the firebase-config endpoint). Shows only what the server returns:
// nothing configured → «غير مهيأ». The service-account key is never shown or stored from the UI.
import React, { useEffect, useState } from 'react';
import { ShieldCheck } from 'lucide-react';
import { api, type FirebaseConfig } from '../../lib/api';
import { StatCard } from '../../components/ui/StatCard';
import { ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';

export function SettingsPanel({ isAdmin }: { isAdmin: boolean }) {
  const { toast } = useToast();
  const [cfg, setCfg] = useState<FirebaseConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({ projectId: '', senderId: '', androidPackageName: '', googlePlayUrl: '' });
  const [saving, setSaving] = useState(false);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getFirebaseConfig();
      setCfg(res);
      setForm({
        projectId: res.configured ? res.projectId ?? '' : '',
        senderId: res.senderId ?? '',
        androidPackageName: res.configured ? res.androidPackageName ?? '' : '',
        googlePlayUrl: res.googlePlayUrl ?? '',
      });
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل إعدادات التطبيق');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (isAdmin) load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin]);

  const save = async () => {
    setSaving(true);
    try {
      await api.saveFirebaseConfig(form);
      toast('تم حفظ إعدادات التطبيق', 'success');
      load();
    } catch (err: any) {
      toast(err?.message || 'تعذر الحفظ', 'danger');
    } finally {
      setSaving(false);
    }
  };

  if (!isAdmin) return <ErrorState title="لا تملك صلاحية" message="إعدادات التطبيق لمدير النظام فقط." />;
  if (loading) return <Skeleton className="h-40 w-full rounded-2xl" />;
  if (error || !cfg) return <ErrorState title="تعذر التحميل" message={error || ''} onRetry={load} />;

  const input = (key: keyof typeof form, label: string) => (
    <label className="space-y-1 block">
      <span className="text-xs font-bold">{label}</span>
      <input
        dir="ltr"
        value={form[key]}
        onChange={(e) => setForm({ ...form, [key]: e.target.value })}
        className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono"
      />
    </label>
  );

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatCard label="جلسات التطبيق المسجلة" value={cfg.activeSessionsCount} />
        <StatCard label="أجهزة مسجلة للإشعارات" value={cfg.fcmRegisteredDevicesCount} />
        <StatCard label="إشعارات بانتظار الإرسال" value={cfg.outboxPendingCount} />
        <StatCard label="إشعارات فشل إرسالها" value={cfg.outboxFailedCount} />
      </div>
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4 max-w-xl">
        <div className="flex items-center justify-between">
          <h3 className="font-bold text-sm">ربط خدمة إشعارات التطبيق</h3>
          <span className="text-xs font-bold">{cfg.configured ? 'مهيأ' : 'غير مهيأ'}</span>
        </div>
        {input('projectId', 'معرّف المشروع')}
        {input('senderId', 'معرّف المرسل')}
        {input('androidPackageName', 'معرّف حزمة أندرويد')}
        {input('googlePlayUrl', 'رابط المتجر')}
        <div className="p-3 rounded-xl bg-[var(--raised)] text-[11px] text-[var(--muted)] flex items-center gap-2">
          <ShieldCheck size={16} className="text-[var(--accent)] shrink-0" />
          <span>
            مفتاح حساب الخدمة: {cfg.hasServiceAccountJson ? 'مضبوط على الخادم' : 'غير مضبوط'} — يُضبط على الخادم فقط ولا يُعرض أو يُحفظ من هذه الشاشة.
          </span>
        </div>
        <p className="text-[11px] text-[var(--muted)]">لغات التطبيق: العربية، الإنجليزية، الأردية — يختارها المندوب من التطبيق.</p>
        <button
          type="button"
          disabled={saving || !form.projectId.trim()}
          onClick={save}
          className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
        >
          {saving ? 'جاري الحفظ…' : 'حفظ'}
        </button>
      </div>
    </div>
  );
}
