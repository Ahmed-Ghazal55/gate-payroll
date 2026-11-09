'use client';

import React, { useState } from 'react';
import { Search, AlertTriangle } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState } from '../../components/ui/EmptyState';
import type { ValidateGapsReport } from '../../lib/types';

const COUNT_LABELS: Record<string, string> = {
  accounts_total: 'إجمالي الحسابات',
  accounts_with_one_operator: 'حساب بمندوب واحد',
  accounts_no_assignment_overlap: 'بدون تداخل تعيين',
  accounts_multi_operator: 'حساب متعدد المناديب',
  riders_missing_record: 'مندوب بلا سجل',
  riders_missing_category: 'فئة أجر ناقصة',
  riders_invalid_category: 'فئة أجر غير صالحة',
  ninja_daily_rows: 'صفوف يومية نينجا',
  ninja_daily_unmatched_account: 'يومي بلا حساب',
  ninja_daily_no_assignment_on_date: 'يومي بلا تعيين في التاريخ',
};

export function ValidateGapsPanel({ defaultCompanyId = '', defaultPeriod = '2026-10' }: { defaultCompanyId?: string; defaultPeriod?: string }) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const [companyId, setCompanyId] = useState(defaultCompanyId || 'BW');
  const [period, setPeriod] = useState(defaultPeriod);
  const [app, setApp] = useState('ninja');
  const [report, setReport] = useState<ValidateGapsReport | null>(null);
  const [loading, setLoading] = useState(false);

  if (!can('pay.view') && !isAdmin) {
    return <EmptyState title="صلاحية غير كافية" description="يلزم pay.view لتشخيص فجوات التحقق." />;
  }

  const run = async () => {
    setLoading(true);
    try {
      const res = await api.getValidateGaps({ companyId, period, app });
      setReport(res);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التشخيص', 'danger');
      setReport(null);
    } finally { setLoading(false); }
  };

  const counts = report?.counts || {};
  const blockers = ['accounts_no_assignment_overlap', 'riders_missing_category', 'riders_invalid_category', 'riders_missing_record']
    .map((k) => ({ key: k, n: Number(counts[k] || 0) }))
    .filter((x) => x.n > 0);

  return (
    <div className="space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <div>
        <h3 className="text-sm font-bold">فجوات ValidateRiders (أعداد فقط)</h3>
        <p className="text-xs text-[var(--muted)] mt-0.5">لا يعرض هويات. استخدمه لإصلاح التعيينات والفئات قبل تجاوز المرحلة 2.</p>
      </div>
      <div className="flex flex-wrap gap-3 items-end">
        <label className="text-xs font-semibold space-y-1">
          <span>الشركة</span>
          <input value={companyId} onChange={(e) => setCompanyId(e.target.value)} className="block p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>الشهر</span>
          <input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} className="block p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]" />
        </label>
        <label className="text-xs font-semibold space-y-1">
          <span>التطبيق</span>
          <select value={app} onChange={(e) => setApp(e.target.value)} className="block p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)]">
            <option value="ninja">نينجا</option>
            <option value="keeta">كيتا</option>
            <option value="chefz">شيفز</option>
          </select>
        </label>
        <button type="button" disabled={loading} onClick={run} className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
          <Search size={14} /> {loading ? 'جاري…' : 'تشخيص'}
        </button>
      </div>

      {report && (
        <div className="space-y-3">
          {blockers.length > 0 && (
            <div className="flex items-start gap-2 p-3 rounded-xl border border-[var(--warning)]/40 bg-[var(--warning)]/10 text-xs">
              <AlertTriangle size={16} className="text-[var(--warning)] shrink-0 mt-0.5" />
              <div>
                <p className="font-bold text-[var(--warning)]">يحتاج إصلاحًا في الواجهة قبل المرحلة 3</p>
                <ul className="mt-1 space-y-0.5 text-[var(--ink)]">
                  {blockers.map((b) => (
                    <li key={b.key}>{COUNT_LABELS[b.key] || b.key}: <span className="font-mono font-bold">{b.n}</span></li>
                  ))}
                </ul>
              </div>
            </div>
          )}
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2">
            {Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => (
              <div key={k} className="p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40">
                <div className="text-[11px] text-[var(--muted)]">{COUNT_LABELS[k] || k}</div>
                <div className="text-lg font-bold font-mono mt-1">{v}</div>
              </div>
            ))}
          </div>
          {report.note && <p className="text-[11px] text-[var(--muted)]">{report.note}</p>}
        </div>
      )}
    </div>
  );
}
