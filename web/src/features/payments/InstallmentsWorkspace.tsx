'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { RefreshCw, Plus, Archive, Download, Eye } from 'lucide-react';
import {
  api,
  ApiError,
  type OperationsPeriodRow,
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
import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor, DebtRecord } from '../../lib/types';
import {
  type CoaAccountOption,
  documentStatusLabel,
  exportPaymentsSheet,
  hasAllowedAction,
  loadPostableAccounts,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

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

export function InstallmentsWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'contracts', label: 'عقود التمويل' },
      { id: 'schedule', label: 'جدول الأقساط' },
      { id: 'accidents', label: 'أقساط فترات الحوادث' },
      { id: 'financiers', label: 'لوحة الممولين' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('contracts');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [contracts, setContracts] = useState<ContractRow[]>([]);
  const [contractActions, setContractActions] = useState<string[]>([]);
  const [financiers, setFinanciers] = useState<ContractRow[]>([]);
  const [financierActions, setFinancierActions] = useState<string[]>([]);
  const [installmentDocs, setInstallmentDocs] = useState<PaymentDocumentItem[]>([]);
  const [accidentDebts, setAccidentDebts] = useState<DebtRecord[]>([]);
  const [vehicles, setVehicles] = useState<OperationsPeriodRow[]>([]);

  const [contractDialogOpen, setContractDialogOpen] = useState(false);
  const [financierDialogOpen, setFinancierDialogOpen] = useState(false);
  const [scheduleDrawer, setScheduleDrawer] = useState<{
    contract: ContractRow;
    items: ScheduleRow[];
  } | null>(null);

  const [financierForm, setFinancierForm] = useState({ companyId: '', nameAr: '', contactNote: '' });
  const [contractForm, setContractForm] = useState({
    companyId: '',
    nameAr: '',
    financierId: '',
    erpVehicleId: '',
    startOn: `${period}-01`,
    months: '24',
    principal: '',
    monthlyEffectiveRate: '0.005',
    liabilityAccountCode: '221201',
    costCentreId: NONE_COST_CENTRE_ID,
  });

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [compList, coaRes, conRes, finRes, docsRes, accidentRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listFinanceContracts(),
        api.listPaymentFinanciers(),
        api.listPaymentDocuments({ type: 'installment' }),
        api.getDebts({ category: 'AccidentDamage', page: 1, pageSize: 200 }),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setContracts((conRes?.items as unknown as ContractRow[]) || []);
      setContractActions(Array.isArray(conRes?.allowedActions) ? conRes.allowedActions : []);
      setFinanciers((finRes?.items as unknown as ContractRow[]) || []);
      setFinancierActions(Array.isArray(finRes?.allowedActions) ? finRes.allowedActions : []);
      setInstallmentDocs(docsRes?.items || []);
      const accidentItems = Array.isArray(accidentRes)
        ? accidentRes
        : Array.isArray((accidentRes as { items?: DebtRecord[] })?.items)
          ? (accidentRes as { items: DebtRecord[] }).items
          : [];
      setAccidentDebts(accidentItems);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setFinancierForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
        setContractForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
      const { from, to } = periodDateRange(period);
      api
        .getOperationsPeriodReport({ from, to, mode: 'vehicle', pageSize: 200 })
        .then((res) => setVehicles(res?.items || []))
        .catch(() => setVehicles([]));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل بيانات الأقساط والتمويل');
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

  const financierOptions = useMemo(
    () =>
      financiers
        .filter((f) => Number(f.status) === 1 && (!contractForm.companyId || f.companyId === contractForm.companyId))
        .map((f) => ({ value: String(f.id || ''), label: String(f.nameAr || f.id || '') })),
    [financiers, contractForm.companyId]
  );

  const vehicleOptions = useMemo(
    () =>
      vehicles.map((v) => ({
        value: String(v.id || ''),
        label: String(v.plateNumberAr || v.assetNumber || v.id || 'مركبة'),
      })),
    [vehicles]
  );

  const activeContracts = useMemo(() => contracts.filter((c) => Number(c.status) === 1), [contracts]);
  const dueInPeriodTotal = useMemo(
    () =>
      installmentDocs
        .filter((d) => String(d.dueOn || '').startsWith(period) && d.status !== 5)
        .reduce((s, d) => s + Number(d.availableBalance ?? d.amount ?? 0), 0),
    [installmentDocs, period]
  );
  const totalPrincipal = useMemo(
    () => activeContracts.reduce((s, c) => s + Number(c.metadata?.principal || 0), 0),
    [activeContracts]
  );

  const createFinancier = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.createPaymentFinancier({
        companyId: financierForm.companyId,
        nameAr: financierForm.nameAr.trim(),
        contactNote: financierForm.contactNote.trim(),
      });
      toast('تم تسجيل الممول بنجاح', 'success');
      setFinancierDialogOpen(false);
      setFinancierForm((p) => ({ ...p, nameAr: '', contactNote: '' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل الممول', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const createContract = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.createFinanceContract({
        companyId: contractForm.companyId,
        nameAr: contractForm.nameAr.trim(),
        financierId: contractForm.financierId,
        erpVehicleId: contractForm.erpVehicleId,
        startOn: contractForm.startOn,
        months: Number(contractForm.months),
        principal: Number(contractForm.principal),
        monthlyEffectiveRate: Number(contractForm.monthlyEffectiveRate || 0),
        liabilityAccountCode: contractForm.liabilityAccountCode,
        costCentreId: contractForm.costCentreId || NONE_COST_CENTRE_ID,
      });
      toast('تم إنشاء عقد التمويل وتوليد جدول الأقساط', 'success');
      setContractDialogOpen(false);
      setContractForm((p) => ({ ...p, nameAr: '', principal: '', erpVehicleId: '' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء عقد التمويل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const openSchedule = async (contract: ContractRow) => {
    if (!contract.id) return;
    setBusy(true);
    try {
      const res = await api.getFinanceContractSchedule(contract.id);
      setScheduleDrawer({
        contract,
        items: (res?.items as unknown as ScheduleRow[]) || [],
      });
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر قراءة جدول الأقساط', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const archiveRow = async (kind: 'finance-contracts' | 'financiers', id?: string) => {
    if (!id) return;
    setBusy(true);
    try {
      await api.archivePaymentContract(kind, id);
      toast('تمت الأرشفة دون حذف', 'success');
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر الأرشفة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم المستند', 'الشركة', 'الاستحقاق', 'المبلغ', 'المدفوع', 'الحالة'],
        installmentDocs.map((d) => [
          d.docNo || d.id,
          companyName(d.companyId),
          String(d.dueOn || '').slice(0, 10),
          Number(d.amount || 0),
          Number(d.amountPaid || 0),
          documentStatusLabel(d),
        ]),
        [3, 4],
        `installments-${period}`
      );
      toast('تم تصدير جدول الأقساط', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const contractColumns: ColumnDef<ContractRow>[] = [
    { key: 'nameAr', header: 'العقد', type: 'text', getValue: (r) => String(r.nameAr || '') },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'principal',
      header: 'أصل التمويل',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.metadata?.principal || 0),
      render: (r) => <span className="num font-bold">{formatSar(Number(r.metadata?.principal || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'months',
      header: 'المدة (أشهر)',
      type: 'number',
      getValue: (r) => Number(r.metadata?.months || 0),
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
          {hasAllowedAction(r, 'archive') && Number(r.status) === 1 && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void archiveRow('finance-contracts', r.id)}
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
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
    {
      key: 'amount',
      header: 'قيمة القسط',
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

  const financierColumns: ColumnDef<ContractRow>[] = [
    { key: 'nameAr', header: 'الممول / الجهة البنكية', type: 'text', getValue: (r) => String(r.nameAr || '') },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    { key: 'status', header: 'الحالة', type: 'list', getValue: (r) => (Number(r.status) === 1 ? 'نشط' : 'مؤرشف') },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: (r) =>
        hasAllowedAction(r, 'archive') && Number(r.status) === 1 ? (
          <button
            type="button"
            disabled={busy}
            onClick={() => void archiveRow('financiers', r.id)}
            className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[var(--muted)] text-[11px] hover:text-[var(--danger)] cursor-pointer"
          >
            <Archive size={12} aria-hidden="true" />
            <span>أرشفة</span>
          </button>
        ) : (
          <span className="text-[11px] text-[var(--muted)]">—</span>
        ),
    },
  ];

  const accidentColumns: ColumnDef<DebtRecord>[] = [
    {
      key: 'iqama',
      header: 'هوية المندوب',
      type: 'text',
      getValue: (r) => r.iqama,
      render: (r) => <bdi className="font-mono text-xs">{r.iqama || '—'}</bdi>,
    },
    {
      key: 'categoryAr',
      header: 'التصنيف',
      type: 'list',
      getValue: (r) => String(r.categoryAr || r.category || 'تحمل حادث'),
    },
    {
      key: 'originalAmount',
      header: 'أصل المطالبة',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.originalAmount || 0),
      render: (r) => <span className="num font-bold">{formatSar(r.originalAmount)}</span>,
      summary: 'sum',
    },
    {
      key: 'remainingAmount',
      header: 'المتبقي',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.remainingAmount || 0),
      render: (r) => <span className="num">{formatSar(r.remainingAmount)}</span>,
      summary: 'sum',
    },
    {
      key: 'monthlyInstallment',
      header: 'القسط الشهري',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.monthlyInstallment || 0),
      render: (r) => <span className="num">{formatSar(r.monthlyInstallment ?? 0)}</span>,
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => {
        const map: Record<string, string> = {
          active: 'نشطة',
          settled: 'مسددة',
          waived: 'متنازل عنها',
          disputed: 'متنازع عليها',
          pending_approval: 'بانتظار الاعتماد',
          cancelled: 'ملغاة',
        };
        return map[r.status] || r.status || '—';
      },
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      sortable: false,
      getValue: () => '',
      render: () => (
        <Link
          href={`/debts/?tab=debts&period=${encodeURIComponent(period)}`}
          className="inline-flex items-center gap-1 px-2 py-1 rounded-lg border border-[var(--line)] text-[11px] font-semibold text-[var(--accent)] hover:underline"
        >
          فتح سجل المديونيات
        </Link>
      ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض الأقساط والمدفوعات البنكية يحتاج صلاحية عرض المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="الأقساط والمدفوعات البنكية"
        description="عقود تمويل المركبات، جدول الأقساط بأصل الالتزام وتكلفة التمويل، ولوحة الممولين."
        primaryAction={
          activeTab === 'financiers' && financierActions.includes('create')
            ? { label: 'إضافة ممول', icon: <Plus size={14} />, onClick: () => setFinancierDialogOpen(true) }
            : contractActions.includes('create')
              ? { label: 'إنشاء عقد تمويل', icon: <Plus size={14} />, onClick: () => setContractDialogOpen(true) }
              : undefined
        }
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <StatCard label="عقود التمويل النشطة" value={activeContracts.length} />
        <StatCard label={`أقساط مستحقة في ${period}`} value={formatSar(dueInPeriodTotal)} />
        <StatCard label="أصل الالتزام المسجل (221201)" value={formatSar(totalPrincipal)} />
        <StatCard
          label="مطالبات حوادث مفتوحة"
          value={accidentDebts.filter((d) => d.status !== 'settled' && d.status !== 'waived' && d.status !== 'cancelled').length}
        />
        <StatCard label="الممولون النشطون" value={financiers.filter((f) => Number(f.status) === 1).length} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'contracts' && (
            <DataTable
              columns={contractColumns}
              data={contracts}
              emptyTitle="لا توجد عقود تمويل"
              emptyDescription="أضف ممولًا أولًا ثم أنشئ عقد تمويل لتوليد جدول الأقساط."
            />
          )}
          {activeTab === 'schedule' && (
            <DataTable
              columns={docColumns}
              data={installmentDocs}
              emptyTitle="لا توجد أقساط مجدولة"
              emptyDescription="الأقساط تُولّد تلقائيًا عند تسجيل عقد التمويل."
            />
          )}
          {activeTab === 'accidents' && (
            <div className="space-y-3">
              <p className="text-xs text-[var(--muted)] leading-relaxed">
                أقساط تحمل الحوادث المسجّلة في نظام المديونيات (تصنيف تحمل حادث). التحصيل والاعتماد من شاشة المديونيات.
              </p>
              <DataTable
                columns={accidentColumns}
                data={accidentDebts}
                emptyTitle="لا توجد أقساط حوادث مفتوحة"
                emptyDescription="عند تسجيل مديونية تحمل حادث تظهر أقساطها هنا للمتابعة."
              />
            </div>
          )}
          {activeTab === 'financiers' && (
            <DataTable
              columns={financierColumns}
              data={financiers}
              emptyTitle="لا يوجد ممولون مسجلون"
              emptyDescription="أضف البنوك أو شركات التمويل لربطها بعقود المركبات."
            />
          )}
        </>
      )}

      {/* Create Financier Dialog */}
      <Dialog open={financierDialogOpen} onClose={() => !busy && setFinancierDialogOpen(false)} title="إضافة ممول جديد" size="sm">
        <form onSubmit={createFinancier} className="space-y-3 text-xs">
          <div className="space-y-1">
            <span className="block text-[var(--muted)]">الشركة</span>
            <SearchableSelect
              value={financierForm.companyId}
              onChange={(v) => setFinancierForm((p) => ({ ...p, companyId: v }))}
              options={companyOptions}
              placeholder="اختر الشركة..."
            />
          </div>
          <label className="block space-y-1">
            <span className="text-[var(--muted)]">اسم الممول / البنك</span>
            <input
              className={inputClass}
              value={financierForm.nameAr}
              onChange={(e) => setFinancierForm((p) => ({ ...p, nameAr: e.target.value }))}
              required
            />
          </label>
          <label className="block space-y-1">
            <span className="text-[var(--muted)]">ملاحظة / جهة تواصل (اختياري)</span>
            <input
              className={inputClass}
              value={financierForm.contactNote}
              onChange={(e) => setFinancierForm((p) => ({ ...p, contactNote: e.target.value }))}
            />
          </label>
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setFinancierDialogOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ الممول
            </button>
          </div>
        </form>
      </Dialog>

      {/* Create Finance Contract Dialog */}
      <Dialog open={contractDialogOpen} onClose={() => !busy && setContractDialogOpen(false)} title="إنشاء عقد تمويل مركبة" size="lg">
        <form onSubmit={createContract} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة</span>
              <SearchableSelect
                value={contractForm.companyId}
                onChange={(v) => setContractForm((p) => ({ ...p, companyId: v, financierId: '' }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">اسم العقد</span>
              <input
                className={inputClass}
                value={contractForm.nameAr}
                onChange={(e) => setContractForm((p) => ({ ...p, nameAr: e.target.value }))}
                required
              />
            </label>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الممول</span>
              <SearchableSelect
                value={contractForm.financierId}
                onChange={(v) => setContractForm((p) => ({ ...p, financierId: v }))}
                options={financierOptions}
                placeholder="اختر الممول..."
              />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المركبة (مباشر من الموارد)</span>
              <SearchableSelect
                value={contractForm.erpVehicleId}
                onChange={(v) => setContractForm((p) => ({ ...p, erpVehicleId: v }))}
                options={vehicleOptions}
                placeholder="اختر لوحة المركبة..."
              />
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ أول قسط</span>
              <input
                type="date"
                className={inputClass}
                value={contractForm.startOn}
                onChange={(e) => setContractForm((p) => ({ ...p, startOn: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">عدد الأقساط (أشهر)</span>
              <input
                type="number"
                min="1"
                max="360"
                className={inputClass}
                value={contractForm.months}
                onChange={(e) => setContractForm((p) => ({ ...p, months: e.target.value }))}
                required
              />
            </label>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">حساب أصل الالتزام</span>
              <SearchableSelect
                value={contractForm.liabilityAccountCode}
                onChange={(v) => setContractForm((p) => ({ ...p, liabilityAccountCode: v }))}
                options={[
                  { value: '221201', label: '221201 — التزامات تمويل طويل الأجل' },
                  { value: '214203', label: '214203 — أقساط تمويل قصيرة الأجل' },
                ]}
              />
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">أصل مبلغ التمويل</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={contractForm.principal}
                onChange={(e) => setContractForm((p) => ({ ...p, principal: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">معدل الفائدة الشهري الفعلي (0 إلى 1)</span>
              <input
                type="number"
                step="0.0001"
                min="0"
                max="1"
                className={inputClass}
                value={contractForm.monthlyEffectiveRate}
                onChange={(e) => setContractForm((p) => ({ ...p, monthlyEffectiveRate: e.target.value }))}
                required
              />
            </label>
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setContractDialogOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
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
        title={`جدول أقساط: ${String(scheduleDrawer?.contract.nameAr || '')}`}
        subtitle="توزيع أصل الالتزام وتكلفة التمويل (541301)"
      >
        {scheduleDrawer && (
          <DataTable
            columns={[
              { key: 'dueOn', header: 'تاريخ الاستحقاق', type: 'date', getValue: (r) => String(r.dueOn || '').slice(0, 10) },
              {
                key: 'principal',
                header: 'أصل القسط',
                type: 'number',
                numeric: true,
                money: true,
                getValue: (r) => Number(r.principal || 0),
                render: (r) => <span className="num">{formatSar(Number(r.principal || 0))}</span>,
                summary: 'sum',
              },
              {
                key: 'interest',
                header: 'تكلفة التمويل',
                type: 'number',
                numeric: true,
                money: true,
                getValue: (r) => Number(r.interest || 0),
                render: (r) => <span className="num">{formatSar(Number(r.interest || 0))}</span>,
                summary: 'sum',
              },
              {
                key: 'total',
                header: 'إجمالي القسط',
                type: 'number',
                numeric: true,
                money: true,
                getValue: (r) => Number(r.principal || 0) + Number(r.interest || 0),
                render: (r) => <span className="num font-bold">{formatSar(Number(r.principal || 0) + Number(r.interest || 0))}</span>,
                summary: 'sum',
              },
            ]}
            data={scheduleDrawer.items}
            emptyTitle="لا توجد أقساط"
            emptyDescription="لم تُسجّل أقساط لهذا العقد."
          />
        )}
      </Drawer>
    </div>
  );
}
