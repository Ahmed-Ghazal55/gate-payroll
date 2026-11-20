'use client';

import { formatSar } from '../../lib/money';

import React, { useState, useEffect, useMemo, useCallback, useRef, Suspense } from 'react';
import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import {
  Building2,
  FileText,
  AlertTriangle,
  Download,
  Plus,
  AlertCircle,
  Clock,
  Eye,
  EyeOff,
  Sparkles,
  Users,
  BarChart3,
  Layers,
  RefreshCw,
  ArrowUpLeft,
  Wallet,
  CheckCircle2,
  UserCheck,
  Pencil,
  Trash2,
  Lock,
} from 'lucide-react';
import {
  api,
  ApiError,
  type CompanyContract,
  type CompanyDocumentContract,
  type CompanyExpiringContract,
  type CompanyImportPlanContract,
  type CompanyRegistrationContract,
  type CompaniesStatsResponse,
  type CompanyStatsItem,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { inApp } from '../../lib/paths';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs } from '../../components/ui/SectionTabs';
import { Toolbar } from '../../components/ui/Toolbar';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { SourceBadge } from '../../components/ui/SourceBadge';
import {
  BALANCES_PLACEHOLDER_AR,
  auditActionLabelAr,
  buildCompanyStatsExport,
  buildWorkforceCompanyUrl,
  computeCompanyStatsTotals,
  filterCompanyStatsItems,
  getPlatformColumns,
  normalizeCompaniesTab,
  platformCountFor,
  platformLabelAr,
  type CompaniesTabId,
} from '../../features/companies/company-stats';
import { ImportUploadDialog } from '../../features/imports/ImportUploadDialog';
import { canShowImportStep, COMPANIES_IMPORT } from '../../features/imports/import-steps';

// Official signed company certificate baseline from docs/07-companies.md
const OFFICIAL_COMPANIES_PAYLOAD = [
  {
    code: 'BW',
    nameAr: 'شركة البوابة المقبلة',
    nameEn: 'Albwaba almoqbla Company',
    unifiedNumber: '7015658094',
    crNumber: '4030362130',
    crType: 'رئيسي',
    entityType: 'شركة ذات مسؤولية محدودة',
    characteristic: 'شخص واحد',
    city: 'جدة',
    address: 'حي الصفا، وادي الحدره',
    capitalSar: 14000000,
    crVersion: 6,
    issueDate: '2019-07-28',
    issueDateHijri: '1440-11-25',
    annualConfirmation: '2027-05-22',
    annualConfirmationHijri: '1448-12-16',
    status: 'active',
    activitiesIncomplete: true,
    activities: [
      { isic: '410010', nameAr: 'الإنشاءات العامة للمباني السكنية' },
      { isic: '433010', nameAr: 'تشطيب المباني' },
      { isic: '492300', nameAr: 'النقل الثقيل للبضائع' },
      { isic: '492311', nameAr: 'النقل الخفيف للبضائع' },
    ],
  },
  {
    code: 'EX',
    nameAr: 'شركة إكسبرس جايت',
    nameEn: 'EXPRESS GATE Company',
    unifiedNumber: '7034861059',
    crNumber: '4031283351',
    crType: 'رئيسي',
    entityType: 'شركة ذات مسؤولية محدودة',
    characteristic: 'شخص واحد',
    city: 'جدة',
    address: 'حي الحمدانية، عبدالله بن عبدالرحمن مرداد',
    capitalSar: 14000000,
    crVersion: 5,
    issueDate: '2023-06-18',
    issueDateHijri: '1444-11-29',
    annualConfirmation: '2027-06-18',
    annualConfirmationHijri: '1449-01-13',
    status: 'active',
    activitiesIncomplete: false,
    activities: [
      { isic: '492311', nameAr: 'النقل الخفيف للبضائع' },
      { isic: '522931', nameAr: 'الخدمات اللوجستية' },
    ],
  },
  {
    code: 'TR',
    nameAr: 'مؤسسة البوابة المقبلة للتجارة',
    nameEn: 'Albwaba Almoqbla Trading Est.',
    unifiedNumber: '7038745530',
    crNumber: '4030551794',
    crType: 'رئيسي',
    entityType: 'مؤسسة فردية',
    characteristic: 'مؤسسة',
    city: 'جدة',
    address: 'حي الصفا، الأمير محمد بن عبدالعزيز الفرعي',
    capitalSar: 5000,
    crVersion: 9,
    issueDate: '2024-04-01',
    issueDateHijri: '1445-09-22',
    annualConfirmation: '2027-04-01',
    annualConfirmationHijri: '1448-10-24',
    status: 'active',
    activitiesIncomplete: false,
    activities: [
      { isic: '410010', nameAr: 'الإنشاءات العامة للمباني السكنية' },
      { isic: '452022', nameAr: 'إصلاح ميكانيكا وكهرباء وسمكرة ودهان السيارات' },
      { isic: '452041', nameAr: 'تبديل الزيوت' },
      { isic: '492311', nameAr: 'النقل الخفيف للبضائع' },
    ],
  },
];

function getDaysRemaining(dateStr?: string): number | null {
  if (!dateStr) return null;
  const target = new Date(dateStr);
  if (isNaN(target.getTime())) return null;
  const now = new Date();
  const diffTime = target.getTime() - now.getTime();
  return Math.ceil(diffTime / (1000 * 60 * 60 * 24));
}

function isDueWithinDays(dateStr?: string, days = 60): boolean {
  const rem = getDaysRemaining(dateStr);
  return rem !== null && rem <= days;
}

function formatNumber(n: number | undefined): string {
  return (Number(n) || 0).toLocaleString('en-US');
}

function CompaniesPageContent() {
  const { toast } = useToast();
  const { isAdmin, can } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const canManage = isAdmin || can('companies.manage');

  const tabFromUrl = normalizeCompaniesTab(searchParams?.get('tab'));
  const [activeTab, setActiveTab] = useState<CompaniesTabId>(tabFromUrl);

  useEffect(() => {
    setActiveTab(normalizeCompaniesTab(searchParams?.get('tab')));
  }, [searchParams]);

  const selectTab = useCallback(
    (next: CompaniesTabId) => {
      setActiveTab(next);
      if (searchParams && router) {
        const params = new URLSearchParams(searchParams.toString());
        if (params.get('tab') !== next) {
          params.set('tab', next);
          router.replace(`${pathname}?${params.toString()}`, { scroll: false });
        }
      }
    },
    [pathname, router, searchParams]
  );

  // Core company tables (our DB)
  const [companies, setCompanies] = useState<CompanyContract[]>([]);
  const [registrations, setRegistrations] = useState<CompanyRegistrationContract[]>([]);
  const [expiring, setExpiring] = useState<CompanyExpiringContract[]>([]);
  const [auditLogs, setAuditLogs] = useState<Array<{ id: string; action: string; actor: string; at: string }>>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Live company statistics (Omar's ERP read-only via GET /api/companies/stats)
  const [stats, setStats] = useState<CompaniesStatsResponse | null>(null);
  const [statsLoading, setStatsLoading] = useState(true);
  const [statsError, setStatsError] = useState<string | null>(null);
  const statsAbort = useRef<AbortController | null>(null);

  // Filters & Search
  const [searchQuery, setSearchQuery] = useState('');
  const [entityTypeFilter, setEntityTypeFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [expiringOnly, setExpiringOnly] = useState(false);
  const [expiringBucket, setExpiringBucket] = useState<'all' | 'soon30' | 'expired'>('all');

  // Drawer state
  const [selectedCompany, setSelectedCompany] = useState<CompanyContract | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerTab, setDrawerTab] = useState<'details' | 'registrations' | 'activities' | 'documents' | 'stats' | 'audit'>('details');
  const [companyDocs, setCompanyDocs] = useState<CompanyDocumentContract[]>([]);
  const [companyRegs, setCompanyRegs] = useState<CompanyRegistrationContract[]>([]);
  const [loadingDocs, setLoadingDocs] = useState(false);

  // Company Create/Edit Modal state
  const [companyModalOpen, setCompanyModalOpen] = useState(false);
  const [editingCompanyId, setEditingCompanyId] = useState<string | null>(null);
  const [companyForm, setCompanyForm] = useState({
    code: '',
    nameAr: '',
    shortNameAr: '',
    unifiedNumber: '',
    commercialReg: '',
    city: '',
    status: 'active',
  });
  const [savingCompany, setSavingCompany] = useState(false);

  // CR Create/Edit Modal state
  const [crModalOpen, setCrModalOpen] = useState(false);
  const [crCompanyId, setCrCompanyId] = useState<string>('');
  const [editingCrId, setEditingCrId] = useState<string | null>(null);
  const [crForm, setCrForm] = useState({
    number: '',
    city: '',
    issueDate: '',
    expiryDate: '',
    status: 'active',
  });
  const [savingCr, setSavingCr] = useState(false);

  // Document upload state (company documents remain our uploads per H32)
  const [uploadDocModalOpen, setUploadDocModalOpen] = useState(false);
  const [docType, setDocType] = useState('شهادة السجل التجاري');
  const [docNumber, setDocNumber] = useState('');
  const [docExpiryDate, setDocExpiryDate] = useState('');
  const [docFile, setDocFile] = useState<File | null>(null);
  const [uploadingDoc, setUploadingDoc] = useState(false);

  // User balance visibility preference (per user, localStorage)
  const [showBalances, setShowBalances] = useState<boolean>(() => {
    if (typeof window === 'undefined') return true;
    try {
      const saved = localStorage.getItem('gate_show_balances');
      return saved !== null ? saved === 'true' : true;
    } catch {
      return true;
    }
  });

  const toggleShowBalances = () => {
    setShowBalances((prev) => {
      const next = !prev;
      try {
        localStorage.setItem('gate_show_balances', String(next));
      } catch {
        // ignore storage errors
      }
      return next;
    });
  };

  // File import dialog (catalog companies → same preview/commit endpoints)
  const [fileImportOpen, setFileImportOpen] = useState(false);
  // Import Modal State (Admin only — official certificates payload)
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [importPreviewPlan, setImportPreviewPlan] = useState<CompanyImportPlanContract | null>(null);
  const [importLoading, setImportLoading] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);

  // Load local company data
  const fetchData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [compList, expList] = await Promise.all([
        api.getCompaniesList(),
        api.getExpiringCompanies(60),
      ]);
      const safeCompanies = Array.isArray(compList) ? compList : [];
      setCompanies(safeCompanies);
      setExpiring(Array.isArray(expList) ? expList : []);
      const regLists = await Promise.all(
        safeCompanies.map((c) => api.getCompanyRegistrations(c.id).catch(() => [] as CompanyRegistrationContract[]))
      );
      setRegistrations(regLists.flat());
      if (isAdmin) {
        try {
          const logs = await api.getAuditLogs({ page: 1, pageSize: 50 });
          setAuditLogs(Array.isArray(logs?.items) ? logs.items : []);
        } catch {
          setAuditLogs([]);
        }
      }
    } catch (err: unknown) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل بيانات الشركات والسجلات.';
      setError(message);
    } finally {
      setLoading(false);
    }
  }, [isAdmin]);

  // Load live company stats independently so ERP outages do not block tabs 1-3
  const fetchStats = useCallback(async () => {
    statsAbort.current?.abort();
    const ac = new AbortController();
    statsAbort.current = ac;
    setStatsLoading(true);
    setStatsError(null);
    try {
      const res = await api.getCompaniesStats(ac.signal);
      if (ac.signal.aborted) return;
      setStats(res);
    } catch (err: unknown) {
      if (ac.signal.aborted || (err instanceof Error && err.name === 'AbortError')) return;
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل إحصائيات الشركات من نظام الموارد.';
      setStatsError(message);
      setStats(null);
    } finally {
      if (!ac.signal.aborted) setStatsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  useEffect(() => {
    fetchStats();
    return () => {
      statsAbort.current?.abort();
    };
  }, [fetchStats]);

  // Deep-link from global search: /companies/?id=…
  useEffect(() => {
    const id = searchParams?.get('id');
    if (!id || companies.length === 0) return;
    const match = companies.find((c) => c.id === id);
    if (match) {
      setSelectedCompany(match);
      setDrawerOpen(true);
    }
  }, [searchParams, companies]);

  const statsByCompanyId = useMemo(() => {
    const map = new Map<string, CompanyStatsItem>();
    for (const item of stats?.items || []) {
      map.set(item.companyId, item);
    }
    return map;
  }, [stats]);

  const canFilterWorkforce = useMemo(
    () => (stats?.allowedActions || []).includes('filter_workforce'),
    [stats]
  );

  const canExportStats = useMemo(
    () => (stats?.allowedActions || []).includes('export'),
    [stats]
  );

  const canExportTable = useMemo(
    () => (activeTab === 'stats' ? canExportStats : isAdmin || can('companies.view') || can('riders.view')),
    [activeTab, canExportStats, isAdmin, can]
  );

  const canViewBalances = useMemo(
    () =>
      isAdmin ||
      can('companies.balances.view') ||
      (stats?.allowedActions || []).includes('view_balances'),
    [isAdmin, can, stats]
  );

  // Open drawer and load company documents + registrations
  const handleOpenDrawer = async (comp: CompanyContract) => {
    setSelectedCompany(comp);
    setDrawerTab('details');
    setDrawerOpen(true);
    setLoadingDocs(true);
    try {
      const [docs, regs] = await Promise.all([
        api.getCompanyDocuments(comp.id).catch(() => [] as CompanyDocumentContract[]),
        api.getCompanyRegistrations(comp.id).catch(() => [] as CompanyRegistrationContract[]),
      ]);
      setCompanyDocs(Array.isArray(docs) ? docs : []);
      setCompanyRegs(Array.isArray(regs) ? regs : []);
    } finally {
      setLoadingDocs(false);
    }
  };

  const openCreateCompanyModal = () => {
    setEditingCompanyId(null);
    setCompanyForm({
      code: '',
      nameAr: '',
      shortNameAr: '',
      unifiedNumber: '',
      commercialReg: '',
      city: '',
      status: 'active',
    });
    setCompanyModalOpen(true);
  };

  const openEditCompanyModal = (comp: CompanyContract) => {
    setEditingCompanyId(comp.id);
    setCompanyForm({
      code: comp.code || '',
      nameAr: comp.nameAr || '',
      shortNameAr: comp.shortNameAr || '',
      unifiedNumber: comp.unifiedNumber || '',
      commercialReg: comp.crNumber || comp.commercialReg || '',
      city: comp.city || '',
      status: comp.status || 'active',
    });
    setCompanyModalOpen(true);
  };

  const handleSaveCompany = async (e: React.FormEvent) => {
    e.preventDefault();
    if (savingCompany || !companyForm.nameAr.trim()) return;
    setSavingCompany(true);
    try {
      if (editingCompanyId) {
        await api.updateCompany(editingCompanyId, companyForm);
        toast('تم تحديث بيانات الشركة بنجاح', 'success');
      } else {
        await api.createCompany(companyForm);
        toast('تمت إضافة الشركة بنجاح', 'success');
      }
      setCompanyModalOpen(false);
      await Promise.all([fetchData(), fetchStats()]);
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر حفظ بيانات الشركة';
      toast(message, 'danger');
    } finally {
      setSavingCompany(false);
    }
  };

  const handleArchiveCompany = async (comp: CompanyContract) => {
    if (!canManage) return;
    if (typeof window !== 'undefined' && !window.confirm(`أرشفة الشركة "${comp.nameAr}" مع الاحتفاظ بسجلاتها المالية؟`)) {
      return;
    }
    try {
      await api.archiveCompany(comp.id);
      toast('تمت أرشفة الشركة بنجاح', 'success');
      if (selectedCompany?.id === comp.id) setDrawerOpen(false);
      await Promise.all([fetchData(), fetchStats()]);
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر أرشفة الشركة';
      toast(message, 'danger');
    }
  };

  const openCreateCrModal = (companyId: string) => {
    setCrCompanyId(companyId);
    setEditingCrId(null);
    setCrForm({ number: '', city: '', issueDate: '', expiryDate: '', status: 'active' });
    setCrModalOpen(true);
  };

  const openEditCrModal = (reg: CompanyRegistrationContract) => {
    setCrCompanyId(reg.companyId);
    setEditingCrId(reg.id);
    setCrForm({
      number: reg.number || '',
      city: reg.city || '',
      issueDate: reg.issueDate || '',
      expiryDate: reg.expiryDate || reg.annualConfirmation || '',
      status: reg.status || 'active',
    });
    setCrModalOpen(true);
  };

  const handleSaveCr = async (e: React.FormEvent) => {
    e.preventDefault();
    if (savingCr || !crCompanyId || !crForm.number.trim()) return;
    setSavingCr(true);
    try {
      if (editingCrId) {
        await api.updateCompanyRegistration(crCompanyId, editingCrId, crForm);
        toast('تم تحديث السجل التجاري بنجاح', 'success');
      } else {
        await api.createCompanyRegistration(crCompanyId, crForm);
        toast('تمت إضافة السجل التجاري بنجاح', 'success');
      }
      setCrModalOpen(false);
      await fetchData();
      if (selectedCompany?.id === crCompanyId) {
        const updatedRegs = await api.getCompanyRegistrations(crCompanyId).catch(() => [] as CompanyRegistrationContract[]);
        setCompanyRegs(Array.isArray(updatedRegs) ? updatedRegs : []);
      }
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر حفظ السجل التجاري';
      toast(message, 'danger');
    } finally {
      setSavingCr(false);
    }
  };

  const handleArchiveCr = async (companyId: string, registrationId: string, number: string) => {
    if (!canManage) return;
    if (typeof window !== 'undefined' && !window.confirm(`أرشفة السجل التجاري رقم "${number}"؟`)) {
      return;
    }
    try {
      await api.archiveCompanyRegistration(companyId, registrationId);
      toast('تمت أرشفة السجل التجاري بنجاح', 'success');
      await fetchData();
      if (selectedCompany?.id === companyId) {
        const updatedRegs = await api.getCompanyRegistrations(companyId).catch(() => [] as CompanyRegistrationContract[]);
        setCompanyRegs(Array.isArray(updatedRegs) ? updatedRegs : []);
      }
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر أرشفة السجل التجاري';
      toast(message, 'danger');
    }
  };

  // Upload document (stays as our uploads per H32)
  const handleUploadDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedCompany || !docFile || uploadingDoc) return;

    setUploadingDoc(true);
    try {
      const reader = new FileReader();
      const base64Promise = new Promise<string>((resolve, reject) => {
        reader.onload = () => resolve(String(reader.result).split(',')[1]);
        reader.onerror = reject;
        reader.readAsDataURL(docFile);
      });
      const dataBase64 = await base64Promise;

      const created = await api.uploadCompanyDocument(selectedCompany.id, {
        type: docType,
        number: docNumber.trim() || undefined,
        expiryDate: docExpiryDate || undefined,
        fileName: docFile.name,
        dataBase64,
      });

      setCompanyDocs((prev) => [created, ...prev]);
      toast('تم رفع المستند بنجاح', 'success');
      setUploadDocModalOpen(false);
      setDocFile(null);
      setDocNumber('');
      setDocExpiryDate('');
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر رفع المستند';
      toast(message, 'danger');
    } finally {
      setUploadingDoc(false);
    }
  };

  // Admin Import Preview
  const handleRunImportPreview = async () => {
    setImportLoading(true);
    setImportError(null);
    try {
      const plan = await api.previewCompanyImport(OFFICIAL_COMPANIES_PAYLOAD);
      setImportPreviewPlan(plan);
    } catch (err: unknown) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر استخراج خطة استيراد الشهادات';
      setImportError(message);
    } finally {
      setImportLoading(false);
    }
  };

  // Admin Import Commit
  const handleCommitImport = async () => {
    if (!importPreviewPlan || importLoading) return;
    setImportLoading(true);
    setImportError(null);
    try {
      await api.commitCompanyImport({
        rows: importPreviewPlan.rows as Record<string, unknown>[],
        fingerprint: importPreviewPlan.fingerprint,
        confirm: true,
      });
      toast('تم اعتماد وتحديث شهادات الشركات بنجاح', 'success');
      setImportModalOpen(false);
      setImportPreviewPlan(null);
      await Promise.all([fetchData(), fetchStats()]);
    } catch (err: unknown) {
      const message =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'فشل اعتماد استيراد بيانات الشركات';
      setImportError(message);
    } finally {
      setImportLoading(false);
    }
  };

  // Filtered companies
  const filteredCompanies = useMemo(() => {
    return companies.filter((c) => {
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const match =
          c.nameAr.toLowerCase().includes(q) ||
          (c.nameEn && c.nameEn.toLowerCase().includes(q)) ||
          c.code.toLowerCase().includes(q) ||
          (c.unifiedNumber && c.unifiedNumber.includes(q)) ||
          (c.commercialReg && c.commercialReg.includes(q)) ||
          (c.crNumber && c.crNumber.includes(q)) ||
          (c.city && c.city.toLowerCase().includes(q));
        if (!match) return false;
      }
      if (entityTypeFilter !== 'all' && c.entityType !== entityTypeFilter) {
        return false;
      }
      if (statusFilter !== 'all' && c.status !== statusFilter) {
        return false;
      }
      if (expiringOnly && !isDueWithinDays(c.annualConfirmation, 60)) {
        return false;
      }
      return true;
    });
  }, [companies, searchQuery, entityTypeFilter, statusFilter, expiringOnly]);

  // Filtered stats items (honors search query on stats tab too)
  const filteredStatsItems = useMemo(
    () => filterCompanyStatsItems(stats?.items || [], searchQuery),
    [stats, searchQuery]
  );

  const filteredStatsTotals = useMemo(
    () => computeCompanyStatsTotals(filteredStatsItems),
    [filteredStatsItems]
  );

  const isStatsFiltered = searchQuery.trim().length > 0;

  const statsPlatformColumns = useMemo(
    () => getPlatformColumns(filteredStatsItems, filteredStatsTotals.byPlatform),
    [filteredStatsItems, filteredStatsTotals]
  );

  const companiesById = useMemo(() => {
    const map = new Map<string, CompanyContract>();
    for (const c of companies) map.set(c.id, c);
    return map;
  }, [companies]);

  // Registrations list: real fetched commercial_registration records plus fallback for seeded companies without child CR rows
  const allRegistrations = useMemo(() => {
    const seen = new Set<string>();
    const list: Array<{
      id: string;
      companyId: string;
      companyNameAr: string;
      companyCode: string;
      number: string;
      crType: string;
      city: string;
      issueDate?: string;
      issueDateHijri?: string;
      annualConfirmation?: string;
      annualConfirmationHijri?: string;
      crVersion?: number;
      status: string;
      isRealRecord: boolean;
    }> = [];

    for (const reg of registrations) {
      const comp = companiesById.get(reg.companyId);
      const key = `${reg.companyId}:${reg.number}`;
      seen.add(key);
      list.push({
        id: reg.id,
        companyId: reg.companyId,
        companyNameAr: comp?.nameAr || reg.company || '—',
        companyCode: comp?.code || '',
        number: reg.number,
        crType: comp?.crType || '—',
        city: reg.city || comp?.city || '—',
        issueDate: reg.issueDate,
        issueDateHijri: reg.issueDateHijri,
        annualConfirmation: reg.annualConfirmation || reg.expiryDate,
        annualConfirmationHijri: reg.annualConfirmationHijri,
        crVersion: reg.crVersion,
        status: reg.status || 'active',
        isRealRecord: true,
      });
    }

    for (const c of companies) {
      const num = c.crNumber || c.commercialReg;
      if (!num) continue;
      const key = `${c.id}:${num}`;
      if (seen.has(key)) continue;
      seen.add(key);
      list.push({
        id: `cr-${c.id}`,
        companyId: c.id,
        companyNameAr: c.nameAr,
        companyCode: c.code,
        number: num,
        crType: c.crType || '—',
        city: c.city || '—',
        issueDate: c.issueDate,
        issueDateHijri: c.issueDateHijri,
        annualConfirmation: c.annualConfirmation,
        annualConfirmationHijri: c.annualConfirmationHijri,
        crVersion: c.crVersion,
        status: c.status || 'active',
        isRealRecord: false,
      });
    }

    return list;
  }, [registrations, companies, companiesById]);

  const filteredRegistrations = useMemo(() => {
    if (!searchQuery.trim()) return allRegistrations;
    const q = searchQuery.toLowerCase().trim();
    return allRegistrations.filter(
      (cr) =>
        cr.number.toLowerCase().includes(q) ||
        cr.companyNameAr.toLowerCase().includes(q) ||
        cr.companyCode.toLowerCase().includes(q) ||
        cr.city.toLowerCase().includes(q)
    );
  }, [allRegistrations, searchQuery]);

  const filteredExpiring = useMemo(() => {
    const q = searchQuery.toLowerCase().trim();
    return expiring.filter((item) => {
      const comp = companiesById.get(item.companyId);
      if (q) {
        const hay = [
          comp?.nameAr || '',
          comp?.code || '',
          item.number || '',
          item.type || '',
          item.kind === 'commercial_registration' ? 'سجل تجاري' : 'وثيقة شركة',
        ]
          .join(' ')
          .toLowerCase();
        if (!hay.includes(q)) return false;
      }
      const rem = getDaysRemaining(item.annualConfirmation || item.expiryDate);
      if (expiringBucket === 'soon30') return rem !== null && rem > 0 && rem <= 30;
      if (expiringBucket === 'expired') return rem !== null && rem <= 0;
      return true;
    });
  }, [expiring, companiesById, searchQuery, expiringBucket]);

  // Summary metrics (recount against active filters per H3 / U-W3-03)
  const filteredCompanyIds = useMemo(
    () => new Set(filteredCompanies.map((c) => c.id)),
    [filteredCompanies]
  );

  const activeRegsCount = useMemo(
    () => allRegistrations.filter((r) => filteredCompanyIds.has(r.companyId) && r.status === 'active').length,
    [allRegistrations, filteredCompanyIds]
  );

  const expiringSoon30Count = useMemo(
    () =>
      expiring.filter((item) => {
        if (!filteredCompanyIds.has(item.companyId)) return false;
        const rem = getDaysRemaining(item.annualConfirmation || item.expiryDate);
        return rem !== null && rem > 0 && rem <= 30;
      }).length,
    [expiring, filteredCompanyIds]
  );

  const expiredDocsCount = useMemo(
    () =>
      expiring.filter((item) => {
        if (!filteredCompanyIds.has(item.companyId)) return false;
        const rem = getDaysRemaining(item.annualConfirmation || item.expiryDate);
        return rem !== null && rem <= 0;
      }).length,
    [expiring, filteredCompanyIds]
  );

  const filteredStatsForCompanies = useMemo(() => {
    if (activeTab === 'stats') return filteredStatsTotals;
    const matchingStats = (stats?.items || []).filter((item) => filteredCompanyIds.has(item.companyId));
    return computeCompanyStatsTotals(matchingStats);
  }, [activeTab, filteredStatsTotals, stats, filteredCompanyIds]);

  // Export Excel / CSV
  const handleExport = async (type: 'all' | 'filtered' | 'csv') => {
    try {
      if (activeTab === 'stats' && stats) {
        const exportSource = type === 'all' ? stats.items : filteredStatsItems;
        const { headers, rows, numericColumns } = buildCompanyStatsExport(exportSource);
        if (type === 'csv') {
          const csvContent = [
            headers.join(','),
            ...rows.map((r) => r.map((val) => `"${String(val).replace(/"/g, '""')}"`).join(',')),
          ].join('\n');
          const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8' });
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `companies_stats_${new Date().toISOString().slice(0, 10)}.csv`;
          a.click();
          URL.revokeObjectURL(url);
          toast('تم تصدير ملف إحصائيات الشركات بنجاح', 'success');
          return;
        }
        const blob = await api.exportExcel({
          headers,
          rows,
          numericColumns,
          filename: 'companies_stats.xlsx',
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `companies_stats_${new Date().toISOString().slice(0, 10)}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تصدير ملف إحصائيات الشركات بنجاح', 'success');
        return;
      }

      const rowsToExport = type === 'all' ? companies : filteredCompanies;
      const headers = [
        'رمز الشركة',
        'اسم المنشأة',
        'الرقم الموحد',
        'رقم السجل التجاري',
        'نوع الكيان',
        'المدينة',
        'رأس المال بالريال',
        'المكفولون',
        'التأكيد السنوي القادم',
        'الحالة',
      ];
      const rows = rowsToExport.map((c) => [
        c.code,
        c.nameAr,
        c.unifiedNumber || '',
        c.crNumber || c.commercialReg || '',
        c.entityType || '',
        c.city || '',
        Number(c.capitalSar || 0),
        statsByCompanyId.get(c.id)?.sponsorships.total ?? '',
        c.annualConfirmation ? `${c.annualConfirmationHijri || ''} (${c.annualConfirmation})` : '',
        c.status === 'active' ? 'نشط' : 'غير نشط',
      ]);

      if (type === 'csv') {
        const csvContent = [
          headers.join(','),
          ...rows.map((r) => r.map((val) => `"${String(val).replace(/"/g, '""')}"`).join(',')),
        ].join('\n');
        const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `companies_${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تصدير الملف النصي بنجاح', 'success');
        return;
      }

      const blob = await api.exportExcel({
        headers,
        rows,
        numericColumns: [6, 7],
        filename: 'companies_registry.xlsx',
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `companies_${new Date().toISOString().slice(0, 10)}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      toast('تم تصدير جدول البيانات بنجاح', 'success');
    } catch (err: unknown) {
      const message =
        err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'تعذر تصدير الملف';
      toast(message, 'danger');
    }
  };

  const selectedCompanyStats = selectedCompany ? statsByCompanyId.get(selectedCompany.id) ?? null : null;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="الشركات والسجلات التجارية"
        eyebrow="الكيانات القانونية والشهادات الرسمية"
        description="إدارة الكيانات القانونية والسجلات والشهادات الصادرة من وزارة التجارة وتنبيهات التأكيد السنوي وإحصائيات الكفالات وحسابات المنصات."
        primaryAction={
          isAdmin
            ? {
                label: 'استيراد بيانات الشركات',
                onClick: () => {
                  setImportModalOpen(true);
                  handleRunImportPreview();
                },
                icon: <Sparkles size={16} />,
              }
            : undefined
        }
      >
        {canShowImportStep(COMPANIES_IMPORT, { isAdmin, can }) && (
          <button
            type="button"
            data-testid="import-companies"
            onClick={() => setFileImportOpen(true)}
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] active:scale-[0.98] transition shadow-xs"
          >
            <Download size={15} aria-hidden="true" />
            <span>استيراد الشركات</span>
          </button>
        )}
        {canManage && (
          <button
            type="button"
            onClick={openCreateCompanyModal}
            className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-95 active:scale-[0.98] transition shadow-xs"
          >
            <Plus size={15} aria-hidden="true" />
            <span>إضافة شركة</span>
          </button>
        )}
        <button
          type="button"
          onClick={toggleShowBalances}
          className="inline-flex items-center gap-1.5 px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] active:scale-[0.98] transition shadow-xs"
          title={showBalances ? 'إخفاء الأرصدة ورؤوس الأموال' : 'إظهار الأرصدة ورؤوس الأموال'}
        >
          {showBalances ? <EyeOff size={15} /> : <Eye size={15} />}
          <span>{showBalances ? 'إخفاء الأرصدة' : 'إظهار الأرصدة'}</span>
        </button>
      </PageHeader>

      {/* Top KPI Filter Strip (6 filter cards per U-W3-03 / H3 — recount on active filters) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-3">
        <StatCard
          label="إجمالي الشركات"
          value={formatNumber(filteredCompanies.length)}
          hint={searchQuery.trim() || statusFilter !== 'all' || entityTypeFilter !== 'all' || expiringOnly ? 'حسب الفلتر الحالي' : 'الكيانات ضمن النطاق'}
          icon={Building2}
          active={activeTab === 'companies' && statusFilter === 'all' && !expiringOnly}
          onClick={() => {
            setStatusFilter('all');
            setEntityTypeFilter('all');
            setExpiringOnly(false);
            setExpiringBucket('all');
            selectTab('companies');
          }}
        />
        <StatCard
          label="السجلات النشطة"
          value={formatNumber(activeRegsCount)}
          hint="عرض السجلات التجارية"
          icon={FileText}
          active={activeTab === 'cr'}
          onClick={() => {
            setExpiringBucket('all');
            selectTab('cr');
          }}
        />
        <StatCard
          label="وثائق تنتهي خلال 30 يومًا"
          value={formatNumber(expiringSoon30Count)}
          hint="تصفية المستحقة قريبًا"
          icon={Clock}
          active={activeTab === 'expiring' && expiringBucket === 'soon30'}
          className={expiringSoon30Count > 0 ? 'border-[var(--warning)]/50' : undefined}
          onClick={() => {
            if (activeTab === 'expiring' && expiringBucket === 'soon30') {
              setExpiringBucket('all');
            } else {
              setExpiringBucket('soon30');
              selectTab('expiring');
            }
          }}
        />
        <StatCard
          label="وثائق منتهية"
          value={formatNumber(expiredDocsCount)}
          hint="تصفية الوثائق المنتهية"
          icon={AlertTriangle}
          active={activeTab === 'expiring' && expiringBucket === 'expired'}
          className={expiredDocsCount > 0 ? 'border-[var(--danger)]/50' : undefined}
          onClick={() => {
            if (activeTab === 'expiring' && expiringBucket === 'expired') {
              setExpiringBucket('all');
            } else {
              setExpiringBucket('expired');
              selectTab('expiring');
            }
          }}
        />
        <StatCard
          label="إجمالي المكفولين"
          value={stats ? formatNumber(filteredStatsForCompanies.sponsoredTotal) : '—'}
          hint={stats ? `مناديب ${formatNumber(filteredStatsForCompanies.sponsoredRiders)} · إداريون ${formatNumber(filteredStatsForCompanies.sponsoredAdmins)}` : 'من نظام الموارد'}
          icon={Users}
          active={activeTab === 'stats'}
          loading={statsLoading && !stats}
          onClick={() => selectTab('stats')}
        />
        <StatCard
          label="حسابات المنصات النشطة"
          value={stats ? formatNumber(filteredStatsForCompanies.platformAccountsActive) : '—'}
          hint={stats ? `إجمالي ${formatNumber(filteredStatsForCompanies.platformAccountsTotal)} · مسند ${formatNumber(filteredStatsForCompanies.platformAccountsAssigned)}` : 'من نظام الموارد'}
          icon={Layers}
          active={activeTab === 'stats'}
          loading={statsLoading && !stats}
          onClick={() => selectTab('stats')}
        />
      </div>

      {/* Horizontal Inner Tabs (?tab=companies|cr|expiring|stats) */}
      <SectionTabs
        ariaLabel="أقسام الشركات والسجلات"
        tabs={[
          { id: 'companies', label: 'الشركات والكفلاء', icon: <Building2 size={15} /> },
          { id: 'cr', label: 'السجلات التجارية', icon: <FileText size={15} />, badge: allRegistrations.length },
          {
            id: 'expiring',
            label: 'الوثائق والانتهاءات',
            icon: <Clock size={15} />,
            badge: expiring.length > 0 ? expiring.length : undefined,
          },
          {
            id: 'stats',
            label: 'الإحصائيات والمؤشرات',
            icon: <BarChart3 size={15} />,
            badge: stats ? stats.totals.sponsoredTotal : undefined,
          },
        ]}
        activeTab={activeTab}
        onChange={(tab) => setActiveTab(normalizeCompaniesTab(tab))}
      />

      {/* Toolbar */}
      <Toolbar
        searchPlaceholder="بحث باسم الشركة أو الرمز أو السجل التجاري أو الرقم الموحد أو المدينة..."
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        extraControls={
          activeTab === 'companies' ? (
            <div className="flex flex-wrap items-center gap-2">
              <SearchableSelect
                value={entityTypeFilter}
                onChange={setEntityTypeFilter}
                ariaLabel="تصفية حسب نوع الكيان"
                className="w-48"
                options={[
                  { value: 'all', label: 'كل أنواع الكيانات' },
                  { value: 'شركة ذات مسؤولية محدودة', label: 'شركة ذات مسؤولية محدودة' },
                  { value: 'مؤسسة فردية', label: 'مؤسسة فردية' },
                ]}
              />

              <SearchableSelect
                value={statusFilter}
                onChange={setStatusFilter}
                ariaLabel="تصفية حسب الحالة"
                className="w-36"
                options={[
                  { value: 'all', label: 'كل الحالات' },
                  { value: 'active', label: 'نشط' },
                  { value: 'inactive', label: 'غير نشط' },
                ]}
              />

              <button
                type="button"
                onClick={() => setExpiringOnly(!expiringOnly)}
                className={`h-10 px-3 rounded-xl border text-xs font-semibold flex items-center gap-1.5 transition ${
                  expiringOnly
                    ? 'bg-[var(--warning)]/15 border-[var(--warning)] text-[var(--warning)]'
                    : 'bg-[var(--surface)] border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <Clock size={14} />
                <span>استحقاق أقل من 60 يومًا</span>
              </button>
            </div>
          ) : activeTab === 'cr' && canManage && companies.length > 0 ? (
            <button
              type="button"
              onClick={() => openCreateCrModal(companies[0].id)}
              className="h-10 px-3 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold inline-flex items-center gap-1.5 hover:opacity-95 transition"
            >
              <Plus size={14} aria-hidden="true" />
              <span>إضافة سجل تجاري</span>
            </button>
          ) : activeTab === 'stats' ? (
            <div className="flex flex-wrap items-center gap-2">
              <SourceBadge source={stats?.source} asOf={stats?.asOf} />
              <button
                type="button"
                onClick={fetchStats}
                disabled={statsLoading}
                className="h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] disabled:opacity-50 inline-flex items-center gap-1.5 transition"
              >
                <RefreshCw size={14} className={statsLoading ? 'animate-spin' : undefined} />
                <span>تحديث الإحصائيات</span>
              </button>
            </div>
          ) : undefined
        }
        onExport={
          canExportTable
            ? (format) => {
                if (format === 'excel-all') handleExport('all');
                else if (format === 'excel-filtered') handleExport('filtered');
                else if (format === 'csv-filtered') handleExport('csv');
              }
            : undefined
        }
      />

      {/* Main Tab Panels */}
      {activeTab === 'stats' ? (
        /* Sub-tab 4: الإحصائيات (U-W3-03 / H32) */
        <div
          id="tabpanel-stats"
          role="tabpanel"
          aria-labelledby="tab-stats"
          className="space-y-6"
        >
          {statsError ? (
            <ErrorState
              title="تعذر تحميل إحصائيات الشركات"
              message={statsError}
              onRetry={fetchStats}
            />
          ) : statsLoading && !stats ? (
            <div className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <Skeleton className="h-28 rounded-xl" rows={1} />
                <Skeleton className="h-28 rounded-xl" rows={1} />
                <Skeleton className="h-28 rounded-xl" rows={1} />
                <Skeleton className="h-28 rounded-xl" rows={1} />
              </div>
              <Skeleton className="h-48 rounded-2xl" rows={2} />
            </div>
          ) : !stats || stats.items.length === 0 ? (
            <EmptyState
              icon={<BarChart3 size={28} />}
              title="لا توجد شركات ضمن نطاقك لعرض إحصائياتها"
              description="تحقق من صلاحيات النطاق المالي للشركات المسندة لحسابك."
            />
          ) : (
            <>
              {/* Live KPI Summary Cards (recomputed from filteredStatsItems per H3) */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <StatCard
                  label={isStatsFiltered ? 'الشركات المطابقة للبحث' : 'الشركات ضمن النطاق'}
                  value={formatNumber(filteredStatsTotals.companiesCount)}
                  hint={isStatsFiltered ? 'كل الشركات المفلترة' : 'كل الشركات ضمن صلاحية المستخدم'}
                  icon={Building2}
                />
                <StatCard
                  label="إجمالي المكفولين"
                  value={formatNumber(filteredStatsTotals.sponsoredTotal)}
                  hint={`مناديب: ${formatNumber(filteredStatsTotals.sponsoredRiders)} · إداريون: ${formatNumber(filteredStatsTotals.sponsoredAdmins)}`}
                  icon={Users}
                />
                <StatCard
                  label="المكفولون النشطون"
                  value={formatNumber(filteredStatsTotals.sponsoredActive)}
                  hint={`في إجازة: ${formatNumber(filteredStatsTotals.sponsoredOnLeave)}`}
                  icon={UserCheck}
                />
                <StatCard
                  label="حسابات المنصات المسجلة"
                  value={formatNumber(filteredStatsTotals.platformAccountsTotal)}
                  hint={`نشط: ${formatNumber(filteredStatsTotals.platformAccountsActive)} · مسند: ${formatNumber(filteredStatsTotals.platformAccountsAssigned)}`}
                  icon={Layers}
                />
              </div>

              {/* Filter Cards Per Company/Record -> /workforce/?company=... */}
              <section aria-label="بطاقات إحصائيات الشركات والسجلات" className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <h2 className="text-sm font-bold text-[var(--ink)]">
                      توزيع الكفالات وحسابات المنصات حسب الشركة والسجل
                    </h2>
                    <p className="text-xs text-[var(--muted)]">
                      اختر أي بطاقة شركة أو منصة للانتقال مباشرة إلى صفحة القوى العاملة مفلترة عليها.
                    </p>
                  </div>
                </div>

                {filteredStatsItems.length === 0 ? (
                  <EmptyState
                    icon={<Building2 size={28} />}
                    title="لا توجد شركات مطابقة للبحث"
                    description="جرّب مسح نص البحث لعرض جميع الشركات ضمن نطاقك."
                  />
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                    {filteredStatsItems.map((item) => {
                      const companyKey = item.unifiedNumber || item.companyId;
                      const workforceHref = inApp(buildWorkforceCompanyUrl(companyKey));
                      return (
                        <article
                          key={item.companyId}
                          data-testid={`company-stats-card-${item.code || item.companyId}`}
                          className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 shadow-xs hover:border-[var(--accent)]/60 transition flex flex-col justify-between gap-4"
                        >
                          <div className="space-y-4">
                            {/* Card Header */}
                            <div className="flex items-start justify-between gap-3">
                              <div className="min-w-0 space-y-1">
                                <div className="flex items-center gap-2 flex-wrap">
                                  <h3 className="text-sm font-bold text-[var(--ink)] leading-snug">
                                    {item.nameAr}
                                  </h3>
                                  {item.shortNameAr && item.shortNameAr !== item.nameAr && (
                                    <span className="text-[11px] font-semibold px-2 py-0.5 rounded-full bg-[var(--raised)] text-[var(--muted)]">
                                      {item.shortNameAr}
                                    </span>
                                  )}
                                </div>
                                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-[var(--muted)]">
                                  <span>
                                    الرقم الموحد:{' '}
                                    <bdi dir="ltr" className="font-mono num font-semibold text-[var(--ink)]">
                                      {item.unifiedNumber || '—'}
                                    </bdi>
                                  </span>
                                  <span>
                                    السجل:{' '}
                                    <bdi dir="ltr" className="font-mono num font-semibold text-[var(--ink)]">
                                      {item.commercialReg || '—'}
                                    </bdi>
                                  </span>
                                </div>
                              </div>
                              <span
                                className={`shrink-0 inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold ${
                                  item.erpLinked
                                    ? 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                    : stats.source === 'store'
                                    ? 'bg-[var(--raised)] text-[var(--muted)]'
                                    : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                }`}
                              >
                                <CheckCircle2 size={11} aria-hidden="true" />
                                <span>
                                  {item.erpLinked
                                    ? 'مرتبط بنظام الموارد'
                                    : stats.source === 'store'
                                    ? 'بيانات محلية'
                                    : 'غير مرتبط'}
                                </span>
                              </span>
                            </div>

                            {/* Sponsorships Counters */}
                            <div className="grid grid-cols-2 gap-2.5">
                              <div className="rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] p-3">
                                <span className="text-[11px] text-[var(--muted)] block">إجمالي المكفولين</span>
                                <strong className="num text-xl font-bold text-[var(--ink)] block mt-0.5">
                                  {formatNumber(item.sponsorships.total)}
                                </strong>
                                <span className="text-[11px] text-[var(--muted)] block mt-1">
                                  مناديب {formatNumber(item.sponsorships.riders)} · إداريون{' '}
                                  {formatNumber(item.sponsorships.admins)}
                                </span>
                              </div>
                              <div className="rounded-xl bg-[var(--raised)]/60 border border-[var(--line)] p-3">
                                <span className="text-[11px] text-[var(--muted)] block">حسابات المنصات</span>
                                <strong className="num text-xl font-bold text-[var(--accent)] block mt-0.5">
                                  {formatNumber(item.platformAccounts.total)}
                                </strong>
                                <span className="text-[11px] text-[var(--muted)] block mt-1">
                                  نشط {formatNumber(item.platformAccounts.active)} · مسند{' '}
                                  {formatNumber(item.platformAccounts.assigned)}
                                </span>
                              </div>
                            </div>

                            {/* Platform Chips */}
                            <div className="space-y-1.5">
                              <span className="text-[11px] font-semibold text-[var(--muted)] block">
                                الحسابات المسجلة باسم الشركة حسب المنصة
                              </span>
                              {item.platformAccounts.byPlatform.length === 0 ? (
                                <p className="text-[11px] text-[var(--muted)]">
                                  لا توجد حسابات منصات مسجلة باسم هذه الشركة حاليًا.
                                </p>
                              ) : (
                                <div className="flex flex-wrap gap-1.5">
                                  {item.platformAccounts.byPlatform.map((plat) => {
                                    const label = platformLabelAr(plat.code, plat.nameAr);
                                    const platHref = inApp(buildWorkforceCompanyUrl(companyKey, plat.code));
                                    if (canFilterWorkforce) {
                                      return (
                                        <Link
                                          key={plat.code}
                                          href={platHref}
                                          className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--raised)]/40 hover:border-[var(--accent)] hover:bg-[var(--row-selected)] text-[11px] font-semibold text-[var(--ink)] transition"
                                          title={`تصفية القوى العاملة: ${item.nameAr} — ${label}`}
                                        >
                                          <span>{label}</span>
                                          <span className="num font-bold text-[var(--accent)]">
                                            {formatNumber(plat.total)}
                                          </span>
                                        </Link>
                                      );
                                    }
                                    return (
                                      <span
                                        key={plat.code}
                                        className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--raised)]/40 text-[11px] font-semibold text-[var(--ink)]"
                                      >
                                        <span>{label}</span>
                                        <span className="num font-bold text-[var(--accent)]">
                                          {formatNumber(plat.total)}
                                        </span>
                                      </span>
                                    );
                                  })}
                                </div>
                              )}
                            </div>
                          </div>

                          {/* Workforce Filter Action */}
                          <div className="pt-3 border-t border-[var(--line)] flex items-center justify-between gap-2">
                            <span className="text-[11px] text-[var(--muted)]">
                              نشطون: <strong className="num text-[var(--ink)]">{formatNumber(item.sponsorships.active)}</strong>
                              {' · '}في إجازة:{' '}
                              <strong className="num text-[var(--ink)]">{formatNumber(item.sponsorships.onLeave)}</strong>
                            </span>
                            {canFilterWorkforce ? (
                              <Link
                                href={workforceHref}
                                className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-95 transition whitespace-nowrap"
                              >
                                <span>عرض في القوى العاملة</span>
                                <ArrowUpLeft size={14} aria-hidden="true" />
                              </Link>
                            ) : (
                              <span className="text-[11px] text-[var(--muted)]">عرض إحصائي فقط</span>
                            )}
                          </div>
                        </article>
                      );
                    })}
                  </div>
                )}
              </section>

              {/* Detailed Table: Sponsorships & Platform Accounts per Company */}
              <section
                aria-label="جدول إحصائيات الكفالات وحسابات المنصات"
                className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden"
              >
                <div className="p-4 bg-[var(--raised)]/40 border-b border-[var(--line)] flex flex-wrap items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <BarChart3 size={16} className="text-[var(--accent)]" aria-hidden="true" />
                    <h3 className="text-xs font-bold text-[var(--ink)]">
                      مقارنة الكفالات وحسابات المنصات لكل شركة وسجل
                    </h3>
                  </div>
                  <span className="text-[11px] text-[var(--muted)]">
                    {isStatsFiltered
                      ? 'نطاق الإجماليات: كل الشركات المفلترة'
                      : 'نطاق الإجماليات: كل الشركات ضمن صلاحية المستخدم'}
                  </span>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs border-collapse">
                    <thead>
                      <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                        <th scope="col" className="py-2.5 px-3 text-start">الشركة / السجل</th>
                        <th scope="col" className="py-2.5 px-2.5 text-start whitespace-nowrap">المكفولون</th>
                        <th scope="col" className="py-2.5 px-2.5 text-start whitespace-nowrap">الحسابات</th>
                        {statsPlatformColumns.map((plat) => (
                          <th key={plat.code} scope="col" className="py-2.5 px-2 text-center whitespace-nowrap">
                            {plat.labelAr}
                          </th>
                        ))}
                        <th scope="col" className="py-2.5 px-2.5 text-center whitespace-nowrap">القوى العاملة</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]/60">
                      {filteredStatsItems.map((item) => {
                        const companyKey = item.unifiedNumber || item.companyId;
                        return (
                          <tr key={item.companyId} className="hover:bg-[var(--row-hover)] transition">
                            <td className="py-2.5 px-3">
                              <div className="font-bold text-[var(--ink)]">{item.nameAr}</div>
                              <div className="text-[11px] text-[var(--muted)]">
                                <bdi dir="ltr" className="font-mono num">
                                  {item.unifiedNumber || '—'}
                                </bdi>
                              </div>
                            </td>
                            <td className="py-2.5 px-2.5 whitespace-nowrap">
                              <div className="font-mono font-bold num text-[var(--ink)]">
                                {formatNumber(item.sponsorships.total)}
                              </div>
                              <div className="text-[10px] text-[var(--muted)]">
                                مناديب {formatNumber(item.sponsorships.riders)} · إداريون{' '}
                                {formatNumber(item.sponsorships.admins)} · نشط{' '}
                                <span className="text-[var(--money-pos)] font-semibold">
                                  {formatNumber(item.sponsorships.active)}
                                </span>
                              </div>
                            </td>
                            <td className="py-2.5 px-2.5 whitespace-nowrap">
                              <div className="font-mono font-bold num text-[var(--accent)]">
                                {formatNumber(item.platformAccounts.total)}
                              </div>
                              <div className="text-[10px] text-[var(--muted)]">
                                نشط{' '}
                                <span className="text-[var(--money-pos)] font-semibold">
                                  {formatNumber(item.platformAccounts.active)}
                                </span>{' '}
                                · مسند {formatNumber(item.platformAccounts.assigned)}
                              </div>
                            </td>
                            {statsPlatformColumns.map((plat) => {
                              const count = platformCountFor(item.platformAccounts.byPlatform, plat.code);
                              return (
                                <td key={plat.code} className="py-2.5 px-2 text-center font-mono num">
                                  {count > 0 && canFilterWorkforce ? (
                                    <Link
                                      href={inApp(buildWorkforceCompanyUrl(companyKey, plat.code))}
                                      className="font-semibold text-[var(--accent)] hover:underline"
                                    >
                                      {formatNumber(count)}
                                    </Link>
                                  ) : (
                                    <span className={count > 0 ? 'font-semibold text-[var(--ink)]' : 'text-[var(--muted)]'}>
                                      {formatNumber(count)}
                                    </span>
                                  )}
                                </td>
                              );
                            })}
                            <td className="py-2.5 px-2.5 text-center whitespace-nowrap">
                              {canFilterWorkforce ? (
                                <Link
                                  href={inApp(buildWorkforceCompanyUrl(companyKey))}
                                  className="h-8 px-2.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:border-[var(--accent)] hover:bg-[var(--row-selected)] text-xs font-semibold inline-flex items-center gap-1 text-[var(--ink)] transition"
                                >
                                  <span>تصفية</span>
                                  <ArrowUpLeft size={13} aria-hidden="true" />
                                </Link>
                              ) : (
                                <span className="text-[var(--muted)]">—</span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                    <tfoot>
                      <tr className="border-t-2 border-[var(--line)] bg-[var(--raised)]/60 font-bold text-[var(--ink)]">
                        <td className="py-2.5 px-3 whitespace-nowrap">
                          {isStatsFiltered ? 'إجمالي الشركات المفلترة' : 'الإجمالي العام'}
                        </td>
                        <td className="py-2.5 px-2.5 whitespace-nowrap">
                          <div className="font-mono num">{formatNumber(filteredStatsTotals.sponsoredTotal)}</div>
                          <div className="text-[10px] font-normal text-[var(--muted)]">
                            مناديب {formatNumber(filteredStatsTotals.sponsoredRiders)} · إداريون{' '}
                            {formatNumber(filteredStatsTotals.sponsoredAdmins)} · نشط{' '}
                            {formatNumber(filteredStatsTotals.sponsoredActive)}
                          </div>
                        </td>
                        <td className="py-2.5 px-2.5 whitespace-nowrap">
                          <div className="font-mono num text-[var(--accent)]">
                            {formatNumber(filteredStatsTotals.platformAccountsTotal)}
                          </div>
                          <div className="text-[10px] font-normal text-[var(--muted)]">
                            نشط {formatNumber(filteredStatsTotals.platformAccountsActive)} · مسند{' '}
                            {formatNumber(filteredStatsTotals.platformAccountsAssigned)}
                          </div>
                        </td>
                        {statsPlatformColumns.map((plat) => (
                          <td key={plat.code} className="py-2.5 px-2 text-center font-mono num">
                            {formatNumber(platformCountFor(filteredStatsTotals.byPlatform, plat.code))}
                          </td>
                        ))}
                        <td className="py-2.5 px-2.5 text-center">—</td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </section>

              {/* Company Financial Balances Placeholder (deferred until accounting tables; gated by companies.balances.view per U-W3-03) */}
              <section
                aria-label="الأرصدة المالية للشركات"
                data-testid="company-balances-placeholder"
                className="rounded-2xl border border-dashed border-[var(--line)] bg-[var(--raised)]/35 p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4"
              >
                <div className="flex items-start gap-3">
                  <div className="h-10 w-10 rounded-xl bg-[var(--surface)] border border-[var(--line)] flex items-center justify-center text-[var(--accent)] shrink-0">
                    {canViewBalances ? (
                      <Wallet size={20} aria-hidden="true" />
                    ) : (
                      <Lock size={20} aria-hidden="true" />
                    )}
                  </div>
                  <div className="space-y-1">
                    <h3 className="text-sm font-bold text-[var(--ink)]">الأرصدة المالية لكل شركة</h3>
                    {canViewBalances ? (
                      <>
                        <p className="text-xs font-semibold text-[var(--accent)]">
                          {stats.balancesNote || BALANCES_PLACEHOLDER_AR}
                        </p>
                        <p className="text-xs text-[var(--muted)]">
                          ستظهر هنا أرصدة البنوك والخزن والذمم والمديونيات وأصول المركبات لكل شركة فور ربط جداول المحاسبة.
                        </p>
                      </>
                    ) : (
                      <p className="text-xs font-semibold text-[var(--warning)]">
                        عرض الأرصدة المالية يتطلب صلاحية أرصدة الشركات
                      </p>
                    )}
                  </div>
                </div>
                <span className="shrink-0 inline-flex items-center px-3 py-1 rounded-full bg-[var(--surface)] border border-[var(--line)] text-xs font-bold text-[var(--muted)]">
                  {canViewBalances ? BALANCES_PLACEHOLDER_AR : 'يتطلب صلاحية'}
                </span>
              </section>
            </>
          )}
        </div>
      ) : error ? (
        <ErrorState
          title="تعذر تحميل بيانات الشركات"
          message={error}
          onRetry={fetchData}
        />
      ) : loading ? (
        <div className="space-y-3">
          <Skeleton className="h-12 rounded-xl" rows={1} />
          <Skeleton className="h-48 rounded-xl" rows={3} />
        </div>
      ) : activeTab === 'companies' ? (
        /* Sub-tab 1: الشركات والكفلاء */
        <div
          id="tabpanel-companies"
          role="tabpanel"
          aria-labelledby="tab-companies"
          className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden"
        >
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th scope="col" className="py-3 px-4 text-start">الرمز</th>
                  <th scope="col" className="py-3 px-4 text-start">المنشأة الرسمية</th>
                  <th scope="col" className="py-3 px-4 text-start">الرقم الموحد</th>
                  <th scope="col" className="py-3 px-4 text-start">السجل التجاري</th>
                  <th scope="col" className="py-3 px-4 text-start">نوع الكيان</th>
                  <th scope="col" className="py-3 px-4 text-start">المدينة</th>
                  <th scope="col" className="py-3 px-4 text-start">رأس المال</th>
                  <th scope="col" className="py-3 px-4 text-start">المكفولون</th>
                  <th scope="col" className="py-3 px-4 text-start">التأكيد السنوي القادم</th>
                  <th scope="col" className="py-3 px-4 text-start">الحالة</th>
                  <th scope="col" className="py-3 px-4 text-center">إجراءات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]/60">
                {filteredCompanies.length === 0 ? (
                  <tr>
                    <td colSpan={11} className="py-12 text-center text-[var(--muted)]">
                      لا توجد منشآت مطابقة للبحث الحالي.
                    </td>
                  </tr>
                ) : (
                  filteredCompanies.map((c) => {
                    const daysRemaining = getDaysRemaining(c.annualConfirmation);
                    const isExpiring = daysRemaining !== null && daysRemaining <= 60;
                    const liveItem = statsByCompanyId.get(c.id);
                    const sponsoredCount = liveItem?.sponsorships.total;

                    return (
                      <tr
                        key={c.id}
                        className="hover:bg-[var(--row-hover)] transition cursor-pointer"
                        onClick={() => handleOpenDrawer(c)}
                      >
                        <td className="py-3 px-4 font-mono font-bold text-[var(--accent)]">
                          <bdi dir="ltr">{c.code}</bdi>
                        </td>
                        <td className="py-3 px-4">
                          <div className="font-bold text-[var(--ink)]">{c.nameAr}</div>
                          {c.shortNameAr && c.shortNameAr !== c.nameAr && (
                            <div className="text-[11px] text-[var(--muted)]">{c.shortNameAr}</div>
                          )}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          <bdi dir="ltr">{c.unifiedNumber || '—'}</bdi>
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          <bdi dir="ltr">{c.crNumber || c.commercialReg || '—'}</bdi>
                        </td>
                        <td className="py-3 px-4 text-[var(--muted)]">{c.entityType || '—'}</td>
                        <td className="py-3 px-4 text-[var(--ink)]">{c.city || '—'}</td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {c.capitalSar
                            ? showBalances
                              ? formatSar(c.capitalSar)
                              : '••••••••'
                            : '—'}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {sponsoredCount !== undefined ? (
                            canFilterWorkforce ? (
                              <Link
                                href={inApp(buildWorkforceCompanyUrl(c.unifiedNumber || c.id))}
                                onClick={(e) => e.stopPropagation()}
                                className="inline-flex items-center gap-1 font-bold text-[var(--accent)] hover:underline"
                              >
                                <Users size={12} aria-hidden="true" />
                                <span>{formatNumber(sponsoredCount)}</span>
                              </Link>
                            ) : (
                              <span className="inline-flex items-center gap-1 font-bold text-[var(--ink)]">
                                <Users size={12} aria-hidden="true" />
                                <span>{formatNumber(sponsoredCount)}</span>
                              </span>
                            )
                          ) : (
                            <span className="text-[var(--muted)]">—</span>
                          )}
                        </td>
                        <td className="py-3 px-4">
                          {c.annualConfirmation ? (
                            <div className="space-y-1">
                              <div className="font-mono text-xs num">
                                {c.annualConfirmationHijri ? `${c.annualConfirmationHijri} هـ` : ''}{' '}
                                <span className="text-[var(--muted)]">({c.annualConfirmation})</span>
                              </div>
                              {isExpiring && (
                                <span
                                  className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold ${
                                    daysRemaining !== null && daysRemaining <= 0
                                      ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                      : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                  }`}
                                >
                                  <AlertTriangle size={11} aria-hidden="true" />
                                  <span>
                                    {daysRemaining !== null && daysRemaining <= 0
                                      ? 'منتهي'
                                      : `يستحق خلال ${daysRemaining} يوم`}
                                  </span>
                                </span>
                              )}
                            </div>
                          ) : (
                            <span className="text-[var(--muted)]">—</span>
                          )}
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold ${
                              c.status === 'active'
                                ? 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                : 'bg-[var(--raised)] text-[var(--muted)] border border-[var(--line)]'
                            }`}
                          >
                            <span className="h-1.5 w-1.5 rounded-full bg-current" />
                            {c.status === 'active' ? 'نشط' : 'غير نشط'}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-center">
                          <div className="inline-flex items-center gap-1">
                            <button
                              type="button"
                              onClick={(e) => {
                                e.stopPropagation();
                                handleOpenDrawer(c);
                              }}
                              className="h-8 px-2.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold inline-flex items-center gap-1 transition"
                            >
                              <Eye size={13} aria-hidden="true" />
                              <span>التفاصيل</span>
                            </button>
                            {canManage && (
                              <>
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    openEditCompanyModal(c);
                                  }}
                                  className="h-8 px-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold inline-flex items-center gap-1 transition"
                                  title="تعديل الشركة"
                                  aria-label={`تعديل ${c.nameAr}`}
                                >
                                  <Pencil size={13} aria-hidden="true" />
                                </button>
                                <button
                                  type="button"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    handleArchiveCompany(c);
                                  }}
                                  className="h-8 px-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--danger)]/10 hover:text-[var(--danger)] text-xs font-semibold inline-flex items-center gap-1 transition"
                                  title="أرشفة الشركة"
                                  aria-label={`أرشفة ${c.nameAr}`}
                                >
                                  <Trash2 size={13} aria-hidden="true" />
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      ) : activeTab === 'cr' ? (
        /* Sub-tab 2: السجلات التجارية */
        <div
          id="tabpanel-cr"
          role="tabpanel"
          aria-labelledby="tab-cr"
          className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden"
        >
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th scope="col" className="py-3 px-4 text-start">رقم السجل التجاري</th>
                  <th scope="col" className="py-3 px-4 text-start">المنشأة التابعة</th>
                  <th scope="col" className="py-3 px-4 text-start">نوع السجل</th>
                  <th scope="col" className="py-3 px-4 text-start">النسخة</th>
                  <th scope="col" className="py-3 px-4 text-start">المدينة</th>
                  <th scope="col" className="py-3 px-4 text-start">تاريخ القيد</th>
                  <th scope="col" className="py-3 px-4 text-start">التأكيد السنوي القادم</th>
                  <th scope="col" className="py-3 px-4 text-start">الحالة</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]/60">
                {filteredRegistrations.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="py-12 text-center text-[var(--muted)]">
                      لا توجد سجلات تجارية مطابقة للبحث الحالي.
                    </td>
                  </tr>
                ) : (
                  filteredRegistrations.map((cr) => {
                    const rem = getDaysRemaining(cr.annualConfirmation);
                    const isExpired = rem !== null && rem <= 0;
                    const isExpiring = rem !== null && rem > 0 && rem <= 60;

                    return (
                      <tr key={cr.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="py-3 px-4 font-mono font-bold text-xs num">
                          <bdi dir="ltr">{cr.number}</bdi>
                        </td>
                        <td className="py-3 px-4 font-bold text-[var(--ink)]">{cr.companyNameAr}</td>
                        <td className="py-3 px-4 text-[var(--muted)]">{cr.crType || '—'}</td>
                        <td className="py-3 px-4 font-mono text-xs num">{cr.crVersion ?? '—'}</td>
                        <td className="py-3 px-4 text-[var(--ink)]">{cr.city || '—'}</td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {cr.issueDateHijri ? `${cr.issueDateHijri} هـ ` : ''}
                          {cr.issueDate ? `(${cr.issueDate})` : '—'}
                        </td>
                        <td className="py-3 px-4">
                          <div className="space-y-1">
                            <div className="font-mono text-xs num">
                              {cr.annualConfirmationHijri ? `${cr.annualConfirmationHijri} هـ ` : ''}
                              <span className="text-[var(--muted)]">({cr.annualConfirmation || '—'})</span>
                            </div>
                            {isExpired ? (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-[var(--danger)]/15 text-[var(--danger)]">
                                <AlertTriangle size={11} aria-hidden="true" />
                                <span>{rem === 0 ? 'ينتهي اليوم' : `منتهي منذ ${Math.abs(rem)} يوم`}</span>
                              </span>
                            ) : isExpiring ? (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
                                <AlertTriangle size={11} aria-hidden="true" />
                                <span>مستحق خلال {rem} يوم</span>
                              </span>
                            ) : null}
                          </div>
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold ${
                              cr.status === 'active'
                                ? 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                : cr.status === 'expired'
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : 'bg-[var(--raised)] text-[var(--muted)] border border-[var(--line)]'
                            }`}
                          >
                            {cr.status === 'active' ? 'نشط' : cr.status === 'expired' ? 'منتهي' : 'غير نشط'}
                          </span>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      ) : (
        /* Sub-tab 3: الوثائق والانتهاءات */
        <div
          id="tabpanel-expiring"
          role="tabpanel"
          aria-labelledby="tab-expiring"
          className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden"
        >
          <div className="p-4 bg-[var(--raised)]/40 border-b border-[var(--line)] flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Clock size={16} className="text-[var(--accent)]" aria-hidden="true" />
              <h3 className="text-xs font-bold text-[var(--ink)]">
                السجلات والوثائق مستحقة التجديد والتأكيد
              </h3>
            </div>
            <span className="text-xs text-[var(--muted)]">نافذة التنبيه: 60 يومًا</span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th scope="col" className="py-3 px-4 text-start">النوع</th>
                  <th scope="col" className="py-3 px-4 text-start">المنشأة</th>
                  <th scope="col" className="py-3 px-4 text-start">رقم المستند / السجل</th>
                  <th scope="col" className="py-3 px-4 text-start">تاريخ الاستحقاق / الانتهاء</th>
                  <th scope="col" className="py-3 px-4 text-start">المدة المتبقية</th>
                  <th scope="col" className="py-3 px-4 text-start">الموقف</th>
                  <th scope="col" className="py-3 px-4 text-center">المرفق / إجراء</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]/60">
                {filteredExpiring.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="py-12 text-center text-[var(--muted)]">
                      لا توجد سجلات أو وثائق منتهية أو مستحقة خلال نافذة التنبيه الحالية.
                    </td>
                  </tr>
                ) : (
                  filteredExpiring.map((item) => {
                    const comp = companiesById.get(item.companyId);
                    const dueDate = item.annualConfirmation || item.expiryDate;
                    const rem = getDaysRemaining(dueDate);
                    const isExpired = rem !== null && rem <= 0;
                    const isClose = rem !== null && rem > 0 && rem <= 60;
                    const isDocument = item.kind === 'company_document';

                    return (
                      <tr key={`${item.kind}-${item.id}`} className="hover:bg-[var(--row-hover)] transition">
                        <td className="py-3 px-4 font-bold text-[var(--accent)]">
                          {isDocument
                            ? item.type || 'وثيقة شركة'
                            : 'التأكيد السنوي للسجل التجاري'}
                        </td>
                        <td className="py-3 px-4 font-bold text-[var(--ink)]">
                          {comp?.nameAr || item.companyId}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          <bdi dir="ltr">{item.number || '—'}</bdi>
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {dueDate || '—'}
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`font-mono font-bold text-xs ${
                              isExpired
                                ? 'text-[var(--danger)]'
                                : rem !== null && rem <= 30
                                ? 'text-[var(--danger)]'
                                : isClose
                                ? 'text-[var(--warning)]'
                                : 'text-[var(--muted)]'
                            }`}
                          >
                            {rem === null
                              ? '—'
                              : rem < 0
                              ? `منتهي منذ ${Math.abs(rem)} يوم`
                              : rem === 0
                              ? 'ينتهي اليوم'
                              : `${rem} يوم`}
                          </span>
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold ${
                              isExpired
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : isClose
                                ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                            }`}
                          >
                            {isExpired ? 'منتهي' : isClose ? 'يستحق المتابعة' : 'ساري'}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-center">
                          <div className="inline-flex items-center gap-1.5">
                            {isDocument && (
                              <a
                                href={`/api/companies/${encodeURIComponent(item.companyId)}/documents/${encodeURIComponent(item.id)}/download`}
                                target="_blank"
                                rel="noreferrer"
                                className="h-7 px-2 rounded bg-[var(--raised)] hover:bg-[var(--line)] text-[11px] font-semibold inline-flex items-center gap-1 transition"
                              >
                                <Download size={12} aria-hidden="true" />
                                <span>تنزيل</span>
                              </a>
                            )}
                            {comp && (
                              <button
                                type="button"
                                onClick={() => handleOpenDrawer(comp)}
                                className="h-7 px-2 rounded border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-[11px] font-semibold inline-flex items-center gap-1 transition"
                              >
                                <Eye size={12} aria-hidden="true" />
                                <span>تفاصيل</span>
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Drawer: تفاصيل المنشأة والأنشطة والوثائق والإحصائيات */}
      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title={selectedCompany ? selectedCompany.nameAr : 'تفاصيل المنشأة'}
        width="wide"
      >
        {selectedCompany && (
          <div className="space-y-6">
            {/* Drawer Sub-tabs */}
            <div className="flex border-b border-[var(--line)] gap-2 overflow-x-auto">
              <button
                type="button"
                onClick={() => setDrawerTab('details')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition whitespace-nowrap ${
                  drawerTab === 'details'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                البيانات الأساسية
              </button>
              <button
                type="button"
                onClick={() => setDrawerTab('registrations')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition flex items-center gap-1.5 whitespace-nowrap ${
                  drawerTab === 'registrations'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <span>السجلات</span>
                <span className="text-[10px] font-bold px-1.5 py-0.2 rounded bg-[var(--raised)] text-[var(--muted)]">
                  {companyRegs.length}
                </span>
              </button>
              <button
                type="button"
                onClick={() => setDrawerTab('activities')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition flex items-center gap-1.5 whitespace-nowrap ${
                  drawerTab === 'activities'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <span>الأنشطة المعتمدة</span>
                {selectedCompany.activitiesIncomplete && (
                  <span className="text-[10px] font-bold px-1.5 py-0.2 rounded bg-[var(--warning)]/15 text-[var(--warning)]">
                    ناقص
                  </span>
                )}
              </button>
              <button
                type="button"
                onClick={() => setDrawerTab('documents')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition flex items-center gap-1.5 whitespace-nowrap ${
                  drawerTab === 'documents'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <span>الوثائق والمستندات</span>
                <span className="text-[10px] font-bold px-1.5 py-0.2 rounded bg-[var(--raised)] text-[var(--muted)]">
                  {companyDocs.length}
                </span>
              </button>
              <button
                type="button"
                onClick={() => setDrawerTab('stats')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition whitespace-nowrap ${
                  drawerTab === 'stats'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                الإحصائيات
              </button>
              {isAdmin && (
                <button
                  type="button"
                  onClick={() => setDrawerTab('audit')}
                  className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition whitespace-nowrap ${
                    drawerTab === 'audit'
                      ? 'border-[var(--accent)] text-[var(--accent)]'
                      : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  سجل العمليات
                </button>
              )}
            </div>

            {/* Drawer Tab 1: البيانات الرسمية */}
            {drawerTab === 'details' && (
              <div className="space-y-4 text-xs">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-4 rounded-xl bg-[var(--raised)]/40 border border-[var(--line)]">
                  <div>
                    <span className="text-[var(--muted)] block mb-1">الرقم الوطني الموحد</span>
                    <strong className="font-mono text-sm num text-[var(--ink)]">
                      <bdi dir="ltr">{selectedCompany.unifiedNumber || '—'}</bdi>
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">رقم السجل التجاري</span>
                    <strong className="font-mono text-sm num text-[var(--ink)]">
                      <bdi dir="ltr">{selectedCompany.crNumber || selectedCompany.commercialReg || '—'}</bdi>
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">نوع الكيان القانوني</span>
                    <strong className="text-[var(--ink)]">{selectedCompany.entityType || '—'}</strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">صفة الشركة</span>
                    <strong className="text-[var(--ink)]">{selectedCompany.characteristic || '—'}</strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">المدينة والمقر</span>
                    <strong className="text-[var(--ink)]">{selectedCompany.city || '—'}</strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">رأس المال المثبت</span>
                    <strong className="font-mono text-sm num text-[var(--money-pos)]">
                      {selectedCompany.capitalSar
                        ? showBalances
                          ? formatSar(selectedCompany.capitalSar)
                          : '••••••••'
                        : '—'}
                    </strong>
                  </div>
                </div>

                <div className="p-4 rounded-xl bg-[var(--raised)]/40 border border-[var(--line)] space-y-3">
                  <h4 className="font-bold text-[var(--ink)]">تواريخ القيد والتأكيد</h4>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <div>
                      <span className="text-[var(--muted)] block mb-1">تاريخ القيد الأولي</span>
                      <div className="font-mono text-xs num">
                        {selectedCompany.issueDateHijri ? `${selectedCompany.issueDateHijri} هـ` : ''}{' '}
                        ({selectedCompany.issueDate || '—'})
                      </div>
                    </div>
                    <div>
                      <span className="text-[var(--muted)] block mb-1">التأكيد السنوي القادم</span>
                      <div className="font-mono text-xs num">
                        {selectedCompany.annualConfirmationHijri
                          ? `${selectedCompany.annualConfirmationHijri} هـ`
                          : ''}{' '}
                        ({selectedCompany.annualConfirmation || '—'})
                      </div>
                    </div>
                  </div>
                </div>

                {selectedCompany.address && (
                  <div className="p-3.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)]">
                    <span className="font-bold text-[var(--ink)] block mb-1">العنوان الوطني المعتمد:</span>
                    <span>{selectedCompany.address}</span>
                  </div>
                )}
              </div>
            )}

            {/* Drawer Sub-tab: السجلات التجارية */}
            {drawerTab === 'registrations' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold text-[var(--ink)]">السجلات التجارية التابعة للمنشأة</h4>
                  {canManage && (
                    <button
                      type="button"
                      onClick={() => openCreateCrModal(selectedCompany.id)}
                      className="h-8 px-3 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold inline-flex items-center gap-1.5 shadow-2xs hover:opacity-95 transition"
                    >
                      <Plus size={14} aria-hidden="true" />
                      <span>إضافة سجل تجاري</span>
                    </button>
                  )}
                </div>

                {loadingDocs ? (
                  <Skeleton className="h-24 rounded-xl" rows={2} />
                ) : companyRegs.length === 0 ? (
                  <div className="p-8 text-center text-xs text-[var(--muted)] border border-dashed border-[var(--line)] rounded-xl">
                    لا توجد سجلات تجارية فرعية مضافة بعد؛ الرقم المعتمد على المنشأة:{' '}
                    <bdi dir="ltr" className="font-mono font-bold text-[var(--ink)]">
                      {selectedCompany.crNumber || selectedCompany.commercialReg || '—'}
                    </bdi>
                  </div>
                ) : (
                  <div className="divide-y divide-[var(--line)] border border-[var(--line)] rounded-xl bg-[var(--surface)] overflow-hidden">
                    {companyRegs.map((reg) => (
                      <div key={reg.id} className="p-3 text-xs flex items-center justify-between gap-2">
                        <div>
                          <strong className="font-mono num text-[var(--ink)] block">
                            <bdi dir="ltr">{reg.number}</bdi>
                          </strong>
                          <span className="text-[11px] text-[var(--muted)]">
                            {reg.city || '—'} · الانتهاء: {reg.annualConfirmation || reg.expiryDate || '—'}
                          </span>
                        </div>
                        <div className="flex items-center gap-1.5">
                          <span
                            className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold ${
                              reg.status === 'active'
                                ? 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                                : reg.status === 'expired'
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : 'bg-[var(--raised)] text-[var(--muted)]'
                            }`}
                          >
                            {reg.status === 'active' ? 'نشط' : reg.status === 'expired' ? 'منتهي' : 'غير نشط'}
                          </span>
                          {canManage && (
                            <>
                              <button
                                type="button"
                                onClick={() => openEditCrModal(reg)}
                                className="h-7 px-2 rounded bg-[var(--raised)] hover:bg-[var(--line)] text-[11px] font-semibold inline-flex items-center gap-1 transition"
                                title="تعديل السجل"
                              >
                                <Pencil size={12} aria-hidden="true" />
                              </button>
                              <button
                                type="button"
                                onClick={() => handleArchiveCr(reg.companyId, reg.id, reg.number)}
                                className="h-7 px-2 rounded bg-[var(--raised)] hover:bg-[var(--danger)]/15 hover:text-[var(--danger)] text-[11px] font-semibold inline-flex items-center gap-1 transition"
                                title="أرشفة السجل"
                              >
                                <Trash2 size={12} aria-hidden="true" />
                              </button>
                            </>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Drawer Tab 2: الأنشطة المعتمدة */}
            {drawerTab === 'activities' && (
              <div className="space-y-4">
                {selectedCompany.activitiesIncomplete && (
                  <div className="p-3 rounded-xl bg-[var(--warning)]/15 border border-[var(--warning)]/30 text-xs text-[var(--warning)] flex items-start gap-2">
                    <AlertTriangle size={15} className="shrink-0 mt-0.5" aria-hidden="true" />
                    <p>
                      <strong>أنشطة ناقصة:</strong> توجد أنشطة إضافية في شهادة السجل لم تُسجّل بعد في النظام بانتظار استكمال بياناتها.
                    </p>
                  </div>
                )}

                <div className="space-y-2">
                  <h4 className="text-xs font-bold text-[var(--ink)]">قائمة أنشطة المنشأة المعتمدة:</h4>
                  {selectedCompany.activities && selectedCompany.activities.length > 0 ? (
                    <div className="flex flex-wrap gap-2">
                      {selectedCompany.activities.map((act) => (
                        <div
                          key={act.isic}
                          className="p-2.5 rounded-xl border border-[var(--line)] bg-[var(--raised)]/50 text-xs flex items-center gap-2"
                        >
                          <span className="font-mono font-bold text-[var(--accent)] bg-[var(--surface)] px-2 py-0.5 rounded border border-[var(--line)]">
                            <bdi dir="ltr">{act.isic}</bdi>
                          </span>
                          <span className="text-[var(--ink)] font-semibold">{act.nameAr}</span>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="text-xs text-[var(--muted)]">لا توجد أنشطة مسجلة لهذه المنشأة بعد.</p>
                  )}
                </div>
              </div>
            )}

            {/* Drawer Tab 3: الوثائق والمستندات (تبقى مرفوعاتنا الخاصة) */}
            {drawerTab === 'documents' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold text-[var(--ink)]">مستندات وتراخيص المنشأة</h4>
                  {canManage && (
                    <button
                      type="button"
                      onClick={() => setUploadDocModalOpen(true)}
                      className="h-8 px-3 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold inline-flex items-center gap-1.5 shadow-2xs hover:opacity-95 transition"
                    >
                      <Plus size={14} aria-hidden="true" />
                      <span>إضافة وثيقة</span>
                    </button>
                  )}
                </div>

                {loadingDocs ? (
                  <Skeleton className="h-24 rounded-xl" rows={2} />
                ) : companyDocs.length === 0 ? (
                  <div className="p-8 text-center text-xs text-[var(--muted)] border border-dashed border-[var(--line)] rounded-xl">
                    لا توجد وثائق مرفوعة لهذه الشركة بعد.
                  </div>
                ) : (
                  <div className="divide-y divide-[var(--line)] border border-[var(--line)] rounded-xl bg-[var(--surface)] overflow-hidden">
                    {companyDocs.map((doc) => (
                      <div key={doc.id} className="p-3 text-xs flex items-center justify-between gap-2">
                        <div className="flex items-center gap-2.5">
                          <FileText size={16} className="text-[var(--accent)] shrink-0" aria-hidden="true" />
                          <div>
                            <span className="font-bold text-[var(--ink)] block">{doc.type}</span>
                            <span className="text-[11px] text-[var(--muted)]">
                              {doc.fileName || doc.number || 'ملف مرفق'}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          {doc.expiryDate && (
                            <span className="font-mono text-[11px] num text-[var(--muted)]">
                              الانتهاء: {doc.expiryDate}
                            </span>
                          )}
                          {doc.file && (
                            <a
                              href={doc.file}
                              target="_blank"
                              rel="noreferrer"
                              className="h-7 px-2 rounded bg-[var(--raised)] hover:bg-[var(--line)] text-[11px] font-semibold inline-flex items-center gap-1 transition"
                            >
                              <Download size={12} aria-hidden="true" />
                              <span>تنزيل</span>
                            </a>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Drawer Tab 4: الإحصائيات */}
            {drawerTab === 'stats' && (
              <div className="space-y-4 text-xs">
                {statsError ? (
                  <ErrorState title="تعذر تحميل الإحصائيات" message={statsError} onRetry={fetchStats} />
                ) : !selectedCompanyStats ? (
                  <div className="p-6 text-center text-[var(--muted)] rounded-xl border border-dashed border-[var(--line)]">
                    جاري تجهيز الإحصائيات أو لا توجد بيانات مرتبطة بعد.
                  </div>
                ) : (
                  <>
                    <div className="grid grid-cols-2 gap-3">
                      <div className="p-3.5 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">إجمالي المكفولين</span>
                        <strong className="num text-xl font-bold text-[var(--ink)]">
                          {formatNumber(selectedCompanyStats.sponsorships.total)}
                        </strong>
                        <span className="text-[11px] text-[var(--muted)] block mt-1">
                          مناديب {formatNumber(selectedCompanyStats.sponsorships.riders)} · إداريون{' '}
                          {formatNumber(selectedCompanyStats.sponsorships.admins)}
                        </span>
                      </div>
                      <div className="p-3.5 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)]">
                        <span className="text-[var(--muted)] block">حسابات المنصات المسجلة</span>
                        <strong className="num text-xl font-bold text-[var(--accent)]">
                          {formatNumber(selectedCompanyStats.platformAccounts.total)}
                        </strong>
                        <span className="text-[11px] text-[var(--muted)] block mt-1">
                          نشط {formatNumber(selectedCompanyStats.platformAccounts.active)} · مسند{' '}
                          {formatNumber(selectedCompanyStats.platformAccounts.assigned)}
                        </span>
                      </div>
                    </div>

                    {selectedCompanyStats.platformAccounts.byPlatform.length > 0 && (
                      <div className="space-y-2">
                        <h4 className="font-bold text-[var(--ink)]">توزيع الحسابات حسب المنصة</h4>
                        <div className="flex flex-wrap gap-2">
                          {selectedCompanyStats.platformAccounts.byPlatform.map((plat) => (
                            <span
                              key={plat.code}
                              className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] font-semibold"
                            >
                              <span>{platformLabelAr(plat.code, plat.nameAr)}</span>
                              <strong className="num text-[var(--accent)]">{formatNumber(plat.total)}</strong>
                            </span>
                          ))}
                        </div>
                      </div>
                    )}

                    <div className="p-3.5 rounded-xl border border-dashed border-[var(--line)] bg-[var(--raised)]/30 text-[var(--muted)]">
                      <strong className="text-[var(--accent)] block">
                        {stats?.balancesNote || BALANCES_PLACEHOLDER_AR}
                      </strong>
                    </div>

                    {canFilterWorkforce && (
                      <Link
                        href={inApp(
                          buildWorkforceCompanyUrl(
                            selectedCompanyStats.unifiedNumber || selectedCompanyStats.companyId
                          )
                        )}
                        className="w-full h-10 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold inline-flex items-center justify-center gap-1.5 hover:opacity-95 transition"
                      >
                        <span>فتح القوى العاملة مفلترة بهذه الشركة</span>
                        <ArrowUpLeft size={15} aria-hidden="true" />
                      </Link>
                    )}
                  </>
                )}
              </div>
            )}

            {/* Drawer Tab 5: سجل العمليات (Admin only) */}
            {drawerTab === 'audit' && isAdmin && (
              <div className="space-y-3">
                <h4 className="text-xs font-bold text-[var(--ink)]">العمليات المرتبطة بالشركات</h4>
                <div className="divide-y divide-[var(--line)] border border-[var(--line)] rounded-xl bg-[var(--surface)] max-h-80 overflow-y-auto">
                  {auditLogs.length === 0 ? (
                    <div className="p-6 text-center text-xs text-[var(--muted)]">
                      لا توجد أحداث مراجعة مسجلة حاليًا.
                    </div>
                  ) : (
                    auditLogs.map((log) => (
                      <div key={log.id} className="p-3 text-xs flex items-center justify-between gap-2">
                        <div>
                          <strong className="text-[var(--ink)] block">{auditActionLabelAr(log.action)}</strong>
                          <span className="text-[11px] text-[var(--muted)]">بواسطة: {log.actor}</span>
                        </div>
                        <span className="text-[11px] font-mono text-[var(--muted)] num">
                          {new Date(log.at).toLocaleDateString('ar-SA')}
                        </span>
                      </div>
                    ))
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </Drawer>

      {/* Modal: إضافة / تعديل شركة */}
      <Dialog
        open={companyModalOpen}
        onClose={() => setCompanyModalOpen(false)}
        title={editingCompanyId ? 'تعديل بيانات الشركة' : 'إضافة شركة جديدة'}
      >
        <form onSubmit={handleSaveCompany} className="space-y-3 text-xs">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">اسم الشركة بالعربية</label>
              <input
                type="text"
                required
                value={companyForm.nameAr}
                onChange={(e) => setCompanyForm((p) => ({ ...p, nameAr: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">الرمز المختصر</label>
              <input
                type="text"
                value={companyForm.code}
                onChange={(e) => setCompanyForm((p) => ({ ...p, code: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">الاسم المختصر</label>
              <input
                type="text"
                value={companyForm.shortNameAr}
                onChange={(e) => setCompanyForm((p) => ({ ...p, shortNameAr: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">الرقم الوطني الموحد</label>
              <input
                type="text"
                value={companyForm.unifiedNumber}
                onChange={(e) => setCompanyForm((p) => ({ ...p, unifiedNumber: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-mono"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">رقم السجل التجاري</label>
              <input
                type="text"
                value={companyForm.commercialReg}
                onChange={(e) => setCompanyForm((p) => ({ ...p, commercialReg: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-mono"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">المدينة</label>
              <input
                type="text"
                value={companyForm.city}
                onChange={(e) => setCompanyForm((p) => ({ ...p, city: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
          </div>
          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">الحالة</label>
            <SearchableSelect
              value={companyForm.status}
              onChange={(val) => setCompanyForm((p) => ({ ...p, status: val }))}
              ariaLabel="حالة الشركة"
              options={[
                { value: 'active', label: 'نشط' },
                { value: 'inactive', label: 'غير نشط' },
              ]}
            />
          </div>
          <div className="flex items-center justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={() => setCompanyModalOpen(false)}
              className="h-10 px-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={savingCompany}
              className="h-10 px-5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
            >
              {savingCompany ? 'جاري الحفظ...' : 'حفظ'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Modal: إضافة / تعديل سجل تجاري */}
      <Dialog
        open={crModalOpen}
        onClose={() => setCrModalOpen(false)}
        title={editingCrId ? 'تعديل سجل تجاري' : 'إضافة سجل تجاري'}
      >
        <form onSubmit={handleSaveCr} className="space-y-3 text-xs">
          {!editingCrId && (
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">الشركة التابعة</label>
              <SearchableSelect
                value={crCompanyId}
                onChange={setCrCompanyId}
                ariaLabel="الشركة التابعة"
                options={companies.map((c) => ({ value: c.id, label: c.nameAr }))}
              />
            </div>
          )}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">رقم السجل التجاري</label>
              <input
                type="text"
                required
                value={crForm.number}
                onChange={(e) => setCrForm((p) => ({ ...p, number: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-mono"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">المدينة</label>
              <input
                type="text"
                value={crForm.city}
                onChange={(e) => setCrForm((p) => ({ ...p, city: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">تاريخ الإصدار</label>
              <input
                type="date"
                value={crForm.issueDate}
                onChange={(e) => setCrForm((p) => ({ ...p, issueDate: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
            <div>
              <label className="block font-semibold text-[var(--ink)] mb-1">تاريخ الانتهاء / التأكيد السنوي</label>
              <input
                type="date"
                value={crForm.expiryDate}
                onChange={(e) => setCrForm((p) => ({ ...p, expiryDate: e.target.value }))}
                className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              />
            </div>
          </div>
          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">الحالة</label>
            <SearchableSelect
              value={crForm.status}
              onChange={(val) => setCrForm((p) => ({ ...p, status: val }))}
              ariaLabel="حالة السجل"
              options={[
                { value: 'active', label: 'نشط' },
                { value: 'inactive', label: 'غير نشط' },
                { value: 'expired', label: 'منتهي' },
              ]}
            />
          </div>
          <div className="flex items-center justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={() => setCrModalOpen(false)}
              className="h-10 px-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={savingCr}
              className="h-10 px-5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold disabled:opacity-50"
            >
              {savingCr ? 'جاري الحفظ...' : 'حفظ السجل'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Modal: رفع وثيقة جديدة */}
      <Dialog
        open={uploadDocModalOpen}
        onClose={() => setUploadDocModalOpen(false)}
        title="إضافة وثيقة أو ترخيص منشأة"
      >
        <form onSubmit={handleUploadDocument} className="space-y-4 text-xs">
          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">نوع المستند</label>
            <SearchableSelect
              value={docType}
              onChange={setDocType}
              ariaLabel="نوع المستند"
              options={[
                { value: 'شهادة السجل التجاري', label: 'شهادة السجل التجاري' },
                { value: 'شهادة التأمينات الاجتماعية', label: 'شهادة التأمينات الاجتماعية' },
                { value: 'شهادة الزكاة والضريبة', label: 'شهادة الزكاة والضريبة' },
                { value: 'رخصة البلدية', label: 'رخصة البلدية' },
                { value: 'اشتراك الغرفة التجارية', label: 'اشتراك الغرفة التجارية' },
                { value: 'شهادة العنوان الوطني', label: 'شهادة العنوان الوطني' },
                { value: 'أخرى', label: 'أخرى' },
              ]}
            />
          </div>

          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">رقم المستند أو المرجع</label>
            <input
              type="text"
              value={docNumber}
              onChange={(e) => setDocNumber(e.target.value)}
              placeholder="مثال: 4030362130"
              className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-none focus:ring-2 focus:ring-[var(--focus)]"
            />
          </div>

          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">تاريخ الانتهاء (إن وجد)</label>
            <input
              type="date"
              value={docExpiryDate}
              onChange={(e) => setDocExpiryDate(e.target.value)}
              className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-none focus:ring-2 focus:ring-[var(--focus)]"
            />
          </div>

          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">ملف الوثيقة (مستند أو صورة)</label>
            <input
              type="file"
              required
              accept=".pdf,.png,.jpg,.jpeg"
              onChange={(e) => setDocFile(e.target.files?.[0] || null)}
              className="w-full text-xs text-[var(--muted)] file:me-3 file:py-2 file:px-4 file:rounded-xl file:border-0 file:text-xs file:font-semibold file:bg-[var(--accent)] file:text-[var(--accent-ink)] hover:file:opacity-90 cursor-pointer"
            />
          </div>

          <div className="flex items-center justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={() => setUploadDocModalOpen(false)}
              className="h-10 px-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] font-semibold transition"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={uploadingDoc || !docFile}
              className="h-10 px-5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold shadow-sm hover:opacity-95 disabled:opacity-50 transition"
            >
              {uploadingDoc ? 'جاري الرفع...' : 'حفظ الوثيقة'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* Modal: استيراد بيانات الشركات (Admin only Preview -> Commit) */}
      <Dialog
        open={importModalOpen}
        onClose={() => setImportModalOpen(false)}
        title="استيراد وتحديث بيانات الشركات والسجلات"
      >
        <div className="space-y-4 text-xs">
          <p className="text-[var(--muted)] leading-relaxed">
            استيراد مباشر للشهادات المعتمدة من وزارة التجارة لشركات المجموعة مع فحص البصمة والتطابق قبل الاعتماد.
          </p>

          {importError && (
            <div className="p-3.5 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-[var(--danger)] flex items-start gap-2">
              <AlertCircle size={16} className="shrink-0 mt-0.5" aria-hidden="true" />
              <span>{importError}</span>
            </div>
          )}

          {importLoading ? (
            <div className="p-8 text-center text-[var(--muted)]">
              <span className="inline-block h-5 w-5 border-2 border-current border-t-transparent rounded-full animate-spin mb-2" />
              <p>جاري معالجة خطة الاستيراد وفحص البصمة...</p>
            </div>
          ) : importPreviewPlan ? (
            <div className="space-y-4">
              <div className="p-3.5 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)] space-y-2">
                <div className="flex items-center justify-between">
                  <span className="font-bold text-[var(--ink)]">ملخص خطة التغييرات:</span>
                  <span className="font-mono text-[10px] px-2 py-0.5 rounded bg-[var(--surface)] border border-[var(--line)] text-[var(--muted)]">
                    البصمة: <bdi dir="ltr">{importPreviewPlan.fingerprint.slice(0, 12)}...</bdi>
                  </span>
                </div>

                <div className="grid grid-cols-2 gap-2 text-[11px] pt-1">
                  <div className="p-2.5 rounded-lg bg-[var(--surface)] border border-[var(--line)]">
                    <strong className="block text-[var(--accent)] mb-1">الشركات:</strong>
                    <div>جديد: {importPreviewPlan.counts.company.new}</div>
                    <div>معدل: {importPreviewPlan.counts.company.changed}</div>
                    <div>بدون تغيير: {importPreviewPlan.counts.company.unchanged}</div>
                  </div>
                  <div className="p-2.5 rounded-lg bg-[var(--surface)] border border-[var(--line)]">
                    <strong className="block text-[var(--accent)] mb-1">السجلات التجارية:</strong>
                    <div>جديد: {importPreviewPlan.counts.commercial_registration.new}</div>
                    <div>معدل: {importPreviewPlan.counts.commercial_registration.changed}</div>
                    <div>بدون تغيير: {importPreviewPlan.counts.commercial_registration.unchanged}</div>
                  </div>
                </div>
              </div>

              <div className="p-3 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/30 text-[var(--ink)] text-[11px]">
                سيتم تحديث الكيانات المسجلة مع الأرقام الموحدة وتواريخ التأكيد السنوي والأنشطة المعتمدة.
              </div>

              <div className="flex items-center justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setImportModalOpen(false)}
                  className="h-10 px-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] font-semibold transition"
                >
                  إلغاء
                </button>
                <button
                  type="button"
                  onClick={handleCommitImport}
                  className="h-10 px-5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold shadow-sm hover:opacity-95 transition"
                >
                  تأكيد الاستيراد والاعتماد النهائي
                </button>
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setImportModalOpen(false)}
                className="h-10 px-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] font-semibold transition"
              >
                إغلاق
              </button>
              <button
                type="button"
                onClick={handleRunImportPreview}
                className="h-10 px-5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold shadow-sm hover:opacity-95 transition"
              >
                توليد خطة الاستيراد
              </button>
            </div>
          )}
        </div>
      </Dialog>

      <ImportUploadDialog
        open={fileImportOpen}
        onClose={() => setFileImportOpen(false)}
        step={COMPANIES_IMPORT}
        onCommitted={() => {
          void fetchData();
        }}
      />
    </div>
  );
}

export default function CompaniesPage() {
  return (
    <Suspense
      fallback={
        <div className="p-8 text-center text-xs text-[var(--muted)]">
          جاري تحميل صفحة الشركات والسجلات...
        </div>
      }
    >
      <CompaniesPageContent />
    </Suspense>
  );
}
