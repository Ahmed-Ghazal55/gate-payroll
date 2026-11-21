'use client';

import React, { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import {
  ArrowDownAZ,
  ArrowUpAZ,
  Check,
  ChevronDown,
  ChevronUp,
  ChevronsUpDown,
  Filter,
  Palette,
  RotateCcw,
  Search,
  SlidersHorizontal,
  X,
} from 'lucide-react';
import {
  type BadgeTone,
  type ColumnDataType,
  type ColumnFilterOption,
  type ColumnFilterValue,
  type FilterOperator,
  DATE_OPERATORS,
  NUMBER_OPERATORS,
  TEXT_OPERATORS,
  TONE_META,
  applySearchResultsSelection,
  compareColumnValues,
  computeTopNThreshold,
  evaluateColumnFilter,
  extractDistinctOptions,
  extractDistinctTones,
  filterSelectOptions,
  isColumnFilterActive,
  resolveColumnType,
  toggleSelectAllOptions,
} from '@/lib/columnFilter';

export interface ColumnFilterMenuProps {
  columnKey: string;
  columnHeader: string;
  columnType: ColumnDataType;
  filter: ColumnFilterValue | undefined;
  onFilterChange: (next: ColumnFilterValue | undefined) => void;
  sortDir: 'asc' | 'desc' | null;
  sortTone?: BadgeTone | null;
  onSortChange: (dir: 'asc' | 'desc' | null, tone?: BadgeTone | null) => void;
  options: ColumnFilterOption[];
  loadingOptions?: boolean;
  availableTones?: { tone: BadgeTone; label: string; swatch: string; count: number }[];
  anchorRect: DOMRect;
  onClose: () => void;
  popoverRef?: React.RefObject<HTMLDivElement | null>;
}

const TYPE_BADGE_LABELS: Record<ColumnDataType, string> = {
  text: 'نص',
  list: 'قائمة',
  number: 'رقمي',
  date: 'تاريخ',
};

export function ColumnFilterMenu({
  columnKey,
  columnHeader,
  columnType,
  filter,
  onFilterChange,
  sortDir,
  sortTone,
  onSortChange,
  options,
  loadingOptions = false,
  availableTones = [],
  anchorRect,
  onClose,
  popoverRef,
}: ColumnFilterMenuProps) {
  const [searchTerm, setSearchTerm] = useState<string>('');
  const propOp: FilterOperator | '' =
    filter?.op && filter.op !== 'in' && filter.op !== 'color' ? filter.op : '';
  const [draftOp, setDraftOp] = useState<FilterOperator | ''>(propOp);
  const [draftValue, setDraftValue] = useState<string>(filter?.value ?? '');
  const [draftValueTo, setDraftValueTo] = useState<string>(filter?.valueTo ?? '');

  useEffect(() => {
    setSearchTerm('');
    setDraftOp(propOp);
    setDraftValue(filter?.value ?? '');
    setDraftValueTo(filter?.valueTo ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [columnKey]);

  useEffect(() => {
    if (propOp) {
      setDraftOp(propOp);
      setDraftValue(filter?.value ?? '');
      setDraftValueTo(filter?.valueTo ?? '');
    } else if (
      draftValue !== '' ||
      draftValueTo !== '' ||
      draftOp === 'thisMonth' ||
      draftOp === 'lastMonth'
    ) {
      setDraftOp('');
      setDraftValue('');
      setDraftValueTo('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [propOp, filter?.value, filter?.valueTo]);

  const operators = useMemo(() => {
    if (columnType === 'number') return NUMBER_OPERATORS;
    if (columnType === 'date') return DATE_OPERATORS;
    return TEXT_OPERATORS;
  }, [columnType]);

  const shownOptions = useMemo(
    () => filterSelectOptions(options, searchTerm),
    [options, searchTerm]
  );

  const selectedSet = useMemo(() => {
    if (filter?.values === undefined) return null; // null means ALL selected
    return new Set(filter.values);
  }, [filter?.values]);

  const allShownChecked = useMemo(() => {
    if (shownOptions.length === 0) return false;
    if (selectedSet === null) return true;
    return shownOptions.every((o) => selectedSet.has(o.value));
  }, [shownOptions, selectedSet]);

  const someShownChecked = useMemo(() => {
    if (shownOptions.length === 0) return false;
    if (selectedSet === null) return true;
    return shownOptions.some((o) => selectedSet.has(o.value));
  }, [shownOptions, selectedSet]);

  const hasActiveFilter = isColumnFilterActive(filter);

  const handleToggleOption = (rawValue: string, checked: boolean) => {
    const baseSet = new Set(
      selectedSet === null ? options.map((o) => o.value) : Array.from(selectedSet)
    );
    if (checked) baseSet.add(rawValue);
    else baseSet.delete(rawValue);

    const nextFilter: ColumnFilterValue = { ...filter };
    if (baseSet.size >= options.length && options.every((o) => baseSet.has(o.value))) {
      delete nextFilter.values;
    } else {
      nextFilter.values = Array.from(baseSet);
    }
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleSelectAllChange = (checked: boolean) => {
    const nextValues = toggleSelectAllOptions(
      options,
      searchTerm,
      filter?.values,
      checked
    );
    const nextFilter: ColumnFilterValue = { ...filter };
    if (nextValues === undefined) {
      delete nextFilter.values;
    } else {
      nextFilter.values = nextValues;
    }
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleApplySearchResults = () => {
    if (!searchTerm.trim()) return;
    const nextFilter = applySearchResultsSelection(options, searchTerm, filter);
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleOperatorChange = (opStr: string) => {
    const nextFilter: ColumnFilterValue = { ...filter };
    if (!opStr) {
      setDraftOp('');
      setDraftValue('');
      setDraftValueTo('');
      delete nextFilter.op;
      delete nextFilter.value;
      delete nextFilter.valueTo;
      onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
      return;
    }
    const nextOp = opStr as FilterOperator;
    setDraftOp(nextOp);
    nextFilter.op = nextOp;
    if (nextOp === 'thisMonth' || nextOp === 'lastMonth') {
      setDraftValue('');
      setDraftValueTo('');
      delete nextFilter.value;
      delete nextFilter.valueTo;
    } else {
      if (draftValue !== '') {
        nextFilter.value = draftValue;
      } else {
        delete nextFilter.value;
      }
      if (nextOp === 'between') {
        if (draftValueTo !== '') {
          nextFilter.valueTo = draftValueTo;
        } else {
          delete nextFilter.valueTo;
        }
      } else {
        setDraftValueTo('');
        delete nextFilter.valueTo;
      }
    }
    const hasConditionValue =
      nextOp === 'thisMonth' ||
      nextOp === 'lastMonth' ||
      (nextFilter.value !== undefined && String(nextFilter.value).trim() !== '') ||
      (nextOp === 'between' &&
        nextFilter.valueTo !== undefined &&
        String(nextFilter.valueTo).trim() !== '');
    if (!hasConditionValue) {
      delete nextFilter.op;
      delete nextFilter.value;
      delete nextFilter.valueTo;
    }
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleConditionValueChange = (val: string, isSecond = false) => {
    const defaultOp: FilterOperator =
      columnType === 'number'
        ? 'eq'
        : columnType === 'date'
          ? 'eq'
          : 'contains';
    const effectiveOp: FilterOperator =
      draftOp && draftOp !== 'in' && draftOp !== 'color'
        ? draftOp
        : filter?.op && filter.op !== 'in' && filter.op !== 'color'
          ? filter.op
          : defaultOp;
    if (!draftOp) setDraftOp(effectiveOp);
    const nextVal = isSecond ? draftValue : val;
    const nextValTo = isSecond ? val : draftValueTo;
    if (isSecond) {
      setDraftValueTo(val);
    } else {
      setDraftValue(val);
    }
    const nextFilter: ColumnFilterValue = {
      ...filter,
      op: effectiveOp,
      value: nextVal,
      ...(effectiveOp === 'between' ? { valueTo: nextValTo } : {}),
    };
    const hasConditionValue =
      nextVal.trim() !== '' || (effectiveOp === 'between' && nextValTo.trim() !== '');
    if (!hasConditionValue) {
      delete nextFilter.op;
      delete nextFilter.value;
      delete nextFilter.valueTo;
    }
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleToneFilterToggle = (tone: BadgeTone) => {
    const nextFilter: ColumnFilterValue = { ...filter };
    if (nextFilter.colorTone === tone) {
      delete nextFilter.colorTone;
    } else {
      nextFilter.colorTone = tone;
    }
    onFilterChange(isColumnFilterActive(nextFilter) ? nextFilter : undefined);
  };

  const handleClearColumnFilter = () => {
    setSearchTerm('');
    setDraftOp('');
    setDraftValue('');
    setDraftValueTo('');
    onFilterChange(undefined);
    if (sortDir || sortTone) {
      onSortChange(null, null);
    }
  };

  const handleSelectOnly = (rawValue: string) => {
    const nextFilter: ColumnFilterValue = { ...filter, values: [rawValue] };
    onFilterChange(nextFilter);
  };

  if (typeof document === 'undefined') return null;

  const isRtl =
    document.dir === 'rtl' || document.documentElement.dir === 'rtl';
  const popoverWidth = 288;
  let left = isRtl
    ? anchorRect.right - popoverWidth
    : anchorRect.left;
  left = Math.max(8, Math.min(window.innerWidth - popoverWidth - 8, left));
  const estimatedHeight = availableTones.length > 0 ? 470 : 390;
  const openUpward =
    anchorRect.bottom + estimatedHeight > window.innerHeight &&
    anchorRect.top > estimatedHeight;

  const ascLabel =
    columnType === 'number'
      ? 'الأصغر ← الأكبر ↑'
      : columnType === 'date'
        ? 'الأقدم ← الأحدث ↑'
        : 'ترتيب أ ← ي ↑';
  const descLabel =
    columnType === 'number'
      ? 'الأكبر ← الأصغر ↓'
      : columnType === 'date'
        ? 'الأحدث ← الأقدم ↓'
        : 'ترتيب ي ← أ ↓';

  const activeOp = draftOp;
  const needsValueInput =
    activeOp !== '' && activeOp !== 'thisMonth' && activeOp !== 'lastMonth';
  const inputType =
    columnType === 'date'
      ? 'date'
      : columnType === 'number' || activeOp === 'topN'
        ? 'number'
        : 'text';

  return createPortal(
    <div
      ref={popoverRef as React.RefObject<HTMLDivElement>}
      role="dialog"
      aria-label={`تصفية ${columnHeader}`}
      style={{
        position: 'fixed',
        top: openUpward
          ? `${anchorRect.top - 6}px`
          : `${anchorRect.bottom + 6}px`,
        left: `${left}px`,
        transform: openUpward ? 'translateY(-100%)' : undefined,
        zIndex: 9999,
      }}
      className="w-72 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-xl p-3 font-normal text-xs text-start select-none"
    >
      {/* Header */}
      <div className="flex items-center justify-between pb-2 border-b border-[var(--line)] mb-2">
        <div className="flex items-center gap-1.5 min-w-0">
          <span className="font-bold text-[11px] text-[var(--ink)] truncate">
            خيارات {columnHeader}
          </span>
          <span className="px-1.5 py-0.5 text-[10px] rounded bg-[var(--raised)] text-[var(--muted)] font-medium">
            {TYPE_BADGE_LABELS[columnType]}
          </span>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="إغلاق قائمة التصفية"
          className="p-0.5 rounded text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]"
        >
          <X size={14} />
        </button>
      </div>

      {/* 1. Sort A->Z / Z->A / Smallest->Largest */}
      <div className="flex items-center gap-1.5 mb-2.5">
        <button
          type="button"
          onClick={() => onSortChange(sortDir === 'asc' && !sortTone ? null : 'asc', null)}
          className={`flex-1 inline-flex items-center justify-center gap-1 py-1.5 px-2 rounded-lg border text-center transition font-medium ${
            sortDir === 'asc' && !sortTone
              ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
              : 'border-[var(--line)] hover:bg-[var(--raised)]'
          }`}
        >
          <ArrowUpAZ size={13} aria-hidden="true" />
          <span>{ascLabel}</span>
        </button>
        <button
          type="button"
          onClick={() => onSortChange(sortDir === 'desc' && !sortTone ? null : 'desc', null)}
          className={`flex-1 inline-flex items-center justify-center gap-1 py-1.5 px-2 rounded-lg border text-center transition font-medium ${
            sortDir === 'desc' && !sortTone
              ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
              : 'border-[var(--line)] hover:bg-[var(--raised)]'
          }`}
        >
          <ArrowDownAZ size={13} aria-hidden="true" />
          <span>{descLabel}</span>
        </button>
      </div>

      {/* 2. Sort by Color & Filter by Color (when column renders status tones) */}
      {availableTones.length > 0 && (
        <div className="mb-2.5 pb-2 border-b border-[var(--line)] space-y-2">
          <div>
            <div className="flex items-center gap-1 text-[10px] font-semibold text-[var(--muted)] mb-1">
              <Palette size={11} aria-hidden="true" />
              <span>ترتيب حسب اللون</span>
            </div>
            <div className="flex flex-wrap gap-1">
              {availableTones.map((t) => {
                const isActive = sortTone === t.tone;
                return (
                  <button
                    key={`sort-tone-${t.tone}`}
                    type="button"
                    onClick={() =>
                      onSortChange(
                        isActive ? null : sortDir || 'asc',
                        isActive ? null : t.tone
                      )
                    }
                    aria-label={`ترتيب حسب اللون ${t.label}`}
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full border text-[10px] transition ${
                      isActive
                        ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                        : 'border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]'
                    }`}
                  >
                    <span
                      className="w-2 h-2 rounded-full shrink-0"
                      style={{ backgroundColor: t.swatch }}
                      aria-hidden="true"
                    />
                    <span>{t.label.split(' ')[0]}</span>
                  </button>
                );
              })}
            </div>
          </div>

          <div>
            <div className="flex items-center gap-1 text-[10px] font-semibold text-[var(--muted)] mb-1">
              <Palette size={11} aria-hidden="true" />
              <span>تصفية حسب اللون</span>
            </div>
            <div className="flex flex-wrap gap-1">
              {availableTones.map((t) => {
                const isActive = filter?.colorTone === t.tone;
                return (
                  <button
                    key={`filter-tone-${t.tone}`}
                    type="button"
                    onClick={() => handleToneFilterToggle(t.tone)}
                    aria-label={`تصفية حسب اللون ${t.label}`}
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full border text-[10px] transition ${
                      isActive
                        ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                        : 'border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]'
                    }`}
                  >
                    <span
                      className="w-2 h-2 rounded-full shrink-0"
                      style={{ backgroundColor: t.swatch }}
                      aria-hidden="true"
                    />
                    <span>{t.label.split(' ')[0]}</span>
                    <span className="num text-[9px] text-[var(--muted)]">({t.count})</span>
                  </button>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* 3. Operator / Condition Filter (Text / Number / Date filters) */}
      <div className="mb-2.5 pb-2 border-b border-[var(--line)] space-y-1.5">
        <div className="flex items-center justify-between gap-2">
          <label className="inline-flex items-center gap-1 text-[10px] font-semibold text-[var(--muted)]">
            <SlidersHorizontal size={11} aria-hidden="true" />
            <span>
              {columnType === 'number'
                ? 'فلاتر الأرقام'
                : columnType === 'date'
                  ? 'فلاتر التاريخ'
                  : 'فلاتر النص'}
            </span>
          </label>
          <select
            aria-label={`عامل التصفية لعمود ${columnHeader}`}
            value={activeOp}
            onChange={(e) => handleOperatorChange(e.target.value)}
            className="flex-1 max-w-[175px] py-1 px-2 rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-[11px] focus:border-[var(--accent)] focus:outline-hidden"
          >
            <option value="">بدون شرط مخصص</option>
            {operators.map((o) => (
              <option key={o.op} value={o.op}>
                {o.label}
              </option>
            ))}
          </select>
        </div>

        {needsValueInput && (
          <div className="flex items-center gap-1.5">
            <input
              type={inputType}
              aria-label={`قيمة شرط ${columnHeader}`}
              placeholder={
                activeOp === 'topN'
                  ? 'أعلى N (مثال: 10)'
                  : activeOp === 'between'
                    ? 'من...'
                    : 'القيمة...'
              }
              value={draftValue}
              onChange={(e) => handleConditionValueChange(e.target.value, false)}
              className="w-full px-2 py-1 text-xs rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
            />
            {activeOp === 'between' && (
              <input
                type={inputType}
                aria-label={`القيمة الثانية لشرط ${columnHeader}`}
                placeholder="إلى..."
                value={draftValueTo}
                onChange={(e) => handleConditionValueChange(e.target.value, true)}
                className="w-full px-2 py-1 text-xs rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
              />
            )}
          </div>
        )}
      </div>

      {/* 4. Value List Search Input (Enter applies immediately to matches) */}
      <div className="relative mb-1.5">
        <Search
          size={12}
          className="absolute inset-y-0 start-2.5 my-auto text-[var(--muted)] pointer-events-none"
          aria-hidden="true"
        />
        <input
          type="text"
          aria-label="بحث داخل القيم"
          placeholder="بحث داخل القيم (Enter للتطبيق)..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              handleApplySearchResults();
            }
          }}
          className="w-full ps-7 pe-6 py-1 text-xs rounded-md border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
        />
        {searchTerm && (
          <button
            type="button"
            onClick={() => setSearchTerm('')}
            aria-label="مسح بحث القيم"
            className="absolute inset-y-0 end-1.5 flex items-center text-[var(--muted)] hover:text-[var(--ink)]"
          >
            <X size={12} />
          </button>
        )}
      </div>

      {searchTerm.trim() && shownOptions.length > 0 && (
        <button
          type="button"
          onClick={handleApplySearchResults}
          className="w-full mb-1.5 py-1 px-2 rounded-md bg-[var(--accent)]/10 text-[var(--accent)] hover:bg-[var(--accent)]/20 text-[11px] font-semibold flex items-center justify-center gap-1 transition"
        >
          <Check size={12} aria-hidden="true" />
          <span>تحديد نتائج البحث فقط ({shownOptions.length})</span>
        </button>
      )}

      {/* 5. Distinct Values Checkbox List */}
      <div className="max-h-44 overflow-y-auto space-y-1 my-1.5 pe-0.5">
        {loadingOptions && options.length === 0 ? (
          <div className="py-4 text-center text-[var(--muted)] text-[11px]">
            جاري تحميل القيم...
          </div>
        ) : shownOptions.length === 0 ? (
          <div className="py-3 text-center text-[var(--muted)] text-[11px]">
            لا توجد قيم مطابقة
          </div>
        ) : (
          <>
            {/* Master Select All / Select All Search Results checkbox */}
            <label className="flex items-center justify-between gap-2 cursor-pointer hover:bg-[var(--row-hover)] px-1.5 py-1 rounded font-semibold border-b border-[var(--line)]/60">
              <div className="flex items-center gap-2 min-w-0">
                <input
                  type="checkbox"
                  checked={allShownChecked}
                  ref={(el) => {
                    if (el) {
                      el.indeterminate = !allShownChecked && someShownChecked;
                    }
                  }}
                  onChange={(e) => handleSelectAllChange(e.target.checked)}
                  className="h-3.5 w-3.5 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)]"
                />
                <span className="truncate">
                  {searchTerm.trim() ? 'تحديد كل نتائج البحث' : 'تحديد الكل'}
                </span>
              </div>
              <span className="num text-[10px] text-[var(--muted)]">
                ({shownOptions.length})
              </span>
            </label>

            {shownOptions.map((opt, idx) => {
              const isChecked =
                selectedSet === null || selectedSet.has(opt.value);
              const swatch = opt.tone ? TONE_META[opt.tone]?.swatch : undefined;

              return (
                <div
                  key={`${opt.value || '__empty__'}-${idx}`}
                  className="group flex items-center justify-between gap-1.5 hover:bg-[var(--row-hover)] px-1.5 py-1 rounded"
                >
                  <label className="flex items-center gap-2 min-w-0 flex-1 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={isChecked}
                      onChange={(e) => handleToggleOption(opt.value, e.target.checked)}
                      className="h-3.5 w-3.5 rounded border-[var(--line)] text-[var(--accent)] accent-[var(--accent)] shrink-0"
                    />
                    {swatch && (
                      <span
                        className="w-2 h-2 rounded-full shrink-0"
                        style={{ backgroundColor: swatch }}
                        aria-hidden="true"
                      />
                    )}
                    <span className="truncate" title={opt.label}>
                      {opt.label || '(فارغ)'}
                    </span>
                  </label>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        handleSelectOnly(opt.value);
                      }}
                      aria-label={`تحديد ${opt.label || '(فارغ)'} فقط`}
                      className="opacity-0 group-hover:opacity-100 focus:opacity-100 px-1 py-0.2 rounded text-[10px] font-semibold text-[var(--accent)] hover:bg-[var(--accent)]/15 transition"
                    >
                      فقط
                    </button>
                    {opt.count !== undefined && opt.count > 0 && (
                      <span className="num text-[10px] text-[var(--muted)] px-1.5 py-0.2 rounded bg-[var(--raised)]">
                        {opt.count}
                      </span>
                    )}
                  </div>
                </div>
              );
            })}
          </>
        )}
      </div>

      {/* 6. Footer Quick Actions */}
      <div className="flex items-center justify-between pt-2 border-t border-[var(--line)] text-[11px]">
        <button
          type="button"
          onClick={() => handleSelectAllChange(true)}
          className="text-[var(--accent)] font-semibold hover:underline"
        >
          تحديد الكل
        </button>
        <button
          type="button"
          onClick={handleClearColumnFilter}
          disabled={!hasActiveFilter && !sortDir && !sortTone}
          className="inline-flex items-center gap-1 text-[var(--muted)] hover:text-[var(--danger)] disabled:opacity-40 transition"
        >
          <RotateCcw size={11} aria-hidden="true" />
          <span>إعادة تعيين</span>
        </button>
      </div>
    </div>,
    document.body
  );
}

export interface AutoFilterColDef<T> {
  key: string;
  header: string;
  type?: ColumnDataType;
  numeric?: boolean;
  sortable?: boolean;
  filterable?: boolean;
  getValue?: (row: T) => any;
  labelOf?: (value: any, row?: T) => string;
  toneOf?: (value: any, row?: T) => BadgeTone | null | undefined;
  filterOptions?: ColumnFilterOption[];
}

export interface TableAutoFilterController<T> {
  rows: T[];
  columns: AutoFilterColDef<T>[];
  filters: Record<string, ColumnFilterValue>;
  sortKey: string | null;
  sortDir: 'asc' | 'desc';
  sortTone: BadgeTone | null;
  activeCount: number;
  clearAll: () => void;
  setColumnFilter: (key: string, next: ColumnFilterValue | undefined) => void;
  toggleSort: (key: string) => void;
  setSort: (key: string, dir: 'asc' | 'desc' | null, tone?: BadgeTone | null) => void;
  optionsByCol: Record<string, ColumnFilterOption[]>;
  tonesByCol: Record<
    string,
    { tone: BadgeTone; label: string; swatch: string; count: number }[]
  >;
  activePopover: { colKey: string; rect: DOMRect } | null;
  setActivePopover: React.Dispatch<
    React.SetStateAction<{ colKey: string; rect: DOMRect } | null>
  >;
  popoverRef: React.RefObject<HTMLDivElement | null>;
}

/**
 * Shared Excel AutoFilter controller hook for custom <table> elements so every table
 * in the app uses the exact same ColumnFilterMenu and operator/sort engine as DataTable.
 */
export function useTableAutoFilter<T>(
  data: ReadonlyArray<T>,
  columns: ReadonlyArray<AutoFilterColDef<T>>
): TableAutoFilterController<T> {
  const [filters, setFilters] = useState<Record<string, ColumnFilterValue>>({});
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [sortTone, setSortTone] = useState<BadgeTone | null>(null);
  const [activePopover, setActivePopover] = useState<{
    colKey: string;
    rect: DOMRect;
  } | null>(null);
  const popoverRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!activePopover) return;
    function handleOutside(e: MouseEvent) {
      if (popoverRef.current && popoverRef.current.contains(e.target as Node)) return;
      setActivePopover(null);
    }
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setActivePopover(null);
    }
    document.addEventListener('mousedown', handleOutside);
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('mousedown', handleOutside);
      document.removeEventListener('keydown', handleKey);
    };
  }, [activePopover]);

  const getRaw = useCallback((row: T, col: AutoFilterColDef<T>) => {
    if (col.getValue) return col.getValue(row);
    return (row as any)?.[col.key];
  }, []);

  const optionsByCol = useMemo(() => {
    const map: Record<string, ColumnFilterOption[]> = {};
    for (const col of columns) {
      if (col.filterable === false || col.key === 'actions') continue;
      const colType = resolveColumnType(col);
      map[col.key] = extractDistinctOptions(
        data.map((r) => getRaw(r, col)),
        {
          labelOf: col.labelOf ? (v) => col.labelOf!(v) : undefined,
          toneOf: col.toneOf ? (v) => col.toneOf!(v) : undefined,
          presetOptions: col.filterOptions,
          includeEmpty: true,
          type: colType,
        }
      );
    }
    return map;
  }, [data, columns, getRaw]);

  const tonesByCol = useMemo(() => {
    const map: Record<
      string,
      { tone: BadgeTone; label: string; swatch: string; count: number }[]
    > = {};
    for (const col of columns) {
      if (col.filterable === false || col.key === 'actions') continue;
      const opts = optionsByCol[col.key] || [];
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
  }, [columns, optionsByCol]);

  const topNThresholdByCol = useMemo(() => {
    const map: Record<string, number | null> = {};
    for (const col of columns) {
      const spec = filters[col.key];
      if (spec?.op === 'topN' && spec.value) {
        const n = Number(spec.value);
        if (Number.isFinite(n) && n > 0) {
          map[col.key] = computeTopNThreshold(
            data.map((r) => getRaw(r, col)),
            n
          );
        }
      }
    }
    return map;
  }, [columns, filters, data, getRaw]);

  const rows = useMemo(() => {
    const filtered = data.filter((row) => {
      for (const col of columns) {
        const spec = filters[col.key];
        if (!isColumnFilterActive(spec)) continue;
        const colType = resolveColumnType(col);
        const raw = getRaw(row, col);
        const ok = evaluateColumnFilter(raw, spec, {
          type: colType,
          labelOf: col.labelOf ? (v) => col.labelOf!(v, row) : undefined,
          toneOf: col.toneOf ? (v) => col.toneOf!(v, row) : undefined,
          topNThreshold: topNThresholdByCol[col.key],
        });
        if (!ok) return false;
      }
      return true;
    });

    if (!sortKey) return filtered;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return filtered;
    const colType = resolveColumnType(col);
    return [...filtered].sort((a, b) =>
      compareColumnValues(getRaw(a, col), getRaw(b, col), {
        type: colType,
        dir: sortDir,
        sortTone,
        labelOf: col.labelOf ? (v) => col.labelOf!(v) : undefined,
        toneOf: col.toneOf ? (v) => col.toneOf!(v) : undefined,
      })
    );
  }, [data, columns, filters, sortKey, sortDir, sortTone, getRaw, topNThresholdByCol]);

  const setColumnFilter = useCallback(
    (key: string, next: ColumnFilterValue | undefined) => {
      setFilters((prev) => {
        const updated = { ...prev };
        if (!next || !isColumnFilterActive(next)) delete updated[key];
        else updated[key] = next;
        return updated;
      });
    },
    []
  );

  const toggleSort = useCallback(
    (key: string) => {
      if (sortKey !== key) {
        setSortKey(key);
        setSortDir('asc');
        setSortTone(null);
      } else if (sortDir === 'asc') {
        setSortDir('desc');
        setSortTone(null);
      } else {
        setSortKey(null);
        setSortDir('asc');
        setSortTone(null);
      }
    },
    [sortKey, sortDir]
  );

  const setSort = useCallback(
    (key: string, dir: 'asc' | 'desc' | null, tone?: BadgeTone | null) => {
      setSortKey(dir ? key : null);
      setSortDir(dir || 'asc');
      setSortTone(tone ?? null);
    },
    []
  );

  const clearAll = useCallback(() => {
    setFilters({});
    setSortKey(null);
    setSortDir('asc');
    setSortTone(null);
  }, []);

  const activeCount = useMemo(() => {
    let c = 0;
    for (const spec of Object.values(filters)) {
      if (isColumnFilterActive(spec)) c += 1;
    }
    if (sortKey) c += 1;
    return c;
  }, [filters, sortKey]);

  return {
    rows,
    columns: columns as AutoFilterColDef<T>[],
    filters,
    sortKey,
    sortDir,
    sortTone,
    activeCount,
    clearAll,
    setColumnFilter,
    toggleSort,
    setSort,
    optionsByCol,
    tonesByCol,
    activePopover,
    setActivePopover,
    popoverRef,
  };
}

export function AutoFilterTh<T>({
  controller,
  colKey,
  className = 'p-2.5 text-start',
  children,
}: {
  controller: TableAutoFilterController<T>;
  colKey: string;
  className?: string;
  children?: React.ReactNode;
}) {
  const col = controller.columns.find((c) => c.key === colKey);
  if (!col) {
    return (
      <th scope="col" className={className}>
        {children}
      </th>
    );
  }
  const isSorted = controller.sortKey === col.key;
  const hasFilter = isColumnFilterActive(controller.filters[col.key]);
  const colType = resolveColumnType(col);
  const isOpen = controller.activePopover?.colKey === col.key;

  return (
    <th
      scope="col"
      aria-sort={
        isSorted
          ? controller.sortDir === 'asc'
            ? 'ascending'
            : 'descending'
          : undefined
      }
      data-type={colType}
      className={`${className} select-none ${
        hasFilter || isSorted ? 'bg-[var(--accent)]/5' : ''
      }`}
    >
      <div className="inline-flex items-center gap-1">
        <span>{children ?? col.header}</span>
        {col.sortable !== false && col.key !== 'actions' && (
          <button
            type="button"
            onClick={() => controller.toggleSort(col.key)}
            aria-label={`ترتيب حسب ${col.header}`}
            className={`p-0.5 rounded hover:bg-[var(--line)] transition ${
              isSorted ? 'text-[var(--accent)] font-bold' : 'text-[var(--muted)]'
            }`}
          >
            {isSorted ? (
              controller.sortDir === 'asc' ? (
                <ChevronUp size={13} aria-hidden="true" />
              ) : (
                <ChevronDown size={13} aria-hidden="true" />
              )
            ) : (
              <ChevronsUpDown size={13} aria-hidden="true" />
            )}
          </button>
        )}
        {col.filterable !== false && col.key !== 'actions' && (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              if (isOpen) {
                controller.setActivePopover(null);
              } else {
                const rect = e.currentTarget.getBoundingClientRect();
                controller.setActivePopover({ colKey: col.key, rect });
              }
            }}
            aria-label={`تصفية عمود ${col.header}`}
            className={`relative p-0.5 rounded border transition ${
              hasFilter || isSorted
                ? 'text-[var(--accent)] border-[var(--accent)]/40 bg-[var(--accent)]/10 font-bold'
                : 'text-[var(--muted)] border-transparent hover:bg-[var(--line)]'
            }`}
          >
            <Filter size={12} aria-hidden="true" />
            {hasFilter && (
              <span
                className="absolute -top-0.5 -end-0.5 w-1.5 h-1.5 rounded-full bg-[var(--accent)]"
                aria-hidden="true"
              />
            )}
          </button>
        )}
      </div>
      {isOpen && controller.activePopover && (
        <ColumnFilterMenu
          columnKey={col.key}
          columnHeader={col.header}
          columnType={colType}
          filter={controller.filters[col.key]}
          onFilterChange={(next) => controller.setColumnFilter(col.key, next)}
          sortDir={isSorted ? controller.sortDir : null}
          sortTone={isSorted ? controller.sortTone : null}
          onSortChange={(dir, tone) => controller.setSort(col.key, dir, tone)}
          options={controller.optionsByCol[col.key] || []}
          availableTones={controller.tonesByCol[col.key] || []}
          anchorRect={controller.activePopover.rect}
          onClose={() => controller.setActivePopover(null)}
          popoverRef={controller.popoverRef}
        />
      )}
    </th>
  );
}

export function AutoFilterBar<T>({
  controller,
  totalCount,
}: {
  controller: TableAutoFilterController<T>;
  totalCount?: number;
}) {
  if (controller.activeCount === 0) return null;
  return (
    <div className="flex items-center justify-between gap-2 px-3 py-1.5 bg-[var(--surface)] border-b border-[var(--line)] text-xs">
      <div className="text-[11px] text-[var(--muted)]">
        {totalCount !== undefined && (
          <span>
            عرض <strong className="num text-[var(--ink)]">{controller.rows.length}</strong> من{' '}
            <strong className="num text-[var(--ink)]">{totalCount}</strong>
          </span>
        )}
      </div>
      <button
        type="button"
        onClick={controller.clearAll}
        aria-label="مسح كل الفلاتر"
        className="inline-flex items-center gap-1 px-2 py-1 text-[11px] font-medium text-[var(--muted)] hover:text-[var(--danger)] hover:bg-[var(--danger)]/10 rounded-md border border-dashed border-[var(--line)] transition"
      >
        <X size={12} aria-hidden="true" />
        <span>مسح كل الفلاتر ({controller.activeCount})</span>
      </button>
    </div>
  );
}

