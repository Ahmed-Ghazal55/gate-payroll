'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { RefreshCw, Plus, Download, AlertTriangle } from 'lucide-react';
import {
  api,
  ApiError,
  type OperationsPeriodRow,
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
import { Dialog } from '../../components/ui/Dialog';
import { formatSar } from '../../lib/money';
import { periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import {
  accountOptions,
  BEARER_LABELS,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentMeta,
  documentStatusLabel,
  exportPaymentsSheet,
  loadPostableAccounts,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function TrafficViolationsWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || can('payments.disburse') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'open', label: 'مخالفات للسداد' },
      { id: 'paid', label: 'السجل والمدفوعة' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('open');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [docs, setDocs] = useState<PaymentDocumentItem[]>([]);
  const [vehicles, setVehicles] = useState<OperationsPeriodRow[]>([]);

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedRider, setSelectedRider] = useState<RiderOption | null>(null);
  const [riderAccounts, setRiderAccounts] = useState<RiderAccountRow[]>([]);
  const [form, setForm] = useState({
    companyId: '',
    violationNo: '',
    vehiclePlate: '',
    erpEmployeeId: '',
    erpPlatformAccountId: '',
    bearer: 'rider' as 'rider' | 'company' | 'split',
    amount: '',
    riderShare: '',
    companyShare: '0',
    companyExpenseAccountCode: '512108',
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
      const [compList, coaRes, res] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listTrafficViolations(),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setDocs(res?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
      const { from, to } = periodDateRange(period);
      api
        .getOperationsPeriodReport({ from, to, mode: 'vehicle', pageSize: 200 })
        .then((v) => setVehicles(v?.items || []))
        .catch(() => setVehicles([]));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل سجل المخالفات المرورية');
    } finally {
      setLoading(false);
    }
  }, [canView, period]);

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
    setForm((p) => ({ ...p, erpEmployeeId: empId, erpPlatformAccountId: '' }));
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

  const vehicleOptions = useMemo(
    () =>
      vehicles.map((v) => {
        const plate = String(v.plateNumberAr || v.assetNumber || v.id || '');
        return { value: plate, label: plate };
      }),
    [vehicles]
  );

  const platformAccountOptions = useMemo(
    () =>
      riderAccounts.map((a) => ({
        value: String(a.id || ''),
        label: `${String(a.platformCode || 'منصة')} — ${String(a.accountCode || a.platformAccountId || a.id || '')}`,
      })),
    [riderAccounts]
  );

  const expenseAccountOptions = useMemo(() => accountOptions(accounts), [accounts]);

  const openDocs = useMemo(
    () => docs.filter((d) => d.status === 1 || d.status === 2 || (d.status === 3 && Number(d.availableBalance ?? 0) > 0)),
    [docs]
  );
  const paidDocs = useMemo(
    () =>
      docs.filter(
        (d) =>
          d.status === 4 ||
          d.status === 5 ||
          (d.status === 3 && Number(d.amountPaid ?? 0) >= Number(d.amount || 0))
      ),
    [docs]
  );

  const onBearerOrAmountChange = (bearer: 'rider' | 'company' | 'split', amountStr: string) => {
    const n = Number(amountStr);
    if (!Number.isFinite(n) || n <= 0) {
      setForm((p) => ({ ...p, bearer, amount: amountStr }));
      return;
    }
    if (bearer === 'rider') {
      setForm((p) => ({ ...p, bearer, amount: amountStr, riderShare: n.toFixed(2), companyShare: '0.00' }));
    } else if (bearer === 'company') {
      setForm((p) => ({ ...p, bearer, amount: amountStr, riderShare: '0.00', companyShare: n.toFixed(2) }));
    } else {
      const half = Math.round((n / 2) * 100) / 100;
      const rest = Math.round((n - half) * 100) / 100;
      setForm((p) => ({ ...p, bearer, amount: amountStr, riderShare: half.toFixed(2), companyShare: rest.toFixed(2) }));
    }
  };

  const submitViolation = async (e: React.FormEvent) => {
    e.preventDefault();
    const total = Number(form.amount);
    const riderShare = Number(form.riderShare || 0);
    const companyShare = Number(form.companyShare || 0);
    if (!Number.isFinite(total) || total <= 0) {
      toast('أدخل مبلغ مخالفة صالحًا', 'warning');
      return;
    }
    if (Math.abs(riderShare + companyShare - total) > 0.001) {
      toast('يجب أن يساوي مجموع حصة المندوب وحصة الشركة إجمالي المخالفة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.createTrafficViolation({
        companyId: form.companyId,
        erpEmployeeId: form.erpEmployeeId,
        erpPlatformAccountId: form.erpPlatformAccountId,
        vehiclePlate: form.vehiclePlate,
        riderIqama: selectedRider?.iqama || '',
        bearer: form.bearer,
        amount: total,
        riderShare,
        companyShare,
        ...(companyShare > 0 ? { companyExpenseAccountCode: form.companyExpenseAccountCode } : {}),
        reference: form.violationNo.trim(),
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        costCentreId: NONE_COST_CENTRE_ID,
        idempotencyKey: newIdempotencyKey(`tv-${form.violationNo.trim()}`),
      });
      toast('تم تسجيل المخالفة المرورية بنجاح', 'success');
      setCreateOpen(false);
      setForm((p) => ({ ...p, violationNo: '', amount: '', riderShare: '', companyShare: '0' }));
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر تسجيل المخالفة المرورية', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم المخالفة', 'التاريخ', 'اللوحة', 'الشركة', 'المتحمل', 'حصة المندوب', 'حصة الشركة', 'الإجمالي', 'الحالة'],
        docs.map((d) => {
          const m = documentMeta(d);
          return [
            String(d.reference || d.docNo || d.id),
            String(d.occurredOn || '').slice(0, 10),
            String(m.vehiclePlate || ''),
            companyName(d.companyId),
            BEARER_LABELS[String(m.bearer || '')] || '—',
            Number(m.riderShare || 0),
            Number(m.companyShare || 0),
            Number(d.amount || 0),
            documentStatusLabel(d),
          ];
        }),
        [5, 6, 7],
        `traffic-violations-${period}`
      );
      toast('تم تصدير سجل المخالفات المرورية', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const columns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'reference',
      header: 'رقم المخالفة وتاريخها',
      type: 'text',
      getValue: (r) => `${String(r.reference || r.docNo || '')} ${String(r.occurredOn || '').slice(0, 10)}`,
      render: (r) => (
        <div className="space-y-0.5">
          <div className="font-bold text-[var(--ink)]">
            <bdi>{String(r.reference || r.docNo || r.id.slice(0, 8))}</bdi>
          </div>
          <div className="text-[11px] text-[var(--muted)]">{String(r.occurredOn || '').slice(0, 10)}</div>
        </div>
      ),
    },
    {
      key: 'vehiclePlate',
      header: 'المركبة (لوحة)',
      type: 'text',
      getValue: (r) => String(documentMeta(r).vehiclePlate || '—'),
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'bearer',
      header: 'المتحمل',
      type: 'list',
      getValue: (r) => BEARER_LABELS[String(documentMeta(r).bearer || '')] || '—',
    },
    {
      key: 'riderShare',
      header: 'على المندوب (112502)',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(documentMeta(r).riderShare || 0),
      render: (r) => <span className="num">{formatSar(Number(documentMeta(r).riderShare || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'companyShare',
      header: 'على الشركة',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(documentMeta(r).companyShare || 0),
      render: (r) => <span className="num">{formatSar(Number(documentMeta(r).companyShare || 0))}</span>,
      summary: 'sum',
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
    { key: 'status', header: 'الحالة', type: 'list', getValue: (r) => documentStatusLabel(r) },
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
          customPayConfirmText={(doc) => {
            const m = documentMeta(doc);
            return `عند سداد المخالفة، ستُرحّل حصة المندوب (${formatSar(Number(m.riderShare || 0))}) كمديونية مخالفة مرورية تُخصم في المستوى الأول من مسير الراتب القادم، وحصة الشركة (${formatSar(Number(m.companyShare || 0))}) كمصروف.`;
          }}
        />
      ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض المخالفات المرورية يحتاج صلاحية المدفوعات." />;
  }

  const openTotal = openDocs.reduce((s, d) => s + Number(d.amount || 0), 0);
  const riderTotal = docs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(documentMeta(d).riderShare || 0), 0);
  const companyTotal = docs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(documentMeta(d).companyShare || 0), 0);
  const paidThisMonth = docs
    .filter((d) => String(d.periodMonth || d.occurredOn || '').startsWith(period) && Number(d.amountPaid ?? 0) > 0)
    .reduce((s, d) => s + Number(d.amountPaid ?? 0), 0);

  return (
    <div className="space-y-4">
      <PageHeader
        title="سداد المخالفات المرورية"
        description="تسجيل وسداد المخالفات المرورية مع ترحيل حصة المندوب تلقائيًا كمديونية مستوى أول بالراتب القادم."
        primaryAction={
          canDisburse
            ? { label: 'تسجيل وسداد مخالفة مرورية', icon: <Plus size={14} />, onClick: () => setCreateOpen(true) }
            : undefined
        }
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <div className="rounded-2xl border border-[var(--warning)]/30 bg-[var(--warning)]/5 p-3 flex flex-wrap items-center justify-between gap-2 text-xs">
        <div className="flex items-center gap-2">
          <AlertTriangle size={15} className="text-[var(--warning)] shrink-0" aria-hidden="true" />
          <span>مديونية المخالفة المرورية تدخل تلقائيًا في استقطاعات المستوى الأول بالراتب القادم عند الصرف.</span>
        </div>
        <Link href={`/debts/?tab=debts&period=${encodeURIComponent(period)}`} className="font-bold text-[var(--accent)] hover:underline">
          فتح سجل المديونيات
        </Link>
      </div>

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="إجمالي المخالفات المفتوحة" value={formatSar(openTotal)} />
        <StatCard label="محمّلة على المناديب (مديونية مستوى 1)" value={formatSar(riderTotal)} />
        <StatCard label="محمّلة على الشركة" value={formatSar(companyTotal)} />
        <StatCard label={`مسددة في ${period}`} value={formatSar(paidThisMonth)} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <DataTable
          columns={columns}
          data={activeTab === 'open' ? openDocs : paidDocs}
          emptyTitle={activeTab === 'open' ? 'لا توجد مخالفات مفتوحة للسداد' : 'لا توجد مخالفات مدفوعة'}
          emptyDescription="سجّل مخالفة مرورية وحدد المتحمل وحساب المصروف عند اللزوم."
        />
      )}

      {/* Create Traffic Violation Dialog */}
      <Dialog open={createOpen} onClose={() => !busy && setCreateOpen(false)} title="تسجيل وسداد مخالفة مرورية" size="lg">
        <form onSubmit={submitViolation} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">رقم المخالفة</span>
              <input
                className={inputClass}
                value={form.violationNo}
                onChange={(e) => setForm((p) => ({ ...p, violationNo: e.target.value }))}
                required
              />
            </label>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">الشركة</span>
              <SearchableSelect
                value={form.companyId}
                onChange={(v) => setForm((p) => ({ ...p, companyId: v }))}
                options={companyOptions}
                placeholder="اختر الشركة..."
              />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المركبة (لوحة)</span>
              <SearchableSelect
                value={form.vehiclePlate}
                onChange={(v) => setForm((p) => ({ ...p, vehiclePlate: v }))}
                options={vehicleOptions}
                placeholder="اختر لوحة المركبة..."
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المندوب وقت المخالفة</span>
              <RiderPicker value={selectedRider?.iqama} onChange={(r) => void handleRiderChange(r)} required />
            </div>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">حساب المنصة المرتبط</span>
              <SearchableSelect
                value={form.erpPlatformAccountId}
                onChange={(v) => setForm((p) => ({ ...p, erpPlatformAccountId: v }))}
                options={platformAccountOptions}
                placeholder="اختر حساب المنصة..."
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المتحمل</span>
              <SearchableSelect
                value={form.bearer}
                onChange={(v) => onBearerOrAmountChange(v as 'rider' | 'company' | 'split', form.amount)}
                options={[
                  { value: 'rider', label: 'مندوب بالكامل (112502)' },
                  { value: 'company', label: 'شركة بالكامل' },
                  { value: 'split', label: 'مشترك (مندوب + شركة)' },
                ]}
              />
            </div>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">مبلغ المخالفة</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={form.amount}
                onChange={(e) => onBearerOrAmountChange(form.bearer, e.target.value)}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ المخالفة (ضمن {period})</span>
              <input
                type="date"
                className={inputClass}
                value={form.occurredOn}
                onChange={(e) => setForm((p) => ({ ...p, occurredOn: e.target.value, dueOn: e.target.value }))}
                required
              />
            </label>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">حصة المندوب</span>
              <input
                type="number"
                step="0.01"
                min="0"
                className={inputClass}
                value={form.riderShare}
                onChange={(e) => setForm((p) => ({ ...p, riderShare: e.target.value }))}
                disabled={form.bearer !== 'split'}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">حصة الشركة</span>
              <input
                type="number"
                step="0.01"
                min="0"
                className={inputClass}
                value={form.companyShare}
                onChange={(e) => setForm((p) => ({ ...p, companyShare: e.target.value }))}
                disabled={form.bearer !== 'split'}
                required
              />
            </label>
            {Number(form.companyShare || 0) > 0 && (
              <div className="space-y-1">
                <span className="block text-[var(--muted)]">حساب مصروف الشركة</span>
                <SearchableSelect
                  value={form.companyExpenseAccountCode}
                  onChange={(v) => setForm((p) => ({ ...p, companyExpenseAccountCode: v }))}
                  options={expenseAccountOptions}
                  placeholder="اختر الحساب..."
                />
              </div>
            )}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={() => setCreateOpen(false)} className="px-3 py-1.5 rounded-lg border border-[var(--line)]">
              إلغاء
            </button>
            <button type="submit" disabled={busy} className="px-4 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold">
              حفظ المخالفة
            </button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
