'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import {
  ClipboardList,
  Plus,
  Link as LinkIcon,
  QrCode,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Trash2,
  ExternalLink,
  Copy,
  Clock,
  Eye,
  Check,
  X,
  FileSpreadsheet,
  Download,
  Filter,
  Layers,
  Sparkles,
  BarChart3,
  Calendar,
  Building,
  User,
  ShieldCheck,
  RefreshCw,
} from 'lucide-react';
import { api } from '../../lib/api';
import type {
  Campaign,
  Submission,
  CustomForm,
  CustomFormQuestion,
} from '../../lib/types';
import { PageHeader } from '../../components/ui/PageHeader';
import { Tabs } from '../../components/ui/Tabs';
import { Toolbar } from '../../components/ui/Toolbar';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { Dialog, Drawer } from '../../components/ui/Dialog';
import { StatCard } from '../../components/ui/StatCard';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';

export default function FormsPage() {
  return (
    <Suspense fallback={<Skeleton rows={6} className="h-16" />}>
      <FormsWorkspace />
    </Suspense>
  );
}

function FormsWorkspace() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { toast } = useToast();

  const activeTab = searchParams?.get('tab') || 'campaigns';

  // State: Campaigns
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignsLoading, setCampaignsLoading] = useState(false);
  const [campaignsError, setCampaignsError] = useState<string | null>(null);
  const [createCampaignOpen, setCreateCampaignOpen] = useState(false);
  const [deleteCampaignTarget, setDeleteCampaignTarget] = useState<Campaign | null>(null);
  const [qrCampaignTarget, setQrCampaignTarget] = useState<Campaign | null>(null);

  // State: Submissions / Responses
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [submissionsLoading, setSubmissionsLoading] = useState(false);
  const [submissionsError, setSubmissionsError] = useState<string | null>(null);
  const [selectedSubmissionIds, setSelectedSubmissionIds] = useState<string[]>([]);
  const [responseStatusFilter, setResponseStatusFilter] = useState<string>('all');
  const [responseCampaignFilter, setResponseCampaignFilter] = useState<string>('');
  const [reviewDrawerTarget, setReviewDrawerTarget] = useState<Submission | null>(null);
  const [reviewNote, setReviewNote] = useState('');
  const [bulkActionTarget, setBulkActionTarget] = useState<'approve' | 'delete' | null>(null);

  // State: Custom Forms Builder
  const [customForms, setCustomForms] = useState<CustomForm[]>([]);
  const [formsLoading, setFormsLoading] = useState(false);
  const [formsError, setFormsError] = useState<string | null>(null);
  const [createFormOpen, setCreateFormOpen] = useState(false);

  // Load data based on tab
  useEffect(() => {
    if (activeTab === 'campaigns' || activeTab === 'analytics' || activeTab === 'responses') {
      loadCampaigns();
    }
    if (activeTab === 'responses' || activeTab === 'analytics') {
      loadSubmissions();
    }
    if (activeTab === 'builder') {
      loadCustomForms();
    }
  }, [activeTab]);

  const loadCampaigns = async () => {
    setCampaignsLoading(true);
    setCampaignsError(null);
    try {
      const data = await api.getCampaigns();
      setCampaigns(data);
    } catch (err: any) {
      setCampaignsError(err?.message || 'تعذر تحميل الحملات');
    } finally {
      setCampaignsLoading(false);
    }
  };

  const loadSubmissions = async () => {
    setSubmissionsLoading(true);
    setSubmissionsError(null);
    try {
      const data = await api.getSubmissions({
        campaignId: responseCampaignFilter || undefined,
        status: responseStatusFilter !== 'all' ? responseStatusFilter : undefined,
      });
      setSubmissions(data);
    } catch (err: any) {
      setSubmissionsError(err?.message || 'تعذر تحميل الردود');
    } finally {
      setSubmissionsLoading(false);
    }
  };

  const loadCustomForms = async () => {
    setFormsLoading(true);
    setFormsError(null);
    try {
      const data = await api.getCustomForms();
      setCustomForms(data);
    } catch (err: any) {
      setFormsError(err?.message || 'تعذر تحميل مصمم النماذج');
    } finally {
      setFormsLoading(false);
    }
  };

  const copyToClipboard = (text: string, label: string) => {
    if (navigator.clipboard) {
      navigator.clipboard.writeText(text);
      toast(`تم نسخ رابط ${label} إلى الحافظة`, 'success');
    }
  };

  // Helper for IBAN masking: SA** **** **** **** **1234
  const maskIban = (iban?: string) => {
    if (!iban) return '—';
    const clean = iban.replace(/\s+/g, '');
    if (clean.length < 10) return clean;
    const prefix = clean.slice(0, 4);
    const suffix = clean.slice(-4);
    return `${prefix} **** **** **** ${suffix}`;
  };

  // -------------------------------------------------------------
  // TAB 1: Campaigns & Links
  // -------------------------------------------------------------
  const [newCampaignTitle, setNewCampaignTitle] = useState('');
  const [newCampaignKind, setNewCampaignKind] = useState<'work-choice' | 'iban'>('iban');
  const [newCampaignDays, setNewCampaignDays] = useState(14);

  const handleCreateCampaign = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCampaignTitle.trim()) return;

    try {
      await api.createCampaign({
        title: newCampaignTitle.trim(),
        kind: newCampaignKind,
        days: newCampaignDays,
      });
      toast('تم إنشاء الحملة بنجاح', 'success');
      setCreateCampaignOpen(false);
      setNewCampaignTitle('');
      loadCampaigns();
    } catch (err: any) {
      toast(err?.message || 'تعذر إنشاء الحملة', 'danger');
    }
  };

  const handleCloseCampaign = async (id: string) => {
    try {
      await api.closeCampaign(id);
      toast('تم إغلاق الحملة وتوقيف استقبال الردود', 'warning');
      loadCampaigns();
    } catch (err: any) {
      toast(err?.message || 'تعذر إغلاق الحملة', 'danger');
    }
  };

  const handleDeleteCampaign = async () => {
    if (!deleteCampaignTarget) return;
    try {
      await api.deleteCampaign(deleteCampaignTarget.id);
      toast('تم حذف الحملة بنجاح', 'success');
      setDeleteCampaignTarget(null);
      loadCampaigns();
    } catch (err: any) {
      toast(err?.message || 'تعذر حذف الحملة', 'danger');
    }
  };

  const campaignColumns: ColumnDef<Campaign>[] = [
    {
      key: 'title',
      header: 'عنوان الحملة',
      render: (c) => (
        <div className="flex flex-col">
          <strong className="text-[var(--ink)] font-bold">{c.title}</strong>
          <span className="text-[11px] text-[var(--muted)] font-mono">{c.path}</span>
        </div>
      ),
    },
    {
      key: 'kind',
      header: 'النوع',
      render: (c) => (
        <span
          className={`px-2 py-0.5 rounded-full text-[11px] font-semibold ${
            c.kind === 'iban'
              ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
              : 'bg-[var(--info)]/15 text-[var(--info)]'
          }`}
        >
          {c.kind === 'iban' ? 'تحديث الآيبان' : 'اختيار نظام العمل'}
        </span>
      ),
    },
    {
      key: 'status',
      header: 'الحالة',
      render: (c) => (
        <span
          className={`px-2.5 py-0.5 rounded-full text-xs font-semibold ${
            c.status === 'open'
              ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
              : 'bg-[var(--muted)]/20 text-[var(--muted)]'
          }`}
        >
          {c.status === 'open' ? 'مفتوحة للاستقبال' : 'مغلقة'}
        </span>
      ),
    },
    {
      key: 'responseCount',
      header: 'عدد الردود',
      numeric: true,
      render: (c) => (
        <span className="num font-bold text-[var(--ink)]">{c.responseCount}</span>
      ),
    },
    {
      key: 'expiresAt',
      header: 'تاريخ الانتهاء',
      render: (c) => (
        <span className="num text-xs text-[var(--muted)]">
          {new Date(c.expiresAt).toLocaleDateString('ar-SA')}
        </span>
      ),
    },
  ];

  // -------------------------------------------------------------
  // TAB 3: Responses Inbox
  // -------------------------------------------------------------
  const filteredSubmissions = useMemo(() => {
    return submissions.filter((s) => {
      if (responseStatusFilter !== 'all' && s.status !== responseStatusFilter) return false;
      if (responseCampaignFilter && s.campaignId !== responseCampaignFilter) return false;
      return true;
    });
  }, [submissions, responseStatusFilter, responseCampaignFilter]);

  const responseColumns: ColumnDef<Submission>[] = [
    {
      key: 'name',
      header: 'اسم المندوب',
      render: (s) => (
        <div className="flex flex-col">
          <strong className="font-bold text-[var(--ink)]">{s.name}</strong>
          {s.isEdited && (
            <span className="text-[10px] text-[var(--warning)] font-semibold">
              معدل (نسخة {s.version})
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
            <span className="num font-mono text-xs text-[var(--accent)] font-semibold">
              {maskIban(s.iban)}
            </span>
            <span className="text-[11px] text-[var(--muted)]">{s.bank || 'مصرف الراجحي'}</span>
          </div>
        ) : (
          <span className="font-semibold text-xs">
            {s.workSystem === 'monthly' ? 'راتب شهري' : s.workSystem === 'per-order' ? 'بالطلب' : s.workSystem}
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
      header: 'حالة الرد',
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

  const handleReviewSubmission = async (status: 'approved' | 'rejected') => {
    if (!reviewDrawerTarget) return;
    try {
      await api.reviewSubmission(reviewDrawerTarget.id, {
        status,
        note: reviewNote,
        version: reviewDrawerTarget.version,
      });
      toast(status === 'approved' ? 'تم اعتماد الرد بنجاح' : 'تم رفض الرد', 'success');
      setReviewDrawerTarget(null);
      setReviewNote('');
      loadSubmissions();
    } catch (err: any) {
      toast(err?.message || 'تعذر مراجعة الرد', 'danger');
    }
  };

  const handleBulkAction = async () => {
    if (!bulkActionTarget || selectedSubmissionIds.length === 0) return;
    try {
      if (bulkActionTarget === 'approve') {
        await api.bulkApproveSubmissions(
          responseCampaignFilter || undefined,
          selectedSubmissionIds
        );
        toast(`تم اعتماد ${selectedSubmissionIds.length} رد بنجاح`, 'success');
      } else {
        await api.bulkDeleteSubmissions(
          responseCampaignFilter || undefined,
          selectedSubmissionIds
        );
        toast(`تم حذف ${selectedSubmissionIds.length} رد`, 'warning');
      }
      setSelectedSubmissionIds([]);
      setBulkActionTarget(null);
      loadSubmissions();
    } catch (err: any) {
      toast(err?.message || 'تعذر تنفيذ الإجراء الجماعي', 'danger');
    }
  };

  // -------------------------------------------------------------
  // TAB 2: Form Builder
  // -------------------------------------------------------------
  const [newFormTitle, setNewFormTitle] = useState('');
  const [questions, setQuestions] = useState<CustomFormQuestion[]>([
    { id: 'q1', label: 'الاسم الكامل', type: 'text', required: true },
    { id: 'q2', label: 'رقم الإقامة', type: 'text', required: true },
    {
      id: 'q3',
      label: 'المدينة الحالية',
      type: 'choice',
      required: true,
      options: ['الرياض', 'جدة', 'الدمام'],
    },
  ]);

  const handleCreateCustomForm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newFormTitle.trim()) return;

    try {
      await api.createCustomForm({
        title: newFormTitle.trim(),
        questions,
      });
      toast('تم حفظ النموذج الجديد كمسودة', 'success');
      setCreateFormOpen(false);
      setNewFormTitle('');
      loadCustomForms();
    } catch (err: any) {
      toast(err?.message || 'تعذر حفظ النموذج', 'danger');
    }
  };

  const handlePublishCustomForm = async (id: string) => {
    try {
      await api.publishCustomForm(id);
      toast('تم نشر النموذج وبدء استقبال الاستبيانات', 'success');
      loadCustomForms();
    } catch (err: any) {
      toast(err?.message || 'تعذر نشر النموذج', 'danger');
    }
  };

  // -------------------------------------------------------------
  // Excel Export Handler with formula-injection guard
  // -------------------------------------------------------------
  const handleExportResponses = async (type: 'excel-all' | 'excel-filtered' | 'csv-filtered') => {
    const list = type === 'excel-all' ? submissions : filteredSubmissions;
    const headers = ['اسم المندوب', 'الإقامة', 'الآيبان', 'البنك', 'النظام', 'الحملة', 'الحالة', 'التاريخ'];

    // Formula injection guard matching Frontend/src/table-tools.ts:86
    const sanitize = (val: string) => {
      if (/^[=+\-@\t\r]/.test(val)) return "'" + val;
      return val;
    };

    const rows = list.map((s) => [
      sanitize(s.name),
      sanitize(s.iqama),
      sanitize(s.iban || '—'),
      sanitize(s.bank || '—'),
      sanitize(s.workSystem || '—'),
      sanitize(s.campaignTitle || s.campaignId),
      sanitize(s.status),
      sanitize(s.createdAt),
    ]);

    try {
      toast('جاري تجهيز وتنزيل الملف...', 'info');
      const blob = await api.exportExcel({
        kind: 'responses',
        columns: headers,
        rows,
        filename: `responses-${type}.xlsx`,
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `responses-${type}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err: any) {
      toast(err?.message || 'تعذر تصدير الملف', 'danger');
    }
  };

  // Tabs Definition
  const workspaceTabs = [
    { id: 'campaigns', label: 'الحملات والروابط', badge: campaigns.length },
    { id: 'builder', label: 'مصمم النماذج', badge: customForms.length },
    {
      id: 'responses',
      label: 'مراجعة الردود',
      badge: submissions.filter((s) => s.status === 'pending').length || undefined,
    },
    { id: 'analytics', label: 'التحليلات' },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="النماذج والردود"
        eyebrow="مساحة الإدارة 1.0"
        description="إدارة حملات استلام الآيبان وأنظمة العمل، مصمم النماذج المخصصة، وصندوق موحد لمراجعة واعتماد ردود المناديب."
        primaryAction={
          activeTab === 'campaigns'
            ? {
                label: 'إنشاء حملة جديدة',
                onClick: () => setCreateCampaignOpen(true),
                icon: <Plus size={16} />,
              }
            : activeTab === 'builder'
            ? {
                label: 'تصميم نموذج جديد',
                onClick: () => setCreateFormOpen(true),
                icon: <Plus size={16} />,
              }
            : undefined
        }
      />

      {/* URL Tabs */}
      <Tabs
        tabs={workspaceTabs}
        activeTab={activeTab}
        onChange={(tabId) => {
          const params = new URLSearchParams(searchParams?.toString() || '');
          params.set('tab', tabId);
          router.push(`/app/forms/?${params.toString()}`);
        }}
      />

      {/* TAB 1: Campaigns */}
      {activeTab === 'campaigns' && (
        <div className="space-y-4">
          <Toolbar
            searchPlaceholder="بحث في الحملات والروابط..."
            extraControls={
              <button
                type="button"
                onClick={loadCampaigns}
                className="p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)]"
                title="تحديث"
              >
                <RefreshCw size={14} className={campaignsLoading ? 'animate-spin' : ''} />
              </button>
            }
          />

          {campaignsError ? (
            <ErrorState
              title="تعذر تحميل الحملات"
              message={campaignsError}
              onRetry={loadCampaigns}
            />
          ) : (
            <DataTable
              columns={campaignColumns}
              data={campaigns}
              loading={campaignsLoading}
              emptyTitle="لا توجد حملات نشطة حاليًا"
              emptyDescription="قم بإنشاء حملة جديدة لمشاركة الرابط مع المناديب واستقبال بيانات الآيبان أو نظام العمل."
              rowActions={[
                {
                  label: 'نسخ رابط النموذج',
                  onClick: (c) =>
                    copyToClipboard(
                      `${window.location.origin}${c.path}`,
                      c.title
                    ),
                  icon: <Copy size={14} />,
                },
                {
                  label: 'عرض رمز QR',
                  onClick: (c) => setQrCampaignTarget(c),
                  icon: <QrCode size={14} />,
                },
                {
                  label: 'إغلاق الاستقبال',
                  disabled: (c) => c.status === 'closed',
                  onClick: (c) => handleCloseCampaign(c.id),
                  icon: <XCircle size={14} />,
                },
                {
                  label: 'حذف الحملة',
                  danger: true,
                  onClick: (c) => setDeleteCampaignTarget(c),
                  icon: <Trash2 size={14} />,
                },
              ]}
            />
          )}
        </div>
      )}

      {/* TAB 2: Form Builder */}
      {activeTab === 'builder' && (
        <div className="space-y-4">
          <Toolbar searchPlaceholder="بحث في النماذج المصممة..." />

          {formsError ? (
            <ErrorState
              title="تعذر تحميل النماذج"
              message={formsError}
              onRetry={loadCustomForms}
            />
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {customForms.map((form) => (
                <div
                  key={form.id}
                  className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 flex flex-col justify-between shadow-xs"
                >
                  <div>
                    <div className="flex items-center justify-between gap-2 mb-2">
                      <span
                        className={`px-2 py-0.5 rounded-full text-[10px] font-semibold ${
                          form.status === 'published'
                            ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                            : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                        }`}
                      >
                        {form.status === 'published' ? 'منشور ونشط' : 'مسودة'}
                      </span>
                      <span className="num text-xs text-[var(--muted)]">
                        {form.questions.length} أسئلة
                      </span>
                    </div>

                    <h3 className="text-base font-bold text-[var(--ink)] mb-1">
                      {form.title}
                    </h3>
                    <p className="text-xs text-[var(--muted)] mb-4">
                      إجمالي الردود المستلمة: <strong className="num text-[var(--ink)]">{form.responsesCount}</strong>
                    </p>
                  </div>

                  <div className="flex items-center justify-between gap-2 pt-3 border-t border-[var(--line)]">
                    {form.status === 'draft' ? (
                      <button
                        type="button"
                        onClick={() => handlePublishCustomForm(form.id)}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 transition"
                      >
                        <Sparkles size={13} />
                        <span>نشر النموذج</span>
                      </button>
                    ) : (
                      <span className="text-xs text-[var(--money-pos)] font-semibold flex items-center gap-1">
                        <CheckCircle2 size={14} />
                        جاهز للاستقبال
                      </span>
                    )}

                    <button
                      type="button"
                      onClick={() =>
                        copyToClipboard(
                          `${window.location.origin}/app/forms/${form.id}`,
                          form.title
                        )
                      }
                      className="p-1.5 rounded-lg border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]"
                      title="نسخ الرابط"
                    >
                      <Copy size={14} />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* TAB 3: Responses Inbox */}
      {activeTab === 'responses' && (
        <div className="space-y-4">
          <Toolbar
            searchPlaceholder="بحث في الردود برقم الهوية أو الاسم..."
            filterChips={[
              {
                id: 'all',
                label: 'كل الردود',
                active: responseStatusFilter === 'all',
                count: submissions.length,
                onToggle: () => setResponseStatusFilter('all'),
              },
              {
                id: 'pending',
                label: 'بانتظار المراجعة',
                active: responseStatusFilter === 'pending',
                count: submissions.filter((s) => s.status === 'pending').length,
                onToggle: () => setResponseStatusFilter('pending'),
              },
              {
                id: 'approved',
                label: 'معتمدة',
                active: responseStatusFilter === 'approved',
                count: submissions.filter((s) => s.status === 'approved').length,
                onToggle: () => setResponseStatusFilter('approved'),
              },
              {
                id: 'rejected',
                label: 'مرفوضة',
                active: responseStatusFilter === 'rejected',
                count: submissions.filter((s) => s.status === 'rejected').length,
                onToggle: () => setResponseStatusFilter('rejected'),
              },
            ]}
            onExport={(format) => handleExportResponses(format)}
            extraControls={
              <div className="flex items-center gap-2">
                {/* Campaign filter */}
                <select
                  value={responseCampaignFilter}
                  onChange={(e) => setResponseCampaignFilter(e.target.value)}
                  className="text-xs font-semibold px-2.5 py-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden"
                >
                  <option value="">كل الحملات</option>
                  {campaigns.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.title}
                    </option>
                  ))}
                </select>

                {/* Bulk Actions if items selected */}
                {selectedSubmissionIds.length > 0 && (
                  <div className="flex items-center gap-1.5 bg-[var(--row-selected)] px-2.5 py-1 rounded-lg border border-[var(--accent)]">
                    <span className="text-xs font-bold text-[var(--accent)] num">
                      {selectedSubmissionIds.length} محدد
                    </span>
                    <button
                      type="button"
                      onClick={() => setBulkActionTarget('approve')}
                      className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded bg-[var(--accent)] text-[var(--accent-ink)]"
                    >
                      <Check size={12} />
                      اعتماد
                    </button>
                    <button
                      type="button"
                      onClick={() => setBulkActionTarget('delete')}
                      className="inline-flex items-center gap-1 px-2 py-0.5 text-xs font-bold rounded bg-[var(--danger)] text-white"
                    >
                      <Trash2 size={12} />
                      حذف
                    </button>
                  </div>
                )}
              </div>
            }
          />

          {submissionsError ? (
            <ErrorState
              title="تعذر تحميل الردود"
              message={submissionsError}
              onRetry={loadSubmissions}
            />
          ) : (
            <DataTable
              columns={responseColumns}
              data={filteredSubmissions}
              loading={submissionsLoading}
              selectable
              selectedIds={selectedSubmissionIds}
              onSelectionChange={setSelectedSubmissionIds}
              getRowId={(r) => r.id}
              emptyTitle="لا توجد ردود مسجلة"
              emptyDescription="لم تصل ردود جديدة تطابق خيارات التصفية المحددة."
              rowActions={[
                {
                  label: 'معاينة ومراجعة',
                  onClick: (s) => setReviewDrawerTarget(s),
                  icon: <Eye size={14} />,
                },
                {
                  label: 'اعتماد فوري',
                  disabled: (s) => s.status === 'approved',
                  onClick: async (s) => {
                    try {
                      await api.reviewSubmission(s.id, {
                        status: 'approved',
                        version: s.version,
                      });
                      toast('تم اعتماد الرد بنجاح', 'success');
                      loadSubmissions();
                    } catch (err: any) {
                      toast(err?.message || 'تعذر الاعتماد', 'danger');
                    }
                  },
                  icon: <Check size={14} />,
                },
                {
                  label: 'رفض الرد',
                  disabled: (s) => s.status === 'rejected',
                  danger: true,
                  onClick: async (s) => {
                    try {
                      await api.reviewSubmission(s.id, {
                        status: 'rejected',
                        version: s.version,
                      });
                      toast('تم رفض الرد', 'warning');
                      loadSubmissions();
                    } catch (err: any) {
                      toast(err?.message || 'تعذر الرفض', 'danger');
                    }
                  },
                  icon: <X size={14} />,
                },
              ]}
            />
          )}
        </div>
      )}

      {/* TAB 4: Analytics */}
      {activeTab === 'analytics' && (
        <div className="space-y-6">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              label="إجمالي الردود المستلمة"
              value={submissions.length}
              hint="عبر كل الحملات المنشورة"
              icon={BarChart3}
            />
            <StatCard
              label="نسبة الاعتماد"
              value={
                submissions.length > 0
                  ? `${Math.round(
                      (submissions.filter((s) => s.status === 'approved').length /
                        submissions.length) *
                        100
                    )}%`
                  : '0%'
              }
              hint="ردود تم توثيقها بنجاح"
              icon={CheckCircle2}
              trend={{ value: 'مستقر', isPositive: true }}
            />
            <StatCard
              label="حملات نشطة"
              value={campaigns.filter((c) => c.status === 'open').length}
              hint="مفتوحة لاستقبال الإدخال"
              icon={Layers}
            />
            <StatCard
              label="متوسط زمن المراجعة"
              value="2.4 ساعة"
              hint="من وقت الإرسال حتى الاعتماد"
              icon={Clock}
            />
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
              <h3 className="text-base font-bold text-[var(--ink)] mb-3">
                توزيع الردود حسب نظام العمل
              </h3>
              <div className="space-y-3">
                <div className="flex items-center justify-between text-xs">
                  <span>راتب شهري ثابت</span>
                  <span className="num font-bold">58%</span>
                </div>
                <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                  <div className="bg-[var(--accent)] h-full w-[58%]" />
                </div>

                <div className="flex items-center justify-between text-xs">
                  <span>محاسبة بالطلب / عمولة</span>
                  <span className="num font-bold">42%</span>
                </div>
                <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                  <div className="bg-[var(--info)] h-full w-[42%]" />
                </div>
              </div>
            </div>

            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5">
              <h3 className="text-base font-bold text-[var(--ink)] mb-3">
                توزيع الردود حسب المدن
              </h3>
              <div className="space-y-3">
                <div className="flex items-center justify-between text-xs">
                  <span>الرياض</span>
                  <span className="num font-bold">64%</span>
                </div>
                <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                  <div className="bg-[var(--money-pos)] h-full w-[64%]" />
                </div>

                <div className="flex items-center justify-between text-xs">
                  <span>جدة والمنطقة الغربية</span>
                  <span className="num font-bold">24%</span>
                </div>
                <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                  <div className="bg-[var(--warning)] h-full w-[24%]" />
                </div>

                <div className="flex items-center justify-between text-xs">
                  <span>الدمام والمنطقة الشرقية</span>
                  <span className="num font-bold">12%</span>
                </div>
                <div className="w-full bg-[var(--raised)] h-2 rounded-full overflow-hidden">
                  <div className="bg-[var(--muted)] h-full w-[12%]" />
                </div>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ------------------------------------------------------------- */}
      {/* DIALOG: Create Campaign */}
      {/* ------------------------------------------------------------- */}
      <Dialog
        open={createCampaignOpen}
        onClose={() => setCreateCampaignOpen(false)}
        title="إنشاء حملة نماذج واستبيانات جديدة"
        description="أنشئ رابطًا عامًا آمنًا لمشاركته مع المناديب لجمع اختياراتهم أو بيانات الآيبان."
        actions={
          <>
            <button
              type="button"
              onClick={() => setCreateCampaignOpen(false)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              onClick={handleCreateCampaign}
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90"
            >
              إنشاء الحملة وتوليد الرابط
            </button>
          </>
        }
      >
        <form onSubmit={handleCreateCampaign} className="space-y-4">
          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              عنوان الحملة
            </label>
            <input
              type="text"
              required
              value={newCampaignTitle}
              onChange={(e) => setNewCampaignTitle(e.target.value)}
              placeholder="مثال: تحديث الآيبان البنكي — أكتوبر 2026"
              className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
            />
          </div>

          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              نوع النموذج المطلوب
            </label>
            <div className="grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => setNewCampaignKind('iban')}
                className={`p-3 rounded-xl border text-start transition ${
                  newCampaignKind === 'iban'
                    ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <div className="text-xs font-bold">تحديث الآيبان (IBAN)</div>
                <div className="text-[11px] font-normal opacity-80 mt-0.5">
                  جمع الحساب البنكي والتحقق التلقائي
                </div>
              </button>

              <button
                type="button"
                onClick={() => setNewCampaignKind('work-choice')}
                className={`p-3 rounded-xl border text-start transition ${
                  newCampaignKind === 'work-choice'
                    ? 'border-[var(--accent)] bg-[var(--row-selected)] text-[var(--accent)] font-bold'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <div className="text-xs font-bold">اختيار نظام العمل</div>
                <div className="text-[11px] font-normal opacity-80 mt-0.5">
                  راتب شهري أو بالطلب وتحديد المدينة
                </div>
              </button>
            </div>
          </div>

          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">
              فترة صلاحية الرابط (أيام)
            </label>
            <input
              type="number"
              min={1}
              max={90}
              value={newCampaignDays}
              onChange={(e) => setNewCampaignDays(Number(e.target.value))}
              className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] num"
            />
          </div>
        </form>
      </Dialog>

      {/* ------------------------------------------------------------- */}
      {/* DIALOG: Delete Campaign Preview Confirmation */}
      {/* ------------------------------------------------------------- */}
      <Dialog
        open={!!deleteCampaignTarget}
        onClose={() => setDeleteCampaignTarget(null)}
        title="تأكيد حذف الحملة"
        description="يرجى مراجعة المعاينة قبل الحذف النهائي لتجنب فقدان البيانات."
        actions={
          <>
            <button
              type="button"
              onClick={() => setDeleteCampaignTarget(null)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              تراجع
            </button>
            <button
              type="button"
              onClick={handleDeleteCampaign}
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--danger)] text-white hover:opacity-90"
            >
              تأكيد حذف الحملة وكل ردودها
            </button>
          </>
        }
      >
        {deleteCampaignTarget && (
          <div className="space-y-3">
            <div className="p-3.5 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/20 text-xs text-[var(--danger)]">
              تحذير: سيتم حذف الحملة «{deleteCampaignTarget.title}» وجميع الردود المرتبطة بها ({deleteCampaignTarget.responseCount} رد).
            </div>
            <div className="text-xs text-[var(--muted)] leading-relaxed">
              إذا كانت هناك ردود معتمدة تم ترحيلها ماليًا، فلن تتأثر كشوف الحسابات المعتمدة، ولكن ستُزال سجلات الاستبيان من قائمة الحملات.
            </div>
          </div>
        )}
      </Dialog>

      {/* ------------------------------------------------------------- */}
      {/* DIALOG: QR Code */}
      {/* ------------------------------------------------------------- */}
      <Dialog
        open={!!qrCampaignTarget}
        onClose={() => setQrCampaignTarget(null)}
        title={`رمز QR: ${qrCampaignTarget?.title || ''}`}
        actions={
          <button
            type="button"
            onClick={() => setQrCampaignTarget(null)}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)]"
          >
            إغلاق
          </button>
        }
      >
        <div className="flex flex-col items-center justify-center p-6 text-center space-y-3">
          <div className="h-44 w-44 rounded-2xl border-2 border-[var(--line)] bg-white p-3 flex items-center justify-center shadow-md">
            {/* Synthetic SVG QR code */}
            <svg viewBox="0 0 100 100" className="w-full h-full text-black fill-current">
              <rect x="10" y="10" width="25" height="25" fill="black" />
              <rect x="15" y="15" width="15" height="15" fill="white" />
              <rect x="18" y="18" width="9" height="9" fill="black" />
              <rect x="65" y="10" width="25" height="25" fill="black" />
              <rect x="70" y="15" width="15" height="15" fill="white" />
              <rect x="73" y="18" width="9" height="9" fill="black" />
              <rect x="10" y="65" width="25" height="25" fill="black" />
              <rect x="15" y="70" width="15" height="15" fill="white" />
              <rect x="18" y="73" width="9" height="9" fill="black" />
              <rect x="42" y="15" width="8" height="8" fill="black" />
              <rect x="42" y="35" width="8" height="18" fill="black" />
              <rect x="65" y="45" width="18" height="8" fill="black" />
              <rect x="42" y="65" width="18" height="18" fill="black" />
              <rect x="70" y="70" width="15" height="15" fill="black" />
            </svg>
          </div>
          <p className="text-xs text-[var(--muted)]">
            امسح الرمز بكاميرا الجوال للانتقال لصفحة النموذج مباشرة
          </p>
          <button
            type="button"
            onClick={() =>
              copyToClipboard(
                `${window.location.origin}${qrCampaignTarget?.path}`,
                qrCampaignTarget?.title || ''
              )
            }
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--ink)]"
          >
            <Copy size={13} />
            <span>نسخ الرابط المباشر</span>
          </button>
        </div>
      </Dialog>

      {/* ------------------------------------------------------------- */}
      {/* DRAWER: Submission Review */}
      {/* ------------------------------------------------------------- */}
      <Drawer
        open={!!reviewDrawerTarget}
        onClose={() => setReviewDrawerTarget(null)}
        title="مراجعة وتدقيق الرد"
        subtitle={`مندوب: ${reviewDrawerTarget?.name || ''} — هوية: ${reviewDrawerTarget?.iqama || ''}`}
        footer={
          <div className="flex items-center justify-between gap-3">
            <button
              type="button"
              onClick={() => handleReviewSubmission('rejected')}
              className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-bold rounded-xl bg-[var(--danger)] text-white hover:opacity-90"
            >
              <X size={15} />
              <span>رفض الرد</span>
            </button>
            <button
              type="button"
              onClick={() => handleReviewSubmission('approved')}
              className="flex-1 inline-flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 shadow-xs"
            >
              <Check size={15} />
              <span>اعتماد رسمي</span>
            </button>
          </div>
        }
      >
        {reviewDrawerTarget && (
          <div className="space-y-5 text-xs sm:text-sm">
            {/* Identity & Basic details */}
            <div className="p-4 rounded-xl bg-[var(--raised)] border border-[var(--line)] space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">اسم المندوب:</span>
                <strong className="text-[var(--ink)]">{reviewDrawerTarget.name}</strong>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-[var(--muted)]">رقم الإقامة:</span>
                <strong className="num font-mono text-[var(--ink)]">{reviewDrawerTarget.iqama}</strong>
              </div>
              {reviewDrawerTarget.phone && (
                <div className="flex items-center justify-between">
                  <span className="text-[var(--muted)]">رقم الجوال:</span>
                  <strong className="num text-[var(--ink)]">{reviewDrawerTarget.phone}</strong>
                </div>
              )}
              {reviewDrawerTarget.city && (
                <div className="flex items-center justify-between">
                  <span className="text-[var(--muted)]">المدينة:</span>
                  <strong className="text-[var(--ink)]">{reviewDrawerTarget.city}</strong>
                </div>
              )}
            </div>

            {/* Unmasked IBAN & Banking info */}
            {reviewDrawerTarget.iban && (
              <div className="p-4 rounded-xl border border-[var(--accent)]/30 bg-[var(--accent)]/5 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-bold text-[var(--accent)]">الآيبان البنكي (معروض بالكامل للمراجعة):</span>
                  <span className="text-[10px] font-semibold px-2 py-0.5 rounded bg-[var(--accent)] text-[var(--accent-ink)]">
                    موثق
                  </span>
                </div>
                <div className="num font-mono font-bold text-sm tracking-wide text-[var(--ink)] select-all bg-[var(--surface)] p-2.5 rounded-lg border border-[var(--line)] text-center">
                  {reviewDrawerTarget.iban}
                </div>
                <div className="flex items-center justify-between text-xs text-[var(--muted)] pt-1">
                  <span>اسم البنك:</span>
                  <strong className="text-[var(--ink)]">{reviewDrawerTarget.bank || 'مصرف الراجحي'}</strong>
                </div>
              </div>
            )}

            {/* Work choice info */}
            {reviewDrawerTarget.workSystem && (
              <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
                <span className="text-xs font-bold text-[var(--ink)]">نظام العمل المختار:</span>
                <div className="text-xs font-semibold text-[var(--accent)]">
                  {reviewDrawerTarget.workSystem === 'monthly' ? 'راتب شهري ثابت' : 'محاسبة بالطلب (عمولة)'}
                </div>
              </div>
            )}

            {/* Review Note Input */}
            <div>
              <label className="block text-xs font-bold text-[var(--ink)] mb-1">
                ملاحظة المراجعة (تظهر في سجل التدقيق):
              </label>
              <textarea
                rows={3}
                value={reviewNote}
                onChange={(e) => setReviewNote(e.target.value)}
                placeholder="أضف أي ملاحظات أو أسباب الرفض أو الاعتماد..."
                className="w-full p-2.5 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
              />
            </div>

            {/* History timeline */}
            {reviewDrawerTarget.history && reviewDrawerTarget.history.length > 0 && (
              <div>
                <h4 className="text-xs font-bold text-[var(--ink)] mb-2.5">
                  سجل التعديلات والنسخ السابقة ({reviewDrawerTarget.history.length})
                </h4>
                <div className="space-y-2 border-s-2 border-[var(--line)] ms-2 ps-3">
                  {reviewDrawerTarget.history.map((h, i) => (
                    <div key={i} className="text-xs space-y-0.5">
                      <div className="flex items-center justify-between text-[11px] text-[var(--muted)]">
                        <span>نسخة {h.version} ({h.status})</span>
                        <span className="num">{new Date(h.submittedAt).toLocaleDateString('ar-SA')}</span>
                      </div>
                      <div className="font-semibold text-[var(--ink)]">
                        {h.iban || h.workSystem}
                      </div>
                      {h.reviewNote && <p className="text-[11px] text-[var(--muted)]">{h.reviewNote}</p>}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </Drawer>

      {/* ------------------------------------------------------------- */}
      {/* DIALOG: Bulk Action Confirmation */}
      {/* ------------------------------------------------------------- */}
      <Dialog
        open={!!bulkActionTarget}
        onClose={() => setBulkActionTarget(null)}
        title={bulkActionTarget === 'approve' ? 'تأكيد الاعتماد الجماعي' : 'تأكيد الحذف الجماعي'}
        actions={
          <>
            <button
              type="button"
              onClick={() => setBulkActionTarget(null)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              onClick={handleBulkAction}
              className={`px-4 py-2 text-xs font-semibold rounded-lg ${
                bulkActionTarget === 'approve'
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
          هل أنت متأكد من رغبتك في {bulkActionTarget === 'approve' ? 'اعتماد' : 'حذف'}{' '}
          <strong className="num text-[var(--ink)]">{selectedSubmissionIds.length}</strong> رد دفعة واحدة؟
        </p>
      </Dialog>
    </div>
  );
}
