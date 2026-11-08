'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronDown } from 'lucide-react';

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
  id?: string;
  ariaLabel?: string;
  className?: string;
  onFocus?: () => void;
}

const normalize = (text: string) =>
  text
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .replace(/[ؤ]/g, 'و')
    .replace(/[ئ]/g, 'ي')
    .replace(/[ً-ْـ]/g, '')
    .replace(/[٠-٩]/g, (d) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)))
    .toLowerCase()
    .trim();

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
  id,
  ariaLabel,
  className = '',
  onFocus,
}: SearchableSelectProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);

  const selected = options.find((o) => o.value === value);
  const matches = useMemo(() => {
    const words = normalize(query).split(/\s+/).filter(Boolean);
    return options.filter((o) => {
      const label = normalize(o.label);
      return words.every((w) => label.includes(w));
    });
  }, [options, query]);
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

  const choose = (option: SearchOption) => {
    onChange(option.value);
    setOpen(false);
    setQuery('');
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
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

  return (
    <div className={`relative ${className}`}>
      <input
        ref={inputRef}
        id={id}
        type="text"
        role="combobox"
        aria-expanded={open}
        aria-label={ariaLabel}
        autoComplete="off"
        disabled={disabled}
        required={required && !value}
        value={open ? query : (selected?.label ?? '')}
        placeholder={selected?.label ?? placeholder}
        onFocus={() => {
          onFocus?.();
          setOpen(true);
          setActive(0);
        }}
        onBlur={() => {
          setOpen(false);
          setQuery('');
        }}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
          setActive(0);
        }}
        onKeyDown={onKeyDown}
        className="w-full ps-2.5 pe-7 py-2 text-xs rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden focus:border-[var(--accent)] disabled:opacity-60"
      />
      <ChevronDown size={14} className="absolute end-2 top-2.5 text-[var(--muted)] pointer-events-none" />
      {open && rect && typeof document !== 'undefined' &&
        createPortal(
          <ul
            role="listbox"
            onMouseDown={(e) => e.preventDefault()}
            style={{ position: 'fixed', top: rect.bottom + 4, left: rect.left, width: Math.max(rect.width, 240), zIndex: 1000 }}
            className="max-h-60 overflow-y-auto rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-lg text-xs"
            dir="rtl"
          >
            {shown.length === 0 && <li className="p-2.5 text-[var(--muted)]">{emptyText}</li>}
            {shown.map((option, i) => (
              <li
                key={option.value}
                role="option"
                aria-selected={option.value === value}
                onClick={() => choose(option)}
                onMouseEnter={() => setActive(i)}
                className={`p-2.5 cursor-pointer ${i === active ? 'bg-[var(--accent)]/15' : ''} ${option.value === value ? 'font-bold' : ''}`}
              >
                {option.label}
              </li>
            ))}
            {matches.length > MAX_SHOWN && (
              <li className="p-2 text-[var(--muted)]">اكتب أكثر لتضييق {matches.length} نتيجة</li>
            )}
          </ul>,
          document.body,
        )}
    </div>
  );
}
