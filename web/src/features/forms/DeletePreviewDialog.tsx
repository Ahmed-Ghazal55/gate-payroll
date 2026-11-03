'use client';

import React, { useState, useEffect } from 'react';
import { Dialog } from '../../components/ui/Dialog';
import { api, ApiError } from '../../lib/api';
import { AlertTriangle, Loader2 } from 'lucide-react';
import { useToast } from '../../components/ui/Toast';

export interface DeleteTarget {
  id: string;
  title: string;
  kind: 'campaign' | 'custom_form';
}

export interface DeletePreviewDialogProps {
  target: DeleteTarget | null;
  onClose: () => void;
  onSuccess: () => void;
}

export function DeletePreviewDialog({
  target,
  onClose,
  onSuccess,
}: DeletePreviewDialogProps) {
  const { toast } = useToast();
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [previewData, setPreviewData] = useState<{
    id: string;
    title: string;
    responseCount: number;
    fingerprint: string;
  } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fetchPreview = async () => {
    if (!target) return;
    setLoadingPreview(true);
    setError(null);
    try {
      const data =
        target.kind === 'campaign'
          ? await api.getCampaignDeletePreview(target.id)
          : await api.getCustomFormDeletePreview(target.id);
      setPreviewData(data);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل معاينة الحذف');
    } finally {
      setLoadingPreview(false);
    }
  };

  useEffect(() => {
    if (target) {
      fetchPreview();
    } else {
      setPreviewData(null);
      setError(null);
    }
  }, [target]);

  const handleConfirmDelete = async () => {
    if (!target || !previewData) return;
    setDeleting(true);
    try {
      if (target.kind === 'campaign') {
        await api.deleteCampaign(target.id, previewData.fingerprint);
      } else {
        await api.deleteCustomForm(target.id, previewData.fingerprint);
      }
      toast('تم حذف النموذج وردوده نهائيًا', 'success');
      onSuccess();
      onClose();
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        toast('تغيرت البيانات أثناء الحذف؛ تم تحديث المعاينة تلقائيًا.', 'warning');
        await fetchPreview();
      } else {
        toast(err?.message || 'تعذر إتمام عملية الحذف', 'danger');
      }
    } finally {
      setDeleting(false);
    }
  };

  if (!target) return null;

  return (
    <Dialog
      open={!!target}
      onClose={onClose}
      title={`تأكيد حذف ${target.kind === 'campaign' ? 'حملة النماذج' : 'النموذج المخصص'}`}
      description="يرجى التحقق من بصمة البيانات وعدد الردود المرتبطة قبل الحذف النهائي."
      actions={
        <>
          <button
            type="button"
            disabled={deleting}
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-lg border border-[var(--line)] hover:bg-[var(--raised)] text-[var(--muted)]"
          >
            إلغاء
          </button>
          <button
            type="button"
            disabled={deleting || loadingPreview || !previewData}
            onClick={handleConfirmDelete}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-lg bg-[var(--danger)] text-white hover:opacity-90 disabled:opacity-50"
          >
            {deleting && <Loader2 size={13} className="animate-spin" />}
            <span>تأكيد الحذف النهائي</span>
          </button>
        </>
      }
    >
      {loadingPreview ? (
        <div className="flex items-center justify-center py-8 text-[var(--muted)]">
          <Loader2 size={24} className="animate-spin text-[var(--accent)]" />
        </div>
      ) : error ? (
        <div className="p-4 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/20 text-xs text-[var(--danger)]">
          {error}
          <div className="mt-2">
            <button
              type="button"
              onClick={fetchPreview}
              className="font-bold underline"
            >
              إعادة تحميل المعاينة
            </button>
          </div>
        </div>
      ) : previewData ? (
        <div className="space-y-4">
          <div className="p-4 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/25 text-xs text-[var(--danger)] flex items-start gap-3">
            <AlertTriangle size={18} className="shrink-0 mt-0.5" />
            <div>
              <div className="font-bold mb-1">تحذير لا يمكن التراجع عنه</div>
              <div>
                سيتم مسح سجلات النموذج «{previewData.title}» وكافة الردود التابعة له (
                <strong className="num font-bold">{previewData.responseCount}</strong> رد مسجل) من قاعدة البيانات.
              </div>
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] space-y-1.5 text-xs">
            <div className="flex items-center justify-between">
              <span className="text-[var(--muted)]">عنوان النموذج:</span>
              <strong className="text-[var(--ink)]">{previewData.title}</strong>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[var(--muted)]">إجمالي الردود التابعة:</span>
              <span className="num font-bold text-[var(--ink)]">{previewData.responseCount}</span>
            </div>
            <div className="flex items-center justify-between">
              <span className="text-[var(--muted)]">بصمة التحقق (Fingerprint):</span>
              <span className="num font-mono text-[10px] text-[var(--muted)] truncate max-w-[200px]" title={previewData.fingerprint}>
                {previewData.fingerprint.slice(0, 16)}...
              </span>
            </div>
          </div>
        </div>
      ) : null}
    </Dialog>
  );
}
