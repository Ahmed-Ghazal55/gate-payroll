import React, { useState, useRef, useEffect } from 'react';
import { MoreVertical } from 'lucide-react';

export interface PageHeaderAction {
  label: string;
  onClick: () => void;
  icon?: React.ReactNode;
  variant?: 'primary' | 'secondary' | 'danger';
  disabled?: boolean;
}

export interface PageHeaderProps {
  title: string;
  description?: string;
  eyebrow?: string;
  primaryAction?: {
    label: string;
    onClick: () => void;
    icon?: React.ReactNode;
    disabled?: boolean;
  };
  moreActions?: Array<{
    label: string;
    onClick: () => void;
    icon?: React.ReactNode;
    danger?: boolean;
    disabled?: boolean;
  }>;
  children?: React.ReactNode;
}

export function PageHeader({
  title,
  description,
  eyebrow,
  primaryAction,
  moreActions,
  children,
}: PageHeaderProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    function handleClickOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    }
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setMenuOpen(false);
    }
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [menuOpen]);

  return (
    <div className="flex flex-col gap-3 pb-4 mb-4 border-b border-[var(--line)] sm:flex-row sm:items-center sm:justify-between">
      <div className="space-y-1">
        {eyebrow && (
          <div className="text-xs font-semibold text-[var(--accent)] tracking-wide">
            {eyebrow}
          </div>
        )}
        <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--ink)]">
          {title}
        </h1>
        {description && (
          <p className="text-xs sm:text-sm text-[var(--muted)] leading-relaxed max-w-2xl">
            {description}
          </p>
        )}
      </div>

      <div className="flex items-center gap-2 flex-wrap">
        {children}

        {primaryAction && (
          <button
            type="button"
            disabled={primaryAction.disabled}
            onClick={primaryAction.onClick}
            className="inline-flex items-center justify-center gap-2 px-4 py-2 text-sm font-semibold text-[var(--accent-ink)] bg-[var(--accent)] rounded-lg hover:opacity-90 active:scale-[0.98] transition disabled:opacity-50 disabled:pointer-events-none shadow-xs"
          >
            {primaryAction.icon}
            <span>{primaryAction.label}</span>
          </button>
        )}

        {moreActions && moreActions.length > 0 && (
          <div className="relative" ref={menuRef}>
            <button
              type="button"
              aria-label="المزيد من الإجراءات"
              aria-haspopup="true"
              aria-expanded={menuOpen}
              onClick={() => setMenuOpen(!menuOpen)}
              className="inline-flex items-center justify-center p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] active:scale-95 transition"
            >
              <MoreVertical size={18} />
            </button>

            {menuOpen && (
              <div className="absolute end-0 mt-1 w-48 rounded-xl border border-[var(--line)] bg-[var(--surface)] shadow-lg py-1.5 z-30 focus:outline-hidden">
                <div className="px-3 py-1 text-[11px] font-semibold text-[var(--muted)] border-b border-[var(--line)] mb-1">
                  المزيد من الخيارات
                </div>
                {moreActions.map((action, idx) => (
                  <button
                    key={idx}
                    type="button"
                    disabled={action.disabled}
                    onClick={() => {
                      setMenuOpen(false);
                      action.onClick();
                    }}
                    className={`flex w-full items-center gap-2.5 px-3 py-2 text-xs font-medium text-start transition disabled:opacity-40 ${
                      action.danger
                        ? 'text-[var(--danger)] hover:bg-[var(--row-hover)]'
                        : 'text-[var(--ink)] hover:bg-[var(--row-hover)]'
                    }`}
                  >
                    {action.icon}
                    <span>{action.label}</span>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
