'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Download, Info } from 'lucide-react';
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
import { Dialog } from '../../components/ui/Dialog';
import { formatSar } from '../../lib/money';
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
  HR_FEE_CATEGORIES,
  loadPostableAccounts,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function HrRequestsWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || can('payments.hr_request.create') || isAdmin;
  const canCreate = can('payments.hr_request.create') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'requests', label: 'الطلبات المفتوحة' },
      { id: 'paid', label: 'المعتمدة والمدفوعة' },
      { id: 'partner_box', label: 'صندوق الشريك (تحصيل نقدي)' },
    ],
    []
  );

  const [activeTab, setActiveTab] = useState('requests');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [docs, setDocs] = useState<PaymentDocumentItem[]>([]);

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedRider, setSelectedRider] = useState<RiderOption | null>(null);
  const [riderAccounts, setRiderAccounts] = useState<RiderAccountRow[]>([]);
  const [form, setForm] = useState({
    companyId: '',
    erpEmployeeId: '',
    erpPlatformAccountId: '',
    category: 'إقامة',
    bearer: 'rider' as 'rider' | 'company' | 'split',
    amount: '',
    riderShare: '',
    companyShare: '0',
    companyExpenseAccountCode: '512104',
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
      const [compList, coaRes, res] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listHrFees(),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setDocs(res?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل طلبات الموارد البشرية');
    } finally {
      setLoading(false);
    }
  }, [canView]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleRiderChange = async (rider: RiderOption | null) => {
    setSelectedRider(rider);
    const empId = String((rider as unknown as Record<string, unknown>)?.erpId || (rider as unknown as Record<string, unknown>)?.erpRiderId || rider?.id || '');
    setForm((p) => ({ ...p, erpEmployeeId: empId, erpPlatformAccountId: '' }));
    if (!rider?.iqama) {
      setRiderAccounts([]);
      return;
    }
    try {
      const res = await api.getRiderAccounts(rider.iqama);
      const list = res?.items || [];
      setRiderAccounts(list);
      if (list[0]?.id) {
        setForm((p) => ({ ...p, erpPlatformAccountId: String(list[0].id) }));
      }
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
    () => docs.filter((d) => d.status === 4 || (d.status === 3 && Number(d.amountPaid ?? 0) > 0)),
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

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const total = Number(form.amount);
    const riderShare = Number(form.riderShare || 0);
    const companyShare = Number(form.companyShare || 0);
    if (!Number.isFinite(total) || total <= 0) {
      toast('أدخل مبلغًا موجبًا', 'warning');
      return;
    }
    if (Math.abs(riderShare + companyShare - total) > 0.001) {
      toast('يجب أن يساوي مجموع حصة المندوب وحصة الشركة إجمالي المبلغ', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.createHrFee({
        companyId: form.companyId,
        erpEmployeeId: form.erpEmployeeId,
        erpPlatformAccountId: form.erpPlatformAccountId,
        category: form.category,
        bearer: form.bearer,
        amount: total,
        riderShare,
        companyShare,
        ...(companyShare > 0 ? { companyExpenseAccountCode: form.companyExpenseAccountCode } : {}),
        reference: form.reference.trim() || `${form.category}-${period}`,
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        costCentreId: NONE_COST_CENTRE_ID,
        idempotencyKey: newIdempotencyKey('hr-fee'),
      });
      toast('تم إنشاء طلب رسوم الموارد البشرية', 'success');
      setCreateOpen(false);
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء الطلب', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم المستند', 'الشركة', 'البند', 'المتحمل', 'حصة المندوب', 'حصة الشركة', 'الإجمالي', 'الحالة'],
        docs.map((d) => {
          const m = documentMeta(d);
          return [
            d.docNo || d.id,
            companyName(d.companyId),
            String(m.category || ''),
            BEARER_LABELS[String(m.bearer || '')] || '—',
            Number(m.riderShare || 0),
            Number(m.companyShare || 0),
            Number(d.amount || 0),
            documentStatusLabel(d),
          ];
        }),
        [4, 5, 6],
        `hr-fees-${period}`
      );
      toast('تم تصدير طلبات الموارد البشرية', 'success');
    } catch {
      toast('تعذر التصدير', 'danger');
    }
  };

  const columns: ColumnDef<PaymentDocumentItem>[] = [
    {
      key: 'docNo',
      header: 'رقم الطلب',
      type: 'text',
      getValue: (r) => String(r.docNo || r.id),
      render: (r) => <bdi className="font-mono text-xs">{r.docNo || r.id.slice(0, 8)}</bdi>,
    },
    { key: 'companyId', header: 'الشركة', type: 'list', getValue: (r) => companyName(r.companyId) },
    {
      key: 'category',
      header: 'البند',
      type: 'list',
      getValue: (r) => String(documentMeta(r).category || '—'),
    },
    {
      key: 'bearer',
      header: 'المتحمل',
      type: 'list',
      getValue: (r) => BEARER_LABELS[String(documentMeta(r).bearer || '')] || '—',
    },
    {
      key: 'riderShare',
      header: 'حصة المندوب (112506)',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(documentMeta(r).riderShare || 0),
      render: (r) => <span className="num">{formatSar(Number(documentMeta(r).riderShare || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'companyShare',
      header: 'حصة الشركة',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(documentMeta(r).companyShare || 0),
      render: (r) => <span className="num">{formatSar(Number(documentMeta(r).companyShare || 0))}</span>,
      summary: 'sum',
    },
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
    return <EmptyState title="صلاحية غير كافية" description="عرض طلبات الموارد البشرية يحتاج صلاحية المدفوعات." />;
  }

  const totalRiderShare = docs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(documentMeta(d).riderShare || 0), 0);
  const totalCompanyShare = docs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(documentMeta(d).companyShare || 0), 0);
  const splitCount = docs.filter((d) => d.status !== 5 && documentMeta(d).bearer === 'split').length;

  return (
    <div className="space-y-4">
      <PageHeader
        title="طلبات الموارد البشرية والخصومات"
        description="رسوم الإقامة والجوازات والرخص وكروت السائقين والتأمين بتوزيع دقيق بين المندوب والشركة."
        primaryAction={
          canCreate
            ? { label: 'إنشاء طلب رسوم موارد بشرية', icon: <Plus size={14} />, onClick: () => setCreateOpen(true) }
            : undefined
        }
        moreActions={[
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="طلبات مفتوحة" value={openDocs.length} />
        <StatCard label="على المندوب (112506)" value={formatSar(totalRiderShare)} />
        <StatCard label="على الشركة" value={formatSar(totalCompanyShare)} />
        <StatCard label="طلبات مشتركة" value={splitCount} />
      </div>

      {loading ? (
        <Skeleton className="h-44 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : (
        <>
          {activeTab === 'requests' && (
            <DataTable
              columns={columns}
              data={openDocs}
              emptyTitle="لا توجد طلبات مفتوحة"
              emptyDescription="أنشئ طلب رسوم موارد بشرية لتسجيله واعتماده وصرفه عبر محرك المدفوعات."
            />
          )}
          {activeTab === 'paid' && (
            <DataTable
              columns={columns}
              data={paidDocs}
              emptyTitle="لا توجد طلبات مدفوعة بعد"
              emptyDescription="تظهر هنا الطلبات المعتمدة والمسددة."
            />
          )}
          {activeTab === 'partner_box' && (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 flex items-start gap-3 text-xs text-[var(--ink)]">
              <Info size={18} className="text-[var(--accent)] shrink-0 mt-0.5" aria-hidden="true" />
              <div className="space-y-1">
                <div className="font-bold text-sm">التحصيل النقدي خارج الدفاتر (صندوق الشريك)</div>
                <p className="text-[var(--muted)] leading-relaxed">
                  التحصيل النقدي لحساب صندوق الشريك يتطلب ربط سجل صندوق الشريك خارج الدفاتر قبل تفعيله في الخادم. استخدم توزيع «مندوب» أو «شركة» أو «مشترك» للطلبات المقيدة داخل دفاتر الشركة.
                </p>
              </div>
            </div>
          )}
        </>
      )}

      {/* Create HR Fee Dialog */}
      <Dialog open={createOpen} onClose={() => !busy && setCreateOpen(false)} title="إنشاء طلب رسوم موارد بشرية" size="lg">
        <form onSubmit={submit} className="space-y-3 text-xs">
          <div className="grid gap-3 sm:grid-cols-2">
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
              <span className="block text-[var(--muted)]">البند</span>
              <SearchableSelect
                value={form.category}
                onChange={(v) => setForm((p) => ({ ...p, category: v }))}
                options={HR_FEE_CATEGORIES.map((c) => ({ value: c.value, label: c.label }))}
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المندوب / الموظف</span>
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
                  { value: 'rider', label: 'مندوب بالكامل (112506)' },
                  { value: 'company', label: 'شركة بالكامل' },
                  { value: 'split', label: 'مشترك (مندوب + شركة)' },
                ]}
              />
            </div>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">إجمالي المبلغ</span>
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
              <span className="text-[var(--muted)]">المرجع</span>
              <input
                className={inputClass}
                value={form.reference}
                onChange={(e) => setForm((p) => ({ ...p, reference: e.target.value }))}
                placeholder="رقم السداد أو المعاملة"
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
                  placeholder="اختر حساب المصروف..."
                />
              </div>
            )}
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
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
              حفظ الطلب
            </button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
