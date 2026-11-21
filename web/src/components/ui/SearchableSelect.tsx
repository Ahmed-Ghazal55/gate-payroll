'use client';

import React, { useEffect, useId, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown, X } from 'lucide-react';
import { filterSelectOptions } from '../../lib/columnFilter';

export interface SearchOption {
  value: string;
  label: string;
}

interface SearchableSelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SearchOption[];
  placeholder?: string;
  emptyText?: string;
  disabled?: boolean;
  required?: boolean;
  clearable?: boolean;
  loading?: boolean;
  size?: 'sm' | 'md';
  id?: string;
  ariaLabel?: string;
  className?: string;
  onFocus?: () => void;
  onOpen?: () => void;
}

const MAX_SHOWN = 100;

/** Dropdown you can type in: the text narrows the list; only a listed option can be chosen. */
export function SearchableSelect({
  value,
  onChange,
  options,
  placeholder = 'اكتب للبحث أو اختر...',
  emptyText = 'لا توجد نتائج مطابقة',
  disabled = false,
  required = false,
  clearable = false,
  loading = false,
  size = 'md',
  id,
  ariaLabel,
  className = '',
  onFocus,
  onOpen,
}: SearchableSelectProps) {
  const autoId = useId();
  const listboxId = `${id || autoId}-listbox`;
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);

  const selected = options.find((o) => o.value === value);
  const displayLabel = selected?.label ?? value;
  const matches = useMemo(() => filterSelectOptions(options, query), [options, query]);
  const shown = matches.slice(0, MAX_SHOWN);

  useEffect(() => {
    if (!open) return;
    const place = () => setRect(inputRef.current?.getBoundingClientRect() ?? null);
    place();
    window.addEventListener('scroll', place, true);
    window.addEventListener('resize', place);
    return () => {
      window.removeEventListener('scroll', place, true);
      window.removeEventListener('resize', place);
    };
  }, [open]);

  const openList = () => {
    if (disabled) return;
    if (!open) onOpen?.();
    setOpen(true);
    setActive(0);
  };

  const choose = (option: SearchOption) => {
    onChange(option.value);
    setOpen(false);
    setQuery('');
  };

  const handleClear = (e: React.MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    onChange('');
    setQuery('');
    setOpen(false);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!open) onOpen?.();
      setOpen(true);
      setActive((i) => {
        const next = i + (e.key === 'ArrowDown' ? 1 : -1);
        return shown.length === 0 ? 0 : (next + shown.length) % shown.length;
      });
    } else if (e.key === 'Enter' && open) {
      e.preventDefault();
      if (shown[active]) choose(shown[active]);
    } else if (e.key === 'Escape') {
      setOpen(false);
      setQuery('');
    }
  };

  const isSm = size === 'sm';
  const showClear = clearable && !!value && !disabled;

  const portalStyle = useMemo<React.CSSProperties | undefined>(() => {
    if (!rect || typeof window === 'undefined') return undefined;
    const width = Math.max(rect.width, isSm ? 190 : 240);
    const maxLeft = Math.max(8, window.innerWidth - width - 8);
    const left = Math.max(8, Math.min(maxLeft, rect.left));
    const openUpward = rect.bottom + 250 > window.innerHeight && rect.top > 220;
    return {
      position: 'fixed',
      top: openUpward ? rect.top - 4 : rect.bottom + 4,
      left,
      width,
      transform: openUpward ? 'translateY(-100%)' : undefined,
      zIndex: 1000,
    };
  }, [rect, isSm]);

  return (
    <div className={`relative ${className}`}>
      <input
        ref={inputRef}
        id={id}
        type="text"
        role="combobox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        aria-autocomplete="list"
        aria-label={ariaLabel}
        autoComplete="off"
        disabled={disabled}
        required={required && !value}
        value={open ? query : displayLabel}
        placeholder={displayLabel || placeholder}
        onFocus={() => {
          onFocus?.();
          openList();
        }}
        onClick={() => {
          openList();
        }}
        onBlur={() => {
          setOpen(false);
          setQuery('');
        }}
        onChange={(e) => {
          setQuery(e.target.value);
          if (!open) onOpen?.();
          setOpen(true);
          setActive(0);
        }}
        onKeyDown={onKeyDown}
        className={
          isSm
            ? `w-full ps-2 ${showClear ? 'pe-10' : 'pe-6'} py-1 text-[11px] rounded border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden focus:border-[var(--accent)] disabled:opacity-60`
            : `w-full ps-2.5 ${showClear ? 'pe-12' : 'pe-7'} py-2 text-xs rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden focus:border-[var(--accent)] disabled:opacity-60`
        }
      />
      {showClear && (
        <button
          type="button"
          tabIndex={-1}
          aria-label="مسح التصفية"
          onMouseDown={(e) => e.preventDefault()}
          onClick={handleClear}
          className={`absolute ${isSm ? 'end-5 top-1' : 'end-6 top-2'} p-0.5 text-[var(--muted)] hover:text-[var(--ink)]`}
        >
          <X size={isSm ? 11 : 13} />
        </button>
      )}
      <ChevronDown
        size={isSm ? 12 : 14}
        className={`absolute ${isSm ? 'end-1.5 top-1.5' : 'end-2 top-2.5'} text-[var(--muted)] pointer-events-none`}
      />
      {open &&
        portalStyle &&
        typeof document !== 'undefined' &&
        createPortal(
          <ul
            id={listboxId}
            role="listbox"
            onMouseDown={(e) => e.preventDefault()}
            style={portalStyle}
            className="max-h-60 overflow-y-auto rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-lg text-xs"
            dir="rtl"
          >
            {loading && <li className="p-2.5 text-[var(--muted)]">جاري تحميل الخيارات...</li>}
            {!loading && shown.length === 0 && (
              <li className="p-2.5 text-[var(--muted)]">{emptyText}</li>
            )}
            {!loading &&
              shown.map((option, i) => (
                <li
                  key={option.value}
                  role="option"
                  aria-selected={option.value === value}
                  onClick={() => choose(option)}
                  onMouseEnter={() => setActive(i)}
                  className={`p-2 cursor-pointer ${i === active ? 'bg-[var(--accent)]/15' : ''} ${
                    option.value === value ? 'font-bold' : ''
                  }`}
                >
                  {option.label}
                </li>
              ))}
            {!loading && matches.length > MAX_SHOWN && (
              <li className="p-2 text-[var(--muted)]">اكتب أكثر لتضييق {matches.length} نتيجة</li>
            )}
          </ul>,
          document.body
        )}
    </div>
  );
}
