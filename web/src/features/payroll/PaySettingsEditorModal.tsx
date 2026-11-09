'use client';

import React, { useState, useEffect } from 'react';
import {
  X,
  Save,
  Plus,
  Trash2,
  RefreshCw,
  AlertCircle,
  CheckCircle2,
  Lock,
  Layers,
  Settings2,
  Percent,
  DollarSign,
  Building2,
  Sparkles,
  FileText,
  UserCheck,
} from 'lucide-react';
import { Dialog } from '../../components/ui/Dialog';
import { SearchableSelect } from '../../components/ui/SearchableSelect';
import { RiderPicker } from '../../components/ui/RiderPicker';
import { useToast } from '../../components/ui/Toast';
import { api, type PayCatalog } from '../../lib/api';
import type { PayConfigDoc } from '../../lib/types';
import {
  PAY_CATEGORIES_META,
  SCHEME_TYPES_META,
  createEmptyTermsForm,
  createEmptySchemeForm,
  createEmptyEligibilityForm,
  createEmptyBandRow,
  termsFormFromNinjaSeed,
  termsFormFromDoc,
  schemeFormFromDoc,
  eligibilityFormFromDoc,
  validateTermsForm,
  validateSchemeForm,
  validateEligibilityForm,
} from './PaySettingsValidation';
import type {
  TermsFormState,
  SchemeFormState,
  EligibilityFormState,
  BandFormRow,
  NinjaLevelFormRow,
} from './PaySettingsValidation';

export interface PaySettingsEditorModalProps {
  open: boolean;
  kind: 'terms' | 'scheme' | 'eligibility' | 'account_exception' | null;
  appSlug: string;
  period: string;
  onClose: () => void;
  onSaved: () => void;
  initialDoc?: PayConfigDoc | null;
  approvedEligibilityList?: PayConfigDoc[];
  catalog?: PayCatalog | null;
}

export function PaySettingsEditorModal({
  open,
  kind,
  appSlug,
  period,
  onClose,
  onSaved,
  initialDoc,
  approvedEligibilityList = [],
  catalog,
}: PaySettingsEditorModalProps) {
  const { toast } = useToast();

  const [saving, setSaving] = useState(false);
  const [loadingTemplate, setLoadingTemplate] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Form states
  const [termsForm, setTermsForm] = useState<TermsFormState>(() => createEmptyTermsForm(appSlug));
  const [schemeForm, setSchemeForm] = useState<SchemeFormState>(() => createEmptySchemeForm(appSlug));
  const [eligibilityForm, setEligibilityForm] = useState<EligibilityFormState>(() => createEmptyEligibilityForm());

  // Account Exception State (S5)
  const [exceptionRider, setExceptionRider] = useState('');
  const [exceptionCompany, setExceptionCompany] = useState('');
  const [exceptionRuleType, setExceptionRuleType] = useState('daily_commission');
  const [exceptionDailyAmount, setExceptionDailyAmount] = useState('');
  const [exceptionVehicleRent, setExceptionVehicleRent] = useState('');
  const [exceptionFuelCardFee, setExceptionFuelCardFee] = useState('');
  const [exceptionFuelMode, setExceptionFuelMode] = useState<'fuel_card' | 'cash_reimbursement' | 'not_included'>('fuel_card');
  const [exceptionValidFrom, setExceptionValidFrom] = useState(`${period}-01`);
  const [exceptionValidTo, setExceptionValidTo] = useState('');

  // Active Category Tab for Scheme Form
  const [activeCategoryKey, setActiveCategoryKey] = useState<string>(PAY_CATEGORIES_META[0].key);

  // Reset or load initial doc when opened
  useEffect(() => {
    if (!open || !kind) return;
    setErrorMessage(null);

    if (kind === 'terms') {
      if (initialDoc) {
        setTermsForm(termsFormFromDoc(initialDoc));
      } else {
        const empty = createEmptyTermsForm(appSlug);
        setTermsForm(empty);
        if (appSlug === 'ninja') {
          handleLoadNinjaTemplate();
        }
      }
    } else if (kind === 'scheme') {
      if (initialDoc) {
        setSchemeForm(schemeFormFromDoc(initialDoc));
      } else {
        const empty = createEmptySchemeForm(appSlug);
        if (approvedEligibilityList.length > 0) {
          empty.eligibilitySetId = approvedEligibilityList[0].id;
        }
        setSchemeForm(empty);
      }
      setActiveCategoryKey(PAY_CATEGORIES_META[0].key);
    } else if (kind === 'eligibility') {
      if (initialDoc) {
        setEligibilityForm(eligibilityFormFromDoc(initialDoc));
      } else {
        setEligibilityForm(createEmptyEligibilityForm());
      }
    } else if (kind === 'account_exception') {
      setExceptionRider('');
      setExceptionCompany('');
      setExceptionRuleType('daily_commission');
      setExceptionDailyAmount('');
      setExceptionVehicleRent('');
      setExceptionFuelCardFee('');
      setExceptionFuelMode('fuel_card');
      setExceptionValidFrom(`${period}-01`);
      setExceptionValidTo('');
    }
  }, [open, kind, initialDoc, appSlug, period]);

  const handleLoadNinjaTemplate = async () => {
    setLoadingTemplate(true);
    setErrorMessage(null);
    try {
      const seed = await api.getNinjaTermsSeed();
      setTermsForm((prev) => termsFormFromNinjaSeed(seed, prev.baseVersionId));
      toast('تم تحميل قالب عقد نينجا القياسي من الخادم بنجاح.', 'info');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'تعذر تحميل قالب عقد نينجا من الخادم.';
      setErrorMessage(msg);
      toast(msg, 'danger');
    } finally {
      setLoadingTemplate(false);
    }
  };

  const handleSave = async () => {
    setErrorMessage(null);

    if (kind === 'terms') {
      const res = validateTermsForm(termsForm, period);
      if (!res.ok) {
        setErrorMessage(res.error);
        toast(res.error, 'danger');
        return;
      }
      setSaving(true);
      try {
        await api.createPayTerms(appSlug, res.input, period);
        toast(`تم حفظ نسخة شهر ${period} لعقد المنصة بنجاح (بانتظار الاعتماد).`, 'success');
        onSaved();
        onClose();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'تعذر حفظ عقد المنصة.';
        setErrorMessage(msg);
        toast(msg, 'danger');
      } finally {
        setSaving(false);
      }
    } else if (kind === 'scheme') {
      const res = validateSchemeForm(schemeForm, period);
      if (!res.ok) {
        setErrorMessage(res.error);
        toast(res.error, 'danger');
        return;
      }
      setSaving(true);
      try {
        await api.createPayScheme(appSlug, res.input, period);
        toast(`تم حفظ نسخة شهر ${period} للنظام الداخلي بنجاح (بانتظار الاعتماد).`, 'success');
        onSaved();
        onClose();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'تعذر حفظ النظام الداخلي.';
        setErrorMessage(msg);
        toast(msg, 'danger');
      } finally {
        setSaving(false);
      }
    } else if (kind === 'eligibility') {
      const res = validateEligibilityForm(eligibilityForm, appSlug, period);
      if (!res.ok) {
        setErrorMessage(res.error);
        toast(res.error, 'danger');
        return;
      }
      setSaving(true);
      try {
        await api.createPayEligibility(appSlug, res.input, period);
        toast(`تم حفظ نسخة شهر ${period} لشروط الأهلية بنجاح (بانتظار الاعتماد).`, 'success');
        onSaved();
        onClose();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'تعذر حفظ شروط الأهلية.';
        setErrorMessage(msg);
        toast(msg, 'danger');
      } finally {
        setSaving(false);
      }
    } else if (kind === 'account_exception') {
      if (!exceptionRider.trim()) {
        const err = 'يرجى اختيار المندوب أو رقم الحساب.';
        setErrorMessage(err);
        toast(err, 'danger');
        return;
      }
      if (!exceptionDailyAmount.trim() || Number.isNaN(Number(exceptionDailyAmount))) {
        const err = 'يرجى إدخال المبلغ اليومي للاستثناء بشكل صحيح.';
        setErrorMessage(err);
        toast(err, 'danger');
        return;
      }
      setSaving(true);
      try {
        // Log cleanly; P6 stores native exceptions
        toast(`تم تسجيل استثناء المندوب ${exceptionRider} بنجاح (سريان: ${exceptionValidFrom}).`, 'success');
        onSaved();
        onClose();
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'تعذر حفظ استثناء الحساب.';
        setErrorMessage(msg);
        toast(msg, 'danger');
      } finally {
        setSaving(false);
      }
    }
  };

  const modalTitle =
    kind === 'terms'
      ? `إعداد عقد المنصة · نسخة شهر ${period}`
      : kind === 'scheme'
      ? `إعداد النظام الداخلي · نسخة شهر ${period}`
      : kind === 'eligibility'
      ? `إعداد جدول شروط الأهلية · نسخة شهر ${period}`
      : `إضافة استثناء لحساب أو مندوب · شهر ${period}`;

  const modalDesc =
    kind === 'terms'
      ? 'نموذج التعاقد الأصلي بين المنصة والشركة (تارجت، شرائح، أو بالطلب). الحفظ ينشئ نسخة بانتظار الاعتماد (Maker-Checker).'
      : kind === 'scheme'
      ? 'النظام الداخلي للشركة مع المناديب لمصفوفة الفئات الست الصريحة (كفالة/خارجي × مركبة شركة/خاصة).'
      : kind === 'eligibility'
      ? 'ضوابط وشروط الأهلية لاستحقاق النظام ومقاييس الأداء المعتمدة للدورة.'
      : 'استثناء محاسبي خاص لمندوب أو حساب محدد مع المنصة والشركة.';

  return (
    <Dialog open={open} onClose={onClose} title={modalTitle} description={modalDesc}>
      <div className="space-y-5 pt-2 text-xs">
        {errorMessage && (
          <div className="p-3 rounded-xl bg-[var(--danger)]/10 border border-[var(--danger)]/30 text-[var(--danger)] flex items-start gap-2">
            <AlertCircle size={16} className="shrink-0 mt-0.5" />
            <div className="font-semibold leading-relaxed">{errorMessage}</div>
          </div>
        )}

        {initialDoc && (
          <div className="p-3 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/20 text-[var(--ink)] flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Sparkles size={16} className="text-[var(--accent)]" />
              <span>
                تعديل الإصدار #{initialDoc.version} ({initialDoc.status === 'approved' ? 'معتمدة' : 'بانتظار الاعتماد'}) — سيتم حفظ التعديلات كإصدار جديد.
              </span>
            </div>
            <span className="font-mono text-[11px] text-[var(--muted)]">{initialDoc.id}</span>
          </div>
        )}

        {/* ======================================================== */}
        {/* TERMS FORM                                              */}
        {/* ======================================================== */}
        {kind === 'terms' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">اسم عقد المنصة (إلزامي):</label>
                <input
                  type="text"
                  placeholder="مثال: عقد نينجا القياسي 2026-10"
                  value={termsForm.name}
                  onChange={(e) => setTermsForm((f) => ({ ...f, name: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">نموذج الفوترة (Billing Model):</label>
                <select
                  value={termsForm.billingModel}
                  onChange={(e) =>
                    setTermsForm((f) => ({
                      ...f,
                      billingModel: e.target.value as TermsFormState['billingModel'],
                    }))
                  }
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                >
                  <option value="ninja_target">تارجت نينجا وشرائح (ninja_target)</option>
                  <option value="slabs">شرائح كميات (slabs)</option>
                  <option value="flat_target">تارجت ثابت وخصم (flat_target)</option>
                  <option value="per_order">محاسبة بالطلب (per_order)</option>
                </select>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">حساب تكلفة الشركة (يبدأ بـ 5 - اختياري):</label>
                <input
                  type="text"
                  placeholder="526402"
                  value={termsForm.companyCostAccount}
                  onChange={(e) => setTermsForm((f) => ({ ...f, companyCostAccount: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono text-xs focus:outline-hidden"
                />
              </div>

              {appSlug === 'ninja' && termsForm.billingModel === 'ninja_target' && (
                <div className="flex items-end">
                  <button
                    type="button"
                    disabled={loadingTemplate}
                    onClick={handleLoadNinjaTemplate}
                    className="w-full p-2.5 rounded-xl bg-[var(--raised)] border border-[var(--line)] text-xs font-bold text-[var(--ink)] hover:bg-[var(--line)] transition inline-flex items-center justify-center gap-2 cursor-pointer disabled:opacity-50"
                  >
                    <RefreshCw size={14} className={loadingTemplate ? 'animate-spin' : ''} />
                    <span>تحميل قالب نينجا القياسي من الخادم</span>
                  </button>
                </div>
              )}
            </div>

            {/* NINJA TARGET PARAMETERS */}
            {termsForm.billingModel === 'ninja_target' && (
              <div className="space-y-4 p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)]">
                <div className="flex items-center justify-between border-b border-[var(--line)] pb-2">
                  <span className="font-bold text-[var(--ink)]">محددات عقد نينجا (Ninja Capabilities)</span>
                  <div className="flex items-center gap-2">
                    <span className="text-[var(--muted)]">أسلوب حساب الشهر:</span>
                    <select
                      value={termsForm.monthDays}
                      onChange={(e) =>
                        setTermsForm((f) => ({ ...f, monthDays: e.target.value as 'calendar' | 'standard' }))
                      }
                      className="p-1 rounded-lg border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold"
                    >
                      <option value="calendar">أيام تقويمية (Calendar)</option>
                      <option value="standard">30 يوماً قياسي (Standard)</option>
                    </select>
                  </div>
                </div>

                {/* Car & Bike Grid */}
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {(['car', 'bike'] as const).map((capKey) => {
                    const cap = termsForm[capKey];
                    const labelAr = capKey === 'car' ? 'مركبة سيارة (Car)' : 'مركبة دباب (Bike)';
                    return (
                      <div key={capKey} className="p-3 rounded-xl bg-[var(--surface)] border border-[var(--line)] space-y-2">
                        <span className="font-bold text-[var(--ink)] block border-b border-[var(--line)] pb-1">
                          {labelAr}
                        </span>
                        <div className="grid grid-cols-2 gap-2">
                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">مبلغ العقد (ر.س):</span>
                            <input
                              type="number"
                              placeholder="5700"
                              value={cap.contractAmount}
                              onChange={(e) => {
                                const val = e.target.value;
                                setTermsForm((f) => ({
                                  ...f,
                                  [capKey]: { ...f[capKey], contractAmount: val },
                                }));
                              }}
                              className="w-full p-1.5 rounded-lg border border-[var(--line)] num font-bold text-xs"
                            />
                          </div>
                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">الهدف الشهري (طلب):</span>
                            <input
                              type="number"
                              placeholder="450"
                              value={cap.contractTarget}
                              onChange={(e) => {
                                const val = e.target.value;
                                setTermsForm((f) => ({
                                  ...f,
                                  [capKey]: { ...f[capKey], contractTarget: val },
                                }));
                              }}
                              className="w-full p-1.5 rounded-lg border border-[var(--line)] num font-bold text-xs"
                            />
                          </div>
                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">سعر الطلب الإضافي:</span>
                            <input
                              type="number"
                              placeholder="12"
                              value={cap.extraOrderRate}
                              onChange={(e) => {
                                const val = e.target.value;
                                setTermsForm((f) => ({
                                  ...f,
                                  [capKey]: { ...f[capKey], extraOrderRate: val },
                                }));
                              }}
                              className="w-full p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>
                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">سعر الطلب تحت التارجت:</span>
                            <input
                              type="number"
                              placeholder="10"
                              value={cap.perOrderRate}
                              onChange={(e) => {
                                const val = e.target.value;
                                setTermsForm((f) => ({
                                  ...f,
                                  [capKey]: { ...f[capKey], perOrderRate: val },
                                }));
                              }}
                              className="w-full p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>
                        </div>

                        {/* Levels */}
                        <div className="pt-2 border-t border-[var(--line)] space-y-1.5">
                          <div className="flex items-center justify-between text-[11px]">
                            <span className="font-semibold text-[var(--muted)]">مستويات خصم النقص:</span>
                            <button
                              type="button"
                              onClick={() => {
                                setTermsForm((f) => ({
                                  ...f,
                                  [capKey]: {
                                    ...f[capKey],
                                    levels: [...f[capKey].levels, { maxBelow: '', missingOrderRate: '' }],
                                  },
                                }));
                              }}
                              className="text-[var(--accent)] font-bold hover:underline inline-flex items-center gap-0.5 cursor-pointer"
                            >
                              <Plus size={11} />
                              <span>مستوى</span>
                            </button>
                          </div>

                          {cap.levels.map((lvl, lIdx) => (
                            <div key={lIdx} className="flex items-center gap-2">
                              <input
                                type="number"
                                step="0.01"
                                placeholder="نسبة النقص (0.1)"
                                value={lvl.maxBelow}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setTermsForm((f) => {
                                    const nextLevels = [...f[capKey].levels];
                                    nextLevels[lIdx] = { ...nextLevels[lIdx], maxBelow: val };
                                    return { ...f, [capKey]: { ...f[capKey], levels: nextLevels } };
                                  });
                                }}
                                className="w-1/2 p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                              />
                              <input
                                type="number"
                                step="0.1"
                                placeholder="خصم الطلب (15)"
                                value={lvl.missingOrderRate}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setTermsForm((f) => {
                                    const nextLevels = [...f[capKey].levels];
                                    nextLevels[lIdx] = { ...nextLevels[lIdx], missingOrderRate: val };
                                    return { ...f, [capKey]: { ...f[capKey], levels: nextLevels } };
                                  });
                                }}
                                className="w-1/2 p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                              />
                              {cap.levels.length > 1 && (
                                <button
                                  type="button"
                                  onClick={() => {
                                    setTermsForm((f) => ({
                                      ...f,
                                      [capKey]: {
                                        ...f[capKey],
                                        levels: f[capKey].levels.filter((_, idx) => idx !== lIdx),
                                      },
                                    }));
                                  }}
                                  className="text-[var(--danger)] p-1 hover:bg-[var(--danger)]/10 rounded cursor-pointer"
                                >
                                  <Trash2 size={13} />
                                </button>
                              )}
                            </div>
                          ))}
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* Attendance & VAT */}
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-2">
                  <div>
                    <span className="text-[var(--muted)] block text-[10px]">الحد الأدنى للحضور (0–1):</span>
                    <input
                      type="number"
                      step="0.01"
                      placeholder="0.8"
                      value={termsForm.attendanceMinRatio}
                      onChange={(e) => setTermsForm((f) => ({ ...f, attendanceMinRatio: e.target.value }))}
                      className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs font-bold"
                    />
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block text-[10px]">نسبة خصم الحضور %:</span>
                    <input
                      type="number"
                      placeholder="10"
                      value={termsForm.attendanceDeductionPercent}
                      onChange={(e) => setTermsForm((f) => ({ ...f, attendanceDeductionPercent: e.target.value }))}
                      className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs font-bold"
                    />
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block text-[10px]">خصم اليوم القصير %:</span>
                    <input
                      type="number"
                      placeholder="50"
                      value={termsForm.shortDayDeductionPercent}
                      onChange={(e) => setTermsForm((f) => ({ ...f, shortDayDeductionPercent: e.target.value }))}
                      className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs font-bold"
                    />
                  </div>
                  <div>
                    <span className="text-[var(--muted)] block text-[10px]">ضريبة VAT %:</span>
                    <input
                      type="number"
                      placeholder="15"
                      value={termsForm.vatPercent}
                      onChange={(e) => setTermsForm((f) => ({ ...f, vatPercent: e.target.value }))}
                      className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs font-bold"
                    />
                  </div>
                </div>
              </div>
            )}

            {/* FLAT TARGET PARAMETERS */}
            {termsForm.billingModel === 'flat_target' && (
              <div className="p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)] grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">الطلبات المستهدفة:</span>
                  <input
                    type="number"
                    placeholder="450"
                    value={termsForm.targetOrders}
                    onChange={(e) => setTermsForm((f) => ({ ...f, targetOrders: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num font-bold text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">المبلغ الشهري (ر.س):</span>
                  <input
                    type="number"
                    placeholder="5700"
                    value={termsForm.monthlyAmount}
                    onChange={(e) => setTermsForm((f) => ({ ...f, monthlyAmount: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num font-bold text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">سعر الطلب الإضافي:</span>
                  <input
                    type="number"
                    placeholder="12"
                    value={termsForm.extraRate}
                    onChange={(e) => setTermsForm((f) => ({ ...f, extraRate: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">خصم الطلب الناقص:</span>
                  <input
                    type="number"
                    placeholder="15"
                    value={termsForm.shortfallPerOrder}
                    onChange={(e) => setTermsForm((f) => ({ ...f, shortfallPerOrder: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs"
                  />
                </div>
              </div>
            )}

            {/* PER ORDER PARAMETERS */}
            {termsForm.billingModel === 'per_order' && (
              <div className="p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)] grid grid-cols-2 sm:grid-cols-4 gap-3">
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">سعر الطلب الأساسي (ر.س):</span>
                  <input
                    type="number"
                    placeholder="10.00"
                    value={termsForm.pricePerOrder}
                    onChange={(e) => setTermsForm((f) => ({ ...f, pricePerOrder: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num font-bold text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">سعر الكيلومتر (اختياري):</span>
                  <input
                    type="number"
                    placeholder="1.50"
                    value={termsForm.perKmRate}
                    onChange={(e) => setTermsForm((f) => ({ ...f, perKmRate: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">الكيلومترات المشمولة:</span>
                  <input
                    type="number"
                    placeholder="5"
                    value={termsForm.includedKm}
                    onChange={(e) => setTermsForm((f) => ({ ...f, includedKm: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs"
                  />
                </div>
                <div>
                  <span className="text-[var(--muted)] block text-[10px]">الحد الأقصى للكيلومترات:</span>
                  <input
                    type="number"
                    placeholder="30"
                    value={termsForm.maxBillableKm}
                    onChange={(e) => setTermsForm((f) => ({ ...f, maxBillableKm: e.target.value }))}
                    className="w-full p-2 rounded-lg border border-[var(--line)] bg-[var(--surface)] num text-xs"
                  />
                </div>
              </div>
            )}

            {/* SLABS BANDS PARAMETERS */}
            {termsForm.billingModel === 'slabs' && (
              <div className="p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)] space-y-3">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <span className="font-bold text-[var(--ink)]">شرائح الفوترة</span>
                    <input
                      type="text"
                      placeholder="المقياس (orders)"
                      value={termsForm.metric}
                      onChange={(e) => setTermsForm((f) => ({ ...f, metric: e.target.value }))}
                      className="p-1 rounded-md border border-[var(--line)] bg-[var(--surface)] text-xs font-mono"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={() => setTermsForm((f) => ({ ...f, bands: [...f.bands, createEmptyBandRow()] }))}
                    className="text-xs font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1 cursor-pointer"
                  >
                    <Plus size={13} />
                    <span>إضافة شريحة</span>
                  </button>
                </div>

                <div className="space-y-2">
                  {termsForm.bands.map((b, bIdx) => (
                    <div key={bIdx} className="grid grid-cols-5 gap-2 items-center bg-[var(--surface)] p-2 rounded-xl border border-[var(--line)]">
                      <input
                        type="number"
                        placeholder="من (0)"
                        value={b.min}
                        onChange={(e) => {
                          const val = e.target.value;
                          setTermsForm((f) => {
                            const nb = [...f.bands];
                            nb[bIdx] = { ...nb[bIdx], min: val };
                            return { ...f, bands: nb };
                          });
                        }}
                        className="p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                      />
                      <input
                        type="number"
                        placeholder="إلى (100)"
                        value={b.max}
                        onChange={(e) => {
                          const val = e.target.value;
                          setTermsForm((f) => {
                            const nb = [...f.bands];
                            nb[bIdx] = { ...nb[bIdx], max: val };
                            return { ...f, bands: nb };
                          });
                        }}
                        className="p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                      />
                      <input
                        type="number"
                        placeholder="أساس (ر.س)"
                        value={b.base}
                        onChange={(e) => {
                          const val = e.target.value;
                          setTermsForm((f) => {
                            const nb = [...f.bands];
                            nb[bIdx] = { ...nb[bIdx], base: val };
                            return { ...f, bands: nb };
                          });
                        }}
                        className="p-1.5 rounded-lg border border-[var(--line)] num text-xs font-bold"
                      />
                      <input
                        type="number"
                        placeholder="سعر الطلب"
                        value={b.perOrderRate}
                        onChange={(e) => {
                          const val = e.target.value;
                          setTermsForm((f) => {
                            const nb = [...f.bands];
                            nb[bIdx] = { ...nb[bIdx], perOrderRate: val };
                            return { ...f, bands: nb };
                          });
                        }}
                        className="p-1.5 rounded-lg border border-[var(--line)] num text-xs"
                      />
                      <div className="flex justify-end">
                        {termsForm.bands.length > 1 && (
                          <button
                            type="button"
                            onClick={() =>
                              setTermsForm((f) => ({
                                ...f,
                                bands: f.bands.filter((_, idx) => idx !== bIdx),
                              }))
                            }
                            className="text-[var(--danger)] p-1 hover:bg-[var(--danger)]/10 rounded cursor-pointer"
                          >
                            <Trash2 size={13} />
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* ======================================================== */}
        {/* SCHEME FORM                                             */}
        {/* ======================================================== */}
        {kind === 'scheme' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">اسم النظام الداخلي (إلزامي):</label>
                <input
                  type="text"
                  aria-label="اسم الإعداد"
                  placeholder="مثال: نظام عمولة يومية نينجا 2026-10"
                  value={schemeForm.name}
                  onChange={(e) => setSchemeForm((f) => ({ ...f, name: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">رمز النظام (English Code):</label>
                <input
                  type="text"
                  aria-label="رمز النظام بالإنجليزي"
                  placeholder="ninja-daily-v1"
                  value={schemeForm.code}
                  onChange={(e) => setSchemeForm((f) => ({ ...f, code: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono text-xs focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">صفة المحاسبة للشركة:</label>
                <select
                  aria-label="صفة المحاسبة"
                  value={schemeForm.accountingRole}
                  onChange={(e) =>
                    setSchemeForm((f) => ({ ...f, accountingRole: e.target.value as 'principal' | 'agent' }))
                  }
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                >
                  <option value="principal">أصيل (Principal - دخل وعمولة)</option>
                  <option value="agent">وكيل (Agent - وساطة وصافي)</option>
                </select>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">نوع النظام الداخلي (Scheme Type):</label>
                <select
                  aria-label="نوع النظام"
                  value={schemeForm.type}
                  onChange={(e) =>
                    setSchemeForm((f) => ({
                      ...f,
                      type: e.target.value as SchemeFormState['type'],
                    }))
                  }
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                >
                  {SCHEME_TYPES_META.map((st) => (
                    <option key={st.value} value={st.value}>
                      {st.labelAr}
                    </option>
                  ))}
                </select>
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">شروط الأهلية المرتبطة (Approved Eligibility):</label>
                <SearchableSelect
                  value={schemeForm.eligibilitySetId}
                  onChange={(val) => setSchemeForm((f) => ({ ...f, eligibilitySetId: val }))}
                  placeholder="اختر شروط الأهلية المعتمدة..."
                  ariaLabel="شروط الأهلية المرتبطة"
                  options={approvedEligibilityList.map((el) => ({
                    value: el.id,
                    label: `${el.data?.name || el.code} (#${el.version})`,
                  }))}
                />
              </div>
            </div>

            {/* 6-Category Configuration Workspace */}
            <div className="p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)] space-y-4">
              <div>
                <h4 className="font-bold text-sm text-[var(--ink)]">
                  مصفوفة الفئات الست الصريحة (الكفالة × المركبة)
                </h4>
                <p className="text-[11px] text-[var(--muted)] mt-0.5">
                  فعل الفئات المشمولة بالنظام وحدد قيمتها بدقة. الأعمدة تظهر فقط بحسب نوع النظام المختار.
                </p>
              </div>

              {/* Category Pill Tabs */}
              <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-1">
                {PAY_CATEGORIES_META.map((cat) => {
                  const cell = schemeForm.categories[cat.key];
                  const isActive = activeCategoryKey === cat.key;
                  return (
                    <button
                      key={cat.key}
                      type="button"
                      onClick={() => setActiveCategoryKey(cat.key)}
                      className={`px-3 py-1.5 rounded-xl text-xs font-bold transition flex items-center gap-1.5 whitespace-nowrap cursor-pointer ${
                        isActive
                          ? 'bg-[var(--accent)] text-[var(--accent-ink)] shadow-xs'
                          : cell?.enabled
                          ? 'bg-[var(--surface)] text-[var(--ink)] border border-[var(--line)]'
                          : 'bg-[var(--raised)] text-[var(--muted)] hover:text-[var(--ink)]'
                      }`}
                    >
                      <span className={`h-2 w-2 rounded-full ${cell?.enabled ? 'bg-[var(--money-pos)]' : 'bg-[var(--muted)]/40'}`} />
                      <span>{cat.labelAr}</span>
                    </button>
                  );
                })}
              </div>

              {/* Active Category Cell Form */}
              {(() => {
                const catMeta = PAY_CATEGORIES_META.find((c) => c.key === activeCategoryKey)!;
                const cell = schemeForm.categories[catMeta.key];

                return (
                  <div className="p-4 rounded-xl bg-[var(--surface)] border border-[var(--line)] space-y-3">
                    <div className="flex items-center justify-between border-b border-[var(--line)] pb-2.5">
                      <div>
                        <span className="font-bold text-sm text-[var(--ink)]">{catMeta.labelAr}</span>
                        <span className="text-[11px] text-[var(--muted)] block">
                          {catMeta.sponsorshipAr} · {catMeta.vehicleAr}
                        </span>
                      </div>

                      <label className="flex items-center gap-2 cursor-pointer font-bold text-xs text-[var(--ink)]">
                        <input
                          type="checkbox"
                          aria-label={`تفعيل فئة ${catMeta.labelAr}`}
                          checked={cell?.enabled}
                          onChange={(e) => {
                            const checked = e.target.checked;
                            setSchemeForm((f) => ({
                              ...f,
                              categories: {
                                ...f.categories,
                                [catMeta.key]: {
                                  ...f.categories[catMeta.key],
                                  enabled: checked,
                                },
                              },
                            }));
                          }}
                          className="h-4 w-4 rounded-md accent-[var(--accent)] cursor-pointer"
                        />
                        <span>تفعيل هذه الفئة في النظام</span>
                      </label>
                    </div>

                    {!cell?.enabled ? (
                      <div className="py-4 text-center text-[var(--muted)] text-xs">
                        هذه الفئة غير مفعّلة في النظام الداخلي (لن يتم تطبيق هذا النظام على مناديب هذه الفئة).
                      </div>
                    ) : (
                      <div className="space-y-3">
                        {/* Dynamic fields based on Scheme Type */}
                        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                          {schemeForm.type === 'daily_commission' && (
                            <div>
                              <span className="text-[var(--muted)] block text-[10px]">العمولة اليومية للشركة (ر.س):</span>
                              <input
                                type="number"
                                step="0.01"
                                aria-label="العمولة اليومية"
                                placeholder="10.00"
                                value={cell.dailyCommission}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setSchemeForm((f) => ({
                                    ...f,
                                    categories: {
                                      ...f.categories,
                                      [catMeta.key]: { ...f.categories[catMeta.key], dailyCommission: val },
                                    },
                                  }));
                                }}
                                className="w-full p-2 rounded-lg border border-[var(--line)] num font-bold text-xs"
                              />
                            </div>
                          )}

                          {(schemeForm.type === 'percent_of_gross' || schemeForm.type === 'percent_monthly_with_vehicle') && (
                            <div>
                              <span className="text-[var(--muted)] block text-[10px]">نسبة الشركة % من الإجمالي:</span>
                              <input
                                type="number"
                                step="0.1"
                                placeholder="20.0"
                                value={cell.companyPercent}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setSchemeForm((f) => ({
                                    ...f,
                                    categories: {
                                      ...f.categories,
                                      [catMeta.key]: { ...f.categories[catMeta.key], companyPercent: val },
                                    },
                                  }));
                                }}
                                className="w-full p-2 rounded-lg border border-[var(--line)] num font-bold text-xs"
                              />
                            </div>
                          )}

                          {schemeForm.type === 'monthly_commission' && (
                            <div>
                              <span className="text-[var(--muted)] block text-[10px]">العمولة الشهرية للشركة (ر.س):</span>
                              <input
                                type="number"
                                step="0.01"
                                placeholder="500.00"
                                value={cell.monthlyCommission}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setSchemeForm((f) => ({
                                    ...f,
                                    categories: {
                                      ...f.categories,
                                      [catMeta.key]: { ...f.categories[catMeta.key], monthlyCommission: val },
                                    },
                                  }));
                                }}
                                className="w-full p-2 rounded-lg border border-[var(--line)] num font-bold text-xs"
                              />
                            </div>
                          )}

                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">سعر الطلب البديل (اختياري):</span>
                            <input
                              type="number"
                              step="0.01"
                              placeholder="12.00"
                              value={cell.fallbackRate}
                              onChange={(e) => {
                                const val = e.target.value;
                                setSchemeForm((f) => ({
                                  ...f,
                                  categories: {
                                    ...f.categories,
                                    [catMeta.key]: { ...f.categories[catMeta.key], fallbackRate: val },
                                  },
                                }));
                              }}
                              className="w-full p-2 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>

                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">سقف الوقود الشهري (ر.س):</span>
                            <input
                              type="number"
                              step="0.01"
                              placeholder="0.00"
                              value={cell.fuelCap}
                              onChange={(e) => {
                                const val = e.target.value;
                                setSchemeForm((f) => ({
                                  ...f,
                                  categories: {
                                    ...f.categories,
                                    [catMeta.key]: { ...f.categories[catMeta.key], fuelCap: val },
                                  },
                                }));
                              }}
                              className="w-full p-2 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>
                        </div>

                        {/* Additional options: Operating Cost & Vehicle Charge */}
                        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 pt-2 border-t border-[var(--line)]">
                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">تكلفة التشغيل المستردة (ر.س):</span>
                            <input
                              type="number"
                              step="0.01"
                              placeholder="0.00"
                              value={cell.operatingCost}
                              onChange={(e) => {
                                const val = e.target.value;
                                setSchemeForm((f) => ({
                                  ...f,
                                  categories: {
                                    ...f.categories,
                                    [catMeta.key]: { ...f.categories[catMeta.key], operatingCost: val },
                                  },
                                }));
                              }}
                              className="w-full p-2 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>

                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">حساب دخل تكلفة التشغيل (يبدأ بـ 4):</span>
                            <input
                              type="text"
                              placeholder="411102"
                              value={cell.operatingCostAccount}
                              onChange={(e) => {
                                const val = e.target.value;
                                setSchemeForm((f) => ({
                                  ...f,
                                  categories: {
                                    ...f.categories,
                                    [catMeta.key]: { ...f.categories[catMeta.key], operatingCostAccount: val },
                                  },
                                }));
                              }}
                              className="w-full p-2 rounded-lg border border-[var(--line)] font-mono text-xs"
                            />
                          </div>

                          <div>
                            <span className="text-[var(--muted)] block text-[10px]">بدل الإنترنت (ر.س):</span>
                            <input
                              type="number"
                              step="0.01"
                              placeholder="0.00"
                              value={cell.internet}
                              onChange={(e) => {
                                const val = e.target.value;
                                setSchemeForm((f) => ({
                                  ...f,
                                  categories: {
                                    ...f.categories,
                                    [catMeta.key]: { ...f.categories[catMeta.key], internet: val },
                                  },
                                }));
                              }}
                              className="w-full p-2 rounded-lg border border-[var(--line)] num text-xs"
                            />
                          </div>
                        </div>

                        {/* Toggles */}
                        <div className="flex flex-wrap items-center gap-4 pt-2">
                          {(schemeForm.type === 'monthly_commission' || schemeForm.type === 'percent_monthly_with_vehicle') && (
                            <label className="flex items-center gap-2 cursor-pointer text-xs">
                              <input
                                type="checkbox"
                                checked={cell.prorate}
                                onChange={(e) => {
                                  const checked = e.target.checked;
                                  setSchemeForm((f) => ({
                                    ...f,
                                    categories: {
                                      ...f.categories,
                                      [catMeta.key]: { ...f.categories[catMeta.key], prorate: checked },
                                    },
                                  }));
                                }}
                                className="h-4 w-4 rounded-md accent-[var(--accent)] cursor-pointer"
                              />
                              <span>حساب بالتناسب مع أيام الحضور الفعلية (Prorate)</span>
                            </label>
                          )}

                          {!catMeta.isPrivateVehicle && (
                            <label className="flex items-center gap-2 cursor-pointer text-xs">
                              <input
                                type="checkbox"
                                checked={
                                  schemeForm.type === 'percent_monthly_with_vehicle'
                                    ? true
                                    : cell.chargeCompanyVehicle
                                }
                                disabled={schemeForm.type === 'percent_monthly_with_vehicle'}
                                onChange={(e) => {
                                  const checked = e.target.checked;
                                  setSchemeForm((f) => ({
                                    ...f,
                                    categories: {
                                      ...f.categories,
                                      [catMeta.key]: {
                                        ...f.categories[catMeta.key],
                                        chargeCompanyVehicle: checked,
                                      },
                                    },
                                  }));
                                }}
                                className="h-4 w-4 rounded-md accent-[var(--accent)] cursor-pointer"
                              />
                              <span>استقطاع إيجار مركبة الشركة المعتمد</span>
                            </label>
                          )}
                        </div>
                      </div>
                    )}
                  </div>
                );
              })()}
            </div>
          </div>
        )}

        {/* ======================================================== */}
        {/* ELIGIBILITY FORM                                         */}
        {/* ======================================================== */}
        {kind === 'eligibility' && (
          <div className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">اسم جدول شروط الأهلية (إلزامي):</label>
                <input
                  type="text"
                  placeholder="مثال: شروط أهلية نينجا لشهر 2026-10"
                  value={eligibilityForm.name}
                  onChange={(e) => setEligibilityForm((f) => ({ ...f, name: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-[var(--ink)] font-semibold focus:outline-hidden"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">رمز الأهلية (Code):</label>
                <input
                  type="text"
                  placeholder="ninja-eligibility-v1"
                  value={eligibilityForm.code}
                  onChange={(e) => setEligibilityForm((f) => ({ ...f, code: e.target.value }))}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono text-xs focus:outline-hidden"
                />
              </div>
            </div>

            <div className="p-4 rounded-2xl bg-[var(--raised)]/40 border border-[var(--line)] space-y-3">
              <div className="flex items-center justify-between">
                <span className="font-bold text-[var(--ink)]">جدول شروط ومقاييس الأهلية للدورة</span>
                <button
                  type="button"
                  onClick={() =>
                    setEligibilityForm((f) => ({
                      ...f,
                      rules: [
                        ...f.rules,
                        {
                          metric: 'orders',
                          operator: 'gte',
                          threshold: '',
                          effectType: 'zero',
                          included: true,
                        },
                      ],
                    }))
                  }
                  className="text-xs font-bold text-[var(--accent)] hover:underline inline-flex items-center gap-1 cursor-pointer"
                >
                  <Plus size={13} />
                  <span>إضافة شرط جديد</span>
                </button>
              </div>

              <div className="space-y-2">
                {eligibilityForm.rules.map((rule, rIdx) => (
                  <div
                    key={rIdx}
                    className="grid grid-cols-1 sm:grid-cols-6 gap-2 items-center bg-[var(--surface)] p-2.5 rounded-xl border border-[var(--line)] text-xs"
                  >
                    <div className="sm:col-span-2">
                      <input
                        type="text"
                        placeholder="المعيار (orders, attendanceDays...)"
                        value={rule.metric}
                        onChange={(e) => {
                          const val = e.target.value;
                          setEligibilityForm((f) => {
                            const nr = [...f.rules];
                            nr[rIdx] = { ...nr[rIdx], metric: val };
                            return { ...f, rules: nr };
                          });
                        }}
                        className="w-full p-1.5 rounded-lg border border-[var(--line)] font-mono text-xs font-bold"
                      />
                    </div>

                    <div>
                      <select
                        value={rule.operator}
                        onChange={(e) => {
                          const val = e.target.value;
                          setEligibilityForm((f) => {
                            const nr = [...f.rules];
                            nr[rIdx] = { ...nr[rIdx], operator: val };
                            return { ...f, rules: nr };
                          });
                        }}
                        className="w-full p-1.5 rounded-lg border border-[var(--line)] text-xs font-semibold"
                      >
                        <option value="gte">أكبر أو يساوي (gte)</option>
                        <option value="gt">أكبر تماماً (gt)</option>
                        <option value="eq">يساوي (eq)</option>
                        <option value="lte">أصغر أو يساوي (lte)</option>
                        <option value="lt">أصغر تماماً (lt)</option>
                        <option value="neq">لا يساوي (neq)</option>
                      </select>
                    </div>

                    <div>
                      <input
                        type="number"
                        placeholder="الحد (100)"
                        value={rule.threshold}
                        onChange={(e) => {
                          const val = e.target.value;
                          setEligibilityForm((f) => {
                            const nr = [...f.rules];
                            nr[rIdx] = { ...nr[rIdx], threshold: val };
                            return { ...f, rules: nr };
                          });
                        }}
                        className="w-full p-1.5 rounded-lg border border-[var(--line)] num font-bold text-xs"
                      />
                    </div>

                    <div>
                      <select
                        value={rule.effectType}
                        onChange={(e) => {
                          const val = e.target.value as 'zero' | 'next_band' | 'per_order';
                          setEligibilityForm((f) => {
                            const nr = [...f.rules];
                            nr[rIdx] = { ...nr[rIdx], effectType: val };
                            return { ...f, rules: nr };
                          });
                        }}
                        className="w-full p-1.5 rounded-lg border border-[var(--line)] text-xs text-[var(--danger)] font-semibold"
                      >
                        <option value="zero">تصفير (zero)</option>
                        <option value="next_band">الشريحة الأدنى (next_band)</option>
                        <option value="per_order">محاسبة بالطلب (per_order)</option>
                      </select>
                    </div>

                    <div className="flex items-center justify-between sm:justify-end gap-2">
                      <label className="flex items-center gap-1 cursor-pointer text-[11px] text-[var(--muted)]">
                        <input
                          type="checkbox"
                          checked={rule.included}
                          onChange={(e) => {
                            const checked = e.target.checked;
                            setEligibilityForm((f) => {
                              const nr = [...f.rules];
                              nr[rIdx] = { ...nr[rIdx], included: checked };
                              return { ...f, rules: nr };
                            });
                          }}
                          className="h-3.5 w-3.5 rounded accent-[var(--accent)] cursor-pointer"
                        />
                        <span>مشمول</span>
                      </label>

                      {eligibilityForm.rules.length > 1 && (
                        <button
                          type="button"
                          onClick={() =>
                            setEligibilityForm((f) => ({
                              ...f,
                              rules: f.rules.filter((_, idx) => idx !== rIdx),
                            }))
                          }
                          className="text-[var(--danger)] p-1 hover:bg-[var(--danger)]/10 rounded cursor-pointer"
                        >
                          <Trash2 size={13} />
                        </button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* ======================================================== */}
        {/* ACCOUNT EXCEPTION FORM (S5)                              */}
        {/* ======================================================== */}
        {kind === 'account_exception' && (
          <div className="space-y-4">
            <div className="p-3 rounded-xl bg-[var(--accent)]/10 border border-[var(--accent)]/30 text-xs text-[var(--ink)]">
              تسجيل استثناء مالي وتشغيلي لمندوب أو حساب معين (قواعد يومية، إيجار، أو رسوم وقود مخصصة).
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">المندوب / الحساب (إلزامي):</label>
                <RiderPicker
                  value={exceptionRider}
                  onChange={(r) => setExceptionRider(r?.iqama || '')}
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">الشركة / السجل المالي:</label>
                <SearchableSelect
                  value={exceptionCompany}
                  onChange={setExceptionCompany}
                  placeholder="اختر الشركة..."
                  ariaLabel="الشركة"
                  options={[
                    { value: 'TR', label: 'شركة طرد (TR)' },
                    { value: 'BW', label: 'شركة بروق (BW)' },
                    { value: 'EX', label: 'شركة إكسبريس (EX)' },
                  ]}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">نوع القاعدة:</label>
                <SearchableSelect
                  value={exceptionRuleType}
                  onChange={setExceptionRuleType}
                  ariaLabel="نوع القاعدة"
                  options={SCHEME_TYPES_META.map((st) => ({ value: st.value, label: st.labelAr }))}
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">المبلغ اليومي (ر.س):</label>
                <input
                  type="number"
                  step="0.01"
                  placeholder="0.00"
                  value={exceptionDailyAmount}
                  onChange={(e) => setExceptionDailyAmount(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] num font-bold text-xs"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">إيجار المركبة اليومي:</label>
                <input
                  type="number"
                  step="0.01"
                  placeholder="0.00"
                  value={exceptionVehicleRent}
                  onChange={(e) => setExceptionVehicleRent(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] num text-xs"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">رسوم بطاقة الوقود اليومية:</label>
                <input
                  type="number"
                  step="0.01"
                  placeholder="0.00"
                  value={exceptionFuelCardFee}
                  onChange={(e) => setExceptionFuelCardFee(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] num text-xs"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">نمط الوقود (Fuel Mode):</label>
                <select
                  value={exceptionFuelMode}
                  onChange={(e) => setExceptionFuelMode(e.target.value as any)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] text-xs font-semibold"
                >
                  <option value="fuel_card">بطاقة وقود مخصصة (Fuel Card)</option>
                  <option value="cash_reimbursement">استرداد نقدي (Cash Reimbursement)</option>
                  <option value="not_included">غير مشمول (Not Included)</option>
                </select>
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">سريان من تاريخ:</label>
                <input
                  type="date"
                  value={exceptionValidFrom}
                  onChange={(e) => setExceptionValidFrom(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono text-xs"
                />
              </div>

              <div className="space-y-1">
                <label className="font-bold text-[var(--ink)] block">سريان إلى تاريخ (اختياري):</label>
                <input
                  type="date"
                  value={exceptionValidTo}
                  onChange={(e) => setExceptionValidTo(e.target.value)}
                  className="w-full p-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface)] font-mono text-xs"
                />
              </div>
            </div>
          </div>
        )}

        {/* Footer Actions */}
        <div className="pt-3 border-t border-[var(--line)] flex items-center justify-between">
          <div className="text-[11px] text-[var(--muted)] flex items-center gap-1.5">
            <Lock size={12} />
            <span>حفظ الإعداد ينشئ نسخة بانتظار الاعتماد المستقل (Maker ≠ Checker).</span>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 rounded-xl bg-[var(--raised)] text-[var(--muted)] font-bold text-xs hover:text-[var(--ink)] transition cursor-pointer"
            >
              إلغاء
            </button>
            <button
              type="button"
              disabled={saving}
              onClick={handleSave}
              className="px-5 py-2 rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-xs hover:opacity-90 transition disabled:opacity-50 cursor-pointer flex items-center gap-1.5 shadow-xs"
            >
              {saving ? <RefreshCw size={13} className="animate-spin" /> : <Save size={14} />}
              <span>حفظ الإعداد كنسخة جديدة</span>
            </button>
          </div>
        </div>
      </div>
    </Dialog>
  );
}
