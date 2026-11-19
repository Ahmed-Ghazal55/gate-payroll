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
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { SourceBadge } from '../../components/ui/SourceBadge';
import { Drawer } from '../../components/ui/Drawer';
import { useToast } from '../../components/ui/Toast';

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
  const [items, setItems] = useState<OpsRow[]>([]);
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
        })) as OperationsHousingResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'invoices') {
        const res = await api.getOperationsPurchaseInvoices({ from, to, page, pageSize, q: debouncedQ || undefined }) as OperationsPurchasesResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'vehicles' && view === 'oil') {
        const res = await api.getOperationsOilChangesPeriod({ from, to, page, pageSize, q: debouncedQ || undefined }) as OperationsOilChangesResponse;
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
        })) as OperationsPeriodResponse;
        setItems((res.items || []) as OpsRow[]);
        setTotal(Number(res.total || 0));
        setSummary((res.summary || {}) as Record<string, unknown>);
        setSource(String(res.source || ''));
        setAsOf(String(res.asOf || ''));
        setAllowedActions((res.allowedActions || []) as string[]);
      } else if (tab === 'fuel' && view === 'log') {
        const res = await api.getOperationsFuelAssignments({ from, to, page, pageSize });
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
  }, [tab, view, page, pageSize, debouncedQ, from, to, mode, month]);

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
                <div className="font-semibold mt-1">{String(detail.status || '—')}</div>
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
                <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
                  <table className="w-full text-xs text-start">
                    <thead className="bg-[var(--surface-2)] border-b border-[var(--border)]">
                      <tr>
                        <th className="px-3 py-2 text-start">السجل</th>
                        <th className="px-3 py-2 text-start">الرقم الموحد</th>
                        <th className="px-3 py-2 text-start">المركبات</th>
                        <th className="px-3 py-2 text-start">المناديب</th>
                        <th className="px-3 py-2 text-start">الحسابات</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border)]">
                      {byReg.map((row, idx) => (
                        <tr key={String(row.id || idx)}>
                          <td className="px-3 py-2">{String(row.nameAr || '—')}</td>
                          <td className="px-3 py-2 font-mono"><bdi>{String(row.unifiedNumber || '—')}</bdi></td>
                          <td className="px-3 py-2">{String(row.vehicles ?? 0)}</td>
                          <td className="px-3 py-2">{String(row.riders ?? 0)}</td>
                          <td className="px-3 py-2">{String(row.accounts ?? 0)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div>
              <h4 className="font-semibold mb-2">التوزيع حسب التطبيق</h4>
              {byApp.length === 0 ? (
                <EmptyState title="لا تطبيقات" description="لا توجد حسابات تطبيقات مسجلة بهذه المدينة." />
              ) : (
                <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
                  <table className="w-full text-xs text-start">
                    <thead className="bg-[var(--surface-2)] border-b border-[var(--border)]">
                      <tr>
                        <th className="px-3 py-2 text-start">التطبيق</th>
                        <th className="px-3 py-2 text-start">الإجمالي</th>
                        <th className="px-3 py-2 text-start">النشط</th>
                        <th className="px-3 py-2 text-start">المسند</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border)]">
                      {byApp.map((row, idx) => (
                        <tr key={String(row.platformCode || idx)}>
                          <td className="px-3 py-2">{String(row.nameAr || row.platformCode || '—')}</td>
                          <td className="px-3 py-2">{String(row.accountsTotal ?? 0)}</td>
                          <td className="px-3 py-2">{String(row.accountsActive ?? 0)}</td>
                          <td className="px-3 py-2">{String(row.accountsAssigned ?? 0)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
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
                          <a className="text-[var(--accent)]" href={`/workforce/?tab=360&iqama=${encodeURIComponent(String(r.iqama))}`}>
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
                <div className="text-xs text-[var(--muted)]">رقم الهيكل (VIN)</div>
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
                <div className="overflow-x-auto rounded-lg border border-[var(--border)]">
                  <table className="w-full text-xs text-start">
                    <thead className="bg-[var(--surface-2)] border-b border-[var(--border)]">
                      <tr>
                        <th className="px-3 py-2 text-start">اسم المندوب</th>
                        <th className="px-3 py-2 text-start">رقم الإقامة</th>
                        <th className="px-3 py-2 text-start">من</th>
                        <th className="px-3 py-2 text-start">إلى</th>
                        <th className="px-3 py-2 text-start">مندوب فعلي</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--border)]">
                      {history.map((row, idx) => (
                        <tr key={String(row.id || idx)}>
                          <td className="px-3 py-2 font-medium">{String(row.riderNameAr || '—')}</td>
                          <td className="px-3 py-2 font-mono"><bdi>{String(row.iqama || '—')}</bdi></td>
                          <td className="px-3 py-2 font-mono"><bdi>{formatDateShort(row.startedAt)}</bdi></td>
                          <td className="px-3 py-2 font-mono">
                            <bdi>{row.endedAt ? formatDateShort(row.endedAt) : 'حتى الآن'}</bdi>
                          </td>
                          <td className="px-3 py-2">{row.isRealRider ? 'نعم' : 'لا'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
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
          { label: 'إجمالي المبالغ', value: summary.totalAmount },
        ];
      }
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
  }, [tab, view, summary]);

  const columns: ColumnDef<OpsRow>[] = useMemo(() => {
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
          {
            key: 'cardName',
            header: 'اسم البطاقة',
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
          { key: 'card', header: 'رقم البطاقة', render: (r) => <bdi className="font-mono font-medium">{String(r.cardNumber || '—')}</bdi> },
          {
            key: 'rider',
            header: 'المندوب',
            render: (r) => (
              <div className="flex flex-col">
                <span className="font-medium">{String(r.riderNameAr || '—')}</span>
                {r.iqama ? <bdi className="text-xs text-[var(--muted)] font-mono">{String(r.iqama)}</bdi> : null}
              </div>
            ),
          },
          { key: 'from', header: 'من', render: (r) => formatDateShort(r.effectiveFrom) },
          { key: 'to', header: 'إلى', render: (r) => (r.effectiveTo ? formatDateShort(r.effectiveTo) : 'حتى الآن') },
        ];
      }
      return [
        {
          key: 'cardName',
          header: 'اسم البطاقة',
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
        { key: 'cardNumber', header: 'رقم البطاقة', render: (r) => <bdi className="font-mono font-medium">{String(r.cardNumber || '—')}</bdi> },
        {
          key: 'rider',
          header: 'المندوب الفعلي',
          render: (r) => (
            <div className="flex flex-col">
              <span className="font-medium">{String(r.riderNameAr || '—')}</span>
              {r.iqama ? <bdi className="text-xs text-[var(--muted)] font-mono">{String(r.iqama)}</bdi> : null}
            </div>
          ),
        },
        { key: 'plate', header: 'المركبة', render: (r) => String(r.plateNumber || '—') },
        { key: 'liters', header: 'اللترات', render: (r) => String(r.liters ?? '') },
        { key: 'before', header: 'قبل الضريبة', render: (r) => String(r.amountBeforeTax ?? '') },
        { key: 'vat', header: 'الضريبة', render: (r) => String(r.vat ?? '') },
        { key: 'total', header: 'الإجمالي', render: (r) => String(r.totalInclVat ?? '') },
        { key: 'cap', header: 'السقف', render: () => NO_SOURCE },
        {
          key: 'status',
          header: 'الحالة',
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
        { key: 'num', header: 'رقم الاستلام', render: (r) => String(r.receiptNumber || '') },
        { key: 'supInv', header: 'فاتورة المورد', render: (r) => String(r.supplierInvoiceNumber || '—') },
        { key: 'sup', header: 'المورد', render: (r) => String(r.supplierNameAr || '—') },
        { key: 'date', header: 'تاريخ الفاتورة', render: (r) => formatDateShort(r.invoiceDate) },
        { key: 'receivedAt', header: 'تاريخ الاستلام', render: (r) => formatDateShort(r.receivedAt) },
        { key: 'total', header: 'الإجمالي', render: (r) => String(r.totalAmount ?? '') },
        { key: 'status', header: 'الحالة', render: (r) => formatPurchaseStatus(r.status) },
      ];
    }
    if (view === 'oil') {
      return [
        { key: 'plate', header: 'اللوحة', render: (r) => cell(r.plateNumberAr) },
        { key: 'date', header: 'التاريخ', render: (r) => cell(r.performedAt) },
        { key: 'odo', header: 'العداد', render: (r) => cell(r.odometer) },
        { key: 'liters', header: 'لترات', render: (r) => cell(r.oilLiters) },
        { key: 'oil', header: 'تكلفة الزيت', render: (r) => cell(r.oilCost) },
        { key: 'filter', header: 'الفلتر', render: (r) => cell(r.filterCost) },
        { key: 'labor', header: 'أجور', render: (r) => cell(r.laborCost) },
        { key: 'other', header: 'أخرى', render: (r) => cell(r.otherCost) },
        { key: 'total', header: 'الإجمالي', render: (r) => cell(r.totalCost) },
      ];
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
        <EmptyState title="لا بيانات" description={view === 'oil' ? 'لا تغييرات زيت في هذه الفترة.' :'لا صفوف مطابقة للمرشحات.'} />
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
