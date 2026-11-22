'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Eye, ShieldCheck, Banknote, Ban, Search } from 'lucide-react';
import {
  api,
  ApiError,
  type PaymentDocumentItem,
  type PaymentVoucherItem,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { StatCard } from '../../components/ui/StatCard';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { Drawer } from '../../components/ui/Drawer';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { ApprovalRequest, CompanySponsor } from '../../lib/types';
import {
  type CoaAccountOption,
  DOCUMENT_TYPE_LABELS,
  documentStatusLabel,
  hasAllowedAction,
  loadPostableAccounts,
  VOUCHER_STATUS_LABELS,
} from './payment-engine';
import { AddSupplierDialog, PaymentDocumentActions } from './PaymentWorkflowPanel';

type SupplierRow = Record<string, unknown> & {
  id?: string;
  companyId?: string;
  nameAr?: string;
  vatNo?: string;
  status?: number;
};

type LedgerRow = Record<string, unknown> & {
  id?: string;
  reference?: string;
  amount?: number;
  amountPaid?: number;
  amountReserved?: number;
};

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function SuppliersWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;
  const canApprove = can('approvals.decide') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'list', label: 'قائمة الموردين' },
      { id: 'queue', label: 'طابور طلبات الدفع' },
      { id: 'vouchers', label: 'سندات الصرف' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('list');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [suppliers, setSuppliers] = useState<SupplierRow[]>([]);
  const [supplierActions, setSupplierActions] = useState<string[]>([]);
  const [allDocs, setAllDocs] = useState<PaymentDocumentItem[]>([]);
  const [vouchers, setVouchers] = useState<PaymentVoucherItem[]>([]);
  const [voucherLookupId, setVoucherLookupId] = useState('');

  const [supplierDialogOpen, setSupplierDialogOpen] = useState(false);
  const [ledgerDrawer, setLedgerDrawer] = useState<{
    supplier: SupplierRow;
    invoiced: number;
    paid: number;
    outstanding: number;
    items: LedgerRow[];
  } | null>(null);

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
        api.listPaymentSuppliers(),
        api.listPaymentDocuments(),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setSuppliers((supRes?.items as unknown as SupplierRow[]) || []);
      setSupplierActions(Array.isArray(supRes?.allowedActions) ? supRes.allowedActions : []);
      setAllDocs(docsRes?.items || []);

      // Load pending payment_out dual-approval vouchers when permitted.
      if (can('approvals.view') || isAdmin) {
        try {
          const approvals: ApprovalRequest[] = await api.getApprovals();
          const voucherIds = Array.from(
            new Set(
              (approvals || [])
                .filter((a) => {
                  const rec = a as unknown as Record<string, unknown>;
                  return String(rec.operation || rec.type || '') === 'payment_out';
                })
                .map((a) => String((a as unknown as Record<string, unknown>).entityId || (a as unknown as Record<string, unknown>).recordId || ''))
                .filter(Boolean)
            )
          ).slice(0, 30);
          if (voucherIds.length > 0) {
            const loaded = await Promise.all(
              voucherIds.map((id) => api.getPaymentVoucher(id).catch(() => null))
            );
            const valid = loaded.filter(Boolean) as PaymentVoucherItem[];
            setVouchers((prev) => {
              const map = new Map<string, PaymentVoucherItem>();
              for (const v of prev) map.set(v.id, v);
              for (const v of valid) map.set(v.id, v);
              return Array.from(map.values());
            });
          }
        } catch {
          // ignore optional approvals lookup
        }
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل بيانات الموردين والدفعات');
    } finally {
      setLoading(false);
    }
  }, [canView, can, isAdmin]);

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

  const payableQueue = useMemo(
    () => allDocs.filter((d) => d.status === 3 && Number(d.availableBalance ?? 0) > 0),
    [allDocs]
  );

  const openLedger = async (supplier: SupplierRow) => {
    if (!supplier.id) return;
    setBusy(true);
    try {
      const res = await api.getPaymentSupplierLedger(supplier.id);
      setLedgerDrawer({
        supplier,
        invoiced: Number(res.invoiced || 0),
        paid: Number(res.paid || 0),
        outstanding: Number(res.outstanding || 0),
        items: (res.items as unknown as LedgerRow[]) || [],
      });
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر قراءة كشف حساب المورد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const lookupVoucher = async (e: React.FormEvent) => {
    e.preventDefault();
    const id = voucherLookupId.trim();
    if (!id) return;
    setBusy(true);
    try {
      const v = await api.getPaymentVoucher(id);
      setVouchers((prev) => [v, ...prev.filter((x) => x.id !== v.id)]);
      setVoucherLookupId('');
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'السند غير موجود أو خارج نطاقك', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const approveVoucher = async (v: PaymentVoucherItem) => {
    if (!v.approvalRequestId) return;
    setBusy(true);
    try {
      await api.decideApproval(v.approvalRequestId, { decision: 'approved', note: 'اعتماد ثنائي لسند الصرف' });
      const refreshed = await api.getPaymentVoucher(v.id);
      setVouchers((prev) => prev.map((x) => (x.id === v.id ? refreshed : x)));
      toast('تم الاعتماد الثنائي للسند', 'success');
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر الاعتماد الثنائي', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const payVoucher = async (v: PaymentVoucherItem) => {
    setBusy(true);
    try {
      const paid = await api.payPaymentVoucher(v.id);
      setVouchers((prev) => prev.map((x) => (x.id === v.id ? paid : x)));
      toast(`تم صرف السند ${paid.paymentNo || ''} وترحيل القيد`, 'success');
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر الصرف والترحيل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const cancelVoucher = async (v: PaymentVoucherItem) => {
    setBusy(true);
    try {
      const cancelled = await api.cancelPaymentVoucher(v.id);
      setVouchers((prev) => prev.map((x) => (x.id === v.id ? cancelled : x)));
      toast('تم إلغاء السند وتحرير الحجز', 'success');
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إلغاء السند', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const supplierColumns: ColumnDef<SupplierRow>[] = [
    { key: 'nameAr', header: 'اسم المورد', type: 'text', getValue: (r) => String(r.nameAr || '') },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'vatNo',
      header: 'الرقم الضريبي',
      type: 'text',
      getValue: (r) => String(r.vatNo || ''),
      render: (r) => <bdi className="font-mono text-xs">{String(r.vatNo || '—')}</bdi>,
    },
    { key: 'status', header: 'الحالة', type: 'list', getValue: (r) => (Number(r.status) === 1 ? 'نشط' : 'موقوف') },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <button
          type="button"
          disabled={busy}
          onClick={() => void openLedger(r)}
          className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg border border-[var(--line)] text-[11px] font-semibold hover:bg-[var(--raised)] cursor-pointer"
        >
          <Eye size={12} aria-hidden="true" />
          <span>كشف حساب المورد</span>
        </button>
      ),
    },
  ];

  const queueColumns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'docNo',
      header: 'رقم المستند',
      type: 'text',
      getValue: (r) => String(r.docNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.docNo || r.id.slice(0, 8)}</bdi>,
    },
    {
      key: 'type',
      header: 'النوع',
      type: 'list',
      getValue: (r) => DOCUMENT_TYPE_LABELS[r.type] || r.type,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
    {
      key: 'availableBalance',
      header: 'المتاح للصرف',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.availableBalance ?? 0),
      render: (r) => <span className="num font-bold">{formatSar(r.availableBalance ?? 0)}</span>,
      summary: 'sum',
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <PaymentDocumentActions
          doc={r}
          period={period}
          accounts={accounts}
          onUpdated={load}
          onVoucherCreated={(v) => setVouchers((prev) => [v, ...prev.filter((x) => x.id !== v.id)])}
        />
      ),
    },
  ];

  const voucherColumns: ColumnDef<PaymentVoucherItem>[] = [
    {
      key: 'paymentNo',
      header: 'رقم السند',
      type: 'text',
      getValue: (r) => String(r.paymentNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.paymentNo || r.id.slice(0, 8)}</bdi>,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'paidOn', header: 'تاريخ الصرف', type: 'date', getValue: (r) => String(r.paidOn || '').slice(0, 10) },
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
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => VOUCHER_STATUS_LABELS[Number(r.status)] || String(r.status),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <div className="flex flex-wrap items-center gap-1.5">
          {canApprove && r.status === 1 && r.approvalRequestId && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void approveVoucher(r)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--accent)] text-[var(--accent)] text-[11px] font-bold cursor-pointer"
            >
              <ShieldCheck size={12} aria-hidden="true" />
              <span>اعتماد ثنائي</span>
            </button>
          )}
          {hasAllowedAction(r, 'pay_voucher') && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void payVoucher(r)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-[var(--money-pos)] text-white text-[11px] font-bold cursor-pointer"
            >
              <Banknote size={12} aria-hidden="true" />
              <span>صرف وترحيل</span>
            </button>
          )}
          {hasAllowedAction(r, 'cancel_draft') && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void cancelVoucher(r)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--danger)]/40 text-[var(--danger)] text-[11px] cursor-pointer"
            >
              <Ban size={12} aria-hidden="true" />
              <span>إلغاء</span>
            </button>
          )}
        </div>
      ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض الموردين والدفعات يحتاج صلاحية عرض المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="الموردون والدفعات"
        description="إدارة الموردين وكشوف حساباتهم، طابور طلبات الدفع المعتمدة، وسندات الصرف."
        primaryAction={
          supplierActions.includes('create')
            ? { label: 'إضافة مورد جديد', icon: <Plus size={14} />, onClick: () => setSupplierDialogOpen(true) }
            : undefined
        }
        moreActions={[
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="الموردون المسجلون" value={suppliers.length} />
        <StatCard label="مستندات في طابور الدفع" value={payableQueue.length} />
        <StatCard
          label="إجمالي المتاح للصرف"
          value={formatSar(payableQueue.reduce((s, d) => s + Number(d.availableBalance ?? 0), 0))}
        />
        <StatCard label="سندات الصرف المفتوحة" value={vouchers.filter((v) => v.status === 1).length} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'list' && (
            <DataTable
              columns={supplierColumns}
              data={suppliers}
              emptyTitle="لا يوجد موردون مسجلون"
              emptyDescription="أضف موردًا لربط فواتير المشتريات وعقود الإيجار ومتابعة كشف حسابه."
            />
          )}
          {activeTab === 'queue' && (
            <DataTable
              columns={queueColumns}
              data={payableQueue}
              emptyTitle="طابور الدفع فارغ"
              emptyDescription="كل المستندات المعتمدة مسددة أو محجوزة بالكامل."
            />
          )}
          {activeTab === 'vouchers' && (
            <div className="space-y-3">
              <form onSubmit={lookupVoucher} className="flex flex-wrap items-center gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3">
                <input
                  className={inputClass + ' max-w-md'}
                  value={voucherLookupId}
                  onChange={(e) => setVoucherLookupId(e.target.value)}
                  placeholder="أدخل معرف سند الصرف لعرضه أو متابعة اعتماده..."
                />
                <button
                  type="submit"
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold cursor-pointer"
                >
                  <Search size={13} aria-hidden="true" />
                  <span>عرض السند</span>
                </button>
              </form>
              <DataTable
                columns={voucherColumns}
                data={vouchers}
                emptyTitle="لا توجد سندات صرف معروضة"
                emptyDescription="أصدر طلب دفع من طابور الدفع أو ابحث بمعرف سند الصرف أعلاه."
              />
            </div>
          )}
        </>
      )}

      {/* Supplier Ledger Drawer */}
      <Drawer
        open={ledgerDrawer !== null}
        onClose={() => setLedgerDrawer(null)}
        title={`كشف حساب المورد: ${String(ledgerDrawer?.supplier.nameAr || '')}`}
        subtitle="فواتير المشتريات المعتمدة والمسدد منها والمتبقي"
      >
        {ledgerDrawer && (
          <div className="space-y-4">
            <div className="grid grid-cols-3 gap-2">
              <StatCard label="إجمالي الفواتير" value={formatSar(ledgerDrawer.invoiced)} />
              <StatCard label="المسدد" value={formatSar(ledgerDrawer.paid)} />
              <StatCard label="المتبقي" value={formatSar(ledgerDrawer.outstanding)} />
            </div>
            <DataTable
              columns={[
                { key: 'reference', header: 'المرجع', type: 'text', getValue: (r) => String(r.reference || r.id || '') },
                {
                  key: 'amount',
                  header: 'المبلغ',
                  type: 'number',
                  numeric: true,
                  money: true,
                  getValue: (r) => Number(r.amount || 0),
                  render: (r) => <span className="num font-bold">{formatSar(Number(r.amount || 0))}</span>,
                  summary: 'sum',
                },
                {
                  key: 'amountPaid',
                  header: 'المسدد',
                  type: 'number',
                  numeric: true,
                  money: true,
                  getValue: (r) => Number(r.amountPaid || 0),
                  render: (r) => <span className="num">{formatSar(Number(r.amountPaid || 0))}</span>,
                  summary: 'sum',
                },
                {
                  key: 'amountReserved',
                  header: 'المحجوز',
                  type: 'number',
                  numeric: true,
                  money: true,
                  getValue: (r) => Number(r.amountReserved || 0),
                  render: (r) => <span className="num">{formatSar(Number(r.amountReserved || 0))}</span>,
                  summary: 'sum',
                },
              ]}
              data={ledgerDrawer.items}
              emptyTitle="لا توجد فواتير معتمدة"
              emptyDescription="تظهر هنا فواتير المشتريات المعتمدة لهذا المورد."
            />
          </div>
        )}
      </Drawer>

      <AddSupplierDialog
        open={supplierDialogOpen}
        onClose={() => setSupplierDialogOpen(false)}
        companyOptions={companyOptions}
        onCreated={load}
      />
    </div>
  );
}
