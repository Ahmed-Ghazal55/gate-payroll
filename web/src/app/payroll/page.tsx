'use client';

import React, { useState, useEffect, useMemo, useRef, useCallback, Suspense } from 'react';
import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { formatReadableValue, formatRecordEntries } from '../../lib/formatRecord';
import {
  PAYROLL_TAB_LABELS,
  buildTabRedirectUrl,
  resolvePayrollTab,
  type PayrollTab,
} from '../../lib/page-tabs';
import { appendPeriodToHref } from '../../lib/period';
import { payAppHasEligibility } from '../../lib/pay-app-flags';
import { usePeriod } from '../../lib/use-period';
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
  Trash2,
  Plus,
  Ban,
  HelpCircle,
  Hash,
  Fuel,
  Car,
  Settings,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import type {
  PayReviewDraft,
  PayReviewBlocker,
  PayReviewInvoice,
  PayReviewAccount,
  PayReviewRider,
  PayCatalog,
  PayEligibilityItem,
  PayCycleItem,
  PayCycleLineItem,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef, useTableAutoFilter, AutoFilterTh, AutoFilterBar, defaultStatusTone } from '../../components/ui/DataTable';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { formatRiyadhDateTime } from '../../components/ui/SourceBadge';
import { DebtDeductionStage } from '../../features/payroll/DebtDeductionStage';
import { PayrollReviewGrid } from '../../features/payroll/PayrollReviewGrid';
import { NinjaInvoiceUploadScreen } from '../../features/payroll/NinjaInvoiceUploadScreen';
import { ChefzReviewPanel } from '../../features/payroll/ChefzReviewPanel';
import { MudadPanel } from '../../features/payroll/MudadPanel';
import { ChefzSettlementsPanel } from '../../features/payroll/ChefzSettlementsPanel';

import { ImportUploadDialog } from '../../features/imports/ImportUploadDialog';
import { KeetaOperationsImportDialog } from '../../features/imports/KeetaOperationsImportDialog';
import {
  canShowImportStep,
  HS_OPS_IMPORT,
  KEETA_OPS_IMPORT,
  NINJA_OPS_IMPORT,
  type PageImportStep,
} from '../../features/imports/import-steps';
import { ValidateGapsPanel } from '../../features/payroll/ValidateGapsPanel';
import { PaySettingsEditorModal } from '../../features/payroll/PaySettingsEditorModal';
import { PaySettingsCycleReview } from '../../features/payroll/PaySettingsCycleReview';
import { ParentTermsDisplayPanel } from '../../features/payroll/ParentTermsDisplayPanel';
import { RiderAssignmentsWorkspace } from '../../features/payroll/RiderAssignmentsWorkspace';
import { RuleRecordWorkspace } from '../../features/payroll/RuleRecordWorkspace';
import { SimulationBanner, SIMULATION_BLOCKED_HINT } from '../../components/payroll/SimulationBanner';
import {
  createEmptyTermsForm,
  createEmptySchemeForm,
  createEmptyEligibilityForm,
  termsFormFromNinjaSeed,
  validateTermsForm,
  validateSchemeForm,
  validateEligibilityForm,
} from '../../features/payroll/PaySettingsValidation';
import type {
  TermsFormState,
  SchemeFormState,
  EligibilityFormState,
} from '../../features/payroll/PaySettingsValidation';
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
import { formatSar } from '../../lib/money';
import { payrollBlockerLabelAr } from '../../lib/payroll-labels';

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

const CAPTURE_KIND_LABELS: Record<string, string> = {
  ninja_daily: 'الأداء اليومي',
  platform_account: 'حسابات المنصة',
  vehicle_assignment: 'تسليم المركبات',
  vehicle: 'المركبات',
  housing_residence: 'السكن',
};

const BLOCKER_ACTION_MAP: Record<string, { label: string; href: string }> = {
  upload_invoice: { label: 'رفع فاتورة نينجا', href: '/app/payroll?tab=ninja_upload' },
  approve_invoice: { label: 'اعتماد الفاتورة', href: '/app/payroll?tab=ninja_upload' },
  approve_settings: { label: 'اعتماد شروط الأهلية والأنظمة', href: '/app/payroll?tab=rules' },
  apply_default_settings: { label: 'تطبيق الافتراضي بنقرة واحدة', href: '/app/payroll?tab=cycle' },
  resolve_dispute: { label: 'حسم الاعتراض الرسمي', href: '/app/payroll?tab=ninja_upload' },
  classify_invoice: { label: 'تصنيف خصومات الفاتورة', href: '/app/payroll?tab=ninja_upload' },
  review_invoice_account: { label: 'مراجعة حسابات الفاتورة', href: '/app/workforce/' },
  link_account: { label: 'ربط حساب المنصة بالسجل', href: '/app/workforce/' },
  link_actual_operator: { label: 'ربط المشغل الفعلي للحساب', href: '/app/workforce/' },
  import_operations: { label: 'استيراد التشغيل والمناديب', href: '/app/payroll?tab=platforms&app=ninja&sub=settlements&opsImport=1' },
  import_scheme_choices: { label: 'خيارات الأنظمة', href: '/app/payroll?tab=rules' },
  review_operations: { label: 'مراجعة بيانات التشغيل اليومي', href: '/app/operations' },
  review_rider_record: { label: 'مراجعة سجل المندوب', href: '/app/riders' },
  sync_riders: { label: 'مراجعة سجل المندوب', href: '/app/riders' },
  review_rider: { label: 'مراجعة بيانات المندوب', href: '/app/riders' },
  review_posting: { label: 'مراجعة الترحيل والقيود', href: '/app/treasury' },
  open_cycle: { label: 'عرض الدورة القائمة', href: '/app/payroll?tab=cycle' },
  split_dues: { label: 'فصل مستحقات اليوزرات', href: '/app/payroll?tab=cycle&stage=split_dues' },
};

const NINJA_METRICS_MAP: Record<string, string> = {
  orders: 'عدد الطلبات الفعلية',
  days: 'أيام التشغيل',
  gross: 'إجمالي دخل المنصة',
  incentives: 'الحوافز الإضافية',
  attendanceDays: 'أيام الحضور والعمل',
  hours8Days: 'أيام تحقيق 8 ساعات',
  weekendDays: 'أيام عطلة نهاية الأسبوع',
  workingHours: 'ساعات العمل الإجمالية',
  activeWorkingHours: 'ساعات العمل الفعلية',
  committedWorkingHours: 'ساعات الالتزام',
  shiftCompliancePercent: 'نسبة الالتزام بالورديات',
  onTimeAttendancePercent: 'نسبة الحضور بالوقت',
};

const OPERATORS_MAP: Record<string, string> = {
  gte: 'أكبر من أو يساوي',
  gt: 'أكبر من',
  lte: 'أصغر من أو يساوي',
  lt: 'أصغر من',
  eq: 'يساوي',
  neq: 'لا يساوي',
};

const EFFECT_TYPES_MAP: Record<string, string> = {
  zero: 'تصفير المستحق',
  next_band: 'الانتقال للشريحة التالية',
  per_order: 'محاسبة بسعر الطلب الفردي',
};

function PayrollPageInner() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const showToast = (msg: string, type: 'success' | 'error' | 'info' | 'warning' = 'info') => {
    toast(msg, type === 'error' ? 'danger' : type);
  };

  // Main shared sections (sidebar-driven, page-tabs-rule 3.0):
  // platforms · cycle · current-cycle · ninja_upload · rules · deductions · bank · policies
  type PayAppSubTab = 'settlements' | 'parent_terms' | 'internal_schemes' | 'eligibility' | 'accounts';
  const resolvedTab = resolvePayrollTab(searchParams?.get('tab'));
  const tabFromUrl: PayrollTab = resolvedTab.tab;
  const rawAppFromUrl = searchParams?.get('app') || 'hungerstation';
  const appFromUrl =
    rawAppFromUrl === 'thechefz' || rawAppFromUrl === 'the-chefz' ? 'chefz' : rawAppFromUrl;
  const subFromUrl = (searchParams?.get('sub') as PayAppSubTab) || 'settlements';
  const [mainTab, setMainTab] = useState<PayrollTab>(tabFromUrl);

  // Redirect legacy or unknown ?tab= ids cleanly while keeping all other query params (period, app, sub, etc.).
  useEffect(() => {
    if (!resolvedTab.legacy) return;
    router.replace(buildTabRedirectUrl(pathname || '/payroll/', searchParams, resolvedTab), { scroll: false });
  }, [resolvedTab.legacy, resolvedTab.tab, pathname, router, searchParams]);

  const navigateMainTab = useCallback(
    (nextTab: PayrollTab) => {
      setMainTab(nextTab);
      const params = new URLSearchParams(searchParams?.toString() || '');
      params.set('tab', nextTab);
      router.replace(`${pathname || '/payroll/'}?${params.toString()}`, { scroll: false });
    },
    [pathname, router, searchParams]
  );

  // Selected App within platforms tab
  const [selectedPlatform, setSelectedPlatform] = useState<string>(appFromUrl);

  // Normalized app slug for pay endpoints
  const appSlug = useMemo(() => {
    if (selectedPlatform === 'hungerstation' || selectedPlatform === 'hs') return 'hs';
    if (selectedPlatform === 'thechefz' || selectedPlatform === 'chefz') return 'chefz';
    return selectedPlatform;
  }, [selectedPlatform]);

  // Sub-tabs inside selected app
  const [appSubTab, setAppSubTab] = useState<PayAppSubTab>(subFromUrl);

  const selectPlatformInUrl = useCallback(
    (nextApp: string) => {
      setSelectedPlatform(nextApp);
      const params = new URLSearchParams(searchParams?.toString() || '');
      params.set('app', nextApp);
      router.replace(`${pathname || '/payroll/'}?${params.toString()}`, { scroll: false });
    },
    [pathname, router, searchParams]
  );

  const selectAppSubInUrl = useCallback(
    (nextSub: PayAppSubTab) => {
      const safeSub =
        nextSub === 'eligibility' && !payAppHasEligibility(appSlug) ? 'settlements' : nextSub;
      setAppSubTab(safeSub);
      const params = new URLSearchParams(searchParams?.toString() || '');
      params.set('sub', safeSub);
      router.replace(`${pathname || '/payroll/'}?${params.toString()}`, { scroll: false });
    },
    [pathname, router, searchParams, appSlug]
  );

  useEffect(() => {
    if (appSubTab === 'eligibility' && !payAppHasEligibility(appSlug)) {
      selectAppSubInUrl('settlements');
    }
  }, [appSubTab, appSlug, selectAppSubInUrl]);

  // Filters & period (URL ?period=YYYY-MM single source of truth)
  const { period: selectedPeriod, setPeriod: setSelectedPeriod } = usePeriod();
  const [selectedCompany, setSelectedCompany] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [simBusy, setSimBusy] = useState(false);
  const [simTick, setSimTick] = useState(0);
  const runMode = searchParams?.get('runMode') || 'live';
  const simulationId = searchParams?.get('simulationId') || '';
  const payoutBlocked = searchParams?.get('payoutBlocked') === '1';
  const isSimulation = runMode === 'simulation' && Boolean(simulationId);

  const clearSimulationUrl = useCallback(() => {
    const params = new URLSearchParams(searchParams?.toString() || '');
    params.delete('runMode');
    params.delete('simulationId');
    params.delete('payoutBlocked');
    router.replace(`${pathname || '/payroll/'}?${params.toString()}`, { scroll: false });
    setSimTick((n) => n + 1);
  }, [pathname, router, searchParams]);

  const startSimulation = useCallback(async () => {
    if (!can('salaries.simulation.run')) {
      showToast('لا تملك صلاحية تشغيل المحاكاة', 'warning');
      return;
    }
    if (selectedPlatform === 'amazon') {
      showToast('أمازون مؤجّل حتى تتوفر فاتورة عيّنة', 'warning');
      return;
    }
    const companyId = selectedCompany !== 'all' ? selectedCompany : 'BW';
    setSimBusy(true);
    try {
      const created = await api.createSimulation({
        companyId,
        app: appSlug,
        periodMonth: selectedPeriod,
        payoutBlocked: false,
      });
      const id = String(created.id || '');
      if (!id) throw new Error('لم يُرجع الخادم معرّف المحاكاة');
      const params = new URLSearchParams(searchParams?.toString() || '');
      params.set('runMode', 'simulation');
      params.set('simulationId', id);
      if (created.payoutBlocked === true || created.payoutBlocked === 1) params.set('payoutBlocked', '1');
      else params.delete('payoutBlocked');
      router.replace(`${pathname || '/payroll/'}?${params.toString()}`, { scroll: false });
      showToast('تم إنشاء محاكاة جديدة', 'success');
      setSimTick((n) => n + 1);
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر إنشاء المحاكاة', 'error');
    } finally {
      setSimBusy(false);
    }
  }, [
    can,
    selectedPlatform,
    selectedCompany,
    appSlug,
    selectedPeriod,
    searchParams,
    pathname,
    router,
    showToast,
  ]);

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
    if (searchParams?.get('opsImport') === '1' && canShowImportStep(NINJA_OPS_IMPORT, { isAdmin, can })) {
      setOpsImportStep(NINJA_OPS_IMPORT);
    }
  }, [tabFromUrl, appFromUrl, subFromUrl, searchParams, isAdmin, can]);

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
  const [opsImportStep, setOpsImportStep] = useState<PageImportStep | null>(null);
  const [importAllowedActions, setImportAllowedActions] = useState<string[] | null>(null);
  useEffect(() => {
    let cancelled = false;
    api
      .getPayCatalog()
      .then((cat) => {
        const actions = (cat as { allowedActions?: string[] } | null)?.allowedActions;
        if (!cancelled) setImportAllowedActions(Array.isArray(actions) ? actions : []);
      })
      .catch(() => {
        if (!cancelled) setImportAllowedActions([]);
      });
    return () => {
      cancelled = true;
    };
  }, []);
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
  const [omarCommission, setOmarCommission] = useState<Awaited<ReturnType<typeof api.getOmarCommissionRates>> | null>(null);
  const [omarCommissionError, setOmarCommissionError] = useState<string | null>(null);
  const [loadingOmarCommission, setLoadingOmarCommission] = useState(false);
  const usesOmarCommission = appSlug === 'jahez' || appSlug === 'chefz';
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
  const [cyclesList, setCyclesList] = useState<PayCycleItem[]>([]);
  const [loadingCycles, setLoadingCycles] = useState<boolean>(false);
  const [cyclesError, setCyclesError] = useState<string | null>(null);
  const [advancingCycle, setAdvancingCycle] = useState<boolean>(false);
  const [cycleLines, setCycleLines] = useState<PayCycleLineItem[]>([]);
  const [loadingLines, setLoadingLines] = useState<boolean>(false);
  const [selectedLineForExplain, setSelectedLineForExplain] = useState<PayCycleLineItem | null>(null);

  const cycleLineColumns: ColumnDef<PayCycleLineItem>[] = useMemo(
    () => [
      {
        key: 'rider',
        header: 'إقامة المندوب',
        type: 'text',
        filterable: true,
        filterType: 'text',
        getValue: (line) => line.rider || '',
        render: (line) => (
          <Link
            href={`/riders/statement?iqama=${line.rider}&period=${selectedPeriod}`}
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
        type: 'list',
        filterable: true,
        filterType: 'list',
        getValue: (line) => line.app || '',
        render: (line) => <span className="font-mono text-xs">{line.app}</span>,
      },
      {
        key: 'category',
        header: 'الفئة',
        type: 'list',
        filterType: 'list',
        getValue: (line) => ('category' in line ? String((line as Record<string, unknown>).category || '') : ''),
        render: (line) => <span className="text-xs text-[var(--muted)]">{'category' in line ? String((line as Record<string, unknown>).category || '-') : '-'}</span>,
      },
      {
        key: 'platformGross',
        header: 'إجمالي المنصة',
        type: 'number',
        numeric: true,
        getValue: (line) => line.platformGross || line.gross || 0,
        render: (line) => (
          <span>
            {formatSar((line.platformGross || line.gross || 0))}
          </span>
        ),
      },
      {
        key: 'vehicleCharge',
        header: 'إيجار المركبة',
        type: 'number',
        numeric: true,
        getValue: (line) => line.allocatedVehicleCharge ?? 0,
        render: (line) => (
          <span className="text-[var(--muted)]">
            {formatSar((line.allocatedVehicleCharge ?? 0))}
          </span>
        ),
      },
      {
        key: 'deductions',
        header: 'الاستقطاعات',
        type: 'number',
        numeric: true,
        getValue: (line) => line.accountDeduction || 0,
        render: (line) => (
          <span className="text-[var(--danger)]">
            {formatSar((line.accountDeduction || 0))}
          </span>
        ),
      },
      {
        key: 'net',
        header: 'الصافي النهائي',
        type: 'number',
        numeric: true,
        getValue: (line) => line.entitlement ?? line.gross ?? 0,
        render: (line) => {
          const net = line.entitlement ?? line.gross ?? 0;
          return (
            <span className={`font-bold ${net < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'}`}>
              {formatSar(net)}
            </span>
          );
        },
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list',
        filterType: 'list',
        getValue: (line) => line.status || 'needs_decision',
        labelOf: (v) => (v === 'ready' ? 'جاهز' : v === 'approved' ? 'معتمد' : 'يحتاج قرار'),
        toneOf: (v) => (v === 'ready' || v === 'approved' ? 'success' : 'danger'),
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
        sortable: false,
        filterable: false,
        render: (line) => (
          <button
            type="button"
            onClick={() => setSelectedLineForExplain(line)}
            className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-xs font-bold text-[var(--accent)] hover:bg-[var(--line)] transition cursor-pointer flex items-center gap-1"
          >
            <Calculator size={13} />
            <span>كيف حُسب؟</span>
          </button>
        ),
      },
    ],
    [selectedPeriod]
  );

  // Review Draft States (A5)
  const [cycleViewMode, setCycleViewMode] = useState<'draft' | 'active'>('draft');
  const [reviewDraft, setReviewDraft] = useState<PayReviewDraft | null>(null);
  const [duesSplit, setDuesSplit] = useState<Awaited<ReturnType<typeof api.getDuesSplit>> | null>(null);
  const [loadingDuesSplit, setLoadingDuesSplit] = useState(false);
  const [duesSplitError, setDuesSplitError] = useState<string | null>(null);
  const [applyingDefaults, setApplyingDefaults] = useState(false);
  const [loadingDraft, setLoadingDraft] = useState<boolean>(false);
  const [draftError, setDraftError] = useState<string | null>(null);
  const [creatingCycle, setCreatingCycle] = useState<boolean>(false);
  const [capturingInputs, setCapturingInputs] = useState<boolean>(false);
  const [captureError, setCaptureError] = useState<{ status?: number; message: string } | null>(null);

  // Rules Editor States (A6)
  const [rulesSection, setRulesSection] = useState<'eligibility' | 'record' | 'global'>('eligibility');
  const [ninjaEligibilityList, setNinjaEligibilityList] = useState<PayEligibilityItem[]>([]);
  const [loadingNinjaEligibility, setLoadingNinjaEligibility] = useState<boolean>(false);
  const [ninjaEligibilityError, setNinjaEligibilityError] = useState<string | null>(null);
  const [ninjaCatalog, setNinjaCatalog] = useState<PayCatalog | null>(null);
  const [ruleModalOpen, setRuleModalOpen] = useState<boolean>(false);
  const [newRuleMetric, setNewRuleMetric] = useState<string>('orders');
  const [newRuleOperator, setNewRuleOperator] = useState<string>('gte');
  const [newRuleThreshold, setNewRuleThreshold] = useState<string>('');
  const [newRuleEffectType, setNewRuleEffectType] = useState<string>('zero');
  const [newRuleIncluded, setNewRuleIncluded] = useState<boolean>(true);
  const [rejectModalOpen, setRejectModalOpen] = useState<boolean>(false);
  const [rejectionReason, setRejectionReason] = useState<string>('');
  const [submittingRuleLifecycle, setSubmittingRuleLifecycle] = useState<boolean>(false);

  // Pay Settings Form States (S1 / S2)
  const [settingsFormKind, setSettingsFormKind] = useState<'terms' | 'scheme' | 'eligibility' | null>(null);
  const [termsForm, setTermsForm] = useState<TermsFormState>(() => createEmptyTermsForm('hs'));
  const [schemeForm, setSchemeForm] = useState<SchemeFormState>(() => createEmptySchemeForm('hs'));
  const [eligibilityForm, setEligibilityForm] = useState<EligibilityFormState>(() => createEmptyEligibilityForm());
  const [settingsFormError, setSettingsFormError] = useState<string | null>(null);
  const [savingSettingsForm, setSavingSettingsForm] = useState<boolean>(false);
  const [loadingNinjaSeed, setLoadingNinjaSeed] = useState<boolean>(false);

  // Editor Modal States (S2 / S5)
  const [editorModalOpen, setEditorModalOpen] = useState<boolean>(false);
  const [editorKind, setEditorKind] = useState<'terms' | 'scheme' | 'eligibility' | 'account_exception' | null>(null);
  const [editingDoc, setEditingDoc] = useState<PayConfigDoc | null>(null);

  // Reject / Delete Setting States (S2)
  const [rejectSettingModalOpen, setRejectSettingModalOpen] = useState<boolean>(false);
  const [rejectSettingDoc, setRejectSettingDoc] = useState<PayConfigDoc | null>(null);
  const [rejectSettingKind, setRejectSettingKind] = useState<'terms' | 'scheme' | 'eligibility' | null>(null);
  const [rejectSettingReason, setRejectSettingReason] = useState<string>('');

  // Cycle Review and Diff States (S3)
  const [prevTermsList, setPrevTermsList] = useState<PayConfigDoc[]>([]);
  const [prevSchemesList, setPrevSchemesList] = useState<PayConfigDoc[]>([]);
  const [prevEligibilityList, setPrevEligibilityList] = useState<PayConfigDoc[]>([]);
  const [showCycleReview, setShowCycleReview] = useState<boolean>(false);

  // Columns for Review Draft Accounts Breakdown
  const draftAccountColumns: ColumnDef<PayReviewAccount>[] = useMemo(
    () => [
      {
        key: 'accountId',
        header: 'المعرف / الحساب',
        type: 'text',
        filterable: true,
        getValue: (acc) => acc.accountId || '',
        render: (acc) => <span className="font-mono font-bold text-[var(--accent)]">{acc.accountId}</span>,
      },
      {
        key: 'sourceLineId',
        header: 'سطر المصدر',
        type: 'text',
        getValue: (acc) => acc.sourceLineId || '',
        render: (acc) => <span className="font-mono text-xs text-[var(--muted)]">{acc.sourceLineId || '-'}</span>,
      },
      {
        key: 'orders',
        header: 'الطلبات',
        type: 'number',
        numeric: true,
        getValue: (acc) => acc.orders || 0,
        render: (acc) => <span className="font-mono">{acc.orders || 0}</span>,
      },
      {
        key: 'baseGross',
        header: 'الأساسي',
        type: 'number',
        numeric: true,
        getValue: (acc) => acc.baseGross || 0,
        render: (acc) => <span>{formatSar((acc.baseGross || 0))}</span>,
      },
      {
        key: 'incentives',
        header: 'الحوافز',
        type: 'number',
        numeric: true,
        getValue: (acc) => acc.incentives || 0,
        render: (acc) => <span>{formatSar((acc.incentives || 0))}</span>,
      },
      {
        key: 'totalGross',
        header: 'إجمالي الاستحقاق',
        type: 'number',
        numeric: true,
        getValue: (acc) => (acc.baseGross || 0) + (acc.incentives || 0),
        render: (acc) => (
          <span className="font-bold text-[var(--money-pos)]">
            {formatSar(((acc.baseGross || 0) + (acc.incentives || 0)))}
          </span>
        ),
      },
      {
        key: 'ridersCount',
        header: 'المناديب المشغلين',
        type: 'number',
        numeric: true,
        getValue: (acc) => (acc.riders || []).length,
        render: (acc) => (
          <span className="font-mono text-xs">
            {(acc.riders || []).length} مندوب
          </span>
        ),
      },
    ],
    []
  );

  // Global settings state (effective-dated)
  const [globalSettings, setGlobalSettings] = useState<PayConfigDoc[]>([]);
  const [loadingGlobalSettings, setLoadingGlobalSettings] = useState<boolean>(false);
  const [globalSettingsError, setGlobalSettingsError] = useState<string | null>(null);

  const [copyingMonth, setCopyingMonth] = useState<boolean>(false);
  const [seedingDefaults, setSeedingDefaults] = useState<boolean>(false);

  // ========================================================
  // LEGACY PRODUCTION PAYROLL RUN STATES (F2)
  // ========================================================
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
      const prevMonth = getPreviousMonth(selectedPeriod);
      api
        .getPayTerms(appSlug, prevMonth)
        .then((r) => setPrevTermsList(r?.items || (Array.isArray(r) ? r : [])))
        .catch(() => setPrevTermsList([]));
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
      const prevMonth = getPreviousMonth(selectedPeriod);
      api
        .getPaySchemes(appSlug, prevMonth)
        .then((r) => setPrevSchemesList(r?.items || (Array.isArray(r) ? r : [])))
        .catch(() => setPrevSchemesList([]));
    } catch (err: any) {
      setSchemesError(err.message || 'تعذر تحميل الأنظمة الداخلية');
      setSchemesList([]);
    } finally {
      setLoadingSchemes(false);
    }
  };

  const loadOmarCommission = async () => {
    if (!usesOmarCommission) {
      setOmarCommission(null);
      setOmarCommissionError(null);
      return;
    }
    setLoadingOmarCommission(true);
    setOmarCommissionError(null);
    try {
      const res = await api.getOmarCommissionRates(appSlug, selectedPeriod);
      setOmarCommission(res);
    } catch (err: any) {
      setOmarCommissionError(err?.message || 'تعذر قراءة عمولة نظام عمر');
      setOmarCommission(null);
    } finally {
      setLoadingOmarCommission(false);
    }
  };

  // Load Eligibility
  const loadEligibility = async () => {
    if (!payAppHasEligibility(appSlug)) {
      setEligibilityList([]);
      setPrevEligibilityList([]);
      return;
    }
    setLoadingEligibility(true);
    setEligibilityError(null);
    try {
      const res = await api.getPayEligibility(appSlug, selectedPeriod);
      setEligibilityList(res?.items || (Array.isArray(res) ? res : []));
      const prevMonth = getPreviousMonth(selectedPeriod);
      api
        .getPayEligibility(appSlug, prevMonth)
        .then((r) => setPrevEligibilityList(r?.items || (Array.isArray(r) ? r : [])))
        .catch(() => setPrevEligibilityList([]));
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

  // Load Review Draft (A5)
  const loadReviewDraft = async () => {
    setLoadingDraft(true);
    setDraftError(null);
    try {
      const comp = selectedCompany !== 'all' ? selectedCompany : 'BW';
      const draftApp = selectedPlatform === 'keeta' ? 'keeta' : 'ninja';
      const draft = await api.getPayReviewDraft({
        app: draftApp,
        period: selectedPeriod,
        companyId: comp,
      });
      setReviewDraft(draft);
    } catch (err: any) {
      setDraftError(err.message || 'تعذر تحميل مسودة المراجعة المقترحة');
      setReviewDraft(null);
    } finally {
      setLoadingDraft(false);
    }
  };

  const loadDuesSplit = async () => {
    setLoadingDuesSplit(true);
    setDuesSplitError(null);
    try {
      const comp = selectedCompany !== 'all' ? selectedCompany : 'BW';
      const params = new URLSearchParams(typeof window !== 'undefined' ? window.location.search : '');
      const split = await api.getDuesSplit({
        app: 'ninja',
        period: selectedPeriod,
        companyId: comp,
        invoiceId: params.get('invoiceId') || undefined,
      });
      setDuesSplit(split);
    } catch (err: unknown) {
      setDuesSplitError(err instanceof Error ? err.message : 'تعذر تحميل فصل المستحقات');
      setDuesSplit(null);
    } finally {
      setLoadingDuesSplit(false);
    }
  };

  const handleApplyDefaultSettings = async () => {
    setApplyingDefaults(true);
    try {
      await api.applyPaySetupDefaults({ app: 'ninja', period: selectedPeriod });
      showToast('تم تطبيق الاعتمادات الافتراضية للشروط والأهلية والإعدادات العامة', 'success');
      await loadReviewDraft();
      await loadDuesSplit();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر تطبيق الافتراضي', 'error');
    } finally {
      setApplyingDefaults(false);
    }
  };

  // Load Ninja Eligibility (A6)
  const loadNinjaEligibility = async () => {
    setLoadingNinjaEligibility(true);
    setNinjaEligibilityError(null);
    try {
      const [elRes, catRes] = await Promise.allSettled([
        api.getAppEligibility('ninja'),
        api.getPayCatalog(),
      ]);
      if (elRes.status === 'fulfilled') {
        const val = elRes.value;
        const items = Array.isArray(val) ? val : (val && 'items' in val && Array.isArray(val.items) ? val.items : []);
        setNinjaEligibilityList(items);
      } else {
        const reason = elRes.reason;
        setNinjaEligibilityError(reason instanceof Error ? reason.message : 'تعذر تحميل ضوابط الأهلية');
        setNinjaEligibilityList([]);
      }
      if (catRes.status === 'fulfilled') {
        setNinjaCatalog(catRes.value);
      }
    } catch (err: unknown) {
      setNinjaEligibilityError(err instanceof Error ? err.message : 'تعذر تحميل بيانات الأهلية');
    } finally {
      setLoadingNinjaEligibility(false);
    }
  };

  // Create Cycle from Draft (A5)
  const handleCreateCycleFromDraft = async () => {
    if (!reviewDraft || !reviewDraft.canCreate || (reviewDraft.blockers && reviewDraft.blockers.length > 0)) {
      showToast('لا يمكن إنشاء الدورة مع وجود موانع استحقاق', 'error');
      return;
    }
    setCreatingCycle(true);
    try {
      const comp = selectedCompany !== 'all' ? selectedCompany : 'BW';
      await api.createPayCycle({
        app: 'ninja',
        period: selectedPeriod,
        companyId: comp,
        invoices: reviewDraft.invoices,
        adjustmentAccounts: {},
      });
      showToast('تم إنشاء دورة الرواتب بنجاح', 'success');
      await loadCycles();
      setCycleViewMode('active');
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر إنشاء دورة الرواتب', 'error');
    } finally {
      setCreatingCycle(false);
    }
  };

  // Rule Editing Handlers (A6)
  const activeNinjaEligibility = useMemo(() => {
    if (!ninjaEligibilityList || ninjaEligibilityList.length === 0) return null;
    const forPeriod = ninjaEligibilityList.filter((d) => d.period === selectedPeriod);
    if (forPeriod.length === 0) return ninjaEligibilityList[0];
    const draft = forPeriod.find((d) => d.status === 'draft');
    if (draft) return draft;
    return forPeriod.sort((a, b) => (b.version || 0) - (a.version || 0))[0];
  }, [ninjaEligibilityList, selectedPeriod]);

  const handleSaveNewRule = async () => {
    const trimmedThreshold = newRuleThreshold.trim();
    if (trimmedThreshold === '' || Number.isNaN(Number(trimmedThreshold))) {
      showToast('يرجى إدخال قيمة الحد الأدنى للشرط قبل الحفظ', 'error');
      return;
    }
    setSubmittingRuleLifecycle(true);
    try {
      const existingRules = [...(activeNinjaEligibility?.data?.rules || [])];
      existingRules.push({
        metric: newRuleMetric,
        operator: newRuleOperator,
        threshold: parseFloat(trimmedThreshold),
        effect: { type: newRuleEffectType },
        included: newRuleIncluded,
      });
      await api.createAppEligibility(
        'ninja',
        {
          name: activeNinjaEligibility?.data?.name || `شروط أهلية نينجا ${selectedPeriod}`,
          rules: existingRules,
        },
        selectedPeriod
      );
      showToast('تمت إضافة الشرط إلى نسخة الأهلية بنجاح', 'success');
      setRuleModalOpen(false);
      setNewRuleThreshold('');
      await loadNinjaEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر إضافة الشرط', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  const handleDeleteRule = async (idx: number) => {
    if (!activeNinjaEligibility || activeNinjaEligibility.status !== 'draft') return;
    setSubmittingRuleLifecycle(true);
    try {
      const existingRules = [...(activeNinjaEligibility?.data?.rules || [])];
      existingRules.splice(idx, 1);
      await api.createAppEligibility(
        'ninja',
        {
          name: activeNinjaEligibility.data?.name || `شروط أهلية نينجا ${selectedPeriod}`,
          rules: existingRules,
        },
        selectedPeriod
      );
      showToast('تم حذف الشرط من المسودة', 'success');
      await loadNinjaEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر حذف الشرط', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  const handleApproveNinjaEligibility = async () => {
    if (!activeNinjaEligibility) return;
    setSubmittingRuleLifecycle(true);
    try {
      await api.approveAppEligibility('ninja', activeNinjaEligibility.id);
      showToast('تم اعتماد شروط الأهلية بنجاح', 'success');
      await loadNinjaEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد شروط الأهلية', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  const handleRejectNinjaEligibility = async () => {
    if (!activeNinjaEligibility) return;
    if (!rejectionReason.trim()) {
      showToast('يرجى كتابة سبب الرفض', 'warning');
      return;
    }
    setSubmittingRuleLifecycle(true);
    try {
      await api.rejectPaySetting('pay_eligibility', 'ninja', activeNinjaEligibility.id, rejectionReason.trim());
      showToast('تم رفض المسودة وتوثيق السبب', 'success');
      setRejectModalOpen(false);
      setRejectionReason('');
      await loadNinjaEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر رفض المسودة', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  const handleDeleteNinjaDraft = async () => {
    if (!activeNinjaEligibility || activeNinjaEligibility.status !== 'draft') return;
    setSubmittingRuleLifecycle(true);
    try {
      await api.deletePaySetting('pay_eligibility', 'ninja', activeNinjaEligibility.id);
      showToast('تم حذف المسودة بنجاح', 'success');
      await loadNinjaEligibility();
    } catch (err: any) {
      showToast(err.message || 'تعذر حذف المسودة', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  const handleCreateNewNinjaDraft = async () => {
    const existingRules = activeNinjaEligibility?.data?.rules || [];
    if (existingRules.length === 0) {
      setNewRuleThreshold('');
      setRuleModalOpen(true);
      return;
    }
    setSubmittingRuleLifecycle(true);
    try {
      await api.createAppEligibility(
        'ninja',
        {
          name: `شروط أهلية نينجا ${selectedPeriod}`,
          rules: existingRules,
        },
        selectedPeriod
      );
      showToast('تم إنشاء نسخة شروط أهلية جديدة لشهر ' + selectedPeriod, 'success');
      await loadNinjaEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر إنشاء نسخة شروط الأهلية', 'error');
    } finally {
      setSubmittingRuleLifecycle(false);
    }
  };

  interface DraftRuleItem {
    metric: string;
    operator: string;
    threshold: number | string;
    effect?: { type: string; value?: number };
    included?: boolean;
  }

  const draftRuleColumns: ColumnDef<DraftRuleItem>[] = useMemo(
    () => [
      {
        key: 'metric',
        header: 'المقياس',
        type: 'list',
        filterType: 'list',
        getValue: (r) => r.metric || '',
        labelOf: (v) => NINJA_METRICS_MAP[String(v ?? '')] || String(v ?? ''),
        render: (r) => (
          <div className="font-semibold text-[var(--ink)]">
            <span>{NINJA_METRICS_MAP[r.metric] || r.metric}</span>
          </div>
        ),
      },
      {
        key: 'operator',
        header: 'المعامل',
        type: 'list',
        filterType: 'list',
        getValue: (r) => r.operator || '',
        labelOf: (v) => OPERATORS_MAP[String(v ?? '')] || String(v ?? ''),
        render: (r) => (
          <span className="font-mono text-xs bg-[var(--raised)] px-2 py-0.5 rounded-md text-[var(--accent)] font-bold">
            {OPERATORS_MAP[r.operator] || r.operator}
          </span>
        ),
      },
      {
        key: 'threshold',
        header: 'الحد الأدنى / القيمة',
        type: 'number',
        numeric: true,
        getValue: (r) => Number(r.threshold ?? 0),
        render: (r) => <span className="font-mono font-bold text-[var(--ink)]">{r.threshold}</span>,
      },
      {
        key: 'effect',
        header: 'الأثر عند عدم التحقق',
        type: 'list',
        filterType: 'list',
        getValue: (r) => r.effect?.type || 'zero',
        labelOf: (v) => EFFECT_TYPES_MAP[String(v ?? 'zero')] || String(v ?? 'zero'),
        toneOf: (v) => (v === 'zero' ? 'danger' : 'accent'),
        render: (r) => {
          const type = r.effect?.type || 'zero';
          return (
            <span className={`px-2 py-0.5 rounded-full text-xs font-bold ${
              type === 'zero' ? 'bg-[var(--danger)]/15 text-[var(--danger)]' : 'bg-[var(--accent)]/15 text-[var(--accent)]'
            }`}>
              {EFFECT_TYPES_MAP[type] || type}
            </span>
          );
        },
      },
      {
        key: 'included',
        header: 'ضمن القاعدة',
        type: 'list',
        filterType: 'list',
        getValue: (r) => (r.included !== false ? 'enabled' : 'disabled'),
        labelOf: (v) => (v === 'enabled' ? 'مفعّل' : 'معطّل'),
        toneOf: (v) => (v === 'enabled' ? 'success' : 'neutral'),
        render: (r) => (
          <span className={`px-2 py-0.5 rounded-full text-xs font-bold ${
            r.included !== false ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]' : 'bg-[var(--raised)] text-[var(--muted)]'
          }`}>
            {r.included !== false ? 'مفعّل' : 'معطّل'}
          </span>
        ),
      },
      {
        key: 'actions',
        header: 'إجراءات',
        sortable: false,
        filterable: false,
        render: (r, idx) => (
          activeNinjaEligibility?.status === 'draft' ? (
            <button
              type="button"
              onClick={() => handleDeleteRule(idx)}
              className="p-1 rounded-md text-[var(--danger)] hover:bg-[var(--danger)]/10 transition cursor-pointer"
              title="حذف الشرط"
            >
              <Trash2 size={14} />
            </button>
          ) : (
            <span className="text-[11px] text-[var(--muted)]">معتمد</span>
          )
        ),
      },
    ],
    [activeNinjaEligibility]
  );

  // Load Cycle Lines
  const loadCycleLines = async (cycleId: string) => {
    setLoadingLines(true);
    try {
      const res = await api.getPayCycleLines(cycleId);
      setCycleLines(res?.items || []);
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
        api.getLegacyPayrollPreview(selectedPeriod),
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
      else if (appSubTab === 'internal_schemes') {
        loadSchemes();
        loadOmarCommission();
      }
      else if (appSubTab === 'eligibility') loadEligibility();
      else if (appSubTab === 'accounts') loadAssignments();
    } else if (mainTab === 'cycle') {
      loadCycles();
      loadReviewDraft();
      loadDuesSplit();
    } else if (mainTab === 'rules') {
      loadGlobalSettings();
      loadNinjaEligibility();
    } else if (mainTab === 'current-cycle') {
      loadLegacyPayroll();
    }
  }, [mainTab, appSubTab, selectedPlatform, selectedPeriod, selectedCompany]);

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
      showToast('لا يجوز أن يعتمد نفس الشخص الذي أنشأ التوزيع؛ يلزم مراجع آخر', 'error');
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
      showToast('لا يجوز أن يبت نفس الشخص الذي أنشأ الاعتراض؛ يلزم مراجع آخر', 'error');
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
      if (appSubTab === 'internal_schemes') {
        loadSchemes();
        loadOmarCommission();
      }
      if (appSubTab === 'eligibility') loadEligibility();
      if (mainTab === 'rules') loadGlobalSettings();
    } catch (err: any) {
      showToast(err.message || `تعذر نسخ إعدادات شهر ${prevMonth}`, 'error');
    } finally {
      setCopyingMonth(false);
    }
  };


  const handleSeedDefaults = async (targetApp = appSlug) => {
    setSeedingDefaults(true);
    try {
      const res = await api.seedPayDefaults(targetApp, selectedPeriod);
      showToast(
        `تمت تهيئة ${res.created} مسودة افتراضية لشهر ${selectedPeriod} (${res.existing} موجودة مسبقاً) — يلزم اعتماد مستقل`,
        'success'
      );
      loadSchemes();
      loadTerms();
      loadEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر تهيئة الأنظمة الافتراضية', 'error');
    } finally {
      setSeedingDefaults(false);
    }
  };

  const isSelfMaker = (doc: PayConfigDoc) =>
    Boolean(doc.createdBy && user?.username && doc.createdBy.toLowerCase() === user.username.toLowerCase());

  const handleOpenTermsForm = () => {
    setEditorKind('terms');
    setEditingDoc(null);
    setEditorModalOpen(true);
  };

  const handleEditTerms = (doc: PayConfigDoc) => {
    setEditorKind('terms');
    setEditingDoc(doc);
    setEditorModalOpen(true);
  };

  const handleApproveTerms = async (termsDoc: PayConfigDoc) => {
    if (isSelfMaker(termsDoc)) {
      showToast('لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة؛ يلزم مستخدم آخر مستقل.', 'warning');
      return;
    }
    try {
      await api.approvePayTerms(appSlug, termsDoc.id);
      showToast('تم اعتماد شروط المنصة بنجاح وتثبيتها للقراءة فقط', 'success');
      loadTerms();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر اعتماد شروط المنصة', 'error');
    }
  };

  const handleOpenSchemeForm = () => {
    setEditorKind('scheme');
    setEditingDoc(null);
    setEditorModalOpen(true);
  };

  const handleEditScheme = (doc: PayConfigDoc) => {
    setEditorKind('scheme');
    setEditingDoc(doc);
    setEditorModalOpen(true);
  };

  const handleApproveScheme = async (schemeDoc: PayConfigDoc) => {
    if (isSelfMaker(schemeDoc)) {
      showToast('لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة؛ يلزم مستخدم آخر مستقل.', 'warning');
      return;
    }
    try {
      await api.approvePayScheme(appSlug, schemeDoc.id);
      showToast('تم اعتماد النظام الداخلي بنجاح', 'success');
      loadSchemes();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر اعتماد النظام الداخلي', 'error');
    }
  };

  const handleOpenEligibilityForm = () => {
    if (!payAppHasEligibility(appSlug)) return;
    setEditorKind('eligibility');
    setEditingDoc(null);
    setEditorModalOpen(true);
  };

  const handleEditEligibility = (doc: PayConfigDoc) => {
    if (!payAppHasEligibility(appSlug)) return;
    setEditorKind('eligibility');
    setEditingDoc(doc);
    setEditorModalOpen(true);
  };

  const handleApproveEligibility = async (elDoc: PayConfigDoc) => {
    if (isSelfMaker(elDoc)) {
      showToast('لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة؛ يلزم مستخدم آخر مستقل.', 'warning');
      return;
    }
    try {
      await api.approvePayEligibility(appSlug, elDoc.id);
      showToast('تم اعتماد ضوابط الأهلية بنجاح', 'success');
      loadEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر اعتماد ضوابط الأهلية', 'error');
    }
  };

  const handleOpenRejectSetting = (doc: PayConfigDoc, kind: 'terms' | 'scheme' | 'eligibility') => {
    setRejectSettingDoc(doc);
    setRejectSettingKind(kind);
    setRejectSettingReason('');
    setRejectSettingModalOpen(true);
  };

  const handleConfirmRejectSetting = async () => {
    if (!rejectSettingDoc || !rejectSettingKind) return;
    if (!rejectSettingReason.trim()) {
      showToast('يرجى كتابة سبب الرفض لتوثيقه في سجل التدقيق المالي', 'error');
      return;
    }
    try {
      await api.rejectPaySetting(
        rejectSettingKind === 'terms' ? 'pay_terms' : rejectSettingKind === 'scheme' ? 'pay_scheme' : 'pay_eligibility',
        appSlug,
        rejectSettingDoc.id,
        rejectSettingReason.trim()
      );
      showToast('تم رفض المسودة وتوثيق سبب الرفض بنجاح', 'success');
      setRejectSettingModalOpen(false);
      setRejectSettingDoc(null);
      if (rejectSettingKind === 'terms') await loadTerms();
      if (rejectSettingKind === 'scheme') await loadSchemes();
      if (rejectSettingKind === 'eligibility') await loadEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر رفض الإعداد', 'error');
    }
  };

  const handleDeleteSetting = async (doc: PayConfigDoc, kind: 'terms' | 'scheme' | 'eligibility') => {
    if (!confirm(`هل أنت متأكد من حذف هذه المسودة (${doc.data?.name || doc.code}) نهائياً؟`)) return;
    try {
      await api.deletePaySetting(
        kind === 'terms' ? 'pay_terms' : kind === 'scheme' ? 'pay_scheme' : 'pay_eligibility',
        appSlug,
        doc.id
      );
      showToast('تم حذف المسودة بنجاح من سجلات النظام', 'success');
      if (kind === 'terms') await loadTerms();
      if (kind === 'scheme') await loadSchemes();
      if (kind === 'eligibility') await loadEligibility();
    } catch (err: unknown) {
      showToast(err instanceof Error ? err.message : 'تعذر حذف المسودة', 'error');
    }
  };

  // Preview assignments import
  const handlePreviewAssignments = async () => {
    if (!assignmentFormId.trim()) {
      showToast('يرجى إدخال معرّف النموذج أو الحملة', 'warning');
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
      showToast(`تمت معاينة خيارات ${res.rows?.length ?? 0} مندوب بنجاح`, 'success');
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
  const handleAdvanceCycle = async (cycle: PayCycleItem) => {
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

  // Capture Cycle Inputs (BR §39.2)
  const handleCaptureCycleInputs = async (cycle: PayCycleItem) => {
    setCapturingInputs(true);
    setCaptureError(null);
    try {
      const updated = await api.capturePayCycleInputs(cycle.id);
      setCyclesList((prev) =>
        prev.length > 0 ? prev.map((c) => (c.id === updated.id ? updated : c)) : [updated]
      );
      showToast('تم التقاط وتثبيت مدخلات الدورة بنجاح', 'success');
    } catch (err: unknown) {
      const status =
        err instanceof ApiError
          ? err.status
          : typeof err === 'object' && err !== null && 'status' in err && typeof err.status === 'number'
          ? err.status
          : undefined;
      const msg = err instanceof Error && err.message ? err.message : 'تعذر التقاط مدخلات الدورة';
      setCaptureError({ status, message: msg });
      showToast(msg, status === 503 ? 'warning' : 'error');
    } finally {
      setCapturingInputs(false);
    }
  };

  // Create Global Settings Draft from Defaults
  const handleCreateGlobalSettingsDraft = async () => {
    try {
      await api.createGlobalPaySettings({
        effectiveFrom: `${selectedPeriod}-01`,
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
        period: selectedPeriod,
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
        period: selectedPeriod,
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
        period: selectedPeriod,
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

  const legacyPreviewFilterCols = useMemo(
    () => [
      {
        key: 'rider',
        header: 'المندوب',
        type: 'text' as const,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const rider = ridersList.find((y) => y.iqama === row.iqama);
          return `${rider?.name || ''} ${row.iqama}`.trim();
        },
      },
      {
        key: 'orders',
        header: 'الطلبات',
        type: 'number' as const,
        numeric: true,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          return Number((run?.result || row).orders ?? 0);
        },
      },
      {
        key: 'policyName',
        header: 'السياسة',
        type: 'list' as const,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          return (run?.result || row).policyName || '';
        },
      },
      {
        key: 'openingBalance',
        header: 'رصيد سابق',
        type: 'number' as const,
        numeric: true,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          return Number((run?.result || row).openingBalance ?? 0);
        },
      },
      {
        key: 'periodMovement',
        header: 'حركة الشهر',
        type: 'number' as const,
        numeric: true,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          return Number((run?.result || row).periodMovement ?? 0);
        },
      },
      {
        key: 'closingBalance',
        header: 'الرصيد الختامي',
        type: 'number' as const,
        numeric: true,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          return Number((run?.result || row).closingBalance ?? 0);
        },
      },
      {
        key: 'status',
        header: 'الموقف',
        type: 'list' as const,
        getValue: (row: LegacyPayrollPreviewRow) => {
          const run = legacyRuns.find((x) => x.iqama === row.iqama && x.period === selectedPeriod);
          if (run) return 'approved';
          if (row.blockers && row.blockers.length > 0) return 'blocked';
          return 'ready';
        },
        labelOf: (v: unknown) => (v === 'approved' ? 'معتمد' : v === 'blocked' ? 'يوجد موانع' : 'جاهز'),
        toneOf: (v: unknown) => (v === 'approved' ? 'success' : v === 'blocked' ? 'danger' : 'warning'),
      },
    ],
    [ridersList, legacyRuns, selectedPeriod]
  );
  const legacyPreviewFilter = useTableAutoFilter(legacyPreview, legacyPreviewFilterCols);

  const hsInvoicesFilterCols = useMemo(
    () => [
      { key: 'invoiceNumber', header: 'رقم الفاتورة', type: 'text' as const, getValue: (inv: HsInvoice) => (inv as any).invoiceNumber || inv.id || '' },
      { key: 'period', header: 'الشهر', type: 'date' as const, getValue: (inv: HsInvoice) => inv.period || '' },
      {
        key: 'amountExclVat',
        header: 'المبلغ قبل الضريبة',
        type: 'number' as const,
        numeric: true,
        getValue: (inv: HsInvoice) => Number((inv as any).amountExclVat ?? (inv.totalAmount || 0) - (inv.vat || 0)),
      },
      {
        key: 'vatAmount',
        header: 'الضريبة',
        type: 'number' as const,
        numeric: true,
        getValue: (inv: HsInvoice) => Number((inv as any).vatAmount ?? inv.vat ?? 0),
      },
      {
        key: 'totalAmount',
        header: 'الإجمالي',
        type: 'number' as const,
        numeric: true,
        getValue: (inv: HsInvoice) => Number(inv.totalAmount ?? 0),
      },
      {
        key: 'deductionsTotal',
        header: 'الخصومات',
        type: 'number' as const,
        numeric: true,
        getValue: (inv: HsInvoice) => Number((inv as any).deductionsTotal ?? 0),
      },
      {
        key: 'status',
        header: 'حالة الاعتماد',
        type: 'list' as const,
        getValue: (inv: HsInvoice) => inv.status || 'draft',
        labelOf: (v: unknown) => (v === 'approved' ? 'معتمدة' : v === 'posted' ? 'مرحلة' : 'مسودة'),
        toneOf: (v: unknown) => (v === 'approved' || v === 'posted' ? 'success' : 'warning'),
      },
    ],
    []
  );
  const hsInvoicesFilter = useTableAutoFilter(invoices, hsInvoicesFilterCols);

  // T11b: rider category (فئة) + rating (تقييم) from uploaded HS invoice lines — never manual settings.
  const hsRiderInvoiceRows = useMemo(() => {
    const rows: Array<{
      invoiceId: string;
      platformId: string;
      riderCategory: string;
      riderRating: string;
      basicPayment: number;
      period: string;
    }> = [];
    for (const inv of invoices as Array<HsInvoice & { lines?: Array<Record<string, unknown>> }>) {
      const lines = Array.isArray(inv.lines) ? inv.lines : [];
      for (const line of lines) {
        if (String(line.lineType || '') !== 'rider') continue;
        const facts = (line.invoiceFacts || {}) as Record<string, unknown>;
        const category = String(line.riderCategory || facts.category || '');
        const rating = String(line.riderRating || facts.rating || '');
        rows.push({
          invoiceId: inv.id,
          platformId: String(line.platformId || ''),
          riderCategory: category || 'غير متوفر في الفاتورة',
          riderRating: rating || 'غير متوفر في الفاتورة',
          basicPayment: Number(line.basicPayment ?? 0),
          period: inv.period || '',
        });
      }
    }
    return rows;
  }, [invoices]);
  const hsRiderFactsFilterCols = useMemo(
    () => [
      { key: 'platformId', header: 'معرف المندوب في المنصة', type: 'text' as const, getValue: (r: (typeof hsRiderInvoiceRows)[0]) => r.platformId },
      { key: 'riderCategory', header: 'فئة المندوب', type: 'list' as const, getValue: (r: (typeof hsRiderInvoiceRows)[0]) => r.riderCategory },
      { key: 'riderRating', header: 'تقييم المندوب', type: 'text' as const, getValue: (r: (typeof hsRiderInvoiceRows)[0]) => r.riderRating },
      { key: 'basicPayment', header: 'الدفعة الأساسية', type: 'number' as const, numeric: true, getValue: (r: (typeof hsRiderInvoiceRows)[0]) => r.basicPayment },
      { key: 'period', header: 'الشهر', type: 'date' as const, getValue: (r: (typeof hsRiderInvoiceRows)[0]) => r.period },
    ],
    []
  );
  const hsRiderFactsFilter = useTableAutoFilter(hsRiderInvoiceRows, hsRiderFactsFilterCols);

  const hsDisputesFilterCols = useMemo(
    () => [
      { key: 'id', header: 'رقم النزاع', type: 'text' as const, getValue: (d: HsDispute) => d.id || '' },
      { key: 'amount', header: 'المبلغ', type: 'number' as const, numeric: true, getValue: (d: HsDispute) => Number(d.amount ?? 0) },
      { key: 'reason', header: 'سبب الاعتراض', type: 'text' as const, getValue: (d: HsDispute) => d.reason || '' },
      {
        key: 'status',
        header: 'الحالة والمسار المحاسبي',
        type: 'list' as const,
        getValue: (d: HsDispute) => {
          if (d.status === 'escalated' || (d.status === 'resolved' && (d.outcome === 'platform_rejected' || (d as any).decision === 'platform_rejected'))) {
            return 'escalated';
          }
          if (d.status === 'resolved') return 'resolved';
          return 'open';
        },
        labelOf: (v: unknown) =>
          v === 'escalated'
            ? 'مرحّل لحساب النزاعات 112803 لحين فض النزاع'
            : v === 'resolved'
            ? 'مردود بإشعار دائن من المنصة'
            : 'مفتوح',
        toneOf: (v: unknown) => (v === 'escalated' ? 'danger' : v === 'resolved' ? 'success' : 'warning'),
      },
    ],
    []
  );
  const hsDisputesFilter = useTableAutoFilter(disputes, hsDisputesFilterCols);

  const hsAllocationsFilterCols = useMemo(
    () => [
      { key: 'id', header: 'السطر / المعرف', type: 'text' as const, getValue: (a: HsAllocation) => a.id || '' },
      { key: 'amount', header: 'المبلغ الإجمالي', type: 'number' as const, numeric: true, getValue: (a: HsAllocation) => Number(a.amount ?? 0) },
      { key: 'companyAmount', header: 'نصيب الشركة', type: 'number' as const, numeric: true, getValue: (a: HsAllocation) => Number(a.companyAmount ?? 0) },
      { key: 'riderAmount', header: 'نصيب المناديب', type: 'number' as const, numeric: true, getValue: (a: HsAllocation) => Number(a.riderAmount ?? 0) },
      {
        key: 'status',
        header: 'الموقف',
        type: 'list' as const,
        getValue: (a: HsAllocation) => (a.status === 'approved' ? 'approved' : 'pending'),
        labelOf: (v: unknown) => (v === 'approved' ? 'معتمد' : 'بانتظار الاعتماد'),
        toneOf: (v: unknown) => (v === 'approved' ? 'success' : 'warning'),
      },
      { key: 'requestedBy', header: 'مقدم المقترح', type: 'list' as const, getValue: (a: HsAllocation) => a.requestedBy || 'system' },
    ],
    []
  );
  const hsAllocationsFilter = useTableAutoFilter(allocations, hsAllocationsFilterCols);

  const policiesFilterCols = useMemo(
    () => [
      { key: 'name', header: 'اسم السياسة', type: 'text' as const, getValue: (p: any) => p.name || p.id || '' },
      {
        key: 'kind',
        header: 'النوع',
        type: 'list' as const,
        getValue: (p: any) => p.kind || '',
        labelOf: (v: unknown) => {
          const s = String(v ?? '').toLowerCase();
          if (s === 'scheme' || s === 'pay_scheme') return 'خطة احتساب';
          if (s === 'terms' || s === 'pay_terms') return 'شروط تشغيل';
          if (s === 'eligibility' || s === 'pay_eligibility') return 'قاعدة استحقاق';
          if (s === 'deduction') return 'سياسة استقطاع';
          if (s === 'allowance') return 'سياسة بدل';
          return String(v ?? '');
        },
      },
      {
        key: 'platform',
        header: 'المنصة',
        type: 'list' as const,
        getValue: (p: any) => p.platform || '',
        labelOf: (v: unknown) => {
          const s = String(v ?? '').toLowerCase();
          if (s === 'ninja') return 'نينجا';
          if (s === 'jahez') return 'جاهز';
          if (s === 'hungerstation') return 'هنقرستيشن';
          if (s === 'keeta') return 'كيتا';
          if (s === 'thechefz' || s === 'chefz') return 'ذا شيفز';
          return String(v ?? '');
        },
      },
      { key: 'effectiveFrom', header: 'تاريخ السريان', type: 'date' as const, getValue: (p: any) => p.effectiveFrom || '' },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (p: any) => (p.status === 'draft' ? 'draft' : 'approved'),
        labelOf: (v: unknown) => (v === 'draft' ? 'مسودة' : 'معتمدة'),
        toneOf: (v: unknown) => (v === 'draft' ? 'warning' : 'success'),
      },
    ],
    []
  );
  const policiesFilter = useTableAutoFilter(policies, policiesFilterCols);

  const ledgerRows = useMemo(() => ledgerData?.rows || [], [ledgerData?.rows]);
  const ledgerFilterCols = useMemo(
    () => [
      { key: 'period', header: 'الفترة', type: 'date' as const, getValue: (r: any) => r.period || '' },
      {
        key: 'kind',
        header: 'نوع الحركة',
        type: 'list' as const,
        getValue: (r: any) => r.kind || '',
        labelOf: (v: unknown) => {
          const s = String(v ?? '').toLowerCase();
          if (s === 'salary' || s === 'net_pay') return 'صافي راتب';
          if (s === 'deduction') return 'استقطاع';
          if (s === 'debt' || s === 'debt_installment') return 'قسط مديونية';
          if (s === 'advance') return 'سلفة';
          if (s === 'adjustment') return 'تسوية';
          if (s === 'payout') return 'صرف';
          if (s === 'fuel') return 'وقود';
          if (s === 'rent' || s === 'vehicle_rent') return 'إيجار مركبة';
          if (s === 'housing') return 'سكن';
          if (s === 'bonus') return 'مكافأة';
          return String(v ?? '');
        },
      },
      { key: 'amount', header: 'المبلغ', type: 'number' as const, numeric: true, getValue: (r: any) => Number(r.amount ?? 0) },
      { key: 'sourceRef', header: 'المرجع', type: 'text' as const, getValue: (r: any) => r.sourceRef || '' },
    ],
    []
  );
  const ledgerRowsFilter = useTableAutoFilter(ledgerRows, ledgerFilterCols);

  // Platforms List
  const platformsList = [
    { id: 'hungerstation', nameAr: 'هنقرستيشن', nameEn: 'HungerStation', tag: 'فواتير معتمدة', active: true },
    { id: 'keeta', nameAr: 'كيتا', nameEn: 'Keeta', tag: 'شرائح وعمولات', active: true },
    { id: 'ninja', nameAr: 'نينجا', nameEn: 'Ninja', tag: 'تارجت مقطوع', active: true },
    { id: 'chefz', nameAr: 'ذا شيفز', nameEn: 'The Chefz', tag: 'كشف دوري', active: true },
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
    { num: 6, title: 'مراجعة واعتماد المسير', desc: 'مراجعة أسطر المسير واعتماد المناديب تمهيداً للاعتماد النهائي' },
    { num: 7, title: 'ملف الصرف البنكي والترحيل', desc: 'إصدار ملف CSV لصرف الرواتب وترحيل القيود اليومية للدليل المستهدف' },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="الرواتب والمسيرات · منظومة المنصات الموحدة"
        eyebrow={PAYROLL_TAB_LABELS[mainTab]}
        description="إدارة ومراجعة فواتير المنصات، الدورة القائمة الفعالة للرواتب، محرر الأنظمة الداخلية، الاستقطاعات، واعتماد ملفات الصرف."
        primaryAction={
          can('salaries.simulation.run') && selectedPlatform !== 'amazon'
            ? {
                label: simBusy ? 'جاري إنشاء المحاكاة…' : 'بدء محاكاة جديدة',
                onClick: () => void startSimulation(),
                disabled: simBusy,
              }
            : mainTab === 'platforms' &&
                selectedPlatform === 'ninja' &&
                canShowImportStep(NINJA_OPS_IMPORT, { isAdmin, can })
              ? {
                  label: 'استيراد تشغيل نينجا',
                  icon: <Upload size={16} />,
                  onClick: () => setOpsImportStep(NINJA_OPS_IMPORT),
                }
              : undefined
        }
        moreActions={
          can('salaries.simulation.run') &&
          selectedPlatform !== 'amazon' &&
          mainTab === 'platforms' &&
          selectedPlatform === 'ninja' &&
          canShowImportStep(NINJA_OPS_IMPORT, { isAdmin, can })
            ? [
                {
                  label: 'استيراد تشغيل نينجا',
                  icon: <Upload size={16} />,
                  onClick: () => setOpsImportStep(NINJA_OPS_IMPORT),
                },
              ]
            : undefined
        }
      />
      {isSimulation ? (
        <div key={simTick} className="space-y-2">
          <SimulationBanner
            simulationId={simulationId}
            payoutBlocked={payoutBlocked}
            onChanged={clearSimulationUrl}
          />
          <p className="text-xs text-[var(--warning)]" title={SIMULATION_BLOCKED_HINT}>
            تصدير البنك وترحيل القيود وإرسال إشعار المندوب: {SIMULATION_BLOCKED_HINT}
          </p>
        </div>
      ) : null}
      {opsImportStep && opsImportStep.key === 'keeta_operations' && (
        <KeetaOperationsImportDialog
          open
          onClose={() => setOpsImportStep(null)}
          period={selectedPeriod}
          allowedActions={importAllowedActions}
        />
      )}
      {opsImportStep && opsImportStep.key !== 'keeta_operations' && (
        <ImportUploadDialog
          open={Boolean(opsImportStep)}
          onClose={() => setOpsImportStep(null)}
          step={opsImportStep}
          allowedActions={importAllowedActions}
        />
      )}

      {/* ======================================================== */}
      {/* SECTION: MUDAD SALARIES (رواتب مدد)                      */}
      {/* ======================================================== */}
      {mainTab === 'mudad' && <MudadPanel />}

      {/* ======================================================== */}
      {/* SECTION: NINJA INVOICE RECONCILE (BR §26, §31)           */}
      {/* ======================================================== */}
      {mainTab === 'ninja_upload' && (
        <div className="space-y-6">
          <NinjaInvoiceUploadScreen initialPeriod={selectedPeriod} platform="ninja" />
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
                  value={selectedPeriod}
                  onChange={(e) => setSelectedPeriod(e.target.value)}
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
                href={`/api/payroll/export.csv?period=${encodeURIComponent(selectedPeriod)}`}
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
              hint={`شهر ${selectedPeriod}`}
              icon={Users}
            />
            <StatCard
              label="كشوف معتمدة ومرحلة"
              value={legacyRuns.filter((r) => r.period === selectedPeriod).length}
              hint="مرحلة محاسبياً"
              icon={CheckCircle2}
            />
            <StatCard
              label="كشوف جاهزة للاعتماد"
              value={
                legacyPreview.filter(
                  (p) =>
                    (!p.blockers || p.blockers.length === 0) &&
                    !legacyRuns.some((r) => r.iqama === p.iqama && r.period === selectedPeriod)
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
              title={`لا توجد بيانات مسير لشهر ${selectedPeriod}`}
              description="ارفع مصدر التشغيل والتسوية وحدد سياسة حساب المناديب لاحتساب المسودة."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={legacyPreviewFilter} totalCount={legacyPreview.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="rider" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="orders" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="policyName" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="openingBalance" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="periodMovement" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="closingBalance" className="p-3 text-start" />
                      <AutoFilterTh controller={legacyPreviewFilter} colKey="status" className="p-3 text-start" />
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {legacyPreviewFilter.rows.map((row) => {
                      const run = legacyRuns.find(
                        (x) => x.iqama === row.iqama && x.period === selectedPeriod
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
                            {formatSar(displayRow.openingBalance || 0)}
                          </td>
                          <td className="p-3 num font-medium">
                            {formatSar(displayRow.periodMovement || 0)}
                          </td>
                          <td
                            className={`p-3 num font-bold ${
                              (displayRow.closingBalance || 0) < 0
                                ? 'text-[var(--danger)]'
                                : 'text-[var(--money-pos)]'
                            }`}
                          >
                            {formatSar(displayRow.closingBalance || 0)}
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
                  onClick={() => selectPlatformInUrl(p.id)}
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
              { id: 'parent_terms', label: 'الشروط الأصلية' },
              { id: 'internal_schemes', label: 'الأنظمة الداخلية' },
              ...(payAppHasEligibility(appSlug)
                ? [{ id: 'eligibility' as const, label: 'الأهلية والضوابط' }]
                : []),
              { id: 'accounts', label: 'الحسابات والمعرفات' },
            ].map((sub) => (
              <button
                key={sub.id}
                type="button"
                onClick={() => selectAppSubInUrl(sub.id as PayAppSubTab)}
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
          {selectedPlatform === 'hungerstation' && appSubTab === 'settlements' && canShowImportStep(HS_OPS_IMPORT, { isAdmin, can }) && (
            <button
              type="button"
              data-testid="import-hs-operations"
              onClick={() => setOpsImportStep(HS_OPS_IMPORT)}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold border border-[var(--line)] mb-3"
            >
              <Upload size={14} />
              استيراد تشغيل هنقرستيشن
            </button>
          )}
          {selectedPlatform === 'hungerstation' && appSubTab === 'settlements' && (
            <div className="space-y-6">
              {/* Stat Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
                <StatCard
                  label="إجمالي الصفحة الحالية"
                  value={formatSar(totalInvoicesAmount)}
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
                      <AutoFilterBar controller={hsInvoicesFilter} totalCount={invoices.length} />
                      <div className="overflow-x-auto">
                        <table className="w-full text-start text-xs sm:text-sm">
                          <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                            <tr>
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="invoiceNumber" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="period" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="amountExclVat" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="vatAmount" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="totalAmount" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="deductionsTotal" className="p-3 text-start" />
                              <AutoFilterTh controller={hsInvoicesFilter} colKey="status" className="p-3 text-start" />
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-[var(--line)]">
                            {hsInvoicesFilter.rows.map((inv) => (
                              <tr key={inv.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--ink)]">{(inv as any).invoiceNumber || inv.id}</td>
                                <td className="p-3 font-mono">{inv.period}</td>
                                <td className="p-3 num">
                                  {formatSar(((inv as any).amountExclVat || (inv.totalAmount || 0) - (inv.vat || 0)))}
                                </td>
                                <td className="p-3 num text-[var(--muted)]">
                                  {formatSar(((inv as any).vatAmount || inv.vat || 0))}
                                </td>
                                <td className="p-3 num font-bold text-[var(--accent)]">
                                  {formatSar((inv.totalAmount || 0))}
                                </td>
                                <td className="p-3 num text-[var(--danger)]">
                                  {formatSar(((inv as any).deductionsTotal || 0))}
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
                  {hsRiderInvoiceRows.length > 0 && (
                    <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                      <div className="p-3 border-b border-[var(--line)]">
                        <h4 className="font-bold text-sm text-[var(--ink)]">وقائع الفاتورة · فئة المندوب وتقييمه</h4>
                        <p className="text-[11px] text-[var(--muted)] mt-0.5">من ملف فاتورة هنقرستيشن فقط (الشريحة/التقييم) — ليست إعدادات يدوية.</p>
                      </div>
                      <AutoFilterBar controller={hsRiderFactsFilter} totalCount={hsRiderInvoiceRows.length} />
                      <div className="overflow-x-auto">
                        <table className="w-full text-start text-xs sm:text-sm">
                          <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                            <tr>
                              <AutoFilterTh controller={hsRiderFactsFilter} colKey="platformId" className="p-3 text-start" />
                              <AutoFilterTh controller={hsRiderFactsFilter} colKey="riderCategory" className="p-3 text-start" />
                              <AutoFilterTh controller={hsRiderFactsFilter} colKey="riderRating" className="p-3 text-start" />
                              <AutoFilterTh controller={hsRiderFactsFilter} colKey="basicPayment" className="p-3 text-start" />
                              <AutoFilterTh controller={hsRiderFactsFilter} colKey="period" className="p-3 text-start" />
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-[var(--line)]">
                            {hsRiderFactsFilter.rows.map((row) => (
                              <tr key={`${row.invoiceId}-${row.platformId}`} className="hover:bg-[var(--row-hover)]">
                                <td className="p-3 font-mono">{row.platformId}</td>
                                <td className="p-3 font-bold">{row.riderCategory}</td>
                                <td className="p-3 font-mono">{row.riderRating}</td>
                                <td className="p-3 num">{formatSar(row.basicPayment)}</td>
                                <td className="p-3 font-mono">{row.period}</td>
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
                    <AutoFilterBar controller={hsDisputesFilter} totalCount={disputes.length} />
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <AutoFilterTh controller={hsDisputesFilter} colKey="id" className="p-3 text-start" />
                            <AutoFilterTh controller={hsDisputesFilter} colKey="amount" className="p-3 text-start" />
                            <AutoFilterTh controller={hsDisputesFilter} colKey="reason" className="p-3 text-start" />
                            <AutoFilterTh controller={hsDisputesFilter} colKey="status" className="p-3 text-start" />
                            <th className="p-3 text-start">الإجراء</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {hsDisputesFilter.rows.length === 0 ? (
                            <tr>
                              <td colSpan={5} className="p-6 text-center text-[var(--muted)] text-xs">
                                لا توجد نزاعات CAR مفتوحة لهذا الشهر.
                              </td>
                            </tr>
                          ) : (
                            hsDisputesFilter.rows.map((d) => (
                              <tr key={d.id} className="hover:bg-[var(--row-hover)] transition">
                                <td className="p-3 font-mono font-bold text-[var(--ink)]">{d.id}</td>
                                <td className="p-3 num font-bold text-[var(--danger)]">
                                  {formatSar((d.amount || 0))}
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
                    <AutoFilterBar controller={hsAllocationsFilter} totalCount={allocations.length} />
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="id" className="p-3 text-start" />
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="amount" className="p-3 text-start" />
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="companyAmount" className="p-3 text-start" />
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="riderAmount" className="p-3 text-start" />
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="status" className="p-3 text-start" />
                            <AutoFilterTh controller={hsAllocationsFilter} colKey="requestedBy" className="p-3 text-start" />
                            <th className="p-3 text-start">الإجراءات</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {hsAllocationsFilter.rows.length === 0 ? (
                            <tr>
                              <td colSpan={7} className="p-6 text-center text-[var(--muted)] text-xs">
                                لا توجد اقتطاعات TGA مسجلة لهذا الشهر.
                              </td>
                            </tr>
                          ) : (
                            hsAllocationsFilter.rows.map((alloc) => {
                              const isMaker =
                                alloc.requestedBy &&
                                user &&
                                alloc.requestedBy.toLowerCase() === user.username.toLowerCase();
                              return (
                                <tr key={alloc.id} className="hover:bg-[var(--row-hover)] transition">
                                  <td className="p-3 font-mono font-bold text-[var(--accent)]">{alloc.id}</td>
                                  <td className="p-3 num font-bold">
                                    {formatSar((alloc.amount || 0))}
                                  </td>
                                  <td className="p-3 num text-[var(--ink)]">
                                    {formatSar((alloc.companyAmount || 0))}
                                  </td>
                                  <td className="p-3 num text-[var(--muted)]">
                                    {formatSar((alloc.riderAmount || 0))}
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
              {canShowImportStep(NINJA_OPS_IMPORT, { isAdmin, can }) && (
                <button
                  type="button"
                  data-testid="import-ninja-operations"
                  onClick={() => setOpsImportStep(NINJA_OPS_IMPORT)}
                  className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold border border-[var(--line)]"
                >
                  <Upload size={14} />
                  استيراد تشغيل نينجا
                </button>
              )}
              <ValidateGapsPanel defaultPeriod={selectedPeriod} />
              <NinjaInvoiceUploadScreen initialPeriod={selectedPeriod} platform="ninja" />
            </div>
          )}

          {/* Case C: Keeta Settlements Tab (BR §26, §31 / R9 W1) */}
          {selectedPlatform === 'keeta' && appSubTab === 'settlements' && (
            <div className="space-y-6">
              {canShowImportStep(KEETA_OPS_IMPORT, { isAdmin, can }) && (
                <button
                  type="button"
                  data-testid="import-keeta-operations"
                  onClick={() => setOpsImportStep(KEETA_OPS_IMPORT)}
                  className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold border border-[var(--line)]"
                >
                  <Upload size={14} />
                  استيراد تشغيل كيتا
                </button>
              )}
              <NinjaInvoiceUploadScreen initialPeriod={selectedPeriod} platform="keeta" />
            </div>
          )}

          {/* Chefz review */}
          {(selectedPlatform === 'thechefz' || selectedPlatform === 'chefz') && appSubTab === 'settlements' && (
            <div className="space-y-4">
              <ChefzSettlementsPanel />
              <ChefzReviewPanel />
            </div>
          )}

          {/* Jahez treasury link + stub */}
          {selectedPlatform === 'jahez' && appSubTab === 'settlements' && (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3 text-xs">
              <p className="font-bold text-sm">جاهز: المطابقة وعمليات الخزينة في صفحة الخزينة</p>
              <p className="text-[var(--muted)]">تقارير §37 والتحويلات والتسويات والعهدة متاحة من تبويب جاهز في الخزينة.</p>
              <Link href="/treasury/?tab=jahez" className="inline-flex px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold">فتح خزينة جاهز</Link>
            </div>
          )}

          {/* Fallback for other platforms settlements */}
          {selectedPlatform !== 'hungerstation' && selectedPlatform !== 'ninja' && selectedPlatform !== 'keeta' && selectedPlatform !== 'jahez' && selectedPlatform !== 'thechefz' && selectedPlatform !== 'chefz' && appSubTab === 'settlements' && (
            <EmptyState
              title={`لا توجد فواتير أو تسويات مسجلة لمنصة ${platformsList.find((p) => p.id === selectedPlatform)?.nameAr || selectedPlatform}`}
              description="يمكنك إدارة الشروط الأصلية والأنظمة الداخلية والحسابات والمعرفات لهذه المنصة من التبويبات أعلاه."
            />
          )}

          {/* ======================================================== */}
          {/* PARENT TERMS SUB-TAB — display only (Ghazal 2026-11-27)  */}
          {/* ======================================================== */}
          {appSubTab === 'parent_terms' && (
            <ParentTermsDisplayPanel
              appSlug={appSlug}
              appNameAr={platformsList.find((p) => p.id === selectedPlatform)?.nameAr || appSlug}
              period={selectedPeriod}
            />
          )}

          {/* ======================================================== */}
          {/* INTERNAL SCHEMES SUB-TAB (F1)                            */}
          {/* ======================================================== */}
          {appSubTab === 'internal_schemes' && (
            <div className="space-y-6">
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                <div>
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    الأنظمة الداخلية · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                  </h3>
                  <p className="text-xs text-[var(--muted)] mt-1">
                    {usesOmarCommission
                      ? 'عمولة جاهز/شيفز تُقرأ من نظام عمر (قراءة فقط) لكل مدينة؛ لا إعادة إدخال يدوي.'
                      : 'تحديد مصفوفة الفئات الست الصريحة (كفالة × مركبة) ونوع النظام ودور المحاسبة (أصيل / وكيل).'}
                  </p>
                </div>

                {!usesOmarCommission && (
                <div className="flex items-center gap-2">
                  {appSlug === 'ninja' && (
                    <button
                      type="button"
                      disabled={seedingDefaults}
                      onClick={() => handleSeedDefaults(appSlug)}
                      className="px-3.5 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--raised)] transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                    >
                      <span>استيراد الأنظمة الافتراضية (مسودة)</span>
                    </button>
                  )}
                  <button
                    type="button"
                    disabled={copyingMonth}
                    onClick={() => handleCopyMonth(appSlug)}
                    className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                  >
                    <Copy size={14} />
                    <span>اعتماد نفس إعدادات الشهر السابق</span>
                  </button>
                  <button
                    type="button"
                    onClick={handleOpenSchemeForm}
                    className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                  >
                    + إضافة نظام داخلي
                  </button>
                </div>
                )}
              </div>

              {usesOmarCommission && (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="p-4 border-b border-[var(--line)] flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <h4 className="font-bold text-sm text-[var(--ink)]">عمولة نظام عمر (قراءة فقط)</h4>
                      <p className="text-[11px] text-[var(--muted)] mt-0.5">
                        المصدر: {omarCommission?.source || 'omar_erp.app.PlatformDailyCommissionRates'}
                        {omarCommission?.readAt ? ` · وقت القراءة ${omarCommission.readAt}` : ''}
                      </p>
                    </div>
                    <button
                      type="button"
                      onClick={loadOmarCommission}
                      className="px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-bold hover:bg-[var(--raised)] cursor-pointer inline-flex items-center gap-1.5"
                    >
                      <RefreshCw size={12} />
                      تحديث
                    </button>
                  </div>
                  {loadingOmarCommission ? (
                    <div className="p-6 space-y-2"><Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" /></div>
                  ) : omarCommissionError ? (
                    <ErrorState title="تعذر قراءة عمولة نظام عمر" message={omarCommissionError} onRetry={loadOmarCommission} />
                  ) : (
                    <div className="p-3 space-y-2">
                      {(omarCommission?.schemeRows || []).map((row, idx) => (
                        <div
                          key={`${row.identifierType}-${row.cityCode || 'all'}-${idx}`}
                          className="grid grid-cols-2 sm:grid-cols-5 gap-2 p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/30 text-xs"
                        >
                          <div><span className="text-[var(--muted)]">النوع </span><span className="font-bold">{row.identifierType}</span></div>
                          <div><span className="text-[var(--muted)]">المدينة </span><span>{row.cityNameAr || '—'}</span></div>
                          <div><span className="text-[var(--muted)]">الحساب </span><span className="font-mono">{row.kind === 'percentage' ? 'نسبة مئوية' : row.kind === 'fixed' ? 'ثابت يومي' : row.kind || '—'}</span></div>
                          <div className="num font-mono">
                            {row.available && row.rate != null
                              ? row.kind === 'percentage'
                                ? `${row.rate}%`
                                : formatSar(Number(row.rate))
                              : '—'}
                          </div>
                          <div>
                            {row.available ? (
                              <span className="text-[var(--money-pos)] font-bold">من نظام عمر</span>
                            ) : (
                              <span className="text-[var(--warning)] font-bold">{row.message || 'غير متوفر في نظام عمر'}</span>
                            )}
                          </div>
                        </div>
                      ))}
                      {(omarCommission?.missing?.length || 0) > 0 && (
                        <div className="p-2 text-[11px] text-[var(--warning)]">
                          للملف عند عمر: {(omarCommission?.missing || []).map((m) => `${m.cityNameAr} (${m.omarFile || 'PlatformDailyCommissionRates'})`).join(' · ')}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              {!usesOmarCommission && (loadingSchemes ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-10 w-full" />
                  <Skeleton className="h-10 w-full" />
                </div>
              ) : schemesError ? (
                <ErrorState title="تعذر تحميل الأنظمة" message={schemesError} onRetry={loadSchemes} />
              ) : schemesList.length === 0 ? (
                <EmptyState
                  title={`لا توجد أنظمة داخلية مسجلة لشهر ${selectedPeriod}`}
                  description="لم يتم العثور على أنظمة داخلية مسجلة لهذا التطبيق في الشهر المحدد. انسخ الشهر السابق أو أنشئ نظاماً داخلياً جديداً."
                  action={{
                    label: 'اعتماد نفس إعدادات الشهر السابق',
                    onClick: () => handleCopyMonth(appSlug),
                  }}
                />
              ) : (
                <div className="space-y-4">
                  {schemesList.map((sc) => {
                    const isApproved = sc.status === 'approved';
                    const isMaker = isSelfMaker(sc);
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
                              {isApproved ? `معتمدة من @${sc.approvedBy}` : 'بانتظار الاعتماد'}
                            </span>
                            {sc.createdBy && (
                              <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--raised)] font-mono text-[var(--muted)]">
                                أنشأه: @{sc.createdBy}
                              </span>
                            )}
                            <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--raised)] font-mono">
                              صفة: {sc.data?.accountingRole === 'principal' ? 'أصيل' : 'وكيل'}
                            </span>
                            <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--accent)]/15 text-[var(--accent)] font-mono">
                              نوع: {sc.data?.type || 'daily_commission'}
                            </span>
                            {isApproved && (
                              <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                                <Lock size={12} />
                                <span>للقراءة فقط</span>
                              </span>
                            )}
                          </div>

                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              onClick={() => handleEditScheme(sc)}
                              className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--raised)] transition cursor-pointer"
                            >
                              {isApproved ? 'إنشاء إصدار معدّل' : 'تعديل الإعداد'}
                            </button>

                            {!isApproved && (
                              <>
                                <button
                                  type="button"
                                  disabled={isMaker}
                                  title={isMaker ? 'لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة' : 'اعتماد النظام'}
                                  onClick={() => handleApproveScheme(sc)}
                                  className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer shadow-xs"
                                >
                                  اعتماد النظام
                                </button>
                                <button
                                  type="button"
                                  onClick={() => handleOpenRejectSetting(sc, 'scheme')}
                                  className="px-3 py-1.5 rounded-xl border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                >
                                  رفض
                                </button>
                                <button
                                  type="button"
                                  onClick={() => handleDeleteSetting(sc, 'scheme')}
                                  className="px-2.5 py-1.5 rounded-xl text-[var(--danger)] text-xs hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                  title="حذف المسودة"
                                >
                                  حذف
                                </button>
                              </>
                            )}
                          </div>
                        </div>

                        {/* 6 Category cells table */}
                        <SchemeCategoriesTable categories={categories} />
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>
          )}

          {/* ======================================================== */}
          {/* ELIGIBILITY RULES SUB-TAB (F1)                           */}
          {/* ======================================================== */}
          {appSubTab === 'eligibility' && payAppHasEligibility(appSlug) && (
            <div className="space-y-6">
                <div className="space-y-6">
                  <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
                    <div>
                      <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                        شروط وضوابط الأهلية · منصة {platformsList.find((p) => p.id === selectedPlatform)?.nameAr}
                      </h3>
                      <p className="text-xs text-[var(--muted)] mt-1">
                        شروط الحد الأدنى وساعات العمل اليومية وتارجت الطلبات المرتبطة بالتطبيق حصرياً.
                      </p>
                    </div>

                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => setShowCycleReview((prev) => !prev)}
                        className={`px-3.5 py-2 rounded-xl text-xs font-bold transition inline-flex items-center gap-1.5 cursor-pointer ${
                          showCycleReview
                            ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                            : 'bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)]'
                        }`}
                      >
                        <span>{showCycleReview ? 'إخفاء مقارنة الدورة' : 'مقارنة الدورة والفروقات'}</span>
                      </button>

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
                        onClick={handleOpenEligibilityForm}
                        className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
                      >
                        + إعداد جدول شروط الأهلية
                      </button>
                    </div>
                  </div>

                  {showCycleReview && (
                    <PaySettingsCycleReview
                      period={selectedPeriod}
                      previousPeriod={getPreviousMonth(selectedPeriod)}
                      appSlug={appSlug}
                      appNameAr={platformsList.find((p) => p.id === selectedPlatform)?.nameAr || appSlug}
                      currentUser={user?.username || ''}
                      termsList={termsList}
                      schemesList={schemesList}
                      eligibilityList={eligibilityList}
                      prevTermsList={prevTermsList}
                      prevSchemesList={prevSchemesList}
                      prevEligibilityList={prevEligibilityList}
                      onCopyPreviousMonth={() => handleCopyMonth(appSlug)}
                      copying={copyingMonth}
                      onEditSetting={(doc, kind) => {
                        if (kind === 'terms') handleEditTerms(doc);
                        if (kind === 'scheme') handleEditScheme(doc);
                        if (kind === 'eligibility') handleEditEligibility(doc);
                      }}
                      onApproveSetting={(doc, kind) => {
                        if (kind === 'terms') handleApproveTerms(doc);
                        if (kind === 'scheme') handleApproveScheme(doc);
                        if (kind === 'eligibility') handleApproveEligibility(doc);
                      }}
                      onRejectSetting={(doc, kind) => handleOpenRejectSetting(doc, kind)}
                    />
                  )}

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
                        const isMaker = isSelfMaker(el);
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
                                  {isApproved ? `معتمدة من @${el.approvedBy}` : 'بانتظار الاعتماد'}
                                </span>
                                {el.createdBy && (
                                  <span className="text-xs px-2 py-0.5 rounded-full bg-[var(--raised)] font-mono text-[var(--muted)]">
                                    أنشأه: @{el.createdBy}
                                  </span>
                                )}
                                {isApproved && (
                                  <span className="inline-flex items-center gap-1 text-[11px] text-[var(--muted)]">
                                    <Lock size={12} />
                                    <span>للقراءة فقط</span>
                                  </span>
                                )}
                              </div>

                              <div className="flex items-center gap-2">
                                <button
                                  type="button"
                                  onClick={() => handleEditEligibility(el)}
                                  className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--raised)] transition cursor-pointer"
                                >
                                  {isApproved ? 'إنشاء إصدار معدّل' : 'تعديل الإعداد'}
                                </button>

                                {!isApproved && (
                                  <>
                                    <button
                                      type="button"
                                      disabled={isMaker}
                                      title={isMaker ? 'لا يجوز أن يعتمد نفس الشخص الذي أنشأ المسودة' : 'اعتماد الأهلية'}
                                      onClick={() => handleApproveEligibility(el)}
                                      className="px-4 py-1.5 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer shadow-xs"
                                    >
                                      اعتماد الأهلية
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() => handleOpenRejectSetting(el, 'eligibility')}
                                      className="px-3 py-1.5 rounded-xl border border-[var(--danger)]/30 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                    >
                                      رفض
                                    </button>
                                    <button
                                      type="button"
                                      onClick={() => handleDeleteSetting(el, 'eligibility')}
                                      className="px-2.5 py-1.5 rounded-xl text-[var(--danger)] text-xs hover:bg-[var(--danger)]/10 transition cursor-pointer"
                                      title="حذف المسودة"
                                    >
                                      حذف
                                    </button>
                                  </>
                                )}
                              </div>
                            </div>

                            {/* Rules table */}
                            <EligibilityRulesMiniTable rules={rules} />
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
            </div>
          )}

          {/* ======================================================== */}
          {/* SCHEME CHOICES & ASSIGNMENTS SUB-TAB (F1 / R4.3 / S4)     */}
          {/* ======================================================== */}
          {appSubTab === 'accounts' && (
            <RiderAssignmentsWorkspace
              appSlug={appSlug}
              appNameAr={platformsList.find((p) => p.id === selectedPlatform)?.nameAr || selectedPlatform}
              period={selectedPeriod}
              schemesList={schemesList}
              ridersList={ridersList}
              onOpenImportModal={() => {
                setAssignmentsImportModalOpen(true);
                setAssignmentPreviewResult(null);
              }}
            />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* SECTION 2: PAYROLL CYCLE & REVIEW DRAFT (0-7) (F1 / A5)   */}
      {/* ======================================================== */}
      {mainTab === 'cycle' && (
        <div className="space-y-6">
          {/* Sub-tab view toggle */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-2 bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-xs">
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setCycleViewMode('draft')}
                className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-bold transition flex items-center gap-2 cursor-pointer ${
                  cycleViewMode === 'draft'
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                    : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <FileSpreadsheet size={15} />
                <span>مسودة المراجعة المقترحة</span>
                {reviewDraft?.blockers && (
                  <span
                    className={`px-2 py-0.5 rounded-full text-[10px] font-mono font-bold ${
                      reviewDraft.blockers.length === 0
                        ? 'bg-[var(--money-pos)]/20 text-[var(--money-pos)]'
                        : 'bg-[var(--danger)] text-white'
                    }`}
                  >
                    {reviewDraft.blockers.length === 0 ? '0 موانع' : `${reviewDraft.blockers.length} مانع`}
                  </span>
                )}
              </button>

              <button
                type="button"
                onClick={() => setCycleViewMode('active')}
                className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-bold transition flex items-center gap-2 cursor-pointer ${
                  cycleViewMode === 'active'
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                    : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <RefreshCw size={15} />
                <span>دورة الرواتب ومراحل المسير (0-7)</span>
                {activeCycle && (
                  <span className="px-2 py-0.5 rounded-full text-[10px] font-mono font-bold bg-[var(--raised)] text-[var(--accent)] border border-[var(--line)]">
                    مرحلة {activeCycle.stage}
                  </span>
                )}
              </button>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="month"
                value={selectedPeriod}
                onChange={(e) => setSelectedPeriod(e.target.value)}
                className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
              />
              <button
                type="button"
                onClick={() => {
                  loadCycles();
                  loadReviewDraft();
                  loadDuesSplit();
                }}
                className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
                title="تحديث البيانات"
              >
                <RefreshCw size={14} className={loadingDraft || loadingCycles || loadingDuesSplit ? 'animate-spin' : ''} />
              </button>
            </div>
          </div>

          {/* NFLOW1: dues split stage after invoice approval */}
          {cycleViewMode === 'draft' && (
            <div id="dues-split-stage" className="mb-6 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h3 className="text-sm font-bold text-[var(--ink)]">فصل مستحقات اليوزرات وتوزيعها على المناديب</h3>
                  <p className="text-[11px] text-[var(--muted)] mt-0.5">قراءة تشغيل عمر (قراءة فقط) + أنظمة داخلية؛ الفاتورة لا تتغيّر.</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={loadDuesSplit}
                    disabled={loadingDuesSplit}
                    className="px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs font-bold text-[var(--ink)] cursor-pointer disabled:opacity-50"
                  >
                    تحديث التوزيع
                  </button>
                  <button
                    type="button"
                    onClick={handleApplyDefaultSettings}
                    disabled={applyingDefaults}
                    className="px-3 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold cursor-pointer disabled:opacity-50"
                  >
                    {applyingDefaults ? 'جاري التطبيق…' : 'تطبيق الافتراضي بنقرة واحدة'}
                  </button>
                </div>
              </div>
              {duesSplitError && <p className="text-xs text-[var(--danger)]">{duesSplitError}</p>}
              {duesSplit && (
                <>
                  <div className="grid grid-cols-2 sm:grid-cols-6 gap-2">
                    <StatCard label="يوزرات" value={String(duesSplit.counts.users)} icon={Hash} />
                    <StatCard label="توزيع مناديب" value={String(duesSplit.counts.riderSplits)} icon={Users} />
                    <StatCard label="وقود" value={String(duesSplit.counts.fuelRows)} icon={Fuel} />
                    <StatCard label="عهدة مركبة" value={String(duesSplit.counts.custodyRows)} icon={Car} />
                    <StatCard label="سطور محاسب" value={String(duesSplit.counts.accountantLines)} icon={FileText} />
                    <StatCard label="إعداد متبقٍ" value={String(duesSplit.counts.setupLeft)} icon={Settings} />
                  </div>
                  {duesSplit.setupChecklist.length > 0 && (
                    <div className="p-3 rounded-xl border border-amber-500/30 bg-amber-500/5 space-y-1">
                      <p className="text-xs font-bold text-[var(--ink)]">قائمة إعداد قصيرة (ما لا يوجد في نظام عمر)</p>
                      {duesSplit.setupChecklist.map((item, i) => (
                        <p key={i} className="text-[11px] text-[var(--muted)]">• {item.message}{item.hint ? ` — ${item.hint}` : ''}</p>
                      ))}
                    </div>
                  )}
                  {duesSplit.accountantLines.length > 0 && (
                    <div className="p-3 rounded-xl border border-[var(--line)] space-y-1">
                      <p className="text-xs font-bold text-[var(--ink)]">سطور المحاسب (فروق / بلا تشغيل) — قبول الفاتورة افتراضيًا</p>
                      {duesSplit.accountantLines.slice(0, 20).map((line, i) => (
                        <p key={i} className="text-[11px] text-[var(--muted)]">• {String((line as { message?: string }).message || line.kind || '')}</p>
                      ))}
                      {duesSplit.accountantLines.length > 20 && (
                        <p className="text-[11px] text-[var(--muted)]">… و{duesSplit.accountantLines.length - 20} سطرًا إضافيًا</p>
                      )}
                    </div>
                  )}
                </>
              )}
            </div>
          )}

          {/* VIEW 1: REVIEW DRAFT (A5) */}
          {cycleViewMode === 'draft' && (
            <div className="space-y-6">
              {loadingDraft ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-20 w-full" />
                  <Skeleton className="h-40 w-full" />
                </div>
              ) : draftError ? (
                <ErrorState
                  title="تعذر تحميل مسودة المراجعة المقترحة"
                  message={draftError}
                  onRetry={loadReviewDraft}
                />
              ) : !reviewDraft ? (
                <EmptyState
                  title="لا توجد مسودة مراجعة مقترحة"
                  description={
                    selectedPlatform === 'keeta'
                      ? 'ارفع واعتمد فاتورة كيتا واستورد تشغيل السجل أولاً لتوليد مسودة المراجعة المقترحة.'
                      : 'ارفع واعتمد فاتورة نينجا أولاً لتوليد مسودة المراجعة المقترحة.'
                  }
                  action={{
                    label: selectedPlatform === 'keeta' ? 'انتقل لتسويات كيتا' : 'انتقل لرفع فاتورة نينجا',
                    onClick: () =>
                      selectedPlatform === 'keeta'
                        ? navigateMainTab('platforms')
                        : navigateMainTab('ninja_upload'),
                  }}
                />
              ) : (
                <div className="space-y-6">
                  {/* Draft KPI Cards */}
                  <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
                    <StatCard
                      label="إجمالي الفاتورة"
                      value={`${formatSar((reviewDraft.totals?.gross || 0))}`}
                      icon={Coins}
                    />
                    <StatCard
                      label="الاستقطاعات والخصم"
                      value={`${formatSar((reviewDraft.totals?.deductions || 0))}`}
                      hint="خصومات المنصة"
                      icon={DollarSign}
                    />
                    <StatCard
                      label="ضريبة القيمة المضافة"
                      value={`${formatSar((reviewDraft.totals?.vat || 0))}`}
                      icon={Scale}
                    />
                    <StatCard
                      label="صافي الفاتورة المستحق"
                      value={formatSar(
                        (reviewDraft.totals?.gross || 0) -
                          (reviewDraft.totals?.deductions || 0) +
                          (reviewDraft.totals?.vat || 0),
                      )}
                      icon={ShieldCheck}
                    />
                    <StatCard
                      label="موانع الاستحقاق النشطة"
                      value={reviewDraft.blockers?.length || 0}
                      hint={(reviewDraft.blockers?.length || 0) > 0 ? 'يتطلب تسوية' : 'جاهز'}
                      icon={AlertTriangle}
                    />
                  </div>

                  {/* Next-Step Card for Review Draft (R9 W3) */}
                  <div
                    id="draft-next-step"
                    className="p-4 rounded-2xl border-2 border-[var(--accent)]/40 bg-[var(--accent)]/5 flex flex-wrap items-center justify-between gap-3"
                  >
                    <div className="space-y-1">
                      <span className="text-[11px] font-bold text-[var(--accent)]">الخطوة التالية</span>
                      <div className="flex items-center gap-2 flex-wrap">
                        <p className="text-sm font-bold text-[var(--ink)]">
                          {reviewDraft.blockers && reviewDraft.blockers.length > 0
                            ? `يوجد ${reviewDraft.blockers.length} مانع يمنع إنشاء دورة الرواتب لشهر ${selectedPeriod} — عالج الموانع أولاً`
                            : reviewDraft.canCreate
                            ? `مسودة المراجعة جاهزة (0 موانع) — أنشئ دورة الرواتب لشهر ${selectedPeriod}`
                            : `لا توجد فواتير معتمدة في المسودة لشهر ${selectedPeriod} — ارفع واعتمد الفاتورة أولاً`}
                        </p>
                        <span
                          className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                            reviewDraft.canCreate && (!reviewDraft.blockers || reviewDraft.blockers.length === 0)
                              ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                              : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                          }`}
                        >
                          {reviewDraft.canCreate && (!reviewDraft.blockers || reviewDraft.blockers.length === 0)
                            ? 'جاهز للإنشاء (0 موانع)'
                            : 'يتطلب تسوية الموانع'}
                        </span>
                      </div>
                      <p className="text-xs text-[var(--muted)]">
                        {reviewDraft.blockers && reviewDraft.blockers.length > 0
                          ? `السبب الأول: ${reviewDraft.blockers[0].message} (${payrollBlockerLabelAr(reviewDraft.blockers[0].code)}). استخدم الروابط المباشرة لإصلاح الموانع.`
                          : reviewDraft.canCreate
                          ? 'كافة شروط الفاتورة والتشغيل والأنظمة معتمدة ومطابقة، اضغط الزر لبدء دورة الرواتب الآن.'
                          : 'انتقل لشاشة رفع واعتماد الفاتورة لتجهيز مدخلات مسودة المراجعة.'}
                      </p>
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      {reviewDraft.blockers && reviewDraft.blockers.length > 0 ? (
                        Array.from(new Set(reviewDraft.blockers.map((b) => b.fixAction))).map((fixAct) => {
                          const actionInfo = BLOCKER_ACTION_MAP[fixAct] || {
                            label: 'معالجة المانع',
                            href: '/app/payroll',
                          };
                          return (
                            <a
                              key={fixAct}
                              href={actionInfo.href}
                              onClick={(e) => {
                                if (fixAct === 'open_cycle') {
                                  e.preventDefault();
                                  setCycleViewMode('active');
                                } else if (
                                  fixAct === 'upload_invoice' ||
                                  fixAct === 'approve_invoice' ||
                                  fixAct === 'resolve_dispute' ||
                                  fixAct === 'classify_invoice'
                                ) {
                                  e.preventDefault();
                                  navigateMainTab('ninja_upload');
                                } else if (fixAct === 'approve_settings') {
                                  e.preventDefault();
                                  navigateMainTab('rules');
                                }
                              }}
                              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition inline-flex items-center gap-1.5 shadow-xs"
                            >
                              <span>{actionInfo.label}</span>
                              <ChevronRight size={14} />
                            </a>
                          );
                        })
                      ) : !reviewDraft.canCreate ? (
                        <a
                          href="/app/payroll?tab=ninja_upload"
                          onClick={(e) => {
                            e.preventDefault();
                            navigateMainTab('ninja_upload');
                          }}
                          className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition inline-flex items-center gap-1.5 shadow-xs"
                        >
                          <span>رفع فاتورة نينجا</span>
                          <ChevronRight size={14} />
                        </a>
                      ) : null}

                      <button
                        type="button"
                        disabled={creatingCycle || !reviewDraft.canCreate || (reviewDraft.blockers && reviewDraft.blockers.length > 0)}
                        onClick={handleCreateCycleFromDraft}
                        className="px-5 py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer flex items-center gap-2 shadow-xs"
                      >
                        {creatingCycle ? (
                          <>
                            <RefreshCw size={15} className="animate-spin" />
                            <span>جاري إنشاء الدورة...</span>
                          </>
                        ) : (
                          <>
                            <Sparkles size={15} />
                            <span>إنشاء دورة الرواتب</span>
                          </>
                        )}
                      </button>
                    </div>
                  </div>

                  {/* Blockers Section */}
                  {reviewDraft.blockers && reviewDraft.blockers.length > 0 ? (
                    <div className="p-4 rounded-2xl bg-[var(--danger)]/5 border border-[var(--danger)]/30 space-y-3">
                      <div className="flex items-center gap-2 text-[var(--danger)]">
                        <AlertTriangle size={18} />
                        <h4 className="font-bold text-xs sm:text-sm">
                          موانع الاستحقاق النشطة ({reviewDraft.blockers.length} مانع يجب حسمه لإنشاء الدورة):
                        </h4>
                      </div>

                      <div className="space-y-2">
                        {reviewDraft.blockers.map((b, idx: number) => {
                          const actionInfo = BLOCKER_ACTION_MAP[b.fixAction] || { label: 'معالجة المانع', href: '/app/payroll' };
                          return (
                            <div
                              key={idx}
                              className="p-3 rounded-xl border border-[var(--danger)]/20 bg-[var(--surface)] flex flex-wrap items-center justify-between gap-3 text-xs"
                            >
                              <div className="space-y-1">
                                <div className="flex items-center gap-2">
                                  <span className="text-[10px] bg-[var(--danger)]/15 text-[var(--danger)] px-2 py-0.5 rounded-md font-bold">
                                    {payrollBlockerLabelAr(b.code)}
                                  </span>
                                  <span className="font-bold text-[var(--ink)]">{b.message}</span>
                                </div>
                                {b.sourceLineId && (
                                  <div className="text-[10px] font-mono text-[var(--muted)]">
                                    سطر المصدر: {b.sourceLineId}
                                  </div>
                                )}
                              </div>

                              <a
                                href={actionInfo.href}
                                onClick={(e) => {
                                  if (b.fixAction === 'open_cycle') {
                                    e.preventDefault();
                                    setCycleViewMode('active');
                                  } else if (
                                    b.fixAction === 'upload_invoice' ||
                                    b.fixAction === 'approve_invoice' ||
                                    b.fixAction === 'resolve_dispute' ||
                                    b.fixAction === 'classify_invoice'
                                  ) {
                                    e.preventDefault();
                                    navigateMainTab('ninja_upload');
                                  } else if (b.fixAction === 'approve_settings') {
                                    e.preventDefault();
                                    navigateMainTab('rules');
                                  }
                                }}
                                className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-[var(--accent)] font-bold hover:bg-[var(--accent)] hover:text-[var(--accent-ink)] transition inline-flex items-center gap-1 shrink-0"
                              >
                                <span>{actionInfo.label}</span>
                                <ChevronRight size={13} />
                              </a>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  ) : (
                    <div className="p-4 rounded-2xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/30 text-xs text-[var(--money-pos)] flex items-center gap-2">
                      <CheckCircle2 size={18} className="shrink-0" />
                      <span className="font-bold">
                        لا توجد أي موانع استحقاق (0 Blockers) — مسودة المراجعة المقترحة مستوفية لكافة الشروط المحاسبية.
                      </span>
                    </div>
                  )}

                  {/* Warnings for Unassigned Orders & Missing Riders */}
                  {reviewDraft.unassignedOrders && reviewDraft.unassignedOrders.length > 0 && (
                    <div className="p-3.5 rounded-2xl bg-[var(--warning)]/10 border border-[var(--warning)]/30 text-xs text-[var(--ink)] flex items-center justify-between gap-3">
                      <div className="flex items-center gap-2 text-[var(--warning)]">
                        <AlertCircle size={16} />
                        <span>
                          يوجد <strong>{reviewDraft.unassignedOrders.length}</strong> وقائع تشغيل بلا مشغل فعلي مطابق في الفترة.
                        </span>
                      </div>
                      <a
                        href="/app/workforce/"
                        className="font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1 text-xs shrink-0"
                      >
                        <span>ربط المشغل الفعلي</span>
                        <ChevronRight size={13} />
                      </a>
                    </div>
                  )}

                  {reviewDraft.ridersNotFound && reviewDraft.ridersNotFound.length > 0 && (
                    <div className="p-3.5 rounded-2xl bg-[var(--warning)]/10 border border-[var(--warning)]/30 text-xs text-[var(--ink)] flex items-center justify-between gap-3">
                      <div className="flex items-center gap-2 text-[var(--warning)]">
                        <Users size={16} />
                        <span>
                          يوجد <strong>{reviewDraft.ridersNotFound.length}</strong> أسطر تشغيل تخص مناديب غير مسجلين في الدليل.
                        </span>
                      </div>
                      <a
                        href="/app/riders"
                        className="font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1 text-xs shrink-0"
                      >
                        <span>إضافة ومزامنة المناديب</span>
                        <ChevronRight size={13} />
                      </a>
                    </div>
                  )}

                  {/* Per-Account Totals Table */}
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                          توزيع استحقاق الحسابات والمعرفات مقارنة بالفاتورة
                        </h4>
                        <p className="text-xs text-[var(--muted)] mt-0.5">
                          مطابقة ما حققه كل معرف/حساب على المنصة وعدد المناديب المشغلين الفعليين.
                        </p>
                      </div>
                    </div>

                    <DataTable
                      tableId="ninja-review-draft-accounts"
                      columns={draftAccountColumns}
                      data={reviewDraft.invoices?.flatMap((inv) => inv.accounts || []) || []}
                      emptyTitle="لا توجد حسابات مسجلة في الفاتورة"
                      emptyDescription="لم يتم العثور على أسطر حسابات في فاتورة نينجا المعتمدة."
                      exportFilename={`ninja_draft_accounts_${selectedPeriod}`}
                      showTotals={true}
                    />
                  </div>
                </div>
              )}
            </div>
          )}

          {/* VIEW 2: ACTIVE CYCLE STAGES (0-7) */}
          {cycleViewMode === 'active' && (
            <div className="space-y-6">
              {/* Next-Step Card for Active Cycle (R9 W3) */}
              {(() => {
                const cycleRiders = (activeCycle?.riders || []) as Array<{
                  rider: string;
                  status?: string;
                  review?: string;
                }>;
                const unapprovedRiders = cycleRiders.filter((r) => r.review !== 'approved').length;
                const cycleHistory = (activeCycle?.history || []) as unknown as Array<{
                  stage: number;
                  advancedBy?: string;
                }>;
                const uname = (user?.username || '').toLowerCase();
                const createdByUser =
                  activeCycle && 'createdBy' in activeCycle && typeof activeCycle.createdBy === 'string'
                    ? activeCycle.createdBy
                    : '';
                const isParticipant = Boolean(
                  uname &&
                    activeCycle &&
                    (createdByUser.toLowerCase() === uname ||
                      cycleHistory.some((h) => (h.advancedBy || '').toLowerCase() === uname))
                );
                const draftBlockers = reviewDraft?.blockers || [];

                return (
                  <div
                    id="cycle-next-step"
                    className="p-4 rounded-2xl border-2 border-[var(--accent)]/40 bg-[var(--accent)]/5 flex flex-wrap items-center justify-between gap-3"
                  >
                    <div className="space-y-1">
                      <span className="text-[11px] font-bold text-[var(--accent)]">الخطوة التالية</span>
                      {!activeCycle ? (
                        <>
                          <p className="text-sm font-bold text-[var(--ink)]">
                            لا توجد دورة رواتب قائمة لشهر {selectedPeriod} — ابدأ من مسودة المراجعة لإنشاء الدورة
                          </p>
                          <p className="text-xs text-[var(--muted)]">
                            {draftBlockers.length > 0
                              ? `يوجد ${draftBlockers.length} مانع يمنع إنشاء الدورة حالياً (${draftBlockers[0].message}). عالج الموانع أو افتح مسودة المراجعة.`
                              : 'افتح مسودة المراجعة المقترحة للتحقق من الفواتير وإنشاء دورة الرواتب.'}
                          </p>
                        </>
                      ) : activeCycle.stage >= 7 && activeCycle.stage < 8 ? (
                        <>
                          <p className="text-sm font-bold text-[var(--ink)]">
                            الرواتب قيد الاعتماد — أكمل تأكيد الديون المتبقية إن وُجدت قبل ملف البنك
                          </p>
                          <p className="text-xs text-[var(--muted)]">
                            راجع جدول تأكيد الديون تحت. اللي لسه ما اتأكدش ماينزلش في ملف البنك.
                          </p>
                        </>
                      ) : activeCycle.status === 'approved' || activeCycle.stage >= 8 ? (
                        <>
                          <p className="text-sm font-bold text-[var(--ink)]">
                            اكتملت دورة الرواتب واعتمدت بالكامل — الخطوة التالية: تصدير ملف الصرف البنكي
                          </p>
                          <p className="text-xs text-[var(--muted)]">
                            تم ترحيل القيد المحاسبي واعتماد المسير النهائي. انتقل للخزينة لتصدير ملف البنك (CSV).
                          </p>
                        </>
                      ) : activeCycle.status === 'needs_decision' ? (
                        <>
                          <p className="text-sm font-bold text-[var(--danger)]">
                            الدورة متوقفة عند المرحلة {activeCycle.stage} وتحتاج قراراً مالياً (§22.9.2)
                          </p>
                          <p className="text-xs text-[var(--muted)]">
                            لماذا توقف التقديم؟ توجد أسطر مناديب بصافي سالب أو عجز سابق أو شروط غير مكتملة تمنع التقديم الآلي. راجع الأسطر أو عدّل الأقساط والتسويات للمتابعة.
                          </p>
                        </>
                      ) : activeCycle.stage === 6 ? (
                        unapprovedRiders > 0 ? (
                          <>
                            <p className="text-sm font-bold text-[var(--ink)]">
                              المرحلة 6 (مراجعة المسير): اعتمد المناديب المتبقين ({unapprovedRiders} مندوب) في جدول المراجعة أدناه قبل الاعتماد النهائي
                            </p>
                            <p className="text-xs text-[var(--muted)]">
                              يشترط النظام مراجعة واعتماد جميع أسطر المناديب في جدول المراجعة (اعتماد فردي أو اعتماد الكل)، ثم تأكيد الديون بمستخدم مستقل قبل المرحلة 7.
                            </p>
                          </>
                        ) : isParticipant ? (
                          <>
                            <p className="text-sm font-bold text-[var(--ink)]">
                              تم اعتماد جميع المناديب — بانتظار مستخدم مستقل لتأكيد الديون ثم اعتماد المرحلة 7
                            </p>
                            <p className="text-xs text-[var(--muted)]">
                              المُعِدّ لا يعتمد ولا يؤكّد الديون لنفسه؛ يلزم مستخدم آخر مستقل عن من أنشأ الدورة أو قدّم مراحلها.
                            </p>
                          </>
                        ) : (
                          <>
                            <p className="text-sm font-bold text-[var(--ink)]">
                              جميع المناديب معتمدون — أكّد استقطاع الديون ثم اعتمد المرحلة 7 وترحيل القيد
                            </p>
                            <p className="text-xs text-[var(--muted)]">
                              تأكيد الديون قبل اعتماد الراتب النهائي. {cycleSteps[7]?.desc || 'الاعتماد النهائي وترحيل قيود الاستحقاق والمديونيات.'}
                            </p>
                          </>
                        )
                      ) : (
                        <>
                          <p className="text-sm font-bold text-[var(--ink)]">
                            المرحلة الحالية ({activeCycle.stage}: {cycleSteps[activeCycle.stage]?.title}) جاهزة — الخطوة التالية: التقديم إلى المرحلة {activeCycle.stage + 1} ({cycleSteps[activeCycle.stage + 1]?.title})
                          </p>
                          <p className="text-xs text-[var(--muted)]">
                            {cycleSteps[activeCycle.stage + 1]?.desc || ''}
                          </p>
                          {!activeCycle.inputsCapturedAt && (activeCycle.approvalBlockers || []).length > 0 && (
                            <p className="text-xs font-bold text-[var(--warning)] mt-1 flex flex-wrap items-center gap-1.5">
                              <AlertTriangle size={13} className="shrink-0" />
                              <span>{(activeCycle.approvalBlockers || []).join(' • ')}</span>
                              <a
                                href="#btn-capture-cycle-inputs"
                                onClick={(e) => {
                                  e.preventDefault();
                                  const el = document.getElementById('btn-capture-cycle-inputs');
                                  el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                  el?.focus();
                                }}
                                className="underline text-[var(--accent)] hover:opacity-80"
                              >
                                انتقال لالتقاط المدخلات
                              </a>
                            </p>
                          )}
                        </>
                      )}
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      {!activeCycle ? (
                        <>
                          <button
                            type="button"
                            onClick={() => setCycleViewMode('draft')}
                            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition cursor-pointer inline-flex items-center gap-1.5 shadow-xs"
                          >
                            <span>فتح مسودة المراجعة</span>
                            <ChevronRight size={14} />
                          </button>
                          {Array.from(new Set(draftBlockers.map((b) => b.fixAction))).map((fixAct) => {
                            const actionInfo = BLOCKER_ACTION_MAP[fixAct] || {
                              label: 'معالجة المانع',
                              href: '/app/payroll',
                            };
                            return (
                              <a
                                key={fixAct}
                                href={actionInfo.href}
                                onClick={(e) => {
                                  if (
                                    fixAct === 'upload_invoice' ||
                                    fixAct === 'approve_invoice' ||
                                    fixAct === 'resolve_dispute' ||
                                    fixAct === 'classify_invoice'
                                  ) {
                                    e.preventDefault();
                                    navigateMainTab('ninja_upload');
                                  } else if (fixAct === 'approve_settings') {
                                    e.preventDefault();
                                    navigateMainTab('rules');
                                  }
                                }}
                                className="px-3.5 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-bold text-xs hover:bg-[var(--raised)] transition inline-flex items-center gap-1"
                              >
                                <span>{actionInfo.label}</span>
                                <ChevronRight size={13} />
                              </a>
                            );
                          })}
                        </>
                      ) : activeCycle.status === 'approved' || activeCycle.stage >= 7 ? (
                        <>
                          <Link
                            href={appendPeriodToHref('/treasury/?tab=bank_file', selectedPeriod)}
                            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition inline-flex items-center gap-1.5 shadow-xs"
                          >
                            <span>تصدير ملف البنك (CSV)</span>
                            <ChevronRight size={14} />
                          </Link>
                          <Link
                            href="/accounting"
                            className="px-3.5 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-bold text-xs hover:bg-[var(--raised)] transition inline-flex items-center gap-1"
                          >
                            <span>مراجعة القيود المحاسبية</span>
                            <ChevronRight size={13} />
                          </Link>
                        </>
                      ) : activeCycle.status === 'needs_decision' ? (
                        <>
                          <a
                            href="/app/debts"
                            className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition inline-flex items-center gap-1 shadow-xs"
                          >
                            <span>مراجعة المديونيات والسلف</span>
                            <ChevronRight size={13} />
                          </a>
                          <a
                            href="/app/operations"
                            className="px-3.5 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-bold text-xs hover:bg-[var(--raised)] transition inline-flex items-center gap-1"
                          >
                            <span>مراجعة التشغيل اليومي</span>
                            <ChevronRight size={13} />
                          </a>
                          <button
                            type="button"
                            onClick={() => setCycleViewMode('draft')}
                            className="px-3.5 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-bold text-xs hover:bg-[var(--raised)] transition cursor-pointer"
                          >
                            مسودة المراجعة
                          </button>
                        </>
                      ) : (
                        <button
                          type="button"
                          disabled={advancingCycle}
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
                );
              })()}

              {/* Cycle Inputs Snapshot Card (U2 / BR §39.2) */}
              {activeCycle && (() => {
                const canCaptureInputs = Boolean(activeCycle.allowedActions?.includes('capture_inputs'));
                const isCaptured = Boolean(activeCycle.inputsCapturedAt);
                const countEntries = Object.entries(activeCycle.inputsCaptureCounts || {});
                const blockers = activeCycle.approvalBlockers || [];

                return (
                  <div
                    id="cycle-inputs-card"
                    data-testid="cycle-inputs-card"
                    className="p-4 sm:p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="flex items-center gap-2">
                          <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                            مدخلات التشغيل المثبتة للدورة
                          </h4>
                          <span
                            className={`px-2.5 py-0.5 rounded-full text-xs font-bold ${
                              isCaptured
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                            }`}
                          >
                            {isCaptured ? 'تم التقاط المدخلات' : 'غير ملتقطة بعد'}
                          </span>
                        </div>
                        <p className="text-xs text-[var(--muted)] mt-0.5">
                          تجميد لقطة مدخلات التشغيل الميداني والأداء اليومي قبل احتساب مراحل الدورة.
                        </p>
                      </div>

                      {canCaptureInputs && (
                        <button
                          id="btn-capture-cycle-inputs"
                          data-testid="btn-capture-cycle-inputs"
                          type="button"
                          disabled={capturingInputs}
                          onClick={() => handleCaptureCycleInputs(activeCycle)}
                          className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs sm:text-sm hover:opacity-90 transition disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer inline-flex items-center gap-1.5 shadow-xs"
                        >
                          <RefreshCw size={14} className={capturingInputs ? 'animate-spin' : ''} />
                          <span>{capturingInputs ? 'جاري التقاط المدخلات...' : 'التقاط المدخلات'}</span>
                        </button>
                      )}
                    </div>

                    {captureError && (
                      <div
                        role="alert"
                        data-testid="cycle-capture-error"
                        className={`p-3.5 rounded-xl border flex flex-wrap items-center justify-between gap-3 text-xs font-bold ${
                          captureError.status === 503
                            ? 'border-[var(--warning)]/30 bg-[var(--warning)]/10 text-[var(--warning)]'
                            : 'border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)]'
                        }`}
                      >
                        <div className="flex items-center gap-2">
                          <AlertTriangle size={16} className="shrink-0" />
                          <span>{captureError.message}</span>
                        </div>
                        {captureError.status === 503 && canCaptureInputs && (
                          <button
                            type="button"
                            disabled={capturingInputs}
                            onClick={() => handleCaptureCycleInputs(activeCycle)}
                            className="px-3 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition inline-flex items-center gap-1 cursor-pointer"
                          >
                            <RefreshCw size={13} className={capturingInputs ? 'animate-spin' : ''} />
                            <span>إعادة المحاولة</span>
                          </button>
                        )}
                      </div>
                    )}

                    {!isCaptured ? (
                      <div
                        data-testid="cycle-inputs-blocker"
                        className="p-3.5 rounded-xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 flex flex-wrap items-center justify-between gap-3 text-xs"
                      >
                        <div className="flex items-center gap-2 text-[var(--warning)] font-bold">
                          <AlertTriangle size={16} className="shrink-0" />
                          <span>
                            {blockers.length > 0
                              ? blockers.join(' • ')
                              : 'التقط المدخلات أولًا'}
                          </span>
                        </div>
                        {canCaptureInputs && (
                          <a
                            href="#btn-capture-cycle-inputs"
                            onClick={(e) => {
                              e.preventDefault();
                              const el = document.getElementById('btn-capture-cycle-inputs');
                              el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                              el?.focus();
                            }}
                            className="font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1"
                          >
                            <span>انتقل إلى زر التقاط المدخلات</span>
                            <ChevronRight size={13} />
                          </a>
                        )}
                      </div>
                    ) : (
                      <div className="space-y-3">
                        <div className="flex flex-wrap items-center gap-4 text-xs text-[var(--muted)] bg-[var(--raised)]/50 px-3.5 py-2.5 rounded-xl border border-[var(--line)]">
                          <div>
                            <span>توقيت الالتقاط (الرياض): </span>
                            <strong
                              data-testid="cycle-inputs-captured-at"
                              className="num font-mono font-bold text-[var(--ink)]"
                            >
                              {formatRiyadhDateTime(activeCycle.inputsCapturedAt)}
                            </strong>
                          </div>
                          <div>
                            <span>التقط بواسطة: </span>
                            <strong
                              data-testid="cycle-inputs-captured-by"
                              className="font-mono font-bold text-[var(--ink)]"
                            >
                              {activeCycle.inputsCapturedBy || '-'}
                            </strong>
                          </div>
                        </div>

                        <CycleInputsCountsTable countEntries={countEntries} kindLabels={CAPTURE_KIND_LABELS} />
                      </div>
                    )}
                  </div>
                );
              })()}

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
                      مراحل احتساب الرواتب والمسيرات وفق نموذج العمل المالي المعتمد
                    </p>
                  </div>
                </div>

                {/* Stepper Navigation with Who/When History */}
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 mt-6">
                  {cycleSteps.map((st) => {
                    const currentStage = activeCycle?.stage ?? 0;
                    const isCurrent = currentStage === st.num;
                    const isPast = currentStage > st.num;
                    return (
                      <div
                        key={st.num}
                        className={`p-3.5 rounded-xl border transition-all flex flex-col justify-between ${
                          isCurrent
                            ? 'border-[var(--accent)] bg-[var(--accent)]/5 shadow-xs'
                            : isPast
                            ? 'border-[var(--money-pos)]/40 bg-[var(--money-pos)]/5'
                            : 'border-[var(--line)] bg-[var(--surface)] opacity-75'
                        }`}
                      >
                        <div>
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

                        {/* Who / When progression history */}
                        <div className="mt-3 pt-2 border-t border-[var(--line)]/50 text-[10px] text-[var(--muted)]">
                          {st.num === 0 && activeCycle && (
                            <div>
                              <span className="text-[var(--accent)] font-semibold">الدورة:</span> {activeCycle.period}
                            </div>
                          )}
                          {st.num > 0 && (() => {
                            const entry = (activeCycle?.history as unknown as Array<{ stage: number; advancedBy?: string; at?: string }>)?.find(
                              (h) => h.stage === st.num
                            );
                            if (!entry) return <span className="opacity-50">في انتظار الوصول للمرحلة</span>;
                            return (
                              <div>
                                <span className="text-[var(--money-pos)] font-semibold">تقدم بواسطة:</span> @{entry.advancedBy}
                                {entry.at && (
                                  <span className="block text-[9px] mt-0.5 opacity-80" dir="ltr">
                                    {new Date(entry.at).toLocaleString('ar-SA')}
                                  </span>
                                )}
                              </div>
                            );
                          })()}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

          {/* Cycle Deduction Phases Flow (BR §29) */}
          {activeCycle && (activeCycle.riders || []).length > 0 && (() => {
            const riders = (activeCycle.riders || []) as Array<{
              rider: string;
              net: number;
              gross?: number;
              afterCommission?: number;
              phase1?: number;
              preliminaryDue?: number;
              phase2?: number;
              phase3?: number;
            }>;
            const gross = riders.reduce((s: number, r) => s + (r.gross || 0), 0);
            const afterCommission = riders.reduce((s: number, r) => s + (r.afterCommission || 0), 0);
            const phase1 = riders.reduce((s: number, r) => s + (r.phase1 || 0), 0);
            const preliminaryDue = riders.reduce((s: number, r) => s + (r.preliminaryDue || 0), 0);
            const phase2 = riders.reduce((s: number, r) => s + (r.phase2 || 0), 0);
            const phase3 = riders.reduce((s: number, r) => s + (r.phase3 || 0), 0);
            const net = riders.reduce((s: number, r) => s + (r.net || 0), 0);

            return (
              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                <div className="flex items-center justify-between border-b border-[var(--line)] pb-2">
                  <div>
                    <h4 className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                      مراحل احتساب الاستقطاعات الإجمالية للدورة
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
                    <span className="text-[10px] text-[var(--muted)] block">الإجمالي</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {formatSar(gross)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">بعد العمولة</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {formatSar(afterCommission)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 1 (تشغيلي)</span>
                    <span className="num font-mono font-bold text-[var(--danger)]">
                      {formatSar(phase1)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/40">
                    <span className="text-[10px] font-bold text-[var(--accent)] block">المستحق الأولي</span>
                    <span className="num font-mono font-black text-[var(--accent)]">
                      {formatSar(preliminaryDue)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 2 (مديونيات)</span>
                    <span className="num font-mono font-bold text-[var(--danger)]">
                      {formatSar(phase2)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)]">
                    <span className="text-[10px] text-[var(--muted)] block">المرحلة 3 (تسويات)</span>
                    <span className="num font-mono font-bold text-[var(--ink)]">
                      {formatSar(phase3)}
                    </span>
                  </div>

                  <div className="p-2.5 rounded-xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/40">
                    <span className="text-[10px] font-bold text-[var(--money-pos)] block">الصافي النهائي</span>
                    <span className="num font-mono font-black text-[var(--money-pos)]">
                      {formatSar(net)}
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
                    مراجعة واعتماد المسير
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
              {activeCycle.stage >= 6 && activeCycle.stage < 8 && (
                <DebtDeductionStage
                  cycle={activeCycle}
                  onChanged={() => {
                    loadCycles();
                    loadCycleLines(activeCycle.id);
                  }}
                />
              )}
            </div>
          )}

          {/* Cycle Lines & Explain per Rider */}
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  كشوف احتساب المناديب وشرح المعادلات
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
    </div>
  )}

      {/* ======================================================== */}
      {/* SECTION 3: RULES EDITOR & GLOBAL SETTINGS (F1 / A6)      */}
      {/* ======================================================== */}
      {mainTab === 'rules' && (
        <div className="space-y-6">
          {/* Sub-tab view toggle */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-2 bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-xs">
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setRulesSection('eligibility')}
                className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-bold transition flex items-center gap-2 cursor-pointer ${
                  rulesSection === 'eligibility'
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                    : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <FileText size={15} />
                <span>شروط وأهلية نينجا</span>
                {activeNinjaEligibility && (
                  <span
                    className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                      activeNinjaEligibility.status === 'approved'
                        ? 'bg-[var(--money-pos)]/20 text-[var(--money-pos)]'
                        : 'bg-[var(--warning)]/20 text-[var(--warning)]'
                    }`}
                  >
                    {activeNinjaEligibility.status === 'approved'
                      ? `معتمد (v${activeNinjaEligibility.version})`
                      : `مسودة (v${activeNinjaEligibility.version})`}
                  </span>
                )}
              </button>

              <button
                type="button"
                onClick={() => setRulesSection('record')}
                className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-bold transition flex items-center gap-2 cursor-pointer ${
                  rulesSection === 'record'
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                    : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <Layers size={15} />
                <span>سجل القواعد وتخصيصات الحسابات (S5)</span>
              </button>

              <button
                type="button"
                onClick={() => setRulesSection('global')}
                className={`px-4 py-2 rounded-xl text-xs sm:text-sm font-bold transition flex items-center gap-2 cursor-pointer ${
                  rulesSection === 'global'
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                    : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <Scale size={15} />
                <span>الإعدادات العامة الموحدة</span>
                {globalSettings.length > 0 && (
                  <span className="px-2 py-0.5 rounded-full text-[10px] font-bold bg-[var(--raised)] text-[var(--accent)] border border-[var(--line)]">
                    v{globalSettings[0].version}
                  </span>
                )}
              </button>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="month"
                value={selectedPeriod}
                onChange={(e) => setSelectedPeriod(e.target.value)}
                className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-bold focus:outline-hidden"
              />
              <button
                type="button"
                onClick={() => {
                  loadNinjaEligibility();
                  loadGlobalSettings();
                }}
                className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
                title="تحديث البيانات"
              >
                <RefreshCw size={14} className={loadingNinjaEligibility || loadingGlobalSettings ? 'animate-spin' : ''} />
              </button>
            </div>
          </div>

          {/* SECTION A: NINJA ELIGIBILITY RULES (A6) */}
          {rulesSection === 'eligibility' && (
            <div className="space-y-6">
              {loadingNinjaEligibility ? (
                <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
                  <Skeleton className="h-20 w-full" />
                  <Skeleton className="h-40 w-full" />
                </div>
              ) : ninjaEligibilityError ? (
                <ErrorState
                  title="تعذر تحميل ضوابط الأهلية لنينجا"
                  message={ninjaEligibilityError}
                  onRetry={loadNinjaEligibility}
                />
              ) : !activeNinjaEligibility ? (
                <EmptyState
                  title="لا توجد شروط أهلية مسجلة لشهر "
                  description={`لم يتم العثور على شروط أهلية مسجلة لتطبيق نينجا في شهر ${selectedPeriod}. يمكنك إنشاء مسودة جديدة من القيم القياسية بنقرة زر.`}
                  action={{
                    label: `إنشاء مسودة شروط أهلية لشهر ${selectedPeriod}`,
                    onClick: handleCreateNewNinjaDraft,
                  }}
                />
              ) : (
                <div className="space-y-6">
                  {/* Active Eligibility Header Card */}
                  <div
                    className={`p-5 rounded-2xl border transition shadow-xs space-y-4 ${
                      activeNinjaEligibility.status === 'approved'
                        ? 'border-[var(--line)] bg-[var(--surface)]'
                        : activeNinjaEligibility.status === 'rejected'
                        ? 'border-[var(--danger)]/40 bg-[var(--danger)]/5'
                        : 'border-[var(--warning)]/40 bg-[var(--warning)]/5'
                    }`}
                  >
                    <div className="flex flex-wrap items-center justify-between gap-4">
                      <div>
                        <div className="flex items-center gap-2">
                          <h4 className="font-bold text-base sm:text-lg text-[var(--ink)]">
                            {activeNinjaEligibility.data?.name || `شروط أهلية نينجا ${selectedPeriod}`} (إصدار #{activeNinjaEligibility.version})
                          </h4>
                          <span
                            className={`px-3 py-1 rounded-full text-xs font-bold ${
                              activeNinjaEligibility.status === 'approved'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : activeNinjaEligibility.status === 'rejected'
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                            }`}
                          >
                            {activeNinjaEligibility.status === 'approved'
                              ? 'معتمد للقراءة فقط'
                              : activeNinjaEligibility.status === 'rejected'
                              ? 'مرفوض'
                              : `مسودة قيد التعديل (@${activeNinjaEligibility.createdBy || 'النظام'})`}
                          </span>
                        </div>
                        <p className="text-xs text-[var(--muted)] mt-1">
                          تخضع شروط الأهلية للاعتماد المستقل من شخص آخر غير المنشئ. التعديل يتم في المسودة حصراً.
                        </p>
                      </div>

                      {/* Action buttons */}
                      <div className="flex flex-wrap items-center gap-2">
                        {activeNinjaEligibility.status === 'draft' ? (
                          <>
                            <button
                              type="button"
                              onClick={() => setRuleModalOpen(true)}
                              className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer flex items-center gap-1.5 shadow-xs"
                            >
                              <Plus size={14} />
                              <span>إضافة شرط</span>
                            </button>

                            <button
                              type="button"
                              disabled={submittingRuleLifecycle}
                              onClick={handleApproveNinjaEligibility}
                              className="px-3.5 py-2 rounded-xl bg-[var(--money-pos)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer flex items-center gap-1.5 shadow-xs disabled:opacity-50"
                            >
                              {submittingRuleLifecycle ? <RefreshCw size={14} className="animate-spin" /> : <ShieldCheck size={14} />}
                              <span>اعتماد المسودة</span>
                            </button>

                            <button
                              type="button"
                              onClick={() => setRejectModalOpen(true)}
                              className="px-3 py-2 rounded-xl bg-[var(--raised)] text-[var(--danger)] border border-[var(--danger)]/30 text-xs font-bold hover:bg-[var(--danger)]/10 transition cursor-pointer flex items-center gap-1.5"
                            >
                              <Ban size={14} />
                              <span>رفض</span>
                            </button>

                            <button
                              type="button"
                              onClick={handleDeleteNinjaDraft}
                              className="px-3 py-2 rounded-xl bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--danger)] hover:bg-[var(--danger)]/10 transition cursor-pointer text-xs font-bold flex items-center gap-1.5"
                              title="حذف المسودة بالكامل"
                            >
                              <Trash2 size={14} />
                              <span>حذف المسودة</span>
                            </button>
                          </>
                        ) : (
                          <button
                            type="button"
                            onClick={handleCreateNewNinjaDraft}
                            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer flex items-center gap-1.5 shadow-xs"
                          >
                            <Copy size={14} />
                            <span>إنشاء مسودة جديدة من النسخة الحالية</span>
                          </button>
                        )}
                      </div>
                    </div>
                  </div>

                  {/* Rules DataTable */}
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                          جدول شروط ومقاييس الأهلية
                        </h4>
                        <p className="text-xs text-[var(--muted)] mt-0.5">
                          تحديد الحد الأدنى المطلوب لكل مقياس، المعامل الحسابي، وأثر عدم التحقق على احتساب أجر المندوب.
                        </p>
                      </div>
                      <span className="text-xs text-[var(--muted)] font-mono">
                        {(activeNinjaEligibility.data?.rules || []).length} شرط مسجل
                      </span>
                    </div>

                    <DataTable
                      tableId="ninja-eligibility-rules"
                      columns={draftRuleColumns}
                      data={activeNinjaEligibility.data?.rules || []}
                      emptyTitle="لا توجد شروط مسجلة في هذه المسودة"
                      emptyDescription="انقر على «إضافة شرط» لإدراج مقياس جديد للأهلية."
                      exportFilename={`ninja_eligibility_rules_${selectedPeriod}`}
                      showTotals={false}
                    />
                  </div>
                </div>
              )}
            </div>
          )}

          {/* SECTION B: RULE RECORD & ACCOUNT EXCEPTIONS (S5) */}
          {rulesSection === 'record' && (
            <RuleRecordWorkspace
              appSlug={appSlug}
              appNameAr={platformsList.find((p) => p.id === selectedPlatform)?.nameAr || appSlug}
              period={selectedPeriod}
              onOpenExceptionModal={() => {
                setEditorKind('account_exception');
                setEditingDoc(null);
                setEditorModalOpen(true);
              }}
              activeEligibilityDoc={activeNinjaEligibility}
              activeTermsDoc={termsList.find((t) => t.status === 'approved') || termsList[0] || null}
              activeSchemeDoc={schemesList.find((s) => s.status === 'approved') || schemesList[0] || null}
            />
          )}

          {/* SECTION C: GLOBAL SETTINGS (Existing F1) */}
          {rulesSection === 'global' && (
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
                value={selectedPeriod}
                onChange={(e) => setSelectedPeriod(e.target.value)}
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
                const v = setting.data?.values;
                const rent = v?.vehicleRent;
                const acc = v?.accounts;

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
                          اعتماد الإعدادات العامة
                        </button>
                      )}
                    </div>

                    <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs pt-2">
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">أيام الشهر القياسية:</span>
                        <span className="font-bold text-[var(--ink)] num">{v?.standardDays || 30} يوماً</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">أيام شهر فبراير:</span>
                        <span className="font-bold text-[var(--ink)] num">{v?.februaryDays || 28} يوماً</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">موعد قطع اليوم:</span>
                        <span className="font-mono font-bold">{v?.billingCutoff || '00:00'}</span>
                      </div>
                      <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">النطاق الزمني:</span>
                        <span className="font-mono font-bold">{v?.timeZone || 'Asia/Riyadh'}</span>
                      </div>
                    </div>

                    {/* 6-Column Category Matrix */}
                    <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                      <div className="p-4 border-b border-[var(--line)]">
                        <h4 className="font-bold text-sm text-[var(--ink)]">
                          مصفوفة الفئات (الكفالة × المركبة) · شبكة الـ 6 أعمدة المعتمدة
                        </h4>
                        <p className="text-xs text-[var(--muted)] mt-0.5">
                          تحديد تكلفة إيجار المركبة اليومي (سيارة {formatSar(rent?.company_car || 60)} / دباب {formatSar(rent?.company_bike || 26.67)} / خاص {formatSar(0)}) وحسابات التوجيه.
                        </p>
                      </div>

                      <GlobalCategoryMatrixTable rent={rent} acc={acc} />
                    </div>
                  </div>
                );
              })()}
            </div>
          )}
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
              سجل استقطاعات التشغيل والتحملات
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
              href={appendPeriodToHref('/treasury/?tab=bank_file', selectedPeriod)}
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
                سجل السياسات التاريخية السابقة قبل الانتقال لمنظومة المنصات الموحدة.
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
              description="كافة السياسات يتم إدارتها حالياً عبر محرر الأنظمة وقواعد المنصات."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <AutoFilterBar controller={policiesFilter} totalCount={policies.length} />
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <AutoFilterTh controller={policiesFilter} colKey="name" className="p-3 text-start" />
                      <AutoFilterTh controller={policiesFilter} colKey="kind" className="p-3 text-start" />
                      <AutoFilterTh controller={policiesFilter} colKey="platform" className="p-3 text-start" />
                      <AutoFilterTh controller={policiesFilter} colKey="effectiveFrom" className="p-3 text-start" />
                      <AutoFilterTh controller={policiesFilter} colKey="status" className="p-3 text-start" />
                      <th className="p-3 text-start">إجراء</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {policiesFilter.rows.map((p) => (
                      <tr key={p.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-bold text-[var(--ink)]">{p.name || p.id}</td>
                        <td className="p-3 text-xs text-[var(--muted)]">{({
                          pay_scheme: 'خطة احتساب', scheme: 'خطة احتساب',
                          pay_terms: 'شروط تشغيل', terms: 'شروط تشغيل',
                          pay_eligibility: 'قاعدة استحقاق', eligibility: 'قاعدة استحقاق',
                          deduction: 'سياسة استقطاع', allowance: 'سياسة بدل',
                        } as Record<string, string>)[String(p.kind ?? '').toLowerCase()] || String(p.kind ?? '')}</td>
                        <td className="p-3">{p.platform}</td>
                        <td className="p-3 font-mono text-xs">{p.effectiveFrom}</td>
                        <td className="p-3">
                          <span className={`px-2 py-0.5 rounded-full text-xs font-bold ${p.status === 'draft' ? 'bg-[var(--warning)]/15 text-[var(--warning)]' : 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'}`}>
                            {p.status === 'draft' ? 'مسودة' : 'معتمدة'}
                          </span>
                        </td>
                        <td className="p-3">
                          {p.status === 'draft' && (isAdmin || can('pay.manage') || can('policies.approve')) && (
                            <button
                              type="button"
                              className="text-xs font-bold text-[var(--accent)]"
                              onClick={async () => {
                                try {
                                  await api.approvePolicy(p.id);
                                  toast('اعتُمدت السياسة', 'success');
                                  const res = await api.getPolicies();
                                  setPolicies(res || []);
                                } catch (err: any) {
                                  toast(err.message || 'تعذر اعتماد السياسة', 'danger');
                                }
                              }}
                            >
                              اعتماد
                            </button>
                          )}
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
        title="استيراد وتوزيع خيارات المناديب"
        description="ربط خيارات المناديب المسجلة بالنماذج أو الاستبيانات بأنظمة التطبيق المعتمدة دون تعديل الردود الأصلية."
      >
        <div className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">نوع المصدر:</label>
            <select
              value={assignmentSourceType}
              onChange={(e) => setAssignmentSourceType(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
            >
              <option value="campaign">حملة / استبيان</option>
              <option value="custom_form">نموذج مخصص</option>
            </select>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">معرف الحملة / النموذج:</label>
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
              <label className="text-xs font-bold text-[var(--ink)]">حقل الاختيار:</label>
              <input
                type="text"
                placeholder="workSystem"
                value={assignmentFieldId}
                onChange={(e) => setAssignmentFieldId(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">قيمة الرد:</label>
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
            <label className="text-xs font-bold text-[var(--ink)]">النظام المستهدف المعتمد:</label>
            <SearchableSelect
              value={assignmentMappingScheme}
              onChange={setAssignmentMappingScheme}
              placeholder="حدد النظام المعتمد..."
              ariaLabel="النظام المستهدف المعتمد"
              options={schemesList.map((sc) => ({
                value: sc.id,
                label: `${sc.data?.name ?? sc.id} (#${sc.version}) - ${sc.id}`,
              }))}
            />
          </div>

          <div className="pt-2 flex items-center justify-between border-t border-[var(--line)]">
            <button
              type="button"
              disabled={previewingAssignments}
              onClick={handlePreviewAssignments}
              className="px-4 py-2 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
            >
              {previewingAssignments ? 'جاري الفحص والمعاينة...' : 'معاينة الاستيراد التجريبي'}
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
                <span className="text-[var(--muted)]">الحالة:</span>
                <span className="font-bold">{selectedLineForExplain.status === 'ready' ? 'جاهز' : selectedLineForExplain.status}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">إجمالي المنصة:</span>
                <span className="num font-bold">
                  {formatSar((selectedLineForExplain.platformGross || selectedLineForExplain.gross || 0))}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">صافي المستحق:</span>
                <span className="num font-bold text-[var(--money-pos)]">
                  {formatSar((selectedLineForExplain.entitlement ?? selectedLineForExplain.gross ?? 0))}
                </span>
              </div>
            </div>

            <div className="space-y-2">
              <h5 className="font-bold text-xs text-[var(--ink)]">بنود المعادلة والأدلة المحاسبية:</h5>
              {(!selectedLineForExplain.lines || selectedLineForExplain.lines.length === 0) ? (
                <div className="p-4 text-center text-[var(--muted)] bg-[var(--surface)] rounded-xl border border-[var(--line)]">
                  لا توجد بنود تفصيلية مسجلة لهذا السطر.
                </div>
              ) : (
                <div className="space-y-2">
                  {(selectedLineForExplain.lines as unknown as Array<{
                    label?: string;
                    category?: string;
                    amount?: number;
                    formula?: string;
                    inputs?: Record<string, unknown>;
                  }>).map((l, idx: number) => (
                    <div key={idx} className="p-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] space-y-1">
                      <div className="flex justify-between items-center">
                        <span className="font-bold text-[var(--ink)]">{l.label || l.category}</span>
                        <span
                          className={`num font-bold ${
                            (l.amount || 0) < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'
                          }`}
                        >
                          {formatSar((l.amount || 0))}
                        </span>
                      </div>
                      {l.formula && (
                        <div className="font-mono text-[10px] text-[var(--muted)] bg-[var(--raised)] p-1.5 rounded-md text-left" dir="ltr">
                          {l.formula}
                        </div>
                      )}
                      {l.inputs && (
                        <div className="text-[10px] text-[var(--muted)]">
                          مدخلات: <span>{formatReadableValue(l.inputs)}</span>
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
      {/* DIALOG: ADD NINJA ELIGIBILITY RULE (A6)                  */}
      {/* ======================================================== */}
      <Dialog
        open={ruleModalOpen}
        onClose={() => setRuleModalOpen(false)}
        title="إضافة شرط أهلية جديد لنينجا"
      >
        <div className="space-y-4 text-xs pt-2">
          <div className="space-y-1">
            <label className="font-bold text-[var(--ink)] block">المقياس المطلوب:</label>
            <SearchableSelect
              value={newRuleMetric}
              onChange={setNewRuleMetric}
              ariaLabel="المقياس المطلوب"
              placeholder="اختر المقياس المطلوب..."
              options={(ninjaCatalog?.ninjaMetrics || Object.keys(NINJA_METRICS_MAP)).map((m: string) => ({
                value: m,
                label: NINJA_METRICS_MAP[m] || m,
              }))}
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">المعامل:</label>
              <select
                value={newRuleOperator}
                onChange={(e) => setNewRuleOperator(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-semibold focus:outline-hidden"
              >
                {Object.entries(OPERATORS_MAP).map(([op, label]) => (
                  <option key={op} value={op}>
                    {label}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label className="font-bold text-[var(--ink)] block">الحد الأدنى / القيمة:</label>
              <input
                type="number"
                value={newRuleThreshold}
                onChange={(e) => setNewRuleThreshold(e.target.value)}
                placeholder="مثلاً: 100"
                required
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-mono font-bold focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--ink)] block">الأثر عند عدم التحقق:</label>
            <select
              value={newRuleEffectType}
              onChange={(e) => setNewRuleEffectType(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-semibold focus:outline-hidden"
            >
              {Object.entries(EFFECT_TYPES_MAP).map(([eff, label]) => (
                <option key={eff} value={eff}>
                  {label}
                </option>
              ))}
            </select>
          </div>

          <div className="p-3 rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] flex items-center justify-between">
            <div>
              <span className="font-bold text-[var(--ink)] block">ضمن القاعدة</span>
              <span className="text-[11px] text-[var(--muted)]">
                تفعيل الشرط ضمن احتساب الأهلية الفعلي للدورة.
              </span>
            </div>
            <input
              type="checkbox"
              checked={newRuleIncluded}
              onChange={(e) => setNewRuleIncluded(e.target.checked)}
              className="h-4 w-4 rounded-md accent-[var(--accent)] cursor-pointer"
            />
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setRuleModalOpen(false)}
              className="px-4 py-2 rounded-xl bg-[var(--raised)] text-[var(--muted)] text-xs font-bold hover:text-[var(--ink)] transition cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={submittingRuleLifecycle}
              onClick={handleSaveNewRule}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer flex items-center gap-1.5"
            >
              {submittingRuleLifecycle && <RefreshCw size={13} className="animate-spin" />}
              <span>إضافة الشرط وحفظ المسودة</span>
            </button>
          </div>
        </div>
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: REJECT NINJA ELIGIBILITY DRAFT (A6)              */}
      {/* ======================================================== */}
      <Dialog
        open={rejectModalOpen}
        onClose={() => setRejectModalOpen(false)}
        title="رفض مسودة شروط الأهلية وتوثيق السبب"
      >
        <div className="space-y-4 text-xs pt-2">
          <div className="p-3 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-xs text-[var(--danger)] flex items-start gap-2">
            <AlertTriangle size={16} className="shrink-0 mt-0.5" />
            <div>
              <span className="font-bold block">ملاحظة التدقيق والرقابة المالية:</span>
              <p className="mt-0.5 text-[11px] opacity-90 leading-relaxed">
                يتطلب رفض المسودة إبداء سبب مالي أو تشغيلي واضح لتوثيقه في سجل العمليات وإشعار منشئ المسودة.
              </p>
            </div>
          </div>

          <div className="space-y-1">
            <label className="font-bold text-[var(--ink)] block">سبب الرفض (إلزامي):</label>
            <textarea
              value={rejectionReason}
              onChange={(e) => setRejectionReason(e.target.value)}
              placeholder="اكتب سبب الرفض أو التعديلات المطلوبة على المقاييس والحدود..."
              rows={4}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="flex justify-end gap-2 pt-3 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setRejectModalOpen(false)}
              className="px-4 py-2 rounded-xl bg-[var(--raised)] text-[var(--muted)] text-xs font-bold hover:text-[var(--ink)] transition cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={submittingRuleLifecycle || !rejectionReason.trim()}
              onClick={handleRejectNinjaEligibility}
              className="px-4 py-2 rounded-xl bg-[var(--danger)] text-white text-xs font-bold hover:opacity-90 transition disabled:opacity-40 cursor-pointer flex items-center gap-1.5"
            >
              {submittingRuleLifecycle && <RefreshCw size={13} className="animate-spin" />}
              <span>تأكيد الرفض وتوثيق السبب</span>
            </button>
          </div>
        </div>
      </Dialog>

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
                <span className="font-bold num">{formatSar(detailPreviewRow.openingBalance)}</span>
              </div>
              <div>
                <span className="text-[var(--muted)] block">حركة الشهر:</span>
                <span className="font-bold num">{formatSar(detailPreviewRow.periodMovement)}</span>
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
              <div>الشهر: <span className="font-mono">{selectedPeriod}</span></div>
              <div>صافي الرصيد الختامي: <span className="num font-bold text-[var(--accent)]">{formatSar(approvePreviewRow.closingBalance)}</span></div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">السجل المالي صاحب المسير:</label>
              <SearchableSelect
                value={approveSponsorId}
                onChange={setApproveSponsorId}
                placeholder="حدد السجل المالي"
                options={(financeCatalog?.sponsors ?? []).map((sp: any) => ({ value: sp.id, label: `${sp.nameAr || sp.name} (${sp.id})` }))}
              />
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
            <RiderPicker value={adjIqama} onChange={(r) => setAdjIqama(r?.iqama ?? '')} />
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
              <SearchableSelect
                value={adjCategory}
                onChange={setAdjCategory}
                ariaLabel="التصنيف"
                options={[
                  { value: 'fuel', label: 'وقود فعلي' },
                  { value: 'rent', label: 'إيجار مركبة' },
                  { value: 'housing', label: 'سكن' },
                  { value: 'internet', label: 'اتصالات' },
                  { value: 'advance', label: 'سلفة' },
                  { value: 'hr', label: 'تحملات HR' },
                  { value: 'adjustment', label: 'تسوية أخرى' },
                ]}
              />
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
            <RiderPicker
              value={payIqama}
              onChange={(picked) => {
                setPayIqama(picked?.iqama ?? '');
                const r = ridersList.find((x) => x.iqama === picked?.iqama);
                if (r) setPaySponsorId(r.financeSponsorId || r.sponsorId || 'BW');
              }}
            />
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
            <SearchableSelect
              value={payVaultCode}
              onChange={setPayVaultCode}
              placeholder="الحساب الافتراضي للسجل"
              options={(financeCatalog?.vaults ?? []).map((v: any) => ({ value: v.code, label: `${v.nameAr} (${v.code})` }))}
            />
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
                  {formatSar((ledgerData.balance || 0))}
                </span>
              </div>

              <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
                <AutoFilterBar controller={ledgerRowsFilter} totalCount={ledgerRows.length} />
                <table className="w-full text-start text-xs">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)] text-[var(--muted)]">
                    <tr>
                      <AutoFilterTh controller={ledgerRowsFilter} colKey="period" className="p-2 text-start" />
                      <AutoFilterTh controller={ledgerRowsFilter} colKey="kind" className="p-2 text-start" />
                      <AutoFilterTh controller={ledgerRowsFilter} colKey="amount" className="p-2 text-start" />
                      <AutoFilterTh controller={ledgerRowsFilter} colKey="sourceRef" className="p-2 text-start" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {ledgerRowsFilter.rows.map((row, idx) => (
                      <tr key={idx}>
                        <td className="p-2 font-mono">{row.period}</td>
                        <td className="p-2">{row.kind}</td>
                        <td
                          className={`p-2 num font-bold ${
                            row.amount < 0 ? 'text-[var(--danger)]' : 'text-[var(--money-pos)]'
                          }`}
                        >
                          {formatSar((row.amount || 0))}
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
                  {formatSar(selectedDispute.amount)}
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
              <label className="text-xs font-bold">مرجع المنصة الرسمي:</label>
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

      {/* ======================================================== */}
      {/* MODAL: PAY SETTINGS EDITOR (S2 / S5)                     */}
      {/* ======================================================== */}
      <PaySettingsEditorModal
        open={editorModalOpen}
        onClose={() => {
          setEditorModalOpen(false);
          setEditingDoc(null);
          setEditorKind(null);
        }}
        kind={editorKind}
        appSlug={appSlug}
        period={selectedPeriod}
        initialDoc={editingDoc}
        approvedEligibilityList={eligibilityList.filter((e) => e.status === 'approved')}
        catalog={ninjaCatalog}
        onSaved={() => {
          if (editorKind === 'terms') loadTerms();
          if (editorKind === 'scheme') loadSchemes();
          if (editorKind === 'eligibility') loadEligibility();
          if (editorKind === 'account_exception') {
            showToast('تم حفظ الاستثناء الفردي بنجاح', 'success');
          }
        }}
      />

      {/* ======================================================== */}
      {/* DIALOG: REJECT SETTING DRAFT (S2)                        */}
      {/* ======================================================== */}
      <Dialog
        open={rejectSettingModalOpen}
        onClose={() => {
          setRejectSettingModalOpen(false);
          setRejectSettingDoc(null);
          setRejectSettingKind(null);
        }}
        title="رفض مسودة إعدادات الأجور"
        description="توثيق سبب الرفض في سجل التدقيق المالي وإعادة المسودة للمنشئ لتعديلها."
      >
        <div className="space-y-4 pt-2">
          <div className="p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] text-xs space-y-1">
            <div className="flex justify-between">
              <span className="text-[var(--muted)]">الإعداد:</span>
              <span className="font-bold text-[var(--ink)]">
                {rejectSettingDoc?.data?.name || rejectSettingDoc?.code || rejectSettingDoc?.id}
              </span>
            </div>
            <div className="flex justify-between">
              <span className="text-[var(--muted)]">المنشئ:</span>
              <span className="font-mono">@{rejectSettingDoc?.createdBy || 'غير محدد'}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-[var(--muted)]">النوع:</span>
              <span className="font-mono">
                {rejectSettingKind === 'terms'
                  ? 'عقد منصة'
                  : rejectSettingKind === 'scheme'
                  ? 'نظام داخلي'
                  : 'ضوابط أهلية'}
              </span>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">سبب الرفض (إلزامي للتدقيق):</label>
            <textarea
              rows={3}
              value={rejectSettingReason}
              onChange={(e) => setRejectSettingReason(e.target.value)}
              placeholder="اكتب أسباب رفض هذا الإعداد بالتفصيل ليتمكن المنشئ من تصحيحه..."
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => {
                setRejectSettingModalOpen(false);
                setRejectSettingDoc(null);
                setRejectSettingKind(null);
              }}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              onClick={handleConfirmRejectSetting}
              className="px-4 py-2 rounded-xl bg-[var(--danger)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
            >
              تأكيد الرفض
            </button>
          </div>
        </div>
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

function SchemeCategoriesTable({ categories }: { categories: Record<string, any> }) {
  const rows = useMemo(
    () => Object.entries(categories || {}).map(([catKey, cell]) => ({ catKey, cell: cell || {} })),
    [categories]
  );
  const cols = useMemo(
    () => [
      { key: 'catKey', header: 'الفئة التشغيلية (الكفالة × المركبة)', type: 'list' as const, getValue: (r: { catKey: string; cell: any }) => r.catKey },
      {
        key: 'enabled',
        header: 'الحالة',
        type: 'list' as const,
        getValue: (r: { catKey: string; cell: any }) => (r.cell.enabled ? 'enabled' : 'disabled'),
        labelOf: (v: unknown) => (v === 'enabled' ? 'مفعل' : 'معطل'),
        toneOf: (v: unknown) => (v === 'enabled' ? 'success' : 'neutral'),
      },
      { key: 'dailyCommission', header: 'العمولة اليومية', type: 'number' as const, numeric: true, getValue: (r: { catKey: string; cell: any }) => Number(r.cell.dailyCommission ?? 0) },
      { key: 'monthlyCommission', header: 'العمولة الشهرية', type: 'number' as const, numeric: true, getValue: (r: { catKey: string; cell: any }) => Number(r.cell.monthlyCommission ?? 0) },
      { key: 'companyPercent', header: 'نسبة الشركة', type: 'number' as const, numeric: true, getValue: (r: { catKey: string; cell: any }) => Number(r.cell.companyPercent ?? 0) },
      {
        key: 'chargeCompanyVehicle',
        header: 'مركبة الشركة',
        type: 'list' as const,
        getValue: (r: { catKey: string; cell: any }) => (r.cell.chargeCompanyVehicle ? 'yes' : 'no'),
        labelOf: (v: unknown) => (v === 'yes' ? 'نعم (يُخصم)' : 'لا'),
      },
    ],
    []
  );
  const ctrl = useTableAutoFilter(rows, cols);
  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface)]">
      <AutoFilterBar controller={ctrl} totalCount={rows.length} />
      <table className="w-full text-start text-xs">
        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
          <tr>
            <AutoFilterTh controller={ctrl} colKey="catKey" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="enabled" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="dailyCommission" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="monthlyCommission" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="companyPercent" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="chargeCompanyVehicle" className="p-2.5 text-start" />
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--line)]">
          {ctrl.rows.map(({ catKey, cell }) => (
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
              <td className="p-2.5 num font-mono">{cell.dailyCommission ? formatSar(cell.dailyCommission) : '-'}</td>
              <td className="p-2.5 num font-mono">{cell.monthlyCommission ? formatSar(cell.monthlyCommission) : '-'}</td>
              <td className="p-2.5 num font-mono">{cell.companyPercent ? `${cell.companyPercent}%` : '-'}</td>
              <td className="p-2.5 font-mono">{cell.chargeCompanyVehicle ? 'نعم (يُخصم)' : 'لا'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function EligibilityRulesMiniTable({
  rules,
}: {
  rules: Array<{ metric?: string; operator?: string; threshold?: number | string; effect?: { type?: string } }>;
}) {
  const cols = useMemo(
    () => [
      {
        key: 'metric',
        header: 'المعيار',
        type: 'list' as const,
        getValue: (r: any) => r.metric || '',
        labelOf: (v: unknown) => {
          const map: Record<string, string> = {
            orders: 'الطلبات',
            work_days: 'أيام العمل',
            workDays: 'أيام العمل',
            hours: 'ساعات العمل',
            acceptance_rate: 'نسبة القبول',
            attendance_days: 'أيام الحضور',
          };
          return map[String(v ?? '')] || String(v ?? '');
        },
      },
      {
        key: 'operator',
        header: 'عامل المقارنة',
        type: 'list' as const,
        getValue: (r: any) => r.operator || '',
        labelOf: (v: unknown) => {
          const map: Record<string, string> = {
            '>=': 'أكبر من أو يساوي',
            '>': 'أكبر من',
            '<=': 'أصغر من أو يساوي',
            '<': 'أصغر من',
            '==': 'يساوي',
            '=': 'يساوي',
            '!=': 'لا يساوي',
            gte: 'أكبر من أو يساوي',
            gt: 'أكبر من',
            lte: 'أصغر من أو يساوي',
            lt: 'أصغر من',
            eq: 'يساوي',
          };
          return map[String(v ?? '')] || String(v ?? '');
        },
      },
      { key: 'threshold', header: 'الحد الأدنى', type: 'number' as const, numeric: true, getValue: (r: any) => Number(r.threshold ?? 0) },
      {
        key: 'effect',
        header: 'الأثر عند عدم التحقق',
        type: 'list' as const,
        getValue: (r: any) => r.effect?.type || 'zero',
        labelOf: (v: unknown) => (v === 'zero' ? 'تصفير الاستحقاق' : v === 'deduct' ? 'خصم' : v === 'cap' ? 'تحديد سقف' : String(v ?? '')),
        toneOf: (v: unknown) => (v === 'zero' ? 'danger' : 'warning'),
      },
    ],
    []
  );
  const ctrl = useTableAutoFilter(rules, cols);
  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface)]">
      <AutoFilterBar controller={ctrl} totalCount={rules.length} />
      <table className="w-full text-start text-xs">
        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
          <tr>
            <AutoFilterTh controller={ctrl} colKey="metric" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="operator" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="threshold" className="p-2.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="effect" className="p-2.5 text-start" />
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--line)]">
          {ctrl.rows.map((r, idx) => (
            <tr key={`${r.metric || 'rule'}-${idx}`}>
              <td className="p-2.5 font-bold font-mono">{r.metric}</td>
              <td className="p-2.5 font-mono">{r.operator}</td>
              <td className="p-2.5 num font-bold">{r.threshold}</td>
              <td className="p-2.5 text-[var(--danger)] font-mono">{r.effect?.type || 'zero'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function CycleInputsCountsTable({
  countEntries,
  kindLabels,
}: {
  countEntries: Array<[string, number]>;
  kindLabels: Record<string, string>;
}) {
  const rows = useMemo(
    () => countEntries.map(([kind, count]) => ({ kind, label: kindLabels[kind] || kind, count })),
    [countEntries, kindLabels]
  );
  const cols = useMemo(
    () => [
      {
        key: 'kind',
        header: 'النوع',
        type: 'list' as const,
        getValue: (r: { kind: string; label: string; count: number }) => r.kind,
        labelOf: (v: unknown) => kindLabels[String(v ?? '')] || String(v ?? ''),
        toneOf: (_v: unknown, row?: { kind: string; label: string; count: number }) =>
          row && row.count === 0 ? 'warning' : 'success',
      },
      { key: 'count', header: 'عدد الصفوف', type: 'number' as const, numeric: true, getValue: (r: { kind: string; label: string; count: number }) => r.count },
    ],
    [kindLabels]
  );
  const ctrl = useTableAutoFilter(rows, cols);
  return (
    <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
      <AutoFilterBar controller={ctrl} totalCount={rows.length} />
      <table className="w-full text-start text-xs sm:text-sm" data-testid="cycle-inputs-counts-table">
        <thead className="bg-[var(--raised)]/60 border-b border-[var(--line)] text-[var(--muted)] font-semibold">
          <tr>
            <AutoFilterTh controller={ctrl} colKey="kind" className="py-2.5 px-3.5 text-start" />
            <AutoFilterTh controller={ctrl} colKey="count" className="py-2.5 px-3.5 text-start" />
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--line)]">
          {ctrl.rows.map(({ kind, label, count }) => {
            const isZero = count === 0;
            return (
              <tr
                key={kind}
                data-kind={kind}
                data-zero={isZero ? 'true' : 'false'}
                className={
                  isZero
                    ? 'bg-[var(--warning)]/10 text-[var(--warning)] font-bold'
                    : 'hover:bg-[var(--row-hover)] text-[var(--ink)]'
                }
              >
                <td className="py-2.5 px-3.5 font-semibold">
                  <div className="flex items-center gap-2">
                    {isZero && <AlertTriangle size={14} className="shrink-0 text-[var(--warning)]" />}
                    <span>{label}</span>
                  </div>
                </td>
                <td className="py-2.5 px-3.5">
                  <span className="num font-mono font-bold">{count}</span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function GlobalCategoryMatrixTable({ rent, acc }: { rent: any; acc: any }) {
  const rows = useMemo(
    () => [
      {
        id: 'rent',
        label: 'إيجار المركبة اليومي',
        c1: String(rent?.company_car || '60.00'),
        c2: String(rent?.company_bike || '26.67'),
        c3: '0.00',
        c4: String(rent?.company_car || '60.00'),
        c5: String(rent?.company_bike || '26.67'),
        c6: '0.00',
        isMoney: true,
      },
      {
        id: 'account',
        label: 'حساب إيراد الإيجار',
        c1: String(acc?.vehicleIncomeCar || '411102'),
        c2: String(acc?.vehicleIncomeBike || '411102'),
        c3: '',
        c4: String(acc?.vehicleIncomeCar || '411102'),
        c5: String(acc?.vehicleIncomeBike || '411102'),
        c6: '',
        isMoney: false,
      },
    ],
    [rent, acc]
  );
  const cols = useMemo(
    () => [
      { key: 'label', header: 'البند / الفئة', type: 'list' as const, getValue: (r: any) => r.label },
      { key: 'c1', header: 'كفالة × سيارة', type: 'text' as const, getValue: (r: any) => r.c1 },
      { key: 'c2', header: 'كفالة × دباب', type: 'text' as const, getValue: (r: any) => r.c2 },
      { key: 'c3', header: 'كفالة × خاص', type: 'text' as const, getValue: (r: any) => r.c3 },
      { key: 'c4', header: 'خارجي × سيارة', type: 'text' as const, getValue: (r: any) => r.c4 },
      { key: 'c5', header: 'خارجي × دباب', type: 'text' as const, getValue: (r: any) => r.c5 },
      { key: 'c6', header: 'خارجي × خاص', type: 'text' as const, getValue: (r: any) => r.c6 },
    ],
    []
  );
  const ctrl = useTableAutoFilter(rows, cols);
  return (
    <div className="overflow-x-auto">
      <AutoFilterBar controller={ctrl} totalCount={rows.length} />
      <table className="w-full text-start text-xs sm:text-sm">
        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
          <tr>
            <AutoFilterTh controller={ctrl} colKey="label" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c1" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c2" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c3" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c4" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c5" className="p-3 text-start" />
            <AutoFilterTh controller={ctrl} colKey="c6" className="p-3 text-start" />
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--line)]">
          {ctrl.rows.map((r) => (
            <tr key={r.id}>
              <td className="p-3 font-semibold">{r.label}</td>
              {r.isMoney ? (
                <>
                  <td className="p-3 num font-bold text-[var(--accent)]">{formatSar(r.c1)}</td>
                  <td className="p-3 num font-bold text-[var(--accent)]">{formatSar(r.c2)}</td>
                  <td className="p-3 num text-[var(--muted)]">{formatSar(r.c3)}</td>
                  <td className="p-3 num font-bold text-[var(--accent)]">{formatSar(r.c4)}</td>
                  <td className="p-3 num font-bold text-[var(--accent)]">{formatSar(r.c5)}</td>
                  <td className="p-3 num text-[var(--muted)]">{formatSar(r.c6)}</td>
                </>
              ) : (
                <>
                  <td className="p-3 font-mono text-xs">{r.c1}</td>
                  <td className="p-3 font-mono text-xs">{r.c2}</td>
                  <td className="p-3 text-[var(--muted)]">-</td>
                  <td className="p-3 font-mono text-xs">{r.c4}</td>
                  <td className="p-3 font-mono text-xs">{r.c5}</td>
                  <td className="p-3 text-[var(--muted)]">-</td>
                </>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
