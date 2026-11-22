'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Plus, RefreshCw } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { PageHeader } from '../../components/ui/PageHeader';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { formatCurrency, formatSar } from '../../lib/money';
import { usePeriod } from '../../lib/use-period';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';

type PayeeRow = Record<string, unknown> & {
  id?: string;
  nameAr?: string;
  payeeType?: string;
  amount?: number;
  currencyCode?: string;
  paymentMethod?: string;
  status?: number;
  activeFrom?: string;
  activeTo?: string;
};

type RunRow = Record<string, unknown> & {
  id?: string;
  periodMonth?: string;
  status?: number;
  companyId?: string;
};

type RunLine = Record<string, unknown> & {
  id?: string;
  nameAr?: string;
  amount?: number;
  currencyCode?: string;
  exchangeRate?: number;
  transferFee?: number;
  fxDifference?: number;
  sarAmount?: number;
  skipped?: boolean;
  paymentId?: string;
};

type VoucherRow = Record<string, unknown> & {
  id?: string;
  amount?: number;
  status?: number;
  approvalRequestId?: string;
  allowedActions?: string[];
  documentId?: string;
};

/** org.cost_centre sentinel «بدون مركز تكلفة» (SchemaOrgCostCentre.NoneId). */
const NONE_COST_CENTRE_ID = '019c18d5-62e1-7000-8000-00000000cc00';

const inputClass = 'w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs';

const TYPE_LABEL: Record<string, string> = {
  trial_outside_sponsorship: 'تجربة خارج الكفالة',
  egypt_office: 'مكتب مصر',
  other: 'أخرى',
};

const RUN_STATUS: Record<number, string> = {
  1: 'مسودة',
  2: 'بانتظار الاعتماد',
  3: 'معتمد',
  4: 'تم التنفيذ',
  5: 'ملغى',
};

export function RecurringPayeesWorkspace({
  mode,
}: {
  mode: 'payees' | 'runs' | 'run-detail';
}) {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const { period } = usePeriod();
  const canView = can('payments.view') || isAdmin;
  const canDisburse = can('payments.disburse') || isAdmin;
  const canApprove = can('approvals.decide') || isAdmin;

  const [tab, setTab] = useState<'list' | 'form' | 'runs' | 'oneoff'>(
    mode === 'runs' || mode === 'run-detail' ? 'runs' : 'list'
  );
  const tabs: SectionTabItem[] = useMemo(
    () => [
      { id: 'list', label: 'المستفيدون' },
      { id: 'form', label: 'ملف مستفيد' },
      { id: 'runs', label: 'التشغيل الشهري' },
      { id: 'oneoff', label: 'دفعة لمرة' },
    ],
    []
  );

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [payees, setPayees] = useState<PayeeRow[]>([]);
  const [runs, setRuns] = useState<RunRow[]>([]);
  const [run, setRun] = useState<(RunRow & { lines?: RunLine[]; allowedActions?: string[] }) | null>(null);
  const [historyPayeeId, setHistoryPayeeId] = useState('');
  const [history, setHistory] = useState<Record<string, unknown>[]>([]);

  const [form, setForm] = useState({
    id: '',
    companyId: '',
    nameAr: '',
    payeeType: 'trial_outside_sponsorship',
    amount: '',
    currencyCode: 'SAR',
    paymentMethod: 'bank_transfer',
    bankDetails: '',
    bankDetailsTouched: false,
    clearBankDetails: false,
    costCentreId: NONE_COST_CENTRE_ID,
    activeFrom: `${period}-01`,
    activeTo: '',
    notes: '',
  });
  const [oneOff, setOneOff] = useState({
    companyId: '',
    nameAr: '',
    amount: '',
    currencyCode: 'SAR',
    exchangeRate: '1',
    transferFee: '0',
    fxDifference: '0',
    bankAccountCode: '111101',
    paidOn: new Date().toISOString().slice(0, 10),
    bankDetails: '',
    costCentreId: NONE_COST_CENTRE_ID,
    idempotencyKey: '',
  });
  const [executeForm, setExecuteForm] = useState({
    paidOn: new Date().toISOString().slice(0, 10),
    bankAccountCode: '111101',
  });
  const [vouchers, setVouchers] = useState<VoucherRow[]>([]);
  const [oneOffLine, setOneOffLine] = useState({
    nameAr: '',
    amount: '',
    currencyCode: 'SAR',
    exchangeRate: '1',
    transferFee: '0',
    fxDifference: '0',
    costCentreId: NONE_COST_CENTRE_ID,
  });

  const load = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setError(null);
    try {
      const [c, p, r] = await Promise.all([
        loadCompanyOptions(),
        api.listRecurringPayees(),
        api.listRecurringRuns(),
      ]);
      setCompanies(c);
      setPayees(p.items || []);
      setRuns(r.items || []);
      if (!form.companyId && c[0]) setForm((f) => ({ ...f, companyId: String(c[0].id || '') }));
      if (!oneOff.companyId && c[0]) setOneOff((f) => ({ ...f, companyId: String(c[0].id || '') }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'تعذر التحميل');
    } finally {
      setLoading(false);
    }
  }, [canView, form.companyId, oneOff.companyId]);

  useEffect(() => {
    void load();
  }, [load]);

  const openRun = async (id: string) => {
    setBusy(true);
    try {
      const detail = (await api.getRecurringRun(id)) as RunRow & { lines?: RunLine[]; allowedActions?: string[] };
      setRun(detail);
      const fromLines = (detail.lines || [])
        .map((l) => (l.paymentId ? ({ id: String(l.paymentId) } as VoucherRow) : null))
        .filter(Boolean) as VoucherRow[];
      if (fromLines.length) await refreshVouchers(fromLines);
      else setVouchers([]);
      setTab('runs');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر فتح التشغيل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const savePayee = async () => {
    setBusy(true);
    try {
      const body: Record<string, unknown> = {
        companyId: form.companyId,
        nameAr: form.nameAr,
        payeeType: form.payeeType,
        amount: Number(form.amount),
        currencyCode: form.currencyCode,
        paymentMethod: form.paymentMethod,
        costCentreId: form.costCentreId || NONE_COST_CENTRE_ID,
        activeFrom: form.activeFrom,
        activeTo: form.activeTo || null,
        notes: form.notes,
      };
      if (form.id) {
        if (form.clearBankDetails) body.clearBankDetails = true;
        else if (form.bankDetailsTouched) body.bankDetails = form.bankDetails;
      } else {
        body.bankDetails = form.bankDetails;
      }
      if (form.id) await api.updateRecurringPayee(form.id, body);
      else await api.createRecurringPayee(body);
      toast('تم الحفظ');
      setForm((f) => ({
        ...f,
        id: '',
        nameAr: '',
        amount: '',
        bankDetails: '',
        bankDetailsTouched: false,
        clearBankDetails: false,
        costCentreId: NONE_COST_CENTRE_ID,
        notes: '',
      }));
      setTab('list');
      await load();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الحفظ', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const refreshVouchers = async (rows: VoucherRow[]) => {
    const next: VoucherRow[] = [];
    for (const v of rows) {
      if (!v.id) continue;
      try {
        next.push((await api.getPaymentVoucher(v.id)) as VoucherRow);
      } catch {
        next.push(v);
      }
    }
    setVouchers(next);
  };

  const propose = async () => {
    if (!form.companyId && !companies[0]) return;
    setBusy(true);
    try {
      const companyId = form.companyId || String(companies[0]?.id || '');
      const created = await api.proposeRecurringRun({ companyId, period }) as RunRow & { lines?: RunLine[] };
      toast('تم اقتراح التشغيل');
      setRun(created);
      await load();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الاقتراح', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const patchLine = async (line: RunLine, patch: Partial<RunLine>) => {
    if (!run?.id) return;
    setBusy(true);
    try {
      const updated = await api.updateRecurringRunLines(run.id, { lines: [{ id: line.id, ...patch }] }) as typeof run;
      setRun(updated);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر التعديل', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const runAction = async (action: 'submit' | 'approve' | 'execute') => {
    if (!run?.id) return;
    setBusy(true);
    try {
      if (action === 'execute') {
        const result = (await api.executeRecurringRun(run.id, executeForm)) as {
          vouchers?: VoucherRow[];
        };
        const list = result.vouchers || [];
        await refreshVouchers(list);
        toast(`تم إنشاء ${list.length} سند — أكمل الاعتماد الثنائي ثم الصرف`);
      } else {
        if (action === 'submit') await api.submitRecurringRun(run.id);
        else await api.approveRecurringRun(run.id);
        toast(action === 'submit' ? 'تم الإرسال' : 'تم الاعتماد');
      }
      await openRun(run.id);
      await load();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الإجراء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const approveVoucherDual = async (v: VoucherRow) => {
    if (!v.approvalRequestId) return;
    setBusy(true);
    try {
      await api.decideApproval(String(v.approvalRequestId), { decision: 'approved' });
      toast('تم اعتماد الصرف الثنائي');
      await refreshVouchers(vouchers);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الاعتماد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const payVoucher = async (v: VoucherRow) => {
    if (!v.id) return;
    setBusy(true);
    try {
      await api.payPaymentVoucher(String(v.id));
      toast('تم الصرف والترحيل');
      await refreshVouchers(vouchers);
      if (run?.id) await openRun(run.id);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الصرف', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const addOneOffLine = async () => {
    if (!run?.id) return;
    setBusy(true);
    try {
      const updated = (await api.updateRecurringRunLines(run.id, {
        lines: [
          {
            nameAr: oneOffLine.nameAr,
            amount: Number(oneOffLine.amount),
            currencyCode: oneOffLine.currencyCode,
            exchangeRate: Number(oneOffLine.exchangeRate || 1),
            transferFee: Number(oneOffLine.transferFee || 0),
            fxDifference: Number(oneOffLine.fxDifference || 0),
            paymentMethod: 'bank_transfer',
            bankDetails: '',
            costCentreId: oneOffLine.costCentreId || NONE_COST_CENTRE_ID,
            skipped: false,
          },
        ],
      })) as typeof run;
      setRun(updated);
      setOneOffLine({
        nameAr: '',
        amount: '',
        currencyCode: 'SAR',
        exchangeRate: '1',
        transferFee: '0',
        fxDifference: '0',
        costCentreId: NONE_COST_CENTRE_ID,
      });
      toast('تمت إضافة البند');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الإضافة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const submitOneOff = async () => {
    setBusy(true);
    try {
      const idempotencyKey = oneOff.idempotencyKey || crypto.randomUUID().replace(/-/g, '');
      const created = (await api.createRecurringOneOff({
        companyId: oneOff.companyId,
        nameAr: oneOff.nameAr,
        amount: Number(oneOff.amount),
        currencyCode: oneOff.currencyCode,
        exchangeRate: Number(oneOff.exchangeRate || 1),
        transferFee: Number(oneOff.transferFee || 0),
        fxDifference: Number(oneOff.fxDifference || 0),
        paymentMethod: 'bank_transfer',
        bankDetails: oneOff.bankDetails,
        costCentreId: oneOff.costCentreId || NONE_COST_CENTRE_ID,
        paidOn: oneOff.paidOn,
        bankAccountCode: oneOff.bankAccountCode,
        period,
        idempotencyKey,
      })) as VoucherRow;
      setOneOff((f) => ({ ...f, idempotencyKey }));
      if (created?.id) await refreshVouchers([created]);
      toast('تم إنشاء السند — أكمل الاعتماد الثنائي ثم الصرف');
      setOneOff((f) => ({ ...f, nameAr: '', amount: '', bankDetails: '', idempotencyKey: '' }));
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر الإنشاء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const loadHistory = async (payeeId: string) => {
    setHistoryPayeeId(payeeId);
    try {
      const res = await api.getRecurringPayeeHistory(payeeId);
      setHistory(res.items || []);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر تحميل السجل', 'danger');
    }
  };

  const payeeColumns: ColumnDef<PayeeRow>[] = [
    { key: 'nameAr', header: 'الاسم', getValue: (r) => String(r.nameAr || '') },
    {
      key: 'payeeType',
      header: 'النوع',
      getValue: (r) => TYPE_LABEL[String(r.payeeType || '')] || String(r.payeeType || ''),
    },
    {
      key: 'amount',
      header: 'المبلغ',
      money: true,
      numeric: true,
      getValue: (r) => Number(r.amount || 0),
      render: (r) => (
        <span className="num whitespace-nowrap">{formatCurrency(r.amount, r.currencyCode)}</span>
      ),
    },
    { key: 'currencyCode', header: 'العملة', getValue: (r) => String(r.currencyCode || '') },
    {
      key: 'status',
      header: 'الحالة',
      getValue: (r) => (Number(r.status) === 1 ? 'نشط' : 'موقوف'),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      getValue: () => '',
      render: (r) => (
        <div className="flex gap-2 text-xs">
          <button
            type="button"
            className="underline"
            onClick={() => {
              setForm({
                id: String(r.id || ''),
                companyId: String(r.companyId || form.companyId),
                nameAr: String(r.nameAr || ''),
                payeeType: String(r.payeeType || 'other'),
                amount: String(r.amount ?? ''),
                currencyCode: String(r.currencyCode || 'SAR'),
                paymentMethod: String(r.paymentMethod || 'bank_transfer'),
                bankDetails: '',
                bankDetailsTouched: false,
                clearBankDetails: false,
                costCentreId: String(r.costCentreId || NONE_COST_CENTRE_ID),
                activeFrom: String(r.activeFrom || `${period}-01`).slice(0, 10),
                activeTo: String(r.activeTo || '').slice(0, 10),
                notes: String(r.notes || ''),
              });
              setTab('form');
            }}
          >
            تعديل
          </button>
          <button type="button" className="underline" onClick={() => void loadHistory(String(r.id))}>
            السجل
          </button>
        </div>
      ),
    },
  ];

  const runColumns: ColumnDef<RunRow>[] = [
    {
      key: 'periodMonth',
      header: 'الشهر',
      getValue: (r) => String(r.periodMonth || '').slice(0, 7),
    },
    {
      key: 'status',
      header: 'الحالة',
      getValue: (r) => RUN_STATUS[Number(r.status)] || String(r.status || ''),
    },
    {
      key: 'open',
      header: '',
      getValue: () => '',
      render: (r) => (
        <button type="button" className="underline text-xs" onClick={() => void openRun(String(r.id))}>
          فتح
        </button>
      ),
    },
  ];

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="عرض المدفوعات الدورية يحتاج صلاحية المدفوعات." />;
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="مدفوعات دورية"
        description="مستفيدون اختياريون خارج المسير (تجربة خارج الكفالة / مكتب مصر) عبر محرك المدفوعات."
        primaryAction={{
          label: 'تحديث',
          icon: <RefreshCw size={14} />,
          onClick: () => void load(),
          disabled: busy,
        }}
        moreActions={
          canDisburse
            ? [{ label: 'مستفيد', onClick: () => setTab('form'), icon: <Plus size={14} /> }]
            : undefined
        }
      />
      <SectionTabs tabs={tabs} activeTab={tab} onChange={(id) => setTab(id as typeof tab)} paramName="tab" />
      {loading ? (
        <Skeleton className="h-40 w-full" />
      ) : error ? (
        <ErrorState title="تعذر التحميل" message={error} onRetry={() => void load()} />
      ) : tab === 'list' ? (
        <div className="space-y-4">
          <DataTable
            data={payees}
            columns={payeeColumns}
            emptyTitle="لا مستفيدين بعد"
            emptyDescription="أضف ملفاً اختيارياً ثم اقترح تشغيلاً شهرياً."
          />
          {historyPayeeId && (
            <div className="space-y-2">
              <h3 className="text-sm font-medium">سجل المستفيد</h3>
              <DataTable
                data={history}
                columns={[
                  { key: 'periodMonth', header: 'الشهر', getValue: (r) => String(r.periodMonth || '').slice(0, 7) },
                  {
                    key: 'sarAmount',
                    header: 'بالريال',
                    money: true,
                    getValue: (r) => Number(r.sarAmount || 0),
                    render: (r) => <span className="num">{formatSar(r.sarAmount as number)}</span>,
                  },
                  {
                    key: 'skipped',
                    header: 'تخطي',
                    getValue: (r) => (r.skipped ? 'نعم' : 'لا'),
                  },
                ]}
                emptyTitle="لا سجل"
                emptyDescription="لم يُدرج هذا المستفيد في تشغيل بعد."
              />
            </div>
          )}
        </div>
      ) : tab === 'form' ? (
        <div className="grid gap-3 max-w-xl">
          <label className="text-xs space-y-1">
            الشركة
            <select
              className={inputClass}
              value={form.companyId}
              onChange={(e) => setForm({ ...form, companyId: e.target.value })}
            >
              {companies.map((c) => (
                <option key={String(c.id)} value={String(c.id)}>
                  {companyLabel(c)}
                </option>
              ))}
            </select>
          </label>
          <label className="text-xs space-y-1">
            الاسم
            <input className={inputClass} value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} />
          </label>
          <label className="text-xs space-y-1">
            النوع
            <select
              className={inputClass}
              value={form.payeeType}
              onChange={(e) => setForm({ ...form, payeeType: e.target.value })}
            >
              {Object.entries(TYPE_LABEL).map(([k, v]) => (
                <option key={k} value={k}>
                  {v}
                </option>
              ))}
            </select>
          </label>
          <div className="grid grid-cols-2 gap-2">
            <label className="text-xs space-y-1">
              المبلغ
              <input className={inputClass} value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
            </label>
            <label className="text-xs space-y-1">
              العملة
              <input
                className={inputClass}
                value={form.currencyCode}
                onChange={(e) => setForm({ ...form, currencyCode: e.target.value.toUpperCase() })}
              />
            </label>
          </div>
          <label className="text-xs space-y-1">
            طريقة الدفع
            <select
              className={inputClass}
              value={form.paymentMethod}
              onChange={(e) => setForm({ ...form, paymentMethod: e.target.value })}
            >
              <option value="bank_transfer">تحويل بنكي</option>
              <option value="cash">نقدي</option>
              <option value="other">أخرى</option>
            </select>
          </label>
          <label className="text-xs space-y-1">
            مركز التكلفة
            <select
              className={inputClass}
              value={form.costCentreId}
              onChange={(e) => setForm({ ...form, costCentreId: e.target.value })}
            >
              <option value={NONE_COST_CENTRE_ID}>بدون مركز تكلفة</option>
            </select>
          </label>
          <label className="text-xs space-y-1">
            تفاصيل التحويل (لا تُسجَّل في سجل التدقيق)
            <textarea
              className={inputClass}
              rows={2}
              value={form.bankDetails}
              placeholder={form.id && !form.bankDetailsTouched ? 'اتركه فارغاً للإبقاء على التفاصيل الحالية' : ''}
              onChange={(e) =>
                setForm({ ...form, bankDetails: e.target.value, bankDetailsTouched: true, clearBankDetails: false })
              }
            />
          </label>
          {form.id && (
            <button
              type="button"
              className="underline text-xs w-fit"
              disabled={busy}
              onClick={() => setForm({ ...form, clearBankDetails: true, bankDetails: '', bankDetailsTouched: false })}
            >
              مسح تفاصيل التحويل
            </button>
          )}
          {form.clearBankDetails && <p className="text-xs text-[var(--muted)]">سيتم مسح تفاصيل التحويل عند الحفظ.</p>}
          <div className="grid grid-cols-2 gap-2">
            <label className="text-xs space-y-1">
              نشط من
              <input
                type="date"
                className={inputClass}
                value={form.activeFrom}
                onChange={(e) => setForm({ ...form, activeFrom: e.target.value })}
              />
            </label>
            <label className="text-xs space-y-1">
              نشط إلى
              <input
                type="date"
                className={inputClass}
                value={form.activeTo}
                onChange={(e) => setForm({ ...form, activeTo: e.target.value })}
              />
            </label>
          </div>
          <label className="text-xs space-y-1">
            ملاحظات
            <input className={inputClass} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </label>
          {canDisburse && (
            <button type="button" className="btn-primary text-xs w-fit" disabled={busy} onClick={() => void savePayee()}>
              حفظ
            </button>
          )}
        </div>
      ) : tab === 'oneoff' ? (
        <div className="grid gap-3 max-w-xl">
          <p className="text-xs text-[var(--muted)]">دفعة لمرة دون ملف مستفيد — عبر محرك المدفوعات والاعتماد الثنائي.</p>
          <label className="text-xs space-y-1">
            الشركة
            <select
              className={inputClass}
              value={oneOff.companyId}
              onChange={(e) => setOneOff({ ...oneOff, companyId: e.target.value })}
            >
              {companies.map((c) => (
                <option key={String(c.id)} value={String(c.id)}>
                  {companyLabel(c)}
                </option>
              ))}
            </select>
          </label>
          <label className="text-xs space-y-1">
            الاسم
            <input className={inputClass} value={oneOff.nameAr} onChange={(e) => setOneOff({ ...oneOff, nameAr: e.target.value })} />
          </label>
          <div className="grid grid-cols-3 gap-2">
            <label className="text-xs space-y-1">
              المبلغ
              <input className={inputClass} value={oneOff.amount} onChange={(e) => setOneOff({ ...oneOff, amount: e.target.value })} />
            </label>
            <label className="text-xs space-y-1">
              العملة
              <input
                className={inputClass}
                value={oneOff.currencyCode}
                onChange={(e) => setOneOff({ ...oneOff, currencyCode: e.target.value.toUpperCase() })}
              />
            </label>
            <label className="text-xs space-y-1">
              سعر الصرف
              <input
                className={inputClass}
                value={oneOff.exchangeRate}
                onChange={(e) => setOneOff({ ...oneOff, exchangeRate: e.target.value })}
              />
            </label>
          </div>
          <label className="text-xs space-y-1">
            رسوم التحويل (ر.س)
            <input
              className={inputClass}
              value={oneOff.transferFee}
              onChange={(e) => setOneOff({ ...oneOff, transferFee: e.target.value })}
            />
          </label>
          <label className="text-xs space-y-1">
            فرق العملة (ر.س، سالب = ربح)
            <input
              className={inputClass}
              value={oneOff.fxDifference}
              onChange={(e) => setOneOff({ ...oneOff, fxDifference: e.target.value })}
            />
          </label>
          <label className="text-xs space-y-1">
            مركز التكلفة
            <select
              className={inputClass}
              value={oneOff.costCentreId}
              onChange={(e) => setOneOff({ ...oneOff, costCentreId: e.target.value })}
            >
              <option value={NONE_COST_CENTRE_ID}>بدون مركز تكلفة</option>
            </select>
          </label>
          <label className="text-xs space-y-1">
            حساب البنك
            <input
              className={inputClass}
              value={oneOff.bankAccountCode}
              onChange={(e) => setOneOff({ ...oneOff, bankAccountCode: e.target.value })}
            />
          </label>
          <label className="text-xs space-y-1">
            تاريخ الدفع
            <input
              type="date"
              className={inputClass}
              value={oneOff.paidOn}
              onChange={(e) => setOneOff({ ...oneOff, paidOn: e.target.value })}
            />
          </label>
          {canDisburse && (
            <button type="button" className="btn-primary text-xs w-fit" disabled={busy} onClick={() => void submitOneOff()}>
              إنشاء سند
            </button>
          )}
        </div>
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            {canDisburse && (
              <button type="button" className="btn-primary text-xs" disabled={busy} onClick={() => void propose()}>
                اقتراح شهر {period}
              </button>
            )}
            <Link href="/payments/recurring" className="text-xs underline self-center">
              قائمة المستفيدين
            </Link>
          </div>
          <DataTable
            data={runs}
            columns={runColumns}
            emptyTitle="لا تشغيلات"
            emptyDescription="اقترح تشغيلاً من المستفيدين النشطين لهذا الشهر."
          />
          {run && (
            <div className="space-y-3 border border-[var(--line)] rounded-xl p-3">
              <div className="flex flex-wrap items-center gap-3 text-sm">
                <span>تشغيل {String(run.periodMonth || '').slice(0, 7)}</span>
                <span>{RUN_STATUS[Number(run.status)] || run.status}</span>
                {canDisburse && Number(run.status) === 1 && (
                  <button type="button" className="btn-secondary text-xs" disabled={busy} onClick={() => void runAction('submit')}>
                    إرسال
                  </button>
                )}
                {canApprove && Number(run.status) === 2 && (
                  <button type="button" className="btn-secondary text-xs" disabled={busy} onClick={() => void runAction('approve')}>
                    اعتماد
                  </button>
                )}
                {canDisburse && Number(run.status) === 3 && (
                  <>
                    <input
                      type="date"
                      className={inputClass + ' w-auto'}
                      value={executeForm.paidOn}
                      onChange={(e) => setExecuteForm({ ...executeForm, paidOn: e.target.value })}
                    />
                    <input
                      className={inputClass + ' w-28'}
                      value={executeForm.bankAccountCode}
                      onChange={(e) => setExecuteForm({ ...executeForm, bankAccountCode: e.target.value })}
                    />
                    <button type="button" className="btn-primary text-xs" disabled={busy} onClick={() => void runAction('execute')}>
                      تنفيذ الصرف
                    </button>
                  </>
                )}
              </div>
              <DataTable
                data={run.lines || []}
                columns={[
                  { key: 'nameAr', header: 'الاسم', getValue: (r) => String(r.nameAr || '') },
                  {
                    key: 'amount',
                    header: 'المبلغ',
                    getValue: (r) => Number(r.amount || 0),
                    render: (r) =>
                      Number(run.status) === 1 ? (
                        <input
                          className={inputClass + ' w-24'}
                          defaultValue={String(r.amount ?? '')}
                          onBlur={(e) =>
                            void patchLine(r, {
                              amount: Number(e.target.value),
                              currencyCode: String(r.currencyCode),
                              exchangeRate: Number(r.exchangeRate || 0),
                              transferFee: Number(r.transferFee || 0),
                              fxDifference: Number(r.fxDifference || 0),
                              skipped: Boolean(r.skipped),
                            })
                          }
                        />
                      ) : (
                        <span className="num">{formatCurrency(r.amount, r.currencyCode)}</span>
                      ),
                  },
                  {
                    key: 'exchangeRate',
                    header: 'سعر الصرف',
                    getValue: (r) => Number(r.exchangeRate || 0),
                    render: (r) =>
                      Number(run.status) === 1 && String(r.currencyCode) !== 'SAR' ? (
                        <input
                          className={inputClass + ' w-24'}
                          defaultValue={String(r.exchangeRate || '')}
                          onBlur={(e) =>
                            void patchLine(r, {
                              exchangeRate: Number(e.target.value),
                              amount: Number(r.amount),
                              currencyCode: String(r.currencyCode),
                              transferFee: Number(r.transferFee || 0),
                              fxDifference: Number(r.fxDifference || 0),
                              skipped: Boolean(r.skipped),
                            })
                          }
                        />
                      ) : (
                        <span className="num">{r.exchangeRate}</span>
                      ),
                  },
                  {
                    key: 'transferFee',
                    header: 'رسوم',
                    money: true,
                    getValue: (r) => Number(r.transferFee || 0),
                    render: (r) =>
                      Number(run.status) === 1 ? (
                        <input
                          className={inputClass + ' w-20'}
                          defaultValue={String(r.transferFee ?? 0)}
                          onBlur={(e) =>
                            void patchLine(r, {
                              transferFee: Number(e.target.value),
                              amount: Number(r.amount),
                              currencyCode: String(r.currencyCode),
                              exchangeRate: Number(r.exchangeRate || 0),
                              fxDifference: Number(r.fxDifference || 0),
                              skipped: Boolean(r.skipped),
                            })
                          }
                        />
                      ) : (
                        <span className="num">{formatSar(r.transferFee)}</span>
                      ),
                  },
                  {
                    key: 'fxDifference',
                    header: 'فرق عملة',
                    money: true,
                    getValue: (r) => Number(r.fxDifference || 0),
                    render: (r) =>
                      Number(run.status) === 1 ? (
                        <input
                          className={inputClass + ' w-20'}
                          defaultValue={String(r.fxDifference ?? 0)}
                          onBlur={(e) =>
                            void patchLine(r, {
                              fxDifference: Number(e.target.value),
                              amount: Number(r.amount),
                              currencyCode: String(r.currencyCode),
                              exchangeRate: Number(r.exchangeRate || 0),
                              transferFee: Number(r.transferFee || 0),
                              skipped: Boolean(r.skipped),
                            })
                          }
                        />
                      ) : (
                        <span className="num">{formatSar(r.fxDifference)}</span>
                      ),
                  },
                  {
                    key: 'sarAmount',
                    header: 'بالريال',
                    money: true,
                    getValue: (r) => Number(r.sarAmount || 0),
                    render: (r) => <span className="num">{formatSar(r.sarAmount)}</span>,
                  },
                  {
                    key: 'skipped',
                    header: 'تخطي',
                    getValue: (r) => (r.skipped ? 'نعم' : 'لا'),
                    render: (r) =>
                      Number(run.status) === 1 ? (
                        <button
                          type="button"
                          className="underline text-xs"
                          onClick={() => void patchLine(r, { skipped: !r.skipped })}
                        >
                          {r.skipped ? 'إرجاع' : 'تخطي'}
                        </button>
                      ) : (
                        <span>{r.skipped ? 'نعم' : 'لا'}</span>
                      ),
                  },
                ]}
                emptyTitle="لا بنود"
                emptyDescription="لا بنود في هذا التشغيل."
              />
              {canDisburse && Number(run.status) === 1 && (
                <div className="grid gap-2 max-w-xl border border-dashed border-[var(--line)] rounded-xl p-3">
                  <p className="text-xs font-medium">إضافة بند لمرة (oneOffLine)</p>
                  <div className="grid grid-cols-2 gap-2">
                    <input
                      className={inputClass}
                      placeholder="الاسم"
                      value={oneOffLine.nameAr}
                      onChange={(e) => setOneOffLine({ ...oneOffLine, nameAr: e.target.value })}
                    />
                    <input
                      className={inputClass}
                      placeholder="المبلغ"
                      value={oneOffLine.amount}
                      onChange={(e) => setOneOffLine({ ...oneOffLine, amount: e.target.value })}
                    />
                    <input
                      className={inputClass}
                      placeholder="رسوم"
                      value={oneOffLine.transferFee}
                      onChange={(e) => setOneOffLine({ ...oneOffLine, transferFee: e.target.value })}
                    />
                    <input
                      className={inputClass}
                      placeholder="فرق عملة"
                      value={oneOffLine.fxDifference}
                      onChange={(e) => setOneOffLine({ ...oneOffLine, fxDifference: e.target.value })}
                    />
                  </div>
                  <button type="button" className="btn-secondary text-xs w-fit" disabled={busy} onClick={() => void addOneOffLine()}>
                    إضافة بند
                  </button>
                </div>
              )}
              {vouchers.length > 0 && (
                <div className="space-y-2">
                  <h3 className="text-sm font-medium">سندات الصرف (vouchers)</h3>
                  <DataTable
                    data={vouchers}
                    columns={[
                      { key: 'id', header: 'المعرّف', getValue: (r) => String(r.id || '').slice(0, 8) },
                      {
                        key: 'amount',
                        header: 'المبلغ',
                        money: true,
                        getValue: (r) => Number(r.amount || 0),
                        render: (r) => <span className="num">{formatSar(r.amount)}</span>,
                      },
                      {
                        key: 'status',
                        header: 'الحالة',
                        getValue: (r) => (Number(r.status) === 2 ? 'مصروف' : 'محجوز'),
                      },
                      {
                        key: 'actions',
                        header: 'إجراءات',
                        getValue: () => '',
                        render: (r) => (
                          <div className="flex gap-2 text-xs">
                            {canApprove && r.approvalRequestId && Number(r.status) === 1 && (
                              <button
                                type="button"
                                className="underline"
                                disabled={busy}
                                onClick={() => void approveVoucherDual(r)}
                              >
                                اعتماد ثنائي
                              </button>
                            )}
                            {canDisburse && Number(r.status) === 1 && (
                              <button type="button" className="underline" disabled={busy} onClick={() => void payVoucher(r)}>
                                صرف وترحيل
                              </button>
                            )}
                          </div>
                        ),
                      },
                    ]}
                    emptyTitle="لا سندات"
                    emptyDescription="نفّذ التشغيل لإنشاء السندات."
                  />
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
