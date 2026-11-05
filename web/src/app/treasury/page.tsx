'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
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
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import type {
  FinanceAccount,
  FinanceJournal,
  BankPayrollFilePreview,
  AdvanceRequest,
} from '../../lib/types';

type TabId = 'vaults' | 'advance_payouts' | 'bank_file' | 'movements';

function TreasuryPageContent() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();
  const router = useRouter();

  const tabFromUrl = searchParams?.get('tab');
  const resolvedTab: TabId = useMemo(() => {
    if (tabFromUrl === 'advance_payouts') return 'advance_payouts';
    if (tabFromUrl === 'bank_file') return 'bank_file';
    if (tabFromUrl === 'movements') return 'movements';
    return 'vaults';
  }, [tabFromUrl]);

  const [activeTab, setActiveTab] = useState<TabId>(resolvedTab);

  useEffect(() => {
    if (resolvedTab && resolvedTab !== activeTab) {
      setActiveTab(resolvedTab);
    }
  }, [resolvedTab]);

  const handleTabChange = (newTab: TabId) => {
    setActiveTab(newTab);
    router.replace(`/treasury/?tab=${newTab}`, { scroll: false });
  };

  const [selectedPeriod, setSelectedPeriod] = useState<string>('2026-10');
  const [selectedSponsor, setSelectedSponsor] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [vaultTypeFilter, setVaultTypeFilter] = useState<string>('all');

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<FinanceAccount[]>([]);
  const [journals, setJournals] = useState<FinanceJournal[]>([]);
  const [bankFilePreview, setBankFilePreview] = useState<BankPayrollFilePreview | null>(null);
  const [loadingBankFile, setLoadingBankFile] = useState<boolean>(false);
  const [bankFileError, setBankFileError] = useState<string | null>(null);
  const [exportingBankFile, setExportingBankFile] = useState<boolean>(false);

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

  const loadBankFilePreview = async (period: string, sponsorId?: string) => {
    setLoadingBankFile(true);
    setBankFileError(null);
    try {
      const sp = sponsorId === 'all' ? undefined : sponsorId;
      const preview = await api.previewBankFile({ period, sponsorId: sp });
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
  }, [activeTab, selectedPeriod, selectedSponsor]);

  const handleExportBankCsv = async () => {
    setExportingBankFile(true);
    try {
      const sp = selectedSponsor === 'all' ? undefined : selectedSponsor;
      const blob = await api.exportBankFile({ period: selectedPeriod, sponsorId: sp });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `bank-payroll-${selectedPeriod}.csv`;
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
      toast(`تم صرف السلفة بنجاح (${selectedPayoutReq.amount} ر.س) وإنشاء القيد المحاسبي والمديونية`, 'success');
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
      const isVault = a.code?.startsWith('111') || a.code?.startsWith('112') || a.code?.startsWith('113') || a.code?.startsWith('114');
      if (!isVault) return false;
      if (vaultTypeFilter !== 'all') {
        if (vaultTypeFilter === 'bank' && !a.code?.startsWith('111')) return false;
        if (vaultTypeFilter === 'cash' && a.code?.startsWith('111')) return false;
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
    () => accounts.filter((a) => a.code?.startsWith('111')).reduce((s, a) => s + (a.netBalance || 0), 0),
    [accounts]
  );
  const totalCashBalance = useMemo(
    () =>
      accounts
        .filter((a) => a.code?.startsWith('112') || a.code?.startsWith('113') || a.code?.startsWith('114'))
        .reduce((s, a) => s + (a.netBalance || 0), 0),
    [accounts]
  );

  // Selected account movements
  const selectedAccountJournals = useMemo(() => {
    if (!selectedAccount) return [];
    return journals.filter((j) =>
      j.lines?.some((l) => l.accountCode === selectedAccount.code)
    );
  }, [selectedAccount, journals]);

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="البنوك والصناديق والخزن"
        description="إدارة ومتابعة أرصدة الحسابات البنكية والخزن النقدية (1110-1140)، طابور صرف السلف المعتمدة لأمناء الصناديق، وإصدار ملف صرف الرواتب البنكي (CSV)."
        eyebrow="الخزينة والمالية"
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
          value={`${totalBankBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint="الأرصدة الصافية الفعلية في الحسابات البنكية"
          icon={Landmark}
        />
        <StatCard
          label="إجمالي الصناديق والخزن"
          value={`${totalCashBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
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
          value={bankFilePreview ? `${bankFilePreview.totalAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : selectedPeriod}
          hint={bankFilePreview ? `${bankFilePreview.validRecordsCount} سجلات جاهزة للصرف` : 'اختر تبويب ملف البنك للمعاينة'}
          icon={FileSpreadsheet}
        />
      </div>

      {/* Sub Tabs */}
      <div className="border-b border-[var(--line)]">
        <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
          {[
            { id: 'vaults' as TabId, label: 'الحسابات البنكية والصناديق', count: vaultAccounts.length },
            {
              id: 'advance_payouts' as TabId,
              label: 'صرف السلف المعتمدة (الأمناء)',
              count: advancePayouts.length,
              alert: advancePayouts.length > 0,
            },
            { id: 'bank_file' as TabId, label: 'ملف صرف البنك (CSV)', count: bankFilePreview?.validRecordsCount },
            { id: 'movements' as TabId, label: 'سجل حركات الخزينة والقيود', count: journals.length },
          ].map((tab) => (
            <button
              key={tab.id}
              id={`tab-${tab.id}`}
              type="button"
              onClick={() => handleTabChange(tab.id)}
              className={`inline-flex items-center gap-2 px-4 py-3 font-bold text-xs sm:text-sm border-b-2 transition whitespace-nowrap cursor-pointer ${
                activeTab === tab.id
                  ? 'border-[var(--accent)] text-[var(--accent)]'
                  : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span>{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span
                  className={`num px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                    tab.alert ? 'bg-[var(--danger)] text-white' : 'bg-[var(--raised)] text-[var(--ink)]'
                  }`}
                >
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* ======================================================== */}
      {/* Sub-tab 1: الحسابات البنكية والصناديق                      */}
      {/* ======================================================== */}
      {activeTab === 'vaults' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="بحث برقم الحساب أو اسم البنك/الخزينة..."
                className="w-full bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2">
              <select
                value={vaultTypeFilter}
                onChange={(e) => setVaultTypeFilter(e.target.value)}
                className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كافة أنواع الخزن</option>
                <option value="bank">حسابات بنكية (1110)</option>
                <option value="cash">صناديق نقدية وعهد (1120-1140)</option>
              </select>
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
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رمز الحساب</th>
                      <th className="p-3 text-start">اسم البنك / الخزينة</th>
                      <th className="p-3 text-start">النوع والوظيفة</th>
                      <th className="p-3 text-start">الجهة الكفيلة</th>
                      <th className="p-3 text-start">الرصيد الصافي الفعلي</th>
                      <th className="p-3 text-start">طبيعة الحساب</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {vaultAccounts.map((a) => (
                      <tr key={a.code} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--accent)]">{a.code}</td>
                        <td className="p-3 font-semibold">{a.nameAr}</td>
                        <td className="p-3 text-[var(--muted)]">
                          <span className="px-2 py-0.5 rounded-full text-xs bg-[var(--raised)] text-[var(--ink)]">
                            {a.code?.startsWith('111') ? 'حساب بنكي رئيسي' : 'صندوق نقدية تشغيلي'}
                          </span>
                        </td>
                        <td className="p-3 text-[var(--muted)]">{(a as any).sponsorName || 'عام'}</td>
                        <td className="p-3 num font-bold text-sm">
                          <span className={a.netBalance < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'}>
                            {a.netBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">معرف السلفة</th>
                      <th className="p-3 text-start">المندوب والإقامة</th>
                      <th className="p-3 text-start">النوع والخزينة المكلفة</th>
                      <th className="p-3 text-start">المبلغ المعتمد</th>
                      <th className="p-3 text-start">القسط الشهري</th>
                      <th className="p-3 text-start">المعتمد وتاريخه</th>
                      <th className="p-3 text-start">سبب الاعتماد</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {advancePayouts.map((req) => (
                      <tr key={req.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-[11px] text-[var(--muted)]">{req.id}</td>
                        <td className="p-3">
                          <div className="font-bold text-[var(--ink)]">{req.riderName}</div>
                          <div className="font-mono text-[11px] text-[var(--accent)]">{req.riderIqama}</div>
                        </td>
                        <td className="p-3">
                          <div className="inline-flex items-center gap-1 font-semibold">
                            {req.type === 'cash' ? <Wallet size={13} /> : <Banknote size={13} />}
                            <span>{req.type === 'cash' ? 'خزينة نقدية' : 'حساب بنكي'}</span>
                          </div>
                          <div className="font-mono text-[10px] text-[var(--muted)]">{req.vaultCode}</div>
                        </td>
                        <td className="p-3 num font-bold text-sm text-[var(--money-pos)]">
                          {req.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-3 num font-medium text-xs text-[var(--ink)]">
                          {req.monthlyInstallment ? `${req.monthlyInstallment} ر.س` : '—'}
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
                <select
                  value={selectedSponsor}
                  onChange={(e) => setSelectedSponsor(e.target.value)}
                  className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold focus:outline-hidden"
                >
                  <option value="all">كافة الكفلاء والشركات</option>
                  <option value="BW">بوابات الوطن (BW)</option>
                  <option value="TR">طريق الرواد (TR)</option>
                  <option value="WZ">وصول الرواد (WZ)</option>
                </select>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={exportingBankFile || !bankFilePreview || !bankFilePreview.canExport}
                onClick={handleExportBankCsv}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 disabled:opacity-50 transition cursor-pointer"
              >
                <Download size={14} />
                <span>{exportingBankFile ? 'جاري التصدير...' : 'تحميل ملف البنك (CSV)'}</span>
              </button>
            </div>
          </div>

          {bankFileError ? (
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
                    {bankFilePreview.totalAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs sm:text-sm">
                    <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                      <tr>
                        <th className="p-3 text-start">رقم الإقامة</th>
                        <th className="p-3 text-start">اسم المندوب</th>
                        <th className="p-3 text-start">الآيبان البنكي (IBAN)</th>
                        <th className="p-3 text-start">البنك</th>
                        <th className="p-3 text-start">مبلغ الصرف المستحق</th>
                        <th className="p-3 text-start">الحالة</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {bankFilePreview.records.map((r, idx) => (
                        <tr key={idx} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono">{r.riderIqama}</td>
                          <td className="p-3 font-semibold">{r.riderName}</td>
                          <td className="p-3 font-mono text-xs">{r.iban}</td>
                          <td className="p-3">{r.bankName}</td>
                          <td className="p-3 num font-bold text-[var(--accent)]">
                            {r.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم القيد</th>
                      <th className="p-3 text-start">البيان والوصف</th>
                      <th className="p-3 text-start">تاريخ الحركة</th>
                      <th className="p-3 text-start">المبلغ الإجمالي</th>
                      <th className="p-3 text-start">المسؤول</th>
                      <th className="p-3 text-start">الحالة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {journals.map((j) => (
                      <tr key={j.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs text-[var(--accent)]">#{j.id.substring(0, 8)}</td>
                        <td className="p-3 font-semibold">{j.description}</td>
                        <td className="p-3 num text-xs text-[var(--muted)]">{j.entryDate?.split('T')[0]}</td>
                        <td className="p-3 num font-bold text-[var(--ink)]">
                          {(j.totalDebit ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
                  {selectedPayoutReq.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
        subtitle={payoutDetailsReq ? `المبلغ: ${payoutDetailsReq.amount} ر.س` : ''}
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
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">نوع الحساب:</span>
                <span className="font-bold">{selectedAccount.vaultKind || selectedAccount.accountType}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">طبيعة الحساب:</span>
                <span className="font-bold">{selectedAccount.naturalSideAr}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">الرصيد الصافي الحالي:</span>
                <span className="num font-bold text-sm text-[var(--accent)]">
                  {selectedAccount.netBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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
