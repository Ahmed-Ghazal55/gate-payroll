'use client';

import { formatSar } from '../../lib/money';
import {
  isBankVaultCode,
  isCashVaultCode,
  vaultGroupLabelAr,
  vaultKindLabelAr,
} from '../../lib/vault-labels';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import {
  TREASURY_TAB_LABELS,
  buildTabRedirectUrl,
  resolveTreasuryTab,
  type TreasuryTab,
} from '../../lib/page-tabs';
import { usePeriod } from '../../lib/use-period';
import {
  Landmark,
  Coins,
  FileSpreadsheet,
  Download,
  Search,
  Filter,
  Eye,
  ArrowRightLeft,
  Calendar,
  CheckCircle2,
  AlertCircle,
  Building2,
  Clock,
  ArrowDownLeft,
  ArrowUpRight,
  RefreshCw,
  Wallet,
  Banknote,
  Check,
  AlertTriangle,
  Receipt,
  History,
  Wifi,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { IbanDisplay } from '../../components/ui/IbanDisplay';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import type {
  FinanceAccount,
  FinanceJournal,
  BankPayrollFilePreview,
  AdvanceRequest,
  CompanySponsor,
} from '../../lib/types';
import { JahezWorkspace } from '../../features/treasury/JahezWorkspace';
import { TreasuryExtrasPanel } from '../../features/treasury/TreasuryExtrasPanel';

type TabId = TreasuryTab;

function TreasuryPageContent() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();

  const resolvedUrlTab = resolveTreasuryTab(searchParams?.get('tab'));
  const resolvedTab: TabId = resolvedUrlTab.tab;

  useEffect(() => {
    if (!resolvedUrlTab.legacy) return;
    router.replace(buildTabRedirectUrl(pathname || '/treasury/', searchParams, resolvedUrlTab), { scroll: false });
  }, [resolvedUrlTab.legacy, resolvedUrlTab.tab, pathname, router, searchParams]);

  const [activeTab, setActiveTab] = useState<TabId>(resolvedTab);

  useEffect(() => {
    if (resolvedTab && resolvedTab !== activeTab) {
      setActiveTab(resolvedTab);
    }
  }, [resolvedTab]);

  const handleTabChange = (newTab: TabId) => {
    setActiveTab(newTab);
    const params = new URLSearchParams(searchParams?.toString() || '');
    params.set('tab', newTab);
    router.replace(`${pathname || '/treasury/'}?${params.toString()}`, { scroll: false });
  };

  const { period: selectedPeriod, setPeriod: setSelectedPeriod } = usePeriod();
  const [selectedSponsor, setSelectedSponsor] = useState<string>('all');
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [vaultTypeFilter, setVaultTypeFilter] = useState<string>('all');

  useEffect(() => {
    api.getFinanceSponsors().then((res) => setCompanies(res || [])).catch(() => {});
  }, []);

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<FinanceAccount[]>([]);
  const [journals, setJournals] = useState<FinanceJournal[]>([]);
  const [bankFilePreview, setBankFilePreview] = useState<BankPayrollFilePreview | null>(null);
  const [loadingBankFile, setLoadingBankFile] = useState<boolean>(false);
  const [bankFileError, setBankFileError] = useState<string | null>(null);
  const [exportingBankFile, setExportingBankFile] = useState<boolean>(false);
  const [comparisonOnly, setComparisonOnly] = useState<boolean>(false);

  // Drawer state for account details
  const [selectedAccount, setSelectedAccount] = useState<FinanceAccount | null>(null);
  const [drawerOpen, setDrawerOpen] = useState<boolean>(false);

  // ========================================================
  // R5-2 ADVANCE PAYOUTS QUEUE (BR §25.3)
  // ========================================================
  const [advancePayouts, setAdvancePayouts] = useState<AdvanceRequest[]>([]);
  const [loadingPayouts, setLoadingPayouts] = useState<boolean>(false);
  const [payoutsError, setPayoutsError] = useState<string | null>(null);

  // Payout Dialog
  const [payoutModalOpen, setPayoutModalOpen] = useState<boolean>(false);
  const [selectedPayoutReq, setSelectedPayoutReq] = useState<AdvanceRequest | null>(null);
  const [payoutReference, setPayoutReference] = useState<string>('');
  const [payoutNote, setPayoutNote] = useState<string>('');
  const [submittingPayout, setSubmittingPayout] = useState<boolean>(false);
  const [payoutServerError, setPayoutServerError] = useState<string | null>(null);

  // Details Drawer for Payout Request
  const [payoutDetailsReq, setPayoutDetailsReq] = useState<AdvanceRequest | null>(null);

  const canPayoutAdvances = can('advances.payout') || isAdmin;

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [accsRes, jrnlsRes] = await Promise.all([
        api.getFinanceAccounts(),
        api.getFinanceJournals(),
      ]);
      setAccounts(accsRes || []);
      setJournals(jrnlsRes || []);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل بيانات الخزينة والحسابات';
      setError(msg);
      toast(msg, 'danger');
    } finally {
      setLoading(false);
    }
  };

  const loadAdvancePayouts = async () => {
    setLoadingPayouts(true);
    setPayoutsError(null);
    try {
      const list = await api.getAdvanceRequests({ role: 'payout', status: 'approved' });
      setAdvancePayouts(Array.isArray(list) ? list : []);
    } catch (err: any) {
      setPayoutsError(err.message || 'تعذر استرجاع طابور صرف السلف');
    } finally {
      setLoadingPayouts(false);
    }
  };

  const loadBankFilePreview = async (period: string, sponsorId?: string, parallel = comparisonOnly) => {
    setLoadingBankFile(true);
    setBankFileError(null);
    try {
      const sp = sponsorId === 'all' ? undefined : sponsorId;
      if (!sp) {
        setBankFilePreview(null);
        setBankFileError(null);
        return;
      }
      const preview = await api.previewBankFile({ period, sponsorId: sp, comparisonOnly: parallel });
      setBankFilePreview(preview);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل معاينة ملف صرف البنك';
      setBankFileError(msg);
      toast(msg, 'danger');
      setBankFilePreview(null);
    } finally {
      setLoadingBankFile(false);
    }
  };

  useEffect(() => {
    loadData();
    if (canPayoutAdvances) {
      loadAdvancePayouts();
    }
  }, []);

  useEffect(() => {
    if (activeTab === 'bank_file') {
      loadBankFilePreview(selectedPeriod, selectedSponsor);
    } else if (activeTab === 'advance_payouts') {
      loadAdvancePayouts();
    }
  }, [activeTab, selectedPeriod, selectedSponsor, comparisonOnly]);

  const handleExportBankCsv = async () => {
    setExportingBankFile(true);
    try {
      const sp = selectedSponsor === 'all' ? undefined : selectedSponsor;
      if (!sp || !bankFilePreview) throw new Error('معاينة ملف البنك مطلوبة مع شركة محددة');
      const rows = bankFilePreview.records.map((r) => ({
        bank: r.bank || r.bankName,
        iban: r.iban,
        amount: r.amount,
        notes: r.notes || '',
        name: r.name || r.riderName,
        iqama: r.iqama || r.riderIqama,
        address: r.address || '',
        errors: r.errors || [],
      }));
      const blob = await api.exportBankFile({
        period: selectedPeriod,
        sponsorId: sp,
        comparisonOnly,
        previewToken: bankFilePreview.previewToken,
        rows,
      });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${comparisonOnly ? 'DO-NOT-SEND_' : ''}bank-payroll-${selectedPeriod}.csv`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      toast('تم تصدير ملف صرف الرواتب البنكي بنجاح', 'success');
    } catch (err: any) {
      toast(err.message || 'تعذر تصدير ملف البنك', 'danger');
    } finally {
      setExportingBankFile(false);
    }
  };

  const handleOpenPayoutDialog = (req: AdvanceRequest) => {
    setSelectedPayoutReq(req);
    setPayoutReference('');
    setPayoutNote('');
    setPayoutServerError(null);
    setPayoutModalOpen(true);
  };

  const handleExecutePayout = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedPayoutReq) return;
    if (!payoutReference.trim()) {
      toast('يرجى إدخال المرجع البنكي أو رقم سند الصرف', 'warning');
      return;
    }
    setSubmittingPayout(true);
    setPayoutServerError(null);
    try {
      await api.payoutAdvanceRequest(selectedPayoutReq.id, {
        reference: payoutReference.trim(),
        note: payoutNote.trim() || undefined,
      });
      toast(`تم صرف السلفة بنجاح (${formatSar(selectedPayoutReq.amount)}) وإنشاء القيد المحاسبي والمديونية`, 'success');
      setPayoutModalOpen(false);
      setSelectedPayoutReq(null);
      setPayoutReference('');
      setPayoutNote('');
      loadAdvancePayouts();
      loadData(); // reload accounts balances
    } catch (err: any) {
      setPayoutServerError(err.message || 'تعذر إتمام الصرف');
      toast(err.message || 'فشل صرف السلفة', 'danger');
    } finally {
      setSubmittingPayout(false);
    }
  };

  // Filtered Vault Accounts (1110 - 1140)
  const vaultAccounts = useMemo(() => {
    return accounts.filter((a) => {
      const isVault =
        a.code?.startsWith('111') ||
        a.code?.startsWith('112') ||
        a.code?.startsWith('113') ||
        a.code?.startsWith('114');
      if (!isVault) return false;
      if (vaultTypeFilter !== 'all') {
        if (vaultTypeFilter === 'bank' && !isBankVaultCode(a.code)) return false;
        if (vaultTypeFilter === 'cash' && !isCashVaultCode(a.code)) return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchCode = a.code?.toLowerCase().includes(q);
        const matchName = a.nameAr?.toLowerCase().includes(q);
        if (!matchCode && !matchName) return false;
      }
      return true;
    });
  }, [accounts, vaultTypeFilter, searchQuery]);

  // Derived balances
  const totalBankBalance = useMemo(
    () => accounts.filter((a) => isBankVaultCode(a.code)).reduce((s, a) => s + (a.netBalance || 0), 0),
    [accounts]
  );
  const totalCashBalance = useMemo(
    () => accounts.filter((a) => isCashVaultCode(a.code)).reduce((s, a) => s + (a.netBalance || 0), 0),
    [accounts]
  );

  // Selected account movements
  const selectedAccountJournals = useMemo(() => {
    if (!selectedAccount) return [];
    return journals.filter((j) =>
      j.lines?.some((l) => l.accountCode === selectedAccount.code)
    );
  }, [selectedAccount, journals]);

  const vaultsCols = useMemo(
    () => [
      { key: 'code', header: 'رمز الحساب', type: 'list' as const, getValue: (a: FinanceAccount) => a.code || '' },
      { key: 'nameAr', header: 'اسم البنك / الخزينة', type: 'text' as const, getValue: (a: FinanceAccount) => a.nameAr || '' },
      {
        key: 'type',
        header: 'النوع والوظيفة',
        type: 'list' as const,
        getValue: (a: FinanceAccount) => (isBankVaultCode(a.code) ? 'bank' : 'cash'),
        labelOf: (v: unknown) => (v === 'bank' ? 'حساب بنكي رسمي' : 'صندوق نقدية تشغيلي'),
      },
      { key: 'sponsorName', header: 'الجهة الكفيلة', type: 'list' as const, getValue: (a: FinanceAccount) => (a as any).sponsorName || 'عام' },
      {
        key: 'netBalance',
        header: 'الرصيد الصافي الفعلي',
        type: 'number' as const,
        getValue: (a: FinanceAccount) => Number(a.netBalance ?? 0),
        toneOf: (v: unknown) => (Number(v ?? 0) < 0 ? 'danger' : 'success'),
      },
      { key: 'naturalSideAr', header: 'طبيعة الحساب', type: 'list' as const, getValue: (a: FinanceAccount) => a.naturalSideAr || '' },
    ],
    []
  );
  const vaultsFilter = useTableAutoFilter<FinanceAccount>(vaultAccounts, vaultsCols);

  const payoutsCols = useMemo(
    () => [
      { key: 'id', header: 'معرف السلفة', type: 'text' as const, getValue: (r: AdvanceRequest) => r.id || '' },
      { key: 'rider', header: 'المندوب والإقامة', type: 'text' as const, getValue: (r: AdvanceRequest) => `${r.riderName || ''} ${r.riderIqama || ''}`.trim() },
      {
        key: 'type',
        header: 'النوع والخزينة المكلفة',
        type: 'list' as const,
        getValue: (r: AdvanceRequest) => r.type || '',
        labelOf: (v: unknown) => (v === 'internet' ? 'سلفة إنترنت' : v === 'cash' ? 'خزينة نقدية' : 'حساب بنكي'),
      },
      { key: 'amount', header: 'المبلغ المعتمد', type: 'number' as const, getValue: (r: AdvanceRequest) => Number(r.amount ?? 0) },
      { key: 'monthlyInstallment', header: 'القسط الشهري', type: 'number' as const, getValue: (r: AdvanceRequest) => Number(r.monthlyInstallment ?? 0) },
      { key: 'approvedBy', header: 'المعتمد وتاريخه', type: 'list' as const, getValue: (r: AdvanceRequest) => r.approvedBy || '' },
      { key: 'reason', header: 'سبب الاعتماد', type: 'text' as const, getValue: (r: AdvanceRequest) => r.coverageReason || r.reason || '' },
    ],
    []
  );
  const payoutsFilter = useTableAutoFilter<AdvanceRequest>(advancePayouts, payoutsCols);

  const bankRecords = useMemo(() => bankFilePreview?.records || [], [bankFilePreview?.records]);
  const bankRecordsCols = useMemo(
    () => [
      { key: 'riderIqama', header: 'رقم الإقامة', type: 'text' as const, getValue: (r: any) => r.riderIqama || '' },
      { key: 'riderName', header: 'اسم المندوب', type: 'text' as const, getValue: (r: any) => r.riderName || '' },
      { key: 'iban', header: 'الآيبان البنكي', type: 'text' as const, getValue: (r: any) => `${r.iban || ''} ${r.bankName || ''}`.trim() },
      { key: 'amount', header: 'مبلغ الصرف المستحق', type: 'number' as const, getValue: (r: any) => Number(r.amount ?? 0) },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (r: any) => r.status || 'excluded',
        labelOf: (v: unknown) => (v === 'valid' ? 'جاهز للصرف' : 'مستبعد'),
        toneOf: (v: unknown) => (v === 'valid' ? 'success' : 'warning'),
      },
    ],
    []
  );
  const bankRecordsFilter = useTableAutoFilter(bankRecords, bankRecordsCols);

  const movementsCols = useMemo(
    () => [
      { key: 'id', header: 'رقم القيد', type: 'text' as const, getValue: (j: FinanceJournal) => j.id || '' },
      { key: 'description', header: 'البيان والوصف', type: 'text' as const, getValue: (j: FinanceJournal) => j.description || '' },
      { key: 'entryDate', header: 'تاريخ الحركة', type: 'date' as const, getValue: (j: FinanceJournal) => j.entryDate?.split('T')[0] || '' },
      { key: 'totalDebit', header: 'المبلغ الإجمالي', type: 'number' as const, getValue: (j: FinanceJournal) => Number(j.totalDebit ?? 0) },
      { key: 'postedBy', header: 'المسؤول', type: 'list' as const, getValue: (j: FinanceJournal) => (j as any).postedBy || j.createdBy || 'النظام' },
      { key: 'status', header: 'الحالة', type: 'list' as const, getValue: () => 'posted', labelOf: () => 'مرحل', toneOf: () => 'success' as const },
    ],
    []
  );
  const movementsFilter = useTableAutoFilter<FinanceJournal>(journals, movementsCols);

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="البنوك والصناديق والخزن"
        description="إدارة ومتابعة أرصدة الحسابات البنكية والخزن النقدية (1110-1140)، طابور صرف السلف المعتمدة لأمناء الصناديق، وإصدار ملف صرف الرواتب البنكي (CSV)."
        eyebrow={TREASURY_TAB_LABELS[activeTab]}
        primaryAction={{
          label: 'تحديث الأرصدة',
          icon: <RefreshCw size={15} />,
          onClick: activeTab === 'advance_payouts' ? loadAdvancePayouts : loadData,
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي أرصدة البنوك"
          value={`${formatSar(totalBankBalance)}`}
          hint="الأرصدة الصافية الفعلية في الحسابات البنكية"
          icon={Landmark}
        />
        <StatCard
          label="إجمالي الصناديق والخزن"
          value={`${formatSar(totalCashBalance)}`}
          hint="الخزن الرئيسية والفرعية وصناديق العهد"
          icon={Coins}
        />
        <StatCard
          label="طابور صرف السلف"
          value={String(advancePayouts.length)}
          hint="سلف معتمدة بانتظار تأكيد الصرف والتحويل"
          icon={Clock}
        />
        <StatCard
          label="ملف صرف البنك للفترة"
          value={bankFilePreview ? `${formatSar(bankFilePreview.totalAmount)}` : selectedPeriod}
          hint={bankFilePreview ? `${bankFilePreview.validRecordsCount} سجلات جاهزة للصرف` : 'اختر تبويب ملف البنك للمعاينة'}
          icon={FileSpreadsheet}
        />
      </div>

      {/* ======================================================== */}
      {/* Sub-tab 1: الحسابات البنكية والصناديق                      */}
      {/* ======================================================== */}
      {activeTab === 'vaults' && (
        <div className="space-y-4">
          <div className="flex flex-col sm:flex-row sm:flex-wrap sm:items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 min-w-0 w-full sm:max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="بحث برقم الحساب أو اسم البنك/الخزينة..."
                className="w-full min-w-0 bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2 w-full sm:w-auto shrink-0">
              <SearchableSelect
                id="treasury-vault-type"
                value={vaultTypeFilter}
                onChange={setVaultTypeFilter}
                ariaLabel="نوع الخزينة"
                placeholder="كافة أنواع الخزن"
                size="sm"
                className="w-full sm:w-52"
                options={[
                  { value: 'all', label: 'كافة أنواع الخزن' },
                  { value: 'cash', label: 'صناديق نقدية (1110–1120)' },
                  { value: 'bank', label: 'حسابات بنكية (1130)' },
                ]}
              />
            </div>
          </div>

          {error ? (
            <ErrorState title="تعذر تحميل الحسابات" message={error} onRetry={loadData} />
          ) : loading ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : vaultAccounts.length === 0 ? (
            <EmptyState
              title="لم يتم العثور على حسابات خزن"
              description="لم يتم العثور على حسابات بنكية أو صناديق نقدية مطابقة لمعايير البحث."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={vaultsFilter} totalCount={vaultAccounts.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={vaultsFilter} colKey="code" className="p-3 text-start" />
                      <AutoFilterTh controller={vaultsFilter} colKey="nameAr" className="p-3 text-start" />
                      <AutoFilterTh controller={vaultsFilter} colKey="type" className="p-3 text-start" />
                      <AutoFilterTh controller={vaultsFilter} colKey="sponsorName" className="p-3 text-start" />
                      <AutoFilterTh controller={vaultsFilter} colKey="netBalance" className="p-3 text-start" />
                      <AutoFilterTh controller={vaultsFilter} colKey="naturalSideAr" className="p-3 text-start" />
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {vaultsFilter.rows.map((a) => (
                      <tr key={a.code} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--accent)]">{a.code}</td>
                        <td className="p-3 font-semibold">{a.nameAr}</td>
                        <td className="p-3 text-[var(--muted)]">
                          <span className="inline-flex max-w-full px-2 py-0.5 rounded-full text-xs leading-snug whitespace-normal break-words bg-[var(--raised)] text-[var(--ink)]">
                            {vaultGroupLabelAr(a.code)}
                          </span>
                        </td>
                        <td className="p-3 text-[var(--muted)]">{(a as any).sponsorName || 'عام'}</td>
                        <td className="p-3 num font-bold text-sm">
                          <span className={a.netBalance < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'}>
                            {formatSar(a.netBalance)}
                          </span>
                        </td>
                        <td className="p-3 text-xs text-[var(--muted)]">{a.naturalSideAr}</td>
                        <td className="p-3">
                          <button
                            type="button"
                            onClick={() => {
                              setSelectedAccount(a);
                              setDrawerOpen(true);
                            }}
                            className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-xs transition cursor-pointer"
                          >
                            <Eye size={13} />
                            <span>كشف الحركات</span>
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 2: صرف السلف المعتمدة (الأمناء BR §25.3)           */}
      {/* ======================================================== */}
      {activeTab === 'advance_payouts' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">طابور صرف السلف المعتمدة (أمين الخزينة أو البنك)</h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              يعرض طلبات السلف التي تم اعتمادها محاسبياً وتقع ضمن الخزن أو الحسابات البنكية التي تملك صلاحية صرفها (نظام التوجيه).
            </p>
          </div>

          {payoutsError ? (
            <ErrorState title="تعذر تحميل طابور الصرف" message={payoutsError} onRetry={loadAdvancePayouts} />
          ) : loadingPayouts ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : advancePayouts.length === 0 ? (
            <EmptyState
              title="لا توجد سلف معتمدة بانتظار الصرف"
              description="كافة السلف المعتمدة للخزن والبنوك المكلف بإدارتها تم صرفها بالكامل."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={payoutsFilter} totalCount={advancePayouts.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={payoutsFilter} colKey="id" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="rider" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="type" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="amount" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="monthlyInstallment" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="approvedBy" className="p-3 text-start" />
                      <AutoFilterTh controller={payoutsFilter} colKey="reason" className="p-3 text-start" />
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {payoutsFilter.rows.map((req) => (
                      <tr key={req.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-[11px] text-[var(--muted)]">{req.id}</td>
                        <td className="p-3">
                          <div className="font-bold text-[var(--ink)]">{req.riderName}</div>
                          <div className="font-mono text-[11px] text-[var(--accent)]">{req.riderIqama}</div>
                        </td>
                        <td className="p-3">
                          <div className="inline-flex items-center gap-1 font-semibold">
                            {req.type === 'internet' ? (
                              <Wifi size={13} className="text-sky-600" />
                            ) : req.type === 'cash' ? (
                              <Wallet size={13} />
                            ) : (
                              <Banknote size={13} />
                            )}
                            <span>
                              {req.type === 'internet'
                                ? req.fulfilment === 'recharge_card'
                                  ? `كارت شحن (${req.networkCode || ''} - ${req.packageCode || ''})`
                                  : req.fulfilment === 'bank'
                                  ? 'سلفة نت (بنكي)'
                                  : 'سلفة نت (نقدي)'
                                : req.type === 'cash'
                                ? 'خزينة نقدية'
                                : 'حساب بنكي'}
                            </span>
                          </div>
                          <div className="font-mono text-[10px] text-[var(--muted)]">{req.vaultCode}</div>
                        </td>
                        <td className="p-3 num font-bold text-sm text-[var(--money-pos)]">
                          {formatSar(req.amount)}
                        </td>
                        <td className="p-3 num font-medium text-xs text-[var(--ink)]">
                          {req.monthlyInstallment ? formatSar(req.monthlyInstallment) : '—'}
                        </td>
                        <td className="p-3 text-xs">
                          <div className="font-semibold text-[var(--ink)]">{req.approvedBy}</div>
                          <div className="text-[10px] text-[var(--muted)] num">{req.approvedAt?.split('T')[0]}</div>
                        </td>
                        <td className="p-3 text-xs text-[var(--muted)] max-w-xs truncate">
                          {req.coverageReason || req.reason}
                        </td>
                        <td className="p-3">
                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              id={`btn-payout-advance-${req.id}`}
                              onClick={() => handleOpenPayoutDialog(req)}
                              className="px-3 py-1.5 rounded-lg bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer inline-flex items-center gap-1"
                            >
                              <Check size={13} />
                              <span>صرف السلفة</span>
                            </button>
                            <button
                              type="button"
                              onClick={() => setPayoutDetailsReq(req)}
                              className="p-1.5 rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-xs text-[var(--muted)] transition cursor-pointer"
                              title="عرض القرارات السابقة"
                            >
                              <History size={14} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 3: ملف صرف البنك (CSV)                             */}
      {/* ======================================================== */}
      {activeTab === 'bank_file' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-3 flex-wrap">
              <div>
                <label className="block text-[11px] font-bold text-[var(--muted)] mb-1">فترة الاستحقاق:</label>
                <input
                  type="month"
                  value={selectedPeriod}
                  onChange={(e) => setSelectedPeriod(e.target.value)}
                  className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono font-bold focus:outline-hidden"
                />
              </div>

              <div>
                <label className="block text-[11px] font-bold text-[var(--muted)] mb-1">الشركة الكفيلة:</label>
                <SearchableSelect
                  id="treasury-sponsor-filter"
                  value={selectedSponsor}
                  onChange={setSelectedSponsor}
                  ariaLabel="الشركة الكفيلة"
                  placeholder="كافة الكفلاء والشركات"
                  size="sm"
                  className="w-52"
                  options={[
                    { value: 'all', label: 'كافة الكفلاء والشركات' },
                    ...companies.map((c) => ({
                      value: c.code || c.id,
                      label: `${c.nameAr || c.shortNameAr || c.code || c.id}${c.code ? ` (${c.code})` : ''}`,
                    })),
                  ]}
                />
              </div>
              <label className="inline-flex items-center gap-2 text-xs font-bold mt-5 cursor-pointer">
                <input type="checkbox" checked={comparisonOnly} onChange={(e) => setComparisonOnly(e.target.checked)} />
                تشغيل موازٍ — لا يُرسل
              </label>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={exportingBankFile || !bankFilePreview || !bankFilePreview.canExport || selectedSponsor === 'all'}
                onClick={handleExportBankCsv}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 disabled:opacity-50 transition cursor-pointer"
              >
                <Download size={14} />
                <span>{exportingBankFile ? 'جاري التصدير...' : comparisonOnly ? 'تحميل ملف مقارنة (لا يُرسل)' : 'تحميل ملف البنك (CSV)'}</span>
              </button>
            </div>
          </div>

          {comparisonOnly && bankFilePreview?.doNotSendMark && (
            <div className="p-3 rounded-xl border border-[var(--warning)]/40 bg-[var(--warning)]/10 text-xs font-bold text-[var(--warning)]">
              {bankFilePreview.doNotSendMark} — الملف للمقارنة فقط ولن يُرسل للبنك.
            </div>
          )}
          {selectedSponsor === 'all' ? (
            <EmptyState
              title="اختر شركة كفيلة"
              description="حدد الشركة الكفيلة أعلاه لمعاينة ملف صرف الرواتب البنكي لهذه الفترة."
            />
          ) : bankFileError ? (
            <ErrorState
              title="تعذر تحميل ملف البنك"
              message={bankFileError}
              onRetry={() => loadBankFilePreview(selectedPeriod, selectedSponsor)}
            />
          ) : loadingBankFile ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : !bankFilePreview || bankFilePreview.records.length === 0 ? (
            <EmptyState
              title="لا توجد بيانات لصرف الرواتب في هذه الفترة"
              description="يتم بناء ملف البنك بناءً على المسيرات المعتمدة والأرصدة المستحقة للمناديب."
            />
          ) : (
            <div className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)] font-medium">إجمالي مبلغ الصرف</div>
                  <div className="text-lg font-bold num text-[var(--accent)] mt-1">
                    {formatSar(bankFilePreview.totalAmount)}
                  </div>
                </div>
                <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)] font-medium">السجلات الجاهزة للصرف</div>
                  <div className="text-lg font-bold num text-[var(--money-pos)] mt-1">
                    {bankFilePreview.validRecordsCount} مندوب
                  </div>
                </div>
                <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)] font-medium">السجلات المستبعدة (أرصدة صفرية/أخطاء)</div>
                  <div className="text-lg font-bold num text-[var(--muted)] mt-1">
                    {bankFilePreview.excludedCount} سجل
                  </div>
                </div>
              </div>

              <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                <AutoFilterBar controller={bankRecordsFilter} totalCount={bankFilePreview.records.length} />
                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs sm:text-sm">
                    <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                      <tr>
                        <AutoFilterTh controller={bankRecordsFilter} colKey="riderIqama" className="p-3 text-start" />
                        <AutoFilterTh controller={bankRecordsFilter} colKey="riderName" className="p-3 text-start" />
                        <AutoFilterTh controller={bankRecordsFilter} colKey="iban" className="p-3 text-start" />
                        <AutoFilterTh controller={bankRecordsFilter} colKey="amount" className="p-3 text-start" />
                        <AutoFilterTh controller={bankRecordsFilter} colKey="status" className="p-3 text-start" />
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {bankRecordsFilter.rows.map((r, idx) => (
                        <tr key={idx} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono">{r.riderIqama}</td>
                          <td className="p-3 font-semibold">{r.riderName}</td>
                          <td className="p-3">
                            <IbanDisplay iban={r.iban} bank={r.bankName} />
                          </td>
                          <td className="p-3 num font-bold text-[var(--accent)]">
                            {formatSar(r.amount)}
                          </td>
                          <td className="p-3">
                            <span
                              className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                                r.status === 'valid'
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              }`}
                            >
                              {r.status === 'valid' ? (
                                <>
                                  <CheckCircle2 size={12} />
                                  <span>جاهز للصرف</span>
                                </>
                              ) : (
                                <>
                                  <AlertCircle size={12} />
                                  <span>{r.validationMessage || 'مستبعد'}</span>
                                </>
                              )}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 4: سجل حركات الخزينة والقيود                      */}
      {/* ======================================================== */}

      {activeTab === 'jahez' && <JahezWorkspace />}
      {activeTab === 'extras' && <TreasuryExtrasPanel />}

      {activeTab === 'movements' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">سجل القيود المزدوجة وحركات الخزينة</h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              عرض القيود المحاسبية مع ربط الحركات العكسية بأصولها والتأكد من التوازن المحاسبي.
            </p>
          </div>

          {error ? (
            <ErrorState title="تعذر تحميل سجل القيود" message={error} onRetry={loadData} />
          ) : loading ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : journals.length === 0 ? (
            <EmptyState
              title="لا توجد قيود يومية مسجلة"
              description="لم يتم تسجيل أي حركات محاسبية أو قيود يومية في النظام بعد."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={movementsFilter} totalCount={journals.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={movementsFilter} colKey="id" className="p-3 text-start" />
                      <AutoFilterTh controller={movementsFilter} colKey="description" className="p-3 text-start" />
                      <AutoFilterTh controller={movementsFilter} colKey="entryDate" className="p-3 text-start" />
                      <AutoFilterTh controller={movementsFilter} colKey="totalDebit" className="p-3 text-start" />
                      <AutoFilterTh controller={movementsFilter} colKey="postedBy" className="p-3 text-start" />
                      <AutoFilterTh controller={movementsFilter} colKey="status" className="p-3 text-start" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {movementsFilter.rows.map((j) => (
                      <tr key={j.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs text-[var(--accent)]">#{j.id.substring(0, 8)}</td>
                        <td className="p-3 font-semibold">{j.description}</td>
                        <td className="p-3 num text-xs text-[var(--muted)]">{j.entryDate?.split('T')[0]}</td>
                        <td className="p-3 num font-bold text-[var(--ink)]">
                          {formatSar((j.totalDebit ?? 0))}
                        </td>
                        <td className="p-3 font-mono text-xs text-[var(--muted)]">{(j as any).postedBy || j.createdBy || 'النظام'}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--money-pos)]/10 text-[var(--money-pos)]">
                            مرحل
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* DIALOG: تأكيد صرف السلفة (أمين الخزينة)                     */}
      {/* ======================================================== */}
      <Dialog
        open={payoutModalOpen}
        onClose={() => setPayoutModalOpen(false)}
        title="تأكيد صرف السلفة المعتمدة"
      >
        {selectedPayoutReq && (
          <form onSubmit={handleExecutePayout} className="space-y-4">
            {payoutServerError && (
              <div className="p-3 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)] text-xs flex items-center gap-2">
                <AlertTriangle size={16} className="shrink-0" />
                <span>{payoutServerError}</span>
              </div>
            )}

            <div className="p-3 rounded-xl bg-[var(--raised)] space-y-1.5 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المندوب:</span>
                <span className="font-bold">{selectedPayoutReq.riderName} ({selectedPayoutReq.riderIqama})</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المبلغ المطلوب صرفه:</span>
                <span className="font-bold num text-sm text-[var(--money-pos)]">
                  {formatSar(selectedPayoutReq.amount)}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">نوع الصرف والخزينة:</span>
                <span className="font-mono text-[var(--accent)]">{selectedPayoutReq.vaultCode}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المعتمد محاسبياً:</span>
                <span>{selectedPayoutReq.approvedBy}</span>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">
                المرجع البنكي أو رقم سند القبض/الصرف (إلزامي):
              </label>
              <input
                type="text"
                id="input-payout-reference"
                value={payoutReference}
                onChange={(e) => setPayoutReference(e.target.value)}
                placeholder="مثال: TRF-98231 أو CSH-REC-01"
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono font-bold focus:outline-hidden"
              />
              <span className="text-[10px] text-[var(--muted)]">
                سيتم توليد قيد اليومية (مدين 1210 سلف / دائن الخزينة أو البنك) وتثبيت المديونية بهذا المرجع.
              </span>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">ملاحظات أمين الصرف (اختياري):</label>
              <input
                type="text"
                id="input-payout-note"
                value={payoutNote}
                onChange={(e) => setPayoutNote(e.target.value)}
                placeholder="أي ملاحظة إضافية للتدقيق..."
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setPayoutModalOpen(false)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer"
              >
                إلغاء
              </button>
              <button
                type="submit"
                id="btn-confirm-payout"
                disabled={submittingPayout}
                className="px-5 py-2 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
              >
                {submittingPayout ? 'جاري الصرف والترحيل...' : 'تأكيد الصرف وترحيل القيد'}
              </button>
            </div>
          </form>
        )}
      </Dialog>

      {/* Details Drawer for Payout Request */}
      <Drawer
        open={Boolean(payoutDetailsReq)}
        onClose={() => setPayoutDetailsReq(null)}
        title={payoutDetailsReq ? `تفاصيل سلفة: ${payoutDetailsReq.riderName}` : 'تفاصيل السلفة'}
        subtitle={payoutDetailsReq ? `المبلغ: ${formatSar(payoutDetailsReq.amount)}` : ''}
      >
        {payoutDetailsReq && (
          <div className="space-y-4">
            <div className="p-4 rounded-xl bg-[var(--raised)] space-y-2 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">رقم الإقامة:</span>
                <span className="font-mono">{payoutDetailsReq.riderIqama}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">مقدم الطلب:</span>
                <span>{payoutDetailsReq.requestedBy}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المعتمد:</span>
                <span>{payoutDetailsReq.approvedBy}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">سبب السلفة:</span>
                <span>{payoutDetailsReq.reason}</span>
              </div>
              {payoutDetailsReq.coverageReason && (
                <div className="flex justify-between">
                  <span className="text-[var(--muted)]">سبب الاعتماد:</span>
                  <span>{payoutDetailsReq.coverageReason}</span>
                </div>
              )}
            </div>

            <div className="space-y-2">
              <h4 className="font-bold text-xs text-[var(--ink)] flex items-center gap-1.5">
                <History size={14} />
                <span>سجل القرارات:</span>
              </h4>
              <div className="space-y-2">
                {payoutDetailsReq.decisions?.map((d, i) => (
                  <div key={i} className="p-2.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-xs space-y-1">
                    <div className="flex justify-between">
                      <span className="font-bold">{d.actor}</span>
                      <span className="num text-[10px] text-[var(--muted)]">{d.at?.split('T')[0]}</span>
                    </div>
                    <div className="font-semibold text-[var(--accent)]">{d.action}</div>
                    {d.note && <div className="text-[11px] text-[var(--muted)]">{d.note}</div>}
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </Drawer>

      {/* Account Movements Drawer */}
      <Drawer
        open={drawerOpen}
        onClose={() => {
          setDrawerOpen(false);
          setSelectedAccount(null);
        }}
        title={`حركات الحساب: ${selectedAccount?.nameAr || ''}`}
        subtitle={`رقم الحساب: ${selectedAccount?.code || ''}`}
      >
        {selectedAccount && (
          <div className="space-y-4">
            <div className="p-4 rounded-xl bg-[var(--raised)] space-y-2 text-xs">
              <div className="flex justify-between gap-3">
                <span className="text-[var(--muted)] shrink-0">نوع الحساب:</span>
                <span className="font-bold text-end">
                  {vaultKindLabelAr(selectedAccount.vaultKind || selectedAccount.accountType)}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">طبيعة الحساب:</span>
                <span className="font-bold">{selectedAccount.naturalSideAr}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">الرصيد الصافي الحالي:</span>
                <span className="num font-bold text-sm text-[var(--accent)]">
                  {formatSar(selectedAccount.netBalance)}
                </span>
              </div>
            </div>

            <div className="space-y-2">
              <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                القيود والحركات المرتبطة بالحساب ({selectedAccountJournals.length}):
              </h4>

              {selectedAccountJournals.length === 0 ? (
                <p className="text-xs text-[var(--muted)] py-4 text-center">لا توجد حركات مسجلة لهذا الحساب.</p>
              ) : (
                <div className="space-y-2">
                  {selectedAccountJournals.map((j) => (
                    <div key={j.id} className="p-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs space-y-1">
                      <div className="flex justify-between items-center">
                        <span className="font-bold text-[var(--ink)]">{j.description}</span>
                        <span className="num text-[11px] text-[var(--muted)]">{j.entryDate?.split('T')[0]}</span>
                      </div>
                      <div className="text-[11px] text-[var(--muted)] font-mono">رقم القيد: #{j.id.substring(0, 10)}</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
      </Drawer>
    </div>
  );
}

export default function TreasuryPage() {
  return (
    <Suspense fallback={<div className="p-8 text-center text-xs text-[var(--muted)]">جاري تحميل صفحة الخزينة...</div>}>
      <TreasuryPageContent />
    </Suspense>
  );
}
