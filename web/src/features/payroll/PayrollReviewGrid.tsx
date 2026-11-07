'use client';

import React, { useState, useEffect, useMemo } from 'react';
import {
  CheckCircle2,
  AlertTriangle,
  Clock,
  Search,
  Filter,
  Check,
  CheckCheck,
  RefreshCw,
  Eye,
  SlidersHorizontal,
  ChevronDown,
  Loader2,
  Users,
  DollarSign,
  Info,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import type { PayReviewResponse, PayReviewRow, PayReviewTotals } from '../../lib/types';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { Dialog } from '../../components/ui/Dialog';
import { PayrollReviewDrawer } from './PayrollReviewDrawer';

export interface PayrollReviewGridProps {
  cycleId: string;
  onCycleUpdated?: () => void;
}

export function PayrollReviewGrid({ cycleId, onCycleUpdated }: PayrollReviewGridProps) {
  const { toast } = useToast();
  const { can } = useAuth();
  const canManage = can('pay.manage');

  const [data, setData] = useState<PayReviewResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'ready' | 'needs_decision'>('all');
  const [reviewFilter, setReviewFilter] = useState<'all' | 'pending' | 'approved'>('all');

  const [selectedRow, setSelectedRow] = useState<PayReviewRow | null>(null);
  const [bulkDialogOpen, setBulkDialogOpen] = useState(false);
  const [bulkSubmitting, setBulkSubmitting] = useState(false);

  const fetchReviewData = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getPayCycleReview(cycleId);
      setData(res);
    } catch (err: any) {
      setError(err.message || 'تعذر تحميل بيانات مراجعة المسير.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (cycleId) {
      fetchReviewData();
    }
  }, [cycleId]);

  const maskIqama = (iqama: string) => {
    if (!iqama || iqama.length < 6) return iqama;
    return `${iqama.slice(0, 4)}****${iqama.slice(-2)}`;
  };

  const filteredRows = useMemo(() => {
    if (!data?.rows) return [];
    return data.rows.filter((r) => {
      if (statusFilter !== 'all' && r.status !== statusFilter) return false;
      if (reviewFilter !== 'all' && r.review !== reviewFilter) return false;
      if (searchTerm) {
        const q = searchTerm.trim().toLowerCase();
        const matchName = r.name?.toLowerCase().includes(q);
        const matchIqama = r.rider?.includes(q);
        if (!matchName && !matchIqama) return false;
      }
      return true;
    });
  }, [data, statusFilter, reviewFilter, searchTerm]);

  const handleBulkApprove = async () => {
    setBulkSubmitting(true);
    try {
      const res = await api.approveAllPayCycleReview(cycleId);
      toast(`تم اعتماد ${res.approved} مندوباً بنجاح (تم تخطي ${res.skipped} لعدم الجاهزية).`, 'success');
      setBulkDialogOpen(false);
      await fetchReviewData();
      if (onCycleUpdated) onCycleUpdated();
    } catch (err: any) {
      toast(err.message || 'تعذر تنفيذ الاعتماد الجماعي.', 'danger');
    } finally {
      setBulkSubmitting(false);
    }
  };

  const handleRowUpdated = (updated: PayReviewRow) => {
    if (!data) return;
    const newRows = data.rows.map((r) => (r.rider === updated.rider ? updated : r));
    const approvedCount = newRows.filter((r) => r.review === 'approved').length;
    const pendingCount = newRows.filter((r) => r.review !== 'approved').length;
    const needsDecisionCount = newRows.filter((r) => r.status !== 'ready').length;

    setData({
      ...data,
      totals: {
        ...data.totals,
        approved: approvedCount,
        pending: pendingCount,
        needsDecision: needsDecisionCount,
      },
      rows: newRows,
    });
    setSelectedRow(updated);
    if (onCycleUpdated) onCycleUpdated();
  };

  if (loading && !data) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-[var(--muted)] space-y-3">
        <Loader2 size={32} className="animate-spin text-[var(--accent)]" />
        <p className="text-xs">جاري تحميل جدول مراجعة المسير (BR §32)...</p>
      </div>
    );
  }

  if (error && !data) {
    return (
      <div className="rounded-2xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 p-6 text-center space-y-3">
        <AlertTriangle size={32} className="mx-auto text-[var(--danger)]" />
        <h4 className="text-sm font-bold text-[var(--danger)]">تعذر عرض جدول المراجعة</h4>
        <p className="text-xs text-[var(--muted)]">{error}</p>
        <button
          type="button"
          onClick={fetchReviewData}
          className="px-4 py-2 text-xs font-bold rounded-xl bg-[var(--surface)] border border-[var(--line)] hover:bg-[var(--raised)]"
        >
          إعادة المحاولة
        </button>
      </div>
    );
  }

  const totals = data?.totals || { riders: 0, approved: 0, pending: 0, needsDecision: 0, net: 0 };
  const readyPendingCount = (data?.rows || []).filter((r) => r.review !== 'approved' && r.status === 'ready').length;

  return (
    <div className="space-y-4">
      {/* Sticky Totals Bar */}
      <div className="sticky top-16 z-20 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:p-4 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-3 sm:gap-6 text-xs">
            <div className="flex items-center gap-2">
              <span className="h-2 w-2 rounded-full bg-[var(--accent)]" />
              <span className="text-[var(--muted)]">إجمالي المناديب:</span>
              <span className="font-mono font-bold num text-sm text-[var(--ink)]">{totals.riders}</span>
            </div>

            <div className="flex items-center gap-2">
              <span className="h-2 w-2 rounded-full bg-[var(--money-pos)]" />
              <span className="text-[var(--muted)]">تم الاعتماد:</span>
              <span className="font-mono font-bold num text-sm text-[var(--money-pos)]">{totals.approved}</span>
            </div>

            <div className="flex items-center gap-2">
              <span className="h-2 w-2 rounded-full bg-[var(--warning)]" />
              <span className="text-[var(--muted)]">بانتظار المراجعة:</span>
              <span className="font-mono font-bold num text-sm text-[var(--warning)]">{totals.pending}</span>
            </div>

            {totals.needsDecision > 0 && (
              <div className="flex items-center gap-2">
                <span className="h-2 w-2 rounded-full bg-[var(--danger)] animate-pulse" />
                <span className="text-[var(--danger)] font-semibold">يحتاج قرار:</span>
                <span className="font-mono font-bold num text-sm text-[var(--danger)]">{totals.needsDecision}</span>
              </div>
            )}

            <div className="flex items-center gap-2">
              <span className="text-[var(--muted)]">إجمالي الصافي:</span>
              <span className="font-mono font-bold num text-sm text-[var(--ink)]">
                {totals.net.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
              </span>
            </div>
          </div>

          {/* Bulk Approve Action */}
          {canManage && totals.pending > 0 && (
            <button
              type="button"
              disabled={readyPendingCount === 0}
              onClick={() => setBulkDialogOpen(true)}
              className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed shadow-xs transition"
            >
              <CheckCheck size={15} />
              <span>اعتماد الكل ({readyPendingCount})</span>
            </button>
          )}
        </div>
      </div>

      {/* Filters Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-3 p-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] text-xs">
        <div className="flex-1 min-w-[220px] max-w-sm relative">
          <Search size={14} className="absolute start-3 top-2.5 text-[var(--muted)]" />
          <input
            type="text"
            placeholder="بحث بالاسم أو رقم الهوية..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full ps-9 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs focus:outline-hidden"
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {/* Status Filter */}
          <div className="flex items-center gap-1 p-1 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
            <button
              type="button"
              onClick={() => setStatusFilter('all')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                statusFilter === 'all'
                  ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              كل الحالات
            </button>
            <button
              type="button"
              onClick={() => setStatusFilter('ready')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                statusFilter === 'ready'
                  ? 'bg-[var(--surface)] text-[var(--money-pos)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              جاهز
            </button>
            <button
              type="button"
              onClick={() => setStatusFilter('needs_decision')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                statusFilter === 'needs_decision'
                  ? 'bg-[var(--surface)] text-[var(--danger)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              يحتاج قرار
            </button>
          </div>

          {/* Review Filter */}
          <div className="flex items-center gap-1 p-1 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
            <button
              type="button"
              onClick={() => setReviewFilter('all')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                reviewFilter === 'all'
                  ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              الكل
            </button>
            <button
              type="button"
              onClick={() => setReviewFilter('pending')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                reviewFilter === 'pending'
                  ? 'bg-[var(--surface)] text-[var(--warning)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              بانتظار المراجعة
            </button>
            <button
              type="button"
              onClick={() => setReviewFilter('approved')}
              className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                reviewFilter === 'approved'
                  ? 'bg-[var(--surface)] text-[var(--money-pos)] shadow-xs font-bold'
                  : 'text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              معتمد
            </button>
          </div>

          <button
            type="button"
            onClick={fetchReviewData}
            title="تحديث البيانات"
            className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] hover:bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] transition"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
          </button>
        </div>
      </div>

      {/* Review Table (Horizontal Scroll on Mobile, No Page Scroll) */}
      <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-start text-xs border-collapse">
            <thead>
              <tr className="bg-[var(--raised)] border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                <th className="py-2.5 px-3 text-start">المندوب</th>
                <th className="py-2.5 px-2 text-center">الحالة</th>
                <th className="py-2.5 px-2 text-end num">الإجمالي</th>
                <th className="py-2.5 px-2 text-end num">العمولة</th>
                <th className="py-2.5 px-2 text-end num">بعد العمولة</th>
                <th className="py-2.5 px-2 text-end num text-[var(--danger)]">مرحلة 1</th>
                <th className="py-2.5 px-2 text-end num font-bold text-[var(--accent)]">المستحق الأولي</th>
                <th className="py-2.5 px-2 text-end num text-[var(--danger)]">مرحلة 2</th>
                <th className="py-2.5 px-2 text-end num">مرحلة 3</th>
                <th className="py-2.5 px-2 text-end num font-black text-[var(--ink)]">الصافي</th>
                <th className="py-2.5 px-3 text-center">المراجعة</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {filteredRows.length === 0 ? (
                <tr>
                  <td colSpan={11} className="py-12 text-center text-xs text-[var(--muted)]">
                    لا توجد سجلات مطابقة لمعايير البحث والتصفية.
                  </td>
                </tr>
              ) : (
                filteredRows.map((row) => (
                  <tr
                    key={row.rider}
                    onClick={() => setSelectedRow(row)}
                    className="hover:bg-[var(--row-hover)] cursor-pointer transition"
                  >
                    <td className="py-2.5 px-3">
                      <div className="font-bold text-[var(--ink)]">{row.name || '—'}</div>
                      <div className="font-mono text-[10px] text-[var(--muted)]">{maskIqama(row.rider)}</div>
                    </td>

                    <td className="py-2.5 px-2 text-center">
                      <span
                        className={`text-[10px] font-bold px-2 py-0.5 rounded-full ${
                          row.status === 'ready'
                            ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                            : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                        }`}
                      >
                        {row.status === 'ready' ? 'جاهز' : 'يحتاج قرار'}
                      </span>
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num">
                      {row.gross.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num text-[var(--warning)]">
                      {row.commission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num">
                      {row.afterCommission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num text-[var(--danger)]">
                      {row.phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num font-bold text-[var(--accent)] bg-[var(--accent)]/5">
                      {row.preliminaryDue.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num text-[var(--danger)]">
                      {row.phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num">
                      {row.phase3.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                    </td>

                    <td className="py-2.5 px-2 text-end font-mono num font-black text-sm">
                      <span className={row.net >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--danger)]'}>
                        {row.net.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </span>
                    </td>

                    <td className="py-2.5 px-3 text-center" onClick={(e) => e.stopPropagation()}>
                      {row.review === 'approved' ? (
                        <span className="inline-flex items-center gap-1 text-[10px] font-bold text-[var(--money-pos)] bg-[var(--money-pos)]/10 px-2 py-0.5 rounded-full">
                          <CheckCircle2 size={12} />
                          <span>معتمد</span>
                        </span>
                      ) : (
                        <button
                          type="button"
                          onClick={() => setSelectedRow(row)}
                          className="px-2.5 py-1 text-[11px] font-bold rounded-lg border border-[var(--line)] bg-[var(--raised)] text-[var(--accent)] hover:bg-[var(--surface)] transition"
                        >
                          مراجعة
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Review Drawer */}
      <PayrollReviewDrawer
        open={Boolean(selectedRow)}
        onClose={() => setSelectedRow(null)}
        row={selectedRow}
        cycleId={cycleId}
        onRowUpdated={handleRowUpdated}
      />

      {/* Bulk Approve Confirm Dialog */}
      <Dialog
        open={bulkDialogOpen}
        onClose={() => setBulkDialogOpen(false)}
        title="تأكيد الاعتماد الجماعي للمسير"
        description="سيتم اعتماد كافة أسطر المناديب الجاهزة وتخطي الحالات التي تتطلب قراراً مالياً."
        actions={
          <div className="flex items-center justify-end gap-2">
            <button
              type="button"
              onClick={() => setBulkDialogOpen(false)}
              className="px-3 py-1.5 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={bulkSubmitting}
              onClick={handleBulkApprove}
              className="px-4 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50"
            >
              {bulkSubmitting ? 'جاري الاعتماد...' : `اعتماد (${readyPendingCount}) مندوباً`}
            </button>
          </div>
        }
      >
        <div className="space-y-3 text-xs text-[var(--ink)]">
          <div className="p-3 rounded-xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/30 space-y-1">
            <span className="font-bold text-[var(--money-pos)]">المناديب الجاهزون للاعتماد:</span>
            <p className="text-[11px] text-[var(--muted)]">
              سيتم اعتماد <span className="font-mono font-bold text-[var(--money-pos)]">{readyPendingCount}</span> مندوباً جاهزاً تلقائياً وتثبيت اسمك وتاريخ المراجعة.
            </p>
          </div>

          {totals.needsDecision > 0 && (
            <div className="p-3 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 space-y-1">
              <span className="font-bold text-[var(--danger)]">الحالات المستثناة ({totals.needsDecision}):</span>
              <p className="text-[11px] text-[var(--muted)]">
                سيتم تخطي <span className="font-mono font-bold text-[var(--danger)]">{totals.needsDecision}</span> مندوباً لوجود موانع استحقاق أو صافي سالب (§22.9.2). يجب معالجة كل حالة يدوياً عبر لوحة المراجعة.
              </p>
            </div>
          )}
        </div>
      </Dialog>
    </div>
  );
}
