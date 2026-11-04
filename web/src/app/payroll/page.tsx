'use client';

import React, { useState, useEffect, useMemo, useRef, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  Calculator,
  Upload,
  FileSpreadsheet,
  AlertTriangle,
  CheckCircle2,
  AlertCircle,
  Clock,
  ShieldAlert,
  ArrowRightLeft,
  BookOpen,
  PieChart,
  Search,
  Filter,
  Download,
  Check,
  X,
  Eye,
  FileText,
  Building2,
  Users,
  Coins,
  DollarSign,
  Tag,
  Layers,
  ChevronRight,
  ExternalLink,
  ShieldCheck,
  Sparkles,
  Copy,
  Landmark,
  Scale,
  Calendar,
  RefreshCw,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import type {
  HsInvoice,
  HsDispute,
  HsAllocation,
  HsFact,
  HsProposedJournal,
  HsImportFile,
  HsImportPreviewResult,
} from '../../lib/types';

function PayrollPageInner() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const showToast = (msg: string, type: 'success' | 'error' | 'info' | 'warning' = 'info') => {
    toast(msg, type === 'error' ? 'danger' : type);
  };

  // Main shared tabs: platforms · cycle · rules · deductions · bank · policies
  const tabFromUrl = searchParams?.get('tab') || 'platforms';
  const [mainTab, setMainTab] = useState<string>(tabFromUrl);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== mainTab) {
      setMainTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  // Selected App within platforms tab
  const [selectedPlatform, setSelectedPlatform] = useState<string>('hungerstation');

  // Sub-tabs inside selected app
  const [appSubTab, setAppSubTab] = useState<'settlements' | 'parent_terms' | 'internal_schemes' | 'eligibility' | 'accounts'>('settlements');

  // Navigation tabs within HungerStation settlements
  const [activeTab, setActiveTab] = useState<string>('invoices');

  // Filters & period
  const [selectedPeriod, setSelectedPeriod] = useState<string>('2026-10');
  const [selectedCompany, setSelectedCompany] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Data states for HungerStation
  const [loading, setLoading] = useState<boolean>(true);
  const [invoices, setInvoices] = useState<HsInvoice[]>([]);
  const [disputes, setDisputes] = useState<HsDispute[]>([]);
  const [allocations, setAllocations] = useState<HsAllocation[]>([]);
  const [facts, setFacts] = useState<HsFact[]>([]);
  const [proposedJournals, setProposedJournals] = useState<HsProposedJournal[]>([]);

  // Sub-tab 4 (ERP Facts) states
  const [factsViewMode, setFactsViewMode] = useState<'accounts' | 'riders'>('accounts');
  const [selectedFactKind, setSelectedFactKind] = useState<string>('hs_wallet_event');

  // Sub-tab 6 (Proposed Journals) selected invoice
  const [selectedInvoiceForJournal, setSelectedInvoiceForJournal] = useState<HsInvoice | null>(null);
  const [currentProposal, setCurrentProposal] = useState<HsProposedJournal | null>(null);
  const [loadingProposal, setLoadingProposal] = useState<boolean>(false);

  // Import Dialog states
  const [importDialogOpen, setImportDialogOpen] = useState<boolean>(false);
  const [uploadFiles, setUploadFiles] = useState<HsImportFile[]>([]);
  const [previewResult, setPreviewResult] = useState<HsImportPreviewResult | null>(null);
  const [previewLoading, setPreviewLoading] = useState<boolean>(false);
  const [commitLoading, setCommitLoading] = useState<boolean>(false);

  // Dispute Resolve Modal states
  const [selectedDispute, setSelectedDispute] = useState<HsDispute | null>(null);
  const [resolveOutcome, setResolveOutcome] = useState<'platform_credit' | 'platform_rejected'>('platform_credit');
  const [platformReference, setPlatformReference] = useState<string>('');
  const [resolveReason, setResolveReason] = useState<string>('');
  const [resolvingDispute, setResolvingDispute] = useState<boolean>(false);

  // TGA Allocation Split Modal states
  const [selectedAllocation, setSelectedAllocation] = useState<HsAllocation | null>(null);
  const [splitMode, setSplitMode] = useState<'amount' | 'percent'>('amount');
  const [splitCompanyAmount, setSplitCompanyAmount] = useState<string>('');
  const [splitRiderAmount, setSplitRiderAmount] = useState<string>('');
  const [splitRiderPercent, setSplitRiderPercent] = useState<string>('50');
  const [splitRiderId, setSplitRiderId] = useState<string>('');
  const [savingSplit, setSavingSplit] = useState<boolean>(false);

  // Legacy Policies state
  const [policies, setPolicies] = useState<any[]>([]);
  const [loadingPolicies, setLoadingPolicies] = useState<boolean>(false);

  // Rules Editor state (W9)
  const [rulesMonth, setRulesMonth] = useState<string>('2026-10');
  const [rulesStatus, setRulesStatus] = useState<'draft' | 'approved'>('draft');
  const [rulesApprovedBy, setRulesApprovedBy] = useState<string>('');
  const [categoryMatrix, setCategoryMatrix] = useState({
    spCarRent: 60,
    spBikeRent: 26.67,
    spPrivateRent: 0,
    extCarRent: 60,
    extBikeRent: 26.67,
    extPrivateRent: 0,
    spCarCommission: 2500,
    spBikeCommission: 2000,
    extCarCommission: 2200,
    extBikeCommission: 1800,
  });

  // Cycle Stepper state (W8 / W9)
  const [currentCycleStep, setCurrentCycleStep] = useState<number>(0);

  // Load HungerStation Data
  const loadData = async () => {
    setLoading(true);
    try {
      const comp = selectedCompany === 'all' ? undefined : selectedCompany;
      const [invRes, dispRes, allocRes, factRes, propRes] = await Promise.all([
        api.getHsInvoices({ period: selectedPeriod, companyId: comp }).catch(() => ({ items: [] })),
        api.getHsDisputes({ period: selectedPeriod }).catch(() => ({ items: [] })),
        api.getHsAllocations({ period: selectedPeriod }).catch(() => ({ items: [] })),
        api.getHsFacts({ kind: selectedFactKind }).catch(() => ({ items: [] })),
        api.getHsProposedJournals({ period: selectedPeriod } as any).catch(() => ({ items: [] })),
      ]);

      setInvoices((invRes as any)?.items || (Array.isArray(invRes) ? invRes : []));
      setDisputes((dispRes as any)?.items || (Array.isArray(dispRes) ? dispRes : []));
      setAllocations((allocRes as any)?.items || (Array.isArray(allocRes) ? allocRes : []));
      setFacts((factRes as any)?.items || (Array.isArray(factRes) ? factRes : []));
      setProposedJournals((propRes as any)?.items || (Array.isArray(propRes) ? propRes : []));
    } catch (err: any) {
      showToast(err.message || 'تعذر تحميل بيانات منصة هنقرستيشن', 'error');
    } finally {
      setLoading(false);
    }
  };

  // Load Policies
  const loadPolicies = async () => {
    setLoadingPolicies(true);
    try {
      const res = await api.getPolicies();
      setPolicies(Array.isArray(res) ? res : []);
    } catch (_) {
      setPolicies([]);
    } finally {
      setLoadingPolicies(false);
    }
  };

  useEffect(() => {
    loadData();
    loadPolicies();
  }, [selectedPeriod, selectedCompany, selectedFactKind]);

  // Derived KPI values
  const totalInvoicesAmount = useMemo(() => {
    return invoices.reduce((sum, inv) => sum + (inv.totalAmount || 0), 0);
  }, [invoices]);

  const openDisputesCount = useMemo(() => {
    return disputes.filter((d) => d.status === 'open').length;
  }, [disputes]);

  const pendingAllocationsCount = useMemo(() => {
    return allocations.filter((a) => a.status === 'pending' || a.status === 'pending_allocation').length;
  }, [allocations]);

  const isPostingGateOn = useMemo(() => {
    return invoices.length > 0 && invoices.every((i) => i.status === 'approved' || i.status === 'posted');
  }, [invoices]);

  // Handle Save TGA Split (Pure mode)
  const handleSaveTgaSplit = async () => {
    if (!selectedAllocation) return;

    let compAmt = 0;
    let riderAmt = 0;
    const total = selectedAllocation.amount;

    if (splitMode === 'percent') {
      const p = parseFloat(splitRiderPercent) || 0;
      if (p < 0 || p > 100) {
        showToast('النسبة المئوية يجب أن تكون بين 0 و 100%', 'error');
        return;
      }
      riderAmt = Math.round(((total * p) / 100) * 100) / 100;
      compAmt = Math.round((total - riderAmt) * 100) / 100;
    } else {
      compAmt = parseFloat(splitCompanyAmount) || 0;
      riderAmt = parseFloat(splitRiderAmount) || 0;
      if (Math.abs(compAmt + riderAmt - total) > 0.01) {
        showToast(`مجموع نصيبي الشركة والمناديب (${compAmt + riderAmt}) يجب أن يساوي الإجمالي (${total}) تماماً`, 'error');
        return;
      }
    }

    if (riderAmt > 0 && !splitRiderId.trim()) {
      showToast('يجب إدخال معرّف أو إقامة المندوب لتخصيص نصيب المندوب', 'error');
      return;
    }

    setSavingSplit(true);
    try {
      const payload: { companyAmount?: number; riderAmount?: number; riderId?: string; riderPercent?: number } = {};
      if (splitMode === 'percent') {
        payload.riderPercent = parseFloat(splitRiderPercent);
        if (splitRiderId.trim()) payload.riderId = splitRiderId.trim();
      } else {
        payload.companyAmount = compAmt;
        payload.riderAmount = riderAmt;
        if (splitRiderId.trim()) payload.riderId = splitRiderId.trim();
      }

      await api.splitHsAllocation(selectedAllocation.lineId, payload);
      showToast('تم حفظ مقترح تقسيم TGA بنجاح وفي انتظار اعتماد المدقق المستقل', 'success');
      setSelectedAllocation(null);
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر حفظ توزيع TGA', 'error');
    } finally {
      setSavingSplit(false);
    }
  };

  // Approve TGA Allocation (maker-checker)
  const handleApproveAllocation = async (alloc: HsAllocation) => {
    if (alloc.requestedBy && user && alloc.requestedBy.toLowerCase() === user.username.toLowerCase()) {
      showToast('لا يمكن لمنشئ التوزيع اعتماده بنفسه (قاعدة التدقيق المستقل Maker-Checker)', 'error');
      return;
    }

    try {
      await api.approveHsAllocation(alloc.id);
      showToast('تم اعتماد توزيع TGA بنجاح', 'success');
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد توزيع TGA', 'error');
    }
  };

  // Resolve Dispute
  const handleResolveDispute = async () => {
    if (!selectedDispute) return;

    if (selectedDispute.createdBy && user && selectedDispute.createdBy.toLowerCase() === user.username.toLowerCase()) {
      showToast('لا يمكنك البت في اعتراض أنشأته بنفسك (قاعدة التدقيق المستقل Maker-Checker)', 'error');
      return;
    }

    if (resolveOutcome === 'platform_credit' && !platformReference.trim()) {
      showToast('يرجى إدخال رقم مرجع إشعار الدائن الصادر من المنصة', 'error');
      return;
    }

    if (!resolveReason.trim()) {
      showToast('سبب وبيان المعالجة مطلوب', 'error');
      return;
    }

    setResolvingDispute(true);
    try {
      await api.resolveHsDispute(selectedDispute.invoiceId, {
        mode: 'dispute',
        disputeId: selectedDispute.id,
        outcome: resolveOutcome,
        platformReference: platformReference.trim() || selectedDispute.id,
        reason: resolveReason.trim(),
      });

      showToast(
        resolveOutcome === 'platform_credit'
          ? 'تم تسجيل موافقة المنصة وإشعار الدائن'
          : 'تم تسجيل رفض المنصة وترحيل المطالبة لحساب 112803',
        'success'
      );
      setSelectedDispute(null);
      setPlatformReference('');
      setResolveReason('');
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر فض النزاع', 'error');
    } finally {
      setResolvingDispute(false);
    }
  };

  // Section Tabs (Shared across Payroll)
  const sharedSectionTabs: SectionTabItem[] = [
    { id: 'platforms', label: 'منصات التشغيل والفواتير', icon: <Calculator size={16} /> },
    { id: 'cycle', label: 'دورة الرواتب والمسير (0-7)', icon: <Layers size={16} /> },
    { id: 'rules', label: 'محرر الأنظمة والقواعد', icon: <FileText size={16} /> },
    { id: 'deductions', label: 'الاستقطاعات الشهرية', icon: <DollarSign size={16} /> },
    { id: 'bank', label: 'التحويل والبنوك', icon: <Landmark size={16} /> },
    { id: 'policies', label: 'السياسات السابقة', icon: <BookOpen size={16} />, badge: policies.length },
  ];

  // Platforms List
  const platformsList = [
    { id: 'hungerstation', nameAr: 'هنقرستيشن', nameEn: 'HungerStation', tag: 'فواتير معتمدة', active: true },
    { id: 'keeta', nameAr: 'كيتا', nameEn: 'Keeta', tag: 'شرائح وعمولات', active: true },
    { id: 'ninja', nameAr: 'نينجا', nameEn: 'Ninja', tag: 'تارجت مقطوع', active: true },
    { id: 'thechefz', nameAr: 'ذا شيفز', nameEn: 'The Chefz', tag: 'كشف دوري', active: true },
    { id: 'amazon', nameAr: 'أمازون', nameEn: 'Amazon', tag: 'شحنات لوجستية', active: true },
    { id: 'jahez', nameAr: 'جاهز (دورة مستقلة)', nameEn: 'Jahez', tag: 'تسويات متغيرة', active: true },
  ];

  // Cycle Stepper Steps (BR §24.7)
  const cycleSteps = [
    { num: 0, title: 'فواتير وتسويات المنصات', desc: 'استيراد وقائع فواتير المنصات ومراجعة الخصومات واقتطاعات TGA/CAR' },
    { num: 1, title: 'استحقاق الحسابات والمعرفات', desc: 'تحديد ما حققه كل معرف/حساب على المنصة وربطه بالوقائع' },
    { num: 2, title: 'استحقاق المناديب وتوزيع الفترات', desc: 'فصل وتوزيع الحسابات المشتركة حسب تواريخ الاستلام والتسليم' },
    { num: 3, title: 'تطبيق الأنظمة الداخلية', desc: 'حساب إجمالي الأجر وفق النظام المعتمد (راتب/عمولة/نسبة) لكل مندوب' },
    { num: 4, title: 'الاستقطاعات والتحملات', desc: 'حسم السلف، المخالفات، المحروقات، باقات النت، والخصومات التشغيلية' },
    { num: 5, title: 'الدمج وتحديد الصافي', desc: 'دمج مستحقات المناديب العاملين على أكثر من تطبيق وفحص الصافي السالب (§22.9.2)' },
    { num: 6, title: 'الاعتماد المزدوج المستقل', desc: 'مراجعة المدقق المالي المستقل والتوقيع المعتمد (Maker-Checker)' },
    { num: 7, title: 'ملف الصرف البنكي والترحيل', desc: 'إصدار ملف CSV لصرف الرواتب وترحيل القيود اليومية للدليل المستهدف' },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الرواتب والمسيرات · منظومة المنصات الموحدة"
        eyebrow="الرواتب والتعويضات"
        description="إدارة ومراجعة فواتير المنصات، دورة الرواتب الشهرية الشاملة، محرر الأنظمة الداخلية، الاستقطاعات، واعتماد ملفات الصرف."
        primaryAction={{
          label: 'استيراد فواتير المنصة',
          icon: <Upload size={16} />,
          onClick: () => setImportDialogOpen(true),
        }}
      />

      {/* Main Top Navigation Tabs */}
      <SectionTabs
        tabs={sharedSectionTabs}
        activeTab={mainTab}
        onChange={(tabId) => setMainTab(tabId)}
      />

      {/* ======================================================== */}
      {/* SECTION 1: PLATFORMS & SETTLEMENTS                      */}
      {/* ======================================================== */}
      {mainTab === 'platforms' && (
        <div className="space-y-6">
          {/* App Selector Pills */}
          <div className="flex items-center gap-2 overflow-x-auto no-scrollbar p-1.5 rounded-2xl bg-[var(--raised)] border border-[var(--line)]">
            {platformsList.map((p) => {
              const isSelected = selectedPlatform === p.id;
              return (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => setSelectedPlatform(p.id)}
                  className={`inline-flex items-center gap-2 px-3.5 py-2 rounded-xl text-xs sm:text-sm font-bold transition-all whitespace-nowrap cursor-pointer ${
                    isSelected
                      ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs'
                      : 'text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  <span>{p.nameAr}</span>
                  <span
                    className={`text-[10px] px-1.5 py-0.2 rounded-full font-semibold ${
                      isSelected
                        ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
                        : 'bg-[var(--surface)] text-[var(--muted)]'
                    }`}
                  >
                    {p.tag}
                  </span>
                </button>
              );
            })}
          </div>

          {/* Sub-tabs for the selected App */}
          <div className="flex items-center gap-2 border-b border-[var(--line)] overflow-x-auto no-scrollbar">
            {[
              { id: 'settlements', label: 'الفواتير والتسويات' },
              { id: 'parent_terms', label: 'الشروط الأصلية (Level 2)' },
              { id: 'internal_schemes', label: 'الأنظمة الداخلية (Level 3)' },
              { id: 'eligibility', label: 'الأهلية والضوابط (Level 5)' },
              { id: 'accounts', label: 'الحسابات والمعرفات' },
            ].map((sub) => (
              <button
                key={sub.id}
                type="button"
                onClick={() => setAppSubTab(sub.id as any)}
                className={`py-2 px-3.5 text-xs sm:text-sm font-semibold border-b-2 transition whitespace-nowrap ${
                  appSubTab === sub.id
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                {sub.label}
              </button>
            ))}
          </div>

          {/* Case A: HungerStation Settlements Tab (The full existing HS workspace) */}
          {selectedPlatform === 'hungerstation' && appSubTab === 'settlements' && (
            <div className="space-y-6">
              {/* Stat Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
                <StatCard
                  label="إجمالي الصفحة الحالية"
                  value={`${totalInvoicesAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
                  hint={`${invoices.length} فواتير مسجلة في الصفحة الحالية لشهر ${selectedPeriod}`}
                  icon={Calculator}
                />
                <StatCard
                  label="الاعتراضات المعلقة (CAR)"
                  value={openDisputesCount}
                  hint={openDisputesCount > 0 ? 'تتطلب رد المنصة أو التصعيد لحساب 112803' : 'لا توجد نزاعات معلقة'}
                  icon={ShieldAlert}
                />
                <StatCard
                  label="مبالغ TGA بانتظار التوزيع"
                  value={pendingAllocationsCount}
                  hint="تتطلب تحديد حصة المندوب والشركة بدون افتراض مسبق"
                  icon={ArrowRightLeft}
                />
                <StatCard
                  label="بوابة الترحيل المحاسبي"
                  value={isPostingGateOn ? 'مفعّلة' : 'موقوفة'}
                  hint={isPostingGateOn ? 'جاهز لترحيل القيود المعتمدة' : 'الترحيل موقوف لحين اعتماد ملف المنصة'}
                  icon={ShieldCheck}
                />
              </div>

              {/* Main HS Tabs */}
              <div className="border-b border-[var(--line)]">
                <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
                  {[
                    { id: 'invoices', label: 'الفواتير', count: invoices.length },
                    { id: 'disputes', label: 'الاعتراضات (CAR)', count: openDisputesCount, alert: openDisputesCount > 0 },
                    { id: 'allocations', label: 'توزيع TGA', count: pendingAllocationsCount },
                    { id: 'facts', label: 'وقائع المحفظة من ERP' },
                    { id: 'month_close', label: 'إقفال الشهر (C/M/A)' },
                    { id: 'proposed_journals', label: 'القيود المقترحة' },
                  ].map((tab) => (
                    <button
                      key={tab.id}
                      id={`tab-${tab.id}`}
                      role="tab"
                      aria-selected={activeTab === tab.id}
                      type="button"
                      onClick={() => setActiveTab(tab.id)}
                      className={`inline-flex items-center gap-2 px-4 py-3 font-bold text-xs sm:text-sm border-b-2 transition whitespace-nowrap ${
                        activeTab === tab.id
                          ? 'border-[var(--accent)] text-[var(--accent)]'
                          : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                      }`}
                    >
                      <span>{tab.label}</span>
                      {tab.count !== undefined && tab.count > 0 && (
                        <span
                          className={`num px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                            tab.alert
                              ? 'bg-[var(--danger)] text-white'
                              : 'bg-[var(--raised)] text-[var(--ink)]'
                          }`}
                        >
                          {tab.count}
                        </span>
                      )}
                    </button>
                  ))}
                </div>
              </div>

              {/* Sub-tab 1: Invoices */}
              {activeTab === 'invoices' && (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                    <div className="p-4 border-b border-[var(--line)] flex flex-wrap items-center justify-between gap-3">
                      <div className="flex items-center gap-2">
                        <select
                          value={selectedPeriod}
                          onChange={(e) => setSelectedPeriod(e.target.value)}
                          className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-xl px-3 py-1.5 focus:outline-hidden"
                        >
                          <option value="2026-10">شهر أكتوبر 2026</option>
                          <option value="2026-09">شهر سبتمبر 2026</option>
                        </select>
                      </div>
                    </div>

                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <th className="p-3 text-start">معرف الفاتورة</th>
                            <th className="p-3 text-start">الشهر</th>
                            <th className="p-3 text-start">السجل التجاري</th>
                            <th className="p-3 text-start">إجمالي غير شامل VAT</th>
                            <th className="p-3 text-start">ضريبة القيمة المضافة</th>
                            <th className="p-3 text-start">الإجمالي شامل VAT</th>
                            <th className="p-3 text-start">الحالة</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {invoices.length === 0 ? (
                            <tr>
                              <td colSpan={7} className="p-8 text-center text-xs text-[var(--muted)]">
                                لا توجد فواتير مسجلة لشهر {selectedPeriod}
                              </td>
                            </tr>
                          ) : (
                            invoices.map((inv) => (
                              <tr key={inv.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--accent)]">{inv.id}</td>
                                <td className="p-3 font-mono">{inv.period}</td>
                                <td className="p-3 font-mono">{inv.companyId}</td>
                                <td className="p-3 num font-bold text-[var(--ink)]">
                                  {(inv.totalAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 num font-bold text-[var(--muted)]">
                                  {(inv.vat || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 num font-bold text-[var(--ink)]">
                                  {(inv.totalAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3">
                                  <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                                    {inv.status}
                                  </span>
                                </td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              )}

              {/* Sub-tab 2: Disputes */}
              {activeTab === 'disputes' && (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <th className="p-3 text-start">رقم النزاع</th>
                            <th className="p-3 text-start">النوع</th>
                            <th className="p-3 text-start">المبلغ المتنازع عليه</th>
                            <th className="p-3 text-start">الحالة والمسار المحاسبي</th>
                            <th className="p-3 text-start">الملاحظات</th>
                            <th className="p-3 text-start">الإجراء</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {disputes.length === 0 ? (
                            <tr>
                              <td colSpan={6} className="p-8 text-center text-xs text-[var(--muted)]">
                                لا توجد اعتراضات مسجلة
                              </td>
                            </tr>
                          ) : (
                            disputes.map((disp) => (
                              <tr key={disp.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--accent)]">{disp.id}</td>
                                <td className="p-3 font-semibold">{disp.category || 'CAR'}</td>
                                <td className="p-3 num font-bold text-[var(--danger)]">
                                  {(disp.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3">
                                  {disp.outcome === 'platform_rejected' ? (
                                    <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--danger)]/15 text-[var(--danger)]">
                                      مرحّل لحساب النزاعات 112803 لحين فض النزاع
                                    </span>
                                  ) : (
                                    <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                                      {disp.status}
                                    </span>
                                  )}
                                </td>
                                <td className="p-3 text-xs text-[var(--muted)] max-w-xs truncate">{disp.reason || '-'}</td>
                                <td className="p-3">
                                  {disp.status === 'open' ? (
                                    <button
                                      type="button"
                                      onClick={() => setSelectedDispute(disp)}
                                      className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                                    >
                                      معالجة النزاع
                                    </button>
                                  ) : (
                                    <span className="text-xs text-[var(--muted)]">تم الإجراء</span>
                                  )}
                                </td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              )}

              {/* Sub-tab 3: Allocations */}
              {activeTab === 'allocations' && (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <th className="p-3 text-start">معرف التوزيع</th>
                            <th className="p-3 text-start">المبلغ الإجمالي</th>
                            <th className="p-3 text-start">حصة الشركة</th>
                            <th className="p-3 text-start">حصة المناديب</th>
                            <th className="p-3 text-start">الحالة</th>
                            <th className="p-3 text-start">المنشئ (Maker)</th>
                            <th className="p-3 text-start">الإجراء</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {allocations.length === 0 ? (
                            <tr>
                              <td colSpan={7} className="p-8 text-center text-xs text-[var(--muted)]">
                                لا توجد مبالغ TGA بانتظار التوزيع
                              </td>
                            </tr>
                          ) : (
                            allocations.map((alloc) => {
                              const isMaker =
                                alloc.requestedBy &&
                                user &&
                                alloc.requestedBy.toLowerCase() === user.username.toLowerCase();

                              return (
                                <tr key={alloc.id} className="hover:bg-[var(--row-hover)] transition">
                                  <td className="p-3 font-mono font-bold text-[var(--accent)]">{alloc.id}</td>
                                  <td className="p-3 num font-bold">
                                    {(alloc.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                  </td>
                                  <td className="p-3 num text-[var(--ink)]">
                                    {(alloc.companyAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                  </td>
                                  <td className="p-3 num text-[var(--muted)]">
                                    {(alloc.riderAmount || (alloc.riderShares?.reduce((s, r) => s + r.amount, 0)) || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                  </td>
                                  <td className="p-3">
                                    <span
                                      className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                                        alloc.status === 'approved'
                                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                          : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                      }`}
                                    >
                                      {alloc.status === 'approved' ? 'معتمد' : 'بانتظار الاعتماد'}
                                    </span>
                                  </td>
                                  <td className="p-3 font-mono text-xs">@{alloc.requestedBy || 'system'}</td>
                                  <td className="p-3">
                                    {alloc.status === 'pending' || alloc.status === 'pending_allocation' ? (
                                      <div className="flex items-center gap-1.5">
                                        <button
                                          type="button"
                                          disabled={Boolean(isMaker)}
                                          onClick={() => handleApproveAllocation(alloc)}
                                          title={isMaker ? 'لا يمكن لمنشئ التوزيع اعتماده بنفسه (قاعدة التدقيق المستقل)' : undefined}
                                          className="px-2.5 py-1 rounded-lg bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
                                        >
                                          اعتماد المدقق
                                        </button>
                                        <button
                                          type="button"
                                          onClick={() => {
                                            setSelectedAllocation(alloc);
                                            setSplitCompanyAmount(String(alloc.companyAmount || alloc.amount));
                                            setSplitRiderAmount('0');
                                          }}
                                          className="px-2 py-1 rounded-lg border border-[var(--line)] text-xs text-[var(--muted)] hover:bg-[var(--raised)] transition cursor-pointer"
                                        >
                                          تعديل
                                        </button>
                                      </div>
                                    ) : (
                                      <span className="text-xs text-[var(--muted)]">معتمد</span>
                                    )}
                                  </td>
                                </tr>
                              );
                            })
                          )}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </div>
              )}

              {/* Sub-tab 4: Facts */}
              {activeTab === 'facts' && (
                <div className="space-y-4">
                  <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
                    عرض وقائع المحفظة المستوردة والمطابقة مع نظام ERP الداخلي.
                  </div>
                </div>
              )}

              {/* Sub-tab 5: Proposed Journals */}
              {activeTab === 'proposed_journals' && (
                <div className="space-y-4">
                  <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
                    سجل القيود المحاسبية المقترحة قبل الترحيل للدليل المستهدف.
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Other App / SubTab views */}
          {(selectedPlatform !== 'hungerstation' || appSubTab !== 'settlements') && (
            <div className="p-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-base font-bold text-[var(--ink)]">
                    منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr} · {
                      appSubTab === 'parent_terms'
                        ? 'الشروط الأصلية (Parent Company Terms)'
                        : appSubTab === 'internal_schemes'
                        ? 'الأنظمة الداخلية (Internal Schemes)'
                        : appSubTab === 'eligibility'
                        ? 'الأهلية والضوابط (Eligibility)'
                        : appSubTab === 'accounts'
                        ? 'الحسابات والمعرفات الميدانية'
                        : 'الفواتير والتسويات'
                    }
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-0.5">
                    هيكلية الاحتساب وفق الموديل المعتمد BR §24.
                  </p>
                </div>
                <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/15 text-[var(--accent)]">
                  شهر {selectedPeriod}
                </span>
              </div>

              {appSubTab === 'parent_terms' && (
                <div className="p-4 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-2">
                  <div className="font-bold text-[var(--ink)]">نموذج الفوترة والتعاقد مع المنصة:</div>
                  <p className="text-[var(--muted)] leading-relaxed">
                    {selectedPlatform === 'keeta'
                      ? 'نظام الشرائح الحافزة (Slabs): رسوم الطلبات + مستويات الخبرة والقدرة التشغيلية × معامل الأهلية والانتظام (26 يوماً) مع تارجت الشركة 70% / 95%.'
                      : selectedPlatform === 'ninja'
                      ? 'تارجت مقطوع: 5,700 ر.س للدباب و 6,500 ر.س للسيارة مقابل 450/460 طلب شهرياً، مع شرائح العجز ونسبة الالتزام 15%.'
                      : 'احتساب بحسب الطلب والمسافة (Per Order / Per KM) مع حوافز النطاقات الجغرافية وبدل الكيلومترات.'}
                  </p>
                </div>
              )}

              {appSubTab === 'internal_schemes' && (
                <div className="p-4 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-2">
                  <div className="font-bold text-[var(--ink)]">الأنظمة الداخلية النشطة:</div>
                  <p className="text-[var(--muted)] leading-relaxed">
                    عمولة يومية مقطوعة (Daily Commission)، نسبة من الإجمالي قبل الخصومات (Percent of Gross)، أو راتب مستهدف مع بدل الوقود والاتصالات وإيجار المركبة.
                  </p>
                </div>
              )}

              {appSubTab === 'eligibility' && (
                <div className="p-4 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-2">
                  <div className="font-bold text-[var(--ink)]">شروط الأهلية والحد الأدنى:</div>
                  <p className="text-[var(--muted)] leading-relaxed">
                    10 ساعات عمل يومية، 3 طلبات على الأقل، 26 يوم حضور، 330/350 طلباً شهرياً، والتحقق الدوري من بصمة الوجه.
                  </p>
                </div>
              )}

              {appSubTab === 'accounts' && (
                <div className="p-4 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-2">
                  <div className="font-bold text-[var(--ink)]">ربط المعرفات واستحقاق المناديب:</div>
                  <p className="text-[var(--muted)] leading-relaxed">
                    مطابقة معرفات المناديب على التطبيق وفصل الفترات عند تبادل الحساب المشترك بين أكثر من مندوب.
                  </p>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 2: PAYROLL CYCLE STEPPER (0-7)                  */}
      {/* ======================================================== */}
      {mainTab === 'cycle' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              دورة الرواتب الشهرية والمسير المعتمد (المراحل 0-7)
            </h3>
            <p className="text-xs sm:text-sm text-[var(--muted)] mt-1">
              مراحل احتساب الرواتب والمسيرات وفق نموذج العمل المالي المعتمد في BR §24.7
            </p>

            {/* Stepper Navigation */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mt-6">
              {cycleSteps.map((st) => {
                const isCurrent = currentCycleStep === st.num;
                return (
                  <div
                    key={st.num}
                    onClick={() => setCurrentCycleStep(st.num)}
                    className={`p-3.5 rounded-xl border transition-all cursor-pointer ${
                      isCurrent
                        ? 'border-[var(--accent)] bg-[var(--accent)]/5 shadow-xs'
                        : 'border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span
                        className={`h-6 w-6 rounded-full flex items-center justify-center font-bold text-xs ${
                          isCurrent
                            ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                            : 'bg-[var(--raised)] text-[var(--muted)]'
                        }`}
                      >
                        {st.num}
                      </span>
                      {st.num === 5 && (
                        <span className="px-1.5 py-0.2 rounded-full text-[9px] font-bold bg-[var(--warning)]/20 text-[var(--warning)]">
                          يحتاج قرار
                        </span>
                      )}
                    </div>
                    <div className="font-bold text-xs sm:text-sm text-[var(--ink)] mt-2">
                      {st.title}
                    </div>
                    <div className="text-[11px] text-[var(--muted)] mt-1 line-clamp-2">
                      {st.desc}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Step Detail Card */}
          <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <span className="text-xs font-bold text-[var(--accent)]">المرحلة {currentCycleStep} من 7</span>
                <h4 className="text-base sm:text-lg font-bold text-[var(--ink)] mt-0.5">
                  {cycleSteps[currentCycleStep].title}
                </h4>
              </div>
              {currentCycleStep === 5 && (
                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-bold bg-[var(--warning)]/15 text-[var(--warning)] border border-[var(--warning)]/30">
                  <AlertTriangle size={14} />
                  <span>شارة «يحتاج قرار» للصافي السالب (§22.9.2)</span>
                </span>
              )}
            </div>
            <p className="text-xs sm:text-sm text-[var(--muted)] leading-relaxed">
              {cycleSteps[currentCycleStep].desc}
            </p>
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 3: RULES EDITOR (W9)                            */}
      {/* ======================================================== */}
      {mainTab === 'rules' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                  محرر الأنظمة الداخلية ومصفوفة الفئات التشغيلية
                </h3>
                <span
                  className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                    rulesStatus === 'approved'
                      ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                      : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                  }`}
                >
                  {rulesStatus === 'approved' ? `معتمد من @${rulesApprovedBy || 'admin'}` : 'مسودة قابلة للتعديل'}
                </span>
              </div>
              <p className="text-xs text-[var(--muted)] mt-1">
                الأنظمة خاضعة لإصدارات شهرية مستقلة. تتطلب المسودة اعتماد مستخدم ثانٍ مستقل (Maker-Checker) لتصبح للقراءة فقط.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="month"
                value={rulesMonth}
                onChange={(e) => setRulesMonth(e.target.value)}
                className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
              <button
                type="button"
                onClick={() => {
                  toast('تم نسخ إعدادات وقواعد الشهر السابق بنجاح كمسودة جديدة', 'success');
                }}
                className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1 cursor-pointer"
              >
                <Copy size={13} />
                <span>انسخ الشهر السابق</span>
              </button>
            </div>
          </div>

          {/* 6-Column Category Matrix */}
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
            <div className="p-4 border-b border-[var(--line)]">
              <h4 className="font-bold text-sm text-[var(--ink)]">
                مصفوفة الفئات (الكفالة × المركبة) · شبكة الـ 6 أعمدة المعتمدة
              </h4>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                تحديد تكلفة إيجار المركبة اليومي (سيارة 1800/30 = 60 ر.س / دباب 800/30 = 26.67 ر.س) ونسب العمولة لكل فئة.
              </p>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-start text-xs sm:text-sm">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                  <tr>
                    <th className="p-3 text-start">البند / الفئة</th>
                    <th className="p-3 text-start">كفالة × سيارة</th>
                    <th className="p-3 text-start">كفالة × دباب</th>
                    <th className="p-3 text-start">كفالة × خاص</th>
                    <th className="p-3 text-start">خارجي × سيارة</th>
                    <th className="p-3 text-start">خارجي × دباب</th>
                    <th className="p-3 text-start">خارجي × خاص</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  <tr>
                    <td className="p-3 font-semibold">إيجار المركبة اليومي</td>
                    <td className="p-3 num font-bold text-[var(--accent)]">60.00 ر.س</td>
                    <td className="p-3 num font-bold text-[var(--accent)]">26.67 ر.س</td>
                    <td className="p-3 num text-[var(--muted)]">0.00 ر.س</td>
                    <td className="p-3 num font-bold text-[var(--accent)]">60.00 ر.س</td>
                    <td className="p-3 num font-bold text-[var(--accent)]">26.67 ر.س</td>
                    <td className="p-3 num text-[var(--muted)]">0.00 ر.س</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold">العمولة الشهرية المستهدفة</td>
                    <td className="p-3 num">2,500 ر.س</td>
                    <td className="p-3 num">2,000 ر.س</td>
                    <td className="p-3 num">1,800 ر.س</td>
                    <td className="p-3 num">2,200 ر.س</td>
                    <td className="p-3 num">1,800 ر.س</td>
                    <td className="p-3 num">1,500 ر.س</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 4: DEDUCTIONS (Stage 2)                         */}
      {/* ======================================================== */}
      {mainTab === 'deductions' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              سجل استقطاعات التشغيل والتحملات (Stage 2 Deductions)
            </h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              تطبيق الاستقطاعات مرة واحدة لكل مندوب شهرياً: سلف، مخالفات مرورية، إنترنت، محروقات، محفظة هنقرستيشن، ومداد.
            </p>
          </div>

          <div className="p-8 text-center bg-[var(--surface)] rounded-2xl border border-[var(--line)] text-xs text-[var(--muted)]">
            يتم استدعاء استقطاعات المرحلة الثانية وربطها آلياً من موديول المديونيات والسلف وسجلات الفواتير.
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 5: BANK TRANSFER                                */}
      {/* ======================================================== */}
      {mainTab === 'bank' && (
        <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4 text-center max-w-xl mx-auto">
          <div className="h-14 w-14 rounded-2xl bg-[var(--accent)]/15 text-[var(--accent)] flex items-center justify-center mx-auto">
            <Landmark size={28} />
          </div>
          <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
            ملف صرف الرواتب البنكي المعتمد (CSV)
          </h3>
          <p className="text-xs text-[var(--muted)] leading-relaxed">
            يتم إصدار وتصدير ملف صرف رواتب المناديب المعتمد متوافقاً مع مواصفات البنوك السعودية عبر موديول الخزينة والبنوك.
          </p>
          <div className="pt-2">
            <Link
              href="/treasury/?tab=bank_file"
              className="px-5 py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition shadow-xs inline-flex items-center gap-2"
            >
              <span>الانتقال لمولد ملف البنك بالخزينة</span>
              <ChevronRight size={16} />
            </Link>
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 6: LEGACY POLICIES                              */}
      {/* ======================================================== */}
      {mainTab === 'policies' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              أرشيف السياسات المالية السابقة (Legacy Policies)
            </h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              متاحة للقراءة والمطابقة التاريخية فقط لحين استكمال محرر الأنظمة الجديد بالكامل.
            </p>
          </div>

          {loadingPolicies ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : policies.length === 0 ? (
            <EmptyState
              title="لا توجد سياسات سابقة مؤرشفة"
              description="لم يتم العثور على أي سياسات مسجلة في /api/policies."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">معرف السياسة</th>
                      <th className="p-3 text-start">اسم السياسة</th>
                      <th className="p-3 text-start">النوع (Kind)</th>
                      <th className="p-3 text-start">العمولة / الشريحة</th>
                      <th className="p-3 text-start">تاريخ الإنشاء</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {policies.map((p: any, idx: number) => (
                      <tr key={p.id || idx} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--accent)]">{p.id || idx + 1}</td>
                        <td className="p-3 font-semibold">{p.name || '-'}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                            {p.kind || 'standard'}
                          </span>
                        </td>
                        <td className="p-3 num font-bold">{p.commissionAmount || p.monthlyFee || '-'}</td>
                        <td className="p-3 num text-xs text-[var(--muted)]">{p.createdAt || '-'}</td>
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
      {/* TGA SPLIT DIALOG                                        */}
      {/* ======================================================== */}
      <Dialog
        open={!!selectedAllocation}
        onClose={() => setSelectedAllocation(null)}
        title="توزيع مبالغ TGA بين الشركة والمناديب"
      >
        {selectedAllocation && (
          <div className="space-y-4 pt-2">
            <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-1">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">معرف التوزيع:</span>
                <span className="font-mono font-bold">{selectedAllocation.id}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">إجمالي مبلغ TGA:</span>
                <span className="num font-bold text-[var(--accent)]">
                  {selectedAllocation.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>
            </div>

            <div className="flex rounded-xl bg-[var(--raised)] p-1 border border-[var(--line)]">
              <button
                type="button"
                onClick={() => setSplitMode('amount')}
                className={`flex-1 py-1 rounded-lg text-xs font-bold transition ${
                  splitMode === 'amount' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                تحديد المبالغ صراحة (ر.س)
              </button>
              <button
                type="button"
                onClick={() => setSplitMode('percent')}
                className={`flex-1 py-1 rounded-lg text-xs font-bold transition ${
                  splitMode === 'percent' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                تقسيم بالنسبة المئوية %
              </button>
            </div>

            {splitMode === 'percent' ? (
              <div className="space-y-3">
                <div className="space-y-1">
                  <div className="flex justify-between text-xs font-bold">
                    <span>نسبة المندوب: {splitRiderPercent}%</span>
                    <span>نسبة الشركة: {100 - Number(splitRiderPercent)}%</span>
                  </div>
                  <input
                    type="range"
                    min="0"
                    max="100"
                    value={splitRiderPercent}
                    onChange={(e) => setSplitRiderPercent(e.target.value)}
                    className="w-full cursor-pointer"
                  />
                </div>
                <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)] space-y-1 text-xs">
                  <div className="flex justify-between">
                    <span className="text-[var(--muted)]">نصيب المندوب المحتسب:</span>
                    <span className="num font-bold">
                      {(((selectedAllocation.amount * Number(splitRiderPercent)) / 100)).toFixed(2)} ر.س
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-[var(--muted)]">نصيب الشركة المحتسب:</span>
                    <span className="num font-bold">
                      {(selectedAllocation.amount - ((selectedAllocation.amount * Number(splitRiderPercent)) / 100)).toFixed(2)} ر.س
                    </span>
                  </div>
                  <div className="text-[10px] text-[var(--muted)] text-center pt-1 border-t border-[var(--line)]">
                    تقريبي — المبلغ النهائي من الخادم
                  </div>
                </div>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label className="text-xs font-bold">نصيب الشركة (ر.س):</label>
                  <input
                    type="number"
                    step="0.01"
                    value={splitCompanyAmount}
                    onChange={(e) => setSplitCompanyAmount(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-bold">نصيب المندوب (ر.س):</label>
                  <input
                    type="number"
                    step="0.01"
                    value={splitRiderAmount}
                    onChange={(e) => setSplitRiderAmount(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                  />
                </div>
              </div>
            )}

            <div className="space-y-1">
              <label className="text-xs font-bold">رقم إقامة أو معرف المندوب المستفيد:</label>
              <input
                type="text"
                placeholder="2000000789"
                value={splitRiderId}
                onChange={(e) => setSplitRiderId(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
              />
            </div>

            <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setSelectedAllocation(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={savingSplit}
                onClick={handleSaveTgaSplit}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
              >
                {savingSplit ? 'جاري الحفظ...' : 'حفظ مقترح التوزيع'}
              </button>
            </div>
          </div>
        )}
      </Dialog>

      {/* ======================================================== */}
      {/* DISPUTE RESOLVE DIALOG                                   */}
      {/* ======================================================== */}
      <Dialog
        open={!!selectedDispute}
        onClose={() => setSelectedDispute(null)}
        title="معالجة وفض نزاع استقطاع CAR"
        description="توثيق رد المنصة الرسمي. رفض المنصة يتيح نقل المبلغ لحساب النزاعات 112803 للسماح بترحيل الفاتورة دون الإضرار بالمندوب."
      >
        {selectedDispute && (
          <div className="space-y-4 pt-2">
            <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-1">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">معرف النزاع:</span>
                <span className="font-mono font-bold">{selectedDispute.id}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المبلغ المتنازع عليه:</span>
                <span className="num font-bold text-[var(--danger)]">
                  {selectedDispute.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>
            </div>

            <div className="space-y-2">
              <label className="text-xs font-bold">قرار المنصة الرسمي:</label>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setResolveOutcome('platform_credit')}
                  className={`p-2.5 rounded-xl border text-xs font-bold text-center transition cursor-pointer ${
                    resolveOutcome === 'platform_credit'
                      ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)]'
                      : 'border-[var(--line)] text-[var(--muted)] hover:bg-[var(--raised)]'
                  }`}
                >
                  المنصة ردّت المبلغ
                </button>
                <button
                  type="button"
                  onClick={() => setResolveOutcome('platform_rejected')}
                  className={`p-2.5 rounded-xl border text-xs font-bold text-center transition cursor-pointer ${
                    resolveOutcome === 'platform_rejected'
                      ? 'border-[var(--warning)] bg-[var(--warning)]/10 text-[var(--warning)]'
                      : 'border-[var(--line)] text-[var(--muted)] hover:bg-[var(--raised)]'
                  }`}
                >
                  المنصة رفضت (تصعيد 112803)
                </button>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">مرجع المنصة الرسمي (Platform Reference):</label>
              <input
                type="text"
                placeholder="مثال: HS-TICKET-89421"
                value={platformReference}
                onChange={(e) => setPlatformReference(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">بيان وسبب التسوية أو قرار المنصة:</label>
              <textarea
                rows={3}
                placeholder="اكتب تفاصيل أو حيثيات قرار المنصة وسبب التسوية أو التصعيد..."
                value={resolveReason}
                onChange={(e) => setResolveReason(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setSelectedDispute(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={resolvingDispute}
                onClick={handleResolveDispute}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
              >
                {resolvingDispute ? 'جاري الفض...' : 'تأكيد قرار فض النزاع'}
              </button>
            </div>
          </div>
        )}
      </Dialog>
    </div>
  );
}

export default function PayrollPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <PayrollPageInner />
    </Suspense>
  );
}
