import React from 'react';
import Link from 'next/link';
import { ExternalLink, ArrowRight, Layers, Clock } from 'lucide-react';

export interface UnderMigrationProps {
  sectionTitle: string;
  legacyHash: string;
  description?: string;
  features?: string[];
}

export function UnderMigration({
  sectionTitle,
  legacyHash,
  description = 'يجري تجهيز ونقل هذه الشاشة إلى بنية الإصدار 1.0 الجديدة مع تحسين سرعة الأداء وفلاتر الجداول المتقدمة.',
  features = [],
}: UnderMigrationProps) {
  return (
    <div className="max-w-3xl mx-auto py-10 px-4">
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] p-6 sm:p-8 shadow-xs">
        <div className="flex items-center gap-3 mb-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-[var(--raised)] text-[var(--accent)]">
            <Layers size={24} />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-[var(--warning)]/15 text-[var(--warning)]">
                <Clock size={12} />
                قيد النقل التدريجي
              </span>
            </div>
            <h2 className="text-xl sm:text-2xl font-bold mt-1 text-[var(--ink)]">
              {sectionTitle}
            </h2>
          </div>
        </div>

        <p className="text-sm text-[var(--muted)] leading-relaxed mb-6">
          {description}
        </p>

        {features.length > 0 && (
          <div className="mb-6 p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
            <h4 className="text-xs font-bold text-[var(--ink)] mb-2.5">
              الوظائف المتاحة في الواجهة الحالية:
            </h4>
            <ul className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs text-[var(--muted)]">
              {features.map((f, i) => (
                <li key={i} className="flex items-center gap-2">
                  <span className="h-1.5 w-1.5 rounded-full bg-[var(--accent)] shrink-0" />
                  <span>{f}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        <div className="flex flex-col sm:flex-row items-center gap-3 pt-2">
          <Link
            href="/"
            className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-5 py-2.5 text-sm font-bold text-[var(--accent-ink)] bg-[var(--accent)] rounded-xl hover:opacity-95 active:scale-95 transition shadow-xs"
          >
            <span>العودة للرئيسية</span>
          </Link>
        </div>
      </div>
    </div>
  );
}
