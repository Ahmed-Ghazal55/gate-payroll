'use client';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import {
  Users,
  Upload,
  ClipboardList,
  CheckCircle2,
  ArrowUpRight,
  ShieldAlert,
  Clock,
  Sparkles,
  Layers,
  FileSpreadsheet,
} from 'lucide-react';
import { api } from '../lib/api';
import type { DashboardMetrics } from '../lib/types';
import { PageHeader } from '../components/ui/PageHeader';
import { StatCard } from '../components/ui/StatCard';
import { DataTable, ColumnDef } from '../components/ui/DataTable';
import { ErrorState, Skeleton } from '../components/ui/EmptyState';

export default function DashboardPage() {
  const [data, setData] = useState<DashboardMetrics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchDashboard = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getDashboard();
      setData(res);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل بيانات لوحة القيادة');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDashboard();
  }, []);

  const auditColumns: ColumnDef<any>[] = [
    {
      key: 'action',
      header: 'العملية',
      render: (row) => (
        <span className="font-semibold text-[var(--ink)]">{row.action}</span>
      ),
    },
    {
      key: 'actor',
      header: 'المنفذ',
      render: (row) => (
        <span className="px-2 py-0.5 rounded-md bg-[var(--raised)] text-xs text-[var(--muted)]">
          {row.actor}
        </span>
      ),
    },
    {
      key: 'at',
      header: 'التوقيت',
      render: (row) => (
        <span className="num text-xs text-[var(--muted)]">
          {new Date(row.at).toLocaleString('ar-SA')}
        </span>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="لوحة القيادة الرئيسية"
        eyebrow="مساحة الإدارة 1.0"
        description="ابدأ ببيانات تشغيلية فعلية، راجع مصدر كل بند، ثم اعتمد المسير قبل مشاركته وترحيله للمالية."
        primaryAction={{
          label: 'استيراد بيانات تشغيل',
          onClick: () => {
            window.location.href = '/legacy/#/imports';
          },
          icon: <Upload size={16} />,
        }}
      />

      {error ? (
        <ErrorState
          title="تعذر تحميل مؤشرات النظام"
          message={error}
          onRetry={fetchDashboard}
        />
      ) : loading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <Skeleton className="h-32 rounded-xl" rows={1} />
          <Skeleton className="h-32 rounded-xl" rows={1} />
          <Skeleton className="h-32 rounded-xl" rows={1} />
          <Skeleton className="h-32 rounded-xl" rows={1} />
        </div>
      ) : data ? (
        <>
          {/* KPI Stat Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              label="المناديب المسجلون"
              value={data.riders}
              hint="هوية واحدة موحدة لكل مندوب"
              icon={Users}
              trend={{ value: 'نشط', isPositive: true }}
              onClick={() => {
                window.location.href = '/app/riders/';
              }}
            />

            <StatCard
              label="دفعات الاستيراد"
              value={data.imports}
              hint="مصادر محفوظة وقابلة للتتبع"
              icon={Upload}
              trend={{ value: 'مكتمل', isPositive: true }}
            />

            <StatCard
              label="ردود بانتظار المراجعة"
              value={data.pendingResponses}
              hint="لا تغير المستحقات إلا بعد اعتمادها"
              icon={ClipboardList}
              trend={{
                value: data.pendingResponses > 0 ? 'مطلوب إجراء' : 'محدث',
                isPositive: data.pendingResponses === 0,
              }}
              onClick={() => {
                window.location.href = '/app/forms/?tab=responses';
              }}
            />

            <StatCard
              label="كشوف الرواتب المعتمدة"
              value={data.approvedRuns}
              hint="اعتماد موثق وغير مكرر"
              icon={CheckCircle2}
              trend={{ value: 'معتمد', isPositive: true }}
              onClick={() => {
                window.location.href = '/app/payroll/';
              }}
            />
          </div>

          {/* Process Workflow Banner */}
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 sm:p-6 shadow-xs">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 pb-4 mb-4 border-b border-[var(--line)]">
              <div>
                <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                  من الفاتورة إلى كشف الحساب
                </h2>
                <p className="text-xs text-[var(--muted)]">
                  مسار دورة الرواتب والعمليات المحاسبية المعتمد
                </p>
              </div>
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-[var(--accent)]/10 text-[var(--accent)]">
                <Sparkles size={14} />
                مسار تدقيق موحد
              </span>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div className="p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
                <div className="flex items-center gap-2 mb-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs num">
                    1
                  </span>
                  <strong className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                    استيراد وربط
                  </strong>
                </div>
                <p className="text-xs text-[var(--muted)] leading-relaxed">
                  حدد إقامة المندوب الفعلي ومصادر المبالغ من ملفات التشغيل قبل الحفظ.
                </p>
              </div>

              <div className="p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
                <div className="flex items-center gap-2 mb-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs num">
                    2
                  </span>
                  <strong className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                    معاينة ومراجعة
                  </strong>
                </div>
                <p className="text-xs text-[var(--muted)] leading-relaxed">
                  سياسة سارية وبنود فعلية مع بيان أي بيانات ناقصة أو استقطاعات مديونية.
                </p>
              </div>

              <div className="p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
                <div className="flex items-center gap-2 mb-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs num">
                    3
                  </span>
                  <strong className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                    اعتماد وترحيل
                  </strong>
                </div>
                <p className="text-xs text-[var(--muted)] leading-relaxed">
                  كشف محفوظ ببصمة مصادره، ثم تسجيل دفعات الصرف والترحيل لدفتر الأستاذ.
                </p>
              </div>
            </div>
          </div>

          {/* Bottom Grid: Latest Audit + Forms Shortcut */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            <div className="lg:col-span-2 space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-sm sm:text-base font-bold text-[var(--ink)]">
                  آخر العمليات وسجل التدقيق
                </h3>
                <Link
                  href="/admin/"
                  className="text-xs font-semibold text-[var(--accent)] hover:underline"
                >
                  عرض كل السجل
                </Link>
              </div>

              <DataTable
                columns={auditColumns}
                data={data.latestAudit}
                emptyTitle="لا توجد عمليات مسجلة حديثًا"
                emptyDescription="ستظهر هنا العمليات المنفذة على المسيرات والمديونيات فور تسجيلها."
              />
            </div>

            <div className="space-y-4">
              <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
                <h3 className="text-sm font-bold text-[var(--ink)] mb-1">
                  نماذج واستبيانات المناديب
                </h3>
                <p className="text-xs text-[var(--muted)] mb-4 leading-relaxed">
                  اجمع بيانات الآيبان واختيارات نظام العمل برابط موثق، وراجع الردود في مكان موحد.
                </p>

                <div className="p-3 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs text-[var(--muted)] mb-4">
                  اختيار المندوب تفضيل للمراجعة؛ ولا يستبدل سياسة الراتب أو الاتفاق القائم دون اعتماد.
                </div>

                <div className="flex flex-col gap-2">
                  <Link
                    href="/forms/?tab=campaigns"
                    className="inline-flex items-center justify-center gap-2 px-4 py-2 text-xs font-bold text-[var(--accent-ink)] bg-[var(--accent)] rounded-lg hover:opacity-90 transition shadow-xs"
                  >
                    <span>إنشاء حملة نماذج جديدة</span>
                    <ArrowUpRight size={14} />
                  </Link>

                  <Link
                    href="/forms/?tab=responses"
                    className="inline-flex items-center justify-center gap-2 px-4 py-2 text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] rounded-lg border border-[var(--line)] transition"
                  >
                    <span>صندوق مراجعة الردود</span>
                    {data.pendingResponses > 0 && (
                      <span className="num font-bold px-1.5 py-0.2 rounded-full bg-[var(--danger)] text-white text-[10px]">
                        {data.pendingResponses}
                      </span>
                    )}
                  </Link>
                </div>
              </div>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
