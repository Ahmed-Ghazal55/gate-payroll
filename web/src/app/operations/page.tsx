'use client';

import React, { useState, useEffect, Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import {
  Truck,
  MapPin,
  Home,
  Car,
  PackageCheck,
  ShieldAlert,
  Scale,
  Smartphone,
  RefreshCw,
  Search,
  Filter,
  Download,
  AlertCircle,
  Clock,
  Layers,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';

const KIND_MAP: Record<string, string> = {
  cities: 'city',
  housing: 'housing',
  vehicles: 'vehicle',
  custody: 'custody_handover',
  guarantees: 'sponsorship_period',
  cases: 'legal_case',
  accounts: 'platform_account',
};

function OperationsPageInner() {
  const { toast } = useToast();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'cities';
  const [activeTab, setActiveTab] = useState<string>(tabFromUrl);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  const [loading, setLoading] = useState<boolean>(true);
  const [items, setItems] = useState<any[]>([]);
  const [total, setTotal] = useState<number>(0);
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(25);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [apiUnavailable, setApiUnavailable] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const loadTabData = async (tabKey: string, targetPage = page, query = searchQuery) => {
    const kind = KIND_MAP[tabKey] || tabKey;
    setLoading(true);
    setError(null);
    setApiUnavailable(false);
    try {
      const res = await api.getOperations(kind, {
        page: targetPage,
        pageSize,
        q: query.trim() || undefined,
      });
      const rows = res?.items || (Array.isArray(res) ? res : []);
      setItems(rows);
      setTotal(res?.total ?? rows.length);
    } catch (err: any) {
      if (err?.status === 404 || err?.message?.includes('404') || err?.message?.includes('غير موجود')) {
        setApiUnavailable(true);
      } else {
        setError(err?.message || 'تعذر استرجاع بيانات التشغيل');
      }
      setItems([]);
      setTotal(0);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setPage(1);
    loadTabData(activeTab, 1, searchQuery);
  }, [activeTab]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    loadTabData(activeTab, 1, searchQuery);
  };

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    loadTabData(activeTab, newPage, searchQuery);
  };

  const sectionTabs: SectionTabItem[] = [
    { id: 'cities', label: 'المدن والمناطق', icon: <MapPin size={16} /> },
    { id: 'housing', label: 'السكن والإيواء', icon: <Home size={16} /> },
    { id: 'vehicles', label: 'المركبات والتسليمات', icon: <Car size={16} /> },
    { id: 'custody', label: 'العهد الشهرية والأجهزة', icon: <PackageCheck size={16} /> },
    { id: 'guarantees', label: 'الكفالات والضمانات', icon: <ShieldAlert size={16} /> },
    { id: 'cases', label: 'القضايا والمنازعات', icon: <Scale size={16} /> },
    { id: 'accounts', label: 'حسابات المنصات والمعرفات', icon: <Smartphone size={16} /> },
  ];

  const currentTabInfo = {
    cities: { title: 'المدن التشغيلية والمناطق', desc: 'مواقع ومراكز التشغيل، نطاقات التوصيل، والمشرفين الميدانيين المسؤولين.' },
    housing: { title: 'السكن والإيواء الميداني', desc: 'إدارة وحدات الإيواء وسكن المناديب، العقود، وتوزيع التكاليف التشغيلية.' },
    vehicles: { title: 'أسطول المركبات والتسليمات', desc: 'السيارات والدراجات النارية، تسليمات العهد، رخص السير والتأمين، ومتابعة الصيانة.' },
    custody: { title: 'العهد الشهرية والأجهزة', desc: 'الأجهزة الذكية، الحقائب الحرارية، والزي الرسمي الموزع على الكوادر.' },
    guarantees: { title: 'الكفالات والضمانات المالية', desc: 'الكفالات الغارمة وسندات الضمان ضد المخاطر المالية والعهد.' },
    cases: { title: 'القضايا والمنازعات التشغيلية', desc: 'متابعة البلاغات المرورية، المطالبات القانونية، والنزاعات مع المنصات أو الأطراف الخارجية.' },
    accounts: { title: 'حسابات المنصات ومعرفات العمل', desc: 'معرفات المناديب على تطبيقات التوصيل، التبديل والتسليم بين المناديب، ومطابقة الهويات.' },
  }[activeTab] || { title: 'التشغيل والأصول', desc: 'إدارة العمليات التشغيلية والأصول الميدانية.' };

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="التشغيل والأصول الميدانية"
        eyebrow="العمليات واللوجستيات"
        description="إدارة أسطول المركبات، السكن والإيواء، تسليم واستلام العهد والأجهزة، حسابات المنصات، والقضايا والضمانات التشغيلية."
        primaryAction={{
          label: 'تحديث البيانات',
          icon: <RefreshCw size={15} />,
          onClick: () => loadTabData(activeTab, page, searchQuery),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="المدن والمناطق"
          value={activeTab === 'cities' ? total : 'مغطاة'}
          hint="الرياض، جدة، الدمام، ومراكز الفروع"
          icon={MapPin}
        />
        <StatCard
          label="أسطول المركبات"
          value={activeTab === 'vehicles' ? total : 'أسطول مسجل'}
          hint="سيارات ودراجات نارية مسجلة"
          icon={Car}
        />
        <StatCard
          label="وحدات السكن"
          value={activeTab === 'housing' ? total : 'سكن معتمد'}
          hint="إيواء الكوادر وكفالة الشركة"
          icon={Home}
        />
        <StatCard
          label="العهد والأجهزة"
          value={activeTab === 'custody' ? total : 'عهد موثقة'}
          hint="أجهزة ذكية وحقائب حرارية"
          icon={PackageCheck}
        />
      </div>

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(tabId) => setActiveTab(tabId)}
      />

      {/* Content Area */}
      <div className="space-y-4">
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center justify-between gap-3">
          <div>
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              {currentTabInfo.title}
            </h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              {currentTabInfo.desc}
            </p>
          </div>

          {/* Search bar */}
          <form onSubmit={handleSearch} className="flex items-center gap-2">
            <div className="relative">
              <Search size={15} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
              <input
                type="text"
                placeholder="بحث في السجلات..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden w-48 sm:w-64"
              />
            </div>
            <button
              type="submit"
              className="px-3 py-1.5 rounded-xl bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
            >
              بحث
            </button>
          </form>
        </div>

        {apiUnavailable ? (
          <div className="p-8 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-4 shadow-xs">
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] text-xs font-bold">
              <Clock size={15} />
              <span>غير متاح بعد — بانتظار اكتمال واجهات خادم التشغيل</span>
            </div>
            <h4 className="text-base font-bold text-[var(--ink)]">
              واجهة استرجاع {currentTabInfo.title} قيد التطوير
            </h4>
            <div className="pt-2">
              <button
                type="button"
                onClick={() => loadTabData(activeTab)}
                className="px-4 py-2 rounded-xl bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] text-xs font-bold transition inline-flex items-center gap-1.5 cursor-pointer"
              >
                <RefreshCw size={14} />
                <span>إعادة المحاولة والفحص</span>
              </button>
            </div>
          </div>
        ) : error ? (
          <ErrorState title="تعذر تحميل بيانات التشغيل" message={error} onRetry={() => loadTabData(activeTab, page, searchQuery)} />
        ) : loading ? (
          <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            title={`لا توجد سجلات حالية في ${currentTabInfo.title}`}
            description={searchQuery ? 'لم يتم العثور على نتائج تطابق معايير البحث.' : 'تظهر السجلات هنا فور تسجيلها أو استيرادها من مساحات التشغيل.'}
          />
        ) : (
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
            <div className="overflow-x-auto">
              <table className="w-full text-start text-xs sm:text-sm">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                  {activeTab === 'cities' && (
                    <tr>
                      <th className="p-3 text-start">رمز المدينة</th>
                      <th className="p-3 text-start">الاسم بالعربية</th>
                      <th className="p-3 text-start">الاسم بالإنجليزية</th>
                      <th className="p-3 text-start">المعرف الموحد</th>
                    </tr>
                  )}
                  {activeTab === 'housing' && (
                    <tr>
                      <th className="p-3 text-start">كود السكن</th>
                      <th className="p-3 text-start">اسم الوحدة</th>
                      <th className="p-3 text-start">المدينة</th>
                      <th className="p-3 text-start">عدد الغرف</th>
                      <th className="p-3 text-start">تاريخ الافتتاح</th>
                    </tr>
                  )}
                  {activeTab === 'vehicles' && (
                    <tr>
                      <th className="p-3 text-start">رقم اللوحة</th>
                      <th className="p-3 text-start">نوع المركبة</th>
                      <th className="p-3 text-start">سنة الموديل</th>
                      <th className="p-3 text-start">رقم الهيكل / VIN</th>
                      <th className="p-3 text-start">العداد الحالي</th>
                      <th className="p-3 text-start">الحالة التشغيلية</th>
                    </tr>
                  )}
                  {activeTab === 'custody' && (
                    <tr>
                      <th className="p-3 text-start">معرف التكليف</th>
                      <th className="p-3 text-start">نوع الحدث</th>
                      <th className="p-3 text-start">الاتجاه</th>
                      <th className="p-3 text-start">تاريخ التوثيق</th>
                    </tr>
                  )}
                  {activeTab === 'guarantees' && (
                    <tr>
                      <th className="p-3 text-start">إقامة المندوب</th>
                      <th className="p-3 text-start">السجل / الكفيل</th>
                      <th className="p-3 text-start">سريان من</th>
                      <th className="p-3 text-start">سريان إلى</th>
                    </tr>
                  )}
                  {activeTab === 'cases' && (
                    <tr>
                      <th className="p-3 text-start">رقم القضية</th>
                      <th className="p-3 text-start">اسم الطرف</th>
                      <th className="p-3 text-start">الصفة</th>
                      <th className="p-3 text-start">التاريخ</th>
                      <th className="p-3 text-start">التفاصيل</th>
                    </tr>
                  )}
                  {activeTab === 'accounts' && (
                    <tr>
                      <th className="p-3 text-start">كود الحساب</th>
                      <th className="p-3 text-start">معرف المنصة الخارجي</th>
                      <th className="p-3 text-start">المنصة</th>
                      <th className="p-3 text-start">المندوب المسجل</th>
                      <th className="p-3 text-start">المشغلون الفعليون</th>
                    </tr>
                  )}
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {items.map((it: any, idx: number) => (
                    <tr key={it.id || idx} className="hover:bg-[var(--row-hover)] transition">
                      {activeTab === 'cities' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--accent)]">{it.code || it.id}</td>
                          <td className="p-3 font-semibold">{it.nameAr || it.name || '-'}</td>
                          <td className="p-3 text-xs text-[var(--muted)] font-mono">{it.nameEn || '-'}</td>
                          <td className="p-3 font-mono text-xs">{it.globalCityId || '-'}</td>
                        </>
                      )}
                      {activeTab === 'housing' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--accent)]">{it.code || it.id}</td>
                          <td className="p-3 font-semibold">{it.nameAr || it.name || '-'}</td>
                          <td className="p-3 text-xs">{it.cityId || '-'}</td>
                          <td className="p-3 num">{it.rooms?.length ?? '-'}</td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{it.openedDate || '-'}</td>
                        </>
                      )}
                      {activeTab === 'vehicles' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--ink)]">
                            {it.plateNumberAr || it.plateNumberEn || it.assetNumber || it.id}
                          </td>
                          <td className="p-3">
                            <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                              {it.vehicleType === 'Car' || it.vehicleType === 'car' ? 'سيارة' : 'دباب'}
                            </span>
                          </td>
                          <td className="p-3 num">{it.modelYear || '-'}</td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{it.vin || it.chassisNumber || '-'}</td>
                          <td className="p-3 num font-medium">{it.currentOdometer ? `${it.currentOdometer} كم` : '-'}</td>
                          <td className="p-3">
                            <span
                              className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                                it.currentOperationalStatus === 'Active' || it.currentOperationalStatus === 'active'
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              }`}
                            >
                              {it.currentOperationalStatus || 'نشط'}
                            </span>
                          </td>
                        </>
                      )}
                      {activeTab === 'custody' && (
                        <>
                          <td className="p-3 font-mono font-bold">{it.assignmentId || it.id}</td>
                          <td className="p-3">{it.eventTypeCode || '-'}</td>
                          <td className="p-3 text-xs font-mono">{it.direction || '-'}</td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{it.occurredAt || '-'}</td>
                        </>
                      )}
                      {activeTab === 'guarantees' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--ink)]">{it.riderId}</td>
                          <td className="p-3 text-xs font-mono">{it.sponsorId || it.companyId || '-'}</td>
                          <td className="p-3 font-mono text-xs">{it.effectiveFrom || '-'}</td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{it.effectiveTo || 'مستمر'}</td>
                        </>
                      )}
                      {activeTab === 'cases' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--danger)]">{it.number || it.id}</td>
                          <td className="p-3 font-semibold">{it.personName || it.riderId || '-'}</td>
                          <td className="p-3 text-xs">{it.personTypeCode || '-'}</td>
                          <td className="p-3 font-mono text-xs">{it.caseDate || '-'}</td>
                          <td className="p-3 text-xs text-[var(--muted)] max-w-xs truncate">{it.details || '-'}</td>
                        </>
                      )}
                      {activeTab === 'accounts' && (
                        <>
                          <td className="p-3 font-mono font-bold text-[var(--accent)]">{it.code || it.id}</td>
                          <td className="p-3 font-mono">{it.externalAccountId || '-'}</td>
                          <td className="p-3 text-xs font-bold">{it.platformCode || '-'}</td>
                          <td className="p-3 font-mono text-xs">{it.registeredRiderId || '-'}</td>
                          <td className="p-3 num font-bold">
                            {it.assignments ? `${it.assignments.length} مشغل` : '-'}
                          </td>
                        </>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Pagination footer */}
            <div className="p-3 border-t border-[var(--line)] bg-[var(--surface)] flex items-center justify-between text-xs text-[var(--muted)]">
              <span>
                إجمالي {total} سجل · صفحة {page} من {totalPages}
              </span>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={page <= 1}
                  onClick={() => handlePageChange(page - 1)}
                  className="px-2.5 py-1 rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer inline-flex items-center gap-1"
                >
                  <ChevronRight size={14} />
                  <span>السابق</span>
                </button>
                <button
                  type="button"
                  disabled={page >= totalPages}
                  onClick={() => handlePageChange(page + 1)}
                  className="px-2.5 py-1 rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer inline-flex items-center gap-1"
                >
                  <span>التالي</span>
                  <ChevronLeft size={14} />
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default function OperationsPage() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <OperationsPageInner />
    </Suspense>
  );
}
