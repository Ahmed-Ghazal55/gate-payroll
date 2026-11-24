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

/** Known evaluation staff departments (matches server StaffDepartmentOptions). */
export const STAFF_DEPARTMENT_OPTIONS = [
  'الحركة والمركبات',
  'المالية والحسابات',
  'الموارد البشرية',
  'الإدارة',
  'الدعم الفني والكول سنتر',
  'العمليات',
] as const;

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

export type EvalAttachmentDraft = {
  kind: 'image' | 'voice';
  fileName: string;
  contentType: string;
  dataBase64: string;
  durationSeconds?: number;
};

/** Shrink an image on the device (~JPEG ≤ ~300KB) before upload. */
export async function shrinkImageForEvaluation(file: File): Promise<EvalAttachmentDraft> {
  const bitmap = await createImageBitmap(file);
  const maxSide = 1280;
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  const w = Math.max(1, Math.round(bitmap.width * scale));
  const h = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('canvas');
  ctx.drawImage(bitmap, 0, 0, w, h);
  bitmap.close();
  let quality = 0.72;
  let dataUrl = canvas.toDataURL('image/jpeg', quality);
  while (dataUrl.length > 400_000 && quality > 0.4) {
    quality -= 0.08;
    dataUrl = canvas.toDataURL('image/jpeg', quality);
  }
  const dataBase64 = dataUrl.split(',')[1] || '';
  return {
    kind: 'image',
    fileName: (file.name.replace(/\.[^.]+$/, '') || 'photo') + '.jpg',
    contentType: 'image/jpeg',
    dataBase64,
  };
}

export async function voiceFileForEvaluation(
  file: File,
  durationSeconds: number
): Promise<EvalAttachmentDraft> {
  if (durationSeconds < 1 || durationSeconds > 60) throw new Error('voice_duration');
  const buf = await file.arrayBuffer();
  if (buf.byteLength > 1024 * 1024) throw new Error('voice_too_large');
  const bytes = new Uint8Array(buf);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]!);
  return {
    kind: 'voice',
    fileName: file.name || 'note.webm',
    contentType: file.type || 'audio/webm',
    dataBase64: btoa(binary),
    durationSeconds: Math.round(durationSeconds),
  };
}

export const EVAL_IDENTITY_STORAGE_KEY = 'gate_eval_identity_v1';

export type SuggestedChipKind = 'staff' | 'movement' | 'hr' | 'finance' | 'support' | 'app' | 'systems';

const CHIP_SETS: Record<SuggestedChipKind, Record<EvalLanguage, string[]>> = {
  staff: {
    ar: [
      'متعاون ويرد بسرعة',
      'يحل المشاكل من أول مرة',
      'محترم في التعامل',
      'لا يرد على الاتصالات أو الرسائل',
      'يتأخر في حل المشاكل',
      'أسلوب تعامل غير مناسب',
      'لا يشرح الإجراءات بوضوح',
      'يفرّق في المعاملة بين المناديب',
    ],
    en: [
      'Helpful and replies quickly',
      'Solves issues the first time',
      'Respectful in dealing',
      'Does not answer calls or messages',
      'Slow to solve problems',
      'Inappropriate manner',
      'Does not explain procedures clearly',
      'Treats riders unequally',
    ],
    ur: [
      'مددگار اور فوری جواب دیتا ہے',
      'پہلی بار میں مسائل حل کرتا ہے',
      'احترام سے پیش آتا ہے',
      'کال یا میسج کا جواب نہیں دیتا',
      'مسائل حل کرنے میں دیر کرتا ہے',
      'نامناسب رویہ',
      'طریقہ کار واضح نہیں بتاتا',
      'منادیب کے ساتھ فرق کرتا ہے',
    ],
  },
  movement: {
    ar: ['تأخر صيانة المركبة', 'مشاكل في السكن', 'تأخر توفير مركبة بديلة'],
    en: ['Vehicle maintenance delay', 'Housing problems', 'Delay providing a replacement vehicle'],
    ur: ['گاڑی کی مرمت میں تاخیر', 'رہائش کے مسائل', 'متبادل گاڑی فراہم کرنے میں تاخیر'],
  },
  hr: {
    ar: ['تأخر الإقامة أو الأوراق الرسمية', 'تأخر الرد على طلبات الإجازة'],
    en: ['Iqama or papers delayed', 'Slow reply to leave requests'],
    ur: ['اقامہ یا کاغذات میں تاخیر', 'چھٹی کی درخواست پر تاخیر'],
  },
  finance: {
    ar: [
      'الراتب ناقص أو فيه خصم غير مفهوم',
      'تأخر الصرف',
      'تأخر تعديل الفرق بعد الإبلاغ',
      'لا يوجد رد على استفسار الراتب',
      'الحساب واضح وصحيح',
    ],
    en: [
      'Salary short or unclear deduction',
      'Payout delayed',
      'Correction after report was slow',
      'No reply to salary inquiry',
      'Account is clear and correct',
    ],
    ur: [
      'تنخواہ کم یا غیر واضح کٹوتی',
      'ادائیگی میں تاخیر',
      'اطلاع کے بعد فرق درست کرنے میں تاخیر',
      'تنخواہ کے سوال کا جواب نہیں',
      'حساب واضح اور درست',
    ],
  },
  support: {
    ar: ['طول الانتظار على الخط', 'لم تُحل المشكلة بعد المكالمة'],
    en: ['Long wait on the line', 'Issue not solved after the call'],
    ur: ['لائن پر طویل انتظار', 'کال کے بعد مسئلہ حل نہیں ہوا'],
  },
  app: {
    ar: ['الطلبات قليلة', 'أوقات الشفتات غير مناسبة', 'مشاكل في التطبيق نفسه', 'خدمة عملاء المنصة لا ترد', 'الشغل يستاهل'],
    en: ['Few orders', 'Shift times not suitable', 'Problems in the app itself', 'Platform support does not reply', 'The work is worth it'],
    ur: ['آرڈرز کم', 'شفٹ اوقات مناسب نہیں', 'ایپ میں مسائل', 'پلیٹ فارم سپورٹ جواب نہیں دیتی', 'کام قابل قدر ہے'],
  },
  systems: {
    ar: [
      'النماذج سهلة وواضحة',
      'البيانات اللي بتوصلني فيها أخطاء',
      'الجداول غير واضحة',
      'لا يوجد رد على مقترحاتي أو طلباتي',
      'الروابط لا تعمل أو بطيئة',
    ],
    en: [
      'Forms are easy and clear',
      'The data I receive has errors',
      'Tables are unclear',
      'No reply to my suggestions or requests',
      'Links do not work or are slow',
    ],
    ur: [
      'فارم آسان اور واضح ہیں',
      'آنے والے ڈیٹا میں غلطیاں ہیں',
      'جدول واضح نہیں',
      'میری تجاویز یا درخواستوں کا جواب نہیں',
      'لنکس کام نہیں کرتے یا سست ہیں',
    ],
  },
};

export function sectionChipKindFromKey(sectionKey: string | undefined | null): SuggestedChipKind {
  if (!sectionKey) return 'staff';
  if (sectionKey.startsWith('app:')) return 'app';
  if (sectionKey === 'dept:movement') return 'movement';
  if (sectionKey === 'dept:hr') return 'hr';
  if (sectionKey === 'dept:finance') return 'finance';
  if (sectionKey === 'dept:support') return 'support';
  return 'staff';
}

export function suggestedChipsFor(kind: SuggestedChipKind, lang: EvalLanguage): string[] {
  const base = CHIP_SETS.staff[lang] || CHIP_SETS.staff.ar;
  if (kind === 'staff') return base;
  if (kind === 'app') return CHIP_SETS.app[lang] || CHIP_SETS.app.ar;
  const extra = CHIP_SETS[kind][lang] || CHIP_SETS[kind].ar;
  if (kind === 'finance' || kind === 'systems') return [...extra];
  return [...base, ...extra];
}

export function appendSuggestedChip(current: string, chip: string): string {
  const value = String(current || '').trim();
  if (!value) return chip;
  if (value.includes(chip)) return value;
  return `${value}\n${chip}`;
}
