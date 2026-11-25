'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus, Download } from 'lucide-react';
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
import { RiderAsksCard } from '../../components/debts/RiderAsksCard';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor, ConfigListItem } from '../../lib/types';
import {
  accountOptions,
  ADVANCE_TYPE_LABELS,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentMeta,
  documentStatusLabel,
  exportPaymentsSheet,
  FULFILMENT_LABELS,
  isBankOrCashCode,
  loadPostableAccounts,
  newIdempotencyKey,
  NONE_COST_CENTRE_ID,
} from './payment-engine';
import { PaymentDocumentActions } from './PaymentWorkflowPanel';

const inputClass =
  'w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs text-[var(--ink)]';

export function AdvancesWorkspace() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView =
    can('payments.view') ||
    can('advances.request') ||
    can('advances.review') ||
    can('advances.payout') ||
    isAdmin;
  const canRequest = can('advances.request') || isAdmin;
  const canPayout = can('advances.payout') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(() => {
    const items: SectionTabItem[] = [
      { id: 'requests', label: 'طلبات السلف' },
    ];
    if (canPayout) items.push({ id: 'payouts', label: 'صرف السلف المعتمدة' });
    items.push(
      { id: 'hs_topups', label: 'طلبات شحن محفظة هنقرستيشن' },
      { id: 'rider_asks', label: 'طلبات المناديب من التطبيق' },
      { id: 'recharge_catalog', label: 'شبكات وباقات الشحن' }
    );
    return items;
  }, [canPayout]);

  const [activeTab, setActiveTab] = useState('requests');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [advanceDocs, setAdvanceDocs] = useState<PaymentDocumentItem[]>([]);
  const [hsDocs, setHsDocs] = useState<PaymentDocumentItem[]>([]);
  const [networks, setNetworks] = useState<ConfigListItem[]>([]);
  const [packages, setPackages] = useState<ConfigListItem[]>([]);

  const [createOpen, setCreateOpen] = useState(false);
  const [selectedRider, setSelectedRider] = useState<RiderOption | null>(null);
  const [riderAccounts, setRiderAccounts] = useState<RiderAccountRow[]>([]);
  const [form, setForm] = useState({
    companyId: '',
    riderIqama: '',
    erpEmployeeId: '',
    erpPlatformAccountId: '',
    advanceType: 'cash' as 'cash' | 'bank' | 'internet' | 'hs_topup',
    fulfilment: 'cash' as 'cash' | 'bank' | 'recharge_card',
    networkCode: '',
    packageCode: '',
    amount: '',
    monthlyInstallment: '',
    payoutAccountCode: '111101',
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
      const [compList, coaRes, advRes, hsRes] = await Promise.all([
        loadCompanyOptions(),
        loadPostableAccounts(),
        api.listPaymentDocuments({ type: 'advance' }).catch(() => ({ items: [], allowedActions: [] })),
        api.listPaymentDocuments({ type: 'hs_topup' }).catch(() => ({ items: [], allowedActions: [] })),
      ]);
      setCompanies(compList);
      setAccounts(coaRes.accounts);
      setAdvanceDocs(advRes?.items || []);
      setHsDocs(hsRes?.items || []);
      const firstCompany = compList[0]?.id || compList[0]?.code || '';
      if (firstCompany) {
        setForm((p) => ({ ...p, companyId: p.companyId || firstCompany }));
      }
      api.getConfigListGroup('recharge_networks').then((r) => setNetworks(r?.items || [])).catch(() => {});
      api.getConfigListGroup('recharge_packages').then((r) => setPackages(r?.items || [])).catch(() => {});
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل طلبات السلف');
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
    setForm((p) => ({
      ...p,
      riderIqama: rider?.iqama || '',
      erpEmployeeId: empId,
      erpPlatformAccountId: '',
    }));
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

  const platformAccountOptions = useMemo(
    () =>
      riderAccounts.map((a) => ({
        value: String(a.id || ''),
        label: `${String(a.platformCode || 'منصة')} — ${String(a.accountCode || a.platformAccountId || a.id || '')}`,
      })),
    [riderAccounts]
  );

  const payoutOptions = useMemo(() => {
    const isCard = form.advanceType === 'internet' && form.fulfilment === 'recharge_card';
    if (isCard) return [{ value: '112404', label: '112404 — رصيد جهاز مدا' }];
    const list = accountOptions(accounts, (a) => isBankOrCashCode(a.code));
    return list.length > 0
      ? list
      : [
          { value: '111101', label: '111101 — الصندوق الرئيسي' },
          { value: '113101', label: '113101 — البنك الرئيسي' },
        ];
  }, [accounts, form.advanceType, form.fulfilment]);

  const networkOptions = useMemo(
    () => networks.map((n) => ({ value: String(n.code || ''), label: String(n.nameAr || n.code || '') })),
    [networks]
  );

  const packageOptions = useMemo(
    () =>
      packages
        .filter((p) => !form.networkCode || String(p.values?.network || '') === form.networkCode)
        .map((p) => ({
          value: String(p.code || ''),
          label: `${String(p.nameAr || p.code || '')} — ${formatSar(Number(p.values?.price || 0))}`,
        })),
    [packages, form.networkCode]
  );

  const openCreateModal = (defaultType: 'cash' | 'internet' | 'hs_topup' = 'cash') => {
    const d = defaultDateInPeriod(period);
    setForm((p) => ({
      ...p,
      advanceType: defaultType,
      fulfilment: defaultType === 'internet' ? 'recharge_card' : 'cash',
      payoutAccountCode: defaultType === 'internet' ? '112404' : '111101',
      occurredOn: d,
      dueOn: d,
    }));
    setCreateOpen(true);
  };

  const onPackageSelect = (code: string) => {
    const pkg = packages.find((p) => p.code === code);
    const price = Number(pkg?.values?.price || 0);
    setForm((p) => ({
      ...p,
      packageCode: code,
      amount: price > 0 ? price.toFixed(2) : p.amount,
      monthlyInstallment: price > 0 ? price.toFixed(2) : p.monthlyInstallment,
    }));
  };

  const submitAdvance = async (e: React.FormEvent) => {
    e.preventDefault();
    const amount = Number(form.amount);
    const monthlyInstallment = Number(form.monthlyInstallment || form.amount);
    if (!Number.isFinite(amount) || amount <= 0) {
      toast('أدخل مبلغ سلفة صالحًا', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.createPaymentAdvance({
        companyId: form.companyId,
        riderIqama: form.riderIqama,
        erpEmployeeId: form.erpEmployeeId,
        erpPlatformAccountId: form.erpPlatformAccountId,
        advanceType: form.advanceType,
        fulfilment: form.advanceType === 'internet' ? form.fulfilment : form.advanceType === 'bank' ? 'bank' : 'cash',
        ...(form.advanceType === 'internet' && form.fulfilment === 'recharge_card'
          ? { networkCode: form.networkCode, packageCode: form.packageCode }
          : {}),
        amount,
        monthlyInstallment,
        payoutAccountCode: form.payoutAccountCode,
        reference: form.reference.trim() || `${form.advanceType}-${form.riderIqama}`,
        period,
        occurredOn: form.occurredOn,
        dueOn: form.dueOn,
        costCentreId: NONE_COST_CENTRE_ID,
        idempotencyKey: newIdempotencyKey(`adv-${form.advanceType}`),
      });
      toast('تم إنشاء طلب السلفة بنجاح', 'success');
      setCreateOpen(false);
      await load();
    } catch (err) {
      toast(err instanceof ApiError ? err.message : 'تعذر إنشاء طلب السلفة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const allAdvanceDocs = useMemo(() => [...advanceDocs, ...hsDocs], [advanceDocs, hsDocs]);
  const payoutReadyDocs = useMemo(
    () => allAdvanceDocs.filter((d) => d.status === 3 && Number(d.availableBalance ?? 0) > 0),
    [allAdvanceDocs]
  );

  const handleExport = async () => {
    try {
      await exportPaymentsSheet(
        ['رقم الطلب', 'النوع', 'الشركة', 'الإقامة', 'المبلغ', 'القسط الشهري', 'الحالة'],
        allAdvanceDocs.map((d) => {
          const m = documentMeta(d);
          return [
            d.docNo || d.id,
            ADVANCE_TYPE_LABELS[String(m.advanceType || d.type)] || d.type,
            companyName(d.companyId),
            String(m.riderIqama || ''),
            Number(d.amount || 0),
            Number(m.monthlyInstallment || 0),
            documentStatusLabel(d),
          ];
        }),
        [4, 5],
        `advances-${period}`
      );
      toast('تم تصدير جدول السلف', 'success');
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
    {
      key: 'riderIqama',
      header: 'إقامة المندوب',
      type: 'text',
      getValue: (r) => String(documentMeta(r).riderIqama || ''),
      render: (r) => <bdi className="font-mono text-xs">{String(documentMeta(r).riderIqama || '—')}</bdi>,
    },
    {
      key: 'advanceType',
      header: 'النوع',
      type: 'list',
      getValue: (r) => ADVANCE_TYPE_LABELS[String(documentMeta(r).advanceType || r.type)] || r.type,
    },
    {
      key: 'fulfilment',
      header: 'طريقة التسليم',
      type: 'list',
      getValue: (r) => FULFILMENT_LABELS[String(documentMeta(r).fulfilment || '')] || '—',
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

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض السلف والشحن يحتاج صلاحية السلف أو المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="السلف والشحن"
        description="طلبات السلف النقدية والبنكية وسلف الإنترنت وطلبات شحن محفظة هنقرستيشن عبر محرك المدفوعات."
        primaryAction={
          canRequest
            ? { label: 'طلب سلفة', icon: <Plus size={14} />, onClick: () => openCreateModal('cash') }
            : undefined
        }
        moreActions={[
          ...(canRequest
            ? [
                { label: 'طلب سلفة نت', onClick: () => openCreateModal('internet') },
                { label: 'طلب شحن هنقرستيشن', onClick: () => openCreateModal('hs_topup') },
              ]
            : []),
          { label: 'تصدير', icon: <Download size={14} />, onClick: () => void handleExport() },
          { label: 'تحديث', icon: <RefreshCw size={14} />, onClick: () => void load(), disabled: loading || busy },
        ]}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="بانتظار المراجعة" value={allAdvanceDocs.filter((d) => d.status === 2).length} />
        <StatCard label="معتمدة بانتظار الصرف" value={payoutReadyDocs.length} />
        <StatCard
          label="سلف إنترنت (112503)"
          value={formatSar(
            advanceDocs
              .filter((d) => documentMeta(d).advanceType === 'internet' && d.status !== 5)
              .reduce((s, d) => s + Number(d.amount || 0), 0)
          )}
        />
        <StatCard
          label="شحن هنقرستيشن (112506)"
          value={formatSar(hsDocs.filter((d) => d.status !== 5).reduce((s, d) => s + Number(d.amount || 0), 0))}
        />
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
              data={advanceDocs}
              emptyTitle="لا توجد طلبات سلف"
              emptyDescription="أنشئ طلب سلفة نقدية أو بنكية أو إنترنت ليمر بدورة الاعتماد والصرف."
            />
          )}
          {activeTab === 'payouts' && (
            <DataTable
              columns={columns}
              data={payoutReadyDocs}
              emptyTitle="لا توجد سلف بانتظار الصرف"
              emptyDescription="كل السلف المعتمدة تم صرفها أو حجز سندات صرف لها."
            />
          )}
          {activeTab === 'hs_topups' && (
            <DataTable
              columns={columns}
              data={hsDocs}
              emptyTitle="لا توجد طلبات شحن هنقرستيشن"
              emptyDescription="أنشئ طلب شحن محفظة هنقرستيشن للمندوب من زر «طلب شحن هنقرستيشن»."
            />
          )}
          {activeTab === 'rider_asks' && <RiderAsksCard onConverted={() => void load()} />}
          {activeTab === 'recharge_catalog' && (
            <div className="grid gap-4 lg:grid-cols-2">
              <DataTable
                columns={[
                  { key: 'code', header: 'الرمز', type: 'text', getValue: (r) => String(r.code || '') },
                  { key: 'nameAr', header: 'الشبكة', type: 'text', getValue: (r) => String(r.nameAr || '') },
                ]}
                data={networks}
                emptyTitle="لا توجد شبكات معتمدة"
                emptyDescription="تُدار الشبكات من قوائم الإعدادات المعتمدة."
              />
              <DataTable
                columns={[
                  { key: 'code', header: 'الرمز', type: 'text', getValue: (r) => String(r.code || '') },
                  { key: 'nameAr', header: 'الباقة', type: 'text', getValue: (r) => String(r.nameAr || '') },
                  {
                    key: 'price',
                    header: 'السعر',
                    type: 'number',
                    numeric: true,
                    money: true,
                    getValue: (r) => Number(r.values?.price || 0),
                    render: (r) => <span className="num font-bold">{formatSar(Number(r.values?.price || 0))}</span>,
                  },
                ]}
                data={packages}
                emptyTitle="لا توجد باقات معتمدة"
                emptyDescription="تُدار الباقات من قوائم الإعدادات المعتمدة."
              />
            </div>
          )}
        </>
      )}

      {/* Create Advance Dialog */}
      <Dialog open={createOpen} onClose={() => !busy && setCreateOpen(false)} title="إنشاء طلب سلفة أو شحن" size="lg">
        <form onSubmit={submitAdvance} className="space-y-3 text-xs">
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
              <span className="block text-[var(--muted)]">نوع الطلب</span>
              <SearchableSelect
                value={form.advanceType}
                onChange={(v) => {
                  const next = v as 'cash' | 'bank' | 'internet' | 'hs_topup';
                  setForm((p) => ({
                    ...p,
                    advanceType: next,
                    fulfilment: next === 'internet' ? 'recharge_card' : next === 'bank' ? 'bank' : 'cash',
                    payoutAccountCode: next === 'internet' ? '112404' : '111101',
                  }));
                }}
                options={[
                  { value: 'cash', label: 'سلفة نقدية (112501)' },
                  { value: 'bank', label: 'سلفة بنكية (112501)' },
                  { value: 'internet', label: 'سلفة إنترنت (112503)' },
                  { value: 'hs_topup', label: 'شحن محفظة هنقرستيشن (112506)' },
                ]}
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">المندوب</span>
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

          {form.advanceType === 'internet' && (
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="space-y-1">
                <span className="block text-[var(--muted)]">طريقة التسليم</span>
                <SearchableSelect
                  value={form.fulfilment}
                  onChange={(v) => {
                    const next = v as 'cash' | 'bank' | 'recharge_card';
                    setForm((p) => ({
                      ...p,
                      fulfilment: next,
                      payoutAccountCode: next === 'recharge_card' ? '112404' : '111101',
                    }));
                  }}
                  options={[
                    { value: 'recharge_card', label: 'بطاقة شحن مدا (112404)' },
                    { value: 'cash', label: 'نقدي' },
                    { value: 'bank', label: 'تحويل بنكي' },
                  ]}
                />
              </div>
              {form.fulfilment === 'recharge_card' && (
                <>
                  <div className="space-y-1">
                    <span className="block text-[var(--muted)]">الشبكة</span>
                    <SearchableSelect
                      value={form.networkCode}
                      onChange={(v) => setForm((p) => ({ ...p, networkCode: v, packageCode: '' }))}
                      options={networkOptions}
                      placeholder="اختر الشبكة..."
                    />
                  </div>
                  <div className="space-y-1">
                    <span className="block text-[var(--muted)]">الباقة</span>
                    <SearchableSelect
                      value={form.packageCode}
                      onChange={onPackageSelect}
                      options={packageOptions}
                      placeholder="اختر الباقة..."
                    />
                  </div>
                </>
              )}
            </div>
          )}

          <div className="grid gap-3 sm:grid-cols-3">
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
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">القسط الشهري</span>
              <input
                type="number"
                step="0.01"
                min="0.01"
                className={inputClass}
                value={form.monthlyInstallment}
                onChange={(e) => setForm((p) => ({ ...p, monthlyInstallment: e.target.value }))}
                required
              />
            </label>
            <div className="space-y-1">
              <span className="block text-[var(--muted)]">حساب الصرف المعتمد</span>
              <SearchableSelect
                value={form.payoutAccountCode}
                onChange={(v) => setForm((p) => ({ ...p, payoutAccountCode: v }))}
                options={payoutOptions}
              />
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">المرجع / السبب</span>
              <input
                className={inputClass}
                value={form.reference}
                onChange={(e) => setForm((p) => ({ ...p, reference: e.target.value }))}
                required
              />
            </label>
            <label className="block space-y-1">
              <span className="text-[var(--muted)]">تاريخ الطلب</span>
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
