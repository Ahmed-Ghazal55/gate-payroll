'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { RefreshCw, Info, Plus } from 'lucide-react';
import { api, ApiError, type PaymentDocumentItem } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  type CoaAccountOption,
  documentMeta,
  documentStatusLabel,
  loadPostableAccounts,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

type SupervisorRow = {
  supervisor: string;
  count: number;
  totalAmount: number;
  paidAmount: number;
  pendingAmount: number;
};

export function HsCustodyWorkspace() {
  const { can, isAdmin } = useAuth();
  const { period } = usePeriod();
  const canView = can('payments.view') || can('advances.request') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'events', label: 'أحداث العهدة والشحن' },
      { id: 'supervisors', label: 'ملخص حسب المشرف' },
      { id: 'reconcile', label: 'فحص التوازن وإغلاق الشهر' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('events');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [docs, setDocs] = useState<PaymentDocumentItem[]>([]);

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [compList, coaRes, res] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listPaymentDocuments({ type: 'hs_topup' }),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setDocs(res?.items || []);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل عهد وشحن هنقرستيشن');
    } finally {
      setLoading(false);
    }
  }, [canView]);

  useEffect(() => {
    void load();
  }, [load]);

  const companyName = useCallback(
    (id?: string) => {
      const m = companies.find((c) => c.id === id || c.code === id);
      return m ? companyLabel(m) : id || '—';
    },
    [companies]
  );

  const periodDocs = useMemo(
    () => docs.filter((d) => String(d.periodMonth || d.occurredOn || '').startsWith(period) && d.status !== 5),
    [docs, period]
  );

  const supervisorRows: SupervisorRow[] = useMemo(() => {
    const map = new Map<string, SupervisorRow>();
    for (const d of periodDocs) {
      const sup = String((d as unknown as Record<string, unknown>).createdBy || 'غير محدد');
      const cur = map.get(sup) || {
        supervisor: sup,
        count: 0,
        totalAmount: 0,
        paidAmount: 0,
        pendingAmount: 0,
      };
      cur.count += 1;
      cur.totalAmount += Number(d.amount || 0);
      cur.paidAmount += Number(d.amountPaid || 0);
      cur.pendingAmount += Number(d.availableBalance ?? 0);
      map.set(sup, cur);
    }
    return Array.from(map.values());
  }, [periodDocs]);

  const totalAmount = periodDocs.reduce((s, d) => s + Number(d.amount || 0), 0);
  const totalPaid = periodDocs.reduce((s, d) => s + Number(d.amountPaid || 0), 0);
  const totalPending = Math.max(0, totalAmount - totalPaid);

  const columns: ColumnDef<PaymentDocumentItem>[] = [
    { key: 'occurredOn', header: 'التاريخ', type: 'date', getValue: (r) => String(r.occurredOn || '').slice(0, 10) },
    {
      key: 'docNo',
      header: 'رقم المستند',
      type: 'text',
      getValue: (r) => String(r.docNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.docNo || r.id.slice(0, 8)}</bdi>,
    },
    {
      key: 'riderIqama',
      header: 'إقامة المندوب',
      type: 'text',
      getValue: (r) => String(documentMeta(r).riderIqama || ''),
      render: (r) => <bdi className="font-mono text-xs">{String(documentMeta(r).riderIqama || '—')}</bdi>,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'amount',
      header: 'المبلغ',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.amount || 0),
      render: (r) => <span className="num font-bold">{formatSar(r.amount)}</span>,
      summary: 'sum',
    },
    { key: 'status', header: 'الحالة', type: 'list', getValue: (r) => documentStatusLabel(r) },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => <PaymentDocumentActions doc={r} period={period} accounts={accounts} onUpdated={load} />,
    },
  ];

  const supColumns: ColumnDef<SupervisorRow>[] = [
    { key: 'supervisor', header: 'المشرف / المنشئ', type: 'text', getValue: (r) => r.supervisor },
    { key: 'count', header: 'عدد العمليات', type: 'number', getValue: (r) => r.count, summary: 'sum' },
    {
      key: 'totalAmount',
      header: 'إجمالي الشحن',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => r.totalAmount,
      render: (r) => <span className="num font-bold">{formatSar(r.totalAmount)}</span>,
      summary: 'sum',
    },
    {
      key: 'paidAmount',
      header: 'المصروف والمرحّل',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => r.paidAmount,
      render: (r) => <span className="num">{formatSar(r.paidAmount)}</span>,
      summary: 'sum',
    },
    {
      key: 'pendingAmount',
      header: 'بانتظار الصرف',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => r.pendingAmount,
      render: (r) => <span className="num">{formatSar(r.pendingAmount)}</span>,
      summary: 'sum',
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض عهد هنقرستيشن يحتاج صلاحية المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="عهد هنقرستيشن"
        description="متابعة مستندات شحن محافظ هنقرستيشن وتوزيعها حسب المشرف وفحص توازن الفترة."
        moreActions={[
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading },
        ]}
      />

      <div className="rounded-2xl border border-[var(--accent)]/30 bg-[var(--accent)]/5 p-3.5 flex flex-wrap items-center justify-between gap-3 text-xs">
        <div className="flex items-start gap-2.5">
          <Info size={16} className="text-[var(--accent)] shrink-0 mt-0.5" aria-hidden="true" />
          <span>
            التسجيل حاليًا عبر مستندات شحن هنقرستيشن على الحساب المعتمد (112506) لحين تفعيل ربط أحداث عهد المشرفين المباشر من نظام الموارد.
          </span>
        </div>
        <Link
          href={`/payments/advances?tab=hs_topups&period=${encodeURIComponent(period)}`}
          className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold"
        >
          <Plus size={13} aria-hidden="true" />
          <span>طلب شحن هنقرستيشن</span>
        </Link>
      </div>

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label={`شحنات فترة ${period}`} value={periodDocs.length} />
        <StatCard label="إجمالي الشحن (112506)" value={formatSar(totalAmount)} />
        <StatCard label="المصروف والمرحّل" value={formatSar(totalPaid)} />
        <StatCard label="المتبقي غير المصروف" value={formatSar(totalPending)} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'events' && (
            <DataTable
              columns={columns}
              data={docs}
              emptyTitle="لا توجد عمليات شحن هنقرستيشن"
              emptyDescription="تظهر هنا مستندات شحن محفظة هنقرستيشن المعتمدة والمسودات."
            />
          )}
          {activeTab === 'supervisors' && (
            <DataTable
              columns={supColumns}
              data={supervisorRows}
              emptyTitle="لا توجد بيانات مشرفين في هذه الفترة"
              emptyDescription="اختر فترة أخرى أو أنشئ طلبات شحن جديدة."
            />
          )}
          {activeTab === 'reconcile' && (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-3 text-xs">
              <div className="font-bold text-sm text-[var(--ink)]">ملخص فحص توازن عهدة فترة {period}</div>
              <div className="grid gap-3 sm:grid-cols-3">
                <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3">
                  <div className="text-[var(--muted)]">إجمالي مستندات الشحن</div>
                  <div className="num text-base font-bold mt-1">{formatSar(totalAmount)}</div>
                </div>
                <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3">
                  <div className="text-[var(--muted)]">المصروف فعليًا بسند صرف</div>
                  <div className="num text-base font-bold mt-1">{formatSar(totalPaid)}</div>
                </div>
                <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3">
                  <div className="text-[var(--muted)]">فارق التوازن (غير مصروف)</div>
                  <div className="num text-base font-bold mt-1">{formatSar(totalPending)}</div>
                </div>
              </div>
              <p className="text-[var(--muted)]">
                إغلاق شهر عهد المشرفين الآلي وربط سلفة الإقفال بانتظار اعتماد عقد رصيد المندوب وحساب المنصة والتسويات السابقة.
              </p>
            </div>
          )}
        </>
      )}
    </div>
  );
}
