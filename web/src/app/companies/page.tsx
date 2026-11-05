'use client';

import React, { useState, useEffect, useMemo, useRef } from 'react';
import {
  Building2,
  FileText,
  Calendar,
  AlertTriangle,
  Coins,
  Search,
  Filter,
  Download,
  Upload,
  Plus,
  ExternalLink,
  CheckCircle2,
  AlertCircle,
  Clock,
  ShieldCheck,
  ChevronRight,
  Eye,
  FileSpreadsheet,
  X,
  Sparkles,
  Layers,
  History,
  Tag,
  Users,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Tabs } from '../../components/ui/Tabs';
import { Toolbar } from '../../components/ui/Toolbar';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import type {
  Company,
  CommercialRegistration,
  CompanyDocument,
  ExpiringRecord,
  CompanyImportPlan,
} from '../../lib/types';

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

export default function CompaniesPage() {
  const { toast } = useToast();
  const { isAdmin } = useAuth();

  // State
  const [activeTab, setActiveTab] = useState<'companies' | 'registrations' | 'documents'>('companies');
  const [companies, setCompanies] = useState<Company[]>([]);
  const [expiring, setExpiring] = useState<ExpiringRecord[]>([]);
  const [auditLogs, setAuditLogs] = useState<Array<{ id: string; action: string; actor: string; at: string }>>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters & Search
  const [searchQuery, setSearchQuery] = useState('');
  const [entityTypeFilter, setEntityTypeFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [expiringOnly, setExpiringOnly] = useState(false);

  // Drawer state
  const [selectedCompany, setSelectedCompany] = useState<Company | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerTab, setDrawerTab] = useState<'details' | 'activities' | 'documents' | 'audit'>('details');
  const [companyDocs, setCompanyDocs] = useState<CompanyDocument[]>([]);
  const [loadingDocs, setLoadingDocs] = useState(false);

  // Document upload state
  const [uploadDocModalOpen, setUploadDocModalOpen] = useState(false);
  const [docType, setDocType] = useState('شهادة السجل التجاري');
  const [docNumber, setDocNumber] = useState('');
  const [docExpiryDate, setDocExpiryDate] = useState('');
  const [docFile, setDocFile] = useState<File | null>(null);
  const [uploadingDoc, setUploadingDoc] = useState(false);

  // Import Modal State (Admin only)
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [importPreviewPlan, setImportPreviewPlan] = useState<CompanyImportPlan | null>(null);
  const [importLoading, setImportLoading] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);

  // Load data
  const fetchData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [compList, expList, logs] = await Promise.all([
        api.getCompaniesList(),
        api.getExpiringCompanies(60),
        api.getAuditLogs(),
      ]);
      setCompanies(compList);
      setExpiring(expList);
      setAuditLogs(logs);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل بيانات الشركات والسجلات.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
  }, []);

  // Open drawer and load company documents
  const handleOpenDrawer = async (comp: Company) => {
    setSelectedCompany(comp);
    setDrawerTab('details');
    setDrawerOpen(true);
    setLoadingDocs(true);
    try {
      const docs = await api.getCompanyDocuments(comp.id);
      setCompanyDocs(docs);
    } catch (_) {
      setCompanyDocs([]);
    } finally {
      setLoadingDocs(false);
    }
  };

  // Upload document
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
    } catch (err: any) {
      toast(err?.message || 'تعذر رفع المستند', 'danger');
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
    } catch (err: any) {
      setImportError(err?.message || 'تعذر استخراج خطة استيراد الشهادات');
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
        rows: importPreviewPlan.rows,
        fingerprint: importPreviewPlan.fingerprint,
        confirm: true,
      });
      toast('تم اعتماد وتحديث شهادات الشركات بنجاح', 'success');
      setImportModalOpen(false);
      setImportPreviewPlan(null);
      await fetchData();
    } catch (err: any) {
      setImportError(err?.message || 'فشل اعتماد استيراد بيانات الشركات');
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

  // Registrations list derived from companies
  const allRegistrations: CommercialRegistration[] = useMemo(() => {
    return companies
      .filter((c) => c.commercialReg || c.crNumber)
      .map((c) => ({
        id: `cr-${c.id}`,
        companyId: c.id,
        company: `${c.nameAr} (${c.code})`,
        number: c.crNumber || c.commercialReg || '—',
        city: c.city || 'جدة',
        issueDate: c.issueDate,
        issueDateHijri: c.issueDateHijri,
        annualConfirmation: c.annualConfirmation,
        annualConfirmationHijri: c.annualConfirmationHijri,
        crVersion: c.crVersion || 1,
        status: (c.status as any) || 'active',
        createdAt: c.createdAt,
      }));
  }, [companies]);

  // Stats calculation
  const totalCapital = useMemo(() => {
    return companies.reduce((acc, c) => acc + (Number(c.capitalSar) || 0), 0);
  }, [companies]);

  const expiringCount = useMemo(() => {
    return companies.filter((c) => isDueWithinDays(c.annualConfirmation, 60)).length;
  }, [companies]);

  // Export Excel / CSV
  const handleExport = async (type: 'all' | 'filtered' | 'csv') => {
    try {
      const rowsToExport = type === 'all' ? companies : filteredCompanies;
      const headers = [
        'كود الشركة',
        'اسم المنشأة',
        'الرقم الموحد (700)',
        'رقم السجل التجاري',
        'نوع الكيان',
        'المدينة',
        'رأس المال (ر.س)',
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
        c.annualConfirmation ? `${c.annualConfirmationHijri || ''} (${c.annualConfirmation})` : '',
        c.status === 'active' ? 'نشط' : 'غير نشط',
      ]);

      if (type === 'csv') {
        const csvContent = [headers.join(','), ...rows.map((r) => r.map((val) => `"${String(val).replace(/"/g, '""')}"`).join(','))].join('\n');
        const blob = new Blob(['\uFEFF' + csvContent], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `companies_${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تصدير ملف CSV بنجاح', 'success');
        return;
      }

      const blob = await api.exportExcel({
        headers,
        rows,
        numericColumns: [2, 3, 6],
        filename: 'companies_registry.xlsx',
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `companies_${new Date().toISOString().slice(0, 10)}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      toast('تم تصدير ملف Excel بنجاح', 'success');
    } catch (err: any) {
      toast(err?.message || 'تعذر تصدير الملف', 'danger');
    }
  };

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="الشركات والسجلات التجارية"
        eyebrow="الكيانات القانونية والشهادات الرسمية"
        description="إدارة الكيانات القانونية الثلاثة (BW, EX, TR) والسجلات والشهادات الصادرة من وزارة التجارة وتنبيهات التأكيد السنوي."
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
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          label="الشركات والمنشآت"
          value={companies.length}
          hint="كيانات قانونية رئيسية مسجلة"
          icon={Building2}
        />
        <StatCard
          label="السجلات التجارية النشطة"
          value={allRegistrations.length}
          hint="سجلات مركزية معتمدة"
          icon={FileText}
        />
        <StatCard
          label="استحقاقات التأكيد خلال 60 يوم"
          value={expiringCount}
          hint="تنبيه قبل الاستحقاق السنوي"
          icon={AlertTriangle}
          className={expiringCount > 0 ? 'border-[var(--warning)]/50' : undefined}
        />
        <StatCard
          label="إجمالي رأس المال المثبت"
          value={totalCapital.toLocaleString('en-US') + ' ر.س'}
          hint="رأس المال الكلي في السجلات"
          icon={Coins}
        />
      </div>

      {/* Tabs */}
      <Tabs
        tabs={[
          { id: 'companies', label: 'الشركات والمنشآت' },
          { id: 'registrations', label: 'السجلات التجارية', badge: allRegistrations.length },
          { id: 'documents', label: 'الوثائق والانتهاءات', badge: expiring.length > 0 ? expiring.length : undefined },
        ]}
        activeTab={activeTab}
        onChange={(tab) => setActiveTab(tab as any)}
      />

      {/* Toolbar */}
      <Toolbar
        searchPlaceholder="بحث باسم الشركة، الكود (BW/EX/TR)، السجل التجاري، الرقم الموحد أو المدينة..."
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
                <span>استحقاق &lt; 60 يوم</span>
              </button>
            </div>
          ) : undefined
        }
        onExport={(format) => {
          if (format === 'excel-all') handleExport('all');
          else if (format === 'excel-filtered') handleExport('filtered');
          else if (format === 'csv-filtered') handleExport('csv');
        }}
      />

      {/* Main Content Areas */}
      {error ? (
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
        /* Sub-tab 1: الشركات */
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th className="py-3 px-4 text-start">الكود</th>
                  <th className="py-3 px-4 text-start">المنشأة الرسمية</th>
                  <th className="py-3 px-4 text-start">الرقم الموحد (700)</th>
                  <th className="py-3 px-4 text-start">السجل التجاري</th>
                  <th className="py-3 px-4 text-start">نوع الكيان</th>
                  <th className="py-3 px-4 text-start">المدينة</th>
                  <th className="py-3 px-4 text-start">رأس المال</th>
                  <th className="py-3 px-4 text-start">المكفولون</th>
                  <th className="py-3 px-4 text-start">التأكيد السنوي القادم</th>
                  <th className="py-3 px-4 text-start">الحالة</th>
                  <th className="py-3 px-4 text-center">إجراءات</th>
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

                    return (
                      <tr
                        key={c.id}
                        className="hover:bg-[var(--row-hover)] transition cursor-pointer"
                        onClick={() => handleOpenDrawer(c)}
                      >
                        <td className="py-3 px-4 font-mono font-bold text-[var(--accent)]">
                          {c.code}
                        </td>
                        <td className="py-3 px-4">
                          <div className="font-bold text-[var(--ink)]">{c.nameAr}</div>
                          {c.nameEn && (
                            <div className="font-mono text-[11px] text-[var(--muted)] ltr text-start">
                              {c.nameEn}
                            </div>
                          )}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num ltr">
                          {c.unifiedNumber || '—'}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num ltr">
                          {c.crNumber || c.commercialReg || '—'}
                        </td>
                        <td className="py-3 px-4 text-[var(--muted)]">
                          {c.entityType || '—'}
                        </td>
                        <td className="py-3 px-4 text-[var(--ink)]">
                          {c.city || 'جدة'}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {c.capitalSar ? Number(c.capitalSar).toLocaleString('en-US') + ' ر.س' : '—'}
                        </td>
                        <td className="py-3 px-4 font-mono text-xs num">
                          {c.sponsoredRidersCount ? (
                            <span className="inline-flex items-center gap-1 font-bold text-[var(--accent)]">
                              <Users size={12} />
                              {c.sponsoredRidersCount}
                            </span>
                          ) : (
                            '0'
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
                                  <AlertTriangle size={11} />
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
                            نشط
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
                            <Eye size={13} />
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
      ) : activeTab === 'registrations' ? (
        /* Sub-tab 2: السجلات التجارية */
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th className="py-3 px-4 text-start">رقم السجل التجاري</th>
                  <th className="py-3 px-4 text-start">المنشأة التابعة</th>
                  <th className="py-3 px-4 text-start">نوع السجل</th>
                  <th className="py-3 px-4 text-start">النسخة</th>
                  <th className="py-3 px-4 text-start">المدينة</th>
                  <th className="py-3 px-4 text-start">تاريخ القيد</th>
                  <th className="py-3 px-4 text-start">التأكيد السنوي القادم</th>
                  <th className="py-3 px-4 text-start">الحالة</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]/60">
                {allRegistrations.map((cr) => {
                  const isExpiring = isDueWithinDays(cr.annualConfirmation, 60);
                  const rem = getDaysRemaining(cr.annualConfirmation);

                  return (
                    <tr key={cr.id} className="hover:bg-[var(--row-hover)] transition">
                      <td className="py-3 px-4 font-mono font-bold text-xs num ltr">
                        {cr.number}
                      </td>
                      <td className="py-3 px-4 font-bold text-[var(--ink)]">
                        {cr.company}
                      </td>
                      <td className="py-3 px-4 text-[var(--muted)]">
                        رئيسي
                      </td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        {cr.crVersion}
                      </td>
                      <td className="py-3 px-4 text-[var(--ink)]">
                        {cr.city || 'جدة'}
                      </td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        {cr.issueDateHijri ? `${cr.issueDateHijri} هـ` : ''}{' '}
                        {cr.issueDate ? `(${cr.issueDate})` : '—'}
                      </td>
                      <td className="py-3 px-4">
                        <div className="space-y-1">
                          <div className="font-mono text-xs num">
                            {cr.annualConfirmationHijri ? `${cr.annualConfirmationHijri} هـ` : ''}{' '}
                            <span className="text-[var(--muted)]">({cr.annualConfirmation})</span>
                          </div>
                          {isExpiring && (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
                              <AlertTriangle size={11} />
                              <span>مستحق خلال {rem} يوم</span>
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3 px-4">
                        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-[var(--money-pos)]/10 text-[var(--money-pos)]">
                          نشط
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
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden">
          <div className="p-4 bg-[var(--raised)]/40 border-b border-[var(--line)] flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Clock size={16} className="text-[var(--accent)]" />
              <h3 className="text-xs font-bold text-[var(--ink)]">
                السجلات والوثائق مستحقة التجديد والتأكيد
              </h3>
            </div>
            <span className="text-xs text-[var(--muted)]">
              نافذة التنبيه: 60 يوماً
            </span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-bold">
                  <th className="py-3 px-4 text-start">النوع</th>
                  <th className="py-3 px-4 text-start">المنشأة</th>
                  <th className="py-3 px-4 text-start">رقم المستند / السجل</th>
                  <th className="py-3 px-4 text-start">تاريخ الاستحقاق / الانتهاء</th>
                  <th className="py-3 px-4 text-start">المدة المتبقية</th>
                  <th className="py-3 px-4 text-start">الموقف</th>
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
                      <td className="py-3 px-4 font-bold text-[var(--ink)]">
                        {c.nameAr} ({c.code})
                      </td>
                      <td className="py-3 px-4 font-mono text-xs num ltr">
                        {c.crNumber || c.commercialReg || '—'}
                      </td>
                      <td className="py-3 px-4 font-mono text-xs num">
                        {c.annualConfirmationHijri ? `${c.annualConfirmationHijri} هـ` : ''}{' '}
                        ({c.annualConfirmation})
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

      {/* Drawer: تفاصيل المنشأة والأنشطة والوثائق */}
      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title={selectedCompany ? `${selectedCompany.nameAr} (${selectedCompany.code})` : 'تفاصيل المنشأة'}
        width="wide"
      >
        {selectedCompany && (
          <div className="space-y-6">
            {/* Drawer Sub-tabs */}
            <div className="flex border-b border-[var(--line)] gap-2">
              <button
                type="button"
                onClick={() => setDrawerTab('details')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition ${
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
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition flex items-center gap-1.5 ${
                  drawerTab === 'activities'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                <span>الأنشطة المعتمدة (ISIC)</span>
                {selectedCompany.activitiesIncomplete && (
                  <span className="text-[10px] font-bold px-1.5 py-0.2 rounded bg-[var(--warning)]/15 text-[var(--warning)]">
                    ناقص
                  </span>
                )}
              </button>
              <button
                type="button"
                onClick={() => setDrawerTab('documents')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition flex items-center gap-1.5 ${
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
                onClick={() => setDrawerTab('audit')}
                className={`pb-2.5 px-3 text-xs font-bold border-b-2 transition ${
                  drawerTab === 'audit'
                    ? 'border-[var(--accent)] text-[var(--accent)]'
                    : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                سجل العمليات
              </button>
            </div>

            {/* Drawer Tab 1: البيانات الرسمية */}
            {drawerTab === 'details' && (
              <div className="space-y-4 text-xs">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-4 rounded-xl bg-[var(--raised)]/40 border border-[var(--line)]">
                  <div>
                    <span className="text-[var(--muted)] block mb-1">الرقم الوطني الموحد (700)</span>
                    <strong className="font-mono text-sm num text-[var(--ink)]">
                      {selectedCompany.unifiedNumber || '—'}
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">رقم السجل التجاري</span>
                    <strong className="font-mono text-sm num text-[var(--ink)]">
                      {selectedCompany.crNumber || selectedCompany.commercialReg || '—'}
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">نوع الكيان القانوني</span>
                    <strong className="text-[var(--ink)]">
                      {selectedCompany.entityType || '—'}
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">صفة الشركة</span>
                    <strong className="text-[var(--ink)]">
                      {selectedCompany.characteristic || '—'}
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">المدينة والمقر</span>
                    <strong className="text-[var(--ink)]">
                      {selectedCompany.city || 'جدة'}
                    </strong>
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block mb-1">رأس المال المثبت</span>
                    <strong className="font-mono text-sm num text-[var(--money-pos)]">
                      {selectedCompany.capitalSar
                        ? Number(selectedCompany.capitalSar).toLocaleString('en-US') + ' ر.س'
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
                        {selectedCompany.annualConfirmationHijri ? `${selectedCompany.annualConfirmationHijri} هـ` : ''}{' '}
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

            {/* Drawer Tab 2: الأنشطة المعتمدة ISIC */}
            {drawerTab === 'activities' && (
              <div className="space-y-4">
                {selectedCompany.activitiesIncomplete && (
                  <div className="p-3 rounded-xl bg-[var(--warning)]/15 border border-[var(--warning)]/30 text-xs text-[var(--warning)] flex items-start gap-2">
                    <AlertTriangle size={15} className="shrink-0 mt-0.5" />
                    <p>
                      <strong>أنشطة ناقصة:</strong> توجد أنشطة إضافية في شهادة السجل لم يتم تسجيلها بعد في النظام بانتظار استكمال بياناتها.
                    </p>
                  </div>
                )}

                <div className="space-y-2">
                  <h4 className="text-xs font-bold text-[var(--ink)]">قائمة أنشطة المنشأة (ISIC):</h4>
                  {selectedCompany.activities && selectedCompany.activities.length > 0 ? (
                    <div className="flex flex-wrap gap-2">
                      {selectedCompany.activities.map((act) => (
                        <div
                          key={act.isic}
                          className="p-2.5 rounded-xl border border-[var(--line)] bg-[var(--raised)]/50 text-xs flex items-center gap-2"
                        >
                          <span className="font-mono font-bold text-[var(--accent)] bg-[var(--surface)] px-2 py-0.5 rounded border border-[var(--line)]">
                            {act.isic}
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

            {/* Drawer Tab 3: الوثائق والمستندات */}
            {drawerTab === 'documents' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold text-[var(--ink)]">مستندات وتراخيص المنشأة</h4>
                  {isAdmin && (
                    <button
                      type="button"
                      onClick={() => setUploadDocModalOpen(true)}
                      className="h-8 px-3 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold inline-flex items-center gap-1.5 shadow-2xs hover:opacity-95 transition"
                    >
                      <Plus size={14} />
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
                          <FileText size={16} className="text-[var(--accent)] shrink-0" />
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
                              <Download size={12} />
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

            {/* Drawer Tab 4: سجل العمليات */}
            {drawerTab === 'audit' && (
              <div className="space-y-3">
                <h4 className="text-xs font-bold text-[var(--ink)]">العمليات المرتبطة بالشركات</h4>
                <div className="divide-y divide-[var(--line)] border border-[var(--line)] rounded-xl bg-[var(--surface)] max-h-80 overflow-y-auto">
                  {auditLogs.length === 0 ? (
                    <div className="p-6 text-center text-xs text-[var(--muted)]">
                      لا توجد أحداث مراجعة مسجلة حالياً.
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
        title="إضافة وثيقة / ترخيص منشأة"
      >
        <form onSubmit={handleUploadDocument} className="space-y-4 text-xs">
          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">نوع المستند</label>
            <select
              value={docType}
              onChange={(e) => setDocType(e.target.value)}
              className="w-full h-10 px-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-none focus:ring-2 focus:ring-[var(--focus)]"
            >
              <option value="شهادة السجل التجاري">شهادة السجل التجاري</option>
              <option value="شهادة التأمينات الاجتماعية (GOSI)">شهادة التأمينات الاجتماعية (GOSI)</option>
              <option value="شهادة الزكاة والضريبة (ZATCA)">شهادة الزكاة والضريبة (ZATCA)</option>
              <option value="رخصة البلدية">رخصة البلدية</option>
              <option value="اشتراك الغرفة التجارية">اشتراك الغرفة التجارية</option>
              <option value="شهادة العنوان الوطني">شهادة العنوان الوطني</option>
              <option value="أخرى">أخرى</option>
            </select>
          </div>

          <div>
            <label className="block font-semibold text-[var(--ink)] mb-1">رقم المستند / المرجع</label>
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
            <label className="block font-semibold text-[var(--ink)] mb-1">ملف الوثيقة (PDF أو صورة PNG/JPG)</label>
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
            استيراد مباشر للشهادات المعتمدة من وزارة التجارة لشركات المجموعة (BW, EX, TR) مع فحص البصمة والتطابق قبل الاعتماد.
          </p>

          {importError && (
            <div className="p-3.5 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-[var(--danger)] flex items-start gap-2">
              <AlertCircle size={16} className="shrink-0 mt-0.5" />
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
                    البصمة: {importPreviewPlan.fingerprint.slice(0, 12)}...
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
                سيتم تحديث الكيانات الثلاثة (BW, EX, TR) مع الأرقام الموحدة وتواريخ التأكيد السنوي وأنشطة ISIC المسجلة.
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
