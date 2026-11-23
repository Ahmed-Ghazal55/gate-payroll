'use client';

import React, { useEffect, useMemo, useState } from 'react';
import {
  api,
  type PublicEvaluationSchema,
  ApiError,
} from '../../lib/api';
import {
  NATIONALITY_SUGGESTIONS,
  nationalityIdFromLabel,
  normalizeEvalDigits,
  localizedLabel,
  platformDisplayName,
  suggestedChipsFor,
  sectionChipKindFromKey,
  appendSuggestedChip,
  EVAL_IDENTITY_STORAGE_KEY,
  type EvalAttachmentDraft,
  type EvalLanguage,
} from './evaluation-helpers';

type StaffDraft = {
  reason: string;
  ratings: Record<string, number>;
  notes: string;
  complaint: boolean;
  complaintDetails: string;
  attachments: EvalAttachmentDraft[];
};

type AppDraft = {
  reason: string;
  overall: number;
  factorRatings: Record<string, number>;
  notes: string;
  attachments: EvalAttachmentDraft[];
};

type FinanceDraft = {
  reason: string;
  overall: number;
  factorRatings: Record<string, number>;
  notes: string;
  complaint: boolean;
  complaintDetails: string;
  attachments: EvalAttachmentDraft[];
};

type Step = 'notice' | 'identity' | 'staff' | 'app' | 'finance' | 'done';

const COPY: Record<EvalLanguage, Record<string, string>> = {
  ar: {
    noticeContinue: 'متابعة',
    identityTitle: 'بياناتك',
    cityTitle: 'مدينة العمل',
    staffProgress: 'موظف',
    appTitle: 'تقييم المنصة',
    financeTitle: 'تقييم القسم المالي',
    next: 'التالي',
    submitBtn: 'إرسال التقييم',
    submitting: 'جاري الإرسال…',
    chipsHint: 'اضغط لإضافة ملاحظة (يمكنك التعديل بعد ذلك)',
    missingIdentity: 'أكمل الإقامة والاسم والمدينة والجنسية ومنصات العمل.',
    unansweredStaff: 'أجب عن بطاقة الموظف الحالية.',
    unansweredApp: 'أجب عن بطاقة المنصة.',
    unansweredFinance: 'أجب عن بطاقة المالية.',
    missingComplaint: 'اكتب تفاصيل الشكوى (٥ أحرف على الأقل).',
    recorded: 'تم تسجيل تقييمك. شكرًا لك.',
    receipt: 'احتفظ بإيصال التعديل:',
  },
  en: {
    noticeContinue: 'Continue',
    identityTitle: 'Your details',
    cityTitle: 'Work city',
    staffProgress: 'Staff',
    appTitle: 'Platform rating',
    financeTitle: 'Finance department rating',
    next: 'Next',
    submitBtn: 'Submit evaluation',
    submitting: 'Submitting…',
    chipsHint: 'Tap to add a note (still editable)',
    missingIdentity: 'Complete iqama, name, city, nationality and work apps.',
    unansweredStaff: 'Answer the current staff card.',
    unansweredApp: 'Answer the platform card.',
    unansweredFinance: 'Answer the finance card.',
    missingComplaint: 'Write complaint details (at least 5 characters).',
    recorded: 'Your evaluation was recorded. Thank you.',
    receipt: 'Keep your edit receipt:',
  },
  ur: {
    noticeContinue: 'جاری رکھیں',
    identityTitle: 'آپ کی تفصیلات',
    cityTitle: 'کام کا شہر',
    staffProgress: 'عملہ',
    appTitle: 'پلیٹ فارم درجہ بندی',
    financeTitle: 'مالیہ کی درجہ بندی',
    next: 'اگلا',
    submitBtn: 'درجہ بندی جمع کریں',
    submitting: 'جمع ہو رہا ہے…',
    chipsHint: 'نوٹ شامل کرنے کے لیے دبائیں (بعد میں ترمیم ممکن)',
    missingIdentity: 'اقامہ، نام، شہر، قومیت اور پلیٹ فارمز مکمل کریں۔',
    unansweredStaff: 'موجودہ عملہ کارڈ کا جواب دیں۔',
    unansweredApp: 'پلیٹ فارم کارڈ کا جواب دیں۔',
    unansweredFinance: 'مالیہ کارڈ کا جواب دیں۔',
    missingComplaint: 'شکایت کی تفصیل لکھیں (کم از کم ۵ حروف)۔',
    recorded: 'آپ کی درجہ بندی محفوظ ہو گئی۔ شکریہ۔',
    receipt: 'ترمیم کی رسید محفوظ رکھیں:',
  },
};

function ChipRow({
  chips,
  onPick,
  hint,
}: {
  chips: string[];
  onPick: (chip: string) => void;
  hint: string;
}) {
  return (
    <div className="space-y-1">
      <p className="text-xs text-slate-500">{hint}</p>
      <div className="flex flex-wrap gap-2">
        {chips.map((chip) => (
          <button
            key={chip}
            type="button"
            className="rounded-full border border-slate-300 bg-white px-3 py-1 text-xs text-slate-700"
            data-testid="eval-suggested-chip"
            onClick={() => onPick(chip)}
          >
            {chip}
          </button>
        ))}
      </div>
    </div>
  );
}

export function EvaluationSectionFlow({
  token,
  schema,
  lang,
  mode,
  sessionToken,
  lockedIqama,
}: {
  token: string;
  schema: PublicEvaluationSchema;
  lang: EvalLanguage;
  mode: 'public' | 'app';
  sessionToken?: string;
  lockedIqama?: boolean;
}) {
  const t = (key: string) => COPY[lang][key] || COPY.ar[key] || key;
  const sectionKey = String((schema as { sectionKey?: string }).sectionKey || '');
  const includeFinance = Boolean((schema as { includeFinanceCard?: boolean }).includeFinanceCard);
  const chipKind = sectionChipKindFromKey(sectionKey);
  const staffChips = suggestedChipsFor(chipKind === 'app' ? 'staff' : chipKind, lang);
  const appChips = suggestedChipsFor('app', lang);
  const financeChips = suggestedChipsFor('finance', lang);

  const [step, setStep] = useState<Step>('notice');
  const [iqama, setIqama] = useState('');
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [nationality, setNationality] = useState('');
  const [phone, setPhone] = useState('');
  const [workApps, setWorkApps] = useState<string[]>([]);
  const [staffIndex, setStaffIndex] = useState(0);
  const [staffAnswers, setStaffAnswers] = useState<Record<string, StaffDraft>>({});
  const [appAnswers, setAppAnswers] = useState<Record<string, AppDraft>>({});
  const [financeAnswer, setFinanceAnswer] = useState<FinanceDraft>({
    reason: '',
    overall: 0,
    factorRatings: {},
    notes: '',
    complaint: false,
    complaintDetails: '',
    attachments: [],
  });
  const [formError, setFormError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [receipt, setReceipt] = useState('');

  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      const raw = localStorage.getItem(EVAL_IDENTITY_STORAGE_KEY);
      if (!raw) return;
      const saved = JSON.parse(raw) as {
        iqama?: string;
        name?: string;
        nationality?: string;
        phone?: string;
        workApps?: string[];
      };
      if (saved.iqama && !lockedIqama) setIqama(saved.iqama);
      if (saved.name) setName(saved.name);
      if (saved.nationality) setNationality(saved.nationality);
      if (saved.phone) setPhone(saved.phone);
      if (Array.isArray(saved.workApps)) setWorkApps(saved.workApps.filter((a) => typeof a === 'string'));
    } catch {}
  }, [lockedIqama]);

  const cityStaff = useMemo(() => {
    const list = schema.staff || [];
    if (!city) return list;
    return list.filter((s) => {
      const cities = (s.cities || []) as string[];
      return cities.includes(city);
    });
  }, [schema.staff, city]);

  const currentStaff = cityStaff[staffIndex];

  const isStaffComplete = (id: string) => {
    const a = staffAnswers[id];
    if (!a) return false;
    if (a.complaint && (a.complaintDetails || '').trim().length < 5) return false;
    if (a.reason) return (schema.staffReasons || []).some((r) => r.id === a.reason);
    return (schema.staffDimensions || []).every((d) => {
      const v = Number(a.ratings?.[d.id]);
      return Number.isInteger(v) && v >= (schema.staffRatingMin ?? 1) && v <= (schema.staffRatingMax ?? 10);
    });
  };

  const isAppComplete = (appId: string) => {
    const a = appAnswers[appId];
    if (!a) return false;
    if (a.reason) return (schema.appReasons || []).some((r) => r.id === a.reason);
    return Number.isInteger(a.overall) && a.overall >= 1 && a.overall <= 5;
  };

  const isFinanceComplete = () => {
    if (financeAnswer.complaint && financeAnswer.complaintDetails.trim().length < 5) return false;
    if (financeAnswer.reason) {
      const reasons = ((schema as { financeReasons?: { id: string }[] }).financeReasons || []).map((r) => r.id);
      return reasons.includes(financeAnswer.reason);
    }
    return Number.isInteger(financeAnswer.overall) && financeAnswer.overall >= 1 && financeAnswer.overall <= 5;
  };

  const updateStaff = (id: string, updater: (prev: StaffDraft) => StaffDraft) => {
    setStaffAnswers((prev) => {
      const cur = prev[id] || {
        reason: '',
        ratings: {},
        notes: '',
        complaint: false,
        complaintDetails: '',
        attachments: [],
      };
      return { ...prev, [id]: updater(cur) };
    });
    setFormError('');
  };

  const saveIdentity = () => {
    try {
      localStorage.setItem(
        EVAL_IDENTITY_STORAGE_KEY,
        JSON.stringify({
          iqama: normalizeEvalDigits(iqama),
          name: name.trim(),
          nationality: nationality.trim(),
          phone: phone.trim(),
          workApps,
        })
      );
    } catch {}
  };

  const goAfterIdentity = () => {
    const cleanIqama = normalizeEvalDigits(iqama);
    if (
      !/^[0-9]{10}$/.test(cleanIqama) ||
      !name.trim() ||
      !city ||
      !nationality.trim() ||
      workApps.length === 0
    ) {
      setFormError(t('missingIdentity'));
      return;
    }
    saveIdentity();
    setFormError('');
    if (cityStaff.length === 0) {
      if ((schema.apps || []).length > 0) setStep('app');
      else if (includeFinance) setStep('finance');
      else void submitAll();
      return;
    }
    setStaffIndex(0);
    setStep('staff');
  };

  const advanceStaff = () => {
    if (!currentStaff || !isStaffComplete(currentStaff.id)) {
      setFormError(t('unansweredStaff'));
      return;
    }
    const ans = staffAnswers[currentStaff.id];
    if (ans?.complaint && (ans.complaintDetails || '').trim().length < 5) {
      setFormError(t('missingComplaint'));
      return;
    }
    setFormError('');
    if (staffIndex + 1 < cityStaff.length) {
      setStaffIndex(staffIndex + 1);
      return;
    }
    if ((schema.apps || []).length > 0) setStep('app');
    else if (includeFinance) setStep('finance');
    else void submitAll();
  };

  const advanceApp = () => {
    const appId = schema.apps?.[0];
    if (!appId || !isAppComplete(appId)) {
      setFormError(t('unansweredApp'));
      return;
    }
    setFormError('');
    if (includeFinance) setStep('finance');
    else void submitAll();
  };

  const submitAll = async () => {
    if (includeFinance && !isFinanceComplete()) {
      setFormError(t('unansweredFinance'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    const payload: Record<string, unknown> = {
      iqama: normalizeEvalDigits(iqama),
      name: name.trim(),
      city,
      nationality: nationalityIdFromLabel(nationality.trim()),
      phone: phone.trim(),
      workApps,
      staffAnswers: cityStaff.map((s) => {
        const a = staffAnswers[s.id] || {
          reason: '',
          ratings: {},
          notes: '',
          complaint: false,
          complaintDetails: '',
          attachments: [],
        };
        return {
          staffId: s.id,
          reason: a.reason,
          ...(a.reason === '' ? { ratings: a.ratings } : {}),
          notes: a.notes.trim(),
          complaint: Boolean(a.complaint),
          complaintDetails: a.complaint ? a.complaintDetails.trim() : '',
          attachments: a.attachments || [],
        };
      }),
      appAnswers: (schema.apps || []).map((app) => {
        const a = appAnswers[app] || {
          reason: '',
          overall: 0,
          factorRatings: {},
          notes: '',
          attachments: [],
        };
        return {
          app,
          reason: a.reason,
          ...(a.reason === '' ? { overall: a.overall, factorRatings: a.factorRatings } : {}),
          notes: a.notes.trim(),
          attachments: a.attachments || [],
        };
      }),
    };
    if (includeFinance) {
      payload.financeAnswer = {
        reason: financeAnswer.reason,
        ...(financeAnswer.reason === ''
          ? { overall: financeAnswer.overall, factorRatings: financeAnswer.factorRatings }
          : {}),
        notes: financeAnswer.notes.trim(),
        complaint: Boolean(financeAnswer.complaint),
        complaintDetails: financeAnswer.complaint ? financeAnswer.complaintDetails.trim() : '',
        attachments: financeAnswer.attachments || [],
      };
    }
    try {
      const res =
        mode === 'app' &&
        sessionToken &&
        (sessionToken.startsWith('eves_') || sessionToken.startsWith('rmob_'))
          ? await api.submitRiderMobileEvaluation(token, payload, sessionToken)
          : await api.submitPublicEvaluation(token, payload);
      saveIdentity();
      setReceipt(res.receipt);
      setStep('done');
    } catch (err: unknown) {
      setFormError(err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'error');
    } finally {
      setSubmitting(false);
    }
  };

  if (step === 'done') {
    return (
      <div className="space-y-3 p-4" data-testid="eval-section-done">
        <p className="text-lg font-semibold text-emerald-700">{t('recorded')}</p>
        <p className="text-sm text-slate-600">{t('receipt')}</p>
        <code className="block break-all rounded bg-slate-100 p-2 text-xs">{receipt}</code>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg space-y-4 p-4" data-testid="eval-section-flow">
      {step === 'notice' && (
        <div className="space-y-4">
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
            {lang === 'en' ? schema.noticeEn : lang === 'ur' ? schema.noticeUr : schema.noticeAr}
          </div>
          <h1 className="text-xl font-bold text-slate-900">{schema.title}</h1>
          <button
            type="button"
            className="w-full rounded-lg bg-teal-700 px-4 py-3 text-white"
            onClick={() => setStep('identity')}
          >
            {t('noticeContinue')}
          </button>
        </div>
      )}

      {step === 'identity' && (
        <div className="space-y-3">
          <h2 className="text-lg font-semibold">{t('identityTitle')}</h2>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'iqama'), lang)}
            <input
              className="mt-1 w-full rounded border px-3 py-2"
              value={iqama}
              readOnly={lockedIqama}
              onChange={(e) => setIqama(normalizeEvalDigits(e.target.value))}
            />
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'name'), lang)}
            <input className="mt-1 w-full rounded border px-3 py-2" value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <label className="block text-sm">
            {t('cityTitle')}
            <select className="mt-1 w-full rounded border px-3 py-2" value={city} onChange={(e) => setCity(e.target.value)}>
              <option value="">—</option>
              {(schema.cities || []).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'nationality'), lang)}
            <input
              className="mt-1 w-full rounded border px-3 py-2"
              list="eval-nationalities"
              value={nationality}
              onChange={(e) => setNationality(e.target.value)}
            />
            <datalist id="eval-nationalities">
              {(NATIONALITY_SUGGESTIONS[lang] || []).map((n) => (
                <option key={n} value={n} />
              ))}
            </datalist>
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'phone'), lang)}
            <input className="mt-1 w-full rounded border px-3 py-2" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </label>
          <fieldset className="space-y-1">
            <legend className="text-sm font-medium">
              {localizedLabel(schema.identityFields?.find((f) => f.id === 'workApps'), lang)}
            </legend>
            {['Jahez', 'Keeta', 'HungerStation', 'Ninja', 'Chefz', 'Amazon'].map((app) => (
              <label key={app} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={workApps.includes(app)}
                  onChange={(e) =>
                    setWorkApps((prev) =>
                      e.target.checked ? [...prev, app] : prev.filter((x) => x !== app)
                    )
                  }
                />
                {platformDisplayName(app, lang)}
              </label>
            ))}
          </fieldset>
          <button type="button" className="w-full rounded-lg bg-teal-700 px-4 py-3 text-white" onClick={goAfterIdentity}>
            {t('next')}
          </button>
        </div>
      )}

      {step === 'staff' && currentStaff && (
        <div className="space-y-3" data-testid="eval-section-staff-step">
          <p className="text-sm text-slate-500">
            {t('staffProgress')} {staffIndex + 1}/{cityStaff.length}
          </p>
          <h2 className="text-lg font-semibold">{currentStaff.name}</h2>
          <select
            className="w-full rounded border px-3 py-2"
            value={staffAnswers[currentStaff.id]?.reason || ''}
            onChange={(e) =>
              updateStaff(currentStaff.id, (prev) => ({
                ...prev,
                reason: e.target.value,
                ratings: e.target.value ? {} : prev.ratings,
              }))
            }
          >
            <option value="">—</option>
            {(schema.staffReasons || []).map((r) => (
              <option key={r.id} value={r.id}>
                {localizedLabel(r, lang)}
              </option>
            ))}
          </select>
          {!(staffAnswers[currentStaff.id]?.reason) &&
            (schema.staffDimensions || []).map((d) => (
              <label key={d.id} className="block text-sm">
                {localizedLabel(d, lang)}
                <input
                  type="number"
                  min={schema.staffRatingMin ?? 1}
                  max={schema.staffRatingMax ?? 10}
                  className="mt-1 w-full rounded border px-3 py-2"
                  value={staffAnswers[currentStaff.id]?.ratings?.[d.id] ?? ''}
                  onChange={(e) =>
                    updateStaff(currentStaff.id, (prev) => ({
                      ...prev,
                      ratings: { ...prev.ratings, [d.id]: Number(e.target.value) },
                    }))
                  }
                />
              </label>
            ))}
          <textarea
            className="w-full rounded border px-3 py-2"
            rows={3}
            value={staffAnswers[currentStaff.id]?.notes || ''}
            onChange={(e) => updateStaff(currentStaff.id, (prev) => ({ ...prev, notes: e.target.value }))}
          />
          <ChipRow
            chips={staffChips}
            hint={t('chipsHint')}
            onPick={(chip) =>
              updateStaff(currentStaff.id, (prev) => ({
                ...prev,
                notes: appendSuggestedChip(prev.notes, chip),
              }))
            }
          />
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={Boolean(staffAnswers[currentStaff.id]?.complaint)}
              onChange={(e) => updateStaff(currentStaff.id, (prev) => ({ ...prev, complaint: e.target.checked }))}
            />
            شكوى / Complaint
          </label>
          {staffAnswers[currentStaff.id]?.complaint && (
            <>
              <textarea
                className="w-full rounded border px-3 py-2"
                rows={2}
                value={staffAnswers[currentStaff.id]?.complaintDetails || ''}
                onChange={(e) =>
                  updateStaff(currentStaff.id, (prev) => ({ ...prev, complaintDetails: e.target.value }))
                }
              />
              <ChipRow
                chips={staffChips}
                hint={t('chipsHint')}
                onPick={(chip) =>
                  updateStaff(currentStaff.id, (prev) => ({
                    ...prev,
                    complaintDetails: appendSuggestedChip(prev.complaintDetails, chip),
                  }))
                }
              />
            </>
          )}
          <button type="button" className="w-full rounded-lg bg-teal-700 px-4 py-3 text-white" onClick={advanceStaff}>
            {t('next')}
          </button>
        </div>
      )}

      {step === 'app' && schema.apps?.[0] && (
        <div className="space-y-3" data-testid="eval-section-app-step">
          <h2 className="text-lg font-semibold">
            {t('appTitle')}: {platformDisplayName(schema.apps[0], lang)}
          </h2>
          {(() => {
            const appId = schema.apps[0]!;
            const ans = appAnswers[appId] || {
              reason: '',
              overall: 0,
              factorRatings: {},
              notes: '',
              attachments: [],
            };
            return (
              <>
                <select
                  className="w-full rounded border px-3 py-2"
                  value={ans.reason}
                  onChange={(e) =>
                    setAppAnswers((prev) => ({
                      ...prev,
                      [appId]: { ...ans, reason: e.target.value, overall: e.target.value ? 0 : ans.overall },
                    }))
                  }
                >
                  <option value="">—</option>
                  {(schema.appReasons || []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {localizedLabel(r, lang)}
                    </option>
                  ))}
                </select>
                {!ans.reason && (
                  <label className="block text-sm">
                    Overall 1–5
                    <input
                      type="number"
                      min={1}
                      max={5}
                      className="mt-1 w-full rounded border px-3 py-2"
                      value={ans.overall || ''}
                      onChange={(e) =>
                        setAppAnswers((prev) => ({
                          ...prev,
                          [appId]: { ...ans, overall: Number(e.target.value) },
                        }))
                      }
                    />
                  </label>
                )}
                <textarea
                  className="w-full rounded border px-3 py-2"
                  rows={3}
                  value={ans.notes}
                  onChange={(e) =>
                    setAppAnswers((prev) => ({ ...prev, [appId]: { ...ans, notes: e.target.value } }))
                  }
                />
                <ChipRow
                  chips={appChips}
                  hint={t('chipsHint')}
                  onPick={(chip) =>
                    setAppAnswers((prev) => ({
                      ...prev,
                      [appId]: { ...ans, notes: appendSuggestedChip(ans.notes, chip) },
                    }))
                  }
                />
              </>
            );
          })()}
          <button type="button" className="w-full rounded-lg bg-teal-700 px-4 py-3 text-white" onClick={advanceApp}>
            {t('next')}
          </button>
        </div>
      )}

      {step === 'finance' && (
        <div className="space-y-3" data-testid="eval-finance-card">
          <h2 className="text-lg font-semibold">{t('financeTitle')}</h2>
          <select
            className="w-full rounded border px-3 py-2"
            value={financeAnswer.reason}
            onChange={(e) =>
              setFinanceAnswer((prev) => ({
                ...prev,
                reason: e.target.value,
                overall: e.target.value ? 0 : prev.overall,
              }))
            }
          >
            <option value="">—</option>
            {(((schema as { financeReasons?: { id: string; labelAr?: string; labelEn?: string; labelUr?: string }[] })
              .financeReasons) || []).map((r) => (
              <option key={r.id} value={r.id}>
                {localizedLabel(r, lang)}
              </option>
            ))}
          </select>
          {!financeAnswer.reason && (
            <>
              <label className="block text-sm">
                Overall 1–5
                <input
                  type="number"
                  min={1}
                  max={5}
                  className="mt-1 w-full rounded border px-3 py-2"
                  value={financeAnswer.overall || ''}
                  onChange={(e) => setFinanceAnswer((prev) => ({ ...prev, overall: Number(e.target.value) }))}
                />
              </label>
              {(((schema as { financeFactors?: { id: string; labelAr?: string; labelEn?: string; labelUr?: string }[] })
                .financeFactors) || []).map((f) => (
                <label key={f.id} className="block text-sm">
                  {localizedLabel(f, lang)}
                  <input
                    type="number"
                    min={1}
                    max={5}
                    className="mt-1 w-full rounded border px-3 py-2"
                    value={financeAnswer.factorRatings[f.id] ?? ''}
                    onChange={(e) =>
                      setFinanceAnswer((prev) => ({
                        ...prev,
                        factorRatings: { ...prev.factorRatings, [f.id]: Number(e.target.value) },
                      }))
                    }
                  />
                </label>
              ))}
            </>
          )}
          <textarea
            className="w-full rounded border px-3 py-2"
            rows={3}
            value={financeAnswer.notes}
            onChange={(e) => setFinanceAnswer((prev) => ({ ...prev, notes: e.target.value }))}
          />
          <ChipRow
            chips={financeChips}
            hint={t('chipsHint')}
            onPick={(chip) =>
              setFinanceAnswer((prev) => ({ ...prev, notes: appendSuggestedChip(prev.notes, chip) }))
            }
          />
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={financeAnswer.complaint}
              onChange={(e) => setFinanceAnswer((prev) => ({ ...prev, complaint: e.target.checked }))}
            />
            شكوى / Complaint
          </label>
          {financeAnswer.complaint && (
            <>
              <textarea
                className="w-full rounded border px-3 py-2"
                rows={2}
                value={financeAnswer.complaintDetails}
                onChange={(e) => setFinanceAnswer((prev) => ({ ...prev, complaintDetails: e.target.value }))}
              />
              <ChipRow
                chips={financeChips}
                hint={t('chipsHint')}
                onPick={(chip) =>
                  setFinanceAnswer((prev) => ({
                    ...prev,
                    complaintDetails: appendSuggestedChip(prev.complaintDetails, chip),
                  }))
                }
              />
            </>
          )}
          <button
            type="button"
            className="w-full rounded-lg bg-teal-700 px-4 py-3 text-white"
            disabled={submitting}
            onClick={() => void submitAll()}
          >
            {submitting ? t('submitting') : t('submitBtn')}
          </button>
        </div>
      )}

      {formError && (
        <p className="text-sm text-red-600" role="alert">
          {formError}
        </p>
      )}
    </div>
  );
}
