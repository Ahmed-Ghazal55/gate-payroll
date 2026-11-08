'use client';

import React, { useState, useEffect, useMemo } from 'react';
import {
  FileSpreadsheet,
  Upload,
  CheckCircle,
  AlertTriangle,
  Info,
  Layers,
  ArrowRightLeft,
  Search,
  Filter,
  Check,
  X,
  FileCheck,
  ChevronDown,
  Building,
  Calendar,
  DollarSign,
  ShieldAlert,
  Percent,
  CheckCircle2,
  Send,
  Lock,
  FilePlus,
  RefreshCw,
  Hash,
  AlertCircle,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { StatCard } from '../../components/ui/StatCard';
import { EmptyState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import type { paths } from '../../lib/api-contract';
import {
  NinjaReconcileResponse,
  NinjaInvoiceResult,
  NinjaReconciliationLine,
} from '../../lib/types';

type ApiNinjaInvoice = paths['/api/platforms/ninja/invoices']['get']['responses'][200]['content']['application/json']['items'][number];

const COMPANY_LABELS: Record<string, string> = {
  BW: 'البوابة الموكلة',
  EX: 'البوابة إكسبريس',
  TR: 'البوابة للتجارة',
  '019c18d5-62e1-7000-8000-000000000041': 'البوابة الموكلة',
  '019c18d5-62e1-7000-8000-000000000042': 'البوابة إكسبريس',
  '019c18d5-62e1-7000-8000-000000000040': 'البوابة للتجارة',
};

type DeductionGroup = { category: string; lineIds: string[]; amount: number; unclassified: number; companyOnly: boolean };

const DEDUCTION_LABELS: Record<string, string> = {
  penalty: 'غرامة (Penalty / الخصم)',
  naqilBlocked: 'حظر نقل (NAQIL BLOCKED)',
  stopsWithoutNotice: 'توقف بدون إشعار',
  supplier_adjustment: 'تعديل المورد',
  deductionRefund: 'رد خصم',
  gasolineSupport: 'دعم بنزين',
  onDemand: 'On demand',
  other_deduction: 'خصومات أخرى',
  penalty_fine: 'غرامة مُخالفة',
  food_damage: 'تعويض عن تلف الطعام',
  food_compensation: 'تعويض طعام',
  registration_fee: 'رسوم خدمة التسجيل',
  tga: 'خصم TGA',
  service_fee: 'خصم رسوم الخدمة',
  timeliness_deduction: 'خصم مكافأة التوصيل على الموعد',
  merchant_subsidy_deduction: 'خصم إعانة التاجر',
  other_adjustment: 'تعديل آخر',
  other_adjustments: 'تعديلات أخرى',
  equipment_deduction: 'خصومات المعدات',
};

interface NinjaInvoiceUploadScreenProps {
  initialPeriod?: string;
  platform?: 'ninja' | 'keeta';
}

const isCompanyMatch = (invoiceCompanyId?: string, target?: string) => {
  if (!invoiceCompanyId || !target) return false;
  if (invoiceCompanyId === target) return true;
  const targetUpper = target.toUpperCase();
  if (targetUpper === 'BW' && (invoiceCompanyId === '019c18d5-62e1-7000-8000-000000000041' || invoiceCompanyId === 'BW')) return true;
  if (targetUpper === 'EX' && (invoiceCompanyId === '019c18d5-62e1-7000-8000-000000000042' || invoiceCompanyId === 'EX')) return true;
  if (targetUpper === 'TR' && (invoiceCompanyId === '019c18d5-62e1-7000-8000-000000000040' || invoiceCompanyId === 'TR')) return true;
  return false;
};

export function NinjaInvoiceUploadScreen({
  initialPeriod = '2026-10',
  platform = 'ninja',
}: NinjaInvoiceUploadScreenProps) {
  const { user } = useAuth();
  const { toast } = useToast();
  const isKeeta = platform === 'keeta';
  const platformNameAr = isKeeta ? 'كيتا' : 'نينجا';
  const platformPrefix = isKeeta ? 'KEETA' : 'NINJA';

  const [period, setPeriod] = useState<string>(initialPeriod);
  const [companyId, setCompanyId] = useState<string>('BW');
  const [odooVoucherNo, setOdooVoucherNo] = useState<string>('');

  // Keeta conditional parser inputs (shown only when API needs them or workbook has >1 sheet)
  const [sheetName, setSheetName] = useState<string>('');
  const [companyRowsInput, setCompanyRowsInput] = useState<string>('');
  const [needsSheetName, setNeedsSheetName] = useState<boolean>(false);
  const [needsCompanyRows, setNeedsCompanyRows] = useState<boolean>(false);
  const [detectedSheets, setDetectedSheets] = useState<string[]>([]);

  // Upload mode: consolidated (ملف مجمع للكل) or per-account (تخصيص حسابات المنصة)
  const [uploadMode, setUploadMode] = useState<'all' | 'per_account'>('all');
  const [availableAccounts, setAvailableAccounts] = useState<Array<{ id: string; code: string; name?: string; externalAccountId?: string }>>([]);
  const [selectedAccountIds, setSelectedAccountIds] = useState<string[]>([]);
  const [accountSearch, setAccountSearch] = useState<string>('');

  const [selectedFiles, setSelectedFiles] = useState<File[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [submittingAction, setSubmittingAction] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Stepper state: 1: رفع -> 2: مطابقة -> 3: تأكيد -> 4: اعتماد -> 5: ترحيل
  const [stepperStage, setStepperStage] = useState<number>(1);

  // Reconcile and preview results
  const [reconcileResult, setReconcileResult] = useState<NinjaReconcileResponse | null>(null);
  const [serverPreview, setServerPreview] = useState<ApiNinjaInvoice | null>(null);
  const [currentInvoice, setCurrentInvoice] = useState<ApiNinjaInvoice | null>(null);
  const [savedInvoices, setSavedInvoices] = useState<ApiNinjaInvoice[]>([]);
  const [, setLoadingSavedInvoices] = useState<boolean>(false);

  // Dispute modal state
  const [disputeModalOpen, setDisputeModalOpen] = useState<boolean>(false);
  const [disputeReason, setDisputeReason] = useState<string>('');
  const [isResolvingDispute, setIsResolvingDispute] = useState<boolean>(false);
  const [selectedDisputeId, setSelectedDisputeId] = useState<string>('');

  // Active invoice tab if multiple files
  const [activeInvoiceIdx, setActiveInvoiceIdx] = useState<number>(0);

  // Deduction classification (required before approval)
  const [classLevel, setClassLevel] = useState<Record<string, 'account_level' | 'company_level'>>({});
  const [classAccount, setClassAccount] = useState<Record<string, string>>({});
  const [costAccounts, setCostAccounts] = useState<Array<{ code: string; nameAr?: string; posting?: boolean }>>([]);

  // Filters for the captains table
  const [filterMode, setFilterMode] = useState<'all' | 'different' | 'matched'>('all');
  const [searchCaptain] = useState<string>('');

  // Load platform accounts for the selected company
  useEffect(() => {
    type OperationAccount = { id: string; code: string; name?: string; externalAccountId?: string; platformCode?: string; companyId?: string };
    let cancelled = false;
    (async () => {
      const found: OperationAccount[] = [];
      for (let page = 1; page <= 20; page++) {
        const res = (await api.getOperations('platform_account', { page, pageSize: 200, q: platform })) as { items?: OperationAccount[]; total?: number };
        const items = res?.items || [];
        found.push(...items);
        if (items.length === 0 || found.length >= (res?.total ?? 0)) break;
      }
      if (!cancelled) {
        setAvailableAccounts(
          found.filter(
            (a) => a.platformCode?.toLowerCase().includes(platform) && isCompanyMatch(a.companyId, companyId),
          ),
        );
      }
    })().catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [companyId, platform]);

  const selectSavedInvoice = async (inv: ApiNinjaInvoice) => {
    if (isKeeta && inv.id && !Array.isArray(inv.lines)) {
      try {
        const lines = await api.getKeetaInvoiceLines(inv.id);
        setCurrentInvoice({ ...inv, lines });
      } catch {
        setCurrentInvoice(inv);
      }
    } else {
      setCurrentInvoice(inv);
    }
    if (inv.journalEntryId) setStepperStage(5);
    else if (inv.approvalStatus === 'approved') setStepperStage(4);
    else setStepperStage(3);
  };

  // Load saved invoices from server
  const loadSavedInvoices = async (replaceCurrent = false) => {
    setLoadingSavedInvoices(true);
    try {
      const res = isKeeta
        ? await api.getKeetaInvoices({ pageSize: 50 })
        : await api.getNinjaInvoices({ pageSize: 50 });
      const items = (res?.items || []) as ApiNinjaInvoice[];
      setSavedInvoices(items);
      const match = items.find((i) => isCompanyMatch(i.companyId, companyId) && i.period === period);
      if (match && (replaceCurrent || !currentInvoice)) {
        await selectSavedInvoice(match);
      }
    } catch {
      // ignore
    } finally {
      setLoadingSavedInvoices(false);
    }
  };

  const startNextInvoice = () => {
    setCurrentInvoice(null);
    setServerPreview(null);
    setReconcileResult(null);
    setSelectedFiles([]);
    setSelectedAccountIds([]);
    setSheetName('');
    setCompanyRowsInput('');
    setNeedsSheetName(false);
    setNeedsCompanyRows(false);
    setDetectedSheets([]);
    setActionError(null);
    setStepperStage(1);
  };

  // Another company or month is another invoice: never keep showing the previous one.
  useEffect(() => {
    startNextInvoice();
    loadSavedInvoices(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [companyId, period, platform]);

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    setServerPreview(null);
    setReconcileResult(null);
    const files = Array.from(e.target.files || []);
    if (files.length === 0) return;
    if (files.length > (isKeeta ? 1 : 5)) {
      toast(
        isKeeta
          ? 'يرجى رفع ملف فاتورة كيتا واحد (.xlsx) في كل مرة'
          : 'الحد الأقصى هو 5 ملفات فواتير نينجا في المرة الواحدة',
        'warning',
      );
      return;
    }
    const nonXlsx = files.find((f) => !f.name.toLowerCase().endsWith('.xlsx'));
    if (nonXlsx) {
      toast(`جميع فواتير ${platformNameAr} يجب أن تكون ملفات Excel بصيغة .xlsx`, 'danger');
      return;
    }
    setSelectedFiles(files);
    setStepperStage(1);
    setActionError(null);

    if (isKeeta && files[0]) {
      files[0]
        .arrayBuffer()
        .then((buf) => {
          const text = new TextDecoder('utf-8', { fatal: false }).decode(new Uint8Array(buf));
          const matches = Array.from(text.matchAll(/<sheet\b[^>]*\bname="([^"]+)"/g)).map((m) => m[1]);
          if (matches.length > 1) {
            setDetectedSheets(matches);
            setNeedsSheetName(true);
          } else {
            setDetectedSheets([]);
          }
        })
        .catch(() => {});
    }
  };

  const handleRemoveFile = (idx: number) => {
    setSelectedFiles((prev) => prev.filter((_, i) => i !== idx));
  };

  const readFileBase64 = (file: File): Promise<{ fileName: string; dataBase64: string }> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const base64 = (reader.result as string).split(',')[1] || '';
        resolve({ fileName: file.name, dataBase64: base64 });
      };
      reader.onerror = () => reject(new Error(`فشل قراءة الملف ${file.name}`));
      reader.readAsDataURL(file);
    });
  };

  // A company can have several invoices a month; the file name keeps each default reference distinct.
  const invoiceReference = () => `${platformPrefix}-${period}-${selectedFiles[0]?.name ?? ''}`.slice(0, 200);

  const appendKeetaOptions = (formData: FormData) => {
    if (!isKeeta) return;
    if (sheetName.trim()) {
      formData.append('sheetName', sheetName.trim());
    }
    if (companyRowsInput.trim()) {
      const rowsArr = companyRowsInput
        .split(/[,\s]+/)
        .map((s) => parseInt(s.trim(), 10))
        .filter((n) => Number.isFinite(n) && n > 0);
      if (rowsArr.length > 0) {
        formData.append('companyRows', JSON.stringify(rowsArr));
      }
    }
  };

  const inspectKeetaError = (msg: string) => {
    if (!isKeeta) return;
    if (msg.includes('ورقة الفاتورة') || msg.includes('sheetName')) {
      setNeedsSheetName(true);
    }
    if (msg.includes('سطر تعديل الشركة') || msg.includes('companyRows')) {
      setNeedsCompanyRows(true);
      const rowMatch = /صف\s+(\d+)/.exec(msg);
      if (rowMatch && rowMatch[1]) {
        const rowNum = rowMatch[1];
        setCompanyRowsInput((prev) => {
          const existing = prev
            .split(/[,\s]+/)
            .map((s) => s.trim())
            .filter(Boolean);
          return existing.includes(rowNum) ? prev : [...existing, rowNum].join(', ');
        });
      }
    }
  };

  const loadCostAccounts = () => {
    if (costAccounts.length > 0) return;
    api.getTargetCoa()
      .then((res) => setCostAccounts((res?.accounts || []).filter((a) => a.code.startsWith('5') && a.posting)))
      .catch(() => {});
  };

  const handleClassifyCategory = async (group: DeductionGroup) => {
    if (!currentInvoice?.id) return;
    const level = classLevel[group.category] ?? (group.companyOnly ? 'company_level' : 'account_level');
    const expenseAccount = classAccount[group.category];
    if (level === 'company_level' && !expenseAccount) {
      toast('اختر حساب التكلفة لخصم الشركة', 'warning');
      return;
    }
    setSubmittingAction(`classify:${group.category}`);
    setActionError(null);
    try {
      const items = group.lineIds.map((lineId) => ({
        lineId,
        category: group.category,
        level,
        ...(level === 'company_level' ? { expenseAccount } : {}),
      }));
      const updated = isKeeta
        ? await api.classifyKeetaInvoiceBatch(currentInvoice.id, items, currentInvoice)
        : await api.classifyNinjaInvoiceBatch(currentInvoice.id, items);
      setCurrentInvoice(updated);
      toast(`تم تصنيف ${group.lineIds.length} خصم`, 'success');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'تعذر تصنيف الخصومات';
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setSubmittingAction(null);
    }
  };

  // Step 2: Preview and Reconcile
  const handleRunReconcile = async () => {
    if (selectedFiles.length === 0) {
      toast(`يرجى اختيار ملف فاتورة ${platformNameAr} واحد على الأقل (.xlsx)`, 'warning');
      return;
    }
    setLoading(true);
    setActionError(null);
    try {
      if (!isKeeta) {
        // 1. Reconcile detailed captain lines (Ninja only)
        const filePayloads = await Promise.all(selectedFiles.map((f) => readFileBase64(f)));
        const res = await api.reconcileNinjaInvoices({
          period,
          files: filePayloads,
        });
        setReconcileResult(res);
        setActiveInvoiceIdx(0);

        // The server stores one invoice per file; never drop the other files silently.
        if (selectedFiles.length > 1) {
          setServerPreview(null);
          setStepperStage(2);
          setActionError('تمت المطابقة للعلم. للحفظ ارفع كل ملف لوحده: كل ملف فاتورة مستقلة.');
          return;
        }
      }

      // 2. Run server preview check
      const formData = new FormData();
      formData.append('file', selectedFiles[0]);
      formData.append('companyId', companyId);
      formData.append('period', period);
      formData.append('reference', invoiceReference());
      formData.append('odooVoucherNo', odooVoucherNo);
      if (!isKeeta && uploadMode === 'per_account' && selectedAccountIds.length > 0) {
        selectedAccountIds.forEach((id) => formData.append('accountIds[]', id));
      }
      appendKeetaOptions(formData);

      const preview = isKeeta
        ? await api.uploadKeetaInvoice(formData, false)
        : await api.uploadNinjaInvoice(formData, false);
      setServerPreview(preview);
      setStepperStage(2);
      toast(`تمت عملية معاينة ومطابقة فاتورة ${platformNameAr} بنجاح`, 'success');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : `فشلت مطابقة فاتورة ${platformNameAr}`;
      inspectKeetaError(msg);
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setLoading(false);
    }
  };

  // Step 3: Confirm and Commit Invoice
  const handleConfirmInvoice = async () => {
    if (selectedFiles.length === 0) return;
    setSubmittingAction('confirm');
    setActionError(null);
    try {
      const formData = new FormData();
      formData.append('file', selectedFiles[0]);
      formData.append('companyId', companyId);
      formData.append('period', period);
      formData.append('reference', invoiceReference());
      formData.append('odooVoucherNo', odooVoucherNo);
      formData.append('confirm', 'true');
      if (serverPreview && 'fingerprint' in serverPreview && serverPreview.fingerprint) {
        formData.append('fingerprint', String(serverPreview.fingerprint));
      }
      if (serverPreview && 'rows' in serverPreview && serverPreview.rows) {
        formData.append('rows', JSON.stringify(serverPreview.rows));
      }
      if (!isKeeta && uploadMode === 'per_account' && selectedAccountIds.length > 0) {
        selectedAccountIds.forEach((id) => formData.append('accountIds[]', id));
      }
      appendKeetaOptions(formData);

      const confirmed = isKeeta
        ? await api.uploadKeetaInvoice(formData, true)
        : await api.uploadNinjaInvoice(formData, true);
      setCurrentInvoice(confirmed);
      setStepperStage(3);
      toast(`تم تأكيد ورفع فاتورة ${platformNameAr} بنجاح`, 'success');
      loadSavedInvoices();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'فشل تأكيد الفاتورة';
      inspectKeetaError(msg);
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setSubmittingAction(null);
    }
  };

  // Step 4: Approve Invoice (Allowed if allowedActions.includes('approve'))
  const handleApproveInvoice = async () => {
    if (!currentInvoice?.id) return;
    setSubmittingAction('approve');
    setActionError(null);
    try {
      const updated = isKeeta
        ? await api.approveKeetaInvoice(currentInvoice.id)
        : await api.approveNinjaInvoice(currentInvoice.id);
      setCurrentInvoice(updated);
      setStepperStage(4);
      toast(`تم اعتماد فاتورة ${platformNameAr} بنجاح`, 'success');
      loadSavedInvoices();
    } catch (err: unknown) {
      // Maker != Checker message from API is shown verbatim as is
      const msg = err instanceof Error ? err.message : 'تعذر اعتماد الفاتورة';
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setSubmittingAction(null);
    }
  };

  // Step 5: Post Invoice (Allowed if allowedActions.includes('post'))
  const handlePostInvoice = async () => {
    if (!currentInvoice?.id) return;
    setSubmittingAction('post');
    setActionError(null);
    try {
      const updated = isKeeta
        ? await api.postKeetaInvoice(currentInvoice.id)
        : await api.postNinjaInvoice(currentInvoice.id);
      setCurrentInvoice(updated);
      setStepperStage(5);
      toast('تم ترحيل الفاتورة للقيد العام', 'success');
      loadSavedInvoices();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'تعذر ترحيل الفاتورة';
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setSubmittingAction(null);
    }
  };

  // Dispute and Resolve Actions
  const handleDisputeSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentInvoice?.id || !disputeReason.trim()) return;
    setSubmittingAction(isResolvingDispute ? 'resolve' : 'dispute');
    setActionError(null);
    try {
      if (isResolvingDispute) {
        const updated = isKeeta
          ? await api.resolveKeetaDispute(
              currentInvoice.id,
              { disputeId: selectedDisputeId, reason: disputeReason.trim() },
              currentInvoice,
            )
          : await api.resolveNinjaDispute(currentInvoice.id, {
              disputeId: selectedDisputeId,
              reason: disputeReason.trim(),
            });
        setCurrentInvoice(updated);
        toast('تم حسم الاعتراض بنجاح', 'success');
      } else {
        const updated = isKeeta
          ? await api.disputeKeetaInvoice(
              currentInvoice.id,
              { reason: disputeReason.trim() },
              currentInvoice,
            )
          : await api.disputeNinjaInvoice(currentInvoice.id, {
              reason: disputeReason.trim(),
            });
        setCurrentInvoice(updated);
        toast('تم تسجيل الاعتراض الرسمي بنجاح', 'success');
      }
      setDisputeModalOpen(false);
      setDisputeReason('');
      loadSavedInvoices();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'فشل معالجة الاعتراض';
      setActionError(msg);
      toast(msg, 'danger');
    } finally {
      setSubmittingAction(null);
    }
  };

  // Currently active invoice result from reconcile
  const activeInvoice: NinjaInvoiceResult | null = useMemo(() => {
    if (!reconcileResult || !reconcileResult.invoices) return null;
    return reconcileResult.invoices[activeInvoiceIdx] || reconcileResult.invoices[0] || null;
  }, [reconcileResult, activeInvoiceIdx]);

  // Overall totals across all uploaded invoices
  const overallTotals = useMemo(() => {
    if (!reconcileResult || !reconcileResult.invoices) return null;
    let totalCaptains = 0;
    let totalMatched = 0;
    let totalDifferent = 0;
    let totalAdjustments = 0;
    let invoiceCostSum = 0;
    let engineCostSum = 0;

    for (const inv of reconcileResult.invoices) {
      const matched = inv.matched || 0;
      const diff = inv.different || 0;
      totalCaptains += matched + diff;
      totalMatched += matched;
      totalDifferent += diff;
      totalAdjustments += (inv.adjustments || []).length;

      const costObj = inv.totals?.totalCost;
      if (costObj) {
        invoiceCostSum += costObj.invoice || 0;
        engineCostSum += costObj.engine || 0;
      }
    }

    return {
      totalCaptains,
      totalMatched,
      totalDifferent,
      totalAdjustments,
      invoiceCost: invoiceCostSum,
      engineCost: engineCostSum,
      diffCost: invoiceCostSum - engineCostSum,
    };
  }, [reconcileResult]);

  // Filtered lines for active invoice
  const filteredLines = useMemo(() => {
    if (!activeInvoice || !activeInvoice.lines) return [];
    return activeInvoice.lines.filter((line) => {
      const isDiff = (line.diffs && line.diffs.length > 0) || line.status === 'error';
      if (filterMode === 'different' && !isDiff) return false;
      if (filterMode === 'matched' && isDiff) return false;
      if (!searchCaptain.trim()) return true;
      return line.captainId.toLowerCase().includes(searchCaptain.toLowerCase().trim());
    });
  }, [activeInvoice, filterMode, searchCaptain]);

  const mapReasonText = (reason: string): string => {
    if (reason.startsWith('capability_label_edited:')) {
      const label = reason.split(':')[1] || '';
      return `تعديل نوع المركبة من المنصة (${label})`;
    }
    if (reason === 'contract_amount_differs_from_terms') {
      return 'مبلغ العقد في الفاتورة يختلف عن الشروط المعتمدة';
    }
    if (reason === 'unexplained') {
      return 'فروقات تفصيلية في بنود الأوامر أو الساعات';
    }
    return reason;
  };

  const mapFieldLabel = (field: string): string => {
    const map: Record<string, string> = {
      finalAmount: 'المبلغ النهائي',
      totalCost: 'التكلفة الإجمالية (شامل الضريبة)',
      targetTotal: 'استحقاق التارجت',
      attendanceDeduction: 'خصم نسبة الحضور',
      shortDaysDeduction: 'خصم الأيام الأقل من 8 ساعات',
      extraOrderRate: 'قيمة الطلبات الإضافية',
      missingOrderRate: 'خصم الطلبات الناقصة',
      vat: 'ضريبة القيمة المضافة',
    };
    return map[field] || field;
  };

  const captainColumns: ColumnDef<NinjaReconciliationLine>[] = useMemo(
    () => [
      {
        key: 'row',
        header: '#',
        numeric: true,
        width: '60px',
        getValue: (line) => line.row,
        render: (line) => <span className="font-mono text-[11px] text-[var(--muted)]">{line.row}</span>,
      },
      {
        key: 'captainId',
        header: 'معرف الكابتن (Captain ID)',
        getValue: (line) => line.captainId,
        render: (line) => <span className="font-mono font-bold text-xs text-[var(--ink)]">{line.captainId}</span>,
      },
      {
        key: 'status',
        header: 'حالة المطابقة',
        getValue: (line) => {
          const hasDiffs = (line.diffs && line.diffs.length > 0) || line.status === 'error';
          return hasDiffs ? 'يوجد فرق' : 'مطابق 100%';
        },
        render: (line) => {
          const hasDiffs = (line.diffs && line.diffs.length > 0) || line.status === 'error';
          return hasDiffs ? (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-amber-500/10 text-amber-700 dark:text-amber-400">
              <AlertTriangle size={12} />
              <span>يوجد فرق</span>
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
              <CheckCircle size={12} />
              <span>مطابق 100%</span>
            </span>
          );
        },
      },
      {
        key: 'reasons',
        header: 'أسباب الفروقات',
        getValue: (line) => (line.reasons || []).map(mapReasonText).join(', ') || line.reason || '',
        render: (line) => {
          if (line.reasons && line.reasons.length > 0) {
            return (
              <div className="flex flex-wrap gap-1">
                {line.reasons.map((r, ri) => (
                  <span
                    key={ri}
                    className="px-2 py-0.5 rounded-md bg-[var(--raised)] border border-[var(--line)] text-[10px] font-semibold text-[var(--ink)]"
                  >
                    {mapReasonText(r)}
                  </span>
                ))}
              </div>
            );
          }
          if (line.reason) {
            return <span className="text-[11px] text-[var(--danger)]">{line.reason}</span>;
          }
          return <span className="text-[var(--muted)] text-[11px]">—</span>;
        },
      },
      {
        key: 'diffs',
        header: 'تفاصيل الفروقات (الفاتورة vs المحرك)',
        getValue: (line) =>
          (line.diffs || [])
            .map((d) => `${mapFieldLabel(d.field)}: فاتورة ${d.invoice} | محرك ${d.engine}`)
            .join('; ') || 'مطابق',
        render: (line) => {
          if (line.diffs && line.diffs.length > 0) {
            return (
              <div className="space-y-1">
                {line.diffs.map((d, di) => (
                  <div
                    key={di}
                    className="inline-flex items-center gap-2 p-1 px-2 rounded-lg bg-[var(--raised)] text-[10px] num font-mono me-1 mb-1"
                  >
                    <span className="font-sans font-bold text-[var(--ink)]">
                      {mapFieldLabel(d.field)}:
                    </span>
                    <span className="text-[var(--muted)]">فاتورة: {d.invoice.toFixed(2)}</span>
                    <span>|</span>
                    <span className="text-[var(--muted)]">محرك: {d.engine.toFixed(2)}</span>
                    <span>|</span>
                    <span className="text-amber-600 font-bold">
                      الفرق: {d.difference > 0 ? `+${d.difference.toFixed(2)}` : d.difference.toFixed(2)}
                    </span>
                  </div>
                ))}
              </div>
            );
          }
          return (
            <span className="text-[11px] text-emerald-600 font-medium">
              كافة البنود مطابقة بدقة السنت
            </span>
          );
        },
      },
    ],
    []
  );

  // Stepper definition
  const stepperSteps = [
    { num: 1, title: 'رفع الملف', desc: 'اختيار ملف .xlsx' },
    { num: 2, title: 'المطابقة', desc: isKeeta ? 'معاينة شرائح الفاتورة' : 'مطابقة شروط التارجت' },
    { num: 3, title: 'التأكيد', desc: 'تثبيت الفاتورة بالنظام' },
    { num: 4, title: 'الاعتماد', desc: 'موافقة المراجع المستقل' },
    { num: 5, title: 'الترحيل', desc: 'قيد الحسابات العامة' },
  ];

  // Use server-supplied allowedActions/approvalBlockers when present (Ninja); compute fallback when omitted (Keeta).
  const computedView = useMemo(() => {
    if (!currentInvoice) return { actions: [] as string[], blockers: [] as string[] };
    if (Array.isArray(currentInvoice.allowedActions)) {
      return {
        actions: currentInvoice.allowedActions,
        blockers: currentInvoice.approvalBlockers || [],
      };
    }
    const actions: string[] = [];
    const blockers: string[] = [];
    const username = (user?.username || '').toLowerCase();
    const createdBy = (currentInvoice.createdBy || '').toLowerCase();
    const independent = Boolean(username && createdBy && username !== createdBy);
    const posted = Boolean(currentInvoice.journalEntryId);
    const status = currentInvoice.approvalStatus || 'pending';
    type RuntimeDeduction = {
      category: string;
      amount: number;
      level: string;
      expenseAccount?: string;
      classifiedBy?: string;
    };
    const parts: RuntimeDeduction[] = (currentInvoice.lines || []).flatMap(
      (l) => (l.deductions || []) as RuntimeDeduction[],
    );
    const disputes = currentInvoice.disputes || [];

    if (!posted) {
      if (status === 'pending' && parts.length > 0) actions.push('classify');
      const allClassified = parts.every(
        (d) =>
          (d.level === 'company_level' || d.level === 'account_level') &&
          (d.classifiedBy || '').toLowerCase() !== username,
      );
      if (status === 'pending' && independent && allClassified) actions.push('approve');
      actions.push('dispute');
      if (disputes.some((d) => d.status === 'open' && (d.createdBy || '').toLowerCase() !== username)) {
        actions.push('resolve');
      }
      if (
        status === 'approved' &&
        independent &&
        disputes.every((d) => d.status === 'resolved') &&
        parts.every((d) => d.level === 'company_level')
      ) {
        actions.push('post');
      }
    }

    if (!posted && status === 'pending') {
      if (!independent) {
        blockers.push('أنت من رفع هذه الفاتورة؛ يعتمدها مستخدم آخر (فصل الصلاحيات).');
      }
      const unclassified = parts.filter(
        (d) => d.level !== 'company_level' && d.level !== 'account_level',
      ).length;
      if (unclassified > 0) {
        blockers.push(`${unclassified} خصم بلا تصنيف؛ صنّفها أولًا (على اليوزر أو على الشركة).`);
      }
      const ownClass = parts.filter(
        (d) => (d.classifiedBy || '').toLowerCase() === username,
      ).length;
      if (ownClass > 0) {
        blockers.push(`أنت صنّفت ${ownClass} خصم؛ يعتمدها مستخدم غير الذي صنّف.`);
      }
    }
    return { actions, blockers };
  }, [currentInvoice, user?.username]);

  const allowedActions = computedView.actions;
  const approvalBlockers = computedView.blockers;
  const deductionGroups: DeductionGroup[] = Object.values(
    (currentInvoice?.lines || []).reduce<Record<string, DeductionGroup>>((groups, line) => {
      for (const d of line.deductions || []) {
        const group = (groups[d.category] ??= { category: d.category, lineIds: [], amount: 0, unclassified: 0, companyOnly: true });
        group.lineIds.push(line.id);
        group.amount += d.amount;
        if (d.level !== 'account_level' && d.level !== 'company_level') group.unclassified += 1;
        if (line.lineType !== 'company') group.companyOnly = false;
      }
      return groups;
    }, {}),
  );

  const savedThisMonth = savedInvoices.filter((i) => i.period === period);
  const primaryButton = 'px-4 py-2 rounded-xl bg-[var(--accent)] text-white text-xs sm:text-sm font-bold cursor-pointer disabled:opacity-50';
  const nextInvoiceButton = (
    <button type="button" onClick={startNextInvoice} className="px-4 py-2 rounded-xl border border-[var(--line)] text-[var(--ink)] text-xs sm:text-sm font-bold cursor-pointer">
      رفع فاتورة سجل آخر
    </button>
  );
  const nextStep: { title: string; detail?: string; action?: React.ReactNode } = (() => {
    if (currentInvoice?.journalEntryId) {
      return { title: 'الفاتورة مرحّلة ومقيدة.', action: nextInvoiceButton };
    }
    if (currentInvoice?.approvalStatus === 'approved') {
      return {
        title: 'الفاتورة معتمدة. افتح مسودة المراجعة لإنشاء دورة الرواتب.',
        action: (
          <>
            <a href={`/app/payroll?tab=cycle&period=${period}`} className={primaryButton}>فتح مسودة المراجعة</a>
            {nextInvoiceButton}
          </>
        ),
      };
    }
    if (currentInvoice) {
      if (allowedActions.includes('approve')) {
        return {
          title: 'راجع الفاتورة ثم اعتمدها.',
          action: (
            <button type="button" disabled={submittingAction === 'approve'} onClick={handleApproveInvoice} className={primaryButton}>
              اعتماد الفاتورة
            </button>
          ),
        };
      }
      const unclassified = deductionGroups.reduce((n, g) => n + g.unclassified, 0);
      if (unclassified > 0) {
        return { title: `صنّف ${unclassified} خصم من اللوحة أسفل الصفحة.`, detail: 'لكل نوع خصم: على اليوزر أم على الشركة.', action: nextInvoiceButton };
      }
      return { title: 'الفاتورة محفوظة وتنتظر مراجعًا آخر.', detail: approvalBlockers.join(' '), action: nextInvoiceButton };
    }
    if (serverPreview) {
      return {
        title: 'المعاينة سليمة ولم تُحفظ بعد. أكّد لحفظ الفاتورة.',
        action: (
          <button type="button" disabled={submittingAction === 'confirm'} onClick={handleConfirmInvoice} className={primaryButton}>
            تأكيد وحفظ الفاتورة
          </button>
        ),
      };
    }
    if (actionError) {
      return {
        title: 'الفاتورة لم تُحفظ. أصلح السبب المذكور ثم أعد المطابقة.',
        detail: actionError,
        action: selectedFiles.length > 0 ? (
          <button type="button" disabled={loading} onClick={handleRunReconcile} className={primaryButton}>
            إعادة المطابقة والمعاينة
          </button>
        ) : undefined,
      };
    }
    if (selectedFiles.length > 0) {
      return {
        title: `الملف جاهز لـ ${COMPANY_LABELS[companyId] ?? companyId} وشهر ${period}. ابدأ المطابقة.`,
        action: (
          <button type="button" disabled={loading} onClick={handleRunReconcile} className={primaryButton}>
            بدء المطابقة والمعاينة
          </button>
        ),
      };
    }
    return { title: 'اختر الشركة والشهر من الأعلى، ثم ارفع ملف الفاتورة.' };
  })();

  return (
    <div className="space-y-6">
      {/* Header card with Company, Period and Stepper */}
      <div className="p-4 sm:p-5 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="flex items-center gap-2">
            <div className="p-2.5 rounded-xl bg-orange-500/10 text-orange-600 dark:text-orange-400">
              <FileSpreadsheet size={24} />
            </div>
            <div>
              <h2 className="text-base sm:text-lg font-bold text-[var(--ink)]">
                مسار فاتورة {platformNameAr} ({isKeeta ? 'Keeta' : 'Ninja'} Invoice Stepper)
              </h2>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                دورة الفاتورة المعتمدة: رفع ← مطابقة ← تأكيد ← اعتماد ← ترحيل (BR §26, §31).
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
              <Building size={14} className="text-[var(--muted)]" />
              <span className="text-xs text-[var(--muted)]">الشركة:</span>
              <select
                value={companyId}
                onChange={(e) => setCompanyId(e.target.value)}
                className="text-xs font-bold bg-[var(--surface)] text-[var(--ink)] border-none focus:outline-hidden cursor-pointer"
              >
                <option value="BW">البوابة المقبلة (BW)</option>
                <option value="EX">إكسبرس جايت (EX)</option>
                <option value="TR">طريق الرواد (TR)</option>
              </select>
            </div>

            <div className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
              <Calendar size={14} className="text-[var(--muted)]" />
              <span className="text-xs text-[var(--muted)]">الشهر:</span>
              <input
                type="month"
                value={period}
                onChange={(e) => setPeriod(e.target.value)}
                className="text-xs font-bold font-mono bg-transparent text-[var(--ink)] focus:outline-hidden"
              />
            </div>
          </div>
        </div>

        {/* Stepper Progress Bar */}
        <div className="pt-2 border-t border-[var(--line)]">
          <div className="grid grid-cols-5 gap-2">
            {stepperSteps.map((step) => {
              const isDone = stepperStage > step.num || (step.num === 5 && currentInvoice?.journalEntryId);
              const isCurrent = stepperStage === step.num;
              return (
                <div
                  key={step.num}
                  className={`flex flex-col items-center text-center p-2 rounded-xl transition ${
                    isDone
                      ? 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
                      : isCurrent
                      ? 'bg-[var(--accent)]/10 text-[var(--accent)] font-bold border border-[var(--accent)]/30'
                      : 'bg-[var(--raised)]/40 text-[var(--muted)]'
                  }`}
                >
                  <div className="flex items-center gap-1 text-xs">
                    {isDone ? (
                      <CheckCircle2 size={14} className="text-emerald-600 dark:text-emerald-400" />
                    ) : (
                      <span className="w-4 h-4 rounded-full bg-[var(--line)] text-[10px] flex items-center justify-center font-bold">
                        {step.num}
                      </span>
                    )}
                    <span>{step.title}</span>
                  </div>
                  <span className="text-[10px] hidden sm:inline mt-0.5 opacity-80">{step.desc}</span>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Action Error Alert (e.g. Maker != Checker message from API verbatim) */}
      {/* Next step: one clear instruction and one button, always on screen */}
      <div id="ninja-next-step" className="p-4 rounded-2xl border-2 border-[var(--accent)]/50 bg-[var(--accent)]/5 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="space-y-1">
          <p className="text-xs font-bold text-[var(--accent)]">الخطوة التالية</p>
          <p className="text-sm font-bold text-[var(--ink)]">{nextStep.title}</p>
          {nextStep.detail && <p className="text-xs text-[var(--muted)]">{nextStep.detail}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2 shrink-0">{nextStep.action}</div>
      </div>

      {savedThisMonth.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-[var(--ink)]">
          <span className="font-bold">فواتير {period}:</span>
          {savedThisMonth.map((i) => (
            <button
              key={i.id}
              type="button"
              onClick={() => selectSavedInvoice(i)}
              className={`px-2.5 py-1 rounded-full border cursor-pointer ${
                currentInvoice?.id === i.id ? 'border-[var(--accent)] font-bold' : 'border-[var(--line)]'
              }`}
            >
              {COMPANY_LABELS[i.companyId] ?? i.companyId}: {i.approvalStatus === 'approved' ? 'معتمدة' : 'بانتظار الاعتماد'}
            </button>
          ))}
        </div>
      )}

      {actionError && (
        <div className="p-4 rounded-2xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)] space-y-1">
          <div className="flex items-center gap-2 font-bold text-xs sm:text-sm">
            <AlertCircle size={16} className="shrink-0" />
            <span>رسالة النظام الرقابي:</span>
          </div>
          <p className="text-xs leading-relaxed ps-6 font-medium">{actionError}</p>
        </div>
      )}

      {/* Prominent BR §31 Rule Banner */}
      <div className="p-4 rounded-2xl border border-sky-500/20 bg-sky-500/5 text-sky-900 dark:text-sky-200 space-y-1">
        <div className="flex items-center gap-2 font-bold text-xs sm:text-sm">
          <Info size={16} className="text-sky-600 dark:text-sky-400 shrink-0" />
          <span>المعيار الملزم (BR §31): «المعتمد هو الفاتورة — الفرق للعلم»</span>
        </div>
        <p className="text-xs text-sky-800/80 dark:text-sky-300/80 leading-relaxed ps-6">
          في منصة {platformNameAr}، الاستحقاق المالي المعتمد للكباتن هو ما ورد نصاً في فاتورة المنصة الصادرة؛ وفروقات الاحتساب المحسوبة بواسطة المحرك الداخلي هي للعلم والتدقيق التشغيلي مع المنصة ولا توقف الاعتماد أو الصرف.
        </p>
      </div>

      {/* STEP 1: Upload Settings & File Picker */}
      <div className="p-5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-[var(--line)] pb-3">
          <div>
            <h3 className="text-xs sm:text-sm font-bold text-[var(--ink)]">1. إعدادات رفع الفاتورة وتخصيص الحسابات:</h3>
            <p className="text-[11px] text-[var(--muted)] mt-0.5">
              {isKeeta
                ? 'ارفع ملف فاتورة كيتا الرسمي (.xlsx) بعد اختيار الشركة والشهر من الأعلى.'
                : 'حدد وضع الرفع (ملف مجمع للكل أو تخصيص حسابات منصة نينجا محددة).'}
            </p>
          </div>

          {/* Mode Switcher (Ninja only) */}
          {!isKeeta && (
            <div className="flex items-center gap-1 p-1 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
              <button
                type="button"
                onClick={() => setUploadMode('all')}
                className={`px-3 py-1 text-xs font-bold rounded-lg transition ${
                  uploadMode === 'all'
                    ? 'bg-[var(--surface)] text-[var(--ink)] shadow-xs'
                    : 'text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                ملف مجمع للكل
              </button>
              <button
                type="button"
                onClick={() => setUploadMode('per_account')}
                className={`px-3 py-1 text-xs font-bold rounded-lg transition ${
                  uploadMode === 'per_account'
                    ? 'bg-[var(--surface)] text-[var(--ink)] shadow-xs'
                    : 'text-[var(--muted)] hover:text-[var(--ink)]'
                }`}
              >
                تخصيص حسابات المنصة
              </button>
            </div>
          )}
        </div>

        {/* Per-account searchable multi-select */}
        {!isKeeta && uploadMode === 'per_account' && (
          <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--raised)]/30 space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-[var(--ink)]">
                اختر حسابات منصة نينجا التابعة لشركة ({companyId}):
              </span>
              <span className="text-xs font-mono text-[var(--muted)]">
                {selectedAccountIds.length} من أصل {availableAccounts.length} حساب
              </span>
            </div>

            <div className="relative">
              <Search size={14} className="absolute start-3 top-2.5 text-[var(--muted)]" />
              <input
                type="text"
                value={accountSearch}
                onChange={(e) => setAccountSearch(e.target.value)}
                placeholder="بحث برقم الحساب أو الاسم أو المعرف الخارجي..."
                className="w-full ps-9 pe-3 py-1.5 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden"
              />
            </div>

            <div className="max-h-40 overflow-y-auto space-y-1 p-1 rounded-lg border border-[var(--line)] bg-[var(--surface)]">
              {availableAccounts
                .filter(
                  (a) =>
                    !accountSearch.trim() ||
                    (a.code || '').toLowerCase().includes(accountSearch.toLowerCase()) ||
                    (a.name || '').toLowerCase().includes(accountSearch.toLowerCase()) ||
                    (a.externalAccountId || '').toLowerCase().includes(accountSearch.toLowerCase())
                )
                .map((acc) => {
                  const isSelected = selectedAccountIds.includes(acc.id);
                  return (
                    <label
                      key={acc.id}
                      className="flex items-center justify-between p-2 rounded-lg hover:bg-[var(--row-hover)] cursor-pointer text-xs"
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={(e) => {
                            if (e.target.checked) setSelectedAccountIds((prev) => [...prev, acc.id]);
                            else setSelectedAccountIds((prev) => prev.filter((id) => id !== acc.id));
                          }}
                          className="rounded text-[var(--accent)] focus:ring-0"
                        />
                        <span className="font-mono font-bold text-[var(--ink)]">{acc.code || acc.id}</span>
                        {acc.name && <span className="text-[var(--muted)]">({acc.name})</span>}
                      </div>
                      {acc.externalAccountId && (
                        <span className="font-mono text-[10px] text-[var(--muted)]">
                          معرف: {acc.externalAccountId}
                        </span>
                      )}
                    </label>
                  );
                })}
              {availableAccounts.length === 0 && (
                <div className="p-3 text-center text-xs text-[var(--muted)]">
                  لا توجد حسابات نينجا مسجلة لهذه الشركة.
                </div>
              )}
            </div>
          </div>
        )}

        {/* Odoo voucher is optional; the invoice reference is generated from the file name */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">رقم سند أودو (اختياري):</label>
            <input
              type="text"
              placeholder="مثال: BILL/2026/10/0042"
              value={odooVoucherNo}
              onChange={(e) => setOdooVoucherNo(e.target.value)}
              className="w-full px-3 py-2 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden"
            />
          </div>

          {isKeeta && (needsSheetName || detectedSheets.length > 1) && (
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">ورقة الفاتورة الأصلية (sheetName):</label>
              {detectedSheets.length > 1 ? (
                <SearchableSelect
                  ariaLabel="ورقة الفاتورة الأصلية"
                  value={sheetName}
                  onChange={setSheetName}
                  placeholder="اختر ورقة الفاتورة الأصلية..."
                  options={detectedSheets.map((s) => ({ value: s, label: s }))}
                />
              ) : (
                <input
                  type="text"
                  aria-label="ورقة الفاتورة الأصلية"
                  placeholder="اسم ورقة الفاتورة داخل الملف..."
                  value={sheetName}
                  onChange={(e) => setSheetName(e.target.value)}
                  className="w-full px-3 py-2 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden"
                />
              )}
            </div>
          )}

          {isKeeta && needsCompanyRows && (
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">أسطر تعديل الشركة (companyRows):</label>
              <input
                type="text"
                aria-label="أسطر تعديل الشركة"
                placeholder="مثال: 5"
                value={companyRowsInput}
                onChange={(e) => setCompanyRowsInput(e.target.value)}
                className="w-full px-3 py-2 text-xs rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden font-mono"
              />
            </div>
          )}
        </div>

        {/* Drag & Drop File Upload Box */}
        <div className="border-2 border-dashed border-[var(--line)] hover:border-[var(--accent)]/50 rounded-2xl p-6 text-center transition bg-[var(--raised)]/30">
          <input
            type="file"
            id="ninja-file-input"
            multiple={!isKeeta}
            accept=".xlsx"
            onChange={handleFileSelect}
            className="hidden"
          />
          <label htmlFor="ninja-file-input" className="cursor-pointer flex flex-col items-center justify-center gap-2">
            <div className="p-3 rounded-full bg-[var(--surface)] text-[var(--accent)] shadow-xs">
              <Upload size={22} />
            </div>
            <div className="text-xs font-bold text-[var(--ink)]">
              انقر لاختيار ملف فاتورة {platformNameAr} (.xlsx) أو اسحب الملف هنا
            </div>
            <div className="text-[11px] text-[var(--muted)]">
              {isKeeta ? 'يدعم ملف إكسل رسمي بصيغة .xlsx' : 'يدعم ملفات إكسل رسمية بصيغة .xlsx بحد أقصى 5 ملفات'}
            </div>
          </label>
        </div>

        {/* Selected files list */}
        {selectedFiles.length > 0 && (
          <div className="space-y-2 pt-2">
            <div className="text-xs font-bold text-[var(--ink)]">الملفات المحددة ({selectedFiles.length}):</div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
              {selectedFiles.map((file, idx) => (
                <div
                  key={idx}
                  className="flex items-center justify-between p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
                >
                  <div className="flex items-center gap-2 truncate">
                    <FileSpreadsheet size={16} className="text-emerald-600 shrink-0" />
                    <span className="font-semibold text-[var(--ink)] truncate">{file.name}</span>
                    <span className="text-[10px] text-[var(--muted)] num">({(file.size / 1024).toFixed(0)} KB)</span>
                  </div>
                  <button
                    type="button"
                    onClick={() => handleRemoveFile(idx)}
                    className="p-1 text-[var(--muted)] hover:text-[var(--danger)] transition cursor-pointer"
                  >
                    <X size={14} />
                  </button>
                </div>
              ))}
            </div>

            <div className="pt-2 flex flex-wrap items-center justify-end gap-2">
              <button
                type="button"
                id="btn-run-reconcile"
                disabled={loading}
                onClick={handleRunReconcile}
                className="px-5 py-2.5 rounded-xl bg-orange-600 hover:bg-orange-700 text-white text-xs sm:text-sm font-bold shadow-xs transition flex items-center gap-2 cursor-pointer disabled:opacity-50"
              >
                {loading ? (
                  <>
                    <span className="animate-spin text-sm">⏳</span>
                    <span>جاري مطابقة ومعاينة وقائع الفاتورة...</span>
                  </>
                ) : (
                  <>
                    <ArrowRightLeft size={16} />
                    <span>2. بدء المطابقة والمعاينة</span>
                  </>
                )}
              </button>

              {serverPreview && stepperStage >= 2 && (
                <button
                  type="button"
                  id="btn-confirm-invoice"
                  disabled={submittingAction === 'confirm'}
                  onClick={handleConfirmInvoice}
                  className="px-5 py-2.5 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white text-xs sm:text-sm font-bold shadow-xs transition flex items-center gap-2 cursor-pointer disabled:opacity-50"
                >
                  {submittingAction === 'confirm' ? (
                    <>
                      <span className="animate-spin text-sm">⏳</span>
                      <span>جاري تأكيد الفاتورة...</span>
                    </>
                  ) : (
                    <>
                      <CheckCircle2 size={16} />
                      <span>3. تأكيد ورفع الفاتورة</span>
                    </>
                  )}
                </button>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Action Bar (Buttons rendered ONLY from allowedActions returned by API) */}
      {currentInvoice && (
        <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
          {approvalBlockers.length > 0 && (
            <div id="ninja-approval-blockers" className="p-3 rounded-xl border border-amber-500/40 bg-amber-500/10 space-y-1">
              <p className="text-xs font-bold text-[var(--ink)]">لماذا لا يظهر زر الاعتماد:</p>
              <ul className="list-disc ps-5 text-xs text-[var(--ink)] space-y-0.5">
                {approvalBlockers.map((b) => (
                  <li key={b}>{b}</li>
                ))}
              </ul>
            </div>
          )}

          {allowedActions.includes('classify') && deductionGroups.length > 0 && (
            <div id="ninja-classify-panel" className="p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/30 space-y-2">
              <p className="text-xs font-bold text-[var(--ink)]">تصنيف خصومات الفاتورة: على اليوزر أم على الشركة</p>
              {deductionGroups.map((group) => {
                const level = classLevel[group.category] ?? (group.companyOnly ? 'company_level' : 'account_level');
                return (
                  <div key={group.category} data-category={group.category} className="flex flex-wrap items-center gap-2 text-xs text-[var(--ink)]">
                    <span className="font-bold min-w-40">{DEDUCTION_LABELS[group.category] ?? group.category}</span>
                    <span className="font-mono text-[var(--muted)]">
                      {group.lineIds.length} سطر · {group.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س · {group.unclassified} بلا تصنيف
                    </span>
                    <select
                      aria-label="جهة تحمل الخصم"
                      value={level}
                      disabled={group.companyOnly}
                      onChange={(e) => {
                        const value = e.target.value as 'account_level' | 'company_level';
                        setClassLevel((prev) => ({ ...prev, [group.category]: value }));
                        if (value === 'company_level') loadCostAccounts();
                      }}
                      onFocus={() => group.companyOnly && loadCostAccounts()}
                      className="px-2 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)]"
                    >
                      <option value="account_level">على اليوزر</option>
                      <option value="company_level">على الشركة</option>
                    </select>
                    {level === 'company_level' && (
                      <SearchableSelect
                        ariaLabel="حساب التكلفة"
                        className="min-w-64"
                        value={classAccount[group.category] ?? ''}
                        onFocus={loadCostAccounts}
                        onChange={(code) => setClassAccount((prev) => ({ ...prev, [group.category]: code }))}
                        placeholder="اكتب حساب التكلفة..."
                        options={costAccounts.map((a) => ({ value: a.code, label: `${a.code} ${a.nameAr ?? ''}` }))}
                      />
                    )}
                    <button
                      type="button"
                      disabled={submittingAction === `classify:${group.category}`}
                      onClick={() => handleClassifyCategory(group)}
                      className="px-3 py-1 rounded-lg bg-[var(--accent)] text-white font-bold disabled:opacity-50 cursor-pointer"
                    >
                      تطبيق
                    </button>
                  </div>
                );
              })}
            </div>
          )}

          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                  إجراءات الفاتورة المتاحة من المحرك (Allowed Actions):
                </span>
                <span className="text-[11px] font-bold px-2 py-0.5 rounded-full bg-[var(--accent)]/15 text-[var(--accent)] font-mono">
                  {currentInvoice.approvalStatus || 'pending'}
                </span>
              </div>
              <p className="text-[11px] text-[var(--muted)] mt-0.5">
                تظهر الأزرار أدناه حصراً بناءً على عقد السيرفر (allowedActions) وقواعد فصل الصلاحيات (BR §26).
              </p>
            </div>

            {/* Allowed action buttons */}
            <div className="flex flex-wrap items-center gap-2">
              {allowedActions.includes('approve') && (
                <button
                  type="button"
                  id="btn-approve-ninja-invoice"
                  disabled={submittingAction === 'approve'}
                  onClick={handleApproveInvoice}
                  className="px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-bold transition flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                >
                  <CheckCircle size={15} />
                  <span>4. اعتماد الفاتورة (مستقل)</span>
                </button>
              )}

              {allowedActions.includes('post') && (
                <button
                  type="button"
                  id="btn-post-ninja-invoice"
                  disabled={submittingAction === 'post'}
                  onClick={handlePostInvoice}
                  className="px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-700 text-white text-xs font-bold transition flex items-center gap-1.5 cursor-pointer disabled:opacity-50"
                >
                  <Send size={15} />
                  <span>5. ترحيل الفاتورة للقيد العام</span>
                </button>
              )}

              {allowedActions.includes('dispute') && (
                <button
                  type="button"
                  onClick={() => {
                    setIsResolvingDispute(false);
                    setDisputeReason('');
                    setDisputeModalOpen(true);
                  }}
                  className="px-3.5 py-2 rounded-xl border border-[var(--warning)]/50 bg-[var(--warning)]/10 text-[var(--warning)] text-xs font-bold hover:bg-[var(--warning)]/20 transition flex items-center gap-1.5 cursor-pointer"
                >
                  <AlertTriangle size={14} />
                  <span>تسجيل اعتراض رسمي</span>
                </button>
              )}

              {allowedActions.includes('resolve') && (
                <button
                  type="button"
                  onClick={() => {
                    setIsResolvingDispute(true);
                    const openDisp = (currentInvoice.disputes || []).find((d) => d.status === 'open');
                    setSelectedDisputeId(openDisp?.id || '');
                    setDisputeReason('');
                    setDisputeModalOpen(true);
                  }}
                  className="px-3.5 py-2 rounded-xl border border-sky-500/50 bg-sky-500/10 text-sky-700 dark:text-sky-300 text-xs font-bold hover:bg-sky-500/20 transition flex items-center gap-1.5 cursor-pointer"
                >
                  <Check size={14} />
                  <span>حسم الاعتراض</span>
                </button>
              )}

              {currentInvoice.journalEntryId && (
                <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-emerald-500/15 text-emerald-700 dark:text-emerald-400 font-mono text-xs font-bold">
                  <CheckCircle2 size={15} />
                  <span>مرحل بقيد: {currentInvoice.journalEntryId}</span>
                </span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Keeta Invoice Totals & Preview Summary */}
      {isKeeta && (currentInvoice || serverPreview) && (() => {
        type KeetaPreviewRow = {
          amountExclVat?: number;
          deductionsTotal?: number;
          vat?: number;
          lines?: Array<{ orders?: number; lineType?: string }>;
        };
        const previewRows = (serverPreview as { rows?: KeetaPreviewRow[] } | null)?.rows;
        const src: KeetaPreviewRow = (currentInvoice as KeetaPreviewRow | null) ?? previewRows?.[0] ?? {};
        const lines = src.lines || [];
        const accountCount = lines.filter((l) => l.lineType !== 'company').length || lines.length;
        const orders = lines.reduce((sum, l) => sum + (l.orders || 0), 0);
        const gross = src.amountExclVat ?? 0;
        const deductionsTotal = src.deductionsTotal ?? 0;
        const vat = src.vat ?? 0;
        const totalPayable = gross - deductionsTotal + vat;
        return (
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
            <StatCard
              label="عدد الحسابات"
              value={accountCount}
              hint={`فترة ${period}`}
              icon={Layers}
            />
            <StatCard
              label="الطلبات المسلمة"
              value={orders}
              hint="إجمالي الطلبات"
              icon={CheckCircle}
            />
            <StatCard
              label="إجمالي المستحق (Gross)"
              value={`${gross.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint="قبل الخصومات"
              icon={DollarSign}
            />
            <StatCard
              label="إجمالي الخصومات"
              value={`${deductionsTotal.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint="تخضع للتصنيف"
              icon={AlertTriangle}
            />
            <StatCard
              label="الضريبة (VAT)"
              value={`${vat.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint="ضريبة القيمة المضافة"
              icon={Percent}
            />
            <StatCard
              label="الإجمالي شامل الضريبة"
              value={`${totalPayable.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint="المعتمد في الصرف"
              icon={FileCheck}
            />
          </div>
        );
      })()}

      {/* Results Section (Reconcile & Captains Grid) */}
      {reconcileResult && overallTotals && (
        <div className="space-y-6">
          {/* Top Summary StatCards */}
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
            <StatCard
              label="إجمالي الكباتن"
              value={overallTotals.totalCaptains}
              hint={`فترة ${reconcileResult.period}`}
              icon={Layers}
              active={filterMode === 'all'}
              onClick={() => setFilterMode('all')}
            />
            <StatCard
              label="مطابق تماماً"
              value={overallTotals.totalMatched}
              hint="تطابق 100% مع المحرك"
              icon={CheckCircle}
              active={filterMode === 'matched'}
              onClick={() => setFilterMode(filterMode === 'matched' ? 'all' : 'matched')}
            />
            <StatCard
              label="كباتن بفروقات"
              value={overallTotals.totalDifferent}
              hint="الفرق للعلم فقط (§31)"
              icon={AlertTriangle}
              active={filterMode === 'different'}
              onClick={() => setFilterMode(filterMode === 'different' ? 'all' : 'different')}
            />
            <StatCard
              label="تسويات الفاتورة"
              value={overallTotals.totalAdjustments}
              hint="بنود بدون عقد كابتن"
              icon={FileCheck}
            />
            <StatCard
              label="إجمالي الفاتورة"
              value={`${overallTotals.invoiceCost.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint="المعتمد في الصرف"
              icon={DollarSign}
            />
            <StatCard
              label="صافي الفرق"
              value={`${Math.abs(overallTotals.diffCost).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
              hint={overallTotals.diffCost >= 0 ? 'الفاتورة أعلى' : 'المحرك أعلى'}
              icon={Percent}
            />
          </div>

          {/* Invoices Tabs (if multiple files uploaded) */}
          {reconcileResult.invoices.length > 1 && (
            <div className="flex items-center gap-2 overflow-x-auto no-scrollbar border-b border-[var(--line)] pb-2">
              {reconcileResult.invoices.map((inv, idx) => (
                <button
                  key={idx}
                  type="button"
                  onClick={() => setActiveInvoiceIdx(idx)}
                  className={`px-3.5 py-2 rounded-xl text-xs font-bold transition flex items-center gap-2 cursor-pointer ${
                    activeInvoiceIdx === idx
                      ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                      : 'bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] border border-[var(--line)]'
                  }`}
                >
                  <FileSpreadsheet size={14} />
                  <span>{inv.fileName}</span>
                  <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-black/10 dark:bg-white/10">
                    {inv.matched} مطابق / {inv.different} فرق
                  </span>
                </button>
              ))}
            </div>
          )}

          {activeInvoice && (
            <div className="space-y-6">
              {/* Totals Comparison Box */}
              <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-[var(--line)] pb-2">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-xs sm:text-sm text-[var(--ink)]">
                      مقارنة الإجماليات · ملف {activeInvoice.fileName}
                    </span>
                    {activeInvoice.supplier && (
                      <span className="text-[11px] font-mono text-[var(--muted)]">
                        (المورد: {activeInvoice.supplier})
                      </span>
                    )}
                  </div>
                  <div className="text-[11px] text-[var(--muted)]">
                    مصدر الشروط: {reconcileResult.termsSource === 'approved' ? 'شروط معتمدة' : 'شروط البداية (Seed)'}
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                  {Object.entries(activeInvoice.totals || {}).map(([key, t]) => {
                    const diff = Math.round((t.invoice - t.engine) * 100) / 100;
                    return (
                      <div
                        key={key}
                        className="p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40 space-y-1"
                      >
                        <span className="text-xs font-bold text-[var(--ink)]">{mapFieldLabel(key)}</span>
                        <div className="flex items-center justify-between text-xs pt-1">
                          <span className="text-[var(--muted)]">الفاتورة:</span>
                          <span className="font-bold num text-[var(--ink)]">
                            {t.invoice.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </span>
                        </div>
                        <div className="flex items-center justify-between text-xs">
                          <span className="text-[var(--muted)]">المحرك:</span>
                          <span className="font-bold num text-[var(--muted)]">
                            {t.engine.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </span>
                        </div>
                        <div className="flex items-center justify-between text-xs pt-1 border-t border-[var(--line)]">
                          <span className="text-[var(--muted)]">الفرق:</span>
                          <span
                            className={`font-bold num ${
                              diff === 0 ? 'text-emerald-600' : 'text-amber-600'
                            }`}
                          >
                            {diff === 0 ? '0.00' : diff > 0 ? `+${diff.toFixed(2)}` : diff.toFixed(2)} ر.س
                          </span>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Adjustments Table (if any) */}
              {activeInvoice.adjustments && activeInvoice.adjustments.length > 0 && (
                <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs space-y-3">
                  <div className="flex items-center justify-between">
                    <div>
                      <h4 className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                        تسويات على مستوى الفاتورة (Invoice-level Adjustments)
                      </h4>
                      <p className="text-[11px] text-[var(--muted)] mt-0.5">
                        بنود بدون كابتن محدد (مثل الخصومات العامة DEDUCTIONS)؛ تم تضمينها كما وردت من نينجا.
                      </p>
                    </div>
                    <span className="text-xs font-mono font-bold text-[var(--muted)]">
                      {activeInvoice.adjustments.length} بند
                    </span>
                  </div>

                  <div className="rounded-xl border border-[var(--line)] overflow-hidden">
                    <table className="w-full text-xs">
                      <thead className="bg-[var(--raised)] text-[var(--muted)] border-b border-[var(--line)]">
                        <tr>
                          <th className="p-2.5 text-start">السطر</th>
                          <th className="p-2.5 text-start">المعرف / البند</th>
                          <th className="p-2.5 text-start">نوع المستوى</th>
                          <th className="p-2.5 text-start">المبلغ النهائي</th>
                          <th className="p-2.5 text-start">التكلفة الإجمالية</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--line)]">
                        {activeInvoice.adjustments.map((adj, i) => (
                          <tr key={i} className="hover:bg-[var(--row-hover)]">
                            <td className="p-2.5 font-mono text-[var(--muted)]">{adj.row}</td>
                            <td className="p-2.5 font-bold text-[var(--ink)]">{adj.captainId}</td>
                            <td className="p-2.5 text-[var(--muted)]">{adj.levelType || '—'}</td>
                            <td className="p-2.5 num font-bold text-[var(--ink)]">
                              {adj.finalAmount?.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                            </td>
                            <td className="p-2.5 num font-bold text-[var(--ink)]">
                              {adj.totalCost?.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* Captains Grid */}
              <div className="space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                  <div className="flex items-center gap-2">
                    <h3 className="text-xs sm:text-sm font-bold text-[var(--ink)]">
                      كشف مقارنة الكباتن ({filteredLines.length} من أصل {(activeInvoice.lines || []).length}):
                    </h3>
                  </div>

                  <div className="flex items-center gap-1 p-1 rounded-xl bg-[var(--raised)] border border-[var(--line)]">
                    <button
                      type="button"
                      onClick={() => setFilterMode('all')}
                      className={`px-2.5 py-1 rounded-lg text-xs font-bold transition cursor-pointer ${
                        filterMode === 'all'
                          ? 'bg-[var(--surface)] text-[var(--ink)] shadow-xs'
                          : 'text-[var(--muted)]'
                      }`}
                    >
                      الكل
                    </button>
                    <button
                      type="button"
                      onClick={() => setFilterMode('different')}
                      className={`px-2.5 py-1 rounded-lg text-xs font-bold transition cursor-pointer ${
                        filterMode === 'different'
                          ? 'bg-[var(--surface)] text-amber-600 shadow-xs'
                          : 'text-[var(--muted)]'
                      }`}
                    >
                      الفروقات فقط ({activeInvoice.different})
                    </button>
                    <button
                      type="button"
                      onClick={() => setFilterMode('matched')}
                      className={`px-2.5 py-1 rounded-lg text-xs font-bold transition cursor-pointer ${
                        filterMode === 'matched'
                          ? 'bg-[var(--surface)] text-emerald-600 shadow-xs'
                          : 'text-[var(--muted)]'
                      }`}
                    >
                      المطابق فقط ({activeInvoice.matched})
                    </button>
                  </div>
                </div>

                <DataTable
                  tableId="ninja-reconcile-captains"
                  columns={captainColumns}
                  data={filteredLines}
                  emptyTitle="لا توجد سجلات كباتن"
                  emptyDescription="لم يتم العثور على أي كباتن يطابقون شروط البحث والتصفية."
                  exportFilename={`ninja_captains_${activeInvoice.fileName || 'reconcile'}`}
                  showTotals={true}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* Dispute Dialog */}
      <Dialog
        open={disputeModalOpen}
        onClose={() => setDisputeModalOpen(false)}
        title={isResolvingDispute ? 'حسم الاعتراض الرسمي على الفاتورة' : 'تسجيل اعتراض رسمي على الفاتورة'}
      >
        <form onSubmit={handleDisputeSubmit} className="space-y-4 pt-2 text-xs">
          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">
              {isResolvingDispute ? 'سبب وطريقة حسم الاعتراض: *' : 'سبب الاعتراض الرسمي (مطلوب): *'}
            </label>
            <textarea
              required
              rows={3}
              value={disputeReason}
              onChange={(e) => setDisputeReason(e.target.value)}
              placeholder="اكتب تفاصيل ومبررات القرار هنا..."
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="pt-2 flex justify-end gap-2 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setDisputeModalOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold"
            >
              إلغاء
            </button>
            <button
              type="submit"
              disabled={submittingAction !== null || !disputeReason.trim()}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold transition disabled:opacity-50"
            >
              {submittingAction ? 'جاري المعالجة...' : isResolvingDispute ? 'تأكيد حسم الاعتراض' : 'تسجيل الاعتراض'}
            </button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
