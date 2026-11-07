'use client';

import React, { useState, useEffect, useMemo } from 'react';
import {
  FileText,
  Plus,
  CheckCircle,
  AlertCircle,
  Clock,
  RotateCcw,
  Paperclip,
  Trash2,
  Check,
  Search,
  Building2,
  Eye,
  ArrowRightLeft,
  Calendar,
  AlertTriangle,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { Dialog } from '../../components/ui/Dialog';
import { Drawer } from '../../components/ui/Drawer';
import { EmptyState } from '../../components/ui/EmptyState';
import { ManualJournal, ManualJournalLine, CompanySponsor } from '../../lib/types';

interface ManualJournalsPanelProps {
  companies: CompanySponsor[];
  coaAccounts: any[];
  defaultOpenNew?: boolean;
}

interface NewLineState {
  accountCode: string;
  debit: string;
  credit: string;
  note: string;
}

export function ManualJournalsPanel({
  companies,
  coaAccounts,
  defaultOpenNew = false,
}: ManualJournalsPanelProps) {
  const { user, can, isAdmin } = useAuth();
  const { toast } = useToast();

  const canCreate = can('finance.journal.create') || isAdmin;
  const canApprove = can('finance.journal.approve') || isAdmin;

  // Active tab: draft (pending approval) vs approved
  const [statusTab, setStatusTab] = useState<'draft' | 'approved'>('draft');
  const [journals, setJournals] = useState<ManualJournal[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [selectedCompanyFilter, setSelectedCompanyFilter] = useState<string>('all');

  // Selected Journal for Details Drawer
  const [selectedJournal, setSelectedJournal] = useState<ManualJournal | null>(null);
  const [drawerOpen, setDrawerOpen] = useState<boolean>(false);

  // New Journal Dialog
  const [newModalOpen, setNewModalOpen] = useState<boolean>(defaultOpenNew);
  const [newCompanyId, setNewCompanyId] = useState<string>('');
  const [newDate, setNewDate] = useState<string>(() => new Date().toISOString().split('T')[0]);
  const [newReference, setNewReference] = useState<string>('');
  const [newDescription, setNewDescription] = useState<string>('');
  const [newOdooVoucher, setNewOdooVoucher] = useState<string>('');
  const [newAttachment, setNewAttachment] = useState<{ fileName: string; dataBase64: string } | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);

  // New Lines
  const [lines, setLines] = useState<NewLineState[]>([
    { accountCode: '', debit: '', credit: '', note: '' },
    { accountCode: '', debit: '', credit: '', note: '' },
  ]);

  // Reversal Dialog
  const [reverseModalOpen, setReverseModalOpen] = useState<boolean>(false);
  const [journalToReverse, setJournalToReverse] = useState<ManualJournal | null>(null);
  const [reverseReason, setReverseReason] = useState<string>('');
  const [reverseDate, setReverseDate] = useState<string>(() => new Date().toISOString().split('T')[0]);
  const [submittingReverse, setSubmittingReverse] = useState<boolean>(false);

  // Posting accounts list for dropdown
  const postingAccounts = useMemo(() => {
    return (coaAccounts || []).filter(
      (a) => (a.posting ?? a.isPosting ?? true) && (a.status === 'active' || a.isActive !== false)
    );
  }, [coaAccounts]);

  // Account map for quick lookup
  const accountMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of coaAccounts || []) {
      map.set(a.code, a.nameAr || a.name || a.code);
    }
    return map;
  }, [coaAccounts]);

  // Load manual journals
  const loadJournals = async () => {
    setLoading(true);
    try {
      const res = await api.getManualJournals({ status: statusTab });
      const items = Array.isArray(res) ? res : res?.items || [];
      setJournals(items);
    } catch {
      // Backend may not be mounted or database may be empty
      setJournals([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadJournals();
  }, [statusTab]);

  useEffect(() => {
    if (defaultOpenNew) {
      setNewModalOpen(true);
    }
  }, [defaultOpenNew]);

  // Calculate live debit/credit totals
  const { totalDebit, totalCredit, diff, isBalanced } = useMemo(() => {
    let dSum = 0;
    let cSum = 0;
    for (const l of lines) {
      const d = parseFloat(l.debit) || 0;
      const c = parseFloat(l.credit) || 0;
      dSum += d;
      cSum += c;
    }
    const dFixed = Math.round(dSum * 100) / 100;
    const cFixed = Math.round(cSum * 100) / 100;
    const difference = Math.round(Math.abs(dFixed - cFixed) * 100) / 100;
    const balanced = difference === 0 && dFixed > 0;
    return { totalDebit: dFixed, totalCredit: cFixed, diff: difference, isBalanced: balanced };
  }, [lines]);

  const handleLineChange = (index: number, field: keyof NewLineState, value: string) => {
    setLines((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], [field]: value };
      return next;
    });
  };

  const handleAddLine = () => {
    setLines((prev) => [...prev, { accountCode: '', debit: '', credit: '', note: '' }]);
  };

  const handleRemoveLine = (index: number) => {
    if (lines.length <= 2) {
      toast('يجب أن يحتوي القيد على سطرين على الأقل', 'warning');
      return;
    }
    setLines((prev) => prev.filter((_, i) => i !== index));
  };

  const handleAutoBalance = () => {
    if (diff === 0) return;
    const lastIdx = lines.length - 1;
    if (totalDebit > totalCredit) {
      const neededCredit = (parseFloat(lines[lastIdx].credit) || 0) + diff;
      handleLineChange(lastIdx, 'credit', neededCredit.toFixed(2));
    } else {
      const neededDebit = (parseFloat(lines[lastIdx].debit) || 0) + diff;
      handleLineChange(lastIdx, 'debit', neededDebit.toFixed(2));
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      toast('حجم المرفق يتجاوز الحد المسموح (2 ميجابايت)', 'danger');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const base64 = (reader.result as string).split(',')[1] || '';
      setNewAttachment({ fileName: file.name, dataBase64: base64 });
      toast('تم تحميل المرفق بنجاح', 'success');
    };
    reader.readAsDataURL(file);
  };

  const handleCreateJournal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCompanyId) {
      toast('يرجى اختيار الشركة', 'warning');
      return;
    }
    if (!newDate) {
      toast('يرجى اختيار تاريخ القيد', 'warning');
      return;
    }
    if (!newDescription.trim()) {
      toast('يرجى كتابة البيان العام للقيد', 'warning');
      return;
    }
    if (!isBalanced) {
      toast(`القيد غير متزن! الفرق الحالي: ${diff.toFixed(2)} ر.س`, 'danger');
      return;
    }
    for (let i = 0; i < lines.length; i++) {
      const l = lines[i];
      if (!l.accountCode) {
        toast(`يرجى تحديد الحساب في السطر رقم ${i + 1}`, 'warning');
        return;
      }
      const d = parseFloat(l.debit) || 0;
      const c = parseFloat(l.credit) || 0;
      if (d === 0 && c === 0) {
        toast(`يجب تحديد مبلغ مدين أو دائن في السطر رقم ${i + 1}`, 'warning');
        return;
      }
      if (d > 0 && c > 0) {
        toast(`لا يجوز أن يكون السطر رقم ${i + 1} مديناً ودائناً في نفس الوقت`, 'warning');
        return;
      }
    }

    setSubmitting(true);
    try {
      await api.createManualJournal({
        companyId: newCompanyId,
        date: newDate,
        reference: newReference.trim(),
        description: newDescription.trim(),
        odooVoucherNo: newOdooVoucher.trim() || undefined,
        attachment: newAttachment || undefined,
        lines: lines.map((l) => ({
          accountCode: l.accountCode,
          debit: parseFloat(l.debit) || 0,
          credit: parseFloat(l.credit) || 0,
          note: l.note.trim() || undefined,
        })),
      });

      toast('تم إنشاء مسودة القيد وإرسالها للاعتماد بنجاح', 'success');
      setNewModalOpen(false);
      // Reset form
      setNewReference('');
      setNewDescription('');
      setNewOdooVoucher('');
      setNewAttachment(null);
      setLines([
        { accountCode: '', debit: '', credit: '', note: '' },
        { accountCode: '', debit: '', credit: '', note: '' },
      ]);
      setStatusTab('draft');
      loadJournals();
    } catch (err: any) {
      toast(err.message || 'فشل إنشاء القيد المحاسبي', 'danger');
    } finally {
      setSubmitting(false);
    }
  };

  const handleApproveJournal = async (journal: ManualJournal) => {
    if (journal.maker === user?.username) {
      toast('لا يجوز لمنشئ القيد اعتماده (مبدأ الرقابة الثنائية Maker-Checker)', 'danger');
      return;
    }
    try {
      await api.approveManualJournal(journal.id);
      toast('تم اعتماد القيد وترحيله بنجاح إلى السجلات المالية', 'success');
      if (drawerOpen) setDrawerOpen(false);
      loadJournals();
    } catch (err: any) {
      toast(err.message || 'فشل اعتماد القيد المحاسبي', 'danger');
    }
  };

  const handleOpenReverse = (journal: ManualJournal) => {
    setJournalToReverse(journal);
    setReverseReason('');
    setReverseDate(new Date().toISOString().split('T')[0]);
    setReverseModalOpen(true);
  };

  const handleConfirmReverse = async () => {
    if (!journalToReverse || !reverseReason.trim()) {
      toast('يرجى ذكر سبب عكس القيد بشكل واضح (إلزامي)', 'warning');
      return;
    }
    setSubmittingReverse(true);
    try {
      await api.reverseManualJournal(journalToReverse.id, {
        reason: reverseReason.trim(),
        date: reverseDate,
      });
      toast('تم إنشاء مسودة القيد العكسي بنجاح، بانتظار اعتماد مستخدم ثانٍ', 'success');
      setReverseModalOpen(false);
      if (drawerOpen) setDrawerOpen(false);
      setStatusTab('draft');
      loadJournals();
    } catch (err: any) {
      toast(err.message || 'فشل إنشاء قيد العكس', 'danger');
    } finally {
      setSubmittingReverse(false);
    }
  };

  // Filter journals
  const filteredJournals = useMemo(() => {
    return journals.filter((j) => {
      if (selectedCompanyFilter !== 'all' && j.companyId !== selectedCompanyFilter) return false;
      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase().trim();
      return (
        j.id.toLowerCase().includes(q) ||
        (j.description || '').toLowerCase().includes(q) ||
        (j.reference || '').toLowerCase().includes(q) ||
        (j.maker || '').toLowerCase().includes(q)
      );
    });
  }, [journals, selectedCompanyFilter, searchQuery]);

  return (
    <div className="space-y-6">
      {/* Header & Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 rounded-2xl bg-[var(--surface)] border border-[var(--line)] shadow-xs">
        <div>
          <div className="flex items-center gap-2">
            <div className="p-2 rounded-xl bg-[var(--accent)]/10 text-[var(--accent)]">
              <FileText size={20} />
            </div>
            <div>
              <h2 className="text-base font-bold text-[var(--ink)]">القيود المحاسبية اليدوية (BR §28)</h2>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                إنشاء واعتماد القيود بنظام التدقيق الثنائي (Maker-Checker) مع موازنة فورية للدائن والمدين ومرفقات ثبوتية.
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          {canCreate && (
            <button
              type="button"
              id="btn-new-manual-journal"
              onClick={() => setNewModalOpen(true)}
              className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs sm:text-sm font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer shadow-xs"
            >
              <Plus size={16} />
              <span>إنشاء قيد يدوي جديد</span>
            </button>
          )}
        </div>
      </div>

      {/* Tabs and Filters */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-[var(--line)] pb-2">
        <div className="flex items-center gap-2">
          <button
            type="button"
            id="tab-manual-drafts"
            onClick={() => setStatusTab('draft')}
            className={`px-3 py-1.5 rounded-xl text-xs sm:text-sm font-bold transition cursor-pointer flex items-center gap-2 ${
              statusTab === 'draft'
                ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                : 'text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]'
            }`}
          >
            <Clock size={15} />
            <span>قيود بانتظار الاعتماد (مسودات)</span>
          </button>

          <button
            type="button"
            id="tab-manual-approved"
            onClick={() => setStatusTab('approved')}
            className={`px-3 py-1.5 rounded-xl text-xs sm:text-sm font-bold transition cursor-pointer flex items-center gap-2 ${
              statusTab === 'approved'
                ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                : 'text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]'
            }`}
          >
            <CheckCircle size={15} />
            <span>القيود المعتمدة</span>
          </button>
        </div>

        <div className="flex items-center gap-2">
          <div className="relative">
            <Search size={14} className="absolute start-2.5 top-1/2 -translate-y-1/2 text-[var(--muted)]" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="بحث بالبيان، المرجع، المعرف..."
              className="ps-8 pe-3 py-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden w-48 sm:w-60"
            />
          </div>

          <select
            value={selectedCompanyFilter}
            onChange={(e) => setSelectedCompanyFilter(e.target.value)}
            className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold focus:outline-hidden"
          >
            <option value="all">كل الشركات والكفلاء</option>
            {companies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nameAr}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Main Table */}
      {loading ? (
        <div className="p-12 text-center text-xs text-[var(--muted)] animate-pulse">
          جاري تحميل القيود المحاسبية...
        </div>
      ) : filteredJournals.length === 0 ? (
        <EmptyState
          icon={<FileText size={40} className="text-[var(--muted)]" />}
          title={statusTab === 'draft' ? 'لا توجد قيود بانتظار الاعتماد' : 'لا توجد قيود معتمدة حتى الآن'}
          description={
            statusTab === 'draft'
              ? 'كافة القيود اليدوية المنشأة تم اعتمادها أو لم يتم إنشاء مسودات جديدة.'
              : 'لم يتم اعتماد أي قيود يدوية بعد.'
          }
        />
      ) : (
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-hidden shadow-xs">
          <div className="overflow-x-auto">
            <table className="w-full text-start text-xs sm:text-sm">
              <thead className="border-b border-[var(--line)] bg-[var(--raised)]/60 text-[var(--muted)] font-semibold">
                <tr>
                  <th className="p-3 text-start">معرف القيد</th>
                  <th className="p-3 text-start">الشركة</th>
                  <th className="p-3 text-start">التاريخ</th>
                  <th className="p-3 text-start">المرجع</th>
                  <th className="p-3 text-start">البيان العام</th>
                  <th className="p-3 text-start">المبلغ (ر.س)</th>
                  <th className="p-3 text-start">المنشئ</th>
                  <th className="p-3 text-start">الحالة</th>
                  <th className="p-3 text-start">مرفق</th>
                  <th className="p-3 text-start">الإجراءات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[var(--line)]">
                {filteredJournals.map((j) => {
                  const companyObj = companies.find((c) => c.id === j.companyId);
                  const total = j.totalDebit ?? (j.lines || []).reduce((sum, l) => sum + (l.debit || 0), 0);
                  const isMaker = j.maker === user?.username;

                  return (
                    <tr key={j.id} className="hover:bg-[var(--row-hover)] transition">
                      <td className="p-3 font-mono text-[11px] text-[var(--muted)]">{j.id}</td>
                      <td className="p-3 font-semibold text-[var(--ink)]">
                        {companyObj?.nameAr || j.companyName || j.companyId}
                      </td>
                      <td className="p-3 num font-medium text-[var(--ink)]">{j.date}</td>
                      <td className="p-3 font-mono text-xs text-[var(--muted)]">{j.reference || '—'}</td>
                      <td className="p-3 text-xs text-[var(--ink)] max-w-xs truncate">{j.description}</td>
                      <td className="p-3 num font-bold text-sm text-[var(--ink)]">
                        {total.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                      <td className="p-3 font-mono text-xs text-[var(--muted)]">{j.maker}</td>
                      <td className="p-3">
                        {j.status === 'approved' ? (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-[var(--badge-success-bg)] text-[var(--badge-success-text)]">
                            <CheckCircle size={12} />
                            <span>معتمد</span>
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-bold bg-[var(--badge-warning-bg)] text-[var(--badge-warning-text)]">
                            <Clock size={12} />
                            <span>مسودة (بانتظار الاعتماد)</span>
                          </span>
                        )}
                      </td>
                      <td className="p-3">
                        {j.hasAttachment || j.attachmentName ? (
                          <span className="text-[var(--accent)] inline-flex items-center" title={j.attachmentName || 'مرفق ثبوتي'}>
                            <Paperclip size={14} />
                          </span>
                        ) : (
                          <span className="text-[var(--muted)]">—</span>
                        )}
                      </td>
                      <td className="p-3">
                        <div className="flex items-center gap-2">
                          <button
                            type="button"
                            onClick={() => {
                              setSelectedJournal(j);
                              setDrawerOpen(true);
                            }}
                            className="px-2.5 py-1 rounded-lg border border-[var(--line)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--raised)] transition inline-flex items-center gap-1 cursor-pointer"
                          >
                            <Eye size={13} />
                            <span>تفاصيل</span>
                          </button>

                          {statusTab === 'draft' && canApprove && (
                            <button
                              type="button"
                              disabled={isMaker}
                              onClick={() => handleApproveJournal(j)}
                              title={isMaker ? 'لا يجوز لمنشئ القيد اعتماده (Maker-Checker)' : 'اعتماد القيد المحاسبي'}
                              className="px-2.5 py-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
                            >
                              <Check size={13} />
                              <span>اعتماد</span>
                            </button>
                          )}

                          {statusTab === 'approved' && canApprove && (
                            <button
                              type="button"
                              onClick={() => handleOpenReverse(j)}
                              title="عكس القيد المحاسبي وإنشاء مسودة عكسية"
                              className="px-2.5 py-1 rounded-lg border border-[var(--line)] text-[var(--muted)] hover:text-[var(--danger)] text-xs font-bold hover:bg-[var(--raised)] transition inline-flex items-center gap-1 cursor-pointer"
                            >
                              <RotateCcw size={13} />
                              <span>عكس</span>
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

      {/* DRAWER: Journal Details */}
      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title={`تفاصيل القيد المحاسبي: ${selectedJournal?.id || ''}`}
        subtitle={`تاريخ القيد: ${selectedJournal?.date || ''} · المرجع: ${selectedJournal?.reference || '—'}`}
        width="wide"
      >
        {selectedJournal && (
          <div className="space-y-6">
            {/* Header info card */}
            <div className="p-4 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40 space-y-3">
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 text-xs">
                <div>
                  <span className="text-[var(--muted)] block">الشركة / الكفيل:</span>
                  <span className="font-bold text-[var(--ink)]">
                    {companies.find((c) => c.id === selectedJournal.companyId)?.nameAr || selectedJournal.companyId}
                  </span>
                </div>
                <div>
                  <span className="text-[var(--muted)] block">تاريخ القيد:</span>
                  <span className="font-bold num text-[var(--ink)]">{selectedJournal.date}</span>
                </div>
                <div>
                  <span className="text-[var(--muted)] block">المرجع:</span>
                  <span className="font-mono font-bold text-[var(--ink)]">{selectedJournal.reference || '—'}</span>
                </div>
                <div>
                  <span className="text-[var(--muted)] block">منشئ القيد (Maker):</span>
                  <span className="font-mono font-bold text-[var(--ink)]">{selectedJournal.maker}</span>
                </div>
              </div>

              <div className="pt-2 border-t border-[var(--line)]">
                <span className="text-xs text-[var(--muted)] block">البيان العام:</span>
                <p className="text-xs font-semibold text-[var(--ink)] mt-0.5">{selectedJournal.description}</p>
              </div>

              {selectedJournal.approvedBy && (
                <div className="pt-2 border-t border-[var(--line)] flex items-center justify-between text-xs">
                  <span className="text-[var(--muted)]">
                    تم الاعتماد بواسطة: <strong className="font-mono text-[var(--ink)]">{selectedJournal.approvedBy}</strong>
                  </span>
                  {selectedJournal.approvedAt && (
                    <span className="text-[var(--muted)] num">{selectedJournal.approvedAt.split('T')[0]}</span>
                  )}
                </div>
              )}
            </div>

            {/* Lines table */}
            <div className="space-y-2">
              <h4 className="text-xs font-bold text-[var(--ink)]">أسطر القيد وتوزيع المبالغ:</h4>
              <div className="rounded-xl border border-[var(--line)] overflow-hidden">
                <table className="w-full text-xs">
                  <thead className="bg-[var(--raised)] text-[var(--muted)] border-b border-[var(--line)]">
                    <tr>
                      <th className="p-2.5 text-start w-12">#</th>
                      <th className="p-2.5 text-start">الحساب</th>
                      <th className="p-2.5 text-start w-32">مدين (ر.س)</th>
                      <th className="p-2.5 text-start w-32">دائن (ر.س)</th>
                      <th className="p-2.5 text-start">البيان / الملاحظة</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--line)]">
                    {(selectedJournal.lines || []).map((line, idx) => (
                      <tr key={idx} className="hover:bg-[var(--row-hover)]">
                        <td className="p-2.5 font-mono text-[var(--muted)]">{idx + 1}</td>
                        <td className="p-2.5">
                          <span className="font-mono font-bold text-[var(--accent)]">{line.accountCode}</span>
                          <span className="ms-2 text-[var(--ink)]">
                            {line.accountNameAr || accountMap.get(line.accountCode) || ''}
                          </span>
                        </td>
                        <td className="p-2.5 num font-bold text-[var(--ink)]">
                          {line.debit ? line.debit.toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                        </td>
                        <td className="p-2.5 num font-bold text-[var(--ink)]">
                          {line.credit ? line.credit.toLocaleString('en-US', { minimumFractionDigits: 2 }) : '—'}
                        </td>
                        <td className="p-2.5 text-[var(--muted)]">{line.note || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="bg-[var(--raised)]/80 border-t border-[var(--line)] font-bold">
                    <tr>
                      <td colSpan={2} className="p-2.5 text-start text-[var(--ink)]">
                        الإجمالي:
                      </td>
                      <td className="p-2.5 num text-[var(--ink)]">
                        {(selectedJournal.lines || [])
                          .reduce((sum, l) => sum + (l.debit || 0), 0)
                          .toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                      <td className="p-2.5 num text-[var(--ink)]">
                        {(selectedJournal.lines || [])
                          .reduce((sum, l) => sum + (l.credit || 0), 0)
                          .toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                      <td className="p-2.5 text-end text-[var(--badge-success-text)] font-semibold">
                        <span className="inline-flex items-center gap-1">
                          <CheckCircle size={13} />
                          <span>قيد متزن</span>
                        </span>
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </div>

            {/* Actions inside drawer */}
            <div className="pt-4 border-t border-[var(--line)] flex items-center justify-between">
              <button
                type="button"
                onClick={() => setDrawerOpen(false)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold text-[var(--muted)] hover:bg-[var(--raised)] transition cursor-pointer"
              >
                إغلاق
              </button>

              <div className="flex items-center gap-2">
                {selectedJournal.status === 'draft' && canApprove && (
                  <button
                    type="button"
                    disabled={selectedJournal.maker === user?.username}
                    onClick={() => handleApproveJournal(selectedJournal)}
                    className="px-4 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition inline-flex items-center gap-1.5 cursor-pointer disabled:opacity-40"
                  >
                    <Check size={15} />
                    <span>اعتماد القيد المحاسبي</span>
                  </button>
                )}

                {selectedJournal.status === 'approved' && canApprove && (
                  <button
                    type="button"
                    onClick={() => handleOpenReverse(selectedJournal)}
                    className="px-4 py-2 rounded-xl border border-[var(--line)] text-[var(--danger)] text-xs font-bold hover:bg-[var(--raised)] transition inline-flex items-center gap-1.5 cursor-pointer"
                  >
                    <RotateCcw size={15} />
                    <span>طلب عكس القيد</span>
                  </button>
                )}
              </div>
            </div>
          </div>
        )}
      </Drawer>

      {/* DIALOG: New Manual Journal */}
      <Dialog
        open={newModalOpen}
        onClose={() => setNewModalOpen(false)}
        title="إنشاء قيد محاسبي يدوي جديد (BR §28)"
        actions={
          <div className="flex items-center justify-between w-full">
            <div className="flex items-center gap-2">
              {isBalanced ? (
                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-xl text-xs font-bold bg-[var(--badge-success-bg)] text-[var(--badge-success-text)]">
                  <CheckCircle size={15} />
                  <span>متزن (الفرق: 0.00 ر.س)</span>
                </span>
              ) : (
                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-xl text-xs font-bold bg-[var(--badge-danger-bg)] text-[var(--badge-danger-text)]">
                  <AlertCircle size={15} />
                  <span>غير متزن (الفرق: {diff.toFixed(2)} ر.س)</span>
                </span>
              )}
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setNewModalOpen(false)}
                className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer hover:bg-[var(--raised)]"
              >
                إلغاء
              </button>
              <button
                type="submit"
                form="form-manual-journal"
                disabled={submitting || !isBalanced}
                className="px-5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold hover:opacity-90 transition cursor-pointer disabled:opacity-50"
              >
                {submitting ? 'جاري الحفظ...' : 'حفظ كمسودة وإرسال للاعتماد'}
              </button>
            </div>
          </div>
        }
      >
        <form id="form-manual-journal" onSubmit={handleCreateJournal} className="space-y-4 max-h-[70vh] overflow-y-auto p-1">
          {/* Header Fields */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 p-3 rounded-xl bg-[var(--raised)]/50 border border-[var(--line)]">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">الشركة / الكفيل: *</label>
              <select
                id="select-journal-company"
                value={newCompanyId}
                onChange={(e) => setNewCompanyId(e.target.value)}
                required
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold focus:outline-hidden"
              >
                <option value="">-- اختر الشركة --</option>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.nameAr}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">تاريخ القيد: *</label>
              <input
                type="date"
                id="input-journal-date"
                value={newDate}
                onChange={(e) => setNewDate(e.target.value)}
                required
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs num font-semibold focus:outline-hidden"
              />
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">رقم المرجع / السند:</label>
              <input
                type="text"
                id="input-journal-reference"
                value={newReference}
                onChange={(e) => setNewReference(e.target.value)}
                placeholder="مثال: REF-2026-10"
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">البيان العام للقيد: *</label>
            <textarea
              id="input-journal-description"
              rows={2}
              value={newDescription}
              onChange={(e) => setNewDescription(e.target.value)}
              placeholder="اكتب وصفاً محاسبياً وافياً للعملية..."
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">المرفق الثبوتي (PDF / صورة ≤ 2MB):</label>
              <input
                type="file"
                accept=".pdf,.png,.jpg,.jpeg"
                onChange={handleFileChange}
                className="w-full p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs text-[var(--muted)] file:me-2 file:py-1 file:px-2.5 file:rounded-lg file:border-0 file:text-xs file:font-bold file:bg-[var(--accent)] file:text-[var(--accent-ink)]"
              />
              {newAttachment && (
                <span className="text-[11px] text-[var(--accent)] block">✓ تم إرفاق: {newAttachment.fileName}</span>
              )}
            </div>

            <div className="space-y-1">
              <label className="text-xs font-bold text-[var(--ink)]">رقم قيد أودو الإضافي (اختياري):</label>
              <input
                type="text"
                value={newOdooVoucher}
                onChange={(e) => setNewOdooVoucher(e.target.value)}
                placeholder="مثال: MISC/2026/0014"
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono focus:outline-hidden"
              />
            </div>
          </div>

          {/* Lines Table */}
          <div className="space-y-2 pt-2 border-t border-[var(--line)]">
            <div className="flex items-center justify-between">
              <label className="text-xs font-bold text-[var(--ink)]">أسطر القيد (حد أدنى سطرين):</label>
              <div className="flex items-center gap-2">
                {diff > 0 && (
                  <button
                    type="button"
                    onClick={handleAutoBalance}
                    className="px-2.5 py-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[11px] font-bold text-[var(--accent)] hover:bg-[var(--raised)] cursor-pointer"
                  >
                    موازنة السطر الأخير تلقائياً ({diff.toFixed(2)} ر.س)
                  </button>
                )}
                <button
                  type="button"
                  id="btn-add-journal-line"
                  onClick={handleAddLine}
                  className="px-2.5 py-1 rounded-lg bg-[var(--raised)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center gap-1 cursor-pointer"
                >
                  <Plus size={13} />
                  <span>إضافة سطر</span>
                </button>
              </div>
            </div>

            <div className="rounded-xl border border-[var(--line)] overflow-hidden">
              <table className="w-full text-xs">
                <thead className="bg-[var(--raised)] text-[var(--muted)] border-b border-[var(--line)]">
                  <tr>
                    <th className="p-2 text-start w-8">#</th>
                    <th className="p-2 text-start">الحساب *</th>
                    <th className="p-2 text-start w-28">مدين (ر.س)</th>
                    <th className="p-2 text-start w-28">دائن (ر.س)</th>
                    <th className="p-2 text-start">ملاحظة السطر</th>
                    <th className="p-2 text-center w-10">حذف</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {lines.map((line, idx) => (
                    <tr key={idx} className="bg-[var(--surface)]">
                      <td className="p-2 font-mono text-[var(--muted)] text-center">{idx + 1}</td>
                      <td className="p-2">
                        <select
                          value={line.accountCode}
                          onChange={(e) => handleLineChange(idx, 'accountCode', e.target.value)}
                          required
                          className="w-full p-1.5 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs font-semibold focus:outline-hidden"
                        >
                          <option value="">-- اختر الحساب --</option>
                          {postingAccounts.map((a) => (
                            <option key={a.code} value={a.code}>
                              {a.code} - {a.nameAr || a.name}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td className="p-2">
                        <input
                          type="number"
                          step="0.01"
                          min="0"
                          value={line.debit}
                          onChange={(e) => handleLineChange(idx, 'debit', e.target.value)}
                          placeholder="0.00"
                          className="w-full p-1.5 rounded-lg border border-[var(--line)] text-xs num font-bold focus:outline-hidden"
                        />
                      </td>
                      <td className="p-2">
                        <input
                          type="number"
                          step="0.01"
                          min="0"
                          value={line.credit}
                          onChange={(e) => handleLineChange(idx, 'credit', e.target.value)}
                          placeholder="0.00"
                          className="w-full p-1.5 rounded-lg border border-[var(--line)] text-xs num font-bold focus:outline-hidden"
                        />
                      </td>
                      <td className="p-2">
                        <input
                          type="text"
                          value={line.note}
                          onChange={(e) => handleLineChange(idx, 'note', e.target.value)}
                          placeholder="بيان تفصيلي للسطر..."
                          className="w-full p-1.5 rounded-lg border border-[var(--line)] text-xs focus:outline-hidden"
                        />
                      </td>
                      <td className="p-2 text-center">
                        <button
                          type="button"
                          disabled={lines.length <= 2}
                          onClick={() => handleRemoveLine(idx)}
                          className="p-1 rounded-md text-[var(--muted)] hover:text-[var(--danger)] transition disabled:opacity-30 cursor-pointer"
                        >
                          <Trash2 size={13} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="bg-[var(--raised)]/80 border-t border-[var(--line)] font-bold">
                  <tr>
                    <td colSpan={2} className="p-2 text-start text-[var(--ink)]">
                      المجموع:
                    </td>
                    <td className="p-2 num text-[var(--ink)]">{totalDebit.toFixed(2)}</td>
                    <td className="p-2 num text-[var(--ink)]">{totalCredit.toFixed(2)}</td>
                    <td colSpan={2} className="p-2 text-end">
                      {isBalanced ? (
                        <span className="text-[var(--badge-success-text)] font-bold inline-flex items-center gap-1">
                          <CheckCircle size={13} />
                          <span>متزن</span>
                        </span>
                      ) : (
                        <span className="text-[var(--badge-danger-text)] font-bold inline-flex items-center gap-1">
                          <AlertCircle size={13} />
                          <span>الفرق: {diff.toFixed(2)}</span>
                        </span>
                      )}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        </form>
      </Dialog>

      {/* DIALOG: Reverse Journal */}
      <Dialog
        open={reverseModalOpen}
        onClose={() => setReverseModalOpen(false)}
        title="عكس قيد محاسبي معتمد (BR §28)"
        actions={
          <div className="flex items-center justify-end gap-2 w-full">
            <button
              type="button"
              onClick={() => setReverseModalOpen(false)}
              className="px-4 py-2 rounded-xl border border-[var(--line)] text-xs font-bold cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={submittingReverse || !reverseReason.trim()}
              onClick={handleConfirmReverse}
              className="px-4 py-2 rounded-xl bg-[var(--danger)] text-white text-xs font-bold hover:opacity-90 transition cursor-pointer disabled:opacity-50"
            >
              {submittingReverse ? 'جاري العكس...' : 'تأكيد إنشاء مسودة العكس'}
            </button>
          </div>
        }
      >
        <div className="space-y-4">
          <div className="p-3 rounded-xl bg-[var(--raised)] text-xs text-[var(--ink)] space-y-1">
            <div className="font-bold">مبدأ الرقابة الثنائية في التصحيح:</div>
            <p className="text-[var(--muted)]">
              ينشئ هذا الإجراء مسودة قيد عكسي مستقل في الفترة المفتوحة دون تعديل القيد الأصلي. يتطلب القيد العكسي اعتماد مستخدم ثانٍ مستقل قبل التأثير على الأرصدة.
            </p>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">تاريخ القيد العكسي: *</label>
            <input
              type="date"
              value={reverseDate}
              onChange={(e) => setReverseDate(e.target.value)}
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs num font-semibold focus:outline-hidden"
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-bold text-[var(--ink)]">سبب عكس القيد (إلزامي): *</label>
            <textarea
              rows={3}
              value={reverseReason}
              onChange={(e) => setReverseReason(e.target.value)}
              placeholder="اكتب سبب عكس القيد والخطأ المراد تصحيحه بالتفصيل..."
              required
              className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs focus:outline-hidden"
            />
          </div>
        </div>
      </Dialog>
    </div>
  );
}
