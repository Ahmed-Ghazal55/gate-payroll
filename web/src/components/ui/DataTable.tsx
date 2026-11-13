'use client';

import React, { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
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
import { Skeleton, EmptyState, ErrorState } from './EmptyState';
import { api } from '@/lib/api';

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
  numeric?: boolean;
  sortable?: boolean;
  filterable?: boolean;
  width?: string;
  render?: (row: T, index: number) => React.ReactNode;
  getValue?: (row: T) => any;
  className?: string;
  summary?: 'sum' | 'count' | ((rows: T[]) => React.ReactNode);
}

export interface RowAction<T> {
  label: string;
  onClick: (row: T) => void;
  icon?: React.ReactNode;
  danger?: boolean;
  disabled?: (row: T) => boolean;
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
  // Standard extensions (R8 Task A1)
  searchable?: boolean;
  globalSearchPlaceholder?: string;
  syncUrl?: boolean;
  exportable?: boolean;
  exportFilename?: string;
  onExport?: (filteredRows: T[]) => Promise<void> | void;
  toolbarExtra?: React.ReactNode;
  showTotals?: boolean;
  columnFilters?: Record<string, string>;
  onColumnFiltersChange?: (filters: Record<string, string>) => void;
  serverSide?: boolean;
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
  columnFilters: controlledColumnFilters,
  onColumnFiltersChange,
  serverSide = false,
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

  // Global search state
  const [globalSearch, setGlobalSearch] = useState<string>('');

  // Per-column filter inputs (search row)
  const [internalColumnSearch, setInternalColumnSearch] = useState<Record<string, string>>({});
  const columnSearch = controlledColumnFilters ?? internalColumnSearch;

  const updateColumnSearch = useCallback(
    (next: Record<string, string> | ((prev: Record<string, string>) => Record<string, string>)) => {
      setInternalColumnSearch((prev) => {
        const updated = typeof next === 'function' ? next(prev) : next;
        if (onColumnFiltersChange) onColumnFiltersChange(updated);
        return updated;
      });
      onPageChange(1);
    },
    [onColumnFiltersChange, onPageChange]
  );

  // Column popover filters (distinct value sets)
  const [activePopover, setActivePopover] = useState<{ colKey: string; rect: DOMRect } | null>(null);
  const [selectedColValues, setSelectedColValues] = useState<Record<string, Set<string>>>({});
  const [popoverSearchTerm, setPopoverSearchTerm] = useState<string>('');

  // Row actions active dropdown menu
  const [activeMenu, setActiveMenu] = useState<{ id: string; row: T; rect: DOMRect } | null>(null);

  // Export state
  const [isExporting, setIsExporting] = useState<boolean>(false);

  const popoverRef = useRef<HTMLDivElement>(null);
  const popoverTriggerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuTriggerRef = useRef<HTMLTableCellElement>(null);

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
      sp.forEach((val, key) => {
        if (key.startsWith('f_')) {
          const colKey = key.slice(2);
          colFilters[colKey] = val;
        }
      });
      if (Object.keys(colFilters).length > 0) {
        setInternalColumnSearch((prev) => ({ ...prev, ...colFilters }));
        if (onColumnFiltersChange) onColumnFiltersChange(colFilters);
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
          // Remove old f_ params
          Array.from(sp.keys()).forEach((k) => {
            if (k.startsWith('f_')) sp.delete(k);
          });
          // Set new ones
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

    function handleScrollOrResize() {
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

  // Cell text extractor helper
  const getCellString = useCallback((row: T, col: ColumnDef<T>): string => {
    if (col.getValue) return String(col.getValue(row) ?? '');
    const val = row[col.key];
    if (val === null || val === undefined) return '';
    return String(val);
  }, []);

  // Cell export raw value helper
  const getCellExportValue = useCallback((row: T, col: ColumnDef<T>): string | number => {
    const raw = col.getValue ? col.getValue(row) : row[col.key];
    if (raw === null || raw === undefined) return '';
    if (col.numeric) {
      const n = typeof raw === 'number' ? raw : parseFloat(String(raw).replace(/,/g, ''));
      return isNaN(n) ? String(raw) : n;
    }
    return String(raw);
  }, []);

  // Distinct values for the active popover column
  const popoverDistinctValues = useMemo(() => {
    if (!activePopover) return [];
    const col = columns.find((c) => c.key === activePopover.colKey);
    if (!col) return [];
    const set = new Set<string>();
    data.forEach((r) => set.add(getCellString(r, col)));
    return Array.from(set).sort(new Intl.Collator('ar', { numeric: true }).compare);
  }, [activePopover?.colKey, columns, data, getCellString]);

  // Filtered dataset
  const filteredData = useMemo(() => {
    if (serverSide) return data;
    const normGlobal = normalizeArabicSearch(globalSearch);

    return data.filter((row) => {
      // 1. Check Global Search (matches any searchable column)
      if (normGlobal) {
        let matchGlobal = false;
        for (const col of columns) {
          if (col.filterable === false || col.key === 'actions') continue;
          const cellNorm = normalizeArabicSearch(getCellString(row, col));
          if (cellNorm.includes(normGlobal)) {
            matchGlobal = true;
            break;
          }
        }
        if (!matchGlobal) return false;
      }

      // 2. Check per-column text search inputs (OR within column via comma/pipe, AND across columns)
      for (const [key, term] of Object.entries(columnSearch)) {
        if (!term || !term.trim()) continue;
        const col = columns.find((c) => c.key === key);
        if (!col) continue;

        const cellNorm = normalizeArabicSearch(getCellString(row, col));
        const terms = term
          .split(/[,|]/)
          .map((t) => normalizeArabicSearch(t))
          .filter(Boolean);

        if (terms.length > 0) {
          const colMatches = terms.some((t) => cellNorm.includes(t));
          if (!colMatches) return false;
        }
      }

      // 3. Check column distinct values selections (popovers)
      for (const [key, allowedValues] of Object.entries(selectedColValues)) {
        if (!allowedValues || allowedValues.size === 0) continue;
        const col = columns.find((c) => c.key === key);
        if (!col) continue;
        const cell = getCellString(row, col);
        if (!allowedValues.has(cell)) return false;
      }

      return true;
    });
  }, [data, globalSearch, columnSearch, selectedColValues, columns, getCellString]);

  // Sorted dataset
  const sortedData = useMemo(() => {
    if (serverSide) return data;
    if (!sortKey) return filteredData;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return filteredData;

    return [...filteredData].sort((a, b) => {
      const aVal = getCellString(a, col);
      const bVal = getCellString(b, col);

      if (col.numeric) {
        const aNum = parseFloat(aVal.replace(/,/g, '')) || 0;
        const bNum = parseFloat(bVal.replace(/,/g, '')) || 0;
        return sortDir === 'asc' ? aNum - bNum : bNum - aNum;
      }

      const cmp = new Intl.Collator('ar', { numeric: true }).compare(aVal, bVal);
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [serverSide, data, filteredData, sortKey, sortDir, columns, getCellString]);

  // Paginated slice
  const paginatedData = useMemo(() => {
    if (serverSide) return data;
    if (totalCount !== undefined && totalCount > data.length) {
      return sortedData;
    }
    const start = (page - 1) * pageSize;
    return sortedData.slice(start, start + pageSize);
  }, [serverSide, data, sortedData, page, pageSize, totalCount]);

  const effectiveTotal = totalCount !== undefined ? totalCount : (serverSide ? data.length : filteredData.length);
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

  // Column filter change handler
  const handleColumnFilterChange = (key: string, val: string) => {
    updateColumnSearch((prev) => {
      const next = { ...prev };
      if (!val) delete next[key];
      else next[key] = val;
      updateUrlParams({ colFilters: next, page: 1 });
      return next;
    });
  };

  // Clear all filters handler
  const handleClearAllFilters = () => {
    setGlobalSearch('');
    updateColumnSearch({});
    setSelectedColValues({});
    onPageChange(1);
    updateUrlParams({ q: '', colFilters: {}, page: 1 });
  };

  const activeFiltersCount = useMemo(() => {
    let count = 0;
    if (globalSearch.trim()) count += 1;
    for (const val of Object.values(columnSearch)) {
      if (val && val.trim()) count += 1;
    }
    for (const set of Object.values(selectedColValues)) {
      if (set && set.size > 0) count += 1;
    }
    return count;
  }, [globalSearch, columnSearch, selectedColValues]);

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
    updateUrlParams({ sort: nextKey, dir: nextDir });
  };

  // Excel Export handler
  const handleExportExcel = async () => {
    if (filteredData.length === 0 || isExporting) return;

    if (onExport) {
      try {
        setIsExporting(true);
        await onExport(filteredData);
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
      const headers = exportCols.map((c) => c.header);
      const numericColumns = exportCols
        .map((c, i) => (c.numeric ? i : -1))
        .filter((i) => i >= 0);

      const rows = filteredData.map((row) =>
        exportCols.map((col) => getCellExportValue(row, col))
      );

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

  const hasNumericColumns = columns.some((c) => c.numeric);

  return (
    <div
      className={`flex flex-col rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] overflow-hidden shadow-xs ${className}`}
    >
      {/* Table Toolbar: Global Search, Active Filter Reset, Excel Export, Extra Controls */}
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

            {/* Clear all filters button */}
            {activeFiltersCount > 0 && (
              <button
                type="button"
                onClick={handleClearAllFilters}
                className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium text-[var(--muted)] hover:text-[var(--danger)] hover:bg-[var(--danger)]/10 rounded-lg border border-dashed border-[var(--line)] transition"
              >
                <X size={13} />
                <span>مسح الفلاتر ({activeFiltersCount})</span>
              </button>
            )}
          </div>

          <div className="flex items-center gap-2">
            {toolbarExtra}

            {/* Excel Export Button */}
            {exportable && (
              <button
                type="button"
                disabled={loading || filteredData.length === 0 || isExporting}
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
      <div className="relative overflow-x-auto overflow-y-auto max-h-[68vh] no-scrollbar">
        <table className="w-full text-xs sm:text-sm text-start border-collapse" id={tableId}>
          {/* Table Header */}
          <thead className="sticky top-0 z-20 bg-[var(--raised)] border-b border-[var(--line)] text-[var(--ink)] shadow-xs">
            {/* Header Titles Row */}
            <tr>
              {selectable && (
                <th className="w-10 px-3 py-3 text-center">
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
                const hasValueFilter =
                  selectedColValues[col.key] && selectedColValues[col.key].size > 0;

                return (
                  <th
                    key={col.key}
                    data-header={col.header}
                    data-numeric={col.numeric ? 'true' : 'false'}
                    style={{ width: col.width }}
                    className={`px-3.5 py-3 font-bold text-start whitespace-nowrap select-none ${
                      col.numeric ? 'text-end' : ''
                    } ${col.className || ''}`}
                  >
                    <div
                      className={`inline-flex items-center gap-1.5 ${
                        col.numeric ? 'flex-row-reverse' : ''
                      }`}
                    >
                      <span>{col.header}</span>

                      {/* Sort button */}
                      {col.sortable !== false && (
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
                              <ChevronUp size={14} />
                            ) : (
                              <ChevronDown size={14} />
                            )
                          ) : (
                            <ChevronsUpDown size={14} />
                          )}
                        </button>
                      )}

                      {/* Filter popover button */}
                      {col.filterable !== false && (
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
                                const rect = e.currentTarget.getBoundingClientRect();
                                setActivePopover({ colKey: col.key, rect });
                                setPopoverSearchTerm('');
                              }
                            }}
                            aria-label={`تصفية عمود ${col.header}`}
                            className={`p-0.5 rounded hover:bg-[var(--line)] transition ${
                              hasValueFilter ? 'text-[var(--accent)]' : 'text-[var(--muted)]'
                            }`}
                          >
                            <Filter size={13} />
                          </button>
                        </div>
                      )}
                    </div>
                  </th>
                );
              })}

              {rowActions && rowActions.length > 0 && (
                <th className="w-12 px-3 py-3 text-center font-bold">
                  <span>الإجراء</span>
                </th>
              )}
            </tr>

            {/* Per-column search input row */}
            <tr className="border-t border-[var(--line)] bg-[var(--surface)]">
              {selectable && <th className="px-3 py-1.5" />}
              {columns.map((col) => (
                <th key={`search-${col.key}`} className="px-2 py-1.5 font-normal">
                  {col.filterable !== false && (
                    <div className="relative">
                      <input
                        type="text"
                        placeholder={`بحث...`}
                        value={columnSearch[col.key] || ''}
                        onChange={(e) => handleColumnFilterChange(col.key, e.target.value)}
                        className="w-full px-2 py-1 text-[11px] rounded border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden"
                      />
                      {columnSearch[col.key] && (
                        <button
                          type="button"
                          onClick={() => handleColumnFilterChange(col.key, '')}
                          className="absolute inset-y-0 end-0 flex items-center pe-1.5 text-[var(--muted)] hover:text-[var(--ink)]"
                        >
                          <X size={11} />
                        </button>
                      )}
                    </div>
                  )}
                </th>
              ))}
              {rowActions && rowActions.length > 0 && <th className="px-2 py-1.5" />}
            </tr>
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
                      const content = col.render ? col.render(row, rowIdx) : getCellString(row, col);

                      return (
                        <td
                          key={col.key}
                          className={`px-3.5 py-2.5 text-start whitespace-nowrap ${
                            col.numeric ? 'text-end num' : ''
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
                        <span>إجمالي المعروض</span>
                        {filteredData.length !== (totalCount ?? data.length) && (
                          <span className="text-[10px] text-[var(--muted)] font-normal ms-1">
                            (الكل: {totalCount ?? data.length})
                          </span>
                        )}
                      </td>
                    );
                  }

                  if (col.numeric) {
                    const sum = filteredData.reduce((acc, row) => {
                      const raw = col.getValue ? col.getValue(row) : row[col.key];
                      const n =
                        typeof raw === 'number'
                          ? raw
                          : parseFloat(String(raw || '').replace(/,/g, '')) || 0;
                      return acc + n;
                    }, 0);

                    return (
                      <td
                        key={`total-${col.key}`}
                        className="px-3.5 py-2.5 text-end num text-[var(--ink)]"
                      >
                        {sum.toLocaleString('en-US', {
                          minimumFractionDigits: 0,
                          maximumFractionDigits: 2,
                        })}
                      </td>
                    );
                  }

                  return <td key={`total-${col.key}`} className="px-3.5 py-2.5" />;
                })}
                {rowActions && rowActions.length > 0 && <td className="px-3 py-2.5" />}
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

      {/* Portaled Column Filter Popover */}
      {activePopover &&
        typeof document !== 'undefined' &&
        (() => {
          const col = columns.find((c) => c.key === activePopover.colKey);
          if (!col) return null;
          const isSorted = sortKey === col.key;
          const isRtl =
            typeof document !== 'undefined' &&
            (document.dir === 'rtl' || document.documentElement.dir === 'rtl');
          const popoverWidth = 240;
          let left = isRtl
            ? activePopover.rect.right - popoverWidth
            : activePopover.rect.left;
          left = Math.max(8, Math.min(window.innerWidth - popoverWidth - 8, left));
          const openUpward =
            activePopover.rect.bottom + 340 > window.innerHeight &&
            activePopover.rect.top > 300;

          return createPortal(
            <div
              ref={popoverRef}
              role="dialog"
              aria-label={`تصفية ${col.header}`}
              style={{
                position: 'fixed',
                top: openUpward
                  ? `${activePopover.rect.top - 6}px`
                  : `${activePopover.rect.bottom + 6}px`,
                left: `${left}px`,
                transform: openUpward ? 'translateY(-100%)' : undefined,
                zIndex: 9999,
              }}
              className="w-60 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl p-3 font-normal text-xs text-start"
            >
              <div className="flex items-center justify-between pb-2 border-b border-[var(--line)] mb-2">
                <span className="font-bold text-[11px] text-[var(--muted)]">
                  خيارات {col.header}
                </span>
                <button
                  type="button"
                  onClick={() => setActivePopover(null)}
                  className="text-[var(--muted)] hover:text-[var(--ink)]"
                >
                  <X size={14} />
                </button>
              </div>

              {/* Asc / Desc buttons */}
              <div className="flex items-center gap-1.5 mb-2.5">
                <button
                  type="button"
                  onClick={() => {
                    setSortKey(col.key);
                    setSortDir('asc');
                    updateUrlParams({ sort: col.key, dir: 'asc' });
                  }}
                  className={`flex-1 py-1 px-2 rounded-md border text-center transition font-medium ${
                    isSorted && sortDir === 'asc'
                      ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)]'
                      : 'border-[var(--line)] hover:bg-[var(--raised)]'
                  }`}
                >
                  تصاعدي ↑
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSortKey(col.key);
                    setSortDir('desc');
                    updateUrlParams({ sort: col.key, dir: 'desc' });
                  }}
                  className={`flex-1 py-1 px-2 rounded-md border text-center transition font-medium ${
                    isSorted && sortDir === 'desc'
                      ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)]'
                      : 'border-[var(--line)] hover:bg-[var(--raised)]'
                  }`}
                >
                  تنازلي ↓
                </button>
              </div>

              {/* Search inside values */}
              <div className="relative mb-2">
                <input
                  type="text"
                  placeholder="بحث داخل القيم..."
                  value={popoverSearchTerm}
                  onChange={(e) => setPopoverSearchTerm(e.target.value)}
                  className="w-full px-2.5 py-1 text-xs rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
                />
              </div>

              {/* Value selection checkboxes */}
              <div className="max-h-36 overflow-y-auto space-y-1.5 my-2 no-scrollbar">
                {popoverDistinctValues
                  .filter((v) =>
                    normalizeArabicSearch(v).includes(normalizeArabicSearch(popoverSearchTerm))
                  )
                  .map((val, idx) => {
                    const colSet = selectedColValues[col.key];
                    const isChecked = !colSet || colSet.has(val);

                    return (
                      <label
                        key={idx}
                        className="flex items-center gap-2 cursor-pointer hover:bg-[var(--row-hover)] px-1.5 py-0.5 rounded"
                      >
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={(e) => {
                            const nextSet = new Set(colSet || popoverDistinctValues);
                            if (e.target.checked) nextSet.add(val);
                            else nextSet.delete(val);

                            setSelectedColValues((prev) => ({
                              ...prev,
                              [col.key]: nextSet,
                            }));
                          }}
                          className="h-3.5 w-3.5 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)]"
                        />
                        <span className="truncate">{val || '(فارغ)'}</span>
                      </label>
                    );
                  })}
              </div>

              {/* Quick actions */}
              <div className="flex items-center justify-between pt-2 border-t border-[var(--line)] text-[11px]">
                <button
                  type="button"
                  onClick={() => {
                    setSelectedColValues((prev) => ({
                      ...prev,
                      [col.key]: new Set(popoverDistinctValues),
                    }));
                  }}
                  className="text-[var(--accent)] font-semibold hover:underline"
                >
                  تحديد الكل
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setSelectedColValues((prev) => {
                      const next = { ...prev };
                      delete next[col.key];
                      return next;
                    });
                  }}
                  className="text-[var(--muted)] hover:text-[var(--ink)]"
                >
                  إعادة تعيين
                </button>
              </div>
            </div>,
            document.body
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
