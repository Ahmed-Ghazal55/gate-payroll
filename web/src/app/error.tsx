'use client';

import { useEffect } from 'react';

// Any render error inside a page lands here instead of the browser's blank "page couldn't load".
export default function PageError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return (
    <div dir="rtl" className="max-w-lg mx-auto my-16 p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-center space-y-3">
      <h2 className="text-lg font-bold text-[var(--ink)]">تعذر عرض هذه الصفحة</h2>
      <p className="text-sm text-[var(--muted)]">
        حدث خطأ أثناء عرض البيانات. بياناتك محفوظة ولم يتغير شيء. أعد المحاولة، وإذا تكرر الخطأ أرسل الرمز أدناه للدعم.
      </p>
      {error.digest && <p className="num text-xs text-[var(--muted)]">الرمز: {error.digest}</p>}
      <div className="flex justify-center gap-2 pt-2">
        <button
          type="button"
          onClick={reset}
          className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-sm font-bold cursor-pointer"
        >
          إعادة المحاولة
        </button>
        <a href="/app/" className="px-4 py-2 rounded-xl border border-[var(--line)] text-sm font-semibold text-[var(--ink)]">
          الرئيسية
        </a>
      </div>
    </div>
  );
}
