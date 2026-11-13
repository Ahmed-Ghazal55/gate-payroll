'use client';

import React from 'react';
import { Database, Server } from 'lucide-react';

export function formatRiyadhDateTime(iso: string | null | undefined): string {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return String(iso);
    return new Intl.DateTimeFormat('ar-SA-u-nu-latn', {
      timeZone: 'Asia/Riyadh',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: true,
    }).format(d);
  } catch {
    return String(iso);
  }
}

export interface SourceBadgeProps {
  source: string | null | undefined;
  asOf?: string | null | undefined;
  className?: string;
}

export function SourceBadge({ source, asOf, className = '' }: SourceBadgeProps) {
  if (!source) return null;

  if (source === 'erp_live') {
    const formatted = formatRiyadhDateTime(asOf);
    return (
      <span
        data-testid="source-badge-live"
        className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/15 text-emerald-800 dark:text-emerald-300 border border-emerald-500/30 shadow-2xs ${className}`}
        title={`مصدر القراءة: مباشر من نظام الموارد (آخر قراءة: ${formatted} بتوقيت الرياض)`}
      >
        <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse shrink-0" aria-hidden="true" />
        <Database size={13} className="shrink-0 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
        <span>مباشر من نظام الموارد — آخر قراءة {formatted}</span>
      </span>
    );
  }

  if (source === 'store') {
    return (
      <span
        data-testid="source-badge-store"
        className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-amber-500/15 text-amber-800 dark:text-amber-300 border border-amber-500/30 shadow-2xs ${className}`}
        title="مصدر القراءة: بيانات تجريبية محلية"
      >
        <span className="w-2 h-2 rounded-full bg-amber-500 shrink-0" aria-hidden="true" />
        <Server size={13} className="shrink-0 text-amber-600 dark:text-amber-400" aria-hidden="true" />
        <span>بيانات تجريبية محلية</span>
      </span>
    );
  }

  return (
    <span
      className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--muted)] border border-[var(--line)] ${className}`}
    >
      <span>{source}</span>
    </span>
  );
}
