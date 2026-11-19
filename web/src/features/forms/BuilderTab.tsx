'use client';

import React, { useEffect, useState } from 'react';
import {
  Plus,
  Sparkles,
  CheckCircle2,
  Copy,
  Trash2,
  RefreshCw,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { CompanySponsor, CustomForm, CustomFormQuestion } from '../../lib/types';
import { Toolbar } from '../../components/ui/Toolbar';
import { Dialog } from '../../components/ui/Dialog';
import { ErrorState } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { DeletePreviewDialog, DeleteTarget } from './DeletePreviewDialog';

const TARGET_OPTIONS = [
  { value: 'rider', label: 'المناديب فقط' },
  { value: 'staff', label: 'الموظفون فقط' },
  { value: 'ALL', label: 'الجميع' },
] as const;

export interface BuilderTabProps {
  forms: CustomForm[];
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
}

export function BuilderTab({
  forms,
  loading,
  error,
  onRefresh,
}: BuilderTabProps) {
  const { toast } = useToast();
  const { canManageForms, isAdmin } = useAuth();

  const [createOpen, setCreateOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newTarget, setNewTarget] = useState<(typeof TARGET_OPTIONS)[number]['value']>('rider');
  const [newCompanyId, setNewCompanyId] = useState('ALL');
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [questions] = useState<CustomFormQuestion[]>([
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
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null);

  const formTitle = (form: CustomForm) => form.titleAr || form.title || 'نموذج بدون عنوان';
  const formFieldCount = (form: CustomForm) =>
    Array.isArray(form.fields) ? form.fields.length : (form.questions?.length ?? 0);
  const isPublished = (form: CustomForm) => form.status === 'open' || form.status === 'published';
  const isDraft = (form: CustomForm) => form.status === 'draft';

  const handleClose = async (id: string) => {
    try {
      await api.closeCustomForm(id);
      toast('تم إغلاق النموذج', 'success');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر إغلاق النموذج', 'danger');
    }
  };

  useEffect(() => {
    api
      .getCompanies()
      .then((rows) => setCompanies(Array.isArray(rows) ? rows : []))
      .catch(() => setCompanies([]));
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim()) return;

    setSaving(true);
    try {
      const fields = questions.map((q) => ({
        id: q.id,
        type: q.type === 'choice' ? 'select' : q.type === 'file' ? 'text' : q.type,
        labelAr: q.label,
        required: q.required,
        ...(q.options
          ? {
              options: q.options.map((opt) => ({
                value: opt,
                labelAr: opt,
              })),
            }
          : {}),
      }));
      await api.createCustomForm({
        titleAr: newTitle.trim(),
        expiresAt: new Date(Date.now() + 14 * 86400000).toISOString(),
        fields,
        target: newTarget,
        ...(newCompanyId && newCompanyId !== 'ALL'
          ? { targetCompanyId: newCompanyId, companyId: newCompanyId }
          : {}),
      } as Partial<CustomForm>);
      toast('تم حفظ النموذج كمسودة بنجاح', 'success');
      setCreateOpen(false);
      setNewTitle('');
      setNewTarget('rider');
      setNewCompanyId('ALL');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر حفظ النموذج', 'danger');
    } finally {
      setSaving(false);
    }
  };

  const handlePublish = async (id: string) => {
    try {
      await api.publishCustomForm(id);
      toast('تم نشر النموذج وبدء استقبال الاستبيانات', 'success');
      onRefresh();
    } catch (err: any) {
      toast(err?.message || 'تعذر نشر النموذج', 'danger');
    }
  };

  const copyToClipboard = (text: string, label: string) => {
    if (navigator.clipboard) {
      navigator.clipboard.writeText(text);
      toast(`تم نسخ رابط ${label} إلى الحافظة`, 'success');
    }
  };

  return (
    <div className="space-y-4">
      <Toolbar
        searchPlaceholder="بحث في النماذج المصممة..."
        extraControls={
          <div className="flex items-center gap-2">
            {canManageForms && (
              <button
                type="button"
                onClick={() => setCreateOpen(true)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 shadow-xs"
              >
                <Plus size={15} />
                <span>تصميم نموذج جديد</span>
              </button>
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
          title="تعذر تحميل النماذج"
          message={error}
          onRetry={onRefresh}
        />
      ) : forms.length === 0 ? (
        <div className="text-center py-12 border border-dashed border-[var(--line)] rounded-2xl bg-[var(--surface)]">
          <h3 className="text-sm font-bold text-[var(--ink)] mb-1">لا توجد نماذج مخصصة</h3>
          <p className="text-xs text-[var(--muted)]">أنشئ نموذجًا مخصصًا لتجميع معلومات إضافية من المناديب.</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {forms.map((form) => (
            <div
              key={form.id}
              className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-5 flex flex-col justify-between shadow-xs"
            >
              <div>
                <div className="flex items-center justify-between gap-2 mb-2">
                  <span
                    className={`px-2 py-0.5 rounded-full text-[10px] font-semibold ${
                      form.status === 'open' || form.status === 'published'
                        ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                        : form.status === 'closed'
                          ? 'bg-[var(--muted)]/15 text-[var(--muted)]'
                          : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                    }`}
                  >
                    {form.status === 'open' || form.status === 'published'
                      ? 'منشور ونشط'
                      : form.status === 'closed'
                        ? 'مغلق'
                        : 'مسودة'}
                  </span>
                  <span className="num text-xs text-[var(--muted)]">
                    {formFieldCount(form)} أسئلة
                  </span>
                </div>

                <h3 className="text-base font-bold text-[var(--ink)] mb-1">
                  {form.titleAr || form.title || '—'}
                </h3>
                <p className="text-xs text-[var(--muted)] mb-4">
                  {form.expiresAt ? `ينتهي: ${new Date(form.expiresAt).toLocaleDateString('ar-SA')}` : 'بدون تاريخ انتهاء'}
                </p>
              </div>

              <div className="flex items-center justify-between gap-2 pt-3 border-t border-[var(--line)]">
                {form.status === 'draft' ? (
                  canManageForms ? (
                    <button
                      type="button"
                      onClick={() => handlePublish(form.id)}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 transition"
                    >
                      <Sparkles size={13} />
                      <span>نشر النموذج</span>
                    </button>
                  ) : (
                    <span className="text-xs text-[var(--muted)]">مسودة قيد الإعداد</span>
                  )
                ) : form.status === 'open' || form.status === 'published' ? (
                  canManageForms ? (
                    <button
                      type="button"
                      onClick={async () => {
                        try {
                          await api.closeCustomForm(form.id);
                          toast('تم إغلاق النموذج', 'success');
                          onRefresh();
                        } catch (err: any) {
                          toast(err?.message || 'تعذر إغلاق النموذج', 'danger');
                        }
                      }}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-[var(--line)] text-[var(--ink)] hover:bg-[var(--raised)]"
                    >
                      إغلاق
                    </button>
                  ) : (
                    <span className="text-xs text-[var(--money-pos)] font-semibold flex items-center gap-1">
                      <CheckCircle2 size={14} />
                      جاهز للاستقبال
                    </span>
                  )
                ) : (
                  <span className="text-xs text-[var(--muted)]">مغلق</span>
                )}

                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() =>
                      copyToClipboard(
                        form.publicUrl || '',
                        form.titleAr || form.title || form.id
                      )
                    }
                    className="p-1.5 rounded-lg border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]"
                    title="نسخ الرابط"
                  >
                    <Copy size={14} />
                  </button>

                  {isAdmin && (
                    <button
                      type="button"
                      onClick={() =>
                        setDeleteTarget({
                          id: form.id,
                          title: form.titleAr || form.title || form.id,
                          kind: 'custom_form',
                        })
                      }
                      className="p-1.5 rounded-lg border border-[var(--line)] text-[var(--danger)] hover:bg-[var(--danger)]/10"
                      title="حذف النموذج"
                    >
                      <Trash2 size={14} />
                    </button>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      <Dialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="تصميم نموذج مخصص جديد"
        description="حدد الجمهور والشركة المستهدفة من القوائم المعتمدة؛ لا يُقبل إدخال حر."
        actions={
          <>
            <button
              type="button"
              onClick={() => setCreateOpen(false)}
              className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={saving}
              onClick={handleCreate}
              className="px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50"
            >
              حفظ كمسودة
            </button>
          </>
        }
      >
        <form onSubmit={handleCreate} className="space-y-4">
          <div>
            <label className="block text-xs font-bold text-[var(--ink)] mb-1">عنوان النموذج</label>
            <input
              type="text"
              required
              value={newTitle}
              onChange={(e) => setNewTitle(e.target.value)}
              placeholder="مثال: استبيان السكن — أكتوبر 2026"
              className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:border-[var(--accent)] focus:outline-hidden"
            />
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-bold text-[var(--ink)] mb-1">
                نوع الجمهور المستهدف
              </label>
              <select
                value={newTarget}
                onChange={(e) =>
                  setNewTarget(e.target.value as (typeof TARGET_OPTIONS)[number]['value'])
                }
                className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              >
                {TARGET_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs font-bold text-[var(--ink)] mb-1">
                الشركة المستهدفة
              </label>
              <select
                value={newCompanyId}
                onChange={(e) => setNewCompanyId(e.target.value)}
                className="w-full px-3 py-2 text-xs sm:text-sm rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
              >
                <option value="ALL">كل الشركات</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.shortNameAr || c.nameAr} ({c.code})
                  </option>
                ))}
              </select>
            </div>
          </div>
          <p className="text-[11px] text-[var(--muted)]">
            يُحفظ بثلاثة أسئلة افتراضية (الاسم، الإقامة، المدينة). يمكن توسيع المحرر لاحقًا.
          </p>
        </form>
      </Dialog>

      {/* Delete Confirmation */}
      <DeletePreviewDialog
        target={deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onSuccess={onRefresh}
      />
    </div>
  );
}
