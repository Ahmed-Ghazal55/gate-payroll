'use client';

import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';

type Row = {
  id?: string;
  summaryDate?: string;
  summary_date?: string;
  userName?: string;
  user?: string;
  area?: string;
  areaAr?: string;
  readCount?: number;
  exportCount?: number;
  sensitiveViewCount?: number;
};

const TABS: SectionTabItem[] = [
  { id: 'today', label: 'اليوم' },
  { id: 'range', label: 'فترة' },
  { id: 'sensitive', label: 'المناطق الحساسة' },
];

export function AdminReadSummaryPanel() {
  const [subtab, setSubtab] = useState('today');
  const [rows, setRows] = useState<Row[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const today = new Date().toISOString().slice(0, 10);
        const from = subtab === 'today' ? today : undefined;
        const list = await api.getAdminReadSummary({ from, to: from });
        if (!cancelled) setRows(Array.isArray(list) ? list : []);
      } catch (err: unknown) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'تعذر تحميل ملخص القراءة');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [subtab]);

  const columns: ColumnDef<Row>[] = [
    {
      key: 'summaryDate',
      header: 'التاريخ',
      render: (r) => r.summaryDate || r.summary_date || '—',
    },
    { key: 'user', header: 'المستخدم', filterable: true, render: (r) => r.userName || r.user || '—' },
    { key: 'area', header: 'المنطقة', filterable: true, render: (r) => r.areaAr || r.area || '—' },
    { key: 'readCount', header: 'القراءات', numeric: true, render: (r) => String(r.readCount ?? 0) },
    { key: 'exportCount', header: 'التصديرات', numeric: true, render: (r) => String(r.exportCount ?? 0) },
    {
      key: 'sensitiveViewCount',
      header: 'عروض حساسة',
      numeric: true,
      render: (r) => String(r.sensitiveViewCount ?? 0),
    },
  ];

  return (
    <div className="space-y-3">
      <SectionTabs tabs={TABS} activeTab={subtab} onChange={setSubtab} paramName="subtab" ariaLabel="تبويبات ملخص القراءة" />
      {loading ? (
        <Skeleton rows={5} />
      ) : error ? (
        <ErrorState message={error} />
      ) : rows.length === 0 ? (
        <EmptyState title="لا ملخص قراءة لهذه الفترة" />
      ) : (
        <DataTable
          columns={columns}
          data={rows}
          getRowId={(r) => r.id || `${r.user || r.userName || 'u'}-${r.summaryDate || r.summary_date || 'd'}-${r.area || 'a'}`}
        />
      )}
    </div>
  );
}
