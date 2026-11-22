'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  BarChart3,
  Copy,
  Download,
  Eye,
  FileUp,
  PauseCircle,
  PlayCircle,
  Plus,
  RefreshCw,
  ShieldAlert,
  Smartphone,
  Star,
  Users,
  XCircle,
} from 'lucide-react';
import {
  api,
  type EvaluationBucketItem,
  type EvaluationReplyItem,
  type EvaluationRoundItem,
  type EvaluationStaffItem,
  type EvaluationStatisticsResponse,
} from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { DataTable, type ColumnDef, type ServerTableQuery } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { Drawer } from '../../components/ui/Drawer';
import { ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { StatCard } from '../../components/ui/StatCard';
import { useToast } from '../../components/ui/Toast';
import {
  platformDisplayName,
  STAFF_DIMENSION_LABELS_AR,
  STAFF_SKIP_REASON_LABELS_AR,
  APP_SKIP_REASON_LABELS_AR,
  FACTOR_LABELS_AR,
  ROUND_STATUS_LABELS_AR,
} from './evaluation-helpers';

export {
  STAFF_DIMENSION_LABELS_AR,
  STAFF_SKIP_REASON_LABELS_AR,
  APP_SKIP_REASON_LABELS_AR,
  FACTOR_LABELS_AR,
  ROUND_STATUS_LABELS_AR,
};

export type EvaluationDimensionKey =
  | 'all'
  | 'city'
  | 'app'
  | 'supervisor'
  | 'nationality'
  | 'factors'
  | 'trend';

type SupervisorTableRow = {
  id: string;
  name: string;
  citiesText: string;
  departmentsText: string;
  appsText: string;
  rolesText: string;
  pendingRoleApproval: boolean;
  ratedCount: number;
  skippedCount: number;
  average: number | null;
  complaintCount: number;
  professionalAvg: number | null;
  treatmentAvg: number | null;
  supportAvg: number | null;
  responseSpeedAvg: number | null;
  problemSolvingAvg: number | null;
  reasonsText: string;
};

function pctWidth(value: number | null | undefined, max: number): number {
  if (value == null || max <= 0) return 0;
  return Math.max(0, Math.min(100, Math.round((Number(value) / max) * 100)));
}

export function EvaluationsTab() {
  const { toast } = useToast();
  const { can, isAdmin, canManageForms, user } = useAuth();

  const isSupervisorRole = Boolean(
    user?.role && user.role.toLowerCase().includes('supervisor')
  );
  const canViewEvaluations =
    !isSupervisorRole && (isAdmin || can('forms.evaluations.view'));
  const canExportEvaluations =
    canViewEvaluations && (isAdmin || can('forms.evaluations.export'));

  const [staffList, setStaffList] = useState<EvaluationStaffItem[]>([]);
  const [rounds, setRounds] = useState<EvaluationRoundItem[]>([]);
  const [selectedRoundId, setSelectedRoundId] = useState<string>('');
  const [stats, setStats] = useState<EvaluationStatisticsResponse | null>(null);
  const [dimensionFocus, setDimensionFocus] = useState<EvaluationDimensionKey>('all');
  const [dimensionItems, setDimensionItems] = useState<EvaluationBucketItem[] | null>(null);

  const [responses, setResponses] = useState<EvaluationReplyItem[]>([]);
  const [responsesTotal, setResponsesTotal] = useState(0);
  const [responsesPage, setResponsesPage] = useState(1);
  const [responsesPageSize, setResponsesPageSize] = useState(50);
  const [responsesServerQuery, setResponsesServerQuery] = useState<ServerTableQuery>({
    filters: [],
    sort: [],
  });

  const [loading, setLoading] = useState(false);
  const [responsesLoading, setResponsesLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Dimension filters
  const [cityFilter, setCityFilter] = useState<string>('ALL');
  const [appFilter, setAppFilter] = useState<string>('ALL');
  const [supervisorFilter, setSupervisorFilter] = useState<string>('ALL');
  const [departmentFilter, setDepartmentFilter] = useState<string>('ALL');
  const [nationalityFilter, setNationalityFilter] = useState<string>('ALL');

  // Dialogs & Drawers
  const [importOpen, setImportOpen] = useState(false);
  const [importJsonText, setImportJsonText] = useState('');
  const [importing, setImporting] = useState(false);

  const [createOpen, setCreateOpen] = useState(false);
  const [newRoundTitle, setNewRoundTitle] = useState('');
  const [creatingRound, setCreatingRound] = useState(false);
  const [transitioningAction, setTransitioningAction] = useState<string | null>(null);
  const [exportingExcel, setExportingExcel] = useState(false);
  const [inspectReply, setInspectReply] = useState<EvaluationReplyItem | null>(null);

  const selectedRound = useMemo(() => {
    if (rounds.length === 0) return null;
    return (
      rounds.find((r) => r.id === selectedRoundId) ??
      rounds.find((r) => r.status === 'open') ??
      rounds[0]
    );
  }, [rounds, selectedRoundId]);

  const loadRoundsAndStaff = useCallback(async () => {
    if (!canViewEvaluations) return;
    setLoading(true);
    setError(null);
    try {
      const [loadedStaff, loadedRounds] = await Promise.all([
        api.getEvaluationStaff(),
        api.getEvaluationRounds(),
      ]);
      setStaffList(Array.isArray(loadedStaff) ? loadedStaff : []);
      const rList = Array.isArray(loadedRounds) ? loadedRounds : [];
      setRounds(rList);
      if (rList.length > 0 && !selectedRoundId) {
        const preferred = rList.find((r) => r.status === 'open') ?? rList[0];
        setSelectedRoundId(preferred.id);
      }
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل بيانات التقييمات');
    } finally {
      setLoading(false);
    }
  }, [canViewEvaluations, selectedRoundId]);

  const loadRoundDetails = useCallback(async () => {
    if (!canViewEvaluations || !selectedRound) return;
    setResponsesLoading(true);
    try {
      const statsFilters = {
        city: cityFilter !== 'ALL' ? cityFilter : undefined,
        nationalityId: nationalityFilter !== 'ALL' ? nationalityFilter : undefined,
      };
      const [loadedStats, loadedResponses] = await Promise.all([
        api.getEvaluationStatistics(selectedRound.id, statsFilters),
        api.getEvaluationResponses(selectedRound.id, {
          page: responsesPage,
          pageSize: responsesPageSize,
          filters: responsesServerQuery.filtersParam,
          sort: responsesServerQuery.sortParam,
        }),
      ]);
      setStats(loadedStats);
      setResponses(loadedResponses.items || []);
      setResponsesTotal(loadedResponses.total || 0);
    } catch (err: any) {
      toast(err?.message || 'تعذر تحميل إحصائيات وردود الجولة', 'danger');
    } finally {
      setResponsesLoading(false);
    }
  }, [
    canViewEvaluations,
    selectedRound,
    responsesPage,
    responsesPageSize,
    responsesServerQuery.filtersParam,
    responsesServerQuery.sortParam,
    cityFilter,
    nationalityFilter,
    toast,
  ]);

  useEffect(() => {
    void loadRoundsAndStaff();
  }, [loadRoundsAndStaff]);

  useEffect(() => {
    void loadRoundDetails();
  }, [loadRoundDetails]);

  useEffect(() => {
    if (!canViewEvaluations || !selectedRound || dimensionFocus === 'all') {
      setDimensionItems(null);
      return;
    }
    const statsFilters = {
      city: cityFilter !== 'ALL' ? cityFilter : undefined,
      nationalityId: nationalityFilter !== 'ALL' ? nationalityFilter : undefined,
    };
    api
      .getEvaluationStatisticsByDimension(selectedRound.id, dimensionFocus, statsFilters)
      .then((res) => setDimensionItems(res.items || []))
      .catch(() => setDimensionItems(null));
  }, [canViewEvaluations, selectedRound, dimensionFocus, cityFilter, nationalityFilter]);

  if (!canViewEvaluations) {
    return (
      <ErrorState
        title="التقييمات سرية ومتاحة للإدارة المخولة فقط"
        message="لا يطلع المشرفون أو غير المخولين بصلاحية عرض التقييمات على نتائج أو ردود التقييم."
      />
    );
  }

  const staffById = useMemo(() => {
    const map = new Map<string, EvaluationRoundItem['staff'][number]>();
    for (const s of selectedRound?.staff || staffList) {
      map.set(s.id, s);
    }
    return map;
  }, [selectedRound, staffList]);

  const roundCities = useMemo(() => {
    const set = new Set<string>(selectedRound?.cities || []);
    for (const b of stats?.byCity || []) {
      if (b.key) set.add(b.key);
    }
    return [...set];
  }, [selectedRound, stats]);

  const roundApps = useMemo(() => {
    const set = new Set<string>(selectedRound?.apps || []);
    for (const b of stats?.byApp || []) {
      if (b.key) set.add(b.key);
    }
    return [...set];
  }, [selectedRound, stats]);

  const roundDepartments = useMemo(() => {
    const set = new Set<string>();
    for (const s of selectedRound?.staff || staffList) {
      for (const d of s.departments || []) {
        if (d) set.add(d);
      }
    }
    return [...set];
  }, [selectedRound, staffList]);

  const roundNationalities = useMemo(() => {
    const set = new Set<string>();
    for (const b of stats?.byNationality || []) {
      if (b.key) set.add(b.key);
    }
    return [...set];
  }, [stats]);

  // Filtered chart buckets
  const filteredByCity = useMemo(() => {
    const source =
      dimensionFocus === 'city' && dimensionItems ? dimensionItems : stats?.byCity || [];
    return source.filter((b) => cityFilter === 'ALL' || b.key === cityFilter);
  }, [stats, dimensionFocus, dimensionItems, cityFilter]);

  const filteredByApp = useMemo(() => {
    const source =
      dimensionFocus === 'app' && dimensionItems ? dimensionItems : stats?.byApp || [];
    return source.filter((b) => appFilter === 'ALL' || b.key === appFilter);
  }, [stats, dimensionFocus, dimensionItems, appFilter]);

  const filteredByNationality = useMemo(() => {
    const source =
      dimensionFocus === 'nationality' && dimensionItems
        ? dimensionItems
        : stats?.byNationality || [];
    return source.filter(
      (b) => nationalityFilter === 'ALL' || b.key === nationalityFilter
    );
  }, [stats, dimensionFocus, dimensionItems, nationalityFilter]);

  const filteredCommonFactors = useMemo(() => {
    const source =
      dimensionFocus === 'factors' && dimensionItems
        ? dimensionItems
        : stats?.commonFactors || [];
    return source.filter((b) => appFilter === 'ALL' || !b.app || b.app === appFilter);
  }, [stats, dimensionFocus, dimensionItems, appFilter]);

  const filteredDailyTrend = useMemo(() => {
    return dimensionFocus === 'trend' && dimensionItems
      ? dimensionItems
      : stats?.dailyTrend || [];
  }, [stats, dimensionFocus, dimensionItems]);

  const supervisorRows = useMemo((): SupervisorTableRow[] => {
    const rawBuckets =
      dimensionFocus === 'supervisor' && dimensionItems
        ? dimensionItems
        : stats?.bySupervisor || [];
    const bucketByStaffId = new Map<string, EvaluationBucketItem>();
    for (const b of rawBuckets) {
      bucketByStaffId.set(b.key, b);
    }

    const candidates =
      selectedRound?.staff && selectedRound.staff.length > 0
        ? selectedRound.staff
        : staffList.filter((s) => !s.excluded);

    return candidates
      .filter((s) => {
        if (supervisorFilter !== 'ALL' && s.id !== supervisorFilter) return false;
        if (cityFilter !== 'ALL' && !(s.cities || []).includes(cityFilter)) return false;
        if (appFilter !== 'ALL' && !(s.apps || []).includes(appFilter)) return false;
        if (
          departmentFilter !== 'ALL' &&
          !(s.departments || []).includes(departmentFilter)
        ) {
          return false;
        }
        return true;
      })
      .map((s) => {
        const b = bucketByStaffId.get(s.id);
        const dimMap = new Map<string, number | null>();
        for (const d of b?.dimensions || []) {
          dimMap.set(d.key, d.average ?? null);
        }
        const roles = s.pendingRoleApproval ? ['بانتظار موافقة غزال'] : s.roles || [];
        const reasons = (b?.reasons || [])
          .map(
            (r) =>
              `${STAFF_SKIP_REASON_LABELS_AR[r.key] || r.key} (${r.count})`
          )
          .join('، ');
        return {
          id: s.id,
          name: s.name,
          citiesText: (s.cities || []).join('، '),
          departmentsText: (s.departments || []).join('، '),
          appsText: (s.apps || []).map((a) => platformDisplayName(a, 'ar')).join('، '),
          rolesText: roles.join(' · '),
          pendingRoleApproval: Boolean(s.pendingRoleApproval),
          ratedCount: Number(b?.ratedCount ?? 0),
          skippedCount: Number(b?.skippedCount ?? 0),
          average: b?.average ?? null,
          complaintCount: Number(b?.complaintCount ?? 0),
          professionalAvg: dimMap.get('professional') ?? null,
          treatmentAvg: dimMap.get('treatment') ?? null,
          supportAvg: dimMap.get('support') ?? null,
          responseSpeedAvg: dimMap.get('responseSpeed') ?? null,
          problemSolvingAvg: dimMap.get('problemSolving') ?? null,
          reasonsText: reasons || '—',
        };
      });
  }, [
    stats,
    dimensionFocus,
    dimensionItems,
    selectedRound,
    staffList,
    supervisorFilter,
    cityFilter,
    appFilter,
    departmentFilter,
  ]);

  // Overall KPIs
  const overallStaffAverage = useMemo(() => {
    const rated = supervisorRows.filter(
      (r) => r.average != null && r.ratedCount > 0
    );
    if (rated.length === 0) return null;
    const totalWeight = rated.reduce((acc, r) => acc + r.ratedCount, 0);
    if (totalWeight === 0) return null;
    const weighted = rated.reduce(
      (acc, r) => acc + Number(r.average) * r.ratedCount,
      0
    );
    return Math.round((weighted / totalWeight) * 100) / 100;
  }, [supervisorRows]);

  const overallAppAverage = useMemo(() => {
    const rated = filteredByApp.filter(
      (a) => a.average != null && Number(a.ratedCount ?? 0) > 0
    );
    if (rated.length === 0) return null;
    const totalWeight = rated.reduce(
      (acc, a) => acc + Number(a.ratedCount ?? 0),
      0
    );
    if (totalWeight === 0) return null;
    const weighted = rated.reduce(
      (acc, a) => acc + Number(a.average) * Number(a.ratedCount ?? 0),
      0
    );
    return Math.round((weighted / totalWeight) * 100) / 100;
  }, [filteredByApp]);

  const totalComplaints = useMemo(() => {
    return (stats?.bySupervisor || []).reduce(
      (acc, b) => acc + Number(b.complaintCount ?? 0),
      0
    );
  }, [stats]);

  const handleStaffFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      const text = await file.text();
      setImportJsonText(text);
    } catch {
      toast('تعذر قراءة ملف الموظفين المختار', 'danger');
    }
  };

  const handleImportStaff = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!importJsonText.trim()) return;
    setImporting(true);
    try {
      const parsed = JSON.parse(importJsonText);
      const staffArray = Array.isArray(parsed)
        ? parsed
        : Array.isArray(parsed?.staff)
        ? parsed.staff
        : null;
      if (!staffArray || staffArray.length === 0) {
        toast('ملف JSON يجب أن يحتوي على مصفوفة staff لبطاقات الموظفين.', 'danger');
        setImporting(false);
        return;
      }
      const res = await api.importEvaluationStaff(staffArray);
      toast(
        `تم استيراد ${res.imported} بطاقة موظف بنجاح (منها ${res.excluded} مستبعدة من العرض للمناديب)`,
        'success'
      );
      setImportOpen(false);
      setImportJsonText('');
      await loadRoundsAndStaff();
    } catch (err: any) {
      toast(err?.message || 'تعذر استيراد بطاقات الموظفين', 'danger');
    } finally {
      setImporting(false);
    }
  };

  const handleCreateRound = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newRoundTitle.trim()) return;
    setCreatingRound(true);
    try {
      const created = await api.createEvaluationRound(newRoundTitle.trim());
      toast('تم إنشاء جولة التقييم بنجاح', 'success');
      setCreateOpen(false);
      setNewRoundTitle('');
      setSelectedRoundId(created.id);
      await loadRoundsAndStaff();
    } catch (err: any) {
      toast(err?.message || 'تعذر إنشاء جولة التقييم', 'danger');
    } finally {
      setCreatingRound(false);
    }
  };

  const handleTransitionRound = async (action: 'open' | 'pause' | 'close') => {
    if (!selectedRound) return;
    setTransitioningAction(action);
    try {
      await api.transitionEvaluationRound(selectedRound.id, action);
      const msg =
        action === 'open'
          ? 'تم فتح جولة التقييم لاستقبال ردود المناديب'
          : action === 'pause'
          ? 'تم إيقاف جولة التقييم مؤقتًا'
          : 'تم إغلاق جولة التقييم نهائيًا';
      toast(msg, 'success');
      await loadRoundsAndStaff();
      await loadRoundDetails();
    } catch (err: any) {
      toast(err?.message || 'تعذر تحديث حالة الجولة', 'danger');
    } finally {
      setTransitioningAction(null);
    }
  };

  const handleExportWorkbook = async () => {
    if (!selectedRound) return;
    setExportingExcel(true);
    try {
      const blob = await api.exportEvaluationWorkbook(selectedRound.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      const safeTitle = (selectedRound.title || 'evaluations')
        .replace(/[\\/:*?"<>|]+/g, '-')
        .slice(0, 60);
      a.download = `${safeTitle}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      toast('تم تنزيل ملف إكسل الشامل للتقييمات بنجاح', 'success');
    } catch (err: any) {
      toast(err?.message || 'تعذر تصدير ملف التقييمات', 'danger');
    } finally {
      setExportingExcel(false);
    }
  };

  const buildPublicEvaluationUrl = (round: EvaluationRoundItem | null): string => {
    if (!round?.sharingToken) return '';
    const origin = typeof window !== 'undefined' ? window.location.origin : '';
    return `${origin}/rider-form.html?evaluation=${encodeURIComponent(round.sharingToken)}`;
  };

  const copyPublicLink = async () => {
    const link = buildPublicEvaluationUrl(selectedRound);
    if (!link) return;
    try {
      await navigator.clipboard.writeText(link);
      toast('تم نسخ رابط نموذج التقييم إلى الحافظة', 'success');
    } catch {
      window.prompt('انسخ رابط التقييم:', link);
    }
  };

  const supervisorColumns: ColumnDef<SupervisorTableRow>[] = [
    {
      key: 'name',
      header: 'المشرف / الموظف',
      type: 'text',
      filterType: 'text',
      getValue: (r) => r.name,
      render: (r) => (
        <div className="flex flex-col">
          <strong className="font-bold text-[var(--ink)]">{r.name}</strong>
          <span className="text-[11px] text-[var(--muted)]">{r.rolesText || '—'}</span>
        </div>
      ),
    },
    {
      key: 'citiesText',
      header: 'المدن',
      type: 'list',
      filterType: 'list',
      getValue: (r) => r.citiesText,
    },
    {
      key: 'departmentsText',
      header: 'الأقسام',
      type: 'list',
      filterType: 'list',
      getValue: (r) => r.departmentsText,
    },
    {
      key: 'appsText',
      header: 'المنصات',
      type: 'list',
      filterType: 'list',
      getValue: (r) => r.appsText || '—',
    },
    {
      key: 'average',
      header: 'المتوسط العام (من 10)',
      type: 'number',
      numeric: true,
      getValue: (r) => r.average,
      render: (r) =>
        r.average == null ? (
          <span className="text-[var(--muted)]">—</span>
        ) : (
          <span className="num font-bold text-[var(--accent)]">{r.average} / 10</span>
        ),
    },
    {
      key: 'ratedCount',
      header: 'عدد المقيمين',
      type: 'number',
      numeric: true,
      summary: 'sum',
      getValue: (r) => r.ratedCount,
    },
    {
      key: 'skippedCount',
      header: 'عدم التعامل / التخطي',
      type: 'number',
      numeric: true,
      summary: 'sum',
      getValue: (r) => r.skippedCount,
      render: (r) => (
        <span className="text-xs" title={r.reasonsText}>
          <b className="num">{r.skippedCount}</b>
          {r.skippedCount > 0 && (
            <span className="text-[11px] text-[var(--muted)]"> ({r.reasonsText})</span>
          )}
        </span>
      ),
    },
    {
      key: 'complaintCount',
      header: 'الشكاوى السرية',
      type: 'number',
      numeric: true,
      summary: 'sum',
      getValue: (r) => r.complaintCount,
      render: (r) =>
        r.complaintCount > 0 ? (
          <span className="rounded-full bg-[var(--danger)]/15 px-2 py-0.5 text-xs font-bold text-[var(--danger)] num">
            {r.complaintCount}
          </span>
        ) : (
          <span className="num text-xs text-[var(--muted)]">0</span>
        ),
    },
  ];

  const responseColumns: ColumnDef<EvaluationReplyItem>[] = [
    {
      key: 'name',
      header: 'اسم المندوب',
      type: 'text',
      filterType: 'text',
      getValue: (r) => r.name,
      render: (r) => (
        <div className="flex flex-col">
          <strong className="font-bold text-[var(--ink)]">{r.name}</strong>
          {r.identityVerified && (
            <span className="text-[10px] font-semibold text-[var(--money-pos)]">
              ✓ موثق من تطبيق المناديب
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'iqama',
      header: 'رقم الإقامة',
      type: 'text',
      filterType: 'text',
      getValue: (r) => r.iqama,
      render: (r) => <span className="num font-mono text-xs font-bold">{r.iqama}</span>,
    },
    {
      key: 'city',
      header: 'المدينة',
      type: 'list',
      filterType: 'list',
      getValue: (r) => r.city,
    },
    {
      key: 'nationality',
      header: 'الجنسية',
      type: 'list',
      filterType: 'list',
      getValue: (r) => r.nationality,
    },
    {
      key: 'workApps',
      header: 'منصات العمل',
      type: 'list',
      filterType: 'list',
      getValue: (r) =>
        (r.workApps || []).map((a) => platformDisplayName(a, 'ar')).join('، '),
    },
    {
      key: 'phone',
      header: 'الجوال',
      type: 'text',
      filterType: 'text',
      getValue: (r) => r.phone || '',
      render: (r) =>
        r.phone ? (
          <bdi dir="ltr" className="num font-mono text-xs">
            {r.phone}
          </bdi>
        ) : (
          <span className="text-[var(--muted)]">—</span>
        ),
    },
    {
      key: 'createdAt',
      header: 'تاريخ الإرسال',
      type: 'date',
      getValue: (r) => r.createdAt,
      render: (r) => (
        <span className="num text-xs text-[var(--muted)]">
          {r.createdAt ? new Date(r.createdAt).toLocaleDateString('ar-SA') : '—'}
        </span>
      ),
    },
  ];

  const staffDirectoryColumns: ColumnDef<EvaluationStaffItem>[] = [
    {
      key: 'name',
      header: 'اسم الموظف',
      type: 'text',
      filterType: 'text',
      getValue: (s) => s.name,
    },
    {
      key: 'cities',
      header: 'المدن',
      type: 'list',
      filterType: 'list',
      getValue: (s) => (s.cities || []).join('، '),
    },
    {
      key: 'departments',
      header: 'الأقسام',
      type: 'list',
      filterType: 'list',
      getValue: (s) => (s.departments || []).join('، '),
    },
    {
      key: 'apps',
      header: 'المنصات',
      type: 'list',
      filterType: 'list',
      getValue: (s) =>
        (s.apps || []).map((a) => platformDisplayName(a, 'ar')).join('، ') || '—',
    },
    {
      key: 'roles',
      header: 'المهام والأدوار',
      type: 'text',
      filterType: 'text',
      getValue: (s) =>
        s.pendingRoleApproval
          ? 'بانتظار موافقة غزال'
          : (s.roles || []).join(' · '),
    },
    {
      key: 'status',
      header: 'حالة البطاقة',
      type: 'list',
      filterType: 'list',
      getValue: (s) => (s.excluded ? 'مستبعد من التقييم' : 'بطاقة نشطة'),
    },
  ];

  const allowedRoundActions = selectedRound?.allowedActions || [];

  return (
    <div className="space-y-6" data-testid="evaluations-admin-page">
      {/* Top Management & Round Action Bar */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2.5">
            <SearchableSelect
              id="select-evaluation-round"
              value={selectedRound?.id ?? ''}
              onChange={(id) => {
                setSelectedRoundId(id);
                setResponsesPage(1);
              }}
              ariaLabel="اختيار جولة التقييم"
              placeholder="اختر جولة التقييم"
              className="w-72"
              options={rounds.map((r) => ({
                value: r.id,
                label: `${r.title} (${ROUND_STATUS_LABELS_AR[r.status] || r.status})`,
              }))}
            />

            {selectedRound && (
              <span
                className={`px-2.5 py-1 rounded-full text-xs font-bold ${
                  selectedRound.status === 'open'
                    ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                    : selectedRound.status === 'paused'
                    ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                    : 'bg-[var(--raised)] text-[var(--muted)]'
                }`}
              >
                {ROUND_STATUS_LABELS_AR[selectedRound.status] || selectedRound.status}
              </span>
            )}

            {/* Round transition buttons driven strictly by allowedActions */}
            {allowedRoundActions.includes('open') && (
              <button
                type="button"
                disabled={transitioningAction !== null}
                onClick={() => handleTransitionRound('open')}
                data-testid="eval-round-open-btn"
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--money-pos)] px-3 py-1.5 text-xs font-bold text-white"
              >
                <PlayCircle size={14} />
                <span>فتح الجولة</span>
              </button>
            )}

            {allowedRoundActions.includes('pause') && (
              <button
                type="button"
                disabled={transitioningAction !== null}
                onClick={() => handleTransitionRound('pause')}
                data-testid="eval-round-pause-btn"
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--warning)] px-3 py-1.5 text-xs font-bold text-white"
              >
                <PauseCircle size={14} />
                <span>إيقاف مؤقت</span>
              </button>
            )}

            {allowedRoundActions.includes('close') && (
              <button
                type="button"
                disabled={transitioningAction !== null}
                onClick={() => handleTransitionRound('close')}
                data-testid="eval-round-close-btn"
                className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--danger)] px-3 py-1.5 text-xs font-bold text-white"
              >
                <XCircle size={14} />
                <span>إغلاق الجولة</span>
              </button>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {selectedRound && (
              <>
                <button
                  type="button"
                  onClick={copyPublicLink}
                  data-testid="eval-copy-link-btn"
                  className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--line)] bg-[var(--raised)] px-3 py-1.5 text-xs font-bold text-[var(--ink)]"
                >
                  <Copy size={14} />
                  <span>نسخ رابط التقييم</span>
                </button>
                <button
                  type="button"
                  onClick={() => {
                    const link = buildPublicEvaluationUrl(selectedRound);
                    if (link) {
                      window.open(
                        link,
                        '_blank',
                        'popup,width=360,height=780,noopener,noreferrer'
                      );
                    }
                  }}
                  data-testid="eval-preview-mobile-btn"
                  className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] px-3 py-1.5 text-xs font-semibold text-[var(--ink)]"
                >
                  <Smartphone size={14} />
                  <span>معاينة الجوال</span>
                </button>
              </>
            )}

            {canExportEvaluations && selectedRound && (
              <button
                type="button"
                disabled={exportingExcel}
                onClick={handleExportWorkbook}
                data-testid="eval-export-excel-btn"
                className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--accent)] bg-[var(--surface)] px-3 py-1.5 text-xs font-bold text-[var(--accent)]"
              >
                <Download size={14} />
                <span>{exportingExcel ? 'جاري التصدير…' : 'تصدير إكسل شامل'}</span>
              </button>
            )}

            {canManageForms && (
              <>
                <button
                  type="button"
                  onClick={() => setImportOpen(true)}
                  data-testid="eval-import-staff-btn"
                  className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] px-3 py-1.5 text-xs font-bold text-[var(--ink)]"
                >
                  <FileUp size={14} />
                  <span>استيراد بطاقات الموظفين</span>
                </button>
                <button
                  type="button"
                  onClick={() => setCreateOpen(true)}
                  data-testid="eval-create-round-btn"
                  className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--accent)] px-3 py-1.5 text-xs font-bold text-[var(--accent-ink)]"
                >
                  <Plus size={14} />
                  <span>إنشاء جولة تقييم</span>
                </button>
              </>
            )}

            <button
              type="button"
              onClick={() => {
                void loadRoundsAndStaff();
                void loadRoundDetails();
              }}
              className="p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)]"
              title="تحديث النتائج"
            >
              <RefreshCw size={14} className={loading || responsesLoading ? 'animate-spin' : ''} />
            </button>
          </div>
        </div>

        {/* Multi-dimension Filters */}
        <div
          className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-2.5 pt-2 border-t border-[var(--line)]"
          data-testid="eval-filters-bar"
        >
          <SearchableSelect
            id="eval-filter-city"
            value={cityFilter}
            onChange={setCityFilter}
            ariaLabel="تصفية حسب المدينة"
            placeholder="كل المدن"
            options={[
              { value: 'ALL', label: 'كل المدن' },
              ...roundCities.map((c) => ({ value: c, label: c })),
            ]}
          />
          <SearchableSelect
            id="eval-filter-app"
            value={appFilter}
            onChange={setAppFilter}
            ariaLabel="تصفية حسب المنصة"
            placeholder="كل المنصات"
            options={[
              { value: 'ALL', label: 'كل المنصات' },
              ...roundApps.map((a) => ({
                value: a,
                label: platformDisplayName(a, 'ar'),
              })),
            ]}
          />
          <SearchableSelect
            id="eval-filter-supervisor"
            value={supervisorFilter}
            onChange={setSupervisorFilter}
            ariaLabel="تصفية حسب المشرف أو الموظف"
            placeholder="كل المشرفين والموظفين"
            options={[
              { value: 'ALL', label: 'كل المشرفين والموظفين' },
              ...(selectedRound?.staff || staffList)
                .filter((s) => !s.excluded)
                .map((s) => ({ value: s.id, label: s.name })),
            ]}
          />
          <SearchableSelect
            id="eval-filter-department"
            value={departmentFilter}
            onChange={setDepartmentFilter}
            ariaLabel="تصفية حسب القسم"
            placeholder="كل الأقسام"
            options={[
              { value: 'ALL', label: 'كل الأقسام' },
              ...roundDepartments.map((d) => ({ value: d, label: d })),
            ]}
          />
          <SearchableSelect
            id="eval-filter-nationality"
            value={nationalityFilter}
            onChange={setNationalityFilter}
            ariaLabel="تصفية حسب الجنسية"
            placeholder="كل الجنسيات"
            options={[
              { value: 'ALL', label: 'كل الجنسيات' },
              ...roundNationalities.map((n) => ({ value: n, label: n })),
            ]}
          />
        </div>

        {/* Dimension Drill-down Pills */}
        <div className="flex flex-wrap items-center gap-1.5 pt-1">
          {(
            [
              ['all', 'نظرة شاملة'],
              ['city', 'حسب المدينة'],
              ['app', 'حسب المنصة'],
              ['supervisor', 'حسب المشرف والموظف'],
              ['nationality', 'حسب الجنسية'],
              ['factors', 'العوامل الشائعة'],
              ['trend', 'الاتجاه اليومي'],
            ] as [EvaluationDimensionKey, string][]
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              data-dimension={key}
              onClick={() => setDimensionFocus(key)}
              className={`px-3 py-1 rounded-full text-xs font-bold transition ${
                dimensionFocus === key
                  ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                  : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      {error ? (
        <ErrorState
          title="تعذر تحميل نتائج التقييم"
          message={error}
          onRetry={loadRoundsAndStaff}
        />
      ) : (
        <>
          {/* KPI Summary Cards */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              label="إجمالي الردود المستلمة"
              value={stats?.totalResponses ?? 0}
              hint={selectedRound?.title || 'في الجولة المحددة'}
              icon={BarChart3}
            />
            <StatCard
              label="متوسط تقييم المشرفين والموظفين"
              value={overallStaffAverage != null ? `${overallStaffAverage} / 10` : '—'}
              hint="عبر الأبعاد المهنية والسلوكية الخمسة"
              icon={Users}
            />
            <StatCard
              label="متوسط تقييم المنصات"
              value={overallAppAverage != null ? `${overallAppAverage} / 5 ★` : '—'}
              hint="تقييم المنصات بالنجوم من 1 إلى 5"
              icon={Star}
            />
            <StatCard
              label="الشكاوى السرية المسجلة"
              value={totalComplaints}
              hint="تُرحّل تلقائيًا إلى صندوق الشكاوى السرية للإدارة"
              icon={ShieldAlert}
            />
          </div>

          {/* Charts Grid: City & Nationality */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* 1. By City Chart */}
            <section
              className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-4"
              data-testid="eval-chart-by-city"
            >
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-[var(--ink)]">
                  الإحصائيات والرسوم البيانية حسب المدينة
                </h3>
                <span className="text-xs text-[var(--muted)] num">
                  {filteredByCity.length} مدن
                </span>
              </div>
              {filteredByCity.length === 0 ? (
                <p className="text-xs text-[var(--muted)]">لا توجد ردود مسجلة حسب المدن بعد.</p>
              ) : (
                <div className="space-y-3">
                  {filteredByCity.map((c) => (
                    <div
                      key={c.key}
                      className="rounded-xl border border-[var(--line)] bg-[var(--bg)] p-3 space-y-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
                        <strong className="text-sm text-[var(--ink)]">{c.key}</strong>
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 font-bold num">
                            {c.responseCount ?? 0} رد
                          </span>
                          {(c.complaintCount ?? 0) > 0 && (
                            <span className="rounded-full bg-[var(--danger)]/15 px-2 py-0.5 font-bold text-[var(--danger)] num">
                              {c.complaintCount} شكوى
                            </span>
                          )}
                        </div>
                      </div>
                      <div className="space-y-1">
                        <div className="flex justify-between text-[11px]">
                          <span className="text-[var(--muted)]">متوسط المشرفين والموظفين</span>
                          <span className="num font-bold">
                            {c.staffAverage != null ? `${c.staffAverage} / 10` : '—'}
                          </span>
                        </div>
                        <div className="w-full h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                          <div
                            className="h-full bg-[var(--accent)]"
                            style={{ width: `${pctWidth(c.staffAverage, 10)}%` }}
                          />
                        </div>
                      </div>
                      <div className="space-y-1">
                        <div className="flex justify-between text-[11px]">
                          <span className="text-[var(--muted)]">متوسط المنصات</span>
                          <span className="num font-bold">
                            {c.appAverage != null ? `${c.appAverage} / 5 ★` : '—'}
                          </span>
                        </div>
                        <div className="w-full h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                          <div
                            className="h-full bg-[var(--money-pos)]"
                            style={{ width: `${pctWidth(c.appAverage, 5)}%` }}
                          />
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>

            {/* 2. By Nationality Chart */}
            <section
              className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-4"
              data-testid="eval-chart-by-nationality"
            >
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-[var(--ink)]">
                  التوزيع والتقييم حسب جنسية المندوب
                </h3>
                <span className="text-xs text-[var(--muted)] num">
                  {filteredByNationality.length} جنسيات
                </span>
              </div>
              {filteredByNationality.length === 0 ? (
                <p className="text-xs text-[var(--muted)]">
                  لا توجد بيانات جنسيات مسجلة في الردود بعد.
                </p>
              ) : (
                <div className="space-y-3">
                  {filteredByNationality.map((n) => (
                    <div
                      key={n.key}
                      className="rounded-xl border border-[var(--line)] bg-[var(--bg)] p-3 space-y-2"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
                        <strong className="text-sm text-[var(--ink)]">{n.key}</strong>
                        <div className="flex items-center gap-2">
                          <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 font-bold num">
                            {n.responseCount ?? 0} مندوب
                          </span>
                          {(n.complaintCount ?? 0) > 0 && (
                            <span className="rounded-full bg-[var(--danger)]/15 px-2 py-0.5 font-bold text-[var(--danger)] num">
                              {n.complaintCount} شكوى
                            </span>
                          )}
                        </div>
                      </div>
                      <div className="grid grid-cols-2 gap-3 text-[11px]">
                        <div>
                          <div className="flex justify-between mb-1">
                            <span className="text-[var(--muted)]">تقييم المشرفين</span>
                            <span className="num font-bold">
                              {n.staffAverage != null ? `${n.staffAverage} / 10` : '—'}
                            </span>
                          </div>
                          <div className="w-full h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                            <div
                              className="h-full bg-[var(--accent)]"
                              style={{ width: `${pctWidth(n.staffAverage, 10)}%` }}
                            />
                          </div>
                        </div>
                        <div>
                          <div className="flex justify-between mb-1">
                            <span className="text-[var(--muted)]">تقييم المنصات</span>
                            <span className="num font-bold">
                              {n.appAverage != null ? `${n.appAverage} / 5 ★` : '—'}
                            </span>
                          </div>
                          <div className="w-full h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                            <div
                              className="h-full bg-[var(--money-pos)]"
                              style={{ width: `${pctWidth(n.appAverage, 5)}%` }}
                            />
                          </div>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>

          {/* 3. By Platform / App Chart & Star Distribution + 4. Common Factors */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <section
              className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-4"
              data-testid="eval-chart-by-app"
            >
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-[var(--ink)]">
                  تقييم منصات العمل وتوزيع النجوم (1–5 ★)
                </h3>
                <span className="text-xs text-[var(--muted)] num">
                  {filteredByApp.length} منصات
                </span>
              </div>
              {filteredByApp.length === 0 ? (
                <p className="text-xs text-[var(--muted)]">لا توجد تقييمات للمنصات بعد.</p>
              ) : (
                <div className="space-y-3">
                  {filteredByApp.map((a) => {
                    const totalRated = Number(a.ratedCount ?? 0);
                    return (
                      <div
                        key={a.key}
                        className="rounded-xl border border-[var(--line)] bg-[var(--bg)] p-3.5 space-y-2.5"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <strong className="text-sm font-bold text-[var(--ink)]">
                              {platformDisplayName(a.key, 'ar')}
                            </strong>
                            <div className="text-[11px] text-[var(--muted)]">
                              مقيّم: <b className="num">{totalRated}</b> · لم يعمل عليها / غير متاحة:{' '}
                              <b className="num">{a.skippedCount ?? 0}</b>
                            </div>
                          </div>
                          <span className="rounded-xl bg-[var(--accent)]/15 px-3 py-1 text-sm font-black text-[var(--accent)] num">
                            {a.average != null ? `${a.average} / 5 ★` : '—'}
                          </span>
                        </div>

                        {/* Star distribution bars 1..5 */}
                        {Array.isArray(a.distribution) && a.distribution.length > 0 && (
                          <div className="space-y-1 pt-1">
                            {a.distribution
                              .slice()
                              .sort((x, y) => y.stars - x.stars)
                              .map((dist) => {
                                const w =
                                  totalRated > 0
                                    ? Math.round((dist.count / totalRated) * 100)
                                    : 0;
                                return (
                                  <div
                                    key={dist.stars}
                                    className="flex items-center gap-2 text-[11px]"
                                  >
                                    <span className="w-10 num font-bold">{dist.stars} ★</span>
                                    <div className="flex-1 h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                                      <div
                                        className="h-full bg-[var(--accent)]"
                                        style={{ width: `${w}%` }}
                                      />
                                    </div>
                                    <span className="w-8 text-end num text-[var(--muted)]">
                                      {dist.count}
                                    </span>
                                  </div>
                                );
                              })}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </section>

            {/* 4. Common Factors & Daily Trend */}
            <section
              className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-4"
              data-testid="eval-chart-common-factors"
            >
              <div className="flex items-center justify-between">
                <h3 className="text-base font-bold text-[var(--ink)]">
                  العوامل والأسباب الأكثر شيوعًا في تقييم المنصات
                </h3>
                <span className="text-xs text-[var(--muted)] num">
                  {filteredCommonFactors.length} عامل
                </span>
              </div>
              {filteredCommonFactors.length === 0 ? (
                <p className="text-xs text-[var(--muted)]">
                  لم تُسجل عوامل تقييم للمنصات بعد.
                </p>
              ) : (
                <div className="space-y-2.5">
                  {(() => {
                    const maxFactorCount = Math.max(
                      1,
                      ...filteredCommonFactors.map((f) => Number(f.count ?? 0))
                    );
                    return filteredCommonFactors.map((f, idx) => (
                      <div key={`${f.app}-${f.key}-${idx}`} className="space-y-1">
                        <div className="flex items-center justify-between text-xs">
                          <span className="font-semibold text-[var(--ink)]">
                            {FACTOR_LABELS_AR[f.key] || f.key}
                            {f.app && (
                              <span className="ms-1.5 rounded-full bg-[var(--raised)] px-2 py-0.5 text-[11px] text-[var(--muted)]">
                                {platformDisplayName(f.app, 'ar')}
                              </span>
                            )}
                          </span>
                          <span className="num font-bold">{f.count ?? 0}</span>
                        </div>
                        <div className="w-full h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                          <div
                            className="h-full bg-[var(--info)]"
                            style={{
                              width: `${pctWidth(Number(f.count ?? 0), maxFactorCount)}%`,
                            }}
                          />
                        </div>
                      </div>
                    ));
                  })()}
                </div>
              )}

              {/* Daily Trend summary */}
              {filteredDailyTrend.length > 0 && (
                <div className="pt-4 border-t border-[var(--line)] space-y-2">
                  <h4 className="text-xs font-bold text-[var(--muted)]">
                    الاتجاه اليومي للردود ({filteredDailyTrend.length} يوم)
                  </h4>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                    {filteredDailyTrend.map((day) => (
                      <div
                        key={day.key}
                        className="flex items-center justify-between rounded-lg bg-[var(--bg)] px-3 py-2 text-xs border border-[var(--line)]"
                      >
                        <span className="num font-semibold">{day.key}</span>
                        <span className="num text-[var(--muted)]">
                          <b>{day.responseCount ?? 0}</b> رد · مشرفين:{' '}
                          <b>{day.staffAverage ?? '—'}</b> · منصات:{' '}
                          <b>{day.appAverage ?? '—'}</b>
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </section>
          </div>

          {/* 5. By Supervisor / Staff Visual Chart with 5 Dimensions Breakdown */}
          <section
            className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 space-y-4"
            data-testid="eval-chart-by-supervisor"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <h3 className="text-base font-bold text-[var(--ink)]">
                  مؤشرات وتقييمات المشرفين والموظفين حسب الأبعاد الخمسة (1–10)
                </h3>
                <p className="text-xs text-[var(--muted)]">
                  بطاقة واحدة لكل موظف مع توزيع الأبعاد الخمسة وعدد التقييمات وأسباب التخطي والشكاوى السرية
                </p>
              </div>
              <span className="text-xs font-bold text-[var(--muted)] num">
                {supervisorRows.length} بطاقة
              </span>
            </div>

            {supervisorRows.length === 0 ? (
              <p className="text-xs text-[var(--muted)]">
                لا توجد بطاقات موظفين مطابقة للفلاتر المختارة.
              </p>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {supervisorRows.map((row) => (
                  <div
                    key={row.id}
                    data-supervisor-chart-card={row.id}
                    className="rounded-xl border border-[var(--line)] bg-[var(--bg)] p-4 space-y-3"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div>
                        <strong className="text-sm font-bold text-[var(--ink)]">
                          {row.name}
                        </strong>
                        <div className="text-[11px] text-[var(--muted)] mt-0.5">
                          {row.citiesText} · {row.departmentsText}
                          {row.appsText !== '—' ? ` · ${row.appsText}` : ''}
                        </div>
                        {row.rolesText && (
                          <div className="text-[11px] text-[var(--muted)] mt-0.5">
                            المهام: {row.rolesText}
                          </div>
                        )}
                      </div>
                      <div className="text-end shrink-0">
                        <span className="inline-block rounded-xl bg-[var(--accent)]/15 px-2.5 py-1 text-xs font-black text-[var(--accent)] num">
                          {row.average != null ? `${row.average} / 10` : '—'}
                        </span>
                        {row.complaintCount > 0 && (
                          <div className="mt-1">
                            <span className="rounded-full bg-[var(--danger)]/15 px-2 py-0.5 text-[10px] font-bold text-[var(--danger)] num">
                              {row.complaintCount} شكوى سرية
                            </span>
                          </div>
                        )}
                      </div>
                    </div>

                    <div className="grid grid-cols-1 gap-1.5 text-[11px]">
                      {(
                        [
                          ['professional', row.professionalAvg],
                          ['treatment', row.treatmentAvg],
                          ['support', row.supportAvg],
                          ['responseSpeed', row.responseSpeedAvg],
                          ['problemSolving', row.problemSolvingAvg],
                        ] as [string, number | null][]
                      ).map(([dimKey, val]) => (
                        <div key={dimKey} className="flex items-center gap-2">
                          <span className="w-36 truncate text-[var(--muted)]">
                            {STAFF_DIMENSION_LABELS_AR[dimKey] || dimKey}
                          </span>
                          <div className="flex-1 h-2 rounded-full bg-[var(--raised)] overflow-hidden">
                            <div
                              className="h-full bg-[var(--accent)]"
                              style={{ width: `${pctWidth(val, 10)}%` }}
                            />
                          </div>
                          <span className="w-10 text-end num font-bold">
                            {val != null ? val : '—'}
                          </span>
                        </div>
                      ))}
                    </div>

                    <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-[var(--line)] text-[11px] text-[var(--muted)]">
                      <span>
                        عدد التقييمات: <b className="num text-[var(--ink)]">{row.ratedCount}</b>
                      </span>
                      <span>
                        عدم التعامل: <b className="num text-[var(--ink)]">{row.skippedCount}</b>
                        {row.skippedCount > 0 ? ` (${row.reasonsText})` : ''}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Table 1: Supervisors Summary DataTable (with ColumnFilterMenu) */}
          <section className="space-y-3" data-testid="eval-supervisors-table-section">
            <h3 className="text-base font-bold text-[var(--ink)]">
              جدول ملخص تقييم المشرفين والموظفين
            </h3>
            <DataTable
              tableId="eval-supervisors-table"
              columns={supervisorColumns}
              data={supervisorRows}
              loading={loading || responsesLoading}
              getRowId={(r) => r.id}
              syncUrl={false}
              exportable={canExportEvaluations}
              exportFilename="ملخص_تقييم_المشرفين"
              emptyTitle="لا توجد بطاقات مشرفين في هذه الجولة"
              emptyDescription="استورد بطاقات الموظفين وأنشئ جولة تقييم لعرض الملخص."
            />
          </section>

          {/* Table 2: Server-Paged Evaluation Responses DataTable */}
          <section className="space-y-3" data-testid="eval-responses-table-section">
            <h3 className="text-base font-bold text-[var(--ink)]">
              جدول ردود المناديب التفصيلي ({responsesTotal} رد)
            </h3>
            <DataTable
              tableId="eval-responses-table"
              columns={responseColumns}
              data={responses}
              totalCount={responsesTotal}
              page={responsesPage}
              pageSize={responsesPageSize}
              onPageChange={setResponsesPage}
              onPageSizeChange={setResponsesPageSize}
              serverSide
              onServerQueryChange={(sq) => {
                setResponsesServerQuery(sq);
                setResponsesPage(1);
              }}
              loading={responsesLoading}
              getRowId={(r) => r.id}
              syncUrl={false}
              exportable={false}
              emptyTitle="لا توجد ردود مسجلة في هذه الجولة بعد"
              emptyDescription="شارك رابط الجولة مع المناديب أو عبر تطبيق المناديب لاستقبال التقييمات."
              rowActions={[
                {
                  label: 'عرض تفاصيل الرد والبطاقات',
                  icon: <Eye size={14} />,
                  onClick: (r) => setInspectReply(r),
                },
              ]}
            />
          </section>

          {/* Table 3: Imported Staff Directory Cards */}
          {staffList.length > 0 && (
            <section className="space-y-3" data-testid="eval-staff-directory-section">
              <h3 className="text-base font-bold text-[var(--ink)]">
                دليل بطاقات الموظفين المستوردة ({staffList.length})
              </h3>
              <DataTable
                tableId="eval-staff-directory-table"
                columns={staffDirectoryColumns}
                data={staffList}
                loading={loading}
                getRowId={(s) => s.id}
                syncUrl={false}
                exportable={canExportEvaluations}
                exportFilename="دليل_بطاقات_تقييم_الموظفين"
              />
            </section>
          )}
        </>
      )}

      {/* Import Staff JSON Dialog */}
      <Dialog
        open={importOpen}
        onClose={() => (importing ? null : setImportOpen(false))}
        title="استيراد بطاقات الموظفين والمشرفين (JSON)"
        description="اختر ملف بيانات الموظفين المحلي أو الصق محتوى JSON. تُدمج الأدوار المتعددة في بطاقة واحدة لكل شخص مع حفظ المستبعدين وعلامة «بانتظار موافقة غزال»."
        actions={
          <>
            <button
              type="button"
              disabled={importing}
              onClick={() => setImportOpen(false)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={importing || !importJsonText.trim()}
              onClick={handleImportStaff}
              data-testid="eval-confirm-import-staff-btn"
              className="px-4 py-2 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] disabled:opacity-50"
            >
              {importing ? 'جاري الاستيراد…' : 'استيراد وحفظ البطاقات'}
            </button>
          </>
        }
      >
        <form onSubmit={handleImportStaff} className="space-y-3">
          <label className="block space-y-1 text-xs">
            <span className="font-bold text-[var(--ink)]">اختيار ملف JSON من الجهاز</span>
            <input
              type="file"
              accept=".json,application/json"
              onChange={handleStaffFileChange}
              className="w-full text-xs"
            />
          </label>
          <label className="block space-y-1 text-xs">
            <span className="font-bold text-[var(--ink)]">أو لصق بيانات JSON مباشرة</span>
            <textarea
              rows={7}
              value={importJsonText}
              onChange={(e) => setImportJsonText(e.target.value)}
              placeholder='{"staff":[{"name":"...","cities":["..."],"departments":["..."],"roles":["..."],"apps":["Ninja"]}]}'
              data-testid="eval-import-json-textarea"
              className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] p-2.5 font-mono text-xs text-[var(--ink)]"
            />
          </label>
        </form>
      </Dialog>

      {/* Create Evaluation Round Dialog */}
      <Dialog
        open={createOpen}
        onClose={() => (creatingRound ? null : setCreateOpen(false))}
        title="إنشاء جولة تقييم جديدة"
        description="تُنشأ الجولة بنسخة ثابتة من بطاقات الموظفين النشطة غير المستبعدة."
        actions={
          <>
            <button
              type="button"
              disabled={creatingRound}
              onClick={() => setCreateOpen(false)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={creatingRound || !newRoundTitle.trim()}
              onClick={handleCreateRound}
              data-testid="eval-confirm-create-round-btn"
              className="px-4 py-2 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] disabled:opacity-50"
            >
              {creatingRound ? 'جاري الإنشاء…' : 'إنشاء الجولة'}
            </button>
          </>
        }
      >
        <form onSubmit={handleCreateRound} className="space-y-3">
          <label className="block space-y-1 text-xs">
            <span className="font-bold text-[var(--ink)]">عنوان جولة التقييم</span>
            <input
              type="text"
              required
              maxLength={200}
              value={newRoundTitle}
              onChange={(e) => setNewRoundTitle(e.target.value)}
              placeholder="مثال: تقييم المشرفين والمنصات — نوفمبر 2026"
              data-testid="eval-round-title-input"
              className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
            />
          </label>
        </form>
      </Dialog>

      {/* Reply Details Drawer */}
      <Drawer
        open={!!inspectReply}
        onClose={() => setInspectReply(null)}
        title={inspectReply ? `تفاصيل تقييم المندوب: ${inspectReply.name}` : 'تفاصيل الرد'}
      >
        {inspectReply && (
          <div className="space-y-5 text-xs" data-testid="eval-reply-drawer">
            <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3.5 grid grid-cols-2 gap-2">
              <div>
                <span className="text-[var(--muted)]">رقم الإقامة:</span>{' '}
                <b className="num font-mono">{inspectReply.iqama}</b>
              </div>
              <div>
                <span className="text-[var(--muted)]">المدينة:</span>{' '}
                <b>{inspectReply.city}</b>
              </div>
              <div>
                <span className="text-[var(--muted)]">الجنسية:</span>{' '}
                <b>{inspectReply.nationality}</b>
              </div>
              <div>
                <span className="text-[var(--muted)]">الجوال:</span>{' '}
                <bdi dir="ltr" className="num font-mono">
                  {inspectReply.phone || '—'}
                </bdi>
              </div>
              <div className="col-span-2">
                <span className="text-[var(--muted)]">منصات العمل الحالية:</span>{' '}
                <b>
                  {(inspectReply.workApps || [])
                    .map((a) => platformDisplayName(a, 'ar'))
                    .join('، ')}
                </b>
              </div>
            </div>

            <div className="space-y-2.5">
              <h4 className="font-bold text-sm text-[var(--ink)]">
                إجابات بطاقات المشرفين والموظفين ({inspectReply.staffAnswers.length})
              </h4>
              {inspectReply.staffAnswers.map((card, i) => {
                const st = card.staffId ? staffById.get(card.staffId) : undefined;
                return (
                  <div
                    key={card.staffId || i}
                    className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-3 space-y-1.5"
                  >
                    <div className="flex items-center justify-between">
                      <strong>{st?.name || card.staffId}</strong>
                      {card.reason ? (
                        <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 text-[11px] text-[var(--muted)]">
                          {STAFF_SKIP_REASON_LABELS_AR[card.reason] || card.reason}
                        </span>
                      ) : (
                        <span className="rounded-full bg-[var(--money-pos)]/15 px-2 py-0.5 text-[11px] font-bold text-[var(--money-pos)]">
                          تم التقييم
                        </span>
                      )}
                    </div>
                    {!card.reason && card.ratings && (
                      <div className="grid grid-cols-2 gap-1 text-[11px] text-[var(--muted)]">
                        {Object.entries(card.ratings).map(([k, v]) => (
                          <div key={k}>
                            {STAFF_DIMENSION_LABELS_AR[k] || k}:{' '}
                            <b className="num text-[var(--ink)]">{v} / 10</b>
                          </div>
                        ))}
                      </div>
                    )}
                    {card.notes && (
                      <div className="text-[11px] text-[var(--ink)]">
                        ملاحظات: {card.notes}
                      </div>
                    )}
                    {card.complaint && (
                      <div className="rounded-lg border border-[var(--danger)]/40 bg-[var(--danger)]/10 p-2 text-[11px] text-[var(--danger)]">
                        <strong>🛡️ شكوى سرية:</strong> {card.complaintDetails}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>

            <div className="space-y-2.5">
              <h4 className="font-bold text-sm text-[var(--ink)]">
                إجابات بطاقات المنصات ({inspectReply.appAnswers.length})
              </h4>
              {inspectReply.appAnswers.map((card, i) => (
                <div
                  key={card.app || i}
                  className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-3 space-y-1.5"
                >
                  <div className="flex items-center justify-between">
                    <strong>{platformDisplayName(card.app || '', 'ar')}</strong>
                    {card.reason ? (
                      <span className="rounded-full bg-[var(--raised)] px-2 py-0.5 text-[11px] text-[var(--muted)]">
                        {APP_SKIP_REASON_LABELS_AR[card.reason] || card.reason}
                      </span>
                    ) : (
                      <span className="rounded-full bg-[var(--accent)]/15 px-2 py-0.5 text-[11px] font-bold text-[var(--accent)] num">
                        {card.overall} / 5 ★
                      </span>
                    )}
                  </div>
                  {Array.isArray(card.factors) && card.factors.length > 0 && (
                    <div className="flex flex-wrap gap-1">
                      {card.factors.map((f) => (
                        <span
                          key={f}
                          className="rounded-full bg-[var(--raised)] px-2 py-0.5 text-[10px]"
                        >
                          {FACTOR_LABELS_AR[f] || f}
                        </span>
                      ))}
                    </div>
                  )}
                  {card.notes && (
                    <div className="text-[11px] text-[var(--ink)]">
                      ملاحظات: {card.notes}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
      </Drawer>
    </div>
  );
}
