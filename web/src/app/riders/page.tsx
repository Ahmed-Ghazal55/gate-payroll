'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import {
  Users,
  UserCheck,
  Search,
  Filter,
  Download,
  Upload,
  RefreshCw,
  Sparkles,
  FileText,
  AlertCircle,
  CheckCircle2,
  Clock,
  Building2,
  Phone,
  CreditCard,
  MapPin,
  Car,
  Bike,
  ShieldAlert,
  ArrowRight,
  Eye,
  FileCheck,
  Calendar,
  DollarSign,
  ChevronRight,
  AlertTriangle,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import type {
  RiderRecord,
  RiderProfile360,
  RidersDataQuality,
  RidersBaselineSyncResult,
} from '../../lib/types';

function RidersPageInner() {
  const { user } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();

  // Tab state
  const tabFromUrl = searchParams?.get('tab') || 'directory';
  const [activeTab, setActiveTab] = useState<string>(tabFromUrl);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  // Directory state
  const [riders, setRiders] = useState<RiderRecord[]>([]);
  const [loadingRiders, setLoadingRiders] = useState<boolean>(true);
  const [ridersError, setRidersError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [sponsorFilter, setSponsorFilter] = useState<string>('all');

  // Profile 360 state
  const [profileIqama, setProfileIqama] = useState<string>(searchParams?.get('iqama') || '');
  const [profileData, setProfileData] = useState<RiderProfile360 | null>(null);
  const [loadingProfile, setLoadingProfile] = useState<boolean>(false);
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSubTab, setProfileSubTab] = useState<'documents' | 'accounts'>('documents');
  const [riderAccounts, setRiderAccounts] = useState<any[]>([]);
  const [loadingAccounts, setLoadingAccounts] = useState<boolean>(false);

  // Data Quality state
  const [qualityData, setQualityData] = useState<RidersDataQuality | null>(null);
  const [loadingQuality, setLoadingQuality] = useState<boolean>(false);
  const [qualityError, setQualityError] = useState<string | null>(null);
  const [enriching, setEnriching] = useState<boolean>(false);

  // Sync Baseline state
  const [syncPreview, setSyncPreview] = useState<RidersBaselineSyncResult | null>(null);
  const [syncLoading, setSyncLoading] = useState<boolean>(false);
  const [syncCommitting, setSyncCommitting] = useState<boolean>(false);
  const [syncError, setSyncError] = useState<string | null>(null);

  // Document upload dialog state
  const [uploadDialogOpen, setUploadDialogOpen] = useState<boolean>(false);
  const [uploadDocCode, setUploadDocCode] = useState<string>('national_id');
  const [uploadDocNumber, setUploadDocNumber] = useState<string>('');
  const [uploadIssueDate, setUploadIssueDate] = useState<string>('');
  const [uploadExpiryDate, setUploadExpiryDate] = useState<string>('');
  const [uploadFileName, setUploadFileName] = useState<string>('');
  const [uploadFileBase64, setUploadFileBase64] = useState<string>('');
  const [uploadingDoc, setUploadingDoc] = useState<boolean>(false);

  // Load Directory
  const loadDirectory = async () => {
    setLoadingRiders(true);
    setRidersError(null);
    try {
      const res = await api.getRiders();
      const items = Array.isArray(res) ? res : (res as any)?.items || [];
      setRiders(items);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل دليل المناديب';
      setRidersError(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingRiders(false);
    }
  };

  // Load Rider Accounts
  const loadRiderAccounts = async (targetIqama: string) => {
    setLoadingAccounts(true);
    try {
      const res = await api.getRiderAccounts(targetIqama);
      setRiderAccounts(res?.items || (Array.isArray(res) ? res : []));
    } catch (_) {
      setRiderAccounts([]);
    } finally {
      setLoadingAccounts(false);
    }
  };

  // Load Profile 360
  const loadProfile360 = async (iqamaToLoad?: string) => {
    const targetIqama = (iqamaToLoad || profileIqama).trim();
    if (!targetIqama) {
      toast('يرجى إدخال رقم إقامة المندوب للبحث', 'warning');
      return;
    }
    setLoadingProfile(true);
    setProfileError(null);
    try {
      const [data] = await Promise.all([
        api.getRiderProfile360(targetIqama),
        loadRiderAccounts(targetIqama),
      ]);
      setProfileData(data);
      setProfileIqama(targetIqama);
    } catch (err: any) {
      const msg = err.message || 'تعذر استرجاع ملف المندوب 360°';
      setProfileError(msg);
      toast(msg, 'danger');
      setProfileData(null);
    } finally {
      setLoadingProfile(false);
    }
  };

  // Load Data Quality
  const loadDataQuality = async () => {
    setLoadingQuality(true);
    setQualityError(null);
    try {
      const data = await api.getRidersDataQuality();
      setQualityData(data);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل تقرير جودة البيانات';
      setQualityError(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingQuality(false);
    }
  };

  // Initial load
  useEffect(() => {
    loadDirectory();
    loadDataQuality();
    const queryIqama = searchParams?.get('iqama');
    if (queryIqama) {
      loadProfile360(queryIqama);
    }
  }, []);

  // Enrich Missing Action
  const handleEnrichMissing = async () => {
    setEnriching(true);
    try {
      const res = await api.enrichRidersMissing();
      toast(
        `تم الاستكمال بنجاح: تم تحديث ${res.enrichedExisting} سجلاً وإضافة ${res.addedFromSources} مناديب جدد من مصادر التشغيل`,
        'success'
      );
      loadDirectory();
      loadDataQuality();
    } catch (err: any) {
      toast(err.message || 'تعذر استكمال الحقول المفقودة', 'danger');
    } finally {
      setEnriching(false);
    }
  };

  // Baseline Sync Preview
  const handleBaselinePreview = async () => {
    setSyncLoading(true);
    setSyncError(null);
    try {
      const res = await api.syncRidersBaseline({
        mode: 'preview',
        useLocalBaseline: true,
      });
      setSyncPreview(res);
      toast('تم إنشاء معاينة المزامنة بنجاح مع الدليل المرجعي', 'success');
    } catch (err: any) {
      const msg = err.message || 'تعذر معاينة المزامنة';
      setSyncError(msg);
      toast(msg, 'danger');
    } finally {
      setSyncLoading(false);
    }
  };

  // Baseline Sync Commit
  const handleBaselineCommit = async () => {
    if (!syncPreview?.fingerprint) return;
    setSyncCommitting(true);
    setSyncError(null);
    try {
      const res = await api.syncRidersBaseline({
        mode: 'commit',
        useLocalBaseline: true,
        fingerprint: syncPreview.fingerprint,
      });
      toast(`تم اعتماد المزامنة بنجاح: ${res.count} سجلات جديدة و ${res.updatedCount} تحديثات`, 'success');
      setSyncPreview(null);
      loadDirectory();
      loadDataQuality();
    } catch (err: any) {
      const msg = err.message || 'تعذر اعتماد المزامنة';
      setSyncError(msg);
      toast(msg, 'danger');
    } finally {
      setSyncCommitting(false);
    }
  };

  // Document Upload
  const handleUploadDocument = async () => {
    if (!profileData?.rider?.iqama) return;
    if (!uploadDocCode) {
      toast('يرجى تحديد نوع الوثيقة', 'warning');
      return;
    }
    setUploadingDoc(true);
    try {
      await api.uploadRiderDocument(profileData.rider.iqama, {
        documentTypeCode: uploadDocCode,
        documentNumber: uploadDocNumber.trim() || undefined,
        issueDate: uploadIssueDate || undefined,
        expiryDate: uploadExpiryDate || undefined,
        fileName: uploadFileName || undefined,
        dataBase64: uploadFileBase64 || undefined,
      });
      toast('تم حفظ الوثيقة الرسمية وتحديث ملف المندوب بنجاح', 'success');
      setUploadDialogOpen(false);
      setUploadFileBase64('');
      setUploadFileName('');
      loadProfile360(profileData.rider.iqama);
    } catch (err: any) {
      toast(err.message || 'تعذر حفظ الوثيقة', 'danger');
    } finally {
      setUploadingDoc(false);
    }
  };

  // Handle File Input
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploadFileName(file.name);
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      const base64 = result.split(',')[1] || '';
      setUploadFileBase64(base64);
    };
    reader.readAsDataURL(file);
  };

  // Filtered Riders
  const filteredRiders = useMemo(() => {
    return riders.filter((r) => {
      if (statusFilter !== 'all' && r.status !== statusFilter) return false;
      if (sponsorFilter !== 'all' && r.sponsor !== sponsorFilter && r.sponsorId !== sponsorFilter) return false;
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchIqama = r.iqama?.includes(q);
        const matchName = r.name?.toLowerCase().includes(q);
        const matchPhone = r.phone?.includes(q);
        if (!matchIqama && !matchName && !matchPhone) return false;
      }
      return true;
    });
  }, [riders, statusFilter, sponsorFilter, searchQuery]);

  // Unique sponsors
  const sponsorsList = useMemo(() => {
    const set = new Set<string>();
    riders.forEach((r) => {
      if (r.sponsor) set.add(r.sponsor);
    });
    return Array.from(set);
  }, [riders]);

  // Tab definitions
  const sectionTabs: SectionTabItem[] = [
    { id: 'directory', label: 'دليل المناديب', icon: <Users size={16} /> },
    { id: '360', label: 'ملف المندوب 360°', icon: <UserCheck size={16} /> },
    { id: 'quality', label: 'جودة واكتمال البيانات', icon: <Sparkles size={16} />, badge: qualityData?.needsReviewCount },
    { id: 'sync', label: 'المزامنة والدليل المرجعي', icon: <RefreshCw size={16} /> },
  ];

  // Helper for masking Iqama
  const maskIqama = (iqama: string) => {
    if (!iqama || iqama.length < 8) return iqama;
    return `${iqama.slice(0, 2)}****${iqama.slice(-4)}`;
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <PageHeader
        title="المناديب وسجلات التشغيل"
        eyebrow="إدارة الكوادر والتشغيل"
        description="دليل المناديب الموحد، ملف المندوب الشامل 360°، متابعة اكتمال وجودة البيانات، والمزامنة المعتمدة مع ملفات الدليل المرجعي."
        primaryAction={{
          label: 'تحديث الدليل',
          icon: <RefreshCw size={15} />,
          onClick: loadDirectory,
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي المناديب المسجلين"
          value={riders.length}
          hint={`${filteredRiders.length} مندوب يطابق الفلترة الحالية`}
          icon={Users}
        />
        <StatCard
          label="مناديب كفالة الشركة"
          value={riders.filter((r) => r.engagementType === 'SponsoredInternal').length}
          hint="إقامات على كفالة كيانات البوابة"
          icon={Building2}
        />
        <StatCard
          label="مناديب خارجيون (Outside)"
          value={riders.filter((r) => r.engagementType === 'External').length}
          hint="تشغيل بدون كفالة مباشرة"
          icon={Car}
        />
        <StatCard
          label="سجلات بحاجة لمراجعة"
          value={qualityData?.needsReviewCount || riders.filter((r) => r.status === 'NeedsReview').length}
          hint="بيانات غير مكتملة أو بانتظار التدقيق"
          icon={ShieldAlert}
        />
      </div>

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(tabId) => setActiveTab(tabId)}
      />

      {/* Sub-tab 1: Directory */}
      {activeTab === 'directory' && (
        <div className="space-y-4">
          {/* Toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex flex-wrap items-center gap-2 flex-1 min-w-[240px]">
              <div className="relative flex-1 max-w-sm">
                <Search size={15} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
                <input
                  type="text"
                  placeholder="بحث بالإقامة، الاسم أو الجوال..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm focus:outline-hidden"
                />
              </div>

              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-xl px-3 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل الحالات</option>
                <option value="Active">نشط (Active)</option>
                <option value="NeedsReview">بحاجة لمراجعة (NeedsReview)</option>
                <option value="Inactive">غير نشط (Inactive)</option>
              </select>

              <select
                value={sponsorFilter}
                onChange={(e) => setSponsorFilter(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-xl px-3 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل الكفلاء</option>
                {sponsorsList.map((sp) => (
                  <option key={sp} value={sp}>
                    {sp}
                  </option>
                ))}
              </select>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-xs text-[var(--muted)]">
                {filteredRiders.length} من {riders.length} مندوب
              </span>
            </div>
          </div>

          {/* Table */}
          {ridersError ? (
            <ErrorState title="تعذر تحميل دليل المناديب" message={ridersError} onRetry={loadDirectory} />
          ) : loadingRiders ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : filteredRiders.length === 0 ? (
            <EmptyState
              title="لم يتم العثور على مناديب"
              description="لا توجد سجلات مناديب تطابق معايير البحث الحالية."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم الإقامة</th>
                      <th className="p-3 text-start">اسم المندوب</th>
                      <th className="p-3 text-start">الكفيل / الشركة</th>
                      <th className="p-3 text-start">نوع العلاقة</th>
                      <th className="p-3 text-start">المركبة</th>
                      <th className="p-3 text-start">المدينة</th>
                      <th className="p-3 text-start">المنصات</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {filteredRiders.map((r) => (
                      <tr key={r.id || r.iqama} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--ink)]">
                          {maskIqama(r.iqama || r.id)}
                        </td>
                        <td className="p-3 font-semibold">{r.name}</td>
                        <td className="p-3 text-[var(--muted)]">{r.sponsor || '-'}</td>
                        <td className="p-3">
                          <span
                            className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                              r.engagementType === 'SponsoredInternal'
                                ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
                                : 'bg-[var(--raised)] text-[var(--muted)]'
                            }`}
                          >
                            {r.engagementType === 'SponsoredInternal' ? 'كفالة الشركة' : 'خارجي'}
                          </span>
                        </td>
                        <td className="p-3">
                          <span className="inline-flex items-center gap-1">
                            {r.operationalRole === 'Car' ? (
                              <Car size={14} className="text-[var(--accent)]" />
                            ) : (
                              <Bike size={14} className="text-[var(--muted)]" />
                            )}
                            <span>{r.operationalRole === 'Car' ? 'سيارة' : 'دباب'}</span>
                          </span>
                        </td>
                        <td className="p-3">{r.city || '-'}</td>
                        <td className="p-3 text-xs text-[var(--muted)] truncate max-w-[140px]">
                          {r.platforms || '-'}
                        </td>
                        <td className="p-3">
                          <span
                            className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                              r.status === 'Active'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : r.status === 'NeedsReview'
                                ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                : 'bg-[var(--muted)]/15 text-[var(--muted)]'
                            }`}
                          >
                            {r.status === 'Active' ? 'نشط' : r.status === 'NeedsReview' ? 'بحاجة لمراجعة' : 'غير نشط'}
                          </span>
                        </td>
                        <td className="p-3">
                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              onClick={() => {
                                setProfileIqama(r.iqama || r.id);
                                setActiveTab('360');
                                loadProfile360(r.iqama || r.id);
                              }}
                              className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--accent)] hover:text-[var(--accent-ink)] font-bold text-xs transition cursor-pointer"
                            >
                              الملف 360°
                            </button>
                            <Link
                              href={`/riders/statement/?iqama=${encodeURIComponent(r.iqama || r.id)}`}
                              className="px-2.5 py-1 rounded-lg bg-[var(--surface)] border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--accent)] text-xs font-medium transition"
                            >
                              كشف الحساب
                            </Link>
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

      {/* Sub-tab 2: Profile 360 */}
      {activeTab === '360' && (
        <div className="space-y-6">
          {/* Search bar */}
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center gap-3">
            <div className="relative flex-1 max-w-md">
              <Search size={16} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
              <input
                type="text"
                placeholder="أدخل رقم الإقامة للبحث واستعراض الملف الشامل..."
                value={profileIqama}
                onChange={(e) => setProfileIqama(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && loadProfile360()}
                className="w-full ps-9 pe-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-sm font-mono focus:outline-hidden"
              />
            </div>
            <button
              type="button"
              disabled={loadingProfile}
              onClick={() => loadProfile360()}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {loadingProfile ? 'جاري التحميل...' : 'عرض الملف 360°'}
            </button>
          </div>

          {/* Profile details */}
          {profileError ? (
            <ErrorState title="تعذر استرجاع ملف المندوب" message={profileError} onRetry={() => loadProfile360()} />
          ) : loadingProfile ? (
            <div className="p-8 space-y-4 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-24 w-full" />
              <Skeleton className="h-48 w-full" />
            </div>
          ) : !profileData ? (
            <EmptyState
              title="ابحث برقم الإقامة لاستعراض الملف 360°"
              description="يعرض الملف كافة بيانات المندوب، الوثائق الرسمية، المديونيات القائمة، وسجل الاستجابات والطلبات."
            />
          ) : (
            <div className="space-y-6">
              {/* Header card */}
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="flex items-center gap-4">
                    <div className="h-14 w-14 rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] flex items-center justify-center font-bold text-2xl shadow-xs">
                      {profileData.rider.name?.charAt(0) || 'م'}
                    </div>
                    <div>
                      <h2 className="text-lg sm:text-xl font-bold text-[var(--ink)]">
                        {profileData.rider.name}
                      </h2>
                      <div className="flex flex-wrap items-center gap-3 text-xs text-[var(--muted)] mt-1 font-mono">
                        <span>رقم الإقامة: {profileData.rider.iqama}</span>
                        <span>•</span>
                        <span>الجوال: {profileData.rider.phone || 'غير مسجل'}</span>
                        <span>•</span>
                        <span>المدينة: {profileData.rider.city || 'غير محددة'}</span>
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center gap-2">
                    <Link
                      href={`/riders/statement/?iqama=${encodeURIComponent(profileData.rider.iqama)}`}
                      className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5"
                    >
                      <FileText size={15} />
                      <span>كشف الحساب المالي</span>
                    </Link>
                    <button
                      type="button"
                      onClick={() => setUploadDialogOpen(true)}
                      className="px-3.5 py-2 rounded-xl bg-[var(--raised)] text-[var(--ink)] text-xs font-bold hover:bg-[var(--line)] transition inline-flex items-center gap-1.5 cursor-pointer"
                    >
                      <Upload size={15} />
                      <span>إضافة وثيقة رسمية</span>
                    </button>
                  </div>
                </div>

                {/* KPI metrics row */}
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mt-6 pt-5 border-t border-[var(--line)]">
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)] font-medium">المديونيات القائمة</div>
                    <div className="text-base font-bold text-[var(--ink)] mt-0.5">
                      {profileData.activeDebtsCount} مديونية
                    </div>
                  </div>
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)] font-medium">إجمالي المتبقي المالي</div>
                    <div className="text-base font-bold num text-[var(--danger)] mt-0.5">
                      {profileData.totalRemainingDebt.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                    </div>
                  </div>
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)] font-medium">الوثائق الرسمية</div>
                    <div className="text-base font-bold text-[var(--ink)] mt-0.5">
                      {profileData.documentsCount} مرفوعة
                    </div>
                  </div>
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)] font-medium">إجمالي طلبات التشغيل</div>
                    <div className="text-base font-bold text-[var(--ink)] mt-0.5">
                      {profileData.totalOrders} طلب
                    </div>
                  </div>
                </div>
              </div>

              {/* Sub-tabs inside Profile 360 */}
              <div className="flex items-center gap-2 border-b border-[var(--line)]">
                <button
                  type="button"
                  onClick={() => setProfileSubTab('documents')}
                  className={`py-2 px-3.5 text-xs font-semibold border-b-2 transition cursor-pointer ${
                    profileSubTab === 'documents'
                      ? 'border-[var(--accent)] text-[var(--accent)]'
                      : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  الوثائق والمستندات الرسمية
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setProfileSubTab('accounts');
                    if (profileData?.rider?.iqama) {
                      loadRiderAccounts(profileData.rider.iqama);
                    }
                  }}
                  className={`py-2 px-3.5 text-xs font-semibold border-b-2 transition cursor-pointer ${
                    profileSubTab === 'accounts'
                      ? 'border-[var(--accent)] text-[var(--accent)]'
                      : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
                  }`}
                >
                  حسابات المنصات والمعرفات ({riderAccounts.length})
                </button>
              </div>

              {/* View 1: Documents Checklist */}
              {profileSubTab === 'documents' && (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                <div className="p-4 border-b border-[var(--line)] flex items-center justify-between">
                  <div>
                    <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                      قائمة التحقق من الوثائق الرسمية والمستندات
                    </h3>
                    <p className="text-xs text-[var(--muted)] mt-0.5">
                      مطابقة متطلبات الإقامة، رخصة القيادة، بطاقة السائق، الكرت الصحي، وعقد أجير.
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setUploadDialogOpen(true)}
                    className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
                  >
                    + رفع مستند
                  </button>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs sm:text-sm">
                    <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                      <tr>
                        <th className="p-3 text-start">نوع المستند</th>
                        <th className="p-3 text-start">رقم المستند</th>
                        <th className="p-3 text-start">تاريخ الإصدار</th>
                        <th className="p-3 text-start">تاريخ الانتهاء</th>
                        <th className="p-3 text-start">الملف المرفق</th>
                        <th className="p-3 text-start">الحالة</th>
                        <th className="p-3 text-start">الإجراء</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {profileData.documentChecklist?.map((doc) => (
                        <tr key={doc.code || doc.documentTypeId} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-semibold">{doc.nameAr}</td>
                          <td className="p-3 font-mono">{doc.documentNumber || '-'}</td>
                          <td className="p-3 num text-xs">{doc.issueDate || '-'}</td>
                          <td className="p-3 num text-xs">{doc.expiryDate || '-'}</td>
                          <td className="p-3 text-xs text-[var(--muted)]">
                            {doc.isUploaded ? (
                              <span className="text-[var(--ink)] font-mono truncate max-w-[150px] inline-block">
                                {doc.fileName || 'مستند مرفق'}
                              </span>
                            ) : (
                              <span className="text-[var(--danger)]">غير مرفوع</span>
                            )}
                          </td>
                          <td className="p-3">
                            <span
                              className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
                                doc.isUploaded
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                              }`}
                            >
                              {doc.isUploaded ? 'مكتمل' : 'ناقص'}
                            </span>
                          </td>
                          <td className="p-3">
                            <button
                              type="button"
                              onClick={() => {
                                setUploadDocCode(doc.code);
                                setUploadDialogOpen(true);
                              }}
                              className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--accent)] hover:text-[var(--accent-ink)] transition cursor-pointer"
                            >
                              {doc.isUploaded ? 'تحديث' : 'رفع الآن'}
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
              )}

              {/* View 2: Platform Accounts (F3) */}
              {profileSubTab === 'accounts' && (
                <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                  <div className="p-4 border-b border-[var(--line)]">
                    <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                      حسابات وتكليفات المنصات الميدانية (GET /api/riders/{'{iqama}'}/accounts)
                    </h3>
                    <p className="text-xs text-[var(--muted)] mt-0.5">
                      مطابقة معرفات العمل وتكليفات التشغيل الفعلي مع تواريخ الاستلام والتسليم.
                    </p>
                  </div>

                  {loadingAccounts ? (
                    <div className="p-6 space-y-2">
                      <Skeleton className="h-8 w-full" />
                      <Skeleton className="h-8 w-full" />
                    </div>
                  ) : riderAccounts.length === 0 ? (
                    <div className="p-8 text-center text-xs text-[var(--muted)]">
                      لا توجد حسابات منصات مرتبطة بهذا المندوب حالياً.
                    </div>
                  ) : (
                    <div className="overflow-x-auto">
                      <table className="w-full text-start text-xs sm:text-sm">
                        <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                          <tr>
                            <th className="p-3 text-start">كود الحساب</th>
                            <th className="p-3 text-start">معرف المنصة الخارجي</th>
                            <th className="p-3 text-start">تطبيق المنصة</th>
                            <th className="p-3 text-start">المدينة</th>
                            <th className="p-3 text-start">العلاقة</th>
                            <th className="p-3 text-start">فترات التكليف الفعلي</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-[var(--line)]">
                          {riderAccounts.map((acc: any) => (
                            <tr key={acc.id || acc.code} className="hover:bg-[var(--row-hover)] transition">
                              <td className="p-3 font-mono font-bold text-[var(--accent)]">{acc.code || acc.id}</td>
                              <td className="p-3 font-mono font-bold">{acc.externalAccountId || '-'}</td>
                              <td className="p-3 font-bold">{acc.platformCode || '-'}</td>
                              <td className="p-3 text-xs">{acc.cityId || '-'}</td>
                              <td className="p-3">
                                <span className="px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                                  {acc.relationship === 'actual_operator' ? 'مشغل فعلي' : acc.relationship || 'مشغل'}
                                </span>
                              </td>
                              <td className="p-3 text-xs font-mono text-[var(--muted)]">
                                {acc.assignments?.map((a: any, i: number) => (
                                  <div key={i}>
                                    {a.RawEffectiveFrom?.slice(0, 10) || a.EffectiveFrom?.slice(0, 10) || 'بداية'} ➔ {a.RawEffectiveTo?.slice(0, 10) || a.EffectiveTo?.slice(0, 10) || 'مستمر'}
                                  </div>
                                )) || '-'}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 3: Data Quality */}
      {activeTab === 'quality' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
            <div>
              <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                تشخيص اكتمال وجودة بيانات المناديب
              </h3>
              <p className="text-xs sm:text-sm text-[var(--muted)] mt-1">
                فحص النواقص بالحقول الجوهرية (الجوال، المدينة، الكفيل، الآيبان، والسياسة) مع استكمالها آلياً من مصادر التشغيل وردود الاستبيانات.
              </p>
            </div>
            <button
              type="button"
              disabled={enriching}
              onClick={handleEnrichMissing}
              className="px-4 py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 inline-flex items-center gap-2 cursor-pointer shadow-xs"
            >
              <Sparkles size={16} />
              <span>{enriching ? 'جاري الاستكمال الآلي...' : 'استكمال الحقول المفقودة آلياً'}</span>
            </button>
          </div>

          {qualityError ? (
            <ErrorState title="تعذر تحميل مؤشرات الجودة" message={qualityError} onRetry={loadDataQuality} />
          ) : loadingQuality ? (
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <Skeleton className="h-28 rounded-2xl" />
              <Skeleton className="h-28 rounded-2xl" />
              <Skeleton className="h-28 rounded-2xl" />
            </div>
          ) : qualityData ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">سجلات مفقودة الجوال</div>
                <div className="text-2xl font-bold num text-[var(--danger)] mt-1">
                  {qualityData.missingPhoneCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  يؤثر على التواصل والإشعارات
                </div>
              </div>

              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">سجلات مفقودة المدينة</div>
                <div className="text-2xl font-bold num text-[var(--warning)] mt-1">
                  {qualityData.missingCityCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  ضرورية لتقارير التشغيل الإقليمية
                </div>
              </div>

              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">سجلات مفقودة الكفيل</div>
                <div className="text-2xl font-bold num text-[var(--warning)] mt-1">
                  {qualityData.missingSponsorCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  يؤثر على مطابقة السجلات التجارية
                </div>
              </div>

              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">سجلات مفقودة الآيبان البنكي (IBAN)</div>
                <div className="text-2xl font-bold num text-[var(--danger)] mt-1">
                  {qualityData.missingIbanCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  يمنع إصدار ملف صرف الرواتب البنكي
                </div>
              </div>

              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">سجلات مفقودة السياسة المالية</div>
                <div className="text-2xl font-bold num text-[var(--muted)] mt-1">
                  {qualityData.missingPolicyCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  تعتمد السياسة الافتراضية
                </div>
              </div>

              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
                <div className="text-xs text-[var(--muted)] font-medium">إجمالي السجلات بحاجة لمراجعة</div>
                <div className="text-2xl font-bold num text-[var(--accent)] mt-1">
                  {qualityData.needsReviewCount}
                </div>
                <div className="text-[11px] text-[var(--muted)] mt-2">
                  من إجمالي {qualityData.totalRiders} مندوب
                </div>
              </div>
            </div>
          ) : null}
        </div>
      )}

      {/* Sub-tab 4: Sync Baseline */}
      {activeTab === 'sync' && (
        <div className="space-y-6">
          <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              مزامنة الدليل الخاص مع الملف المرجعي (Baseline Sync)
            </h3>
            <p className="text-xs sm:text-sm text-[var(--muted)] mt-1 leading-relaxed">
              تتيح هذه الشاشة مطابقة المناديب المسجلين محلياً مع الدليل المرجعي (data/riders-baseline.json)، فحص التعارضات، إضافة المناديب الجدد وتحديث الحقول المكتملة عبر توقيع البصمة (Fingerprint).
            </p>

            <div className="flex items-center gap-3 mt-4 pt-4 border-t border-[var(--line)]">
              <button
                type="button"
                disabled={syncLoading}
                onClick={handleBaselinePreview}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 inline-flex items-center gap-2 cursor-pointer shadow-xs"
              >
                <RefreshCw size={15} />
                <span>{syncLoading ? 'جاري الفحص والمعاينة...' : 'معاينة مزامنة الدليل المرجعي'}</span>
              </button>
            </div>
          </div>

          {syncError && (
            <div className="p-4 rounded-2xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)] text-xs font-bold">
              {syncError}
            </div>
          )}

          {syncPreview && (
            <div className="p-5 rounded-2xl border border-[var(--accent)]/30 bg-[var(--surface)] shadow-xs space-y-4">
              <div className="flex items-center justify-between">
                <h4 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                  نتيجة معاينة المزامنة مع الدليل
                </h4>
                <span className="font-mono text-xs text-[var(--muted)]">
                  بصمة الحالة: {syncPreview.fingerprint?.slice(0, 16)}...
                </span>
              </div>

              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">إجمالي السجلات المفحوصة</div>
                  <div className="text-lg font-bold num mt-0.5">{syncPreview.total || 0}</div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">مناديب جدد للإضافة</div>
                  <div className="text-lg font-bold num text-[var(--money-pos)] mt-0.5">
                    {syncPreview.addCount || 0}
                  </div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">سجلات بحاجة للتحديث</div>
                  <div className="text-lg font-bold num text-[var(--accent)] mt-0.5">
                    {syncPreview.updateCount || 0}
                  </div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">التعارضات المكتشفة</div>
                  <div className="text-lg font-bold num text-[var(--danger)] mt-0.5">
                    {syncPreview.conflicts?.length || 0}
                  </div>
                </div>
              </div>

              <div className="pt-3 border-t border-[var(--line)] flex justify-end">
                <button
                  type="button"
                  disabled={syncCommitting}
                  onClick={handleBaselineCommit}
                  className="px-5 py-2.5 rounded-xl bg-[var(--money-pos)] text-white text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {syncCommitting ? 'جاري الاعتماد...' : 'اعتماد وحفظ المزامنة في قاعدة البيانات'}
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Upload Document Dialog */}
      <Dialog
        open={uploadDialogOpen}
        onClose={() => setUploadDialogOpen(false)}
        title="رفع وتحديث وثيقة رسمية للمندوب"
      >
        <div className="space-y-4 pt-2">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">نوع الوثيقة الرسمية:</label>
            <select
              value={uploadDocCode}
              onChange={(e) => setUploadDocCode(e.target.value)}
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            >
              <option value="national_id">الهوية الوطنية / الإقامة (National ID / Iqama)</option>
              <option value="driving_license">رخصة القيادة (Driving License)</option>
              <option value="driver_card">بطاقة سائق هيئة النقل TGA (Driver Card)</option>
              <option value="health_certificate">الكرت الصحي (Health Certificate)</option>
              <option value="ajeer_contract">عقد إشعار أجير (Ajeer Contract)</option>
              <option value="iban_certificate">شهادة الآيبان البنكي (Bank IBAN Certificate)</option>
            </select>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">رقم الوثيقة:</label>
              <input
                type="text"
                placeholder="أدخل رقم الوثيقة..."
                value={uploadDocNumber}
                onChange={(e) => setUploadDocNumber(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">تاريخ الانتهاء:</label>
              <input
                type="date"
                value={uploadExpiryDate}
                onChange={(e) => setUploadExpiryDate(e.target.value)}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">الملف المرفق (PDF أو صور):</label>
            <input
              type="file"
              accept=".pdf,.png,.jpg,.jpeg"
              onChange={handleFileChange}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden cursor-pointer"
            />
            {uploadFileName && (
              <p className="text-[11px] text-[var(--accent)] font-medium mt-1 truncate">
                الملف المحدد: {uploadFileName}
              </p>
            )}
          </div>

          <div className="pt-3 border-t border-[var(--line)] flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setUploadDialogOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={uploadingDoc}
              onClick={handleUploadDocument}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer"
            >
              {uploadingDoc ? 'جاري الرفع...' : 'حفظ الوثيقة'}
            </button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

export default function RidersPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <RidersPageInner />
    </Suspense>
  );
}
