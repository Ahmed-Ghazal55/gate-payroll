'use client';

import React, { useState, useEffect, useMemo } from 'react';
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
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Drawer } from '../../components/ui/Drawer';
import { Skeleton, EmptyState } from '../../components/ui/EmptyState';
import type {
  FinanceAccount,
  FinanceJournal,
  BankPayrollFilePreview,
} from '../../lib/types';

export default function TreasuryPage() {
  const { user } = useAuth();
  const { toast } = useToast();

  const [activeTab, setActiveTab] = useState<'vaults' | 'bank_file' | 'movements'>('vaults');
  const [selectedPeriod, setSelectedPeriod] = useState<string>('2026-10');
  const [selectedSponsor, setSelectedSponsor] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [vaultTypeFilter, setVaultTypeFilter] = useState<string>('all');

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
  const [accounts, setAccounts] = useState<FinanceAccount[]>([]);
  const [journals, setJournals] = useState<FinanceJournal[]>([]);
  const [bankFilePreview, setBankFilePreview] = useState<BankPayrollFilePreview | null>(null);
  const [loadingBankFile, setLoadingBankFile] = useState<boolean>(false);
  const [exportingBankFile, setExportingBankFile] = useState<boolean>(false);

  // Drawer state for account details
  const [selectedAccount, setSelectedAccount] = useState<FinanceAccount | null>(null);
  const [drawerOpen, setDrawerOpen] = useState<boolean>(false);

  const loadData = async () => {
    setLoading(true);
    try {
      const sp = selectedSponsor === 'all' ? undefined : selectedSponsor;
      const [accRes, jrnRes] = await Promise.all([
        api.getFinanceAccounts(sp).catch(() => []),
        api.getFinanceJournals({ sponsorId: sp }).catch(() => []),
      ]);
      setAccounts(accRes || []);
      setJournals(jrnRes || []);
    } catch (err: any) {
      toast(err.message || 'تعذر تحميل بيانات الخزينة', 'danger');
    } finally {
      setLoading(false);
    }
  };

  const loadBankFile = async () => {
    setLoadingBankFile(true);
    try {
      const sp = selectedSponsor === 'all' ? undefined : selectedSponsor;
      const res = await api.previewBankFile({ period: selectedPeriod, sponsorId: sp });
      setBankFilePreview(res);
    } catch (err: any) {
      setBankFilePreview(null);
    } finally {
      setLoadingBankFile(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [selectedSponsor]);

  useEffect(() => {
    if (activeTab === 'bank_file') {
      loadBankFile();
    }
  }, [activeTab, selectedPeriod, selectedSponsor]);

  const handleExportBankFile = async () => {
    setExportingBankFile(true);
    try {
      const sp = selectedSponsor === 'all' ? undefined : selectedSponsor;
      const blob = await api.exportBankFile({ period: selectedPeriod, sponsorId: sp });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `bank-payroll-${selectedPeriod}.csv`;
      a.click();
      URL.revokeObjectURL(url);
      toast('تم تصدير ملف صرف البنك بنجاح بصيغة CSV', 'success');
    } catch (err: any) {
      toast(err.message || 'تعذر تصدير ملف البنك', 'danger');
    } finally {
      setExportingBankFile(false);
    }
  };

  // Filter accounts for vaults and banks
  const vaultAccounts = useMemo(() => {
    return accounts.filter((a) => {
      // Must be a vault account or cash/bank asset
      const isVaultOrBank = a.isVault || a.code.startsWith('111') || a.code.startsWith('112') || a.code.startsWith('114');
      if (!isVaultOrBank) return false;

      if (vaultTypeFilter !== 'all') {
        if (vaultTypeFilter === 'Bank' && a.vaultKind !== 'Bank') return false;
        if (vaultTypeFilter === 'CashBox' && a.vaultKind !== 'CashBox') return false;
        if (vaultTypeFilter === 'MainInternal' && a.vaultKind !== 'MainInternal') return false;
      }

      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchCode = a.code.toLowerCase().includes(q);
        const matchName = a.nameAr.toLowerCase().includes(q);
        if (!matchCode && !matchName) return false;
      }

      return true;
    });
  }, [accounts, vaultTypeFilter, searchQuery]);

  // Total balances
  const totalBankBalance = useMemo(() => {
    return accounts
      .filter((a) => a.vaultKind === 'Bank' || (a.isVault && a.nameAr.includes('بنك')))
      .reduce((sum, a) => sum + (a.netBalance || 0), 0);
  }, [accounts]);

  const totalCashBalance = useMemo(() => {
    return accounts
      .filter((a) => a.vaultKind === 'CashBox' || a.vaultKind === 'MainInternal' || (a.isVault && !a.nameAr.includes('بنك')))
      .reduce((sum, a) => sum + (a.netBalance || 0), 0);
  }, [accounts]);

  // Account lines for selected account drawer
  const selectedAccountJournals = useMemo(() => {
    if (!selectedAccount) return [];
    return journals.filter((j) =>
      j.lines?.some((l) => l.accountCode === selectedAccount.code || l.accountCode.startsWith(selectedAccount.code))
    );
  }, [journals, selectedAccount]);

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="البنوك والصناديق والخزن"
        description="إدارة ومتابعة أرصدة الحسابات البنكية والخزن النقدية (1110-1140)، إصدار ملف صرف الرواتب البنكي المعتمد (CSV)، ومطابقة حركات القيود اليومية."
        eyebrow="الخزينة والمالية"
        primaryAction={{
          label: 'تحديث الأرصدة',
          icon: <RefreshCw size={15} />,
          onClick: loadData,
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
          label="الحسابات النشطة"
          value={vaultAccounts.length}
          hint="حسابات بنكية وخزن مخصصة للصرف والقبض"
          icon={Building2}
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
            { id: 'vaults', label: 'الحسابات البنكية والصناديق', count: vaultAccounts.length },
            { id: 'bank_file', label: 'ملف صرف البنك (CSV)', count: bankFilePreview?.validRecordsCount },
            { id: 'movements', label: 'سجل حركات الخزينة والقيود', count: journals.length },
          ].map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id as any)}
              className={`inline-flex items-center gap-2 px-4 py-3 font-bold text-xs sm:text-sm border-b-2 transition whitespace-nowrap ${
                activeTab === tab.id
                  ? 'border-[var(--accent)] text-[var(--accent)]'
                  : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span>{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span className="num px-1.5 py-0.2 rounded-full text-[10px] font-bold bg-[var(--raised)] text-[var(--ink)]">
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* Sub-tab 1: الحسابات البنكية والصناديق */}
      {activeTab === 'vaults' && (
        <div className="space-y-4">
          {/* Toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="بحث برقم الحساب أو الاسم..."
                className="w-full bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2">
              <span className="text-xs text-[var(--muted)] font-medium">النوع:</span>
              <select
                value={vaultTypeFilter}
                onChange={(e) => setVaultTypeFilter(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-lg px-2.5 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل الأنواع</option>
                <option value="Bank">حسابات بنكية (Bank)</option>
                <option value="CashBox">صناديق نقدية (CashBox)</option>
                <option value="MainInternal">خزن رئيسية داخلية</option>
              </select>
            </div>
          </div>

          {loading ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : vaultAccounts.length === 0 ? (
            <EmptyState
              title="لا توجد حسابات خزينة مطابقة"
              description="لم يتم العثور على أي حسابات بنكية أو خزن تطابق خيارات البحث الحالية."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم الحساب</th>
                      <th className="p-3 text-start">اسم الحساب</th>
                      <th className="p-3 text-start">نوع الصندوق</th>
                      <th className="p-3 text-start">اتجاه الحركات</th>
                      <th className="p-3 text-start">إجمالي المدين</th>
                      <th className="p-3 text-start">إجمالي الدائن</th>
                      <th className="p-3 text-start">الرصيد الصافي</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {vaultAccounts.map((acc) => {
                      const isBank = acc.vaultKind === 'Bank' || acc.nameAr.includes('بنك');

                      return (
                        <tr key={acc.code} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono font-bold text-[var(--accent)]">{acc.code}</td>
                          <td className="p-3 font-semibold text-[var(--ink)]">
                            <div className="flex items-center gap-2">
                              {isBank ? (
                                <Landmark size={15} className="text-[var(--accent)] shrink-0" />
                              ) : (
                                <Coins size={15} className="text-[var(--warning)] shrink-0" />
                              )}
                              <span>{acc.nameAr}</span>
                            </div>
                          </td>
                          <td className="p-3">
                            <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--muted)]">
                              {acc.vaultKind || (acc.isVault ? 'خزينة' : 'أصل')}
                            </span>
                          </td>
                          <td className="p-3 text-[11px] text-[var(--muted)]">
                            {acc.allowedDirectionsAr || 'قبض وصرف'}
                          </td>
                          <td className="p-3 num text-[var(--muted)]">
                            {acc.debit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </td>
                          <td className="p-3 num text-[var(--muted)]">
                            {acc.credit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </td>
                          <td className="p-3 num font-bold text-sm">
                            <span className={acc.netBalance >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--money-neg)]'}>
                              {acc.netBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                            </span>
                          </td>
                          <td className="p-3">
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedAccount(acc);
                                setDrawerOpen(true);
                              }}
                              className="px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold transition"
                            >
                              عرض الحركات
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 2: ملف صرف البنك (CSV) */}
      {activeTab === 'bank_file' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div>
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">معاينة ملف صرف الرواتب البنكي المعتمد</h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                توليد ملف التحويل البنكي المتوافق مع متطلبات الرواتب وبنك الرياض والراجحي، مع التحقق من صحة الآيبان واستبعاد الأرصدة الصفرية.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-xs font-bold text-[var(--muted)]">فترة الصرف:</span>
              <select
                value={selectedPeriod}
                onChange={(e) => setSelectedPeriod(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs font-bold rounded-lg px-2.5 py-1.5 focus:outline-hidden"
              >
                <option value="2026-10">2026-10</option>
                <option value="2026-09">2026-09</option>
                <option value="2026-08">2026-08</option>
              </select>

              <button
                type="button"
                disabled={exportingBankFile || !bankFilePreview?.canExport}
                onClick={handleExportBankFile}
                className="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 disabled:opacity-50 transition"
              >
                <Download size={14} />
                <span>{exportingBankFile ? 'جاري التصدير...' : 'تصدير ملف البنك (CSV)'}</span>
              </button>
            </div>
          </div>

          {loadingBankFile ? (
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
              {/* Summary cards */}
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

              {/* Records table */}
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

      {/* Sub-tab 3: سجل حركات الخزينة والقيود */}
      {activeTab === 'movements' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">سجل القيود المزدوجة وحركات الخزينة</h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              عرض القيود المحاسبية مع ربط الحركات العكسية بأصولها والتأكد من التوازن المحاسبي.
            </p>
          </div>

          {journals.length === 0 ? (
            <EmptyState
              title="لا توجد قيود مسجلة"
              description="تظهر القيود اليومية بعد ترحيل المسيرات أو تسجيل التحويلات والإيداعات."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم القيد</th>
                      <th className="p-3 text-start">التاريخ</th>
                      <th className="p-3 text-start">البيان والشرح</th>
                      <th className="p-3 text-start">الحسابات والبنود</th>
                      <th className="p-3 text-start">إجمالي القيد</th>
                      <th className="p-3 text-start">النوع / العكس</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {journals.map((j) => {
                      const totalDebit = j.lines?.reduce((sum, l) => sum + (l.debit || 0), 0) || 0;

                      return (
                        <tr key={j.id} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono font-bold text-xs">{j.id.substring(0, 12)}</td>
                          <td className="p-3 num text-xs">{j.entryDate ? j.entryDate.split('T')[0] : '-'}</td>
                          <td className="p-3 font-medium max-w-xs truncate">{j.description}</td>
                          <td className="p-3 text-xs">
                            <div className="space-y-0.5">
                              {j.lines?.slice(0, 2).map((l, idx) => (
                                <div key={idx} className="flex items-center gap-2 text-[11px]">
                                  <span className="font-mono text-[var(--accent)]">{l.accountCode}</span>
                                  <span className="num font-bold">
                                    {l.debit > 0 ? `مدين: ${l.debit}` : `دائن: ${l.credit}`}
                                  </span>
                                </div>
                              ))}
                              {j.lines && j.lines.length > 2 && (
                                <span className="text-[10px] text-[var(--muted)]">+{j.lines.length - 2} بنود أخرى</span>
                              )}
                            </div>
                          </td>
                          <td className="p-3 num font-bold text-[var(--ink)]">
                            {totalDebit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </td>
                          <td className="p-3">
                            {j.reversalOf ? (
                              <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
                                قيد عكسي
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--muted)]">
                                قيد أصلي
                              </span>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

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
