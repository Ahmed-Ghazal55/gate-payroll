'use client';

import React, { useEffect, useMemo, useState } from 'react';
import {
  api,
  type PublicEvaluationSchema,
} from '../../lib/api';
import {
  NATIONALITY_SUGGESTIONS,
  nationalityIdFromLabel,
  PLATFORM_NAMES,
  normalizeEvalDigits,
  localizedLabel,
  platformDisplayName,
  shrinkImageForEvaluation,
  voiceFileForEvaluation,
  type EvalAttachmentDraft,
  type EvalLanguage,
} from './evaluation-helpers';
import { ApiError } from '../../lib/api';
import { EvaluationSectionFlow } from './EvaluationSectionFlow';

export {
  NATIONALITY_SUGGESTIONS,
  PLATFORM_NAMES,
  normalizeEvalDigits,
  localizedLabel,
  platformDisplayName,
  type EvalLanguage,
};

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

const FORM_COPY: Record<EvalLanguage, Record<string, string>> = {
  ar: {
    brand: 'البوابة',
    subtitle: 'تقييم المشرفين والمنصات',
    loading: 'جاري تحميل نموذج التقييم…',
    unavailable: 'نموذج التقييم غير متاح أو مغلق حاليًا.',
    page1Title: 'الصفحة 1 من 2 — تقييم المشرفين والموظفين',
    page2Title: 'الصفحة 2 من 2 — تقييم منصات العمل',
    identityTitle: 'بيانات المندوب الأساسية',
    iqama: 'رقم الإقامة',
    iqamaPlaceholder: 'رقم الإقامة من 10 أرقام',
    name: 'الاسم الكامل',
    namePlaceholder: 'الاسم كما في الهوية',
    city: 'مدينة العمل',
    pickCity: 'اختر مدينة العمل لعرض بطاقات المشرفين والموظفين',
    nationality: 'الجنسية',
    nationalityPlaceholder: 'اختر أو اكتب الجنسية',
    phone: 'رقم الجوال (اختياري)',
    workApps: 'منصات العمل الحالية (يمكن اختيار أكثر من منصة)',
    allDepts: 'كل الأقسام',
    allApps: 'كل المنصات',
    cardsProgress: 'البطاقات المجابة',
    rateMode: 'تقييم (1–10)',
    appRateMode: 'تقييم بالنجوم (1–5)',
    skipLabel: 'أو اختر سبب عدم التقييم',
    notes: 'ملاحظات إضافية (اختياري)',
    complaintBtn: 'تقديم شكوى سرية على هذه البطاقة',
    complaintActive: 'شكوى سرية مفعّلة (تراجعها الإدارة فقط)',
    complaintDetails: 'تفاصيل الشكوى السرية (مطلوب، 5 أحرف على الأقل)',
    factorsTitle: 'عوامل المنصة (اختياري، نجمة إلى 5 لكل عامل)',
    factorOptional: 'اختياري',
    starsLabel: 'التقييم العام للمنصة (من 1 إلى 5 نجوم)',
    nextBtn: 'التالي: تقييم المنصات ←',
    prevBtn: '→ السابق: بطاقات المشرفين',
    submitBtn: 'إرسال التقييم',
    submitting: 'جاري الإرسال…',
    unansweredStaff: 'أجب عن جميع بطاقات المشرفين والموظفين في مدينتك قبل الانتقال للصفحة التالية.',
    unansweredApps: 'أجب عن جميع بطاقات المنصات قبل الإرسال.',
    missingIdentity: 'أكمل رقم الإقامة (10 أرقام)، الاسم، مدينة العمل، الجنسية، ومنصة عمل واحدة على الأقل.',
    missingComplaint: 'اكتب تفاصيل الشكوى السرية (5 أحرف على الأقل) لكل بطاقة فعّلت فيها الشكوى.',
    uploadsPending: 'لكل بطاقة: صورتان كحد أقصى + تسجيل صوتي حتى 60 ثانية. الصور تُصغَّر على الجهاز. حجم الرد كاملًا حتى 6 ميجابايت.',
    answeredBadge: '✓ مكتملة',
    pendingBadge: 'مطلوب الإجابة',
    rolesLabel: 'المهام والأدوار',
    deptLabel: 'القسم',
    recordedTitle: 'تم تسجيل تقييمك السري بنجاح',
    receiptLabel: 'رقم الإيصال والمتابعة',
    appSessionVerified: 'تم التحقق من رقم الإقامة تلقائيًا من جلستك في التطبيق',
    err_already_submitted: 'سبق إرسال هذا الرد؛ يمكنك تعديله إن كانت الجولة مفتوحة.',
    err_invalid_request: 'بيانات غير صالحة. راجع الحقول وأعد المحاولة.',
    err_rate_limited: 'محاولات كثيرة؛ حاول بعد قليل.',
    err_conflict: 'تعارض مع حالة الجولة الحالية.',
    err_forbidden: 'هذا الإجراء غير مسموح.',
  },
  en: {
    brand: 'Al Bawaba',
    subtitle: 'Supervisors & Platforms Evaluation',
    loading: 'Loading evaluation survey…',
    unavailable: 'This evaluation round is unavailable or closed.',
    page1Title: 'Page 1 of 2 — Supervisors & Staff Evaluation',
    page2Title: 'Page 2 of 2 — Work Platforms Evaluation',
    identityTitle: 'Rider Basic Details',
    iqama: 'Iqama number',
    iqamaPlaceholder: '10-digit Iqama',
    name: 'Full name',
    namePlaceholder: 'Name as shown on ID',
    city: 'Work city',
    pickCity: 'Choose your work city to display supervisor and staff cards',
    nationality: 'Nationality',
    nationalityPlaceholder: 'Select or type nationality',
    phone: 'Phone number (optional)',
    workApps: 'Current work platforms (select all that apply)',
    allDepts: 'All departments',
    allApps: 'All platforms',
    cardsProgress: 'Answered cards',
    rateMode: 'Rate (1–10)',
    appRateMode: 'Star rating (1–5)',
    skipLabel: 'Or choose a reason not to rate',
    notes: 'Additional notes (optional)',
    complaintBtn: 'File a confidential complaint on this card',
    complaintActive: 'Confidential complaint active (management only)',
    complaintDetails: 'Confidential complaint details (required, min 5 characters)',
    factorsTitle: 'Platform factors (optional, 1 to 5 stars each)',
    factorOptional: 'Optional',
    starsLabel: 'Overall platform rating (1 to 5 stars)',
    nextBtn: 'Next: Evaluate Platforms →',
    prevBtn: '← Previous: Staff Cards',
    submitBtn: 'Submit Evaluation',
    submitting: 'Submitting…',
    unansweredStaff: 'Please answer every staff card in your city before continuing to Page 2.',
    unansweredApps: 'Please answer every platform card before submitting.',
    missingIdentity: 'Complete your 10-digit Iqama, full name, work city, nationality, and at least one work platform.',
    missingComplaint: 'Enter confidential complaint details (at least 5 characters) for every complaint card.',
    uploadsPending: 'Per card: up to 2 images + 1 voice note (≤60 s). Images are shrunk on the phone. Whole reply ≤ 6 MB.',
    answeredBadge: '✓ Answered',
    pendingBadge: 'Required',
    rolesLabel: 'Roles',
    deptLabel: 'Department',
    recordedTitle: 'Your confidential evaluation has been recorded',
    receiptLabel: 'Receipt number',
    appSessionVerified: 'Iqama verified automatically from your active app session',
    err_already_submitted: 'This reply was already submitted; you may edit it while the round is open.',
    err_invalid_request: 'Invalid data. Check the fields and try again.',
    err_rate_limited: 'Too many attempts; try again shortly.',
    err_conflict: 'Conflicts with the current round status.',
    err_forbidden: 'This action is not allowed.',
  },
  ur: {
    brand: 'البوابہ',
    subtitle: 'سپروائزرز اور پلیٹ فارمز کی درجہ بندی',
    loading: 'درجہ بندی کا فارم لوڈ ہو رہا ہے…',
    unavailable: 'یہ درجہ بندی فارم دستیاب نہیں یا بند ہے۔',
    page1Title: 'صفحہ 1 از 2 — سپروائزرز اور عملے کی درجہ بندی',
    page2Title: 'صفحہ 2 از 2 — کام کے پلیٹ فارمز کی درجہ بندی',
    identityTitle: 'رائیڈر کی بنیادی معلومات',
    iqama: 'اقامہ نمبر',
    iqamaPlaceholder: '10 ہندسوں کا اقامہ نمبر',
    name: 'پورا نام',
    namePlaceholder: 'شناختی دستاویز کے مطابق نام',
    city: 'کام کا شہر',
    pickCity: 'سپروائزرز اور عملے کے کارڈز دیکھنے کے لیے کام کا شہر منتخب کریں',
    nationality: 'قومیت',
    nationalityPlaceholder: 'قومیت منتخب کریں یا لکھیں',
    phone: 'موبائل نمبر (اختیاری)',
    workApps: 'موجودہ کام کے پلیٹ فارم (ایک سے زیادہ منتخب کر سکتے ہیں)',
    allDepts: 'تمام شعبے',
    allApps: 'تمام پلیٹ فارمز',
    cardsProgress: 'مکمل کردہ کارڈز',
    rateMode: 'درجہ بندی (1–10)',
    appRateMode: 'ستاروں کی درجہ بندی (1–5)',
    skipLabel: 'یا درجہ بندی نہ کرنے کی وجہ منتخب کریں',
    notes: 'اضافی نوٹس (اختیاری)',
    complaintBtn: 'اس کارڈ پر خفیہ شکایت درج کریں',
    complaintActive: 'خفیہ شکایت فعال ہے (صرف انتظامیہ کے لیے)',
    complaintDetails: 'خفیہ شکایت کی تفصیل (لازمی، کم از کم 5 حروف)',
    factorsTitle: 'پلیٹ فارم عوامل (اختیاری، ہر عامل 1 سے 5 ستارے)',
    factorOptional: 'اختیاری',
    starsLabel: 'پلیٹ فارم کی مجموعی درجہ بندی (1 سے 5 ستارے)',
    nextBtn: 'اگلا: پلیٹ فارمز کی درجہ بندی ←',
    prevBtn: '→ پچھلا: عملے کے کارڈز',
    submitBtn: 'درجہ بندی جمع کریں',
    submitting: 'جمع ہو رہا ہے…',
    unansweredStaff: 'اگلے صفحے پر جانے سے پہلے اپنے شہر کے تمام عملے کے کارڈز کا جواب دیں۔',
    unansweredApps: 'جمع کرنے سے پہلے تمام پلیٹ فارم کارڈز کا جواب دیں۔',
    missingIdentity: 'اپنا 10 ہندسوں کا اقامہ، پورا نام، کام کا شہر، قومیت اور کم از کم ایک پلیٹ فارم مکمل کریں۔',
    missingComplaint: 'جس کارڈ پر شکایت منتخب کی ہے اس کی تفصیل (کم از کم 5 حروف) درج کریں۔',
    uploadsPending: 'ہر کارڈ: زیادہ سے زیادہ 2 تصاویر + 1 صوتی نوٹ (≤60 سیکنڈ)۔ تصاویر فون پر چھوٹی ہوتی ہیں۔ پورا جواب ≤ 6 MB۔',
    answeredBadge: '✓ مکمل',
    pendingBadge: 'جواب لازمی',
    rolesLabel: 'ذمہ داریاں',
    deptLabel: 'شعبہ',
    recordedTitle: 'آپ کی خفیہ درجہ بندی محفوظ ہو گئی',
    receiptLabel: 'رسید نمبر',
    appSessionVerified: 'آپ کے فعال ایپ سیشن سے اقامہ نمبر خودکار درج کیا گیا ہے',
    err_already_submitted: 'یہ جواب پہلے جمع ہو چکا ہے؛ جولہ کھلی ہو تو ترمیم ممکن ہے۔',
    err_invalid_request: 'غلط ڈیٹا۔ خانے چیک کر کے دوبارہ کوشش کریں۔',
    err_rate_limited: 'زیادہ کوششیں؛ تھوڑی دیر بعد دوبارہ کوشش کریں۔',
    err_conflict: 'موجودہ جولہ کی حالت سے تعارض۔',
    err_forbidden: 'یہ عمل اجازت نہیں رکھتا۔',
  },
};

export interface EvaluationPublicFormProps {
  token: string;
  mode?: 'public' | 'app';
}

export function EvaluationPublicForm({ token, mode = 'public' }: EvaluationPublicFormProps) {
  const [lang, setLang] = useState<EvalLanguage>('ar');
  const [schema, setSchema] = useState<PublicEvaluationSchema | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [page, setPage] = useState<1 | 2>(1);
  const [iqama, setIqama] = useState('');
  const [name, setName] = useState('');
  const [city, setCity] = useState('');
  const [nationality, setNationality] = useState('');
  const [phone, setPhone] = useState('');
  const [workApps, setWorkApps] = useState<string[]>([]);
  const [deptFilter, setDeptFilter] = useState('ALL');
  const [appFilter, setAppFilter] = useState('ALL');
  const [staffAnswers, setStaffAnswers] = useState<Record<string, StaffDraft>>({});
  const [appAnswers, setAppAnswers] = useState<Record<string, AppDraft>>({});
  const [formError, setFormError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [receipt, setReceipt] = useState('');
  const [sessionToken, setSessionToken] = useState<string | undefined>(undefined);
  const [sessionLockedIqama, setSessionLockedIqama] = useState(false);

  const t = (key: string) => FORM_COPY[lang][key] || FORM_COPY.ar[key] || key;

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const surveyKey = 'gate_eval_survey';
    try {
      const savedLang = localStorage.getItem('gate-language');
      if (savedLang === 'ar' || savedLang === 'en' || savedLang === 'ur') {
        setLang(savedLang);
      }
      const params = new URLSearchParams(window.location.search);
      const hashParams = new URLSearchParams(
        window.location.hash.startsWith('#') ? window.location.hash.slice(1) : window.location.hash
      );
      const launchCode = hashParams.get('launch');
      if (launchCode && token) {
        void fetch(`/api/public/evaluations/${encodeURIComponent(token)}/session`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ code: launchCode }),
        })
          .then(async (r) => {
            const d = (await r.json().catch(() => ({}))) as {
              accessToken?: string;
              iqama?: string;
              error?: string;
            };
            if (!r.ok || !d.accessToken || !d.accessToken.startsWith('eves_'))
              throw new Error(d.error || 'launch_invalid');
            try {
              sessionStorage.setItem(
                surveyKey,
                JSON.stringify({ accessToken: d.accessToken, iqama: d.iqama })
              );
            } catch {}
            setSessionToken(d.accessToken);
            if (d.iqama && /^[0-9]{10}$/.test(d.iqama)) {
              setIqama(d.iqama);
              setSessionLockedIqama(true);
            }
            hashParams.delete('launch');
            const nextHash = hashParams.toString();
            const clean =
              window.location.pathname +
              (params.toString() ? `?${params.toString()}` : '') +
              (nextHash ? `#${nextHash}` : '');
            window.history.replaceState(null, '', clean);
          })
          .catch(() => {});
      }
      // Hosted survey: only eves_ in gate_eval_survey. In-app /app/evaluation may use rmob_ from the app store.
      const applySession = (raw: string | null, allowAppBearer: boolean) => {
        if (!raw) return false;
        const parsed = JSON.parse(raw) as {
          iqama?: string;
          name?: string;
          accessToken?: string;
        };
        const tok = parsed.accessToken;
        if (typeof tok !== 'string') return false;
        if (tok.startsWith('eves_') || (allowAppBearer && tok.startsWith('rmob_'))) {
          setSessionToken(tok);
          if (parsed.iqama && /^[0-9]{10}$/.test(parsed.iqama)) {
            setIqama(parsed.iqama);
            setSessionLockedIqama(true);
          }
          if (parsed.name) setName(parsed.name);
          return true;
        }
        return false;
      };
      if (!applySession(sessionStorage.getItem(surveyKey), false)) {
        const inApp = mode === 'app' && window.location.pathname.startsWith('/app/');
        if (inApp) applySession(sessionStorage.getItem('gate_rider_mob'), true);
      }
    } catch {}
  }, [token, mode]);

  useEffect(() => {
    if (!token) {
      setLoadError(t('unavailable'));
      setLoading(false);
      return;
    }
    setLoading(true);
    setLoadError(null);
    const loadPromise =
      mode === 'app' &&
      sessionToken &&
      (sessionToken.startsWith('eves_') || sessionToken.startsWith('rmob_'))
        ? api.getRiderMobileEvaluationSchema(token, undefined, sessionToken)
        : api.getPublicEvaluationSchema(token);
    loadPromise
      .then((res) => {
        setSchema(res);
        if (res.cities.length === 1) setCity(res.cities[0]);
      })
      .catch((err: any) => {
        setLoadError(err?.message || t('unavailable'));
      })
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, mode, sessionToken]);

  const switchLang = (next: EvalLanguage) => {
    setLang(next);
    try {
      localStorage.setItem('gate-language', next);
    } catch {}
  };

  const cityStaff = useMemo(() => {
    if (!schema || !city) return [];
    return (schema.staff || []).filter((s) => !s.excluded && s.cities.includes(city));
  }, [schema, city]);

  const departments = useMemo(
    () => [...new Set(cityStaff.flatMap((s) => s.departments || []))],
    [cityStaff]
  );

  const staffApps = useMemo(
    () => [...new Set(cityStaff.flatMap((s) => s.apps || []))],
    [cityStaff]
  );

  const visibleStaff = useMemo(
    () =>
      cityStaff.filter(
        (s) =>
          (deptFilter === 'ALL' || (s.departments || []).includes(deptFilter)) &&
          (appFilter === 'ALL' || (s.apps || []).includes(appFilter))
      ),
    [cityStaff, deptFilter, appFilter]
  );

  const isStaffCardComplete = (staffId: string): boolean => {
    if (!schema) return false;
    const ans = staffAnswers[staffId];
    if (!ans) return false;
    if (ans.complaint && (ans.complaintDetails || '').trim().length < 5) return false;
    if (ans.reason !== '') {
      return schema.staffReasons.some((r) => r.id === ans.reason);
    }
    const min = schema.staffRatingMin ?? 1;
    const max = schema.staffRatingMax ?? 10;
    return schema.staffDimensions.every((dim) => {
      const v = Number(ans.ratings?.[dim.id]);
      return Number.isInteger(v) && v >= min && v <= max;
    });
  };

  const isAppCardComplete = (appId: string): boolean => {
    if (!schema) return false;
    const ans = appAnswers[appId];
    if (!ans) return false;
    if (ans.reason !== '') {
      return schema.appReasons.some((r) => r.id === ans.reason);
    }
    const min = schema.appRatingMin ?? 1;
    const max = schema.appRatingMax ?? 5;
    const v = Number(ans.overall);
    return Number.isInteger(v) && v >= min && v <= max;
  };

  const answeredStaffCount = cityStaff.filter((s) => isStaffCardComplete(s.id)).length;
  const answeredAppCount = (schema?.apps || []).filter((a) => isAppCardComplete(a)).length;

  const updateStaffDraft = (staffId: string, updater: (prev: StaffDraft) => StaffDraft) => {
    setStaffAnswers((prev) => {
      const cur = prev[staffId] || {
        reason: '',
        ratings: {},
        notes: '',
        complaint: false,
        complaintDetails: '',
        attachments: [],
      };
      return { ...prev, [staffId]: updater(cur) };
    });
    setFormError('');
  };

  const updateAppDraft = (appId: string, updater: (prev: AppDraft) => AppDraft) => {
    setAppAnswers((prev) => {
      const cur = prev[appId] || {
        reason: '',
        overall: 0,
        factorRatings: {},
        notes: '',
        attachments: [],
      };
      return { ...prev, [appId]: updater(cur) };
    });
    setFormError('');
  };

  const handleNextPage = () => {
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
    if (
      cityStaff.some((s) => {
        const a = staffAnswers[s.id];
        return a?.complaint && (a.complaintDetails || '').trim().length < 5;
      })
    ) {
      setFormError(t('missingComplaint'));
      setDeptFilter('ALL');
      setAppFilter('ALL');
      return;
    }
    if (!cityStaff.every((s) => isStaffCardComplete(s.id))) {
      setFormError(t('unansweredStaff'));
      setDeptFilter('ALL');
      setAppFilter('ALL');
      return;
    }
    setFormError('');
    setPage(2);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!schema) return;
    if (!schema.apps.every((a) => isAppCardComplete(a))) {
      setFormError(t('unansweredApps'));
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
      appAnswers: schema.apps.map((app) => {
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
    try {
      const res =
        mode === 'app' &&
        sessionToken &&
        (sessionToken.startsWith('eves_') || sessionToken.startsWith('rmob_'))
          ? await api.submitRiderMobileEvaluation(token, payload, sessionToken)
          : await api.submitPublicEvaluation(token, payload);
      setReceipt(res.receipt);
    } catch (err: unknown) {
      const code = err instanceof ApiError ? err.code : undefined;
      const mapped =
        code === 'already_submitted'
          ? t('err_already_submitted')
          : code === 'rate_limited'
          ? t('err_rate_limited')
          : code === 'conflict'
          ? t('err_conflict')
          : code === 'forbidden'
          ? t('err_forbidden')
          : code === 'invalid_request' || code === 'invalid_field' || code === 'cards_incomplete'
          ? t('err_invalid_request')
          : err instanceof Error
          ? err.message
          : t('unavailable');
      setFormError(mapped);
    } finally {
      setSubmitting(false);
    }
  };

  const dir = lang === 'en' ? 'ltr' : 'rtl';
  const notice =
    lang === 'en'
      ? schema?.noticeEn || schema?.noticeAr
      : lang === 'ur'
      ? schema?.noticeUr || schema?.noticeAr
      : schema?.noticeAr;
  const noticeLines: string[] =
    schema?.noticeLines && schema.noticeLines.length > 0
      ? schema.noticeLines
          .map((line) => (lang === 'en' ? line.en || line.ar : lang === 'ur' ? line.ur || line.ar : line.ar) || '')
          .filter(Boolean)
      : (notice || '').split('\n').filter(Boolean);

  return (
    <div dir={dir} lang={lang} className="space-y-4 py-4" data-testid="evaluation-public-form">
      {/* Header & 3-Language Switcher */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-[var(--accent-ink)] font-bold text-lg">
            ب
          </div>
          <div>
            <h1 className="text-base font-black text-[var(--ink)]">{t('brand')}</h1>
            <p className="text-xs text-[var(--muted)]">{schema?.title || t('subtitle')}</p>
          </div>
        </div>
        <div className="flex items-center gap-1.5" role="group" aria-label="Language">
          {(['ar', 'en', 'ur'] as EvalLanguage[]).map((l) => (
            <button
              key={l}
              type="button"
              data-lang={l}
              aria-pressed={lang === l}
              onClick={() => switchLang(l)}
              className={`px-3 py-1.5 rounded-lg text-xs font-bold transition ${
                lang === l
                  ? 'bg-[var(--accent)] text-[var(--accent-ink)]'
                  : 'border border-[var(--line)] bg-[var(--raised)] text-[var(--ink)]'
              }`}
            >
              {l === 'ar' ? 'العربية' : l === 'en' ? 'English' : 'اردو'}
            </button>
          ))}
        </div>
      </div>

      {loading ? (
        <div className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-6 text-center text-sm text-[var(--muted)]">
          {t('loading')}
        </div>
      ) : loadError || !schema ? (
        <div
          className="rounded-2xl border border-[var(--danger)]/40 bg-[var(--surface)] p-6 text-center space-y-2"
          role="alert"
        >
          <h2 className="text-base font-bold text-[var(--danger)]">{t('unavailable')}</h2>
          {loadError && <p className="text-xs text-[var(--muted)]">{loadError}</p>}
        </div>
      ) : receipt ? (
        <div
          className="rounded-2xl border border-[var(--money-pos)] bg-[var(--surface)] p-6 text-center space-y-3"
          data-testid="evaluation-receipt"
        >
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-[var(--money-pos)]/15 text-[var(--money-pos)] text-2xl font-bold">
            ✓
          </div>
          <h2 className="text-lg font-bold text-[var(--ink)]">{t('recordedTitle')}</h2>
          <p className="text-xs text-[var(--muted)]">{t('receiptLabel')}</p>
          <div className="rounded-xl border border-[var(--line)] bg-[var(--raised)] p-3 font-mono text-xs break-all">
            {receipt}
          </div>
        </div>
      ) : (schema as { sectionKey?: string }).sectionKey ? (
        <EvaluationSectionFlow
          token={token}
          schema={schema}
          lang={lang}
          mode={mode}
          sessionToken={sessionToken}
          lockedIqama={sessionLockedIqama}
        />
      ) : (
        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          {notice && (
            <div
              className="flex gap-3 rounded-xl border-2 border-amber-400 bg-amber-50 p-3.5 text-sm font-semibold text-amber-950"
              data-testid="evaluation-confidential-notice"
              id="eval-confidential-notice"
            >
              <span className="text-2xl leading-none" aria-hidden>
                🛡️
              </span>
              <ul className="space-y-2">
                {noticeLines.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
            </div>
          )}

          {page === 1 ? (
            <div className="space-y-4" data-testid="evaluation-page-1">
              <div className="rounded-xl bg-[var(--raised)] px-3.5 py-2 text-xs font-bold text-[var(--ink)]">
                {t('page1Title')}
              </div>

              {/* Identity Section */}
              <fieldset className="rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-4 space-y-3">
                <legend className="px-2 text-xs font-bold text-[var(--ink)]">
                  {t('identityTitle')}
                </legend>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <label className="block space-y-1 text-xs">
                    <span className="font-bold text-[var(--ink)]">{t('iqama')} *</span>
                    <input
                      type="text"
                      name="iqama"
                      dir="ltr"
                      inputMode="numeric"
                      maxLength={10}
                      readOnly={sessionLockedIqama}
                      value={iqama}
                      onChange={(e) => {
                        setIqama(normalizeEvalDigits(e.target.value));
                        setFormError('');
                      }}
                      placeholder={t('iqamaPlaceholder')}
                      className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      required
                    />
                    {sessionLockedIqama && (
                      <span className="block text-[11px] text-[var(--money-pos)]">
                        ✓ {t('appSessionVerified')}
                      </span>
                    )}
                  </label>

                  <label className="block space-y-1 text-xs">
                    <span className="font-bold text-[var(--ink)]">{t('name')} *</span>
                    <input
                      type="text"
                      name="name"
                      maxLength={200}
                      value={name}
                      onChange={(e) => {
                        setName(e.target.value);
                        setFormError('');
                      }}
                      placeholder={t('namePlaceholder')}
                      className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      required
                    />
                  </label>

                  <label className="block space-y-1 text-xs">
                    <span className="font-bold text-[var(--ink)]">{t('city')} *</span>
                    <select
                      name="city"
                      value={city}
                      onChange={(e) => {
                        setCity(e.target.value);
                        setDeptFilter('ALL');
                        setAppFilter('ALL');
                        setFormError('');
                      }}
                      className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      required
                    >
                      <option value="">— {t('pickCity')} —</option>
                      {schema.cities.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label className="block space-y-1 text-xs">
                    <span className="font-bold text-[var(--ink)]">{t('nationality')} *</span>
                    <input
                      type="text"
                      name="nationality"
                      list="web-eval-nationalities"
                      maxLength={100}
                      value={nationality}
                      onChange={(e) => {
                        setNationality(e.target.value);
                        setFormError('');
                      }}
                      placeholder={t('nationalityPlaceholder')}
                      className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      required
                    />
                    <datalist id="web-eval-nationalities">
                      {NATIONALITY_SUGGESTIONS[lang].map((nat) => (
                        <option key={nat} value={nat} />
                      ))}
                    </datalist>
                  </label>

                  <label className="block space-y-1 text-xs sm:col-span-2">
                    <span className="font-bold text-[var(--ink)]">{t('phone')}</span>
                    <input
                      type="tel"
                      name="phone"
                      dir="ltr"
                      inputMode="tel"
                      maxLength={16}
                      value={phone}
                      onChange={(e) => setPhone(e.target.value)}
                      placeholder="05XXXXXXXX"
                      className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                    />
                  </label>
                </div>

                <div className="space-y-1.5 pt-1">
                  <span className="block text-xs font-bold text-[var(--ink)]">
                    {t('workApps')} *
                  </span>
                  <div className="flex flex-wrap gap-2">
                    {schema.apps.map((app) => {
                      const checked = workApps.includes(app);
                      return (
                        <label
                          key={app}
                          className={`inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs cursor-pointer ${
                            checked
                              ? 'border-[var(--accent)] bg-[var(--row-selected)] font-bold text-[var(--ink)]'
                              : 'border-[var(--line)] bg-[var(--bg)] text-[var(--muted)]'
                          }`}
                        >
                          <input
                            type="checkbox"
                            name="workApps"
                            value={app}
                            checked={checked}
                            onChange={(e) => {
                              if (e.target.checked) {
                                setWorkApps((prev) =>
                                  prev.includes(app) ? prev : [...prev, app]
                                );
                              } else {
                                setWorkApps((prev) => prev.filter((a) => a !== app));
                              }
                              setFormError('');
                            }}
                          />
                          <span>{platformDisplayName(app, lang)}</span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              </fieldset>

              {/* Staff Cards for Selected City */}
              {!city ? (
                <div className="rounded-xl border border-[var(--line)] bg-[var(--surface)] p-4 text-center text-xs text-[var(--muted)]">
                  {t('pickCity')}
                </div>
              ) : (
                <div className="space-y-3">
                  <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface)] p-3">
                    <span className="text-xs font-bold text-[var(--ink)]">
                      {t('cardsProgress')}:{' '}
                      <span className="num">
                        {answeredStaffCount} / {cityStaff.length}
                      </span>
                    </span>
                    <div className="flex flex-wrap items-center gap-2">
                      {departments.length > 1 && (
                        <select
                          value={deptFilter}
                          onChange={(e) => setDeptFilter(e.target.value)}
                          aria-label={t('deptLabel')}
                          className="rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2.5 py-1 text-xs"
                        >
                          <option value="ALL">{t('allDepts')}</option>
                          {departments.map((d) => (
                            <option key={d} value={d}>
                              {d}
                            </option>
                          ))}
                        </select>
                      )}
                      {staffApps.length > 1 && (
                        <select
                          value={appFilter}
                          onChange={(e) => setAppFilter(e.target.value)}
                          aria-label={t('allApps')}
                          className="rounded-lg border border-[var(--line)] bg-[var(--bg)] px-2.5 py-1 text-xs"
                        >
                          <option value="ALL">{t('allApps')}</option>
                          {staffApps.map((a) => (
                            <option key={a} value={a}>
                              {platformDisplayName(a, lang)}
                            </option>
                          ))}
                        </select>
                      )}
                    </div>
                  </div>

                  {visibleStaff.map((s) => {
                    const ans = staffAnswers[s.id] || {
                      reason: '',
                      ratings: {},
                      notes: '',
                      complaint: false,
                      complaintDetails: '',
                      attachments: [],
                    };
                    const done = isStaffCardComplete(s.id);
                    const roles = s.pendingRoleApproval
                      ? ['بانتظار موافقة غزال']
                      : s.roles || [];
                    return (
                      <article
                        key={s.id}
                        data-staff-card={s.id}
                        className={`rounded-2xl border bg-[var(--surface)] p-4 space-y-3 ${
                          done ? 'border-[var(--money-pos)]' : 'border-[var(--line)]'
                        }`}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div>
                            <h3 className="text-sm font-bold text-[var(--ink)]">{s.name}</h3>
                            <div className="flex flex-wrap gap-1.5 mt-1">
                              {(s.departments || []).map((d) => (
                                <span
                                  key={d}
                                  className="rounded-full bg-[var(--raised)] px-2 py-0.5 text-[11px] font-semibold text-[var(--muted)]"
                                >
                                  {d}
                                </span>
                              ))}
                              {(s.apps || []).map((a) => (
                                <span
                                  key={a}
                                  className="rounded-full bg-[var(--accent)]/15 px-2 py-0.5 text-[11px] font-semibold text-[var(--accent)]"
                                >
                                  {platformDisplayName(a, lang)}
                                </span>
                              ))}
                            </div>
                            {roles.length > 0 && (
                              <p className="text-xs text-[var(--muted)] mt-1">
                                {t('rolesLabel')}: {roles.join(' · ')}
                              </p>
                            )}
                          </div>
                          <span
                            className={`rounded-full px-2.5 py-0.5 text-xs font-bold ${
                              done
                                ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                                : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                            }`}
                          >
                            {done ? t('answeredBadge') : t('pendingBadge')}
                          </span>
                        </div>

                        <label className="block space-y-1 text-xs">
                          <span className="font-semibold text-[var(--muted)]">
                            {t('skipLabel')}
                          </span>
                          <select
                            data-staff-reason={s.id}
                            value={ans.reason}
                            onChange={(e) => {
                              const reason = e.target.value;
                              updateStaffDraft(s.id, (prev) => ({
                                ...prev,
                                reason,
                                ratings: reason !== '' ? {} : prev.ratings,
                              }));
                            }}
                            className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                          >
                            <option value="">★ {t('rateMode')}</option>
                            {schema.staffReasons.map((r) => (
                              <option key={r.id} value={r.id}>
                                {localizedLabel(r, lang)}
                              </option>
                            ))}
                          </select>
                        </label>

                        {ans.reason === '' && (
                          <div className="space-y-3 pt-1">
                            {schema.staffDimensions.map((dim) => {
                              const currentScore = ans.ratings[dim.id] || 0;
                              return (
                                <div key={dim.id} className="space-y-1">
                                  <div className="flex items-center justify-between text-xs">
                                    <span className="font-semibold text-[var(--ink)]">
                                      {localizedLabel(dim, lang)}
                                    </span>
                                    <span className="num font-bold text-[var(--accent)]">
                                      {currentScore ? `${currentScore} / 10` : '—'}
                                    </span>
                                  </div>
                                  <div className="grid grid-cols-5 sm:grid-cols-10 gap-1.5">
                                    {Array.from({ length: 10 }, (_, idx) => idx + 1).map((n) => (
                                      <button
                                        key={n}
                                        type="button"
                                        data-staff-rate={s.id}
                                        data-dim={dim.id}
                                        data-score={n}
                                        aria-pressed={currentScore === n}
                                        onClick={() =>
                                          updateStaffDraft(s.id, (prev) => ({
                                            ...prev,
                                            reason: '',
                                            ratings: { ...prev.ratings, [dim.id]: n },
                                          }))
                                        }
                                        className={`py-1.5 rounded-lg text-xs font-bold border transition ${
                                          currentScore === n
                                            ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                            : 'border-[var(--line)] bg-[var(--bg)] text-[var(--ink)] hover:bg-[var(--raised)]'
                                        }`}
                                      >
                                        {n}
                                      </button>
                                    ))}
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        )}

                        <label className="block space-y-1 text-xs">
                          <span className="font-semibold text-[var(--muted)]">{t('notes')}</span>
                          <textarea
                            rows={2}
                            maxLength={2000}
                            data-staff-notes={s.id}
                            value={ans.notes}
                            onChange={(e) =>
                              updateStaffDraft(s.id, (prev) => ({
                                ...prev,
                                notes: e.target.value,
                              }))
                            }
                            className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                          />
                        </label>

                        <div className="grid gap-2 sm:grid-cols-2 text-xs" data-staff-attachments={s.id}>
                          <label className="block space-y-1">
                            <span className="font-semibold text-[var(--muted)]">صور (حتى 2)</span>
                            <input
                              type="file"
                              accept="image/jpeg,image/png,image/webp"
                              multiple
                              onChange={async (e) => {
                                const files = Array.from(e.target.files || []).slice(0, 2);
                                e.target.value = '';
                                const images: EvalAttachmentDraft[] = [];
                                for (const f of files) {
                                  try {
                                    images.push(await shrinkImageForEvaluation(f));
                                  } catch {
                                    setFormError(t('err_invalid_request'));
                                  }
                                }
                                updateStaffDraft(s.id, (prev) => ({
                                  ...prev,
                                  attachments: [
                                    ...images,
                                    ...(prev.attachments || []).filter((a) => a.kind === 'voice'),
                                  ].slice(0, 3),
                                }));
                              }}
                              className="w-full text-[11px]"
                            />
                          </label>
                          <label className="block space-y-1">
                            <span className="font-semibold text-[var(--muted)]">صوت (≤60ث)</span>
                            <input
                              type="file"
                              accept="audio/webm,audio/ogg,audio/mpeg,audio/mp4,audio/wav,audio/aac"
                              onChange={async (e) => {
                                const f = e.target.files?.[0];
                                e.target.value = '';
                                if (!f) return;
                                try {
                                  const voice = await voiceFileForEvaluation(f, 30);
                                  updateStaffDraft(s.id, (prev) => ({
                                    ...prev,
                                    attachments: [
                                      ...(prev.attachments || []).filter((a) => a.kind === 'image').slice(0, 2),
                                      voice,
                                    ],
                                  }));
                                } catch {
                                  setFormError(t('err_invalid_request'));
                                }
                              }}
                              className="w-full text-[11px]"
                            />
                          </label>
                          {(ans.attachments || []).length > 0 && (
                            <p className="sm:col-span-2 text-[11px] text-[var(--muted)]">
                              {(ans.attachments || []).map((a) => a.fileName).join(' · ')}
                            </p>
                          )}
                        </div>

                        <div className="space-y-2 pt-1">
                          <button
                            type="button"
                            data-staff-complaint-btn={s.id}
                            aria-pressed={ans.complaint}
                            onClick={() =>
                              updateStaffDraft(s.id, (prev) => ({
                                ...prev,
                                complaint: !prev.complaint,
                                complaintDetails: !prev.complaint ? prev.complaintDetails : '',
                              }))
                            }
                            className={`px-3 py-1.5 rounded-lg text-xs font-bold border transition ${
                              ans.complaint
                                ? 'border-[var(--danger)] bg-[var(--danger)]/15 text-[var(--danger)]'
                                : 'border-[var(--line)] bg-[var(--raised)] text-[var(--ink)]'
                            }`}
                          >
                            🛡️ {ans.complaint ? t('complaintActive') : t('complaintBtn')}
                          </button>

                          {ans.complaint && (
                            <label className="block space-y-1 text-xs">
                              <span className="font-bold text-[var(--danger)]">
                                {t('complaintDetails')} *
                              </span>
                              <textarea
                                rows={2}
                                minLength={5}
                                maxLength={2000}
                                data-staff-complaint-text={s.id}
                                value={ans.complaintDetails}
                                onChange={(e) =>
                                  updateStaffDraft(s.id, (prev) => ({
                                    ...prev,
                                    complaintDetails: e.target.value,
                                  }))
                                }
                                className="w-full rounded-lg border border-[var(--danger)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                                required
                              />
                            </label>
                          )}
                        </div>
                      </article>
                    );
                  })}
                </div>
              )}

              {schema.uploadsPolicyPending && (
                <p className="text-[11px] text-[var(--muted)]">{t('uploadsPending')}</p>
              )}

              {formError && (
                <div
                  className="rounded-xl border border-[var(--danger)] bg-[var(--danger)]/10 p-3 text-xs font-bold text-[var(--danger)]"
                  role="alert"
                >
                  {formError}
                </div>
              )}

              <div className="flex justify-end">
                <button
                  type="button"
                  id="eval-next-btn"
                  onClick={handleNextPage}
                  className="rounded-xl bg-[var(--accent)] px-5 py-2.5 text-xs font-bold text-[var(--accent-ink)]"
                >
                  {t('nextBtn')}
                </button>
              </div>
            </div>
          ) : (
            <div className="space-y-4" data-testid="evaluation-page-2">
              <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-[var(--raised)] px-3.5 py-2 text-xs font-bold text-[var(--ink)]">
                <span>{t('page2Title')}</span>
                <span>
                  {t('cardsProgress')}:{' '}
                  <span className="num">
                    {answeredAppCount} / {schema.apps.length}
                  </span>
                </span>
              </div>

              {schema.apps.map((app) => {
                const ans = appAnswers[app] || {
                  reason: '',
                  overall: 0,
                  factorRatings: {},
                  notes: '',
                };
                const done = isAppCardComplete(app);
                return (
                  <article
                    key={app}
                    data-app-card={app}
                    className={`rounded-2xl border bg-[var(--surface)] p-4 space-y-3 ${
                      done ? 'border-[var(--money-pos)]' : 'border-[var(--line)]'
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <h3 className="text-sm font-bold text-[var(--ink)]">
                        {platformDisplayName(app, lang)}
                      </h3>
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-bold ${
                          done
                            ? 'bg-[var(--money-pos)]/15 text-[var(--money-pos)]'
                            : 'bg-[var(--warning)]/15 text-[var(--warning)]'
                        }`}
                      >
                        {done ? t('answeredBadge') : t('pendingBadge')}
                      </span>
                    </div>

                    <label className="block space-y-1 text-xs">
                      <span className="font-semibold text-[var(--muted)]">{t('skipLabel')}</span>
                      <select
                        data-app-reason={app}
                        value={ans.reason}
                        onChange={(e) => {
                          const reason = e.target.value;
                          updateAppDraft(app, (prev) => ({
                            ...prev,
                            reason,
                            overall: reason !== '' ? 0 : prev.overall,
                            factorRatings: reason !== '' ? {} : prev.factorRatings,
                          }));
                        }}
                        className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      >
                        <option value="">★ {t('appRateMode')}</option>
                        {schema.appReasons.map((r) => (
                          <option key={r.id} value={r.id}>
                            {localizedLabel(r, lang)}
                          </option>
                        ))}
                      </select>
                    </label>

                    {ans.reason === '' && (
                      <div className="space-y-3 pt-1">
                        <div className="space-y-1">
                          <div className="flex items-center justify-between text-xs">
                            <span className="font-semibold text-[var(--ink)]">
                              {t('starsLabel')}
                            </span>
                            <span className="num font-bold text-[var(--accent)]">
                              {ans.overall ? `${ans.overall} / 5 ★` : '—'}
                            </span>
                          </div>
                          <div className="grid grid-cols-5 gap-1.5">
                            {[1, 2, 3, 4, 5].map((star) => (
                              <button
                                key={star}
                                type="button"
                                data-app-star={app}
                                data-stars={star}
                                aria-pressed={ans.overall === star}
                                onClick={() =>
                                  updateAppDraft(app, (prev) => ({
                                    ...prev,
                                    reason: '',
                                    overall: star,
                                  }))
                                }
                                className={`py-2 rounded-lg text-xs font-bold border transition ${
                                  ans.overall === star
                                    ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                    : 'border-[var(--line)] bg-[var(--bg)] text-[var(--ink)] hover:bg-[var(--raised)]'
                                }`}
                              >
                                {star} ★
                              </button>
                            ))}
                          </div>
                        </div>

                        <div className="space-y-2">
                          <span className="block text-xs font-semibold text-[var(--ink)]">
                            {t('factorsTitle')}{' '}
                            <span className="font-normal text-[var(--muted)]">
                              ({t('factorOptional')})
                            </span>
                          </span>
                          <div className="grid gap-2">
                            {schema.factors.map((f) => {
                              const stars = ans.factorRatings[f.id] || 0;
                              return (
                                <div
                                  key={f.id}
                                  data-app-factor-row={app}
                                  data-factor-id={f.id}
                                  className="space-y-1"
                                >
                                  <div className="flex items-center justify-between text-[11px]">
                                    <span className="font-semibold text-[var(--ink)]">
                                      {localizedLabel(f, lang)}
                                    </span>
                                    <span className="num text-[var(--muted)]">
                                      {stars ? `${stars} / 5 ★` : '—'}
                                    </span>
                                  </div>
                                  <div className="grid grid-cols-5 gap-1">
                                    {[1, 2, 3, 4, 5].map((star) => (
                                      <button
                                        key={star}
                                        type="button"
                                        data-app-factor-star={app}
                                        data-factor-id={f.id}
                                        data-stars={star}
                                        aria-pressed={stars === star}
                                        onClick={() =>
                                          updateAppDraft(app, (prev) => {
                                            const next = { ...prev.factorRatings };
                                            if (next[f.id] === star) delete next[f.id];
                                            else next[f.id] = star;
                                            return {
                                              ...prev,
                                              reason: '',
                                              factorRatings: next,
                                            };
                                          })
                                        }
                                        className={`py-1.5 rounded-md text-[11px] font-bold border transition ${
                                          stars === star
                                            ? 'border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-ink)]'
                                            : 'border-[var(--line)] bg-[var(--bg)] text-[var(--ink)] hover:bg-[var(--raised)]'
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
                        </div>
                      </div>
                    )}

                    <label className="block space-y-1 text-xs">
                      <span className="font-semibold text-[var(--muted)]">{t('notes')}</span>
                      <textarea
                        rows={2}
                        maxLength={2000}
                        data-app-notes={app}
                        value={ans.notes}
                        onChange={(e) =>
                          updateAppDraft(app, (prev) => ({
                            ...prev,
                            notes: e.target.value,
                          }))
                        }
                        className="w-full rounded-lg border border-[var(--line)] bg-[var(--bg)] px-3 py-2 text-xs text-[var(--ink)]"
                      />
                    </label>
                  </article>
                );
              })}

              {formError && (
                <div
                  className="rounded-xl border border-[var(--danger)] bg-[var(--danger)]/10 p-3 text-xs font-bold text-[var(--danger)]"
                  role="alert"
                >
                  {formError}
                </div>
              )}

              <div className="flex flex-wrap items-center justify-between gap-2">
                <button
                  type="button"
                  id="eval-prev-btn"
                  onClick={() => {
                    setFormError('');
                    setPage(1);
                  }}
                  className="rounded-xl border border-[var(--line)] bg-[var(--surface)] px-4 py-2.5 text-xs font-bold text-[var(--ink)]"
                >
                  {t('prevBtn')}
                </button>
                <button
                  type="submit"
                  id="eval-submit-btn"
                  disabled={submitting}
                  className="rounded-xl bg-[var(--accent)] px-6 py-2.5 text-xs font-bold text-[var(--accent-ink)] disabled:opacity-50"
                >
                  {submitting ? t('submitting') : t('submitBtn')}
                </button>
              </div>
            </div>
          )}
        </form>
      )}
    </div>
  );
}
