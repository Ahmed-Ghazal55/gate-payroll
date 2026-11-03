'use client';

import React, { useEffect, useRef } from 'react';
import { X } from 'lucide-react';

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  children: React.ReactNode;
  footer?: React.ReactNode;
  width?: string;
}

export function Drawer({
  open,
  onClose,
  title,
  subtitle,
  children,
  footer,
  width = 'max-w-md sm:max-w-xl',
}: DrawerProps) {
  const drawerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = '';
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-hidden bg-black/50 backdrop-blur-xs flex justify-end">
      {/* Backdrop */}
      <div className="absolute inset-0" onClick={onClose} aria-hidden="true" />

      {/* Slide-over Panel */}
      <div
        ref={drawerRef}
        role="dialog"
        aria-modal="true"
        className={`relative w-full ${width} bg-[var(--surface)] text-[var(--ink)] shadow-2xl flex flex-col h-full border-s border-[var(--line)] z-10 animate-in slide-in-from-right duration-200`}
      >
        {/* Drawer Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-[var(--line)] bg-[var(--surface)] shrink-0">
          <div>
            <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">
              {title}
            </h2>
            {subtitle && (
              <p className="text-xs text-[var(--muted)] mt-0.5">{subtitle}</p>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="إغلاق اللوحة"
            className="p-1.5 rounded-lg text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
          >
            <X size={18} />
          </button>
        </div>

        {/* Drawer Content */}
        <div className="flex-1 overflow-y-auto p-5 space-y-4">
          {children}
        </div>

        {/* Drawer Footer */}
        {footer && (
          <div className="p-4 border-t border-[var(--line)] bg-[var(--surface)] shrink-0">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
