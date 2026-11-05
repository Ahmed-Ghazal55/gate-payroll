import React, { useState, useRef, useEffect } from 'react';
import { Search, X, Download, Upload, Filter, FileSpreadsheet, FileText, ChevronDown } from 'lucide-react';

export interface FilterChip {
  id: string;
  label: string;
  active: boolean;
  count?: number;
  onToggle: () => void;
}

export interface ToolbarProps {
  searchQuery?: string;
  onSearchChange?: (q: string) => void;
  searchPlaceholder?: string;
  filterChips?: FilterChip[];
  onExport?: (format: 'excel-all' | 'excel-filtered' | 'csv-filtered') => void;
  onImportTemplate?: () => void;
  onImportUpload?: () => void;
  exportLoading?: boolean;
  extraControls?: React.ReactNode;
  children?: React.ReactNode;
}

export function Toolbar({
  searchQuery = '',
  onSearchChange,
  searchPlaceholder = 'بحث بالاسم، الهوية، أو الكلمة المفتاحية...',
  filterChips = [],
  onExport,
  onImportTemplate,
  onImportUpload,
  exportLoading = false,
  extraControls,
  children,
}: ToolbarProps) {
  const [openMenu, setOpenMenu] = useState<'export' | 'import' | null>(null);
  const exportRef = useRef<HTMLDivElement>(null);
  const importRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      const target = e.target as Node;
      if (openMenu === 'export' && exportRef.current && !exportRef.current.contains(target)) {
        setOpenMenu(null);
      }
      if (openMenu === 'import' && importRef.current && !importRef.current.contains(target)) {
        setOpenMenu(null);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [openMenu]);

  return (
    <div className="flex flex-col gap-3 py-2 text-[var(--ink)]">
      <div className="flex flex-wrap items-center justify-between gap-2.5">
        {/* Search input */}
        {onSearchChange && (
          <div className="relative flex-1 min-w-[240px] max-w-md">
            <span className="absolute inset-y-0 right-0 flex items-center pr-3 pointer-events-none text-[var(--muted)]">
              <Search size={16} />
            </span>
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => onSearchChange(e.target.value)}
              placeholder={searchPlaceholder}
              className="w-full pr-9 pl-8 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-hidden transition"
            />
            {searchQuery && (
              <button
                type="button"
                onClick={() => onSearchChange('')}
                className="absolute inset-y-0 left-0 flex items-center pl-2.5 text-[var(--muted)] hover:text-[var(--ink)]"
                aria-label="مسح البحث"
              >
                <X size={14} />
              </button>
            )}
          </div>
        )}

        {/* Extra controls slot (e.g. company, period picker) */}
        {extraControls && <div className="flex items-center gap-2 flex-wrap">{extraControls}</div>}

        {/* Export / Import Actions */}
        <div className="flex items-center gap-2 mr-auto">
          {children}

          {/* Export Dropdown */}
          {onExport && (
            <div className="relative" ref={exportRef}>
              <button
                type="button"
                disabled={exportLoading}
                onClick={() => setOpenMenu((prev) => (prev === 'export' ? null : 'export'))}
                aria-expanded={openMenu === 'export'}
                aria-label="تصدير البيانات"
                className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-[var(--ink)] transition disabled:opacity-50"
              >
                <Download size={15} className="text-[var(--accent)]" />
                <span>تصدير</span>
                <ChevronDown size={14} className="text-[var(--muted)]" />
              </button>

              {openMenu === 'export' && (
                <div className="absolute left-0 mt-1 w-44 rounded-xl border border-[var(--line)] bg-[var(--surface)] shadow-lg py-1.5 z-30">
                  <button
                    type="button"
                    onClick={() => {
                      setOpenMenu(null);
                      onExport('excel-filtered');
                    }}
                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-right hover:bg-[var(--row-hover)] font-medium"
                  >
                    <FileSpreadsheet size={15} className="text-[var(--money-pos)]" />
                    <span>Excel (المفلتر حاليًا)</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setOpenMenu(null);
                      onExport('excel-all');
                    }}
                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-right hover:bg-[var(--row-hover)] font-medium"
                  >
                    <FileSpreadsheet size={15} className="text-[var(--accent)]" />
                    <span>Excel (كل البيانات)</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setOpenMenu(null);
                      onExport('csv-filtered');
                    }}
                    className="flex w-full items-center gap-2 px-3 py-2 text-xs text-right hover:bg-[var(--row-hover)] font-medium border-t border-[var(--line)]"
                  >
                    <FileText size={15} className="text-[var(--muted)]" />
                    <span>CSV (المفلتر)</span>
                  </button>
                </div>
              )}
            </div>
          )}

          {/* Import Dropdown */}
          {(onImportTemplate || onImportUpload) && (
            <div className="relative" ref={importRef}>
              <button
                type="button"
                onClick={() => setOpenMenu((prev) => (prev === 'import' ? null : 'import'))}
                aria-expanded={openMenu === 'import'}
                aria-label="استيراد بيانات"
                className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-[var(--ink)] transition"
              >
                <Upload size={15} className="text-[var(--accent)]" />
                <span>استيراد</span>
                <ChevronDown size={14} className="text-[var(--muted)]" />
              </button>

              {openMenu === 'import' && (
                <div className="absolute left-0 mt-1 w-44 rounded-xl border border-[var(--line)] bg-[var(--surface)] shadow-lg py-1.5 z-30">
                  {onImportUpload && (
                    <button
                      type="button"
                      onClick={() => {
                        setOpenMenu(null);
                        onImportUpload();
                      }}
                      className="flex w-full items-center gap-2 px-3 py-2 text-xs text-right hover:bg-[var(--row-hover)] font-medium"
                    >
                      <Upload size={15} className="text-[var(--accent)]" />
                      <span>رفع ملف جديد</span>
                    </button>
                  )}
                  {onImportTemplate && (
                    <button
                      type="button"
                      onClick={() => {
                        setOpenMenu(null);
                        onImportTemplate();
                      }}
                      className="flex w-full items-center gap-2 px-3 py-2 text-xs text-right hover:bg-[var(--row-hover)] font-medium border-t border-[var(--line)]"
                    >
                      <Download size={15} className="text-[var(--muted)]" />
                      <span>تحميل قالب Excel</span>
                    </button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Filter chips bar */}
      {filterChips.length > 0 && (
        <div className="flex items-center gap-1.5 flex-wrap pt-1">
          <span className="text-[11px] font-semibold text-[var(--muted)] ml-1 flex items-center gap-1">
            <Filter size={12} />
            تصفية:
          </span>
          {filterChips.map((chip) => (
            <button
              key={chip.id}
              type="button"
              onClick={chip.onToggle}
              className={`inline-flex items-center gap-1 px-2.5 py-1 text-xs rounded-full border transition font-medium ${
                chip.active
                  ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-semibold'
                  : 'border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] hover:border-[var(--muted)]'
              }`}
            >
              <span>{chip.label}</span>
              {typeof chip.count === 'number' && (
                <span className={`num text-[10px] px-1.5 py-0.2 rounded-full font-bold ${
                  chip.active ? 'bg-[var(--accent)] text-[var(--accent-ink)]' : 'bg-[var(--raised)] text-[var(--muted)]'
                }`}>
                  {chip.count}
                </span>
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
