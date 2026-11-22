'use client';

import React, { useState, useMemo, useEffect, useCallback } from 'react';
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
import type { Submission, Campaign, ResponseForm, ResponseFormColumn } from '../../lib/types';
import { Toolbar } from '../../components/ui/Toolbar';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { ErrorState } from '../../components/ui/EmptyState';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { bankLabel, cleanIban } from '../../lib/saudi-banks';
import { splitForExport } from '../../lib/export-split';
import { IbanDisplay } from '../../components/ui/IbanDisplay';
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
  const { can, canReview, isAdmin, user } = useAuth();
  const canViewIban = isAdmin || can('riders.iban.view');
  const sponsorIds = user?.financialScope?.sponsorIds;
  const noCompanyScope =
    !isAdmin &&
    !can('forms.responses.allCompanies') &&
    !(Array.isArray(sponsorIds) && sponsorIds.length > 0);
  const emptyScopeHint = 'لا توجد شركات ضمن نطاقك';

  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [campaignFilter, setCampaignFilter] = useState<string>('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [reviewTarget, setReviewTarget] = useState<Submission | null>(null);
  const [bulkAction, setBulkAction] = useState<'approve' | 'delete' | null>(null);
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [exportLoading, setExportLoading] = useState(false);

  // Full IBAN for review (Ghazal 2026-11-15): reviewers check the collected IBANs on screen and in the exported sheet.
  // The API already returns the full value to anyone allowed to list responses; nothing is unmasked for other roles.

  // Per-form table (Ghazal 2026-11-22): one campaign or builder form at a time, chosen at the top.
  // Its columns are built by looping over that form's questions from GET /api/responses/forms.
  const [forms, setForms] = useState<ResponseForm[]>([]);
  const [formsError, setFormsError] = useState<string | null>(null);
  const loadForms = useCallback(async () => {
    try {
      setForms(await api.getResponseForms());
      setFormsError(null);
    } catch (err: any) {
      setFormsError(err?.message || 'تعذر تحميل قائمة النماذج');
    }
  }, []);
  useEffect(() => {
    loadForms();
  }, [loadForms]);
  const refreshAll = () => {
    loadForms();
    onRefresh();
  };

  const selectedForm = useMemo(() => {
    if (forms.length === 0) return null;
    return (
      forms.find((f) => f.id === campaignFilter) ??
      forms.find((f) => f.responseCount > 0) ??
      forms[0]
    );
  }, [forms, campaignFilter]);
  const isCampaign = selectedForm?.source === 'campaign';
  const canActOnRows = canReview && isCampaign;

  // Start from the form named in the link (?form=), so a link opens the same table.
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const fromUrl = new URLSearchParams(window.location.search).get('form');
    if (fromUrl) setCampaignFilter(fromUrl);
  }, []);
  const chooseForm = (id: string) => {
    setCampaignFilter(id);
    setSelectedIds([]);
    setStatusFilter('all');
    if (typeof window !== 'undefined') {
      const url = new URL(window.location.href);
      url.searchParams.set('form', id);
      window.history.replaceState(null, '', url.toString());
    }
  };
  const formOptions = useMemo(
    () =>
      forms.map((f) => ({
        value: f.id,
        label: `${f.title || 'بدون عنوان'} (${f.responseCount.toLocaleString('en-US')})`,
      })),
    [forms]
  );
  const formRows = useMemo(
    () => (selectedForm ? submissions.filter((s) => s.campaignId === selectedForm.id) : []),
    [submissions, selectedForm]
  );

  const workSystemLabel = (s: Submission) =>
    s.workSystem === 'tiers'
      ? 'نظام الراتب الجديد'
      : s.workSystem === 'commission'
        ? 'نظام العمولة السابق'
        : s.workSystem === 'monthly'
          ? 'راتب شهري'
          : s.workSystem === 'per-order'
            ? 'بالطلب'
            : s.workSystem || '—';
  const statusLabel = (s: Submission) =>
    s.status === 'approved' ? 'معتمد' : s.status === 'rejected' ? 'مرفوض' : 'قيد المراجعة';

  const filteredSubmissions = useMemo(() => {
    return formRows.filter((s) => statusFilter === 'all' || s.status === statusFilter);
  }, [formRows, statusFilter]);

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
      refreshAll();
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
      const selectedRows = formRows.filter((s) => selectedIds.includes(s.id));
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
        const res = await api.bulkApproveSubmissions(selectedForm?.id, selectedIds);
        toast(`تم اعتماد ${res.count} رد بنجاح`, 'success');
      } else {
        const res = await api.bulkDeleteSubmissions(selectedForm?.id, selectedIds);
        toast(`تم حذف ${res.count} رد بنجاح`, 'warning');
      }
      setSelectedIds([]);
      setBulkAction(null);
      refreshAll();
    } catch (err: any) {
      toast(err?.message || 'تعذر تنفيذ الإجراء الجماعي', 'danger');
    } finally {
      setBulkSubmitting(false);
    }
  };

  // Answer of one question on a row (campaign rows carry fields at the top; builder answers are flattened by the API client).
  const answerOf = (s: Submission, id: string): unknown => (s as unknown as Record<string, unknown>)[id];
  const optionLabel = (col: ResponseFormColumn, v: unknown) => {
    const raw = v == null ? '' : String(v);
    return col.options?.find((o) => o.value === raw)?.labelAr || raw;
  };
  const ibanText = (s: Submission) => {
    const iban = cleanIban(String(answerOf(s, 'iban') ?? ''));
    if (!iban) return '';
    return canViewIban ? iban : `••••${iban.slice(-4)}`;
  };
  /** Plain text of a question's answer, used by filters, export and the default cell. */
  const answerText = (s: Submission, col: ResponseFormColumn): string => {
    const v = answerOf(s, col.id);
    if (col.type === 'workSystem') return s.workSystem ? workSystemLabel(s) : '';
    if (col.type === 'iban') return ibanText(s);
    if (Array.isArray(v)) return v.map((x) => optionLabel(col, x)).join('، ');
    if (col.type === 'list') return optionLabel(col, v);
    return v == null ? '' : String(v);
  };

  /** One table column per question of the selected form. */
  const questionColumns = (selectedForm?.columns ?? []).map((q): ColumnDef<Submission> => {
    const key = `q_${q.id}`;
    if (q.type === 'iban') {
      return {
        key,
        header: q.labelAr,
        type: 'text',
        filterType: 'text',
        getValue: (s) => `${ibanText(s)} ${answerOf(s, 'iban') ? bankLabel(s) : ''}`.trim(),
        exportColumns: canViewIban ? [{ header: 'الآيبان', value: (s) => ibanText(s) }] : [],
        render: (s) =>
          !answerOf(s, 'iban') ? (
            <span className="text-[var(--muted)]">—</span>
          ) : canViewIban ? (
            <IbanDisplay iban={String(s.iban)} bankCode={s.bankCode} bank={s.bank} />
          ) : (
            <span className="font-mono text-xs" dir="ltr">{ibanText(s)}</span>
          ),
      };
    }
    if (q.type === 'number') {
      return {
        key,
        header: q.labelAr,
        type: 'number',
        numeric: true,
        summary: 'sum',
        getValue: (s) => {
          const v = answerOf(s, q.id);
          return v === '' || v == null ? null : Number(v);
        },
        render: (s) => {
          const v = answerOf(s, q.id);
          return v === '' || v == null ? (
            <span className="text-[var(--muted)]">—</span>
          ) : (
            <span className="num">{Number(v).toLocaleString('en-US')}</span>
          );
        },
      };
    }
    if (q.type === 'date') {
      return {
        key,
        header: q.labelAr,
        type: 'date',
        getValue: (s) => answerText(s, q),
        render: (s) => <span className="num text-xs">{answerText(s, q) || '—'}</span>,
      };
    }
    const isList = q.type === 'list' || q.type === 'workSystem';
    const isId = q.type === 'phone';
    return {
      key,
      header: q.labelAr,
      type: isList ? 'list' : 'text',
      filterType: isList ? 'list' : 'text',
      getValue: (s) => answerText(s, q),
      render: (s) => {
        const text = answerText(s, q);
        if (!text) return <span className="text-[var(--muted)]">—</span>;
        return isId ? (
          <span className="num font-mono text-xs" dir="ltr">{text}</span>
        ) : (
          <span className={isList ? 'font-semibold text-xs' : 'text-xs'}>{text}</span>
        );
      },
    };
  });

  // Excel / CSV export: the same columns as the screen (one file per form).
  const handleExport = async (format: 'excel-all' | 'excel-filtered' | 'csv-filtered') => {
    if (!selectedForm) return;
    setExportLoading(true);
    const targetRows = format === 'excel-all' ? formRows : filteredSubmissions;
    const questions = selectedForm.columns.filter((q) => q.type !== 'iban' || canViewIban);
    const headers = [
      'اسم المندوب',
      'رقم الإقامة',
      ...questions.map((q) => q.labelAr),
      'الحالة',
      'ملاحظة المراجعة',
      'النسخة',
      'تاريخ الإرسال',
    ];

    // Formula injection guard matching Frontend/src/table-tools.ts:86
    const sanitize = (val: string) => {
      if (/^[=+\-@\t\r]/.test(val)) return "'" + val;
      return val;
    };

    const rows = targetRows.map((s) => [
      sanitize(s.name || ''),
      sanitize(s.iqama || ''),
      ...questions.map((q) => {
        if (q.type === 'number') {
          const v = answerOf(s, q.id);
          return v === '' || v == null ? '' : Number(v);
        }
        return sanitize(answerText(s, q));
      }),
      sanitize(statusLabel(s)),
      sanitize(s.reviewNote || ''),
      sanitize(String(s.version || 1)),
      sanitize(s.createdAt ? new Date(s.createdAt).toLocaleDateString('ar-SA') : '—'),
    ]);
    const numericColumns = questions
      .map((q, i) => (q.type === 'number' ? i + 2 : -1))
      .filter((i) => i >= 0);
    const baseName = (selectedForm.title || 'responses').replace(/[\\/:*?"<>|]+/g, '-').slice(0, 80);

    try {
      if (format === 'csv-filtered') {
        // Client-side CSV generation with UTF-8 BOM and formula guard; same column split as Excel.
        const table = splitForExport(headers, rows);
        const quote = (v: any) => `"${String(v).replace(/"/g, '""')}"`;
        const csvContent =
          '\uFEFF' +
          [table.headers.map(quote).join(','), ...table.rows.map((r) => r.map(quote).join(','))].join('\r\n');
        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${baseName}.csv`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تنزيل الملف النصي المفلتر بنجاح', 'success');
      } else {
        const blob = await api.exportExcel({
          headers,
          rows,
          numericColumns, // iqama, phone and IBAN stay text
          filename: `${baseName}.xlsx`,
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${baseName}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
        toast('تم تنزيل جدول البيانات بنجاح', 'success');
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
      type: 'text',
      filterType: 'text',
      getValue: (s) => s.name || '',
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
      type: 'text',
      filterType: 'text',
      getValue: (s) => s.iqama || '',
      render: (s) => <span className="num font-mono text-xs font-bold">{s.iqama}</span>,
    },
    ...questionColumns,
    {
      key: 'status',
      header: 'الحالة',
      type: 'list',
      filterType: 'list',
      getValue: (s) => s.status || 'pending',
      labelOf: (v) =>
        v === 'approved' ? 'معتمد' : v === 'rejected' ? 'مرفوض' : 'قيد المراجعة',
      toneOf: (v) =>
        v === 'approved' ? 'success' : v === 'rejected' ? 'danger' : 'warning',
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
      type: 'date',
      getValue: (s) => s.createdAt || '',
      labelOf: (v) => (v ? new Date(String(v)).toLocaleDateString('ar-SA') : '—'),
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
            count: formRows.length,
            onToggle: () => setStatusFilter('all'),
          },
          {
            id: 'pending',
            label: 'بانتظار المراجعة',
            active: statusFilter === 'pending',
            count: formRows.filter((s) => s.status === 'pending').length,
            onToggle: () => setStatusFilter('pending'),
          },
          {
            id: 'approved',
            label: 'معتمدة',
            active: statusFilter === 'approved',
            count: formRows.filter((s) => s.status === 'approved').length,
            onToggle: () => setStatusFilter('approved'),
          },
          {
            id: 'rejected',
            label: 'مرفوضة',
            active: statusFilter === 'rejected',
            count: formRows.filter((s) => s.status === 'rejected').length,
            onToggle: () => setStatusFilter('rejected'),
          },
        ]}
        onExport={handleExport}
        exportLoading={exportLoading}
        extraControls={
          <div className="flex items-center gap-2">
            {/* Form / campaign picker: the table shows one form at a time */}
            <SearchableSelect
              id="select-campaign-filter"
              value={selectedForm?.id ?? ''}
              onChange={chooseForm}
              ariaLabel="اختيار الحملة أو النموذج"
              placeholder="اختر حملة أو نموذجًا"
              className="w-72"
              options={formOptions}
            />

            {/* Bulk actions */}
            {canActOnRows && selectedIds.length > 0 && (
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
              onClick={refreshAll}
              className="p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)]"
              title="تحديث البيانات"
            >
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
            </button>
          </div>
        }
      />

      {selectedForm && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-[var(--muted)]">
          <strong className="text-sm text-[var(--ink)]">{selectedForm.title}</strong>
          <span className="px-2 py-0.5 rounded-full bg-[var(--raised)]">
            {selectedForm.source === 'custom'
              ? 'نموذج من المصمم'
              : selectedForm.kind === 'iban'
                ? 'حملة الآيبان'
                : 'حملة نظام العمل'}
          </span>
          <span className="px-2 py-0.5 rounded-full bg-[var(--raised)]">
            {selectedForm.status === 'open' ? 'مفتوح' : selectedForm.status === 'closed' ? 'مغلق' : selectedForm.status}
          </span>
          <span>
            <span className="num">{selectedForm.responseCount.toLocaleString('en-US')}</span> رد،{' '}
            <span className="num">{selectedForm.pendingCount.toLocaleString('en-US')}</span> بانتظار المراجعة
          </span>
          {!isCampaign && <span>ردود نماذج المصمم للعرض والتصدير فقط.</span>}
        </div>
      )}

      {formsError && !error ? (
        <ErrorState title="تعذر تحميل قائمة النماذج" message={formsError} onRetry={refreshAll} />
      ) : error ? (
        <ErrorState
          title={error.includes('404') ? 'خدمة مراجعة الردود قيد التجهيز' : 'تعذر تحميل الردود'}
          message={
            error.includes('404')
              ? 'خدمة مراجعة الردود قيد الإطلاق من الخادم. يمكنك الاستمرار في مراجعة الردود عبر الواجهة السابقة.'
              : error
          }
          onRetry={refreshAll}
        />
      ) : (
        <DataTable
          key={selectedForm?.id ?? 'none'}
          columns={columns}
          data={filteredSubmissions}
          loading={loading}
          exportable={false}
          selectable={canActOnRows}
          selectedIds={selectedIds}
          onSelectionChange={setSelectedIds}
          getRowId={(r) => r.id}
          emptyTitle={
            noCompanyScope
              ? emptyScopeHint
              : !selectedForm
                ? 'لا توجد حملات أو نماذج بعد'
                : 'لا توجد ردود مسجلة'
          }
          emptyDescription={
            noCompanyScope
              ? emptyScopeHint
              : 'لم تصل ردود جديدة تطابق خيارات التصفية المحددة.'
          }
          rowActions={isCampaign ? [
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
          ] : undefined}
        />
      )}

      {/* Review Drawer */}
      <ReviewDrawer
        submission={reviewTarget}
        onClose={() => setReviewTarget(null)}
        onSuccess={refreshAll}
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
