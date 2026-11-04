'use client';

import React, { useState, useEffect, useMemo, useRef } from 'react';
import {
  Calculator,
  Upload,
  FileSpreadsheet,
  AlertTriangle,
  CheckCircle2,
  AlertCircle,
  Clock,
  ShieldAlert,
  ArrowRightLeft,
  BookOpen,
  PieChart,
  Search,
  Filter,
  Download,
  Check,
  X,
  Eye,
  FileText,
  Building2,
  Users,
  Coins,
  DollarSign,
  Tag,
  Layers,
  ChevronRight,
  ExternalLink,
  ShieldCheck,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Tabs } from '../../components/ui/Tabs';
import { Toolbar } from '../../components/ui/Toolbar';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import type {
  HsInvoice,
  HsDispute,
  HsAllocation,
  HsFact,
  HsProposedJournal,
  HsImportFile,
  HsImportPreviewResult,
} from '../../lib/types';

export default function PayrollPage() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();

  const showToast = (msg: string, type: 'success' | 'error' | 'info' | 'warning' = 'info') => {
    toast(msg, type === 'error' ? 'danger' : type);
  };

  // Navigation tabs within Payroll
  const [activeTab, setActiveTab] = useState<string>('invoices');

  // Filters & period
  const [selectedPeriod, setSelectedPeriod] = useState<string>('2026-10');
  const [selectedCompany, setSelectedCompany] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
  const [invoices, setInvoices] = useState<HsInvoice[]>([]);
  const [disputes, setDisputes] = useState<HsDispute[]>([]);
  const [allocations, setAllocations] = useState<HsAllocation[]>([]);
  const [facts, setFacts] = useState<HsFact[]>([]);
  const [proposedJournals, setProposedJournals] = useState<HsProposedJournal[]>([]);

  // Sub-tab 4 (ERP Facts) states
  const [factsViewMode, setFactsViewMode] = useState<'accounts' | 'riders'>('accounts');
  const [selectedFactKind, setSelectedFactKind] = useState<string>('hs_wallet_event');

  // Sub-tab 6 (Proposed Journals) selected invoice
  const [selectedInvoiceForJournal, setSelectedInvoiceForJournal] = useState<HsInvoice | null>(null);
  const [currentProposal, setCurrentProposal] = useState<HsProposedJournal | null>(null);
  const [loadingProposal, setLoadingProposal] = useState<boolean>(false);

  // Import Dialog states
  const [importDialogOpen, setImportDialogOpen] = useState<boolean>(false);
  const [uploadFiles, setUploadFiles] = useState<HsImportFile[]>([]);
  const [previewResult, setPreviewResult] = useState<HsImportPreviewResult | null>(null);
  const [previewLoading, setPreviewLoading] = useState<boolean>(false);
  const [commitLoading, setCommitLoading] = useState<boolean>(false);

  // Dispute Resolve Modal states
  const [selectedDispute, setSelectedDispute] = useState<HsDispute | null>(null);
  const [resolveOutcome, setResolveOutcome] = useState<'platform_credit' | 'platform_rejected'>('platform_credit');
  const [platformReference, setPlatformReference] = useState<string>('');
  const [resolveReason, setResolveReason] = useState<string>('');
  const [resolvingDispute, setResolvingDispute] = useState<boolean>(false);

  // TGA Allocation Split Modal states
  const [selectedAllocation, setSelectedAllocation] = useState<HsAllocation | null>(null);
  const [splitMode, setSplitMode] = useState<'amount' | 'percent'>('amount');
  const [splitCompanyAmount, setSplitCompanyAmount] = useState<string>('');
  const [splitRiderAmount, setSplitRiderAmount] = useState<string>('');
  const [splitRiderPercent, setSplitRiderPercent] = useState<string>('50');
  const [splitRiderId, setSplitRiderId] = useState<string>('');
  const [savingSplit, setSavingSplit] = useState<boolean>(false);

  // Load all review data
  const loadData = async () => {
    setLoading(true);
    try {
      const compParam = selectedCompany === 'all' ? undefined : selectedCompany;
      const [invRes, disRes, allRes] = await Promise.all([
        api.getHsInvoices({ companyId: compParam, period: selectedPeriod }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 50 })),
        api.getHsDisputes({ companyId: compParam, period: selectedPeriod }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 50 })),
        api.getHsAllocations({ companyId: compParam, period: selectedPeriod }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 50 })),
      ]);

      setInvoices(invRes.items || []);
      setDisputes(disRes.items || []);
      setAllocations(allRes.items || []);

      // If invoices exist, pick first one for proposed journals
      if (invRes.items && invRes.items.length > 0 && !selectedInvoiceForJournal) {
        setSelectedInvoiceForJournal(invRes.items[0]);
      }
    } catch (err: any) {
      showToast(err.message || 'تعذر تحميل بيانات المنصة', 'error');
    } finally {
      setLoading(false);
    }
  };

  // Load ERP facts when tab 4 is active or kind changes
  const loadFacts = async () => {
    try {
      const res = await api.getHsFacts({ kind: selectedFactKind });
      setFacts(res.items || []);
    } catch (_) {
      setFacts([]);
    }
  };

  // Load proposed journals when invoice is selected
  const loadProposal = async (invId: string) => {
    setLoadingProposal(true);
    try {
      const res = await api.getHsProposedJournals({ invoiceId: invId });
      if (res && res.rows && res.rows.length > 0) {
        const prop = res.rows[0];
        prop.fingerprint = res.fingerprint;
        setCurrentProposal(prop);
      } else {
        setCurrentProposal(null);
      }
    } catch (err: any) {
      setCurrentProposal(null);
    } finally {
      setLoadingProposal(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [selectedCompany, selectedPeriod]);

  useEffect(() => {
    if (activeTab === 'facts' || activeTab === 'month_close') {
      const kind = activeTab === 'month_close' ? 'hs_month_close' : selectedFactKind;
      api.getHsFacts({ kind }).then(res => setFacts(res.items || [])).catch(() => setFacts([]));
    }
  }, [activeTab, selectedFactKind]);

  useEffect(() => {
    if (selectedInvoiceForJournal) {
      loadProposal(selectedInvoiceForJournal.id);
    }
  }, [selectedInvoiceForJournal]);

  // File upload handler for workbooks
  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;

    if (files.length > 3) {
      showToast('الحد الأقصى المسموح به هو 3 ملفات فواتير فقط', 'error');
      return;
    }

    const readers: Promise<HsImportFile>[] = [];
    let totalSize = 0;

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      totalSize += file.size;

      if (totalSize > 8 * 1024 * 1024) {
        showToast('حجم الملفات الإجمالي يتجاوز الحد المسموح (8 ميجابايت)', 'error');
        return;
      }

      readers.push(
        new Promise((resolve, reject) => {
          const reader = new FileReader();
          reader.onload = () => {
            const result = reader.result as string;
            const base64 = result.includes(',') ? result.split(',')[1] : result;
            resolve({ fileName: file.name, dataBase64: base64 });
          };
          reader.onerror = reject;
          reader.readAsDataURL(file);
        })
      );
    }

    Promise.all(readers).then((loaded) => {
      setUploadFiles(loaded);
    });
  };

  // Run preview import
  const handlePreviewImport = async () => {
    if (uploadFiles.length === 0) {
      showToast('يرجى اختيار ملف أو أكثر من ملفات فواتير هنقرستيشن (XLSX)', 'error');
      return;
    }
    setPreviewLoading(true);
    try {
      const res = await api.previewHsImport({
        period: selectedPeriod,
        files: uploadFiles,
      });
      setPreviewResult(res);
      showToast('تمت معاينة الفواتير والتحقق من التناسق الحسابي', 'success');
    } catch (err: any) {
      showToast(err.message || 'فشل فحص ومعاينة الفواتير', 'error');
    } finally {
      setPreviewLoading(false);
    }
  };

  // Run commit import
  const handleCommitImport = async () => {
    if (!previewResult || !previewResult.canCommit) {
      showToast('لا يمكن اعتماد الاستيراد لوجود أخطاء مانعة', 'error');
      return;
    }
    setCommitLoading(true);
    try {
      await api.commitHsImport({
        period: selectedPeriod,
        files: uploadFiles,
        rows: previewResult.rows,
        fingerprint: previewResult.fingerprint,
        confirm: true,
      });
      showToast('تم استيراد فواتير المنصة وتسجيل الوقائع والنزاعات بنجاح', 'success');
      setImportDialogOpen(false);
      setUploadFiles([]);
      setPreviewResult(null);
      loadData();
    } catch (err: any) {
      showToast(err.message || 'فشل إتمام استيراد الفواتير', 'error');
    } finally {
      setCommitLoading(false);
    }
  };

  // Approve Invoice (maker-checker)
  const handleApproveInvoice = async (inv: HsInvoice) => {
    if (!currentProposal) {
      showToast('جاري تحضير القيد المقترح للمعاينة', 'info');
      await loadProposal(inv.id);
    }
    try {
      const proposalRes = await api.getHsProposedJournals({ invoiceId: inv.id });
      if (!proposalRes.rows || proposalRes.rows.length === 0) {
        throw new Error('تعذر إنشاء معاينة الترحيل');
      }
      await api.approveHsInvoice(inv.id, {
        rows: proposalRes.rows,
        fingerprint: proposalRes.fingerprint,
        confirm: true,
      });
      showToast('تم اعتماد وقائع الفاتورة بنجاح بواسطة المدقق المستقل', 'success');
      loadData();
    } catch (err: any) {
      showToast(err.message || 'فشل اعتماد الفاتورة', 'error');
    }
  };

  // Resolve Dispute
  const handleResolveDispute = async () => {
    if (!selectedDispute) return;
    if (!platformReference.trim()) {
      showToast('مرجع المنصة الرسمي مطلوب لتوثيق التسوية', 'error');
      return;
    }
    if (!resolveReason.trim()) {
      showToast('سبب وبيان المعالجة مطلوب', 'error');
      return;
    }

    setResolvingDispute(true);
    try {
      await api.resolveHsDispute(selectedDispute.invoiceId, {
        mode: 'dispute',
        disputeId: selectedDispute.id,
        outcome: resolveOutcome,
        platformReference: platformReference.trim(),
        reason: resolveReason.trim(),
      });
      showToast(
        resolveOutcome === 'platform_rejected'
          ? 'تم تسجيل رفض المنصة وتصعيد النزاع لحساب 112803 للسماح بترحيل الفاتورة'
          : 'تم تسجيل رد المنصة وتسوية الاعتراض بنجاح',
        'success'
      );
      setSelectedDispute(null);
      setPlatformReference('');
      setResolveReason('');
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر تسجيل قرار فض النزاع', 'error');
    } finally {
      setResolvingDispute(false);
    }
  };

  // Calculate & Submit TGA Split
  const handleSaveTgaSplit = async () => {
    if (!selectedAllocation) return;

    let compAmt = 0;
    let riderAmt = 0;
    const total = selectedAllocation.amount;

    if (splitMode === 'percent') {
      const p = parseFloat(splitRiderPercent) || 0;
      if (p < 0 || p > 100) {
        showToast('النسبة المئوية يجب أن تكون بين 0 و 100%', 'error');
        return;
      }
      riderAmt = Math.round(((total * p) / 100) * 100) / 100;
      compAmt = Math.round((total - riderAmt) * 100) / 100;
    } else {
      compAmt = parseFloat(splitCompanyAmount) || 0;
      riderAmt = parseFloat(splitRiderAmount) || 0;
      if (Math.abs(compAmt + riderAmt - total) > 0.01) {
        showToast(`مجموع نصيبي الشركة والمناديب (${compAmt + riderAmt}) يجب أن يساوي الإجمالي (${total}) تماماً`, 'error');
        return;
      }
    }

    if (riderAmt > 0 && !splitRiderId.trim()) {
      showToast('يجب إدخال معرّف أو إقامة المندوب لتخصيص نصيب المندوب', 'error');
      return;
    }

    setSavingSplit(true);
    try {
      const payload: { companyAmount?: number; riderAmount?: number; riderId?: string; riderPercent?: number } = {};
      if (splitMode === 'percent') {
        payload.riderPercent = parseFloat(splitRiderPercent);
        if (splitRiderId.trim()) payload.riderId = splitRiderId.trim();
      } else {
        payload.companyAmount = compAmt;
        payload.riderAmount = riderAmt;
        if (splitRiderId.trim()) payload.riderId = splitRiderId.trim();
      }

      await api.splitHsAllocation(selectedAllocation.lineId, payload);
      showToast('تم حفظ مقترح تقسيم TGA بنجاح وفي انتظار اعتماد المدقق المستقل', 'success');
      setSelectedAllocation(null);
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر حفظ توزيع TGA', 'error');
    } finally {
      setSavingSplit(false);
    }
  };

  // Approve TGA Allocation (maker-checker)
  const handleApproveAllocation = async (alloc: HsAllocation) => {
    try {
      await api.approveHsAllocation(alloc.id);
      showToast('تم اعتماد توزيع TGA بنجاح بواسطة المدقق المالي المستقل', 'success');
      loadData();
    } catch (err: any) {
      showToast(err.message || 'تعذر اعتماد توزيع TGA (قاعدة الصانع والمدقق تمنع اعتماد الشخص لتقسيمه)', 'error');
    }
  };

  // Export Disputes to Excel
  const handleExportDisputes = async () => {
    try {
      const headers = ['رقم الفاتورة', 'السطر', 'الفئة', 'المبلغ (ر.س)', 'الشهر', 'الحالة', 'المرجع الرسمي', 'السبب'];
      const rows = disputes.map((d) => [
        d.invoiceId,
        d.lineId,
        d.category,
        d.amount,
        d.period,
        d.status === 'open' ? 'مفتوح' : d.status === 'escalated' ? 'مصعّد (112803)' : 'تمت التسوية',
        d.platformReference || '-',
        d.reason || '-',
      ]);
      const blob = await api.exportExcel({
        headers,
        rows,
        numericColumns: [3],
        filename: `hungerstation-disputes-${selectedPeriod}.xlsx`,
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `hungerstation-disputes-${selectedPeriod}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      showToast('تم تصدير ملف إكسل بنجاح', 'success');
    } catch (err: any) {
      showToast('تعذر تصدير ملف إكسل', 'error');
    }
  };

  // Derived statistics
  const totalInvoicesAmount = useMemo(() => invoices.reduce((acc, inv) => acc + (inv.totalAmount || 0), 0), [invoices]);
  const openDisputesCount = useMemo(() => disputes.filter((d) => d.status !== 'resolved').length, [disputes]);
  const pendingAllocationsCount = useMemo(() => allocations.filter((a) => a.status === 'pending_allocation').length, [allocations]);
  const isPostingGateOn = currentProposal?.postingEnabled ?? false;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="الرواتب والمسيرات · منصة هنقرستيشن"
        description="إدارة ومراجعة فواتير منصة هنقرستيشن، فض نزاعات الاستقطاعات CAR، التوزيع المعتمد لمبالغ TGA، ومطابقة وقائع المحفظة وقيود الترحيل."
        eyebrow="منصة معتمدة"
        primaryAction={{
          label: 'استيراد فواتير هنقرستيشن',
          icon: <Upload size={16} />,
          onClick: () => setImportDialogOpen(true),
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي الصفحة الحالية"
          value={`${totalInvoicesAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint={`${invoices.length} فواتير مسجلة في الصفحة الحالية لشهر ${selectedPeriod}`}
          icon={Calculator}
        />
        <StatCard
          label="الاعتراضات المعلقة (CAR)"
          value={openDisputesCount}
          hint={openDisputesCount > 0 ? 'تتطلب رد المنصة أو التصعيد لحساب 112803' : 'لا توجد نزاعات معلقة'}
          icon={ShieldAlert}
        />
        <StatCard
          label="مبالغ TGA بانتظار التوزيع"
          value={pendingAllocationsCount}
          hint="تتطلب تحديد حصة المندوب والشركة بدون افتراض مسبق"
          icon={ArrowRightLeft}
        />
        <StatCard
          label="بوابة الترحيل المحاسبي"
          value={isPostingGateOn ? 'مفعّلة' : 'موقوفة'}
          hint={isPostingGateOn ? 'جاهز لترحيل القيود المعتمدة' : 'الترحيل موقوف لحين اعتماد ملف المنصة'}
          icon={ShieldCheck}
        />
      </div>

      {/* Main Tabs */}
      <div className="border-b border-[var(--line)]">
        <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
          {[
            { id: 'invoices', label: 'الفواتير', count: invoices.length },
            { id: 'disputes', label: 'الاعتراضات (CAR)', count: openDisputesCount, alert: openDisputesCount > 0 },
            { id: 'allocations', label: 'توزيع TGA', count: pendingAllocationsCount },
            { id: 'facts', label: 'وقائع المحفظة من ERP' },
            { id: 'month_close', label: 'إقفال الشهر (C/M/A)' },
            { id: 'proposed_journals', label: 'القيود المقترحة' },
          ].map((tab) => (
            <button
              key={tab.id}
              id={`tab-${tab.id}`}
              role="tab"
              aria-selected={activeTab === tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id)}
              className={`inline-flex items-center gap-2 px-4 py-3 font-bold text-xs sm:text-sm border-b-2 transition whitespace-nowrap ${
                activeTab === tab.id
                  ? 'border-[var(--accent)] text-[var(--accent)]'
                  : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span>{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span
                  className={`num px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                    tab.alert
                      ? 'bg-[var(--danger)] text-white'
                      : 'bg-[var(--raised)] text-[var(--ink)]'
                  }`}
                >
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* Sub-tab 1: الفواتير (Invoices) */}
      {activeTab === 'invoices' && (
        <div className="space-y-4">
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
            <div className="p-4 border-b border-[var(--line)] flex flex-wrap items-center justify-between gap-3">
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">فواتير المنصة المسجلة</h3>
              <div className="flex items-center gap-2">
                <span className="text-xs text-[var(--muted)]">الفترة:</span>
                <span className="num font-bold text-xs bg-[var(--raised)] px-2 py-1 rounded-lg border border-[var(--line)]">
                  {selectedPeriod}
                </span>
              </div>
            </div>

            {loading ? (
              <div className="p-8 space-y-3">
                <Skeleton className="h-10 w-full" />
                <Skeleton className="h-10 w-full" />
                <Skeleton className="h-10 w-full" />
              </div>
            ) : invoices.length === 0 ? (
              <EmptyState
                title="لا توجد فواتير هنقرستيشن مستوردة لهذه الفترة"
                description="اضغط على زر «استيراد فواتير هنقرستيشن» لرفع ملفات العمل الشهرية (1 إلى 3 ملفات XLSX)."
                action={{
                  label: 'رفع ملفات الفواتير',
                  onClick: () => setImportDialogOpen(true),
                }}
              />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">معرّف الفاتورة</th>
                      <th className="p-3 text-start">الشركة</th>
                      <th className="p-3 text-start">الفترة</th>
                      <th className="p-3 text-start">الإجمالي</th>
                      <th className="p-3 text-start">الضريبة</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">النزاع والتعليق</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {invoices.map((inv) => {
                      const isMaker = inv.createdBy === user?.username;
                      const hasDispute = inv.status === 'disputed' || inv.settlementHold;

                      return (
                        <tr key={inv.id} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono text-[11px] text-[var(--ink)]">{inv.id.substring(0, 16)}...</td>
                          <td className="p-3 font-bold">{inv.companyId}</td>
                          <td className="p-3 num">{inv.period}</td>
                          <td className="p-3 num font-bold text-[var(--ink)]">
                            {inv.totalAmount?.toLocaleString('en-US', { minimumFractionDigits: 2 }) ?? 'غير معروف'} ر.س
                          </td>
                          <td className="p-3 num text-[var(--muted)]">
                            {inv.vat?.toLocaleString('en-US', { minimumFractionDigits: 2 }) ?? '0.00'} ر.س
                          </td>
                          <td className="p-3">
                            <span
                              className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                                inv.status === 'posted'
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : inv.status === 'approved'
                                  ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
                                  : inv.status === 'disputed'
                                  ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                  : inv.status === 'pending_allocation'
                                  ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                  : 'bg-[var(--raised)] text-[var(--muted)]'
                              }`}
                            >
                              {inv.status === 'posted'
                                ? 'مرحلة بالكامل'
                                : inv.status === 'approved'
                                ? 'معتمدة الوقائع'
                                : inv.status === 'disputed'
                                ? 'متنازع عليها'
                                : inv.status === 'pending_allocation'
                                ? 'بانتظار التوزيع'
                                : 'مسودة'}
                            </span>
                          </td>
                          <td className="p-3">
                            {hasDispute ? (
                              <span className="inline-flex items-center gap-1 text-[11px] font-bold text-[var(--danger)] bg-[var(--danger)]/10 px-2 py-0.5 rounded-md">
                                <AlertCircle size={12} />
                                <span>حظر الترحيل (نزاع مفتوح)</span>
                              </span>
                            ) : (
                              <span className="text-[11px] text-[var(--money-pos)] flex items-center gap-1">
                                <CheckCircle2 size={12} />
                                <span>جاهزة للاعتماد</span>
                              </span>
                            )}
                          </td>
                          <td className="p-3">
                            <div className="flex items-center gap-2">
                              <button
                                type="button"
                                onClick={() => {
                                  setSelectedInvoiceForJournal(inv);
                                  setActiveTab('proposed_journals');
                                }}
                                className="px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold transition"
                              >
                                معاينة القيد
                              </button>
                              {inv.status !== 'approved' && inv.status !== 'posted' && (
                                <button
                                  type="button"
                                  disabled={isMaker}
                                  onClick={() => handleApproveInvoice(inv)}
                                  title={isMaker ? 'قاعدة الصانع والمدقق: لا يمكن لمنشئ الفاتورة اعتمادها' : 'اعتماد وقائع الفاتورة'}
                                  className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50 text-xs font-bold transition"
                                >
                                  اعتماد
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Sub-tab 2: الاعتراضات (Disputes) */}
      {activeTab === 'disputes' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 bg-[var(--surface)] p-4 rounded-2xl border border-[var(--line)]">
            <div>
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">سجل اعتراضات استقطاعات المنصة (CAR Disputes)</h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                مبالغ CAR المفروضة تسجل كنزاع رسمي؛ ولا يجوز تحميلها للمندوب. استثناء (§23.2): رفض المنصة ينقل النزاع لحساب 112803 ويسمح بترحيل الفاتورة.
              </p>
            </div>
            <button
              type="button"
              onClick={handleExportDisputes}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-bold transition"
            >
              <Download size={14} />
              <span>تصدير الاعتراضات (Excel)</span>
            </button>
          </div>

          {loading ? (
            <div className="p-8 space-y-3">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : disputes.length === 0 ? (
            <EmptyState
              title="لا توجد اعتراضات مسجلة لهذه الفترة"
              description="تنشأ الاعتراضات آلياً عند وجود مبالغ CAR في فواتير المنصة، أو يمكن تسجيل اعتراض يدوي على سطر محدد."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">السطر / المعرّف</th>
                      <th className="p-3 text-start">الفئة</th>
                      <th className="p-3 text-start">المبلغ</th>
                      <th className="p-3 text-start">الشهر</th>
                      <th className="p-3 text-start">السبب والبيان</th>
                      <th className="p-3 text-start">الحالة والمسار المحاسبي</th>
                      <th className="p-3 text-start">مرجع المنصة</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {disputes.map((dis) => (
                      <tr key={dis.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-[11px] text-[var(--ink)]">{dis.lineId || dis.id.substring(0, 12)}</td>
                        <td className="p-3 font-bold">{dis.category}</td>
                        <td className="p-3 num font-bold text-[var(--danger)]">
                          {dis.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-3 num">{dis.period}</td>
                        <td className="p-3 max-w-xs truncate" title={dis.reason}>
                          {dis.reason}
                        </td>
                        <td className="p-3">
                          {dis.status === 'escalated' ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
                              <AlertTriangle size={12} />
                              <span>مرحّل لحساب النزاعات 112803 لحين فض النزاع</span>
                            </span>
                          ) : dis.status === 'resolved' ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
                              <CheckCircle2 size={12} />
                              <span>تمت التسوية (رد المنصة)</span>
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--danger)]/15 text-[var(--danger)]">
                              <AlertCircle size={12} />
                              <span>نزاع مفتوح (يمنع الترحيل)</span>
                            </span>
                          )}
                        </td>
                        <td className="p-3 font-mono text-xs">{dis.platformReference || '-'}</td>
                        <td className="p-3">
                          {dis.status === 'open' && (
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedDispute(dis);
                                setResolveOutcome('platform_credit');
                              }}
                              className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 text-xs font-bold transition"
                            >
                              معالجة النزاع
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 3: توزيع TGA (TGA Distribution) */}
      {activeTab === 'allocations' && (
        <div className="space-y-4">
          <div className="bg-[var(--surface)] p-4 rounded-2xl border border-[var(--line)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">توزيع مبالغ دعم/رسوم هيئة النقل (TGA Distribution)</h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              كل سطر يبدأ كـ «بانتظار التوزيع». يجب تحديد التقسيم بين الشركة وحساب المندوب المعتمد (112502) بدقة دون أي افتراض تلقائي.
            </p>
          </div>

          {loading ? (
            <div className="p-8 space-y-3">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : allocations.length === 0 ? (
            <EmptyState
              title="لا توجد مبالغ TGA بانتظار التوزيع"
              description="تظهر بنود TGA بعد استيراد الفواتير التي تتضمن رسوم أو تعويضات الهيئة."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم السطر</th>
                      <th className="p-3 text-start">الفاتورة</th>
                      <th className="p-3 text-start">المبلغ الإجمالي</th>
                      <th className="p-3 text-start">نصيب الشركة</th>
                      <th className="p-3 text-start">نصيب المناديب</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {allocations.map((alloc) => {
                      const isCreator = (alloc.createdBy || (alloc as any).requestedBy) === user?.username;

                      return (
                        <tr key={alloc.id} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 font-mono text-[11px] text-[var(--ink)]">{alloc.lineId}</td>
                          <td className="p-3 font-mono text-xs">{alloc.invoiceId.substring(0, 12)}</td>
                          <td className="p-3 num font-bold text-[var(--ink)]">
                            {alloc.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </td>
                          <td className="p-3 num text-[var(--muted)]">
                            {alloc.companyAmount !== undefined
                              ? `${alloc.companyAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`
                              : '-'}
                          </td>
                          <td className="p-3 num text-[var(--muted)]">
                            {alloc.riderAmount !== undefined
                              ? `${alloc.riderAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`
                              : '-'}
                          </td>
                          <td className="p-3">
                            <span
                              className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                                alloc.status === 'approved'
                                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                  : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                              }`}
                            >
                              {alloc.status === 'approved' ? 'معتمد' : 'بانتظار التوزيع'}
                            </span>
                          </td>
                          <td className="p-3">
                            <div className="flex items-center gap-2">
                              <button
                                type="button"
                                onClick={() => {
                                  setSelectedAllocation(alloc);
                                  setSplitCompanyAmount(alloc.companyAmount ? String(alloc.companyAmount) : '');
                                  setSplitRiderAmount(alloc.riderAmount ? String(alloc.riderAmount) : '');
                                }}
                                className="px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold transition"
                              >
                                تحديد التقسيم
                              </button>
                              {(alloc.status === 'pending' || alloc.status === 'pending_allocation') && alloc.companyAmount !== undefined && (
                                <button
                                  type="button"
                                  disabled={isCreator}
                                  onClick={() => handleApproveAllocation(alloc)}
                                  title={isCreator ? 'لا يجوز للشخص اعتماد تقسيمه بنفسه' : 'اعتماد التوزيع بواسطة مدقق ثانٍ'}
                                  className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50 text-xs font-bold transition"
                                >
                                  اعتماد المدقق
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 4: وقائع المحفظة من ERP (Wallet Facts) */}
      {activeTab === 'facts' && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 bg-[var(--surface)] p-4 rounded-2xl border border-[var(--line)]">
            <div>
              <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">وقائع المحفظة والتحويلات من ERP (للقراءة فقط)</h3>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                عرض الوقائع الثابتة وفق عقود CR-002 دون إنشاء قيود أو أرصدة دفترية جديدة.
              </p>
            </div>

            {/* Toggle: الحسابات vs المناديب */}
            <div className="flex items-center rounded-xl bg-[var(--raised)] p-1 border border-[var(--line)]">
              <button
                type="button"
                onClick={() => setFactsViewMode('accounts')}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition ${
                  factsViewMode === 'accounts' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                الحسابات
              </button>
              <button
                type="button"
                onClick={() => setFactsViewMode('riders')}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition ${
                  factsViewMode === 'riders' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                المناديب
              </button>
            </div>
          </div>

          {/* Kinds filter chips */}
          <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
            {[
              { id: 'hs_wallet_event', label: 'حركات المحفظة' },
              { id: 'hs_remittance', label: 'الحوالات البنكية' },
              { id: 'hs_remittance_allocation', label: 'توزيع الحوالات' },
              { id: 'hs_handover_review', label: 'مراجعة التسليم' },
              { id: 'hs_month_close', label: 'إقفال الشهر' },
            ].map((k) => (
              <button
                key={k.id}
                type="button"
                onClick={() => setSelectedFactKind(k.id)}
                className={`px-3 py-1.5 rounded-xl text-xs font-semibold whitespace-nowrap transition ${
                  selectedFactKind === k.id
                    ? 'bg-[var(--accent)] text-[var(--accent-ink)] font-bold shadow-xs'
                    : 'bg-[var(--surface)] border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                {k.label}
              </button>
            ))}
          </div>

          {facts.length === 0 ? (
            <div className="rounded-2xl border border-[var(--warning)]/30 bg-[var(--warning)]/5 p-6 text-center space-y-2">
              <AlertTriangle className="mx-auto text-[var(--warning)]" size={32} />
              <h4 className="font-bold text-sm text-[var(--ink)]">المصدر غير متاح بعد</h4>
              <p className="text-xs text-[var(--muted)] max-w-md mx-auto">
                جداول ومصادر وقائع المحفظة لم يتم ربطها بعد في قاعدة بيانات ERP وفق مواصفة CR-002.
              </p>
            </div>
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">معرّف الحدث (SourceEventId)</th>
                      <th className="p-3 text-start">النوع</th>
                      <th className="p-3 text-start">الشركة</th>
                      <th className="p-3 text-start">المبلغ</th>
                      <th className="p-3 text-start">الحالة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {facts.map((f) => (
                      <tr key={f.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono text-[11px] text-[var(--ink)]">
                          {f.sourceEventId || f.id}
                          {f.isReversal && (
                            <span className="ms-2 px-1.5 py-0.2 rounded text-[10px] bg-[var(--warning)]/20 text-[var(--warning)] font-bold">
                              حركة عكسية لـ #{f.reversalOf?.substring(0, 8)}
                            </span>
                          )}
                        </td>
                        <td className="p-3">{f.kind}</td>
                        <td className="p-3 font-bold">{f.companyId}</td>
                        <td className="p-3 num font-bold">
                          {f.sourceFacts?.Amount ? `${Number(f.sourceFacts.Amount).toLocaleString()} ر.س` : '-'}
                        </td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--muted)]">
                            {f.status || 'معتمد'}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 5: إقفال الشهر (C/M/A) */}
      {activeTab === 'month_close' && (
        <div className="space-y-4">
          <div className="bg-[var(--surface)] p-4 rounded-2xl border border-[var(--line)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">معادلات إقفال الشهر ومطابقة الأرصدة (C/M/A Breakdown)</h3>
            <p className="text-xs text-[var(--muted)] mt-1">
              تطبيق معادلة D20 المعتمدة: Expected = -C | RemainingOldCarry = C + A | CurrentPeriodBalance = M - (C + A).
              يظهر «غير معروف» عند نقص C أو A لمنع تسجيل أصفار وهمية.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
              <div className="text-xs text-[var(--muted)] font-bold">المتوقع (Expected)</div>
              <div className="text-lg font-black text-[var(--ink)] font-mono">-C</div>
              <p className="text-xs text-[var(--muted)]">الرصيد الافتتاحي المتوقع للترحيل</p>
            </div>
            <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
              <div className="text-xs text-[var(--muted)] font-bold">المتبقي من الرصيد السابق</div>
              <div className="text-lg font-black text-[var(--ink)] font-mono">C + A</div>
              <p className="text-xs text-[var(--muted)]">تسوية السلف والاستقطاعات السابقة</p>
            </div>
            <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-2">
              <div className="text-xs text-[var(--muted)] font-bold">صافي رصيد الفترة الحالية</div>
              <div className="text-lg font-black text-[var(--ink)] font-mono">M - (C + A)</div>
              <p className="text-xs text-[var(--muted)]">يقترح حساب 112507 عند الصافي السالب فقط</p>
            </div>
          </div>

          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-6 text-center space-y-3">
            <h4 className="font-bold text-sm text-[var(--ink)]">حالة إقفال الشهر الحالية لـ {selectedPeriod}</h4>
            <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--raised)] border border-[var(--line)] text-xs font-semibold text-[var(--muted)]">
              <span>حالة بيانات C و A:</span>
              <span className="font-bold text-[var(--warning)]">غير معروف (المصدر قيد الربط)</span>
            </div>
            <p className="text-xs text-[var(--muted)] max-w-lg mx-auto">
              بناءً على سياسة التدقيق المعتمدة، لا يتم احتساب رصيد افتراضي (0)، بل تظل النتيجة «غير معروف» لحين اكتمال عقود الربط وتوفير C و A بشكل صريح.
            </p>
          </div>
        </div>
      )}

      {/* Sub-tab 6: القيود المقترحة (Proposed Journals) */}
      {activeTab === 'proposed_journals' && (
        <div className="space-y-4">
          {/* Top Posting Gate Banner */}
          {!isPostingGateOn && (
            <div className="flex items-center gap-3 p-4 rounded-2xl border border-[var(--warning)]/40 bg-[var(--warning)]/10 text-[var(--warning)]">
              <AlertTriangle className="shrink-0" size={20} />
              <div className="text-xs sm:text-sm font-bold">
                الترحيل موقوف لحين اعتماد ملف المنصة (بوابة الترحيل معطلة افتراضياً لحماية الدفاتر المحاسبية).
              </div>
            </div>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3 bg-[var(--surface)] p-4 rounded-2xl border border-[var(--line)]">
            <div className="flex items-center gap-2">
              <span className="text-xs font-bold text-[var(--muted)]">الفاتورة المحددة:</span>
              <select
                value={selectedInvoiceForJournal?.id || ''}
                onChange={(e) => {
                  const inv = invoices.find((i) => i.id === e.target.value);
                  if (inv) setSelectedInvoiceForJournal(inv);
                }}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs font-bold rounded-lg px-2.5 py-1.5 focus:outline-hidden"
              >
                {invoices.map((i) => (
                  <option key={i.id} value={i.id}>
                    {i.id.substring(0, 16)}... ({i.companyId} - {i.period})
                  </option>
                ))}
              </select>
            </div>

            {currentProposal && (
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-[var(--muted)]">توازن القيد:</span>
                {(() => {
                  const debits = currentProposal.lines.reduce((s, l) => s + (l.debit || 0), 0);
                  const credits = currentProposal.lines.reduce((s, l) => s + (l.credit || 0), 0);
                  const balanced = Math.abs(debits - credits) < 0.01;
                  return (
                    <span
                      className={`inline-flex items-center gap-1 text-xs font-bold px-2 py-0.5 rounded-full ${
                        balanced
                          ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                          : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                      }`}
                    >
                      {balanced ? <CheckCircle2 size={12} /> : <AlertCircle size={12} />}
                      <span>{balanced ? 'القيد متوازن تماماً' : 'القيد غير متوازن!'}</span>
                    </span>
                  );
                })()}
              </div>
            )}
          </div>

          {loadingProposal ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : !currentProposal || currentProposal.lines.length === 0 ? (
            <EmptyState
              title="لا توجد قيود مقترحة لهذه الفاتورة"
              description="تظهر القيود بعد اكتمال بنود الضريبة وتوزيع TGA وفض نزاعات CAR."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم الحساب</th>
                      <th className="p-3 text-start">اسم الحساب والبيان</th>
                      <th className="p-3 text-start">مدين (Debit)</th>
                      <th className="p-3 text-start">دائن (Credit)</th>
                      <th className="p-3 text-start">المندوب / الملاحظات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {currentProposal.lines.map((line, idx) => {
                      const isTarget = line.isTargetScheme || line.description?.includes('تارجت');

                      return (
                        <tr key={idx} className={`hover:bg-[var(--row-hover)] transition ${isTarget ? 'opacity-60 bg-[var(--raised)]/30' : ''}`}>
                          <td className="p-3 font-mono font-bold text-[var(--accent)]">{line.accountCode}</td>
                          <td className="p-3">
                            <div className="font-semibold">{line.description}</div>
                            {isTarget && (
                              <span className="inline-block mt-1 text-[10px] font-bold text-[var(--warning)] bg-[var(--warning)]/15 px-2 py-0.5 rounded-full">
                                نظام التارجت لم يُعتمد بعد
                              </span>
                            )}
                          </td>
                          <td className="p-3 num font-bold text-[var(--ink)]">
                            {line.debit > 0 ? `${line.debit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '-'}
                          </td>
                          <td className="p-3 num font-bold text-[var(--ink)]">
                            {line.credit > 0 ? `${line.credit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '-'}
                          </td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{line.riderId || '-'}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                  <tfoot className="border-t-2 border-[var(--line)] bg-[var(--raised)]/30 font-bold">
                    <tr>
                      <td colSpan={2} className="p-3 text-end">
                        المجموع الكلي:
                      </td>
                      <td className="p-3 num text-[var(--accent)]">
                        {currentProposal.lines.reduce((s, l) => s + (l.debit || 0), 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                      </td>
                      <td className="p-3 num text-[var(--accent)]">
                        {currentProposal.lines.reduce((s, l) => s + (l.credit || 0), 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                      </td>
                      <td className="p-3"></td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Modal: Import HungerStation Invoices */}
      <Dialog
        open={importDialogOpen}
        onClose={() => {
          setImportDialogOpen(false);
          setUploadFiles([]);
          setPreviewResult(null);
        }}
        title="استيراد وتدقيق فواتير هنقرستيشن (XLSX)"
        description="ارفع من 1 إلى 3 ملفات عمل رسمية لشهر الاستحقاق؛ سيتم فحص الضريبة والبيانات ومطابقة السجلات قبل الترحيل."
      >
        <div className="space-y-4">
          <div className="border-2 border-dashed border-[var(--line)] rounded-2xl p-6 text-center hover:border-[var(--accent)] transition">
            <FileSpreadsheet className="mx-auto text-[var(--muted)] mb-2" size={32} />
            <label className="cursor-pointer">
              <span className="font-bold text-xs sm:text-sm text-[var(--accent)] hover:underline">
                اختر ملفات إكسل (1-3 ملفات)
              </span>
              <input
                type="file"
                multiple
                accept=".xlsx"
                onChange={handleFileUpload}
                className="sr-only"
              />
            </label>
            <p className="text-[11px] text-[var(--muted)] mt-1">يجب ألا يتجاوز الحجم الإجمالي 8 ميجابايت.</p>
          </div>

          {uploadFiles.length > 0 && (
            <div className="space-y-2">
              <div className="text-xs font-bold text-[var(--ink)]">الملفات المختارة ({uploadFiles.length}):</div>
              <ul className="space-y-1">
                {uploadFiles.map((f, i) => (
                  <li key={i} className="flex items-center justify-between text-xs p-2 rounded-lg bg-[var(--raised)]">
                    <span className="font-mono truncate">{f.fileName}</span>
                    <span className="text-[11px] text-[var(--money-pos)] font-bold">جاهز للفحص</span>
                  </li>
                ))}
              </ul>
              <div className="pt-2">
                <button
                  type="button"
                  disabled={previewLoading}
                  onClick={handlePreviewImport}
                  className="w-full py-2 rounded-xl bg-[var(--raised)] border border-[var(--line)] hover:bg-[var(--row-hover)] font-bold text-xs transition"
                >
                  {previewLoading ? 'جاري الفحص والمعاينة...' : 'معاينة وتدقيق الحسابات'}
                </button>
              </div>
            </div>
          )}

          {previewResult && (
            <div className="space-y-3 pt-3 border-t border-[var(--line)]">
              {/* Warnings and blocking checks */}
              {previewResult.errors && previewResult.errors.length > 0 ? (
                <div className="p-3 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-xs space-y-1 text-[var(--danger)]">
                  <div className="font-bold flex items-center gap-1">
                    <AlertCircle size={14} />
                    <span>توجد أخطاء مانعة للاستيراد:</span>
                  </div>
                  <ul className="list-disc list-inside ps-2 space-y-0.5">
                    {previewResult.errors.map((err, i) => (
                      <li key={i}>
                        ملف {err.fileIndex + 1} ({err.sheet}): سطر {err.row} - كود الخطأ: {err.code}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                <div className="p-3 rounded-xl bg-[var(--money-pos)]/10 border border-[var(--money-pos)]/30 text-xs space-y-1 text-[var(--money-pos)]">
                  <div className="font-bold flex items-center gap-1">
                    <CheckCircle2 size={14} />
                    <span>فحوصات الضريبة والإجماليات سليمة تماماً</span>
                  </div>
                  <div className="text-[11px]">
                    عدد الأسطر الجاهزة: {previewResult.rows?.length || 0}
                  </div>
                </div>
              )}

              {/* Warning flags V=Z or T-Z=AA */}
              {previewResult.flags && previewResult.flags.length > 0 && (
                <div className="p-3 rounded-xl bg-[var(--warning)]/10 border border-[var(--warning)]/30 text-xs space-y-1 text-[var(--warning)]">
                  <div className="font-bold flex items-center gap-1">
                    <AlertTriangle size={14} />
                    <span>تنبيهات الفروقات الحسابية:</span>
                  </div>
                  <ul className="list-disc list-inside ps-2 space-y-0.5 text-[11px]">
                    {previewResult.flags.map((fl, i) => (
                      <li key={i}>{fl}</li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="flex items-center justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setImportDialogOpen(false)}
                  className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold hover:bg-[var(--raised)]"
                >
                  إلغاء
                </button>
                <button
                  type="button"
                  disabled={!previewResult.canCommit || commitLoading}
                  onClick={handleCommitImport}
                  className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 disabled:opacity-50"
                >
                  {commitLoading ? 'جاري الاعتماد والتسجيل...' : 'تأكيد وحفظ الفواتير'}
                </button>
              </div>
            </div>
          )}
        </div>
      </Dialog>

      {/* Modal: Resolve Dispute */}
      <Dialog
        open={Boolean(selectedDispute)}
        onClose={() => setSelectedDispute(null)}
        title="معالجة وفض نزاع استقطاع CAR"
        description="توثيق رد المنصة الرسمي. رفض المنصة يتيح نقل المبلغ لحساب النزاعات 112803 للسماح بترحيل الفاتورة دون الإضرار بالمندوب."
      >
        {selectedDispute && (
          <div className="space-y-4">
            <div className="p-3 rounded-xl bg-[var(--raised)] space-y-1 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">مبلغ الاعتراض:</span>
                <span className="font-bold num text-[var(--danger)]">{selectedDispute.amount} ر.س</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">السطر:</span>
                <span className="font-mono">{selectedDispute.lineId}</span>
              </div>
            </div>

            <div className="space-y-2">
              <label className="text-xs font-bold">قرار المنصة الرسمي:</label>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  onClick={() => setResolveOutcome('platform_credit')}
                  className={`p-2.5 rounded-xl border text-xs font-bold text-center transition ${
                    resolveOutcome === 'platform_credit'
                      ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  المنصة ردّت المبلغ
                </button>
                <button
                  type="button"
                  onClick={() => setResolveOutcome('platform_rejected')}
                  className={`p-2.5 rounded-xl border text-xs font-bold text-center transition ${
                    resolveOutcome === 'platform_rejected'
                      ? 'border-[var(--warning)] bg-[var(--warning)]/10 text-[var(--warning)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  المنصة رفضت (تصعيد 112803)
                </button>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">مرجع المنصة الرسمي (Platform Reference):</label>
              <input
                type="text"
                value={platformReference}
                onChange={(e) => setPlatformReference(e.target.value)}
                placeholder="مثال: HS-TICKET-89421"
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">بيان وسبب التسوية:</label>
              <textarea
                value={resolveReason}
                onChange={(e) => setResolveReason(e.target.value)}
                placeholder="اكتب تفاصيل قرار المنصة وسبب التسوية أو التصعيد..."
                rows={3}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setSelectedDispute(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={resolvingDispute}
                onClick={handleResolveDispute}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90"
              >
                {resolvingDispute ? 'جاري التسجيل...' : 'تأكيد قرار فض النزاع'}
              </button>
            </div>
          </div>
        )}
      </Dialog>

      {/* Modal: TGA Allocation Split Calculator */}
      <Dialog
        open={Boolean(selectedAllocation)}
        onClose={() => setSelectedAllocation(null)}
        title="تحديد تقسيم مبالغ TGA"
        description="تحديد نصيب الشركة ونصيب المندوب؛ لا يوجد تقسيم افتراضي، ويجب التحقق من التوازن قبل الحفظ."
      >
        {selectedAllocation && (
          <div className="space-y-4">
            <div className="p-3 rounded-xl bg-[var(--raised)] flex justify-between items-center text-xs">
              <span className="text-[var(--muted)]">إجمالي مبلغ السطر:</span>
              <span className="font-bold text-sm num text-[var(--ink)]">{selectedAllocation.amount} ر.س</span>
            </div>

            <div className="flex items-center rounded-xl bg-[var(--raised)] p-1 border border-[var(--line)]">
              <button
                type="button"
                onClick={() => setSplitMode('amount')}
                className={`flex-1 py-1 rounded-lg text-xs font-bold transition ${
                  splitMode === 'amount' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                تقسيم بالمبلغ المباشر
              </button>
              <button
                type="button"
                onClick={() => setSplitMode('percent')}
                className={`flex-1 py-1 rounded-lg text-xs font-bold transition ${
                  splitMode === 'percent' ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs' : 'text-[var(--muted)]'
                }`}
              >
                تقسيم بالنسبة المئوية %
              </button>
            </div>

            {splitMode === 'percent' ? (
              <div className="space-y-3">
                <div className="space-y-1">
                  <div className="flex justify-between text-xs font-bold">
                    <span>نسبة المندوب: {splitRiderPercent}%</span>
                    <span>نسبة الشركة: {100 - Number(splitRiderPercent)}%</span>
                  </div>
                  <input
                    type="range"
                    min="0"
                    max="100"
                    value={splitRiderPercent}
                    onChange={(e) => setSplitRiderPercent(e.target.value)}
                    className="w-full"
                  />
                </div>
                <div className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)] space-y-1 text-xs">
                  <div className="flex justify-between">
                    <span className="text-[var(--muted)]">نصيب المندوب المحتسب:</span>
                    <span className="num font-bold">
                      {(((selectedAllocation.amount * Number(splitRiderPercent)) / 100)).toFixed(2)} ر.س
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-[var(--muted)]">نصيب الشركة المحتسب:</span>
                    <span className="num font-bold">
                      {(selectedAllocation.amount - ((selectedAllocation.amount * Number(splitRiderPercent)) / 100)).toFixed(2)} ر.س
                    </span>
                  </div>
                  <div className="text-[10px] text-[var(--muted)] text-center pt-1 border-t border-[var(--line)]">
                    تقريبي — المبلغ النهائي من الخادم
                  </div>
                </div>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label className="text-xs font-bold">نصيب الشركة (ر.س):</label>
                  <input
                    type="number"
                    step="0.01"
                    value={splitCompanyAmount}
                    onChange={(e) => setSplitCompanyAmount(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                  />
                </div>
                <div className="space-y-1">
                  <label className="text-xs font-bold">نصيب المندوب (ر.س):</label>
                  <input
                    type="number"
                    step="0.01"
                    value={splitRiderAmount}
                    onChange={(e) => setSplitRiderAmount(e.target.value)}
                    className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                  />
                </div>
              </div>
            )}

            <div className="space-y-1">
              <label className="text-xs font-bold">معرّف المندوب أو الإقامة (لحساب 112502):</label>
              <input
                type="text"
                value={splitRiderId}
                onChange={(e) => setSplitRiderId(e.target.value)}
                placeholder="أدخل رقم الإقامة أو معرّف المندوب..."
                className="w-full p-2.5 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setSelectedAllocation(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={savingSplit}
                onClick={handleSaveTgaSplit}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90"
              >
                {savingSplit ? 'جاري الحفظ...' : 'حفظ مقترح التوزيع'}
              </button>
            </div>
          </div>
        )}
      </Dialog>
    </div>
  );
}
