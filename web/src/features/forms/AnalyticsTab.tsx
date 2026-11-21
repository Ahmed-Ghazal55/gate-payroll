'use client';

import React, { useMemo } from 'react';
import { BarChart3, CheckCircle2, Layers, Clock } from 'lucide-react';
import type { Submission, Campaign } from '../../lib/types';
import { StatCard } from '../../components/ui/StatCard';
import { useAuth } from '../../lib/auth-context';

export interface AnalyticsTabProps {
  submissions: Submission[];
  campaigns: Campaign[];
}

function workLabel(ws?: string) {
  if (ws === 'tiers') return 'نظام الراتب الجديد';
  if (ws === 'commission') return 'نظام العمولة السابق';
  return ws || 'غير محدد';
}

function pct(part: number, total: number) {
  return total === 0 ? 0 : Math.round((100 * part) / total);
}

export function AnalyticsTab({ submissions, campaigns }: AnalyticsTabProps) {
  const { can, isAdmin, user } = useAuth();
  const sponsorIds = user?.financialScope?.sponsorIds;
  const noCompanyScope =
    !isAdmin &&
    !can('forms.responses.allCompanies') &&
    !(Array.isArray(sponsorIds) && sponsorIds.length > 0);
  const emptyScopeHint = 'لا توجد شركات ضمن نطاقك';
  const approvedCount = submissions.filter((s) => s.status === 'approved').length;
  const approvalRate = pct(approvedCount, submissions.length);
  const activeCampaignsCount = campaigns.filter((c) => c.status === 'open').length;

  const byWork = useMemo(() => {
    const map = new Map<string, number>();
    for (const s of submissions) {
      const key = workLabel(s.workSystem);
      map.set(key, (map.get(key) || 0) + 1);
    }
    return [...map.entries()].sort((a, b) => b[1] - a[1]);
  }, [submissions]);

  const byCity = useMemo(() => {
    const map = new Map<string, number>();
    for (const s of submissions) {
      const key = (s.city || '').trim() || 'غير محدد';
      map.set(key, (map.get(key) || 0) + 1);
    }
    return [...map.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8);
  }, [submissions]);

  const reviewed = submissions.filter((s) => s.status === 'approved' || s.status === 'rejected');
  let avgHours = 0;
  if (reviewed.length > 0) {
    const hours = reviewed
      .map((s) => {
        const start = s.createdAt ? Date.parse(s.createdAt) : NaN;
        const end = s.reviewedAt ? Date.parse(s.reviewedAt) : (s.updatedAt ? Date.parse(s.updatedAt) : NaN);
        if (Number.isNaN(start) || Number.isNaN(end) || end < start) return null;
        return (end - start) / 3600000;
      })
      .filter((h): h is number => h != null);
    if (hours.length) avgHours = Math.round((hours.reduce((a, b) => a + b, 0) / hours.length) * 10) / 10;
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard label="إجمالي الردود المستلمة" value={submissions.length} hint="عبر كل الحملات المنشورة" icon={BarChart3} />
        <StatCard label="نسبة الاعتماد" value={`${approvalRate}%`} hint="ردود تم توثيقها واعتمادها" icon={CheckCircle2} />
        <StatCard label="حملات نشطة" value={activeCampaignsCount} hint="مفتوحة لاستقبال الإدخال" icon={Layers} />
        <StatCard
          label="متوسط زمن المراجعة"
          value={avgHours > 0 ? `${avgHours} ساعة` : '—'}
          hint="من وقت الإرسال حتى الاعتماد أو الرفض"
          icon={Clock}
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
          <h3 className="text-base font-bold text-[var(--ink)] mb-3">توزيع الردود حسب نظام العمل</h3>
          <div className="space-y-3">
            {byWork.length === 0 ? (
              <p className="text-xs text-[var(--muted)]">{noCompanyScope ? emptyScopeHint : 'لا توجد ردود بعد.'}</p>
            ) : (
              byWork.map(([label, count]) => (
                <div key={label} className="space-y-1">
                  <div className="flex items-center justify-between text-xs">
                    <span>{label}</span>
                    <span className="num font-bold">{pct(count, submissions.length)}%</span>
                  </div>
                  <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                    <div className="bg-[var(--accent)] h-full" style={{ width: `${pct(count, submissions.length)}%` }} />
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
          <h3 className="text-base font-bold text-[var(--ink)] mb-3">توزيع الردود حسب المدن</h3>
          <div className="space-y-3">
            {byCity.length === 0 ? (
              <p className="text-xs text-[var(--muted)]">{noCompanyScope ? emptyScopeHint : 'لا توجد مدن مسجّلة في الردود.'}</p>
            ) : (
              byCity.map(([label, count]) => (
                <div key={label} className="space-y-1">
                  <div className="flex items-center justify-between text-xs">
                    <span>{label}</span>
                    <span className="num font-bold">{pct(count, submissions.length)}%</span>
                  </div>
                  <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                    <div className="bg-[var(--money-pos)] h-full" style={{ width: `${pct(count, submissions.length)}%` }} />
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
