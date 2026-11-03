'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { Search, X, User, Building2, Receipt, ClipboardList, Loader2, ExternalLink } from 'lucide-react';
import { api } from '../../lib/api';
import type { SearchResult } from '../../lib/types';
import { inApp } from '@/lib/paths';

export interface GlobalSearchModalProps {
  open: boolean;
  onClose: () => void;
}

export function GlobalSearchModal({ open, onClose }: GlobalSearchModalProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const router = useRouter();

  useEffect(() => {
    if (open) {
      setTimeout(() => inputRef.current?.focus(), 50);
      setQuery('');
      setResults([]);
      setSelectedIndex(0);
    }
  }, [open]);

  useEffect(() => {
    if (!query.trim()) {
      setResults([]);
      setLoading(false);
      return;
    }

    const timer = setTimeout(async () => {
      setLoading(true);
      try {
        const data = await api.search(query.trim());
        setResults(data);
        setSelectedIndex(0);
      } catch (err) {
        console.error('Search failed:', err);
      } finally {
        setLoading(false);
      }
    }, 200);

    return () => clearTimeout(timer);
  }, [query]);

  // Keyboard navigation
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      onClose();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      setSelectedIndex((prev) => (results.length > 0 ? (prev + 1) % results.length : 0));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setSelectedIndex((prev) => (results.length > 0 ? (prev - 1 + results.length) % results.length : 0));
    } else if (e.key === 'Enter' && results[selectedIndex]) {
      e.preventDefault();
      handleSelect(results[selectedIndex]);
    }
  };

  const handleSelect = (item: SearchResult) => {
    onClose();
    if (item.url.startsWith('/app/')) {
      router.push(inApp(item.url));
    } else {
      window.location.href = item.url;
    }
  };

  if (!open) return null;

  const kindIcons = {
    rider: <User size={16} className="text-[var(--accent)]" />,
    company: <Building2 size={16} className="text-[var(--info)]" />,
    debt: <Receipt size={16} className="text-[var(--warning)]" />,
    campaign: <ClipboardList size={16} className="text-[var(--money-pos)]" />,
  };

  const kindLabels = {
    rider: 'مندوب',
    company: 'شركة / كفيل',
    debt: 'مديونية',
    campaign: 'حملة نماذج',
  };

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-16 sm:pt-24 p-4 bg-black/60 backdrop-blur-xs">
      <div className="absolute inset-0" onClick={onClose} aria-hidden="true" />

      <div
        role="dialog"
        aria-modal="true"
        aria-label="البحث الشامل"
        className="relative w-full max-w-2xl rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] shadow-2xl overflow-hidden focus:outline-hidden animate-in fade-in zoom-in-95 duration-150"
      >
        {/* Search Input bar */}
        <div className="flex items-center gap-3 px-4 py-3.5 border-b border-[var(--line)]">
          <Search size={20} className="text-[var(--muted)] shrink-0" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="ابحث برقم الهوية، الاسم، الشركة، أو المعاملة..."
            className="flex-1 bg-transparent text-sm sm:text-base text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
          />
          {loading && <Loader2 size={18} className="animate-spin text-[var(--accent)] shrink-0" />}
          {query && !loading && (
            <button
              type="button"
              onClick={() => setQuery('')}
              className="p-1 rounded-md text-[var(--muted)] hover:text-[var(--ink)]"
            >
              <X size={16} />
            </button>
          )}
          <span className="hidden sm:inline-block px-1.5 py-0.5 rounded text-[11px] font-mono border border-[var(--line)] bg-[var(--raised)] text-[var(--muted)]">
            ESC
          </span>
        </div>

        {/* Results List */}
        <div className="max-h-96 overflow-y-auto p-2 no-scrollbar">
          {query.trim() === '' ? (
            <div className="p-8 text-center text-xs text-[var(--muted)]">
              ابدأ بكتابة اسم المندوب أو رقم الإقامة أو رمز الشركة للبحث الفوري...
            </div>
          ) : results.length === 0 && !loading ? (
            <div className="p-8 text-center text-xs text-[var(--muted)]">
              لم يتم العثور على أي نتائج تطابق «{query}»
            </div>
          ) : (
            <div className="space-y-1">
              {results.map((res, idx) => {
                const isSelected = idx === selectedIndex;
                return (
                  <div
                    key={res.id}
                    onClick={() => handleSelect(res)}
                    onMouseEnter={() => setSelectedIndex(idx)}
                    className={`flex items-center justify-between p-3 rounded-xl cursor-pointer transition ${
                      isSelected
                        ? 'bg-[var(--row-selected)] text-[var(--ink)]'
                        : 'hover:bg-[var(--row-hover)]'
                    }`}
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[var(--raised)] shrink-0">
                        {kindIcons[res.kind] || <Search size={16} />}
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-bold truncate">{res.title}</span>
                          <span className="text-[10px] font-semibold px-1.5 py-0.5 rounded-full bg-[var(--raised)] text-[var(--muted)]">
                            {kindLabels[res.kind] || res.kind}
                          </span>
                        </div>
                        <p className="text-xs text-[var(--muted)] truncate">{res.subtitle}</p>
                      </div>
                    </div>

                    <div className="flex items-center gap-1 text-[var(--muted)] shrink-0">
                      <span className="text-xs">فتح</span>
                      <ExternalLink size={14} />
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* Footer shortcuts */}
        <div className="flex items-center justify-between px-4 py-2.5 border-t border-[var(--line)] bg-[var(--surface)] text-[11px] text-[var(--muted)]">
          <div className="flex items-center gap-3">
            <span>
              <kbd className="px-1.5 py-0.5 rounded bg-[var(--raised)] font-mono text-[10px]">↑↓</kbd>{' '}
              للتنقل
            </span>
            <span>
              <kbd className="px-1.5 py-0.5 rounded bg-[var(--raised)] font-mono text-[10px]">↵</kbd>{' '}
              للاختيار
            </span>
          </div>
          <span>بحث فوري عبر قاعدة البيانات</span>
        </div>
      </div>
    </div>
  );
}
