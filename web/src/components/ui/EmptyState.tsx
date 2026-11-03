import React from 'react';
import { Inbox, AlertCircle, RotateCcw } from 'lucide-react';

export interface EmptyStateProps {
  title?: string;
  description?: string;
  icon?: React.ReactNode;
  action?: {
    label: string;
    onClick: () => void;
  };
}

export function EmptyState({
  title = 'لا توجد بيانات متاحة',
  description = 'لم يتم العثور على أي سجلات تطابق الفلاتر المحددة حاليًا.',
  icon,
  action,
}: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-12 px-4 text-center rounded-xl border border-dashed border-[var(--line)] bg-[var(--surface)] my-4">
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-[var(--raised)] text-[var(--muted)] mb-3">
        {icon || <Inbox size={24} />}
      </div>
      <h3 className="text-sm sm:text-base font-bold text-[var(--ink)] mb-1">
        {title}
      </h3>
      <p className="text-xs sm:text-sm text-[var(--muted)] max-w-sm mb-4 leading-relaxed">
        {description}
      </p>
      {action && (
        <button
          type="button"
          onClick={action.onClick}
          className="inline-flex items-center gap-2 px-3.5 py-1.5 text-xs font-semibold text-[var(--accent-ink)] bg-[var(--accent)] rounded-lg hover:opacity-90 active:scale-95 transition"
        >
          {action.label}
        </button>
      )}
    </div>
  );
}

export interface ErrorStateProps {
  title?: string;
  message?: string;
  code?: string;
  onRetry?: () => void;
}

export function ErrorState({
  title = 'تعذر تحميل البيانات',
  message = 'حدث خطأ أثناء الاتصال بالخادم. يرجى إعادة المحاولة.',
  code,
  onRetry,
}: ErrorStateProps) {
  return (
    <div
      role="alert"
      className="flex flex-col items-center justify-center py-10 px-4 text-center rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/5 my-4"
    >
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-[var(--danger)]/15 text-[var(--danger)] mb-3">
        <AlertCircle size={26} />
      </div>
      <h3 className="text-sm sm:text-base font-bold text-[var(--danger)] mb-1">
        {title}
      </h3>
      <p className="text-xs sm:text-sm text-[var(--muted)] max-w-md mb-2 leading-relaxed">
        {message}
      </p>
      {code && (
        <span className="num text-[11px] font-mono text-[var(--danger)] mb-4 px-2 py-0.5 rounded-md bg-[var(--raised)]">
          رمز الخطأ: {code}
        </span>
      )}
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="inline-flex items-center gap-2 px-4 py-2 text-xs sm:text-sm font-semibold text-[var(--accent-ink)] bg-[var(--accent)] rounded-lg hover:opacity-90 active:scale-95 transition mt-2 shadow-xs"
        >
          <RotateCcw size={15} />
          <span>إعادة المحاولة</span>
        </button>
      )}
    </div>
  );
}

export { Skeleton } from './Skeleton';
export type { SkeletonProps } from './Skeleton';
