'use client';

import React, { useCallback, useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';

const TABS: SectionTabItem[] = [
  { id: 'company', label: 'بيانات الشركة' },
  { id: 'notifications', label: 'إعدادات الإشعارات' },
  { id: 'periods', label: 'الفترات المالية' },
  { id: 'snapshots', label: 'جدولة اللقطات التلقائية' },
  { id: 'thresholds', label: 'عتبات التقارير والوسائط والأمان' },
];

type SettingItem = { key: string; value: unknown };

export function AdminSettingsPanel() {
  const { toast } = useToast();
  const [subtab, setSubtab] = useState('company');
  const [items, setItems] = useState<SettingItem[]>([]);
  const [allowed, setAllowed] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [currentPeriod, setCurrentPeriod] = useState('');
  const [totpEnforced, setTotpEnforced] = useState(false);
  const [replyWorkingDays, setReplyWorkingDays] = useState('3');
  const [confirmSave, setConfirmSave] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getAdminSettings();
      const list = Array.isArray((res as { items?: SettingItem[] }).items)
        ? ((res as { items: SettingItem[] }).items)
        : [];
      setItems(list);
      setAllowed(
        Array.isArray((res as { allowedActions?: string[] }).allowedActions)
          ? ((res as { allowedActions: string[] }).allowedActions)
          : []
      );
      const byKey = Object.fromEntries(list.map((i) => [i.key, i.value]));
      setCurrentPeriod(String((byKey.current_period as { period?: string })?.period ?? byKey.current_period ?? ''));
      setTotpEnforced(Boolean((byKey.totp_enforced as { enabled?: boolean })?.enabled ?? byKey.totp_enforced));
      setReplyWorkingDays(String((byKey.reply_working_days as { days?: number })?.days ?? byKey.reply_working_days ?? '3'));
      setDirty(false);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل إعدادات النظام');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (!dirty) return;
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

  const save = async () => {
    if (!allowed.includes('save_settings')) {
      toast('لا تملك صلاحية حفظ الإعدادات', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.putAdminSettings({
        items: [
          { key: 'current_period', value: { period: currentPeriod } },
          { key: 'totp_enforced', value: { enabled: totpEnforced } },
          { key: 'reply_working_days', value: { days: Number(replyWorkingDays) || 3 } },
        ],
      });
      toast('تم حفظ إعدادات النظام وتسجيل التغيير في سجل التدقيق', 'success');
      setConfirmSave(false);
      setDirty(false);
      await load();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر حفظ الإعدادات', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (loading) return <Skeleton rows={5} />;
  if (error) return <ErrorState message={error} onRetry={() => void load()} />;

  return (
    <div className="space-y-4">
      <SectionTabs tabs={TABS} activeTab={subtab} onChange={setSubtab} paramName="subtab" ariaLabel="تبويبات إعدادات النظام" />

      {subtab === 'company' && (
        <div className="space-y-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4">
          <p className="text-sm text-[var(--muted)]">العملة الافتراضية: ر.س · المنطقة الزمنية: آسيا/الرياض</p>
        </div>
      )}

      {subtab === 'notifications' && (
        <div className="space-y-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4">
          <label className="block text-xs font-semibold">مهلة رد الموظفين على المهام والطلبات (أيام عمل)</label>
          <p className="text-[11px] text-[var(--muted)]">
            بعد انتهاء المهلة يُرسل تذكير ثم تصعيد للمدير المسؤول. لا تخص المناديب ولا الرواتب.
          </p>
          <input
            className="w-full max-w-xs rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm num"
            value={replyWorkingDays}
            onChange={(e) => {
              setReplyWorkingDays(e.target.value);
              setDirty(true);
            }}
          />
        </div>
      )}

      {subtab === 'periods' && (
        <div className="space-y-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4">
          <label className="block text-xs font-semibold">الفترة المالية النشطة</label>
          <input
            className="w-full max-w-xs rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm num"
            value={currentPeriod}
            placeholder="2026-10"
            onChange={(e) => {
              setCurrentPeriod(e.target.value);
              setDirty(true);
            }}
          />
        </div>
      )}

      {subtab === 'snapshots' && (
        <EmptyState
          title="جدولة اللقطات"
          description="لقطة يومية لملخص تغييرات نظام الموارد وخزينة جاهز عند منتصف الليل، ولقطة تشغيل إقفال الشهر في اليوم الثاني الساعة ١٠ صباحًا."
        />
      )}

      {subtab === 'thresholds' && (
        <div className="space-y-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4">
          <p className="text-sm">حدود الوسائط: صورة ٥ ميجا · صوت ٢ ميجا وبحد أقصى ١٢٠ ثانية · الأرشيف ٣ سنوات.</p>
          <label className="inline-flex items-center gap-2 text-xs font-semibold">
            <input
              type="checkbox"
              checked={totpEnforced}
              onChange={(e) => {
                setTotpEnforced(e.target.checked);
                setDirty(true);
              }}
            />
            إلزام المصادقة الثنائية للأدوار المالية
          </label>
        </div>
      )}

      {items.length > 0 && (
        <p className="text-[11px] text-[var(--muted)]">عدد المفاتيح المحمّلة من الخادم: {items.length}</p>
      )}

      {allowed.includes('save_settings') && (
        <button
          type="button"
          className="rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] px-3 py-2 text-sm font-semibold disabled:opacity-50"
          disabled={!dirty}
          onClick={() => setConfirmSave(true)}
        >
          حفظ الإعدادات
        </button>
      )}

      <Dialog
        open={confirmSave}
        onClose={() => setConfirmSave(false)}
        title="تأكيد حفظ الإعدادات"
        description="تعديل هذه الإعدادات يؤثر فورًا على جميع المستخدمين والعمليات. هل تريد الحفظ؟"
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setConfirmSave(false)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-semibold disabled:opacity-50"
              disabled={busy}
              onClick={() => void save()}
            >
              حفظ
            </button>
          </>
        }
      >
        <p className="text-sm text-[var(--muted)]">سيُسجَّل التغيير في سجل التدقيق.</p>
      </Dialog>
    </div>
  );
}
