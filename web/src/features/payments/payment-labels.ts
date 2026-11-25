/** Pure Arabic labels & contract-aligned helpers for S-PAY (no runtime API imports). */

/** org.cost_centre sentinel «بدون مركز تكلفة» (SchemaOrgCostCentre.NoneId). */
export const NONE_COST_CENTRE_ID = '019c18d5-62e1-7000-8000-00000000cc00';

export interface CoaAccountOption {
  id: string;
  code: string;
  nameAr: string;
  parentCode?: string;
  isPostable: boolean;
  isActive: boolean;
}

export const DOCUMENT_STATUS_LABELS: Record<number, string> = {
  1: 'مسودة',
  2: 'مرسل للاعتماد',
  3: 'معتمد',
  4: 'مسدد',
  5: 'ملغى',
};

export const DOCUMENT_STATUS_TONES: Record<number, 'neutral' | 'warning' | 'info' | 'success' | 'danger'> = {
  1: 'neutral',
  2: 'warning',
  3: 'info',
  4: 'success',
  5: 'danger',
};

export const VOUCHER_STATUS_LABELS: Record<number, string> = {
  1: 'محجوز بانتظار الاعتماد والصرف',
  2: 'مسدد ومرحّل',
  3: 'معكوس',
  4: 'ملغى',
};

export const DOCUMENT_TYPE_LABELS: Record<string, string> = {
  quick: 'دفعة سريعة',
  expense: 'مصروف',
  purchase: 'فاتورة مشتريات',
  rent: 'إيجار',
  installment: 'قسط تمويل',
  hr_fee: 'رسوم موارد بشرية',
  traffic_violation: 'مخالفة مرورية',
  government: 'سداد حكومي',
  fuel_topup: 'تمويل محفظة وقود',
  prepaid_topup: 'شحن محفظة مقدمة',
  mada_topup: 'تغذية محفظة مدا',
  advance: 'سلفة مندوب',
  hs_topup: 'شحن محفظة هنقرستيشن',
};

export const BEARER_LABELS: Record<string, string> = {
  rider: 'مندوب',
  company: 'شركة',
  split: 'مشترك',
  cash_collection: 'تحصيل نقدي',
};

export const PROPERTY_TYPE_LABELS: Record<string, string> = {
  rider_housing: 'سكن مناديب',
  admin_housing: 'سكن إداري',
  office: 'مكتب',
};

export const FREQUENCY_LABELS: Record<number, string> = {
  3: 'ربع سنوي',
  6: 'نصف سنوي',
  12: 'سنوي',
};

export const HR_FEE_CATEGORIES = [
  { value: 'إقامة', label: 'رسوم إقامة' },
  { value: 'جواز سفر', label: 'رسوم جواز سفر' },
  { value: 'رخصة قيادة', label: 'رخصة قيادة' },
  { value: 'كرت سائق', label: 'كرت سائق' },
  { value: 'تأمين طبي', label: 'تأمين طبي' },
  { value: 'نقل كفالة', label: 'نقل كفالة' },
  { value: 'خروج وعودة', label: 'تأشيرة خروج وعودة' },
] as const;

export const ADVANCE_TYPE_LABELS: Record<string, string> = {
  cash: 'سلفة نقدية',
  bank: 'سلفة بنكية',
  internet: 'سلفة إنترنت',
  hs_topup: 'شحن محفظة هنقرستيشن',
};

export const FULFILMENT_LABELS: Record<string, string> = {
  cash: 'نقدي',
  bank: 'تحويل بنكي',
  recharge_card: 'بطاقة شحن مدا',
};

export const GOV_CATEGORY_LABELS: Record<string, string> = {
  vat: 'ضريبة القيمة المضافة',
  zakat: 'الزكاة وهيئة الزكاة',
  sadad: 'رسوم حكومية وسداد',
};

export function hasAllowedAction(
  item: { allowedActions?: string[] } | null | undefined,
  action: string
): boolean {
  return Array.isArray(item?.allowedActions) && item.allowedActions.includes(action);
}

export function documentStatusLabel(doc: { status: number; amount: number; amountPaid?: number }): string {
  if (Number(doc.status) === 3 && Number(doc.amountPaid || 0) >= Number(doc.amount || 0) && Number(doc.amount || 0) > 0) {
    return 'مسدد بالكامل';
  }
  return DOCUMENT_STATUS_LABELS[Number(doc.status)] || 'غير محدد';
}

export function documentMeta(doc: { metadata?: unknown }): Record<string, unknown> {
  const raw = doc.metadata;
  return raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {};
}

export function defaultDateInPeriod(period: string): string {
  const today = new Date().toISOString().slice(0, 10);
  if (/^\d{4}-(0[1-9]|1[0-2])$/.test(period)) {
    if (today.startsWith(period)) return today;
    return `${period}-01`;
  }
  return today;
}

export function newIdempotencyKey(prefix: string): string {
  const rand =
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  return `${prefix}:${rand}`;
}

export function isBankOrCashCode(code: string): boolean {
  const clean = code.trim();
  return clean.startsWith('1111') || clean.startsWith('1112') || clean.startsWith('113');
}

export function accountOptions(
  accounts: CoaAccountOption[],
  predicate?: (a: CoaAccountOption) => boolean
): Array<{ value: string; label: string }> {
  return accounts
    .filter((a) => (predicate ? predicate(a) : true))
    .map((a) => ({ value: a.code, label: `${a.code} — ${a.nameAr}` }));
}
