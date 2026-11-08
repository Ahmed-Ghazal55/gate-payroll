'use client';

import { useState } from 'react';
import { api } from '../../lib/api';

export default function ChangePasswordPage() {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [again, setAgain] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    if (next.length < 12) return setError('كلمة المرور الجديدة 12 حرفًا على الأقل.');
    if (next !== again) return setError('كلمتا المرور غير متطابقتين.');
    setBusy(true);
    try {
      await api.changeMyPassword(current, next);
      window.location.href = '/app/login/';
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تغيير كلمة المرور.');
      setBusy(false);
    }
  };
  const field = 'w-full rounded-lg border border-[var(--line)] bg-[var(--surface)] p-2 text-sm text-[var(--ink)]';
  return (
    <main className="mx-auto mt-16 max-w-md space-y-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-6">
      <h1 className="text-lg font-bold text-[var(--ink)]">تغيير كلمة المرور</h1>
      <p className="text-xs text-[var(--muted)]">غيّر كلمة المرور عشان تكمل. بعدها هتدخل تاني بالكلمة الجديدة.</p>
      <form onSubmit={submit} className="space-y-3">
        <input id="current-password" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} placeholder="كلمة المرور الحالية" className={field} />
        <input id="new-password" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} placeholder="كلمة المرور الجديدة" className={field} />
        <input id="new-password-again" type="password" autoComplete="new-password" value={again} onChange={(e) => setAgain(e.target.value)} placeholder="أعد كتابتها" className={field} />
        {error && <p role="alert" className="text-xs font-bold text-[var(--danger)]">{error}</p>}
        <button type="submit" disabled={busy} className="w-full rounded-lg bg-[var(--accent)] p-2 text-sm font-bold text-white disabled:opacity-50">حفظ</button>
      </form>
    </main>
  );
}
