'use client';

import React, { useState, useEffect, useMemo } from 'react';
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
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { StatCard } from '../../components/ui/StatCard';
import { Drawer } from '../../components/ui/Drawer';
import { Dialog } from '../../components/ui/Dialog';
import { Skeleton, EmptyState } from '../../components/ui/EmptyState';
import type {
  DebtRecord,
  ApprovalRequest,
  PromissoryNote,
  RiderStatement,
} from '../../lib/types';

export default function DebtsPage() {
  const { user } = useAuth();
  const { toast } = useToast();

  const [activeTab, setActiveTab] = useState<'debts' | 'statements' | 'approvals' | 'promissory'>('debts');
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');
  const [searchQuery, setSearchQuery] = useState<string>('');

  // Data states
  const [loading, setLoading] = useState<boolean>(true);
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

  // Approval decision dialog
  const [selectedApproval, setSelectedApproval] = useState<ApprovalRequest | null>(null);
  const [approvalDecision, setApprovalDecision] = useState<'approved' | 'rejected' | 'suspended'>('approved');
  const [approvalNote, setApprovalNote] = useState<string>('');
  const [submittingDecision, setSubmittingDecision] = useState<boolean>(false);

  // Debt details drawer
  const [selectedDebt, setSelectedDebt] = useState<DebtRecord | null>(null);
  const [debtDrawerOpen, setDebtDrawerOpen] = useState<boolean>(false);

  const loadData = async () => {
    setLoading(true);
    try {
      const [debtsRes, approvalsRes, pNotesRes] = await Promise.all([
        api.getDebts().catch(() => []),
        api.getApprovals().catch(() => []),
        api.getPromissoryNotes().catch(() => ({ featureEnabled: false, items: [] })),
      ]);
      setDebts(debtsRes || []);
      setApprovals(approvalsRes || []);
      setPromissoryNotes(pNotesRes.items || []);
      setPromissoryFeatureEnabled(pNotesRes.featureEnabled || false);
    } catch (err: any) {
      toast(err.message || 'تعذر تحميل بيانات المديونيات', 'danger');
    } finally {
      setLoading(false);
    }
  };

  const handleFetchStatement = async () => {
    if (!statementIqama.trim()) {
      toast('يرجى إدخال رقم إقامة المندوب', 'warning');
      return;
    }
    setLoadingStatement(true);
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
      toast(err.message || 'تعذر استرجاع كشف حساب المندوب', 'danger');
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

  useEffect(() => {
    loadData();
  }, []);

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

  // Derived statistics
  const totalRemainingDebts = useMemo(() => debts.reduce((s, d) => s + (d.remainingAmount || 0), 0), [debts]);
  const totalAdvances = useMemo(
    () => debts.filter((d) => d.category === 'advance').reduce((s, d) => s + (d.remainingAmount || 0), 0),
    [debts]
  );
  const pendingApprovalsCount = useMemo(() => approvals.filter((a) => a.status === 'pending').length, [approvals]);

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <PageHeader
        title="المديونيات والسلف والتحصيل"
        description="إدارة مديونيات وسلف وتكاليف المناديب، كشوف الحساب المطابقة 360 والمبسطة، منظومة الاعتمادات المتبادلة وأرشيف سندات الأمر التنفيذية."
        eyebrow="الذمم والعهد المالية"
        primaryAction={{
          label: 'تحديث البيانات',
          icon: <Receipt size={15} />,
          onClick: loadData,
        }}
      />

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <StatCard
          label="إجمالي المديونيات القائمة"
          value={`${totalRemainingDebts.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint="الأرصدة المتبقية على ذمة المناديب"
          icon={Receipt}
        />
        <StatCard
          label="السلف النقدية القائمة"
          value={`${totalAdvances.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س`}
          hint="سلف مؤقتة بانتظار استقطاع المسير"
          icon={Coins}
        />
        <StatCard
          label="الاعتمادات بانتظار البت"
          value={pendingApprovalsCount}
          hint={pendingApprovalsCount > 0 ? 'طلبات تتطلب اتخاذ قرار مالي' : 'لا توجد طلبات معلقة'}
          icon={ShieldCheck}
        />
        <StatCard
          label="سندات الأمر المؤرشفة"
          value={promissoryNotes.length}
          hint={promissoryFeatureEnabled ? 'الموديول نشط ومعتمد' : 'وضع التعليق المؤقت (Dormant)'}
          icon={FileCheck}
        />
      </div>

      {/* Sub Tabs */}
      <div className="border-b border-[var(--line)]">
        <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
          {[
            { id: 'debts', label: 'المديونيات والسلف والتكاليف', count: filteredDebts.length },
            { id: 'statements', label: 'كشوف حساب المناديب (360 ومبسط)' },
            { id: 'approvals', label: 'منظومة الاعتمادات', count: pendingApprovalsCount, alert: pendingApprovalsCount > 0 },
            { id: 'promissory', label: 'أرشيف سندات الأمر', count: promissoryNotes.length },
          ].map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => setActiveTab(tab.id as any)}
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

      {/* Sub-tab 1: المديونيات والسلف والتكاليف */}
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

            <div className="flex items-center gap-2">
              <span className="text-xs text-[var(--muted)] font-medium">الفئة:</span>
              <select
                value={selectedCategory}
                onChange={(e) => setSelectedCategory(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-lg px-2.5 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل الفئات</option>
                <option value="advance">سلف نقدية</option>
                <option value="traffic_fine">مخالفات مرورية</option>
                <option value="equipment">عهد ومعدات</option>
                <option value="hr_cost">تكاليف موارد بشرية</option>
                <option value="loan">قروض وتسهيلات</option>
              </select>

              <span className="text-xs text-[var(--muted)] font-medium">الحالة:</span>
              <select
                value={selectedStatus}
                onChange={(e) => setSelectedStatus(e.target.value)}
                className="bg-[var(--surface)] border border-[var(--line)] text-xs rounded-lg px-2.5 py-1.5 focus:outline-hidden"
              >
                <option value="all">كل الحالات</option>
                <option value="active">قائمة (نشطة)</option>
                <option value="settled">مسددة بالكامل</option>
                <option value="waived">معفاة / متنازل عنها</option>
              </select>
            </div>
          </div>

          {loading ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : filteredDebts.length === 0 ? (
            <EmptyState
              title="لا توجد مديونيات مسجلة"
              description="لم يتم العثور على أي مديونيات أو سلف تطابق شروط الفلترة الحالية."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم الإقامة</th>
                      <th className="p-3 text-start">اسم المندوب</th>
                      <th className="p-3 text-start">فئة البند</th>
                      <th className="p-3 text-start">المبلغ الأصلي</th>
                      <th className="p-3 text-start">المبلغ المتبقي</th>
                      <th className="p-3 text-start">القسط الشهري</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {filteredDebts.map((d) => (
                      <tr key={d.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--accent)]">{d.iqama}</td>
                        <td className="p-3 font-semibold">{d.riderName || 'مندوب مسجل'}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)] text-[var(--ink)]">
                            {d.category === 'advance'
                              ? 'سلفة نقدية'
                              : d.category === 'traffic_fine'
                              ? 'مخالفة مرورية'
                              : d.category === 'equipment'
                              ? 'عهدة ومعدات'
                              : d.category === 'hr_cost'
                              ? 'تكلفة HR'
                              : d.category}
                          </span>
                        </td>
                        <td className="p-3 num text-[var(--muted)]">
                          {d.originalAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-3 num font-bold text-sm text-[var(--danger)]">
                          {d.remainingAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-3 num text-[var(--muted)]">
                          {d.monthlyDeduction ? `${d.monthlyDeduction.toLocaleString('en-US')} ر.س` : '-'}
                        </td>
                        <td className="p-3">
                          <span
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                              d.status === 'settled'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : d.status === 'active'
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : 'bg-[var(--raised)] text-[var(--muted)]'
                            }`}
                          >
                            {d.status === 'settled' ? 'مسددة' : d.status === 'active' ? 'قائمة' : 'معفاة'}
                          </span>
                        </td>
                        <td className="p-3">
                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              onClick={() => {
                                setStatementIqama(d.iqama);
                                setActiveTab('statements');
                                handleFetchStatement();
                              }}
                              className="px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)] text-xs font-semibold transition"
                            >
                              كشف الحساب
                            </button>
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedDebt(d);
                                setDebtDrawerOpen(true);
                              }}
                              className="px-2.5 py-1 rounded-lg bg-[var(--raised)] hover:bg-[var(--row-hover)] text-xs font-semibold transition"
                            >
                              التفاصيل
                            </button>
                          </div>
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

      {/* Sub-tab 2: كشوف حساب المناديب */}
      {activeTab === 'statements' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] space-y-3">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              استعلام واستخراج كشف حساب المندوب (المعتمد 360 والمبسط)
            </h3>
            <p className="text-xs text-[var(--muted)]">
              يطابق تماماً نتائج المحرك المالي والشاشات السابقة برقم الإقامة وفترة البحث.
            </p>

            <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 pt-2">
              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--muted)]">رقم الإقامة:</label>
                <input
                  type="text"
                  value={statementIqama}
                  onChange={(e) => setStatementIqama(e.target.value)}
                  placeholder="أدخل 10 أرقام..."
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs num font-mono focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--muted)]">من تاريخ:</label>
                <input
                  type="date"
                  value={statementFrom}
                  onChange={(e) => setStatementFrom(e.target.value)}
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="text-xs font-bold text-[var(--muted)]">إلى تاريخ:</label>
                <input
                  type="date"
                  value={statementTo}
                  onChange={(e) => setStatementTo(e.target.value)}
                  className="w-full p-2 rounded-xl border border-[var(--line)] text-xs num focus:outline-hidden"
                />
              </div>

              <div className="flex items-end gap-2">
                <button
                  type="button"
                  disabled={loadingStatement}
                  onClick={handleFetchStatement}
                  className="w-full py-2.5 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 disabled:opacity-50 transition"
                >
                  {loadingStatement ? 'جاري التحميل...' : 'استعراض كشف الحساب'}
                </button>
              </div>
            </div>

            <div className="flex items-center gap-2 pt-2">
              <span className="text-xs text-[var(--muted)] font-medium">نوع الكشف:</span>
              <button
                type="button"
                onClick={() => setStatementMode('detailed')}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition ${
                  statementMode === 'detailed' ? 'bg-[var(--accent)] text-[var(--accent-ink)]' : 'bg-[var(--raised)] text-[var(--muted)]'
                }`}
              >
                كشف تفصيلي (360)
              </button>
              <button
                type="button"
                onClick={() => setStatementMode('simple')}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition ${
                  statementMode === 'simple' ? 'bg-[var(--accent)] text-[var(--accent-ink)]' : 'bg-[var(--raised)] text-[var(--muted)]'
                }`}
              >
                كشف مبسط للمندوب
              </button>
            </div>
          </div>

          {loadingStatement ? (
            <div className="p-8 space-y-3 bg-[var(--surface)] rounded-2xl border border-[var(--line)]">
              <Skeleton className="h-10 w-full" />
              <Skeleton className="h-10 w-full" />
            </div>
          ) : !statementData ? (
            <EmptyState
              title="أدخل رقم الإقامة واستعرض كشف الحساب"
              description="سيتم عرض الرصيد الافتتاحي والختامي وكافة حركات المدين والدائن المعتمدة."
            />
          ) : (
            <div className="space-y-4">
              {/* Balance Summary Header */}
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)]">الرصيد الافتتاحي (Opening)</div>
                  <div className="text-lg font-bold num mt-1">
                    {statementData.openingBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                  </div>
                </div>
                <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)]">إجمالي الحركات في الفترة</div>
                  <div className="text-lg font-bold num mt-1 text-[var(--ink)]">
                    {statementData.entries.length} حركات
                  </div>
                </div>
                <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--surface)]">
                  <div className="text-xs text-[var(--muted)]">الرصيد الختامي المستحق (Closing)</div>
                  <div className="text-xl font-bold num mt-1 text-[var(--accent)]">
                    {statementData.closingBalance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                  </div>
                </div>
              </div>

              {/* Entries Table */}
              <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
                <div className="overflow-x-auto">
                  <table className="w-full text-start text-xs sm:text-sm">
                    <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                      <tr>
                        <th className="p-3 text-start">التاريخ</th>
                        <th className="p-3 text-start">البيان والشرح</th>
                        <th className="p-3 text-start">مدين (Debit)</th>
                        <th className="p-3 text-start">دائن (Credit)</th>
                        <th className="p-3 text-start">الرصيد التراكمي</th>
                        <th className="p-3 text-start">المرجع</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-[var(--line)]">
                      {statementData.entries.map((ent, i) => (
                        <tr key={i} className="hover:bg-[var(--row-hover)] transition">
                          <td className="p-3 num text-xs">{ent.date}</td>
                          <td className="p-3 font-medium">{ent.description}</td>
                          <td className="p-3 num font-bold text-[var(--danger)]">
                            {ent.debit > 0 ? `${ent.debit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '-'}
                          </td>
                          <td className="p-3 num font-bold text-[var(--money-pos)]">
                            {ent.credit > 0 ? `${ent.credit.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '-'}
                          </td>
                          <td className="p-3 num font-bold text-sm text-[var(--ink)]">
                            {ent.balance.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                          </td>
                          <td className="p-3 font-mono text-xs text-[var(--muted)]">{ent.reference || '-'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Sub-tab 3: منظومة الاعتمادات */}
      {activeTab === 'approvals' && (
        <div className="space-y-4">
          <div className="p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
            <h3 className="font-bold text-sm sm:text-base text-[var(--ink)]">
              سجل طلبات الاعتماد المتبادلة (المالية ⇄ HR ⇄ التشغيل)
            </h3>
            <p className="text-xs text-[var(--muted)] mt-0.5">
              اعتماد السلف، تسوية المديونيات، استقطاعات التشغيل، وتعديل بيانات المناديب وفق الصلاحيات المحددة.
            </p>
          </div>

          {approvals.length === 0 ? (
            <EmptyState
              title="لا توجد طلبات اعتماد معلقة حالياً"
              description="تظهر الطلبات هنا عندما تتطلب إجراءات التشغيل أو الموارد البشرية توقيعاً مالياً معتمداً."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">القسم الطالب</th>
                      <th className="p-3 text-start">القسم الموجه له</th>
                      <th className="p-3 text-start">نوع الطلب</th>
                      <th className="p-3 text-start">المبلغ</th>
                      <th className="p-3 text-start">المندوب</th>
                      <th className="p-3 text-start">السبب</th>
                      <th className="p-3 text-start">الحالة</th>
                      <th className="p-3 text-start">الإجراءات</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {approvals.map((req) => (
                      <tr key={req.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-bold">{req.fromDepartment}</td>
                        <td className="p-3">{req.targetDepartment}</td>
                        <td className="p-3">
                          <span className="px-2 py-0.5 rounded-full text-xs font-semibold bg-[var(--raised)]">
                            {req.requestTypeAr || req.requestType}
                          </span>
                        </td>
                        <td className="p-3 num font-bold text-[var(--ink)]">
                          {req.amount ? `${req.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س` : '-'}
                        </td>
                        <td className="p-3 font-mono text-xs">{req.iqama || '-'}</td>
                        <td className="p-3 max-w-xs truncate">{req.reason}</td>
                        <td className="p-3">
                          <span
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                              req.status === 'approved'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : req.status === 'rejected'
                                ? 'bg-[var(--danger)]/15 text-[var(--danger)]'
                                : req.status === 'suspended'
                                ? 'bg-[var(--warning)]/15 text-[var(--warning)]'
                                : 'bg-[var(--accent)]/15 text-[var(--accent)]'
                            }`}
                          >
                            {req.status === 'approved'
                              ? 'معتمد'
                              : req.status === 'rejected'
                              ? 'مرفوض'
                              : req.status === 'suspended'
                              ? 'معلّق'
                              : 'بانتظار القرار'}
                          </span>
                        </td>
                        <td className="p-3">
                          {req.status === 'pending' || req.status === 'suspended' ? (
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedApproval(req);
                                setApprovalDecision('approved');
                              }}
                              className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition"
                            >
                              البت في الطلب
                            </button>
                          ) : (
                            <span className="text-xs text-[var(--muted)]">تم الإجراء</span>
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

      {/* Sub-tab 4: أرشيف سندات الأمر */}
      {activeTab === 'promissory' && (
        <div className="space-y-4">
          {!promissoryFeatureEnabled && (
            <div className="flex items-center gap-3 p-4 rounded-2xl border border-[var(--warning)]/40 bg-[var(--warning)]/10 text-[var(--warning)]">
              <AlertTriangle className="shrink-0" size={20} />
              <div className="text-xs sm:text-sm font-bold">
                موديول أرشيف سندات الأمر التنفيذية في وضع التعليق المؤقت (Dormant Mode) لحين اعتماد التفعيل. السجلات الحالية متاحة للعرض والتحقق فقط.
              </div>
            </div>
          )}

          {promissoryNotes.length === 0 ? (
            <EmptyState
              title="لا توجد سندات أمر مؤرشفة"
              description="سندات الأمر التنفيذية المسجلة ضد المناديب للضمان المالي والعهد."
            />
          ) : (
            <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
              <div className="overflow-x-auto">
                <table className="w-full text-start text-xs sm:text-sm">
                  <thead className="border-b border-[var(--line)] bg-[var(--raised)]/50 text-[var(--muted)] font-semibold">
                    <tr>
                      <th className="p-3 text-start">رقم السند</th>
                      <th className="p-3 text-start">رقم الإقامة</th>
                      <th className="p-3 text-start">المستفيد</th>
                      <th className="p-3 text-start">المبلغ الإجمالي</th>
                      <th className="p-3 text-start">تاريخ الإصدار</th>
                      <th className="p-3 text-start">الحالة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {promissoryNotes.map((pn) => (
                      <tr key={pn.id} className="hover:bg-[var(--row-hover)] transition">
                        <td className="p-3 font-mono font-bold text-[var(--accent)]">{pn.noteNumber}</td>
                        <td className="p-3 font-mono">{pn.iqama}</td>
                        <td className="p-3 font-semibold">{pn.beneficiarySponsorId}</td>
                        <td className="p-3 num font-bold text-[var(--ink)]">
                          {pn.amount.toLocaleString('en-US', { minimumFractionDigits: 2 })} ر.س
                        </td>
                        <td className="p-3 num text-xs">{pn.issueDate}</td>
                        <td className="p-3">
                          <span
                            className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                              pn.status === 'settled'
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : pn.status === 'active'
                                ? 'bg-[var(--accent)]/15 text-[var(--accent)]'
                                : 'bg-[var(--danger)]/15 text-[var(--danger)]'
                            }`}
                          >
                            {pn.status === 'settled' ? 'مسدد' : pn.status === 'active' ? 'نافذ وسارٍ' : 'ملغي'}
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

      {/* Decision Dialog for Approval */}
      <Dialog
        open={Boolean(selectedApproval)}
        onClose={() => setSelectedApproval(null)}
        title="البت في طلب الاعتماد"
        description="تسجيل قرار المسؤول المالي أو مدير القسم؛ يتم تسجيل الإجراء في سجل التدقيق المحاسبي."
      >
        {selectedApproval && (
          <div className="space-y-4">
            <div className="p-3 rounded-xl bg-[var(--raised)] text-xs space-y-1">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">نوع الطلب:</span>
                <span className="font-bold">{selectedApproval.requestTypeAr || selectedApproval.requestType}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">القسم الطالب:</span>
                <span className="font-bold">{selectedApproval.fromDepartment}</span>
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
                  className={`py-2 rounded-xl border text-xs font-bold transition ${
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
                  className={`py-2 rounded-xl border text-xs font-bold transition ${
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
                  className={`py-2 rounded-xl border text-xs font-bold transition ${
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

            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setSelectedApproval(null)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold"
              >
                إلغاء
              </button>
              <button
                type="button"
                disabled={submittingDecision}
                onClick={handleDecideApproval}
                className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90"
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
        subtitle={`فئة البند: ${selectedDebt?.category || ''}`}
      >
        {selectedDebt && (
          <div className="space-y-4">
            <div className="p-4 rounded-xl bg-[var(--raised)] space-y-2 text-xs">
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المبلغ الأصلي:</span>
                <span className="font-bold num">{selectedDebt.originalAmount.toLocaleString('en-US')} ر.س</span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--muted)]">المتبقي:</span>
                <span className="font-bold num text-[var(--danger)]">
                  {selectedDebt.remainingAmount.toLocaleString('en-US')} ر.س
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
          </div>
        )}
      </Drawer>
    </div>
  );
}
