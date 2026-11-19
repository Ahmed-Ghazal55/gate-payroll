'use client';

import React, { useState, useEffect, useMemo, useCallback, Suspense } from 'react';
import { useSearchParams, useRouter, usePathname } from 'next/navigation';
import {
  Briefcase,
  Layers,
  IdCard,
  Car,
  Building2,
  MapPin,
  RefreshCw,
} from 'lucide-react';
import { api, ApiError } from '../../../lib/api';
import { useToast } from '../../../components/ui/Toast';
import { PageHeader } from '../../../components/ui/PageHeader';
import { SectionTabs, SectionTabItem } from '../../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../../components/ui/EmptyState';
import { DataTable, ColumnDef } from '../../../components/ui/DataTable';
import { SourceBadge } from '../../../components/ui/SourceBadge';

const CATALOG_TABS: Array<{
  id: string;
  kind: string;
  label: string;
  icon: React.ReactNode;
}> = [
  { id: 'job-titles', kind: 'job-titles', label: 'المسميات الوظيفية', icon: <Briefcase size={16} /> },
  { id: 'work-types', kind: 'work-types', label: 'أنواع العمل', icon: <Layers size={16} /> },
  { id: 'iqama-professions', kind: 'iqama-professions', label: 'مهن الإقامة', icon: <IdCard size={16} /> },
  { id: 'licence-categories', kind: 'licence-categories', label: 'فئات الرخص', icon: <Car size={16} /> },
  { id: 'sponsors', kind: 'sponsors', label: 'الكفلاء', icon: <Building2 size={16} /> },
  { id: 'city-settings', kind: 'city-settings', label: 'إعدادات المدن', icon: <MapPin size={16} /> },
];

function cellText(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  return String(value);
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

function HrCatalogsPageInner() {
  const { toast } = useToast();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const tabFromUrl = searchParams?.get('tab') || 'job-titles';
  const [activeTab, setActiveTab] = useState(
    CATALOG_TABS.some((t) => t.id === tabFromUrl) ? tabFromUrl : 'job-titles'
  );
  const [items, setItems] = useState<Record<string, unknown>[]>([]);
  const [source, setSource] = useState('');
  const [asOf, setAsOf] = useState('');
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    if (tabFromUrl && tabFromUrl !== activeTab && CATALOG_TABS.some((t) => t.id === tabFromUrl)) {
      setActiveTab(tabFromUrl);
    }
  }, [tabFromUrl]);

  const current = CATALOG_TABS.find((t) => t.id === activeTab) || CATALOG_TABS[0];

  const loadCatalog = useCallback(
    async (kind: string) => {
      setLoading(true);
      setErrorMsg(null);
      setMessage('');
      try {
        const res = await api.getHrCatalog(kind);
        setItems((res?.items as Record<string, unknown>[]) || []);
        setSource(res?.source || '');
        setAsOf((res as { asOf?: string })?.asOf || '');
        setMessage(res?.message || '');
      } catch (err: unknown) {
        const msg =
          err instanceof ApiError
            ? err.message
            : err instanceof Error
            ? err.message
            : 'تعذر تحميل الكتالوج';
        setErrorMsg(msg);
        setItems([]);
        toast(msg, 'danger');
      } finally {
        setLoading(false);
      }
    },
    [toast]
  );

  useEffect(() => {
    loadCatalog(current.kind);
  }, [current.kind, loadCatalog]);

  const handleTabChange = (tabId: string) => {
    setActiveTab(tabId);
    const sp = new URLSearchParams();
    if (tabId !== 'job-titles') sp.set('tab', tabId);
    const qs = sp.toString() ? `?${sp.toString()}` : '';
    router.replace(`${pathname}${qs}`, { scroll: false });
  };

  const sectionTabs: SectionTabItem[] = CATALOG_TABS.map((t) => ({
    id: t.id,
    label: t.label,
    icon: t.icon,
  }));

  const columns: ColumnDef<Record<string, unknown>>[] = useMemo(() => {
    switch (activeTab) {
      case 'sponsors':
        return [
          {
            key: 'RegistryNameAr',
            header: 'الاسم',
            render: (r) => (
              <span className="font-semibold">
                {cellText(pickField(r, 'RegistryNameAr', 'registryNameAr', 'nameAr', 'name'))}
              </span>
            ),
          },
          {
            key: 'UnifiedNationalNumber',
            header: 'الرقم الموحد',
            render: (r) => (
              <span className="font-mono font-bold" dir="ltr">
                {cellText(pickField(r, 'UnifiedNationalNumber', 'unifiedNationalNumber'))}
              </span>
            ),
          },
          {
            key: 'CommercialRegistrationNumber',
            header: 'السجل التجاري',
            render: (r) => (
              <span className="font-mono" dir="ltr">
                {cellText(
                  pickField(r, 'CommercialRegistrationNumber', 'commercialRegistrationNumber')
                )}
              </span>
            ),
          },
          {
            key: 'SponsorType',
            header: 'النوع',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'SponsorType', 'sponsorType')),
            render: (r) => <span>{cellText(pickField(r, 'SponsorType', 'sponsorType'))}</span>,
          },
          {
            key: 'Status',
            header: 'الحالة',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'Status', 'status')),
            render: (r) => <span>{cellText(pickField(r, 'Status', 'status'))}</span>,
          },
        ];
      case 'city-settings':
        return [
          {
            key: 'Code',
            header: 'الرمز',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'Code', 'code')),
            render: (r) => (
              <span className="font-mono font-bold text-[var(--accent)]">
                {cellText(pickField(r, 'Code', 'code'))}
              </span>
            ),
          },
          {
            key: 'NameAr',
            header: 'المدينة',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'NameAr', 'nameAr')),
            render: (r) => (
              <span className="font-semibold">
                {cellText(pickField(r, 'NameAr', 'nameAr'))}
              </span>
            ),
          },
          {
            key: 'RegionAr',
            header: 'المنطقة',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'RegionAr', 'regionAr')),
            render: (r) => <span>{cellText(pickField(r, 'RegionAr', 'regionAr'))}</span>,
          },
          {
            key: 'CountryCode',
            header: 'الدولة',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'CountryCode', 'countryCode')),
            render: (r) => <span className="font-mono">{cellText(pickField(r, 'CountryCode', 'countryCode'))}</span>,
          },
          {
            key: 'Status',
            header: 'الحالة',
            filterType: 'list',
            getValue: (r) => cellText(pickField(r, 'Status', 'status')),
            render: (r) => <span>{cellText(pickField(r, 'Status', 'status'))}</span>,
          },
          {
            key: 'EnabledFrom',
            header: 'مفعّل من',
            render: (r) => (
              <span className="font-mono text-xs">
                {cellText(pickField(r, 'EnabledFrom', 'enabledFrom'))}
              </span>
            ),
          },
        ];
      case 'job-titles':
        return [
          {
            key: 'Code',
            header: 'الرمز',
            render: (r) => (
              <span className="font-mono font-bold text-[var(--accent)]">
                {cellText(pickField(r, 'Code', 'code'))}
              </span>
            ),
          },
          {
            key: 'NameAr',
            header: 'الاسم بالعربية',
            render: (r) => (
              <span className="font-semibold">{cellText(pickField(r, 'NameAr', 'nameAr'))}</span>
            ),
          },
          {
            key: 'NameEn',
            header: 'الاسم بالإنجليزية',
            render: (r) => (
              <span className="text-xs font-mono text-[var(--muted)]">
                {cellText(pickField(r, 'NameEn', 'nameEn'))}
              </span>
            ),
          },
          {
            key: 'WorkTypeCodes',
            header: 'أنواع العمل',
            render: (r) => (
              <span className="text-xs">{cellText(pickField(r, 'WorkTypeCodes', 'workTypeCodes'))}</span>
            ),
          },
          {
            key: 'Status',
            header: 'الحالة',
            render: (r) => <span>{cellText(pickField(r, 'Status', 'status'))}</span>,
          },
        ];
      default:
        return [
          {
            key: 'Code',
            header: 'الرمز',
            render: (r) => (
              <span className="font-mono font-bold text-[var(--accent)]">
                {cellText(pickField(r, 'Code', 'code', 'Id', 'id'))}
              </span>
            ),
          },
          {
            key: 'NameAr',
            header: 'الاسم بالعربية',
            render: (r) => (
              <span className="font-semibold">{cellText(pickField(r, 'NameAr', 'nameAr', 'name'))}</span>
            ),
          },
          {
            key: 'NameEn',
            header: 'الاسم بالإنجليزية',
            render: (r) => (
              <span className="text-xs font-mono text-[var(--muted)]">
                {cellText(pickField(r, 'NameEn', 'nameEn'))}
              </span>
            ),
          },
          {
            key: 'Status',
            header: 'الحالة',
            render: (r) => <span>{cellText(pickField(r, 'Status', 'status'))}</span>,
          },
        ];
    }
  }, [activeTab]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="كتالوجات الموارد البشرية"
        eyebrow="قوائم مرجعية من نظام عمر"
        description="المسميات، أنواع العمل، مهن الإقامة، فئات الرخص، الكفلاء، وإعدادات المدن — قراءة مباشرة من المصدر."
        primaryAction={{
          label: 'تحديث',
          icon: <RefreshCw size={15} />,
          onClick: () => loadCatalog(current.kind),
        }}
      >
        {source ? <SourceBadge source={source} asOf={asOf} /> : null}
      </PageHeader>

      <SectionTabs tabs={sectionTabs} activeTab={activeTab} onChange={handleTabChange} />

      {message ? (
        <div className="p-3 rounded-xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 text-sm text-[var(--warning)]">
          {message}
        </div>
      ) : null}

      {errorMsg ? (
        <ErrorState
          title="تعذر تحميل الكتالوج"
          message={errorMsg}
          onRetry={() => loadCatalog(current.kind)}
        />
      ) : loading && items.length === 0 ? (
        <div className="p-8 space-y-4 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
          <Skeleton className="h-10 w-48" />
          <Skeleton className="h-48 w-full" />
        </div>
      ) : items.length === 0 && !loading ? (
        <EmptyState
          title="لا توجد عناصر"
          description="الكتالوج الحالي فارغ أو لا يتوفر مصدر قراءة بعد."
        />
      ) : (
        <DataTable
          tableId={`hr-catalog-${activeTab}`}
          columns={columns}
          data={items}
          loading={loading}
          emptyTitle="لا توجد عناصر"
          emptyDescription="الكتالوج الحالي فارغ."
          exportFilename={`hr_catalog_${activeTab}`}
          getRowId={(r) =>
            String(
              pickField(r, 'Id', 'id', 'Code', 'code', 'UnifiedNationalNumber', 'NameAr', 'nameAr') ||
                Object.values(r).map((v) => String(v ?? '')).join('|')
            )
          }
          searchable
          syncUrl={false}
        />
      )}
    </div>
  );
}

export default function HrCatalogsPage() {
  return (
    <Suspense
      fallback={
        <div className="p-8 space-y-4">
          <Skeleton className="h-10 w-64" />
          <Skeleton className="h-32 w-full" />
        </div>
      }
    >
      <HrCatalogsPageInner />
    </Suspense>
  );
}
