'use client';

import React, { useState, useMemo } from 'react';
import {
  Check,
  X,
  Eye,
  Trash2,
  FileSpreadsheet,
  AlertTriangle,
  RefreshCw,
  CheckCircle2,
  AlertCircle,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import type { Submission, Campaign } from '../../lib/types';
import { Toolbar } from '../../components/ui/Toolbar';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { ReviewDrawer } from './ReviewDrawer';

export interface ResponsesTabProps {
  submissions: Submission[];
  campaigns: Campaign[];
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
}

export function ResponsesTab({
  submissions,
  campaigns,
  loading,
  error,
  onRefresh,
}: ResponsesTabProps) {
  const { toast } = useToast();
  const { canReview, isAdmin } = useAuth();

  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [campaignFilter, setCampaignFilter] = useState<string>('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [reviewTarget, setReviewTarget] = useState<Submission | null>(null);
  const [bulkAction, setBulkAction] = useState<'approve' | 'delete' | null>(null);
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [exportLoading, setExportLoading] = useState(false);

  // Helper for IBAN masking in list view: SA** **** **** **** **1234
  const maskIban = (iban?: string) => {
    if (!iban) return '—';
    const clean = iban.replace(/\s+/g, '');
    if (clean.length < 10) return clean;
    const prefix = clean.slice(0, 4);
    const suffix = clean.slice(-4);
    return `${prefix} **** **** **** ${suffix}`;
  };

  const filteredSubmissions = useMemo(() => {
    return submissions.filter((s) => {
      if (statusFilter !== 'all' && s.status !== statusFilter) return false;
      if (campaignFilter && s.campaignId !== campaignFilter) return false;
      return true;
    });
  }, [submissions, statusFilter, campaignFilter]);

  // Quick review
  const handleQuickApprove = async (s: Submission) => {
    if (s.iban) {
      toast('لا يمكن الاعتماد الفوري لرد يحتوي على آيبان؛ افتح التفاصيل لتأكيد ملكية الحساب.', 'warning');
      setReviewTarget(s);
      return;
    }
    try {
      await api.reviewSubmission(s.id, {
        status: 'approved',
        version: s.version || 1,
        identityReviewed: true,
      });
      toast('تم اعتماد الاختيار بنجاح', 'success');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر الاعتماد', 'danger');
    }
  };

  const handleQuickReject = async (s: Submission) => {
    try {
      await api.reviewSubmission(s.id, {
        status: 'rejected',
        version: s.version || 1,
      });
      toast('تم رفض الرد', 'warning');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر الرفض', 'danger');
    }
  };

  const handleSingleDelete = async (s: Submission) => {
    if (!confirm(`هل أنت متأكد من حذف رد المندوب «${s.name}» نهائيًا والسماح له بإعادة التسجيل؟`)) return;
    try {
      await api.deleteSubmission(s.id);
      toast('تم حذف الرد بنجاح', 'success');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر حذف الرد', 'danger');
    }
  };

  // Bulk actions
  const handleBulkActionConfirm = async () => {
    if (!bulkAction || selectedIds.length === 0) return;

    if (bulkAction === 'approve') {
      const selectedRows = submissions.filter((s) => selectedIds.includes(s.id));
      const hasIbanRow = selectedRows.some((s) => !!s.iban);
      if (hasIbanRow) {
        toast('تتضمن الدفعة المحددة آيبانات بنكية؛ راجع ملكيتها فرديًا قبل الاعتماد الجماعي.', 'danger');
        setBulkAction(null);
        return;
      }
    }

    setBulkSubmitting(true);
    try {
      if (bulkAction === 'approve') {
        const res = await api.bulkApproveSubmissions(campaignFilter || undefined, selectedIds);
        toast(`تم اعتماد ${res.count} رد بنجاح`, 'success');
      } else {
        const res = await api.bulkDeleteSubmissions(campaignFilter || undefined, selectedIds);
        toast(`تم حذف ${res.count} رد بنجاح`, 'warning');
      }
      setSelectedIds([]);
      setBulkAction(null);
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر تنفيذ الإجراء الجماعي', 'danger');
    } finally {
      setBulkSubmitting(false);
    }
  };

  // Excel / CSV Export
  const handleExport = async (format: 'excel-all' | 'excel-filtered' | 'csv-filtered') => {
    setExportLoading(true);
    const targetRows = format === 'excel-all' ? submissions : filteredSubmissions;
    const headers = ['اسم المندوب', 'الإقامة', 'البيان/الآيبان', 'البنك', 'النظام', 'الحملة', 'الحالة', 'التاريخ'];

    // Formula injection guard matching Frontend/src/table-tools.ts:86
    const sanitize = (val: string) => {
      if (/^[=+\-@\t\r]/.test(val)) return "'" + val;
      return val;
    };

    const rows = targetRows.map((s) => [
      sanitize(s.name),
      sanitize(s.iqama),
      sanitize(s.iban ? maskIban(s.iban) : s.workSystem || '—'),
      sanitize(s.bank || '—'),
      sanitize(s.workSystem || '—'),
      sanitize(s.campaignTitle || s.campaignId),
      sanitize(s.status === 'approved' ? 'معتمد' : s.status === 'rejected' ? 'مرفوض' : 'قيد المراجعة'),
      sanitize(s.createdAt ? new Date(s.createdAt).toLocaleDateString('ar-SA') : '—'),
    ]);

    try {
      if (format === 'csv-filtered') {
        // Client-side CSV generation with UTF-8 BOM and formula guard
        const quote = (v: any) => `"${String(v).replace(/"/g, '""')}"`;
        const csvContent =
          '\uFEFF' +
          [headers.map(quote).join(','), ...rows.map((r) => r.map(quote).join(','))].join('\r\n');
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `responses-filtered.csv`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تنزيل ملف CSV المفلتر بنجاح', 'success');
      } else {
        // Excel Export via backend
        const blob = await api.exportExcel({
          headers,
          rows,
          numericColumns: [1], // iqama column
          filename: `responses-${format}.xlsx`,
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `responses-${format}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تنزيل ملف Excel بنجاح', 'success');
      }
    } catch (err: any) {
      toast(err?.message || 'تعذر تصدير الملف', 'danger');
    } finally {
      setExportLoading(false);
    }
  };

  const columns: ColumnDef<Submission>[] = [
    {
      key: 'name',
      header: 'اسم المندوب',
      render: (s) => (
        <div className="flex flex-col">
          <strong className="font-bold text-[var(--ink)]">{s.name}</strong>
          {s.isEdited && (
            <span className="text-[10px] text-[var(--warning)] font-semibold">
              رد معدل (نسخة {s.version})
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'iqama',
      header: 'رقم الإقامة',
      render: (s) => <span className="num font-mono text-xs font-bold">{s.iqama}</span>,
    },
    {
      key: 'choice',
      header: 'البيان / الآيبان',
      render: (s) =>
        s.iban ? (
          <div className="flex flex-col">
            <span className="num font-mono text-xs text-[var(--accent)] font-semibold tracking-wide">
              {maskIban(s.iban)}
            </span>
            <span className="text-[11px] text-[var(--muted)]">{s.bank || 'مصرف الراجحي'}</span>
          </div>
        ) : (
          <span className="font-semibold text-xs">
            {s.workSystem === 'monthly' ? 'راتب شهري' : s.workSystem === 'per-order' ? 'بالطلب' : s.workSystem || '—'}
          </span>
        ),
    },
    {
      key: 'campaign',
      header: 'الحملة',
      render: (s) => (
        <span className="text-xs text-[var(--muted)] truncate max-w-[150px] inline-block">
          {s.campaignTitle || s.campaignId}
        </span>
      ),
    },
    {
      key: 'status',
      header: 'الحالة',
      render: (s) => {
        const badges = {
          pending: { label: 'قيد المراجعة', class: 'bg-[var(--warning)]/15 text-[var(--warning)]' },
          approved: { label: 'معتمد', class: 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]' },
          rejected: { label: 'مرفوض', class: 'bg-[var(--danger)]/15 text-[var(--danger)]' },
        };
        const b = badges[s.status] || badges.pending;
        return <span className={`px-2 py-0.5 rounded-full text-xs font-semibold ${b.class}`}>{b.label}</span>;
      },
    },
    {
      key: 'createdAt',
      header: 'تاريخ الإرسال',
      render: (s) => (
        <span className="num text-xs text-[var(--muted)]">
          {new Date(s.createdAt).toLocaleDateString('ar-SA')}
        </span>
      ),
    },
  ];

  return (
    <div className="space-y-4">
      <Toolbar
        searchPlaceholder="بحث في الردود برقم الهوية أو الاسم..."
        filterChips={[
          {
            id: 'all',
            label: 'كل الردود',
            active: statusFilter === 'all',
            count: submissions.length,
            onToggle: () => setStatusFilter('all'),
          },
          {
            id: 'pending',
            label: 'بانتظار المراجعة',
            active: statusFilter === 'pending',
            count: submissions.filter((s) => s.status === 'pending').length,
            onToggle: () => setStatusFilter('pending'),
          },
          {
            id: 'approved',
            label: 'معتمدة',
            active: statusFilter === 'approved',
            count: submissions.filter((s) => s.status === 'approved').length,
            onToggle: () => setStatusFilter('approved'),
          },
          {
            id: 'rejected',
            label: 'مرفوضة',
            active: statusFilter === 'rejected',
            count: submissions.filter((s) => s.status === 'rejected').length,
            onToggle: () => setStatusFilter('rejected'),
          },
        ]}
        onExport={handleExport}
        exportLoading={exportLoading}
        extraControls={
          <div className="flex items-center gap-2">
            {/* Campaign filter */}
            <SearchableSelect
              id="select-campaign-filter"
              value={campaignFilter}
              onChange={setCampaignFilter}
              ariaLabel="تصفية حسب الحملة"
              placeholder="كل الحملات"
              className="w-52"
              options={[
                { value: '', label: 'كل الحملات' },
                ...campaigns.map((c) => ({ value: c.id, label: c.title })),
              ]}
            />

            {/* Bulk actions */}
            {canReview && selectedIds.length > 0 && (
              <div className="flex items-center gap-1.5 bg-[var(--row-selected)] px-2.5 py-1 rounded-lg border border-[var(--accent)]">
                <span className="text-xs font-bold text-[var(--accent)] num">
                  {selectedIds.length} محدد
                </span>
                <button
                  type="button"
                  onClick={() => setBulkAction('approve')}
                  className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded bg-[var(--accent)] text-[var(--accent-ink)]"
                >
                  <Check size={12} />
                  اعتماد
                </button>
                {isAdmin && (
                  <button
                    type="button"
                    onClick={() => setBulkAction('delete')}
                    className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded bg-[var(--danger)] text-white"
                  >
                    <Trash2 size={12} />
                    حذف
                  </button>
                )}
              </div>
            )}

            <button
              type="button"
              onClick={onRefresh}
              className="p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)]"
              title="تحديث البيانات"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
            </button>
          </div>
        }
      />

      {error ? (
        <ErrorState
          title={error.includes('404') ? 'خدمة مراجعة الردود قيد التجهيز في الـ API' : 'تعذر تحميل الردود'}
          message={
            error.includes('404')
              ? 'المسار البرمجي الموحد (/api/responses) قيد الإطلاق من الخادم. يمكنك الاستمرار في مراجعة الردود عبر الواجهة السابقة.'
              : error
          }
          onRetry={onRefresh}
        />
      ) : (
        <DataTable
          columns={columns}
          data={filteredSubmissions}
          loading={loading}
          selectable={canReview}
          selectedIds={selectedIds}
          onSelectionChange={setSelectedIds}
          getRowId={(r) => r.id}
          emptyTitle="لا توجد ردود مسجلة"
          emptyDescription="لم تصل ردود جديدة تطابق خيارات التصفية المحددة."
          rowActions={[
            {
              label: 'معاينة وتدقيق',
              onClick: (s) => setReviewTarget(s),
              icon: <Eye size={14} />,
            },
            ...(canReview
              ? [
                  {
                    label: 'اعتماد فوري',
                    // B3: Quick approve is disabled if IBAN is present!
                    disabled: (s: Submission) => s.status === 'approved' || !!s.iban,
                    onClick: (s: Submission) => handleQuickApprove(s),
                    icon: <Check size={14} />,
                  },
                  {
                    label: 'رفض الرد',
                    disabled: (s: Submission) => s.status === 'rejected',
                    danger: true,
                    onClick: (s: Submission) => handleQuickReject(s),
                    icon: <X size={14} />,
                  },
                ]
              : []),
            ...(isAdmin
              ? [
                  {
                    label: 'حذف الرد نهائيًا',
                    danger: true,
                    onClick: (s: Submission) => handleSingleDelete(s),
                    icon: <Trash2 size={14} />,
                  },
                ]
              : []),
          ]}
        />
      )}

      {/* Review Drawer */}
      <ReviewDrawer
        submission={reviewTarget}
        onClose={() => setReviewTarget(null)}
        onSuccess={onRefresh}
      />

      {/* Bulk Action Confirmation Dialog */}
      <Dialog
        open={!!bulkAction}
        onClose={() => setBulkAction(null)}
        title={bulkAction === 'approve' ? 'تأكيد الاعتماد الجماعي' : 'تأكيد الحذف الجماعي'}
        actions={
          <>
            <button
              type="button"
              disabled={bulkSubmitting}
              onClick={() => setBulkAction(null)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={bulkSubmitting}
              onClick={handleBulkActionConfirm}
              className={`px-4 py-2 text-xs font-semibold rounded-lg ${
                bulkAction === 'approve'
                  ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                  : 'bg-[var(--danger)] text-white'
              }`}
            >
              تأكيد التنفيذ
            </button>
          </>
        }
      >
        <p className="text-xs sm:text-sm text-[var(--muted)] leading-relaxed">
          هل أنت متأكد من رغبتك في {bulkAction === 'approve' ? 'اعتماد' : 'حذف'}{' '}
          <strong className="num text-[var(--ink)]">{selectedIds.length}</strong> رد دفعة واحدة؟
        </p>
      </Dialog>
    </div>
  );
}
