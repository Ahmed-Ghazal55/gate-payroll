'use client';

import React, { useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, Download, RefreshCw, Upload, X } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { Dialog } from '../../components/ui/Dialog';
import type { ImportPreviewResult } from '../../lib/types';
import { canCommitImportStep, type PageImportStep } from './import-steps';
import { fileToBase64, parseBrowserFileToRows } from './parseBrowserFile';

export type ImportUploadDialogProps = {
  open: boolean;
  onClose: () => void;
  step: PageImportStep;
  onCommitted?: () => void;
};

export function ImportUploadDialog({ open, onClose, step, onCommitted }: ImportUploadDialogProps) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const inputRef = useRef<HTMLInputElement | null>(null);

  const [file, setFile] = useState<File | null>(null);
  const [base64, setBase64] = useState<string | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [committing, setCommitting] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [preview, setPreview] = useState<ImportPreviewResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const canCommit = canCommitImportStep(step, { isAdmin, can });

  const reset = () => {
    setFile(null);
    setBase64(null);
    setPreview(null);
    setError(null);
    setPreviewing(false);
    setCommitting(false);
    if (inputRef.current) inputRef.current.value = '';
  };

  const handleClose = () => {
    reset();
    onClose();
  };

  const handleDownloadTemplate = async () => {
    if (!step.templateUrl) return;
    setDownloading(true);
    try {
      await api.downloadImportTemplate(step.templateUrl, `${step.key}-template.xlsx`);
      toast(`تم بدء تحميل نموذج: ${step.labelAr}`, 'success');
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'فشل تحميل النموذج.', 'danger');
    } finally {
      setDownloading(false);
    }
  };

  const handleFile = async (next: File) => {
    setFile(next);
    setPreview(null);
    setError(null);
    setPreviewing(true);
    try {
      if (step.upload === 'json-rows') {
        const rows = await parseBrowserFileToRows(next);
        if (rows.length === 0) throw new Error('لم يتم العثور على صفوف صالحة في الملف.');
        const previewRes = await api.postImportPreview(step.previewUrl, {
          rows,
          companies: rows,
          filename: next.name,
        });
        setPreview(previewRes);
        toast(`تمت معاينة ${rows.length} صفاً بنجاح.`, 'success');
      } else {
        const b64 = await fileToBase64(next);
        setBase64(b64);
        const previewRes = await api.postImportPreview(step.previewUrl, {
          filename: next.name,
          dataBase64: b64,
        });
        setPreview(previewRes);
        const errCount = previewRes?.errors?.length ?? 0;
        if (errCount > 0) toast(`تمت المعاينة مع وجود ${errCount} أخطاء تحتاج تصحيح.`, 'warning');
        else toast(`تمت معاينة ${previewRes?.rows?.length ?? 0} صفاً بنجاح.`, 'success');
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'فشلت معاينة الملف.';
      setError(message);
      toast(message, 'danger');
    } finally {
      setPreviewing(false);
    }
  };

  const handleCommit = async () => {
    if (!preview || !canCommit) return;
    setCommitting(true);
    setError(null);
    try {
      const commitPayload =
        step.upload === 'json-rows'
          ? { rows: preview.rows, companies: preview.rows, fingerprint: preview.fingerprint }
          : {
              filename: file?.name,
              dataBase64: base64,
              fingerprint: preview.fingerprint,
              rows: preview.rows,
            };
      await api.postImportCommit(step.commitUrl, commitPayload);
      toast(`تم اعتماد واستيراد «${step.labelAr}» بنجاح.`, 'success');
      onCommitted?.();
      handleClose();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'فشل اعتماد الاستيراد.';
      setError(message);
      toast(message, 'danger');
    } finally {
      setCommitting(false);
    }
  };

  const errorCount = preview?.errors?.length ?? 0;
  const sampleRows = Array.isArray(preview?.rows) ? preview!.rows!.slice(0, 8) : [];
  const sampleCols =
    sampleRows.length > 0
      ? Object.keys(sampleRows[0] as object).filter((k) => !k.startsWith('_')).slice(0, 6)
      : [];

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title={step.labelAr}
      description={step.purposeAr}
      size="lg"
      actions={
        <>
          <button
            type="button"
            onClick={handleClose}
            className="px-3 py-2 rounded-lg text-sm border border-[var(--line)] bg-[var(--surface)]"
          >
            إغلاق
          </button>
          {preview && (
            <button
              type="button"
              onClick={handleCommit}
              disabled={committing || errorCount > 0 || !canCommit}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold bg-emerald-600 text-white disabled:opacity-50"
            >
              {committing ? <RefreshCw size={15} className="animate-spin" /> : <CheckCircle2 size={16} />}
              اعتماد البيانات
            </button>
          )}
        </>
      }
    >
      <div className="space-y-3 text-sm text-[var(--ink)]">
        {step.noteAr && (
          <p className="text-xs text-[var(--ink-muted)] border border-[var(--line)] rounded-lg p-2 bg-[var(--surface-muted)]">
            {step.noteAr}
          </p>
        )}

        <div className="flex flex-wrap gap-2">
          {step.templateUrl && (
            <button
              type="button"
              onClick={handleDownloadTemplate}
              disabled={downloading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-[var(--line)] bg-[var(--surface-muted)]"
            >
              {downloading ? <RefreshCw size={14} className="animate-spin" /> : <Download size={14} />}
              تحميل النموذج
            </button>
          )}
          {preview && (
            <button
              type="button"
              onClick={reset}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-[var(--line)]"
            >
              <X size={14} />
              ملف آخر
            </button>
          )}
        </div>

        {!preview && (
          <>
            <input
              ref={inputRef}
              type="file"
              accept={step.upload === 'json-rows' ? '.xlsx,.csv,.json' : '.xlsx'}
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void handleFile(f);
              }}
            />
            <label
              onClick={() => inputRef.current?.click()}
              className={`flex flex-col items-center justify-center p-6 border-2 border-dashed rounded-xl cursor-pointer ${
                previewing ? 'opacity-50 pointer-events-none' : 'border-[var(--line)] hover:border-[var(--accent)]'
              }`}
            >
              {previewing ? (
                <span className="inline-flex items-center gap-2">
                  <RefreshCw size={18} className="animate-spin text-[var(--accent)]" />
                  جارٍ قراءة ومعاينة الملف...
                </span>
              ) : (
                <>
                  <Upload size={22} className="text-[var(--accent)] mb-1" />
                  <span className="font-medium">
                    اختر ملف {step.upload === 'json-rows' ? 'Excel أو CSV أو JSON' : 'Excel (xlsx)'}
                  </span>
                  <span className="text-[11px] text-[var(--ink-muted)] mt-0.5">معاينة ثم اعتماد قبل الحفظ</span>
                </>
              )}
            </label>
          </>
        )}

        {error && (
          <div className="flex items-start gap-2 p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-700 dark:text-red-400 text-xs">
            <AlertCircle size={15} className="flex-shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        {preview && (
          <div className="space-y-2 border border-[var(--line)] rounded-xl p-3 bg-[var(--surface-muted)]/40">
            <div className="flex flex-wrap gap-3 text-xs">
              <span>الملف: {file?.name}</span>
              <span>صفوف: {preview.rows?.length ?? 0}</span>
              <span className={errorCount ? 'text-amber-700 dark:text-amber-300' : ''}>أخطاء: {errorCount}</span>
            </div>
            {!canCommit && (
              <p className="text-xs text-amber-800 dark:text-amber-300">
                الاعتماد يحتاج صلاحية إضافية ({step.extraPermission}).
              </p>
            )}
            {errorCount > 0 && Array.isArray(preview.errors) && (
              <pre className="text-[11px] max-h-32 overflow-auto whitespace-pre-wrap text-red-700 dark:text-red-400">
                {preview.errors
                  .slice(0, 20)
                  .map((e) => (typeof e === 'string' ? e : JSON.stringify(e)))
                  .join('\n')}
              </pre>
            )}
            {sampleCols.length > 0 && (
              <div className="overflow-x-auto max-h-48 border border-[var(--line)] rounded-lg bg-[var(--surface)]">
                <table className="min-w-full text-[11px]">
                  <thead>
                    <tr className="border-b border-[var(--line)] text-[var(--ink-muted)]">
                      <th className="p-2 text-right">#</th>
                      {sampleCols.map((c) => (
                        <th key={c} className="p-2 text-right">
                          {c}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {sampleRows.map((row, idx) => (
                      <tr key={idx} className="border-b border-[var(--line)] last:border-0">
                        <td className="p-2 text-[var(--ink-muted)]">{idx + 1}</td>
                        {sampleCols.map((c) => (
                          <td key={c} className="p-2 truncate max-w-[140px]">
                            {String((row as Record<string, unknown>)[c] ?? '')}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>
    </Dialog>
  );
}
