'use client';

import React, { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Car, Fuel, Home, RefreshCw, Search } from 'lucide-react';
import {
  api,
  ApiError,
  OperationsFleetListResponse,
  OperationsFuelResponse,
  OperationsHousingResponse,
  OperationsPeriodResponse,
} from '../../lib/api';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { SourceBadge } from '../../components/ui/SourceBadge';
import { Drawer } from '../../components/ui/Drawer';
import { useToast } from '../../components/ui/Toast';

const MAIN_TABS = new Set(['cities', 'housing', 'vehicles', 'fuel']);
const NO_SOURCE = 'لا يوجد مصدر بعد';

function monthBounds(d = new Date()) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const last = new Date(y, d.getMonth() + 1, 0).getDate();
  return { from: `${y}-${m}-01`, to: `${y}-${m}-${String(last).padStart(2, '0')}`, month: `${y}-${m}` };
}

function downloadBlob(res: Response, fallback: string) {
  return res.blob().then((blob) => {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fallback;
    a.click();
    URL.revokeObjectURL(url);
  });
}

function OperationsPageInner() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { toast } = useToast();

  const rawTab = searchParams?.get('tab') || 'cities';
  useEffect(() => {
    if (rawTab === 'cases') {
      router.replace('/workforce/?tab=cases');
      return;
    }
    if (rawTab === 'accounts') {
      router.replace('/workforce/');
      return;
    }
    if (rawTab === 'custody' || rawTab === 'guarantees') {
      router.replace('/operations/?tab=vehicles');
      return;
    }
    if (!MAIN_TABS.has(rawTab)) {
      router.replace('/operations/?tab=cities');
    }
  }, [rawTab, router]);

  const tab = MAIN_TABS.has(rawTab) ? rawTab : 'cities';
  const view = searchParams?.get('view') || (tab === 'vehicles' ? 'period' : tab === 'fuel' ? 'usage' : '');
  const page = Math.max(1, parseInt(searchParams?.get('page') || '1', 10) || 1);
  const pageSize = [25, 50, 100, 200].includes(parseInt(searchParams?.get('size') || '25', 10))
    ? parseInt(searchParams?.get('size') || '25', 10)
    : 25;
  const q = searchParams?.get('q') || '';
  const mode = (searchParams?.get('mode') === 'rider' ? 'rider' : 'vehicle') as 'vehicle' | 'rider';
  const defaults = monthBounds();
  const from = searchParams?.get('from') || defaults.from;
  const to = searchParams?.get('to') || defaults.to;
  const month = searchParams?.get('month') || defaults.month;

  const [query, setQuery] = useState(q);
  const [debouncedQ, setDebouncedQ] = useState(q);
  const [loading, setLoading] = useState(true);
  const [is503, setIs503] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [source, setSource] = useState('');
  const [asOf, setAsOf] = useState('');
  const [allowedActions, setAllowedActions] = useState<string[]>([]);
  const [items, setItems] = useState<Record<string, unknown>[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState<Record<string, unknown>>({});
  const [drawer, setDrawer] = useState<{ title: string; body: React.ReactNode } | null>(null);

  useEffect(() => {
    const t = setTimeout(() => setDebouncedQ(query), 300);
    return () => clearTimeout(t);
  }, [query]);

  const replaceParams = useCallback(
    (patch: Record<string, string | null>) => {
      const sp = new URLSearchParams(searchParams?.toString() || '');
      Object.entries(patch).forEach(([k, v]) => {
        if (v === null || v === '') sp.delete(k);
        else sp.set(k, v);
      });
      if (sp.get('tab') === 'cities') sp.delete('tab');
      const qs = sp.toString();
      router.replace(`${pathname}${qs ? `?${qs}` : ''}`, { scroll: false });
    },
    [router, pathname, searchParams]
  );

  useEffect(() => {
    if (debouncedQ !== q) replaceParams({ q: debouncedQ || null, page: '1' });
  }, [debouncedQ]); // eslint-disable-line react-hooks/exhaustive-deps

  const load = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    setIs503(false);
    try {
      if (tab === 'cities') {
        const res = (await api.getOperationsCities({ q: debouncedQ || undefined })) as OperationsFleetListResponse;
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'housing') {
        const res = (await api.getOperationsHousing({
          page,
          pageSize,
          q: debouncedQ || undefined,
        })) as OperationsHousingResponse;
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'invoices') {
        const res = await api.getOperationsPurchaseInvoices({ from, to, page, pageSize, q: debouncedQ || undefined });
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'oil') {
        setItems([]);
        setTotal(0);
        setSummary({});
        setSource('erp_live');
        setAsOf(new Date().toISOString());
        setAllowedActions(['refresh']);
        setErrorMsg(null);
      } else if (tab === 'vehicles') {
        const res = (await api.getOperationsPeriodReport({
          from,
          to,
          mode,
          page,
          pageSize,
          q: debouncedQ || undefined,
        })) as OperationsPeriodResponse;
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'fuel' && view === 'log') {
        const res = await api.getOperationsFuelAssignments({ from, to, page, pageSize });
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else {
        const res = (await api.getOperationsFuelCards({
          month,
          page,
          pageSize,
          q: debouncedQ || undefined,
        })) as OperationsFuelResponse;
        setItems((res.items || []) as Record<string, unknown>[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      }
    } catch (e) {
      const err = e as ApiError;
      if (err?.status === 503) setIs503(true);
      setErrorMsg(err?.message || 'تعذر تحميل بيانات التشغيل.');
    } finally {
      setLoading(false);
    }
  }, [tab, view, page, pageSize, debouncedQ, from, to, mode, month]);

  useEffect(() => {
    void load();
  }, [load]);

  const canExport = allowedActions.includes('export');
  const canRefresh = allowedActions.includes('refresh');

  const openCity = async (id: string) => {
    try {
      const detail = await api.getOperationsCity(id);
      setDrawer({
        title: String(detail.nameAr || 'تفاصيل المدينة'),
        body: (
          <div className="space-y-4 text-sm">
            <pre className="whitespace-pre-wrap break-words bg-[var(--surface-2)] p-3 rounded-lg overflow-auto max-h-[60vh]">
              {JSON.stringify(detail, null, 2)}
            </pre>
          </div>
        ),
      });
    } catch (e) {
      toast((e as ApiError).message || 'تعذر فتح التفاصيل', 'danger');
    }
  };

  const openHousing = async (id: string) => {
    try {
      const detail = await api.getOperationsHousingRooms(id);
      const rooms = (detail.rooms as Array<Record<string, unknown>>) || [];
      setDrawer({
        title: String(detail.nameAr || 'الغرف والمقيمون'),
        body: (
          <div className="space-y-3">
            <p className="text-sm text-[var(--muted)]">المشرف: {NO_SOURCE}</p>
            {rooms.length === 0 ? (
              <EmptyState title="لا غرف" description="لا توجد غرف لهذا السكن." />
            ) : (
              rooms.map((room) => (
                <div key={String(room.id)} className="border border-[var(--border)] rounded-lg p-3">
                  <div className="font-medium">{String(room.name)}</div>
                  <div className="text-xs text-[var(--muted)] mb-2">
                    سعة {String(room.capacity)} · إشغال {String(room.currentOccupancy)}
                  </div>
                  <ul className="space-y-1 text-sm">
                    {((room.residents as Array<Record<string, unknown>>) || []).map((r, i) => (
                      <li key={i} className="flex flex-col sm:flex-row sm:gap-3">
                        <span>{String(r.nameAr || '')}</span>
                        <bdi className="font-mono">{String(r.iqama || '')}</bdi>
                        <span className="text-[var(--muted)]">{String(r.checkInDate || '')}</span>
                        {r.employeeId ? (
                          <a className="text-[var(--accent)]" href={`/workforce/?tab=directory&focus=${encodeURIComponent(String(r.employeeId))}`}>
                            ملف 360
                          </a>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                </div>
              ))
            )}
          </div>
        ),
      });
    } catch (e) {
      toast((e as ApiError).message || 'تعذر فتح الغرف', 'danger');
    }
  };

  const openVehicle = async (id: string) => {
    try {
      const detail = await api.getOperationsVehicle(id);
      setDrawer({
        title: String(detail.plateNumberAr || detail.assetNumber || 'تفاصيل المركبة'),
        body: (
          <div className="space-y-2 text-sm">
            <div>VIN: <bdi className="font-mono">{String(detail.vin || '')}</bdi></div>
            <div>الهيكل: {String(detail.chassisNumber || '')}</div>
            <div>العداد: {String(detail.currentOdometer ?? '')}</div>
            <div>الكفيل: {String(detail.sponsorNameAr || '')}</div>
            <div>المدينة: {String(detail.city || '')}</div>
            <pre className="whitespace-pre-wrap break-words bg-[var(--surface-2)] p-3 rounded-lg overflow-auto max-h-[40vh] text-xs">
              {JSON.stringify(detail.assignmentHistory || [], null, 2)}
            </pre>
          </div>
        ),
      });
    } catch (e) {
      toast((e as ApiError).message || 'تعذر فتح المركبة', 'danger');
    }
  };

  const vehicleInnerTabs: SectionTabItem[] = [
    { id: 'period', label: 'تقرير فترة العهد والتعيينات' },
    { id: 'oil', label: 'تغيير الزيت والاستهلاك' },
    { id: 'invoices', label: 'فواتير مشتريات المركبات' },
  ];
  const fuelInnerTabs: SectionTabItem[] = [
    { id: 'usage', label: 'الاستهلاك الشهري' },
    { id: 'cards', label: 'بطاقات الوقود والتسليمات' },
    { id: 'log', label: 'سجل البطاقات' },
  ];

  const title =
    tab === 'cities' ? 'المدن والمناطق' : tab === 'housing' ? 'السكن والإسكان' : tab === 'vehicles' ? 'المركبات والتسليمات' : 'الوقود';

  const kpi = useMemo(() => {
    if (tab === 'cities') {
      return [
        { label: 'عدد المدن النشطة', value: summary.activeCities },
        { label: 'إجمالي المركبات', value: summary.totalVehicles },
        { label: 'إجمالي المناديب', value: summary.totalRiders },
        { label: 'حسابات (نشط / مسند)', value: `${summary.platformAccountsActive ?? 0} / ${summary.platformAccountsAssigned ?? 0}` },
      ];
    }
    if (tab === 'housing') {
      return [
        { label: 'إجمالي السكنات', value: summary.housingsTotal },
        { label: 'إجمالي الغرف', value: summary.roomsTotal },
        { label: 'إجمالي المقيمين', value: summary.residentsTotal },
        { label: 'نسبة الإشغال %', value: summary.occupancyPercent },
      ];
    }
    if (tab === 'vehicles') {
      return [
        { label: 'المركبات العاملة', value: summary.vehiclesOperating ?? summary.invoicesTotal },
        { label: 'إجمالي أيام التشغيل', value: summary.totalOperatingDays },
        { label: 'تكلفة قطع الغيار', value: summary.sparePartsCost ?? summary.totalAmount },
        { label: 'الاستقطاع المطلوب', value: summary.amountToCollect },
        { label: 'تكلفة تغييرات الزيت', value: summary.oilChangesCost },
      ];
    }
    return [
      { label: 'البطاقات النشطة', value: summary.activeCards },
      { label: 'المناديب المستهلكون', value: summary.consumingRiders },
      { label: 'إجمالي اللترات', value: summary.totalLiters },
      { label: 'الإجمالي شامل الضريبة', value: summary.totalInclVat },
      { label: 'تجاوز السقف', value: summary.capExceeded ?? NO_SOURCE },
    ];
  }, [tab, summary]);

  const columns: ColumnDef<Record<string, unknown>>[] = useMemo(() => {
    if (tab === 'cities') {
      return [
        { key: 'nameAr', header: 'المدينة', render: (r) => String(r.nameAr || '') },
        { key: 'regs', header: 'السجلات النشطة', render: (r) => String(r.registrationNames || r.activeRegistrations || '') },
        { key: 'veh', header: 'المركبات (سيارة/دراجة)', render: (r) => `${r.vehiclesCars ?? 0} / ${r.vehiclesMotorbikes ?? 0}` },
        { key: 'riders', header: 'المناديب', render: (r) => String(r.ridersTotal ?? '') },
        { key: 'apps', header: 'التطبيقات العاملة', render: (r) => String(r.appsWorking || '') },
        { key: 'acc', header: 'الحسابات', render: (r) => `${r.accountsTotal ?? 0} / ${r.accountsActive ?? 0} / ${r.accountsAssigned ?? 0}` },
        {
          key: 'actions',
          header: 'إجراءات',
          render: (r) => (
            <button type="button" className="text-[var(--accent)]" onClick={() => void openCity(String(r.id))}>
              تفاصيل المدينة
            </button>
          ),
        },
      ];
    }
    if (tab === 'housing') {
      return [
        { key: 'nameAr', header: 'اسم السكن', render: (r) => String(r.nameAr || '') },
        { key: 'city', header: 'المدينة', render: (r) => String(r.city || '') },
        { key: 'rooms', header: 'عدد الغرف', render: (r) => String(r.roomsCount ?? '') },
        { key: 'res', header: 'إجمالي المناديب', render: (r) => String(r.residentsTotal ?? '') },
        { key: 'per', header: 'مندوب/غرفة', render: (r) => String(r.ridersPerRoom ?? '') },
        { key: 'sup', header: 'المشرف', render: () => NO_SOURCE },
        { key: 'status', header: 'الحالة', render: (r) => String(r.status || '') },
        {
          key: 'actions',
          header: 'إجراءات',
          render: (r) => (
            <button type="button" className="text-[var(--accent)]" onClick={() => void openHousing(String(r.id))}>
              عرض الغرف والمقيمين
            </button>
          ),
        },
      ];
    }
    if (tab === 'fuel') {
      if (view === 'log') {
        return [
          { key: 'card', header: 'البطاقة', render: (r) => String(r.cardNumber || '') },
          { key: 'provider', header: 'المزود', render: (r) => String(r.provider || '') },
          { key: 'rider', header: 'المندوب', render: (r) => String(r.riderNameAr || '') },
          { key: 'from', header: 'من', render: (r) => String(r.effectiveFrom || '') },
          { key: 'to', header: 'إلى', render: (r) => String(r.effectiveTo || '') },
        ];
      }
      return [
        { key: 'card', header: 'رقم البطاقة والمزود', render: (r) => `${r.cardNumber || ''} · ${r.provider || ''}` },
        { key: 'rider', header: 'المندوب الفعلي', render: (r) => String(r.riderNameAr || '') },
        { key: 'plate', header: 'المركبة', render: (r) => String(r.plateNumber || '') },
        { key: 'liters', header: 'اللترات', render: (r) => String(r.liters ?? '') },
        { key: 'before', header: 'قبل الضريبة', render: (r) => String(r.amountBeforeTax ?? '') },
        { key: 'vat', header: 'الضريبة', render: (r) => String(r.vat ?? '') },
        { key: 'total', header: 'الإجمالي', render: (r) => String(r.totalInclVat ?? '') },
        { key: 'cap', header: 'السقف', render: () => NO_SOURCE },
        { key: 'status', header: 'الحالة', render: (r) => String(r.status || '') },
      ];
    }
    if (view === 'invoices') {
      return [
        { key: 'num', header: 'رقم الاستلام', render: (r) => String(r.receiptNumber || '') },
        { key: 'sup', header: 'المورد', render: (r) => String(r.supplierNameAr || '') },
        { key: 'date', header: 'تاريخ الفاتورة', render: (r) => String(r.invoiceDate || '') },
        { key: 'total', header: 'الإجمالي', render: (r) => String(r.totalAmount ?? '') },
        { key: 'status', header: 'الحالة', render: (r) => String(r.status || '') },
      ];
    }
    if (view === 'oil') {
      return [{ key: 'hint', header: 'ملاحظة', render: () => 'افتح تفاصيل مركبة من تقرير الفترة لعرض تغييرات الزيت.' }];
    }
    return [
      { key: 'plate', header: 'اللوحة والنوع', render: (r) => `${r.plateNumberAr || ''} · ${r.vehicleType || ''}` },
      { key: 'rider', header: mode === 'rider' ? 'المندوب الفعلي' : 'المندوب / الفترة', render: (r) => String(r.riderNameAr || '') },
      { key: 'city', header: 'المدينة', render: (r) => String(r.city || '') },
      { key: 'days', header: 'أيام التشغيل', render: (r) => String(r.operatingDays ?? '') },
      { key: 'parts', header: 'تكلفة قطع الغيار', render: (r) => String(r.sparePartsCost ?? '') },
      { key: 'collect', header: 'الاستقطاع المطلوب', render: (r) => String(r.amountToCollect ?? '') },
      { key: 'oil', header: 'زيت (عدد/تكلفة/كم)', render: (r) => `${r.oilChangesCount ?? 0} / ${r.oilChangesCost ?? 0} / ${r.oilMileage ?? '—'}` },
      {
        key: 'actions',
        header: 'إجراءات',
        render: (r) => (
          <button type="button" className="text-[var(--accent)]" onClick={() => void openVehicle(String(r.vehicleId || r.id))}>
            تفاصيل المركبة
          </button>
        ),
      },
    ];
  }, [tab, view, mode]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="space-y-4 max-w-full overflow-x-hidden">
      <PageHeader
        title={title}
        description="عرض مباشر من نظام الموارد — بدون ترحيل أو مدفوعات من هذه الشاشة."
      >
        {source ? <SourceBadge source={source === 'erp_live' ? 'erp_live' : 'store'} asOf={asOf || undefined} /> : null}
      </PageHeader>

      {tab === 'fuel' ? (
        <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] px-4 py-3 text-sm">
          عرض مباشر فقط من نظام الموارد — الاستهلاك يُدخل في نظام عمر.
        </div>
      ) : null}

      {tab === 'vehicles' ? (
        <SectionTabs
          tabs={vehicleInnerTabs}
          activeTab={view || 'period'}
          paramName="view"
          onChange={(id) => replaceParams({ view: id, page: '1' })}
        />
      ) : null}
      {tab === 'fuel' ? (
        <SectionTabs
          tabs={fuelInnerTabs}
          activeTab={view || 'usage'}
          paramName="view"
          onChange={(id) => replaceParams({ view: id, page: '1' })}
        />
      ) : null}

      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3">
        {kpi.map((k) => (
          <StatCard key={k.label} label={k.label} value={k.value == null ? '—' : String(k.value)} />
        ))}
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-end">
        <label className="flex-1 min-w-[12rem]">
          <span className="sr-only">بحث</span>
          <div className="relative">
            <Search size={16} className="absolute right-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
            <input
              className="w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] pr-9 pl-3 py-2"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="بحث…"
            />
          </div>
        </label>
        {(tab === 'vehicles' || tab === 'fuel') && (
          <>
            <label className="text-sm">
              من
              <input type="date" className="block mt-1 rounded-lg border border-[var(--border)] px-2 py-2" value={from} onChange={(e) => replaceParams({ from: e.target.value, page: '1' })} />
            </label>
            <label className="text-sm">
              إلى
              <input type="date" className="block mt-1 rounded-lg border border-[var(--border)] px-2 py-2" value={to} onChange={(e) => replaceParams({ to: e.target.value, page: '1' })} />
            </label>
          </>
        )}
        {tab === 'fuel' && view !== 'log' ? (
          <label className="text-sm">
            الشهر
            <input type="month" className="block mt-1 rounded-lg border border-[var(--border)] px-2 py-2" value={month} onChange={(e) => replaceParams({ month: e.target.value, page: '1' })} />
          </label>
        ) : null}
        {tab === 'vehicles' && view === 'period' ? (
          <div className="flex w-full sm:w-auto rounded-lg overflow-hidden border border-[var(--border)]">
            <button type="button" className={`flex-1 sm:flex-none px-3 py-2 text-sm ${mode === 'vehicle' ? 'bg-[var(--accent)] text-white' : ''}`} onClick={() => replaceParams({ mode: 'vehicle', page: '1' })}>
              عرض بالمركبة
            </button>
            <button type="button" className={`flex-1 sm:flex-none px-3 py-2 text-sm ${mode === 'rider' ? 'bg-[var(--accent)] text-white' : ''}`} onClick={() => replaceParams({ mode: 'rider', page: '1' })}>
              عرض بالمندوب الفعلي
            </button>
          </div>
        ) : null}
        <div className="flex flex-wrap gap-2">
          {canRefresh ? (
            <button type="button" className="inline-flex items-center gap-1 rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void load()}>
              <RefreshCw size={16} /> تحديث
            </button>
          ) : null}
          {canExport && tab === 'cities' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsCities().then((r) => downloadBlob(r, 'operations-cities.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'housing' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsHousing({ q: debouncedQ || undefined }).then((r) => downloadBlob(r, 'operations-housing.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'vehicles' && view === 'period' ? (
            <>
              <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsPeriod({ from, to, mode, detail: 'summary' }).then((r) => downloadBlob(r, 'operations-vehicles-summary.xlsx'))}>
                تصدير ملخص
              </button>
              <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsPeriod({ from, to, mode, detail: 'detailed' }).then((r) => downloadBlob(r, 'operations-vehicles-detailed.xlsx'))}>
                تصدير تفصيلي
              </button>
            </>
          ) : null}
          {canExport && tab === 'fuel' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsFuel({ month }).then((r) => downloadBlob(r, 'operations-fuel.xlsx'))}>
              تصدير
            </button>
          ) : null}
        </div>
      </div>

      {is503 ? (
        <div className="rounded-lg border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm">
          مصدر التشغيل غير متاح حاليًا (503). آخر بيانات معروضة إن وُجدت.
        </div>
      ) : null}

      {loading ? (
        <Skeleton className="h-64 w-full" />
      ) : errorMsg && !is503 ? (
        <ErrorState title="تعذر التحميل" message={errorMsg} onRetry={() => void load()} />
      ) : items.length === 0 ? (
        <EmptyState title="لا بيانات" description={view === 'oil' ? 'اختر مركبة من تقرير الفترة لعرض تغييرات الزيت.' : 'لا صفوف مطابقة للمرشحات.'} />
      ) : (
        <DataTable
          columns={columns}
          data={items}
          getRowId={(r) => String(r.id || Math.random())}
          serverSide
          searchable={false}
          exportable={false}
          syncUrl={false}
          page={page}
          pageSize={pageSize}
          totalCount={total}
          onPageChange={(p) => replaceParams({ page: String(p) })}
          onPageSizeChange={(s) => replaceParams({ size: String(s), page: '1' })}
        />
      )}

      <Drawer open={!!drawer} onClose={() => setDrawer(null)} title={drawer?.title || ''}>
        {drawer?.body}
      </Drawer>
    </div>
  );
}

export default function OperationsPage() {
  return (
    <Suspense fallback={<Skeleton className="h-64 w-full" />}>
      <OperationsPageInner />
    </Suspense>
  );
}
