'use client';

import React, { createContext, useContext, useState, useCallback, useId } from 'react';
import { CheckCircle2, AlertTriangle, AlertCircle, Info, X } from 'lucide-react';

export type ToastType = 'success' | 'warning' | 'danger' | 'info';

export interface ToastMessage {
  id: string;
  type: ToastType;
  title?: string;
  message: string;
  duration?: number;
}

interface ToastContextValue {
  toast: (message: string, type?: ToastType, title?: string, duration?: number) => void;
  removeToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback(
    (message: string, type: ToastType = 'info', title?: string, duration: number = 4000) => {
      const id = 'toast-' + Math.random().toString(36).substring(2, 9);
      setToasts((prev) => [...prev, { id, type, title, message, duration }]);

      if (duration > 0) {
        setTimeout(() => {
          removeToast(id);
        }, duration);
      }
    },
    [removeToast]
  );

  return (
    <ToastContext.Provider value={{ toast, removeToast }}>
      {children}
      {/* Toast container */}
      <div
        aria-live="polite"
        className="fixed bottom-4 left-4 z-50 flex flex-col gap-2 max-w-sm w-full pointer-events-none"
      >
        {toasts.map((t) => {
          const icons = {
            success: <CheckCircle2 size={18} className="text-[var(--money-pos)] shrink-0" />,
            warning: <AlertTriangle size={18} className="text-[var(--warning)] shrink-0" />,
            danger: <AlertCircle size={18} className="text-[var(--danger)] shrink-0" />,
            info: <Info size={18} className="text-[var(--info)] shrink-0" />,
          };

          const borderColors = {
            success: 'border-[var(--money-pos)]/40',
            warning: 'border-[var(--warning)]/40',
            danger: 'border-[var(--danger)]/40',
            info: 'border-[var(--info)]/40',
          };

          return (
            <div
              key={t.id}
              role="alert"
              className={`pointer-events-auto flex items-start gap-3 p-3.5 rounded-xl border bg-[var(--surface)] text-[var(--ink)] shadow-lg animate-in slide-in-from-bottom duration-150 ${
                borderColors[t.type]
              }`}
            >
              {icons[t.type]}
              <div className="flex-1 text-xs">
                {t.title && <div className="font-bold mb-0.5">{t.title}</div>}
                <div className="text-[var(--muted)] leading-relaxed">{t.message}</div>
              </div>
              <button
                type="button"
                onClick={() => removeToast(t.id)}
                aria-label="إغلاق التنبيه"
                className="text-[var(--muted)] hover:text-[var(--ink)] p-0.5"
              >
                <X size={14} />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    return {
      toast: (msg: string) => console.log('Toast:', msg),
      removeToast: () => {},
    };
  }
  return context;
}
