'use client';

import React, { useState, useEffect, useMemo, useCallback, useRef, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import {
  Users,
  UserCheck,
  Search,
  RefreshCw,
  Sparkles,
  FileText,
  Building2,
  ShieldAlert,
  Car,
  Bike,
  Briefcase,
  Flag,
  AlertTriangle,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef, defaultStatusTone } from '../../components/ui/DataTable';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { SourceBadge } from '../../components/ui/SourceBadge';
import { IbanDisplay } from '../../components/ui/IbanDisplay';
import { platformLabelAr } from '../../features/companies/company-stats';
import { inApp } from '../../lib/paths';
import type {
  WorkforceListResponse,
  Workforce360Response,
  WorkforceSummary,
  WorkforcePerson,
} from '../../lib/api';
import type { RiderRecord } from '../../lib/types';

const PAGE_SIZES = [25, 50, 100, 200];

type ListFilters = {
  sponsorship: string;
  workType: string;
  missingIban: boolean;
  status: string;
  sponsorUnified: string;
  platform: string;
  city: string;
};

const EMPTY_FILTERS: ListFilters = {
  sponsorship: '',
  workType: '',
  missingIban: false,
  status: '',
  sponsorUnified: '',
  platform: '',
  city: '',
};

const STATUS_LABELS: Record<string, string> = {
  Draft: 'مسودة',
  Onboarding: 'قيد التهيئة',
  Active: 'نشط',
  Suspended: 'موقوف',
  OnLeave: 'في إجازة',
  Terminated: 'منتهي الخدمة',
  Archived: 'مؤرشف',
  Fleeing: 'هروب / انقطاع',
  Accident: 'حادث',
  Sick: 'إجازة مرضية',
  NeedsReview: 'بانتظار المراجعة',
  Inactive: 'غير نشط',
};

const ISSUE_LABELS: Record<string, string> = {
  missing_iban_sponsored: 'آيبان مفقود للمكفول',
  no_active_platform: 'بدون منصة نشطة',
  missing_phone: 'جوال مفقود',
  missing_city: 'مدينة مفقودة',
};

const SOURCE_LABELS: Record<string, string> = {
  erp_live: 'قراءة مباشرة',
  erp_file: 'ملف من نظام عمر',
  store: 'المخزن المحلي',
  mudad: 'مدد',
  erp: 'نظام عمر',
  forms: 'النماذج',
};

function statusLabel(value?: string): string {
  if (!value) return '—';
  return STATUS_LABELS[value] || value;
}

function cellText(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  return String(value);
}

function StackedCell({
  primary,
  secondary,
  tertiary,
}: {
  primary: React.ReactNode;
  secondary?: React.ReactNode;
  tertiary?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5 min-w-0 py-0.5">
      <div className="font-semibold text-[var(--ink)] leading-snug whitespace-nowrap">{primary || '—'}</div>
      {secondary != null && secondary !== '' ? (
        <div className="text-[11px] text-[var(--muted)] leading-snug whitespace-nowrap">{secondary}</div>
      ) : null}
      {tertiary != null && tertiary !== '' ? (
        <div className="text-[11px] text-[var(--muted)] leading-snug whitespace-nowrap">{tertiary}</div>
      ) : null}
    </div>
  );
}

function engagementLabel(value?: string): string {
  if (value === 'SponsoredInternal') return 'على الكفالة';
  if (value === 'External' || value === 'OutsideRider') return 'خارجي';
  return value || '—';
}

function workTypeLabel(value?: string): string {
  if (value === 'admin') return 'إداري';
  if (value === 'rider') return 'مندوب';
  return value || '—';
}

function vehicleLabel(row: WorkforcePerson | RiderRecord): string {
  if (row.hasCompanyVehicle) {
    const type = row.companyVehicleType;
    if (type === 'Car' || type === '2') return 'مركبة شركة · سيارة';
    if (type === 'Motorcycle' || type === '1') return 'مركبة شركة · دباب';
    return type ? `مركبة شركة · ${type}` : 'مركبة شركة';
  }
  const role = 'operationalRole' in row ? row.operationalRole : undefined;
  if (role === 'Car') return 'سيارة (تشغيل)';
  if (role === 'Motorcycle' || role === 'Bike') return 'دباب (تشغيل)';
  return '—';
}

function pickField(row: Record<string, unknown>, ...keys: string[]): string {
  for (const key of keys) {
    const value = row[key];
    if (value !== null && value !== undefined && String(value).trim() !== '') {
      return String(value);
    }
  }
  return '';
}

function WorkforcePageInner() {
  const { toast } = useToast();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'directory';
  const pageFromUrl = parseInt(searchParams?.get('page') || '1', 10);
  const sizeFromUrl = parseInt(searchParams?.get('size') || '50', 10);
  const qFromUrl = searchParams?.get('q') || '';
  const iqamaFromUrl = searchParams?.get('iqama') || searchParams?.get('id') || '';
  const companyFromUrl =
    searchParams?.get('company') ||
    searchParams?.get('sponsorUnified') ||
    searchParams?.get('companyId') ||
    '';
  const platformFromUrl = searchParams?.get('platform') || '';

  const [activeTab, setActiveTab] = useState(tabFromUrl);
  const [page, setPage] = useState(pageFromUrl > 0 ? pageFromUrl : 1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES.includes(sizeFromUrl) ? sizeFromUrl : 50);
  const [searchQuery, setSearchQuery] = useState(qFromUrl);
  const [debouncedQuery, setDebouncedQuery] = useState(qFromUrl);
  const [filters, setFilters] = useState<ListFilters>(() => ({
    ...EMPTY_FILTERS,
    sponsorUnified: companyFromUrl,
    platform: platformFromUrl,
  }));

  const [items, setItems] = useState<WorkforcePerson[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState<WorkforceSummary | null>(null);
  const [source, setSource] = useState('');
  const [asOf, setAsOf] = useState('');
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const [profileKey, setProfileKey] = useState(iqamaFromUrl);
  const [profileData, setProfileData] = useState<Workforce360Response | null>(null);
  const [loadingProfile, setLoadingProfile] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);

  const [qualityItems, setQualityItems] = useState<Record<string, unknown>[]>([]);
  const [qualitySource, setQualitySource] = useState('');
  const [loadingQuality, setLoadingQuality] = useState(false);
  const [qualityError, setQualityError] = useState<string | null>(null);

  const [saudiItems, setSaudiItems] = useState<Record<string, unknown>[]>([]);
  const [saudiSource, setSaudiSource] = useState('');
  const [loadingSaudi, setLoadingSaudi] = useState(false);
  const [saudiError, setSaudiError] = useState<string | null>(null);

  const [terminatedItems, setTerminatedItems] = useState<Record<string, unknown>[]>([]);
  const [absenceItems, setAbsenceItems] = useState<Record<string, unknown>[]>([]);
  const [casesSource, setCasesSource] = useState('');
  const [loadingCases, setLoadingCases] = useState(false);
  const [casesError, setCasesError] = useState<string | null>(null);
  const [legalItems, setLegalItems] = useState<Record<string, unknown>[]>([]);
  const [allowedActions, setAllowedActions] = useState<string[]>([]);
  const directoryAbort = useRef<AbortController | null>(null);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  useEffect(() => {
    setFilters((prev) => {
      if (prev.sponsorUnified === companyFromUrl && prev.platform === platformFromUrl) return prev;
      return { ...prev, sponsorUnified: companyFromUrl, platform: platformFromUrl };
    });
  }, [companyFromUrl, platformFromUrl]);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQuery(searchQuery), 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  const updateUrl = useCallback(
    (opts: {
      tab?: string;
      page?: number;
      size?: number;
      q?: string;
      iqama?: string;
      company?: string;
      platform?: string;
    }) => {
      if (typeof window === 'undefined') return;
      const sp = new URLSearchParams(searchParams?.toString() || '');
      const nextTab = opts.tab ?? activeTab;
      if (nextTab && nextTab !== 'directory') sp.set('tab', nextTab);
      else sp.delete('tab');

      const nextPage = opts.page ?? page;
      if (nextPage > 1) sp.set('page', String(nextPage));
      else sp.delete('page');

      const nextSize = opts.size ?? pageSize;
      if (nextSize !== 50) sp.set('size', String(nextSize));
      else sp.delete('size');

      const nextQ = opts.q ?? debouncedQuery;
      if (nextQ.trim()) sp.set('q', nextQ.trim());
      else sp.delete('q');

      const nextCompany = opts.company ?? filters.sponsorUnified;
      if (nextCompany.trim()) sp.set('company', nextCompany.trim());
      else sp.delete('company');
      sp.delete('sponsorUnified');
      sp.delete('companyId');

      const nextPlatform = opts.platform ?? filters.platform;
      if (nextPlatform.trim()) sp.set('platform', nextPlatform.trim());
      else sp.delete('platform');

      if (opts.iqama !== undefined) {
        if (opts.iqama.trim()) sp.set('iqama', opts.iqama.trim());
        else sp.delete('iqama');
        sp.delete('id');
      }

      const qs = sp.toString() ? `?${sp.toString()}` : '';
      router.replace(`${pathname}${qs}`, { scroll: false });
    },
    [
      router,
      pathname,
      searchParams,
      activeTab,
      page,
      pageSize,
      debouncedQuery,
      filters.sponsorUnified,
      filters.platform,
    ]
  );

  const [directoryServerQuery, setDirectoryServerQuery] = useState<{
    filtersParam?: string;
    sortParam?: string;
  }>({});
  const directoryServerQueryRef = useRef(directoryServerQuery);
  directoryServerQueryRef.current = directoryServerQuery;

  const loadDirectory = useCallback(
    async (
      targetPage: number,
      targetPageSize: number,
      targetQuery: string,
      targetFilters: ListFilters,
      targetServerQuery = directoryServerQueryRef.current
    ) => {
      directoryAbort.current?.abort();
      const ac = new AbortController();
      directoryAbort.current = ac;
      setLoading(true);
      setErrorMsg(null);
      try {
        const res: WorkforceListResponse = await api.getWorkforce({
          page: targetPage,
          pageSize: targetPageSize,
          q: targetQuery.trim() || undefined,
          sponsorship: targetFilters.sponsorship || undefined,
          workType: targetFilters.workType || undefined,
          status: targetFilters.status || undefined,
          sponsorUnified: targetFilters.sponsorUnified || undefined,
          platform: targetFilters.platform || undefined,
          city: targetFilters.city || undefined,
          missingIban: targetFilters.missingIban || undefined,
          filters: targetServerQuery.filtersParam,
          sort: targetServerQuery.sortParam,
          signal: ac.signal,
        });
        if (ac.signal.aborted) return;
        const rows = res?.items || [];
        setItems(rows);
        setTotal(res?.total ?? rows.length);
        setSummary(res?.summary ?? null);
        setSource(res?.source || '');
        setAsOf(res?.asOf || '');
        setPage(res?.page ?? targetPage);
        setPageSize(res?.pageSize ?? targetPageSize);
        setAllowedActions(res?.allowedActions || []);
      } catch (err: unknown) {
        if (ac.signal.aborted || (err instanceof Error && err.name === 'AbortError')) return;
        const msg =
          err instanceof ApiError
            ? err.message
            : err instanceof Error
            ? err.message
            : 'تعذر تحميل دليل القوى العاملة';
        setErrorMsg(msg);
        setItems([]);
        setTotal(0);
        toast(msg, 'danger');
      } finally {
        setLoading(false);
      }
    },
    [toast]
  );

  const loadProfile360 = useCallback(
    async (key?: string) => {
      const target = (key ?? profileKey).trim();
      if (!target) {
        toast('أدخل رقم الإقامة أو المعرّف لعرض الملف 360', 'warning');
        return;
      }
      setLoadingProfile(true);
      setProfileError(null);
      try {
        const data = await api.getWorkforce360(target);
        setProfileData(data);
        setProfileKey(data?.person?.iqama || target);
        setSource(data?.source || source);
        setAsOf(data?.asOf || asOf);
        updateUrl({ tab: '360', iqama: data?.person?.iqama || target });
      } catch (err: unknown) {
        const msg =
          err instanceof ApiError
            ? err.message
            : err instanceof Error
            ? err.message
            : 'تعذر تحميل الملف 360';
        setProfileError(msg);
        setProfileData(null);
        toast(msg, 'danger');
      } finally {
        setLoadingProfile(false);
      }
    },
    [profileKey, toast, updateUrl, source, asOf]
  );

  const loadQuality = useCallback(async () => {
    setLoadingQuality(true);
    setQualityError(null);
    try {
      const res = await api.getWorkforceDataQuality();
      setQualityItems((res?.items as Record<string, unknown>[]) || []);
      setQualitySource(res?.source || '');
    } catch (err: unknown) {
      const msg =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل جودة البيانات';
      setQualityError(msg);
      setQualityItems([]);
      toast(msg, 'danger');
    } finally {
      setLoadingQuality(false);
    }
  }, [toast]);

  const loadSaudi = useCallback(async () => {
    setLoadingSaudi(true);
    setSaudiError(null);
    try {
      const res = await api.getWorkforceSaudi();
      setSaudiItems((res?.items as Record<string, unknown>[]) || []);
      setSaudiSource(res?.source || '');
    } catch (err: unknown) {
      const msg =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل موظفي الرواتب السعوديين';
      setSaudiError(msg);
      setSaudiItems([]);
      toast(msg, 'danger');
    } finally {
      setLoadingSaudi(false);
    }
  }, [toast]);

  const loadCases = useCallback(async () => {
    setLoadingCases(true);
    setCasesError(null);
    try {
      const [terminated, absence] = await Promise.all([
        api.getWorkforceTerminated(),
        api.getWorkforceAbsenceCases(),
      ]);
      setTerminatedItems((terminated?.items as Record<string, unknown>[]) || []);
      setAbsenceItems((absence?.items as Record<string, unknown>[]) || []);
      setCasesSource(terminated?.source || absence?.source || '');
    } catch (err: unknown) {
      const msg =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل حالات الإنهاء والغياب';
      setCasesError(msg);
      setTerminatedItems([]);
      setAbsenceItems([]);
      toast(msg, 'danger');
    } finally {
      setLoadingCases(false);
    }
  }, [toast]);

  const loadLegal = useCallback(async () => {
    setLoadingCases(true);
    setCasesError(null);
    try {
      const res = await api.getWorkforceLegalCases();
      setLegalItems((res?.items as Record<string, unknown>[]) || []);
      setCasesSource(res?.source || '');
      if (res?.message) setCasesError(res.message);
    } catch (err: unknown) {
      const msg =
        err instanceof ApiError
          ? err.message
          : err instanceof Error
          ? err.message
          : 'تعذر تحميل القضايا القانونية';
      setCasesError(msg);
      setLegalItems([]);
      toast(msg, 'danger');
    } finally {
      setLoadingCases(false);
    }
  }, [toast]);

  const isFirstMount = useRef(true);

  useEffect(() => {
    if (activeTab !== 'directory') return;
    if (isFirstMount.current) {
      isFirstMount.current = false;
      loadDirectory(page, pageSize, debouncedQuery, filters);
      return;
    }
    setPage(1);
    updateUrl({
      tab: 'directory',
      page: 1,
      q: debouncedQuery,
      company: filters.sponsorUnified,
      platform: filters.platform,
    });
    loadDirectory(1, pageSize, debouncedQuery, filters);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reload on query/filter change only
  }, [debouncedQuery, filters, activeTab]);

  useEffect(() => {
    if (activeTab === 'quality') loadQuality();
    if (activeTab === 'saudi') loadSaudi();
    if (activeTab === 'cases') loadCases();
    if (activeTab === 'legal') loadLegal();
    if (activeTab === '360' && iqamaFromUrl && !profileData && !loadingProfile) {
      loadProfile360(iqamaFromUrl);
    }
  }, [activeTab]);

  const applyKpiFilter = (next: Partial<ListFilters>) => {
    setFilters((prev) => {
      const merged = { ...prev, ...next };
      const same =
        merged.sponsorship === prev.sponsorship &&
        merged.workType === prev.workType &&
        merged.missingIban === prev.missingIban &&
        merged.status === prev.status;
      if (same) return EMPTY_FILTERS;
      return merged;
    });
    setActiveTab('directory');
    updateUrl({ tab: 'directory', page: 1 });
  };

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    updateUrl({ page: newPage });
    loadDirectory(newPage, pageSize, debouncedQuery, filters);
  };

  const handlePageSizeChange = (newSize: number) => {
    setPageSize(newSize);
    setPage(1);
    updateUrl({ page: 1, size: newSize });
    loadDirectory(1, newSize, debouncedQuery, filters);
  };

  const handleTabChange = (tabId: string) => {
    setActiveTab(tabId);
    updateUrl({ tab: tabId, page: 1 });
  };

  const open360 = (row: WorkforcePerson) => {
    const key = row.id || row.iqama;
    setProfileKey(key);
    setActiveTab('360');
    updateUrl({ tab: '360', iqama: row.iqama || key });
    loadProfile360(key);
  };

  const companyFilterOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of summary?.byCompany || []) {
      if (c.sponsorUnified) {
        map.set(String(c.sponsorUnified), String(c.sponsorNameAr || c.sponsorUnified));
      }
    }
    if (filters.sponsorUnified && !map.has(filters.sponsorUnified)) {
      map.set(filters.sponsorUnified, filters.sponsorUnified);
    }
    return [
      { value: 'all', label: 'كل الشركات' },
      ...Array.from(map.entries()).map(([value, label]) => ({ value, label })),
    ];
  }, [summary, filters.sponsorUnified]);

  const platformFilterOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of items) {
      const p = String(r.platform || r.platforms || '').trim();
      if (p && !map.has(p)) map.set(p, platformLabelAr(p));
    }
    if (filters.platform && !map.has(filters.platform)) {
      map.set(filters.platform, platformLabelAr(filters.platform));
    }
    return [
      { value: 'all', label: 'كل المنصات' },
      ...Array.from(map.entries()).map(([value, label]) => ({ value, label })),
    ];
  }, [items, filters.platform]);

  const sectionTabs: SectionTabItem[] = [
    { id: 'directory', label: 'الدليل', icon: <Users size={16} /> },
    { id: '360', label: 'الملف 360', icon: <UserCheck size={16} /> },
    { id: 'quality', label: 'جودة البيانات', icon: <Sparkles size={16} /> },
    { id: 'saudi', label: 'رواتب سعودية', icon: <Flag size={16} /> },
    { id: 'cases', label: 'الإنهاء والغياب', icon: <AlertTriangle size={16} /> },
    ...(allowedActions.includes('view_legal_cases')
      ? [{ id: 'legal', label: 'القضايا', icon: <ShieldAlert size={16} /> } as SectionTabItem]
      : []),
  ];

  const directoryColumns: ColumnDef<WorkforcePerson>[] = useMemo(
    () => [
      {
        key: 'person',
        header: 'الشخص / الإقامة',
        type: 'text',
        filterable: true,
        filterType: 'text',
        filterColumn: 'name',
        getValue: (r) => `${r.name || ''} ${r.iqama || ''}`,
        render: (r) => (
          <StackedCell
            primary={r.name || '—'}
            secondary={
              <span className="font-mono font-bold text-[var(--ink)]" dir="ltr">
                {r.iqama || r.id || '—'}
              </span>
            }
            tertiary={engagementLabel(r.engagementType)}
          />
        ),
      },
      {
        key: 'contact',
        header: 'التواصل / المدينة',
        type: 'list',
        filterType: 'list',
        filterColumn: 'city',
        getValue: (r) => r.city || '',
        render: (r) => (
          <StackedCell
            primary={r.phone || '—'}
            secondary={r.city || '—'}
            tertiary={r.nationality || undefined}
          />
        ),
      },
      {
        key: 'sponsor',
        header: 'الكفيل / الدور',
        type: 'list',
        filterType: 'list',
        filterColumn: 'sponsorNameAr',
        getValue: (r) => r.sponsorNameAr || r.sponsor || '',
        render: (r) => (
          <StackedCell
            primary={r.sponsorNameAr || r.sponsor || '—'}
            secondary={workTypeLabel(r.workType)}
            tertiary={r.jobTitle || undefined}
          />
        ),
      },
      {
        key: 'platform',
        header: 'المنصة / الآيبان',
        type: 'list',
        filterType: 'list',
        filterColumn: 'platform',
        getValue: (r) => r.platform || r.platforms || '',
        labelOf: (v) => (v ? platformLabelAr(String(v)) : ''),
        render: (r) => (
          <div className="flex flex-col gap-1.5 min-w-0">
            <StackedCell
              primary={r.platform || r.platforms || '—'}
              secondary={r.platformWorkId ? <span className="font-mono" dir="ltr">{r.platformWorkId}</span> : undefined}
            />
            {allowedActions.includes('view_iban') ? (
              <IbanDisplay iban={r.iban} masked={false} />
            ) : (
              <span className="text-[11px] text-[var(--muted)]">مخفي</span>
            )}
            {r.ibanAlert ? (
              <span className="text-[11px] text-[var(--warning)] font-semibold whitespace-nowrap">
                {String(r.ibanAlert)}
              </span>
            ) : null}
          </div>
        ),
      },
      {
        key: 'mudadSalary',
        header: 'راتب مدد / المركبة',
        type: 'list',
        filterType: 'list',
        filterColumn: 'companyVehicleType',
        getValue: (r) => vehicleLabel(r),
        render: (r) => (
          <StackedCell
            primary={<span className="whitespace-nowrap">{r.mudadSalary || '—'}</span>}
            secondary={
              <span className="inline-flex items-center gap-1">
                {r.hasCompanyVehicle || r.operationalRole === 'Car' ? (
                  <Car size={12} className="text-[var(--accent)]" />
                ) : (
                  <Bike size={12} className="text-[var(--muted)]" />
                )}
                {vehicleLabel(r)}
              </span>
            }
          />
        ),
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list',
        filterable: true,
        filterType: 'list',
        getValue: (r) => r.status || '',
        labelOf: (v) => statusLabel(String(v || '')),
        toneOf: (v) =>
          v === 'Active' || v === 'نشط'
            ? 'success'
            : v === 'NeedsReview'
              ? 'warning'
              : defaultStatusTone(v),
        render: (r) => (
          <StackedCell
            primary={
              <span
                className={`inline-flex px-2 py-0.5 rounded-full text-xs font-semibold ${
                  r.status === 'Active'
                    ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                    : r.status === 'NeedsReview'
                    ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                    : 'bg-[var(--muted)]/15 text-[var(--muted)]'
                }`}
              >
                {statusLabel(r.status)}
              </span>
            }
            secondary={
              <div className="flex flex-wrap gap-1.5 mt-1">
                <button
                  type="button"
                  onClick={() => open360(r)}
                  className="px-2 py-0.5 rounded-lg bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--accent)] hover:text-[var(--accent-ink)] font-bold text-[11px] transition cursor-pointer"
                >
                  الملف 360
                </button>
                <Link
                  href={`/riders/statement/?iqama=${encodeURIComponent(r.iqama || r.id)}`}
                  className="px-2 py-0.5 rounded-lg border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--accent)] text-[11px] font-medium transition"
                >
                  كشف الحساب
                </Link>
              </div>
            }
          />
        ),
      },
    ],
    [allowedActions]
  );

  const qualityColumns: ColumnDef<Record<string, unknown>>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'الاسم',
        type: 'text',
        getValue: (r) => cellText(r.name),
        render: (r) => <span className="font-semibold">{cellText(r.name)}</span>,
      },
      {
        key: 'iqama',
        header: 'الإقامة',
        type: 'text',
        getValue: (r) => cellText(r.iqama),
        render: (r) => (
          <span className="font-mono font-bold" dir="ltr">
            {cellText(r.iqama)}
          </span>
        ),
      },
      {
        key: 'issueKind',
        header: 'نوع المشكلة',
        type: 'list',
        getValue: (r) => String(r.issueKind || ''),
        labelOf: (v) => ISSUE_LABELS[String(v || '')] || String(v || ''),
        toneOf: () => 'warning',
        render: (r) => (
          <span className="whitespace-nowrap">
            {ISSUE_LABELS[String(r.issueKind || '')] || cellText(r.issueKind)}
          </span>
        ),
      },
      {
        key: 'suggestedAction',
        header: 'الإجراء المقترح',
        type: 'list',
        getValue: (r) => cellText(r.suggestedAction),
        render: (r) => (
          <span className="text-[var(--muted)] whitespace-nowrap">{cellText(r.suggestedAction)}</span>
        ),
      },
      {
        key: 'source',
        header: 'المصدر',
        type: 'list',
        getValue: (r) => String(r.source || ''),
        labelOf: (v) => SOURCE_LABELS[String(v || '')] || String(v || ''),
        render: (r) => (
          <span className="text-xs whitespace-nowrap">
            {SOURCE_LABELS[String(r.source || '')] || cellText(r.source)}
          </span>
        ),
      },
    ],
    []
  );

  const saudiColumns: ColumnDef<Record<string, unknown>>[] = useMemo(
    () => [
      {
        key: 'number',
        header: 'الرقم',
        type: 'text',
        getValue: (r) => cellText(r.number),
        render: (r) => <span className="font-mono">{cellText(r.number)}</span>,
      },
      {
        key: 'name',
        header: 'الاسم',
        type: 'text',
        getValue: (r) => cellText(r.name),
        render: (r) => <span className="font-semibold">{cellText(r.name)}</span>,
      },
      {
        key: 'nationalId',
        header: 'الهوية',
        type: 'text',
        getValue: (r) => cellText(r.nationalId),
        render: (r) => (
          <span className="font-mono font-bold" dir="ltr">
            {cellText(r.nationalId)}
          </span>
        ),
      },
      {
        key: 'sponsorNameAr',
        header: 'الكفيل',
        type: 'list',
        getValue: (r) => cellText(r.sponsorNameAr),
        render: (r) => <span className="whitespace-nowrap">{cellText(r.sponsorNameAr)}</span>,
      },
      {
        key: 'country',
        header: 'الدولة',
        type: 'list',
        getValue: (r) => cellText(r.country),
        render: (r) => <span className="whitespace-nowrap">{cellText(r.country)}</span>,
      },
      {
        key: 'joiningDate',
        header: 'تاريخ الالتحاق',
        type: 'date',
        getValue: (r) => cellText(r.joiningDate),
        render: (r) => (
          <span className="font-mono text-xs whitespace-nowrap">{cellText(r.joiningDate)}</span>
        ),
      },
      {
        key: 'unifiedNationalNumber',
        header: 'الرقم الموحّد',
        type: 'text',
        getValue: (r) => cellText(r.unifiedNationalNumber),
        render: (r) => (
          <span className="font-mono text-xs whitespace-nowrap" dir="ltr">
            {cellText(r.unifiedNationalNumber)}
          </span>
        ),
      },
      {
        key: 'salary',
        header: 'الراتب',
        type: 'number',
        numeric: true,
        getValue: (r) => (r.salary != null && r.salary !== '' ? Number(r.salary) : ''),
        render: (r) => <span className="num whitespace-nowrap">{cellText(r.salary) || '—'}</span>,
      },
      {
        key: 'personalIban',
        header: 'الآيبان',
        type: 'text',
        getValue: (r) => (allowedActions.includes('view_iban') ? String(r.personalIban || '') : ''),
        render: (r) =>
          allowedActions.includes('view_iban') ? (
            <IbanDisplay iban={String(r.personalIban || '')} masked={false} />
          ) : (
            <span className="text-[11px] text-[var(--muted)]">مخفي</span>
          ),
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list',
        getValue: (r) => String(r.status || ''),
        labelOf: (v) => statusLabel(String(v || '')),
        toneOf: defaultStatusTone,
        render: (r) => <span className="whitespace-nowrap">{statusLabel(String(r.status || ''))}</span>,
      },
    ],
    [allowedActions]
  );

  const terminatedColumns: ColumnDef<Record<string, unknown>>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'الاسم',
        type: 'text',
        getValue: (r) => cellText(r.name),
        render: (r) => <span className="font-semibold">{cellText(r.name)}</span>,
      },
      {
        key: 'iqama',
        header: 'الإقامة',
        type: 'text',
        getValue: (r) => cellText(r.iqama),
        render: (r) => (
          <span className="font-mono font-bold" dir="ltr">
            {cellText(r.iqama)}
          </span>
        ),
      },
      {
        key: 'status',
        header: 'الحالة',
        type: 'list',
        getValue: (r) => String(r.status || ''),
        labelOf: (v) => statusLabel(String(v || '')),
        toneOf: defaultStatusTone,
        render: (r) => <span className="whitespace-nowrap">{statusLabel(String(r.status || ''))}</span>,
      },
      {
        key: 'terminationDate',
        header: 'تاريخ الإنهاء',
        type: 'date',
        getValue: (r) => cellText(r.terminationDate),
        render: (r) => (
          <span className="font-mono text-xs whitespace-nowrap">{cellText(r.terminationDate)}</span>
        ),
      },
      {
        key: 'statusReason',
        header: 'السبب',
        type: 'text',
        getValue: (r) => cellText(r.statusReason),
        render: (r) => <span className="text-[var(--muted)]">{cellText(r.statusReason)}</span>,
      },
      {
        key: 'city',
        header: 'المدينة',
        type: 'list',
        getValue: (r) => cellText(r.city),
        render: (r) => <span>{cellText(r.city)}</span>,
      },
    ],
    []
  );

  const absenceColumns: ColumnDef<Record<string, unknown>>[] = useMemo(
    () => [
      {
        key: 'CaseNumber',
        header: 'رقم الحالة',
        type: 'text',
        getValue: (r) => cellText(pickField(r, 'CaseNumber', 'caseNumber')),
        render: (r) => (
          <span className="font-mono font-bold">
            {cellText(pickField(r, 'CaseNumber', 'caseNumber'))}
          </span>
        ),
      },
      {
        key: 'EmployeeNameAr',
        header: 'الاسم',
        type: 'text',
        getValue: (r) => cellText(pickField(r, 'EmployeeNameAr', 'employeeNameAr', 'name')),
        render: (r) => (
          <span className="font-semibold">
            {cellText(pickField(r, 'EmployeeNameAr', 'employeeNameAr', 'name'))}
          </span>
        ),
      },
      {
        key: 'IqamaNo',
        header: 'الإقامة',
        type: 'text',
        getValue: (r) => cellText(pickField(r, 'IqamaNo', 'iqamaNo', 'iqama')),
        render: (r) => (
          <span className="font-mono font-bold" dir="ltr">
            {cellText(pickField(r, 'IqamaNo', 'iqamaNo', 'iqama'))}
          </span>
        ),
      },
      {
        key: 'AbsenceDate',
        header: 'تاريخ الغياب',
        type: 'date',
        getValue: (r) => cellText(pickField(r, 'AbsenceDate', 'absenceDate')),
        render: (r) => (
          <span className="font-mono text-xs">
            {cellText(pickField(r, 'AbsenceDate', 'absenceDate'))}
          </span>
        ),
      },
      {
        key: 'CurrentPath',
        header: 'المسار',
        type: 'list',
        getValue: (r) => cellText(pickField(r, 'CurrentPath', 'currentPath')),
        render: (r) => <span>{cellText(pickField(r, 'CurrentPath', 'currentPath'))}</span>,
      },
      {
        key: 'Status',
        header: 'الحالة',
        type: 'list',
        getValue: (r) => cellText(pickField(r, 'Status', 'status')),
        toneOf: defaultStatusTone,
        render: (r) => <span>{cellText(pickField(r, 'Status', 'status'))}</span>,
      },
    ],
    []
  );

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const activeSource = activeTab === 'quality' ? qualitySource : activeTab === 'saudi' ? saudiSource : activeTab === 'cases' ? casesSource : source;

  return (
    <div className="space-y-6">
      <PageHeader
        title="القوى العاملة"
        eyebrow="دليل الأشخاص والتشغيل"
        description="دليل موحّد للمكفولين والخارجيين والإداريين، مع ملف 360 مختصر وتقارير الجودة والحالات."
        primaryAction={{
          label: 'تحديث',
          icon: <RefreshCw size={15} />,
          onClick: () => {
            if (activeTab === 'directory') loadDirectory(page, pageSize, debouncedQuery, filters);
            else if (activeTab === '360') loadProfile360();
            else if (activeTab === 'quality') loadQuality();
            else if (activeTab === 'saudi') loadSaudi();
            else if (activeTab === 'cases') loadCases();
          },
        }}
      >
        {activeSource ? <SourceBadge source={activeSource} asOf={asOf} /> : null}
      </PageHeader>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 sm:gap-4">
        <StatCard
          label="الإجمالي"
          value={summary?.totalCount ?? total}
          hint="كل السجلات في الدليل"
          icon={Users}
          loading={loading && activeTab === 'directory'}
          active={!filters.sponsorship && !filters.workType && !filters.missingIban}
          onClick={() => applyKpiFilter(EMPTY_FILTERS)}
        />
        <StatCard
          label="على الكفالة"
          value={summary?.sponsoredCount ?? 0}
          hint="كفالة داخلية"
          icon={Building2}
          loading={loading && activeTab === 'directory'}
          active={filters.sponsorship === 'sponsored' && !filters.workType && !filters.missingIban}
          onClick={() => applyKpiFilter({ sponsorship: 'sponsored', workType: '', missingIban: false })}
        />
        <StatCard
          label="مناديب مكفولون"
          value={summary?.sponsoredRiders ?? 0}
          hint="على الكفالة · مندوب"
          icon={Bike}
          loading={loading && activeTab === 'directory'}
          active={filters.sponsorship === 'sponsored' && filters.workType === 'rider'}
          onClick={() =>
            applyKpiFilter({ sponsorship: 'sponsored', workType: 'rider', missingIban: false })
          }
        />
        <StatCard
          label="إداريون مكفولون"
          value={summary?.sponsoredAdmins ?? 0}
          hint="على الكفالة · إداري"
          icon={Briefcase}
          loading={loading && activeTab === 'directory'}
          active={filters.sponsorship === 'sponsored' && filters.workType === 'admin'}
          onClick={() =>
            applyKpiFilter({ sponsorship: 'sponsored', workType: 'admin', missingIban: false })
          }
        />
        <StatCard
          label="مكفول بلا آيبان"
          value={summary?.sponsoredMissingIban ?? 0}
          hint="تنبيه نقص الآيبان"
          icon={ShieldAlert}
          loading={loading && activeTab === 'directory'}
          active={filters.missingIban}
          onClick={() =>
            applyKpiFilter({ sponsorship: 'sponsored', workType: '', missingIban: true })
          }
        />
      </div>

      <SectionTabs tabs={sectionTabs} activeTab={activeTab} onChange={handleTabChange} />

      {activeTab === 'directory' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex flex-wrap items-center gap-2 flex-1 min-w-[240px]">
              <div className="relative flex-1 max-w-sm">
                <Search size={15} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
                <input
                  type="text"
                  placeholder="بحث بالإقامة أو الاسم أو الجوال..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm focus:outline-hidden"
                />
              </div>
              <SearchableSelect
                id="wf-sponsorship"
                value={filters.sponsorship || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    sponsorship: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية الكفالة"
                placeholder="كل أنواع الكفالة"
                className="w-44"
                options={[
                  { value: 'all', label: 'كل أنواع الكفالة' },
                  { value: 'sponsored', label: 'على الكفالة' },
                  { value: 'external', label: 'خارجي' },
                ]}
              />
              <SearchableSelect
                id="wf-work-type"
                value={filters.workType || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    workType: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية نوع العمل"
                placeholder="كل الأدوار"
                className="w-40"
                options={[
                  { value: 'all', label: 'كل الأدوار' },
                  { value: 'rider', label: 'مندوب' },
                  { value: 'admin', label: 'إداري' },
                ]}
              />
              <SearchableSelect
                id="wf-status"
                value={filters.status || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    status: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية الحالة"
                placeholder="كل الحالات"
                className="w-44"
                options={[
                  { value: 'all', label: 'كل الحالات' },
                  ...Object.entries(STATUS_LABELS).map(([value, label]) => ({ value, label })),
                ]}
              />
              <SearchableSelect
                id="wf-company"
                value={filters.sponsorUnified || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    sponsorUnified: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية الشركة"
                placeholder="كل الشركات"
                className="w-48"
                options={companyFilterOptions}
              />
              <SearchableSelect
                id="wf-platform"
                value={filters.platform || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    platform: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية المنصة"
                placeholder="كل المنصات"
                className="w-40"
                options={platformFilterOptions}
              />
              <SearchableSelect
                id="wf-city"
                value={filters.city || 'all'}
                onChange={(v) =>
                  setFilters((prev) => ({
                    ...prev,
                    city: v === 'all' ? '' : v,
                  }))
                }
                ariaLabel="تصفية المدينة"
                placeholder="كل المدن"
                className="w-40"
                options={[
                  { value: 'all', label: 'كل المدن' },
                  ...Array.from(new Set(items.map((r) => r.city).filter(Boolean) as string[])).map(
                    (c) => ({ value: c, label: c })
                  ),
                ]}
              />
            </div>
            <span className="text-xs text-[var(--muted)]">
              الصفحة {page} من {totalPages} · {total} سجل · حجم {pageSize}
            </span>
          </div>

          {(summary?.byCompany?.length ?? 0) > 0 ? (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              {summary!.byCompany!.map((card) => (
                <StatCard
                  key={card.sponsorUnified || card.sponsorNameAr}
                  label={card.sponsorNameAr || card.sponsorUnified || 'شركة'}
                  value={card.totalCount ?? 0}
                  hint="حسب الشركة"
                  icon={Building2}
                  active={filters.sponsorUnified === card.sponsorUnified}
                  onClick={() =>
                    applyKpiFilter({
                      sponsorUnified:
                        filters.sponsorUnified === card.sponsorUnified
                          ? ''
                          : String(card.sponsorUnified || ''),
                    })
                  }
                />
              ))}
            </div>
          ) : null}

          {errorMsg ? (
            <ErrorState
              title="تعذر تحميل الدليل"
              message={errorMsg}
              onRetry={() => loadDirectory(page, pageSize, debouncedQuery, filters)}
            />
          ) : (
            <DataTable
              tableId="workforce-directory"
              columns={directoryColumns}
              data={items}
              loading={loading}
              emptyTitle="لا توجد سجلات"
              emptyDescription="لا توجد سجلات تطابق الفلاتر الحالية."
              exportFilename="workforce_directory"
              serverSide
              distinctEndpoint="/workforce/distinct"
              distinctParams={{
                sponsorship: filters.sponsorship || undefined,
                workType: filters.workType || undefined,
                status: filters.status || undefined,
                sponsorUnified: filters.sponsorUnified || undefined,
                platform: filters.platform || undefined,
                city: filters.city || undefined,
                filters: directoryServerQuery.filtersParam,
              }}
              onServerQueryChange={(sq) => {
                const next = { filtersParam: sq.filtersParam, sortParam: sq.sortParam };
                setDirectoryServerQuery(next);
                setPage(1);
                loadDirectory(1, pageSize, debouncedQuery, filters, next);
              }}
              page={page}
              pageSize={pageSize}
              pageSizes={PAGE_SIZES}
              totalCount={total}
              onPageChange={handlePageChange}
              onPageSizeChange={handlePageSizeChange}
              searchable={false}
              syncUrl={false}
              getRowId={(r) => r.id || r.iqama}
              onExport={async () => {
                const res = await api.exportWorkforce({
                  q: debouncedQuery.trim() || undefined,
                  sponsorship: filters.sponsorship || undefined,
                  workType: filters.workType || undefined,
                  status: filters.status || undefined,
                  sponsorUnified: filters.sponsorUnified || undefined,
                  platform: filters.platform || undefined,
                  city: filters.city || undefined,
                  missingIban: filters.missingIban || undefined,
                  filters: directoryServerQuery.filtersParam,
                  sort: directoryServerQuery.sortParam,
                });
                if (!res.ok) throw new Error('تعذر تصدير الملف');
                const blob = await res.blob();
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = 'workforce-export.xlsx';
                a.click();
                URL.revokeObjectURL(url);
              }}
            />
          )}
        </div>
      )}

      {activeTab === '360' && (
        <div className="space-y-6">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex flex-wrap items-center gap-3">
            <div className="relative flex-1 max-w-md">
              <Search size={16} className="absolute start-3 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
              <input
                type="text"
                placeholder="رقم الإقامة أو معرّف الموظف..."
                value={profileKey}
                onChange={(e) => setProfileKey(e.target.value)}
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
              {loadingProfile ? 'جاري التحميل...' : 'عرض الملف 360'}
            </button>
            {profileData?.source ? <SourceBadge source={profileData.source} asOf={profileData.asOf} /> : null}
          </div>

          {profileError ? (
            <ErrorState title="تعذر تحميل الملف 360" message={profileError} onRetry={() => loadProfile360()} />
          ) : loadingProfile ? (
            <div className="p-8 space-y-4 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-24 w-full" />
              <Skeleton className="h-48 w-full" />
            </div>
          ) : !profileData ? (
            <EmptyState
              title="ابحث بالإقامة أو المعرّف"
              description="يعرض الملف المختصر سجل العمل لثلاثة أشهر، التقييم المالي، صورة الإقامة عند توفر الصلاحية، سندات الأمر، والمركز المالي."
            />
          ) : (
            <div className="space-y-6">
              <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div className="flex items-center gap-4">
                    <div className="h-14 w-14 rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] flex items-center justify-center font-bold text-2xl shadow-xs">
                      {profileData.person?.name?.charAt(0) || 'ش'}
                    </div>
                    <div>
                      <h2 className="text-lg sm:text-xl font-bold text-[var(--ink)]">
                        {profileData.person?.name || '—'}
                      </h2>
                      <div className="flex flex-wrap items-center gap-3 text-xs text-[var(--muted)] mt-1">
                        <span className="font-mono font-bold text-[var(--ink)]" dir="ltr">
                          {profileData.person?.iqama || '—'}
                        </span>
                        <span>•</span>
                        <span>{profileData.person?.phone || 'بدون جوال'}</span>
                        <span>•</span>
                        <span>{profileData.person?.city || 'بدون مدينة'}</span>
                        <span>•</span>
                        <span>{engagementLabel(profileData.person?.engagementType)}</span>
                      </div>
                      <div className="mt-2">
                        <IbanDisplay iban={profileData.person?.iban} masked={false} />
                      </div>
                    </div>
                  </div>
                  {profileData.financialPosition?.statementPath ? (
                    <Link
                      href={inApp(profileData.financialPosition.statementPath)}
                      className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5"
                    >
                      <FileText size={15} />
                      <span>كشف الحساب المالي</span>
                    </Link>
                  ) : (
                    <Link
                      href={`/riders/statement/?iqama=${encodeURIComponent(profileData.person?.iqama || '')}`}
                      className="px-3.5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5"
                    >
                      <FileText size={15} />
                      <span>كشف الحساب المالي</span>
                    </Link>
                  )}
                </div>
              </div>

              <section className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
                <div className="p-4 border-b border-[var(--line)]">
                  <h3 className="font-bold text-sm">سجل العمل — آخر 3 أشهر</h3>
                </div>
                {(profileData.workRecord3Months || []).length === 0 ? (
                  <EmptyState title="لا يوجد سجل عمل" description="لا توجد تعيينات خلال الأشهر الثلاثة الأخيرة." />
                ) : (
                  <DataTable
                    tableId="workforce-360-work-records"
                    data={profileData.workRecord3Months}
                    getRowId={(r: any) => `${r.month ?? ''}-${r.platform ?? ''}-${r.workDays ?? ''}-${r.orders ?? ''}`}
                    searchable={false}
                    exportable={false}
                    syncUrl={false}
                    showTotals={false}
                    showFilterRow={false}
                    columns={[
                      { key: 'month', header: 'الشهر', type: 'date', getValue: (r) => cellText(r.month), render: (r) => <span className="font-mono">{cellText(r.month)}</span> },
                      { key: 'platform', header: 'المنصة', type: 'list', getValue: (r) => cellText(r.platform), render: (r) => cellText(r.platform) },
                      { key: 'workDays', header: 'أيام العمل', type: 'number', numeric: true, getValue: (r) => cellText(r.workDays), render: (r) => <span className="whitespace-pre-wrap">{cellText(r.workDays)}</span> },
                      { key: 'orders', header: 'الطلبات', type: 'number', numeric: true, getValue: (r) => cellText(r.orders), render: (r) => <span className="whitespace-pre-wrap">{cellText(r.orders)}</span> },
                      { key: 'netDue', header: 'صافي المستحق', type: 'number', numeric: true, getValue: (r) => cellText(r.netDue), render: (r) => <span className="whitespace-pre-wrap">{cellText(r.netDue)}</span> },
                      { key: 'status', header: 'الحالة', type: 'list', getValue: (r) => cellText(r.status), toneOf: defaultStatusTone, render: (r) => cellText(r.status) },
                    ]}
                  />
                )}
              </section>

              <section className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
                <h3 className="font-bold text-sm">التقييم المالي</h3>
                <p className="text-sm text-[var(--muted)] whitespace-pre-wrap">
                  {profileData.financialEvaluation?.message || 'لا يوجد مصدر بعد'}
                </p>
              </section>

              <section className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
                <h3 className="font-bold text-sm">صورة الإقامة</h3>
                <p className="text-sm text-[var(--warning)] whitespace-pre-wrap">
                  {profileData.iqamaImage?.message || 'بانتظار صلاحية القراءة من نظام عمر'}
                </p>
              </section>

              <section className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
                <div className="p-4 border-b border-[var(--line)]">
                  <h3 className="font-bold text-sm">سندات الأمر</h3>
                </div>
                {(profileData.promissoryNotes || []).length === 0 ? (
                  <EmptyState title="لا توجد سندات أمر" description="لا توجد ملفات سندات مرتبطة بهذا الشخص." />
                ) : (
                  <DataTable
                    tableId="workforce-360-promissory"
                    data={profileData.promissoryNotes}
                    getRowId={(note: any) => String(note.id || note.noteNumber || `${note.date ?? ''}-${note.amount ?? ''}`)}
                    searchable={false}
                    exportable={false}
                    syncUrl={false}
                    showTotals={false}
                    showFilterRow={false}
                    columns={[
                      { key: 'noteNumber', header: 'الرقم', type: 'text', getValue: (n) => cellText(n.noteNumber || n.id), render: (n) => <span className="font-mono">{cellText(n.noteNumber || n.id)}</span> },
                      { key: 'date', header: 'التاريخ', type: 'date', getValue: (n) => cellText(n.date), render: (n) => <span className="font-mono text-xs">{cellText(n.date)}</span> },
                      { key: 'amount', header: 'المبلغ', type: 'number', numeric: true, getValue: (n) => cellText(n.amount), render: (n) => <span className="whitespace-pre-wrap">{cellText(n.amount)}</span> },
                      { key: 'status', header: 'الحالة', type: 'list', getValue: (n) => cellText(n.status), toneOf: defaultStatusTone, render: (n) => cellText(n.status) },
                    ]}
                  />
                )}
              </section>

              <section className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
                <h3 className="font-bold text-sm">المركز المالي</h3>
                <p className="text-sm text-[var(--muted)]">
                  {profileData.financialPosition?.message || 'افتح كشف الحساب التفصيلي'}
                </p>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)]">المديونيات النشطة</div>
                    <div className="text-base font-bold mt-0.5">
                      {profileData.financialPosition?.activeDebtsCount ?? 0}
                    </div>
                  </div>
                  <div className="p-3 rounded-xl bg-[var(--raised)]/50">
                    <div className="text-[11px] text-[var(--muted)]">إجمالي المتبقي</div>
                    <div className="text-base font-bold num text-[var(--danger)] mt-0.5">
                      {Number(profileData.financialPosition?.totalRemainingDebt ?? 0).toLocaleString('en-US', {
                        minimumFractionDigits: 2,
                      })}{' '}
                      ر.س
                    </div>
                  </div>
                </div>
                <Link
                  href={
                    profileData.financialPosition?.statementPath
                      ? inApp(profileData.financialPosition.statementPath)
                      : `/riders/statement/?iqama=${encodeURIComponent(profileData.person?.iqama || '')}`
                  }
                  className="inline-flex items-center gap-1.5 text-xs font-bold text-[var(--accent)] hover:underline"
                >
                  <FileText size={14} />
                  فتح كشف الحساب
                </Link>
              </section>

              <section className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
                <div className="p-4 border-b border-[var(--line)]">
                  <h3 className="font-bold text-sm">حسابات المنصات</h3>
                </div>
                {(profileData.platformAccounts || []).length === 0 ? (
                  <EmptyState title="لا توجد حسابات منصات" description="لا توجد حسابات مسجّلة باسم هذا الشخص." />
                ) : (
                  <DataTable
                    tableId="workforce-360-platform-accounts"
                    data={profileData.platformAccounts}
                    getRowId={(acc: any) => String(acc.id || `${acc.platform ?? ''}-${acc.externalAccountId ?? ''}`)}
                    searchable={false}
                    exportable={false}
                    syncUrl={false}
                    showTotals={false}
                    showFilterRow={false}
                    columns={[
                      { key: 'platform', header: 'المنصة', type: 'list', getValue: (acc) => cellText(acc.platform), render: (acc) => <span className="font-semibold">{cellText(acc.platform)}</span> },
                      { key: 'externalAccountId', header: 'المعرّف الخارجي', type: 'text', getValue: (acc) => cellText(acc.externalAccountId), render: (acc) => <span className="font-mono" dir="ltr">{cellText(acc.externalAccountId)}</span> },
                      { key: 'city', header: 'المدينة', type: 'list', getValue: (acc) => cellText(acc.city), render: (acc) => cellText(acc.city) },
                      { key: 'createdAt', header: 'تاريخ الإنشاء', type: 'date', getValue: (acc) => cellText(acc.createdAt), render: (acc) => <span className="font-mono text-xs">{cellText(acc.createdAt)}</span> },
                    ]}
                  />
                )}
              </section>
            </div>
          )}
        </div>
      )}

      {activeTab === 'quality' && (
        <div className="space-y-4">
          {qualityError ? (
            <ErrorState title="تعذر تحميل جودة البيانات" message={qualityError} onRetry={loadQuality} />
          ) : (
            <DataTable
              tableId="workforce-quality"
              columns={qualityColumns}
              data={qualityItems}
              loading={loadingQuality}
              emptyTitle="لا توجد مشاكل جودة"
              emptyDescription="لم تُرصد مشاكل في العيّنة الحالية."
              exportFilename="workforce_quality"
              getRowId={(r) => `${r.iqama || 'row'}-${r.issueKind || r.suggestedAction || 'issue'}`}
              searchable
              syncUrl={false}
            />
          )}
        </div>
      )}

      {activeTab === 'saudi' && (
        <div className="space-y-4">
          {saudiError ? (
            <ErrorState title="تعذر تحميل موظفي الرواتب السعوديين" message={saudiError} onRetry={loadSaudi} />
          ) : (
            <DataTable
              tableId="workforce-saudi"
              columns={saudiColumns}
              data={saudiItems}
              loading={loadingSaudi}
              emptyTitle="لا توجد سجلات"
              emptyDescription="لا يوجد موظفو رواتب سعوديون في المصدر الحالي."
              exportFilename="workforce_saudi"
              getRowId={(r) => String(r.id || r.nationalId || r.number)}
              searchable
              syncUrl={false}
            />
          )}
        </div>
      )}

      {activeTab === 'cases' && (
        <div className="space-y-8">
          {casesError ? (
            <ErrorState title="تعذر تحميل الحالات" message={casesError} onRetry={loadCases} />
          ) : (
            <>
              <div className="space-y-3">
                <h3 className="font-bold text-sm text-[var(--ink)]">المنتهون والهاربون</h3>
                <DataTable
                  tableId="workforce-terminated"
                  columns={terminatedColumns}
                  data={terminatedItems}
                  loading={loadingCases}
                  emptyTitle="لا توجد حالات إنهاء"
                  emptyDescription="لا توجد سجلات منتهية أو هاربة في المصدر الحالي."
                  exportFilename="workforce_terminated"
                  getRowId={(r) => String(r.id || r.iqama)}
                  searchable
                  syncUrl={false}
                />
              </div>
              <div className="space-y-3">
                <h3 className="font-bold text-sm text-[var(--ink)]">حالات الغياب</h3>
                <DataTable
                  tableId="workforce-absence"
                  columns={absenceColumns}
                  data={absenceItems}
                  loading={loadingCases}
                  emptyTitle="لا توجد حالات غياب"
                  emptyDescription="لا توجد حالات امتثال غياب في المصدر الحالي."
                  exportFilename="workforce_absence"
                  getRowId={(r) =>
                    String(pickField(r, 'Id', 'id', 'CaseNumber', 'caseNumber') || 'absence')
                  }
                  searchable
                  syncUrl={false}
                />
              </div>
            </>
          )}
        </div>
      )}

      {activeTab === 'legal' && (
        <div className="space-y-4">
          {casesError ? (
            <ErrorState title="تعذر تحميل القضايا" message={casesError} onRetry={loadLegal} />
          ) : (
            <DataTable
              tableId="workforce-legal"
              columns={[
                {
                  key: 'caseNumber',
                  header: 'رقم القضية',
                  type: 'text',
                  getValue: (r) => cellText(r.caseNumber),
                  render: (r) => (
                    <span className="font-mono font-bold whitespace-nowrap">
                      {cellText(r.caseNumber)}
                    </span>
                  ),
                },
                {
                  key: 'personName',
                  header: 'الشخص',
                  type: 'text',
                  getValue: (r) => cellText(r.personName),
                  render: (r) => (
                    <span className="font-semibold whitespace-nowrap">{cellText(r.personName)}</span>
                  ),
                },
                {
                  key: 'iqama',
                  header: 'الإقامة',
                  type: 'text',
                  getValue: (r) => cellText(r.iqama),
                  render: (r) => (
                    <span className="font-mono whitespace-nowrap" dir="ltr">
                      {cellText(r.iqama)}
                    </span>
                  ),
                },
                {
                  key: 'sponsorNameAr',
                  header: 'الكفيل',
                  type: 'list',
                  getValue: (r) => cellText(r.sponsorNameAr),
                  render: (r) => (
                    <span className="whitespace-nowrap">{cellText(r.sponsorNameAr)}</span>
                  ),
                },
                {
                  key: 'status',
                  header: 'الحالة',
                  type: 'list',
                  getValue: (r) => cellText(r.status),
                  toneOf: defaultStatusTone,
                  render: (r) => <span className="whitespace-nowrap">{cellText(r.status)}</span>,
                },
              ]}
              data={legalItems}
              loading={loadingCases}
              emptyTitle="لا توجد قضايا"
              emptyDescription="لا توجد قضايا قانونية في المصدر الحالي، أو لا يوجد مصدر بعد."
              exportFilename="workforce_legal"
              getRowId={(r) => String(r.id || r.caseNumber)}
              searchable
              syncUrl={false}
            />
          )}
        </div>
      )}
    </div>
  );
}

export default function WorkforcePage() {
  return (
    <Suspense
      fallback={
        <div className="p-8 space-y-4">
          <Skeleton className="h-10 w-64" />
          <Skeleton className="h-32 w-full" />
        </div>
      }
    >
      <WorkforcePageInner />
    </Suspense>
  );
}
