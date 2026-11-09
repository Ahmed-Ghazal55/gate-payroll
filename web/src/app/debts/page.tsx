'use client';

import React, { useState, useEffect, useMemo, Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import {
  Receipt,
  Coins,
  ShieldCheck,
  FileText,
  Search,
  Filter,
  Download,
  Calendar,
  CheckCircle2,
  AlertCircle,
  Clock,
  User,
  ArrowRightLeft,
  ChevronRight,
  Eye,
  Check,
  X,
  FileCheck,
  AlertTriangle,
  History,
  Building2,
  Send,
  Plus,
  RefreshCw,
  SlidersHorizontal,
  Banknote,
  Wallet,
  CheckCircle,
  XCircle,
  MessageSquare,
  Smartphone,
  CreditCard,
  Wifi,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { RiderAsksCard } from '../../components/debts/RiderAsksCard';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState, ErrorState } from '../../components/ui/EmptyState';
import { DataTable, ColumnDef } from '../../components/ui/DataTable';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import type {
  DebtRecord,
  ApprovalRequest,
  PromissoryNote,
  RiderStatement,
  AdvanceRequest,
  AdvanceRequestSummary,
  OrgSupervision,
} from '../../lib/types';

type TabId = 'debts' | 'advance_requests' | 'statements' | 'approvals' | 'promissory';


const LEGACY_CATEGORY_AR: Record<string, string> = {
  advance: 'سلفة نقدية',
  CashAdvance: 'سلفة نقدية',
  BankAdvance: 'سلفة بنكية',
  traffic_fine: 'مخالفة مرورية',
  equipment: 'عهدة ومعدات',
  hr_cost: 'تكلفة HR',
};
function debtCategoryLabel(d: DebtRecord): string {
  return d.categoryAr || LEGACY_CATEGORY_AR[d.category] || d.category || '—';
}
const DEBT_STATUS_AR: Record<string, string> = {
  active: 'قائم',
  settled: 'مسدد',
  waived: 'معفي',
  disputed: 'متنازع عليه',
  pending_approval: 'بانتظار الاعتماد',
  cancelled: 'ملغي',
};
function debtStatusLabel(status: string): string {
  return DEBT_STATUS_AR[status] || status || '—';
}
const isPendingOpening = (d: DebtRecord) =>
  d.entryMode === 'OpeningBalance' && d.status === 'pending_approval' && !d.approvalRequestId;

function DebtsPageContent() {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();
  const searchParams = useSearchParams();
  const router = useRouter();

  // Tab State & URL Synchronization
  const tabFromUrl = searchParams?.get('tab');
  const resolvedTab: TabId = useMemo(() => {
    if (tabFromUrl === 'advance_requests') return 'advance_requests';
    if (tabFromUrl === 'statement' || tabFromUrl === 'statements') return 'statements';
    if (tabFromUrl === 'approvals') return 'approvals';
    if (tabFromUrl === 'promissory') return 'promissory';
    return 'debts';
  }, [tabFromUrl]);

  const [activeTab, setActiveTab] = useState<TabId>(resolvedTab);

  useEffect(() => {
    if (resolvedTab && resolvedTab !== activeTab) {
      setActiveTab(resolvedTab);
    }
  }, [resolvedTab]);

  const handleTabChange = (newTab: TabId) => {
    setActiveTab(newTab);
    const urlParam = newTab === 'statements' ? 'statement' : newTab;
    router.replace(`/debts/?tab=${urlParam}`, { scroll: false });
  };

  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [debts, setDebts] = useState<DebtRecord[]>([]);
  const [approvals, setApprovals] = useState<ApprovalRequest[]>([]);
  const [promissoryNotes, setPromissoryNotes] = useState<PromissoryNote[]>([]);
  const [promissoryFeatureEnabled, setPromissoryFeatureEnabled] = useState<boolean>(false);

  // Statement search states
  const [statementIqama, setStatementIqama] = useState<string>('2000000789');
  const [statementFrom, setStatementFrom] = useState<string>('2026-01-01');
  const [statementTo, setStatementTo] = useState<string>('2026-10-31');
  const [statementMode, setStatementMode] = useState<'detailed' | 'simple'>('detailed');
  const [statementData, setStatementData] = useState<RiderStatement | null>(null);
  const [loadingStatement, setLoadingStatement] = useState<boolean>(false);
  const [statementError, setStatementError] = useState<string | null>(null);

  // Approval decision dialog
  const [selectedApproval, setSelectedApproval] = useState<ApprovalRequest | null>(null);
  const [approvalDecision, setApprovalDecision] = useState<'approved' | 'rejected' | 'suspended'>('approved');
  const [approvalNote, setApprovalNote] = useState<string>('');
  const [submittingDecision, setSubmittingDecision] = useState<boolean>(false);

  // Debt details drawer
  const [selectedDebt, setSelectedDebt] = useState<DebtRecord | null>(null);
  const [debtDrawerOpen, setDebtDrawerOpen] = useState<boolean>(false);

  // ========================================================
  // R5 ADVANCE REQUESTS STATES (BR §25.3)
  // ========================================================
  const [advanceRequests, setAdvanceRequests] = useState<AdvanceRequest[]>([]);
  const [loadingAdvances, setLoadingAdvances] = useState<boolean>(false);
  const [advancesError, setAdvancesError] = useState<string | null>(null);
  const [supervisedRiders, setSupervisedRiders] = useState<OrgSupervision[]>([]);
  const [advancesRoleView, setAdvancesRoleView] = useState<'review' | 'mine'>('review');

  // New Request Form Dialog
  const [newRequestOpen, setNewRequestOpen] = useState<boolean>(false);
  const [newReqIqama, setNewReqIqama] = useState<string>('');
  const [newReqType, setNewReqType] = useState<'cash' | 'bank' | 'internet'>('cash');
  const [newReqFulfilment, setNewReqFulfilment] = useState<'cash' | 'bank' | 'recharge_card'>('recharge_card');
  const [newReqNetwork, setNewReqNetwork] = useState<string>('');
  const [newReqPackage, setNewReqPackage] = useState<string>('');
  const [rechargeNetworks, setRechargeNetworks] = useState<any[]>([]);
  const [rechargePackages, setRechargePackages] = useState<any[]>([]);
  const [newReqAmount, setNewReqAmount] = useState<string>('');
  const [newReqReason, setNewReqReason] = useState<string>('');
  const [submittingNewReq, setSubmittingNewReq] = useState<boolean>(false);

  // Proposal Response Dialog (Supervisor)
  const [proposalReq, setProposalReq] = useState<AdvanceRequest | null>(null);
  const [proposalAction, setProposalAction] = useState<'accept' | 'reject'>('accept');
  const [proposalNote, setProposalNote] = useState<string>('');
  const [submittingProposal, setSubmittingProposal] = useState<boolean>(false);

  // Review Drawer (Accountant)
  const [selectedAdvance, setSelectedAdvance] = useState<AdvanceRequest | null>(null);
  const [advanceSummary, setAdvanceSummary] = useState<AdvanceRequestSummary | null>(null);
  const [loadingAdvanceSummary, setLoadingAdvanceSummary] = useState<boolean>(false);
  const [reviewDrawerOpen, setReviewDrawerOpen] = useState<boolean>(false);
  const [reviewMode, setReviewMode] = useState<'approve' | 'propose' | 'reject'>('approve');
  const [reviewInstallment, setReviewInstallment] = useState<string>('');
  const [reviewCoverageReason, setReviewCoverageReason] = useState<string>('');
  const [reviewNote, setReviewNote] = useState<string>('');
  const [proposeType, setProposeType] = useState<'cash' | 'bank' | 'internet'>('bank');
  const [proposeAmount, setProposeAmount] = useState<string>('');
  const [submittingReview, setSubmittingReview] = useState<boolean>(false);

  const canRequestAdvances = can('advances.request') || isAdmin;
  const canReviewAdvances = can('advances.review') || isAdmin;

  // Set default view based on permissions
  useEffect(() => {
    if (canReviewAdvances) {
      setAdvancesRoleView('review');
    } else if (canRequestAdvances) {
      setAdvancesRoleView('mine');
    }
  }, [user]);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [debtsRes, approvalsRes, pNotesRes] = await Promise.all([
        api.getDebts(),
        api.getApprovals(),
        api.getPromissoryNotes(),
      ]);
      setDebts(debtsRes || []);
      setApprovals(approvalsRes || []);
      setPromissoryNotes(pNotesRes.items || []);
      setPromissoryFeatureEnabled(pNotesRes.featureEnabled || false);
    } catch (err: any) {
      const msg = err.message || 'تعذر تحميل بيانات المديونيات';
      setError(msg);
      toast(msg, 'danger');
    } finally {
      setLoading(false);
    }
  };

  const loadAdvanceRequests = async () => {
    setLoadingAdvances(true);
    setAdvancesError(null);
    try {
      const role = advancesRoleView;
      const list = await api.getAdvanceRequests({ role });
      setAdvanceRequests(Array.isArray(list) ? list : []);
    } catch (err: any) {
      setAdvancesError(err.message || 'تعذر تحميل طلبات السلف');
    } finally {
      setLoadingAdvances(false);
    }
  };

  const loadSupervisedRiders = async () => {
    try {
      const list = await api.getOrgSupervision({ mine: 1 });
      setSupervisedRiders(Array.isArray(list) ? list : []);
    } catch (err: any) {
      setSupervisedRiders([]);
      toast(err.message || 'تعذر تحميل المناديب تحت إشرافك', 'danger');
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  useEffect(() => {
    const action = searchParams?.get('action');
    if (action === 'new') {
      setActiveTab('advance_requests');
      setNewReqType('cash');
      setNewRequestOpen(true);
    } else if (action === 'new_internet') {
      setActiveTab('advance_requests');
      setNewReqType('internet');
      setNewReqFulfilment('recharge_card');
      setNewRequestOpen(true);
    }
  }, [searchParams]);

  useEffect(() => {
    if (newRequestOpen && newReqType === 'internet') {
      api.getConfigListGroup('recharge_networks').then((res) => setRechargeNetworks(res?.items || [])).catch(() => {});
      api.getConfigListGroup('recharge_packages').then((res) => setRechargePackages(res?.items || [])).catch(() => {});
    }
  }, [newRequestOpen, newReqType]);

  useEffect(() => {
    if (!canReviewAdvances && canRequestAdvances) {
      setAdvancesRoleView('mine');
    }
  }, [canReviewAdvances, canRequestAdvances]);

  useEffect(() => {
    if (activeTab === 'advance_requests') {
      loadAdvanceRequests();
      if (canRequestAdvances) {
        loadSupervisedRiders();
      }
    }
  }, [activeTab, advancesRoleView]);

  const handleOpenReviewDrawer = async (req: AdvanceRequest) => {
    setSelectedAdvance(req);
    setReviewDrawerOpen(true);
    setReviewMode('approve');
    setReviewInstallment(String(req.monthlyInstallment || req.amount));
    setReviewCoverageReason(req.coverageReason || '');
    setReviewNote('');
    setProposeType(req.type === 'cash' ? 'bank' : 'cash');
    setProposeAmount(String(req.amount));

    setLoadingAdvanceSummary(true);
    try {
      const [freshReq, summary] = await Promise.all([
        api.getAdvanceRequest(req.id),
        api.getAdvanceRequestSummary(req.id),
      ]);
      setSelectedAdvance(freshReq);
      setAdvanceSummary(summary);
    } catch (err: any) {
      // BR §25: no approval without the summary; the reviewer sees the server message.
      setAdvanceSummary(null);
      toast(err.message || 'تعذر استرجاع ملخص المندوب المالي', 'danger');
    } finally {
      setLoadingAdvanceSummary(false);
    }
  };

  const handleCreateAdvanceRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newReqIqama || !newReqAmount || !newReqReason.trim()) {
      toast('يرجى تعبئة جميع الحقول المطلوبة', 'warning');
      return;
    }
    if (newReqType === 'internet' && newReqFulfilment === 'recharge_card') {
      if (!newReqNetwork || !newReqPackage) {
        toast('يرجى اختيار شبكة الشحن والباقة', 'warning');
        return;
      }
    }
    const amt = parseFloat(newReqAmount);
    if (isNaN(amt) || amt <= 0 || amt > 100000) {
      toast('مبلغ السلفة غير صالح (1 إلى 100,000 ر.س)', 'warning');
      return;
    }
    setSubmittingNewReq(true);
    try {
      await api.createAdvanceRequest({
        riderIqama: newReqIqama.trim(),
        type: newReqType,
        amount: amt,
        reason: newReqReason.trim(),
        fulfilment: newReqType === 'internet' ? newReqFulfilment : undefined,
        networkCode: newReqType === 'internet' && newReqFulfilment === 'recharge_card' ? newReqNetwork : undefined,
        packageCode: newReqType === 'internet' && newReqFulfilment === 'recharge_card' ? newReqPackage : undefined,
      });
      toast('تم تقديم طلب السلفة بنجاح وإشعار المحاسب المسؤول', 'success');
      setNewRequestOpen(false);
      setNewReqIqama('');
      setNewReqAmount('');
      setNewReqReason('');
      setNewReqNetwork('');
      setNewReqPackage('');
      loadAdvanceRequests();
    } catch (err: any) {
      toast(err.message || 'فشل تقديم طلب السلفة', 'danger');
    } finally {
      setSubmittingNewReq(false);
    }
  };

  const handleReviewAdvance = async () => {
    if (!selectedAdvance) return;

    if (reviewMode === 'approve') {
      if (!advanceSummary) {
        toast('لا يمكن الاعتماد قبل تحميل ملخص المندوب المالي', 'danger');
        return;
      }
      const inst = parseFloat(reviewInstallment);
      if (isNaN(inst) || inst <= 0 || inst > selectedAdvance.amount) {
        toast('القسط الشهري غير صالح؛ يجب أن يكون بين صفر ومبلغ السلفة', 'warning');
        return;
      }
      if (advanceSummary && !advanceSummary.covered && (!reviewCoverageReason || reviewCoverageReason.trim().length < 5)) {
        toast('الصافي المتوقع لا يغطي السلفة أو غير معروف؛ اكتب سبب الاعتماد (5 أحرف على الأقل)', 'warning');
        return;
      }
    } else if (reviewMode === 'reject') {
      if (!reviewNote || reviewNote.trim().length < 3) {
        toast('يرجى كتابة سبب الرفض (3 أحرف على الأقل)', 'warning');
        return;
      }
    } else if (reviewMode === 'propose') {
      if (!reviewNote || reviewNote.trim().length < 3) {
        toast('يرجى كتابة سبب اقتراح التعديل (3 أحرف على الأقل)', 'warning');
        return;
      }
      const propAmt = parseFloat(proposeAmount);
      if (isNaN(propAmt) || propAmt <= 0) {
        toast('مبلغ الاقتراح غير صالح', 'warning');
        return;
      }
      if (proposeType === selectedAdvance.type && propAmt === selectedAdvance.amount) {
        toast('الاقتراح لا يغير شيئاً في النوع أو المبلغ', 'warning');
        return;
      }
    }

    setSubmittingReview(true);
    try {
      const body: any = {
        action: reviewMode,
        note: reviewNote.trim(),
        revision: selectedAdvance.revision,
      };
      if (reviewMode === 'approve') {
        body.monthlyInstallment = parseFloat(reviewInstallment);
        body.coverageReason = reviewCoverageReason.trim();
      } else if (reviewMode === 'propose') {
        body.proposed = {
          type: proposeType,
          amount: parseFloat(proposeAmount),
        };
      }

      await api.reviewAdvanceRequest(selectedAdvance.id, body);
      toast(
        reviewMode === 'approve'
          ? 'تم اعتماد السلفة بنجاح وإحالتها لأمين الخزينة للصرف'
          : reviewMode === 'reject'
          ? 'تم تسجيل رفض طلب السلفة'
          : 'تم إرسال اقتراح التعديل إلى المشرف بانتظار رده',
        'success'
      );
      setReviewDrawerOpen(false);
      setSelectedAdvance(null);
      setAdvanceSummary(null);
      loadAdvanceRequests();
    } catch (err: any) {
      toast(err.message || 'فشلت معالجة الطلب', 'danger');
    } finally {
      setSubmittingReview(false);
    }
  };

  const handleRespondToProposal = async () => {
    if (!proposalReq) return;
    setSubmittingProposal(true);
    try {
      await api.respondToAdvanceProposal(proposalReq.id, {
        action: proposalAction,
        note: proposalNote.trim() || undefined,
      });
      toast(
        proposalAction === 'accept'
          ? 'تم قبول التعديل وإعادة الطلب لمراجعة المحاسب'
          : 'تم رفض الاقتراح وإلغاء الطلب نهائياً',
        'success'
      );
      setProposalReq(null);
      setProposalNote('');
      loadAdvanceRequests();
    } catch (err: any) {
      toast(err.message || 'فشل إرسال الرد على الاقتراح', 'danger');
    } finally {
      setSubmittingProposal(false);
    }
  };

  const handleFetchStatement = async () => {
    if (!statementIqama.trim()) {
      toast('يرجى إدخال رقم إقامة المندوب', 'warning');
      return;
    }
    setLoadingStatement(true);
    setStatementError(null);
    try {
      if (statementMode === 'simple') {
        const simple = await api.getRiderStatementSimple(statementIqama.trim(), statementFrom, statementTo);
        setStatementData({
          iqama: statementIqama.trim(),
          riderName: simple.riderName || 'مندوب تجريبي',
          period: { from: statementFrom, to: statementTo },
          openingBalance: simple.openingBalance || 0,
          closingBalance: simple.closingBalance || 0,
          entries: simple.entries || [],
        });
      } else {
        const full = await api.getRiderStatement(statementIqama.trim(), statementFrom, statementTo);
        setStatementData(full);
      }
      toast('تم استرجاع كشف الحساب بنجاح', 'success');
    } catch (err: any) {
      const msg = err.message || 'تعذر استرجاع كشف حساب المندوب';
      setStatementError(msg);
      toast(msg, 'danger');
      setStatementData(null);
    } finally {
      setLoadingStatement(false);
    }
  };

  const handleDecideApproval = async () => {
    if (!selectedApproval) return;
    setSubmittingDecision(true);
    try {
      await api.decideApproval(selectedApproval.id, {
        decision: approvalDecision,
        note: approvalNote.trim() || undefined,
      });
      toast(`تم تسجيل قرار ${approvalDecision === 'approved' ? 'الاعتماد' : approvalDecision === 'rejected' ? 'الرفض' : 'التعليق'} بنجاح`, 'success');
      setSelectedApproval(null);
      setApprovalNote('');
      loadData();
    } catch (err: any) {
      toast(err.message || 'تعذر البت في طلب الاعتماد', 'danger');
    } finally {
      setSubmittingDecision(false);
    }
  };

  // Filtered debts
  const filteredDebts = useMemo(() => {
    return debts.filter((d) => {
      if (selectedCategory !== 'all' && d.category !== selectedCategory) return false;
      if (selectedStatus !== 'all' && d.status !== selectedStatus) return false;
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const matchIqama = d.iqama?.toLowerCase().includes(q);
        const matchName = d.riderName?.toLowerCase().includes(q);
        if (!matchIqama && !matchName) return false;
      }
      return true;
    });
  }, [debts, selectedCategory, selectedStatus, searchQuery]);

  const categoryOptions = useMemo(() => {
    const m = new Map<string, string>();
    debts.forEach((d) => m.set(d.category, debtCategoryLabel(d)));
    return [...m.entries()].sort((a, b) => a[1].localeCompare(b[1], 'ar'));
  }, [debts]);

  // Opening balances from the debt sheet wait for an admin decision (no approval_request exists).
  const pendingOpening = useMemo(() => debts.filter(isPendingOpening), [debts]);
  const pendingOpeningTotal = useMemo(() => pendingOpening.reduce((s, d) => s + (d.remainingAmount || 0), 0), [pendingOpening]);
  const [openingDialog, setOpeningDialog] = useState<null | 'approved' | 'rejected'>(null);
  const [openingNote, setOpeningNote] = useState('');
  const [openingIds, setOpeningIds] = useState<string[]>([]);
  const [openingProgress, setOpeningProgress] = useState<string>('');
  const decideOpening = async (decision: 'approved' | 'rejected', ids: string[]) => {
    if (decision === 'rejected' && openingNote.trim().length < 3) {
      toast('اكتب سبب الرفض (3 أحرف على الأقل)', 'warning');
      return;
    }
    let processed = 0;
    try {
      for (let i = 0; i < ids.length; i += 100) {
        setOpeningProgress(`${Math.min(i + 100, ids.length)} / ${ids.length}`);
        const r = await api.decideOpeningDebts({ ids: ids.slice(i, i + 100), decision, note: openingNote.trim() });
        processed += r.processed;
      }
      toast(`${decision === 'approved' ? 'تم اعتماد' : 'تم رفض'} ${processed} رصيد افتتاحي`, 'success');
      setOpeningDialog(null);
      setOpeningNote('');
      setDebtDrawerOpen(false);
    } catch (err: any) {
      toast(`${err.message || 'تعذر تنفيذ القرار'} — تمت معالجة ${processed} قبل التوقف`, 'danger');
    } finally {
      setOpeningProgress('');
      loadData();
    }
  };

  // Derived statistics
  const totalRemainingDebts = useMemo(() => debts.filter((d) => d.status === 'active').reduce((s, d) => s + (d.remainingAmount || 0), 0), [debts]);
  const totalAdvances = useMemo(
    () => debts.filter((d) => d.category === 'advance' || d.category === 'CashAdvance' || d.category === 'BankAdvance').reduce((s, d) => s + (d.remainingAmount || 0), 0),
    [debts]
  );
  const pendingApprovalsCount = useMemo(() => approvals.filter((a) => a.status === 'pending').length, [approvals]);
  const pendingAdvancesCount = useMemo(() => advanceRequests.filter((r) => r.status === 'under_review').length, [advanceRequests]);

  const renderAdvanceStatusBadge = (status: AdvanceRequest['status']) => {
    switch (status) {
      case 'under_review':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--accent)]/10 text-[var(--accent)]">
            <Clock size={12} />
            <span>بانتظار المراجعة</span>
          </span>
        );
      case 'change_proposed':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--warning)]/15 text-[var(--warning)]">
            <MessageSquare size={12} />
            <span>اقتراح تعديل</span>
          </span>
        );
      case 'approved':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--money-pos)]/15 text-[var(--money-pos)]">
            <CheckCircle2 size={12} />
            <span>معتمدة (بانتظار الصرف)</span>
          </span>
        );
      case 'paid':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--success)] text-[var(--success-ink)]">
            <Check size={12} />
            <span>تم الصرف</span>
          </span>
        );
      case 'rejected':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--danger)]/15 text-[var(--danger)]">
            <XCircle size={12} />
            <span>مرفوضة</span>
          </span>
        );
      case 'cancelled':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-bold bg-[var(--muted)]/20 text-[var(--muted)]">
            <X size={12} />
            <span>ملغاة</span>
          </span>
        );
      default:
        return <span className="px-2 py-0.5 text-xs text-[var(--muted)]">{status}</span>;
    }
  };

  const debtsColumns: ColumnDef<DebtRecord>[] = useMemo(
    () => [
      {
        key: 'iqama',
        header: 'رقم الإقامة',
        filterable: true,
        render: (d) => <span className="font-mono font-bold text-[var(--accent)]">{d.iqama}</span>,
      },
      {
        key: 'riderName',
        header: 'اسم المندوب',
        filterable: true,
        render: (d) => <span className="font-semibold">{d.riderName || 'مندوب مسجل'}</span>,
      },
      {
        key: 'category',
        header: 'فئة البند',
        getValue: (d) => debtCategoryLabel(d),
        render: (d) => (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--ink)]">
            {debtCategoryLabel(d)}
          </span>
        ),
      },
      {
        key: 'originalAmount',
        header: 'المبلغ الأصلي',
        numeric: true,
        getValue: (d) => d.originalAmount ?? 0,
        render: (d) => (
          <span className="num text-[var(--muted)]">
            {(d.originalAmount ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'remainingAmount',
        header: 'المبلغ المتبقي',
        numeric: true,
        getValue: (d) => d.remainingAmount ?? 0,
        render: (d) => (
          <span className="num font-bold text-sm text-[var(--danger)]">
            {(d.remainingAmount ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'monthlyInstallment',
        header: 'القسط الشهري',
        numeric: true,
        getValue: (d) => d.monthlyInstallment || d.monthlyDeduction || 0,
        render: (d) => {
          const val = d.monthlyInstallment || d.monthlyDeduction || 0;
          return (
            <span className="num text-[var(--muted)]">
              {val > 0 ? `${val.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '—'}
            </span>
          );
        },
      },
      {
        key: 'status',
        header: 'الحالة',
        getValue: (d) => debtStatusLabel(d.status),
        render: (d) => (
          <span
            className={`px-2 py-0.5 rounded-full text-xs font-semibold ${
              d.status === 'active'
                ? 'bg-[var(--danger)]/10 text-[var(--danger)]'
                : d.status === 'settled'
                ? 'bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                : 'bg-[var(--warning)]/10 text-[var(--warning)]'
            }`}
          >
            {debtStatusLabel(d.status)}
          </span>
        ),
      },
      {
        key: 'actions',
        header: 'الإجراءات',
        render: (d) => (
          <button
            type="button"
            onClick={() => {
              setSelectedDebt(d);
              setDebtDrawerOpen(true);
            }}
            className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--raised)] hover:bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] transition flex items-center gap-1 cursor-pointer"
          >
            <Eye size={13} />
            <span>عرض السجل</span>
          </button>
        ),
      },
    ],
    []
  );

  const advanceColumns: ColumnDef<AdvanceRequest>[] = useMemo(
    () => [
      {
        key: 'id',
        header: 'معرف الطلب',
        width: '90px',
        render: (req) => <span className="font-mono text-[11px] text-[var(--muted)]">{req.id}</span>,
      },
      {
        key: 'rider',
        header: 'المندوب والإقامة',
        getValue: (req) => `${req.riderName || ''} ${req.riderIqama || ''}`,
        render: (req) => (
          <div>
            <div className="font-bold text-[var(--ink)]">{req.riderName}</div>
            <div className="font-mono text-[11px] text-[var(--accent)]">{req.riderIqama}</div>
          </div>
        ),
      },
      {
        key: 'type',
        header: 'النوع والخزينة',
        render: (req) => (
          <div>
            <div className="inline-flex items-center gap-1 font-semibold">
              {req.type === 'internet' ? (
                <Wifi size={13} className="text-sky-600" />
              ) : req.type === 'cash' ? (
                <Wallet size={13} />
              ) : (
                <Banknote size={13} />
              )}
              <span>
                {req.type === 'internet'
                  ? req.fulfilment === 'recharge_card'
                    ? `سلفة نت (كارت ${req.networkCode || ''} ${req.packageCode || ''})`
                    : req.fulfilment === 'bank'
                    ? 'سلفة نت (بنكي)'
                    : 'سلفة نت (نقدي)'
                  : req.type === 'cash'
                  ? 'نقدية (خزينة)'
                  : 'بنكية (تحويل)'}
              </span>
            </div>
            <div className="font-mono text-[10px] text-[var(--muted)]">{req.vaultCode}</div>
          </div>
        ),
      },
      {
        key: 'amount',
        header: 'المبلغ المطلوب',
        numeric: true,
        getValue: (req) => req.amount,
        render: (req) => (
          <span className="font-bold text-[var(--ink)]">
            {req.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
          </span>
        ),
      },
      {
        key: 'requestedBy',
        header: 'مقدم الطلب',
        render: (req) => <span className="font-mono text-xs text-[var(--muted)]">{req.requestedBy}</span>,
      },
      {
        key: 'createdAt',
        header: 'تاريخ التقديم',
        render: (req) => <span className="num text-xs text-[var(--muted)]">{req.createdAt?.split('T')[0]}</span>,
      },
      {
        key: 'status',
        header: 'الحالة',
        render: (req) => renderAdvanceStatusBadge(req.status),
      },
      {
        key: 'actions',
        header: 'الإجراءات',
        render: (req) => (
          <div className="flex items-center gap-1.5">
            {req.status === 'change_proposed' && req.requestedBy === user?.username && (
              <button
                type="button"
                id={`btn-respond-proposal-${req.id}`}
                onClick={() => {
                  setProposalReq(req);
                  setProposalAction('accept');
                  setProposalNote('');
                }}
                className="px-2.5 py-1 rounded-lg bg-[var(--warning)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer"
              >
                الرد على الاقتراح
              </button>
            )}
            <button
              type="button"
              id={`btn-review-advance-${req.id}`}
              onClick={() => handleOpenReviewDrawer(req)}
              className="inline-flex items-center gap-1 px-3 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-bold transition cursor-pointer"
            >
              <Eye size={13} />
              <span>{req.status === 'under_review' && canReviewAdvances ? 'مراجعة وبَت' : 'عرض التفاصيل'}</span>
            </button>
          </div>
        ),
      },
    ],
    [canReviewAdvances, user?.username]
  );

  const approvalColumns: ColumnDef<ApprovalRequest>[] = useMemo(
    () => [
      {
        key: 'id',
        header: 'معرف الطلب',
        width: '90px',
        render: (app) => <span className="font-mono text-xs">{app.id}</span>,
      },
      {
        key: 'kind',
        header: 'النوع',
        render: (app) => <span className="font-semibold">{app.kind || app.requestTypeAr || app.requestType}</span>,
      },
      {
        key: 'iqama',
        header: 'المندوب',
        render: (app) => <span className="font-mono font-bold text-[var(--accent)]">{app.iqama}</span>,
      },
      {
        key: 'amount',
        header: 'المبلغ',
        numeric: true,
        getValue: (app) => app.amount,
        render: (app) => <span className="num font-bold text-[var(--accent)]">{app.amount} ر.س</span>,
      },
      {
        key: 'status',
        header: 'الحالة',
        render: (app) => (
          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--warning)]/10 text-[var(--warning)]">
            {app.status}
          </span>
        ),
      },
      {
        key: 'actions',
        header: 'الإجراءات',
        render: (app) => (
          <button
            type="button"
            onClick={() => {
              setSelectedApproval(app);
              setApprovalDecision('approved');
              setApprovalNote('');
            }}
            className="px-3 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 cursor-pointer"
          >
            بت في الطلب
          </button>
        ),
      },
    ],
    []
  );

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="المديونيات والسلف والتحصيل"
        description="إدارة مديونيات وسلف وتكاليف المناديب، منظومة طلبات ومراجعة وصرف السلف، كشوف الحساب المطابقة 360 والمبسطة، وسندات الأمر التنفيذية."
        eyebrow="الذمم والعهد المالية"
        primaryAction={{
          label: 'تحديث البيانات',
          icon: <RefreshCw size={15} />,
          onClick: activeTab === 'advance_requests' ? loadAdvanceRequests : loadData,
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي المديونيات القائمة"
          value={`${totalRemainingDebts.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint="الأرصدة المتبقية على ذمة المناديب"
          icon={Receipt}
          active={activeTab === 'debts' && selectedCategory === 'all' && selectedStatus === 'all'}
          onClick={() => {
            handleTabChange('debts');
            setSelectedCategory('all');
            setSelectedStatus('all');
          }}
          loading={loading}
        />
        <StatCard
          label="أرصدة السلف المتبقية"
          value={`${totalAdvances.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint="السلف النقدية والبنكية الجاري استقطاعها"
          icon={Coins}
          active={activeTab === 'debts' && selectedCategory === 'advance'}
          onClick={() => {
            handleTabChange('debts');
            setSelectedCategory(selectedCategory === 'advance' ? 'all' : 'advance');
          }}
          loading={loading}
        />
        <StatCard
          label="طلبات السلف بانتظار المراجعة"
          value={String(pendingAdvancesCount)}
          hint="تحتاج دراسة الملخص المالي والاعتماد"
          icon={Clock}
          active={activeTab === 'advance_requests'}
          onClick={() => handleTabChange(activeTab === 'advance_requests' ? 'debts' : 'advance_requests')}
          loading={loadingAdvances}
        />
        <StatCard
          label="طلبات الاعتمادات المتبادلة"
          value={String(pendingApprovalsCount)}
          hint="طلبات بانتظار قرار الإدارة أو العمليات"
          icon={ShieldCheck}
          active={activeTab === 'approvals'}
          onClick={() => handleTabChange(activeTab === 'approvals' ? 'debts' : 'approvals')}
          loading={loading}
        />
      </div>

      {/* Sub Tabs */}
      <div className="border-b border-[var(--line)]">
        <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
          {[
            { id: 'debts' as TabId, label: 'المديونيات والتكاليف', count: filteredDebts.length },
            {
              id: 'advance_requests' as TabId,
              label: 'طلبات السلف (نظام الرتب)',
              count: advanceRequests.length,
              alert: pendingAdvancesCount > 0,
            },
            { id: 'statements' as TabId, label: 'كشوف حساب المناديب' },
            { id: 'approvals' as TabId, label: 'منظومة الاعتمادات', count: pendingApprovalsCount, alert: pendingApprovalsCount > 0 },
            { id: 'promissory' as TabId, label: 'أرشيف سندات الأمر', count: promissoryNotes.length },
          ].map((tab) => (
            <button
              key={tab.id}
              id={`tab-${tab.id}`}
              type="button"
              onClick={() => handleTabChange(tab.id)}
              className={`inline-flex items-center gap-2 px-4 py-3 font-bold text-xs sm:text-sm border-b-2 transition whitespace-nowrap cursor-pointer ${
                activeTab === tab.id
                  ? 'border-[var(--accent)] text-[var(--accent)]'
                  : 'border-transparent text-[var(--muted)] hover:text-[var(--ink)]'
              }`}
            >
              <span>{tab.label}</span>
              {tab.count !== undefined && tab.count > 0 && (
                <span
                  className={`num px-1.5 py-0.2 rounded-full text-[10px] font-bold ${
                    tab.alert ? 'bg-[var(--danger)] text-white' : 'bg-[var(--raised)] text-[var(--ink)]'
                  }`}
                >
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>
      </div>

      {/* ======================================================== */}
      {/* Sub-tab 1: المديونيات والتكاليف                            */}
      {/* ======================================================== */}
      {activeTab === 'debts' && (
        <div className="space-y-4">
          {/* Toolbar */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2 flex-1 max-w-sm">
              <Search size={16} className="text-[var(--muted)] shrink-0" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="بحث برقم الإقامة أو اسم المندوب..."
                className="w-full bg-transparent text-xs sm:text-sm text-[var(--ink)] placeholder:text-[var(--muted)] focus:outline-hidden"
              />
            </div>

            <div className="flex items-center gap-2 flex-wrap">
              <SearchableSelect
                id="select-debt-category"
                value={selectedCategory}
                onChange={setSelectedCategory}
                ariaLabel="تصفية حسب الفئة"
                placeholder="كافة الفئات"
                className="w-44"
                options={[
                  { value: 'all', label: 'كافة الفئات' },
                  ...categoryOptions.map(([code, label]) => ({ value: code, label })),
                ]}
              />

              <select
                value={selectedStatus}
                onChange={(e) => setSelectedStatus(e.target.value)}
                className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--ink)] focus:outline-hidden"
              >
                <option value="all">كافة الحالات</option>
                <option value="active">نشط وقائم</option>
                <option value="pending_approval">بانتظار الاعتماد</option>
                <option value="settled">مسدد بالكامل</option>
                <option value="waived">معفي</option>
                <option value="cancelled">ملغي / مرفوض</option>
              </select>
            </div>
          </div>

          {isAdmin && pendingOpening.length > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-3 p-3.5 rounded-2xl border border-[var(--warning)]/40 bg-[var(--warning)]/10">
              <div className="text-xs sm:text-sm text-[var(--ink)]">
                <div className="font-bold">أرصدة افتتاحية بانتظار الاعتماد</div>
                <div className="text-[var(--muted)] mt-0.5">
                  <span className="num font-bold">{pendingOpening.length}</span> مديونية بإجمالي{' '}
                  <span className="num font-bold">{pendingOpeningTotal.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span> ر.س — من شيت المديونيات بتاريخ 31-10-2026. الاعتماد يرحّل قيد الافتتاح في شهر الرصيد.
                </div>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setSelectedStatus('pending_approval')}
                  className="px-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] cursor-pointer"
                >
                  عرضها للمراجعة
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setOpeningIds(pendingOpening.map((d) => d.id));
                    setOpeningDialog('approved');
                  }}
                  className="px-3 py-1.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 cursor-pointer"
                >
                  اعتماد الكل
                </button>
              </div>
            </div>
          )}

          {error ? (
            <ErrorState title="تعذر تحميل بيانات المديونيات" message={error} onRetry={loadData} />
          ) : (
            <DataTable
              tableId="debts-list"
              columns={debtsColumns}
              data={filteredDebts}
              loading={loading}
              emptyTitle="لا توجد مديونيات مسجلة"
              emptyDescription="لم يتم العثور على أي مديونيات أو سلف تطابق شروط الفلترة الحالية."
              exportFilename="debts_list"
              showTotals={true}
            />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 2: طلبات السلف (نظام الرتب والتوجيه BR §25)        */}
      {/* ======================================================== */}
      {activeTab === 'advance_requests' && (
        <div className="space-y-4">
          {/* Toolbar and View Switcher */}
          <div className="flex flex-wrap items-center justify-between gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <div className="flex items-center gap-2">
              <span className="text-xs font-bold text-[var(--muted)]">عرض الطلبات:</span>
              <div className="flex items-center gap-1 p-1 rounded-xl bg-[var(--raised)]">
                {canReviewAdvances && (
                  <button
                    type="button"
                    onClick={() => setAdvancesRoleView('review')}
                    className={`px-3 py-1 rounded-lg text-xs font-bold transition cursor-pointer ${
                      advancesRoleView === 'review'
                        ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs'
                        : 'text-[var(--muted)] hover:text-[var(--ink)]'
                    }`}
                  >
                    طابور المراجعة (المحاسب المسؤول)
                  </button>
                )}
                {canRequestAdvances && (
                  <button
                    type="button"
                    onClick={() => setAdvancesRoleView('mine')}
                    className={`px-3 py-1 rounded-lg text-xs font-bold transition cursor-pointer ${
                      advancesRoleView === 'mine'
                        ? 'bg-[var(--surface)] text-[var(--accent)] shadow-xs'
                        : 'text-[var(--muted)] hover:text-[var(--ink)]'
                    }`}
                  >
                    طلباتي المرفوعة (المشرف الميداني)
                  </button>
                )}
              </div>
            </div>

            <div className="flex items-center gap-2">
              {canRequestAdvances && (
                <button
                  type="button"
                  id="btn-new-advance-request"
                  onClick={() => setNewRequestOpen(true)}
                  className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs inline-flex items-center gap-1.5 hover:opacity-90 transition cursor-pointer"
                >
                  <Plus size={15} />
                  <span>طلب سلفة جديد</span>
                </button>
              )}
            </div>
          </div>

          {canRequestAdvances && <RiderAsksCard onConverted={loadAdvanceRequests} />}

          {advancesError ? (
            <ErrorState title="تعذر تحميل طلبات السلف" message={advancesError} onRetry={loadAdvanceRequests} />
          ) : loadingAdvances ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : advanceRequests.length === 0 ? (
            <EmptyState
              title={
                advancesRoleView === 'review'
                  ? 'لا توجد طلبات سلف بانتظار المراجعة'
                  : 'لم تقم برفع أي طلبات سلف بعد'
              }
              description={
                advancesRoleView === 'review'
                  ? 'سيظهر هنا أي طلب سلفة موجه لقسم المحاسبة حسب نظام الرتب والتوجيه المسؤول.'
                  : 'بصفتك مشرفاً ميدانياً، يمكنك تقديم طلبات سلف نقدية أو بنكية للمناديب التابعين لإشرافك.'
              }
              action={
                canRequestAdvances && advancesRoleView === 'mine'
                  ? {
                      label: 'تقديم أول طلب سلفة',
                      onClick: () => setNewRequestOpen(true),
                    }
                  : undefined
              }
            />
          ) : (
            <DataTable
              tableId="advance-requests-list"
              columns={advanceColumns}
              data={advanceRequests}
              loading={loadingAdvances}
              emptyTitle={
                advancesRoleView === 'review'
                  ? 'لا توجد طلبات سلف بانتظار المراجعة'
                  : 'لم تقم برفع أي طلبات سلف بعد'
              }
              emptyDescription={
                advancesRoleView === 'review'
                  ? 'سيظهر هنا أي طلب سلفة موجه لقسم المحاسبة حسب نظام الرتب والتوجيه المسؤول.'
                  : 'بصفتك مشرفاً ميدانياً، يمكنك تقديم طلبات سلف نقدية أو بنكية للمناديب التابعين لإشرافك.'
              }
              exportFilename="advance_requests"
              showTotals={true}
            />
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 3: كشوف حساب المناديب                            */}
      {/* ======================================================== */}
      {activeTab === 'statements' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4">
            <h3 className="font-bold text-sm text-[var(--ink)]">استعلام واستخراج كشف حساب المندوب المطابق</h3>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
              <div>
                <label className="block text-xs font-semibold text-[var(--muted)] mb-1">رقم الإقامة:</label>
                <input
                  type="text"
                  value={statementIqama}
                  onChange={(e) => setStatementIqama(e.target.value)}
                  placeholder="2000000789"
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-[var(--muted)] mb-1">من تاريخ:</label>
                <input
                  type="date"
                  value={statementFrom}
                  onChange={(e) => setStatementFrom(e.target.value)}
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-[var(--muted)] mb-1">إلى تاريخ:</label>
                <input
                  type="date"
                  value={statementTo}
                  onChange={(e) => setStatementTo(e.target.value)}
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs font-mono focus:outline-hidden"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-[var(--muted)] mb-1">نوع الكشف:</label>
                <select
                  value={statementMode}
                  onChange={(e) => setStatementMode(e.target.value as any)}
                  className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
                >
                  <option value="detailed">كشف مالي تفصيلي (360)</option>
                  <option value="simple">كشف حركات مبسط</option>
                </select>
              </div>
            </div>

            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={handleFetchStatement}
                disabled={loadingStatement}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition cursor-pointer"
              >
                {loadingStatement ? 'جاري الاستعلام...' : 'استعراض كشف الحساب'}
              </button>
            </div>
          </div>

          {statementError ? (
            <ErrorState title="تعذر استرجاع كشف الحساب" message={statementError} onRetry={handleFetchStatement} />
          ) : loadingStatement ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : statementData ? (
            <div className="p-6 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-4 border-b border-[var(--line)] pb-4">
                <div>
                  <h4 className="font-bold text-base text-[var(--ink)]">{statementData.riderName}</h4>
                  <div className="font-mono text-xs text-[var(--accent)]">إقامة: {statementData.iqama}</div>
                </div>
                <div className="flex items-center gap-4 text-xs">
                  <div>
                    <span className="text-[var(--muted)]">الرصيد الافتتاحي: </span>
                    <span className="num font-bold">{statementData.openingBalance?.toLocaleString('en-US') || 0} ر.س</span>
                  </div>
                  <div>
                    <span className="text-[var(--muted)]">الرصيد الختامي: </span>
                    <span className="num font-bold text-[var(--accent)]">
                      {statementData.closingBalance?.toLocaleString('en-US') || 0} ر.س
                    </span>
                  </div>
                </div>
              </div>

              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs">
                  <thead className="border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-2.5 text-start">التاريخ</th>
                      <th className="p-2.5 text-start">البيان / الحركة</th>
                      <th className="p-2.5 text-start">مدين</th>
                      <th className="p-2.5 text-start">دائن</th>
                      <th className="p-2.5 text-start">الرصيد</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {(statementData.entries || []).map((e: any, idx: number) => (
                      <tr key={idx} className="hover:bg-[var(--row-hover)]">
                        <td className="p-2.5 font-mono text-[var(--muted)]">{e.date?.split('T')[0] || e.date}</td>
                        <td className="p-2.5 font-medium">{e.description || e.statement}</td>
                        <td className="p-2.5 num font-bold text-[var(--danger)]">{e.debit > 0 ? `${e.debit} ر.س` : '—'}</td>
                        <td className="p-2.5 num font-bold text-[var(--money-pos)]">{e.credit > 0 ? `${e.credit} ر.س` : '—'}</td>
                        <td className="p-2.5 num font-bold text-[var(--ink)]">{e.balance} ر.س</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 4: منظومة الاعتمادات                               */}
      {/* ======================================================== */}
      {activeTab === 'approvals' && (
        <div className="space-y-4">
          <DataTable
            tableId="approvals-list"
            columns={approvalColumns}
            data={approvals}
            loading={loading}
            emptyTitle="لا توجد طلبات اعتماد معلقة"
            emptyDescription="كافة طلبات التكاليف والخصومات والبدلات معتمدة ومطابقة."
            exportFilename="approvals_list"
            showTotals={true}
          />
        </div>
      )}

      {/* ======================================================== */}
      {/* Sub-tab 5: أرشيف سندات الأمر                             */}
      {/* ======================================================== */}
      {activeTab === 'promissory' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] flex items-center justify-between">
            <div>
              <h4 className="font-bold text-sm text-[var(--ink)]">حالة تفعيل سندات الأمر التنفيذية نافذ</h4>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                {promissoryFeatureEnabled
                  ? 'ميزة سندات الأمر التنفيذية مفعلة لتوثيق المديونيات رسمياً.'
                  : 'ميزة سندات الأمر غير مفعلة افتراضياً لحين اكتمال الربط.'}
              </p>
            </div>
            <span
              className={`px-3 py-1 rounded-full text-xs font-bold ${
                promissoryFeatureEnabled
                  ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                  : 'bg-[var(--muted)]/15 text-[var(--muted)]'
              }`}
            >
              {promissoryFeatureEnabled ? 'مفعل' : 'غير مفعل'}
            </span>
          </div>

          {promissoryNotes.length === 0 ? (
            <EmptyState
              title="لا توجد سندات أمر مسجلة بالأرشيف"
              description="لم يتم إصدار أي سندات أمر تنفيذية للتحصيل أو المديونيات بعد."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden">
              <table className="w-full text-start text-xs sm:text-sm">
                <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                  <tr>
                    <th className="p-3 text-start">رقم السند</th>
                    <th className="p-3 text-start">رقم الإقامة</th>
                    <th className="p-3 text-start">المبلغ</th>
                    <th className="p-3 text-start">تاريخ الاستحقاق</th>
                    <th className="p-3 text-start">الحالة</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {promissoryNotes.map((pn) => (
                    <tr key={pn.id} className="hover:bg-[var(--row-hover)]">
                      <td className="p-3 font-mono text-xs">{pn.noteNumber || pn.id}</td>
                      <td className="p-3 font-mono">{pn.iqama}</td>
                      <td className="p-3 num font-bold">{pn.amount} ر.س</td>
                      <td className="p-3 num text-xs">{pn.dueDate}</td>
                      <td className="p-3">
                        <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--ink)]">
                          {pn.status}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* ======================================================== */}
      {/* DIALOG: تقديم طلب سلفة جديد (المشرف)                      */}
      {/* ======================================================== */}
      <Dialog
        open={newRequestOpen}
        onClose={() => setNewRequestOpen(false)}
        title="تقديم طلب سلفة جديد لمندوب"
      >
        <form onSubmit={handleCreateAdvanceRequest} className="space-y-4">
          <div className="p-3 rounded-xl bg-[var(--raised)] text-xs text-[var(--muted)]">
            ملاحظة نظام الرتب: بصفتك مشرفاً، يقتصر اختيار المناديب على من هم مسجلين تحت إشرافك المباشر في سجل التنظيم.
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">المندوب المستفيد:</label>
            <RiderPicker
              value={newReqIqama}
              onChange={(r) => setNewReqIqama(r?.iqama || '')}
              placeholder="ابحث بالاسم أو رقم الإقامة..."
              required
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">نوع السلفة:</label>
            <div className="grid grid-cols-3 gap-2">
              <label
                className={`p-3 rounded-xl border text-xs font-bold flex items-center justify-center gap-2 cursor-pointer transition ${
                  newReqType === 'cash'
                    ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)]'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <input
                  type="radio"
                  name="advanceType"
                  value="cash"
                  checked={newReqType === 'cash'}
                  onChange={() => setNewReqType('cash')}
                  className="sr-only"
                />
                <Wallet size={16} />
                <span>نقدية</span>
              </label>

              <label
                className={`p-3 rounded-xl border text-xs font-bold flex items-center justify-center gap-2 cursor-pointer transition ${
                  newReqType === 'bank'
                    ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)]'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <input
                  type="radio"
                  name="advanceType"
                  value="bank"
                  checked={newReqType === 'bank'}
                  onChange={() => setNewReqType('bank')}
                  className="sr-only"
                />
                <Banknote size={16} />
                <span>بنكية</span>
              </label>

              <label
                className={`p-3 rounded-xl border text-xs font-bold flex items-center justify-center gap-2 cursor-pointer transition ${
                  newReqType === 'internet'
                    ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)]'
                    : 'border-[var(--line)] text-[var(--muted)]'
                }`}
              >
                <input
                  type="radio"
                  name="advanceType"
                  value="internet"
                  checked={newReqType === 'internet'}
                  onChange={() => setNewReqType('internet')}
                  className="sr-only"
                />
                <Wifi size={16} />
                <span>سلفة نت</span>
              </label>
            </div>
          </div>

          {newReqType === 'internet' && (
            <div className="p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/50 space-y-3">
              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--ink)]">طريقة الصرف (Fulfilment):</label>
                <div className="grid grid-cols-3 gap-2">
                  <label
                    className={`p-2 rounded-lg border text-xs font-medium flex items-center justify-center gap-1.5 cursor-pointer transition ${
                      newReqFulfilment === 'cash'
                        ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)] font-bold'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    <input
                      type="radio"
                      name="fulfilmentType"
                      value="cash"
                      checked={newReqFulfilment === 'cash'}
                      onChange={() => setNewReqFulfilment('cash')}
                      className="sr-only"
                    />
                    <Wallet size={14} />
                    <span>نقدي</span>
                  </label>

                  <label
                    className={`p-2 rounded-lg border text-xs font-medium flex items-center justify-center gap-1.5 cursor-pointer transition ${
                      newReqFulfilment === 'bank'
                        ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)] font-bold'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    <input
                      type="radio"
                      name="fulfilmentType"
                      value="bank"
                      checked={newReqFulfilment === 'bank'}
                      onChange={() => setNewReqFulfilment('bank')}
                      className="sr-only"
                    />
                    <Banknote size={14} />
                    <span>بنكي</span>
                  </label>

                  <label
                    className={`p-2 rounded-lg border text-xs font-medium flex items-center justify-center gap-1.5 cursor-pointer transition ${
                      newReqFulfilment === 'recharge_card'
                        ? 'border-[var(--accent)] bg-[var(--accent)]/10 text-[var(--accent)] font-bold'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    <input
                      type="radio"
                      name="fulfilmentType"
                      value="recharge_card"
                      checked={newReqFulfilment === 'recharge_card'}
                      onChange={() => setNewReqFulfilment('recharge_card')}
                      className="sr-only"
                    />
                    <CreditCard size={14} />
                    <span>كارت شحن (مدى)</span>
                  </label>
                </div>
              </div>

              {newReqFulfilment === 'recharge_card' && (
                <div className="grid grid-cols-2 gap-2 pt-1">
                  <div className="space-y-1">
                    <label className="text-xs font-bold text-[var(--ink)]">شبكة الشحن:</label>
                    <SearchableSelect
                      id="select-recharge-network"
                      value={newReqNetwork}
                      ariaLabel="شبكة الشحن"
                      placeholder="-- اختر الشبكة --"
                      required
                      onChange={(net) => {
                        setNewReqNetwork(net);
                        setNewReqPackage('');
                        setNewReqAmount('');
                      }}
                      options={(rechargeNetworks as Array<{ code: string; nameAr?: string }>).map((n) => ({
                        value: n.code,
                        label: n.nameAr || n.code,
                      }))}
                    />
                  </div>

                  <div className="space-y-1">
                    <label className="text-xs font-bold text-[var(--ink)]">باقة الشحن:</label>
                    <SearchableSelect
                      id="select-recharge-package"
                      value={newReqPackage}
                      ariaLabel="باقة الشحن"
                      placeholder="-- اختر الباقة --"
                      disabled={!newReqNetwork}
                      required
                      onChange={(pkgCode) => {
                        setNewReqPackage(pkgCode);
                        const pkg = (rechargePackages as Array<{ code: string; values?: { price?: number | string } }>).find(
                          (p) => p.code === pkgCode
                        );
                        if (pkg?.values?.price) {
                          setNewReqAmount(String(pkg.values.price));
                        }
                      }}
                      options={(rechargePackages as Array<{ code: string; nameAr?: string; values?: { network?: string; price?: number | string } }>)
                        .filter((p) => !newReqNetwork || p.values?.network === newReqNetwork)
                        .map((p) => ({
                          value: p.code,
                          label: `${p.nameAr || p.code} (${p.values?.price ?? 0} ر.س)`,
                        }))}
                    />
                  </div>
                </div>
              )}
            </div>
          )}

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">المبلغ المطلوب (ر.س):</label>
            <input
              type="number"
              id="input-req-amount"
              min="1"
              max="100000"
              step="any"
              value={newReqAmount}
              readOnly={newReqType === 'internet' && newReqFulfilment === 'recharge_card'}
              onChange={(e) => setNewReqAmount(e.target.value)}
              placeholder="مثال: 400"
              required
              className={`w-full p-2.5 rounded-xl border border-[var(--line)] text-xs font-bold num focus:outline-hidden ${
                newReqType === 'internet' && newReqFulfilment === 'recharge_card'
                  ? 'bg-[var(--raised)] text-[var(--muted)] cursor-not-allowed'
                  : 'bg-[var(--surface)]'
              }`}
            />
            {newReqType === 'internet' && newReqFulfilment === 'recharge_card' && (
              <p className="text-[11px] text-[var(--muted)]">
                * يتم تحديد المبلغ تلقائياً بناءً على سعر الباقة المختارة من القوائم المعتمدة.
              </p>
            )}
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">سبب وضرورة السلفة:</label>
            <textarea
              id="input-req-reason"
              rows={3}
              value={newReqReason}
              onChange={(e) => setNewReqReason(e.target.value)}
              placeholder="اكتب سبب طلب السلفة والظرف التشغيلي أو الطارئ..."
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="flex items-center justify-end gap-2 pt-2 border-t border-[var(--line)]">
            <button
              type="button"
              onClick={() => setNewRequestOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="submit"
              id="btn-submit-advance-request"
              disabled={submittingNewReq}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
            >
              {submittingNewReq ? 'جاري الإرسال...' : 'رفع الطلب للمراجعة'}
            </button>
          </div>
        </form>
      </Dialog>

      {/* ======================================================== */}
      {/* DIALOG: الرد على اقتراح التعديل (المشرف)                  */}
      {/* ======================================================== */}
      <Dialog
        open={Boolean(proposalReq)}
        onClose={() => setProposalReq(null)}
        title="الرد على اقتراح المحاسب بتعديل السلفة"
      >
        {proposalReq && (
          <div className="space-y-4">
            <div className="p-4 rounded-xl border border-[var(--warning)]/30 bg-[var(--warning)]/10 space-y-2 text-xs">
              <div className="font-bold text-[var(--warning)]">
                اقترح المحاسب المسؤول ({proposalReq.proposal?.proposedBy || 'المحاسب'}):
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">النوع المقترح:</span>
                <span className="font-bold">
                  {proposalReq.proposal?.type === 'cash' ? 'سلفة نقدية' : 'سلفة بنكية'}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المبلغ المقترح:</span>
                <span className="font-bold num">{proposalReq.proposal?.amount} ر.س</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">الخزينة المقترحة:</span>
                <span className="font-mono">{proposalReq.proposal?.vaultCode}</span>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">قرارك كمشرف:</label>
              <div className="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  id="btn-proposal-accept"
                  onClick={() => setProposalAction('accept')}
                  className={`py-2.5 rounded-xl border text-xs font-bold transition cursor-pointer flex items-center justify-center gap-1.5 ${
                    proposalAction === 'accept'
                      ? 'border-[var(--money-pos)] bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  <Check size={14} />
                  <span>الموافقة على التعديل</span>
                </button>

                <button
                  type="button"
                  id="btn-proposal-reject"
                  onClick={() => setProposalAction('reject')}
                  className={`py-2.5 rounded-xl border text-xs font-bold transition cursor-pointer flex items-center justify-center gap-1.5 ${
                    proposalAction === 'reject'
                      ? 'border-[var(--danger)] bg-[var(--danger)]/10 text-[var(--danger)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  <X size={14} />
                  <span>رفض التعديل وإلغاء الطلب</span>
                </button>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">ملاحظات الرد (اختياري):</label>
              <textarea
                rows={2}
                value={proposalNote}
                onChange={(e) => setProposalNote(e.target.value)}
                placeholder="اكتب أي ملاحظة للمحاسب..."
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setProposalReq(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer"
              >
                إغلاق
              </button>
              <button
                type="button"
                id="btn-submit-proposal-response"
                disabled={submittingProposal}
                onClick={handleRespondToProposal}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer"
              >
                {submittingProposal ? 'جاري الإرسال...' : 'تأكيد القرار'}
              </button>
            </div>
          </div>
        )}
      </Dialog>

      {/* ======================================================== */}
      {/* DRAWER: مراجعة طلب السلفة والملخص المالي (المحاسب)         */}
      {/* ======================================================== */}
      <Drawer
        open={reviewDrawerOpen}
        onClose={() => {
          setReviewDrawerOpen(false);
          setSelectedAdvance(null);
          setAdvanceSummary(null);
        }}
        title={
          selectedAdvance
            ? `مراجعة طلب سلفة: ${selectedAdvance.riderName}`
            : 'مراجعة طلب السلفة'
        }
        subtitle={
          selectedAdvance
            ? `إقامة: ${selectedAdvance.riderIqama} · ${selectedAdvance.type === 'cash' ? 'سلفة نقدية' : 'سلفة بنكية'} ${selectedAdvance.amount} ر.س`
            : ''
        }
      >
        {selectedAdvance && (
          <div className="space-y-5 pb-6">
            {/* Basic Info Bar */}
            <div className="p-4 rounded-xl bg-[var(--raised)] space-y-2 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">مقدم الطلب:</span>
                <span className="font-semibold">{selectedAdvance.requestedBy}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">تاريخ الطلب:</span>
                <span className="num">{selectedAdvance.createdAt?.split('T')[0]}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">الخزينة المحددة:</span>
                <span className="font-mono text-[var(--accent)]">{selectedAdvance.vaultCode}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">سبب السلفة:</span>
                <span className="font-medium text-[var(--ink)]">{selectedAdvance.reason}</span>
              </div>
            </div>

            {/* Financial Summary Card */}
            <div className="space-y-3 p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
              <div className="flex items-center justify-between">
                <h4 className="font-bold text-xs text-[var(--ink)]">الملخص المالي للمندوب والتحقق من التغطية</h4>
                {loadingAdvanceSummary && <span className="text-[10px] text-[var(--muted)] animate-pulse">جاري التحديث...</span>}
              </div>

              {advanceSummary ? (
                <div className="space-y-3">
                  {/* Coverage Banner */}
                  {advanceSummary.covered ? (
                    <div className="p-3 rounded-xl border border-[var(--money-pos)]/30 bg-[var(--money-pos)]/10 text-[var(--money-pos)] text-xs flex items-center gap-2">
                      <CheckCircle size={16} className="shrink-0" />
                      <span>الصافي التقديري لهذا الشهر يغطي مبلغ السلفة بالكامل.</span>
                    </div>
                  ) : (
                    <div className="p-3 rounded-xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 text-[var(--danger)] text-xs flex items-center gap-2">
                      <AlertTriangle size={16} className="shrink-0" />
                      <span>
                        الصافي المتوقع لا يغطي السلفة أو غير معروف؛ كتابة سبب الاعتماد إلزامية حسب نظام الرتب والرقابة المالية.
                      </span>
                    </div>
                  )}

                  {/* Metrics grid */}
                  <div className="grid grid-cols-2 gap-2 text-xs">
                    <div className="p-2.5 rounded-lg bg-[var(--raised)]/60">
                      <div className="text-[11px] text-[var(--muted)]">طلبات هذا الشهر</div>
                      <div className="font-bold text-sm text-[var(--ink)] num">{advanceSummary.ordersThisMonth} طلب</div>
                    </div>
                    <div className="p-2.5 rounded-lg bg-[var(--raised)]/60">
                      <div className="text-[11px] text-[var(--muted)]">الصافي التقديري للمسير</div>
                      <div className="font-bold text-sm text-[var(--accent)] num">
                        {advanceSummary.statement?.net != null ? `${advanceSummary.statement.net.toFixed(2)} ر.س` : 'غير متوفر بعد'}
                      </div>
                    </div>
                    <div className="p-2.5 rounded-lg bg-[var(--raised)]/60">
                      <div className="text-[11px] text-[var(--muted)]">المديونيات المفتوحة</div>
                      <div className="font-bold text-sm text-[var(--danger)] num">
                        {advanceSummary.openDebtBalance.toFixed(2)} ر.س ({advanceSummary.openDebts})
                      </div>
                    </div>
                    <div className="p-2.5 rounded-lg bg-[var(--raised)]/60">
                      <div className="text-[11px] text-[var(--muted)]">الأقساط الشهرية المخططة</div>
                      <div className="font-bold text-sm text-[var(--ink)] num">{advanceSummary.plannedInstallments.toFixed(2)} ر.س</div>
                    </div>
                  </div>

                  {/* Advances since last settlement */}
                  <div className="space-y-1.5 pt-1">
                    <div className="text-[11px] font-bold text-[var(--muted)]">
                      السلف منذ آخر تسوية ({advanceSummary.lastPayrollSettlementAt ? advanceSummary.lastPayrollSettlementAt.split('T')[0] : 'لا يوجد مسير سابق'}):
                    </div>
                    {advanceSummary.advancesSinceLastSettlement && advanceSummary.advancesSinceLastSettlement.length > 0 ? (
                      <div className="space-y-1">
                        {advanceSummary.advancesSinceLastSettlement.map((as, idx) => (
                          <div key={idx} className="p-2 rounded-lg bg-[var(--raised)] text-xs flex justify-between">
                            <span>{as.category === 'CashAdvance' ? 'سلفة نقدية' : 'سلفة بنكية'} ({as.status})</span>
                            <span className="font-bold num">{as.amount} ر.س</span>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <div className="text-xs text-[var(--muted)] italic">لا توجد سلف إضافية مسجلة منذ آخر تسوية.</div>
                    )}
                  </div>

                  {/* Vault Balances */}
                  <div className="space-y-1.5 pt-1">
                    <div className="text-[11px] font-bold text-[var(--muted)]">أرصدة الخزائن والصناديق المتاحة:</div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                      {advanceSummary.vaults?.map((v, i) => (
                        <div
                          key={i}
                          className={`p-2.5 rounded-xl border ${
                            v.selected
                              ? 'border-[var(--accent)] bg-[var(--accent)]/5'
                              : 'border-[var(--line)] bg-[var(--surface)]'
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <span className="font-semibold">{v.type === 'cash' ? 'خزينة نقدية' : 'حساب بنكي'}</span>
                            {v.selected && (
                              <span className="px-1.5 py-0.2 rounded-md bg-[var(--accent)] text-[var(--accent-ink)] text-[10px] font-bold">
                                محددة
                              </span>
                            )}
                          </div>
                          <div className="font-mono text-[11px] text-[var(--muted)] truncate">{v.code || '—'}</div>
                          <div className="font-bold text-xs num mt-1">الرصيد: {v.balance.toFixed(2)} ر.س</div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              ) : (
                <div className="p-4 text-center text-xs text-[var(--muted)]">جاري تحميل بيانات الملخص المالي...</div>
              )}
            </div>

            {/* Decision Controls (For Reviewer when under_review) */}
            {canReviewAdvances && selectedAdvance.status === 'under_review' && (
              <div className="space-y-3 p-4 rounded-xl border border-[var(--accent)]/30 bg-[var(--surface)] shadow-xs">
                <h4 className="font-bold text-xs text-[var(--ink)]">إجراءات المراجعة والبت (محاسب مالي مسؤول)</h4>

                {/* Mode Selector */}
                <div className="grid grid-cols-3 gap-2">
                  <button
                    type="button"
                    id="btn-mode-approve"
                    onClick={() => setReviewMode('approve')}
                    className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                      reviewMode === 'approve'
                        ? 'border-[var(--money-pos)] bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    اعتماد السلفة
                  </button>

                  <button
                    type="button"
                    id="btn-mode-propose"
                    onClick={() => setReviewMode('propose')}
                    className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                      reviewMode === 'propose'
                        ? 'border-[var(--warning)] bg-[var(--warning)]/10 text-[var(--warning)]'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    اقتراح تعديل
                  </button>

                  <button
                    type="button"
                    id="btn-mode-reject"
                    onClick={() => setReviewMode('reject')}
                    className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                      reviewMode === 'reject'
                        ? 'border-[var(--danger)] bg-[var(--danger)]/10 text-[var(--danger)]'
                        : 'border-[var(--line)] text-[var(--muted)]'
                    }`}
                  >
                    رفض الطلب
                  </button>
                </div>

                {/* Approve Form */}
                {reviewMode === 'approve' && (
                  <div className="space-y-2.5 pt-2">
                    <div>
                      <label className="block text-xs font-bold text-[var(--ink)] mb-1">
                        القسط الشهري للاستقطاع (ر.س):
                      </label>
                      <input
                        type="number"
                        id="input-review-installment"
                        min="1"
                        max={selectedAdvance.amount}
                        value={reviewInstallment}
                        onChange={(e) => setReviewInstallment(e.target.value)}
                        className="w-full p-2 rounded-xl border border-[var(--line)] text-xs font-bold num focus:outline-hidden"
                      />
                    </div>

                    {advanceSummary && !advanceSummary.covered && (
                      <div>
                        <label className="block text-xs font-bold text-[var(--danger)] mb-1">
                          سبب الاعتماد الاستثنائي (إلزامي لعدم التغطية):
                        </label>
                        <textarea
                          id="textarea-coverage-reason"
                          rows={2}
                          value={reviewCoverageReason}
                          onChange={(e) => setReviewCoverageReason(e.target.value)}
                          placeholder="اكتب مبررات الاعتماد الاستثنائي رغم عدم كفاية الصافي المتوقع..."
                          required
                          className="w-full p-2 rounded-xl border border-[var(--danger)]/50 bg-[var(--danger)]/5 text-xs focus:outline-hidden"
                        />
                      </div>
                    )}

                    <div>
                      <label className="block text-xs font-bold text-[var(--ink)] mb-1">ملاحظات الاعتماد (اختياري):</label>
                      <input
                        type="text"
                        value={reviewNote}
                        onChange={(e) => setReviewNote(e.target.value)}
                        placeholder="أي ملاحظة تسجل في شريط القرارات..."
                        className="w-full p-2 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
                      />
                    </div>
                  </div>
                )}

                {/* Propose Form */}
                {reviewMode === 'propose' && (
                  <div className="space-y-2.5 pt-2">
                    <div className="grid grid-cols-2 gap-2">
                      <div>
                        <label className="block text-xs font-bold text-[var(--ink)] mb-1">النوع المقترح:</label>
                        <select
                          id="select-propose-type"
                          value={proposeType}
                          onChange={(e) => setProposeType(e.target.value as any)}
                          className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs focus:outline-hidden"
                        >
                          <option value="bank">سلفة بنكية (تحويل)</option>
                          <option value="cash">سلفة نقدية (خزينة)</option>
                        </select>
                      </div>
                      <div>
                        <label className="block text-xs font-bold text-[var(--ink)] mb-1">المبلغ المقترح (ر.س):</label>
                        <input
                          type="number"
                          id="input-propose-amount"
                          min="1"
                          value={proposeAmount}
                          onChange={(e) => setProposeAmount(e.target.value)}
                          className="w-full p-2 rounded-xl border border-[var(--line)] text-xs font-bold num focus:outline-hidden"
                        />
                      </div>
                    </div>

                    <div>
                      <label className="block text-xs font-bold text-[var(--ink)] mb-1">
                        سبب اقتراح التعديل (إلزامي للمشرف):
                      </label>
                      <textarea
                        id="textarea-propose-note"
                        rows={2}
                        value={reviewNote}
                        onChange={(e) => setReviewNote(e.target.value)}
                        placeholder="مثال: الخزينة النقدية غير متاحة حالياً ونقترح الصرف بتحويل بنكي..."
                        required
                        className="w-full p-2 rounded-xl border border-[var(--line)] text-xs focus:outline-hidden"
                      />
                    </div>
                  </div>
                )}

                {/* Reject Form */}
                {reviewMode === 'reject' && (
                  <div className="space-y-2.5 pt-2">
                    <div>
                      <label className="block text-xs font-bold text-[var(--danger)] mb-1">سبب الرفض (إلزامي):</label>
                      <textarea
                        id="textarea-reject-note"
                        rows={2}
                        value={reviewNote}
                        onChange={(e) => setReviewNote(e.target.value)}
                        placeholder="اكتب سبب الرفض الصريح للمشرف والمندوب..."
                        required
                        className="w-full p-2 rounded-xl border border-[var(--danger)]/40 bg-[var(--danger)]/5 text-xs focus:outline-hidden"
                      />
                    </div>
                  </div>
                )}

                <div className="pt-2 flex justify-end">
                  <button
                    type="button"
                    id="btn-submit-review-decision"
                    disabled={submittingReview}
                    onClick={handleReviewAdvance}
                    className="px-5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition cursor-pointer"
                  >
                    {submittingReview ? 'جاري التنفيذ...' : 'تسجيل قرار المراجعة'}
                  </button>
                </div>
              </div>
            )}

            {/* Decision History Timeline */}
            <div className="space-y-3 p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
              <h4 className="font-bold text-xs text-[var(--ink)] flex items-center gap-1.5">
                <History size={14} />
                <span>سجل القرارات والتدقيق الزمني</span>
              </h4>

              {selectedAdvance.decisions && selectedAdvance.decisions.length > 0 ? (
                <div className="space-y-2">
                  {selectedAdvance.decisions.map((dec, idx) => (
                    <div key={idx} className="p-3 rounded-xl bg-[var(--raised)] space-y-1 text-xs">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-1.5">
                          <span className="font-bold text-[var(--ink)]">{dec.actor}</span>
                          {dec.snapshot?.department && (
                            <span className="px-1.5 py-0.2 rounded-md bg-[var(--surface)] border border-[var(--line)] text-[10px] font-mono">
                              {dec.snapshot.department} ({dec.snapshot.rank || '—'})
                            </span>
                          )}
                        </div>
                        <span className="text-[10px] text-[var(--muted)] num">
                          {dec.at ? dec.at.replace('T', ' ').slice(0, 19) : ''}
                        </span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-[var(--accent)]">
                          {dec.action === 'submitted'
                            ? 'تقديم الطلب'
                            : dec.action === 'approved'
                            ? 'اعتماد السلفة'
                            : dec.action === 'rejected'
                            ? 'رفض السلفة'
                            : dec.action === 'proposed'
                            ? 'اقتراح تعديل'
                            : dec.action === 'accepted_proposal'
                            ? 'قبول التعديل'
                            : dec.action === 'rejected_proposal'
                            ? 'رفض التعديل'
                            : dec.action === 'paid'
                            ? 'صرف السلفة'
                            : dec.action}
                        </span>
                        {dec.monthlyInstallment && (
                          <span className="text-[11px] text-[var(--muted)]">
                            (قسط: {dec.monthlyInstallment} ر.س)
                          </span>
                        )}
                        {dec.reference && (
                          <span className="text-[11px] font-mono text-[var(--muted)]">
                            مرجع: {dec.reference}
                          </span>
                        )}
                      </div>
                      {dec.note && <p className="text-[11px] text-[var(--muted)] mt-1">{dec.note}</p>}
                    </div>
                  ))}
                </div>
              ) : (
                <div className="text-xs text-[var(--muted)] italic">لا يوجد سجل قرارات إضافي.</div>
              )}
            </div>
          </div>
        )}
      </Drawer>

      {/* Approvals dialog */}
      <Dialog
        open={Boolean(selectedApproval)}
        onClose={() => setSelectedApproval(null)}
        title="البت في طلب الاعتماد"
      >
        {selectedApproval && (
          <div className="space-y-4">
            <div className="p-3 rounded-xl bg-[var(--raised)] space-y-1.5 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">نوع البند:</span>
                <span className="font-bold">{selectedApproval.kind || selectedApproval.requestTypeAr || selectedApproval.requestType}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">رقم الإقامة:</span>
                <span className="font-mono">{selectedApproval.iqama}</span>
              </div>
              {selectedApproval.amount && (
                <div className="flex justify-between">
                  <span className="text-[var(--muted)]">المبلغ:</span>
                  <span className="font-bold num text-[var(--accent)]">{selectedApproval.amount} ر.س</span>
                </div>
              )}
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">القرار:</label>
              <div className="grid grid-cols-3 gap-2">
                <button
                  type="button"
                  onClick={() => setApprovalDecision('approved')}
                  className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                    approvalDecision === 'approved'
                      ? 'border-[var(--money-pos)] bg-[var(--money-pos)]/10 text-[var(--money-pos)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  اعتماد
                </button>
                <button
                  type="button"
                  onClick={() => setApprovalDecision('suspended')}
                  className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                    approvalDecision === 'suspended'
                      ? 'border-[var(--warning)] bg-[var(--warning)]/10 text-[var(--warning)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  تعليق للدراسة
                </button>
                <button
                  type="button"
                  onClick={() => setApprovalDecision('rejected')}
                  className={`py-2 rounded-xl border text-xs font-bold transition cursor-pointer ${
                    approvalDecision === 'rejected'
                      ? 'border-[var(--danger)] bg-[var(--danger)]/10 text-[var(--danger)]'
                      : 'border-[var(--line)] text-[var(--muted)]'
                  }`}
                >
                  رفض الطلب
                </button>
              </div>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold">ملاحظات وبيان القرار:</label>
              <textarea
                value={approvalNote}
                onChange={(e) => setApprovalNote(e.target.value)}
                placeholder="أدخل ملاحظات البت في الطلب..."
                rows={3}
                className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
              />
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-[var(--line)]">
              <button
                type="button"
                onClick={() => setSelectedApproval(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={submittingDecision}
                onClick={handleDecideApproval}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 cursor-pointer"
              >
                {submittingDecision ? 'جاري التسجيل...' : 'تأكيد القرار'}
              </button>
            </div>
          </div>
        )}
      </Dialog>

      {/* Debt details drawer */}
      <Drawer
        open={debtDrawerOpen}
        onClose={() => {
          setDebtDrawerOpen(false);
          setSelectedDebt(null);
        }}
        title={`تفاصيل المديونية: ${selectedDebt?.iqama || ''}`}
        subtitle={`فئة البند: ${selectedDebt ? debtCategoryLabel(selectedDebt) : ''}`}
      >
        {selectedDebt && (
          <div className="space-y-4">
            <div className="p-4 rounded-xl bg-[var(--raised)] space-y-2 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المبلغ الأصلي:</span>
                <span className="font-bold num">{(selectedDebt.originalAmount ?? 0).toLocaleString('en-US')} ر.س</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المتبقي:</span>
                <span className="font-bold num text-[var(--danger)]">
                  {(selectedDebt.remainingAmount ?? 0).toLocaleString('en-US')} ر.س
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">تاريخ الإنشاء:</span>
                <span className="num">{selectedDebt.createdAt?.split('T')[0]}</span>
              </div>
            </div>

            <div className="space-y-2">
              <h4 className="font-bold text-xs text-[var(--ink)]">ملاحظات البند:</h4>
              <p className="text-xs text-[var(--muted)] bg-[var(--surface)] p-3 rounded-xl border border-[var(--line)]">
                {selectedDebt.notes || 'لا توجد ملاحظات إضافية مسجلة على هذه المديونية.'}
              </p>
            </div>

            {isAdmin && isPendingOpening(selectedDebt) && (
              <div className="flex items-center gap-2 pt-2 border-t border-[var(--line)]">
                <button
                  type="button"
                  onClick={() => {
                    setOpeningIds([selectedDebt.id]);
                    setOpeningDialog('approved');
                  }}
                  className="flex-1 px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 cursor-pointer"
                >
                  اعتماد الرصيد الافتتاحي
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setOpeningIds([selectedDebt.id]);
                    setOpeningDialog('rejected');
                  }}
                  className="flex-1 px-3 py-2 rounded-xl border border-[var(--danger)]/50 text-[var(--danger)] text-xs font-bold hover:bg-[var(--danger)]/10 cursor-pointer"
                >
                  رفض
                </button>
              </div>
            )}
          </div>
        )}
      </Drawer>

      <Dialog
        open={openingDialog !== null}
        onClose={() => (openingProgress ? null : setOpeningDialog(null))}
        title={openingDialog === 'rejected' ? 'رفض رصيد افتتاحي' : 'اعتماد أرصدة افتتاحية'}
        description={`${openingIds.length} مديونية — ${openingDialog === 'rejected' ? 'تُلغى ولا يُرحّل لها قيد.' : 'تصبح قائمة ويُرحّل قيد الافتتاح (ذمم المندوب / 3101) في شهر الرصيد.'}`}
        actions={
          <>
            <button
              type="button"
              disabled={!!openingProgress}
              onClick={() => setOpeningDialog(null)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-semibold text-[var(--ink)] hover:bg-[var(--raised)] cursor-pointer disabled:opacity-50"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={!!openingProgress}
              onClick={() => openingDialog && decideOpening(openingDialog, openingIds)}
              className={`px-4 py-2 rounded-xl text-xs font-bold cursor-pointer disabled:opacity-50 ${openingDialog === 'rejected' ? 'bg-[var(--danger)] text-white' : 'bg-[var(--accent)] text-[var(--accent-ink)]'}`}
            >
              {openingProgress ? `جاري التنفيذ ${openingProgress}` : openingDialog === 'rejected' ? 'تأكيد الرفض' : 'تأكيد الاعتماد'}
            </button>
          </>
        }
      >
        <label className="block space-y-1.5 text-xs">
          <span className="font-semibold text-[var(--ink)]">{openingDialog === 'rejected' ? 'سبب الرفض (إلزامي)' : 'ملاحظة (اختياري)'}</span>
          <textarea
            value={openingNote}
            onChange={(e) => setOpeningNote(e.target.value)}
            rows={3}
            className="w-full px-3 py-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] focus:outline-hidden"
          />
        </label>
      </Dialog>
    </div>
  );
}

export default function DebtsPage() {
  return (
    <Suspense fallback={<div className="p-8 text-center text-xs text-[var(--muted)]">جاري تحميل صفحة المديونيات...</div>}>
      <DebtsPageContent />
    </Suspense>
  );
}
