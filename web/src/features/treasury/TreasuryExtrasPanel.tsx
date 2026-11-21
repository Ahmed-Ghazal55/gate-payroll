'use client';

import React, { useEffect, useMemo, useState } from 'react';
import { Wallet, Landmark, History } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { formatSar } from '../../lib/money';
import { formatRecordEntries } from '../../lib/formatRecord';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';

const inputClass =
  'w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs';

export function TreasuryExtrasPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canMada = can('treasury.view') || can('advances.payout') || isAdmin;
  const canLiability = can('treasury.liability.settle') || isAdmin;
  const canLegacy = can('treasury.opening') || isAdmin;
  const [busy, setBusy] = useState(false);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);

  const [madaCompany, setMadaCompany] = useState('');
  const [madaBalance, setMadaBalance] = useState<number | null>(null);

  const [liability, setLiability] = useState({
    debtId: '',
    paymentVaultCode: '',
    amount: '',
    sourceRef: '',
    period: '2026-10',
  });
  const [legacyJson, setLegacyJson] = useState('');
  const [legacyPreview, setLegacyPreview] = useState<Record<string, unknown> | null>(null);
  const legacyEntries = useMemo(
    () => (legacyPreview ? formatRecordEntries(legacyPreview) : []),
    [legacyPreview]
  );
  const legacyCols = useMemo(
    () => [
      { key: 'key', header: 'الحقل', type: 'list' as const, getValue: (e: { key: string; value: string }) => e.key || '' },
      { key: 'value', header: 'القيمة', type: 'text' as const, getValue: (e: { key: string; value: string }) => String(e.value ?? '') },
    ],
    []
  );
  const legacyFilter = useTableAutoFilter(legacyEntries, legacyCols);

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      const first = list[0]?.id || list[0]?.code || '';
      if (first) setMadaCompany((prev) => prev || first);
    });
  }, []);

  const companyOptions = companies.map((c) => ({
    value: c.id || c.code || '',
    label: companyLabel(c),
  }));

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
    } finally {
      setBusy(false);
    }
  };

  const payLiability = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await api.postLiabilityPayment({ ...liability, amount: Number(liability.amount) });
      toast('تم سداد الالتزام', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر السداد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const previewLegacy = async () => {
    setBusy(true);
    try {
      const trimmed = legacyJson.trim();
      if (!trimmed) {
        toast('أدخل بيانات التوزيع أولاً', 'danger');
        return;
      }
      const input = JSON.parse(trimmed);
      const res = await api.previewLegacyCashAllocation(input);
      setLegacyPreview(res);
      toast('جاهزة المعاينة', 'success');
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر المعاينة', 'danger');
      setLegacyPreview(null);
    } finally {
      setBusy(false);
    }
  };

  const commitLegacy = async () => {
    if (!legacyPreview) return;
    setBusy(true);
    try {
      const input = {
        ...JSON.parse(legacyJson),
        fingerprint: legacyPreview.fingerprint,
        rows: legacyPreview.rows,
        confirm: true,
      };
      await api.commitLegacyCashAllocation(input);
      toast('تم التوزيع النقدي القديم', 'success');
      setLegacyPreview(null);
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التثبيت', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4">
      {canMada && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
          <h3 className="text-sm font-bold inline-flex items-center gap-2">
            <Wallet size={16} /> رصيد شحن مدا (112404)
          </h3>
          <div className="flex flex-wrap gap-2 items-end">
            <label className="text-xs font-semibold space-y-1 min-w-[12rem]">
              <span>الشركة</span>
              <SearchableSelect
                id="mada-company"
                value={madaCompany}
                onChange={setMadaCompany}
                ariaLabel="الشركة"
                placeholder="اختر الشركة"
                options={companyOptions}
                className="w-full"
              />
            </label>
            <button
              type="button"
              disabled={busy || !madaCompany}
              onClick={loadMada}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
            >
              قراءة
            </button>
            {madaBalance != null && <div className="text-lg font-bold font-mono">{formatSar(madaBalance)}</div>}
          </div>
        </div>
      )}

      {canLiability && (
        <form onSubmit={payLiability} className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
          <h3 className="text-sm font-bold inline-flex items-center gap-2">
            <Landmark size={16} /> سداد التزام مثبت (2130)
          </h3>
          <label className="text-xs font-semibold space-y-1 block">
            <span>معرف المديونية</span>
            <input
              required
              value={liability.debtId}
              onChange={(e) => setLiability({ ...liability, debtId: e.target.value })}
              placeholder="رقم أو معرف المديونية"
              className={`${inputClass} font-mono`}
            />
          </label>
          <label className="text-xs font-semibold space-y-1 block">
            <span>رمز خزنة السداد</span>
            <input
              required
              value={liability.paymentVaultCode}
              onChange={(e) => setLiability({ ...liability, paymentVaultCode: e.target.value })}
              placeholder="مثال: BANK.BW أو MAIN.BW.DEBT"
              className={`${inputClass} font-mono`}
            />
          </label>
          <label className="text-xs font-semibold space-y-1 block">
            <span>المبلغ</span>
            <input
              required
              value={liability.amount}
              onChange={(e) => setLiability({ ...liability, amount: e.target.value })}
              placeholder="0.00"
              className={`${inputClass} font-mono`}
            />
          </label>
          <label className="text-xs font-semibold space-y-1 block">
            <span>مرجع المصدر</span>
            <input
              required
              value={liability.sourceRef}
              onChange={(e) => setLiability({ ...liability, sourceRef: e.target.value })}
              placeholder="مرجع العملية"
              className={inputClass}
            />
          </label>
          <label className="text-xs font-semibold space-y-1 block">
            <span>الفترة</span>
            <input
              required
              type="month"
              value={liability.period}
              onChange={(e) => setLiability({ ...liability, period: e.target.value })}
              className={inputClass}
            />
          </label>
          <button
            type="submit"
            disabled={busy}
            className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
          >
            سداد
          </button>
        </form>
      )}

      {canLegacy && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
          <h3 className="text-sm font-bold inline-flex items-center gap-2">
            <History size={16} /> توزيع نقد قديم
          </h3>
          <label className="text-xs font-semibold space-y-1 block">
            <span>بيانات التوزيع</span>
            <textarea
              value={legacyJson}
              onChange={(e) => setLegacyJson(e.target.value)}
              rows={5}
              placeholder="الصق هنا بيانات التوزيع المعتمدة من المالية (بدون ترك الحقل فارغًا)"
              className={`${inputClass} font-mono text-[11px]`}
            />
          </label>
          <div className="flex gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={previewLegacy}
              className="px-3 py-2 rounded-xl border border-[var(--line)] text-xs font-bold disabled:opacity-50"
            >
              معاينة
            </button>
            <button
              type="button"
              disabled={busy || !legacyPreview}
              onClick={commitLegacy}
              className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50"
            >
              تثبيت
            </button>
          </div>
          {legacyPreview && (
            <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)]/50 p-3 max-h-56 overflow-auto">
              <AutoFilterBar controller={legacyFilter} totalCount={legacyEntries.length} />
              <table className="w-full text-xs text-start">
                <thead className="text-[var(--muted)] border-b border-[var(--line)]">
                  <tr>
                    <AutoFilterTh controller={legacyFilter} colKey="key" className="py-1.5 pe-3 text-start" />
                    <AutoFilterTh controller={legacyFilter} colKey="value" className="py-1.5 text-start" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {legacyFilter.rows.map((entry) => (
                    <tr key={entry.key}>
                      <td className="py-1.5 pe-3 font-semibold text-[var(--muted)] whitespace-nowrap">{entry.key}</td>
                      <td className="py-1.5 break-words">{entry.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
