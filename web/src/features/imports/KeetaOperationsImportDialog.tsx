'use client';

import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, Download, RefreshCw, Upload, X } from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import type { paths } from '../../lib/api-contract';
import {
  canCommitImportStep,
  canDownloadImportTemplate,
  KEETA_OPS_IMPORT,
} from './import-steps';
import { fileToBase64 } from './parseBrowserFile';

type OpsPreview = paths['/api/pay/apps/{app}/operations/import/preview']['post']['responses'][200]['content']['application/json'];
type OpsDisplayRow = NonNullable<OpsPreview['displayRows']>[number] & {
  dismissed?: boolean;
  row?: number;
  [key: string]: unknown;
};

export type KeetaOperationsImportDialogProps = {
  open: boolean;
  onClose: () => void;
  period?: string;
  onCommitted?: () => void;
  allowedActions?: string[] | null;
};

function registerCode(c: CompanySponsor): string {
  return (c.code || '').toUpperCase();
}

export function KeetaOperationsImportDialog({
  open,
  onClose,
  period,
  onCommitted,
  allowedActions,
}: KeetaOperationsImportDialogProps) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const step = KEETA_OPS_IMPORT;

  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [register, setRegister] = useState('');
  const [invoiceId, setInvoiceId] = useState('');
  const [invoices, setInvoices] = useState<Array<{ id: string; label: string }>>([]);
  const [file, setFile] = useState<File | null>(null);
  const [base64, setBase64] = useState<string | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [committing, setCommitting] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [preview, setPreview] = useState<OpsPreview | null>(null);
  const [error, setError] = useState<string | null>(null);

  const canCommit = canCommitImportStep(step, { isAdmin, can });
  const canDownload = canDownloadImportTemplate(step, { isAdmin, can, allowedActions });

  useEffect(() => {
    if (!open) return;
    loadCompanyOptions()
      .then((list) => setCompanies(list.filter((c) => ['BW', 'EX', 'TR'].includes(registerCode(c)))))
      .catch(() => setCompanies([]));
  }, [open]);

  useEffect(() => {
    if (!open || !register) {
      setInvoices([]);
      setInvoiceId('');
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const res = await api.getKeetaInvoices({ page: 1, pageSize: 100 });
        const items = (res?.items || []) as Array<{
          id?: string;
          companyId?: string;
          period?: string;
          approvalStatus?: string;
          reference?: string;
        }>;
        const company = companies.find((c) => registerCode(c) === register || c.id === register);
        const matched = items
          .filter((inv) => inv.approvalStatus === 'approved')
          .filter((inv) => !period || inv.period === period)
          .filter((inv) => {
            if (!company) return true;
            return inv.companyId === company.id || inv.companyId === company.code;
          })
          .map((inv) => ({
            id: String(inv.id || ''),
            label: `${inv.period || ''} — ${inv.reference || inv.id || ''}`.trim(),
          }))
          .filter((x) => x.id);
        if (!cancelled) {
          setInvoices(matched);
          setInvoiceId((prev) => (matched.some((m) => m.id === prev) ? prev : matched[0]?.id || ''));
        }
      } catch {
        if (!cancelled) setInvoices([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [open, register, period, companies]);

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
    setRegister('');
    setInvoiceId('');
    onClose();
  };

  const handleDownloadTemplate = async () => {
    if (!step.templateUrl || !canDownload) return;
    setDownloading(true);
    try {
      await api.downloadImportTemplate(step.templateUrl, 'keeta-operations-template-v1.xlsx');
      toast('تم بدء تحميل نموذج تشغيل كيتا العربي.', 'success');
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'فشل تحميل النموذج.', 'danger');
    } finally {
      setDownloading(false);
    }
  };

  const handleFile = async (next: File) => {
    if (!register) {
      setError('اختر السجل قبل رفع الملف.');
      toast('اختر السجل قبل رفع الملف.', 'danger');
      return;
    }
    if (!invoiceId) {
      setError('اختر فاتورة كيتا المعتمدة لهذا السجل.');
      toast('اختر فاتورة كيتا المعتمدة لهذا السجل.', 'danger');
      return;
    }
    setFile(next);
    setPreview(null);
    setError(null);
    setPreviewing(true);
    try {
      const b64 = await fileToBase64(next);
      setBase64(b64);
      const previewRes = (await api.postImportPreview(step.previewUrl, {
        filename: next.name,
        dataBase64: b64,
        invoiceId,
        register,
      })) as OpsPreview;
      setPreview(previewRes);
      toast(`تمت معاينة ${previewRes.counts?.rows ?? previewRes.displayRows?.length ?? 0} صف تشغيل.`, 'success');
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'فشلت معاينة ملف تشغيل كيتا.';
      setError(message);
      toast(message, 'danger');
    } finally {
      setPreviewing(false);
    }
  };

  const handleCommit = async () => {
    if (!preview || !canCommit || !base64) return;
    setCommitting(true);
    setError(null);
    try {
      await api.postImportCommit(step.commitUrl, {
        filename: file?.name,
        dataBase64: base64,
        fingerprint: preview.fingerprint,
        rows: preview.rows,
        invoiceId,
        register,
        confirm: true,
      });
      toast('تم اعتماد تشغيل كيتا للسجل المختار.', 'success');
      onCommitted?.();
      handleClose();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'فشل اعتماد تشغيل كيتا.';
      setError(message);
      toast(message, 'danger');
    } finally {
      setCommitting(false);
    }
  };

  const columns = useMemo(() => {
    const fromPreview = preview?.columns;
    if (Array.isArray(fromPreview) && fromPreview.length > 0) return fromPreview.map(String);
    return [] as string[];
  }, [preview]);

  const displayRows = useMemo(() => {
    const rows = preview?.displayRows;
    return Array.isArray(rows) ? (rows as OpsDisplayRow[]) : [];
  }, [preview]);

  const tableRows = useMemo(
    () =>
      displayRows.map((row, idx): Record<string, unknown> => ({
        ...row,
        __rowNo: Number(row.row ?? idx + 1),
        __dismissed: Boolean(row.dismissed),
      })),
    [displayRows]
  );
  const filterCols = useMemo(
    () => [
      { key: '__rowNo', header: '#', type: 'number' as const, getValue: (r: Record<string, unknown>) => Number(r.__rowNo ?? 0) },
      ...columns.map((c) => ({
        key: c,
        header: c,
        type: 'text' as const,
        getValue: (r: Record<string, unknown>) => String(r[c] ?? ''),
      })),
    ],
    [columns]
  );
  const tableFilter = useTableAutoFilter(tableRows, filterCols);

  const registerOptions = companies.map((c) => ({
    value: registerCode(c) || c.id,
    label: companyLabel(c),
  }));

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title={step.labelAr}
      description={step.purposeAr}
      size="xl"
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
              disabled={committing || !canCommit}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold bg-emerald-600 text-white disabled:opacity-50"
            >
              {committing ? <RefreshCw size={15} className="animate-spin" /> : <CheckCircle2 size={16} />}
              اعتماد تشغيل السجل
            </button>
          )}
        </>
      }
    >
      <div className="space-y-3 text-sm text-[var(--ink)]">
        <p className="text-xs text-[var(--ink-muted)] border border-[var(--line)] rounded-lg p-2 bg-[var(--surface-muted)]">
          {step.noteAr}
        </p>

        <div className="grid gap-3 sm:grid-cols-2">
          <label className="space-y-1 text-xs">
            <span className="font-semibold">السجل (قبل الرفع)</span>
            <SearchableSelect
              value={register}
              onChange={(v) => {
                setRegister(v);
                reset();
              }}
              options={registerOptions}
              placeholder="اختر إكسبرس أو البوابا أو التجارية"
            />
          </label>
          <label className="space-y-1 text-xs">
            <span className="font-semibold">فاتورة كيتا المعتمدة</span>
            <SearchableSelect
              value={invoiceId}
              onChange={(v) => {
                setInvoiceId(v);
                reset();
              }}
              options={invoices.map((i) => ({ value: i.id, label: i.label }))}
              placeholder={register ? 'اختر الفاتورة' : 'اختر السجل أولًا'}
              disabled={!register}
            />
          </label>
        </div>

        <div className="flex flex-wrap gap-2">
          {step.templateUrl && canDownload && (
            <button
              type="button"
              data-testid="import-download-template"
              onClick={handleDownloadTemplate}
              disabled={downloading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-[var(--line)] bg-[var(--surface-muted)]"
            >
              {downloading ? <RefreshCw size={14} className="animate-spin" /> : <Download size={14} />}
              تحميل النموذج العربي
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
              accept=".xlsx"
              className="hidden"
              data-testid="keeta-ops-file"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) void handleFile(f);
              }}
            />
            <label
              onClick={() => {
                if (!register || !invoiceId) {
                  setError(!register ? 'اختر السجل قبل رفع الملف.' : 'اختر فاتورة كيتا المعتمدة لهذا السجل.');
                  return;
                }
                inputRef.current?.click();
              }}
              className={`flex flex-col items-center justify-center p-6 border-2 border-dashed rounded-xl cursor-pointer ${
                previewing ? 'opacity-50 pointer-events-none' : 'border-[var(--line)] hover:border-[var(--accent)]'
              }`}
            >
              {previewing ? (
                <span className="inline-flex items-center gap-2">
                  <RefreshCw size={18} className="animate-spin text-[var(--accent)]" />
                  جارٍ قراءة ورقة السجل...
                </span>
              ) : (
                <>
                  <Upload size={22} className="text-[var(--accent)] mb-1" />
                  <span className="font-medium">اختر ملف إكسل لورقة السجل</span>
                  <span className="text-[11px] text-[var(--ink-muted)] mt-0.5">
                    ورقة واحدة = سجل واحد (أو ملف متعدد يختار ورقة السجل تلقائيًا)
                  </span>
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
              <span>السجل: {preview.register || register}</span>
              <span>صفوف: {preview.counts?.rows ?? displayRows.length}</span>
              <span>مفصول بأداء: {preview.counts?.dismissed ?? 0}</span>
              <span>صفوف معرف مكرر: {preview.counts?.duplicateIdRows ?? 0}</span>
            </div>
            {columns.length > 0 && (
              <div className="overflow-x-auto max-h-80 border border-[var(--line)] rounded-lg bg-[var(--surface)]">
                <AutoFilterBar controller={tableFilter} totalCount={tableRows.length} />
                <table className="min-w-full text-[11px]" data-testid="keeta-ops-preview-table">
                  <thead>
                    <tr className="border-b border-[var(--line)] text-[var(--ink-muted)]">
                      <AutoFilterTh controller={tableFilter} colKey="__rowNo" className="p-2 text-right sticky right-0 bg-[var(--surface)]" />
                      {columns.map((c) => (
                        <AutoFilterTh key={c} controller={tableFilter} colKey={c} className="p-2 text-right whitespace-nowrap" />
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {tableFilter.rows.map((row) => {
                      const dismissed = Boolean(row.__dismissed);
                      return (
                        <tr
                          key={String(row.__rowNo)}
                          className={
                            dismissed
                              ? 'bg-red-600 text-white border-b border-red-700'
                              : 'border-b border-[var(--line)] last:border-0'
                          }
                        >
                          <td className="p-2">{String(row.__rowNo)}</td>
                          {columns.map((c) => (
                            <td key={c} className="p-2 whitespace-nowrap max-w-[160px] truncate">
                              {formatCell(row[c])}
                            </td>
                          ))}
                        </tr>
                      );
                    })}
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

function formatCell(value: unknown): string {
  if (value == null) return '';
  if (typeof value === 'boolean') return value ? 'نعم' : 'لا';
  if (typeof value === 'number') return String(value);
  return String(value);
}
