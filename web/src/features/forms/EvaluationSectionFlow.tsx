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
  shrinkImageForEvaluation,
  voiceFileForEvaluation,
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

type Step = 'notice' | 'identity' | 'cards' | 'review' | 'done';
type CardRef =
  | { kind: 'staff'; id: string; name: string; roles: string[] }
  | { kind: 'app'; id: string }
  | { kind: 'finance' };

type SchemaExtra = PublicEvaluationSchema & {
  sectionKey?: string;
  includeFinanceCard?: boolean;
  titleEn?: string;
  titleUr?: string;
  financeFactors?: { id: string; labelAr?: string; labelEn?: string; labelUr?: string }[];
  financeReasons?: { id: string; labelAr?: string; labelEn?: string; labelUr?: string }[];
};

const COPY: Record<EvalLanguage, Record<string, string>> = {
  ar: {
    noticeContinue: 'متابعة',
    identityTitle: 'بياناتك',
    cityTitle: 'مدينة العمل',
    cardProgress: 'بطاقة',
    cardOf: 'من',
    appTitle: 'تقييم المنصة',
    financeTitle: 'تقييم القسم المالي',
    next: 'التالي',
    prevBtn: 'السابق',
    submitBtn: 'إرسال التقييم',
    reviewTitle: 'مراجعة قبل الإرسال',
    reviewSend: 'تأكيد الإرسال',
    submitting: 'جاري الإرسال…',
    chipsHint: 'اضغط لإضافة ملاحظة (يمكنك التعديل بعد ذلك)',
    missingIdentity: 'أكمل الإقامة والاسم والمدينة والجنسية ومنصات العمل.',
    unansweredCard: 'أجب عن البطاقة الحالية قبل المتابعة.',
    missingComplaint: 'اكتب تفاصيل الشكوى (٥ أحرف على الأقل).',
    recorded: 'تم تسجيل تقييمك. شكرًا لك.',
    receipt: 'احتفظ بإيصال التعديل:',
    editBtn: 'تعديل التقييم',
    editSave: 'حفظ التعديل',
    editsLeft: 'تعديلات متبقية:',
    rateMode: 'تقييم (1–10)',
    skipLabel: 'أو اختر سبب عدم التقييم',
    notes: 'ملاحظات إضافية (اختياري)',
    complaintBtn: 'تقديم شكوى سرية على هذه البطاقة',
    complaintActive: 'شكوى سرية مفعّلة',
    complaintDetails: 'تفاصيل الشكوى السرية (مطلوب، 5 أحرف على الأقل)',
    rolesLabel: 'المهام والأدوار',
    starsLabel: 'التقييم العام (من 1 إلى 5 نجوم)',
    factorsTitle: 'عوامل إضافية (اختياري)',
    factorOptional: 'اختياري',
    imagesLabel: 'صور (حتى 2)',
    voiceLabel: 'صوت (≤60ث)',
    uploadsHint: 'صورتان كحد أقصى + تسجيل صوتي حتى 60 ثانية لكل بطاقة.',
  },
  en: {
    noticeContinue: 'Continue',
    identityTitle: 'Your details',
    cityTitle: 'Work city',
    cardProgress: 'Card',
    cardOf: 'of',
    appTitle: 'Platform rating',
    financeTitle: 'Finance department rating',
    next: 'Next',
    prevBtn: 'Back',
    submitBtn: 'Submit evaluation',
    reviewTitle: 'Review before send',
    reviewSend: 'Confirm send',
    submitting: 'Submitting…',
    chipsHint: 'Tap to add a note (still editable)',
    missingIdentity: 'Complete iqama, name, city, nationality and work apps.',
    unansweredCard: 'Answer the current card before continuing.',
    missingComplaint: 'Write complaint details (at least 5 characters).',
    recorded: 'Your evaluation was recorded. Thank you.',
    receipt: 'Keep your edit receipt:',
    editBtn: 'Edit evaluation',
    editSave: 'Save edit',
    editsLeft: 'Edits left:',
    rateMode: 'Rate (1–10)',
    skipLabel: 'Or pick a skip reason',
    notes: 'Extra notes (optional)',
    complaintBtn: 'File a confidential complaint on this card',
    complaintActive: 'Confidential complaint on',
    complaintDetails: 'Complaint details (required, at least 5 characters)',
    rolesLabel: 'Roles',
    starsLabel: 'Overall rating (1 to 5 stars)',
    factorsTitle: 'Extra factors (optional)',
    factorOptional: 'optional',
    imagesLabel: 'Images (up to 2)',
    voiceLabel: 'Voice (≤60s)',
    uploadsHint: 'Up to 2 images + 1 voice note (≤60 s) per card.',
  },
  ur: {
    noticeContinue: 'جاری رکھیں',
    identityTitle: 'آپ کی تفصیلات',
    cityTitle: 'کام کا شہر',
    cardProgress: 'کارڈ',
    cardOf: 'از',
    appTitle: 'پلیٹ فارم درجہ بندی',
    financeTitle: 'مالیہ کی درجہ بندی',
    next: 'اگلا',
    prevBtn: 'پیچھے',
    submitBtn: 'درجہ بندی جمع کریں',
    reviewTitle: 'ارسال سے پہلے جائزہ',
    reviewSend: 'ارسال کی تصدیق',
    submitting: 'جمع ہو رہا ہے…',
    chipsHint: 'نوٹ شامل کرنے کے لیے دبائیں (بعد میں ترمیم ممکن)',
    missingIdentity: 'اقامہ، نام، شہر، قومیت اور پلیٹ فارمز مکمل کریں۔',
    unansweredCard: 'جاری رکھنے سے پہلے موجودہ کارڈ کا جواب دیں۔',
    missingComplaint: 'شکایت کی تفصیل لکھیں (کم از کم ۵ حروف)۔',
    recorded: 'آپ کی درجہ بندی محفوظ ہو گئی۔ شکریہ۔',
    receipt: 'ترمیم کی رسید محفوظ رکھیں:',
    editBtn: 'درجہ بندی ترمیم کریں',
    editSave: 'ترمیم محفوظ کریں',
    editsLeft: 'باقی ترمیمات:',
    rateMode: 'درجہ بندی (1–10)',
    skipLabel: 'یا وجہ منتخب کریں',
    notes: 'اضافی نوٹس (اختیاری)',
    complaintBtn: 'اس کارڈ پر خفیہ شکایت',
    complaintActive: 'خفیہ شکایت فعال',
    complaintDetails: 'شکایت کی تفصیل (ضروری، کم از کم ۵ حروف)',
    rolesLabel: 'کردار',
    starsLabel: 'مجموعی درجہ بندی (1 تا 5 ستارے)',
    factorsTitle: 'اضافی عوامل (اختیاری)',
    factorOptional: 'اختیاری',
    imagesLabel: 'تصاویر (2 تک)',
    voiceLabel: 'آواز (≤60س)',
    uploadsHint: 'ہر کارڈ: زیادہ سے زیادہ 2 تصاویر + 1 صوتی نوٹ (≤60 سیکنڈ)۔',
  },
};

function emptyStaff(): StaffDraft {
  return { reason: '', ratings: {}, notes: '', complaint: false, complaintDetails: '', attachments: [] };
}
function emptyApp(): AppDraft {
  return { reason: '', overall: 0, factorRatings: {}, notes: '', attachments: [] };
}
function emptyFinance(): FinanceDraft {
  return {
    reason: '',
    overall: 0,
    factorRatings: {},
    notes: '',
    complaint: false,
    complaintDetails: '',
    attachments: [],
  };
}

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
      <p className="text-xs text-[var(--muted)]">{hint}</p>
      <div className="flex flex-wrap gap-2">
        {chips.map((chip) => (
          <button
            key={chip}
            type="button"
            className="rounded-full border border-[var(--line)] bg-[var(--raised)] px-3 py-1 text-xs text-[var(--ink)]"
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

function AttachmentFields({
  attachments,
  onChange,
  t,
  setError,
}: {
  attachments: EvalAttachmentDraft[];
  onChange: (next: EvalAttachmentDraft[]) => void;
  t: (k: string) => string;
  setError: (msg: string) => void;
}) {
  return (
    <div className="grid gap-2 sm:grid-cols-2 text-xs">
      <p className="sm:col-span-2 text-[11px] text-[var(--muted)]">{t('uploadsHint')}</p>
      <label className="block space-y-1">
        <span className="font-semibold text-[var(--muted)]">{t('imagesLabel')}</span>
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          multiple
          className="w-full text-[11px]"
          onChange={async (e) => {
            const files = Array.from(e.target.files || []).slice(0, 2);
            e.target.value = '';
            const images: EvalAttachmentDraft[] = [];
            for (const f of files) {
              try {
                images.push(await shrinkImageForEvaluation(f));
              } catch {
                setError(t('unansweredCard'));
              }
            }
            onChange([
              ...images,
              ...attachments.filter((a) => a.kind === 'voice'),
            ].slice(0, 3));
          }}
        />
      </label>
      <label className="block space-y-1">
        <span className="font-semibold text-[var(--muted)]">{t('voiceLabel')}</span>
        <input
          type="file"
          accept="audio/webm,audio/ogg,audio/mpeg,audio/mp4,audio/wav,audio/aac"
          className="w-full text-[11px]"
          onChange={async (e) => {
            const f = e.target.files?.[0];
            e.target.value = '';
            if (!f) return;
            try {
              const voice = await voiceFileForEvaluation(f, 30);
              onChange([
                ...attachments.filter((a) => a.kind === 'image').slice(0, 2),
                voice,
              ]);
            } catch {
              setError(t('unansweredCard'));
            }
          }}
        />
      </label>
      {attachments.length > 0 && (
        <p className="sm:col-span-2 text-[11px] text-[var(--muted)]">
          {attachments.map((a) => a.fileName).join(' · ')}
        </p>
      )}
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
  const schemaX = schema as SchemaExtra;
  const sectionKey = String(schemaX.sectionKey || '');
  const includeFinance = Boolean(schemaX.includeFinanceCard);
  const chipKind = sectionChipKindFromKey(sectionKey);
  const staffChips = suggestedChipsFor(chipKind === 'app' ? 'staff' : chipKind, lang);
  const appChips = suggestedChipsFor('app', lang);
  const financeChips = suggestedChipsFor('finance', lang);
  const sectionTitle =
    lang === 'en'
      ? schemaX.titleEn || schema.title
      : lang === 'ur'
        ? schemaX.titleUr || schema.title
        : schema.title;

  const [step, setStep] = useState<Step>('notice');
  const [iqama, setIqama] = useState('');
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [nationality, setNationality] = useState('');
  const [phone, setPhone] = useState('');
  const [workApps, setWorkApps] = useState<string[]>([]);
  const [cardIndex, setCardIndex] = useState(0);
  const [staffAnswers, setStaffAnswers] = useState<Record<string, StaffDraft>>({});
  const [appAnswers, setAppAnswers] = useState<Record<string, AppDraft>>({});
  const [financeAnswer, setFinanceAnswer] = useState<FinanceDraft>(emptyFinance());
  const [formError, setFormError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [receipt, setReceipt] = useState('');
  const [editsRemaining, setEditsRemaining] = useState(3);
  const [editing, setEditing] = useState(false);

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
    return list.filter((s) => ((s.cities || []) as string[]).includes(city));
  }, [schema.staff, city]);

  const cards: CardRef[] = useMemo(() => {
    const list: CardRef[] = cityStaff.map((s) => ({
      kind: 'staff' as const,
      id: s.id,
      name: s.name,
      roles: (s.roles || []) as string[],
    }));
    for (const app of schema.apps || []) list.push({ kind: 'app', id: app });
    if (includeFinance) list.push({ kind: 'finance' });
    return list;
  }, [cityStaff, schema.apps, includeFinance]);

  const current = cards[cardIndex];

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
      return (schemaX.financeReasons || []).some((r) => r.id === financeAnswer.reason);
    }
    return Number.isInteger(financeAnswer.overall) && financeAnswer.overall >= 1 && financeAnswer.overall <= 5;
  };

  const updateStaff = (id: string, updater: (prev: StaffDraft) => StaffDraft) => {
    setStaffAnswers((prev) => ({ ...prev, [id]: updater(prev[id] || emptyStaff()) }));
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

  const buildPayload = (): Record<string, unknown> => {
    const payload: Record<string, unknown> = {
      iqama: normalizeEvalDigits(iqama),
      name: name.trim(),
      city,
      nationality: nationalityIdFromLabel(nationality.trim()),
      phone: phone.trim(),
      workApps,
      staffAnswers: cityStaff.map((s) => {
        const a = staffAnswers[s.id] || emptyStaff();
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
        const a = appAnswers[app] || emptyApp();
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
    return payload;
  };

  const submitAll = async (asEdit: boolean) => {
    if (includeFinance && !isFinanceComplete()) {
      setFormError(t('unansweredCard'));
      return;
    }
    for (const s of cityStaff) {
      if (!isStaffComplete(s.id)) {
        setFormError(t('unansweredCard'));
        return;
      }
    }
    for (const app of schema.apps || []) {
      if (!isAppComplete(app)) {
        setFormError(t('unansweredCard'));
        return;
      }
    }
    setSubmitting(true);
    setFormError('');
    const payload = buildPayload();
    if (asEdit) payload.receipt = receipt;
    try {
      const useMob =
        mode === 'app' &&
        sessionToken &&
        (sessionToken.startsWith('eves_') || sessionToken.startsWith('rmob_'));
      const res = asEdit
        ? useMob
          ? await api.editRiderMobileEvaluation(token, payload, sessionToken)
          : await api.editPublicEvaluation(token, payload)
        : useMob
          ? await api.submitRiderMobileEvaluation(token, payload, sessionToken)
          : await api.submitPublicEvaluation(token, payload);
      saveIdentity();
      setReceipt(res.receipt);
      setEditsRemaining(typeof res.editsRemaining === 'number' ? res.editsRemaining : 3);
      setEditing(false);
      setStep('done');
    } catch (err: unknown) {
      setFormError(err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'error');
    } finally {
      setSubmitting(false);
    }
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
    setCardIndex(0);
    if (cards.length === 0) {
      setStep('review');
      return;
    }
    setStep('cards');
  };

  const currentComplete = () => {
    if (!current) return true;
    if (current.kind === 'staff') return isStaffComplete(current.id);
    if (current.kind === 'app') return isAppComplete(current.id);
    return isFinanceComplete();
  };

  const advanceCard = () => {
    if (!currentComplete()) {
      setFormError(t('unansweredCard'));
      return;
    }
    setFormError('');
    if (cardIndex + 1 < cards.length) {
      setCardIndex(cardIndex + 1);
      return;
    }
    setStep('review');
  };

  const backCard = () => {
    setFormError('');
    if (cardIndex > 0) {
      setCardIndex(cardIndex - 1);
      return;
    }
    // On edit, identity is locked — go to review instead of identity.
    setStep(editing ? 'review' : 'identity');
  };

  if (step === 'done' && !editing) {
    return (
      <div className="space-y-3 p-4" data-testid="eval-section-done">
        <p className="text-lg font-semibold text-emerald-700">{t('recorded')}</p>
        <p className="text-sm text-[var(--muted)]">{t('receipt')}</p>
        <code className="block break-all rounded bg-[var(--raised)] p-2 text-xs">{receipt}</code>
        {editsRemaining > 0 && (
          <button
            type="button"
            className="w-full rounded-lg border border-[var(--line)] px-4 py-3 text-sm font-bold"
            data-testid="eval-section-edit-btn"
            onClick={() => {
              setEditing(true);
              setStep('cards');
              setCardIndex(0);
            }}
          >
            {t('editBtn')} ({t('editsLeft')} {editsRemaining})
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-lg space-y-4 p-4" data-testid="eval-section-flow">
      <h1 className="text-xl font-bold text-[var(--ink)]">{sectionTitle}</h1>

      {step === 'notice' && (
        <div className="space-y-4">
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
            {lang === 'en' ? schema.noticeEn : lang === 'ur' ? schema.noticeUr : schema.noticeAr}
          </div>
          <button
            type="button"
            className="w-full rounded-lg bg-[var(--accent)] px-4 py-3 font-bold text-[var(--accent-ink)]"
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
            {t('cityTitle')}
            <select
              className="mt-1 w-full rounded border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
              value={city}
              onChange={(e) => setCity(e.target.value)}
              data-testid="eval-section-city"
            >
              <option value="">—</option>
              {(schema.cities || []).map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'iqama'), lang)}
            <input
              data-testid="eval-section-iqama"
              className="mt-1 w-full rounded border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
              value={iqama}
              readOnly={lockedIqama}
              onChange={(e) => setIqama(normalizeEvalDigits(e.target.value))}
            />
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'name'), lang)}
            <input
              data-testid="eval-section-name"
              className="mt-1 w-full rounded border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label className="block text-sm">
            {localizedLabel(schema.identityFields?.find((f) => f.id === 'nationality'), lang)}
            <input
              data-testid="eval-section-nationality"
              className="mt-1 w-full rounded border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
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
            <input
              className="mt-1 w-full rounded border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
            />
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
          <button
            type="button"
            className="w-full rounded-lg bg-[var(--accent)] px-4 py-3 font-bold text-[var(--accent-ink)]"
            onClick={goAfterIdentity}
          >
            {t('next')}
          </button>
        </div>
      )}

      {step === 'cards' && current && (
        <div className="space-y-3" data-testid={`eval-section-${current.kind}-step`}>
          <p className="text-sm font-bold text-[var(--muted)]" data-testid="eval-section-card-progress">
            {t('cardProgress')} {cardIndex + 1} {t('cardOf')} {cards.length}
          </p>

          {current.kind === 'staff' && (
            <>
              <h2 className="text-lg font-semibold">{current.name}</h2>
              {current.roles.length > 0 && (
                <p className="text-xs text-[var(--muted)]">
                  {t('rolesLabel')}: {current.roles.join(' · ')}
                </p>
              )}
              <label className="block space-y-1 text-xs">
                <span className="font-semibold text-[var(--muted)]">{t('skipLabel')}</span>
                <select
                  data-testid="eval-section-staff-reason"
                  className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
                  value={staffAnswers[current.id]?.reason || ''}
                  onChange={(e) =>
                    updateStaff(current.id, (prev) => ({
                      ...prev,
                      reason: e.target.value,
                      ratings: e.target.value ? {} : prev.ratings,
                    }))
                  }
                >
                  <option value="">★ {t('rateMode')}</option>
                  {(schema.staffReasons || []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {localizedLabel(r, lang)}
                    </option>
                  ))}
                </select>
              </label>
              {!(staffAnswers[current.id]?.reason) &&
                (schema.staffDimensions || []).map((d) => {
                  const score = staffAnswers[current.id]?.ratings?.[d.id] || 0;
                  return (
                    <div key={d.id} className="space-y-1">
                      <div className="flex items-center justify-between text-xs">
                        <span className="font-semibold">{localizedLabel(d, lang)}</span>
                        <span className="font-bold text-[var(--accent)]">
                          {score ? `${score} / 10` : '—'}
                        </span>
                      </div>
                      <div className="grid grid-cols-5 sm:grid-cols-10 gap-1.5">
                        {Array.from({ length: 10 }, (_, i) => i + 1).map((n) => (
                          <button
                            key={n}
                            type="button"
                            data-staff-rate={current.id}
                            data-dim={d.id}
                            data-score={n}
                            aria-pressed={score === n}
                            onClick={() =>
                              updateStaff(current.id, (prev) => ({
                                ...prev,
                                reason: '',
                                ratings: { ...prev.ratings, [d.id]: n },
                              }))
                            }
                            className={`rounded-lg border py-1.5 text-xs font-bold ${
                              score === n
                                ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                : 'border-[var(--line)] bg-[var(--bg)]'
                            }`}
                          >
                            {n}
                          </button>
                        ))}
                      </div>
                    </div>
                  );
                })}
              <label className="block space-y-1 text-xs">
                <span className="font-semibold text-[var(--muted)]">{t('notes')}</span>
                <textarea
                  className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
                  rows={2}
                  value={staffAnswers[current.id]?.notes || ''}
                  onChange={(e) => updateStaff(current.id, (prev) => ({ ...prev, notes: e.target.value }))}
                />
              </label>
              <ChipRow
                chips={staffChips}
                hint={t('chipsHint')}
                onPick={(chip) =>
                  updateStaff(current.id, (prev) => ({
                    ...prev,
                    notes: appendSuggestedChip(prev.notes, chip),
                  }))
                }
              />
              <AttachmentFields
                attachments={staffAnswers[current.id]?.attachments || []}
                onChange={(next) => updateStaff(current.id, (prev) => ({ ...prev, attachments: next }))}
                t={t}
                setError={setFormError}
              />
              <button
                type="button"
                aria-pressed={Boolean(staffAnswers[current.id]?.complaint)}
                onClick={() =>
                  updateStaff(current.id, (prev) => ({
                    ...prev,
                    complaint: !prev.complaint,
                    complaintDetails: !prev.complaint ? prev.complaintDetails : '',
                  }))
                }
                className={`rounded-lg border px-3 py-1.5 text-xs font-bold ${
                  staffAnswers[current.id]?.complaint
                    ? 'border-[var(--danger)] bg-[var(--danger)]/15 text-[var(--danger)]'
                    : 'border-[var(--line)]'
                }`}
              >
                {staffAnswers[current.id]?.complaint ? t('complaintActive') : t('complaintBtn')}
              </button>
              {staffAnswers[current.id]?.complaint && (
                <>
                  <textarea
                    className="w-full rounded-lg border border-[var(--danger)] bg-[var(--bg)] px-3 py-2 text-xs"
                    rows={2}
                    value={staffAnswers[current.id]?.complaintDetails || ''}
                    onChange={(e) =>
                      updateStaff(current.id, (prev) => ({ ...prev, complaintDetails: e.target.value }))
                    }
                    placeholder={t('complaintDetails')}
                  />
                  <ChipRow
                    chips={staffChips}
                    hint={t('chipsHint')}
                    onPick={(chip) =>
                      updateStaff(current.id, (prev) => ({
                        ...prev,
                        complaintDetails: appendSuggestedChip(prev.complaintDetails, chip),
                      }))
                    }
                  />
                </>
              )}
            </>
          )}

          {current.kind === 'app' && (() => {
            const appId = current.id;
            const ans = appAnswers[appId] || emptyApp();
            return (
              <>
                <h2 className="text-lg font-semibold">
                  {t('appTitle')}: {platformDisplayName(appId, lang)}
                </h2>
                <label className="block space-y-1 text-xs">
                  <span className="font-semibold text-[var(--muted)]">{t('skipLabel')}</span>
                  <select
                    className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
                    value={ans.reason}
                    onChange={(e) =>
                      setAppAnswers((prev) => ({
                        ...prev,
                        [appId]: {
                          ...ans,
                          reason: e.target.value,
                          overall: e.target.value ? 0 : ans.overall,
                          factorRatings: e.target.value ? {} : ans.factorRatings,
                        },
                      }))
                    }
                  >
                    <option value="">★ {t('starsLabel')}</option>
                    {(schema.appReasons || []).map((r) => (
                      <option key={r.id} value={r.id}>
                        {localizedLabel(r, lang)}
                      </option>
                    ))}
                  </select>
                </label>
                {!ans.reason && (
                  <>
                    <div className="space-y-1">
                      <div className="flex justify-between text-xs font-semibold">
                        <span>{t('starsLabel')}</span>
                        <span>{ans.overall ? `${ans.overall} / 5 ★` : '—'}</span>
                      </div>
                      <div className="grid grid-cols-5 gap-1.5">
                        {[1, 2, 3, 4, 5].map((star) => (
                          <button
                            key={star}
                            type="button"
                            data-app-star={appId}
                            data-stars={star}
                            aria-pressed={ans.overall === star}
                            onClick={() =>
                              setAppAnswers((prev) => ({
                                ...prev,
                                [appId]: { ...ans, reason: '', overall: star },
                              }))
                            }
                            className={`rounded-lg border py-2 text-xs font-bold ${
                              ans.overall === star
                                ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                : 'border-[var(--line)] bg-[var(--bg)]'
                            }`}
                          >
                            {star} ★
                          </button>
                        ))}
                      </div>
                    </div>
                    <div className="space-y-2">
                      <span className="text-xs font-semibold">
                        {t('factorsTitle')}{' '}
                        <span className="font-normal text-[var(--muted)]">({t('factorOptional')})</span>
                      </span>
                      {(schema.factors || []).map((f) => {
                        const stars = ans.factorRatings[f.id] || 0;
                        return (
                          <div key={f.id} className="space-y-1">
                            <div className="flex justify-between text-[11px]">
                              <span>{localizedLabel(f, lang)}</span>
                              <span>{stars ? `${stars} / 5 ★` : '—'}</span>
                            </div>
                            <div className="grid grid-cols-5 gap-1.5">
                              {[1, 2, 3, 4, 5].map((star) => (
                                <button
                                  key={star}
                                  type="button"
                                  data-app-factor-star={appId}
                                  data-factor-id={f.id}
                                  data-stars={star}
                                  onClick={() =>
                                    setAppAnswers((prev) => {
                                      const cur = prev[appId] || emptyApp();
                                      const next = { ...cur.factorRatings };
                                      if (next[f.id] === star) delete next[f.id];
                                      else next[f.id] = star;
                                      return {
                                        ...prev,
                                        [appId]: { ...cur, reason: '', factorRatings: next },
                                      };
                                    })
                                  }
                                  className={`rounded-lg border py-1.5 text-[11px] font-bold ${
                                    stars === star
                                      ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                      : 'border-[var(--line)] bg-[var(--bg)]'
                                  }`}
                                >
                                  {star} ★
                                </button>
                              ))}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </>
                )}
                <textarea
                  className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs"
                  rows={2}
                  value={ans.notes}
                  onChange={(e) =>
                    setAppAnswers((prev) => ({ ...prev, [appId]: { ...ans, notes: e.target.value } }))
                  }
                  placeholder={t('notes')}
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
                <AttachmentFields
                  attachments={ans.attachments}
                  onChange={(next) =>
                    setAppAnswers((prev) => ({ ...prev, [appId]: { ...ans, attachments: next } }))
                  }
                  t={t}
                  setError={setFormError}
                />
              </>
            );
          })()}

          {current.kind === 'finance' && (
            <div data-testid="eval-finance-card" className="space-y-3">
              <h2 className="text-lg font-semibold">{t('financeTitle')}</h2>
              <label className="block space-y-1 text-xs">
                <span className="font-semibold text-[var(--muted)]">{t('skipLabel')}</span>
                <select
                  className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2"
                  value={financeAnswer.reason}
                  onChange={(e) =>
                    setFinanceAnswer((prev) => ({
                      ...prev,
                      reason: e.target.value,
                      overall: e.target.value ? 0 : prev.overall,
                      factorRatings: e.target.value ? {} : prev.factorRatings,
                    }))
                  }
                >
                  <option value="">★ {t('starsLabel')}</option>
                  {(schemaX.financeReasons || []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {localizedLabel(r, lang)}
                    </option>
                  ))}
                </select>
              </label>
              {!financeAnswer.reason && (
                <>
                  <div className="space-y-1">
                    <div className="flex justify-between text-xs font-semibold">
                      <span>{t('starsLabel')}</span>
                      <span>
                        {financeAnswer.overall ? `${financeAnswer.overall} / 5 ★` : '—'}
                      </span>
                    </div>
                    <div className="grid grid-cols-5 gap-1.5">
                      {[1, 2, 3, 4, 5].map((star) => (
                        <button
                          key={star}
                          type="button"
                          data-finance-star={star}
                          aria-pressed={financeAnswer.overall === star}
                          onClick={() =>
                            setFinanceAnswer((prev) => ({ ...prev, reason: '', overall: star }))
                          }
                          className={`rounded-lg border py-2 text-xs font-bold ${
                            financeAnswer.overall === star
                              ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                              : 'border-[var(--line)] bg-[var(--bg)]'
                          }`}
                        >
                          {star} ★
                        </button>
                      ))}
                    </div>
                  </div>
                  {(schemaX.financeFactors || []).map((f) => {
                    const stars = financeAnswer.factorRatings[f.id] || 0;
                    return (
                      <div key={f.id} className="space-y-1">
                        <div className="flex justify-between text-[11px]">
                          <span>{localizedLabel(f, lang)}</span>
                          <span>{stars ? `${stars} / 5 ★` : '—'}</span>
                        </div>
                        <div className="grid grid-cols-5 gap-1.5">
                          {[1, 2, 3, 4, 5].map((star) => (
                            <button
                              key={star}
                              type="button"
                              data-finance-factor={f.id}
                              data-stars={star}
                              onClick={() =>
                                setFinanceAnswer((prev) => {
                                  const next = { ...prev.factorRatings };
                                  if (next[f.id] === star) delete next[f.id];
                                  else next[f.id] = star;
                                  return { ...prev, reason: '', factorRatings: next };
                                })
                              }
                              className={`rounded-lg border py-1.5 text-[11px] font-bold ${
                                stars === star
                                  ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                  : 'border-[var(--line)] bg-[var(--bg)]'
                              }`}
                            >
                              {star} ★
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  })}
                </>
              )}
              <textarea
                className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs"
                rows={2}
                value={financeAnswer.notes}
                onChange={(e) => setFinanceAnswer((prev) => ({ ...prev, notes: e.target.value }))}
                placeholder={t('notes')}
              />
              <ChipRow
                chips={financeChips}
                hint={t('chipsHint')}
                onPick={(chip) =>
                  setFinanceAnswer((prev) => ({
                    ...prev,
                    notes: appendSuggestedChip(prev.notes, chip),
                  }))
                }
              />
              <AttachmentFields
                attachments={financeAnswer.attachments}
                onChange={(next) => setFinanceAnswer((prev) => ({ ...prev, attachments: next }))}
                t={t}
                setError={setFormError}
              />
              <button
                type="button"
                aria-pressed={financeAnswer.complaint}
                onClick={() =>
                  setFinanceAnswer((prev) => ({
                    ...prev,
                    complaint: !prev.complaint,
                    complaintDetails: !prev.complaint ? prev.complaintDetails : '',
                  }))
                }
                className={`rounded-lg border px-3 py-1.5 text-xs font-bold ${
                  financeAnswer.complaint
                    ? 'border-[var(--danger)] bg-[var(--danger)]/15 text-[var(--danger)]'
                    : 'border-[var(--line)]'
                }`}
              >
                {financeAnswer.complaint ? t('complaintActive') : t('complaintBtn')}
              </button>
              {financeAnswer.complaint && (
                <>
                  <textarea
                    className="w-full rounded-lg border border-[var(--danger)] bg-[var(--bg)] px-3 py-2 text-xs"
                    rows={2}
                    value={financeAnswer.complaintDetails}
                    onChange={(e) =>
                      setFinanceAnswer((prev) => ({ ...prev, complaintDetails: e.target.value }))
                    }
                    placeholder={t('complaintDetails')}
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
            </div>
          )}

          <div className="flex gap-2">
            <button
              type="button"
              className="flex-1 rounded-lg border border-[var(--line)] px-4 py-3 text-sm font-bold"
              data-testid="eval-section-back"
              onClick={backCard}
            >
              {t('prevBtn')}
            </button>
            <button
              type="button"
              className="flex-1 rounded-lg bg-[var(--accent)] px-4 py-3 text-sm font-bold text-[var(--accent-ink)]"
              onClick={advanceCard}
            >
              {t('next')}
            </button>
          </div>
        </div>
      )}

      {step === 'review' && (
        <div className="space-y-3" data-testid="eval-section-review">
          <h2 className="text-lg font-semibold">{t('reviewTitle')}</h2>
          <ul className="space-y-1 text-sm text-[var(--muted)]">
            <li>
              {name} · {city} · {workApps.map((a) => platformDisplayName(a, lang)).join('، ')}
            </li>
            <li>
              {t('cardProgress')}: {cards.length}
            </li>
          </ul>
          <div className="flex gap-2">
            <button
              type="button"
              className="flex-1 rounded-lg border border-[var(--line)] px-4 py-3 text-sm font-bold"
              data-testid="eval-section-back"
              onClick={() => {
                setStep('cards');
                setCardIndex(Math.max(0, cards.length - 1));
              }}
            >
              {t('prevBtn')}
            </button>
            <button
              type="button"
              className="flex-1 rounded-lg bg-[var(--accent)] px-4 py-3 text-sm font-bold text-[var(--accent-ink)]"
              disabled={submitting}
              onClick={() => void submitAll(editing)}
            >
              {submitting ? t('submitting') : editing ? t('editSave') : t('reviewSend')}
            </button>
          </div>
        </div>
      )}

      {formError && (
        <p className="text-sm text-[var(--danger)]" role="alert">
          {formError}
        </p>
      )}
    </div>
  );
}
