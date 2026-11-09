'use client';

import React, { useState } from 'react';
import { Wallet, Landmark, History } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState } from '../../components/ui/EmptyState';

export function TreasuryExtrasPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canMada = can('treasury.view') || can('advances.payout') || isAdmin;
  const canLiability = can('treasury.liability.settle') || isAdmin;
  const canLegacy = can('treasury.opening') || isAdmin;
  const [busy, setBusy] = useState(false);

  const [madaCompany, setMadaCompany] = useState('BW');
  const [madaBalance, setMadaBalance] = useState<number | null>(null);

  const [liability, setLiability] = useState({ debtId: '', paymentVaultCode: '', amount: '', sourceRef: '', period: '2026-10' });
  const [legacyJson, setLegacyJson] = useState('{}');
  const [legacyPreview, setLegacyPreview] = useState<Record<string, unknown> | null>(null);

  if (!canMada && !canLiability && !canLegacy) {
    return <EmptyState title="صلاحية غير كافية" description="لا تملك صلاحيات مدا أو سداد الالتزام أو التوزيع القديم." />;
  }

  const loadMada = async () => {
    setBusy(true);
    try {
      const res = await api.getMadaBalance(madaCompany);
      setMadaBalance(Number(res.balance));
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر قراءة رصيد مدا', 'danger');
    } finally { setBusy(false); }
  };

  const payLiability = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.postLiabilityPayment({ ...liability, amount: Number(liability.amount) });
      toast('تم سداد الالتزام', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر السداد', 'danger');
    } finally { setBusy(false); }
  };

  const previewLegacy = async () => {
    setBusy(true);
    try {
      const input = JSON.parse(legacyJson);
      const res = await api.previewLegacyCashAllocation(input);
      setLegacyPreview(res);
      toast('جاهزة المعاينة', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المعاينة', 'danger');
      setLegacyPreview(null);
    } finally { setBusy(false); }
  };

  const commitLegacy = async () => {
    if (!legacyPreview) return;
    setBusy(true);
    try {
      const input = { ...JSON.parse(legacyJson), fingerprint: legacyPreview.fingerprint, rows: legacyPreview.rows, confirm: true };
      await api.commitLegacyCashAllocation(input);
      toast('تم التوزيع النقدي القديم', 'success');
      setLegacyPreview(null);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التثبيت', 'danger');
    } finally { setBusy(false); }
  };

  return (
    <div className="space-y-4">
      {canMada && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
          <h3 className="text-sm font-bold inline-flex items-center gap-2"><Wallet size={16} /> رصيد شحن مدا (112404)</h3>
          <div className="flex flex-wrap gap-2 items-end">
            <label className="text-xs font-semibold space-y-1">
              <span>الشركة</span>
              <input value={madaCompany} onChange={(e) => setMadaCompany(e.target.value)} className="block p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono" />
            </label>
            <button type="button" disabled={busy} onClick={loadMada} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">قراءة</button>
            {madaBalance != null && <div className="text-lg font-bold font-mono">{madaBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س</div>}
          </div>
        </div>
      )}

      {canLiability && (
        <form onSubmit={payLiability} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
          <h3 className="text-sm font-bold inline-flex items-center gap-2"><Landmark size={16} /> سداد التزام مثبت (2130)</h3>
          {(['debtId', 'paymentVaultCode', 'amount', 'sourceRef', 'period'] as const).map((k) => (
            <input key={k} required placeholder={k} value={liability[k]} onChange={(e) => setLiability({ ...liability, [k]: e.target.value })} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          ))}
          <button type="submit" disabled={busy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">سداد</button>
        </form>
      )}

      {canLegacy && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
          <h3 className="text-sm font-bold inline-flex items-center gap-2"><History size={16} /> توزيع نقد قديم</h3>
          <textarea value={legacyJson} onChange={(e) => setLegacyJson(e.target.value)} rows={5} className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] font-mono text-[11px]" />
          <div className="flex gap-2">
            <button type="button" disabled={busy} onClick={previewLegacy} className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50">معاينة</button>
            <button type="button" disabled={busy || !legacyPreview} onClick={commitLegacy} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">تثبيت</button>
          </div>
          {legacyPreview && <pre className="text-[11px] font-mono whitespace-pre-wrap p-2 rounded-xl bg-[var(--raised)]/50 max-h-48 overflow-auto">{JSON.stringify(legacyPreview, null, 2)}</pre>}
        </div>
      )}
    </div>
  );
}
