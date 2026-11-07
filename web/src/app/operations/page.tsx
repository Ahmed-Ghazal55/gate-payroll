'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
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
import { DataTable, ColumnDef } from '../../components/ui/DataTable';

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

  const tabColumns: ColumnDef<any>[] = useMemo(() => {
    switch (activeTab) {
      case 'cities':
        return [
          {
            key: 'code',
            header: 'رمز المدينة',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold text-[var(--accent)]">{it.code || it.id}</span>,
          },
          {
            key: 'nameAr',
            header: 'الاسم بالعربية',
            filterable: true,
            render: (it: any) => <span className="font-semibold">{it.nameAr || it.name || '-'}</span>,
          },
          {
            key: 'nameEn',
            header: 'الاسم بالإنجليزية',
            filterable: true,
            render: (it: any) => <span className="text-xs text-[var(--muted)] font-mono">{it.nameEn || '-'}</span>,
          },
          {
            key: 'globalCityId',
            header: 'المعرف الموحد',
            render: (it: any) => <span className="font-mono text-xs">{it.globalCityId || '-'}</span>,
          },
        ];
      case 'housing':
        return [
          {
            key: 'code',
            header: 'كود السكن',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold text-[var(--accent)]">{it.code || it.id}</span>,
          },
          {
            key: 'nameAr',
            header: 'اسم الوحدة',
            filterable: true,
            render: (it: any) => <span className="font-semibold">{it.nameAr || it.name || '-'}</span>,
          },
          {
            key: 'cityId',
            header: 'المدينة',
            render: (it: any) => <span className="text-xs">{it.cityId || '-'}</span>,
          },
          {
            key: 'rooms',
            header: 'عدد الغرف',
            numeric: true,
            render: (it: any) => <span className="num">{it.rooms?.length ?? '-'}</span>,
          },
          {
            key: 'openedDate',
            header: 'تاريخ الافتتاح',
            render: (it: any) => <span className="font-mono text-xs text-[var(--muted)]">{it.openedDate || '-'}</span>,
          },
        ];
      case 'vehicles':
        return [
          {
            key: 'plateNumber',
            header: 'رقم اللوحة',
            filterable: true,
            render: (it: any) => (
              <span className="font-mono font-bold text-[var(--ink)]">
                {it.plateNumberAr || it.plateNumberEn || it.assetNumber || it.id}
              </span>
            ),
          },
          {
            key: 'vehicleType',
            header: 'نوع المركبة',
            render: (it: any) => (
              <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                {it.vehicleType === 'Car' || it.vehicleType === 'car' ? 'سيارة' : 'دباب'}
              </span>
            ),
          },
          {
            key: 'modelYear',
            header: 'سنة الموديل',
            numeric: true,
            render: (it: any) => <span className="num">{it.modelYear || '-'}</span>,
          },
          {
            key: 'vin',
            header: 'رقم الهيكل / VIN',
            render: (it: any) => <span className="font-mono text-xs text-[var(--muted)]">{it.vin || it.chassisNumber || '-'}</span>,
          },
          {
            key: 'currentOdometer',
            header: 'العداد الحالي',
            numeric: true,
            render: (it: any) => <span className="num font-medium">{it.currentOdometer ? `${it.currentOdometer} كم` : '-'}</span>,
          },
          {
            key: 'currentOperationalStatus',
            header: 'الحالة التشغيلية',
            render: (it: any) => (
              <span
                className={`px-2 py-0.5 rounded-full text-xs font-bold ${
                  it.currentOperationalStatus === 'Active' || it.currentOperationalStatus === 'active'
                    ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                    : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                }`}
              >
                {it.currentOperationalStatus || 'نشط'}
              </span>
            ),
          },
        ];
      case 'custody':
        return [
          {
            key: 'assignmentId',
            header: 'معرف التكليف',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold">{it.assignmentId || it.id}</span>,
          },
          {
            key: 'eventTypeCode',
            header: 'نوع الحدث',
            render: (it: any) => <span>{it.eventTypeCode || '-'}</span>,
          },
          {
            key: 'direction',
            header: 'الاتجاه',
            render: (it: any) => <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">{it.direction || '-'}</span>,
          },
          {
            key: 'occurredAt',
            header: 'تاريخ التوثيق',
            render: (it: any) => <span className="font-mono text-xs text-[var(--muted)]">{it.occurredAt || '-'}</span>,
          },
        ];
      case 'guarantees':
        return [
          {
            key: 'riderId',
            header: 'إقامة المندوب',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold text-[var(--ink)]">{it.riderId}</span>,
          },
          {
            key: 'sponsorId',
            header: 'السجل / الكفيل',
            filterable: true,
            render: (it: any) => <span className="text-xs font-mono">{it.sponsorId || it.companyId || '-'}</span>,
          },
          {
            key: 'effectiveFrom',
            header: 'سريان من',
            render: (it: any) => <span className="font-mono text-xs">{it.effectiveFrom || '-'}</span>,
          },
          {
            key: 'effectiveTo',
            header: 'سريان إلى',
            render: (it: any) => <span className="font-mono text-xs text-[var(--muted)]">{it.effectiveTo || 'مستمر'}</span>,
          },
        ];
      case 'cases':
        return [
          {
            key: 'number',
            header: 'رقم القضية',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold text-[var(--danger)]">{it.number || it.id}</span>,
          },
          {
            key: 'personName',
            header: 'اسم الطرف',
            filterable: true,
            render: (it: any) => <span className="font-semibold">{it.personName || it.riderId || '-'}</span>,
          },
          {
            key: 'personTypeCode',
            header: 'الصفة',
            render: (it: any) => <span>{it.personTypeCode || '-'}</span>,
          },
          {
            key: 'caseDate',
            header: 'التاريخ',
            render: (it: any) => <span className="font-mono text-xs">{it.caseDate || '-'}</span>,
          },
          {
            key: 'details',
            header: 'التفاصيل',
            render: (it: any) => <span className="text-xs text-[var(--muted)] truncate max-w-xs block">{it.details || '-'}</span>,
          },
        ];
      case 'accounts':
      default:
        return [
          {
            key: 'code',
            header: 'كود الحساب',
            filterable: true,
            render: (it: any) => <span className="font-mono font-bold text-[var(--accent)]">{it.code || it.id}</span>,
          },
          {
            key: 'externalAccountId',
            header: 'معرف المنصة الخارجي',
            render: (it: any) => <span className="font-mono text-xs">{it.externalAccountId || '-'}</span>,
          },
          {
            key: 'platformCode',
            header: 'المنصة',
            filterable: true,
            render: (it: any) => <span className="text-xs font-bold">{it.platformCode || '-'}</span>,
          },
          {
            key: 'registeredRiderId',
            header: 'المندوب المسجل',
            render: (it: any) => <span className="font-mono text-xs">{it.registeredRiderId || '-'}</span>,
          },
          {
            key: 'assignments',
            header: 'المشغلون الفعليون',
            render: (it: any) => <span className="num font-bold">{it.assignments ? `${it.assignments.length} مشغل` : '-'}</span>,
          },
        ];
    }
  }, [activeTab]);

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
          active={activeTab === 'cities'}
          onClick={() => setActiveTab('cities')}
          loading={loading && activeTab === 'cities'}
        />
        <StatCard
          label="أسطول المركبات"
          value={activeTab === 'vehicles' ? total : 'أسطول مسجل'}
          hint="سيارات ودراجات نارية مسجلة"
          icon={Car}
          active={activeTab === 'vehicles'}
          onClick={() => setActiveTab('vehicles')}
          loading={loading && activeTab === 'vehicles'}
        />
        <StatCard
          label="وحدات السكن"
          value={activeTab === 'housing' ? total : 'سكن معتمد'}
          hint="إيواء الكوادر وكفالة الشركة"
          icon={Home}
          active={activeTab === 'housing'}
          onClick={() => setActiveTab('housing')}
          loading={loading && activeTab === 'housing'}
        />
        <StatCard
          label="العهد والأجهزة"
          value={activeTab === 'custody' ? total : 'عهد موثقة'}
          hint="أجهزة ذكية وحقائب حرارية"
          icon={PackageCheck}
          active={activeTab === 'custody'}
          onClick={() => setActiveTab('custody')}
          loading={loading && activeTab === 'custody'}
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
        ) : (
          <DataTable
            tableId={`operations-${activeTab}`}
            columns={tabColumns}
            data={items}
            loading={loading}
            emptyTitle={`لا توجد سجلات حالية في ${currentTabInfo.title}`}
            emptyDescription={
              searchQuery
                ? 'لم يتم العثور على نتائج تطابق معايير البحث.'
                : 'تظهر السجلات هنا فور تسجيلها أو استيرادها من مساحات التشغيل.'
            }
            exportFilename={`operations_${activeTab}`}
            showTotals={true}
          />
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
