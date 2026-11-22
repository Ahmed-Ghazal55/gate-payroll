export type EvalLanguage = 'ar' | 'en' | 'ur';

export const NATIONALITY_IDS = [
  'saudi',
  'egyptian',
  'yemeni',
  'sudanese',
  'pakistani',
  'indian',
  'bangladeshi',
  'syrian',
  'jordanian',
  'filipino',
] as const;

export const NATIONALITY_LABELS: Record<(typeof NATIONALITY_IDS)[number], Record<EvalLanguage, string>> = {
  saudi: { ar: 'سعودي', en: 'Saudi', ur: 'سعودی' },
  egyptian: { ar: 'مصري', en: 'Egyptian', ur: 'مصری' },
  yemeni: { ar: 'يمني', en: 'Yemeni', ur: 'یمنی' },
  sudanese: { ar: 'سوداني', en: 'Sudanese', ur: 'سوڈانی' },
  pakistani: { ar: 'باكستاني', en: 'Pakistani', ur: 'پاکستانی' },
  indian: { ar: 'هندي', en: 'Indian', ur: 'بھارتی' },
  bangladeshi: { ar: 'بنغلاديشي', en: 'Bangladeshi', ur: 'بنگلہ دیشی' },
  syrian: { ar: 'سوري', en: 'Syrian', ur: 'شامی' },
  jordanian: { ar: 'أردني', en: 'Jordanian', ur: 'اردنی' },
  filipino: { ar: 'فلبيني', en: 'Filipino', ur: 'فلپائنی' },
};

export const NATIONALITY_SUGGESTIONS: Record<EvalLanguage, string[]> = {
  ar: NATIONALITY_IDS.map((id) => NATIONALITY_LABELS[id].ar),
  en: NATIONALITY_IDS.map((id) => NATIONALITY_LABELS[id].en),
  ur: NATIONALITY_IDS.map((id) => NATIONALITY_LABELS[id].ur),
};

export function nationalityIdFromLabel(raw: string): string {
  const value = String(raw || '').trim();
  for (const id of NATIONALITY_IDS) {
    const labels = NATIONALITY_LABELS[id];
    if (value === id || value === labels.ar || value === labels.en || value === labels.ur) return id;
  }
  return value;
}

export const PLATFORM_NAMES: Record<string, Record<EvalLanguage, string>> = {
  Jahez: { ar: 'جاهز', en: 'Jahez', ur: 'جاہز' },
  Keeta: { ar: 'كيتا', en: 'Keeta', ur: 'کیٹا' },
  HungerStation: { ar: 'هنقرستيشن', en: 'HungerStation', ur: 'ہنگر اسٹیشن' },
  Ninja: { ar: 'نينجا', en: 'Ninja', ur: 'ننجا' },
  Chefz: { ar: 'ذا شفز', en: 'The Chefz', ur: 'دی شیفز' },
  Amazon: { ar: 'أمازون', en: 'Amazon', ur: 'ایمیزون' },
};

export const STAFF_DIMENSION_LABELS_AR: Record<string, string> = {
  professional: 'المستوى الفني والمهني',
  treatment: 'الاحترام وطريقة التعامل',
  support: 'الدعم المقدم',
  responseSpeed: 'سرعة الاستجابة',
  problemSolving: 'حل المشكلات وتسهيل العمل',
};

export const STAFF_SKIP_REASON_LABELS_AR: Record<string, string> = {
  not_dealt: 'لم أتعامل معه',
  unknown_person: 'لا أعرفه',
  not_worked_app: 'لم أعمل في منصته',
};

export const APP_SKIP_REASON_LABELS_AR: Record<string, string> = {
  unavailable_city: 'غير متاح في مكتب المدينة',
  not_worked: 'لم أعمل عليها',
};

export const FACTOR_LABELS_AR: Record<string, string> = {
  orderVolume: 'عدد الطلبات',
  fairPay: 'عدالة احتساب الأجر',
  workingHours: 'عدد ساعات العمل',
  shiftTimes: 'مواعيد الورديات',
  flexibility: 'المرونة',
  customerService: 'خدمة عملاء المنصة',
  appProblems: 'مشكلات التطبيق',
  worthIt: 'هل العمل يستحق',
};

export const ROUND_STATUS_LABELS_AR: Record<string, string> = {
  draft: 'مسودة',
  open: 'مفتوحة للاستقبال',
  paused: 'متوقفة مؤقتًا',
  closed: 'مغلقة',
};

export function normalizeEvalDigits(raw: string): string {
  return String(raw || '')
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .trim();
}

export function localizedLabel(
  item: { labelAr?: string; labelEn?: string; labelUr?: string } | undefined,
  lang: EvalLanguage
): string {
  if (!item) return '';
  if (lang === 'en') return item.labelEn || item.labelAr || '';
  if (lang === 'ur') return item.labelUr || item.labelAr || '';
  return item.labelAr || '';
}

export function platformDisplayName(app: string, lang: EvalLanguage = 'ar'): string {
  return PLATFORM_NAMES[app]?.[lang] || app;
}
