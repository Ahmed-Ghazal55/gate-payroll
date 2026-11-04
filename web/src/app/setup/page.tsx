'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import { ShieldCheck, KeyRound, User, Lock, AlertCircle, CheckCircle } from 'lucide-react';
import { api, ApiError } from '../../lib/api';

export default function SetupPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [bootstrapKey, setBootstrapKey] = useState('');
  const [loading, setLoading] = useState(false);
  const [checking, setChecking] = useState(true);
  const [alreadyConfigured, setAlreadyConfigured] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    api
      .getAuthStatus()
      .then((status) => {
        if (mounted) {
          if (!status.needsSetup) {
            setAlreadyConfigured(true);
          }
          setChecking(false);
        }
      })
      .catch((err) => {
        if (mounted) {
          setError(err.message || 'تعذر التحقق من حالة النظام.');
          setChecking(false);
        }
      });

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
      await api.setup({
        username: username.trim(),
        password,
        bootstrapKey: bootstrapKey.trim() || undefined,
      });
      // Redirect to main application
      window.location.href = '/app/';
    } catch (err: any) {
      setError(err.message || 'تعذر إنشاء حساب المسؤول. تحقق من مفتاح التهيئة وصلاحية الحقول.');
    } finally {
      setLoading(false);
    }
  };

  if (checking) {
    return (
      <div className="w-full bg-[var(--surface)] border border-[var(--line)] rounded-2xl p-8 text-center text-xs text-[var(--muted)]">
        جاري التحقق من إعدادات النظام...
      </div>
    );
  }

  if (alreadyConfigured) {
    return (
      <div className="w-full bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-lg p-6 sm:p-8 space-y-5 text-center">
        <div className="inline-flex h-12 w-12 items-center justify-center rounded-2xl bg-[var(--accent)]/15 text-[var(--accent)] font-bold mb-1">
          <ShieldCheck size={28} />
        </div>
        <h1 className="text-xl font-bold text-[var(--ink)]">النظام مهيأ بالفعل</h1>
        <p className="text-xs text-[var(--muted)] leading-relaxed">
          تم إنشاء حساب المسؤول مسبقاً. لتسجيل الدخول، يرجى الانتقال إلى شاشة الدخول المعتادة.
        </p>
        <div className="pt-2">
          <Link
            href="/login/"
            className="w-full inline-flex items-center justify-center gap-2 h-11 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-sm shadow-sm hover:opacity-95 transition"
          >
            الانتقال لتسجيل الدخول
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full bg-[var(--surface)] border border-[var(--line)] rounded-2xl shadow-lg p-6 sm:p-8 space-y-6">
      {/* Brand & Header */}
      <div className="text-center space-y-2">
        <div className="inline-flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--accent)] text-[var(--accent-ink)] font-black text-2xl shadow-md mb-1">
          ب
        </div>
        <h1 className="text-2xl font-bold text-[var(--ink)]">تهيئة مساحة الرواتب</h1>
        <p className="text-xs text-[var(--muted)]">
          أنشئ حساب المسؤول الأول. لا توجد كلمة مرور افتراضية.
        </p>
      </div>

      {/* Error alert */}
      {error && (
        <div
          role="alert"
          aria-live="polite"
          className="p-3.5 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-xs text-[var(--danger)] flex items-start gap-2.5"
        >
          <AlertCircle size={16} className="shrink-0 mt-0.5" />
          <p className="leading-relaxed">{error}</p>
        </div>
      )}

      {/* Setup Form */}
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label
            htmlFor="setup-username"
            className="block text-xs font-semibold text-[var(--ink)] mb-1.5"
          >
            اسم المستخدم <span className="text-[var(--danger)]">*</span>
          </label>
          <div className="relative">
            <input
              id="setup-username"
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
            htmlFor="setup-password"
            className="block text-xs font-semibold text-[var(--ink)] mb-1.5"
          >
            كلمة المرور (12 خانة على الأقل) <span className="text-[var(--danger)]">*</span>
          </label>
          <div className="relative">
            <input
              id="setup-password"
              name="password"
              type="password"
              required
              minLength={12}
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="12 خانة أو أكثر"
              className="w-full h-11 px-3 pe-10 text-sm rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)]/60 focus:outline-none focus:ring-2 focus:ring-[var(--focus)] focus:border-transparent transition"
            />
            <Lock
              size={17}
              className="absolute end-3 top-1/2 -translate-y-1/2 text-[var(--muted)] pointer-events-none"
            />
          </div>
        </div>

        <div>
          <label
            htmlFor="setup-bootstrap-key"
            className="block text-xs font-semibold text-[var(--ink)] mb-1.5"
          >
            مفتاح تهيئة الإنتاج{' '}
            <span className="text-[11px] text-[var(--muted)] font-normal">
              (لا يلزم في التشغيل المحلي)
            </span>
          </label>
          <div className="relative">
            <input
              id="setup-bootstrap-key"
              name="bootstrapKey"
              type="password"
              autoComplete="off"
              value={bootstrapKey}
              onChange={(e) => setBootstrapKey(e.target.value)}
              placeholder="مفتاح الحماية عند التشغيل على خادم خارجي"
              className="w-full h-11 px-3 pe-10 text-sm rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] placeholder:text-[var(--muted)]/60 focus:outline-none focus:ring-2 focus:ring-[var(--focus)] focus:border-transparent transition"
            />
            <KeyRound
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
            'إنشاء حساب المسؤول والدخول'
          )}
        </button>
      </form>

      {/* Security note */}
      <div className="pt-2 border-t border-[var(--line)]/60 text-center">
        <p className="text-[11px] text-[var(--muted)] leading-relaxed">
          البيانات محفوظة في قاعدة التطبيق، ولا تُرسل إلى خدمات ذكاء اصطناعي.
        </p>
      </div>
    </div>
  );
}
