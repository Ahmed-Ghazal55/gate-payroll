'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Download, Link2 } from 'lucide-react';
import {
  api,
  ApiError,
  type OperationsPurchaseRow,
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
import { SourceBadge } from '../../components/ui/SourceBadge';
import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  accountOptions,
  type CoaAccountOption,
  defaultDateInPeriod,
  DOCUMENT_TYPE_LABELS,
  documentMeta,
  documentStatusLabel,
  exportPaymentsSheet,
  loadPostableAccounts,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import {
  AddCoaAccountDialog,
  AddSupplierDialog,
  PaymentDocumentActions,
} from './PaymentWorkflowPanel';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function PurchasesWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'erp_receipts', label: 'استلامات الصيانة (مباشر من الموارد)' },
      { id: 'expenses', label: 'فواتير ومصروفات أخرى' },
      { id: 'petty_cash', label: 'العهد النثرية (111102)' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('erp_receipts');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [canCreateCoa, setCanCreateCoa] = useState(false);
  const [suppliers, setSuppliers] = useState<Array<Record<string, unknown>>>([]);
  const [purchaseDocs, setPurchaseDocs] = useState<PaymentDocumentItem[]>([]);
  const [expenseDocs, setExpenseDocs] = useState<PaymentDocumentItem[]>([]);
  const [erpReceipts, setErpReceipts] = useState<OperationsPurchaseRow[]>([]);

  const [docDialogOpen, setDocDialogOpen] = useState(false);
  const [coaDialogOpen, setCoaDialogOpen] = useState(false);
  const [supplierDialogOpen, setSupplierDialogOpen] = useState(false);

  const [form, setForm] = useState({
    type: 'purchase' as 'purchase' | 'expense',
    companyId: '',
    supplierId: '',
    accountCode: '512107',
    reference: '',
    occurredOn: defaultDateInPeriod(period),
    dueOn: defaultDateInPeriod(period),
    netAmount: '',
    vatRate: '0',
    validTaxInvoice: false,
    erpReceiptId: '',
  });

  useEffect(() => {
    const d = defaultDateInPeriod(period);
    setForm((p) => ({ ...p, occurredOn: d, dueOn: d }));
  }, [period]);

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [compList, coaRes, supRes, purchRes, expRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listPaymentSuppliers(),
        api.listPaymentDocuments({ type: 'purchase' }),
        api.listPaymentDocuments({ type: 'expense' }),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setCanCreateCoa(coaRes.canCreate);
      setSuppliers((supRes?.items as unknown as Array<Record<string, unknown>>) || []);
      setPurchaseDocs(purchRes?.items || []);
      setExpenseDocs(expRes?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
      const { from, to } = periodDateRange(period);
      api
        .getOperationsPurchaseInvoices({ from, to, pageSize: 200 })
        .then((res) => setErpReceipts(res?.items || []))
        .catch(() => setErpReceipts([]));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل المشتريات والمصروفات');
    } finally {
      setLoading(false);
    }
  }, [canView, period]);

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

  const supplierOptions = useMemo(
    () =>
      suppliers
        .filter((s) => !form.companyId || String(s.companyId || '') === form.companyId)
        .map((s) => ({ value: String(s.id || ''), label: String(s.nameAr || s.id || '') })),
    [suppliers, form.companyId]
  );

  const expenseAccountOptions = useMemo(() => accountOptions(accounts), [accounts]);

  const allDocs = useMemo(() => [...purchaseDocs, ...expenseDocs], [purchaseDocs, expenseDocs]);

  const linkedReceiptIds = useMemo(() => {
    const set = new Set<string>();
    for (const d of purchaseDocs) {
      const rid = String(documentMeta(d).erpReceiptId || '');
      if (rid) set.add(rid);
    }
    return set;
  }, [purchaseDocs]);

  const pettyCashDocs = useMemo(() => {
    const pettyId = accounts.find((a) => a.code === '111102')?.id;
    return expenseDocs.filter((d) => {
      const m = documentMeta(d);
      if (m.pettyCash === true) return true;
      return Boolean(pettyId && (d.lines || []).some((l) => l.accountId === pettyId));
    });
  }, [expenseDocs, accounts]);

  const openFromErpReceipt = (receipt: OperationsPurchaseRow) => {
    const net = Number(receipt.subtotal ?? receipt.totalAmount ?? 0);
    const total = Number(receipt.totalAmount ?? net);
    const vat = Math.max(0, Math.round((total - net) * 100) / 100);
    const dateVal = defaultDateInPeriod(period);
    setForm((p) => ({
      ...p,
      type: 'purchase',
      reference: String(receipt.receiptNumber || receipt.supplierInvoiceNumber || receipt.id || ''),
      netAmount: net > 0 ? net.toFixed(2) : '',
      vatRate: vat > 0 ? '15' : '0',
      validTaxInvoice: vat > 0,
      erpReceiptId: String(receipt.id || ''),
      occurredOn: dateVal,
      dueOn: dateVal,
    }));
    setDocDialogOpen(true);
  };

  const openNewDoc = (type: 'purchase' | 'expense', pettyCash = false) => {
    const dateVal = defaultDateInPeriod(period);
    setForm((p) => ({
      ...p,
      type,
      accountCode: pettyCash ? '111102' : p.accountCode || '512107',
      reference: '',
      netAmount: '',
      vatRate: '0',
      validTaxInvoice: false,
      erpReceiptId: '',
      occurredOn: dateVal,
      dueOn: dateVal,
    }));
    setDocDialogOpen(true);
  };

  const submitDoc = async (e: React.FormEvent) => {
    e.preventDefault();
    const net = Number(form.netAmount);
    if (!Number.isFinite(net) || net <= 0) {
      toast('أدخل مبلغًا موجبًا قبل الضريبة', 'warning');
      return;
    }
    const vat = form.vatRate === '15' ? Math.round(net * 0.15 * 100) / 100 : 0;
    if (vat > 0 && !form.validTaxInvoice) {
      toast('خصم ضريبة المدخلات يتطلب تأكيد صلاحية الفاتورة الضريبية', 'warning');
      return;
    }
    const lines: Array<Record<string, unknown>> = [
      { accountCode: form.accountCode, amount: net, costCentreId: NONE_COST_CENTRE_ID },
    ];
    if (vat > 0) {
      lines.push({ accountCode: '112601', amount: vat, costCentreId: NONE_COST_CENTRE_ID });
    }
    setBusy(true);
    try {
      await api.createPaymentDocument({
        type: form.type,
        companyId: form.companyId,
        supplierId: form.supplierId || undefined,
        reference: form.reference.trim(),
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        vatAmount: vat,
        validTaxInvoice: form.validTaxInvoice,
        erpReceiptId: form.erpReceiptId || undefined,
        pettyCash: activeTab === 'petty_cash' || form.accountCode === '111102',
        idempotencyKey: newIdempotencyKey(`doc-${form.type}`),
        lines,
      });
      toast('تم حفظ مسودة المستند بنجاح', 'success');
      setDocDialogOpen(false);
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر حفظ المستند', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم المستند', 'النوع', 'الشركة', 'المرجع', 'الإجمالي', 'الضريبة', 'الحالة'],
        allDocs.map((d) => [
          d.docNo || d.id,
          DOCUMENT_TYPE_LABELS[d.type] || d.type,
          companyName(d.companyId),
          d.reference || '',
          Number(d.amount || 0),
          Number(d.vatAmount || 0),
          documentStatusLabel(d),
        ]),
        [4, 5],
        `purchases-expenses-${period}`
      );
      toast('تم تصدير المشتريات والمصروفات', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const receiptColumns: ColumnDef<OperationsPurchaseRow>[] = [
    {
      key: 'receiptNumber',
      header: 'رقم الاستلام',
      type: 'text',
      getValue: (r) => String(r.receiptNumber || r.supplierInvoiceNumber || r.id || ''),
    },
    {
      key: 'supplierNameAr',
      header: 'المورد (الموارد)',
      type: 'text',
      getValue: (r) => String(r.supplierNameAr || '—'),
    },
    {
      key: 'invoiceDate',
      header: 'التاريخ',
      type: 'date',
      getValue: (r) => String(r.invoiceDate || r.receivedAt || '').slice(0, 10),
    },
    {
      key: 'totalAmount',
      header: 'الإجمالي',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.totalAmount || 0),
      render: (r) => <span className="num font-bold">{formatSar(Number(r.totalAmount || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'linked',
      header: 'حالة الربط',
      type: 'list',
      getValue: (r) => (linkedReceiptIds.has(String(r.id || '')) ? 'مربوط بمستند' : 'بانتظار إنشاء مستند'),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) =>
        canDisburse && !linkedReceiptIds.has(String(r.id || '')) ? (
          <button
            type="button"
            onClick={() => openFromErpReceipt(r)}
            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-[11px] font-bold cursor-pointer"
          >
            <Link2 size={12} aria-hidden="true" />
            <span>إنشاء مستند مورد</span>
          </button>
        ) : (
          <span className="text-[11px] text-[var(--muted)]">مكتمل</span>
        ),
    },
  ];

  const docColumns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'docNo',
      header: 'رقم المستند',
      type: 'text',
      getValue: (r) => String(r.docNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.docNo || r.id.slice(0, 8)}</bdi>,
    },
    {
      key: 'type',
      header: 'التصنيف',
      type: 'list',
      getValue: (r) => DOCUMENT_TYPE_LABELS[r.type] || r.type,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'reference', header: 'المرجع / البيان', type: 'text', getValue: (r) => String(r.reference || '') },
    {
      key: 'amount',
      header: 'الإجمالي',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.amount || 0),
      render: (r) => <span className="num font-bold">{formatSar(r.amount)}</span>,
      summary: 'sum',
    },
    {
      key: 'vatAmount',
      header: 'ضريبة المدخلات',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.vatAmount || 0),
      render: (r) => <span className="num">{formatSar(r.vatAmount ?? 0)}</span>,
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
    return <EmptyState title="صلاحية غير كافية" description="عرض المشتريات والمصروفات يحتاج صلاحية عرض المدفوعات." />;
  }

  const totalMonthPurchases = allDocs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(d.amount || 0), 0);
  const totalInputVat = allDocs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(d.vatAmount || 0), 0);
  const unpaidCount = allDocs.filter((d) => d.status === 3 && Number(d.availableBalance ?? 0) > 0).length;

  return (
    <div className="space-y-4">
      <PageHeader
        title="فواتير المشتريات والمصروفات"
        description="استلامات الصيانة المباشرة من نظام الموارد، فواتير الموردين، المصروفات التشغيلية، والعهد النثرية."
        primaryAction={
          canDisburse
            ? {
                label: activeTab === 'petty_cash' ? 'تسجيل حركة عهدة نثرية' : 'إنشاء فاتورة / مصروف',
                icon: <Plus size={14} />,
                onClick: () => openNewDoc(activeTab === 'petty_cash' ? 'expense' : 'purchase', activeTab === 'petty_cash'),
              }
            : undefined
        }
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="استلامات بانتظار قيد المورد"
          value={erpReceipts.filter((r) => !linkedReceiptIds.has(String(r.id || ''))).length}
        />
        <StatCard label="إجمالي فواتير ومصروفات المسجل" value={formatSar(totalMonthPurchases)} />
        <StatCard label="ضريبة المدخلات (112601)" value={formatSar(totalInputVat)} />
        <StatCard label="معتمدة بانتظار السداد" value={unpaidCount} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'erp_receipts' && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs text-[var(--muted)]">استلامات الصيانة والمشتريات لشهر {period}</span>
                <SourceBadge source="live" />
              </div>
              <DataTable
                columns={receiptColumns}
                data={erpReceipts}
                emptyTitle="لا توجد استلامات في الفترة"
                emptyDescription="لم تُرصد فواتير مشتريات صيانة في نظام الموارد لهذه الفترة."
              />
            </div>
          )}
          {activeTab === 'expenses' && (
            <DataTable
              columns={docColumns}
              data={allDocs}
              emptyTitle="لا توجد فواتير أو مصروفات"
              emptyDescription="أنشئ فاتورة مورد أو مصروفًا تشغيليًا ليمر بدورة الاعتماد والصرف."
            />
          )}
          {activeTab === 'petty_cash' && (
            <DataTable
              columns={docColumns}
              data={pettyCashDocs}
              emptyTitle="لا توجد حركات عهدة نثرية"
              emptyDescription="سجّل مصروف عهدة نثرية على الحساب 111102."
            />
          )}
        </>
      )}

      {/* Create Purchase / Expense Dialog */}
      <Dialog open={docDialogOpen} onClose={() => !busy && setDocDialogOpen(false)} title="إنشاء مستند مشتريات أو مصروف" size="lg">
        <form onSubmit={submitDoc} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">نوع المستند</span>
              <SearchableSelect
                value={form.type}
                onChange={(v) => setForm((p) => ({ ...p, type: v as 'purchase' | 'expense' }))}
                options={[
                  { value: 'purchase', label: 'فاتورة مشتريات (تستحق على المورد 211101)' },
                  { value: 'expense', label: 'مصروف مباشر / عهدة نثرية' },
                ]}
              />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة</span>
              <SearchableSelect
                value={form.companyId}
                onChange={(v) => setForm((p) => ({ ...p, companyId: v, supplierId: '' }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">المورد {form.type === 'purchase' ? '(مطلوب)' : '(اختياري)'}</span>
                <button
                  type="button"
                  onClick={() => setSupplierDialogOpen(true)}
                  className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                >
                  + إضافة مورد
                </button>
              </div>
              <SearchableSelect
                value={form.supplierId}
                onChange={(v) => setForm((p) => ({ ...p, supplierId: v }))}
                options={supplierOptions}
                placeholder="اختر المورد..."
                clearable={form.type === 'expense'}
              />
            </div>
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">بند الصرف / الحساب</span>
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
                options={expenseAccountOptions}
                placeholder="اختر الحساب..."
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">رقم الفاتورة / المرجع</span>
              <input
                className={inputClass}
                value={form.reference}
                onChange={(e) => setForm((p) => ({ ...p, reference: e.target.value }))}
                required
              />
            </label>
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

          <div className="grid gap-3 sm:grid-cols-3 items-end">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">المبلغ قبل الضريبة</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={form.netAmount}
                onChange={(e) => setForm((p) => ({ ...p, netAmount: e.target.value }))}
                required
              />
            </label>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">نسبة الضريبة</span>
              <SearchableSelect
                value={form.vatRate}
                onChange={(v) => setForm((p) => ({ ...p, vatRate: v, validTaxInvoice: v === '15' ? p.validTaxInvoice : false }))}
                options={[
                  { value: '0', label: '0% (معفى / بدون ضريبة)' },
                  { value: '15', label: '15% (ضريبة مدخلات 112601)' },
                ]}
              />
            </div>
            <label className="flex items-center gap-2 pb-2 cursor-pointer">
              <input
                type="checkbox"
                checked={form.validTaxInvoice}
                onChange={(e) => setForm((p) => ({ ...p, validTaxInvoice: e.target.checked }))}
                disabled={form.vatRate !== '15'}
              />
              <span>فاتورة ضريبية صالحة</span>
            </label>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setDocDialogOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ المسودة
            </button>
          </div>
        </form>
      </Dialog>

      <AddSupplierDialog
        open={supplierDialogOpen}
        onClose={() => setSupplierDialogOpen(false)}
        companyOptions={companyOptions}
        defaultCompanyId={form.companyId}
        onCreated={async (id) => {
          await load();
          if (id) setForm((p) => ({ ...p, supplierId: id }));
        }}
      />
      <AddCoaAccountDialog
        open={coaDialogOpen}
        onClose={() => setCoaDialogOpen(false)}
        onCreated={async (code) => {
          await load();
          if (code) setForm((p) => ({ ...p, accountCode: code }));
        }}
      />
    </div>
  );
}
