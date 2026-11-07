'use client';

import React, { useState, useEffect, useCallback, useRef } from 'react';
import Link from 'next/link';
import {
  Upload,
  FileSpreadsheet,
  CheckCircle2,
  AlertCircle,
  AlertTriangle,
  RefreshCw,
  Layers,
  Landmark,
  Truck,
  Building2,
  Download,
  ExternalLink,
  ShieldAlert,
  Eye,
  FileText,
  Check,
  ChevronDown,
  ChevronUp,
  FileUp,
  X,
  ArrowRight,
  Database,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { Drawer } from '../../components/ui/Drawer';
import { ErpSyncDrawer } from '../../components/system/ErpSyncDrawer';
import type { ImportCatalogStep, ImportCatalogColumn, ImportPreviewResult } from '../../lib/types';

// =========================================================================
// Browser XLSX / CSV / JSON Parsers for json-rows (companies)
// =========================================================================

async function parseBrowserFileToRows(file: File): Promise<Record<string, any>[]> {
  const name = file.name.toLowerCase();

  // 1. JSON
  if (name.endsWith('.json')) {
    const text = await file.text();
    const data = JSON.parse(text);
    if (Array.isArray(data)) return data;
    if (data && Array.isArray(data.companies)) return data.companies;
    if (data && Array.isArray(data.rows)) return data.rows;
    throw new Error('ملف JSON يجب أن يحتوي على مصفوفة شركات أو حقل companies/rows.');
  }

  // 2. CSV
  if (name.endsWith('.csv')) {
    const text = await file.text();
    const lines = text.split(/\r?\n/).filter((l) => l.trim().length > 0);
    if (lines.length < 2) throw new Error('ملف CSV فارغ أو لا يحتوي على صفوف بيانات.');
    const headers = lines[0].split(',').map((h) => h.trim().replace(/^["']|["']$/g, ''));
    const rows: Record<string, any>[] = [];
    for (let i = 1; i < lines.length; i++) {
      const cells = lines[i].split(',').map((c) => c.trim().replace(/^["']|["']$/g, ''));
      if (cells.every((c) => c === '')) continue;
      const row: Record<string, any> = {};
      headers.forEach((h, idx) => {
        row[h] = cells[idx] ?? '';
      });
      if (!Object.values(row).some((v) => typeof v === 'string' && v.includes('مثال — احذف'))) {
        rows.push(row);
      }
    }
    return rows;
  }

  // 3. XLSX via pure Web APIs (DecompressionStream)
  const arrayBuffer = await file.arrayBuffer();
  const view = new DataView(arrayBuffer);
  let offset = 0;
  const files: Record<string, string> = {};

  while (offset + 30 <= arrayBuffer.byteLength) {
    const sig = view.getUint32(offset, true);
    if (sig !== 0x04034b50) break;
    const compMethod = view.getUint16(offset + 8, true);
    const compSize = view.getUint32(offset + 18, true);
    const fnLen = view.getUint16(offset + 26, true);
    const extraLen = view.getUint16(offset + 28, true);
    const fnBytes = new Uint8Array(arrayBuffer, offset + 30, fnLen);
    const fn = new TextDecoder().decode(fnBytes);
    const dataOffset = offset + 30 + fnLen + extraLen;
    const compData = new Uint8Array(arrayBuffer, dataOffset, compSize);

    if (fn.startsWith('xl/worksheets/sheet') || fn.includes('sharedStrings')) {
      let content = '';
      if (compMethod === 0) {
        content = new TextDecoder().decode(compData);
      } else if (compMethod === 8) {
        const ds = new DecompressionStream('deflate-raw');
        const writer = ds.writable.getWriter();
        writer.write(compData);
        writer.close();
        const reader = ds.readable.getReader();
        const chunks: Uint8Array[] = [];
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          chunks.push(value);
        }
        const total = chunks.reduce((a, b) => a + b.length, 0);
        const merged = new Uint8Array(total);
        let pos = 0;
        for (const c of chunks) {
          merged.set(c, pos);
          pos += c.length;
        }
        content = new TextDecoder().decode(merged);
      }
      files[fn] = content;
    }
    offset = dataOffset + compSize;
  }

  // Extract shared strings
  const shared: string[] = [];
  if (files['xl/sharedStrings.xml']) {
    const sxml = files['xl/sharedStrings.xml'];
    const siRegex = /<si\b[^>]*>(.*?)<\/si>/gs;
    let match: RegExpExecArray | null;
    while ((match = siRegex.exec(sxml)) !== null) {
      const tMatch = /<t\b[^>]*>(.*?)<\/t>/s.exec(match[1]);
      shared.push(tMatch ? tMatch[1] : '');
    }
  }

  // Extract sheet rows
  const sheetXml = files['xl/worksheets/sheet1.xml'] || Object.values(files)[0] || '';
  if (!sheetXml) {
    throw new Error('لم يتم العثور على ورقة البيانات داخل مصنف Excel.');
  }

  const rowRegex = /<row\b[^>]*>(.*?)<\/row>/gs;
  const parsedRows: Record<string, string>[] = [];
  let rowMatch: RegExpExecArray | null;

  while ((rowMatch = rowRegex.exec(sheetXml)) !== null) {
    const rowContent = rowMatch[1];
    const cellRegex = /<c\b([^>]*)>(.*?)<\/c>/gs;
    const cells: Record<string, string> = {};
    let cellMatch: RegExpExecArray | null;

    while ((cellMatch = cellRegex.exec(rowContent)) !== null) {
      const attrs = cellMatch[1];
      const body = cellMatch[2];
      const rAttr = /r=\"([A-Z]+)(\d+)\"/.exec(attrs);
      if (!rAttr) continue;
      const colLetter = rAttr[1];
      const isString = /t=\"s\"/.test(attrs);
      const isInline = /t=\"inlineStr\"/.test(attrs);
      let val = '';
      if (isString) {
        const vMatch = /<v>(.*?)<\/v>/.exec(body);
        if (vMatch) {
          const idx = parseInt(vMatch[1], 10);
          val = shared[idx] || '';
        }
      } else if (isInline) {
        const tMatch = /<t\b[^>]*>(.*?)<\/t>/s.exec(body);
        if (tMatch) val = tMatch[1];
      } else {
        const vMatch = /<v>(.*?)<\/v>/.exec(body);
        if (vMatch) val = vMatch[1];
      }
      cells[colLetter] = val;
    }
    parsedRows.push(cells);
  }

  if (parsedRows.length < 2) {
    throw new Error('ملف Excel فارغ أو لا يحتوي على صفوف بيانات كافية.');
  }

  function colToIdx(col: string): number {
    let num = 0;
    for (let i = 0; i < col.length; i++) {
      num = num * 26 + (col.charCodeAt(i) - 64);
    }
    return num - 1;
  }

  const headerMap: Record<number, string> = {};
  for (const [col, val] of Object.entries(parsedRows[0])) {
    headerMap[colToIdx(col)] = val.trim();
  }

  const result: Record<string, any>[] = [];
  for (let r = 1; r < parsedRows.length; r++) {
    const rowObj: Record<string, any> = {};
    let hasAnyVal = false;
    for (const [col, val] of Object.entries(parsedRows[r])) {
      const idx = colToIdx(col);
      const header = headerMap[idx];
      if (header) {
        rowObj[header] = val;
        if (val) hasAnyVal = true;
      }
    }
    if (hasAnyVal && !Object.values(rowObj).some((v) => typeof v === 'string' && v.includes('مثال — احذف'))) {
      result.push(rowObj);
    }
  }

  return result;
}

// Helper to convert file to Base64 string
function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      const base64 = result.includes(',') ? result.split(',')[1] : result;
      resolve(base64);
    };
    reader.onerror = (err) => reject(err);
    reader.readAsDataURL(file);
  });
}

// =========================================================================
// Main Import Center Page
// =========================================================================

export default function ImportsPage() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();

  const showToast = {
    success: (msg: string) => toast(msg, 'success'),
    error: (msg: string) => toast(msg, 'danger'),
    warning: (msg: string) => toast(msg, 'warning'),
    info: (msg: string) => toast(msg, 'info'),
  };

  // Catalog state
  const [catalog, setCatalog] = useState<ImportCatalogStep[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Completed steps tracking in session
  const [completedSteps, setCompletedSteps] = useState<Set<string>>(new Set());

  // Download state
  const [downloadingKey, setDownloadingKey] = useState<string | null>(null);

  // Drawer for columns
  const [columnDrawerStep, setColumnDrawerStep] = useState<ImportCatalogStep | null>(null);
  const [expandedChoiceColumns, setExpandedChoiceColumns] = useState<Set<string>>(new Set());

  // ERP Sync Drawer
  const [erpDrawerOpen, setErpDrawerOpen] = useState<boolean>(false);

  // Active Upload State per step: key -> { file, uploading, previewResult, committing, commitSuccess, error }
  const [uploadState, setUploadState] = useState<
    Record<
      string,
      {
        file?: File | null;
        base64?: string;
        previewing?: boolean;
        previewResult?: ImportPreviewResult | null;
        committing?: boolean;
        commitResult?: any | null;
        error?: string | null;
      }
    >
  >({});

  const fileInputRefs = useRef<Record<string, HTMLInputElement | null>>({});

  // Fetch catalog
  const loadCatalog = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.getImportsCatalog();
      // Ensure sorted by order
      const sorted = Array.isArray(data) ? [...data].sort((a, b) => a.order - b.order) : [];
      setCatalog(sorted);
    } catch (err: any) {
      setError(err?.message || 'تعذر تحميل دليل الاستيراد من الخادم. يرجى التحقق من الاتصال والمحاولة مجدداً.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadCatalog();
  }, [loadCatalog]);

  // Handle template download
  const handleDownloadTemplate = async (step: ImportCatalogStep) => {
    if (!step.templateUrl) return;
    if (!isAdmin && !can('imports.view')) {
      showToast.error('غير مصرح لك بتحميل نماذج الاستيراد (مطلوب صلاحية imports.view).');
      return;
    }

    setDownloadingKey(step.key);
    try {
      const fallbackName = `${step.key}-template.xlsx`;
      await api.downloadImportTemplate(step.templateUrl, fallbackName);
      showToast.success(`تم بدء تحميل نموذج: ${step.labelAr}`);
    } catch (err: any) {
      if (err?.status === 401) {
        showToast.error('انتهت الجلسة أو غير مسجل الدخول (401). يرجى إعادة تسجيل الدخول.');
      } else if (err?.status === 403) {
        showToast.error('ليس لديك صلاحية لتحميل هذا النموذج (403).');
      } else {
        showToast.error(err?.message || 'فشل تحميل النموذج.');
      }
    } finally {
      setDownloadingKey(null);
    }
  };

  // Handle File Selection
  const handleFileSelect = async (step: ImportCatalogStep, file: File) => {
    setUploadState((prev) => ({
      ...prev,
      [step.key]: {
        file,
        previewing: true,
        previewResult: null,
        committing: false,
        commitResult: null,
        error: null,
      },
    }));

    try {
      if (step.upload === 'json-rows') {
        // Parse rows in browser
        const rows = await parseBrowserFileToRows(file);
        if (rows.length === 0) {
          throw new Error('لم يتم العثور على صفوف صالحة في الملف.');
        }

        const previewRes = await api.postImportPreview(step.previewUrl!, {
          rows,
          companies: rows,
          filename: file.name,
        });

        setUploadState((prev) => ({
          ...prev,
          [step.key]: {
            ...prev[step.key],
            previewing: false,
            previewResult: previewRes,
            error: null,
          },
        }));
        showToast.success(`تمت معاينة ${rows.length} صفاً بنجاح.`);
      } else if (step.upload === 'xlsx') {
        const base64 = await fileToBase64(file);
        const payload: Record<string, any> = {
          filename: file.name,
          dataBase64: base64,
        };

        const previewRes = await api.postImportPreview(step.previewUrl!, payload);

        setUploadState((prev) => ({
          ...prev,
          [step.key]: {
            ...prev[step.key],
            base64,
            previewing: false,
            previewResult: previewRes,
            error: null,
          },
        }));

        const count = previewRes?.rows?.length ?? 0;
        const errCount = previewRes?.errors?.length ?? 0;
        if (errCount > 0) {
          showToast.warning(`تمت المعاينة مع وجود ${errCount} أخطاء تحتاج تصحيح.`);
        } else {
          showToast.success(`تمت معاينة ${count} صفاً بنجاح.`);
        }
      }
    } catch (err: any) {
      setUploadState((prev) => ({
        ...prev,
        [step.key]: {
          ...prev[step.key],
          previewing: false,
          error: err?.message || 'فشلت معاينة الملف.',
        },
      }));
      showToast.error(err?.message || 'فشلت معاينة الملف.');
    }
  };

  // Handle Commit
  const handleCommit = async (step: ImportCatalogStep) => {
    const cur = uploadState[step.key];
    if (!cur || !cur.previewResult || !step.commitUrl) return;

    setUploadState((prev) => ({
      ...prev,
      [step.key]: {
        ...prev[step.key],
        committing: true,
        error: null,
      },
    }));

    try {
      let commitPayload: Record<string, any> = {};

      if (step.upload === 'json-rows') {
        commitPayload = {
          rows: cur.previewResult.rows,
          companies: cur.previewResult.rows,
          fingerprint: cur.previewResult.fingerprint,
        };
      } else {
        commitPayload = {
          filename: cur.file?.name,
          dataBase64: cur.base64,
          fingerprint: cur.previewResult.fingerprint,
          rows: cur.previewResult.rows,
        };
      }

      const res = await api.postImportCommit(step.commitUrl, commitPayload);

      setUploadState((prev) => ({
        ...prev,
        [step.key]: {
          ...prev[step.key],
          committing: false,
          commitResult: res,
          error: null,
        },
      }));

      // Mark step as completed
      setCompletedSteps((prev) => new Set([...prev, step.key]));
      showToast.success(`تم اعتماد واستيراد «${step.labelAr}» بنجاح!`);
    } catch (err: any) {
      setUploadState((prev) => ({
        ...prev,
        [step.key]: {
          ...prev[step.key],
          committing: false,
          error: err?.message || 'فشل اعتماد الاستيراد.',
        },
      }));
      showToast.error(err?.message || 'فشل اعتماد الاستيراد.');
    }
  };

  // Reset upload for a step
  const handleResetStep = (stepKey: string) => {
    setUploadState((prev) => {
      const next = { ...prev };
      delete next[stepKey];
      return next;
    });
    if (fileInputRefs.current[stepKey]) {
      fileInputRefs.current[stepKey]!.value = '';
    }
  };

  // Check user permission for upload
  const canUploadStep = (step: ImportCatalogStep): boolean => {
    if (isAdmin) return true;
    if (!user) return false;
    return can(step.permission);
  };

  // Group steps
  const setupSteps = catalog.filter((s) => s.group === 'setup');
  const openingSteps = catalog.filter((s) => s.group === 'opening');
  const platformSteps = catalog.filter((s) => s.group === 'invoices' || s.group === 'operations');

  // Helper to get step label by key
  const getStepLabelByKey = (key: string) => {
    const s = catalog.find((x) => x.key === key);
    return s ? s.labelAr : key;
  };

  // Render Status Badge
  const renderStatusBadge = (status: ImportCatalogStep['status']) => {
    switch (status) {
      case 'ready':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-emerald-500/15 text-emerald-700 dark:text-emerald-400 border border-emerald-500/30">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
            جاهز
          </span>
        );
      case 'template_only':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-amber-500/15 text-amber-700 dark:text-amber-400 border border-amber-500/30">
            <span className="w-1.5 h-1.5 rounded-full bg-amber-500"></span>
            النموذج جاهز — الرفع قريبًا
          </span>
        );
      case 'planned':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-slate-500/15 text-slate-600 dark:text-slate-400 border border-slate-500/30">
            <span className="w-1.5 h-1.5 rounded-full bg-slate-400"></span>
            قيد التطوير
          </span>
        );
      case 'erp':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-purple-500/15 text-purple-700 dark:text-purple-400 border border-purple-500/30">
            <span className="w-1.5 h-1.5 rounded-full bg-purple-500"></span>
            من Gate ERP
          </span>
        );
      default:
        return null;
    }
  };

  // Render single step card
  const renderStepCard = (step: ImportCatalogStep) => {
    const curUpload = uploadState[step.key];
    const userCanUpload = canUploadStep(step);
    const hasTemplate = Boolean(step.templateUrl);
    const hasColumns = Boolean(step.columns && step.columns.length > 0);
    const isCompleted = completedSteps.has(step.key);

    // Dependencies soft warning check
    const uncompletedDeps = step.dependsOn.filter((depKey) => !completedSteps.has(depKey));
    const showSoftDepWarning = step.dependsOn.length > 0 && uncompletedDeps.length > 0 && !isCompleted;

    return (
      <div
        key={step.key}
        id={`step-${step.key}`}
        className={`relative border rounded-xl p-4 sm:p-6 transition-all bg-[var(--surface)] text-[var(--ink)] shadow-2xs ${
          isCompleted ? 'border-emerald-500/40 bg-emerald-500/5' : 'border-[var(--line)]'
        }`}
      >
        {/* Card Header */}
        <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3 mb-3">
          <div className="flex items-start gap-3">
            {/* Step Order Badge */}
            <div
              className={`flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center font-bold text-sm ${
                isCompleted
                  ? 'bg-emerald-600 text-white'
                  : 'bg-[var(--surface-muted)] text-[var(--ink)] border border-[var(--line)]'
              }`}
            >
              {isCompleted ? <Check size={16} /> : step.order}
            </div>

            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">{step.labelAr}</h3>
                {renderStatusBadge(step.status)}
                {isCompleted && (
                  <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium bg-emerald-500/20 text-emerald-800 dark:text-emerald-300">
                    <CheckCircle2 size={12} />
                    تم الاستيراد
                  </span>
                )}
              </div>
              <p className="text-sm text-[var(--ink-muted)] mt-1">{step.purposeAr}</p>
            </div>
          </div>

          {/* Permission badge */}
          <div className="self-start sm:self-auto flex-shrink-0">
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs bg-[var(--surface-muted)] text-[var(--ink-muted)] border border-[var(--line)]">
              الصلاحية: <code className="font-mono text-xs">{step.permission}</code>
            </span>
          </div>
        </div>

        {/* DependsOn links */}
        {step.dependsOn.length > 0 && (
          <div className="flex flex-wrap items-center gap-1.5 text-xs text-[var(--ink-muted)] mb-3 bg-[var(--surface-muted)]/50 p-2 rounded-lg border border-[var(--line)]">
            <span className="font-semibold text-[var(--ink)]">بعد:</span>
            {step.dependsOn.map((depKey) => {
              const depDone = completedSteps.has(depKey);
              return (
                <a
                  key={depKey}
                  href={`#step-${depKey}`}
                  className={`inline-flex items-center gap-1 px-2 py-0.5 rounded border transition-colors ${
                    depDone
                      ? 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border-emerald-500/20'
                      : 'bg-[var(--surface)] text-[var(--ink)] border-[var(--line)] hover:border-[var(--accent)]'
                  }`}
                >
                  {depDone && <Check size={11} />}
                  <span>{getStepLabelByKey(depKey)}</span>
                </a>
              );
            })}
          </div>
        )}

        {/* Soft Dependency Warning (I2) */}
        {showSoftDepWarning && (
          <div className="mb-3 flex items-start gap-2 p-2.5 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-800 dark:text-amber-300 text-xs">
            <AlertTriangle size={15} className="flex-shrink-0 mt-0.5" />
            <div>
              <span>تنبيه غير ملزم: يُفضل إكمال واستيراد </span>
              <strong>{uncompletedDeps.map((k) => `«${getStepLabelByKey(k)}»`).join(' و ')}</strong>
              <span> أولاً لضمان اكتمال المطابقات والأرصدة المرتبطة.</span>
            </div>
          </div>
        )}

        {/* Note if present */}
        {step.noteAr && (
          <div className="mb-3 flex items-start gap-2 p-2.5 rounded-lg bg-[var(--surface-muted)] border border-[var(--line)] text-xs text-[var(--ink-muted)]">
            <AlertCircle size={14} className="flex-shrink-0 mt-0.5 text-[var(--accent)]" />
            <span>{step.noteAr}</span>
          </div>
        )}

        {/* Actions bar: Download template, Columns button, Upload UI */}
        <div className="pt-3 border-t border-[var(--line)] space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2">
              {/* Template Download Button */}
              {hasTemplate && (
                <button
                  type="button"
                  onClick={() => handleDownloadTemplate(step)}
                  disabled={downloadingKey === step.key}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs sm:text-sm font-medium bg-[var(--surface-muted)] text-[var(--ink)] hover:bg-[var(--line)] border border-[var(--line)] transition-colors disabled:opacity-50"
                >
                  {downloadingKey === step.key ? (
                    <RefreshCw size={14} className="animate-spin" />
                  ) : (
                    <Download size={14} className="text-[var(--accent)]" />
                  )}
                  <span>تحميل النموذج</span>
                </button>
              )}

              {/* Columns Drawer Button */}
              {hasColumns && (
                <button
                  type="button"
                  onClick={() => setColumnDrawerStep(step)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs sm:text-sm font-medium bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--surface-muted)] border border-[var(--line)] transition-colors"
                >
                  <Eye size={14} className="text-[var(--ink-muted)]" />
                  <span>الأعمدة ({step.columns.length})</span>
                </button>
              )}
            </div>

            {/* Step Upload Action Status */}
            {!userCanUpload && (
              <span className="text-xs text-[var(--ink-muted)] italic">
                (زر الرفع متاح فقط لحاملي صلاحية {step.permission})
              </span>
            )}
          </div>

          {/* Upload Area for Allowed Users */}
          {userCanUpload && (
            <div className="mt-2">
              {/* 1. XLSX or JSON-ROWS Flow */}
              {(step.upload === 'xlsx' || step.upload === 'json-rows') && (
                <div>
                  {step.status === 'template_only' ? (
                    <div className="p-3 rounded-lg bg-[var(--surface-muted)] border border-dashed border-[var(--line)] text-center text-xs text-[var(--ink-muted)]">
                      النموذج متاح للتحميل والتحضير؛ ستتاح واجهة الرفع والمعاينة قريباً (R6c-D3).
                    </div>
                  ) : (
                    <div>
                      {/* File selector when no active preview */}
                      {!curUpload?.previewResult && (
                        <div>
                          <input
                            ref={(el) => {
                              fileInputRefs.current[step.key] = el;
                            }}
                            type="file"
                            accept={
                              step.upload === 'json-rows'
                                ? '.xlsx,.csv,.json'
                                : '.xlsx'
                            }
                            onChange={(e) => {
                              const f = e.target.files?.[0];
                              if (f) handleFileSelect(step, f);
                            }}
                            className="hidden"
                            id={`file-input-${step.key}`}
                          />
                          <label
                            htmlFor={`file-input-${step.key}`}
                            className={`flex flex-col items-center justify-center p-4 border-2 border-dashed rounded-xl cursor-pointer transition-colors ${
                              curUpload?.previewing
                                ? 'opacity-50 pointer-events-none'
                                : 'border-[var(--line)] hover:border-[var(--accent)] bg-[var(--surface-muted)]/40 hover:bg-[var(--surface-muted)]'
                            }`}
                          >
                            {curUpload?.previewing ? (
                              <div className="flex items-center gap-2 text-sm text-[var(--ink)]">
                                <RefreshCw size={18} className="animate-spin text-[var(--accent)]" />
                                <span>جارٍ قراءة ومعاينة الملف...</span>
                              </div>
                            ) : (
                              <div className="flex flex-col items-center text-center">
                                <Upload size={22} className="text-[var(--accent)] mb-1" />
                                <span className="text-xs sm:text-sm font-medium text-[var(--ink)]">
                                  اختر ملف {step.upload === 'json-rows' ? 'Excel أو CSV أو JSON' : 'Excel (xlsx)'} للرفع والمعاينة
                                </span>
                                <span className="text-[11px] text-[var(--ink-muted)] mt-0.5">
                                  سيتم فحص البيانات وإظهار تقرير المعاينة قبل الاعتماد
                                </span>
                              </div>
                            )}
                          </label>
                        </div>
                      )}

                      {/* Error state */}
                      {curUpload?.error && (
                        <div className="mt-3 p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-700 dark:text-red-400 text-xs flex items-start justify-between gap-2">
                          <div className="flex items-start gap-2">
                            <AlertCircle size={15} className="flex-shrink-0 mt-0.5" />
                            <span>{curUpload.error}</span>
                          </div>
                          <button
                            type="button"
                            onClick={() => handleResetStep(step.key)}
                            className="text-xs font-semibold hover:underline flex-shrink-0"
                          >
                            إعادة المحاولة
                          </button>
                        </div>
                      )}

                      {/* Preview Results Table */}
                      {curUpload?.previewResult && (
                        <div className="mt-3 p-3 sm:p-4 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/40 space-y-3">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <div className="flex items-center gap-2">
                              <CheckCircle2 size={18} className="text-emerald-600 dark:text-emerald-400" />
                              <span className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                                معاينة الملف: {curUpload.file?.name}
                              </span>
                            </div>

                            <button
                              type="button"
                              onClick={() => handleResetStep(step.key)}
                              className="text-xs text-[var(--ink-muted)] hover:text-[var(--ink)] flex items-center gap-1"
                            >
                              <X size={14} />
                              <span>إلغاء واختيار ملف آخر</span>
                            </button>
                          </div>

                          {/* Preview Stats / Counts */}
                          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
                            <div className="p-2 rounded bg-[var(--surface)] border border-[var(--line)]">
                              <span className="text-[var(--ink-muted)] block">إجمالي الصفوف</span>
                              <span className="font-bold text-sm text-[var(--ink)]">
                                {curUpload.previewResult.rows?.length ??
                                  curUpload.previewResult.count ??
                                  0}
                              </span>
                            </div>

                            {curUpload.previewResult.counts && (
                              <>
                                <div className="p-2 rounded bg-[var(--surface)] border border-[var(--line)]">
                                  <span className="text-[var(--ink-muted)] block">جديد</span>
                                  <span className="font-bold text-sm text-emerald-600 dark:text-emerald-400">
                                    {curUpload.previewResult.counts.new ?? 0}
                                  </span>
                                </div>
                                <div className="p-2 rounded bg-[var(--surface)] border border-[var(--line)]">
                                  <span className="text-[var(--ink-muted)] block">تحديث</span>
                                  <span className="font-bold text-sm text-blue-600 dark:text-blue-400">
                                    {curUpload.previewResult.counts.updated ?? 0}
                                  </span>
                                </div>
                                <div className="p-2 rounded bg-[var(--surface)] border border-[var(--line)]">
                                  <span className="text-[var(--ink-muted)] block">مطابق دون تغيير</span>
                                  <span className="font-bold text-sm text-[var(--ink-muted)]">
                                    {curUpload.previewResult.counts.unchanged ?? 0}
                                  </span>
                                </div>
                              </>
                            )}

                            {curUpload.previewResult.fingerprint && (
                              <div className="p-2 rounded bg-[var(--surface)] border border-[var(--line)] col-span-2 sm:col-span-1">
                                <span className="text-[var(--ink-muted)] block">بصمة المعاينة</span>
                                <code className="font-mono text-[11px] text-[var(--ink)] truncate block" title={curUpload.previewResult.fingerprint}>
                                  {curUpload.previewResult.fingerprint.substring(0, 10)}...
                                </code>
                              </div>
                            )}
                          </div>

                          {/* Errors List if any */}
                          {curUpload.previewResult.errors && curUpload.previewResult.errors.length > 0 && (
                            <div className="p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-xs space-y-1">
                              <span className="font-bold text-red-700 dark:text-red-400 block">
                                أخطاء بالملف ({curUpload.previewResult.errors.length}):
                              </span>
                              <ul className="list-disc list-inside space-y-0.5 text-red-600 dark:text-red-300 max-h-32 overflow-y-auto">
                                {curUpload.previewResult.errors.map((errItem: any, idx: number) => (
                                  <li key={idx}>
                                    {typeof errItem === 'string'
                                      ? errItem
                                      : `الصف ${errItem.row ?? '-'}: ${errItem.error || errItem.message || JSON.stringify(errItem)}`}
                                  </li>
                                ))}
                              </ul>
                            </div>
                          )}

                          {/* Sample Rows Table Preview */}
                          {curUpload.previewResult.rows && curUpload.previewResult.rows.length > 0 && (() => {
                            const pRows = curUpload.previewResult.rows.map((r: any) =>
                              r && typeof r === 'object' && r.data ? r.data : r
                            );
                            const sampleCols = Object.keys(pRows[0] || {}).slice(0, 5);
                            return (
                              <div className="overflow-x-auto rounded-lg border border-[var(--line)] bg-[var(--surface)] max-h-48">
                                <table className="w-full text-xs text-start">
                                  <thead className="bg-[var(--surface-muted)] text-[var(--ink)] sticky top-0">
                                    <tr>
                                      <th className="p-2 text-start font-semibold border-b border-[var(--line)]">#</th>
                                      {sampleCols.map((colKey) => (
                                        <th key={colKey} className="p-2 text-start font-semibold border-b border-[var(--line)]">
                                          {colKey}
                                        </th>
                                      ))}
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {pRows.slice(0, 5).map((row: any, rIdx: number) => (
                                      <tr key={rIdx} className="border-b border-[var(--line)] last:border-0 hover:bg-[var(--surface-muted)]/30">
                                        <td className="p-2 text-[var(--ink-muted)]">{rIdx + 1}</td>
                                        {sampleCols.map((colKey) => (
                                          <td key={colKey} className="p-2 text-[var(--ink)] truncate max-w-[150px]">
                                            {String(row[colKey] ?? '')}
                                          </td>
                                        ))}
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            );
                          })()}

                          {/* Commit Button */}
                          <div className="flex items-center justify-end gap-2 pt-2">
                            <button
                              type="button"
                              onClick={() => handleCommit(step)}
                              disabled={
                                curUpload.committing ||
                                Boolean(curUpload.previewResult.errors && curUpload.previewResult.errors.length > 0)
                              }
                              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-xs sm:text-sm font-semibold bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50 transition-colors shadow-2xs"
                            >
                              {curUpload.committing ? (
                                <>
                                  <RefreshCw size={15} className="animate-spin" />
                                  <span>جارٍ اعتماد البيانات...</span>
                                </>
                              ) : (
                                <>
                                  <CheckCircle2 size={16} />
                                  <span>اعتماد البيانات واستيرادها</span>
                                </>
                              )}
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}

              {/* 2. Platform-File Flow (Ninja / HungerStation / Keeta) */}
              {step.upload === 'platform-file' && (
                <div className="flex flex-wrap items-center gap-2">
                  {step.key === 'ninja_invoice' && (
                    <Link
                      href="/payroll/?tab=ninja_upload"
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs sm:text-sm font-medium bg-[var(--accent)] text-white hover:opacity-90 transition-opacity"
                    >
                      <ExternalLink size={14} />
                      <span>فتح شاشة رفع ومطابقة فاتورة نينجا</span>
                    </Link>
                  )}

                  {step.key === 'hungerstation_invoice' && (
                    <Link
                      href="/payroll/?platform=hungerstation&subTab=settlements"
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs sm:text-sm font-medium bg-[var(--accent)] text-white hover:opacity-90 transition-opacity"
                    >
                      <ExternalLink size={14} />
                      <span>الانتقال لشاشة فواتير وتسويات هنقرستيشن</span>
                    </Link>
                  )}

                  {step.key === 'keeta_invoice' && (
                    <span className="text-xs text-[var(--ink-muted)] bg-[var(--surface-muted)] px-3 py-1.5 rounded-lg border border-[var(--line)]">
                      مستورد فاتورة كيتا قيد التطوير (R6c-D2)
                    </span>
                  )}
                </div>
              )}

              {/* 3. ERP Flow (Chefz) */}
              {step.upload === 'erp' && (
                <div>
                  <button
                    type="button"
                    onClick={() => setErpDrawerOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs sm:text-sm font-medium bg-purple-600 text-white hover:bg-purple-700 transition-colors"
                  >
                    <RefreshCw size={14} />
                    <span>سحب وتشغيل من Gate ERP</span>
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="min-w-0 max-w-full space-y-6">
      {/* Page Header */}
      <PageHeader
        title="مركز الاستيراد"
        description="دليل ومسار استيراد البيانات التشغيلية والمالية وفق الترتيب المعتمد (12 خطوة)"
      >
        <button
          type="button"
          onClick={loadCatalog}
          disabled={loading}
          className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-xs sm:text-sm font-medium bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--surface-muted)] border border-[var(--line)] transition-colors shadow-2xs"
        >
          <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
          <span>تحديث الدليل</span>
        </button>
      </PageHeader>

      {/* Loading State Skeleton (I2) */}
      {loading && (
        <div className="space-y-4">
          {[1, 2, 3].map((g) => (
            <div key={g} className="p-4 sm:p-6 border border-[var(--line)] rounded-xl bg-[var(--surface)] space-y-3 animate-pulse">
              <div className="h-5 w-40 bg-[var(--line)] rounded"></div>
              <div className="h-4 w-72 bg-[var(--line)]/60 rounded"></div>
              <div className="mt-4 space-y-3">
                <div className="h-20 bg-[var(--surface-muted)] rounded-lg"></div>
                <div className="h-20 bg-[var(--surface-muted)] rounded-lg"></div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Catalog Error State (I2) */}
      {!loading && error && (
        <div className="p-6 border border-red-500/20 rounded-xl bg-red-500/10 text-center space-y-3">
          <AlertCircle size={32} className="mx-auto text-red-600 dark:text-red-400" />
          <h3 className="text-base font-bold text-red-800 dark:text-red-300">تعذر تحميل دليل الاستيراد</h3>
          <p className="text-xs sm:text-sm text-red-700 dark:text-red-400 max-w-md mx-auto">{error}</p>
          <button
            type="button"
            onClick={loadCatalog}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-xs sm:text-sm font-semibold bg-red-600 text-white hover:bg-red-700 transition-colors"
          >
            <RefreshCw size={14} />
            <span>إعادة المحاولة</span>
          </button>
        </div>
      )}

      {/* Catalog Rendered in Vertical Stepper */}
      {!loading && !error && catalog.length > 0 && (
        <div className="space-y-8">
          {/* Group 1: التأسيس */}
          {setupSteps.length > 0 && (
            <section className="space-y-4">
              <div className="flex items-center gap-2 pb-2 border-b border-[var(--line)]">
                <div className="w-8 h-8 rounded-lg bg-[var(--accent)]/10 text-[var(--accent)] flex items-center justify-center">
                  <Layers size={18} />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">1. التأسيس</h2>
                    <span className="px-2 py-0.5 rounded text-xs bg-[var(--surface-muted)] text-[var(--ink-muted)] border border-[var(--line)]">
                      الخطوات 1 - 2
                    </span>
                  </div>
                  <p className="text-xs text-[var(--ink-muted)]">
                    تهيئة السجلات التجارية والشركات والمناديب، وهو الأساس لكل القيود والمطابقات اللاحقة.
                  </p>
                </div>
              </div>

              <div className="space-y-4">
                {setupSteps.map((step) => renderStepCard(step))}
              </div>
            </section>
          )}

          {/* Group 2: الأرصدة الافتتاحية */}
          {openingSteps.length > 0 && (
            <section className="space-y-4">
              <div className="flex items-center gap-2 pb-2 border-b border-[var(--line)]">
                <div className="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center">
                  <Landmark size={18} />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">2. الأرصدة الافتتاحية</h2>
                    <span className="px-2 py-0.5 rounded text-xs bg-[var(--surface-muted)] text-[var(--ink-muted)] border border-[var(--line)]">
                      الخطوات 3 - 5
                    </span>
                  </div>
                  <p className="text-xs text-[var(--ink-muted)]">
                    تثبيت أرصدة الخزائن والبنوك والمديونيات القائمة قبل بدء العمليات الحالية.
                  </p>
                </div>
              </div>

              <div className="space-y-4">
                {openingSteps.map((step) => renderStepCard(step))}
              </div>
            </section>
          )}

          {/* Group 3: المنصات */}
          {platformSteps.length > 0 && (
            <section className="space-y-4">
              <div className="flex items-center gap-2 pb-2 border-b border-[var(--line)]">
                <div className="w-8 h-8 rounded-lg bg-blue-500/10 text-blue-600 dark:text-blue-400 flex items-center justify-center">
                  <Truck size={18} />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">3. المنصات</h2>
                    <span className="px-2 py-0.5 rounded text-xs bg-[var(--surface-muted)] text-[var(--ink-muted)] border border-[var(--line)]">
                      الخطوات 6 - 12 (نينجا، كيتا، هنقرستيشن، شفز)
                    </span>
                  </div>
                  <p className="text-xs text-[var(--ink-muted)]">
                    لكل منصة: مطابقة واعتماد «الفاتورة» ثم رفع واعتماد «التشغيل» للمناديب.
                  </p>
                </div>
              </div>

              <div className="space-y-4">
                {platformSteps.map((step) => renderStepCard(step))}
              </div>
            </section>
          )}
        </div>
      )}

      {/* Columns Drawer (Responsive on desktop, solves 360px overflow cleanly) */}
      <Drawer
        open={Boolean(columnDrawerStep)}
        onClose={() => setColumnDrawerStep(null)}
        title={`مواصفات أعمدة: ${columnDrawerStep?.labelAr || ''}`}
        subtitle={`${columnDrawerStep?.columns.length || 0} عموداً في نموذج الإدخال`}
        width="max-w-md sm:max-w-2xl"
      >
        {columnDrawerStep && (
          <div className="space-y-4 text-xs">
            {/* Header info */}
            <div className="p-3 rounded-lg bg-[var(--surface-muted)] border border-[var(--line)] space-y-1">
              <div className="flex items-center justify-between">
                <span className="font-semibold text-[var(--ink)]">معرّف الخطوة:</span>
                <code className="font-mono text-xs">{columnDrawerStep.key}</code>
              </div>
              <div className="flex items-center justify-between">
                <span className="font-semibold text-[var(--ink)]">الحقول الإلزامية:</span>
                <span className="font-bold text-red-600 dark:text-red-400">
                  {columnDrawerStep.columns.filter((c) => c.required).length} من {columnDrawerStep.columns.length}
                </span>
              </div>
              {columnDrawerStep.purposeAr && (
                <div className="text-[var(--ink-muted)] pt-1 text-[11px]">
                  {columnDrawerStep.purposeAr}
                </div>
              )}
            </div>

            {/* Columns Table */}
            <div className="overflow-x-auto rounded-lg border border-[var(--line)]">
              <table className="w-full text-start">
                <thead className="bg-[var(--surface-muted)] text-[var(--ink)]">
                  <tr>
                    <th className="p-2.5 text-start font-semibold border-b border-[var(--line)]">#</th>
                    <th className="p-2.5 text-start font-semibold border-b border-[var(--line)]">اسم العمود (Header)</th>
                    <th className="p-2.5 text-start font-semibold border-b border-[var(--line)]">الإلزامية</th>
                    <th className="p-2.5 text-start font-semibold border-b border-[var(--line)]">النوع</th>
                    <th className="p-2.5 text-start font-semibold border-b border-[var(--line)]">الخيارات المسموحة</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {columnDrawerStep.columns.map((col, idx) => {
                    const isExpanded = expandedChoiceColumns.has(col.name);
                    const hasChoices = col.choices && col.choices.length > 0;
                    const choicesList = col.choices || [];
                    const isCollapsed = hasChoices && choicesList.length > 6 && !isExpanded;
                    const displayedChoices = isCollapsed ? choicesList.slice(0, 6) : choicesList;

                    return (
                      <tr key={col.name} className="hover:bg-[var(--surface-muted)]/40">
                        <td className="p-2.5 text-[var(--ink-muted)]">{idx + 1}</td>
                        <td className="p-2.5 font-medium text-[var(--ink)]">
                          <div className="flex items-center gap-1.5">
                            <span className="font-mono">{col.name}</span>
                            {col.identity && (
                              <span className="px-1.5 py-0.2 rounded text-[10px] bg-blue-500/10 text-blue-700 dark:text-blue-400 border border-blue-500/20">
                                معرّف فريد
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="p-2.5">
                          {col.required ? (
                            <span className="inline-flex items-center gap-1 font-semibold text-red-600 dark:text-red-400">
                              <span className="text-red-500">●</span>
                              <span>إلزامي</span>
                            </span>
                          ) : (
                            <span className="text-[var(--ink-muted)]">اختياري</span>
                          )}
                        </td>
                        <td className="p-2.5">
                          <span className="px-2 py-0.5 rounded text-[11px] bg-[var(--surface-muted)] text-[var(--ink)] border border-[var(--line)]">
                            {col.type === 'number' ? 'رقم' : col.type === 'choice' ? 'قائمة خيارات' : 'نص'}
                          </span>
                        </td>
                        <td className="p-2.5 max-w-[200px]">
                          {hasChoices ? (
                            <div className="space-y-1">
                              <div className="flex flex-wrap gap-1">
                                {displayedChoices.map((choice, cIdx) => (
                                  <span
                                    key={cIdx}
                                    className="px-1.5 py-0.2 rounded text-[10px] bg-[var(--surface-muted)] text-[var(--ink-muted)] border border-[var(--line)]"
                                  >
                                    {choice}
                                  </span>
                                ))}
                              </div>
                              {choicesList.length > 6 && (
                                <button
                                  type="button"
                                  onClick={() => {
                                    setExpandedChoiceColumns((prev) => {
                                      const next = new Set(prev);
                                      if (next.has(col.name)) next.delete(col.name);
                                      else next.add(col.name);
                                      return next;
                                    });
                                  }}
                                  className="text-[11px] text-[var(--accent)] hover:underline font-medium block"
                                >
                                  {isExpanded
                                    ? 'طي الخيارات'
                                    : `+ ${choicesList.length - 6} خيارات أخرى (عرض الكل)`}
                                </button>
                              )}
                            </div>
                          ) : (
                            <span className="text-[var(--ink-muted)]">-</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {columnDrawerStep.templateUrl && (
              <div className="pt-2 flex justify-end">
                <button
                  type="button"
                  onClick={() => handleDownloadTemplate(columnDrawerStep)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-[var(--accent)] text-white hover:opacity-90 transition-opacity"
                >
                  <Download size={14} />
                  <span>تحميل النموذج الرسمي</span>
                </button>
              </div>
            )}
          </div>
        )}
      </Drawer>

      {/* ERP Sync Drawer (for Chefz operations) */}
      <ErpSyncDrawer open={erpDrawerOpen} onClose={() => setErpDrawerOpen(false)} />
    </div>
  );
}
