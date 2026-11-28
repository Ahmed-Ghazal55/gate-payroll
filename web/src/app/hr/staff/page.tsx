'use client';

import React, { useCallback, useEffect, useMemo, useState, Suspense } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { api, ApiError } from '../../../lib/api';
import { useToast } from '../../../components/ui/Toast';
import { PageHeader } from '../../../components/ui/PageHeader';
import { SectionTabs, SectionTabItem } from '../../../components/ui/SectionTabs';
import { EmptyState, ErrorState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { DataTable, ColumnDef } from '../../../components/ui/DataTable';
import { formatSar } from '../../../lib/money';

type Row = Record<string, unknown>;

const TABS: SectionTabItem[] = [
  { id: 'dashboard', label: 'لوحة الموارد البشرية' },
  { id: 'employees', label: 'الموظفون' },
  { id: 'runs', label: 'مسير الموظفين' },
];

function actionsOf(row: Row): string[] {
  const a = row.allowedActions;
  return Array.isArray(a) ? a.map(String) : [];
}

function StaffHrPageInner() {
  const { toast } = useToast();
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const tabFromUrl = searchParams?.get('tab') || 'dashboard';
  const [tab, setTab] = useState(TABS.some((t) => t.id === tabFromUrl) ? tabFromUrl : 'dashboard');
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [dashboard, setDashboard] = useState<Row | null>(null);
  const [employees, setEmployees] = useState<Row[]>([]);
  const [runs, setRuns] = useState<Row[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [card, setCard] = useState<Row | null>(null);
  const [catalogs, setCatalogs] = useState<Record<string, string[]>>({});
  const [companies, setCompanies] = useState<Array<{ id: string; shortNameAr?: string; nameAr?: string }>>([]);
  const [createOpen, setCreateOpen] = useState(false);
  const [form, setForm] = useState({
    displayName: '',
    iqama: '',
    companyId: '',
    department: 'HR',
    jobTitle: 'محاسب',
    city: 'جدة',
    nationality: 'مصري',
    employmentType: 'office',
    erpEmployeeId: '',
  });
  const [monthForm, setMonthForm] = useState({
    period: '2026-11',
    attendanceDays: '22',
    overtimeHours: '0',
    latenessHours: '0',
    bonusAmount: '0',
    deductionAmount: '0',
  });
  const [contractForm, setContractForm] = useState({
    issueDate: '',
    endDate: '',
    basicSalary: '',
    fileBase64: '',
  });
  const [runPeriod, setRunPeriod] = useState('2026-11');
  const [runCompanyId, setRunCompanyId] = useState('');
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);

  const switchTab = useCallback(
    (id: string) => {
      setTab(id);
      const q = new URLSearchParams(searchParams?.toString() || '');
      q.set('tab', id);
      router.replace(`${pathname}?${q.toString()}`);
    },
    [pathname, router, searchParams]
  );

  const load = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const [dash, emps, runList, cats, comps] = await Promise.all([
        api.getStaffHrDashboard(),
        api.getStaffEmployees({ status: 'active' }),
        api.getStaffRuns(),
        api.getStaffHrCatalogs(),
        api.getCompanies().catch(() => []),
      ]);
      setDashboard(dash);
      setEmployees(emps);
      setRuns(runList);
      setCatalogs(cats);
      setCompanies(comps as Array<{ id: string; shortNameAr?: string; nameAr?: string }>);
      if (!form.companyId && comps.length) {
        const first = (comps as Array<{ id: string }>)[0]?.id || '';
        setForm((f) => ({ ...f, companyId: first }));
        setRunCompanyId(first);
      }
    } catch (e) {
      setErrorMsg(e instanceof ApiError ? e.message : 'تعذر تحميل شؤون الموظفين');
    } finally {
      setLoading(false);
    }
  }, [form.companyId]);

  useEffect(() => {
    void load();
  }, [load]);

  const openCard = async (id: string) => {
    setSelectedId(id);
    try {
      setCard(await api.getStaffEmployee(id));
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'تعذر فتح البطاقة', 'danger');
    }
  };

  const createEmployee = async () => {
    try {
      const created = await api.createStaffEmployee(form);
      toast('تم إنشاء بطاقة الموظف', 'success');
      setCreateOpen(false);
      await load();
      await openCard(String(created.id));
      switchTab('employees');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'فشل الإنشاء', 'danger');
    }
  };

  const saveMonth = async () => {
    if (!selectedId) return;
    try {
      await api.upsertStaffMonth(selectedId, {
        period: monthForm.period,
        attendanceDays: Number(monthForm.attendanceDays),
        overtimeHours: Number(monthForm.overtimeHours),
        latenessHours: Number(monthForm.latenessHours),
        bonusAmount: Number(monthForm.bonusAmount),
        deductionAmount: Number(monthForm.deductionAmount),
      });
      toast('تم حفظ الملخص الشهري', 'success');
      await openCard(selectedId);
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'فشل الحفظ', 'danger');
    }
  };

  const addContract = async () => {
    if (!selectedId || !contractForm.fileBase64) {
      toast('اختر ملف عقد قوى', 'danger');
      return;
    }
    try {
      await api.addStaffContract(selectedId, {
        issueDate: contractForm.issueDate,
        endDate: contractForm.endDate,
        basicSalary: Number(contractForm.basicSalary),
        attachment: { fileName: 'qiwa-contract.pdf', dataBase64: contractForm.fileBase64 },
      });
      toast('تم حفظ عقد قوى', 'success');
      await openCard(selectedId);
      await load();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'فشل رفع العقد', 'danger');
    }
  };

  const onFile = (file: File | null) => {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const raw = String(reader.result || '');
      const b64 = raw.includes(',') ? raw.split(',')[1] : raw;
      setContractForm((f) => ({ ...f, fileBase64: b64 }));
    };
    reader.readAsDataURL(file);
  };

  const createRun = async () => {
    try {
      await api.createStaffRun({ period: runPeriod, companyId: runCompanyId });
      toast('تم إنشاء مسودة المسير', 'success');
      await load();
      switchTab('runs');
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'فشل إنشاء المسير', 'danger');
    }
  };

  const runAction = async (id: string, action: Parameters<typeof api.staffRunAction>[1]) => {
    try {
      await api.staffRunAction(id, action);
      toast('تم تنفيذ الإجراء', 'success');
      await load();
    } catch (e) {
      toast(e instanceof ApiError ? e.message : 'فشل الإجراء', 'danger');
    }
  };

  const empCols: ColumnDef<Row>[] = useMemo(
    () => [
      { key: 'displayName', header: 'الاسم', filterable: true },
      { key: 'iqama', header: 'الهوية', filterable: true },
      { key: 'department', header: 'القسم', filterable: true },
      { key: 'jobTitle', header: 'المسمى', filterable: true },
      { key: 'employmentType', header: 'نوع التوظيف', filterable: true },
      {
        key: 'basicSalary',
        header: 'راتب العقد / مدد',
        filterable: true,
        money: true,
        getValue: (r) => Number(r.basicSalary || 0),
        render: (r) => formatSar(Number(r.basicSalary || 0)),
      },
      { key: 'contractEndDate', header: 'انتهاء العقد', filterable: true },
      { key: 'erpEmployeeId', header: 'معرّف موظف النظام الخارجي', filterable: true },
    ],
    []
  );

  const alertCols: ColumnDef<Row>[] = useMemo(
    () => [
      { key: 'displayName', header: 'الموظف', filterable: true },
      { key: 'endDate', header: 'انتهاء العقد', filterable: true },
      { key: 'daysRemaining', header: 'الأيام المتبقية', filterable: true, numeric: true },
      { key: 'level', header: 'المستوى', filterable: true },
    ],
    []
  );

  const runCols: ColumnDef<Row>[] = useMemo(
    () => [
      { key: 'period', header: 'الشهر', filterable: true },
      { key: 'status', header: 'الحالة', filterable: true },
      { key: 'lineCount', header: 'عدد الصفوف', filterable: true, numeric: true },
      {
        key: 'blockedCount',
        header: 'محجوبون',
        filterable: true,
        numeric: true,
        getValue: (r) => Number(r.blockedCount || 0),
      },
      {
        key: 'netTotal',
        header: 'الصافي',
        filterable: true,
        money: true,
        getValue: (r) => Number(r.netTotal || 0),
        render: (r) => formatSar(Number(r.netTotal || 0)),
      },
      { key: 'createdBy', header: 'المجهّز', filterable: true },
      {
        key: 'actions',
        header: 'إجراءات',
        filterable: false,
        render: (row) => {
          const acts = actionsOf(row);
          return (
            <div className="flex flex-wrap gap-1">
              {acts.includes('recalculate') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'recalculate')}>
                  إعادة احتساب
                </button>
              )}
              {acts.includes('submit_review') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'submit-review')}>
                  إرسال للمراجعة
                </button>
              )}
              {acts.includes('back_to_draft') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'back-to-draft')}>
                  إرجاع للمسودة
                </button>
              )}
              {acts.includes('approve') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'approve')}>
                  اعتماد
                </button>
              )}
              {acts.includes('generate_mudad') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'mudad')}>
                  ملف مدد
                </button>
              )}
              {acts.includes('post_journal') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'post')}>
                  ترحيل القيد
                </button>
              )}
              {acts.includes('reverse') && (
                <button type="button" className="text-sm underline" onClick={() => void runAction(String(row.id), 'reverse')}>
                  عكس
                </button>
              )}
              <button type="button" className="text-sm underline" onClick={() => setSelectedRunId(String(row.id))}>
                التفاصيل
              </button>
            </div>
          );
        },
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [runs]
  );

  const selectedRun = useMemo(
    () => (selectedRunId ? runs.find((r) => String(r.id) === selectedRunId) || null : null),
    [runs, selectedRunId]
  );
  const selectedBlocked = useMemo(() => {
    const list = selectedRun?.blocked;
    return Array.isArray(list) ? (list as Row[]) : [];
  }, [selectedRun]);

  if (loading) return <Skeleton rows={8} />;
  if (errorMsg) return <ErrorState message={errorMsg} onRetry={() => void load()} />;

  const alerts = Array.isArray(dashboard?.contractAlerts) ? (dashboard!.contractAlerts as Row[]) : [];
  const dashActions = actionsOf(dashboard || {});

  return (
    <div className="space-y-4">
      <PageHeader title="شؤون الموظفين" description="بطاقات الموظفين وعقود قوى ومسير الرواتب الإداري" />
      <SectionTabs tabs={TABS} activeTab={tab} onChange={switchTab} />

      {tab === 'dashboard' && (
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">عدد الموظفين النشطين: {String(dashboard?.employeeCount ?? 0)}</p>
          <h3 className="text-base font-medium">تنبيهات انتهاء عقود قوى (٦٠ / ٣٠ يومًا)</h3>
          {alerts.length === 0 ? (
            <EmptyState title="لا تنبيهات" description="لا عقود منتهية أو قريبة الانتهاء ضمن النطاق." />
          ) : (
            <DataTable data={alerts} columns={alertCols} getRowId={(r) => String(r.employeeId)} />
          )}
        </div>
      )}

      {tab === 'employees' && (
        <div className="space-y-4">
          {dashActions.includes('create_employee') && (
            <button type="button" className="rounded border px-3 py-1.5 text-sm" onClick={() => setCreateOpen((v) => !v)}>
              {createOpen ? 'إغلاق النموذج' : 'موظف جديد'}
            </button>
          )}
          {createOpen && (
            <div className="grid gap-2 rounded border p-3 md:grid-cols-3">
              <input className="border px-2 py-1" placeholder="الاسم المعروض" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} />
              <input className="border px-2 py-1" placeholder="رقم الهوية" value={form.iqama} onChange={(e) => setForm({ ...form, iqama: e.target.value })} />
              <select className="border px-2 py-1" value={form.companyId} onChange={(e) => setForm({ ...form, companyId: e.target.value })}>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.shortNameAr || c.nameAr || c.id}</option>
                ))}
              </select>
              {(['department', 'jobTitle', 'city', 'nationality', 'employmentType'] as const).map((key) => (
                <select key={key} className="border px-2 py-1" value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })}>
                  {(catalogs[key === 'employmentType' ? 'employmentTypes' : key === 'jobTitle' ? 'jobTitles' : key === 'nationality' ? 'nationalities' : key === 'department' ? 'departments' : 'cities'] || []).map((v) => (
                    <option key={v} value={v}>{v}</option>
                  ))}
                </select>
              ))}
              <input className="border px-2 py-1" placeholder="معرّف موظف النظام الخارجي (للمؤشرات لاحقًا)" value={form.erpEmployeeId} onChange={(e) => setForm({ ...form, erpEmployeeId: e.target.value })} />
              <button type="button" className="rounded bg-foreground px-3 py-1.5 text-sm text-background" onClick={() => void createEmployee()}>حفظ</button>
            </div>
          )}
          {employees.length === 0 ? (
            <EmptyState title="لا موظفون" description="أنشئ بطاقة موظف إدارية (ليست لمندوبي التشغيل)." />
          ) : (
            <DataTable
              data={employees}
              columns={empCols}
              getRowId={(r) => String(r.id)}
              rowActions={[{ label: 'فتح البطاقة', onClick: (r) => void openCard(String(r.id)) }]}
            />
          )}

          {card && selectedId && (
            <div className="space-y-3 rounded border p-3">
              <h3 className="text-base font-medium">بطاقة: {String(card.displayName)}</h3>
              <p className="text-sm">راتب العقد / مدد الأساسي: {formatSar(Number(card.basicSalary || 0))} — انتهاء العقد: {String(card.contractEndDate || '—')}</p>
              <p className="text-sm">معرّف موظف النظام الخارجي (للقراءة فقط للمؤشرات): {String(card.erpUserEmployeeId || card.erpEmployeeId || '—')}</p>

              {actionsOf(card).includes('upload_contract') && (
                <div className="grid gap-2 md:grid-cols-4">
                  <input className="border px-2 py-1" type="date" value={contractForm.issueDate} onChange={(e) => setContractForm({ ...contractForm, issueDate: e.target.value })} />
                  <input className="border px-2 py-1" type="date" value={contractForm.endDate} onChange={(e) => setContractForm({ ...contractForm, endDate: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="الراتب الأساسي" value={contractForm.basicSalary} onChange={(e) => setContractForm({ ...contractForm, basicSalary: e.target.value })} />
                  <input className="border px-2 py-1" type="file" accept=".pdf,image/*" onChange={(e) => onFile(e.target.files?.[0] || null)} />
                  <button type="button" className="rounded border px-3 py-1.5 text-sm" onClick={() => void addContract()}>رفع عقد قوى</button>
                </div>
              )}

              {actionsOf(card).includes('upsert_month') && (
                <div className="grid gap-2 md:grid-cols-4">
                  <input className="border px-2 py-1" placeholder="الشهر YYYY-MM" value={monthForm.period} onChange={(e) => setMonthForm({ ...monthForm, period: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="أيام الحضور" value={monthForm.attendanceDays} onChange={(e) => setMonthForm({ ...monthForm, attendanceDays: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="ساعات إضافي" value={monthForm.overtimeHours} onChange={(e) => setMonthForm({ ...monthForm, overtimeHours: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="ساعات تأخير" value={monthForm.latenessHours} onChange={(e) => setMonthForm({ ...monthForm, latenessHours: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="مكافأة" value={monthForm.bonusAmount} onChange={(e) => setMonthForm({ ...monthForm, bonusAmount: e.target.value })} />
                  <input className="border px-2 py-1" placeholder="خصم يدوي" value={monthForm.deductionAmount} onChange={(e) => setMonthForm({ ...monthForm, deductionAmount: e.target.value })} />
                  <button type="button" className="rounded border px-3 py-1.5 text-sm" onClick={() => void saveMonth()}>حفظ الملخص الشهري</button>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {tab === 'runs' && (
        <div className="space-y-4">
          {dashActions.includes('create_run') && (
            <div className="flex flex-wrap gap-2">
              <input className="border px-2 py-1" value={runPeriod} onChange={(e) => setRunPeriod(e.target.value)} placeholder="YYYY-MM" />
              <select className="border px-2 py-1" value={runCompanyId} onChange={(e) => setRunCompanyId(e.target.value)}>
                {companies.map((c) => (
                  <option key={c.id} value={c.id}>{c.shortNameAr || c.nameAr || c.id}</option>
                ))}
              </select>
              <button type="button" className="rounded border px-3 py-1.5 text-sm" onClick={() => void createRun()}>مسير جديد</button>
            </div>
          )}
          {runs.length === 0 ? (
            <EmptyState title="لا مسيرات" description="أنشئ مسودة مسير موظفين بعد إدخال العقود والملخص الشهري." />
          ) : (
            <DataTable data={runs} columns={runCols} getRowId={(r) => String(r.id)} />
          )}
          {selectedRun && (
            <div className="space-y-2 rounded border p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h3 className="text-base font-medium">
                  تفاصيل المسير {String(selectedRun.period)} — {String(selectedRun.status)}
                </h3>
                <button type="button" className="text-sm underline" onClick={() => setSelectedRunId(null)}>
                  إغلاق
                </button>
              </div>
              {Boolean(selectedRun.postRefused) && (
                <p className="text-sm text-red-700">
                  رُفض الترحيل: {String(selectedRun.postRefuseReason || 'أعد المسير للمسودة بعد إصلاح السبب.')}
                </p>
              )}
              <p className="text-sm">
                صفوف الدفع: {String(selectedRun.lineCount ?? 0)} — محجوبون: {String(selectedRun.blockedCount ?? 0)}
              </p>
              {selectedBlocked.length === 0 ? (
                <EmptyState title="لا موظفون محجوبون" description="كل الموظفين النشطين لهم عقد سارٍ يغطي الشهر." />
              ) : (
                <DataTable
                  data={selectedBlocked}
                  columns={[
                    { key: 'displayName', header: 'الموظف', filterable: true },
                    { key: 'iqama', header: 'الهوية', filterable: true },
                    { key: 'reasonAr', header: 'السبب', filterable: true, getValue: (r) => String(r.reasonAr || r.reason || '') },
                  ]}
                  getRowId={(r) => String(r.employeeId || r.iqama)}
                />
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default function StaffHrPage() {
  return (
    <Suspense fallback={<Skeleton rows={8} />}>
      <StaffHrPageInner />
    </Suspense>
  );
}
