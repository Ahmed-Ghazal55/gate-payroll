'use client';

import React, { useState, useEffect, Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Lock, User, AlertCircle, ShieldAlert, Clock, Sparkles, Check } from 'lucide-react';
import { api, ApiError } from '../../lib/api';

function sanitizeReturnUrl(raw: string | null): string {
  if (!raw) return '/app/';
  let decoded = raw;
  try {
    decoded = decodeURIComponent(raw);
  } catch (_) {}

  // Must be same origin: strictly starts with /app/ or /legacy/
  // Disallow protocol-relative // or backslashes
  if (
    (decoded.startsWith('/app/') || decoded.startsWith('/legacy/')) &&
    !decoded.startsWith('//') &&
    !decoded.includes('\\')
  ) {
    return decoded;
  }
  return '/app/';
}

function LoginForm() {
  const searchParams = useSearchParams();
  const rawReturnUrl = searchParams.get('returnUrl');

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [needsSetup, setNeedsSetup] = useState(false);
  const [error, setError] = useState<{
    message: string;
    code?: string;
    status?: number;
  } | null>(null);

  // Check if system requires initial setup
  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        const status = await api.getAuthStatus();
        if (mounted && status.needsSetup) {
          setNeedsSetup(true);
        }
      } catch {
        // silent initial status check
      }
    })();
    return () => {
      mounted = false;
    };
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (loading) return;

    setError(null);
    setLoading(true);

    try {
      await api.login({ username: username.trim(), password });
      const target = sanitizeReturnUrl(rawReturnUrl);
      window.location.href = target;
    } catch (err: any) {
      const status = err.status || 500;
      const code = err.code || (status === 423 ? 'account_locked' : status === 429 ? 'rate_limited' : 'unauthorized');
      const message =
        err.message ||
        (status === 423
          ? 'الحساب مقفل مؤقتًا بعد محاولات دخول خاطئة؛ حاول بعد 15 دقيقة أو راجع مسؤول النظام.'
          : status === 429
          ? 'تجاوزت الحد المسموح من المحاولات؛ يرجى الانتظار دقيقة والمحاولة مجدداً.'
          : 'اسم المستخدم أو كلمة المرور غير صحيحة.');

      setError({ message, code, status });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="w-full bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-lg p-6 sm:p-8 space-y-6">
      {/* Brand & Header */}
      <div className="text-center space-y-2">
        <div className="inline-flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] font-black text-2xl shadow-md mb-1">
          ب
        </div>
        <h1 className="text-2xl font-bold text-[var(--ink)]">تسجيل الدخول</h1>
        <p className="text-xs text-[var(--muted)]">
          مساحة إدارة الرواتب والحسابات ومطابقة العمليات التشغيلية
        </p>
      </div>

      {/* Setup notification banner if fresh instance */}
      {needsSetup && (
        <div className="p-3.5 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/30 text-xs text-[var(--ink)] flex items-start gap-2.5">
          <Sparkles size={16} className="text-[var(--accent)] shrink-0 mt-0.5" />
          <div className="space-y-1">
            <span className="font-bold text-[var(--accent)]">المساحة جديدة وغير مهيأة</span>
            <p className="text-[var(--muted)]">
              لم يتم إنشاء حساب المسؤول الأول بعد.{' '}
              <Link href="/setup/" className="font-bold text-[var(--accent)] underline hover:opacity-80">
                انتقل إلى شاشة التهيئة الأولى
              </Link>
            </p>
          </div>
        </div>
      )}

      {/* Error / Lockout / Rate limit alerts */}
      {error && (
        <div
          id="auth-error-alert"
          role="alert"
          aria-live="polite"
          className={`p-4 rounded-xl border text-xs space-y-1.5 ${
            error.status === 423 || error.code === 'account_locked'
              ? 'bg-[var(--danger)]/10 border-[var(--danger)]/30 text-[var(--danger)]'
              : error.status === 429 || error.code === 'rate_limited'
              ? 'bg-[var(--warning)]/15 border-[var(--warning)]/30 text-[var(--warning)]'
              : 'bg-[var(--danger)]/10 border-[var(--danger)]/30 text-[var(--danger)]'
          }`}
        >
          <div className="flex items-center justify-between font-bold">
            <div className="flex items-center gap-1.5">
              {error.status === 423 || error.code === 'account_locked' ? (
                <ShieldAlert size={16} className="shrink-0" />
              ) : error.status === 429 || error.code === 'rate_limited' ? (
                <Clock size={16} className="shrink-0" />
              ) : (
                <AlertCircle size={16} className="shrink-0" />
              )}
              <span>
                {error.status === 423 || error.code === 'account_locked'
                  ? 'الحساب مقفل مؤقتاً'
                  : error.status === 429 || error.code === 'rate_limited'
                  ? 'تجاوز حد الطلبات'
                  : 'تعذر تسجيل الدخول'}
              </span>
            </div>
            {error.code && (
              <span className="font-mono text-[10px] px-1.5 py-0.5 rounded bg-[var(--surface)]/80 border border-current">
                {error.code}
              </span>
            )}
          </div>
          <p className="text-[11px] leading-relaxed opacity-95">{error.message}</p>
        </div>
      )}

      {/* Login Form */}
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label
            htmlFor="username"
            className="block text-xs font-semibold text-[var(--ink)] mb-1.5"
          >
            اسم المستخدم
          </label>
          <div className="relative">
            <input
              id="username"
              name="username"
              type="text"
              required
              minLength={3}
              autoComplete="username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="مثال: admin"
              className="w-full h-11 px-3 pe-10 text-sm rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)]/60 focus:outline-none focus:ring-2 focus:ring-[var(--focus)] focus:border-transparent transition"
            />
            <User
              size={17}
              className="absolute end-3 top-1/2 -translate-y-1/2 text-[var(--muted)] pointer-events-none"
            />
          </div>
        </div>

        <div>
          <label
            htmlFor="password"
            className="block text-xs font-semibold text-[var(--ink)] mb-1.5"
          >
            كلمة المرور
          </label>
          <div className="relative">
            <input
              id="password"
              name="password"
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="••••••••••••"
              className="w-full h-11 px-3 pe-10 text-sm rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)]/60 focus:outline-none focus:ring-2 focus:ring-[var(--focus)] focus:border-transparent transition"
            />
            <Lock
              size={17}
              className="absolute end-3 top-1/2 -translate-y-1/2 text-[var(--muted)] pointer-events-none"
            />
          </div>
        </div>

        <button
          type="submit"
          disabled={loading}
          className="w-full h-11 mt-2 inline-flex items-center justify-center gap-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-sm shadow-sm hover:opacity-95 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition"
        >
          {loading ? (
            <span className="inline-block h-4 w-4 border-2 border-current border-t-transparent rounded-full animate-spin" />
          ) : (
            'دخول مساحة الإدارة'
          )}
        </button>
      </form>

      {/* Security note */}
      <div className="pt-2 border-t border-[var(--line)]/60 text-center">
        <p className="text-[11px] text-[var(--muted)] leading-relaxed">
          البيانات محفوظة في قاعدة التطبيق المحلية ولا تُرسل إلى خدمات ذكاء اصطناعي.
        </p>
      </div>
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense
      fallback={
        <div className="w-full max-w-md bg-[var(--surface)] border border-[var(--line)] rounded-2xl p-8 text-center text-xs text-[var(--muted)]">
          جاري التحميل...
        </div>
      }
    >
      <LoginForm />
    </Suspense>
  );
}
