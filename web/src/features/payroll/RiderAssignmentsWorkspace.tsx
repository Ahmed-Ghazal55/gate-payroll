'use client';

import React, { useState, useEffect, useMemo } from 'react';
import {
  Users,
  CheckCircle2,
  AlertTriangle,
  FileSpreadsheet,
  ArrowRightLeft,
  Filter,
  Search,
  RefreshCw,
  Building2,
  ShieldAlert,
} from 'lucide-react';
import { DataTable } from '../../components/ui/DataTable';
import type { ColumnDef } from '../../components/ui/DataTable';
import { Skeleton } from '../../components/ui/Skeleton';
import { useToast } from '../../components/ui/Toast';
import { api } from '../../lib/api';
import type { PayAssignment, PayConfigDoc } from '../../lib/types';

export interface RiderAssignmentRow {
  id: string;
  rider: string;
  name: string;
  sponsor: string;
  schemeId: string;
  schemeName: string;
  isAssigned: boolean;
  choiceApprovedAt: string;
  sourceKind: string;
  status: string;
}

export interface RiderAssignmentsWorkspaceProps {
  appSlug: string;
  appNameAr: string;
  period: string;
  schemesList: PayConfigDoc[];
  ridersList: Array<{
    iqama: string;
    name?: string;
    sponsorId?: string;
    financeSponsorId?: string;
    status?: string;
  }>;
  onOpenImportModal: () => void;
}

export function RiderAssignmentsWorkspace({
  appSlug,
  appNameAr,
  period,
  schemesList,
  ridersList,
  onOpenImportModal,
}: RiderAssignmentsWorkspaceProps) {
  const { toast } = useToast();

  const [loading, setLoading] = useState(false);
  const [assignments, setAssignments] = useState<PayAssignment[]>([]);
  const [error, setError] = useState<string | null>(null);

  // Filter selection: 'all' | 'unassigned' | schemeId
  const [selectedSchemeFilter, setSelectedSchemeFilter] = useState<string>('all');

  const loadAssignments = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getPayAssignments(appSlug, period);
      setAssignments(res.items || []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'تعذر تحميل تكليفات المناديب.';
      setError(msg);
      toast(msg, 'danger');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadAssignments();
  }, [appSlug, period]);

  // Map scheme ID to readable name
  const schemeNameMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const sc of schemesList) {
      map.set(sc.id, sc.data?.name || sc.code || sc.id);
    }
    return map;
  }, [schemesList]);

  // Combined rows: cross-reference assignments with riders
  const combinedRows = useMemo(() => {
    const rows: RiderAssignmentRow[] = [];
    const assignmentByRider = new Map<string, PayAssignment>();

    for (const a of assignments) {
      assignmentByRider.set(a.rider, a);
    }

    // Process assigned riders
    for (const a of assignments) {
      const riderMeta = ridersList.find((r) => r.iqama === a.rider);
      const schemeTitle = schemeNameMap.get(a.schemeId) || a.schemeId || 'نظام غير معروف';
      rows.push({
        id: a.id || `assign-${a.rider}`,
        rider: a.rider,
        name: riderMeta?.name || `مندوب (${a.rider})`,
        sponsor: riderMeta?.financeSponsorId || riderMeta?.sponsorId || a.companyId || '—',
        schemeId: a.schemeId,
        schemeName: schemeTitle,
        isAssigned: true,
        choiceApprovedAt: a.choiceApprovedAt ? a.choiceApprovedAt.slice(0, 10) : '—',
        sourceKind: a.source?.kind === 'campaign' ? 'حملة استبيان' : 'نموذج إلكتروني',
        status: a.status === 'approved' ? 'معتمد' : a.status || 'مسجل',
      });
    }

    // Process riders with NO assignment
    for (const r of ridersList) {
      if (!assignmentByRider.has(r.iqama)) {
        rows.push({
          id: `unassigned-${r.iqama}`,
          rider: r.iqama,
          name: r.name || `مندوب (${r.iqama})`,
          sponsor: r.financeSponsorId || r.sponsorId || '—',
          schemeId: '',
          schemeName: 'بلا تكليف (scheme_assignment_missing)',
          isAssigned: false,
          choiceApprovedAt: '—',
          sourceKind: '—',
          status: 'مفقود',
        });
      }
    }

    return rows;
  }, [assignments, ridersList, schemeNameMap]);

  // Statistics
  const stats = useMemo(() => {
    const total = combinedRows.length;
    const assignedCount = combinedRows.filter((r) => r.isAssigned).length;
    const unassignedCount = combinedRows.filter((r) => !r.isAssigned).length;

    const perSchemeCounts = new Map<string, number>();
    for (const sc of schemesList) {
      perSchemeCounts.set(sc.id, 0);
    }
    for (const r of combinedRows) {
      if (r.isAssigned && r.schemeId) {
        perSchemeCounts.set(r.schemeId, (perSchemeCounts.get(r.schemeId) || 0) + 1);
      }
    }

    return {
      total,
      assignedCount,
      unassignedCount,
      perSchemeCounts,
    };
  }, [combinedRows, schemesList]);

  // Filtered rows for DataTable
  const filteredRows = useMemo(() => {
    if (selectedSchemeFilter === 'all') return combinedRows;
    if (selectedSchemeFilter === 'unassigned') {
      return combinedRows.filter((r) => !r.isAssigned);
    }
    return combinedRows.filter((r) => r.schemeId === selectedSchemeFilter);
  }, [combinedRows, selectedSchemeFilter]);

  // Columns definition for DataTable
  const columns: ColumnDef<RiderAssignmentRow>[] = useMemo(
    () => [
      {
        key: 'rider',
        header: 'إقامة المندوب',
        sortable: true,
        filterable: true,
        render: (row) => (
          <span className="font-mono font-bold text-[var(--ink)]">{row.rider}</span>
        ),
      },
      {
        key: 'name',
        header: 'اسم المندوب',
        sortable: true,
        filterable: true,
        render: (row) => <span className="font-semibold text-xs">{row.name}</span>,
      },
      {
        key: 'sponsor',
        header: 'الشركة / الكفيل',
        sortable: true,
        filterable: true,
        render: (row) => (
          <span className="px-2 py-0.5 rounded-md bg-[var(--raised)] text-xs font-mono">
            {row.sponsor}
          </span>
        ),
      },
      {
        key: 'schemeName',
        header: 'النظام الداخلي المكلف به',
        sortable: true,
        filterable: true,
        render: (row) => {
          const isAssigned = row.isAssigned;
          return isAssigned ? (
            <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/15 text-[var(--accent)]">
              {row.schemeName}
            </span>
          ) : (
            <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--danger)]/15 text-[var(--danger)] flex items-center gap-1 w-fit">
              <AlertTriangle size={12} />
              <span>بلا تكليف (غياب نظام)</span>
            </span>
          );
        },
      },
      {
        key: 'choiceApprovedAt',
        header: 'تاريخ اعتماد الاختيار',
        sortable: true,
        render: (row) => <span className="font-mono text-xs text-[var(--muted)]">{row.choiceApprovedAt}</span>,
      },
      {
        key: 'sourceKind',
        header: 'مصدر الاختيار',
        render: (row) => <span className="text-xs text-[var(--muted)]">{row.sourceKind}</span>,
      },
      {
        key: 'status',
        header: 'الحالة',
        sortable: true,
        filterable: true,
        render: (row) => (
          <span
            className={`px-2 py-0.5 rounded-full text-xs font-bold ${
              row.isAssigned
                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
            }`}
          >
            {row.status}
          </span>
        ),
      },
    ],
    []
  );

  return (
    <div className="space-y-5">
      {/* Header and Actions */}
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              سجل تكليفات الأنظمة للمناديب · منصة {appNameAr} ({period})
            </h3>
            {stats.unassignedCount > 0 ? (
              <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--danger)]/15 text-[var(--danger)] flex items-center gap-1">
                <AlertTriangle size={12} />
                <span>{stats.unassignedCount} مندوب بلا تكليف</span>
              </span>
            ) : (
              <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)] flex items-center gap-1">
                <CheckCircle2 size={12} />
                <span>كافة المناديب مكلفون بأنظمة</span>
              </span>
            )}
          </div>
          <p className="text-xs text-[var(--muted)] mt-1">
            يوضح هذا الجدول النظام المطبق على كل مندوب للشهر لمنع ظهور مانع «scheme_assignment_missing» في المسير.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={loadAssignments}
            disabled={loading}
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-[var(--ink)] hover:bg-[var(--line)] transition cursor-pointer"
            title="تحديث التكليفات"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
          </button>

          <button
            type="button"
            onClick={onOpenImportModal}
            className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
          >
            <ArrowRightLeft size={14} />
            <span>استيراد وتوزيع خيارات المناديب (R4.3)</span>
          </button>
        </div>
      </div>

      {/* Summary Stat Cards with Interactive Filtering */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs">
        <button
          type="button"
          onClick={() => setSelectedSchemeFilter('all')}
          className={`p-3.5 rounded-xl border text-start transition cursor-pointer ${
            selectedSchemeFilter === 'all'
              ? 'border-[var(--accent)] bg-[var(--accent)]/10 shadow-xs'
              : 'border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]'
          }`}
        >
          <span className="text-[var(--muted)] block">إجمالي المناديب:</span>
          <span className="text-base font-bold text-[var(--ink)] num">{stats.total}</span>
        </button>

        <button
          type="button"
          onClick={() => setSelectedSchemeFilter('unassigned')}
          className={`p-3.5 rounded-xl border text-start transition cursor-pointer ${
            selectedSchemeFilter === 'unassigned'
              ? 'border-[var(--danger)] bg-[var(--danger)]/15 shadow-xs'
              : stats.unassignedCount > 0
              ? 'border-[var(--danger)]/40 bg-[var(--danger)]/5 hover:bg-[var(--danger)]/10'
              : 'border-[var(--line)] bg-[var(--surface)]'
          }`}
        >
          <span className="text-[var(--danger)] font-bold block flex items-center gap-1">
            <AlertTriangle size={12} />
            <span>بلا تكليف (يحتاج ربط):</span>
          </span>
          <span className="text-base font-bold text-[var(--danger)] num">{stats.unassignedCount}</span>
        </button>

        {schemesList.map((sc) => {
          const count = stats.perSchemeCounts.get(sc.id) || 0;
          const isSelected = selectedSchemeFilter === sc.id;
          return (
            <button
              key={sc.id}
              type="button"
              onClick={() => setSelectedSchemeFilter(isSelected ? 'all' : sc.id)}
              className={`p-3.5 rounded-xl border text-start transition cursor-pointer ${
                isSelected
                  ? 'border-[var(--accent)] bg-[var(--accent)]/10 shadow-xs'
                  : 'border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]'
              }`}
            >
              <span className="text-[var(--muted)] block truncate">{sc.data?.name || sc.code}:</span>
              <span className="text-base font-bold text-[var(--accent)] num">{count} مندوب</span>
            </button>
          );
        })}
      </div>

      {/* Filter indicator */}
      {selectedSchemeFilter !== 'all' && (
        <div className="flex items-center justify-between p-2.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs">
          <div className="flex items-center gap-2">
            <Filter size={14} className="text-[var(--accent)]" />
            <span>
              عرض مخصص:{' '}
              <strong className="text-[var(--ink)]">
                {selectedSchemeFilter === 'unassigned'
                  ? 'المناديب بدون تكليف فقط'
                  : schemeNameMap.get(selectedSchemeFilter) || selectedSchemeFilter}
              </strong>{' '}
              ({filteredRows.length} من أصل {combinedRows.length})
            </span>
          </div>
          <button
            type="button"
            onClick={() => setSelectedSchemeFilter('all')}
            className="text-[var(--accent)] font-bold hover:underline cursor-pointer"
          >
            إعادة تعيين الفلتر
          </button>
        </div>
      )}

      {/* Main DataTable */}
      {loading ? (
        <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : (
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
          <DataTable
            data={filteredRows}
            columns={columns}
            pageSize={20}
            emptyTitle="لا توجد تكليفات مطابقة"
            emptyDescription="لا توجد نتائج مطابقة لخيارات الفلترة المحددة."
          />
        </div>
      )}
    </div>
  );
}
