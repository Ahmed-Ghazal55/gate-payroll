'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Send, Save, Download, BookOpenCheck } from 'lucide-react';
import {
  api,
  ApiError,
  type PaymentDocumentItem,
  type RiderAccountRow,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { RiderPicker, type RiderOption } from '../../components/ui/RiderPicker';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  accountOptions,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentStatusLabel,
  exportPaymentsSheet,
  isBankOrCashCode,
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

export function QuickPaymentWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || can('payments.disburse') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'form', label: 'تسجيل دفعة سريعة' },
      { id: 'history', label: 'السجل السريع' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('form');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [canCreateCoa, setCanCreateCoa] = useState(false);
  const [suppliers, setSuppliers] = useState<Array<Record<string, unknown>>>([]);
  const [historyDocs, setHistoryDocs] = useState<PaymentDocumentItem[]>([]);
  const [lastCreatedDoc, setLastCreatedDoc] = useState<PaymentDocumentItem | null>(null);

  const [coaDialogOpen, setCoaDialogOpen] = useState(false);
  const [supplierDialogOpen, setSupplierDialogOpen] = useState(false);
  const [selectedRider, setSelectedRider] = useState<RiderOption | null>(null);
  const [riderAccounts, setRiderAccounts] = useState<RiderAccountRow[]>([]);

  const [form, setForm] = useState({
    companyId: '',
    accountCode: '',
    costCentreId: NONE_COST_CENTRE_ID,
    supplierId: '',
    erpRiderId: '',
    erpPlatformAccountId: '',
    netAmount: '',
    vatRate: '0',
    validTaxInvoice: false,
    expectedBankCode: '111101',
    reference: '',
    occurredOn: defaultDateInPeriod(period),
    dueOn: defaultDateInPeriod(period),
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
      const [compList, coaRes, supRes, docsRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listPaymentSuppliers().catch(() => ({ items: [], allowedActions: [] })),
        api.listPaymentDocuments({ type: 'quick' }).catch(() => ({ items: [], allowedActions: [] })),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setCanCreateCoa(coaRes.canCreate);
      setSuppliers((supRes?.items as unknown as Array<Record<string, unknown>>) || []);
      setHistoryDocs(docsRes?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل لوحة الدفع السريع');
    } finally {
      setLoading(false);
    }
  }, [canView]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleRiderChange = async (rider: RiderOption | null) => {
    setSelectedRider(rider);
    const empId = String(
      (rider as unknown as Record<string, unknown>)?.erpId ||
        (rider as unknown as Record<string, unknown>)?.erpRiderId ||
        rider?.id ||
        ''
    );
    setForm((p) => ({ ...p, erpRiderId: empId, erpPlatformAccountId: '' }));
    if (!rider?.iqama) {
      setRiderAccounts([]);
      return;
    }
    try {
      const res = await api.getRiderAccounts(rider.iqama);
      const list = res?.items || [];
      setRiderAccounts(list);
      if (list[0]?.id) setForm((p) => ({ ...p, erpPlatformAccountId: String(list[0].id) }));
    } catch {
      setRiderAccounts([]);
    }
  };

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

  const platformAccountOptions = useMemo(
    () =>
      riderAccounts.map((a) => ({
        value: String(a.id || ''),
        label: `${String(a.platformCode || 'منصة')} — ${String(a.accountCode || a.platformAccountId || a.id || '')}`,
      })),
    [riderAccounts]
  );

  const debitAccountOptions = useMemo(() => accountOptions(accounts), [accounts]);
  const bankAccountOptions = useMemo(() => {
    const filtered = accountOptions(accounts, (a) => isBankOrCashCode(a.code));
    return filtered.length > 0
      ? filtered
      : [
          { value: '111101', label: '111101 — الصندوق الرئيسي' },
          { value: '113101', label: '113101 — البنك الرئيسي' },
        ];
  }, [accounts]);

  const netAmountNum = Number(form.netAmount || 0);
  const vatAmountNum =
    Number.isFinite(netAmountNum) && netAmountNum > 0 && form.vatRate === '15'
      ? Math.round(netAmountNum * 0.15 * 100) / 100
      : 0;
  const totalAmountNum =
    Number.isFinite(netAmountNum) && netAmountNum > 0
      ? Math.round((netAmountNum + vatAmountNum) * 100) / 100
      : 0;

  const selectedDebitLabel =
    debitAccountOptions.find((a) => a.value === form.accountCode)?.label || form.accountCode || 'بند الصرف المختار';
  const selectedBankLabel =
    bankAccountOptions.find((a) => a.value === form.expectedBankCode)?.label || form.expectedBankCode || 'حساب البنك / الخزينة';

  const saveQuick = async (mode: 'draft' | 'submit') => {
    if (!canDisburse) {
      toast('صلاحية الصرف مطلوبة لتسجيل دفعة سريعة', 'danger');
      return;
    }
    if (!form.companyId || !form.accountCode || !form.reference.trim()) {
      toast('أكمل الشركة وبند الصرف والبيان قبل الحفظ', 'warning');
      return;
    }
    if (!Number.isFinite(netAmountNum) || netAmountNum <= 0) {
      toast('أدخل مبلغًا موجبًا قبل الضريبة', 'warning');
      return;
    }
    if (vatAmountNum > 0 && !form.validTaxInvoice) {
      toast('خصم ضريبة المدخلات يتطلب تأكيد صلاحية الفاتورة الضريبية', 'warning');
      return;
    }
    if (form.erpRiderId && !form.erpPlatformAccountId) {
      toast('اختر حساب المنصة المرتبط بالمندوب', 'warning');
      return;
    }

    const lines: Array<Record<string, unknown>> = [
      {
        accountCode: form.accountCode,
        amount: netAmountNum,
        costCentreId: form.costCentreId || NONE_COST_CENTRE_ID,
        ...(form.erpRiderId
          ? { erpRiderId: form.erpRiderId, erpPlatformAccountId: form.erpPlatformAccountId }
          : {}),
      },
    ];
    if (vatAmountNum > 0) {
      lines.push({
        accountCode: '112601',
        amount: vatAmountNum,
        costCentreId: form.costCentreId || NONE_COST_CENTRE_ID,
      });
    }

    setBusy(true);
    try {
      let created = await api.createQuickPayment({
        companyId: form.companyId,
        supplierId: form.supplierId || undefined,
        reference: form.reference.trim(),
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        vatAmount: vatAmountNum,
        validTaxInvoice: form.validTaxInvoice,
        payoutAccountCode: form.expectedBankCode,
        idempotencyKey: newIdempotencyKey('quick'),
        lines,
      });
      if (mode === 'submit' && created.id) {
        created = await api.submitPaymentDocument(created.id);
        toast(`تم حفظ الدفعة ${created.docNo || ''} وإرسالها للاعتماد`, 'success');
      } else {
        toast(`تم حفظ المسودة ${created.docNo || ''}`, 'success');
      }
      setLastCreatedDoc(created);
      setForm((p) => ({ ...p, netAmount: '', reference: '' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل الدفعة السريعة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم المستند', 'الشركة', 'البيان', 'التاريخ', 'الضريبة', 'الإجمالي', 'الحالة'],
        historyDocs.map((d) => [
          d.docNo || d.id,
          companyName(d.companyId),
          d.reference || '',
          String(d.occurredOn || '').slice(0, 10),
          Number(d.vatAmount || 0),
          Number(d.amount || 0),
          documentStatusLabel(d),
        ]),
        [4, 5],
        `quick-payments-${period}`
      );
      toast('تم تصدير سجل الدفعات السريعة', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const columns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'docNo',
      header: 'رقم المستند',
      type: 'text',
      getValue: (r) => String(r.docNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.docNo || r.id.slice(0, 8)}</bdi>,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'reference', header: 'البيان / المرجع', type: 'text', getValue: (r) => String(r.reference || '') },
    { key: 'occurredOn', header: 'التاريخ', type: 'date', getValue: (r) => String(r.occurredOn || '').slice(0, 10) },
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
    return <EmptyState title="صلاحية غير كافية" description="لوحة الدفع السريع تحتاج صلاحية المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="تسجيل مدفوعات سريعة"
        description="لوحة تسجيل سريعة للمصروفات والمدفوعات المباشرة مع معاينة القيد المحاسبي قبل الاعتماد والصرف."
        moreActions={[
          { label: 'تصدير السجل', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : activeTab === 'form' ? (
        <div className="grid gap-4 lg:grid-cols-3 items-start">
          {/* Quick Payment Form */}
          <div className="lg:col-span-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 sm:p-5 space-y-4 text-xs">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <span className="block text-[var(--muted)] font-semibold">الشركة / السجل *</span>
                <SearchableSelect
                  value={form.companyId}
                  onChange={(v) => setForm((p) => ({ ...p, companyId: v, supplierId: '' }))}
                  options={companyOptions}
                  placeholder="اختر الشركة..."
                />
              </div>
              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <span className="text-[var(--muted)] font-semibold">بند الصرف / الحساب *</span>
                  {canCreateCoa && (
                    <button
                      type="button"
                      onClick={() => setCoaDialogOpen(true)}
                      className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                    >
                      + إضافة حساب جديد
                    </button>
                  )}
                </div>
                <SearchableSelect
                  value={form.accountCode}
                  onChange={(v) => setForm((p) => ({ ...p, accountCode: v }))}
                  options={debitAccountOptions}
                  placeholder="اختر الحساب المحاسبي..."
                />
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <span className="block text-[var(--muted)] font-semibold">مركز التكلفة *</span>
                <SearchableSelect
                  value={form.costCentreId}
                  onChange={(v) => setForm((p) => ({ ...p, costCentreId: v }))}
                  options={[{ value: NONE_COST_CENTRE_ID, label: 'بدون مركز تكلفة (عام)' }]}
                />
              </div>
              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <span className="text-[var(--muted)]">المورد (اختياري)</span>
                  {canDisburse && (
                    <button
                      type="button"
                      onClick={() => setSupplierDialogOpen(true)}
                      className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                    >
                      + إضافة مورد
                    </button>
                  )}
                </div>
                <SearchableSelect
                  value={form.supplierId}
                  onChange={(v) => setForm((p) => ({ ...p, supplierId: v }))}
                  options={supplierOptions}
                  placeholder="اختر المورد..."
                  clearable
                />
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <span className="block text-[var(--muted)]">المندوب (اختياري)</span>
                <RiderPicker value={selectedRider?.iqama} onChange={(r) => void handleRiderChange(r)} />
              </div>
              {selectedRider && (
                <div className="space-y-1">
                  <span className="block text-[var(--muted)]">حساب المنصة المرتبط بالمندوب *</span>
                  <SearchableSelect
                    value={form.erpPlatformAccountId}
                    onChange={(v) => setForm((p) => ({ ...p, erpPlatformAccountId: v }))}
                    options={platformAccountOptions}
                    placeholder="اختر حساب المنصة..."
                  />
                </div>
              )}
            </div>

            <div className="grid gap-3 sm:grid-cols-3 items-end">
              <label className="block space-y-1">
                <span className="text-[var(--muted)] font-semibold">المبلغ قبل الضريبة *</span>
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
                    { value: '0', label: '0% (بدون ضريبة)' },
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

            <div className="grid gap-3 sm:grid-cols-3">
              <div className="space-y-1">
                <span className="block text-[var(--muted)]">حساب الصرف المقترح (بنك / خزينة)</span>
                <SearchableSelect
                  value={form.expectedBankCode}
                  onChange={(v) => setForm((p) => ({ ...p, expectedBankCode: v }))}
                  options={bankAccountOptions}
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

            <label className="block space-y-1">
              <span className="text-[var(--muted)] font-semibold">البيان / المرجع *</span>
              <input
                className={inputClass}
                value={form.reference}
                onChange={(e) => setForm((p) => ({ ...p, reference: e.target.value }))}
                placeholder="وصف مختصر للمصروف أو رقم الإيصال"
                required
              />
            </label>

            <div className="flex flex-wrap items-center justify-end gap-2 pt-2 border-t border-[var(--line)]">
              <button
                type="button"
                disabled={busy || !canDisburse}
                onClick={() => void saveQuick('draft')}
                className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-bold text-[var(--ink)] hover:bg-[var(--surface)] cursor-pointer disabled:opacity-50"
              >
                <Save size={14} aria-hidden="true" />
                <span>حفظ كمسودة</span>
              </button>
              <button
                type="button"
                disabled={busy || !canDisburse}
                onClick={() => void saveQuick('submit')}
                className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold hover:opacity-95 cursor-pointer disabled:opacity-50"
              >
                <Send size={14} aria-hidden="true" />
                <span>حفظ وإرسال للاعتماد</span>
              </button>
            </div>

            {lastCreatedDoc && (
              <div className="rounded-xl border border-[var(--accent)]/30 bg-[var(--accent)]/5 p-3 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="font-bold">آخر دفعة مسجلة: <bdi>{lastCreatedDoc.docNo || lastCreatedDoc.id}</bdi></span>
                  <span className="num font-bold">{formatSar(lastCreatedDoc.amount)}</span>
                </div>
                <PaymentDocumentActions
                  doc={lastCreatedDoc}
                  period={period}
                  accounts={accounts}
                  onUpdated={async () => {
                    await load();
                    try {
                      setLastCreatedDoc(await api.getPaymentDocument(lastCreatedDoc.id));
                    } catch {
                      setLastCreatedDoc(null);
                    }
                  }}
                />
              </div>
            )}
          </div>

          {/* Live Journal Preview Card */}
          <aside className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3 text-xs">
            <div className="flex items-center gap-2 font-bold text-sm text-[var(--ink)] border-b border-[var(--line)] pb-2.5">
              <BookOpenCheck size={16} className="text-[var(--accent)]" aria-hidden="true" />
              <span>معاينة القيد المحاسبي عند الصرف</span>
            </div>
            <div className="space-y-2">
              <div className="flex items-center justify-between py-1.5 border-b border-[var(--line)]/60">
                <div>
                  <div className="font-semibold text-[var(--ink)]">مدين: {selectedDebitLabel}</div>
                  <div className="text-[11px] text-[var(--muted)]">المبلغ قبل الضريبة</div>
                </div>
                <span className="num font-bold text-[var(--ink)]">{formatSar(netAmountNum > 0 ? netAmountNum : 0)}</span>
              </div>
              {vatAmountNum > 0 && (
                <div className="flex items-center justify-between py-1.5 border-b border-[var(--line)]/60">
                  <div>
                    <div className="font-semibold text-[var(--ink)]">مدين: 112601 — ضريبة المدخلات</div>
                    <div className="text-[11px] text-[var(--muted)]">نسبة 15% بفاتورة ضريبية</div>
                  </div>
                  <span className="num font-bold text-[var(--ink)]">{formatSar(vatAmountNum)}</span>
                </div>
              )}
              <div className="flex items-center justify-between py-1.5 border-b border-[var(--line)]/60">
                <div>
                  <div className="font-semibold text-[var(--ink)]">دائن: {selectedBankLabel}</div>
                  <div className="text-[11px] text-[var(--muted)]">يُرحّل عند صرف سند الصرف</div>
                </div>
                <span className="num font-bold text-[var(--money-pos)]">{formatSar(totalAmountNum)}</span>
              </div>
              <div className="flex items-center justify-between pt-2 font-bold text-sm">
                <span>الإجمالي المتوازن</span>
                <span className="num text-[var(--accent)]">{formatSar(totalAmountNum)}</span>
              </div>
            </div>
            <p className="text-[11px] text-[var(--muted)] leading-relaxed">
              كل دفعة تمر عبر محرك المدفوعات الموحّد: مسودة ← اعتماد ← طلب صرف واعتماد ثنائي ← سند صرف وترحيل القيد.
            </p>
          </aside>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard label="مسودات سريعة" value={historyDocs.filter((d) => d.status === 1).length} />
            <StatCard label="بانتظار الاعتماد" value={historyDocs.filter((d) => d.status === 2).length} />
            <StatCard label="معتمدة بانتظار الصرف" value={historyDocs.filter((d) => d.status === 3 && Number(d.availableBalance ?? 0) > 0).length} />
            <StatCard
              label={`إجمالي ${period}`}
              value={formatSar(
                historyDocs
                  .filter((d) => String(d.periodMonth || d.occurredOn || '').startsWith(period) && d.status !== 5)
                  .reduce((s, d) => s + Number(d.amount || 0), 0)
              )}
            />
          </div>
          <DataTable
            columns={columns}
            data={historyDocs}
            emptyTitle="لا توجد دفعات سريعة مسجلة"
            emptyDescription="سجّل دفعة جديدة من تبويب «تسجيل دفعة سريعة»."
          />
        </div>
      )}

      <AddCoaAccountDialog
        open={coaDialogOpen}
        onClose={() => setCoaDialogOpen(false)}
        onCreated={async (code) => {
          await load();
          if (code) setForm((p) => ({ ...p, accountCode: code }));
        }}
      />
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
    </div>
  );
}
