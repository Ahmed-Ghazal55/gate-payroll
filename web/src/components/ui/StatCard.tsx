import React from 'react';
import { LucideIcon } from 'lucide-react';

export interface StatCardProps {
  label: string;
  value: number | string;
  hint?: string;
  icon?: LucideIcon;
  trend?: {
    value: string;
    isPositive?: boolean;
  };
  onClick?: () => void;
  className?: string;
  active?: boolean;
  loading?: boolean;
}

export function StatCard({
  label,
  value,
  hint,
  icon: Icon,
  trend,
  onClick,
  className = '',
  active = false,
  loading = false,
}: StatCardProps) {
  return (
    <div
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      aria-pressed={onClick ? active : undefined}
      onKeyDown={onClick ? (e) => (e.key === 'Enter' || e.key === ' ') && onClick() : undefined}
      className={`relative flex flex-col justify-between p-4 sm:p-5 rounded-xl border text-[var(--ink)] shadow-xs transition-all duration-150 ${
        active
          ? 'border-[var(--accent)] bg-[var(--row-selected)] ring-2 ring-[var(--accent)]/30'
          : 'border-[var(--line)] bg-[var(--surface)]'
      } ${
        onClick ? 'cursor-pointer hover:border-[var(--accent)] hover:shadow-sm' : ''
      } ${className}`}
    >
      <div className="flex items-start justify-between gap-3">
        <span className="text-xs sm:text-sm font-medium text-[var(--muted)]">{label}</span>
        {Icon && (
          <div
            className={`flex h-8 w-8 items-center justify-center rounded-lg shrink-0 transition-colors ${
              active
                ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                : 'bg-[var(--raised)] text-[var(--accent)]'
            }`}
          >
            <Icon size={18} aria-hidden="true" />
          </div>
        )}
      </div>

      <div className="mt-3 flex items-baseline gap-2">
        {loading ? (
          <div className="h-8 w-24 rounded-md bg-[var(--line)] animate-pulse my-0.5" />
        ) : (
          <strong className="num text-2xl sm:text-3xl font-bold tracking-tight text-[var(--ink)]">
            {value}
          </strong>
        )}
        {!loading && trend && (
          <span
            className={`text-xs font-semibold num ${
              trend.isPositive ? 'text-[var(--money-pos)]' : 'text-[var(--money-neg)]'
            }`}
          >
            {trend.value}
          </span>
        )}
      </div>

      {hint && (
        <div className="mt-2 text-[11px] sm:text-xs text-[var(--muted)] line-clamp-1">
          {hint}
        </div>
      )}
    </div>
  );
}
