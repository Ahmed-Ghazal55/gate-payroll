'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Plus, Pencil, Ban, RotateCcw, RefreshCw } from 'lucide-react';
import { api } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';
import { DataTable, type ColumnDef } from '../../components/ui/DataTable';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { SectionTabs, type SectionTabItem } from '../../components/ui/SectionTabs';

type CoaRow = {
  code: string;
  nameAr?: string;
  parentCode?: string;
  nature?: string;
  posting?: boolean;
  isActive?: boolean;
  allowedActions?: string[];
};

const INNER: SectionTabItem[] = [
  { id: 'tree', label: 'الشجرة النشطة' },
  { id: 'inactive', label: 'الحسابات الموقوفة' },
  { id: 'audit', label: 'سجل تدقيق الدليل' },
];

export function CoaManagePanel() {
  const { toast } = useToast();
  const [subtab, setSubtab] = useState('tree');
  const [rows, setRows] = useState<CoaRow[]>([]);
  const [listActions, setListActions] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<null | { mode: 'create' | 'edit'; row?: CoaRow }>(null);
  const [form, setForm] = useState({ code: '', nameAr: '', parentCode: '', posting: true });
  const [busy, setBusy] = useState(false);
  const [confirmStop, setConfirmStop] = useState<CoaRow | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getFinanceCoa({ inactive: subtab === 'inactive' });
      const items = Array.isArray((res as { items?: unknown }).items)
        ? ((res as { items: CoaRow[] }).items)
        : Array.isArray(res)
          ? (res as CoaRow[])
          : [];
      setRows(items);
      setListActions(
        Array.isArray((res as { allowedActions?: string[] }).allowedActions)
          ? ((res as { allowedActions: string[] }).allowedActions)
          : []
      );
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل دليل الحسابات');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [subtab]);

  useEffect(() => {
    void load();
  }, [load]);

  const parentOptions = useMemo(
    () => rows.map((r) => ({ value: r.code, label: `${r.code} — ${r.nameAr || ''}` })),
    [rows]
  );

  const columns: ColumnDef<CoaRow>[] = [
    { key: 'code', header: 'الرمز', render: (r) => <span className="num font-bold">{r.code}</span> },
    { key: 'nameAr', header: 'الاسم', filterable: true, render: (r) => r.nameAr || '—' },
    { key: 'nature', header: 'الطبيعة', filterable: true, render: (r) => r.nature || '—' },
    {
      key: 'posting',
      header: 'ترحيل',
      render: (r) => (r.posting ? 'حساب ترحيل' : 'تجميعي'),
    },
    {
      key: 'actions',
      header: 'إجراءات',
      filterable: false,
      render: (r) => {
        const actions = r.allowedActions ?? [];
        return (
          <div className="flex flex-wrap gap-1">
            {actions.includes('edit') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md border border-[var(--line)] px-2 py-1 text-[11px] font-semibold"
                onClick={() => {
                  setForm({
                    code: r.code,
                    nameAr: r.nameAr || '',
                    parentCode: r.parentCode || '',
                    posting: Boolean(r.posting),
                  });
                  setEditor({ mode: 'edit', row: r });
                }}
              >
                <Pencil size={12} /> تعديل
              </button>
            )}
            {actions.includes('deactivate') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md bg-[var(--danger)]/10 text-[var(--danger)] px-2 py-1 text-[11px] font-semibold"
                onClick={() => setConfirmStop(r)}
              >
                <Ban size={12} /> إيقاف
              </button>
            )}
            {actions.includes('reactivate') && (
              <button
                type="button"
                className="inline-flex items-center gap-1 rounded-md bg-[var(--money-pos)]/10 text-[var(--money-pos)] px-2 py-1 text-[11px] font-semibold"
                onClick={async () => {
                  try {
                    await api.reactivateFinanceCoa(r.code);
                    toast('تم تفعيل الحساب', 'success');
                    await load();
                  } catch (err: unknown) {
                    toast(err instanceof Error ? err.message : 'تعذر التفعيل', 'danger');
                  }
                }}
              >
                <RotateCcw size={12} /> تفعيل
              </button>
            )}
          </div>
        );
      },
    },
  ];

  const save = async () => {
    setBusy(true);
    try {
      if (editor?.mode === 'create') {
        await api.createFinanceCoa({
          code: form.code,
          nameAr: form.nameAr,
          parentCode: form.parentCode || undefined,
          posting: form.posting,
        });
        toast('تم إنشاء الحساب', 'success');
      } else if (editor?.mode === 'edit' && editor.row) {
        await api.updateFinanceCoa(editor.row.code, {
          nameAr: form.nameAr,
          posting: form.posting,
        });
        toast('تم تعديل الحساب', 'success');
      }
      setEditor(null);
      await load();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر الحفظ', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionTabs tabs={INNER} activeTab={subtab} onChange={setSubtab} paramName="subtab" ariaLabel="تبويبات دليل الحسابات" />
        <div className="flex gap-2">
          <button
            type="button"
            className="inline-flex items-center gap-1 rounded-lg border border-[var(--line)] px-2.5 py-1.5 text-xs font-semibold"
            onClick={() => void load()}
          >
            <RefreshCw size={14} /> تحديث
          </button>
          {listActions.includes('create') && (
            <button
              type="button"
              className="inline-flex items-center gap-1 rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] px-2.5 py-1.5 text-xs font-semibold"
              onClick={() => {
                setForm({ code: '', nameAr: '', parentCode: '', posting: true });
                setEditor({ mode: 'create' });
              }}
            >
              <Plus size={14} /> حساب جديد
            </button>
          )}
        </div>
      </div>

      {subtab === 'audit' ? (
        <EmptyState title="سجل تدقيق الدليل" description="يعرض تغييرات الإنشاء والتعديل والإيقاف من سجل التدقيق عند توفر الصفوف." />
      ) : loading ? (
        <Skeleton rows={6} />
      ) : error ? (
        <ErrorState message={error} onRetry={() => void load()} />
      ) : (
        <DataTable columns={columns} data={rows} getRowId={(r) => r.code} />
      )}

      <Dialog
        open={Boolean(editor)}
        onClose={() => setEditor(null)}
        title={editor?.mode === 'create' ? 'حساب جديد' : 'تعديل حساب'}
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setEditor(null)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--accent)] text-[var(--accent-ink)] font-semibold disabled:opacity-50"
              disabled={busy}
              onClick={() => void save()}
            >
              حفظ
            </button>
          </>
        }
      >
        <div className="space-y-3">
          {editor?.mode === 'create' && (
            <>
              <label className="block text-xs font-semibold">الحساب الأب</label>
              <SearchableSelect
                ariaLabel="الحساب الأب"
                value={form.parentCode}
                onChange={(v) => setForm((f) => ({ ...f, parentCode: v }))}
                options={[{ value: '', label: 'بدون أب' }, ...parentOptions]}
              />
              <label className="block text-xs font-semibold">رمز الحساب</label>
              <input
                className="w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm num"
                value={form.code}
                onChange={(e) => setForm((f) => ({ ...f, code: e.target.value }))}
              />
            </>
          )}
          <label className="block text-xs font-semibold">الاسم بالعربية</label>
          <input
            className="w-full rounded-lg border border-[var(--line)] bg-[var(--raised)] px-2 py-1.5 text-sm"
            value={form.nameAr}
            onChange={(e) => setForm((f) => ({ ...f, nameAr: e.target.value }))}
          />
          <label className="inline-flex items-center gap-2 text-xs font-semibold">
            <input
              type="checkbox"
              checked={form.posting}
              onChange={(e) => setForm((f) => ({ ...f, posting: e.target.checked }))}
            />
            حساب ترحيل (وليس تجميعيًا فقط)
          </label>
        </div>
      </Dialog>

      <Dialog
        open={Boolean(confirmStop)}
        onClose={() => setConfirmStop(null)}
        title="إيقاف الحساب"
        description="الحساب المستخدم لا يُحذف نهائيًا؛ سيتم إيقافه ومنع الترحيل الجديد عليه مع حفظ تاريخه. متابعة؟"
        actions={
          <>
            <button type="button" className="px-3 py-1.5 text-xs rounded-lg border border-[var(--line)]" onClick={() => setConfirmStop(null)}>
              رجوع
            </button>
            <button
              type="button"
              className="px-3 py-1.5 text-xs rounded-lg bg-[var(--danger)] text-white font-semibold"
              onClick={async () => {
                if (!confirmStop) return;
                try {
                  await api.deactivateFinanceCoa(confirmStop.code);
                  toast('تم إيقاف الحساب', 'success');
                  setConfirmStop(null);
                  await load();
                } catch (err: unknown) {
                  toast(err instanceof Error ? err.message : 'تعذر الإيقاف', 'danger');
                }
              }}
            >
              إيقاف
            </button>
          </>
        }
      >
        <p className="text-sm">{confirmStop?.code} — {confirmStop?.nameAr}</p>
      </Dialog>
    </div>
  );
}
