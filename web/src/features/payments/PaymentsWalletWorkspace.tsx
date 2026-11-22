'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, Plus } from 'lucide-react';
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
import { SourceBadge } from '../../components/ui/SourceBadge';
import { formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor, ConfigListItem } from '../../lib/types';
import {
  accountOptions,
  type CoaAccountOption,
  defaultDateInPeriod,
  documentStatusLabel,
  hasAllowedAction,
  loadPostableAccounts,
  newIdempotencyKey,
} from './payment-engine';
import { AddCoaAccountDialog, PaymentDocumentActions } from './PaymentWorkflowPanel';

type WalletRow = Record<string, unknown> & {
  id?: string;
  companyId?: string;
  provider?: string;
  accountId?: string;
  kind?: string;
  bookBalance?: number;
  allowedActions?: string[];
  status?: number;
};

type Mode = 'fuel' | 'mada' | 'prepaid';

const inputClass =
  'w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs';

export function PaymentsWalletWorkspace({
  mode,
  title,
  description,
}: {
  mode: Mode;
  title: string;
  description: string;
}) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period, setPeriod } = usePeriod();
  const canManage = can('payments.fuel.manage') || isAdmin;
  const canPayout = can('advances.payout') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;

  const tabs: SectionTabItem[] = useMemo(() => {
    if (mode === 'mada') {
      return [
        { id: 'balance', label: 'رصيد محفظة مدا (112404)' },
        { id: 'topups', label: 'عمليات شحن مدا' },
        { id: 'cards', label: 'سلف الإنترنت وبطاقات الشحن' },
        { id: 'settings', label: 'إعدادات الشبكات والباقات' },
      ];
    }
    if (mode === 'fuel') {
      return [
        { id: 'wallets', label: 'محافظ الوقود والتمويل' },
        { id: 'movements', label: 'حركات التمويل' },
        { id: 'reconcile', label: 'المطابقة الشهرية' },
        { id: 'consumption', label: 'الاستهلاك مقابل السقف' },
        { id: 'providers', label: 'إعدادات المزودين' },
      ];
    }
    return [
      { id: 'wallets', label: 'المحافظ والأرصدة' },
      { id: 'movements', label: 'حركات الشحن والاستهلاك' },
      { id: 'settings', label: 'إعدادات المحافظ' },
    ];
  }, [mode]);

  const docType = mode === 'mada' ? 'mada_topup' : mode === 'fuel' ? 'fuel_topup' : 'prepaid_topup';

  const [activeTab, setActiveTab] = useState(tabs[0]?.id || 'wallets');
  const [rows, setRows] = useState<WalletRow[]>([]);
  const [fundingDocs, setFundingDocs] = useState<PaymentDocumentItem[]>([]);
  const [accounts, setAccounts] = useState<CoaAccountOption[]>([]);
  const [canCreateCoa, setCanCreateCoa] = useState(false);
  const [coaDialogOpen, setCoaDialogOpen] = useState(false);
  const [listActions, setListActions] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [consumption, setConsumption] = useState<Record<string, unknown>[]>([]);
  const [networks, setNetworks] = useState<ConfigListItem[]>([]);
  const [packages, setPackages] = useState<ConfigListItem[]>([]);

  const [createForm, setCreateForm] = useState({
    companyId: '',
    provider: '',
    accountCode: mode === 'mada' ? '112404' : '',
  });
  const [fundForm, setFundForm] = useState({ walletId: '', amount: '', reference: '' });
  const [reconcileForm, setReconcileForm] = useState({
    walletId: '',
    statementBalance: '',
    reference: '',
  });
  const [cardForm, setCardForm] = useState({ paymentId: '' });

  const loadWallets = useCallback(async () => {
    if (!canManage && !(mode === 'mada' && canPayout)) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const [res, coaRes, docsRes] = await Promise.all([
        mode === 'mada'
          ? api.listMadaWallets()
          : mode === 'fuel'
            ? api.listFuelWallets()
            : api.listPrepaidWallets(),
        loadPostableAccounts(),
        api.listPaymentDocuments({ type: docType }).catch(() => ({ items: [], allowedActions: [] })),
      ]);
      setRows((res?.items as unknown as WalletRow[]) || []);
      setListActions(Array.isArray(res?.allowedActions) ? res.allowedActions : []);
      setAccounts(coaRes.accounts);
      setCanCreateCoa(coaRes.canCreate);
      setFundingDocs(docsRes?.items || []);
    } catch (err: unknown) {
      setError(err instanceof ApiError ? err.message : 'تعذر تحميل المحافظ');
    } finally {
      setLoading(false);
    }
  }, [canManage, canPayout, docType, mode]);

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      const first = list[0]?.id || list[0]?.code || '';
      if (first) setCreateForm((prev) => ({ ...prev, companyId: prev.companyId || first }));
    });
    void loadWallets();
  }, [loadWallets]);

  useEffect(() => {
    if (mode !== 'fuel' || activeTab !== 'consumption') return;
    api
      .getOperationsFuelCards({ month: period })
      .then((res) => setConsumption((res?.items as unknown as Record<string, unknown>[]) || []))
      .catch(() => setConsumption([]));
  }, [mode, activeTab, period]);

  useEffect(() => {
    if (mode !== 'mada' || activeTab !== 'settings') return;
    api.getConfigListGroup('recharge_networks').then((res) => setNetworks(res?.items || [])).catch(() => {});
    api.getConfigListGroup('recharge_packages').then((res) => setPackages(res?.items || [])).catch(() => {});
  }, [activeTab, mode]);

  const companyOptions = companies.map((c) => ({
    value: c.id || c.code || '',
    label: companyLabel(c),
  }));

  const walletAccountOptions = useMemo(() => {
    if (mode === 'mada') return [{ value: '112404', label: '112404 — رصيد جهاز مدا' }];
    return accountOptions(accounts, (a) => a.code.startsWith('1124') || a.code.startsWith('1141'));
  }, [accounts, mode]);

  const walletOptions = rows.map((w) => ({
    value: String(w.id || ''),
    label: `${String(w.provider || w.kind || 'محفظة')} — ${formatSar(Number(w.bookBalance || 0))}`,
  }));

  const totalBalance = rows.reduce((sum, r) => sum + Number(r.bookBalance || 0), 0);
  const companyName = (id?: string) => {
    const match = companies.find((c) => c.id === id || c.code === id);
    return match ? companyLabel(match) : id || '—';
  };

  const columns: ColumnDef<WalletRow>[] = [
    {
      key: 'provider',
      header: 'المزود / النوع',
      type: 'text',
      getValue: (r) => String(r.provider || r.kind || ''),
      render: (r) => String(r.provider || r.kind || '—'),
    },
    {
      key: 'companyId',
      header: 'الشركة',
      type: 'list',
      getValue: (r) => String(r.companyId || ''),
      render: (r) => companyName(r.companyId),
    },
    {
      key: 'bookBalance',
      header: 'الرصيد الدفتري',
      type: 'number',
      numeric: true,
      money: true,
      getValue: (r) => Number(r.bookBalance || 0),
      render: (r) => <span className="num font-bold">{formatSar(Number(r.bookBalance || 0))}</span>,
      summary: 'sum',
    },
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      getValue: (r) => (Number(r.status) === 1 ? 'نشطة' : 'مغلقة'),
      render: (r) => (Number(r.status) === 1 ? 'نشطة' : 'مغلقة'),
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
    { key: 'reference', header: 'المرجع', type: 'text', getValue: (r) => String(r.reference || '') },
    { key: 'occurredOn', header: 'التاريخ', type: 'date', getValue: (r) => String(r.occurredOn || '').slice(0, 10) },
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
      render: (r) => <PaymentDocumentActions doc={r} period={period} accounts={accounts} onUpdated={loadWallets} />,
    },
  ];

  const createWallet = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!hasAllowedAction({ allowedActions: listActions }, 'create_wallet')) {
      toast('لا تملك صلاحية إنشاء محفظة', 'danger');
      return;
    }
    setBusy(true);
    try {
      if (mode === 'fuel') await api.createFuelWallet(createForm);
      else if (mode === 'prepaid') await api.createPrepaidWallet(createForm);
      else await api.createFuelWallet({ ...createForm, kind: 'mada', accountCode: '112404' });
      toast('أُنشئت المحفظة', 'success');
      setCreateForm((p) => ({ ...p, provider: '' }));
      await loadWallets();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإنشاء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const fundWallet = async (e: React.FormEvent) => {
    e.preventDefault();
    const wallet = rows.find((r) => r.id === fundForm.walletId);
    if (!wallet || !hasAllowedAction(wallet, 'create_topup')) {
      toast('المحفظة لا تسمح بالشحن', 'danger');
      return;
    }
    setBusy(true);
    try {
      const dateVal = defaultDateInPeriod(period);
      const payload = {
        amount: Number(fundForm.amount),
        reference: fundForm.reference,
        walletId: fundForm.walletId,
        period,
        occurredOn: dateVal,
        dueOn: dateVal,
        idempotencyKey: newIdempotencyKey(`wallet-${mode}`),
      };
      if (mode === 'mada') await api.createMadaTopup(payload);
      else if (mode === 'fuel') await api.fundFuelWallet(fundForm.walletId, payload);
      else await api.createPrepaidWalletMovement(fundForm.walletId, payload);
      toast('أُنشئ مستند الشحن في محرك المدفوعات', 'success');
      setFundForm((p) => ({ ...p, amount: '', reference: '' }));
      await loadWallets();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الشحن', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const reconcileWallet = async (e: React.FormEvent) => {
    e.preventDefault();
    const wallet = rows.find((r) => r.id === reconcileForm.walletId);
    if (!wallet || !hasAllowedAction(wallet, 'reconcile')) {
      toast('المحفظة لا تسمح بالمطابقة', 'danger');
      return;
    }
    setBusy(true);
    try {
      const payload = {
        walletId: reconcileForm.walletId,
        period,
        statementBalance: Number(reconcileForm.statementBalance),
        reference: reconcileForm.reference,
      };
      const res =
        mode === 'mada'
          ? await api.reconcileMada(payload)
          : await api.reconcileFuelWallet(reconcileForm.walletId, payload);
      const result = String(res?.result || '');
      toast(
        result === 'matched'
          ? `مطابق — الفرق ${formatSar(Number(res?.difference || 0))}`
          : `فارق — ${formatSar(Number(res?.difference || 0))}`,
        result === 'matched' ? 'success' : 'warning'
      );
      await loadWallets();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المطابقة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const issueCard = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canPayout) {
      toast('صلاحية صرف السلف مطلوبة', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.issueMadaCard({ paymentId: cardForm.paymentId });
      toast('صُرفت بطاقة الشحن', 'success');
      setCardForm({ paymentId: '' });
      await loadWallets();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر صرف البطاقة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  if (!canManage && !(mode === 'mada' && canPayout) && !canDisburse) {
    return <EmptyState title="صلاحية غير كافية" description="إدارة المحافظ تحتاج صلاحية المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title={title}
        description={description}
        primaryAction={{
          label: 'تحديث',
          icon: <RefreshCw size={14} />,
          onClick: () => void loadWallets(),
          disabled: loading || busy,
        }}
      />

      <SectionTabs tabs={tabs} activeTab={activeTab} onChange={setActiveTab} paramName="tab" />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="عدد المحافظ" value={rows.length} />
        <StatCard label="إجمالي الرصيد الدفتري" value={formatSar(totalBalance)} />
        <StatCard label="مستندات الشحن" value={fundingDocs.length} />
        <StatCard label="الفترة" value={period} />
      </div>

      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void loadWallets()} />
      ) : (
        <>
          {(activeTab === 'wallets' ||
            activeTab === 'balance' ||
            activeTab === 'providers' ||
            (mode === 'prepaid' && activeTab === 'settings')) && (
            <DataTable
              columns={columns}
              data={rows}
              emptyTitle="لا توجد محافظ"
              emptyDescription="أنشئ محفظة على حساب دليل معتمد ثم شغّل الشحن من محرك المدفوعات."
            />
          )}

          {(activeTab === 'wallets' ||
            activeTab === 'providers' ||
            activeTab === 'settings' ||
            activeTab === 'balance') &&
            hasAllowedAction({ allowedActions: listActions }, 'create_wallet') && (
              <form onSubmit={createWallet} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:grid-cols-4 items-end">
                <SearchableSelect
                  value={createForm.companyId}
                  onChange={(v) => setCreateForm((p) => ({ ...p, companyId: v }))}
                  options={companyOptions}
                  placeholder="الشركة"
                />
                <input
                  className={inputClass}
                  placeholder="اسم المحفظة / المزود"
                  value={createForm.provider}
                  onChange={(e) => setCreateForm((p) => ({ ...p, provider: e.target.value }))}
                  required={mode !== 'mada'}
                />
                <div className="space-y-1">
                  {canCreateCoa && mode !== 'mada' && (
                    <button
                      type="button"
                      onClick={() => setCoaDialogOpen(true)}
                      className="text-[11px] font-bold text-[var(--accent)] hover:underline cursor-pointer"
                    >
                      + إضافة حساب محفظة في الدليل
                    </button>
                  )}
                  {walletAccountOptions.length > 0 ? (
                    <SearchableSelect
                      value={createForm.accountCode}
                      onChange={(v) => setCreateForm((p) => ({ ...p, accountCode: v }))}
                      options={walletAccountOptions}
                      placeholder="حساب المحفظة (1124 / 1141)"
                      disabled={mode === 'mada'}
                    />
                  ) : (
                    <input
                      className={inputClass}
                      placeholder="رمز الحساب (1124 / 1141)"
                      value={createForm.accountCode}
                      onChange={(e) => setCreateForm((p) => ({ ...p, accountCode: e.target.value }))}
                      required
                      readOnly={mode === 'mada'}
                    />
                  )}
                </div>
                <button
                  type="submit"
                  disabled={busy}
                  className="inline-flex items-center justify-center gap-1 rounded-xl bg-[var(--accent)] px-3 py-2 text-xs font-bold text-[var(--accent-ink)] cursor-pointer"
                >
                  <Plus size={14} aria-hidden="true" /> إضافة محفظة
                </button>
              </form>
            )}

          {(activeTab === 'topups' || activeTab === 'movements' || activeTab === 'wallets') && (
            <div className="space-y-3">
              <form onSubmit={fundWallet} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:grid-cols-4">
                <SearchableSelect
                  value={fundForm.walletId}
                  onChange={(v) => setFundForm((p) => ({ ...p, walletId: v }))}
                  options={walletOptions}
                  placeholder="اختر المحفظة..."
                />
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  className={inputClass}
                  placeholder="المبلغ"
                  value={fundForm.amount}
                  onChange={(e) => setFundForm((p) => ({ ...p, amount: e.target.value }))}
                  required
                />
                <input
                  className={inputClass}
                  placeholder="المرجع"
                  value={fundForm.reference}
                  onChange={(e) => setFundForm((p) => ({ ...p, reference: e.target.value }))}
                  required
                />
                <button type="submit" disabled={busy} className="rounded-xl bg-[var(--accent)] px-3 py-2 text-xs font-bold text-[var(--accent-ink)] cursor-pointer">
                  {mode === 'mada' ? 'تغذية محفظة مدا' : 'شحن المحفظة'}
                </button>
              </form>
              {(activeTab === 'topups' || activeTab === 'movements') && (
                <DataTable
                  columns={docColumns}
                  data={fundingDocs}
                  emptyTitle="لا توجد مستندات شحن"
                  emptyDescription="تظهر هنا مستندات شحن المحفظة لمتابعة اعتمادها وإصدار سندات الصرف."
                />
              )}
            </div>
          )}

          {(activeTab === 'reconcile' || activeTab === 'balance') && mode !== 'prepaid' && (
            <form onSubmit={reconcileWallet} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:grid-cols-5">
              <SearchableSelect
                value={reconcileForm.walletId}
                onChange={(v) => setReconcileForm((p) => ({ ...p, walletId: v }))}
                options={walletOptions}
                placeholder="المحفظة"
              />
              <input
                className={inputClass}
                type="month"
                value={period}
                onChange={(e) => setPeriod(e.target.value)}
              />
              <input
                type="number"
                step="0.01"
                min="0"
                className={inputClass}
                placeholder="رصيد كشف المزود"
                value={reconcileForm.statementBalance}
                onChange={(e) => setReconcileForm((p) => ({ ...p, statementBalance: e.target.value }))}
                required
              />
              <input
                className={inputClass}
                placeholder="مرجع المطابقة"
                value={reconcileForm.reference}
                onChange={(e) => setReconcileForm((p) => ({ ...p, reference: e.target.value }))}
              />
              <button type="submit" disabled={busy} className="rounded-xl bg-[var(--accent)] px-3 py-2 text-xs font-bold text-[var(--accent-ink)] cursor-pointer">
                مطابقة الرصيد
              </button>
            </form>
          )}

          {mode === 'mada' && activeTab === 'cards' && (
            <form onSubmit={issueCard} className="grid gap-2 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:grid-cols-3">
              <input
                className={inputClass}
                placeholder="معرف سند الدفع المعتمد لسلفة الإنترنت"
                value={cardForm.paymentId}
                onChange={(e) => setCardForm({ paymentId: e.target.value })}
                required
              />
              <button
                type="submit"
                disabled={busy || !canPayout}
                className="rounded-xl bg-[var(--accent)] px-3 py-2 text-xs font-bold text-[var(--accent-ink)] sm:col-span-2 cursor-pointer"
              >
                صرف بطاقة شحن لمندوب
              </button>
            </form>
          )}

          {mode === 'mada' && activeTab === 'settings' && (
            <div className="grid gap-3 lg:grid-cols-2">
              <DataTable
                columns={[
                  { key: 'code', header: 'الرمز', type: 'text', getValue: (r) => String(r.code || '') },
                  { key: 'nameAr', header: 'الشبكة', type: 'text', getValue: (r) => String(r.nameAr || '') },
                ]}
                data={networks}
                emptyTitle="لا شبكات معتمدة"
                emptyDescription="أدر الشبكات من قوائم الإعدادات."
              />
              <DataTable
                columns={[
                  { key: 'code', header: 'الرمز', type: 'text', getValue: (r) => String(r.code || '') },
                  { key: 'nameAr', header: 'الباقة', type: 'text', getValue: (r) => String(r.nameAr || '') },
                ]}
                data={packages}
                emptyTitle="لا باقات معتمدة"
                emptyDescription="أدر الباقات من قوائم الإعدادات."
              />
            </div>
          )}

          {mode === 'fuel' && activeTab === 'consumption' && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-xs text-[var(--muted)]">استهلاك بطاقات الوقود لشهر {period}</span>
                <SourceBadge source="live" />
              </div>
              <DataTable
                columns={[
                  {
                    key: 'card',
                    header: 'البطاقة',
                    type: 'text',
                    getValue: (r) => String(r.cardNumber || r.cardName || r.id || ''),
                  },
                  {
                    key: 'provider',
                    header: 'المزود',
                    type: 'list',
                    getValue: (r) => String(r.provider || ''),
                  },
                  {
                    key: 'liters',
                    header: 'اللترات',
                    type: 'number',
                    getValue: (r) => Number(r.liters ?? r.quantity ?? 0),
                  },
                  {
                    key: 'amount',
                    header: 'التكلفة',
                    type: 'number',
                    numeric: true,
                    money: true,
                    getValue: (r) => Number(r.totalAmount ?? r.amountInclVat ?? r.amount ?? 0),
                    render: (r) => <span className="num font-bold">{formatSar(Number(r.totalAmount ?? r.amountInclVat ?? r.amount ?? 0))}</span>,
                    summary: 'sum',
                  },
                ]}
                data={consumption}
                emptyTitle="لا استهلاك في الفترة"
                emptyDescription="القراءة مباشرة من نظام الموارد؛ الإدارة هنا في المدفوعات."
              />
            </div>
          )}
        </>
      )}

      <AddCoaAccountDialog
        open={coaDialogOpen}
        onClose={() => setCoaDialogOpen(false)}
        defaultParentCode="1124"
        onCreated={async (code) => {
          await loadWallets();
          if (code) setCreateForm((p) => ({ ...p, accountCode: code }));
        }}
      />
    </div>
  );
}
