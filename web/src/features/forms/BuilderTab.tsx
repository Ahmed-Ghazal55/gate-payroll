'use client';

import React, { useState } from 'react';
import {
  Plus,
  Sparkles,
  CheckCircle2,
  Copy,
  Trash2,
  RefreshCw,
} from 'lucide-react';
import { api } from '../../lib/api';
import type { CustomForm, CustomFormQuestion } from '../../lib/types';
import { Toolbar } from '../../components/ui/Toolbar';
import { Dialog } from '../../components/ui/Dialog';
import { ErrorState } from '../../components/ui/EmptyState';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { DeletePreviewDialog, DeleteTarget } from './DeletePreviewDialog';

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
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim()) return;

    setSaving(true);
    try {
      await api.createCustomForm({
        title: newTitle.trim(),
        questions,
      });
      toast('تم حفظ النموذج كمسودة بنجاح', 'success');
      setCreateOpen(false);
      setNewTitle('');
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
                ) : (
                  <span className="text-xs text-[var(--money-pos)] font-semibold flex items-center gap-1">
                    <CheckCircle2 size={14} />
                    جاهز للاستقبال
                  </span>
                )}

                <div className="flex items-center gap-1">
                  <button
                    type="button"
                    onClick={() =>
                      copyToClipboard(
                        (form as { publicUrl?: string }).publicUrl || '',
                        form.title
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
                          title: form.title,
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

      {/* Delete Confirmation */}
      <DeletePreviewDialog
        target={deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onSuccess={onRefresh}
      />
    </div>
  );
}
