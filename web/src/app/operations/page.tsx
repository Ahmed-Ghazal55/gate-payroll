'use client';

import React, { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Car, Fuel, Home, RefreshCw, Search } from 'lucide-react';
import {
  api,
  ApiError,
  OperationsFleetListResponse,
  OperationsFuelResponse,
  OperationsHousingResponse,
  OperationsOilChangesResponse,
  OperationsPeriodResponse,
  OperationsPurchasesResponse,
} from '../../lib/api';
/** Shared DataTable row; filled from ApiResp operations items[number]. */
type OpsRow = Record<string, unknown>;
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { SourceBadge } from '../../components/ui/SourceBadge';
import { Drawer } from '../../components/ui/Drawer';
import { useToast } from '../../components/ui/Toast';
import { formatSar } from '../../lib/money';
import { appendPeriodToHref, periodDateRange } from '../../lib/period';
import { usePeriod } from '../../lib/use-period';

const MAIN_TABS = new Set(['cities', 'housing', 'vehicles', 'fuel']);
const NO_SOURCE = 'لا يوجد مصدر بعد';
const cell = (v: unknown) => (v === null || v === undefined || v === '' ? NO_SOURCE : String(v));

function formatDateShort(v: unknown): string {
  if (v === null || v === undefined || v === '') return '—';
  const s = String(v).trim();
  if (!s) return '—';
  const m = s.match(/^(\d{4}-\d{2}-\d{2})/);
  return m ? m[1] : s;
}

function formatPurchaseStatus(v: unknown): string {
  const s = String(v ?? '').trim();
  if (s === '1' || s.toLowerCase() === 'posted') return 'مرحّل';
  if (s === '2' || s.toLowerCase() === 'reversed') return 'معكوس';
  return s || '—';
}

function formatFuelProviderAr(v: unknown): string {
  const s = String(v ?? '').trim();
  const low = s.toLowerCase();
  if (s === '1' || low === 'petroapp') return 'شركة بترو اب';
  if (s === '2' || low === 'sayaraapp') return 'شركة سيارة اب';
  return s || '—';
}

function formatFuelStatusAr(v: unknown): string {
  const s = String(v ?? '').trim();
  const low = s.toLowerCase();
  if (low === 'active' || s === '1' || s === 'نشطة') return 'نشطة';
  if (low === 'inactive' || s === '0' || s === 'غير نشطة') return 'غير نشطة';
  return s || '—';
}

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

  const { period: urlActivePeriod, urlPeriod, setPeriod } = usePeriod();
  const rawTab = searchParams?.get('tab') || 'cities';
  useEffect(() => {
    if (rawTab === 'cases') {
      if (urlPeriod) router.replace(appendPeriodToHref('/workforce/?tab=cases', urlPeriod));
      else router.replace('/workforce/?tab=cases');
      return;
    }
    if (rawTab === 'accounts') {
      if (urlPeriod) router.replace(appendPeriodToHref('/workforce/', urlPeriod));
      else router.replace('/workforce/');
      return;
    }
    if (rawTab === 'custody' || rawTab === 'guarantees') {
      if (urlPeriod) router.replace(appendPeriodToHref('/operations/?tab=vehicles', urlPeriod));
      else router.replace('/operations/?tab=vehicles');
      return;
    }
    if (!MAIN_TABS.has(rawTab)) {
      if (urlPeriod) router.replace(appendPeriodToHref('/operations/?tab=cities', urlPeriod));
      else router.replace('/operations/?tab=cities');
    }
  }, [rawTab, router, urlPeriod]);

  const tab = MAIN_TABS.has(rawTab) ? rawTab : 'cities';
  const view = searchParams?.get('view') || (tab === 'vehicles' ? 'period' : tab === 'fuel' ? 'usage' : '');
  const page = Math.max(1, parseInt(searchParams?.get('page') || '1', 10) || 1);
  const pageSize = [25, 50, 100, 200].includes(parseInt(searchParams?.get('size') || '25', 10))
    ? parseInt(searchParams?.get('size') || '25', 10)
    : 25;
  const q = searchParams?.get('q') || '';
  const mode = (searchParams?.get('mode') === 'rider' ? 'rider' : 'vehicle') as 'vehicle' | 'rider';
  const defaults = periodDateRange(urlActivePeriod);
  const from = searchParams?.get('from') || defaults.from;
  const to = searchParams?.get('to') || defaults.to;
  const month = urlActivePeriod;

  const [query, setQuery] = useState(q);
  const [debouncedQ, setDebouncedQ] = useState(q);
  const [loading, setLoading] = useState(true);
  const [is503, setIs503] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [source, setSource] = useState('');
  const [asOf, setAsOf] = useState('');
  const [allowedActions, setAllowedActions] = useState<string[]>([]);
  const [items, setItems] = useState<OpsRow[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState<Record<string, unknown>>({});
  const [drawer, setDrawer] = useState<{ title: string; body: React.ReactNode } | null>(null);
  const [serverQuery, setServerQuery] = useState<{ filtersParam?: string; sortParam?: string }>({});

  useEffect(() => {
    setServerQuery({});
  }, [tab, view]);

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
    const filters = serverQuery.filtersParam;
    const sort = serverQuery.sortParam;
    try {
      if (tab === 'cities') {
        const res = (await api.getOperationsCities({ q: debouncedQ || undefined, filters, sort })) as OperationsFleetListResponse;
        setItems((res.items || []) as OpsRow[]);
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
          filters,
          sort,
        })) as OperationsHousingResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'invoices') {
        const res = (await api.getOperationsPurchaseInvoices({
          from,
          to,
          page,
          pageSize,
          q: debouncedQ || undefined,
          filters,
          sort,
        })) as OperationsPurchasesResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'oil') {
        const res = (await api.getOperationsOilChangesPeriod({
          from,
          to,
          page,
          pageSize,
          q: debouncedQ || undefined,
          filters,
          sort,
        })) as OperationsOilChangesResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles') {
        const res = (await api.getOperationsPeriodReport({
          from,
          to,
          mode,
          page,
          pageSize,
          q: debouncedQ || undefined,
          filters,
          sort,
        })) as OperationsPeriodResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'fuel' && view === 'log') {
        const res = await api.getOperationsFuelAssignments({
          from,
          to,
          page,
          pageSize,
          filters,
          sort,
        });
        setItems((res.items || []) as OpsRow[]);
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
          filters,
          sort,
        })) as OperationsFuelResponse;
        setItems((res.items || []) as OpsRow[]);
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
  }, [tab, view, page, pageSize, debouncedQ, from, to, mode, month, serverQuery]);

  useEffect(() => {
    void load();
  }, [load]);

  const canExport = allowedActions.includes('export');
  const canRefresh = allowedActions.includes('refresh');

  const openCity = async (id: string) => {
    try {
      const detail = await api.getOperationsCity(id);
      const raw = detail as Record<string, unknown>;
      const byReg = Array.isArray(detail.byRegistration)
        ? (detail.byRegistration as Array<Record<string, unknown>>)
        : [];
      const byApp = Array.isArray(detail.byApp)
        ? (detail.byApp as Array<Record<string, unknown>>)
        : [];
      const vehiclesTotal =
        raw.vehiclesTotal ?? byReg.reduce((sum, r) => sum + Number(r.vehicles ?? 0), 0);
      const ridersTotal =
        raw.ridersTotal ?? byReg.reduce((sum, r) => sum + Number(r.riders ?? 0), 0);
      const accountsActive =
        raw.accountsActive ?? byApp.reduce((sum, r) => sum + Number(r.accountsActive ?? 0), 0);
      const accountsAssigned =
        raw.accountsAssigned ?? byApp.reduce((sum, r) => sum + Number(r.accountsAssigned ?? 0), 0);
      setDrawer({
        title: String(detail.nameAr || 'تفاصيل المدينة'),
        body: (
          <div className="space-y-4 text-sm">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-3">
                <div className="text-xs text-[var(--muted)]">الحالة</div>
                <div className="font-semibold mt-1">{formatFuelStatusAr(detail.status)}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-3">
                <div className="text-xs text-[var(--muted)]">المركبات</div>
                <div className="font-semibold mt-1">{String(vehiclesTotal)}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-3">
                <div className="text-xs text-[var(--muted)]">المناديب</div>
                <div className="font-semibold mt-1">{String(ridersTotal)}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-3">
                <div className="text-xs text-[var(--muted)]">الحسابات (نشط/مسند)</div>
                <div className="font-semibold mt-1">
                  {String(accountsActive)} / {String(accountsAssigned)}
                </div>
              </div>
            </div>

            <div>
              <h4 className="font-semibold mb-2">التوزيع حسب السجل التجاري</h4>
              {byReg.length === 0 ? (
                <EmptyState title="لا سجلات" description="لا توجد سجلات مرتبطة بهذه المدينة." />
              ) : (
                <DataTable
                  tableId="city-registrations"
                  data={byReg}
                  getRowId={(r) => String(r.id || r.unifiedNumber || Math.random())}
                  searchable={false}
                  exportable={false}
                  syncUrl={false}
                  showTotals={false}
                  showFilterRow={false}
                  columns={[
                    { key: 'nameAr', header: 'السجل', type: 'list', getValue: (r) => String(r.nameAr || ''), render: (r) => String(r.nameAr || '—') },
                    { key: 'unifiedNumber', header: 'الرقم الموحد', type: 'text', getValue: (r) => String(r.unifiedNumber || ''), render: (r) => <bdi className="font-mono">{String(r.unifiedNumber || '—')}</bdi> },
                    { key: 'vehicles', header: 'المركبات', type: 'number', numeric: true, getValue: (r) => Number(r.vehicles ?? 0), render: (r) => String(r.vehicles ?? 0) },
                    { key: 'riders', header: 'المناديب', type: 'number', numeric: true, getValue: (r) => Number(r.riders ?? 0), render: (r) => String(r.riders ?? 0) },
                    { key: 'accounts', header: 'الحسابات', type: 'number', numeric: true, getValue: (r) => Number(r.accounts ?? 0), render: (r) => String(r.accounts ?? 0) },
                  ]}
                />
              )}
            </div>

            <div>
              <h4 className="font-semibold mb-2">التوزيع حسب التطبيق</h4>
              {byApp.length === 0 ? (
                <EmptyState title="لا تطبيقات" description="لا توجد حسابات تطبيقات مسجلة بهذه المدينة." />
              ) : (
                <DataTable
                  tableId="city-apps"
                  data={byApp}
                  getRowId={(r) => String(r.platformCode || r.nameAr || Math.random())}
                  searchable={false}
                  exportable={false}
                  syncUrl={false}
                  showTotals={false}
                  showFilterRow={false}
                  columns={[
                    { key: 'nameAr', header: 'التطبيق', type: 'list', getValue: (r) => String(r.nameAr || r.platformCode || ''), render: (r) => String(r.nameAr || r.platformCode || '—') },
                    { key: 'accountsTotal', header: 'الإجمالي', type: 'number', numeric: true, getValue: (r) => Number(r.accountsTotal ?? 0), render: (r) => String(r.accountsTotal ?? 0) },
                    { key: 'accountsActive', header: 'النشط', type: 'number', numeric: true, getValue: (r) => Number(r.accountsActive ?? 0), render: (r) => String(r.accountsActive ?? 0) },
                    { key: 'accountsAssigned', header: 'المسند', type: 'number', numeric: true, getValue: (r) => Number(r.accountsAssigned ?? 0), render: (r) => String(r.accountsAssigned ?? 0) },
                  ]}
                />
              )}
            </div>
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
      const rooms = detail.rooms || [];
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
                  <div className="font-medium">{String(room.name || '')}</div>
                  <div className="text-xs text-[var(--muted)] mb-2">
                    سعة {String(room.capacity ?? '')} · إشغال {String(room.currentOccupancy ?? '')}
                  </div>
                  <ul className="space-y-1 text-sm">
                    {(room.residents || []).map((r, i) => (
                      <li key={i} className="flex flex-col sm:flex-row sm:gap-3">
                        <span>{String(r.nameAr || '')}</span>
                        <bdi className="font-mono">{String(r.iqama || '')}</bdi>
                        <span className="text-[var(--muted)]">{String(r.checkInDate || '')}</span>
                        {r.iqama ? (
                          <Link className="text-[var(--accent)]" href={`/workforce/?tab=360&iqama=${encodeURIComponent(String(r.iqama))}`}>
                            ملف 360
                          </Link>
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
      const rawHistory = Array.isArray(detail.assignmentHistory)
        ? (detail.assignmentHistory as Array<Record<string, unknown>>)
        : [];
      const history = [...rawHistory].sort((a, b) =>
        String(b.startedAt || '').localeCompare(String(a.startedAt || ''))
      );
      setDrawer({
        title: String(detail.plateNumberAr || detail.assetNumber || 'تفاصيل المركبة'),
        body: (
          <div className="space-y-4 text-sm">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">رقم الهيكل</div>
                <div className="font-mono font-medium mt-0.5"><bdi>{String(detail.vin || '—')}</bdi></div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">الهيكل</div>
                <div className="font-mono mt-0.5"><bdi>{String(detail.chassisNumber || '—')}</bdi></div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">العداد</div>
                <div className="font-medium mt-0.5">{String(detail.currentOdometer ?? '—')}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">الكفيل</div>
                <div className="font-medium mt-0.5">{String(detail.sponsorNameAr || '—')}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">المدينة</div>
                <div className="font-medium mt-0.5">{String(detail.city || '—')}</div>
              </div>
              <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-2)] p-2.5">
                <div className="text-xs text-[var(--muted)]">النوع</div>
                <div className="font-medium mt-0.5">{String(detail.vehicleType || '—')}</div>
              </div>
            </div>

            <div>
              <h4 className="font-semibold mb-2">سجل تعيينات المناديب (الأحدث أولًا)</h4>
              {history.length === 0 ? (
                <EmptyState title="لا سجل تعيينات" description="لا توجد تعيينات مسجلة لهذه المركبة." />
              ) : (
                <DataTable
                  tableId="vehicle-assignment-history"
                  data={history}
                  getRowId={(r) => String(r.id || `${r.iqama || ''}-${r.startedAt || ''}`)}
                  searchable={false}
                  exportable={false}
                  syncUrl={false}
                  showTotals={false}
                  showFilterRow={false}
                  columns={[
                    { key: 'riderNameAr', header: 'اسم المندوب', type: 'text', getValue: (r) => String(r.riderNameAr || ''), render: (r) => <span className="font-medium">{String(r.riderNameAr || '—')}</span> },
                    { key: 'iqama', header: 'رقم الإقامة', type: 'text', getValue: (r) => String(r.iqama || ''), render: (r) => <bdi className="font-mono">{String(r.iqama || '—')}</bdi> },
                    { key: 'startedAt', header: 'من', type: 'date', getValue: (r) => String(r.startedAt || ''), labelOf: (v) => formatDateShort(v), render: (r) => <bdi className="font-mono">{formatDateShort(r.startedAt)}</bdi> },
                    { key: 'endedAt', header: 'إلى', type: 'date', getValue: (r) => String(r.endedAt || ''), labelOf: (v) => (v ? formatDateShort(v) : 'حتى الآن'), render: (r) => <bdi className="font-mono">{r.endedAt ? formatDateShort(r.endedAt) : 'حتى الآن'}</bdi> },
                    { key: 'isRealRider', header: 'مندوب فعلي', type: 'list', getValue: (r) => (r.isRealRider ? 'نعم' : 'لا'), toneOf: (v) => (v === 'نعم' ? 'success' : 'neutral'), render: (r) => (r.isRealRider ? 'نعم' : 'لا') },
                  ]}
                />
              )}
            </div>
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
      if (view === 'invoices') {
        return [
          { label: 'عدد الفواتير', value: summary.invoicesTotal },
          { label: 'إجمالي المبالغ', value: formatSar(summary.totalAmount as number | string | null | undefined) },
        ];
      }
      if (view === 'oil') {
        return [
          { label: 'عدد التغييرات', value: summary.oilChangesCount },
          { label: 'إجمالي اللترات', value: summary.totalLiters },
          { label: 'تكلفة تغييرات الزيت', value: formatSar(summary.oilChangesCost as number | string | null | undefined) },
        ];
      }
      return [
        { label: 'المركبات العاملة', value: summary.vehiclesOperating ?? summary.invoicesTotal },
        { label: 'إجمالي أيام التشغيل', value: summary.totalOperatingDays },
        { label: 'تكلفة قطع الغيار', value: formatSar((summary.sparePartsCost ?? summary.totalAmount) as number | string | null | undefined) },
        { label: 'الاستقطاع المطلوب', value: formatSar(summary.amountToCollect as number | string | null | undefined) },
        { label: 'تكلفة تغييرات الزيت', value: formatSar(summary.oilChangesCost as number | string | null | undefined) },
      ];
    }
    if (view === 'log') {
      return [
        { label: 'سجلات التسليم', value: summary.assignmentsTotal },
      ];
    }
    return [
      { label: 'البطاقات النشطة', value: summary.activeCards },
      { label: 'المناديب المستهلكون', value: summary.consumingRiders },
      { label: 'إجمالي اللترات', value: summary.totalLiters },
      { label: 'الإجمالي شامل الضريبة', value: formatSar(summary.totalInclVat as number | string | null | undefined) },
      { label: 'تجاوز السقف', value: summary.capExceeded ?? NO_SOURCE },
    ];
  }, [tab, view, summary]);

  const columns: ColumnDef<OpsRow>[] = useMemo(() => {
    if (tab === 'cities') {
      return [
        { key: 'nameAr', header: 'المدينة', type: 'list', filterType: 'list', getValue: (r) => String(r.nameAr || ''), render: (r) => String(r.nameAr || '') },
        { key: 'regs', header: 'السجلات النشطة', type: 'text', filterType: 'text', filterColumn: 'registrationNames', getValue: (r) => String(r.registrationNames || r.activeRegistrations || ''), render: (r) => String(r.registrationNames || r.activeRegistrations || '') },
        { key: 'veh', header: 'المركبات (سيارة/دراجة)', type: 'number', filterType: 'number', filterColumn: 'vehiclesTotal', getValue: (r) => Number(r.vehiclesTotal ?? (Number(r.vehiclesCars ?? 0) + Number(r.vehiclesMotorbikes ?? 0))), render: (r) => `${r.vehiclesCars ?? 0} / ${r.vehiclesMotorbikes ?? 0}` },
        { key: 'riders', header: 'المناديب', type: 'number', filterType: 'number', numeric: true, filterColumn: 'ridersTotal', getValue: (r) => (r.ridersTotal != null ? Number(r.ridersTotal) : ''), render: (r) => String(r.ridersTotal ?? '') },
        { key: 'apps', header: 'التطبيقات العاملة', type: 'list', filterType: 'list', filterColumn: 'appsWorking', getValue: (r) => String(r.appsWorking || ''), render: (r) => String(r.appsWorking || '') },
        { key: 'acc', header: 'الحسابات', type: 'number', filterType: 'number', filterColumn: 'accountsTotal', getValue: (r) => Number(r.accountsTotal ?? 0), render: (r) => `${r.accountsTotal ?? 0} / ${r.accountsActive ?? 0} / ${r.accountsAssigned ?? 0}` },
        {
          key: 'actions',
          header: 'إجراءات',
          filterable: false,
          sortable: false,
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
        { key: 'nameAr', header: 'اسم السكن', type: 'text', filterType: 'text', getValue: (r) => String(r.nameAr || ''), render: (r) => String(r.nameAr || '') },
        { key: 'city', header: 'المدينة', type: 'list', filterType: 'list', getValue: (r) => String(r.city || ''), render: (r) => String(r.city || '') },
        { key: 'rooms', header: 'عدد الغرف', type: 'number', filterType: 'number', numeric: true, filterColumn: 'roomsCount', getValue: (r) => (r.roomsCount != null ? Number(r.roomsCount) : ''), render: (r) => String(r.roomsCount ?? '') },
        { key: 'res', header: 'إجمالي المناديب', type: 'number', filterType: 'number', numeric: true, filterColumn: 'residentsTotal', getValue: (r) => (r.residentsTotal != null ? Number(r.residentsTotal) : ''), render: (r) => String(r.residentsTotal ?? '') },
        { key: 'per', header: 'مندوب/غرفة', type: 'number', filterType: 'number', numeric: true, summary: 'none', filterColumn: 'ridersPerRoom', getValue: (r) => (r.ridersPerRoom != null ? Number(r.ridersPerRoom) : ''), render: (r) => String(r.ridersPerRoom ?? '') },
        { key: 'sup', header: 'المشرف', filterable: false, sortable: false, render: () => NO_SOURCE },
        { key: 'status', header: 'الحالة', type: 'list', filterType: 'list', getValue: (r) => String(r.status || ''), toneOf: defaultStatusTone, render: (r) => String(r.status || '') },
        {
          key: 'actions',
          header: 'إجراءات',
          filterable: false,
          sortable: false,
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
          {
            key: 'cardName',
            header: 'اسم البطاقة',
            type: 'list',
            filterType: 'list',
            filterColumn: 'cardName',
            getValue: (r) => String(r.cardName || r.providerNameAr || formatFuelProviderAr(r.provider) || ''),
            labelOf: (v) => formatFuelProviderAr(v),
            render: (r) => {
              const providerAr = String(r.providerNameAr || formatFuelProviderAr(r.provider));
              const notes = String(r.cardNotes || '').trim();
              return (
                <div className="flex flex-col">
                  <span className="font-medium">{providerAr}</span>
                  {notes ? <span className="text-xs text-[var(--muted)]">{notes}</span> : null}
                </div>
              );
            },
          },
          { key: 'card', header: 'رقم البطاقة', type: 'list', filterType: 'list', filterColumn: 'cardNumber', getValue: (r) => String(r.cardNumber || ''), render: (r) => <bdi className="font-mono font-medium">{String(r.cardNumber || '—')}</bdi> },
          {
            key: 'rider',
            header: 'المندوب',
            type: 'text',
            filterType: 'text',
            filterColumn: 'riderNameAr',
            getValue: (r) => `${r.riderNameAr || ''} ${r.iqama || ''}`.trim(),
            render: (r) => (
              <div className="flex flex-col">
                <span className="font-medium">{String(r.riderNameAr || '—')}</span>
                {r.iqama ? <bdi className="text-xs text-[var(--muted)] font-mono">{String(r.iqama)}</bdi> : null}
              </div>
            ),
          },
          { key: 'from', header: 'من', type: 'date', filterType: 'date', filterColumn: 'effectiveFrom', getValue: (r) => String(r.effectiveFrom || ''), labelOf: (v) => formatDateShort(v), render: (r) => formatDateShort(r.effectiveFrom) },
          { key: 'to', header: 'إلى', type: 'date', filterType: 'date', filterColumn: 'effectiveTo', getValue: (r) => String(r.effectiveTo || ''), labelOf: (v) => (v ? formatDateShort(v) : 'حتى الآن'), render: (r) => (r.effectiveTo ? formatDateShort(r.effectiveTo) : 'حتى الآن') },
        ];
      }
      return [
        {
          key: 'cardName',
          header: 'اسم البطاقة',
          type: 'list',
          filterType: 'list',
          filterColumn: 'cardName',
          getValue: (r) => String(r.cardName || r.providerNameAr || formatFuelProviderAr(r.provider) || ''),
          labelOf: (v) => formatFuelProviderAr(v),
          render: (r) => {
            const providerAr = String(r.providerNameAr || formatFuelProviderAr(r.provider));
            const notes = String(r.cardNotes || '').trim();
            return (
              <div className="flex flex-col">
                <span className="font-medium">{providerAr}</span>
                {notes ? <span className="text-xs text-[var(--muted)]">{notes}</span> : null}
              </div>
            );
          },
        },
        { key: 'cardNumber', header: 'رقم البطاقة', type: 'list', filterType: 'list', getValue: (r) => String(r.cardNumber || ''), render: (r) => <bdi className="font-mono font-medium">{String(r.cardNumber || '—')}</bdi> },
        {
          key: 'rider',
          header: 'المندوب الفعلي',
          type: 'text',
          filterType: 'text',
          filterColumn: 'riderNameAr',
          getValue: (r) => `${r.riderNameAr || ''} ${r.iqama || ''}`.trim(),
          render: (r) => (
            <div className="flex flex-col">
              <span className="font-medium">{String(r.riderNameAr || '—')}</span>
              {r.iqama ? <bdi className="text-xs text-[var(--muted)] font-mono">{String(r.iqama)}</bdi> : null}
            </div>
          ),
        },
        { key: 'plate', header: 'المركبة', type: 'list', filterType: 'list', filterColumn: 'plateNumber', getValue: (r) => String(r.plateNumber || ''), render: (r) => String(r.plateNumber || '—') },
        { key: 'liters', header: 'اللترات', type: 'number', filterType: 'number', numeric: true, getValue: (r) => (r.liters != null ? Number(r.liters) : ''), render: (r) => String(r.liters ?? '') },
        { key: 'before', header: 'قبل الضريبة', type: 'number', filterType: 'number', numeric: true, filterColumn: 'amountBeforeTax', getValue: (r) => (r.amountBeforeTax != null ? Number(r.amountBeforeTax) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.amountBeforeTax as number | string | null | undefined)}</span> },
        { key: 'vat', header: 'الضريبة', type: 'number', filterType: 'number', numeric: true, getValue: (r) => (r.vat != null ? Number(r.vat) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.vat as number | string | null | undefined)}</span> },
        { key: 'total', header: 'الإجمالي', type: 'number', filterType: 'number', numeric: true, filterColumn: 'totalInclVat', getValue: (r) => (r.totalInclVat != null ? Number(r.totalInclVat) : ''), render: (r) => <span className="num font-medium whitespace-nowrap">{formatSar(r.totalInclVat as number | string | null | undefined)}</span> },
        { key: 'cap', header: 'السقف', filterable: false, sortable: false, render: () => NO_SOURCE },
        {
          key: 'status',
          header: 'الحالة',
          type: 'list',
          filterType: 'list',
          getValue: (r) => formatFuelStatusAr(r.status),
          labelOf: (v) => formatFuelStatusAr(v),
          toneOf: (v) => (formatFuelStatusAr(v) === 'نشطة' ? 'success' : 'danger'),
          render: (r) => {
            const label = formatFuelStatusAr(r.status);
            const active = label === 'نشطة';
            return (
              <span
                className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                  active
                    ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300'
                    : 'bg-slate-500/15 text-slate-700 dark:text-slate-300'
                }`}
              >
                {label}
              </span>
            );
          },
        },
      ];
    }
    if (view === 'invoices') {
      return [
        { key: 'num', header: 'رقم الاستلام', type: 'text', filterType: 'text', filterColumn: 'receiptNumber', getValue: (r) => String(r.receiptNumber || ''), render: (r) => String(r.receiptNumber || '') },
        { key: 'supInv', header: 'فاتورة المورد', type: 'text', filterType: 'text', filterColumn: 'supplierInvoiceNumber', getValue: (r) => String(r.supplierInvoiceNumber || ''), render: (r) => String(r.supplierInvoiceNumber || '—') },
        { key: 'sup', header: 'المورد', type: 'list', filterType: 'list', filterColumn: 'supplierNameAr', getValue: (r) => String(r.supplierNameAr || ''), render: (r) => String(r.supplierNameAr || '—') },
        { key: 'date', header: 'تاريخ الفاتورة', type: 'date', filterType: 'date', filterColumn: 'invoiceDate', getValue: (r) => String(r.invoiceDate || ''), labelOf: (v) => formatDateShort(v), render: (r) => formatDateShort(r.invoiceDate) },
        { key: 'receivedAt', header: 'تاريخ الاستلام', type: 'date', filterType: 'date', getValue: (r) => String(r.receivedAt || ''), labelOf: (v) => formatDateShort(v), render: (r) => formatDateShort(r.receivedAt) },
        { key: 'total', header: 'الإجمالي', type: 'number', filterType: 'number', numeric: true, filterColumn: 'totalAmount', getValue: (r) => (r.totalAmount != null ? Number(r.totalAmount) : ''), render: (r) => <span className="num font-medium whitespace-nowrap">{formatSar(r.totalAmount as number | string | null | undefined)}</span> },
        { key: 'status', header: 'الحالة', type: 'list', filterType: 'list', getValue: (r) => formatPurchaseStatus(r.status), labelOf: (v) => formatPurchaseStatus(v), toneOf: (v) => (formatPurchaseStatus(v) === 'مرحّل' ? 'success' : 'danger'), render: (r) => formatPurchaseStatus(r.status) },
      ];
    }
    if (view === 'oil') {
      return [
        { key: 'plate', header: 'اللوحة', type: 'list', filterType: 'list', filterColumn: 'plateNumberAr', getValue: (r) => String(r.plateNumberAr || ''), render: (r) => cell(r.plateNumberAr) },
        { key: 'date', header: 'التاريخ', type: 'date', filterType: 'date', filterColumn: 'performedAt', getValue: (r) => String(r.performedAt || ''), labelOf: (v) => formatDateShort(v), render: (r) => formatDateShort(r.performedAt) },
        { key: 'odo', header: 'العداد', type: 'number', filterType: 'number', numeric: true, summary: 'none', filterColumn: 'odometer', getValue: (r) => (r.odometer != null ? Number(r.odometer) : ''), render: (r) => cell(r.odometer) },
        { key: 'liters', header: 'لترات', type: 'number', filterType: 'number', numeric: true, filterColumn: 'oilLiters', getValue: (r) => (r.oilLiters != null ? Number(r.oilLiters) : ''), render: (r) => cell(r.oilLiters) },
        { key: 'oil', header: 'تكلفة الزيت', type: 'number', filterType: 'number', numeric: true, filterColumn: 'oilCost', getValue: (r) => (r.oilCost != null ? Number(r.oilCost) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.oilCost as number | string | null | undefined)}</span> },
        { key: 'filter', header: 'الفلتر', type: 'number', filterType: 'number', numeric: true, filterColumn: 'filterCost', getValue: (r) => (r.filterCost != null ? Number(r.filterCost) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.filterCost as number | string | null | undefined)}</span> },
        { key: 'labor', header: 'أجور', type: 'number', filterType: 'number', numeric: true, filterColumn: 'laborCost', getValue: (r) => (r.laborCost != null ? Number(r.laborCost) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.laborCost as number | string | null | undefined)}</span> },
        { key: 'other', header: 'أخرى', type: 'number', filterType: 'number', numeric: true, filterColumn: 'otherCost', getValue: (r) => (r.otherCost != null ? Number(r.otherCost) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.otherCost as number | string | null | undefined)}</span> },
        { key: 'total', header: 'الإجمالي', type: 'number', filterType: 'number', numeric: true, filterColumn: 'totalCost', getValue: (r) => (r.totalCost != null ? Number(r.totalCost) : ''), render: (r) => <span className="num font-medium whitespace-nowrap">{formatSar(r.totalCost as number | string | null | undefined)}</span> },
      ];
    }
    return [
      { key: 'plate', header: 'اللوحة والنوع', type: 'list', filterType: 'list', filterColumn: 'plateNumberAr', getValue: (r) => String(r.plateNumberAr || ''), render: (r) => `${r.plateNumberAr || ''} · ${r.vehicleType || ''}` },
      { key: 'rider', header: mode === 'rider' ? 'المندوب الفعلي' : 'المندوب / الفترة', type: 'text', filterType: 'text', filterColumn: 'riderNameAr', getValue: (r) => `${r.riderNameAr || ''} ${r.iqama || ''}`.trim(), render: (r) => String(r.riderNameAr || '') },
      { key: 'city', header: 'المدينة', type: 'list', filterType: 'list', getValue: (r) => String(r.city || ''), render: (r) => String(r.city || '') },
      { key: 'days', header: 'أيام التشغيل', type: 'number', filterType: 'number', numeric: true, filterColumn: 'operatingDays', getValue: (r) => (r.operatingDays != null ? Number(r.operatingDays) : ''), render: (r) => String(r.operatingDays ?? '') },
      { key: 'parts', header: 'تكلفة قطع الغيار', type: 'number', filterType: 'number', numeric: true, filterColumn: 'sparePartsCost', getValue: (r) => (r.sparePartsCost != null ? Number(r.sparePartsCost) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.sparePartsCost as number | string | null | undefined)}</span> },
      { key: 'collect', header: 'الاستقطاع المطلوب', type: 'number', filterType: 'number', numeric: true, filterColumn: 'amountToCollect', getValue: (r) => (r.amountToCollect != null ? Number(r.amountToCollect) : ''), render: (r) => <span className="num whitespace-nowrap">{formatSar(r.amountToCollect as number | string | null | undefined)}</span> },
      { key: 'oil', header: 'زيت (عدد/تكلفة/كم)', type: 'number', filterType: 'number', filterColumn: 'oilChangesCost', getValue: (r) => Number(r.oilChangesCost ?? 0), render: (r) => <span className="num whitespace-nowrap">{`${r.oilChangesCount ?? 0} / ${formatSar(r.oilChangesCost as number | string | null | undefined)} / ${r.oilMileage ?? '—'}`}</span> },
      {
        key: 'actions',
        header: 'إجراءات',
        filterable: false,
        sortable: false,
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
            <input type="month" className="block mt-1 rounded-lg border border-[var(--border)] px-2 py-2" value={month} onChange={(e) => setPeriod(e.target.value, { page: '1' })} />
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
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsCities({ filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-cities.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'housing' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsHousing({ q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-housing.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'vehicles' && view === 'period' ? (
            <>
              <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsPeriod({ from, to, mode, detail: 'summary', q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-vehicles-summary.xlsx'))}>
                تصدير ملخص
              </button>
              <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsPeriod({ from, to, mode, detail: 'detailed', q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-vehicles-detailed.xlsx'))}>
                تصدير تفصيلي
              </button>
            </>
          ) : null}
          {canExport && tab === 'vehicles' && view === 'oil' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsOilChanges({ from, to, q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-oil-changes.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'vehicles' && view === 'invoices' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsPurchases({ from, to, q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-purchases.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'fuel' && view !== 'log' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsFuel({ month, q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-fuel.xlsx'))}>
              تصدير
            </button>
          ) : null}
          {canExport && tab === 'fuel' && view === 'log' ? (
            <button type="button" className="rounded-lg border border-[var(--border)] px-3 py-2" onClick={() => void api.exportOperationsFuelLog({ from, to, q: debouncedQ || undefined, filters: serverQuery.filtersParam, sort: serverQuery.sortParam }).then((r) => downloadBlob(r, 'operations-fuel-log.xlsx'))}>
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

      {errorMsg && !is503 ? (
        <ErrorState title="تعذر التحميل" message={errorMsg} onRetry={() => void load()} />
      ) : (
        <DataTable
          key={`ops-${tab}-${view}`}
          tableId={`ops-${tab}-${view}`}
          columns={columns}
          data={items}
          loading={loading}
          getRowId={(r) => String(r.id || Math.random())}
          serverSide
          distinctEndpoint="/operations/distinct"
          distinctParams={{ tab, view, from, to, month, mode, q: debouncedQ || undefined, filters: serverQuery.filtersParam }}
          onServerQueryChange={(sq) => {
            setServerQuery({ filtersParam: sq.filtersParam, sortParam: sq.sortParam });
            replaceParams({ page: '1' });
          }}
          emptyTitle="لا بيانات"
          emptyDescription={view === 'oil' ? 'لا تغييرات زيت في هذه الفترة.' : 'لا صفوف مطابقة للمرشحات.'}
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
