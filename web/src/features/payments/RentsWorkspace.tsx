'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Archive, CalendarCheck2, Building2, Download, Eye } from 'lucide-react';
import {
  api,
  ApiError,
  type OperationsHousingRow,
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
import { Drawer } from '../../components/ui/Drawer';
import { SourceBadge } from '../../components/ui/SourceBadge';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  type CoaAccountOption,
  documentStatusLabel,
  exportPaymentsSheet,
  FREQUENCY_LABELS,
  hasAllowedAction,
  loadPostableAccounts,
  NONE_COST_CENTRE_ID,
  PROPERTY_TYPE_LABELS,
} from './payment-engine';
import { AddSupplierDialog, PaymentDocumentActions } from './PaymentWorkflowPanel';

type ContractRow = Record<string, unknown> & {
  id?: string;
  companyId?: string;
  nameAr?: string;
  status?: number;
  metadata?: Record<string, unknown>;
  allowedActions?: string[];
};

type ScheduleRow = Record<string, unknown> & {
  id?: string;
  documentId?: string;
  dueOn?: string;
  principal?: number;
  interest?: number;
  status?: number;
};

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function RentsWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'contracts', label: 'العقود' },
      { id: 'schedule', label: 'جدول الدفعات والاستحقاق' },
      { id: 'properties', label: 'العقارات والمكاتب' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('contracts');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [leases, setLeases] = useState<ContractRow[]>([]);
  const [leaseActions, setLeaseActions] = useState<string[]>([]);
  const [properties, setProperties] = useState<ContractRow[]>([]);
  const [propertyActions, setPropertyActions] = useState<string[]>([]);
  const [suppliers, setSuppliers] = useState<Array<Record<string, unknown>>>([]);
  const [rentDocs, setRentDocs] = useState<PaymentDocumentItem[]>([]);
  const [erpHousing, setErpHousing] = useState<OperationsHousingRow[]>([]);

  const [leaseDialogOpen, setLeaseDialogOpen] = useState(false);
  const [propDialogOpen, setPropDialogOpen] = useState(false);
  const [supplierDialogOpen, setSupplierDialogOpen] = useState(false);
  const [scheduleDrawer, setScheduleDrawer] = useState<{
    lease: ContractRow;
    items: ScheduleRow[];
  } | null>(null);
  const [roomsDrawer, setRoomsDrawer] = useState<{
    title: string;
    rooms: Array<Record<string, unknown>>;
  } | null>(null);

  const [propForm, setPropForm] = useState({
    companyId: '',
    nameAr: '',
    propertyType: 'rider_housing',
    erpHousingId: '',
    costCentreId: NONE_COST_CENTRE_ID,
  });

  const [leaseForm, setLeaseForm] = useState({
    companyId: '',
    nameAr: '',
    propertyId: '',
    landlordSupplierId: '',
    ejarNo: '',
    startOn: `${period}-01`,
    months: '12',
    frequencyMonths: '6',
    rentPerPayment: '',
    deposit: '0',
  });

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [compList, coaRes, leaseRes, propRes, supRes, docsRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listPaymentLeases(),
        api.listPaymentProperties(),
        api.listPaymentSuppliers(),
        api.listPaymentDocuments({ type: 'rent' }),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setLeases((leaseRes?.items as unknown as ContractRow[]) || []);
      setLeaseActions(Array.isArray(leaseRes?.allowedActions) ? leaseRes.allowedActions : []);
      setProperties((propRes?.items as unknown as ContractRow[]) || []);
      setPropertyActions(Array.isArray(propRes?.allowedActions) ? propRes.allowedActions : []);
      setSuppliers((supRes?.items as unknown as Array<Record<string, unknown>>) || []);
      setRentDocs(docsRes?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setPropForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
        setLeaseForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
      api
        .getOperationsHousing({ pageSize: 200 })
        .then((res) => setErpHousing(res?.items || []))
        .catch(() => setErpHousing([]));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل بيانات الإيجارات');
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

  const propertyOptions = useMemo(
    () =>
      properties
        .filter((p) => Number(p.status) === 1 && (!leaseForm.companyId || p.companyId === leaseForm.companyId))
        .map((p) => ({ value: String(p.id || ''), label: String(p.nameAr || p.id || '') })),
    [properties, leaseForm.companyId]
  );

  const supplierOptions = useMemo(
    () =>
      suppliers
        .filter((s) => !leaseForm.companyId || String(s.companyId || '') === leaseForm.companyId)
        .map((s) => ({ value: String(s.id || ''), label: String(s.nameAr || s.id || '') })),
    [suppliers, leaseForm.companyId]
  );

  const erpHousingOptions = useMemo(
    () =>
      erpHousing.map((h) => ({
        value: String(h.id || ''),
        label: `${String(h.nameAr || h.id || '')}${h.city ? ` — ${String(h.city)}` : ''}`,
      })),
    [erpHousing]
  );

  const activeLeases = useMemo(() => leases.filter((l) => Number(l.status) === 1), [leases]);
  const dueInPeriodTotal = useMemo(
    () =>
      rentDocs
        .filter((d) => String(d.dueOn || '').startsWith(period) && d.status !== 5)
        .reduce((s, d) => s + Number(d.availableBalance ?? d.amount ?? 0), 0),
    [rentDocs, period]
  );
  const scheduledTotal = useMemo(
    () => rentDocs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(d.amount || 0), 0),
    [rentDocs]
  );

  const createProperty = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.createPaymentProperty({
        companyId: propForm.companyId,
        nameAr: propForm.nameAr.trim(),
        propertyType: propForm.propertyType,
        costCentreId: propForm.costCentreId || NONE_COST_CENTRE_ID,
        ...(propForm.propertyType === 'rider_housing' ? { erpHousingId: propForm.erpHousingId } : {}),
      });
      toast('تم تسجيل العقار بنجاح', 'success');
      setPropDialogOpen(false);
      setPropForm((p) => ({ ...p, nameAr: '', erpHousingId: '' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل العقار', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const createLease = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.createPaymentLease({
        companyId: leaseForm.companyId,
        nameAr: leaseForm.nameAr.trim(),
        propertyId: leaseForm.propertyId,
        landlordSupplierId: leaseForm.landlordSupplierId,
        ejarNo: leaseForm.ejarNo.trim(),
        startOn: leaseForm.startOn,
        months: Number(leaseForm.months),
        frequencyMonths: Number(leaseForm.frequencyMonths),
        rentPerPayment: Number(leaseForm.rentPerPayment),
        deposit: Number(leaseForm.deposit || 0),
      });
      toast('تم إنشاء عقد الإيجار وتوليد جدول الدفعات', 'success');
      setLeaseDialogOpen(false);
      setLeaseForm((p) => ({ ...p, nameAr: '', ejarNo: '', rentPerPayment: '', deposit: '0' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء عقد الإيجار', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const openSchedule = async (lease: ContractRow) => {
    if (!lease.id) return;
    setBusy(true);
    try {
      const res = await api.getPaymentLeaseSchedule(lease.id);
      setScheduleDrawer({
        lease,
        items: (res?.items as unknown as ScheduleRow[]) || [],
      });
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر قراءة جدول الدفعات', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const recognizeRent = async (lease: ContractRow) => {
    if (!lease.id) return;
    setBusy(true);
    try {
      const res = await api.recognizeLeaseRent(lease.id, period);
      toast(`تم إثبات مصروف إيجار ${period} بمبلغ ${formatSar(res.amount)}`, 'success');
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إثبات مصروف الإيجار الشهري', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const archiveRow = async (kind: 'leases' | 'properties', id?: string) => {
    if (!id) return;
    setBusy(true);
    try {
      await api.archivePaymentContract(kind, id);
      toast('تمت أرشفة السجل دون حذف', 'success');
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر الأرشفة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const openOccupancy = async (prop: ContractRow) => {
    const housingId = String(prop.metadata?.erpHousingId || '');
    if (!housingId) return;
    setBusy(true);
    try {
      const res = await api.getOperationsHousingRooms(housingId);
      setRoomsDrawer({
        title: `إشغال ${String(prop.nameAr || '')}`,
        rooms: (res?.rooms as unknown as Array<Record<string, unknown>>) || [],
      });
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر قراءة بيانات إشغال السكن من نظام الموارد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      if (activeTab === 'schedule') {
        await exportPaymentsSheet(
          ['رقم المستند', 'الشركة', 'المرجع', 'تاريخ الاستحقاق', 'المبلغ', 'المدفوع', 'الحالة'],
          rentDocs.map((d) => [
            d.docNo || d.id,
            companyName(d.companyId),
            d.reference || '',
            String(d.dueOn || '').slice(0, 10),
            Number(d.amount || 0),
            Number(d.amountPaid || 0),
            documentStatusLabel(d),
          ]),
          [4, 5],
          `rents-schedule-${period}`
        );
      } else {
        await exportPaymentsSheet(
          ['العقد', 'الشركة', 'رقم إيجار', 'الدورة', 'قيمة الدفعة', 'الحالة'],
          leases.map((l) => [
            String(l.nameAr || ''),
            companyName(l.companyId),
            String(l.metadata?.ejarNo || ''),
            FREQUENCY_LABELS[Number(l.metadata?.frequencyMonths)] || String(l.metadata?.frequencyMonths || ''),
            Number(l.metadata?.rentPerPayment || 0),
            Number(l.status) === 1 ? 'نشط' : 'مؤرشف',
          ]),
          [4],
          `rents-contracts-${period}`
        );
      }
      toast('تم تصدير ملف الإيجارات', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const leaseColumns: ColumnDef<ContractRow>[] = [
    {
      key: 'nameAr',
      header: 'العقد والعقار',
      type: 'text',
      getValue: (r) => String(r.nameAr || ''),
      render: (r) => (
        <div className="space-y-0.5">
          <div className="font-bold text-[var(--ink)]">{String(r.nameAr || '—')}</div>
          {r.metadata?.ejarNo ? (
            <div className="text-[11px] text-[var(--muted)]">
              رقم إيجار: <bdi>{String(r.metadata.ejarNo)}</bdi>
            </div>
          ) : null}
        </div>
      ),
    },
    {
      key: 'companyId',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => companyName(r.companyId),
    },
    {
      key: 'frequencyMonths',
      header: 'المدة ودورة السداد',
      type: 'list',
      getValue: (r) =>
        `${Number(r.metadata?.months || 0)} شهر · ${FREQUENCY_LABELS[Number(r.metadata?.frequencyMonths)] || '—'}`,
    },
    {
      key: 'rentPerPayment',
      header: 'قيمة الدفعة',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.metadata?.rentPerPayment || 0),
      render: (r) => <span className="num font-bold">{formatSar(Number(r.metadata?.rentPerPayment || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'deposit',
      header: 'الوديعة',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.metadata?.deposit || 0),
      render: (r) => <span className="num">{formatSar(Number(r.metadata?.deposit || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => (Number(r.status) === 1 ? 'نشط' : 'مؤرشف'),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <div className="flex flex-wrap items-center gap-1.5">
          <button
            type="button"
            disabled={busy}
            onClick={() => void openSchedule(r)}
            className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[11px] font-semibold hover:bg-[var(--raised)] cursor-pointer"
          >
            <Eye size={12} aria-hidden="true" />
            <span>جدول الأقساط</span>
          </button>
          {canDisburse && Number(r.status) === 1 && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void recognizeRent(r)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--accent)] text-[var(--accent)] text-[11px] font-semibold hover:bg-[var(--accent)]/10 cursor-pointer"
            >
              <CalendarCheck2 size={12} aria-hidden="true" />
              <span>توزيع شهر {period}</span>
            </button>
          )}
          {hasAllowedAction(r, 'archive') && Number(r.status) === 1 && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void archiveRow('leases', r.id)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[var(--muted)] text-[11px] hover:text-[var(--danger)] cursor-pointer"
            >
              <Archive size={12} aria-hidden="true" />
              <span>أرشفة</span>
            </button>
          )}
        </div>
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
      key: 'companyId',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => companyName(r.companyId),
    },
    {
      key: 'dueOn',
      header: 'تاريخ الاستحقاق',
      type: 'date',
      getValue: (r) => String(r.dueOn || '').slice(0, 10),
    },
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
      key: 'amountPaid',
      header: 'المدفوع',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.amountPaid || 0),
      render: (r) => <span className="num">{formatSar(r.amountPaid ?? 0)}</span>,
      summary: 'sum',
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => documentStatusLabel(r),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <PaymentDocumentActions doc={r} period={period} accounts={accounts} onUpdated={load} />
      ),
    },
  ];

  const propColumns: ColumnDef<ContractRow>[] = [
    {
      key: 'nameAr',
      header: 'اسم العقار / المكتب',
      type: 'text',
      getValue: (r) => String(r.nameAr || ''),
    },
    {
      key: 'propertyType',
      header: 'النوع',
      type: 'list',
      getValue: (r) => PROPERTY_TYPE_LABELS[String(r.metadata?.propertyType || '')] || '—',
    },
    {
      key: 'companyId',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => companyName(r.companyId),
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => (Number(r.status) === 1 ? 'نشط' : 'مؤرشف'),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) => (
        <div className="flex flex-wrap items-center gap-1.5">
          {r.metadata?.erpHousingId ? (
            <button
              type="button"
              disabled={busy}
              onClick={() => void openOccupancy(r)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[11px] font-semibold hover:bg-[var(--raised)] cursor-pointer"
            >
              <Building2 size={12} aria-hidden="true" />
              <span>إشغال السكن (الموارد)</span>
            </button>
          ) : null}
          {hasAllowedAction(r, 'archive') && Number(r.status) === 1 && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void archiveRow('properties', r.id)}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[var(--muted)] text-[11px] hover:text-[var(--danger)] cursor-pointer"
            >
              <Archive size={12} aria-hidden="true" />
              <span>أرشفة</span>
            </button>
          )}
        </div>
      ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض الإيجارات والسكنات يحتاج صلاحية عرض المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="الإيجارات والسكنات"
        description="إدارة عقود الإيجار، جدول الدفعات، توزيع الإيجار المقدم الشهري، والعقارات والمكاتب."
        primaryAction={
          activeTab === 'properties' && propertyActions.includes('create')
            ? {
                label: 'إضافة عقار / مكتب',
                icon: <Plus size={14} />,
                onClick: () => setPropDialogOpen(true),
              }
            : leaseActions.includes('create')
              ? {
                  label: 'إنشاء عقد إيجار',
                  icon: <Plus size={14} />,
                  onClick: () => setLeaseDialogOpen(true),
                }
              : undefined
        }
        moreActions={[
          {
            label: 'تصدير',
            icon: <Download size={14} />,
            onClick: () => void handleExport(),
          },
          {
            label: 'تحديث',
            icon: <RefreshCw size={14} />,
            onClick: () => void load(),
            disabled: loading || busy,
          },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="العقود النشطة" value={activeLeases.length} />
        <StatCard label={`مستحق في ${period}`} value={formatSar(dueInPeriodTotal)} />
        <StatCard label="إجمالي الإيجارات المجدولة" value={formatSar(scheduledTotal)} />
        <StatCard label="العقارات والمكاتب المسجلة" value={properties.length} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر تحميل الإيجارات" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'contracts' && (
            <DataTable
              columns={leaseColumns}
              data={leases}
              emptyTitle="لا توجد عقود إيجار"
              emptyDescription="سجّل عقارًا أولًا ثم أنشئ عقد إيجار لتوليد جدول الدفعات تلقائيًا."
            />
          )}
          {activeTab === 'schedule' && (
            <DataTable
              columns={docColumns}
              data={rentDocs}
              emptyTitle="لا توجد دفعات إيجار مجدولة"
              emptyDescription="تُنشأ دفعات الإيجار تلقائيًا عند تسجيل عقد إيجار جديد."
            />
          )}
          {activeTab === 'properties' && (
            <DataTable
              columns={propColumns}
              data={properties}
              emptyTitle="لا توجد عقارات أو مكاتب مسجلة"
              emptyDescription="أضف سكن مناديب أو سكن إداري أو مكتب لربطه بعقود الإيجار."
            />
          )}
        </>
      )}

      {/* Create Property Dialog */}
      <Dialog open={propDialogOpen} onClose={() => !busy && setPropDialogOpen(false)} title="إضافة عقار أو مكتب" size="md">
        <form onSubmit={createProperty} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة</span>
              <SearchableSelect
                value={propForm.companyId}
                onChange={(v) => setPropForm((p) => ({ ...p, companyId: v }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">نوع العقار</span>
              <SearchableSelect
                value={propForm.propertyType}
                onChange={(v) => setPropForm((p) => ({ ...p, propertyType: v }))}
                options={[
                  { value: 'rider_housing', label: 'سكن مناديب (حساب 512109)' },
                  { value: 'admin_housing', label: 'سكن إداري (حساب 521102)' },
                  { value: 'office', label: 'مكتب (حساب 522101)' },
                ]}
              />
            </div>
          </div>
          <label className="block space-y-1">
            <span className="text-[var(--muted)]">اسم العقار / المكتب</span>
            <input
              className={inputClass}
              value={propForm.nameAr}
              onChange={(e) => setPropForm((p) => ({ ...p, nameAr: e.target.value }))}
              required
            />
          </label>
          {propForm.propertyType === 'rider_housing' && (
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">ربط سكن المناديب في نظام الموارد</span>
                <SourceBadge source="live" />
              </div>
              <SearchableSelect
                value={propForm.erpHousingId}
                onChange={(v) => setPropForm((p) => ({ ...p, erpHousingId: v }))}
                options={erpHousingOptions}
                placeholder="اختر السكن من نظام الموارد..."
              />
            </div>
          )}
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setPropDialogOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ العقار
            </button>
          </div>
        </form>
      </Dialog>

      {/* Create Lease Dialog */}
      <Dialog open={leaseDialogOpen} onClose={() => !busy && setLeaseDialogOpen(false)} title="إنشاء عقد إيجار جديد" size="lg">
        <form onSubmit={createLease} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة</span>
              <SearchableSelect
                value={leaseForm.companyId}
                onChange={(v) => setLeaseForm((p) => ({ ...p, companyId: v, propertyId: '', landlordSupplierId: '' }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">اسم العقد</span>
              <input
                className={inputClass}
                value={leaseForm.nameAr}
                onChange={(e) => setLeaseForm((p) => ({ ...p, nameAr: e.target.value }))}
                required
              />
            </label>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">العقار</span>
              <SearchableSelect
                value={leaseForm.propertyId}
                onChange={(v) => setLeaseForm((p) => ({ ...p, propertyId: v }))}
                options={propertyOptions}
                placeholder="اختر العقار..."
              />
            </div>
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">المؤجر (مورد)</span>
                {canDisburse && (
                  <button
                    type="button"
                    onClick={() => setSupplierDialogOpen(true)}
                    className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                  >
                    + إضافة مؤجر
                  </button>
                )}
              </div>
              <SearchableSelect
                value={leaseForm.landlordSupplierId}
                onChange={(v) => setLeaseForm((p) => ({ ...p, landlordSupplierId: v }))}
                options={supplierOptions}
                placeholder="اختر المؤجر..."
              />
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">رقم عقد إيجار</span>
              <input
                className={inputClass}
                value={leaseForm.ejarNo}
                onChange={(e) => setLeaseForm((p) => ({ ...p, ejarNo: e.target.value }))}
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ البداية</span>
              <input
                type="date"
                className={inputClass}
                value={leaseForm.startOn}
                onChange={(e) => setLeaseForm((p) => ({ ...p, startOn: e.target.value }))}
                required
              />
            </label>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">دورة السداد</span>
              <SearchableSelect
                value={leaseForm.frequencyMonths}
                onChange={(v) => setLeaseForm((p) => ({ ...p, frequencyMonths: v }))}
                options={[
                  { value: '3', label: 'ربع سنوي (كل 3 أشهر)' },
                  { value: '6', label: 'نصف سنوي (كل 6 أشهر)' },
                  { value: '12', label: 'سنوي (كل 12 شهرًا)' },
                ]}
              />
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">مدة العقد (بالأشهر)</span>
              <input
                type="number"
                min="3"
                max="360"
                className={inputClass}
                value={leaseForm.months}
                onChange={(e) => setLeaseForm((p) => ({ ...p, months: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">قيمة الدفعة</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={leaseForm.rentPerPayment}
                onChange={(e) => setLeaseForm((p) => ({ ...p, rentPerPayment: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">الوديعة (125101)</span>
              <input
                type="number"
                step="0.01"
                min="0"
                className={inputClass}
                value={leaseForm.deposit}
                onChange={(e) => setLeaseForm((p) => ({ ...p, deposit: e.target.value }))}
              />
            </label>
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setLeaseDialogOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ العقد وتوليد الجدول
            </button>
          </div>
        </form>
      </Dialog>

      {/* Schedule Drawer */}
      <Drawer
        open={scheduleDrawer !== null}
        onClose={() => setScheduleDrawer(null)}
        title={`جدول دفعات: ${String(scheduleDrawer?.lease.nameAr || '')}`}
        subtitle="الدفعات المولّدة على محرك المدفوعات"
      >
        {scheduleDrawer && (
          <DataTable
            columns={[
              { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
              {
                key: 'principal',
                header: 'المبلغ',
                type: 'number',
                numeric: true,
                money: true,
                getValue: (r) => Number(r.principal || 0),
                render: (r) => <span className="num font-bold">{formatSar(Number(r.principal || 0))}</span>,
                summary: 'sum',
              },
              {
                key: 'status',
                header: 'الحالة',
                type: 'list',
                getValue: (r) => documentStatusLabel({ status: Number(r.status || 1), amount: Number(r.principal || 0) }),
              },
            ]}
            data={scheduleDrawer.items}
            emptyTitle="لا توجد دفعات"
            emptyDescription="لا توجد دفعات مجدولة لهذا العقد."
          />
        )}
      </Drawer>

      {/* Live ERP Occupancy Drawer */}
      <Drawer
        open={roomsDrawer !== null}
        onClose={() => setRoomsDrawer(null)}
        title={roomsDrawer?.title || 'إشغال السكن'}
        subtitle="قراءة مباشرة من نظام الموارد"
      >
        {roomsDrawer && (
          <DataTable
            columns={[
              { key: 'roomNumber', header: 'الغرفة', type: 'text', getValue: (r) => String(r.roomNumber || r.name || '—') },
              { key: 'capacity', header: 'السعة', type: 'number', getValue: (r) => Number(r.capacity || 0) },
              { key: 'occupied', header: 'المشغول', type: 'number', getValue: (r) => Number(r.occupiedCount ?? r.occupied ?? 0) },
            ]}
            data={roomsDrawer.rooms}
            emptyTitle="لا توجد غرف مسجلة"
            emptyDescription="لم تُسجّل غرف لهذا السكن في نظام الموارد."
          />
        )}
      </Drawer>

      <AddSupplierDialog
        open={supplierDialogOpen}
        onClose={() => setSupplierDialogOpen(false)}
        companyOptions={companyOptions}
        defaultCompanyId={leaseForm.companyId}
        onCreated={async (id) => {
          await load();
          if (id) setLeaseForm((p) => ({ ...p, landlordSupplierId: id }));
        }}
      />
    </div>
  );
}
