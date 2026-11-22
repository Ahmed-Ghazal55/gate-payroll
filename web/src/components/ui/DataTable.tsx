'use client';

import { formatDataTableFooterTotal } from '../../lib/money';

import React, { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  type ColumnDef as TanStackColumnDef,
  type ColumnFiltersState,
} from '@tanstack/react-table';
import {
  ChevronDown,
  ChevronUp,
  ChevronsUpDown,
  Filter,
  MoreVertical,
  ChevronRight,
  ChevronLeft,
  X,
  Search,
  Download,
} from 'lucide-react';
import { EmptyState, ErrorState } from './EmptyState';
import { SearchableSelect } from './SearchableSelect';
import {
  ColumnFilterMenu,
  useTableAutoFilter,
  AutoFilterTh,
  AutoFilterBar,
  type AutoFilterColDef,
  type TableAutoFilterController,
} from './ColumnFilterMenu';
import { api } from '@/lib/api';
import {
  type BadgeTone,
  type ColumnDataType,
  type ColumnFilterType,
  type ColumnFilterOption,
  type ColumnFilterValue,
  type ServerFilterClause,
  type ServerSortClause,
  computeTopNThreshold,
  compareColumnValues,
  defaultStatusTone,
  evaluateColumnFilter,
  partitionRowsForFacets,
  facetRowsFor,
  extractDistinctOptions,
  extractDistinctTones,
  inferColumnFilterType,
  isColumnFilterActive,
  resolveColumnType,
  serializeServerFilters,
  serializeServerFiltersParam,
  serializeServerSort,
  serializeServerSortParam,
} from '@/lib/columnFilter';

export {
  ColumnFilterMenu,
  useTableAutoFilter,
  AutoFilterTh,
  AutoFilterBar,
  defaultStatusTone,
  type AutoFilterColDef,
  type TableAutoFilterController,
  type BadgeTone,
  type ColumnDataType,
};

/**
 * Normalizes Arabic text and digits for robust searching:
 * - Converts Arabic-Indic (٠-٩) and Persian (۰-۹) digits to ASCII (0-9)
 * - Removes diacritics / Tashkeel
 * - Removes Tatweel (Kashida)
 * - Normalizes Alef forms (أ، إ، آ، ٱ -> ا)
 * - Normalizes Taa Marbuta (ة -> ه)
 * - Normalizes Alef Maksura (ى -> ي)
 * - Case-insensitive and trimmed
 */
export function normalizeArabicSearch(text: string | null | undefined): string {
  if (text === null || text === undefined) return '';
  return String(text)
    .replace(/[٠۰]/g, '0')
    .replace(/[١۱]/g, '1')
    .replace(/[٢۲]/g, '2')
    .replace(/[٣۳]/g, '3')
    .replace(/[٤۴]/g, '4')
    .replace(/[٥۵]/g, '5')
    .replace(/[٦۶]/g, '6')
    .replace(/[٧۷]/g, '7')
    .replace(/[٨۸]/g, '8')
    .replace(/[٩۹]/g, '9')
    .replace(/[\u064B-\u065F\u0670]/g, '')
    .replace(/\u0640/g, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .toLowerCase()
    .trim();
}

export interface ColumnDef<T> {
  key: string;
  header: string;
  /** Column data type for Excel AutoFilter ('text' | 'number' | 'date' | 'list'). */
  type?: ColumnDataType;
  numeric?: boolean;
  /** When true, footer sum uses formatSar (ر.س). */
  money?: boolean;
  sortable?: boolean;
  filterable?: boolean;
  /** Alias for type: 'list' renders a searchable dropdown of distinct values; 'text' | 'number' | 'date' sets operator mode. */
  filterType?: ColumnFilterType;
  /** Optional explicit closed set of options for a column filter. */
  filterOptions?: ColumnFilterOption[];
  /** Optional server column name for distinctEndpoint and generic filter/sort contract if different from key. */
  filterColumn?: string;
  /** Optional formatter mapping stored raw value to the Arabic label displayed to the user. */
  labelOf?: (value: any, row?: T) => string;
  /** Optional mapper returning semantic color tone ('success' | 'warning' | 'danger' | 'info' | 'neutral' | 'accent') for Sort/Filter by Color. */
  toneOf?: (value: any, row?: T) => BadgeTone | null | undefined;
  width?: string;
  render?: (row: T, index: number) => React.ReactNode;
  /** Raw value accessor used for sorting, filtering, and export (never the formatted ReactNode). */
  getValue?: (row: T) => any;
  /** R1: a stacked screen cell exported as several separate columns (export only; the screen is unchanged). */
  exportColumns?: { header: string; value: (row: T) => any }[];
  className?: string;
  summary?: 'sum' | 'avg' | 'count' | 'none' | ((rows: T[]) => React.ReactNode);
}

export interface RowAction<T> {
  label: string;
  onClick: (row: T) => void;
  icon?: React.ReactNode;
  danger?: boolean;
  disabled?: (row: T) => boolean;
}

export interface ServerTableQuery {
  filters: ServerFilterClause[];
  sort: ServerSortClause[];
  filtersParam?: string;
  sortParam?: string;
}

export interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data: T[];
  totalCount?: number;
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  page?: number;
  pageSize?: number;
  onPageChange?: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  selectable?: boolean;
  selectedIds?: string[];
  onSelectionChange?: (ids: string[]) => void;
  getRowId?: (row: T) => string;
  rowActions?: RowAction<T>[];
  emptyTitle?: string;
  emptyDescription?: string;
  tableId?: string;
  className?: string;
  // Standard extensions (R8 Task A1 & FL2)
  searchable?: boolean;
  globalSearchPlaceholder?: string;
  syncUrl?: boolean;
  exportable?: boolean;
  exportFilename?: string;
  onExport?: (filteredRows: T[]) => Promise<void> | void;
  toolbarExtra?: React.ReactNode;
  showTotals?: boolean;
  /** Show the quick inline filter inputs row beneath the header titles (default true). */
  showFilterRow?: boolean;
  columnFilters?: Record<string, string>;
  onColumnFiltersChange?: (filters: Record<string, string>) => void;
  /** Generic server filter & sort contract callback for server-paged tables. */
  onServerQueryChange?: (query: ServerTableQuery) => void;
  serverSide?: boolean;
  /** Optional light API endpoint (e.g. '/operations/distinct') for fetching distinct column values on server-paged tables. */
  distinctEndpoint?: string;
  /** Optional extra query parameters sent alongside ?column= to distinctEndpoint. */
  distinctParams?: Record<string, string | undefined>;
  pageSizes?: number[];
}

export const PAGE_SIZES = [50, 100, 150, 200, 250, 300];

export function DataTable<T extends Record<string, any>>({
  columns,
  data,
  totalCount,
  loading = false,
  error = null,
  onRetry,
  page: controlledPage,
  pageSize: controlledPageSize,
  onPageChange: controlledOnPageChange,
  onPageSizeChange: controlledOnPageSizeChange,
  selectable = false,
  selectedIds = [],
  onSelectionChange,
  getRowId,
  rowActions,
  emptyTitle,
  emptyDescription,
  tableId = 'data-table',
  className = '',
  searchable = true,
  globalSearchPlaceholder,
  syncUrl = true,
  exportable = true,
  exportFilename,
  onExport,
  toolbarExtra,
  showTotals = true,
  showFilterRow = true,
  columnFilters: controlledColumnFilters,
  onColumnFiltersChange,
  onServerQueryChange,
  serverSide = false,
  distinctEndpoint,
  distinctParams,
  pageSizes: propPageSizes,
}: DataTableProps<T>) {
  const availablePageSizes = propPageSizes || PAGE_SIZES;

  // Paging state
  const [innerPage, setInnerPage] = useState(1);
  const [innerPageSize, setInnerPageSize] = useState(propPageSizes?.[0] ?? PAGE_SIZES[0]);
  const page = controlledPage ?? innerPage;
  const pageSize = controlledPageSize ?? innerPageSize;

  const onPageChange = useCallback(
    (p: number) => {
      if (controlledOnPageChange) controlledOnPageChange(p);
      else setInnerPage(p);
    },
    [controlledOnPageChange]
  );

  const onPageSizeChange = useCallback(
    (size: number) => {
      if (controlledOnPageSizeChange) controlledOnPageSizeChange(size);
      else setInnerPageSize(size);
      onPageChange(1);
    },
    [controlledOnPageSizeChange, onPageChange]
  );

  // Sorting state
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [sortTone, setSortTone] = useState<BadgeTone | null>(null);

  // Global search state
  const [globalSearch, setGlobalSearch] = useState<string>('');

  // Rich Excel-style per-column filter specs
  const [colFilterSpecs, setColFilterSpecs] = useState<Record<string, ColumnFilterValue>>({});
  const colFilterSpecsRef = useRef<Record<string, ColumnFilterValue>>({});
  colFilterSpecsRef.current = colFilterSpecs;

  // Legacy quick-filter string map (kept in sync with colFilterSpecs)
  const [internalColumnSearch, setInternalColumnSearch] = useState<Record<string, string>>({});
  const internalColumnSearchRef = useRef<Record<string, string>>({});
  internalColumnSearchRef.current = internalColumnSearch;
  const columnSearch = controlledColumnFilters ?? internalColumnSearch;

  // Active column filter popover
  const [activePopover, setActivePopover] = useState<{ colKey: string; rect: DOMRect } | null>(null);

  // Row actions active dropdown menu
  const [activeMenu, setActiveMenu] = useState<{ id: string; row: T; rect: DOMRect } | null>(null);

  // Export state
  const [isExporting, setIsExporting] = useState<boolean>(false);

  // Remote distinct values cache (for server-paged tables with distinctEndpoint)
  const [remoteDistinct, setRemoteDistinct] = useState<
    Record<string, { values: string[]; counts?: Record<string, number>; hasEmpty?: boolean; emptyCount?: number }>
  >({});
  const [loadingDistinct, setLoadingDistinct] = useState<Record<string, boolean>>({});
  const distinctFetchedRef = useRef<Set<string>>(new Set());

  const distinctParamsKey = useMemo(() => {
    if (!distinctParams) return '';
    return Object.entries(distinctParams)
      .filter(([, v]) => v !== undefined && v !== '')
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([k, v]) => `${k}=${v}`)
      .join('&');
  }, [distinctParams]);

  const distinctCacheKey = useCallback(
    (col: ColumnDef<T>) =>
      `${distinctEndpoint || ''}|${distinctParamsKey}|${col.filterColumn || col.key}`,
    [distinctEndpoint, distinctParamsKey]
  );

  const ensureDistinctLoaded = useCallback(
    (col: ColumnDef<T>) => {
      if (!distinctEndpoint) return;
      const cacheKey = distinctCacheKey(col);
      if (distinctFetchedRef.current.has(cacheKey)) return;
      distinctFetchedRef.current.add(cacheKey);
      setLoadingDistinct((prev) => ({ ...prev, [cacheKey]: true }));
      api
        .getDistinctValues(distinctEndpoint, col.filterColumn || col.key, distinctParams)
        .then((res) => {
          setRemoteDistinct((prev) => ({
            ...prev,
            [cacheKey]: {
              values: res?.values || [],
              counts: res?.counts,
              hasEmpty: res?.hasEmpty,
              emptyCount: res?.emptyCount,
            },
          }));
        })
        .catch(() => {
          // Fallback seamlessly to loaded page distinct values.
        })
        .finally(() => {
          setLoadingDistinct((prev) => ({ ...prev, [cacheKey]: false }));
        });
    },
    [distinctEndpoint, distinctParams, distinctCacheKey]
  );

  const popoverRef = useRef<HTMLDivElement>(null);
  const popoverTriggerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuTriggerRef = useRef<HTMLTableCellElement>(null);

  // Raw cell value extractor (never the formatted ReactNode)
  const getCellRawValue = useCallback((row: T, col: ColumnDef<T>): any => {
    if (col.getValue) return col.getValue(row);
    return row[col.key];
  }, []);

  // Cell string extractor helper
  const getCellString = useCallback(
    (row: T, col: ColumnDef<T>): string => {
      const raw = getCellRawValue(row, col);
      if (raw === null || raw === undefined) return '';
      return String(raw);
    },
    [getCellRawValue]
  );

  // Displayed cell string (uses labelOf when provided)
  const getCellDisplayString = useCallback(
    (row: T, col: ColumnDef<T>): string => {
      const raw = getCellRawValue(row, col);
      if (col.labelOf) return String(col.labelOf(raw, row) ?? '');
      if (raw === null || raw === undefined) return '';
      return String(raw);
    },
    [getCellRawValue]
  );

  // Cell export raw value helper
  const getCellExportValue = useCallback(
    (row: T, col: ColumnDef<T>): string | number => {
      const raw = getCellRawValue(row, col);
      if (raw === null || raw === undefined) return '';
      const colType = resolveColumnType(col);
      if (col.numeric || colType === 'number') {
        const n = typeof raw === 'number' ? raw : parseFloat(String(raw).replace(/,/g, ''));
        return isNaN(n) ? String(raw) : n;
      }
      if (col.labelOf) return String(col.labelOf(raw, row) ?? raw);
      return String(raw);
    },
    [getCellRawValue]
  );

  // Precompute topN thresholds for any active topN column filter
  const topNThresholdByCol = useMemo(() => {
    const map: Record<string, number | null> = {};
    for (const col of columns) {
      const spec = colFilterSpecs[col.key];
      if (spec?.op === 'topN' && spec.value) {
        const n = Number(spec.value);
        if (Number.isFinite(n) && n > 0) {
          map[col.key] = computeTopNThreshold(
            data.map((r) => getCellRawValue(r, col)),
            n
          );
        }
      }
    }
    return map;
  }, [columns, colFilterSpecs, data, getCellRawValue]);

  const tanstackColumnFilters = useMemo<ColumnFiltersState>(() => {
    const list: ColumnFiltersState = [];
    for (const col of columns) {
      const spec = colFilterSpecs[col.key];
      const quickVal = columnSearch[col.key];
      let effectiveSpec: ColumnFilterValue | undefined = spec;
      if (!isColumnFilterActive(effectiveSpec) && quickVal && quickVal.trim()) {
        const fType = inferColumnFilterType(col.key, col.type ?? col.filterType);
        effectiveSpec = {
          op: fType === 'list' ? 'eq' : 'contains',
          value: quickVal.trim(),
        };
      }
      if (isColumnFilterActive(effectiveSpec)) {
        list.push({ id: col.key, value: effectiveSpec });
      }
    }
    return list;
  }, [columns, colFilterSpecs, columnSearch]);

  const serverOwnsColumnFilters =
    serverSide && (Boolean(onColumnFiltersChange) || Boolean(onServerQueryChange));

  // Global search predicate (normalized term; empty term matches every row)
  const rowMatchesGlobalSearch = useCallback(
    (row: T, normGlobal: string): boolean => {
      if (!normGlobal) return true;
      for (const col of columns) {
        if (col.filterable === false || col.key === 'actions') continue;
        const rawNorm = normalizeArabicSearch(getCellString(row, col));
        const dispNorm = normalizeArabicSearch(getCellDisplayString(row, col));
        if (rawNorm.includes(normGlobal) || dispNorm.includes(normGlobal)) return true;
      }
      return false;
    },
    [columns, getCellString, getCellDisplayString]
  );

  // Excel-style cascading value lists: each column's value list is built only from rows
  // that pass the global search and every OTHER active column filter.
  // `passAll` = rows passing every filter; `failOnly[key]` = rows failing only that column's filter.
  const facetRows = useMemo(() => {
    if (serverOwnsColumnFilters) return null;
    const normGlobal = serverSide ? '' : normalizeArabicSearch(globalSearch);
    if (tanstackColumnFilters.length === 0 && !normGlobal) return null;
    const evaluators = tanstackColumnFilters.flatMap((f) => {
      const col = columns.find((c) => c.key === f.id);
      if (!col) return [];
      const type = resolveColumnType(col);
      const spec = f.value as ColumnFilterValue;
      return [
        {
          key: f.id,
          test: (row: T) =>
            evaluateColumnFilter(getCellRawValue(row, col), spec, {
              type,
              labelOf: col.labelOf ? (v) => col.labelOf!(v, row) : undefined,
              toneOf: col.toneOf ? (v) => col.toneOf!(v, row) : undefined,
              topNThreshold: topNThresholdByCol[col.key],
            }),
        },
      ];
    });
    return partitionRowsForFacets(data, evaluators, (row) =>
      rowMatchesGlobalSearch(row, normGlobal)
    );
  }, [
    serverOwnsColumnFilters,
    serverSide,
    globalSearch,
    tanstackColumnFilters,
    columns,
    data,
    getCellRawValue,
    topNThresholdByCol,
    rowMatchesGlobalSearch,
  ]);

  // Distinct options per column (combines preset filterOptions, remote distinct values, and local row values with labelOf & toneOf)
  const distinctOptionsByCol = useMemo(() => {
    const map: Record<string, ColumnFilterOption[]> = {};
    for (const col of columns) {
      if (col.filterable === false || col.key === 'actions') continue;
      const cacheKey = distinctCacheKey(col);
      const remote = remoteDistinct[cacheKey];
      const rawValues =
        remote && (remote.values.length > 0 || (remote.emptyCount ?? 0) > 0)
          ? remote.values
          : (facetRows
              ? facetRowsFor(facetRows, col.key)
              : data
            ).map((r) => getCellRawValue(r, col));
      const colType = resolveColumnType(col);
      map[col.key] = extractDistinctOptions(rawValues, {
        labelOf: col.labelOf ? (v) => col.labelOf!(v) : undefined,
        toneOf: col.toneOf ? (v) => col.toneOf!(v) : undefined,
        presetOptions: col.filterOptions,
        includeEmpty: remote ? Boolean(remote.hasEmpty || (remote.emptyCount ?? 0) > 0) : true,
        emptyCount: remote?.emptyCount,
        counts: remote?.counts,
        type: colType,
      });
    }
    return map;
  }, [columns, data, facetRows, distinctCacheKey, remoteDistinct, getCellRawValue]);

  // Distinct color tones per column
  const availableTonesByCol = useMemo(() => {
    const map: Record<
      string,
      { tone: BadgeTone; label: string; swatch: string; count: number }[]
    > = {};
    for (const col of columns) {
      if (col.filterable === false || col.key === 'actions') continue;
      const opts = distinctOptionsByCol[col.key] || [];
      const inferStatus =
        Boolean(col.toneOf) ||
        /(status|state|stage|alert|quality|result|decision|mode|priority|severity)/i.test(
          col.key
        );
      map[col.key] = extractDistinctTones(
        opts,
        col.toneOf ? (v) => col.toneOf!(v) : undefined,
        inferStatus
      );
    }
    return map;
  }, [columns, distinctOptionsByCol]);

  // Notify server-paged tables when filters or sort change
  const emitServerQuery = useCallback(
    (
      nextSpecs: Record<string, ColumnFilterValue>,
      nextSortKey: string | null,
      nextSortDir: 'asc' | 'desc',
      nextSortTone: BadgeTone | null
    ) => {
      if (!onServerQueryChange) return;
      const colsWithOpts = columns.map((c) => ({
        key: c.key,
        filterColumn: c.filterColumn,
        type: c.type,
        filterType: c.filterType,
        numeric: c.numeric,
        labelOf: c.labelOf ? (v: any) => c.labelOf!(v) : undefined,
        toneOf: c.toneOf ? (v: any) => c.toneOf!(v) : undefined,
        options: distinctOptionsByCol[c.key],
      }));
      const filters = serializeServerFilters(nextSpecs, colsWithOpts);
      const sort = serializeServerSort(nextSortKey, nextSortDir, nextSortTone, colsWithOpts);
      const filtersParam = serializeServerFiltersParam(nextSpecs, colsWithOpts);
      const sortParam = serializeServerSortParam(
        nextSortKey,
        nextSortDir,
        nextSortTone,
        colsWithOpts
      );
      onServerQueryChange({ filters, sort, filtersParam, sortParam });
    },
    [onServerQueryChange, columns, distinctOptionsByCol]
  );

  // URL state synchronization
  useEffect(() => {
    if (!syncUrl || typeof window === 'undefined') return;

    try {
      const sp = new URLSearchParams(window.location.search);
      const urlQ = sp.get('q');
      const urlSort = sp.get('sort');
      const urlDir = sp.get('dir') as 'asc' | 'desc' | null;
      const urlPage = parseInt(sp.get('page') || '', 10);
      const urlSize = parseInt(sp.get('size') || '', 10);

      if (urlQ) setGlobalSearch(urlQ);
      if (urlSort) {
        setSortKey(urlSort);
        if (urlDir === 'asc' || urlDir === 'desc') setSortDir(urlDir);
      }
      if (!isNaN(urlPage) && urlPage > 0) onPageChange(urlPage);
      if (!isNaN(urlSize) && availablePageSizes.includes(urlSize)) onPageSizeChange(urlSize);

      const colFilters: Record<string, string> = {};
      const initialSpecs: Record<string, ColumnFilterValue> = {};
      sp.forEach((val, key) => {
        if (key.startsWith('f_')) {
          const colKey = key.slice(2);
          colFilters[colKey] = val;
          const col = columns.find((c) => c.key === colKey);
          const fType = col ? inferColumnFilterType(col.key, col.type ?? col.filterType) : 'text';
          initialSpecs[colKey] =
            fType === 'list'
              ? { values: [val] }
              : { op: 'contains', value: val };
        }
      });
      if (Object.keys(colFilters).length > 0) {
        setInternalColumnSearch((prev) => ({ ...prev, ...colFilters }));
        setColFilterSpecs((prev) => ({ ...prev, ...initialSpecs }));
        if (onColumnFiltersChange) onColumnFiltersChange(colFilters);
      }
      if (serverSide && onServerQueryChange && (Object.keys(initialSpecs).length > 0 || urlSort)) {
        emitServerQuery(
          initialSpecs,
          urlSort || sortKey,
          urlDir === 'asc' || urlDir === 'desc' ? urlDir : sortDir,
          null
        );
      }
    } catch {
      // Ignore URL parsing errors
    }
  }, [syncUrl]); // Run once on mount

  // Sync state changes back to URL
  const updateUrlParams = useCallback(
    (changes: {
      q?: string;
      sort?: string | null;
      dir?: 'asc' | 'desc';
      page?: number;
      size?: number;
      colFilters?: Record<string, string>;
    }) => {
      if (!syncUrl || typeof window === 'undefined') return;

      try {
        const sp = new URLSearchParams(window.location.search);

        if (changes.q !== undefined) {
          if (changes.q.trim()) sp.set('q', changes.q.trim());
          else sp.delete('q');
        }

        if (changes.sort !== undefined) {
          if (changes.sort) {
            sp.set('sort', changes.sort);
            sp.set('dir', changes.dir || 'asc');
          } else {
            sp.delete('sort');
            sp.delete('dir');
          }
        }

        if (changes.page !== undefined) {
          if (changes.page > 1) sp.set('page', String(changes.page));
          else sp.delete('page');
        }

        if (changes.size !== undefined) {
          if (changes.size !== PAGE_SIZES[0]) sp.set('size', String(changes.size));
          else sp.delete('size');
        }

        if (changes.colFilters !== undefined) {
          Array.from(sp.keys()).forEach((k) => {
            if (k.startsWith('f_')) sp.delete(k);
          });
          Object.entries(changes.colFilters).forEach(([k, v]) => {
            if (v && v.trim()) sp.set(`f_${k}`, v.trim());
          });
        }

        const newSearch = sp.toString();
        const newUrl = `${window.location.pathname}${newSearch ? `?${newSearch}` : ''}${window.location.hash}`;
        window.history.replaceState(null, '', newUrl);
      } catch {
        // Safe fallback
      }
    },
    [syncUrl]
  );

  // Close popovers on click outside, escape, scroll or resize
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      const target = e.target as Node;
      if (popoverRef.current && popoverRef.current.contains(target)) return;
      if (popoverTriggerRef.current && popoverTriggerRef.current.contains(target)) return;
      if (menuRef.current && menuRef.current.contains(target)) return;
      if (menuTriggerRef.current && menuTriggerRef.current.contains(target)) return;
      setActivePopover(null);
      setActiveMenu(null);
    }

    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setActivePopover(null);
        setActiveMenu(null);
      }
    }

    function handleScrollOrResize(e?: Event) {
      // Do not close popover when scrolling inside the popover's own value list
      if (
        e &&
        e.target &&
        popoverRef.current &&
        popoverRef.current.contains(e.target as Node)
      ) {
        return;
      }
      setActivePopover(null);
      setActiveMenu(null);
    }

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    window.addEventListener('resize', handleScrollOrResize);
    window.addEventListener('scroll', handleScrollOrResize, true);

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
      window.removeEventListener('resize', handleScrollOrResize);
      window.removeEventListener('scroll', handleScrollOrResize, true);
    };
  }, []);

  // Build TanStack Table v8 definitions & state (filtering handled by TanStack; sorting uses compareColumnValues directly)
  const tanstackColumns = useMemo<TanStackColumnDef<T, any>[]>(() => {
    return columns.map((col) => {
      const colType = resolveColumnType(col);
      return {
        id: col.key,
        accessorFn: (row: T) => getCellRawValue(row, col),
        enableSorting: false,
        enableColumnFilter: col.filterable !== false && col.key !== 'actions',
        filterFn: (row, columnId, filterValue: ColumnFilterValue) => {
          const raw = row.getValue(columnId);
          return evaluateColumnFilter(raw, filterValue, {
            type: colType,
            labelOf: col.labelOf ? (v) => col.labelOf!(v, row.original) : undefined,
            toneOf: col.toneOf ? (v) => col.toneOf!(v, row.original) : undefined,
            topNThreshold: topNThresholdByCol[col.key],
          });
        },
      };
    });
  }, [columns, getCellRawValue, topNThresholdByCol]);

  const tableInstance = useReactTable({
    data,
    columns: tanstackColumns,
    state: {
      columnFilters: serverOwnsColumnFilters ? [] : tanstackColumnFilters,
      globalFilter: serverSide ? '' : globalSearch,
    },
    globalFilterFn: (row, _columnId, filterValue: string) =>
      rowMatchesGlobalSearch(row.original, normalizeArabicSearch(filterValue)),
    manualFiltering: serverOwnsColumnFilters,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
  });

  // Filtered dataset (before sorting)
  const filteredData = useMemo(() => {
    if (serverOwnsColumnFilters) return data;
    return tableInstance.getFilteredRowModel().rows.map((r) => r.original);
  }, [serverOwnsColumnFilters, data, tableInstance, tanstackColumnFilters, globalSearch]);

  // Sorted dataset (after filtering + sorting, with blanks-at-end & sortTone priority)
  const sortedData = useMemo(() => {
    if (serverOwnsColumnFilters) return data;
    if (!sortKey) return filteredData;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return filteredData;
    const colType = resolveColumnType(col);
    return [...filteredData].sort((a, b) =>
      compareColumnValues(getCellRawValue(a, col), getCellRawValue(b, col), {
        type: colType,
        dir: sortDir,
        sortTone,
        labelOf: col.labelOf ? (v) => col.labelOf!(v) : undefined,
        toneOf: col.toneOf ? (v) => col.toneOf!(v) : undefined,
      })
    );
  }, [
    serverOwnsColumnFilters,
    data,
    filteredData,
    sortKey,
    sortDir,
    sortTone,
    columns,
    getCellRawValue,
  ]);

  // Paginated slice
  const paginatedData = useMemo(() => {
    if (serverSide) return sortedData;
    if (totalCount !== undefined && totalCount > data.length) {
      return sortedData;
    }
    const start = (page - 1) * pageSize;
    return sortedData.slice(start, start + pageSize);
  }, [serverSide, data.length, sortedData, page, pageSize, totalCount]);

  const effectiveTotal = serverSide
    ? (totalCount ?? filteredData.length)
    : filteredData.length;
  const totalPages = Math.max(1, Math.ceil(effectiveTotal / pageSize));

  // If a narrower filter leaves the current page past the end, go back to the last page
  useEffect(() => {
    if (!serverSide && page > totalPages) {
      onPageChange(totalPages);
      updateUrlParams({ page: totalPages });
    }
  }, [serverSide, page, totalPages, onPageChange, updateUrlParams]);

  // Global search change handler
  const handleGlobalSearch = (val: string) => {
    setGlobalSearch(val);
    onPageChange(1);
    updateUrlParams({ q: val, page: 1 });
  };

  // Update a column's rich filter spec (from ColumnFilterMenu or quick filter row)
  const handleColumnFilterSpecChange = useCallback(
    (colKey: string, nextSpec: ColumnFilterValue | undefined) => {
      const updated = { ...colFilterSpecsRef.current };
      if (!nextSpec || !isColumnFilterActive(nextSpec)) {
        delete updated[colKey];
      } else {
        updated[colKey] = nextSpec;
      }
      colFilterSpecsRef.current = updated;
      setColFilterSpecs(updated);
      emitServerQuery(updated, sortKey, sortDir, sortTone);

      // Sync simple string map for quick-filter row & legacy onColumnFiltersChange
      const simpleVal =
        nextSpec?.values && nextSpec.values.length === 1
          ? nextSpec.values[0]
          : nextSpec?.value ?? '';

      const nextSearch = { ...internalColumnSearchRef.current };
      if (!simpleVal) delete nextSearch[colKey];
      else nextSearch[colKey] = simpleVal;
      internalColumnSearchRef.current = nextSearch;
      setInternalColumnSearch(nextSearch);
      if (onColumnFiltersChange) onColumnFiltersChange(nextSearch);
      updateUrlParams({ colFilters: nextSearch, page: 1 });

      onPageChange(1);
    },
    [
      emitServerQuery,
      sortKey,
      sortDir,
      sortTone,
      onColumnFiltersChange,
      updateUrlParams,
      onPageChange,
    ]
  );

  // Quick filter row input change handler
  const handleColumnFilterChange = (key: string, val: string) => {
    const col = columns.find((c) => c.key === key);
    const fType = col ? inferColumnFilterType(col.key, col.type ?? col.filterType) : 'text';
    if (!val || !val.trim()) {
      handleColumnFilterSpecChange(key, undefined);
    } else if (fType === 'list') {
      const prev = colFilterSpecsRef.current[key];
      handleColumnFilterSpecChange(key, {
        ...(prev?.colorTone ? { colorTone: prev.colorTone } : {}),
        values: [val],
      });
    } else {
      handleColumnFilterSpecChange(key, {
        ...colFilterSpecsRef.current[key],
        op: 'contains',
        value: val,
      });
    }
  };

  // Clear all filters handler («مسح كل الفلاتر»)
  const handleClearAllFilters = () => {
    setGlobalSearch('');
    colFilterSpecsRef.current = {};
    internalColumnSearchRef.current = {};
    setColFilterSpecs({});
    setInternalColumnSearch({});
    setSortKey(null);
    setSortDir('asc');
    setSortTone(null);
    if (onColumnFiltersChange) onColumnFiltersChange({});
    emitServerQuery({}, null, 'asc', null);
    onPageChange(1);
    updateUrlParams({ q: '', sort: null, colFilters: {}, page: 1 });
  };

  const activeFiltersCount = useMemo(() => {
    let count = 0;
    if (globalSearch.trim()) count += 1;
    const countedCols = new Set<string>();
    for (const [k, spec] of Object.entries(colFilterSpecs)) {
      if (isColumnFilterActive(spec)) {
        count += 1;
        countedCols.add(k);
      }
    }
    for (const [k, val] of Object.entries(columnSearch)) {
      if (!countedCols.has(k) && val && val.trim()) {
        count += 1;
      }
    }
    if (sortKey) count += 1;
    return count;
  }, [globalSearch, colFilterSpecs, columnSearch, sortKey]);

  // Selection handlers
  const handleSelectAll = (checked: boolean) => {
    if (!onSelectionChange || !getRowId) return;
    if (checked) {
      const ids = paginatedData.map((r) => getRowId(r));
      onSelectionChange(Array.from(new Set([...selectedIds, ...ids])));
    } else {
      const pageIds = new Set(paginatedData.map((r) => getRowId(r)));
      onSelectionChange(selectedIds.filter((id) => !pageIds.has(id)));
    }
  };

  const handleSelectRow = (id: string, checked: boolean) => {
    if (!onSelectionChange) return;
    if (checked) {
      onSelectionChange([...selectedIds, id]);
    } else {
      onSelectionChange(selectedIds.filter((item) => item !== id));
    }
  };

  // Sort toggle handler
  const handleSort = (colKey: string) => {
    let nextKey: string | null = colKey;
    let nextDir: 'asc' | 'desc' = 'asc';

    if (sortKey === colKey) {
      if (sortDir === 'asc') {
        nextDir = 'desc';
      } else {
        nextKey = null;
        nextDir = 'asc';
      }
    }

    setSortKey(nextKey);
    setSortDir(nextDir);
    setSortTone(null);
    emitServerQuery(colFilterSpecs, nextKey, nextDir, null);
    updateUrlParams({ sort: nextKey, dir: nextDir });
  };

  const handlePopoverSortChange = (
    colKey: string,
    dir: 'asc' | 'desc' | null,
    tone?: BadgeTone | null
  ) => {
    const nextKey = dir ? colKey : null;
    const nextDir = dir || 'asc';
    const nextTone = tone ?? null;
    setSortKey(nextKey);
    setSortDir(nextDir);
    setSortTone(nextTone);
    emitServerQuery(colFilterSpecs, nextKey, nextDir, nextTone);
    updateUrlParams({ sort: nextKey, dir: nextDir });
  };

  // Excel Export handler — uses sortedData so both active filters AND active sort order apply to exports!
  const handleExportExcel = async () => {
    if (sortedData.length === 0 || isExporting) return;

    if (onExport) {
      try {
        setIsExporting(true);
        await onExport(sortedData);
      } finally {
        setIsExporting(false);
      }
      return;
    }

    try {
      setIsExporting(true);
      const exportCols = columns.filter(
        (c) => c.key !== 'actions' && c.header && c.header !== 'الإجراء'
      );
      const flat = exportCols.flatMap((col) =>
        col.exportColumns && col.exportColumns.length > 0
          ? col.exportColumns.map((ec) => ({
              header: ec.header,
              numeric: false,
              value: (row: T) => ec.value(row),
            }))
          : [
              {
                header: col.header,
                numeric: !!col.numeric || resolveColumnType(col) === 'number',
                value: (row: T) => getCellExportValue(row, col),
              },
            ]
      );
      const headers = flat.map((c) => c.header);
      const numericColumns = flat
        .map((c, i) => (c.numeric ? i : -1))
        .filter((i) => i >= 0);

      const rows = sortedData.map((row) => flat.map((col) => col.value(row)));

      const blob = await api.exportExcel({
        headers,
        rows,
        numericColumns,
        filename: `${exportFilename || tableId || 'export'}.xlsx`,
      });

      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${exportFilename || tableId || 'export'}.xlsx`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (err: any) {
      alert(err?.message || 'تعذر تصدير ملف الإكسل');
    } finally {
      setIsExporting(false);
    }
  };

  // Error state
  if (error) {
    return <ErrorState message={error} onRetry={onRetry} />;
  }

  const allPageSelected =
    selectable &&
    getRowId &&
    paginatedData.length > 0 &&
    paginatedData.every((r) => selectedIds.includes(getRowId(r)));

  const isColumnSummarizable = (c: ColumnDef<T>): boolean => {
    if (c.summary === 'none') return false;
    if (typeof c.summary === 'function' || c.summary === 'sum' || c.summary === 'avg' || c.summary === 'count') {
      return true;
    }
    if (!(c.numeric || resolveColumnType(c) === 'number')) return false;
    return !/^(index|idx|row|odo|odometer|mileage|per|ratio|rate|percent|pct|ridersPerRoom|occupancy|year|modelYear|level|version|threshold)$/i.test(
      c.key
    );
  };

  const hasNumericColumns = columns.some((c, i) => i > 0 && isColumnSummarizable(c));

  return (
    <div
      className={`flex flex-col rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] overflow-hidden shadow-xs ${className}`}
    >
      {/* Table Toolbar: Global Search, Active Filter Reset («مسح كل الفلاتر»), Excel Export, Extra Controls */}
      {(searchable || exportable || toolbarExtra || activeFiltersCount > 0) && (
        <div className="flex flex-wrap items-center justify-between gap-3 p-3 border-b border-[var(--line)] bg-[var(--surface)]">
          <div className="flex flex-wrap items-center gap-2 flex-1 min-w-[240px]">
            {/* Global Search Input */}
            {searchable && (
              <div className="relative flex-1 max-w-sm">
                <Search
                  size={14}
                  className="absolute inset-y-0 start-2.5 my-auto text-[var(--muted)] pointer-events-none"
                  aria-hidden="true"
                />
                <input
                  type="text"
                  value={globalSearch}
                  onChange={(e) => handleGlobalSearch(e.target.value)}
                  placeholder={globalSearchPlaceholder || 'بحث عام في كل الأعمدة...'}
                  aria-label="البحث العام في الجدول"
                  className="w-full ps-8 pe-7 py-1.5 text-xs rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden transition"
                />
                {globalSearch && (
                  <button
                    type="button"
                    onClick={() => handleGlobalSearch('')}
                    aria-label="مسح البحث العام"
                    className="absolute inset-y-0 end-2 flex items-center text-[var(--muted)] hover:text-[var(--ink)]"
                  >
                    <X size={13} />
                  </button>
                )}
              </div>
            )}

            {/* Clear all filters button («مسح كل الفلاتر») */}
            {activeFiltersCount > 0 && (
              <button
                type="button"
                onClick={handleClearAllFilters}
                aria-label="مسح كل الفلاتر"
                className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-[var(--muted)] hover:text-[var(--danger)] hover:bg-[var(--danger)]/10 rounded-lg border border-dashed border-[var(--line)] transition"
              >
                <X size={13} />
                <span>مسح كل الفلاتر ({activeFiltersCount})</span>
              </button>
            )}
          </div>

          <div className="flex items-center gap-2">
            {toolbarExtra}

            {/* Excel Export Button */}
            {exportable && (
              <button
                type="button"
                disabled={loading || sortedData.length === 0 || isExporting}
                onClick={handleExportExcel}
                aria-label="تصدير السجلات المفلترة إلى ملف إكسل"
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] hover:border-[var(--accent)] disabled:opacity-40 disabled:pointer-events-none transition shadow-2xs"
              >
                <Download size={13} className={isExporting ? 'animate-bounce' : ''} />
                <span>{isExporting ? 'جاري التصدير...' : 'تصدير إكسل'}</span>
              </button>
            )}
          </div>
        </div>
      )}

      {/* Scrollable table container */}
      <div className="relative overflow-x-auto overflow-y-auto max-h-[68vh] pb-3 no-scrollbar">
        <table className="w-full text-xs sm:text-sm text-start border-collapse" id={tableId}>
          {/* Table Header */}
          <thead className="sticky top-0 z-20 bg-[var(--raised)] border-b border-[var(--line)] text-[var(--ink)] shadow-xs">
            {/* Header Titles Row */}
            <tr>
              {selectable && (
                <th scope="col" className="w-10 px-3 py-3 text-center">
                  <input
                    type="checkbox"
                    aria-label="تحديد كل السجلات في الصفحة"
                    checked={allPageSelected}
                    onChange={(e) => handleSelectAll(e.target.checked)}
                    className="h-4 w-4 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)] cursor-pointer"
                  />
                </th>
              )}

              {columns.map((col) => {
                const isSorted = sortKey === col.key;
                const colSpec = colFilterSpecs[col.key];
                const hasQuickSearch = Boolean(
                  columnSearch[col.key] && columnSearch[col.key].trim()
                );
                const hasColumnFilter = isColumnFilterActive(colSpec) || hasQuickSearch;
                const colType = resolveColumnType(col);
                const isNumeric = col.numeric || colType === 'number';

                return (
                  <th
                    key={col.key}
                    scope="col"
                    aria-sort={
                      isSorted
                        ? sortDir === 'asc'
                          ? 'ascending'
                          : 'descending'
                        : undefined
                    }
                    data-header={col.header}
                    data-type={colType}
                    data-numeric={isNumeric ? 'true' : 'false'}
                    style={{ width: col.width }}
                    className={`px-3.5 py-3 font-bold text-start whitespace-nowrap select-none ${
                      isNumeric ? 'text-end' : ''
                    } ${hasColumnFilter || isSorted ? 'bg-[var(--accent)]/5' : ''} ${
                      col.className || ''
                    }`}
                  >
                    <div
                      className={`inline-flex items-center gap-1.5 ${
                        isNumeric ? 'flex-row-reverse' : ''
                      }`}
                    >
                      <span>{col.header}</span>

                      {/* Sort button */}
                      {col.sortable !== false && col.key !== 'actions' && (
                        <button
                          type="button"
                          onClick={() => handleSort(col.key)}
                          aria-label={`ترتيب حسب ${col.header}`}
                          className={`p-0.5 rounded hover:bg-[var(--line)] transition ${
                            isSorted ? 'text-[var(--accent)] font-bold' : 'text-[var(--muted)]'
                          }`}
                        >
                          {isSorted ? (
                            sortDir === 'asc' ? (
                              <ChevronUp size={14} aria-hidden="true" />
                            ) : (
                              <ChevronDown size={14} aria-hidden="true" />
                            )
                          ) : (
                            <ChevronsUpDown size={14} aria-hidden="true" />
                          )}
                        </button>
                      )}

                      {/* Excel-style AutoFilter Popover trigger button */}
                      {col.filterable !== false && col.key !== 'actions' && (
                        <div
                          className="relative inline-block"
                          ref={activePopover?.colKey === col.key ? popoverTriggerRef : undefined}
                        >
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              if (activePopover?.colKey === col.key) {
                                setActivePopover(null);
                              } else {
                                setActiveMenu(null);
                                ensureDistinctLoaded(col);
                                const rect = e.currentTarget.getBoundingClientRect();
                                setActivePopover({ colKey: col.key, rect });
                              }
                            }}
                            aria-label={`تصفية عمود ${col.header}`}
                            className={`relative p-1 rounded-md border transition ${
                              hasColumnFilter || isSorted
                                ? 'text-[var(--accent)] border-[var(--accent)]/40 bg-[var(--accent)]/10 font-bold'
                                : 'text-[var(--muted)] border-transparent hover:bg-[var(--line)]'
                            }`}
                          >
                            <Filter size={13} aria-hidden="true" />
                            {hasColumnFilter && (
                              <span
                                className="absolute -top-0.5 -end-0.5 w-2 h-2 rounded-full bg-[var(--accent)]"
                                aria-hidden="true"
                              />
                            )}
                          </button>
                        </div>
                      )}
                    </div>
                  </th>
                );
              })}

              {rowActions && rowActions.length > 0 && (
                <th scope="col" className="w-12 px-3 py-3 text-center font-bold">
                  <span>الإجراء</span>
                </th>
              )}
            </tr>

            {/* Per-column quick filter row: type-in searchable dropdown for 'list' columns, text box for 'text'/'number'/'date' columns */}
            {showFilterRow && (
              <tr className="border-t border-[var(--line)] bg-[var(--surface)]">
                {selectable && <th className="px-3 py-1.5" />}
                {columns.map((col) => {
                  const isFilterable = col.filterable !== false && col.key !== 'actions';
                  const fType = inferColumnFilterType(col.key, col.type ?? col.filterType);
                  const cacheKey = distinctCacheKey(col);
                  const listOptions = distinctOptionsByCol[col.key] || [];

                  return (
                    <th key={`search-${col.key}`} className="px-2 py-1.5 font-normal">
                      {isFilterable &&
                        (fType === 'list' ? (
                          <SearchableSelect
                            size="sm"
                            clearable
                            value={columnSearch[col.key] || ''}
                            onChange={(val) => handleColumnFilterChange(col.key, val)}
                            options={listOptions}
                            loading={!!loadingDistinct[cacheKey]}
                            onOpen={() => ensureDistinctLoaded(col)}
                            placeholder="اختر أو اكتب..."
                            ariaLabel={`تصفية ${col.header}`}
                          />
                        ) : (
                          <div className="relative">
                            <input
                              type="text"
                              placeholder="بحث..."
                              aria-label={`بحث في ${col.header}`}
                              value={columnSearch[col.key] || ''}
                              onChange={(e) => handleColumnFilterChange(col.key, e.target.value)}
                              className="w-full px-2 py-1 text-[11px] rounded border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden"
                            />
                            {columnSearch[col.key] && (
                              <button
                                type="button"
                                aria-label={`مسح بحث ${col.header}`}
                                onClick={() => handleColumnFilterChange(col.key, '')}
                                className="absolute inset-y-0 end-0 flex items-center pe-1.5 text-[var(--muted)] hover:text-[var(--ink)]"
                              >
                                <X size={11} />
                              </button>
                            )}
                          </div>
                        ))}
                    </th>
                  );
                })}
                {rowActions && rowActions.length > 0 && <th className="px-2 py-1.5" />}
              </tr>
            )}
          </thead>

          {/* Table Body */}
          <tbody className="divide-y divide-[var(--line)]">
            {loading ? (
              <tr>
                <td
                  colSpan={columns.length + (selectable ? 1 : 0) + (rowActions ? 1 : 0)}
                  className="p-8"
                >
                  <div className="space-y-3">
                    {Array.from({ length: 6 }).map((_, i) => (
                      <div key={i} className="flex items-center gap-4 animate-pulse">
                        {selectable && <div className="h-4 w-4 bg-[var(--raised)] rounded" />}
                        {columns.map((c, j) => (
                          <div
                            key={j}
                            style={{ width: c.width || '100%' }}
                            className="h-4 bg-[var(--raised)] rounded-md"
                          />
                        ))}
                        {rowActions && <div className="h-4 w-6 bg-[var(--raised)] rounded" />}
                      </div>
                    ))}
                  </div>
                </td>
              </tr>
            ) : paginatedData.length === 0 ? (
              <tr>
                <td
                  colSpan={columns.length + (selectable ? 1 : 0) + (rowActions ? 1 : 0)}
                  className="p-8 text-center"
                >
                  <EmptyState title={emptyTitle} description={emptyDescription} />
                </td>
              </tr>
            ) : (
              paginatedData.map((row, rowIdx) => {
                const id = getRowId ? getRowId(row) : String(rowIdx);
                const isSelected = selectedIds.includes(id);

                return (
                  <tr
                    key={id}
                    className={`transition-colors duration-100 ${
                      isSelected ? 'bg-[var(--row-selected)]' : 'hover:bg-[var(--row-hover)]'
                    }`}
                  >
                    {selectable && (
                      <td className="w-10 px-3 py-2.5 text-center">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={(e) => handleSelectRow(id, e.target.checked)}
                          className="h-4 w-4 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)] cursor-pointer"
                        />
                      </td>
                    )}

                    {columns.map((col) => {
                      const content = col.render
                        ? col.render(row, rowIdx)
                        : getCellDisplayString(row, col);
                      const isNumeric = col.numeric || resolveColumnType(col) === 'number';

                      return (
                        <td
                          key={col.key}
                          className={`px-3.5 py-2.5 text-start whitespace-nowrap ${
                            isNumeric ? 'text-end num' : ''
                          } ${col.className || ''}`}
                        >
                          {content}
                        </td>
                      );
                    })}

                    {rowActions && rowActions.length > 0 && (
                      <td
                        className="w-12 px-3 py-2 text-center relative"
                        ref={activeMenu?.id === id ? menuTriggerRef : undefined}
                      >
                        <button
                          type="button"
                          aria-label="خيارات السجل"
                          aria-haspopup="true"
                          aria-expanded={activeMenu?.id === id}
                          onClick={(e) => {
                            e.stopPropagation();
                            if (activeMenu?.id === id) {
                              setActiveMenu(null);
                            } else {
                              setActivePopover(null);
                              const rect = e.currentTarget.getBoundingClientRect();
                              setActiveMenu({ id, row, rect });
                            }
                          }}
                          className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                        >
                          <MoreVertical size={16} />
                        </button>
                      </td>
                    )}
                  </tr>
                );
              })
            )}
          </tbody>

          {/* Totals Footer Row (BR / Table Standard) */}
          {showTotals && !loading && filteredData.length > 0 && hasNumericColumns && (
            <tfoot className="border-t-2 border-[var(--line)] bg-[var(--raised)] font-bold text-xs sm:text-sm">
              <tr>
                {selectable && <td className="px-3 py-2.5" />}
                {columns.map((col, idx) => {
                  if (idx === 0) {
                    return (
                      <td key="total-label" className="px-3.5 py-2.5 text-start text-[var(--ink)]">
                        <span>{serverSide ? 'إجمالي هذه الصفحة' : 'إجمالي المعروض'}</span>
                        {!serverSide && filteredData.length !== (totalCount ?? data.length) && (
                          <span className="text-[10px] text-[var(--muted)] font-normal ms-1">
                            (الكل: {totalCount ?? data.length})
                          </span>
                        )}
                      </td>
                    );
                  }

                  if (col.summary === 'none') {
                    return <td key={`total-${col.key}`} className="px-3.5 py-2.5" />;
                  }

                  if (typeof col.summary === 'function') {
                    return (
                      <td
                        key={`total-${col.key}`}
                        className="px-3.5 py-2.5 text-end num text-[var(--ink)]"
                      >
                        {col.summary(filteredData)}
                      </td>
                    );
                  }

                  if (col.summary === 'count') {
                    return (
                      <td
                        key={`total-${col.key}`}
                        className="px-3.5 py-2.5 text-end num text-[var(--ink)]"
                      >
                        {filteredData.length.toLocaleString('en-US')}
                      </td>
                    );
                  }

                  if (isColumnSummarizable(col)) {
                    let countValid = 0;
                    const sum = filteredData.reduce((acc, row) => {
                      const raw = getCellRawValue(row, col);
                      if (raw === null || raw === undefined || raw === '') return acc;
                      const n =
                        typeof raw === 'number'
                          ? raw
                          : parseFloat(String(raw).replace(/,/g, ''));
                      if (!Number.isFinite(n)) return acc;
                      countValid += 1;
                      return acc + n;
                    }, 0);
                    const value =
                      col.summary === 'avg'
                        ? countValid > 0
                          ? sum / countValid
                          : 0
                        : sum;

                    return (
                      <td
                        key={`total-${col.key}`}
                        className="px-3.5 py-2.5 text-end num text-[var(--ink)]"
                      >
                        {formatDataTableFooterTotal(value, col)}
                      </td>
                    );
                  }

                  return <td key={`total-${col.key}`} className="px-3.5 py-2.5" />;
                })}
                {rowActions && rowActions.length > 0 && <td className="px-3.5 py-2.5" />}
              </tr>
            </tfoot>
          )}
        </table>
      </div>

      {/* Pagination & Totals Count Footer */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-t border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
        <div className="flex items-center gap-2">
          {loading ? (
            <span className="text-[var(--muted)]">جاري تحميل السجلات...</span>
          ) : (
            <span>
              عرض{' '}
              <strong className="num text-[var(--ink)] font-semibold">
                {effectiveTotal === 0 ? 0 : (page - 1) * pageSize + 1}
              </strong>{' '}
              إلى{' '}
              <strong className="num text-[var(--ink)] font-semibold">
                {Math.min(page * pageSize, effectiveTotal)}
              </strong>{' '}
              {serverSide ? (
                <>
                  من إجمالي:{' '}
                  <strong className="num text-[var(--ink)] font-semibold">
                    {effectiveTotal}
                  </strong>{' '}
                  سجل
                </>
              ) : (
                <>
                  من إجمالي المعروض:{' '}
                  <strong className="num text-[var(--ink)] font-semibold">
                    {filteredData.length}
                  </strong>{' '}
                  سجل
                  {filteredData.length !== (totalCount ?? data.length) && (
                    <span className="ms-1 text-[var(--muted)]">
                      (إجمالي الكل:{' '}
                      <strong className="num text-[var(--ink)] font-semibold">
                        {totalCount ?? data.length}
                      </strong>{' '}
                      سجل)
                    </span>
                  )}
                </>
              )}
            </span>
          )}

          <div className="flex items-center gap-1.5 ms-4">
            <span>لكل صفحة:</span>
            <select
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              aria-label="عدد السجلات لكل صفحة"
              className="py-1 px-2 rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
            >
              {availablePageSizes.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
        </div>

        {/* Page navigation buttons */}
        {totalPages > 1 && (
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
              aria-label="الصفحة السابقة"
              className="p-1.5 rounded-md border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:pointer-events-none transition"
            >
              <ChevronRight size={15} />
            </button>

            <span className="px-2 num font-semibold text-[var(--ink)]">
              {page} / {totalPages}
            </span>

            <button
              type="button"
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
              aria-label="الصفحة التالية"
              className="p-1.5 rounded-md border border-[var(--line)] hover:bg-[var(--raised)] disabled:opacity-40 disabled:pointer-events-none transition"
            >
              <ChevronLeft size={15} />
            </button>
          </div>
        )}
      </div>

      {/* Shared Excel-Style ColumnFilterMenu Popover */}
      {activePopover &&
        (() => {
          const col = columns.find((c) => c.key === activePopover.colKey);
          if (!col) return null;
          const colType = resolveColumnType(col);
          const cacheKey = distinctCacheKey(col);
          const options = distinctOptionsByCol[col.key] || [];
          const tones = availableTonesByCol[col.key] || [];

          return (
            <ColumnFilterMenu
              columnKey={col.key}
              columnHeader={col.header}
              columnType={colType}
              filter={colFilterSpecs[col.key]}
              onFilterChange={(next) => handleColumnFilterSpecChange(col.key, next)}
              sortDir={sortKey === col.key ? sortDir : null}
              sortTone={sortKey === col.key ? sortTone : null}
              onSortChange={(dir, tone) => handlePopoverSortChange(col.key, dir, tone)}
              options={options}
              loadingOptions={!!loadingDistinct[cacheKey]}
              availableTones={tones}
              anchorRect={activePopover.rect}
              onClose={() => setActivePopover(null)}
              popoverRef={popoverRef}
            />
          );
        })()}

      {/* Portaled Row Actions Dropdown Menu */}
      {activeMenu &&
        typeof document !== 'undefined' &&
        (() => {
          const isRtl =
            typeof document !== 'undefined' &&
            (document.dir === 'rtl' || document.documentElement.dir === 'rtl');
          const menuWidth = 160;
          let left = isRtl
            ? activeMenu.rect.left
            : activeMenu.rect.right - menuWidth;
          left = Math.max(8, Math.min(window.innerWidth - menuWidth - 8, left));
          const estimatedHeight = Math.min(260, (rowActions?.length || 3) * 40 + 16);
          const openUpward =
            activeMenu.rect.bottom + estimatedHeight > window.innerHeight &&
            activeMenu.rect.top > estimatedHeight;

          return createPortal(
            <div
              ref={menuRef}
              role="menu"
              aria-label="خيارات السجل"
              style={{
                position: 'fixed',
                top: openUpward
                  ? `${activeMenu.rect.top - 4}px`
                  : `${activeMenu.rect.bottom + 4}px`,
                left: `${left}px`,
                transform: openUpward ? 'translateY(-100%)' : undefined,
                zIndex: 9999,
              }}
              className="w-40 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl py-1.5 font-medium text-xs text-start"
            >
              {rowActions?.map((action, actionIdx) => {
                const isDisabled = action.disabled ? action.disabled(activeMenu.row) : false;
                return (
                  <button
                    key={actionIdx}
                    type="button"
                    disabled={isDisabled}
                    onClick={() => {
                      const rowData = activeMenu.row;
                      setActiveMenu(null);
                      action.onClick(rowData);
                    }}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-start transition disabled:opacity-40 ${
                      action.danger
                        ? 'text-[var(--danger)] hover:bg-[var(--row-hover)]'
                        : 'hover:bg-[var(--row-hover)]'
                    }`}
                  >
                    {action.icon}
                    <span>{action.label}</span>
                  </button>
                );
              })}
            </div>,
            document.body
          );
        })()}
    </div>
  );
}
