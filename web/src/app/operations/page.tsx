'use client';

import React, { useState, useEffect, useMemo, useCallback, useRef, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import {
  MapPin,
  Home,
  Car,
  PackageCheck,
  ShieldAlert,
  Scale,
  Smartphone,
  RefreshCw,
  Search,
  X,
  Upload,
  AlertTriangle,
  Clock,
  Layers,
  CheckCircle2,
} from 'lucide-react';
import { api, OperationsResponse } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { SourceBadge, formatRiyadhDateTime } from '../../components/ui/SourceBadge';

const KIND_MAP: Record<string, string> = {
  cities: 'city',
  housing: 'housing',
  vehicles: 'vehicle',
  custody: 'custody_handover',
  guarantees: 'sponsorship_period',
  cases: 'legal_case',
  accounts: 'platform_account',
};

const PAGE_SIZES = [25, 50, 100, 200];

function OperationsPageInner() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'cities';
  const pageFromUrl = parseInt(searchParams?.get('page') || '1', 10);
  const sizeFromUrl = parseInt(searchParams?.get('size') || '25', 10);
  const qFromUrl = searchParams?.get('q') || '';

  const [activeTab, setActiveTab] = useState<string>(tabFromUrl);
  const [page, setPage] = useState<number>(pageFromUrl > 0 ? pageFromUrl : 1);
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZES.includes(sizeFromUrl) ? sizeFromUrl : 25);
  const [searchQuery, setSearchQuery] = useState<string>(qFromUrl);
  const [debouncedQuery, setDebouncedQuery] = useState<string>(qFromUrl);

  const [items, setItems] = useState<any[]>([]);
  const [total, setTotal] = useState<number>(0);
  const [source, setSource] = useState<string>('');
  const [asOf, setAsOf] = useState<string>('');
  const [allowedActions, setAllowedActions] = useState<string[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [is503, setIs503] = useState<boolean>(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  // Sync tab change from URL
  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
      setPage(1);
    }
  }, [tabFromUrl]);

  // Debounce typing (300 ms)
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedQuery(searchQuery);
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  // Sync URL search params
  const updateUrl = useCallback(
    (newTab: string, newPage: number, newSize: number, newQ: string) => {
      if (typeof window === 'undefined') return;
      const sp = new URLSearchParams();
      if (newTab && newTab !== 'cities') sp.set('tab', newTab);
      if (newPage > 1) sp.set('page', String(newPage));
      if (newSize !== 25) sp.set('size', String(newSize));
      if (newQ.trim()) sp.set('q', newQ.trim());
      const qs = sp.toString() ? `?${sp.toString()}` : '';
      router.replace(`${pathname}${qs}`, { scroll: false });
    },
    [router, pathname]
  );

  // Fetch operations data from server
  const loadTabData = useCallback(
    async (
      targetTab: string,
      targetPage: number,
      targetPageSize: number,
      targetQuery: string
    ) => {
      const kind = KIND_MAP[targetTab] || targetTab;
      setLoading(true);
      setErrorMsg(null);
      setIs503(false);

      try {
        const res: OperationsResponse = await api.getOperations(kind, {
          page: targetPage,
          pageSize: targetPageSize,
          q: targetQuery.trim() || undefined,
        });

        const rows = res?.items || (Array.isArray(res) ? res : []);
        setItems(rows);
        setTotal(res?.total ?? rows.length);
        setSource(res?.source || '');
        setAsOf(res?.asOf || '');
        setAllowedActions(res?.allowedActions || []);
        setPage(res?.page ?? targetPage);
        setPageSize(res?.pageSize ?? targetPageSize);
      } catch (err: any) {
        const status = err?.status;
        const msg = err?.message || 'تعذر استرجاع بيانات التشغيل';

        if (status === 503 || msg.includes('503') || msg.includes('مصدر التشغيل')) {
          setIs503(true);
          setErrorMsg(msg);
          // 4. Errors: 503 -> keeping the last page shown!
          // Do NOT clear items if we already have data
        } else {
          setErrorMsg(msg);
          setItems([]);
          setTotal(0);
        }
      } finally {
        setLoading(false);
      }
    },
    []
  );

  // Track initial mount
  const isFirstMount = useRef(true);

  // Primary data-fetching trigger on tab or debounced query change
  useEffect(() => {
    if (isFirstMount.current) {
      isFirstMount.current = false;
      loadTabData(activeTab, page, pageSize, debouncedQuery);
      return;
    }
    // Reset to page 1 on search or tab change
    setPage(1);
    updateUrl(activeTab, 1, pageSize, debouncedQuery);
    loadTabData(activeTab, 1, pageSize, debouncedQuery);
  }, [activeTab, debouncedQuery]);

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    updateUrl(activeTab, newPage, pageSize, debouncedQuery);
    loadTabData(activeTab, newPage, pageSize, debouncedQuery);
  };

  const handlePageSizeChange = (newSize: number) => {
    setPageSize(newSize);
    setPage(1);
    updateUrl(activeTab, 1, newSize, debouncedQuery);
    loadTabData(activeTab, 1, newSize, debouncedQuery);
  };

  const handleTabChange = (tabId: string) => {
    setActiveTab(tabId);
    setSearchQuery('');
    setDebouncedQuery('');
    setPage(1);
    updateUrl(tabId, 1, pageSize, '');
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

  const currentTabInfo = useMemo(() => {
    switch (activeTab) {
      case 'cities':
        return {
          title: 'المدن التشغيلية والمناطق',
          shortLabel: 'المدن والمناطق',
          desc: 'مواقع ومراكز التشغيل، نطاقات التوصيل، والمشرفين الميدانيين المسؤولين.',
          icon: MapPin,
        };
      case 'housing':
        return {
          title: 'السكن والإيواء الميداني',
          shortLabel: 'وحدات السكن',
          desc: 'إدارة وحدات الإيواء وسكن المناديب، العقود، وتوزيع التكاليف التشغيلية.',
          icon: Home,
        };
      case 'vehicles':
        return {
          title: 'أسطول المركبات والتسليمات',
          shortLabel: 'أسطول المركبات',
          desc: 'السيارات والدراجات النارية، تسليمات العهد، رخص السير والتأمين، ومتابعة الصيانة.',
          icon: Car,
        };
      case 'custody':
        return {
          title: 'العهد الشهرية والأجهزة',
          shortLabel: 'العهد والأجهزة',
          desc: 'الأجهزة الذكية، الحقائب الحرارية، والزي الرسمي الموزع على الكوادر.',
          icon: PackageCheck,
        };
      case 'guarantees':
        return {
          title: 'الكفالات والضمانات المالية',
          shortLabel: 'الكفالات والضمانات',
          desc: 'الكفالات الغارمة وسندات الضمان ضد المخاطر المالية والعهد.',
          icon: ShieldAlert,
        };
      case 'cases':
        return {
          title: 'القضايا والمنازعات التشغيلية',
          shortLabel: 'القضايا والمنازعات',
          desc: 'متابعة البلاغات المرورية، المطالبات القانونية، والنزاعات مع المنصات أو الأطراف الخارجية.',
          icon: Scale,
        };
      case 'accounts':
      default:
        return {
          title: 'حسابات المنصات ومعرفات العمل',
          shortLabel: 'حسابات المنصات',
          desc: 'معرفات المناديب على تطبيقات التوصيل، التبديل والتسليم بين المناديب، ومطابقة الهويات.',
          icon: Smartphone,
        };
    }
  }, [activeTab]);

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  const tabColumns: ColumnDef<any>[] = useMemo(() => {
    switch (activeTab) {
      case 'cities':
        return [
          {
            key: 'code',
            header: 'رمز المدينة',
            render: (it: any) => <span className="font-mono font-bold text-[var(--accent)]">{it.code || it.id}</span>,
          },
          {
            key: 'nameAr',
            header: 'الاسم بالعربية',
            render: (it: any) => <span className="font-semibold">{it.nameAr || it.name || '-'}</span>,
          },
          {
            key: 'nameEn',
            header: 'الاسم بالإنجليزية',
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
            render: (it: any) => <span className="font-mono font-bold text-[var(--accent)]">{it.code || it.id}</span>,
          },
          {
            key: 'nameAr',
            header: 'اسم الوحدة',
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
            render: (it: any) => <span className="font-mono font-bold text-[var(--ink)]">{it.riderId}</span>,
          },
          {
            key: 'sponsorId',
            header: 'السجل / الكفيل',
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
            render: (it: any) => <span className="font-mono font-bold text-[var(--danger)]">{it.number || it.id}</span>,
          },
          {
            key: 'personName',
            header: 'اسم الطرف',
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
      {/* Page Header with Source Badge and Import Center Link */}
      <PageHeader
        title="التشغيل والأصول الميدانية"
        eyebrow="العمليات واللوجستيات"
        description="إدارة أسطول المركبات، السكن والإيواء، تسليم واستلام العهد والأجهزة، حسابات المنصات، والقضايا والضمانات التشغيلية."
        primaryAction={{
          label: 'تحديث البيانات',
          icon: <RefreshCw size={15} className={loading ? 'animate-spin' : ''} />,
          onClick: () => loadTabData(activeTab, page, pageSize, debouncedQuery),
        }}
      >
        <div className="flex flex-wrap items-center gap-2 mt-3 pt-3 border-t border-[var(--line)]">
          {source && <SourceBadge source={source} asOf={asOf} />}

          <Link
            href="/imports/"
            className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] transition"
          >
            <Upload size={13} />
            <span>مركز الاستيراد</span>
          </Link>
        </div>
      </PageHeader>

      {/* 6. Stat Cards: Using total of the active tab only (no fake counts for other tabs) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label={`إجمالي سجلات ${currentTabInfo.shortLabel}`}
          value={loading ? '...' : total}
          hint="إجمالي السجلات المطابقة في مصدر التشغيل"
          icon={currentTabInfo.icon}
          active={true}
          loading={loading}
        />
        <StatCard
          label="السجلات المعروضة"
          value={loading ? '...' : items.length}
          hint={`الصفحة ${page} من أصل ${totalPages} (بحجم ${pageSize})`}
          icon={Layers}
          loading={loading}
        />
        <StatCard
          label="مصدر البيانات"
          value={source === 'erp_live' ? 'نظام الموارد المباشر' : source === 'store' ? 'بيانات تجريبية محلية' : 'بانتظار المصدر'}
          hint={source === 'erp_live' ? 'قراءة مباشرة حية للقراءة فقط' : 'مخزن التطوير التجريبي'}
          icon={CheckCircle2}
          loading={loading && !source}
        />
        <StatCard
          label="توقيت آخر قراءة"
          value={asOf ? (formatRiyadhDateTime(asOf).split('،')[1]?.trim() || 'محدث') : '-'}
          hint={asOf ? `بتاريخ ${formatRiyadhDateTime(asOf).split('،')[0]?.trim()} بتوقيت الرياض` : 'بانتظار القراءة'}
          icon={Clock}
          loading={loading && !asOf}
        />
      </div>

      {/* Section Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={handleTabChange}
      />

      {/* Content Area */}
      <div className="space-y-4">
        {/* Tab Toolbar: Title, Source Badge, and Server Search with 300ms Debounce */}
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center justify-between gap-3 shadow-2xs">
          <div>
            <div className="flex items-center gap-2">
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                {currentTabInfo.title}
              </h3>
            </div>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              {currentTabInfo.desc}
            </p>
          </div>

          {/* 1. Server Search Input (Debounced 300 ms) */}
          <div className="flex items-center gap-2">
            <div className="relative">
              <Search size={14} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)] pointer-events-none" />
              <input
                type="text"
                placeholder="بحث في سجلات التشغيل..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="ps-9 pe-8 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden w-52 sm:w-72 transition"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  aria-label="مسح البحث"
                  className="absolute end-2.5 top-1/2 -translate-y-1/2 text-[var(--muted)] hover:text-[var(--ink)] cursor-pointer"
                >
                  <X size={13} />
                </button>
              )}
            </div>
          </div>
        </div>

        {/* 4. Errors: 503 Warning Banner when keeping last page shown */}
        {is503 && items.length > 0 && (
          <div
            role="alert"
            className="p-4 rounded-2xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 flex flex-wrap items-center justify-between gap-3 shadow-xs"
          >
            <div className="flex items-center gap-2.5 text-xs text-[var(--warning)] font-bold">
              <AlertTriangle size={18} className="shrink-0" />
              <div>
                <span className="font-extrabold block text-sm">مصدر التشغيل غير متاح الآن</span>
                <span className="font-normal text-[var(--muted)]">
                  تم الإبقاء على آخر صفحة قراءة معروضة أدناه ({errorMsg || 'تعذر الاتصال بقاعدة بيانات التشغيل'})
                </span>
              </div>
            </div>
            <button
              type="button"
              onClick={() => loadTabData(activeTab, page, pageSize, debouncedQuery)}
              className="px-3.5 py-1.5 rounded-xl bg-[var(--warning)] text-[var(--ink)] font-bold text-xs hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <RefreshCw size={13} />
              <span>إعادة المحاولة</span>
            </button>
          </div>
        )}

        {/* 4. Errors: 503 Full Card when no data was loaded yet */}
        {is503 && items.length === 0 ? (
          <div
            role="alert"
            className="p-8 rounded-2xl border border-[var(--warning)]/30 bg-[var(--surface)] text-center space-y-4 shadow-xs"
          >
            <div className="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] text-xs font-bold">
              <AlertTriangle size={16} />
              <span>مصدر التشغيل غير متاح الآن</span>
            </div>
            <h4 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              تعذر الاتصال بمصدر التشغيل اللوجستي
            </h4>
            <p className="text-xs sm:text-sm text-[var(--muted)] max-w-md mx-auto">
              {errorMsg || 'نظام الموارد الميداني أو اتصال قاعدة بيانات التشغيل غير متاح حالياً. يرجى إعادة المحاولة.'}
            </p>
            <div className="pt-2">
              <button
                type="button"
                onClick={() => loadTabData(activeTab, page, pageSize, debouncedQuery)}
                className="px-5 py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 text-xs sm:text-sm font-bold transition inline-flex items-center gap-2 cursor-pointer shadow-xs"
              >
                <RefreshCw size={14} />
                <span>إعادة المحاولة</span>
              </button>
            </div>
          </div>
        ) : errorMsg && !is503 ? (
          <ErrorState
            title="تعذر تحميل بيانات التشغيل"
            message={errorMsg}
            onRetry={() => loadTabData(activeTab, page, pageSize, debouncedQuery)}
          />
        ) : (
          /* 1. Server Paging and Search: DataTable in serverSide mode */
          <DataTable
            tableId={`operations-${activeTab}`}
            columns={tabColumns}
            data={items}
            totalCount={total}
            loading={loading}
            page={page}
            pageSize={pageSize}
            pageSizes={PAGE_SIZES}
            onPageChange={handlePageChange}
            onPageSizeChange={handlePageSizeChange}
            serverSide={true}
            searchable={false}
            emptyTitle={`لا توجد سجلات حالية في ${currentTabInfo.title}`}
            emptyDescription={
              debouncedQuery
                ? 'لم يتم العثور على نتائج تطابق معايير البحث في مصدر التشغيل.'
                : 'تظهر السجلات هنا فور توفرها من مصدر التشغيل اللوجستي.'
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
