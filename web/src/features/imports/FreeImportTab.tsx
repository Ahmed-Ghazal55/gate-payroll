'use client';

import React, { useState, useEffect, Suspense } from 'react';
import Link from 'next/link';
import {
  Upload,
  FileSpreadsheet,
  CheckCircle2,
  AlertCircle,
  ArrowRight,
  ArrowLeft,
  Search,
  Filter,
  RefreshCw,
  Layers,
  FileText,
  Clock,
  Check,
  ChevronRight,
  Sparkles,
} from 'lucide-react';
import { api } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { SectionTabs, SectionTabItem } from '../../components/ui/SectionTabs';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';

function ImportsPageInner() {
  const { user } = useAuth();
  const { toast } = useToast();

  const [activeTab, setActiveTab] = useState<'wizard' | 'batches'>('wizard');

  // Wizard state: 1: inspect -> 2: sheet -> 3: mapping -> 4: preview -> 5: commit
  const [wizardStep, setWizardStep] = useState<number>(1);

  // File state
  const [file, setFile] = useState<File | null>(null);
  const [fileBase64, setFileBase64] = useState<string>('');
  const [inspecting, setInspecting] = useState<boolean>(false);
  const [importId, setImportId] = useState<string>('');
  const [sheets, setSheets] = useState<string[]>([]);
  const [selectedSheet, setSelectedSheet] = useState<string>('');
  const [headerRow, setHeaderRow] = useState<number>(1);

  // Sheet headers & sample state
  const [loadingSheet, setLoadingSheet] = useState<boolean>(false);
  const [sheetHeaders, setSheetHeaders] = useState<string[]>([]);
  const [sheetRowsCount, setSheetRowsCount] = useState<number>(0);
  const [sampleRows, setSampleRows] = useState<any[]>([]);

  // Mapping state
  const [platform, setPlatform] = useState<string>('HungerStation');
  const [period, setPeriod] = useState<string>('2026-10');
  const [mappingIqama, setMappingIqama] = useState<string>('');
  const [mappingName, setMappingName] = useState<string>('');
  const [mappingOrders, setMappingOrders] = useState<string>('');
  const [mappingAmount, setMappingAmount] = useState<string>('');

  // Preview state
  const [previewing, setPreviewing] = useState<boolean>(false);
  const [previewResult, setPreviewResult] = useState<any>(null);

  // Commit state
  const [committing, setCommitting] = useState<boolean>(false);
  const [commitResult, setCommitResult] = useState<any>(null);

  // Batches history state
  const [batches, setBatches] = useState<any[]>([]);
  const [loadingBatches, setLoadingBatches] = useState<boolean>(true);
  const [batchesError, setBatchesError] = useState<string | null>(null);

  // Load Batches
  const loadBatches = async () => {
    setLoadingBatches(true);
    setBatchesError(null);
    try {
      const res = await api.getImportBatches();
      setBatches(Array.isArray(res) ? res : []);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل سجل الاستيراد';
      setBatchesError(msg);
    } finally {
      setLoadingBatches(false);
    }
  };

  useEffect(() => {
    loadBatches();
  }, []);

  // Step 1: Handle File Selection & Inspect
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0];
    if (!f) return;
    setFile(f);
    const reader = new FileReader();
    reader.onload = () => {
      const res = reader.result as string;
      setFileBase64(res.split(',')[1] || '');
    };
    reader.readAsDataURL(f);
  };

  const handleInspect = async () => {
    if (!file || !fileBase64) {
      toast('يرجى اختيار ملف Excel أو CSV للفحص', 'warning');
      return;
    }
    setInspecting(true);
    try {
      const res = await api.inspectImport({
        filename: file.name,
        dataBase64: fileBase64,
      });
      setImportId(res.id);
      setSheets(res.sheets || []);
      if (res.sheets && res.sheets.length > 0) {
        setSelectedSheet(res.sheets[0]);
      }
      toast('تم فحص الملف واستخراج أوراق العمل بنجاح', 'success');
      setWizardStep(2);
    } catch (err: any) {
      toast(err.message || 'فشل فحص الملف', 'danger');
    } finally {
      setInspecting(false);
    }
  };

  // Step 2: Load Sheet Headers
  const handleLoadSheet = async () => {
    if (!importId || !selectedSheet) return;
    setLoadingSheet(true);
    try {
      const res = await api.getImportSheet(importId, selectedSheet, headerRow);
      setSheetHeaders(res.headers || []);
      setSheetRowsCount(res.rowsCount || 0);
      setSampleRows(res.sampleRows || []);

      // Auto-detect mappings
      const hLower = (res.headers || []).map((h) => h.toLowerCase());
      const findH = (candidates: string[]) =>
        res.headers.find((h) => candidates.some((c) => h.toLowerCase().includes(c))) || '';

      setMappingIqama(findH(['iqama', 'إقامة', 'رقم الهوية', 'identity', 'rider_id', 'courier']));
      setMappingName(findH(['name', 'اسم', 'الاسم', 'rider']));
      setMappingOrders(findH(['order', 'طلبات', 'completed', 'deliveries']));
      setMappingAmount(findH(['amount', 'مبلغ', 'net', 'payment', 'total']));

      setWizardStep(3);
    } catch (err: any) {
      toast(err.message || 'تعذر تحميل بيانات ورقة العمل', 'danger');
    } finally {
      setLoadingSheet(false);
    }
  };

  // Step 3: Preview Mapped Rows
  const handlePreview = async () => {
    if (!importId || !selectedSheet) return;
    if (!mappingIqama) {
      toast('حقل رقم الإقامة إلزامي لمطابقة المناديب', 'warning');
      return;
    }
    setPreviewing(true);
    try {
      const spec = {
        sheet: selectedSheet,
        headerRow,
        platform,
        period,
        mapping: {
          iqama: mappingIqama,
          name: mappingName || undefined,
          orders: mappingOrders || undefined,
          amount: mappingAmount || undefined,
        },
      };
      const res = await api.previewImport(importId, spec);
      setPreviewResult(res);
      toast('تمت المعاينة والتحقق من صحة السطور بنجاح', 'success');
      setWizardStep(4);
    } catch (err: any) {
      toast(err.message || 'فشلت معاينة الاستيراد', 'danger');
    } finally {
      setPreviewing(false);
    }
  };

  // Step 4: Commit Import
  const handleCommit = async () => {
    if (!importId || !selectedSheet || !previewResult) return;
    setCommitting(true);
    try {
      const spec = {
        sheet: selectedSheet,
        headerRow,
        platform,
        period,
        fingerprint: previewResult.fingerprint,
        confirm: true,
        mapping: {
          iqama: mappingIqama,
          name: mappingName || undefined,
          orders: mappingOrders || undefined,
          amount: mappingAmount || undefined,
        },
      };
      const res = await api.commitImport(importId, spec);
      setCommitResult(res);
      toast('تم اعتماد وحفظ بيانات التشغيل في قاعدة البيانات بنجاح', 'success');
      setWizardStep(5);
      loadBatches();
    } catch (err: any) {
      toast(err.message || 'تعذر اعتماد الاستيراد', 'danger');
    } finally {
      setCommitting(false);
    }
  };

  // Reset Wizard
  const handleReset = () => {
    setWizardStep(1);
    setFile(null);
    setFileBase64('');
    setImportId('');
    setSheets([]);
    setPreviewResult(null);
    setCommitResult(null);
  };

  const sectionTabs: SectionTabItem[] = [
    { id: 'wizard', label: 'معالج الاستيراد (5 مراحل)', icon: <Upload size={16} /> },
    { id: 'batches', label: 'دفعات الاستيراد المؤرشفة', icon: <Layers size={16} />, badge: batches.length },
  ];

  const stepsList = [
    { num: 1, label: 'رفع وفحص الملف' },
    { num: 2, label: 'تحديد الورقة' },
    { num: 3, label: 'مطابقة الأعمدة' },
    { num: 4, label: 'المعاينة والفحص' },
    { num: 5, label: 'الاعتماد النهائي' },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        title="استيراد بيانات التشغيل والمنصات"
        eyebrow="العمليات والمسيرات"
        description="معالج الاستيراد الذكي لملفات Excel/CSV: فحص الأوراق، مطابقة الأعمدة تلقائياً، والتحقق المعتمد قبل الترحيل."
        primaryAction={{
          label: 'استيراد ملف جديد',
          icon: <Upload size={15} />,
          onClick: handleReset,
        }}
      />

      {/* Tabs */}
      <SectionTabs
        tabs={sectionTabs}
        activeTab={activeTab}
        onChange={(t: any) => setActiveTab(t)}
      />

      {/* Tab 1: Wizard */}
      {activeTab === 'wizard' && (
        <div className="space-y-6">
          {/* Stepper Header */}
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <div className="flex items-center justify-between overflow-x-auto no-scrollbar gap-2">
              {stepsList.map((st) => {
                const isPassed = wizardStep > st.num;
                const isCurrent = wizardStep === st.num;
                return (
                  <div key={st.num} className="flex items-center gap-2 shrink-0">
                    <div
                      className={`h-7 w-7 rounded-full flex items-center justify-center font-bold text-xs ${
                        isPassed
                          ? 'bg-[var(--money-pos)] text-white'
                          : isCurrent
                          ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                          : 'bg-[var(--raised)] text-[var(--muted)]'
                      }`}
                    >
                      {isPassed ? <Check size={14} /> : st.num}
                    </div>
                    <span
                      className={`text-xs font-semibold whitespace-nowrap ${
                        isCurrent ? 'text-[var(--ink)] font-bold' : 'text-[var(--muted)]'
                      }`}
                    >
                      {st.label}
                    </span>
                    {st.num < 5 && <ChevronRight size={14} className="text-[var(--line)] ms-2" />}
                  </div>
                );
              })}
            </div>
          </div>

          {/* Step 1: Inspect */}
          {wizardStep === 1 && (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs max-w-xl mx-auto space-y-4 text-center">
              <div className="h-14 w-14 rounded-2xl bg-[var(--accent)]/15 text-[var(--accent)] flex items-center justify-center mx-auto">
                <FileSpreadsheet size={28} />
              </div>
              <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                اختر ملف بيانات التشغيل (XLSX, XLS, CSV)
              </h3>
              <p className="text-xs text-[var(--muted)] leading-relaxed">
                يدعم المعالج ملفات المنصات الأصلية (هنقرستيشن، كيتا، نينجا، جاهز، شيفز، أمازون).
              </p>

              <div className="pt-2">
                <input
                  type="file"
                  accept=".xlsx,.xls,.csv"
                  onChange={handleFileChange}
                  className="w-full p-3 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden cursor-pointer"
                />
                {file && (
                  <p className="text-xs font-semibold text-[var(--accent)] mt-2">
                    الملف المحدد: {file.name} ({(file.size / 1024).toFixed(1)} KB)
                  </p>
                )}
              </div>

              <div className="pt-4 flex justify-end">
                <button
                  type="button"
                  disabled={!file || inspecting}
                  onClick={handleInspect}
                  className="w-full py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {inspecting ? 'جاري الفحص والمعاينة...' : 'فحص الملف واستخراج أوراق العمل'}
                </button>
              </div>
            </div>
          )}

          {/* Step 2: Sheet Selection */}
          {wizardStep === 2 && (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs max-w-xl mx-auto space-y-4">
              <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                تحديد ورقة العمل وصف العناوين
              </h3>
              <p className="text-xs text-[var(--muted)]">
                تم اكتشاف {sheets.length} أوراق عمل في الملف: <span className="font-mono text-[var(--ink)]">{file?.name}</span>
              </p>

              <div className="space-y-3 pt-2">
                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">ورقة العمل (Sheet):</label>
                  <select
                    value={selectedSheet}
                    onChange={(e) => setSelectedSheet(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    {sheets.map((sh) => (
                      <option key={sh} value={sh}>
                        {sh}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">رقم صف العناوين (Header Row):</label>
                  <input
                    type="number"
                    min={1}
                    max={10}
                    value={headerRow}
                    onChange={(e) => setHeaderRow(Number(e.target.value) || 1)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs num focus:outline-hidden"
                  />
                </div>
              </div>

              <div className="pt-4 flex justify-between gap-3">
                <button
                  type="button"
                  onClick={() => setWizardStep(1)}
                  className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold hover:bg-[var(--raised)] transition"
                >
                  السابق
                </button>
                <button
                  type="button"
                  disabled={loadingSheet}
                  onClick={handleLoadSheet}
                  className="px-5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {loadingSheet ? 'جاري القراءة...' : 'التالي: مطابقة الأعمدة'}
                </button>
              </div>
            </div>
          )}

          {/* Step 3: Column Mapping */}
          {wizardStep === 3 && (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs max-w-2xl mx-auto space-y-4">
              <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                مطابقة أعمدة ورقة العمل مع حقول المنظومة
              </h3>
              <p className="text-xs text-[var(--muted)]">
                الورقة: <span className="font-mono text-[var(--ink)]">{selectedSheet}</span> ({sheetRowsCount} صف بيانات تم العثور عليها).
              </p>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 pt-2">
                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">المنصة المستهدفة:</label>
                  <select
                    value={platform}
                    onChange={(e) => setPlatform(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    <option value="HungerStation">هنقرستيشن (HungerStation)</option>
                    <option value="Keeta">كيتا (Keeta)</option>
                    <option value="Ninja">نينجا (Ninja)</option>
                    <option value="TheChefz">ذا شيفز (The Chefz)</option>
                    <option value="Jahez">جاهز (Jahez)</option>
                    <option value="Amazon">أمازون (Amazon)</option>
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">فترة التشغيل (الشهر):</label>
                  <input
                    type="month"
                    value={period}
                    onChange={(e) => setPeriod(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">
                    عمود رقم الإقامة / المعرف <span className="text-[var(--danger)]">*</span>:
                  </label>
                  <select
                    value={mappingIqama}
                    onChange={(e) => setMappingIqama(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    <option value="">-- اختر العمود --</option>
                    {sheetHeaders.map((h) => (
                      <option key={h} value={h}>
                        {h}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">عمود اسم المندوب (اختياري):</label>
                  <select
                    value={mappingName}
                    onChange={(e) => setMappingName(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    <option value="">-- بدون مطابقة --</option>
                    {sheetHeaders.map((h) => (
                      <option key={h} value={h}>
                        {h}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">عمود عدد الطلبات المنجزة:</label>
                  <select
                    value={mappingOrders}
                    onChange={(e) => setMappingOrders(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    <option value="">-- بدون مطابقة --</option>
                    {sheetHeaders.map((h) => (
                      <option key={h} value={h}>
                        {h}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="space-y-1">
                  <label className="text-xs font-bold text-[var(--ink)]">عمود إجمالي المستحقات / المبالغ:</label>
                  <select
                    value={mappingAmount}
                    onChange={(e) => setMappingAmount(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
                  >
                    <option value="">-- بدون مطابقة --</option>
                    {sheetHeaders.map((h) => (
                      <option key={h} value={h}>
                        {h}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="pt-4 flex justify-between gap-3">
                <button
                  type="button"
                  onClick={() => setWizardStep(2)}
                  className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold hover:bg-[var(--raised)] transition"
                >
                  السابق
                </button>
                <button
                  type="button"
                  disabled={previewing || !mappingIqama}
                  onClick={handlePreview}
                  className="px-5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {previewing ? 'جاري الفحص...' : 'التالي: المعاينة والتحقق'}
                </button>
              </div>
            </div>
          )}

          {/* Step 4: Preview */}
          {wizardStep === 4 && previewResult && (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs max-w-3xl mx-auto space-y-5">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                    معاينة نتائج فحص بيانات التشغيل
                  </h3>
                  <p className="text-xs text-[var(--muted)]">
                    المنصة: <span className="font-bold text-[var(--accent)]">{platform}</span> · الفترة: <span className="font-bold">{period}</span>
                  </p>
                </div>
                <span className="font-mono text-xs text-[var(--muted)]">
                  بصمة: {previewResult.fingerprint?.slice(0, 14)}...
                </span>
              </div>

              {/* Summary KPIs */}
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">السجلات المقروءة</div>
                  <div className="text-base font-bold num mt-0.5">{previewResult.validCount || 0}</div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">الأخطاء والملاحظات</div>
                  <div className="text-base font-bold num text-[var(--danger)] mt-0.5">
                    {previewResult.errors?.length || 0}
                  </div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">إجمالي الطلبات</div>
                  <div className="text-base font-bold num text-[var(--accent)] mt-0.5">
                    {previewResult.summary?.totalOrders || 0}
                  </div>
                </div>
                <div className="p-3 rounded-xl bg-[var(--raised)]">
                  <div className="text-[11px] text-[var(--muted)]">إجمالي المبالغ</div>
                  <div className="text-base font-bold num text-[var(--money-pos)] mt-0.5">
                    {(previewResult.summary?.totalAmount || 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                  </div>
                </div>
              </div>

              <div className="pt-4 flex justify-between gap-3">
                <button
                  type="button"
                  onClick={() => setWizardStep(3)}
                  className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold hover:bg-[var(--raised)] transition"
                >
                  تعديل المطابقة
                </button>
                <button
                  type="button"
                  disabled={committing}
                  onClick={handleCommit}
                  className="px-6 py-2.5 rounded-xl bg-[var(--money-pos)] text-white text-xs sm:text-sm font-bold hover:opacity-90 transition disabled:opacity-50 cursor-pointer shadow-xs"
                >
                  {committing ? 'جاري الاعتماد...' : 'اعتماد وحفظ الاستيراد في قاعدة البيانات'}
                </button>
              </div>
            </div>
          )}

          {/* Step 5: Success Commit */}
          {wizardStep === 5 && (
            <div className="p-8 rounded-2xl border border-[var(--money-pos)]/30 bg-[var(--surface)] shadow-xs max-w-xl mx-auto text-center space-y-4">
              <div className="h-16 w-16 rounded-full bg-[var(--money-pos)]/15 text-[var(--money-pos)] flex items-center justify-center mx-auto">
                <CheckCircle2 size={32} />
              </div>
              <h3 className="text-lg font-bold text-[var(--ink)]">
                تم استيراد واعتماد بيانات التشغيل بنجاح!
              </h3>
              <p className="text-xs sm:text-sm text-[var(--muted)] leading-relaxed">
                تم إنشاء دفعة استيراد معتمدة ومطابقة وقائع تشغيل المناديب لشهر {period} على منصة {platform}.
              </p>

              <div className="pt-4 flex flex-wrap justify-center gap-3">
                <Link
                  href="/payroll/"
                  className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition shadow-xs"
                >
                  الانتقال لمسيرات الرواتب
                </Link>
                <button
                  type="button"
                  onClick={handleReset}
                  className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--muted)] hover:bg-[var(--raised)] transition"
                >
                  استيراد ملف إضافي
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Batches Archive */}
      {activeTab === 'batches' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex items-center justify-between">
            <div>
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
                أرشيف دفعات الاستيراد السابقة
              </h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                سجل كافة الملفات ودفعات التشغيل المستوردة في المنظومة مع البصمة وتفاصيل المعالجة.
              </p>
            </div>
            <button
              type="button"
              onClick={loadBatches}
              className="p-2 rounded-xl border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)] transition cursor-pointer"
            >
              <RefreshCw size={15} />
            </button>
          </div>

          {batchesError ? (
            <ErrorState title="تعذر استرجاع الدفعات" message={batchesError} onRetry={loadBatches} />
          ) : loadingBatches ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : batches.length === 0 ? (
            <EmptyState
              title="لا توجد دفعات استيراد مؤرشفة"
              description="تظهر الدفعات هنا بعد استيراد ملفات المنصات واعتمادها بنجاح."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">معرف الدفعة (Batch ID)</th>
                      <th className="p-3 text-start">المنصة</th>
                      <th className="p-3 text-start">الفترة</th>
                      <th className="p-3 text-start">عدد السجلات</th>
                      <th className="p-3 text-start">المستورد</th>
                      <th className="p-3 text-start">تاريخ الاعتماد</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {batches.map((b: any, idx: number) => (
                      <tr key={b.id || idx} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-xs font-bold text-[var(--ink)]">{b.id || idx + 1}</td>
                        <td className="p-3 font-semibold text-[var(--accent)]">{b.platform || 'عام'}</td>
                        <td className="p-3 font-mono text-xs">{b.period || '-'}</td>
                        <td className="p-3 num font-bold">{b.count || b.rowsCount || '-'}</td>
                        <td className="p-3 font-mono text-xs text-[var(--muted)]">@{b.importedBy || b.createdBy || 'system'}</td>
                        <td className="p-3 num text-xs text-[var(--muted)]">{b.at || b.createdAt || '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

// Free-form import (any sheet → map columns → preview → commit) and batch history.
// Kept as its own tab in «مركز الاستيراد» so the catalog page does not remove it.
export function FreeImportTab() {
  return (
    <Suspense
      fallback={
        <div className="space-y-4 p-6">
          <Skeleton className="h-12 w-48 rounded-xl" />
          <Skeleton className="h-28 w-full rounded-2xl" />
        </div>
      }
    >
      <ImportsPageInner />
    </Suspense>
  );
}
