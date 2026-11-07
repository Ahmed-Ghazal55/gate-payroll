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
  Lock,
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
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { PayrollReviewGrid } from '../../features/payroll/PayrollReviewGrid';
import { NinjaInvoiceUploadScreen } from '../../features/payroll/NinjaInvoiceUploadScreen';
import type {
  HsInvoice,
  HsDispute,
  HsAllocation,
  HsFact,
  HsProposedJournal,
  HsImportFile,
  HsImportPreviewResult,
  PayConfigDoc,
  PayAssignment,
  PayAssignmentPreviewResult,
  PayCycle,
  PayCycleLine,
  LegacyPayrollPreviewRow,
  LegacyPayrollRun,
} from '../../lib/types';

function getPreviousMonth(period: string): string {
  if (!period || !period.includes('-')) return '2026-09';
  const [yearStr, monthStr] = period.split('-');
  let y = parseInt(yearStr, 10);
  let m = parseInt(monthStr, 10);
  m -= 1;
  if (m < 1) {
    m = 12;
    y -= 1;
  }
  return `${y}-${String(m).padStart(2, '0')}`;
}

function PayrollPageInner() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const showToast = (msg: string, type: 'success' | 'error' | 'info' | 'warning' = 'info') => {
    toast(msg, type === 'error' ? 'danger' : type);
  };

  // Main shared tabs: platforms · current-cycle · cycle · rules · deductions · bank · policies
  const tabFromUrl = searchParams?.get('tab') || 'platforms';
  const appFromUrl = searchParams?.get('app') || 'hungerstation';
  const subFromUrl = (searchParams?.get('sub') as any) || 'settlements';
  const periodFromUrl = searchParams?.get('period') || '2026-10';
  const [mainTab, setMainTab] = useState<string>(tabFromUrl);

  // Selected App within platforms tab
  const [selectedPlatform, setSelectedPlatform] = useState<string>(appFromUrl);

  // Normalized app slug for pay endpoints
  const appSlug = useMemo(() => {
    if (selectedPlatform === 'hungerstation' || selectedPlatform === 'hs') return 'hs';
    if (selectedPlatform === 'thechefz' || selectedPlatform === 'chefz') return 'chefz';
    return selectedPlatform;
  }, [selectedPlatform]);

  // Sub-tabs inside selected app
  const [appSubTab, setAppSubTab] = useState<'settlements' | 'parent_terms' | 'internal_schemes' | 'eligibility' | 'accounts'>(subFromUrl);

  // Filters & period
  const [selectedPeriod, setSelectedPeriod] = useState<string>(periodFromUrl);
  const [selectedCompany, setSelectedCompany] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== mainTab) {
      setMainTab(tabFromUrl);
    }
    if (appFromUrl && appFromUrl !== selectedPlatform) {
      setSelectedPlatform(appFromUrl);
    }
    if (subFromUrl && subFromUrl !== appSubTab) {
      setAppSubTab(subFromUrl);
    }
    if (periodFromUrl && periodFromUrl !== selectedPeriod) {
      setSelectedPeriod(periodFromUrl);
      setRulesMonth(periodFromUrl);
      setLegacyPeriod(periodFromUrl);
    }
  }, [tabFromUrl, appFromUrl, subFromUrl, periodFromUrl]);

  // Navigation tabs within HungerStation settlements
  const [activeTab, setActiveTab] = useState<string>('invoices');

  // Data states for HungerStation
  const [loading, setLoading] = useState<boolean>(true);
  const [invoices, setInvoices] = useState<HsInvoice[]>([]);
  const [disputes, setDisputes] = useState<HsDispute[]>([]);
  const [allocations, setAllocations] = useState<HsAllocation[]>([]);
  const [facts, setFacts] = useState<HsFact[]>([]);
  const [proposedJournals, setProposedJournals] = useState<HsProposedJournal[]>([]);
  const [hsSectionError, setHsSectionError] = useState<string | null>(null);

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

  // ========================================================
  // PAY FRAMEWORK REAL API STATES (F1)
  // ========================================================
  const [termsList, setTermsList] = useState<PayConfigDoc[]>([]);
  const [loadingTerms, setLoadingTerms] = useState<boolean>(false);
  const [termsError, setTermsError] = useState<string | null>(null);

  const [schemesList, setSchemesList] = useState<PayConfigDoc[]>([]);
  const [loadingSchemes, setLoadingSchemes] = useState<boolean>(false);
  const [schemesError, setSchemesError] = useState<string | null>(null);

  const [eligibilityList, setEligibilityList] = useState<PayConfigDoc[]>([]);
  const [loadingEligibility, setLoadingEligibility] = useState<boolean>(false);
  const [eligibilityError, setEligibilityError] = useState<string | null>(null);

  const [assignmentsList, setAssignmentsList] = useState<PayAssignment[]>([]);
  const [loadingAssignments, setLoadingAssignments] = useState<boolean>(false);
  const [assignmentsError, setAssignmentsError] = useState<string | null>(null);

  // Scheme choices import modal (preview -> commit flow R4.3)
  const [assignmentsImportModalOpen, setAssignmentsImportModalOpen] = useState<boolean>(false);
  const [assignmentSourceType, setAssignmentSourceType] = useState<string>('campaign');
  const [assignmentFormId, setAssignmentFormId] = useState<string>('');
  const [assignmentFieldId, setAssignmentFieldId] = useState<string>('workSystem');
  const [assignmentMappingKey, setAssignmentMappingKey] = useState<string>('commission');
  const [assignmentMappingScheme, setAssignmentMappingScheme] = useState<string>('');
  const [assignmentPreviewResult, setAssignmentPreviewResult] = useState<PayAssignmentPreviewResult | null>(null);
  const [previewingAssignments, setPreviewingAssignments] = useState<boolean>(false);
  const [committingAssignments, setCommittingAssignments] = useState<boolean>(false);

  // Cycle Stepper state (0-7)
  const [cyclesList, setCyclesList] = useState<PayCycle[]>([]);
  const [loadingCycles, setLoadingCycles] = useState<boolean>(false);
  const [cyclesError, setCyclesError] = useState<string | null>(null);
  const [advancingCycle, setAdvancingCycle] = useState<boolean>(false);
  const [cycleLines, setCycleLines] = useState<PayCycleLine[]>([]);
  const [loadingLines, setLoadingLines] = useState<boolean>(false);
  const [selectedLineForExplain, setSelectedLineForExplain] = useState<PayCycleLine | null>(null);

  const cycleLineColumns: ColumnDef<any>[] = useMemo(
    () => [
      {
        key: 'rider',
        header: 'إقامة المندوب',
        filterable: true,
        render: (line) => (
          <Link
            href={`/riders/statement?iqama=${line.rider}`}
            className="font-mono font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1"
          >
            <span>{line.rider}</span>
            <ExternalLink size={11} className="opacity-70" />
          </Link>
        ),
      },
      {
        key: 'app',
        header: 'التطبيق',
        filterable: true,
        render: (line) => <span className="font-mono text-xs">{line.app}</span>,
      },
      {
        key: 'category',
        header: 'الفئة',
        render: (line) => <span className="text-xs text-[var(--muted)]">{line.category || '-'}</span>,
      },
      {
        key: 'platformGross',
        header: 'إجمالي المنصة',
        numeric: true,
        getValue: (line) => line.platformGross || line.gross || 0,
        render: (line) => (
          <span>
            {(line.platformGross || line.gross || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'vehicleCharge',
        header: 'إيجار المركبة',
        numeric: true,
        getValue: (line) => line.allocatedVehicleCharge ?? line.vehicleCharge ?? 0,
        render: (line) => (
          <span className="text-[var(--muted)]">
            {(line.allocatedVehicleCharge ?? line.vehicleCharge ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'deductions',
        header: 'الاستقطاعات',
        numeric: true,
        getValue: (line) => line.accountDeduction || 0,
        render: (line) => (
          <span className="text-[var(--danger)]">
            {(line.accountDeduction || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'net',
        header: 'الصافي النهائي',
        numeric: true,
        getValue: (line) => line.net ?? line.entitlement ?? 0,
        render: (line) => {
          const net = line.net ?? line.entitlement ?? 0;
          return (
            <span className={`font-bold ${net < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'}`}>
              {net.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
            </span>
          );
        },
      },
      {
        key: 'status',
        header: 'الحالة',
        render: (line) => (
          <span
            className={`px-2 py-0.5 rounded-full text-xs font-bold ${
              line.status === 'ready' || line.status === 'approved'
                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
            }`}
          >
            {line.status === 'ready' ? 'جاهز' : line.status === 'approved' ? 'معتمد' : 'يحتاج قرار'}
          </span>
        ),
      },
      {
        key: 'explain',
        header: 'شرح الاحتساب',
        render: (line) => (
          <button
            type="button"
            onClick={() => setSelectedLineForExplain(line)}
            className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-xs font-bold text-[var(--accent)] hover:bg-[var(--line)] transition cursor-pointer"
          >
            شرح الاحتساب (Explain)
          </button>
        ),
      },
    ],
    []
  );

  // Global settings state (effective-dated)
  const [globalSettings, setGlobalSettings] = useState<PayConfigDoc[]>([]);
  const [loadingGlobalSettings, setLoadingGlobalSettings] = useState<boolean>(false);
  const [globalSettingsError, setGlobalSettingsError] = useState<string | null>(null);
  const [rulesMonth, setRulesMonth] = useState<string>('2026-10');

  const [copyingMonth, setCopyingMonth] = useState<boolean>(false);

  // ========================================================
  // LEGACY PRODUCTION PAYROLL RUN STATES (F2)
  // ========================================================
  const [legacyPeriod, setLegacyPeriod] = useState<string>('2026-10');
  const [legacyPreview, setLegacyPreview] = useState<LegacyPayrollPreviewRow[]>([]);
  const [legacyRuns, setLegacyRuns] = useState<LegacyPayrollRun[]>([]);
  const [ridersList, setRidersList] = useState<any[]>([]);
  const [financeCatalog, setFinanceCatalog] = useState<{ sponsors: any[]; vaults: any[] } | null>(null);
  const [loadingLegacy, setLoadingLegacy] = useState<boolean>(false);
  const [legacyError, setLegacyError] = useState<string | null>(null);

  // Legacy Modals
  const [detailPreviewRow, setDetailPreviewRow] = useState<LegacyPayrollPreviewRow | null>(null);
  const [approvePreviewRow, setApprovePreviewRow] = useState<LegacyPayrollPreviewRow | null>(null);
  const [approveSponsorId, setApproveSponsorId] = useState<string>('');
  const [approvingRun, setApprovingRun] = useState<boolean>(false);

  const [adjModalOpen, setAdjModalOpen] = useState<boolean>(false);
  const [adjIqama, setAdjIqama] = useState<string>('');
  const [adjAmount, setAdjAmount] = useState<string>('');
  const [adjSourceRef, setAdjSourceRef] = useState<string>('');
  const [adjCategory, setAdjCategory] = useState<string>('fuel');
  const [adjDescription, setAdjDescription] = useState<string>('');
  const [savingAdj, setSavingAdj] = useState<boolean>(false);

  const [payModalOpen, setPayModalOpen] = useState<boolean>(false);
  const [payIqama, setPayIqama] = useState<string>('');
  const [paySponsorId, setPaySponsorId] = useState<string>('');
  const [payVaultCode, setPayVaultCode] = useState<string>('');
  const [payAmount, setPayAmount] = useState<string>('');
  const [paySourceRef, setPaySourceRef] = useState<string>('');
  const [payOnBehalf, setPayOnBehalf] = useState<boolean>(false);
  const [payOnBehalfNote, setPayOnBehalfNote] = useState<string>('');
  const [savingPay, setSavingPay] = useState<boolean>(false);

  const [ledgerModalOpen, setLedgerModalOpen] = useState<boolean>(false);
  const [ledgerIqama, setLedgerIqama] = useState<string | null>(null);
  const [ledgerData, setLedgerData] = useState<{ balance: number; rows: any[] } | null>(null);
  const [loadingLedger, setLoadingLedger] = useState<boolean>(false);

  // ========================================================
  // LOADERS
  // ========================================================

  // Load HungerStation Data
  const loadData = async () => {
    setLoading(true);
    setHsSectionError(null);
    try {
      const comp = selectedCompany === 'all' ? undefined : selectedCompany;
      const [invRes, dispRes, allocRes, factRes, propRes] = await Promise.allSettled([
        api.getHsInvoices({ period: selectedPeriod, companyId: comp }),
        api.getHsDisputes({ period: selectedPeriod }),
        api.getHsAllocations({ period: selectedPeriod }),
        api.getHsFacts({ kind: selectedFactKind }),
        api.getHsProposedJournals({ period: selectedPeriod } as any),
      ]);

      const errs: string[] = [];
      if (invRes.status === 'fulfilled') {
        const val = invRes.value;
        setInvoices((val as any)?.items || (Array.isArray(val) ? val : []));
      } else {
        errs.push(`فواتير المنصة: ${invRes.reason?.message || 'فشل التحميل'}`);
        setInvoices([]);
      }

      if (dispRes.status === 'fulfilled') {
        const val = dispRes.value;
        setDisputes((val as any)?.items || (Array.isArray(val) ? val : []));
      } else {
        errs.push(`نزاعات CAR: ${dispRes.reason?.message || 'فشل التحميل'}`);
        setDisputes([]);
      }

      if (allocRes.status === 'fulfilled') {
        const val = allocRes.value;
        setAllocations((val as any)?.items || (Array.isArray(val) ? val : []));
      } else {
        errs.push(`توزيعات TGA: ${allocRes.reason?.message || 'فشل التحميل'}`);
        setAllocations([]);
      }

      if (factRes.status === 'fulfilled') {
        const val = factRes.value;
        setFacts((val as any)?.items || (Array.isArray(val) ? val : []));
      } else {
        errs.push(`وقائع ERP: ${factRes.reason?.message || 'فشل التحميل'}`);
        setFacts([]);
      }

      if (propRes.status === 'fulfilled') {
        const val = propRes.value;
        setProposedJournals((val as any)?.items || (Array.isArray(val) ? val : []));
      } else {
        errs.push(`القيود المقترحة: ${propRes.reason?.message || 'فشل التحميل'}`);
        setProposedJournals([]);
      }

      if (errs.length > 0) {
        setHsSectionError(errs.join(' • '));
      }
    } catch (err: any) {
      setHsSectionError(err.message || 'تعذر تحميل بيانات منصة هنقرستيشن');
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

  // Load Terms
  const loadTerms = async () => {
    setLoadingTerms(true);
    setTermsError(null);
    try {
      const res = await api.getPayTerms(appSlug, selectedPeriod);
      setTermsList(res?.items || (Array.isArray(res) ? res : []));
    } catch (err: any) {
      setTermsError(err.message || 'تعذر تحميل شروط المنصة');
      setTermsList([]);
    } finally {
      setLoadingTerms(false);
    }
  };

  // Load Schemes
  const loadSchemes = async () => {
    setLoadingSchemes(true);
    setSchemesError(null);
    try {
      const res = await api.getPaySchemes(appSlug, selectedPeriod);
      setSchemesList(res?.items || (Array.isArray(res) ? res : []));
    } catch (err: any) {
      setSchemesError(err.message || 'تعذر تحميل الأنظمة الداخلية');
      setSchemesList([]);
    } finally {
      setLoadingSchemes(false);
    }
  };

  // Load Eligibility
  const loadEligibility = async () => {
    if (appSlug === 'jahez') {
      setEligibilityList([]);
      return;
    }
    setLoadingEligibility(true);
    setEligibilityError(null);
    try {
      const res = await api.getPayEligibility(appSlug, selectedPeriod);
      setEligibilityList(res?.items || (Array.isArray(res) ? res : []));
    } catch (err: any) {
      setEligibilityError(err.message || 'تعذر تحميل ضوابط الأهلية');
      setEligibilityList([]);
    } finally {
      setLoadingEligibility(false);
    }
  };

  // Load Assignments
  const loadAssignments = async () => {
    setLoadingAssignments(true);
    setAssignmentsError(null);
    try {
      const res = await api.getPayAssignments(appSlug, selectedPeriod);
      setAssignmentsList(res?.items || (Array.isArray(res) ? res : []));
    } catch (err: any) {
      setAssignmentsError(err.message || 'تعذر تحميل تكليفات المناديب');
      setAssignmentsList([]);
    } finally {
      setLoadingAssignments(false);
    }
  };

  // Load Cycles
  const loadCycles = async () => {
    setLoadingCycles(true);
    setCyclesError(null);
    try {
      const res = await api.getPayCycles(selectedPeriod);
      const items = res?.items || (Array.isArray(res) ? res : []);
      setCyclesList(items);
      if (items.length > 0) {
        await loadCycleLines(items[0].id);
      } else {
        setCycleLines([]);
      }
    } catch (err: any) {
      setCyclesError(err.message || 'تعذر تحميل دورات الرواتب');
      setCyclesList([]);
      setCycleLines([]);
    } finally {
      setLoadingCycles(false);
    }
  };

  // Load Cycle Lines
  const loadCycleLines = async (cycleId: string) => {
    setLoadingLines(true);
    try {
      const res = await api.getPayCycleLines(cycleId);
      setCycleLines(res?.items || (Array.isArray(res) ? res : []));
    } catch (_) {
      setCycleLines([]);
    } finally {
      setLoadingLines(false);
    }
  };

  // Load Global Settings
  const loadGlobalSettings = async () => {
    setLoadingGlobalSettings(true);
    setGlobalSettingsError(null);
    try {
      const res = await api.getGlobalPaySettings();
      setGlobalSettings(res?.items || (Array.isArray(res) ? res : []));
    } catch (err: any) {
      setGlobalSettingsError(err.message || 'تعذر تحميل الإعدادات العامة');
      setGlobalSettings([]);
    } finally {
      setLoadingGlobalSettings(false);
    }
  };

  // Load Legacy Production Payroll
  const loadLegacyPayroll = async () => {
    setLoadingLegacy(true);
    setLegacyError(null);
    try {
      const [prevRes, runsRes, ridersRes, catRes] = await Promise.allSettled([
        api.getLegacyPayrollPreview(legacyPeriod),
        api.getLegacyPayrollRuns(),
        api.getRiders(),
        api.getFinanceCatalog(),
      ]);

      const errs: string[] = [];
      if (prevRes.status === 'fulfilled') {
        setLegacyPreview(Array.isArray(prevRes.value) ? prevRes.value : []);
      } else {
        errs.push(`المعاينة: ${prevRes.reason?.message || 'فشل التحميل'}`);
        setLegacyPreview([]);
      }

      if (runsRes.status === 'fulfilled') {
        setLegacyRuns(Array.isArray(runsRes.value) ? runsRes.value : []);
      } else {
        errs.push(`المسيرات السابقة: ${runsRes.reason?.message || 'فشل التحميل'}`);
        setLegacyRuns([]);
      }

      if (ridersRes.status === 'fulfilled') {
        setRidersList(Array.isArray(ridersRes.value) ? ridersRes.value : []);
      } else {
        errs.push(`قائمة المناديب: ${ridersRes.reason?.message || 'فشل التحميل'}`);
        setRidersList([]);
      }

      if (catRes.status === 'fulfilled') {
        setFinanceCatalog(catRes.value);
      } else {
        errs.push(`دليل الحسابات المالية: ${catRes.reason?.message || 'فشل التحميل'}`);
        setFinanceCatalog(null);
      }

      if (errs.length > 0) {
        setLegacyError(errs.join(' • '));
      }
    } catch (err: any) {
      setLegacyError(err.message || 'تعذر تحميل مسيرات الرواتب القائمة');
    } finally {
      setLoadingLegacy(false);
    }
  };

  useEffect(() => {
    loadData();
    loadPolicies();
  }, [selectedPeriod, selectedCompany, selectedFactKind]);

  useEffect(() => {
    if (mainTab === 'platforms') {
      if (appSubTab === 'parent_terms') loadTerms();
      else if (appSubTab === 'internal_schemes') loadSchemes();
      else if (appSubTab === 'eligibility') loadEligibility();
      else if (appSubTab === 'accounts') loadAssignments();
    } else if (mainTab === 'cycle') {
      loadCycles();
    } else if (mainTab === 'rules') {
      loadGlobalSettings();
    } else if (mainTab === 'current-cycle') {
      loadLegacyPayroll();
    }
  }, [mainTab, appSubTab, selectedPlatform, selectedPeriod, legacyPeriod]);

  // Derived KPI values for HungerStation
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

  // Active Cycle
  const activeCycle = useMemo(() => {
    return cyclesList.length > 0 ? cyclesList[0] : null;
  }, [cyclesList]);

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
      showToast('يرجى كتابة بيان وسبب التسوية أو الرفض الصادر من المنصة', 'error');
      return;
    }

    setResolvingDispute(true);
    try {
      await api.resolveHsDispute(selectedDispute.invoiceId || selectedDispute.id, {
        mode: 'dispute',
        disputeId: selectedDispute.id,
        outcome: resolveOutcome,
        platformReference: platformReference.trim(),
        reason: resolveReason.trim(),
      });
      showToast(
        resolveOutcome === 'platform_rejected'
          ? 'تم تسجيل رفض المنصة وتصعيد النزاع لحساب 112803 بنجاح'
          : 'تم فض النزاع وإصدار إشعار الدائن بنجاح',
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

  // ========================================================
  // ACTIONS FOR PAY FRAMEWORK (F1)
  // ========================================================

  const handleCopyMonth = async (targetApp = appSlug) => {
    const prevMonth = getPreviousMonth(selectedPeriod);
    setCopyingMonth(true);
    try {
      const res = await api.copyPayMonth(targetApp, prevMonth, selectedPeriod);
      showToast(`تم نسخ ${res.created} إعداد من شهر ${prevMonth} كمسودات جديدة (${res.unchanged} مطابق سابقاً)`, 'success');
      if (appSubTab === 'parent_terms') loadTerms();
      if (appSubTab === 'internal_schemes') loadSchemes();
      if (appSubTab === 'eligibility') loadEligibility();
      if (mainTab === 'rules') loadGlobalSettings();
    } catch (err: any) {
      showToast(err.message || `تعذر نسخ إعدادات شهر ${prevMonth}`, 'error');
    } finally {
      setCopyingMonth(false);
    }
  };

  const handleCreateDraftTerms = async () => {
    try {
      const defaultData =
        appSlug === 'keeta'
          ? {
              name: 'شروط كيتا الأصلية',
              billingModel: 'slabs',
              parameters: {
                metric: 'orders',
                bands: [
                  { min: 0, max: 100, base: 10 },
                  { min: 101, max: 500, base: 12 },
                ],
              },
            }
          : appSlug === 'ninja'
          ? {
              name: 'شروط نينجا الأصلية',
              billingModel: 'flat_target',
              parameters: {
                targetOrders: 450,
                monthlyAmount: 5700,
                extraRate: 12,
                shortfallPerOrder: 15,
                prorate: false,
              },
            }
          : {
              name: `شروط ${appSlug} الأصلية`,
              billingModel: 'per_order',
              parameters: { pricePerOrder: 10, companyCostAccount: '526402' },
            };

      await api.createPayTerms(appSlug, { data: defaultData }, selectedPeriod);
      showToast('تم إنشاء مسودة شروط المنصة بنجاح', 'success');
      loadTerms();
    } catch (err: any) {
      showToast(err.message || 'تعذر إنشاء مسودة الشروط', 'error');
    }
  };

  const handleApproveTerms = async (termsDoc: PayConfigDoc) => {
    try {
      await api.approvePayTerms(appSlug, termsDoc.id);
      showToast('تم اعتماد شروط المنصة بنجاح وتثبيتها للقراءة فقط', 'success');
      loadTerms();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد شروط المنصة', 'error');
    }
  };

  const handleCreateDraftScheme = async () => {
    try {
      const categories = [
        'sponsored_company_car',
        'sponsored_company_bike',
        'sponsored_private',
        'external_company_car',
        'external_company_bike',
        'external_private',
      ];
      const cells: Record<string, any> = {};
      categories.forEach((c) => {
        cells[c] = { enabled: true, dailyCommission: 10, chargeCompanyVehicle: false };
      });

      const body: any = {
        period: selectedPeriod,
        code: `${appSlug}-commission`,
        data: {
          name: `نظام ${appSlug} الداخلي`,
          type: 'daily_commission',
          accountingRole: 'agent',
          parameters: { categories: cells },
        },
      };

      // If approved eligibility exists for app and period, link it
      const approvedEl = eligibilityList.find((e) => e.status === 'approved');
      if (approvedEl) {
        body.data.eligibilitySetId = approvedEl.id;
      }

      await api.createPayScheme(appSlug, body, selectedPeriod);
      showToast('تم إنشاء مسودة النظام الداخلي بنجاح', 'success');
      loadSchemes();
    } catch (err: any) {
      showToast(err.message || 'تعذر إنشاء مسودة النظام الداخلي', 'error');
    }
  };

  const handleApproveScheme = async (schemeDoc: PayConfigDoc) => {
    try {
      await api.approvePayScheme(appSlug, schemeDoc.id);
      showToast('تم اعتماد النظام الداخلي بنجاح', 'success');
      loadSchemes();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد النظام الداخلي', 'error');
    }
  };

  const handleCreateDraftEligibility = async () => {
    if (appSlug === 'jahez') return;
    try {
      const body = {
        period: selectedPeriod,
        data: {
          name: `ضوابط أهلية ${appSlug}`,
          rules: [
            {
              metric: 'orders',
              operator: 'gte',
              threshold: 10,
              effect: { type: 'zero' },
            },
          ],
        },
      };
      await api.createPayEligibility(appSlug, body, selectedPeriod);
      showToast('تم إنشاء مسودة ضوابط الأهلية بنجاح', 'success');
      loadEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر إنشاء مسودة ضوابط الأهلية', 'error');
    }
  };

  const handleApproveEligibility = async (elDoc: PayConfigDoc) => {
    try {
      await api.approvePayEligibility(appSlug, elDoc.id);
      showToast('تم اعتماد ضوابط الأهلية بنجاح', 'success');
      loadEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد ضوابط الأهلية', 'error');
    }
  };

  // Preview assignments import
  const handlePreviewAssignments = async () => {
    if (!assignmentFormId.trim()) {
      showToast('يرجى إدخال معرّف النموذج أو الحملة (Campaign ID)', 'warning');
      return;
    }
    setPreviewingAssignments(true);
    try {
      const payload = {
        period: selectedPeriod,
        sourceType: assignmentSourceType,
        formId: assignmentFormId.trim(),
        fieldId: assignmentFieldId.trim() || 'workSystem',
        mapping: { [assignmentMappingKey.trim() || 'commission']: assignmentMappingScheme.trim() },
      };
      const res = await api.previewPayAssignments(appSlug, payload);
      setAssignmentPreviewResult(res);
      showToast(`تمت معاينة خيارات ${res.total ?? res.rows?.length ?? 0} مندوب بنجاح`, 'success');
    } catch (err: any) {
      showToast(err.message || 'تعذر معاينة استيراد خيارات المناديب', 'error');
    } finally {
      setPreviewingAssignments(false);
    }
  };

  // Commit assignments import
  const handleCommitAssignments = async () => {
    if (!assignmentPreviewResult || !assignmentPreviewResult.canCommit) {
      showToast('المعاينة الحالية غير قابلة للتأكيد أو تحوي تعارضات', 'error');
      return;
    }
    setCommittingAssignments(true);
    try {
      const res = await api.commitPayAssignments(appSlug, {
        period: selectedPeriod,
        rows: assignmentPreviewResult.rows,
        fingerprint: assignmentPreviewResult.fingerprint,
        confirm: true,
      });
      showToast(`تم تثبيت خيارات ${res.imported ?? 0} مندوب بنجاح دون تعديل الردود الأصلية`, 'success');
      setAssignmentsImportModalOpen(false);
      setAssignmentPreviewResult(null);
      loadAssignments();
    } catch (err: any) {
      showToast(err.message || 'تعذر تثبيت خيارات المناديب', 'error');
    } finally {
      setCommittingAssignments(false);
    }
  };

  // Advance Cycle
  const handleAdvanceCycle = async (cycle: PayCycle) => {
    const nextStage = (cycle.stage || 0) + 1;
    if (nextStage > 7) {
      showToast('دورة الرواتب مكتملة ومعتمدة بالكامل في المرحلة 7', 'info');
      return;
    }
    setAdvancingCycle(true);
    try {
      await api.advancePayCycle(cycle.id, { stage: nextStage });
      showToast(`تم تقديم دورة الرواتب بنجاح إلى المرحلة ${nextStage}`, 'success');
      loadCycles();
    } catch (err: any) {
      showToast(err.message || `تعذر تقديم دورة الرواتب إلى المرحلة ${nextStage}`, 'error');
    } finally {
      setAdvancingCycle(false);
    }
  };

  // Create Global Settings Draft from Defaults
  const handleCreateGlobalSettingsDraft = async () => {
    try {
      await api.createGlobalPaySettings({
        effectiveFrom: `${rulesMonth}-01`,
        fromDefaults: true,
      });
      showToast('تم إنشاء مسودة الإعدادات العامة من القيم الافتراضية بنجاح', 'success');
      loadGlobalSettings();
    } catch (err: any) {
      showToast(err.message || 'تعذر إنشاء مسودة الإعدادات العامة', 'error');
    }
  };

  const handleApproveGlobalSettings = async (settingDoc: PayConfigDoc) => {
    try {
      await api.approveGlobalPaySettings(settingDoc.id);
      showToast('تم اعتماد الإعدادات العامة بنجاح وتثبيتها للقراءة فقط', 'success');
      loadGlobalSettings();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد الإعدادات العامة', 'error');
    }
  };

  // ========================================================
  // ACTIONS FOR LEGACY PRODUCTION PAYROLL RUN (F2)
  // ========================================================

  const handleApproveLegacyRun = async () => {
    if (!approvePreviewRow) return;
    if (!approveSponsorId) {
      showToast('يرجى تحديد السجل المالي صاحب المسير', 'warning');
      return;
    }
    setApprovingRun(true);
    try {
      await api.approveLegacyPayrollRun({
        iqama: approvePreviewRow.iqama,
        period: legacyPeriod,
        sponsorId: approveSponsorId,
        fingerprint: approvePreviewRow.fingerprint,
      });
      showToast(`تم اعتماد مسير المندوب ${approvePreviewRow.iqama} وترحيل قيوده المحاسبية بنجاح`, 'success');
      setApprovePreviewRow(null);
      setApproveSponsorId('');
      loadLegacyPayroll();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد المسير', 'error');
    } finally {
      setApprovingRun(false);
    }
  };

  const handleSaveAdjustment = async () => {
    if (!adjIqama || !adjAmount || !adjSourceRef.trim() || !adjDescription.trim()) {
      showToast('يرجى إكمال كافة حقول التحمل/المستحق المطلوبة', 'warning');
      return;
    }
    setSavingAdj(true);
    try {
      await api.createAdjustment({
        iqama: adjIqama,
        period: legacyPeriod,
        amount: parseFloat(adjAmount),
        sourceRef: adjSourceRef.trim(),
        category: adjCategory,
        description: adjDescription.trim(),
      });
      showToast('تم تسجيل البند وتثبيته في مسودة الشهر بنجاح', 'success');
      setAdjModalOpen(false);
      setAdjIqama('');
      setAdjAmount('');
      setAdjSourceRef('');
      setAdjDescription('');
      loadLegacyPayroll();
    } catch (err: any) {
      showToast(err.message || 'تعذر تسجيل البند', 'error');
    } finally {
      setSavingAdj(false);
    }
  };

  const handleSavePayment = async () => {
    if (!payIqama || !payAmount || !paySourceRef.trim()) {
      showToast('يرجى إكمال الحقول الإلزامية لصرف الدفعة', 'warning');
      return;
    }
    setSavingPay(true);
    try {
      await api.createPayment({
        iqama: payIqama,
        period: legacyPeriod,
        amount: parseFloat(payAmount),
        sourceRef: paySourceRef.trim(),
        sponsorId: paySponsorId,
        paymentVaultCode: payVaultCode,
        onBehalf: payOnBehalf,
        onBehalfNote: payOnBehalfNote.trim(),
      });
      showToast('تم تسجيل الدفعة النقدية وترحيلها محاسبياً بنجاح', 'success');
      setPayModalOpen(false);
      setPayIqama('');
      setPayAmount('');
      setPaySourceRef('');
      setPayOnBehalf(false);
      setPayOnBehalfNote('');
      loadLegacyPayroll();
    } catch (err: any) {
      showToast(err.message || 'تعذر تسجيل الدفعة', 'error');
    } finally {
      setSavingPay(false);
    }
  };

  const handleOpenLedger = async (iqama: string) => {
    setLedgerIqama(iqama);
    setLedgerModalOpen(true);
    setLoadingLedger(true);
    try {
      const res = await api.getLedger(iqama);
      setLedgerData(res);
    } catch (err: any) {
      showToast(err.message || 'تعذر تحميل دفتر حساب المندوب', 'error');
      setLedgerData(null);
    } finally {
      setLoadingLedger(false);
    }
  };

  // Section Tabs (Shared across Payroll)
  const sharedSectionTabs: SectionTabItem[] = [
    { id: 'platforms', label: 'منصات التشغيل والفواتير', icon: <Calculator size={16} /> },
    { id: 'ninja_upload', label: 'رفع فاتورة نينجا', icon: <FileSpreadsheet size={16} /> },
    { id: 'current-cycle', label: 'الدورة الحالية (النظام القائم)', icon: <Layers size={16} />, badge: legacyPreview.length },
    { id: 'cycle', label: 'دورة الرواتب والمسير (0-7)', icon: <RefreshCw size={16} /> },
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
    { num: 6, title: 'مراجعة واعتماد المسير', desc: 'مراجعة أسطر المسير واعتماد المناديب (BR §32) تمهيداً للاعتماد النهائي' },
    { num: 7, title: 'ملف الصرف البنكي والترحيل', desc: 'إصدار ملف CSV لصرف الرواتب وترحيل القيود اليومية للدليل المستهدف' },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الرواتب والمسيرات · منظومة المنصات الموحدة"
        eyebrow="الرواتب والتعويضات"
        description="إدارة ومراجعة فواتير المنصات، الدورة القائمة الفعالة للرواتب، محرر الأنظمة الداخلية، الاستقطاعات، واعتماد ملفات الصرف."
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
      {/* SECTION: NINJA INVOICE RECONCILE (BR §26, §31)           */}
      {/* ======================================================== */}
      {mainTab === 'ninja_upload' && (
        <div className="space-y-6">
          <NinjaInvoiceUploadScreen initialPeriod={selectedPeriod} />
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 0: CURRENT PRODUCTION PAYROLL RUN (F2)          */}
      {/* ======================================================== */}
      {mainTab === 'current-cycle' && (
        <div className="space-y-6">
          {/* Controls Bar */}
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
            <div className="flex flex-wrap items-center gap-3">
              <div>
                <label className="text-xs text-[var(--muted)] font-medium block mb-1">شهر المسير:</label>
                <input
                  type="month"
                  value={legacyPeriod}
                  onChange={(e) => setLegacyPeriod(e.target.value)}
                  className="p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm font-bold focus:outline-hidden"
                />
              </div>

              <div className="flex items-center gap-2 pt-5 sm:pt-0">
                <button
                  type="button"
                  onClick={() => setAdjModalOpen(true)}
                  className="px-3 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer"
                >
                  <Coins size={14} />
                  <span>إضافة تحمل/مستحق</span>
                </button>
                <button
                  type="button"
                  onClick={() => setPayModalOpen(true)}
                  className="px-3 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer"
                >
                  <DollarSign size={14} />
                  <span>تسجيل دفعة</span>
                </button>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <a
                href={`/api/payroll/export.csv?period=${encodeURIComponent(legacyPeriod)}`}
                download
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 shadow-xs"
              >
                <Download size={15} />
                <span>تصدير المعتمد (CSV)</span>
              </a>
            </div>
          </div>

          {/* Notice banner */}
          <div className="p-3.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] text-xs text-[var(--muted)] flex items-center gap-2">
            <AlertCircle size={15} className="text-[var(--accent)] shrink-0" />
            <span>
              الرصيد السابق + حركة الشهر = الرصيد الختامي. الدفعات المسجلة تظهر في دفتر المندوب، والتصدير تقرير مراجعة معتمد.
            </span>
          </div>

          {/* Summary Stat Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4">
            <StatCard
              label="إجمالي مناديب المسير"
              value={legacyPreview.length}
              hint={`شهر ${legacyPeriod}`}
              icon={Users}
            />
            <StatCard
              label="كشوف معتمدة ومرحلة"
              value={legacyRuns.filter((r) => r.period === legacyPeriod).length}
              hint="مرحلة محاسبياً"
              icon={CheckCircle2}
            />
            <StatCard
              label="كشوف جاهزة للاعتماد"
              value={
                legacyPreview.filter(
                  (p) =>
                    (!p.blockers || p.blockers.length === 0) &&
                    !legacyRuns.some((r) => r.iqama === p.iqama && r.period === legacyPeriod)
                ).length
              }
              hint="بلا موانع نظامية"
              icon={ShieldCheck}
            />
            <StatCard
              label="تحتاج مراجعة"
              value={legacyPreview.filter((p) => p.blockers && p.blockers.length > 0).length}
              hint="تحوي موانع احتساب"
              icon={AlertTriangle}
            />
          </div>

          {/* Payroll Preview Table */}
          {loadingLegacy ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : legacyError ? (
            <ErrorState
              title="تعذر تحميل مسير الرواتب القائم"
              message={legacyError}
              onRetry={loadLegacyPayroll}
            />
          ) : legacyPreview.length === 0 ? (
            <EmptyState
              title={`لا توجد بيانات مسير لشهر ${legacyPeriod}`}
              description="ارفع مصدر التشغيل والتسوية وحدد سياسة حساب المناديب لاحتساب المسودة."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">المندوب</th>
                      <th className="p-3 text-start">الطلبات</th>
                      <th className="p-3 text-start">السياسة</th>
                      <th className="p-3 text-start">رصيد سابق</th>
                      <th className="p-3 text-start">حركة الشهر</th>
                      <th className="p-3 text-start">الرصيد الختامي</th>
                      <th className="p-3 text-start">الموقف</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {legacyPreview.map((row) => {
                      const run = legacyRuns.find(
                        (x) => x.iqama === row.iqama && x.period === legacyPeriod
                      );
                      const displayRow = run?.result || row;
                      const hasBlockers = row.blockers && row.blockers.length > 0;
                      const rider = ridersList.find((y) => y.iqama === row.iqama);

                      return (
                        <tr key={row.iqama} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3">
                            <div className="font-bold text-[var(--ink)]">
                              {rider?.name || row.iqama}
                            </div>
                            <div className="font-mono text-[11px] text-[var(--muted)]">
                              {row.iqama}
                            </div>
                          </td>
                          <td className="p-3 num font-semibold">{displayRow.orders}</td>
                          <td className="p-3 text-xs text-[var(--muted)] max-w-[140px] truncate">
                            {displayRow.policyName || '-'}
                          </td>
                          <td className="p-3 num">
                            {(displayRow.openingBalance || 0).toLocaleString('en-US', {
                              minimumFractionDigits: 2,
                            })}{' '}
                            ر.س
                          </td>
                          <td className="p-3 num font-medium">
                            {(displayRow.periodMovement || 0).toLocaleString('en-US', {
                              minimumFractionDigits: 2,
                            })}{' '}
                            ر.س
                          </td>
                          <td
                            className={`p-3 num font-bold ${
                              (displayRow.closingBalance || 0) < 0
                                ? 'text-[var(--danger)]'
                                : 'text-[var(--money-pos)]'
                            }`}
                          >
                            {(displayRow.closingBalance || 0).toLocaleString('en-US', {
                              minimumFractionDigits: 2,
                            })}{' '}
                            ر.س
                          </td>
                          <td className="p-3">
                            {run ? (
                              <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                                معتمد
                              </span>
                            ) : hasBlockers ? (
                              <span
                                className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--warning)]/15 text-[var(--warning)] cursor-help"
                                title={row.blockers.join(' | ')}
                              >
                                بيانات تحتاج مراجعة
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/15 text-[var(--accent)]">
                                جاهز للمراجعة
                              </span>
                            )}
                          </td>
                          <td className="p-3">
                            <div className="flex items-center gap-1.5">
                              <button
                                type="button"
                                onClick={() => setDetailPreviewRow(row)}
                                className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
                              >
                                تفاصيل
                              </button>
                              {!run && (
                                <button
                                  type="button"
                                  disabled={hasBlockers}
                                  onClick={() => {
                                    setApprovePreviewRow(row);
                                    setApproveSponsorId(rider?.sponsorId || rider?.financeSponsorId || '');
                                  }}
                                  className="px-2.5 py-1 rounded-lg bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
                                >
                                  اعتماد
                                </button>
                              )}
                              <button
                                type="button"
                                onClick={() => handleOpenLedger(row.iqama)}
                                className="px-2.5 py-1 rounded-lg border border-[var(--line)] text-xs text-[var(--muted)] hover:bg-[var(--raised)] transition cursor-pointer"
                              >
                                الدفتر
                              </button>
                            </div>
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

          {/* Case A: HungerStation Settlements Tab */}
          {selectedPlatform === 'hungerstation' && appSubTab === 'settlements' && (
            <div className="space-y-6">
              {/* Stat Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
                <StatCard
                  label="إجمالي الصفحة الحالية"
                  value={`${totalInvoicesAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
                  hint={`فواتير شهر ${selectedPeriod}`}
                  icon={FileSpreadsheet}
                />
                <StatCard
                  label="نزاعات CAR المفتوحة"
                  value={openDisputesCount}
                  hint="بانتظار رد المنصة"
                  icon={ShieldAlert}
                />
                <StatCard
                  label="اقتطاعات TGA المعلقة"
                  value={pendingAllocationsCount}
                  hint="بانتظار التقسيم أو الاعتماد"
                  icon={Scale}
                />
                <StatCard
                  label="بوابة الترحيل المالي"
                  value={isPostingGateOn ? 'مفتوحة للترحيل' : 'مغلقة (بانتظار الاعتماد)'}
                  hint="مطابقة المعايير المحاسبية"
                  icon={CheckCircle2}
                />
              </div>

              {/* Sub-tab pills */}
              <div className="flex items-center gap-2 border-b border-[var(--line)] overflow-x-auto no-scrollbar">
                {[
                  { id: 'invoices', label: 'فواتير المنصة' },
                  { id: 'disputes', label: 'نزاعات CAR', badge: openDisputesCount },
                  { id: 'allocations', label: 'توزيعات TGA', badge: pendingAllocationsCount },
                  { id: 'facts', label: 'وقائع ERP' },
                  { id: 'proposed_journals', label: 'القيود المقترحة' },
                ].map((item) => (
                  <button
                    key={item.id}
                    id={`tab-${item.id}`}
                    type="button"
                    onClick={() => setActiveTab(item.id)}
                    className={`py-2 px-3 text-xs sm:text-sm font-semibold border-b-2 transition whitespace-nowrap ${
                      activeTab === item.id
                        ? 'border-[var(--accent)] text-[var(--accent)]'
                        : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                    }`}
                  >
                    <span>{item.label}</span>
                    {item.badge !== undefined && item.badge > 0 && (
                      <span className="ms-1.5 px-1.5 py-0.2 rounded-full text-[10px] bg-[var(--danger)]/15 text-[var(--danger)] font-bold">
                        {item.badge}
                      </span>
                    )}
                  </button>
                ))}
              </div>

              {/* HS Section Error Banner */}
              {hsSectionError && (
                <div className="p-4 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)] text-xs flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <AlertTriangle size={16} />
                    <span>{hsSectionError}</span>
                  </div>
                  <button
                    type="button"
                    onClick={loadData}
                    className="px-2.5 py-1 rounded-lg bg-[var(--danger)] text-white font-bold hover:opacity-90 cursor-pointer"
                  >
                    إعادة المحاولة
                  </button>
                </div>
              )}

              {/* Sub-tab 1: Invoices */}
              {activeTab === 'invoices' && (
                <div className="space-y-4">
                  {loading ? (
                    <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                      <Skeleton className="h-10 w-full" />
                      <Skeleton className="h-10 w-full" />
                    </div>
                  ) : invoices.length === 0 ? (
                    <EmptyState
                      title="لا توجد فواتير هنقرستيشن لهذا الشهر"
                      description="استخدم زر استيراد فواتير المنصة بالأعلى لرفع ملفات الفواتير المعتمدة."
                    />
                  ) : (
                    <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                      <div className="overflow-x-auto">
                        <table className="w-full text-start text-xs sm:text-sm">
                          <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                            <tr>
                              <th className="p-3 text-start">رقم الفاتورة</th>
                              <th className="p-3 text-start">الشهر</th>
                              <th className="p-3 text-start">المبلغ قبل الضريبة</th>
                              <th className="p-3 text-start">الضريبة</th>
                              <th className="p-3 text-start">الإجمالي</th>
                              <th className="p-3 text-start">الخصومات</th>
                              <th className="p-3 text-start">حالة الاعتماد</th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-[var(--line)]">
                            {invoices.map((inv) => (
                              <tr key={inv.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--ink)]">{(inv as any).invoiceNumber || inv.id}</td>
                                <td className="p-3 font-mono">{inv.period}</td>
                                <td className="p-3 num">
                                  {((inv as any).amountExclVat || (inv.totalAmount || 0) - (inv.vat || 0)).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 num text-[var(--muted)]">
                                  {((inv as any).vatAmount || inv.vat || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 num font-bold text-[var(--accent)]">
                                  {(inv.totalAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 num text-[var(--danger)]">
                                  {((inv as any).deductionsTotal || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3">
                                  <span
                                    className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                                      inv.status === 'approved' || inv.status === 'posted'
                                        ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                        : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                    }`}
                                  >
                                    {inv.status === 'approved' ? 'معتمدة' : inv.status === 'posted' ? 'مرحلة' : 'مسودة'}
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

              {/* Sub-tab 2: Disputes */}
              {activeTab === 'disputes' && (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <th className="p-3 text-start">رقم النزاع</th>
                            <th className="p-3 text-start">المبلغ</th>
                            <th className="p-3 text-start">سبب الاعتراض</th>
                            <th className="p-3 text-start">الحالة والمسار المحاسبي</th>
                            <th className="p-3 text-start">الإجراء</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {disputes.length === 0 ? (
                            <tr>
                              <td colSpan={5} className="p-6 text-center text-[var(--muted)] text-xs">
                                لا توجد نزاعات CAR مفتوحة لهذا الشهر.
                              </td>
                            </tr>
                          ) : (
                            disputes.map((d) => (
                              <tr key={d.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--ink)]">{d.id}</td>
                                <td className="p-3 num font-bold text-[var(--danger)]">
                                  {(d.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                                </td>
                                <td className="p-3 text-xs text-[var(--muted)]">{d.reason || '-'}</td>
                                <td className="p-3">
                                  <span
                                    className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                                      d.status === 'escalated' || (d.status === 'resolved' && (d.outcome === 'platform_rejected' || (d as any).decision === 'platform_rejected'))
                                        ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                        : d.status === 'resolved'
                                        ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                        : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                    }`}
                                  >
                                    {d.status === 'escalated' || (d.status === 'resolved' && (d.outcome === 'platform_rejected' || (d as any).decision === 'platform_rejected'))
                                      ? 'مرحّل لحساب النزاعات 112803 لحين فض النزاع'
                                      : d.status === 'resolved'
                                      ? 'مردود بإشعار دائن من المنصة'
                                      : 'مفتوح'}
                                  </span>
                                </td>
                                <td className="p-3">
                                  {d.status === 'open' && (
                                    <button
                                      type="button"
                                      onClick={() => setSelectedDispute(d)}
                                      className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition cursor-pointer"
                                    >
                                      معالجة النزاع
                                    </button>
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
                            <th className="p-3 text-start">السطر / المعرف</th>
                            <th className="p-3 text-start">المبلغ الإجمالي</th>
                            <th className="p-3 text-start">نصيب الشركة</th>
                            <th className="p-3 text-start">نصيب المناديب</th>
                            <th className="p-3 text-start">الموقف</th>
                            <th className="p-3 text-start">مقدم المقترح</th>
                            <th className="p-3 text-start">الإجراءات</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {allocations.length === 0 ? (
                            <tr>
                              <td colSpan={7} className="p-6 text-center text-[var(--muted)] text-xs">
                                لا توجد اقتطاعات TGA مسجلة لهذا الشهر.
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
                                    {(alloc.riderAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
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

          {/* Case B: Ninja Settlements Tab (BR §26, §31) */}
          {selectedPlatform === 'ninja' && appSubTab === 'settlements' && (
            <div className="space-y-6">
              <NinjaInvoiceUploadScreen initialPeriod={selectedPeriod} />
            </div>
          )}

          {/* Fallback for other platforms settlements */}
          {selectedPlatform !== 'hungerstation' && selectedPlatform !== 'ninja' && appSubTab === 'settlements' && (
            <div className="p-8 text-center rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
              تسويات وفواتير منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr} قيد التطوير والتفعيل.
            </div>
          )}

          {/* ======================================================== */}
          {/* PARENT TERMS SUB-TAB (F1)                                */}
          {/* ======================================================== */}
          {appSubTab === 'parent_terms' && (
            <div className="space-y-6">
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                <div>
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    الشروط الأصلية (Parent Company Terms) · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-1">
                    إدارة نموذج الفوترة والتعاقد الأصلي مع المنصة. تخضع لاعتماد مستخدم ثانٍ مستقل (Maker-Checker).
                  </p>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    disabled={copyingMonth}
                    onClick={() => handleCopyMonth(appSlug)}
                    className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                  >
                    <Copy size={14} />
                    <span>انسخ الشهر السابق</span>
                  </button>
                  <button
                    type="button"
                    onClick={handleCreateDraftTerms}
                    className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                  >
                    + إنشاء مسودة جديدة
                  </button>
                </div>
              </div>

              {loadingTerms ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : termsError ? (
                <ErrorState title="تعذر تحميل الشروط" message={termsError} onRetry={loadTerms} />
              ) : termsList.length === 0 ? (
                <EmptyState
                  title="لا توجد إعدادات لهذا الشهر — انسخ الشهر السابق أو أنشئ مسودة"
                  description="لم يتم العثور على شروط تعاقد مسجلة لهذا التطبيق في الشهر المحدد. يمكنك نسخ إعدادات الشهر السابق بضغطة زر أو إنشاء مسودة جديدة."
                  action={{
                    label: 'انسخ الشهر السابق الآن',
                    onClick: () => handleCopyMonth(appSlug),
                  }}
                />
              ) : (
                <div className="space-y-4">
                  {termsList.map((t) => {
                    const isApproved = t.status === 'approved';
                    return (
                      <div
                        key={t.id}
                        className={`p-5 rounded-2xl border transition shadow-xs space-y-4 ${
                          isApproved
                            ? 'border-[var(--line)] bg-[var(--surface)]'
                            : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                        }`}
                      >
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div className="flex items-center gap-2">
                            <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                              {t.data?.name || 'شروط التعاقد الأصلية'} (إصدار #{t.version})
                            </h4>
                            <span
                              className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                                isApproved
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              }`}
                            >
                              {isApproved ? `معتمد من @${t.approvedBy}` : 'مسودة قيد المراجعة'}
                            </span>
                            {isApproved && (
                              <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                                <Lock size={12} />
                                <span>للقراءة فقط</span>
                              </span>
                            )}
                          </div>

                          {!isApproved && (
                            <button
                              type="button"
                              onClick={() => handleApproveTerms(t)}
                              className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
                            >
                              اعتماد الشروط (Maker-Checker)
                            </button>
                          )}
                        </div>

                        {/* Terms parameters card */}
                        <div className="p-4 rounded-xl bg-[var(--surface)] border border-[var(--line)] text-xs space-y-2">
                          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                            <div>
                              <span className="text-[var(--muted)] block">نموذج الفوترة:</span>
                              <span className="font-bold text-[var(--ink)] font-mono">
                                {t.data?.billingModel || 'per_order'}
                              </span>
                            </div>
                            <div>
                              <span className="text-[var(--muted)] block">تاريخ الإنشاء:</span>
                              <span className="font-mono">{t.createdAt?.slice(0, 10) || '-'}</span>
                            </div>
                            <div>
                              <span className="text-[var(--muted)] block">بواسطة:</span>
                              <span className="font-mono">@{t.createdBy || 'system'}</span>
                            </div>
                            <div>
                              <span className="text-[var(--muted)] block">حساب تكلفة الشركة:</span>
                              <span className="font-mono">{t.data?.parameters?.companyCostAccount || '-'}</span>
                            </div>
                          </div>

                          <div className="pt-2 border-t border-[var(--line)]">
                            <span className="text-[var(--muted)] block mb-1">المحددات والشرائح:</span>
                            <pre className="p-2.5 rounded-lg bg-[var(--raised)] font-mono text-[11px] overflow-x-auto text-left" dir="ltr">
                              {JSON.stringify(t.data?.parameters || {}, null, 2)}
                            </pre>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* ======================================================== */}
          {/* INTERNAL SCHEMES SUB-TAB (F1)                            */}
          {/* ======================================================== */}
          {appSubTab === 'internal_schemes' && (
            <div className="space-y-6">
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                <div>
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    الأنظمة الداخلية (Internal Schemes Level 3) · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-1">
                    تحديد مصفوفة الفئات الست الصريحة (كفالة × مركبة) ونوع النظام ودور المحاسبة (أصيل / وكيل).
                  </p>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    disabled={copyingMonth}
                    onClick={() => handleCopyMonth(appSlug)}
                    className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                  >
                    <Copy size={14} />
                    <span>انسخ الشهر السابق</span>
                  </button>
                  <button
                    type="button"
                    onClick={handleCreateDraftScheme}
                    className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                  >
                    + إنشاء مسودة نظام
                  </button>
                </div>
              </div>

              {loadingSchemes ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : schemesError ? (
                <ErrorState title="تعذر تحميل الأنظمة" message={schemesError} onRetry={loadSchemes} />
              ) : schemesList.length === 0 ? (
                <EmptyState
                  title="لا توجد إعدادات لهذا الشهر — انسخ الشهر السابق أو أنشئ مسودة"
                  description="لم يتم العثور على أنظمة داخلية مسجلة لهذا التطبيق في الشهر المحدد. انسخ الشهر السابق أو أنشئ مسودة جديدة."
                  action={{
                    label: 'انسخ الشهر السابق الآن',
                    onClick: () => handleCopyMonth(appSlug),
                  }}
                />
              ) : (
                <div className="space-y-4">
                  {schemesList.map((sc) => {
                    const isApproved = sc.status === 'approved';
                    const categories = sc.data?.parameters?.categories || {};
                    return (
                      <div
                        key={sc.id}
                        className={`p-5 rounded-2xl border transition shadow-xs space-y-4 ${
                          isApproved
                            ? 'border-[var(--line)] bg-[var(--surface)]'
                            : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                        }`}
                      >
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div className="flex items-center gap-2">
                            <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                              {sc.data?.name || 'النظام الداخلي المعتمد'} (إصدار #{sc.version})
                            </h4>
                            <span
                              className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                                isApproved
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              }`}
                            >
                              {isApproved ? `معتمد من @${sc.approvedBy}` : 'مسودة قيد المراجعة'}
                            </span>
                            <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--raised)] font-mono">
                              صفة: {sc.data?.accountingRole === 'principal' ? 'أصيل (Principal)' : 'وكيل (Agent)'}
                            </span>
                            {isApproved && (
                              <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                                <Lock size={12} />
                                <span>للقراءة فقط</span>
                              </span>
                            )}
                          </div>

                          {!isApproved && (
                            <button
                              type="button"
                              onClick={() => handleApproveScheme(sc)}
                              className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
                            >
                              اعتماد النظام الداخلي (Maker-Checker)
                            </button>
                          )}
                        </div>

                        {/* 6 Category cells table */}
                        <div className="overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                          <table className="w-full text-start text-xs">
                            <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                              <tr>
                                <th className="p-2.5 text-start">الفئة التشغيلية (الكفالة × المركبة)</th>
                                <th className="p-2.5 text-start">الحالة</th>
                                <th className="p-2.5 text-start">العمولة اليومية</th>
                                <th className="p-2.5 text-start">العمولة الشهرية</th>
                                <th className="p-2.5 text-start">نسبة الشركة</th>
                                <th className="p-2.5 text-start">مركبة الشركة</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--line)]">
                              {Object.entries(categories).map(([catKey, cell]: [string, any]) => (
                                <tr key={catKey}>
                                  <td className="p-2.5 font-bold font-mono">{catKey}</td>
                                  <td className="p-2.5">
                                    <span
                                      className={`px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                                        cell.enabled
                                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                          : 'bg-[var(--muted)]/15 text-[var(--muted)]'
                                      }`}
                                    >
                                      {cell.enabled ? 'مفعل' : 'معطل'}
                                    </span>
                                  </td>
                                  <td className="p-2.5 num font-mono">
                                    {cell.dailyCommission ? `${cell.dailyCommission} ر.س` : '-'}
                                  </td>
                                  <td className="p-2.5 num font-mono">
                                    {cell.monthlyCommission ? `${cell.monthlyCommission} ر.س` : '-'}
                                  </td>
                                  <td className="p-2.5 num font-mono">
                                    {cell.companyPercent ? `${cell.companyPercent}%` : '-'}
                                  </td>
                                  <td className="p-2.5 font-mono">
                                    {cell.chargeCompanyVehicle ? 'نعم (يُخصم)' : 'لا'}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}

          {/* ======================================================== */}
          {/* ELIGIBILITY RULES SUB-TAB (F1)                           */}
          {/* ======================================================== */}
          {appSubTab === 'eligibility' && (
            <div className="space-y-6">
              {appSlug === 'jahez' ? (
                <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-2">
                  <div className="h-10 w-10 rounded-full bg-[var(--accent)]/15 text-[var(--accent)] flex items-center justify-center mx-auto font-bold">
                    i
                  </div>
                  <h4 className="font-bold text-sm text-[var(--ink)]">
                    جاهز بلا شروط أهلية وله دورة تصفيات منفصلة
                  </h4>
                  <p className="text-xs text-[var(--muted)] max-w-md mx-auto">
                    وفق ضوابط نموذج العمل BR §24، تصفيات جاهز مستقلة محاسبياً وتتبع الدورة الميدانية المنفصلة.
                  </p>
                </div>
              ) : (
                <div className="space-y-6">
                  <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                    <div>
                      <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                        شروط وضوابط الأهلية (Eligibility Rules Level 5) · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                      </h3>
                      <p className="text-xs text-[var(--muted)] mt-1">
                        شروط الحد الأدنى وساعات العمل اليومية وتارجت الطلبات المرتبطة بالتطبيق حصرياً.
                      </p>
                    </div>

                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        disabled={copyingMonth}
                        onClick={() => handleCopyMonth(appSlug)}
                        className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                      >
                        <Copy size={14} />
                        <span>انسخ الشهر السابق</span>
                      </button>
                      <button
                        type="button"
                        onClick={handleCreateDraftEligibility}
                        className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                      >
                        + إنشاء مسودة أهلية
                      </button>
                    </div>
                  </div>

                  {loadingEligibility ? (
                    <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                      <Skeleton className="h-10 w-full" />
                      <Skeleton className="h-10 w-full" />
                    </div>
                  ) : eligibilityError ? (
                    <ErrorState title="تعذر تحميل ضوابط الأهلية" message={eligibilityError} onRetry={loadEligibility} />
                  ) : eligibilityList.length === 0 ? (
                    <EmptyState
                      title="لا توجد إعدادات لهذا الشهر — انسخ الشهر السابق أو أنشئ مسودة"
                      description="لم يتم العثور على ضوابط أهلية مسجلة لهذا التطبيق في الشهر المحدد. انسخ الشهر السابق أو أنشئ مسودة جديدة."
                      action={{
                        label: 'انسخ الشهر السابق الآن',
                        onClick: () => handleCopyMonth(appSlug),
                      }}
                    />
                  ) : (
                    <div className="space-y-4">
                      {eligibilityList.map((el) => {
                        const isApproved = el.status === 'approved';
                        const rules = el.data?.rules || [];
                        return (
                          <div
                            key={el.id}
                            className={`p-5 rounded-2xl border transition shadow-xs space-y-4 ${
                              isApproved
                                ? 'border-[var(--line)] bg-[var(--surface)]'
                                : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                            }`}
                          >
                            <div className="flex flex-wrap items-center justify-between gap-3">
                              <div className="flex items-center gap-2">
                                <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                                  {el.data?.name || 'قواعد الأهلية'} (إصدار #{el.version})
                                </h4>
                                <span
                                  className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                                    isApproved
                                      ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                      : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                  }`}
                                >
                                  {isApproved ? `معتمد من @${el.approvedBy}` : 'مسودة قيد المراجعة'}
                                </span>
                                {isApproved && (
                                  <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                                    <Lock size={12} />
                                    <span>للقراءة فقط</span>
                                  </span>
                                )}
                              </div>

                              {!isApproved && (
                                <button
                                  type="button"
                                  onClick={() => handleApproveEligibility(el)}
                                  className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
                                >
                                  اعتماد الأهلية (Maker-Checker)
                                </button>
                              )}
                            </div>

                            {/* Rules table */}
                            <div className="overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                              <table className="w-full text-start text-xs">
                                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                                  <tr>
                                    <th className="p-2.5 text-start">المعيار (Metric)</th>
                                    <th className="p-2.5 text-start">عامل المقارنة</th>
                                    <th className="p-2.5 text-start">الحد الأدنى (Threshold)</th>
                                    <th className="p-2.5 text-start">الأثر عند عدم التحقق</th>
                                  </tr>
                                </thead>
                                <tbody className="divide-y divide-[var(--line)]">
                                  {rules.map((r: any, idx: number) => (
                                    <tr key={idx}>
                                      <td className="p-2.5 font-bold font-mono">{r.metric}</td>
                                      <td className="p-2.5 font-mono">{r.operator}</td>
                                      <td className="p-2.5 num font-bold">{r.threshold}</td>
                                      <td className="p-2.5 text-[var(--danger)] font-mono">
                                        {r.effect?.type || 'zero'}
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ======================================================== */}
          {/* SCHEME CHOICES & ASSIGNMENTS SUB-TAB (F1 / R4.3)         */}
          {/* ======================================================== */}
          {appSubTab === 'accounts' && (
            <div className="space-y-6">
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                <div>
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    ربط الحسابات وتكليفات الأنظمة (Assignments R4.3) · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-1">
                    توزيع خيارات الأنظمة المعتمدة للمناديب بناءً على ردود النماذج والاستبيانات عبر تدفق المعاينة والتأكيد.
                  </p>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      setAssignmentsImportModalOpen(true);
                      setAssignmentPreviewResult(null);
                    }}
                    className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
                  >
                    <ArrowRightLeft size={15} />
                    <span>استيراد وتوزيع خيارات المناديب (R4.3)</span>
                  </button>
                </div>
              </div>

              {loadingAssignments ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : assignmentsError ? (
                <ErrorState title="تعذر تحميل التكليفات" message={assignmentsError} onRetry={loadAssignments} />
              ) : assignmentsList.length === 0 ? (
                <EmptyState
                  title={`لا توجد خيارات أنظمة مسجلة لمنصة ${selectedPlatform} لشهر ${selectedPeriod}`}
                  description="استخدم زر «استيراد وتوزيع خيارات المناديب» بالأعلى لربط ردود النماذج بالأنظمة المعتمدة."
                  action={{
                    label: 'فتح شاشة استيراد الخيارات',
                    onClick: () => {
                      setAssignmentsImportModalOpen(true);
                      setAssignmentPreviewResult(null);
                    },
                  }}
                />
              ) : (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="overflow-x-auto">
                    <table className="w-full text-start text-xs sm:text-sm">
                      <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                        <tr>
                          <th className="p-3 text-start">إقامة المندوب</th>
                          <th className="p-3 text-start">اسم المندوب</th>
                          <th className="p-3 text-start">معرف النظام (Scheme ID)</th>
                          <th className="p-3 text-start">الشهر</th>
                          <th className="p-3 text-start">الحالة</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {assignmentsList.map((a: any) => (
                          <tr key={a.id || a.iqama} className="hover:bg-[var(--row-hover)] transition">
                            <td className="p-3 font-mono font-bold text-[var(--ink)]">{a.iqama || a.riderId}</td>
                            <td className="p-3 font-semibold">{a.riderName || '-'}</td>
                            <td className="p-3 font-mono text-xs text-[var(--accent)]">{a.schemeId || '-'}</td>
                            <td className="p-3 font-mono">{a.period}</td>
                            <td className="p-3">
                              <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                                معتمد
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
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 2: PAYROLL CYCLE STEPPER (0-7) (F1)             */}
      {/* ======================================================== */}
      {mainTab === 'cycle' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    دورة الرواتب الشهرية والمسير المعتمد (المراحل 0-7)
                  </h3>
                  {activeCycle && (
                    <span
                      className={`px-3 py-1 rounded-full text-xs font-bold flex items-center gap-1.5 ${
                        activeCycle.status === 'needs_decision'
                          ? 'bg-[var(--danger)]/15 text-[var(--danger)] border border-[var(--danger)]/30'
                          : activeCycle.status === 'approved'
                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                          : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                      }`}
                    >
                      {activeCycle.status === 'needs_decision' ? (
                        <>
                          <AlertTriangle size={13} />
                          <span>يحتاج قرار (§22.9.2)</span>
                        </>
                      ) : activeCycle.status === 'approved' ? (
                        'معتمد بالكامل'
                      ) : (
                        `قيد المراجعة (مرحلة ${activeCycle.stage})`
                      )}
                    </span>
                  )}
                </div>
                <p className="text-xs sm:text-sm text-[var(--muted)] mt-1">
                  مراحل احتساب الرواتب والمسيرات وفق نموذج العمل المالي المعتمد في BR §24.7
                </p>
              </div>

              <div className="flex items-center gap-2">
                <input
                  type="month"
                  value={selectedPeriod}
                  onChange={(e) => setSelectedPeriod(e.target.value)}
                  className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
                />
                {activeCycle && activeCycle.stage < 7 && activeCycle.status !== 'approved' && (
                  <button
                    type="button"
                    disabled={advancingCycle || activeCycle.status === 'needs_decision'}
                    onClick={() => handleAdvanceCycle(activeCycle)}
                    className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer flex items-center gap-1.5 shadow-xs"
                  >
                    {advancingCycle ? (
                      <>
                        <RefreshCw size={14} className="animate-spin" />
                        <span>جاري التقديم...</span>
                      </>
                    ) : (
                      <>
                        <span>تقديم المرحلة إلى [{activeCycle.stage + 1}: {cycleSteps[activeCycle.stage + 1]?.title}]</span>
                        <ChevronRight size={16} />
                      </>
                    )}
                  </button>
                )}
              </div>
            </div>

            {/* Stepper Navigation */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mt-6">
              {cycleSteps.map((st) => {
                const currentStage = activeCycle?.stage ?? 0;
                const isCurrent = currentStage === st.num;
                const isPast = currentStage > st.num;
                return (
                  <div
                    key={st.num}
                    className={`p-3.5 rounded-xl border transition-all ${
                      isCurrent
                        ? 'border-[var(--accent)] bg-[var(--accent)]/5 shadow-xs'
                        : isPast
                        ? 'border-[var(--money-pos)]/40 bg-[var(--money-pos)]/5'
                        : 'border-[var(--line)] bg-[var(--surface)] opacity-75'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span
                        className={`h-6 w-6 rounded-full flex items-center justify-center font-bold text-xs ${
                          isCurrent
                            ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                            : isPast
                            ? 'bg-[var(--money-pos)] text-white'
                            : 'bg-[var(--raised)] text-[var(--muted)]'
                        }`}
                      >
                        {isPast ? '✓' : st.num}
                      </span>
                      {st.num === 5 && activeCycle?.status === 'needs_decision' && (
                        <span className="px-1.5 py-0.2 rounded-full text-[9px] font-bold bg-[var(--danger)]/20 text-[var(--danger)]">
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

          {/* Cycle Deduction Phases Flow (BR §29) */}
          {activeCycle && (activeCycle.riders || []).length > 0 && (() => {
            const riders = activeCycle.riders || [];
            const gross = riders.reduce((s: number, r: any) => s + (r.gross || 0), 0);
            const afterCommission = riders.reduce((s: number, r: any) => s + (r.afterCommission || 0), 0);
            const phase1 = riders.reduce((s: number, r: any) => s + (r.phase1 || 0), 0);
            const preliminaryDue = riders.reduce((s: number, r: any) => s + (r.preliminaryDue || 0), 0);
            const phase2 = riders.reduce((s: number, r: any) => s + (r.phase2 || 0), 0);
            const phase3 = riders.reduce((s: number, r: any) => s + (r.phase3 || 0), 0);
            const net = riders.reduce((s: number, r: any) => s + (r.net || 0), 0);

            return (
              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                <div className="flex items-center justify-between border-b border-[var(--line)] pb-2">
                  <div>
                    <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                      مراحل احتساب الاستقطاعات الإجمالية للدورة (BR §29)
                    </h4>
                    <p className="text-[11px] text-[var(--muted)] mt-0.5">
                      التدرج المالي لاحتساب المسير: الإجمالي → بعد العمولة → المرحلة 1 → المستحق الأولي → المرحلة 2 → المرحلة 3 → الصافي.
                    </p>
                  </div>
                  <span className="text-xs font-mono font-bold text-[var(--muted)]">
                    {riders.length} مندوب محتسب
                  </span>
                </div>

                <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2 text-xs">
                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">الإجمالي (Gross)</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {gross.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">بعد العمولة</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {afterCommission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 1 (تشغيلي)</span>
                    <span className="num font-mono font-bold text-[var(--danger)]">
                      {phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/40">
                    <span className="text-[10px] font-bold text-[var(--accent)] block">المستحق الأولي</span>
                    <span className="num font-mono font-black text-[var(--accent)]">
                      {preliminaryDue.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 2 (مديونيات)</span>
                    <span className="num font-mono font-bold text-[var(--danger)]">
                      {phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 3 (تسويات)</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {phase3.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/40">
                    <span className="text-[10px] font-bold text-[var(--money-pos)] block">الصافي النهائي</span>
                    <span className="num font-mono font-black text-[var(--money-pos)]">
                      {net.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </span>
                  </div>
                </div>
              </div>
            );
          })()}

          {/* If cycle status is needs_decision, display warning banner */}
          {activeCycle?.status === 'needs_decision' && (
            <div className="p-4 rounded-2xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-xs text-[var(--danger)] flex items-start gap-3">
              <AlertTriangle size={18} className="shrink-0 mt-0.5" />
              <div>
                <div className="font-bold text-sm">تنبيه نظامي: دورة الرواتب تحمل شارة «يحتاج قرار» (§22.9.2)</div>
                <p className="mt-1 leading-relaxed opacity-90">
                  توجد بنود بصافي سالب أو موانع استحقاق تمنع استمرار التقديم الآلي للدورة دون معالجة صريحة من الإدارة المالية. يرفض الخادم الانتقال للمراحل اللاحقة حتى تسوية الموانع.
                </p>
              </div>
            </div>
          )}

          {/* Stage 6: Payroll Review Grid (BR §32) */}
          {activeCycle && activeCycle.stage >= 6 && (
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                    مراجعة واعتماد المسير (BR §32)
                  </h4>
                  <p className="text-xs text-[var(--muted)] mt-0.5">
                    جدول مراجعة استحقاقات المناديب وتعديل الأقساط وتسويات الراتب قبل الترحيل والصرف.
                  </p>
                </div>
              </div>
              <PayrollReviewGrid
                cycleId={activeCycle.id}
                onCycleUpdated={() => {
                  loadCycles();
                  if (activeCycle) loadCycleLines(activeCycle.id);
                }}
              />
            </div>
          )}

          {/* Cycle Lines & Explain per Rider */}
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  كشوف احتساب المناديب وشرح المعادلات (Explain Lines)
                </h4>
                <p className="text-xs text-[var(--muted)] mt-0.5">
                  تفاصيل احتساب كل مندوب مع أثر كل بند وصافي الاستحقاق.
                </p>
              </div>
              <span className="text-xs text-[var(--muted)]">
                {cycleLines.length} مندوب محتسب
              </span>
            </div>

            <DataTable
              tableId="pay-cycle-lines"
              columns={cycleLineColumns}
              data={cycleLines}
              loading={loadingLines}
              emptyTitle="لا توجد أسطر احتساب منشأة"
              emptyDescription="لا توجد أسطر احتساب منشأة لهذه الدورة بعد. سيتم بناء الأسطر في المرحلة 4."
              exportFilename={`cycle_lines_${activeCycle?.id || 'payroll'}`}
              showTotals={true}
            />
          </div>
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 3: RULES EDITOR & GLOBAL SETTINGS (F1)          */}
      {/* ======================================================== */}
      {mainTab === 'rules' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                  محرر الأنظمة الداخلية والإعدادات العامة الفعالة
                </h3>
                {globalSettings.length > 0 && (
                  <span
                    className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                      globalSettings[0].status === 'approved'
                        ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                        : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                    }`}
                  >
                    {globalSettings[0].status === 'approved'
                      ? `معتمد للقراءة فقط (@${globalSettings[0].approvedBy})`
                      : 'مسودة قابلة للتعديل'}
                  </span>
                )}
              </div>
              <p className="text-xs text-[var(--muted)] mt-1">
                الإعدادات العامة (الأيام، موعد القطع، وإيجارات المركبات اليومية) خاضعة لتواريخ السريان والاعتماد المستقل.
              </p>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="month"
                value={rulesMonth}
                onChange={(e) => setRulesMonth(e.target.value)}
                className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
              />
              <button
                type="button"
                disabled={copyingMonth}
                onClick={() => handleCopyMonth(appSlug)}
                className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1 cursor-pointer disabled:opacity-50"
              >
                <Copy size={13} />
                <span>انسخ الشهر السابق</span>
              </button>
            </div>
          </div>

          {loadingGlobalSettings ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : globalSettingsError ? (
            <ErrorState title="تعذر تحميل الإعدادات" message={globalSettingsError} onRetry={loadGlobalSettings} />
          ) : globalSettings.length === 0 ? (
            <EmptyState
              title="لا توجد إعدادات لهذا الشهر — انسخ الشهر السابق أو أنشئ مسودة"
              description="لم يتم العثور على إعدادات عامة معتمدة. يمكنك تهيئة الإعدادات العامة كمسودة من القيم القياسية بنقرة زر."
              action={{
                label: 'إنشاء مسودة من القيم الافتراضية',
                onClick: handleCreateGlobalSettingsDraft,
              }}
            />
          ) : (
            <div className="space-y-6">
              {/* Latest Setting Version Card */}
              {(() => {
                const setting = globalSettings[0];
                const isApproved = setting.status === 'approved';
                const v = setting.data?.values || {};
                const rent = v.vehicleRent || {};
                const acc = v.accounts || {};

                return (
                  <div
                    className={`p-5 rounded-2xl border transition shadow-xs space-y-4 ${
                      isApproved
                        ? 'border-[var(--line)] bg-[var(--surface)]'
                        : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                    }`}
                  >
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div className="flex items-center gap-2">
                        <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                          الإعدادات العامة الفعالة (إصدار #{setting.version}) · سريان من: {setting.effectiveFrom || '-'}
                        </h4>
                        {isApproved && (
                          <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                            <Lock size={12} />
                            <span>للقراءة فقط</span>
                          </span>
                        )}
                      </div>

                      {!isApproved && (
                        <button
                          type="button"
                          onClick={() => handleApproveGlobalSettings(setting)}
                          className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
                        >
                          اعتماد الإعدادات العامة (Maker-Checker)
                        </button>
                      )}
                    </div>

                    <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs pt-2">
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">أيام الشهر القياسية:</span>
                        <span className="font-bold text-[var(--ink)] num">{v.standardDays || 30} يوماً</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">أيام شهر فبراير:</span>
                        <span className="font-bold text-[var(--ink)] num">{v.februaryDays || 28} يوماً</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">موعد قطع اليوم:</span>
                        <span className="font-mono font-bold">{v.billingCutoff || '00:00'}</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">النطاق الزمني:</span>
                        <span className="font-mono font-bold">{v.timeZone || 'Asia/Riyadh'}</span>
                      </div>
                    </div>

                    {/* 6-Column Category Matrix */}
                    <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                      <div className="p-4 border-b border-[var(--line)]">
                        <h4 className="font-bold text-sm text-[var(--ink)]">
                          مصفوفة الفئات (الكفالة × المركبة) · شبكة الـ 6 أعمدة المعتمدة
                        </h4>
                        <p className="text-xs text-[var(--muted)] mt-0.5">
                          تحديد تكلفة إيجار المركبة اليومي (سيارة {rent.company_car || 60} ر.س / دباب {rent.company_bike || 26.67} ر.س / خاص 0 ر.س) وحسابات التوجيه.
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
                              <td className="p-3 num font-bold text-[var(--accent)]">{rent.company_car || '60.00'} ر.س</td>
                              <td className="p-3 num font-bold text-[var(--accent)]">{rent.company_bike || '26.67'} ر.س</td>
                              <td className="p-3 num text-[var(--muted)]">0.00 ر.س</td>
                              <td className="p-3 num font-bold text-[var(--accent)]">{rent.company_car || '60.00'} ر.س</td>
                              <td className="p-3 num font-bold text-[var(--accent)]">{rent.company_bike || '26.67'} ر.س</td>
                              <td className="p-3 num text-[var(--muted)]">0.00 ر.س</td>
                            </tr>
                            <tr>
                              <td className="p-3 font-semibold">حساب إيراد الإيجار</td>
                              <td className="p-3 font-mono text-xs">{acc.vehicleIncomeCar || '411102'}</td>
                              <td className="p-3 font-mono text-xs">{acc.vehicleIncomeBike || '411102'}</td>
                              <td className="p-3 text-[var(--muted)]">-</td>
                              <td className="p-3 font-mono text-xs">{acc.vehicleIncomeCar || '411102'}</td>
                              <td className="p-3 font-mono text-xs">{acc.vehicleIncomeBike || '411102'}</td>
                              <td className="p-3 text-[var(--muted)]">-</td>
                            </tr>
                          </tbody>
                        </table>
                      </div>
                    </div>
                  </div>
                );
              })()}
            </div>
          )}
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
      {/* SECTION 6: POLICIES (LEGACY)                            */}
      {/* ======================================================== */}
      {mainTab === 'policies' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex justify-between items-center">
            <div>
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                سياسات الاحتساب السابقة
              </h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                سجل السياسات التاريخية السابقة قبل الانتقال لمنظومة المنصات الموحدة R4.
              </p>
            </div>
          </div>

          {loadingPolicies ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : policies.length === 0 ? (
            <EmptyState
              title="لا توجد سياسات سابقة مسجلة"
              description="كافة السياسات يتم إدارتها حالياً عبر محرر الأنظمة وقواعد المنصات R4."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">اسم السياسة</th>
                      <th className="p-3 text-start">النوع</th>
                      <th className="p-3 text-start">المنصة</th>
                      <th className="p-3 text-start">تاريخ السريان</th>
                      <th className="p-3 text-start">الحالة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {policies.map((p) => (
                      <tr key={p.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-bold text-[var(--ink)]">{p.name}</td>
                        <td className="p-3 text-xs text-[var(--muted)]">{p.kind}</td>
                        <td className="p-3">{p.platform}</td>
                        <td className="p-3 font-mono text-xs">{p.effectiveFrom}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                            معتمدة
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
      {/* DIALOG: ASSIGNMENTS IMPORT (R4.3 PREVIEW -> COMMIT)       */}
      {/* ======================================================== */}
      <Dialog
        open={assignmentsImportModalOpen}
        onClose={() => setAssignmentsImportModalOpen(false)}
        title="استيراد وتوزيع خيارات المناديب (R4.3 Scheme Choices)"
        description="ربط خيارات المناديب المسجلة بالنماذج أو الاستبيانات بأنظمة التطبيق المعتمدة دون تعديل الردود الأصلية."
      >
        <div className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">نوع المصدر (Source Type):</label>
            <select
              value={assignmentSourceType}
              onChange={(e) => setAssignmentSourceType(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
            >
              <option value="campaign">حملة / استبيان (Campaign)</option>
              <option value="custom_form">نموذج مخصص (Custom Form)</option>
            </select>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">معرف الحملة / النموذج (Form / Campaign ID):</label>
            <input
              type="text"
              placeholder="مثال: synthetic-choice-campaign"
              value={assignmentFormId}
              onChange={(e) => setAssignmentFormId(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">حقل الاختيار (Field ID):</label>
              <input
                type="text"
                placeholder="workSystem"
                value={assignmentFieldId}
                onChange={(e) => setAssignmentFieldId(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">قيمة الرد (Choice Key):</label>
              <input
                type="text"
                placeholder="commission"
                value={assignmentMappingKey}
                onChange={(e) => setAssignmentMappingKey(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">النظام المستهدف المعتمد (Scheme):</label>
            <select
              value={assignmentMappingScheme}
              onChange={(e) => setAssignmentMappingScheme(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
            >
              <option value="">حدد النظام المعتمد</option>
              {schemesList.map((sc) => (
                <option key={sc.id} value={sc.id}>
                  {sc.data?.name} (#{sc.version}) - {sc.id}
                </option>
              ))}
            </select>
          </div>

          <div className="pt-2 flex items-center justify-between border-t border-[var(--line)]">
            <button
              type="button"
              disabled={previewingAssignments}
              onClick={handlePreviewAssignments}
              className="px-4 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
            >
              {previewingAssignments ? 'جاري الفحص والمعاينة...' : 'معاينة الاستيراد (Dry-Run)'}
            </button>

            {assignmentPreviewResult && (
              <button
                type="button"
                disabled={committingAssignments || !assignmentPreviewResult.canCommit}
                onClick={handleCommitAssignments}
                className="px-4 py-2 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer"
              >
                {committingAssignments ? 'جاري التثبيت...' : 'تأكيد الاستيراد المالي'}
              </button>
            )}
          </div>

          {/* Preview Results Box */}
          {assignmentPreviewResult && (
            <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-2 mt-2">
              <div className="flex justify-between items-center">
                <span className="font-bold">حالة المعاينة:</span>
                <span
                  className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                    assignmentPreviewResult.canCommit
                      ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                      : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                  }`}
                >
                  {assignmentPreviewResult.canCommit ? 'جاهز للتأكيد' : 'يوجد موانع/تعارضات'}
                </span>
              </div>
              <div className="text-[11px] text-[var(--muted)]">
                إجمالي المناديب بالمعاينة: {assignmentPreviewResult.rows?.length || 0}
              </div>
            </div>
          )}
        </div>
      </Dialog>

      {/* ======================================================== */}
      {/* DRAWER: EXPLAIN LINES BREAKDOWN                          */}
      {/* ======================================================== */}
      <Drawer
        open={!!selectedLineForExplain}
        onClose={() => setSelectedLineForExplain(null)}
        title={`شرح احتساب مستحقات المندوب: ${selectedLineForExplain?.rider}`}
      >
        {selectedLineForExplain && (
          <div className="space-y-4 text-xs">
            <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] space-y-1.5">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">التطبيق:</span>
                <span className="font-mono font-bold">{selectedLineForExplain.app}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">الفئة:</span>
                <span className="font-bold">{selectedLineForExplain.category || '-'}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">إجمالي المنصة:</span>
                <span className="num font-bold">
                  {(selectedLineForExplain.platformGross || selectedLineForExplain.gross || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">صافي المستحق:</span>
                <span className="num font-bold text-[var(--money-pos)]">
                  {(selectedLineForExplain.net ?? selectedLineForExplain.entitlement ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>
            </div>

            <div className="space-y-2">
              <h5 className="font-bold text-xs text-[var(--ink)]">بنود المعادلة والأدلة المحاسبية (Explain Lines):</h5>
              {(!selectedLineForExplain.lines || selectedLineForExplain.lines.length === 0) ? (
                <div className="p-4 text-center text-[var(--muted)] bg-[var(--surface)] rounded-xl border border-[var(--line)]">
                  لا توجد بنود تفصيلية مسجلة لهذا السطر.
                </div>
              ) : (
                <div className="space-y-2">
                  {selectedLineForExplain.lines.map((l: any, idx: number) => (
                    <div key={idx} className="p-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] space-y-1">
                      <div className="flex justify-between items-center">
                        <span className="font-bold text-[var(--ink)]">{l.label || l.category}</span>
                        <span
                          className={`num font-bold ${
                            l.amount < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'
                          }`}
                        >
                          {(l.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </span>
                      </div>
                      {l.formula && (
                        <div className="font-mono text-[10px] text-[var(--muted)] bg-[var(--raised)] p-1.5 rounded-md text-left" dir="ltr">
                          {l.formula}
                        </div>
                      )}
                      {l.inputs && (
                        <div className="text-[10px] text-[var(--muted)]">
                          مدخلات: <span className="font-mono">{JSON.stringify(l.inputs)}</span>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
      </Drawer>

      {/* ======================================================== */}
      {/* DIALOG: LEGACY PAYROLL DETAILS                           */}
      {/* ======================================================== */}
      <Dialog
        open={!!detailPreviewRow}
        onClose={() => setDetailPreviewRow(null)}
        title={`تفاصيل احتساب المسير: ${detailPreviewRow?.iqama}`}
      >
        {detailPreviewRow && (
          <div className="space-y-3 pt-2 text-xs">
            <div className="grid grid-cols-2 gap-2 p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)]">
              <div>
                <span className="text-[var(--muted)] block">السياسة:</span>
                <span className="font-bold">{detailPreviewRow.policyName}</span>
              </div>
              <div>
                <span className="text-[var(--muted)] block">عدد الطلبات:</span>
                <span className="font-bold num">{detailPreviewRow.orders}</span>
              </div>
              <div>
                <span className="text-[var(--muted)] block">الرصيد الافتتاحي:</span>
                <span className="font-bold num">{detailPreviewRow.openingBalance} ر.س</span>
              </div>
              <div>
                <span className="text-[var(--muted)] block">حركة الشهر:</span>
                <span className="font-bold num">{detailPreviewRow.periodMovement} ر.س</span>
              </div>
            </div>

            {detailPreviewRow.blockers && detailPreviewRow.blockers.length > 0 && (
              <div className="p-3 rounded-xl bg-[var(--warning)]/10 border border-[var(--warning)]/30 text-[var(--warning)] space-y-1">
                <div className="font-bold flex items-center gap-1.5">
                  <AlertTriangle size={14} />
                  <span>موانع الاعتماد:</span>
                </div>
                <ul className="list-disc list-inside space-y-0.5 text-[11px]">
                  {detailPreviewRow.blockers.map((b, i) => (
                    <li key={i}>{b}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: LEGACY PAYROLL APPROVE                           */}
      {/* ======================================================== */}
      <Dialog
        open={!!approvePreviewRow}
        onClose={() => setApprovePreviewRow(null)}
        title="اعتماد وترحيل مسير المندوب محاسبياً"
      >
        {approvePreviewRow && (
          <div className="space-y-4 pt-2 text-xs">
            <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-1">
              <div>المندوب: <span className="font-mono font-bold">{approvePreviewRow.iqama}</span></div>
              <div>الشهر: <span className="font-mono">{legacyPeriod}</span></div>
              <div>صافي الرصيد الختامي: <span className="num font-bold text-[var(--accent)]">{approvePreviewRow.closingBalance} ر.س</span></div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">السجل المالي صاحب المسير (Sponsor):</label>
              <select
                value={approveSponsorId}
                onChange={(e) => setApproveSponsorId(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
              >
                <option value="">حدد السجل المالي</option>
                {financeCatalog?.sponsors?.map((s: any) => (
                  <option key={s.id} value={s.id}>
                    {s.nameAr || s.name} ({s.id})
                  </option>
                ))}
              </select>
            </div>

            <div className="pt-2 flex justify-end gap-2 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setApprovePreviewRow(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={approvingRun || !approveSponsorId}
                onClick={handleApproveLegacyRun}
                className="px-4 py-2 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer"
              >
                {approvingRun ? 'جاري الاعتماد والترحيل...' : 'اعتماد المسير والقيود'}
              </button>
            </div>
          </div>
        )}
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: NEW ADJUSTMENT                                   */}
      {/* ======================================================== */}
      <Dialog
        open={adjModalOpen}
        onClose={() => setAdjModalOpen(false)}
        title="إضافة مستحق أو تحمل تشغيلي"
      >
        <div className="space-y-3 pt-2 text-xs">
          <div className="space-y-1">
            <label className="text-xs font-bold">المندوب:</label>
            <select
              value={adjIqama}
              onChange={(e) => setAdjIqama(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs"
            >
              <option value="">اختر المندوب</option>
              {ridersList.map((r) => (
                <option key={r.iqama} value={r.iqama}>
                  {r.name} ({r.iqama})
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-1">
              <label className="text-xs font-bold">المبلغ (الموجب إضافة والسالب خصم):</label>
              <input
                type="number"
                step="0.01"
                placeholder="0.00"
                value={adjAmount}
                onChange={(e) => setAdjAmount(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold">التصنيف:</label>
              <select
                value={adjCategory}
                onChange={(e) => setAdjCategory(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs"
              >
                <option value="fuel">وقود فعلي</option>
                <option value="rent">إيجار مركبة</option>
                <option value="housing">سكن</option>
                <option value="internet">اتصالات</option>
                <option value="advance">سلفة</option>
                <option value="hr">تحملات HR</option>
                <option value="adjustment">تسوية أخرى</option>
              </select>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold">مرجع المستند / التحويل:</label>
            <input
              type="text"
              placeholder="مثال: REF-98412"
              value={adjSourceRef}
              onChange={(e) => setAdjSourceRef(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold">بيان البند ومصدر اعتماده:</label>
            <input
              type="text"
              placeholder="شرح سبب البند..."
              value={adjDescription}
              onChange={(e) => setAdjDescription(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs"
            />
          </div>

          <div className="pt-2 flex justify-end gap-2 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setAdjModalOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={savingAdj}
              onClick={handleSaveAdjustment}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold"
            >
              {savingAdj ? 'جاري التسجيل...' : 'تسجيل البند'}
            </button>
          </div>
        </div>
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: RECORD PAYMENT                                   */}
      {/* ======================================================== */}
      <Dialog
        open={payModalOpen}
        onClose={() => setPayModalOpen(false)}
        title="تسجيل صرف دفعة راتب فعلية"
      >
        <div className="space-y-3 pt-2 text-xs">
          <div className="space-y-1">
            <label className="text-xs font-bold">المندوب:</label>
            <select
              value={payIqama}
              onChange={(e) => {
                const val = e.target.value;
                setPayIqama(val);
                const r = ridersList.find((x) => x.iqama === val);
                if (r) setPaySponsorId(r.financeSponsorId || r.sponsorId || 'BW');
              }}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs"
            >
              <option value="">اختر المندوب</option>
              {ridersList.map((r) => (
                <option key={r.iqama} value={r.iqama}>
                  {r.name} ({r.iqama})
                </option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-2 gap-2">
            <div className="space-y-1">
              <label className="text-xs font-bold">المبلغ المدفوع:</label>
              <input
                type="number"
                step="0.01"
                min="0.01"
                placeholder="0.00"
                value={payAmount}
                onChange={(e) => setPayAmount(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold">مرجع التحويل الفعلي:</label>
              <input
                type="text"
                placeholder="مثال: BANK-TRX-10293"
                value={paySourceRef}
                onChange={(e) => setPaySourceRef(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold">خزينة / حساب البنك الدافع:</label>
            <select
              value={payVaultCode}
              onChange={(e) => setPayVaultCode(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs"
            >
              <option value="">الحساب الافتراضي للسجل</option>
              {financeCatalog?.vaults?.map((v: any) => (
                <option key={v.code} value={v.code}>
                  {v.nameAr} ({v.code})
                </option>
              ))}
            </select>
          </div>

          <div className="pt-2 flex justify-end gap-2 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setPayModalOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={savingPay}
              onClick={handleSavePayment}
              className="px-4 py-2 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold"
            >
              {savingPay ? 'جاري التسجيل...' : 'تسجيل الدفع وترحيل القيد'}
            </button>
          </div>
        </div>
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: RIDER LEDGER VIEW                                */}
      {/* ======================================================== */}
      <Dialog
        open={ledgerModalOpen}
        onClose={() => setLedgerModalOpen(false)}
        title={`دفتر حساب المندوب: ${ledgerIqama}`}
      >
        <div className="space-y-4 pt-2 text-xs">
          {loadingLedger ? (
            <div className="p-6 space-y-2">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : !ledgerData ? (
            <div className="p-4 text-center text-[var(--muted)]">لا توجد حركات دفترية مسجلة.</div>
          ) : (
            <div className="space-y-3">
              <div className="p-3 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/30 text-xs flex justify-between items-center">
                <span className="font-bold text-[var(--ink)]">الرصيد الدفتري الحالي:</span>
                <span
                  className={`num font-bold text-sm ${
                    ledgerData.balance < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'
                  }`}
                >
                  {(ledgerData.balance || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                </span>
              </div>

              <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
                <table className="w-full text-start text-xs">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)] text-[var(--muted)]">
                    <tr>
                      <th className="p-2 text-start">الفترة</th>
                      <th className="p-2 text-start">نوع الحركة</th>
                      <th className="p-2 text-start">المبلغ</th>
                      <th className="p-2 text-start">المرجع</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {ledgerData.rows?.map((row, idx) => (
                      <tr key={idx}>
                        <td className="p-2 font-mono">{row.period}</td>
                        <td className="p-2">{row.kind}</td>
                        <td
                          className={`p-2 num font-bold ${
                            row.amount < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'
                          }`}
                        >
                          {(row.amount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-2 font-mono text-[10px] text-[var(--muted)]">{row.sourceRef}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: RESOLVE DISPUTE                                  */}
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
