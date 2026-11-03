'use client';

import React, { useState, useMemo, useRef, useEffect } from 'react';
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
} from 'lucide-react';
import { Skeleton, EmptyState, ErrorState } from './EmptyState';

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
}

export function DataTable<T extends Record<string, any>>({
  columns,
  data,
  totalCount,
  loading = false,
  error = null,
  onRetry,
  page = 1,
  pageSize = 20,
  onPageChange,
  onPageSizeChange,
  selectable = false,
  selectedIds = [],
  onSelectionChange,
  getRowId,
  rowActions,
  emptyTitle,
  emptyDescription,
  tableId = 'data-table',
  className = '',
}: DataTableProps<T>) {
  // Sorting state
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  // Per-column filter inputs (search row)
  const [columnSearch, setColumnSearch] = useState<Record<string, string>>({});

  // Column popover filters (distinct value sets)
  const [activePopoverCol, setActivePopoverCol] = useState<string | null>(null);
  const [selectedColValues, setSelectedColValues] = useState<Record<string, Set<string>>>({});
  const [popoverSearchTerm, setPopoverSearchTerm] = useState<string>('');

  // Row actions active dropdown menu
  const [activeMenuRowId, setActiveMenuRowId] = useState<string | null>(null);

  const popoverRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  // Close popovers on click outside or escape
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        setActivePopoverCol(null);
      }
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setActiveMenuRowId(null);
      }
    }
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setActivePopoverCol(null);
        setActiveMenuRowId(null);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  // Cell text extractor helper
  const getCellString = (row: T, col: ColumnDef<T>): string => {
    if (col.getValue) return String(col.getValue(row) ?? '');
    const val = row[col.key];
    if (val === null || val === undefined) return '';
    return String(val);
  };

  // Distinct values for the active popover column
  const popoverDistinctValues = useMemo(() => {
    if (!activePopoverCol) return [];
    const col = columns.find((c) => c.key === activePopoverCol);
    if (!col) return [];
    const set = new Set<string>();
    data.forEach((r) => set.add(getCellString(r, col)));
    return Array.from(set).sort(new Intl.Collator('ar', { numeric: true }).compare);
  }, [activePopoverCol, columns, data]);

  // Filtered dataset
  const filteredData = useMemo(() => {
    return data.filter((row) => {
      // 1. Check per-column text search inputs
      for (const [key, term] of Object.entries(columnSearch)) {
        if (!term) continue;
        const col = columns.find((c) => c.key === key);
        if (!col) continue;
        const cell = getCellString(row, col).toLowerCase();
        if (!cell.includes(term.toLowerCase())) return false;
      }

      // 2. Check column distinct values selections
      for (const [key, allowedValues] of Object.entries(selectedColValues)) {
        if (!allowedValues || allowedValues.size === 0) continue;
        const col = columns.find((c) => c.key === key);
        if (!col) continue;
        const cell = getCellString(row, col);
        if (!allowedValues.has(cell)) return false;
      }

      return true;
    });
  }, [data, columnSearch, selectedColValues, columns]);

  // Sorted dataset
  const sortedData = useMemo(() => {
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
  }, [filteredData, sortKey, sortDir, columns]);

  // Paginated slice
  const paginatedData = useMemo(() => {
    // If server provides totalCount > data.length, data is already paginated by server
    if (totalCount !== undefined && totalCount > data.length) {
      return sortedData;
    }
    const start = (page - 1) * pageSize;
    return sortedData.slice(start, start + pageSize);
  }, [sortedData, page, pageSize, totalCount, data.length]);

  const effectiveTotal = totalCount !== undefined ? totalCount : sortedData.length;
  const totalPages = Math.max(1, Math.ceil(effectiveTotal / pageSize));

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
    if (sortKey === colKey) {
      if (sortDir === 'asc') setSortDir('desc');
      else {
        setSortKey(null);
        setSortDir('asc');
      }
    } else {
      setSortKey(colKey);
      setSortDir('asc');
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

  return (
    <div className={`flex flex-col rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] overflow-hidden shadow-xs ${className}`}>
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
                    <div className={`inline-flex items-center gap-1.5 ${col.numeric ? 'flex-row-reverse' : ''}`}>
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
                        <div className="relative inline-block">
                          <button
                            type="button"
                            onClick={() => {
                              if (activePopoverCol === col.key) setActivePopoverCol(null);
                              else {
                                setActivePopoverCol(col.key);
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

                          {/* Filter Popover Dialog */}
                          {activePopoverCol === col.key && (
                            <div
                              ref={popoverRef}
                              role="dialog"
                              aria-label={`تصفية ${col.header}`}
                              className="absolute top-full start-0 mt-1.5 w-60 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl p-3 z-30 font-normal text-xs text-start"
                            >
                              <div className="flex items-center justify-between pb-2 border-b border-[var(--line)] mb-2">
                                <span className="font-bold text-[11px] text-[var(--muted)]">
                                  خيارات {col.header}
                                </span>
                                <button
                                  type="button"
                                  onClick={() => setActivePopoverCol(null)}
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
                                    v.toLowerCase().includes(popoverSearchTerm.toLowerCase())
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
                            </div>
                          )}
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
                        onChange={(e) =>
                          setColumnSearch((prev) => ({ ...prev, [col.key]: e.target.value }))
                        }
                        className="w-full px-2 py-1 text-[11px] rounded border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden"
                      />
                      {columnSearch[col.key] && (
                        <button
                          type="button"
                          onClick={() =>
                            setColumnSearch((prev) => {
                              const next = { ...prev };
                              delete next[col.key];
                              return next;
                            })
                          }
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
                  <Skeleton rows={6} />
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
                      <td className="w-12 px-3 py-2 text-center relative">
                        <button
                          type="button"
                          aria-label="خيارات السجل"
                          aria-haspopup="true"
                          aria-expanded={activeMenuRowId === id}
                          onClick={() => setActiveMenuRowId(activeMenuRowId === id ? null : id)}
                          className="p-1 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                        >
                          <MoreVertical size={16} />
                        </button>

                        {/* Row Action Dropdown */}
                        {activeMenuRowId === id && (
                          <div
                            ref={menuRef}
                            className="absolute end-4 top-8 mt-1 w-40 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl py-1.5 z-30 font-medium text-xs text-start"
                          >
                            {rowActions.map((action, actionIdx) => {
                              const isDisabled = action.disabled ? action.disabled(row) : false;
                              return (
                                <button
                                  key={actionIdx}
                                  type="button"
                                  disabled={isDisabled}
                                  onClick={() => {
                                    setActiveMenuRowId(null);
                                    action.onClick(row);
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
                          </div>
                        )}
                      </td>
                    )}
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 border-t border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)]">
        <div className="flex items-center gap-2">
          <span>
            عرض{' '}
            <strong className="num text-[var(--ink)] font-semibold">
              {filteredData.length === 0 ? 0 : (page - 1) * pageSize + 1}
            </strong>{' '}
            إلى{' '}
            <strong className="num text-[var(--ink)] font-semibold">
              {Math.min(page * pageSize, effectiveTotal)}
            </strong>{' '}
            من إجمالي{' '}
            <strong className="num text-[var(--ink)] font-semibold">
              {effectiveTotal}
            </strong>{' '}
            سجل
          </span>

          {onPageSizeChange && (
            <div className="flex items-center gap-1.5 ms-4">
              <span>لكل صفحة:</span>
              <select
                value={pageSize}
                onChange={(e) => onPageSizeChange(Number(e.target.value))}
                aria-label="عدد السجلات لكل صفحة"
                className="py-1 px-2 rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
              >
                {[10, 20, 50, 100].map((size) => (
                  <option key={size} value={size}>
                    {size}
                  </option>
                ))}
              </select>
            </div>
          )}
        </div>

        {/* Page navigation buttons */}
        {onPageChange && (
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
    </div>
  );
}
