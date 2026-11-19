'use client';

import React, { useState, useEffect, useMemo, useCallback, useRef, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  Building2,
  FileText,
  AlertTriangle,
  Coins,
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
} from 'lucide-react';
import {
  api,
  ApiError,
  type CompanyContract,
  type CompanyDocumentContract,
  type CompanyExpiringContract,
  type CompanyImportPlanContract,
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
  buildCompanyStatsExport,
  buildWorkforceCompanyUrl,
  getPlatformColumns,
  normalizeCompaniesTab,
  platformCountFor,
  platformLabelAr,
  type CompaniesTabId,
} from '../../features/companies/company-stats';

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
  const searchParams = useSearchParams();
  const canManage = isAdmin || can('companies.manage');

  const tabFromUrl = normalizeCompaniesTab(searchParams?.get('tab'));
  const [activeTab, setActiveTab] = useState<CompaniesTabId>(tabFromUrl);

  useEffect(() => {
    setActiveTab(normalizeCompaniesTab(searchParams?.get('tab')));
  }, [searchParams]);

  // Core company tables (our DB)
  const [companies, setCompanies] = useState<CompanyContract[]>([]);
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

  // Drawer state
  const [selectedCompany, setSelectedCompany] = useState<CompanyContract | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerTab, setDrawerTab] = useState<'details' | 'activities' | 'documents' | 'stats' | 'audit'>('details');
  const [companyDocs, setCompanyDocs] = useState<CompanyDocumentContract[]>([]);
  const [loadingDocs, setLoadingDocs] = useState(false);

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

  // Import Modal State (Admin only)
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
      setCompanies(Array.isArray(compList) ? compList : []);
      setExpiring(Array.isArray(expList) ? expList : []);
      if (isAdmin) {
        try {
          const logs = await api.getAuditLogs();
          setAuditLogs(Array.isArray(logs) ? logs : []);
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

  // Open drawer and load company documents
  const handleOpenDrawer = async (comp: CompanyContract) => {
    setSelectedCompany(comp);
    setDrawerTab('details');
    setDrawerOpen(true);
    setLoadingDocs(true);
    try {
      const docs = await api.getCompanyDocuments(comp.id);
      setCompanyDocs(Array.isArray(docs) ? docs : []);
    } catch {
      setCompanyDocs([]);
    } finally {
      setLoadingDocs(false);
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
  const filteredStatsItems = useMemo(() => {
    const list = stats?.items || [];
    if (!searchQuery.trim()) return list;
    const q = searchQuery.toLowerCase().trim();
    return list.filter(
      (item) =>
        item.nameAr.toLowerCase().includes(q) ||
        item.shortNameAr.toLowerCase().includes(q) ||
        item.code.toLowerCase().includes(q) ||
        item.unifiedNumber.includes(q) ||
        item.commercialReg.includes(q)
    );
  }, [stats, searchQuery]);

  const statsPlatformColumns = useMemo(
    () => getPlatformColumns(filteredStatsItems, stats?.totals.byPlatform),
    [filteredStatsItems, stats]
  );

  // Registrations list derived from companies
  const allRegistrations = useMemo(() => {
    return companies
      .filter((c) => c.commercialReg || c.crNumber)
      .map((c) => ({
        id: `cr-${c.id}`,
        companyId: c.id,
        companyNameAr: c.nameAr,
        companyCode: c.code,
        number: c.crNumber || c.commercialReg || '—',
        city: c.city || 'جدة',
        issueDate: c.issueDate,
        issueDateHijri: c.issueDateHijri,
        annualConfirmation: c.annualConfirmation,
        annualConfirmationHijri: c.annualConfirmationHijri,
        crVersion: c.crVersion || 1,
        status: c.status || 'active',
      }));
  }, [companies]);

  // Summary metrics
  const totalCapital = useMemo(() => {
    return companies.reduce((acc, c) => acc + (Number(c.capitalSar) || 0), 0);
  }, [companies]);

  const expiringCount = useMemo(() => {
    return companies.filter((c) => isDueWithinDays(c.annualConfirmation, 60)).length;
  }, [companies]);

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
        statsByCompanyId.get(c.id)?.sponsorships.total ?? Number(c.sponsoredRidersCount || 0),
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

      {/* Top Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="الشركات والمنشآت"
          value={formatNumber(companies.length)}
          hint="كيانات قانونية رئيسية مسجلة"
          icon={Building2}
        />
        <StatCard
          label="السجلات التجارية النشطة"
          value={formatNumber(allRegistrations.length)}
          hint="سجلات مركزية معتمدة"
          icon={FileText}
        />
        <StatCard
          label="استحقاقات التأكيد خلال 60 يومًا"
          value={formatNumber(expiringCount)}
          hint="تنبيه قبل الاستحقاق السنوي"
          icon={AlertTriangle}
          className={expiringCount > 0 ? 'border-[var(--warning)]/50' : undefined}
        />
        <StatCard
          label="إجمالي رأس المال المثبت"
          value={showBalances ? `${formatNumber(totalCapital)} ر.س` : '••••••••'}
          hint="رأس المال الكلي في السجلات"
          icon={Coins}
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
            label: 'الإحصائيات',
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
              <select
                value={entityTypeFilter}
                onChange={(e) => setEntityTypeFilter(e.target.value)}
                className="h-10 px-3 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-none focus:ring-2 focus:ring-[var(--focus)]"
                aria-label="تصفية حسب نوع الكيان"
              >
                <option value="all">كل أنواع الكيانات</option>
                <option value="شركة ذات مسؤولية محدودة">شركة ذات مسؤولية محدودة</option>
                <option value="مؤسسة فردية">مؤسسة فردية</option>
              </select>

              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="h-10 px-3 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-none focus:ring-2 focus:ring-[var(--focus)]"
                aria-label="تصفية حسب الحالة"
              >
                <option value="all">كل الحالات</option>
                <option value="active">نشط</option>
                <option value="inactive">غير نشط</option>
              </select>

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
              {/* Live KPI Summary Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <StatCard
                  label="الشركات ضمن النطاق"
                  value={formatNumber(stats.totals.companiesCount)}
                  hint="الكيانات المرتبطة بنظام الموارد"
                  icon={Building2}
                />
                <StatCard
                  label="إجمالي المكفولين"
                  value={formatNumber(stats.totals.sponsoredTotal)}
                  hint={`مناديب: ${formatNumber(stats.totals.sponsoredRiders)} · إداريون: ${formatNumber(stats.totals.sponsoredAdmins)}`}
                  icon={Users}
                />
                <StatCard
                  label="المكفولون النشطون"
                  value={formatNumber(stats.totals.sponsoredActive)}
                  hint={`في إجازة: ${formatNumber(stats.totals.sponsoredOnLeave)}`}
                  icon={UserCheck}
                />
                <StatCard
                  label="حسابات المنصات المسجلة"
                  value={formatNumber(stats.totals.platformAccountsTotal)}
                  hint={`نشط: ${formatNumber(stats.totals.platformAccountsActive)} · مسند: ${formatNumber(stats.totals.platformAccountsAssigned)}`}
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
                                    : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                }`}
                              >
                                <CheckCircle2 size={11} aria-hidden="true" />
                                <span>{item.erpLinked ? 'مرتبط بنظام الموارد' : 'غير مرتبط'}</span>
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
                    نطاق الإجماليات: كل الشركات ضمن صلاحية المستخدم
                  </span>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs border-collapse">
                    <thead>
                      <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                        <th scope="col" className="py-3 px-4 text-start whitespace-nowrap">الشركة / السجل</th>
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">إجمالي المكفولين</th>
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">مناديب</th>
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">إداريون</th>
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">نشطون</th>
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">إجمالي الحسابات</th>
                        {statsPlatformColumns.map((plat) => (
                          <th key={plat.code} scope="col" className="py-3 px-3 text-start whitespace-nowrap">
                            {plat.labelAr}
                          </th>
                        ))}
                        <th scope="col" className="py-3 px-3 text-start whitespace-nowrap">نشط / مسند</th>
                        <th scope="col" className="py-3 px-3 text-center whitespace-nowrap">القوى العاملة</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]/60">
                      {filteredStatsItems.map((item) => {
                        const companyKey = item.unifiedNumber || item.companyId;
                        return (
                          <tr key={item.companyId} className="hover:bg-[var(--row-hover)] transition">
                            <td className="py-3 px-4 whitespace-nowrap">
                              <div className="font-bold text-[var(--ink)]">{item.nameAr}</div>
                              <div className="text-[11px] text-[var(--muted)]">
                                <bdi dir="ltr" className="font-mono num">
                                  {item.unifiedNumber || '—'}
                                </bdi>
                              </div>
                            </td>
                            <td className="py-3 px-3 font-mono font-bold num text-[var(--ink)]">
                              {formatNumber(item.sponsorships.total)}
                            </td>
                            <td className="py-3 px-3 font-mono num text-[var(--ink)]">
                              {formatNumber(item.sponsorships.riders)}
                            </td>
                            <td className="py-3 px-3 font-mono num text-[var(--ink)]">
                              {formatNumber(item.sponsorships.admins)}
                            </td>
                            <td className="py-3 px-3 font-mono num text-[var(--money-pos)] font-semibold">
                              {formatNumber(item.sponsorships.active)}
                            </td>
                            <td className="py-3 px-3 font-mono font-bold num text-[var(--accent)]">
                              {formatNumber(item.platformAccounts.total)}
                            </td>
                            {statsPlatformColumns.map((plat) => {
                              const count = platformCountFor(item.platformAccounts.byPlatform, plat.code);
                              return (
                                <td key={plat.code} className="py-3 px-3 font-mono num">
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
                            <td className="py-3 px-3 font-mono num whitespace-nowrap">
                              <span className="text-[var(--money-pos)] font-semibold">
                                {formatNumber(item.platformAccounts.active)}
                              </span>
                              <span className="text-[var(--muted)] mx-1">/</span>
                              <span className="text-[var(--ink)] font-semibold">
                                {formatNumber(item.platformAccounts.assigned)}
                              </span>
                            </td>
                            <td className="py-3 px-3 text-center whitespace-nowrap">
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
                        <td className="py-3 px-4 whitespace-nowrap">الإجمالي العام</td>
                        <td className="py-3 px-3 font-mono num">{formatNumber(stats.totals.sponsoredTotal)}</td>
                        <td className="py-3 px-3 font-mono num">{formatNumber(stats.totals.sponsoredRiders)}</td>
                        <td className="py-3 px-3 font-mono num">{formatNumber(stats.totals.sponsoredAdmins)}</td>
                        <td className="py-3 px-3 font-mono num text-[var(--money-pos)]">
                          {formatNumber(stats.totals.sponsoredActive)}
                        </td>
                        <td className="py-3 px-3 font-mono num text-[var(--accent)]">
                          {formatNumber(stats.totals.platformAccountsTotal)}
                        </td>
                        {statsPlatformColumns.map((plat) => (
                          <td key={plat.code} className="py-3 px-3 font-mono num">
                            {formatNumber(platformCountFor(stats.totals.byPlatform, plat.code))}
                          </td>
                        ))}
                        <td className="py-3 px-3 font-mono num whitespace-nowrap">
                          {formatNumber(stats.totals.platformAccountsActive)} /{' '}
                          {formatNumber(stats.totals.platformAccountsAssigned)}
                        </td>
                        <td className="py-3 px-3 text-center">—</td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </section>

              {/* Company Financial Balances Placeholder (deferred until accounting tables) */}
              <section
                aria-label="الأرصدة المالية للشركات"
                data-testid="company-balances-placeholder"
                className="rounded-2xl border border-dashed border-[var(--line)] bg-[var(--raised)]/35 p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4"
              >
                <div className="flex items-start gap-3">
                  <div className="h-10 w-10 rounded-xl bg-[var(--surface)] border border-[var(--line)] flex items-center justify-center text-[var(--accent)] shrink-0">
                    <Wallet size={20} aria-hidden="true" />
                  </div>
                  <div className="space-y-1">
                    <h3 className="text-sm font-bold text-[var(--ink)]">الأرصدة المالية لكل شركة</h3>
                    <p className="text-xs font-semibold text-[var(--accent)]">
                      {stats.balancesNote || BALANCES_PLACEHOLDER_AR}
                    </p>
                    <p className="text-xs text-[var(--muted)]">
                      ستظهر هنا أرصدة البنوك والخزن والذمم والمديونيات وأصول المركبات لكل شركة فور ربط جداول المحاسبة.
                    </p>
                  </div>
                </div>
                <span className="shrink-0 inline-flex items-center px-3 py-1 rounded-full bg-[var(--surface)] border border-[var(--line)] text-xs font-bold text-[var(--muted)]">
                  {BALANCES_PLACEHOLDER_AR}
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
                        <td className="py-3 px-4 text-[var(--ink)]">{c.city || 'جدة'}</td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {c.capitalSar
                            ? showBalances
                              ? `${formatNumber(c.capitalSar)} ر.س`
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
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-[var(--money-pos)]/10 text-[var(--money-pos)]">
                            <span className="h-1.5 w-1.5 rounded-full bg-current" />
                            {c.status === 'active' ? 'نشط' : 'غير نشط'}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-center">
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
                {allRegistrations.map((cr) => {
                  const isExpiring = isDueWithinDays(cr.annualConfirmation, 60);
                  const rem = getDaysRemaining(cr.annualConfirmation);

                  return (
                    <tr key={cr.id} className="hover:bg-[var(--row-hover)] transition">
                      <td className="py-3 px-4 font-mono font-bold text-xs num">
                        <bdi dir="ltr">{cr.number}</bdi>
                      </td>
                      <td className="py-3 px-4 font-bold text-[var(--ink)]">{cr.companyNameAr}</td>
                      <td className="py-3 px-4 text-[var(--muted)]">رئيسي</td>
                      <td className="py-3 px-4 font-mono text-xs num">{cr.crVersion}</td>
                      <td className="py-3 px-4 text-[var(--ink)]">{cr.city || 'جدة'}</td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        {cr.issueDateHijri ? `${cr.issueDateHijri} هـ` : ''}{' '}
                        {cr.issueDate ? `(${cr.issueDate})` : '—'}
                      </td>
                      <td className="py-3 px-4">
                        <div className="space-y-1">
                          <div className="font-mono text-xs num">
                            {cr.annualConfirmationHijri ? `${cr.annualConfirmationHijri} هـ` : ''}{' '}
                            <span className="text-[var(--muted)]">({cr.annualConfirmation || '—'})</span>
                          </div>
                          {isExpiring && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
                              <AlertTriangle size={11} aria-hidden="true" />
                              <span>مستحق خلال {rem} يوم</span>
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3 px-4">
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-[var(--money-pos)]/10 text-[var(--money-pos)]">
                          {cr.status === 'active' ? 'نشط' : 'غير نشط'}
                        </span>
                      </td>
                    </tr>
                  );
                })}
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
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]/60">
                {companies.map((c) => {
                  const rem = getDaysRemaining(c.annualConfirmation);
                  const isClose = rem !== null && rem <= 60;

                  return (
                    <tr key={`exp-${c.id}`} className="hover:bg-[var(--row-hover)] transition">
                      <td className="py-3 px-4 font-bold text-[var(--accent)]">
                        التأكيد السنوي للسجل التجاري
                      </td>
                      <td className="py-3 px-4 font-bold text-[var(--ink)]">{c.nameAr}</td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        <bdi dir="ltr">{c.crNumber || c.commercialReg || '—'}</bdi>
                      </td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        {c.annualConfirmationHijri ? `${c.annualConfirmationHijri} هـ` : ''}{' '}
                        ({c.annualConfirmation || '—'})
                      </td>
                      <td className="py-3 px-4">
                        <span
                          className={`font-mono font-bold text-xs ${
                            rem !== null && rem <= 30
                              ? 'text-[var(--danger)]'
                              : rem !== null && rem <= 60
                              ? 'text-[var(--warning)]'
                              : 'text-[var(--muted)]'
                          }`}
                        >
                          {rem !== null ? `${rem} يوم` : '—'}
                        </span>
                      </td>
                      <td className="py-3 px-4">
                        <span
                          className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[11px] font-bold ${
                            isClose
                              ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              : 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                          }`}
                        >
                          {isClose ? 'يستحق المتابعة' : 'ساري'}
                        </span>
                      </td>
                    </tr>
                  );
                })}
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
                بيانات المنشأة
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
                    <strong className="text-[var(--ink)]">{selectedCompany.city || 'جدة'}</strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">رأس المال المثبت</span>
                    <strong className="font-mono text-sm num text-[var(--money-pos)]">
                      {selectedCompany.capitalSar
                        ? showBalances
                          ? `${formatNumber(selectedCompany.capitalSar)} ر.س`
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
                          <strong className="text-[var(--ink)] block">{log.action}</strong>
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
