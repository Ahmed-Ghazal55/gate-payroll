'use client';

import React from 'react';
import { BarChart3, CheckCircle2, Layers, Clock } from 'lucide-react';
import type { Submission, Campaign } from '../../lib/types';
import { StatCard } from '../../components/ui/StatCard';

export interface AnalyticsTabProps {
  submissions: Submission[];
  campaigns: Campaign[];
}

export function AnalyticsTab({ submissions, campaigns }: AnalyticsTabProps) {
  const approvedCount = submissions.filter((s) => s.status === 'approved').length;
  const approvalRate = submissions.length > 0 ? Math.round((approvedCount / submissions.length) * 100) : 0;
  const activeCampaignsCount = campaigns.filter((c) => c.status === 'open').length;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="إجمالي الردود المستلمة"
          value={submissions.length}
          hint="عبر كل الحملات المنشورة"
          icon={BarChart3}
        />
        <StatCard
          label="نسبة الاعتماد"
          value={`${approvalRate}%`}
          hint="ردود تم توثيقها واعتمادها"
          icon={CheckCircle2}
          trend={{ value: 'مستقر', isPositive: true }}
        />
        <StatCard
          label="حملات نشطة"
          value={activeCampaignsCount}
          hint="مفتوحة لاستقبال الإدخال"
          icon={Layers}
        />
        <StatCard
          label="متوسط زمن المراجعة"
          value="2.4 ساعة"
          hint="من وقت الإرسال حتى الاعتماد"
          icon={Clock}
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
          <h3 className="text-base font-bold text-[var(--ink)] mb-3">
            توزيع الردود حسب نظام العمل
          </h3>
          <div className="space-y-3">
            <div className="flex items-center justify-between text-xs">
              <span>راتب شهري ثابت</span>
              <span className="num font-bold">58%</span>
            </div>
            <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
              <div className="bg-[var(--accent)] h-full w-[58%]" />
            </div>

            <div className="flex items-center justify-between text-xs">
              <span>محاسبة بالطلب / عمولة</span>
              <span className="num font-bold">42%</span>
            </div>
            <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
              <div className="bg-[var(--info)] h-full w-[42%]" />
            </div>
          </div>
        </div>

        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
          <h3 className="text-base font-bold text-[var(--ink)] mb-3">
            توزيع الردود حسب المدن
          </h3>
          <div className="space-y-3">
            <div className="flex items-center justify-between text-xs">
              <span>الرياض</span>
              <span className="num font-bold">64%</span>
            </div>
            <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
              <div className="bg-[var(--money-pos)] h-full w-[64%]" />
            </div>

            <div className="flex items-center justify-between text-xs">
              <span>جدة والمنطقة الغربية</span>
              <span className="num font-bold">24%</span>
            </div>
            <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
              <div className="bg-[var(--warning)] h-full w-[24%]" />
            </div>

            <div className="flex items-center justify-between text-xs">
              <span>الدمام والمنطقة الشرقية</span>
              <span className="num font-bold">12%</span>
            </div>
            <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
              <div className="bg-[var(--muted)] h-full w-[12%]" />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
