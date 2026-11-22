'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Download } from 'lucide-react';
import { api, ApiError, type PaymentDocumentItem } from '../../lib/api';
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
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  accountOptions,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentMeta,
  documentStatusLabel,
  exportPaymentsSheet,
  GOV_CATEGORY_LABELS,
  loadPostableAccounts,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import { AddCoaAccountDialog, PaymentDocumentActions } from './PaymentWorkflowPanel';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function GovernmentPaymentsWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || can('payments.disburse') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'vat', label: 'إقرارات ضريبة القيمة المضافة' },
      { id: 'zakat', label: 'الزكاة وهيئة الزكاة' },
      { id: 'sadad', label: 'رسوم حكومية وسداد' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('vat');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [canCreateCoa, setCanCreateCoa] = useState(false);
  const [coaDialogOpen, setCoaDialogOpen] = useState(false);
  const [docs, setDocs] = useState<PaymentDocumentItem[]>([]);

  const [createOpen, setCreateOpen] = useState(false);
  const [form, setForm] = useState({
    companyId: '',
    govCategory: 'vat' as 'vat' | 'zakat' | 'sadad',
    taxPeriod: period,
    sadadNo: '',
    accountCode: '213101',
    amount: '',
    occurredOn: defaultDateInPeriod(period),
    dueOn: defaultDateInPeriod(period),
  });

  useEffect(() => {
    const d = defaultDateInPeriod(period);
    setForm((p) => ({ ...p, taxPeriod: period, occurredOn: d, dueOn: d }));
  }, [period]);

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
        api.listGovernmentPayments(),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setCanCreateCoa(coaRes.canCreate);
      setDocs(res?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل المدفوعات الحكومية');
    } finally {
      setLoading(false);
    }
  }, [canView]);

  useEffect(() => {
    void load();
  }, [load]);

  const companyOptions = useMemo(
    () => companies.map((c) => ({ value: String(c.id || c.code || ''), label: companyLabel(c) })),
    [companies]
  );

  const companyName = useCallback(
    (id?: string) => {
      const m = companies.find((c) => c.id === id || c.code === id);
      return m ? companyLabel(m) : id || '—';
    },
    [companies]
  );

  const coaOptions = useMemo(() => accountOptions(accounts), [accounts]);

  const openCreateForTab = () => {
    const cat = (activeTab === 'zakat' || activeTab === 'sadad' ? activeTab : 'vat') as 'vat' | 'zakat' | 'sadad';
    setForm((p) => ({ ...p, govCategory: cat, sadadNo: '', amount: '' }));
    setCreateOpen(true);
  };

  const submitGov = async (e: React.FormEvent) => {
    e.preventDefault();
    const amount = Number(form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      toast('أدخل مبلغًا صالحًا', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.createGovernmentPayment({
        companyId: form.companyId,
        govCategory: form.govCategory,
        taxPeriod: form.taxPeriod,
        sadadNo: form.sadadNo.trim(),
        reference: form.sadadNo.trim() || `${form.govCategory}-${form.taxPeriod}`,
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        vatAmount: 0,
        validTaxInvoice: false,
        idempotencyKey: newIdempotencyKey(`gov-${form.govCategory}`),
        lines: [
          {
            accountCode: form.accountCode,
            amount,
            costCentreId: NONE_COST_CENTRE_ID,
          },
        ],
      });
      toast('تم تسجيل الالتزام الحكومي كمسودة في محرك المدفوعات', 'success');
      setCreateOpen(false);
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل السداد الحكومي', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const filteredDocs = useMemo(
    () =>
      docs.filter((d) => {
        const cat = String(documentMeta(d).govCategory || '');
        if (!cat) return activeTab === 'sadad';
        return cat === activeTab;
      }),
    [docs, activeTab]
  );

  const sumByCategory = (cat: string) =>
    docs
      .filter((d) => String(documentMeta(d).govCategory || 'sadad') === cat && d.status !== 5)
      .reduce((s, d) => s + Number(d.availableBalance ?? d.amount ?? 0), 0);

  const monthTotal = docs
    .filter((d) => String(d.periodMonth || d.occurredOn || '').startsWith(period) && d.status !== 5)
    .reduce((s, d) => s + Number(d.amount || 0), 0);
  const awaitingCount = docs.filter((d) => d.status === 3 && Number(d.availableBalance ?? 0) > 0).length;

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['نوع الالتزام', 'الشركة', 'الفترة الضريبية', 'رقم فاتورة سداد', 'تاريخ الاستحقاق', 'المبلغ', 'الحالة'],
        docs.map((d) => {
          const m = documentMeta(d);
          return [
            GOV_CATEGORY_LABELS[String(m.govCategory || 'sadad')] || 'سداد حكومي',
            companyName(d.companyId),
            String(m.taxPeriod || String(d.periodMonth || '').slice(0, 7)),
            String(m.sadadNo || d.reference || ''),
            String(d.dueOn || '').slice(0, 10),
            Number(d.amount || 0),
            documentStatusLabel(d),
          ];
        }),
        [5],
        `government-payments-${period}`
      );
      toast('تم تصدير سجل المدفوعات الحكومية', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const columns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'govCategory',
      header: 'نوع الالتزام',
      type: 'list',
      getValue: (r) => GOV_CATEGORY_LABELS[String(documentMeta(r).govCategory || 'sadad')] || 'سداد حكومي',
    },
    { key: 'companyId', header: 'الشركة / السجل', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'taxPeriod',
      header: 'الفترة الضريبية',
      type: 'text',
      getValue: (r) => String(documentMeta(r).taxPeriod || String(r.periodMonth || '').slice(0, 7)),
    },
    {
      key: 'sadadNo',
      header: 'رقم فاتورة سداد',
      type: 'text',
      getValue: (r) => String(documentMeta(r).sadadNo || r.reference || ''),
      render: (r) => <bdi className="font-mono text-xs">{String(documentMeta(r).sadadNo || r.reference || '—')}</bdi>,
    },
    { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
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

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض المدفوعات الحكومية يحتاج صلاحية المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="المدفوعات الحكومية"
        description="إقرارات ضريبة القيمة المضافة، الزكاة، وفواتير سداد للتأمينات ومدد والبلدية والغرفة التجارية."
        primaryAction={
          canDisburse
            ? { label: 'تسجيل التزام / سداد حكومي', icon: <Plus size={14} />, onClick: openCreateForTab }
            : undefined
        }
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="ضريبة مستحقة السداد" value={formatSar(sumByCategory('vat'))} />
        <StatCard label="زكاة مستحقة" value={formatSar(sumByCategory('zakat'))} />
        <StatCard label={`مدفوعات حكومية في ${period}`} value={formatSar(monthTotal)} />
        <StatCard label="فواتير سداد بانتظار التنفيذ" value={awaitingCount} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <DataTable
          columns={columns}
          data={filteredDocs}
          emptyTitle="لا توجد التزامات في هذا القسم"
          emptyDescription="سجّل التزامًا حكوميًا أو فاتورة سداد ليمر بدورة الاعتماد والصرف."
        />
      )}

      {/* Create Government Payment Dialog */}
      <Dialog open={createOpen} onClose={() => !busy && setCreateOpen(false)} title="تسجيل التزام أو سداد حكومي" size="lg">
        <form onSubmit={submitGov} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة / السجل</span>
              <SearchableSelect
                value={form.companyId}
                onChange={(v) => setForm((p) => ({ ...p, companyId: v }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">نوع الالتزام</span>
              <SearchableSelect
                value={form.govCategory}
                onChange={(v) => setForm((p) => ({ ...p, govCategory: v as 'vat' | 'zakat' | 'sadad' }))}
                options={[
                  { value: 'vat', label: 'إقرار ضريبة القيمة المضافة' },
                  { value: 'zakat', label: 'الزكاة وهيئة الزكاة' },
                  { value: 'sadad', label: 'رسوم حكومية وفواتير سداد' },
                ]}
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">الفترة الضريبية / المرجعية</span>
              <input
                className={inputClass}
                value={form.taxPeriod}
                onChange={(e) => setForm((p) => ({ ...p, taxPeriod: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">رقم فاتورة سداد</span>
              <input
                className={inputClass}
                value={form.sadadNo}
                onChange={(e) => setForm((p) => ({ ...p, sadadNo: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">المبلغ</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={form.amount}
                onChange={(e) => setForm((p) => ({ ...p, amount: e.target.value }))}
                required
              />
            </label>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">الحساب المحاسبي</span>
                {canCreateCoa && (
                  <button
                    type="button"
                    onClick={() => setCoaDialogOpen(true)}
                    className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                  >
                    + إضافة حساب
                  </button>
                )}
              </div>
              <SearchableSelect
                value={form.accountCode}
                onChange={(v) => setForm((p) => ({ ...p, accountCode: v }))}
                options={coaOptions}
                placeholder="اختر الحساب..."
              />
            </div>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ المستند (ضمن {period})</span>
              <input
                type="date"
                className={inputClass}
                value={form.occurredOn}
                onChange={(e) => setForm((p) => ({ ...p, occurredOn: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ الاستحقاق</span>
              <input
                type="date"
                className={inputClass}
                value={form.dueOn}
                onChange={(e) => setForm((p) => ({ ...p, dueOn: e.target.value }))}
                required
              />
            </label>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setCreateOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ الالتزام
            </button>
          </div>
        </form>
      </Dialog>

      <AddCoaAccountDialog
        open={coaDialogOpen}
        onClose={() => setCoaDialogOpen(false)}
        defaultParentCode="2131"
        onCreated={async (code) => {
          await load();
          if (code) setForm((p) => ({ ...p, accountCode: code }));
        }}
      />
    </div>
  );
}
