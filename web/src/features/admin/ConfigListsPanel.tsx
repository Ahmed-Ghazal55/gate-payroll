'use client';

import React, { useState, useEffect } from 'react';
import {
  SlidersHorizontal,
  Plus,
  Edit2,
  Check,
  CheckCircle2,
  AlertTriangle,
  Clock,
  RefreshCw,
  Tag,
  Hash,
  Database,
  Layers,
  ShieldCheck,
  Loader2,
  Settings,
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import type { ConfigListGroup, ConfigListItem, ConfigFieldDef } from '../../lib/types';
import { useToast } from '../../components/ui/Toast';
import { useAuth } from '../../lib/auth-context';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';

export function ConfigListsPanel() {
  const { toast } = useToast();
  const { can, user } = useAuth();
  const canManage = can('pay.manage');
  const canApprove = can('pay.approve');

  const [groups, setGroups] = useState<ConfigListGroup[]>([]);
  const [activeGroupKey, setActiveGroupKey] = useState<string>('recharge_networks');
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [postingAccounts, setPostingAccounts] = useState<Array<{ code: string; nameAr?: string }>>([]);

  // Add / Edit Item Dialog
  const [itemDialogOpen, setItemDialogOpen] = useState<boolean>(false);
  const [editingItem, setEditingItem] = useState<ConfigListItem | null>(null);
  const [itemCode, setItemCode] = useState<string>('');
  const [itemNameAr, setItemNameAr] = useState<string>('');
  const [itemActive, setItemActive] = useState<boolean>(true);
  const [itemValues, setItemValues] = useState<Record<string, any>>({});
  const [submittingItem, setSubmittingItem] = useState<boolean>(false);

  // Add Field Dialog
  const [fieldDialogOpen, setFieldDialogOpen] = useState<boolean>(false);
  const [fieldKey, setFieldKey] = useState<string>('');
  const [fieldLabelAr, setFieldLabelAr] = useState<string>('');
  const [fieldType, setFieldType] = useState<'text' | 'number' | 'select' | 'account' | 'bool'>('text');
  const [fieldRequired, setFieldRequired] = useState<boolean>(false);
  const [fieldOptionsText, setFieldOptionsText] = useState<string>('');
  const [submittingField, setSubmittingField] = useState<boolean>(false);

  const [approvingCode, setApprovingCode] = useState<string | null>(null);

  const fetchLists = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.getConfigLists();
      setGroups(res || []);
      if (res && res.length > 0 && !res.some((g) => g.group === activeGroupKey)) {
        setActiveGroupKey(res[0].group);
      }
    } catch (err: any) {
      setError(err.message || 'تعذر تحميل القوائم والإعدادات.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLists();
    api
      .getTargetCoa()
      .then((res) => setPostingAccounts((res?.accounts || []).filter((a) => a.posting)))
      .catch(() => {});
  }, []);

  const activeGroup = groups.find((g) => g.group === activeGroupKey) || groups[0];

  const handleOpenAdd = () => {
    setEditingItem(null);
    setItemCode('');
    setItemNameAr('');
    setItemActive(true);
    setItemValues({});
    setItemDialogOpen(true);
  };

  const handleOpenEdit = (item: ConfigListItem) => {
    setEditingItem(item);
    setItemCode(item.code);
    setItemNameAr(item.nameAr);
    setItemActive(item.active);
    setItemValues(item.values || {});
    setItemDialogOpen(true);
  };

  const handleSaveItem = async () => {
    if (!itemCode.trim()) {
      toast('يرجى إدخال كود العنصر.', 'danger');
      return;
    }
    if (!itemNameAr.trim()) {
      toast('يرجى إدخال اسم العنصر بالعربي.', 'danger');
      return;
    }

    setSubmittingItem(true);
    try {
      await api.createConfigListItem(activeGroup.group, {
        code: itemCode.trim(),
        nameAr: itemNameAr.trim(),
        active: itemActive,
        values: itemValues,
      });

      toast(
        activeGroup.financial
          ? 'تم حفظ العنصر، وهو بانتظار اعتماد مسؤول مالي آخر (Maker-Checker).'
          : 'تم حفظ وتحديث العنصر بنجاح.',
        'success'
      );
      setItemDialogOpen(false);
      await fetchLists();
    } catch (err: any) {
      toast(err.message || 'تعذر حفظ العنصر.', 'danger');
    } finally {
      setSubmittingItem(false);
    }
  };

  const handleApproveItem = async (code: string) => {
    setApprovingCode(code);
    try {
      await api.approveConfigListItem(activeGroup.group, code);
      toast('تم اعتماد وتفعيل العنصر المالي بنجاح.', 'success');
      await fetchLists();
    } catch (err: any) {
      toast(err.message || 'تعذر اعتماد العنصر.', 'danger');
    } finally {
      setApprovingCode(null);
    }
  };

  const handleSaveField = async () => {
    if (!fieldKey.trim() || !fieldLabelAr.trim()) {
      toast('يرجى إدخال مفتاح الحقل واسمه بالعربي.', 'danger');
      return;
    }

    const options =
      fieldType === 'select'
        ? fieldOptionsText
            .split(',')
            .map((s) => s.trim())
            .filter(Boolean)
        : undefined;

    setSubmittingField(true);
    try {
      await api.addConfigListField(activeGroup.group, {
        key: fieldKey.trim(),
        labelAr: fieldLabelAr.trim(),
        type: fieldType,
        required: fieldRequired,
        options,
      });

      toast('تمت إضافة الحقل المخصص بنجاح.', 'success');
      setFieldDialogOpen(false);
      setFieldKey('');
      setFieldLabelAr('');
      setFieldOptionsText('');
      await fetchLists();
    } catch (err: any) {
      toast(err.message || 'تعذر إضافة الحقل المخصص.', 'danger');
    } finally {
      setSubmittingField(false);
    }
  };

  if (loading && groups.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-[var(--muted)] space-y-3">
        <Loader2 size={32} className="animate-spin text-[var(--accent)]" />
        <p className="text-xs">جاري تحميل القوائم والإعدادات (BR §27)...</p>
      </div>
    );
  }

  if (error && groups.length === 0) {
    return (
      <div className="rounded-2xl border border-[var(--danger)]/30 bg-[var(--danger)]/10 p-6 text-center space-y-3">
        <AlertTriangle size={32} className="mx-auto text-[var(--danger)]" />
        <h4 className="text-sm font-bold text-[var(--danger)]">تعذر عرض القوائم</h4>
        <p className="text-xs text-[var(--muted)]">{error}</p>
        <button
          type="button"
          onClick={fetchLists}
          className="px-4 py-2 text-xs font-bold rounded-xl bg-[var(--surface)] border border-[var(--line)] hover:bg-[var(--raised)]"
        >
          إعادة المحاولة
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Group Navigation Tabs */}
      <div className="flex items-center gap-1.5 p-1.5 rounded-2xl border border-[var(--line)] bg-[var(--surface)] overflow-x-auto no-scrollbar">
        {groups.map((g) => {
          const isActive = g.group === activeGroupKey;
          return (
            <button
              key={g.group}
              type="button"
              onClick={() => setActiveGroupKey(g.group)}
              className={`inline-flex items-center gap-2 px-3.5 py-2 text-xs font-bold rounded-xl transition whitespace-nowrap ${
                isActive
                  ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                  : 'text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)]'
              }`}
            >
              <span>{g.labelAr}</span>
              {g.financial && (
                <span
                  className={`text-[9px] px-1.5 py-0.2 rounded-md ${
                    isActive ? 'bg-black/20 text-white' : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                  }`}
                >
                  مالي
                </span>
              )}
              <span className="font-mono text-[10px] opacity-75 num">({g.items?.length || 0})</span>
            </button>
          );
        })}
      </div>

      {/* Active Group Content Header & Actions */}
      {activeGroup && (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 p-4 rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs">
            <div>
              <div className="flex items-center gap-2">
                <h3 className="text-sm sm:text-base font-bold text-[var(--ink)]">{activeGroup.labelAr}</h3>
                {activeGroup.financial && (
                  <span className="text-[11px] font-bold px-2 py-0.5 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] flex items-center gap-1">
                    <ShieldCheck size={12} />
                    <span>مجموعة مالية (تتطلب اعتماد مدقق)</span>
                  </span>
                )}
              </div>
              <p className="text-xs text-[var(--muted)] mt-0.5">
                كود المجموعة: <span className="font-mono">{activeGroup.group}</span> · {activeGroup.fields?.length || 0} حقول مخصصة
              </p>
            </div>

            <div className="flex items-center gap-2">
              {canManage && (
                <>
                  <button
                    type="button"
                    onClick={() => setFieldDialogOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] hover:bg-[var(--raised)] shadow-2xs transition"
                  >
                    <Plus size={14} className="text-[var(--accent)]" />
                    <span>إضافة حقل</span>
                  </button>

                  <button
                    type="button"
                    onClick={handleOpenAdd}
                    className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 shadow-xs transition"
                  >
                    <Plus size={14} />
                    <span>إضافة عنصر</span>
                  </button>
                </>
              )}

              <button
                type="button"
                onClick={fetchLists}
                className="p-1.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                title="تحديث البيانات"
              >
                <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
              </button>
            </div>
          </div>

          {/* Items Table */}
          <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-xs overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-start text-xs border-collapse">
                <thead>
                  <tr className="bg-[var(--raised)] border-b border-[var(--line)] text-[var(--muted)] font-semibold">
                    <th className="py-2.5 px-3 text-start">الكود</th>
                    <th className="py-2.5 px-3 text-start">الاسم بالعربي</th>
                    {activeGroup.fields?.map((f) => (
                      <th key={f.key} className="py-2.5 px-2 text-start">
                        {f.labelAr}
                      </th>
                    ))}
                    <th className="py-2.5 px-2 text-center">الحالة</th>
                    <th className="py-2.5 px-2 text-center">الاعتماد المالي</th>
                    <th className="py-2.5 px-3 text-end">الإجراءات</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--line)]">
                  {activeGroup.items?.length === 0 ? (
                    <tr>
                      <td colSpan={5 + (activeGroup.fields?.length || 0)} className="py-12 text-center text-xs text-[var(--muted)]">
                        لا توجد عناصر مسجلة في هذه القائمة حتى الآن.
                      </td>
                    </tr>
                  ) : (
                    activeGroup.items?.map((item) => (
                      <tr key={item.code} className="hover:bg-[var(--row-hover)] transition">
                        <td className="py-2.5 px-3 font-mono font-bold text-[var(--accent)] num">
                          {item.code}
                        </td>

                        <td className="py-2.5 px-3 font-semibold text-[var(--ink)]">
                          {item.nameAr}
                        </td>

                        {activeGroup.fields?.map((f) => (
                          <td key={f.key} className="py-2.5 px-2 text-[var(--ink)]">
                            {f.type === 'bool'
                              ? item.values?.[f.key] ? 'نعم' : 'لا'
                              : f.type === 'number'
                              ? <span className="font-mono num font-bold">{item.values?.[f.key] ?? '—'}</span>
                              : f.type === 'account'
                              ? <span className="font-mono num font-bold text-[var(--info)]">{item.values?.[f.key] ?? '—'}</span>
                              : item.values?.[f.key] ?? '—'}
                          </td>
                        ))}

                        <td className="py-2.5 px-2 text-center">
                          <span
                            className={`text-[10px] font-bold px-2 py-0.5 rounded-full ${
                              item.active
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : 'bg-[var(--muted)]/20 text-[var(--muted)]'
                            }`}
                          >
                            {item.active ? 'نشط' : 'معطل'}
                          </span>
                        </td>

                        <td className="py-2.5 px-2 text-center">
                          {item.status === 'pending_approval' ? (
                            <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-[var(--warning)]/15 text-[var(--warning)] flex items-center justify-center gap-1">
                              <Clock size={11} />
                              <span>بانتظار الاعتماد</span>
                            </span>
                          ) : (
                            <span className="text-[10px] font-bold text-[var(--money-pos)]">
                              معتمد (v{item.version || 1})
                            </span>
                          )}
                        </td>

                        <td className="py-2.5 px-3 text-end">
                          <div className="flex items-center justify-end gap-1.5">
                            {item.status === 'pending_approval' && canApprove && (
                              <button
                                type="button"
                                disabled={approvingCode === item.code}
                                onClick={() => handleApproveItem(item.code)}
                                className="px-2.5 py-1 text-[11px] font-bold rounded-lg bg-[var(--money-pos)] text-white hover:opacity-90 disabled:opacity-50 transition"
                              >
                                {approvingCode === item.code ? 'جاري الاعتماد...' : 'اعتماد'}
                              </button>
                            )}

                            {canManage && (
                              <button
                                type="button"
                                onClick={() => handleOpenEdit(item)}
                                className="p-1.5 rounded-lg border border-[var(--line)] text-[var(--muted)] hover:text-[var(--ink)] hover:bg-[var(--raised)] transition"
                                title="تعديل العنصر"
                              >
                                <Edit2 size={13} />
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* Add / Edit Item Dialog */}
      <Dialog
        open={itemDialogOpen}
        onClose={() => setItemDialogOpen(false)}
        title={editingItem ? `تعديل العنصر: ${editingItem.nameAr}` : `إضافة عنصر جديد إلى: ${activeGroup?.labelAr}`}
        description="العناصر في المجموعات المالية تحتاج موافقة مدقق ثانٍ قبل تطبيقها على المسيرات."
        actions={
          <div className="flex items-center justify-end gap-2">
            <button
              type="button"
              onClick={() => setItemDialogOpen(false)}
              className="px-3 py-1.5 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={submittingItem}
              onClick={handleSaveItem}
              className="px-4 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50"
            >
              {submittingItem ? 'جاري الحفظ...' : 'حفظ العنصر'}
            </button>
          </div>
        }
      >
        <div className="space-y-3.5 text-xs text-[var(--ink)]">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-[11px] font-bold text-[var(--ink)]">الكود البرمجي (Code):</label>
              <input
                type="text"
                disabled={Boolean(editingItem)}
                placeholder="مثلاً: STC أو 112501"
                value={itemCode}
                onChange={(e) => setItemCode(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono disabled:opacity-50"
              />
            </div>

            <div className="space-y-1">
              <label className="text-[11px] font-bold text-[var(--ink)]">الاسم بالعربي:</label>
              <input
                type="text"
                placeholder="مثلاً: باقة سوا نت لا محدود"
                value={itemNameAr}
                onChange={(e) => setItemNameAr(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
              />
            </div>
          </div>

          {/* Dynamic Fields of the Group */}
          {activeGroup?.fields?.map((f) => (
            <div key={f.key} className="space-y-1">
              <label className="text-[11px] font-bold text-[var(--ink)] flex items-center justify-between">
                <span>{f.labelAr}:</span>
                {f.required && <span className="text-[10px] text-[var(--danger)]">* إلزامي</span>}
              </label>

              {f.type === 'select' && f.options ? (
                <SearchableSelect
                  value={String(itemValues[f.key] ?? '')}
                  onChange={(val) => setItemValues({ ...itemValues, [f.key]: val })}
                  ariaLabel={f.labelAr}
                  placeholder={`-- اختر ${f.labelAr} --`}
                  options={f.options.map((opt) => ({ value: opt, label: opt }))}
                />
              ) : f.type === 'account' ? (
                <SearchableSelect
                  value={String(itemValues[f.key] ?? '')}
                  onChange={(val) => setItemValues({ ...itemValues, [f.key]: val })}
                  ariaLabel={f.labelAr}
                  placeholder="ابحث برقم الحساب أو اسمه (مثال: 112501)..."
                  options={postingAccounts.map((a) => ({
                    value: a.code,
                    label: `${a.code} — ${a.nameAr ?? ''}`,
                  }))}
                />
              ) : f.type === 'number' ? (
                <input
                  type="number"
                  step="0.01"
                  value={itemValues[f.key] ?? ''}
                  onChange={(e) => setItemValues({ ...itemValues, [f.key]: parseFloat(e.target.value) || 0 })}
                  className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono font-bold num"
                />
              ) : f.type === 'bool' ? (
                <label className="flex items-center gap-2 cursor-pointer pt-1">
                  <input
                    type="checkbox"
                    checked={Boolean(itemValues[f.key])}
                    onChange={(e) => setItemValues({ ...itemValues, [f.key]: e.target.checked })}
                    className="rounded border-[var(--line)] text-[var(--accent)]"
                  />
                  <span className="text-xs">تفعيل الخيار</span>
                </label>
              ) : (
                <input
                  type="text"
                  value={itemValues[f.key] ?? ''}
                  onChange={(e) => setItemValues({ ...itemValues, [f.key]: e.target.value })}
                  className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono"
                />
              )}
            </div>
          ))}

          <div className="pt-1">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={itemActive}
                onChange={(e) => setItemActive(e.target.checked)}
                className="rounded border-[var(--line)] text-[var(--accent)]"
              />
              <span className="text-xs font-bold text-[var(--ink)]">العنصر نشط في النظام</span>
            </label>
          </div>
        </div>
      </Dialog>

      {/* Add Field Dialog */}
      <Dialog
        open={fieldDialogOpen}
        onClose={() => setFieldDialogOpen(false)}
        title={`إضافة حقل مخصص إلى: ${activeGroup?.labelAr}`}
        description="الحقول المخصصة تتيح ربط بنود القوائم بدليل الحسابات أو خصائص تشغيلية إضافية."
        actions={
          <div className="flex items-center justify-end gap-2">
            <button
              type="button"
              onClick={() => setFieldDialogOpen(false)}
              className="px-3 py-1.5 text-xs font-semibold rounded-xl border border-[var(--line)] bg-[var(--surface)] hover:bg-[var(--raised)]"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={submittingField}
              onClick={handleSaveField}
              className="px-4 py-1.5 text-xs font-bold rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] hover:opacity-90 disabled:opacity-50"
            >
              {submittingField ? 'جاري الإضافة...' : 'إضافة الحقل'}
            </button>
          </div>
        }
      >
        <div className="space-y-3.5 text-xs text-[var(--ink)]">
          <div className="space-y-1">
            <label className="text-[11px] font-bold text-[var(--ink)]">اسم الحقل بالعربي (Label):</label>
            <input
              type="text"
              placeholder="مثلاً: رمز الحساب المحاسبي أو السرعة"
              value={fieldLabelAr}
              onChange={(e) => setFieldLabelAr(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
            />
          </div>

          <div className="space-y-1">
            <label className="text-[11px] font-bold text-[var(--ink)]">المفتاح البرمجي (Key):</label>
            <input
              type="text"
              placeholder="مثلاً: accountCode أو speed"
              value={fieldKey}
              onChange={(e) => setFieldKey(e.target.value)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-mono"
            />
          </div>

          <div className="space-y-1">
            <label className="text-[11px] font-bold text-[var(--ink)]">نوع الحقل (Type):</label>
            <select
              value={fieldType}
              onChange={(e) => setFieldType(e.target.value as any)}
              className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] text-xs"
            >
              <option value="text">نص (Text)</option>
              <option value="number">رقمي (Number)</option>
              <option value="select">قائمة اختيارات (Select)</option>
              <option value="account">حساب محاسبي (Account 1125xx)</option>
              <option value="bool">منطقي / نعم أو لا (Boolean)</option>
            </select>
          </div>

          {fieldType === 'select' && (
            <div className="space-y-1">
              <label className="text-[11px] font-bold text-[var(--ink)]">الخيارات المتاحة (مفصولة بفاصلة):</label>
              <input
                type="text"
                placeholder="خيار 1, خيار 2, خيار 3"
                value={fieldOptionsText}
                onChange={(e) => setFieldOptionsText(e.target.value)}
                className="w-full p-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs"
              />
            </div>
          )}

          <div className="pt-1">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={fieldRequired}
                onChange={(e) => setFieldRequired(e.target.checked)}
                className="rounded border-[var(--line)] text-[var(--accent)]"
              />
              <span className="text-xs font-bold text-[var(--ink)]">الحقل إلزامي عند إدخال عنصر جديد</span>
            </label>
          </div>
        </div>
      </Dialog>
    </div>
  );
}
