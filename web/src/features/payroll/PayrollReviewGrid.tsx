'use client';

import React, { useState, useEffect, useMemo, useCallback } from 'react';
import {
  CheckCircle2,
  AlertTriangle,
  Clock,
  CheckCheck,
  RefreshCw,
  Loader2,
  Eye,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { PayReviewResponse, PayReviewRow } from '../../lib/types';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { Dialog } from '../../components/ui/Dialog';
import { PayrollReviewDrawer } from './PayrollReviewDrawer';
import { DataTable, ColumnDef, RowAction } from '../../components/ui/DataTable';

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

  const [statusFilter, setStatusFilter] = useState<'all' | 'ready' | 'needs_decision'>('all');
  const [reviewFilter, setReviewFilter] = useState<'all' | 'pending' | 'approved'>('all');

  const [selectedRow, setSelectedRow] = useState<PayReviewRow | null>(null);
  const [bulkDialogOpen, setBulkDialogOpen] = useState(false);
  const [bulkSubmitting, setBulkSubmitting] = useState(false);

  const fetchReviewData = useCallback(async () => {
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
  }, [cycleId]);

  useEffect(() => {
    if (cycleId) {
      fetchReviewData();
    }
  }, [cycleId, fetchReviewData]);

  const maskIqama = (iqama: string) => {
    if (!iqama || iqama.length < 6) return iqama;
    return `${iqama.slice(0, 4)}****${iqama.slice(-2)}`;
  };

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

  // Filter rows based on stat card chips (status and review status)
  const rows = useMemo(() => {
    if (!data?.rows) return [];
    return data.rows.filter((r) => {
      if (statusFilter !== 'all' && r.status !== statusFilter) return false;
      if (reviewFilter !== 'all' && r.review !== reviewFilter) return false;
      return true;
    });
  }, [data?.rows, statusFilter, reviewFilter]);

  const columns: ColumnDef<PayReviewRow>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'المندوب',
        getValue: (r) => `${r.name || ''} ${r.rider || ''}`,
        render: (row) => (
          <div
            onClick={() => setSelectedRow(row)}
            className="cursor-pointer hover:underline"
          >
            <div className="font-bold text-[var(--ink)]">{row.name || '—'}</div>
            <div className="font-mono text-[10px] text-[var(--muted)]">{maskIqama(row.rider)}</div>
          </div>
        ),
      },
      {
        key: 'status',
        header: 'الحالة',
        getValue: (r) => (r.status === 'ready' ? 'جاهز' : 'يحتاج قرار'),
        render: (row) => (
          <span
            className={`text-[10px] font-bold px-2 py-0.5 rounded-full ${
              row.status === 'ready'
                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
            }`}
          >
            {row.status === 'ready' ? 'جاهز' : 'يحتاج قرار'}
          </span>
        ),
      },
      {
        key: 'gross',
        header: 'الإجمالي',
        numeric: true,
        getValue: (r) => r.gross,
        render: (row) => row.gross.toLocaleString('en-US', { minimumFractionDigits: 2 }),
      },
      {
        key: 'commission',
        header: 'العمولة',
        numeric: true,
        getValue: (r) => r.commission,
        render: (row) => (
          <span className="text-[var(--warning)]">
            {row.commission.toLocaleString('en-US', { minimumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'afterCommission',
        header: 'بعد العمولة',
        numeric: true,
        getValue: (r) => r.afterCommission,
        render: (row) => row.afterCommission.toLocaleString('en-US', { minimumFractionDigits: 2 }),
      },
      {
        key: 'phase1',
        header: 'مرحلة 1',
        numeric: true,
        getValue: (r) => r.phase1,
        render: (row) => (
          <span className="text-[var(--danger)]">
            {row.phase1.toLocaleString('en-US', { minimumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'preliminaryDue',
        header: 'المستحق الأولي',
        numeric: true,
        getValue: (r) => r.preliminaryDue,
        render: (row) => (
          <span className="font-bold text-[var(--accent)]">
            {row.preliminaryDue.toLocaleString('en-US', { minimumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'phase2',
        header: 'مرحلة 2',
        numeric: true,
        getValue: (r) => r.phase2,
        render: (row) => (
          <span className="text-[var(--danger)]">
            {row.phase2.toLocaleString('en-US', { minimumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'phase3',
        header: 'مرحلة 3',
        numeric: true,
        getValue: (r) => r.phase3,
        render: (row) => row.phase3.toLocaleString('en-US', { minimumFractionDigits: 2 }),
      },
      {
        key: 'net',
        header: 'الصافي',
        numeric: true,
        getValue: (r) => r.net,
        render: (row) => (
          <span
            className={`font-black ${
              row.net >= 0 ? 'text-[var(--money-pos)]' : 'text-[var(--danger)]'
            }`}
          >
            {row.net.toLocaleString('en-US', { minimumFractionDigits: 2 })}
          </span>
        ),
      },
      {
        key: 'review',
        header: 'المراجعة',
        getValue: (r) => (r.review === 'approved' ? 'معتمد' : 'بانتظار المراجعة'),
        render: (row) =>
          row.review === 'approved' ? (
            <span className="inline-flex items-center gap-1 text-[10px] font-bold text-[var(--money-pos)] bg-[var(--money-pos)]/10 px-2 py-0.5 rounded-full">
              <CheckCircle2 size={12} />
              <span>معتمد</span>
            </span>
          ) : (
            <button
              type="button"
              onClick={() => setSelectedRow(row)}
              className="px-2.5 py-1 text-[11px] font-bold rounded-lg border border-[var(--line)] bg-[var(--raised)] text-[var(--accent)] hover:bg-[var(--surface)] transition cursor-pointer"
            >
              مراجعة
            </button>
          ),
      },
    ],
    []
  );

  const rowActions: RowAction<PayReviewRow>[] = useMemo(
    () => [
      {
        label: 'مراجعة التفاصيل المالية',
        icon: <Eye size={14} />,
        onClick: (row) => setSelectedRow(row),
      },
    ],
    []
  );

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
  const readyPendingCount = (data?.rows || []).filter(
    (r) => r.review !== 'approved' && r.status === 'ready'
  ).length;

  return (
    <div className="space-y-4">
      {/* Sticky Totals Bar with Active Filter Chips */}
      <div className="sticky top-16 z-20 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3 sm:p-4 shadow-xs">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2 sm:gap-4 text-xs">
            {/* Total riders filter */}
            <button
              type="button"
              onClick={() => {
                setStatusFilter('all');
                setReviewFilter('all');
              }}
              className={`flex items-center gap-1.5 px-2.5 py-1 rounded-xl border transition cursor-pointer ${
                statusFilter === 'all' && reviewFilter === 'all'
                  ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                  : 'border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span className="h-2 w-2 rounded-full bg-[var(--accent)]" />
              <span>إجمالي المناديب:</span>
              <span className="font-mono font-bold num text-sm text-[var(--ink)]">
                {totals.riders}
              </span>
            </button>

            {/* Approved filter */}
            <button
              type="button"
              onClick={() =>
                setReviewFilter(reviewFilter === 'approved' ? 'all' : 'approved')
              }
              className={`flex items-center gap-1.5 px-2.5 py-1 rounded-xl border transition cursor-pointer ${
                reviewFilter === 'approved'
                  ? 'border-[var(--money-pos)] bg-[var(--money-pos)]/10 text-[var(--money-pos)] font-bold ring-2 ring-[var(--money-pos)]/20'
                  : 'border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span className="h-2 w-2 rounded-full bg-[var(--money-pos)]" />
              <span>تم الاعتماد:</span>
              <span className="font-mono font-bold num text-sm text-[var(--money-pos)]">
                {totals.approved}
              </span>
            </button>

            {/* Pending filter */}
            <button
              type="button"
              onClick={() =>
                setReviewFilter(reviewFilter === 'pending' ? 'all' : 'pending')
              }
              className={`flex items-center gap-1.5 px-2.5 py-1 rounded-xl border transition cursor-pointer ${
                reviewFilter === 'pending'
                  ? 'border-[var(--warning)] bg-[var(--warning)]/10 text-[var(--warning)] font-bold ring-2 ring-[var(--warning)]/20'
                  : 'border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span className="h-2 w-2 rounded-full bg-[var(--warning)]" />
              <span>بانتظار المراجعة:</span>
              <span className="font-mono font-bold num text-sm text-[var(--warning)]">
                {totals.pending}
              </span>
            </button>

            {/* Needs decision filter */}
            {totals.needsDecision > 0 && (
              <button
                type="button"
                onClick={() =>
                  setStatusFilter(statusFilter === 'needs_decision' ? 'all' : 'needs_decision')
                }
                className={`flex items-center gap-1.5 px-2.5 py-1 rounded-xl border transition cursor-pointer ${
                  statusFilter === 'needs_decision'
                    ? 'border-[var(--danger)] bg-[var(--danger)]/10 text-[var(--danger)] font-bold ring-2 ring-[var(--danger)]/20'
                    : 'border-[var(--line)] text-[var(--danger)] hover:bg-[var(--danger)]/5'
                }`}
              >
                <span className="h-2 w-2 rounded-full bg-[var(--danger)] animate-pulse" />
                <span className="font-semibold">يحتاج قرار:</span>
                <span className="font-mono font-bold num text-sm text-[var(--danger)]">
                  {totals.needsDecision}
                </span>
              </button>
            )}

            <div className="flex items-center gap-2 ps-2 text-[var(--muted)]">
              <span>إجمالي الصافي:</span>
              <span className="font-mono font-bold num text-sm text-[var(--ink)]">
                {totals.net.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
              </span>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={fetchReviewData}
              title="تحديث البيانات"
              className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] hover:bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] transition"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
            </button>

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
      </div>

      {/* Unified DataTable */}
      <DataTable
        tableId="payroll-review-grid"
        columns={columns}
        data={rows}
        loading={loading}
        rowActions={rowActions}
        getRowId={(r) => r.rider}
        emptyTitle="لا توجد سجلات مطابقة"
        emptyDescription="لم يتم العثور على أي سجلات في مسير الرواتب تطابق الفلاتر المحددة."
        exportFilename={`pay_review_${cycleId}`}
        showTotals={true}
      />

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
              className="px-3 py-1.5 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={bulkSubmitting}
              onClick={handleBulkApprove}
              className="px-4 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50 cursor-pointer"
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
              سيتم اعتماد{' '}
              <span className="font-mono font-bold text-[var(--money-pos)]">
                {readyPendingCount}
              </span>{' '}
              مندوباً جاهزاً تلقائياً وتثبيت اسمك وتاريخ المراجعة.
            </p>
          </div>

          {totals.needsDecision > 0 && (
            <div className="p-3 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 space-y-1">
              <span className="font-bold text-[var(--danger)]">
                الحالات المستثناة ({totals.needsDecision}):
              </span>
              <p className="text-[11px] text-[var(--muted)]">
                سيتم تخطي{' '}
                <span className="font-mono font-bold text-[var(--danger)]">
                  {totals.needsDecision}
                </span>{' '}
                مندوباً لوجود موانع استحقاق أو صافي سالب (§22.9.2). يجب معالجة كل حالة يدوياً عبر لوحة
                المراجعة.
              </p>
            </div>
          )}
        </div>
      </Dialog>
    </div>
  );
}
