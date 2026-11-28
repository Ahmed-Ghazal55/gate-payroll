'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw, GitBranch, Leaf } from 'lucide-react';
import { api, ApiError, type ApiResp } from '../../lib/api';
import { useAuth } from '../../lib/auth-context';
import { useToast } from '../../components/ui/Toast';
import { EmptyState, ErrorState, Skeleton } from '../../components/ui/EmptyState';
import { useTableAutoFilter, AutoFilterTh, AutoFilterBar } from '../../components/ui/ColumnFilterMenu';
import { formatSar } from '../../lib/money';
import { loadCompanyOptions, companyLabel } from '../../lib/company-options';
import type { CompanySponsor } from '../../lib/types';
import { SearchableSelect } from '../../components/ui/SearchableSelect';

type BoxTree = ApiResp<'/api/finance/owner-external-box/tree'>;
type BoxNode = NonNullable<BoxTree['roots']>[number] & {
  children?: BoxNode[];
  archiveRequestedBy?: string;
  purposeCode?: string;
};
type BoxEntry = NonNullable<ApiResp<'/api/finance/owner-external-box/entries'>['items']>[number] & {
  status?: string;
  companyId?: string;
  id?: string;
};

const OPENING_FEE_CODE = 'JAHEZ_ACCOUNT_OPENING_FEES';
const WALLET_TOPUP_CODE = 'WALLET_TOPUP_COLLECTIONS';

/** Defaults: rider-cash in = collection (no expense); opening-fee leaf defaults to income source.
 * Custody expense is direction out + custody source (JC1e N1 / HIGH #1). */
function defaultSourceForLeaf(leaf: BoxNode | null | undefined): string {
  const code = String(leaf?.code || '');
  const purpose = String(leaf?.purposeCode || '');
  if (code === OPENING_FEE_CODE || purpose === 'jahez_account_opening_fee') return 'jahez_account_opening_fee';
  // Wallet leaf is rider-cash collection by default — expense only when cash leaves custody (out).
  if (code === WALLET_TOPUP_CODE || purpose === 'wallet_topup_collection') return 'collection';
  return 'collection';
}

function isCustodySource(sourceKind: string): boolean {
  return (
    sourceKind === 'custody_wallet_topup' ||
    sourceKind === 'wallet_topup_from_custody' ||
    sourceKind === 'hunger_wallet_custody'
  );
}

function defaultDirectionForSource(sourceKind: string): 'in' | 'out' {
  return isCustodySource(sourceKind) ? 'out' : 'in';
}

function flatten(nodes: BoxNode[], depth = 0): Array<BoxNode & { depth: number }> {
  const out: Array<BoxNode & { depth: number }> = [];
  for (const n of nodes) {
    out.push({ ...n, depth });
    if (Array.isArray(n.children) && n.children.length) out.push(...flatten(n.children, depth + 1));
  }
  return out;
}

export function OwnerExternalBoxPanel() {
  const { can, isAdmin } = useAuth();
  const { toast } = useToast();
  const canView = can('treasury.view') || isAdmin;
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [roots, setRoots] = useState<BoxNode[]>([]);
  const [noteAr, setNoteAr] = useState('');
  const [expenseNote, setExpenseNote] = useState('');
  const [listAllowed, setListAllowed] = useState<string[]>([]);
  const [entries, setEntries] = useState<BoxEntry[]>([]);
  const [showArchived, setShowArchived] = useState(false);
  const [selectedLeaf, setSelectedLeaf] = useState<BoxNode | null>(null);
  const [companies, setCompanies] = useState<CompanySponsor[]>([]);
  const [grow, setGrow] = useState({ parentId: '', kind: 'leaf', code: '', labelAr: '' });
  const [entry, setEntry] = useState({
    occurredOn: '',
    direction: 'in',
    amount: '',
    sourceKind: 'collection',
    notes: '',
    companyId: '',
  });
  const [busy, setBusy] = useState(false);

  const selectLeaf = (leaf: BoxNode) => {
    setSelectedLeaf(leaf);
    const sourceKind = defaultSourceForLeaf(leaf);
    setEntry((e) => ({
      ...e,
      direction: defaultDirectionForSource(sourceKind),
      sourceKind,
    }));
  };

  const setSourceKind = (sourceKind: string) => {
    setEntry((e) => ({
      ...e,
      sourceKind,
      direction: defaultDirectionForSource(sourceKind),
    }));
  };

  useEffect(() => {
    void loadCompanyOptions().then((list) => {
      setCompanies(list);
      const first = list[0]?.id || list[0]?.code || '';
      if (first) setEntry((e) => (e.companyId ? e : { ...e, companyId: first }));
    });
  }, []);

  const load = useCallback(async () => {
    if (!canView) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const tree = await api.getOwnerExternalBoxTree(showArchived);
      setRoots(Array.isArray(tree.roots) ? (tree.roots as BoxNode[]) : []);
      setNoteAr(String(tree.noteAr || ''));
      setExpenseNote(String(tree.companyExpenseNoteAr || ''));
      setListAllowed(Array.isArray(tree.allowedActions) ? tree.allowedActions.map(String) : []);
      const ent = await api.listOwnerExternalBoxEntries();
      setEntries(Array.isArray(ent.items) ? ent.items : []);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'تعذر تحميل صندوق المالك');
    } finally {
      setLoading(false);
    }
  }, [canView, showArchived]);

  useEffect(() => {
    void load();
  }, [load]);

  const flat = useMemo(() => flatten(roots), [roots]);
  const cols = useMemo(
    () => [
      { key: 'label', header: 'العقدة', type: 'text' as const, getValue: (r: BoxNode & { depth: number }) => String(r.labelAr || '') },
      { key: 'code', header: 'الرمز', type: 'text' as const, getValue: (r: BoxNode & { depth: number }) => String(r.code || '') },
      { key: 'kind', header: 'النوع', type: 'text' as const, getValue: (r: BoxNode & { depth: number }) => (r.kind === 'leaf' ? 'ورقة' : 'فرع') },
      { key: 'balance', header: 'الرصيد', type: 'number' as const, getValue: (r: BoxNode & { depth: number }) => Number(r.balance || 0) },
      { key: 'archived', header: 'مؤرشف', type: 'text' as const, getValue: (r: BoxNode & { depth: number }) => (r.archivedAt ? 'نعم' : 'لا') },
    ],
    []
  );
  const filter = useTableAutoFilter(flat, cols);

  const entryCols = useMemo(
    () => [
      { key: 'occurredOn', header: 'التاريخ', type: 'date' as const, getValue: (r: BoxEntry) => String(r.occurredOn || '') },
      { key: 'direction', header: 'الاتجاه', type: 'text' as const, getValue: (r: BoxEntry) => (r.direction === 'out' ? 'خروج' : 'دخول') },
      { key: 'amount', header: 'المبلغ', type: 'number' as const, getValue: (r: BoxEntry) => Number(r.amount || 0) },
      { key: 'sourceKind', header: 'المصدر', type: 'text' as const, getValue: (r: BoxEntry) => String(r.sourceKind || '') },
      { key: 'status', header: 'الحالة', type: 'text' as const, getValue: (r: BoxEntry) => String(r.status || '') },
      { key: 'notes', header: 'ملاحظة', type: 'text' as const, getValue: (r: BoxEntry) => String(r.notes || '') },
    ],
    []
  );
  const entryFilter = useTableAutoFilter(entries, entryCols);

  if (!canView) {
    return <EmptyState title="صلاحية غير كافية" description="صندوق المالك يحتاج صلاحية عرض الخزينة." />;
  }
  if (loading) return <Skeleton className="h-40 rounded-2xl" />;
  if (error) return <ErrorState title="تعذر التحميل" message={error} onRetry={load} />;

  const doGrow = async () => {
    if (!listAllowed.includes('grow')) {
      toast('صلاحية إدارة الصندوق مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.growOwnerExternalBoxNode({
        parentId: grow.parentId.trim(),
        kind: grow.kind,
        code: grow.code.trim(),
        labelAr: grow.labelAr.trim(),
      });
      toast('تمت إضافة العقدة', 'success');
      setGrow({ parentId: '', kind: 'leaf', code: '', labelAr: '' });
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإضافة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doPost = async () => {
    if (!selectedLeaf?.id || !listAllowed.includes('post_entry')) {
      toast('اختر ورقة نشطة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.postOwnerExternalBoxEntry({
        nodeId: selectedLeaf.id,
        occurredOn: entry.occurredOn,
        direction: entry.direction,
        amount: Number(entry.amount),
        sourceKind: entry.sourceKind,
        notes: entry.notes,
        companyId: entry.companyId.trim(),
      });
      toast('تم تجهيز القيد — يلزم شخص ثانٍ للاعتماد', 'success');
      const nextSource = defaultSourceForLeaf(selectedLeaf);
      setEntry((e) => ({
        occurredOn: '',
        direction: defaultDirectionForSource(nextSource),
        amount: '',
        sourceKind: nextSource,
        notes: '',
        companyId: e.companyId,
      }));
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر القيد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doConfirmEntry = async (id: string) => {
    if (!listAllowed.includes('confirm_entry')) {
      toast('صلاحية اعتماد القيد مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.confirmOwnerExternalBoxEntry(id);
      toast('تم اعتماد القيد بشخص ثانٍ', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الاعتماد', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doReverseEntry = async (id: string) => {
    if (!listAllowed.includes('reverse_entry')) {
      toast('صلاحية القيد العكسي مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.reverseOwnerExternalBoxEntry(id);
      toast('جُهّز القيد العكسي — يلزم شخص ثانٍ للاعتماد وترحيل عكس قيد الشركة', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر التجهيز العكسي', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doCancelEntry = async (id: string) => {
    if (!listAllowed.includes('cancel_entry')) {
      toast('صلاحية إلغاء التجهيز مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.cancelOwnerExternalBoxEntry(id);
      toast('أُلغي تجهيز القيد المعلّق', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الإلغاء', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doArchive = async (id: string) => {
    setBusy(true);
    try {
      await api.archiveOwnerExternalBoxNode(id);
      toast('طُلبت الأرشفة أو أُرشفت إن كان الرصيد صفرًا', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر الأرشفة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const doConfirmArchive = async (id: string) => {
    if (!listAllowed.includes('confirm_archive')) {
      toast('صلاحية اعتماد الأرشفة مطلوبة', 'warning');
      return;
    }
    setBusy(true);
    try {
      await api.confirmOwnerExternalBoxArchive(id);
      toast('تم اعتماد الأرشفة بشخص ثانٍ', 'success');
      await load();
    } catch (err: unknown) {
      toast(err instanceof ApiError ? err.message : 'تعذر اعتماد الأرشفة', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-4 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)]">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold">صندوق المالك الخارجي (شجرة حية)</h3>
          <p className="text-xs text-[var(--muted)] mt-0.5">{noteAr}</p>
          {expenseNote ? <p className="text-xs text-[var(--muted)] mt-1">{expenseNote}</p> : null}
          <p className="text-[11px] font-semibold text-[var(--accent)] mt-1">
            نقد الورقة خارج دفاتر الشركة؛ قيود الشركة المرافقة (دخل رسوم الفتح / مصروف شحن المحافظ) تُرحَّل عند اعتماد القيد بشخص ثانٍ.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <label className="text-[11px] flex items-center gap-1">
            <input type="checkbox" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} />
            إظهار المؤرشف
          </label>
          <button type="button" onClick={() => void load()} className="inline-flex items-center gap-1 px-3 py-1.5 rounded-xl border border-[var(--line)] text-xs">
            <RefreshCw size={12} /> تحديث
          </button>
        </div>
      </div>

      <AutoFilterBar controller={filter} totalCount={flat.length} />
      <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
        <table className="w-full text-xs">
          <thead className="bg-[var(--raised)]">
            <tr>
              <AutoFilterTh controller={filter} colKey="label" className="p-2 text-start" />
              <AutoFilterTh controller={filter} colKey="code" className="p-2 text-start" />
              <AutoFilterTh controller={filter} colKey="kind" className="p-2 text-start" />
              <AutoFilterTh controller={filter} colKey="balance" className="p-2 text-start" />
              <AutoFilterTh controller={filter} colKey="archived" className="p-2 text-start" />
              <th className="p-2 text-start">إجراء</th>
            </tr>
          </thead>
          <tbody>
            {filter.rows.map((row) => {
              const actions = Array.isArray(row.allowedActions) ? row.allowedActions.map(String) : [];
              const needsConfirmArchive =
                !row.archivedAt &&
                Boolean(row.archiveRequestedBy) &&
                (actions.includes('confirm_archive') || listAllowed.includes('confirm_archive'));
              return (
                <tr key={String(row.id)} className="border-t border-[var(--line)]">
                  <td className="p-2" style={{ paddingInlineStart: `${8 + row.depth * 16}px` }}>
                    <span className="inline-flex items-center gap-1">
                      {row.kind === 'leaf' ? <Leaf size={12} /> : <GitBranch size={12} />}
                      {row.labelAr}
                    </span>
                  </td>
                  <td className="p-2 font-mono">{row.code}</td>
                  <td className="p-2">{row.kind === 'leaf' ? 'ورقة' : 'فرع'}</td>
                  <td className="p-2">{formatSar(Number(row.balance || 0))}</td>
                  <td className="p-2">{row.archivedAt ? 'نعم' : 'لا'}</td>
                  <td className="p-2">
                    {row.kind === 'leaf' && !row.archivedAt ? (
                      <button type="button" className="underline" onClick={() => selectLeaf(row)}>
                        اختيار للقيد
                      </button>
                    ) : null}
                    {row.archivedAt && listAllowed.includes('restore') ? (
                      <button
                        type="button"
                        className="underline"
                        onClick={() => void api.restoreOwnerExternalBoxNode(String(row.id)).then(load)}
                      >
                        استعادة
                      </button>
                    ) : null}
                    {needsConfirmArchive ? (
                      <button
                        type="button"
                        className="underline ms-2"
                        disabled={busy}
                        onClick={() => void doConfirmArchive(String(row.id))}
                      >
                        اعتماد الأرشفة
                      </button>
                    ) : !row.archivedAt && row.code !== 'OWNER_EXTERNAL' && listAllowed.includes('archive') && !row.archiveRequestedBy ? (
                      <button
                        type="button"
                        className="underline ms-2"
                        disabled={busy}
                        onClick={() => void doArchive(String(row.id))}
                      >
                        أرشفة
                      </button>
                    ) : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {listAllowed.includes('grow') ? (
        <div className="grid grid-cols-1 sm:grid-cols-5 gap-2 p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40">
          <input placeholder="معرّف الأب" value={grow.parentId} onChange={(e) => setGrow({ ...grow, parentId: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          <select value={grow.kind} onChange={(e) => setGrow({ ...grow, kind: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
            <option value="leaf">ورقة</option>
            <option value="branch">فرع</option>
          </select>
          <input placeholder="الرمز" value={grow.code} onChange={(e) => setGrow({ ...grow, code: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs font-mono" />
          <input placeholder="الاسم العربي" value={grow.labelAr} onChange={(e) => setGrow({ ...grow, labelAr: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          <button type="button" disabled={busy} onClick={() => void doGrow()} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            إنبات عقدة
          </button>
        </div>
      ) : null}

      {selectedLeaf ? (
        <div className="grid grid-cols-1 sm:grid-cols-6 gap-2 p-3 rounded-xl border border-[var(--line)] bg-[var(--raised)]/40">
          <div className="sm:col-span-6 text-xs">
            تجهيز قيد على الورقة: <b>{selectedLeaf.labelAr}</b> — الاعتماد بشخص ثانٍ يرحّل نقد الورقة خارج الدفاتر وأي قيد شركة مرافق.
          </div>
          <input type="date" value={entry.occurredOn} onChange={(e) => setEntry({ ...entry, occurredOn: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          <select value={entry.direction} onChange={(e) => setEntry({ ...entry, direction: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs">
            <option value="in">دخول</option>
            <option value="out">خروج</option>
          </select>
          <input placeholder="المبلغ" value={entry.amount} onChange={(e) => setEntry({ ...entry, amount: e.target.value })} className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs" />
          <select
            value={entry.sourceKind}
            onChange={(e) => setSourceKind(e.target.value)}
            className="p-2 rounded-xl border border-[var(--line)] bg-[var(--raised)] text-xs"
          >
            {selectedLeaf.code === OPENING_FEE_CODE || selectedLeaf.purposeCode === 'jahez_account_opening_fee' ? (
              <>
                <option value="jahez_account_opening_fee">رسوم فتح حساب جاهز (دخل شركة)</option>
                <option value="account_opening_fee">رسوم فتح حساب</option>
                <option value="collection">تحصيل نقدي فقط (بدون قيد شركة)</option>
              </>
            ) : selectedLeaf.code === WALLET_TOPUP_CODE || selectedLeaf.purposeCode === 'wallet_topup_collection' ? (
              <>
                <option value="collection">تحصيل نقد المندوب (دخول — بدون مصروف)</option>
                <option value="custody_wallet_topup">شحن من العهدة (خروج — مصروف شركة)</option>
                <option value="wallet_topup_from_custody">شحن محفظة من العهدة (خروج)</option>
                <option value="hunger_wallet_custody">عهدة محافظ هنقر (خروج)</option>
              </>
            ) : (
              <>
                <option value="collection">تحصيل / حركة نقدية</option>
              </>
            )}
          </select>
          <SearchableSelect
            value={entry.companyId}
            onChange={(v) => setEntry({ ...entry, companyId: v })}
            options={companies.map((c) => ({ value: c.id || c.code, label: companyLabel(c) }))}
            placeholder="الشركة (لقيود الشركة المرافقة)"
            className="text-xs"
          />
          <button type="button" disabled={busy} onClick={() => void doPost()} className="px-3 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] text-xs font-bold disabled:opacity-50">
            تجهيز القيد (بانتظار شخص ثانٍ)
          </button>
        </div>
      ) : null}

      <div>
        <h4 className="text-xs font-bold mb-2">قيود الصندوق</h4>
        <AutoFilterBar controller={entryFilter} totalCount={entries.length} />
        <div className="overflow-x-auto rounded-xl border border-[var(--line)]">
          <table className="w-full text-xs">
            <thead className="bg-[var(--raised)]">
              <tr>
                <AutoFilterTh controller={entryFilter} colKey="occurredOn" className="p-2 text-start" />
                <AutoFilterTh controller={entryFilter} colKey="direction" className="p-2 text-start" />
                <AutoFilterTh controller={entryFilter} colKey="amount" className="p-2 text-start" />
                <AutoFilterTh controller={entryFilter} colKey="sourceKind" className="p-2 text-start" />
                <AutoFilterTh controller={entryFilter} colKey="status" className="p-2 text-start" />
                <AutoFilterTh controller={entryFilter} colKey="notes" className="p-2 text-start" />
                <th className="p-2 text-start">إجراء</th>
              </tr>
            </thead>
            <tbody>
              {entryFilter.rows.map((row) => (
                <tr key={String(row.id)} className="border-t border-[var(--line)]">
                  <td className="p-2">{row.occurredOn}</td>
                  <td className="p-2">{row.direction === 'out' ? 'خروج' : 'دخول'}</td>
                  <td className="p-2">{formatSar(Number(row.amount || 0))}</td>
                  <td className="p-2">{row.sourceKind}</td>
                  <td className="p-2">{row.status === 'pending' ? 'بانتظار الاعتماد' : row.status === 'reversed' ? 'معكوس' : 'معتمد'}</td>
                  <td className="p-2">{row.notes}</td>
                  <td className="p-2">
                    {row.status === 'pending' && listAllowed.includes('confirm_entry') && row.id ? (
                      <button type="button" className="underline" disabled={busy} onClick={() => void doConfirmEntry(String(row.id))}>
                        اعتماد (شخص ثانٍ)
                      </button>
                    ) : null}
                    {row.status === 'pending' && listAllowed.includes('cancel_entry') && row.id ? (
                      <button type="button" className="underline ms-2" disabled={busy} onClick={() => void doCancelEntry(String(row.id))}>
                        إلغاء التجهيز
                      </button>
                    ) : null}
                    {row.status === 'posted' && listAllowed.includes('reverse_entry') && row.id ? (
                      <button
                        type="button"
                        className="underline ms-2"
                        disabled={busy}
                        onClick={() => void doReverseEntry(String(row.id))}
                      >
                        قيد عكسي
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
