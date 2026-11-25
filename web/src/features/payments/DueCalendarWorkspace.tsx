'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Download, Banknote } from 'lucide-react';
import {
  api,
  ApiError,
  type PaymentDocumentItem,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { Dialog } from '../../components/ui/Dialog';
import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  type CoaAccountOption,
  DOCUMENT_STATUS_LABELS,
  DOCUMENT_TYPE_LABELS,
  exportPaymentsSheet,
  hasAllowedAction,
  loadPostableAccounts,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

type DueRow = Record<string, unknown> & {
  documentId?: string;
  companyId?: string;
  type?: string;
  reference?: string;
  dueOn?: string;
  amount?: number;
  status?: number;
  amountPaid?: number;
  amountReserved?: number;
  availableBalance?: number;
  allowedActions?: string[];
};

export function DueCalendarWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'week', label: 'مستحق هذا الأسبوع' },
      { id: 'month', label: 'مستحق هذا الشهر' },
      { id: 'calendar', label: 'التقويم الشهري' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('month');
  const [companyId, setCompanyId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [rows, setRows] = useState<DueRow[]>([]);
  const [amountDue, setAmountDue] = useState(0);
  const [amountReserved, setAmountReserved] = useState(0);
  const [selectedDoc, setSelectedDoc] = useState<PaymentDocumentItem | null>(null);

  const range = useMemo(() => {
    if (activeTab === 'week') {
      const start = new Date();
      const end = new Date(start);
      end.setUTCDate(end.getUTCDate() + 7);
      return {
        from: start.toISOString().slice(0, 10),
        to: end.toISOString().slice(0, 10),
      };
    }
    return periodDateRange(period);
  }, [activeTab, period]);

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [compList, coaRes, calRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.getPaymentDueCalendar({
          companyId: companyId || undefined,
          from: range.from,
          to: range.to,
        }),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setRows((calRes?.items as unknown as DueRow[]) || []);
      setAmountDue(Number(calRes?.amountDue || 0));
      setAmountReserved(Number(calRes?.amountReserved || 0));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل لوحة المستحقات');
    } finally {
      setLoading(false);
    }
  }, [canView, companyId, range.from, range.to]);

  useEffect(() => {
    void load();
  }, [load]);

  const companyOptions = useMemo(
    () => [
      { value: '', label: 'كل الشركات' },
      ...companies.map((c) => ({ value: String(c.id || c.code || ''), label: companyLabel(c) })),
    ],
    [companies]
  );

  const companyName = useCallback(
    (id?: string) => {
      const m = companies.find((c) => c.id === id || c.code === id);
      return m ? companyLabel(m) : id || '—';
    },
    [companies]
  );

  const openDocumentActions = async (documentId?: string) => {
    if (!documentId) return;
    setBusy(true);
    try {
      const doc = await api.getPaymentDocument(documentId);
      setSelectedDoc(doc);
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تحميل تفاصيل المستند', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['تاريخ الاستحقاق', 'النوع', 'الشركة', 'المرجع', 'المبلغ', 'المحجوز', 'المتاح', 'الحالة'],
        rows.map((r) => [
          String(r.dueOn || '').slice(0, 10),
          DOCUMENT_TYPE_LABELS[String(r.type || '')] || String(r.type || ''),
          companyName(r.companyId),
          String(r.reference || ''),
          Number(r.amount || 0),
          Number(r.amountReserved || 0),
          Number(r.availableBalance || 0),
          DOCUMENT_STATUS_LABELS[Number(r.status || 1)] || '',
        ]),
        [4, 5, 6],
        `due-calendar-${period}`
      );
      toast('تم تصدير لوحة المستحقات', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const dayGroups = useMemo(() => {
    const map = new Map<string, { day: string; count: number; total: number }>();
    for (const r of rows) {
      const day = String(r.dueOn || '').slice(0, 10) || 'غير محدد';
      const cur = map.get(day) || { day, count: 0, total: 0 };
      cur.count += 1;
      cur.total += Number(r.availableBalance ?? 0);
      map.set(day, cur);
    }
    return Array.from(map.values()).sort((a, b) => a.day.localeCompare(b.day));
  }, [rows]);

  const columns: ColumnDef<DueRow>[] = [
    { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
    {
      key: 'type',
      header: 'النوع',
      type: 'list',
      getValue: (r) => DOCUMENT_TYPE_LABELS[String(r.type || '')] || String(r.type || ''),
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'reference', header: 'المرجع', type: 'text', getValue: (r) => String(r.reference || '') },
    {
      key: 'amount',
      header: 'المبلغ',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.amount || 0),
      render: (r) => <span className="num">{formatSar(Number(r.amount || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'availableBalance',
      header: 'المتاح للدفع',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.availableBalance || 0),
      render: (r) => <span className="num font-bold">{formatSar(Number(r.availableBalance || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => DOCUMENT_STATUS_LABELS[Number(r.status || 1)] || '—',
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) =>
        hasAllowedAction(r, 'create_payment_request') ? (
          <button
            type="button"
            disabled={busy}
            onClick={() => void openDocumentActions(r.documentId)}
            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-[11px] font-bold cursor-pointer"
          >
            <Banknote size={12} aria-hidden="true" />
            <span>إنشاء طلب دفع</span>
          </button>
        ) : (
          <span className="text-[11px] text-[var(--muted)]">—</span>
        ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض لوحة المستحقات يحتاج صلاحية عرض المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="لوحة المستحقات"
        description={`الالتزامات المستحقة من الإيجارات والأقساط والموردين والرسوم للفترة ${range.from} إلى ${range.to}.`}
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <div className="flex flex-wrap items-center justify-between gap-3">
        <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />
        <div className="w-60">
          <SearchableSelect
            value={companyId}
            onChange={setCompanyId}
            options={companyOptions}
            placeholder="تصفية حسب الشركة..."
            clearable
            size="sm"
          />
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="إجمالي المستحق" value={formatSar(amountDue)} />
        <StatCard label="المحجوز للصرف" value={formatSar(amountReserved)} />
        <StatCard label="المتاح لطلب الدفع" value={formatSar(Math.max(0, amountDue - amountReserved))} />
        <StatCard label="عدد البنود المستحقة" value={rows.length} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر تحميل المستحقات" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'calendar' && dayGroups.length > 0 && (
            <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-5">
              {dayGroups.map((g) => (
                <div
                  key={g.day}
                  className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-3 space-y-1"
                >
                  <div className="text-xs font-bold text-[var(--ink)]">{g.day}</div>
                  <div className="text-[11px] text-[var(--muted)]">{g.count} بنود مستحقة</div>
                  <div className="num text-xs font-bold text-[var(--accent)]">{formatSar(g.total)}</div>
                </div>
              ))}
            </div>
          )}
          <DataTable
            columns={columns}
            data={rows}
            emptyTitle="لا توجد مستحقات في هذا النطاق"
            emptyDescription="غيّر الفترة أو الشركة لعرض الالتزامات المستحقة."
          />
        </>
      )}

      <Dialog
        open={selectedDoc !== null}
        onClose={() => setSelectedDoc(null)}
        title={`إجراءات المستند ${selectedDoc?.docNo || ''}`}
        description={selectedDoc ? `المبلغ ${formatSar(selectedDoc.amount)} — المتاح ${formatSar(selectedDoc.availableBalance ?? 0)}` : ''}
        size="md"
      >
        {selectedDoc && (
          <PaymentDocumentActions
            doc={selectedDoc}
            period={period}
            accounts={accounts}
            onUpdated={async () => {
              await load();
              setSelectedDoc(null);
            }}
          />
        )}
      </Dialog>
    </div>
  );
}
