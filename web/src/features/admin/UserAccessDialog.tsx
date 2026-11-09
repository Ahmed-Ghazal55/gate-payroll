'use client';

import React, { useMemo, useState } from 'react';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { api } from '../../lib/api';
import { useToast } from '../../components/ui/Toast';

export interface AccessUser {
  id: string; username: string; role: string; department?: string; rankLevel?: number; status?: string;
  permissions?: string[]; customPermissions?: string[]; deniedPermissions?: string[];
  financialScope?: Record<string, unknown>; approvalExceptionUntil?: string; approvalExceptionReason?: string;
}
export interface PermissionDef { code: string; moduleAr: string; nameAr: string; descriptionAr: string }
export interface RoleDef { code: string; nameAr: string; defaultDepartment: string; defaultRankLevel: number; summaryAr: string; defaultPermissions: string[] }
export interface AccessCatalog { roles: RoleDef[]; permissions: PermissionDef[]; departments: Array<{ code: string; nameAr: string }>; rolePermissions?: Record<string, string[]> }

type Mark = 'role' | 'added' | 'denied';

function grouped(permissions: PermissionDef[]) {
  const map = new Map<string, PermissionDef[]>();
  for (const p of permissions) map.set(p.moduleAr, [...(map.get(p.moduleAr) ?? []), p]);
  return Array.from(map.entries());
}
export function roleBaseline(catalog: AccessCatalog, code: string): string[] {
  return catalog.rolePermissions?.[code] ?? catalog.roles.find((r) => r.code === code)?.defaultPermissions ?? [];
}

export function UserAccessDialog({ user, users, catalog, isAdmin, onClose, onSaved }: {
  user: AccessUser; users: AccessUser[]; catalog: AccessCatalog; isAdmin: boolean; onClose: () => void; onSaved: () => void;
}) {
  const { toast } = useToast();
  const [role, setRole] = useState(user.role);
  const [department, setDepartment] = useState(user.department ?? '');
  const [rank, setRank] = useState(String(user.rankLevel ?? 1));
  const [added, setAdded] = useState<string[]>(user.customPermissions ?? []);
  const [denied, setDenied] = useState<string[]>(user.deniedPermissions ?? []);
  const [password, setPassword] = useState('');
  const [until, setUntil] = useState(user.approvalExceptionUntil ?? '');
  const [reason, setReason] = useState(user.approvalExceptionReason ?? '');
  const [busy, setBusy] = useState(false);

  const baseline = useMemo(() => new Set(roleBaseline(catalog, role)), [catalog, role]);
  const mark = (code: string): Mark => (denied.includes(code) ? 'denied' : added.includes(code) ? 'added' : 'role');
  const effective = (code: string) => !denied.includes(code) && (baseline.has(code) || added.includes(code));
  const setMark = (code: string, value: Mark) => {
    setAdded((p) => (value === 'added' ? Array.from(new Set([...p, code])) : p.filter((x) => x !== code)));
    setDenied((p) => (value === 'denied' ? Array.from(new Set([...p, code])) : p.filter((x) => x !== code)));
  };
  const count = catalog.permissions.filter((p) => effective(p.code)).length;
  const fullAccess = role === 'Admin' || role === 'SuperAdmin';
  const scope = user.financialScope as { companies?: unknown[]; sponsors?: unknown[] } | undefined;
  const emptyScope = !fullAccess && scope != null && (scope.companies ?? scope.sponsors ?? [1]).length === 0;

  const save = async () => {
    setBusy(true);
    try {
      const body: Record<string, unknown> = { role, department, rankLevel: Number(rank), customPermissions: added, deniedPermissions: denied };
      if (isAdmin && user.username.toLowerCase() === 'ghazal') { body.approvalExceptionUntil = until; body.approvalExceptionReason = reason; }
      await api.updateUser(user.id, body);
      toast('تم حفظ صلاحيات المستخدم. جلساته الحالية انتهت.', 'success');
      onSaved();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر الحفظ', 'danger');
    } finally {
      setBusy(false);
    }
  };
  const copyFrom = (id: string) => {
    const source = users.find((u) => u.id === id);
    if (!source) return;
    setRole(source.role); setAdded(source.customPermissions ?? []); setDenied(source.deniedPermissions ?? []);
    if (source.department) setDepartment(source.department);
  };
  const resetPassword = async () => {
    setBusy(true);
    try {
      await api.resetUserPassword(user.id, password);
      toast('تم تعيين كلمة مؤقتة؛ هيتطلب منه تغييرها عند الدخول.', 'success');
      setPassword('');
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر تعيين كلمة المرور', 'danger');
    } finally {
      setBusy(false);
    }
  };
  const field = 'w-full rounded-lg border border-[var(--line)] bg-[var(--surface)] p-2 text-xs text-[var(--ink)]';

  return (
    <Dialog open onClose={onClose} title={`صلاحيات @${user.username}`}>
      <div className="max-h-[70vh] space-y-4 overflow-y-auto">
        <div className="grid gap-3 sm:grid-cols-3">
          <label className="space-y-1 text-xs font-bold">الدور
            <SearchableSelect ariaLabel="الدور" value={role} onChange={setRole} options={catalog.roles.map((r) => ({ value: r.code, label: r.nameAr }))} />
          </label>
          <label className="space-y-1 text-xs font-bold">القسم
            <SearchableSelect ariaLabel="القسم" value={department} onChange={setDepartment} options={catalog.departments.map((d) => ({ value: d.code, label: d.nameAr }))} />
          </label>
          <label className="space-y-1 text-xs font-bold">الرتبة (1-5)
            <input aria-label="الرتبة" type="number" min={1} max={5} value={rank} onChange={(e) => setRank(e.target.value)} className={field} />
          </label>
        </div>
        <label className="block space-y-1 text-xs font-bold">نسخ الصلاحيات من مستخدم
          <SearchableSelect ariaLabel="نسخ من مستخدم" value="" onChange={copyFrom} placeholder="اكتب اسم المستخدم..." options={users.filter((u) => u.id !== user.id).map((u) => ({ value: u.id, label: u.username }))} />
        </label>
        {emptyScope && <p role="alert" className="rounded-lg bg-[var(--warning)]/10 p-2 text-xs font-bold text-[var(--ink)]">نطاق الشركات فاضي: المستخدم مش هيشوف أي بيانات مالية.</p>}
        <p id="effective-count" className="text-xs font-bold text-[var(--ink)]">الصلاحيات الفعلية: {fullAccess ? 'كاملة (مسؤول نظام)' : `${count} من ${catalog.permissions.length}`}</p>
        {!fullAccess && grouped(catalog.permissions).map(([module, list]) => (
          <fieldset key={module} className="space-y-1 rounded-lg border border-[var(--line)] p-2">
            <legend className="px-1 text-xs font-bold text-[var(--accent)]">{module}</legend>
            {list.map((p) => (
              <div key={p.code} data-permission={p.code} className="flex flex-wrap items-center justify-between gap-2 py-1 text-xs">
                <div className="min-w-0 flex-1">
                  <span className={effective(p.code) ? 'font-bold text-[var(--ink)]' : 'text-[var(--muted)]'}>{p.nameAr}</span>
                  <p className="text-[11px] text-[var(--muted)]">{p.descriptionAr}</p>
                </div>
                <select aria-label={p.nameAr} value={mark(p.code)} onChange={(e) => setMark(p.code, e.target.value as Mark)} className="rounded-md border border-[var(--line)] bg-[var(--surface)] p-1 text-[var(--ink)]">
                  <option value="role">{baseline.has(p.code) ? 'من الدور: مسموح' : 'من الدور: غير مسموح'}</option>
                  <option value="added">إضافة</option>
                  <option value="denied">منع</option>
                </select>
              </div>
            ))}
          </fieldset>
        ))}
        <div className="flex flex-wrap items-end gap-2 rounded-lg border border-[var(--line)] p-2">
          <label className="min-w-48 flex-1 space-y-1 text-xs font-bold">كلمة مرور مؤقتة (12 حرف على الأقل)
            <input aria-label="كلمة مرور مؤقتة" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} className={field} />
          </label>
          <button type="button" disabled={busy || password.length < 12} onClick={resetPassword} className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-xs font-bold disabled:opacity-50">تعيين</button>
        </div>
        {isAdmin && user.username.toLowerCase() === 'ghazal' && (
          <div id="approval-exception" className="grid gap-2 rounded-lg border border-red-500 p-2 sm:grid-cols-2">
            <p className="text-xs font-bold text-red-700 dark:text-red-300 sm:col-span-2">استثناء الاعتماد المؤقت: يلغي شرط المعتمد المختلف بس، وكل استخدام بيتسجل. لازم يتقفل قبل الصرف الحقيقي.</p>
            <label className="space-y-1 text-xs font-bold">ساري حتى (حد أقصى 120 يوم)
              <input aria-label="ساري حتى" type="date" value={until} onChange={(e) => setUntil(e.target.value)} className={field} />
            </label>
            <label className="space-y-1 text-xs font-bold">السبب
              <input aria-label="سبب الاستثناء" value={reason} onChange={(e) => setReason(e.target.value)} className={field} />
            </label>
          </div>
        )}
      </div>
      <div className="mt-4 flex justify-end gap-2">
        <button type="button" onClick={onClose} className="rounded-lg border border-[var(--line)] px-3 py-1.5 text-xs font-bold">إلغاء</button>
        <button type="button" id="user-access-save" disabled={busy} onClick={save} className="rounded-lg bg-[var(--accent)] px-3 py-1.5 text-xs font-bold text-white disabled:opacity-50">حفظ</button>
      </div>
    </Dialog>
  );
}

export function RolePermissionsEditor({ catalog, onSaved }: { catalog: AccessCatalog; onSaved: () => void }) {
  const { toast } = useToast();
  const roles = catalog.roles.filter((r) => r.code !== 'Admin' && r.code !== 'SuperAdmin');
  const [code, setCode] = useState(roles[0]?.code ?? '');
  const [selected, setSelected] = useState<string[]>(roleBaseline(catalog, roles[0]?.code ?? ''));
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const pick = (next: string) => { setCode(next); setSelected(roleBaseline(catalog, next)); };
  const save = async () => {
    setBusy(true);
    try {
      await api.saveRolePermissions(code, selected, reason.trim());
      toast('تم حفظ صلاحيات الدور. جلسات مستخدميه انتهت.', 'success');
      setReason(''); onSaved();
    } catch (err: unknown) {
      toast(err instanceof Error ? err.message : 'تعذر الحفظ', 'danger');
    } finally {
      setBusy(false);
    }
  };
  return (
    <section id="role-editor" className="space-y-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4">
      <h3 className="text-sm font-bold text-[var(--ink)]">صلاحيات الأدوار</h3>
      <div className="flex flex-wrap items-center gap-2">
        <div className="min-w-64"><SearchableSelect ariaLabel="الدور" value={code} onChange={pick} options={roles.map((r) => ({ value: r.code, label: r.nameAr }))} /></div>
        <input aria-label="سبب التعديل" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="سبب التعديل (مطلوب)" className="min-w-64 rounded-lg border border-[var(--line)] bg-[var(--surface)] p-2 text-xs text-[var(--ink)]" />
        <button type="button" disabled={busy || reason.trim().length < 3} onClick={save} className="rounded-lg bg-[var(--accent)] px-3 py-2 text-xs font-bold text-white disabled:opacity-50">حفظ الدور</button>
      </div>
      {grouped(catalog.permissions).map(([module, list]) => (
        <fieldset key={module} className="grid gap-1 rounded-lg border border-[var(--line)] p-2 sm:grid-cols-2">
          <legend className="px-1 text-xs font-bold text-[var(--accent)]">{module}</legend>
          {list.map((p) => (
            <label key={p.code} title={p.descriptionAr} className="flex items-center gap-2 text-xs">
              <input type="checkbox" checked={selected.includes(p.code)} onChange={(e) => setSelected((s) => (e.target.checked ? [...s, p.code] : s.filter((x) => x !== p.code)))} />
              <span>{p.nameAr}</span>
            </label>
          ))}
        </fieldset>
      ))}
    </section>
  );
}
