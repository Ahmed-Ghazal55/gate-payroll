'use client';

import React, { useState, useEffect, useRef } from 'react';
import Link from 'next/link';
import { Search, X, User, ChevronDown, Check, UserPlus, AlertCircle } from 'lucide-react';
import { api, type RiderLookupItem } from '../../lib/api';

export type RiderOption = RiderLookupItem;

export interface RiderPickerProps {
  value?: string; // Selected iqama
  onChange: (rider: RiderOption | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  autoFocus?: boolean;
  required?: boolean;
}

export function RiderPicker({
  value,
  onChange,
  placeholder = 'ابحث باسم المندوب أو رقم الإقامة...',
  disabled = false,
  className = '',
  autoFocus = false,
  required = false,
}: RiderPickerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<RiderOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [selectedRider, setSelectedRider] = useState<RiderOption | null>(null);
  const [activeIndex, setActiveIndex] = useState<number>(-1);
  const [hasSearched, setHasSearched] = useState(false);

  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  // Load initial selected rider details if value is provided
  useEffect(() => {
    if (!value) {
      setSelectedRider(null);
      return;
    }
    if (selectedRider && selectedRider.iqama === value) {
      return;
    }

    let isMounted = true;
    const fetchSelected = async () => {
      try {
        const lookup = await api.getRidersLookup(value);
        if (lookup && Array.isArray(lookup) && lookup.length > 0) {
          const match = lookup.find((r) => r.iqama === value || r.id === value);
          if (match && isMounted) {
            setSelectedRider(match);
            return;
          }
        }
        if (isMounted) {
          setSelectedRider({ id: value, iqama: value, name: `مندوب (${value})` });
        }
      } catch {
        if (isMounted) {
          setSelectedRider({ id: value, iqama: value, name: `مندوب (${value})` });
        }
      }
    };

    fetchSelected();
    return () => {
      isMounted = false;
    };
  }, [value]);

  // Search logic with 250ms debounce (server lookup only)
  useEffect(() => {
    if (!isOpen) return;

    const trimmed = query.trim();
    if (trimmed.length < 1) {
      setResults([]);
      setHasSearched(false);
      setLoading(false);
      setSearchError(null);
      return;
    }

    setLoading(true);
    setHasSearched(true);
    setSearchError(null);

    const timer = setTimeout(async () => {
      try {
        const res = await api.getRidersLookup(trimmed);
        const items = Array.isArray(res) ? res : [];
        setResults(items.slice(0, 20));
        setActiveIndex(items.length > 0 ? 0 : -1);
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'فشل البحث في سجلات المناديب';
        setSearchError(msg);
        setResults([]);
        setActiveIndex(-1);
      } finally {
        setLoading(false);
      }
    }, 250);

    return () => clearTimeout(timer);
  }, [query, isOpen]);

  // Close on outside click
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleOutsideClick);
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, []);

  // Keyboard navigation
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (!isOpen) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') {
        setIsOpen(true);
        e.preventDefault();
      }
      return;
    }

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (results.length > 0) {
        setActiveIndex((prev) => (prev + 1) % results.length);
      }
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (results.length > 0) {
        setActiveIndex((prev) => (prev - 1 + results.length) % results.length);
      }
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (results.length > 0 && activeIndex >= 0 && activeIndex < results.length) {
        handleSelect(results[activeIndex]);
      }
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setIsOpen(false);
    }
  };

  const handleSelect = (rider: RiderOption) => {
    setSelectedRider(rider);
    onChange(rider);
    setIsOpen(false);
    setQuery('');
  };

  const handleClear = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedRider(null);
    onChange(null);
    setQuery('');
    if (inputRef.current) inputRef.current.focus();
  };

  return (
    <div ref={containerRef} className={`relative w-full ${className}`}>
      {/* Trigger / Input Box */}
      <div
        onClick={() => {
          if (!disabled) {
            setIsOpen(true);
            setTimeout(() => inputRef.current?.focus(), 50);
          }
        }}
        className={`w-full min-h-[42px] px-3 py-2 rounded-xl border flex items-center justify-between gap-2 transition cursor-pointer text-xs sm:text-sm ${
          disabled
            ? 'opacity-50 cursor-not-allowed bg-[var(--raised)] border-[var(--line)]'
            : isOpen
            ? 'border-[var(--accent)] ring-2 ring-[var(--accent)]/20 bg-[var(--surface)]'
            : 'border-[var(--line)] bg-[var(--surface)] hover:border-[var(--muted)]'
        }`}
      >
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <Search size={15} className="text-[var(--muted)] shrink-0" />

          {selectedRider ? (
            <div className="flex items-center gap-2 flex-1 min-w-0 flex-wrap">
              <span className="font-bold text-[var(--ink)] truncate">
                {selectedRider.name}
              </span>
              {selectedRider.iqama && (
                <span className="font-mono text-xs text-[var(--accent)] bg-[var(--accent)]/10 px-2 py-0.5 rounded-md shrink-0">
                  {selectedRider.iqama}
                </span>
              )}

              {/* Status Chips as returned by server */}
              {selectedRider.chips && selectedRider.chips.length > 0 ? (
                selectedRider.chips.map((chip, i) => (
                  <span
                    key={i}
                    className="text-[10px] font-bold px-1.5 py-0.5 rounded-full shrink-0 bg-[var(--raised)] text-[var(--ink)] border border-[var(--line)]"
                  >
                    {chip}
                  </span>
                ))
              ) : selectedRider.status ? (
                <span className="text-[10px] font-bold px-1.5 py-0.5 rounded-full shrink-0 bg-[var(--raised)] text-[var(--muted)]">
                  {selectedRider.status}
                </span>
              ) : null}
            </div>
          ) : (
            <span className="text-[var(--muted)] truncate select-none">
              {placeholder}
            </span>
          )}
        </div>

        <div className="flex items-center gap-1 shrink-0">
          {selectedRider && !disabled && (
            <button
              type="button"
              onClick={handleClear}
              className="p-1 rounded-md text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
              title="إلغاء الاختيار"
            >
              <X size={14} />
            </button>
          )}
          <ChevronDown
            size={14}
            className={`text-[var(--muted)] transition-transform ${
              isOpen ? 'rotate-180 text-[var(--accent)]' : ''
            }`}
          />
        </div>
      </div>

      {/* Dropdown Menu */}
      {isOpen && (
        <div className="absolute z-50 top-full mt-1.5 w-full bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-xl overflow-hidden animate-in fade-in zoom-in-95 duration-100">
          {/* Search Input inside Dropdown */}
          <div className="p-2.5 border-b border-[var(--line)] bg-[var(--raised)]/40">
            <div className="relative">
              <Search
                size={14}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-[var(--muted)]"
              />
              <input
                ref={inputRef}
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder="ابحث بالاسم، الإقامة أو رقم الهاتف..."
                autoFocus={autoFocus}
                className="w-full pl-3 pr-9 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden focus:border-[var(--accent)]"
              />
              {query && (
                <button
                  type="button"
                  onClick={() => setQuery('')}
                  className="absolute left-3 top-1/2 -translate-y-1/2 text-[var(--muted)] hover:text-[var(--ink)]"
                >
                  <X size={13} />
                </button>
              )}
            </div>
          </div>

          {/* Results List */}
          <div className="max-h-64 overflow-y-auto p-1.5">
            {loading ? (
              <div className="p-4 text-center text-xs text-[var(--muted)] space-y-2">
                <div className="inline-block h-4 w-4 border-2 border-[var(--accent)] border-t-transparent rounded-full animate-spin" />
                <div>جاري البحث في سجلات المناديب...</div>
              </div>
            ) : searchError ? (
              <div className="p-4 text-center space-y-2 text-xs text-[var(--danger)]">
                <div className="flex items-center justify-center gap-1.5 font-bold">
                  <AlertCircle size={15} />
                  <span>خطأ في البحث</span>
                </div>
                <div>{searchError}</div>
              </div>
            ) : query.trim().length === 0 ? (
              <div className="p-4 text-center text-xs text-[var(--muted)]">
                اكتب حرفين أو رقماً على الأقل للبحث عن مندوب.
              </div>
            ) : results.length === 0 && hasSearched ? (
              <div className="p-4 text-center space-y-2.5">
                <div className="text-xs text-[var(--muted)]">
                  لا توجد نتائج مطابقة لـ «{query}»
                </div>
                <div className="pt-1">
                  <Link
                    href="/app/riders"
                    onClick={() => setIsOpen(false)}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-[var(--accent)]/10 text-[var(--accent)] font-bold text-xs hover:bg-[var(--accent)]/20 transition"
                  >
                    <UserPlus size={13} />
                    <span>غير موجود — أضف المندوب أولاً</span>
                  </Link>
                </div>
              </div>
            ) : (
              <ul ref={listRef} className="space-y-1">
                {results.map((rider, index) => {
                  const isSelected = selectedRider?.iqama === rider.iqama;
                  const isHighlighted = activeIndex === index;

                  return (
                    <li
                      key={rider.iqama || rider.id || index}
                      onClick={() => handleSelect(rider)}
                      onMouseEnter={() => setActiveIndex(index)}
                      className={`p-2.5 rounded-xl cursor-pointer transition flex items-center justify-between gap-3 text-xs sm:text-sm ${
                        isHighlighted
                          ? 'bg-[var(--accent)]/10 text-[var(--ink)]'
                          : isSelected
                          ? 'bg-[var(--raised)] text-[var(--ink)]'
                          : 'text-[var(--ink)] hover:bg-[var(--raised)]/60'
                      }`}
                    >
                      <div className="flex items-center gap-2.5 min-w-0 flex-1">
                        <div
                          className={`h-7 w-7 rounded-full flex items-center justify-center shrink-0 ${
                            isSelected
                              ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                              : 'bg-[var(--raised)] text-[var(--muted)]'
                          }`}
                        >
                          <User size={13} />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="font-bold truncate">
                            {rider.name || 'مندوب بدون اسم'}
                          </div>
                          <div className="font-mono text-[11px] text-[var(--muted)] mt-0.5">
                            {rider.iqama && <span>إقامة: {rider.iqama}</span>}
                            {rider.phone && (
                              <span className="mr-2">· هاتف: {rider.phone}</span>
                            )}
                          </div>
                        </div>
                      </div>

                      <div className="flex items-center gap-1.5 shrink-0 flex-wrap">
                        {/* Server Chips */}
                        {rider.chips && rider.chips.length > 0 ? (
                          rider.chips.map((chip, i) => (
                            <span
                              key={i}
                              className="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-[var(--raised)] text-[var(--ink)] border border-[var(--line)]"
                            >
                              {chip}
                            </span>
                          ))
                        ) : rider.status ? (
                          <span className="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-[var(--raised)] text-[var(--muted)]">
                            {rider.status}
                          </span>
                        ) : null}

                        {isSelected && (
                          <Check size={14} className="text-[var(--accent)] mr-1" />
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
