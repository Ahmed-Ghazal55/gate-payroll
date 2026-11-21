export interface FormattedEntry {
  key: string;
  label?: string;
  value: string;
}

const KEY_LABELS_AR: Record<string, string> = {
  id: 'المعرف',
  code: 'الرمز',
  name: 'الاسم',
  nameAr: 'الاسم',
  nameEn: 'الاسم الإنجليزي',
  title: 'العنوان',
  titleAr: 'العنوان',
  label: 'التسمية',
  type: 'النوع',
  kind: 'التصنيف',
  category: 'الفئة',
  status: 'الحالة',
  state: 'الحالة',
  amount: 'المبلغ',
  total: 'الإجمالي',
  totalAmount: 'إجمالي المبلغ',
  subtotal: 'المجموع الفرعي',
  tax: 'الضريبة',
  taxAmount: 'قيمة الضريبة',
  vat: 'ضريبة القيمة المضافة',
  net: 'الصافي',
  gross: 'الإجمالي',
  rate: 'المعدل',
  fee: 'الرسوم',
  cost: 'التكلفة',
  price: 'السعر',
  value: 'القيمة',
  valueTo: 'إلى قيمة',
  min: 'الحد الأدنى',
  max: 'الحد الأقصى',
  minOrders: 'الحد الأدنى للطلبات',
  maxOrders: 'الحد الأقصى للطلبات',
  threshold: 'الحد الأدنى',
  cap: 'السقف',
  limit: 'الحد',
  from: 'من',
  to: 'إلى',
  start: 'البداية',
  end: 'النهاية',
  startDate: 'تاريخ البداية',
  endDate: 'تاريخ النهاية',
  effectiveFrom: 'ساري من',
  effectiveTo: 'ساري إلى',
  date: 'التاريخ',
  period: 'الفترة',
  month: 'الشهر',
  year: 'السنة',
  days: 'الأيام',
  workDays: 'أيام العمل',
  orders: 'الطلبات',
  count: 'العدد',
  percent: 'النسبة %',
  percentage: 'النسبة %',
  metric: 'المقياس',
  operator: 'المعامل',
  platform: 'المنصة',
  app: 'التطبيق',
  company: 'الشركة',
  companyId: 'الشركة',
  sponsor: 'الكفيل',
  sponsorId: 'الكفيل',
  city: 'المدينة',
  iqama: 'رقم الإقامة',
  rider: 'المندوب',
  riderId: 'المندوب',
  riderName: 'اسم المندوب',
  account: 'الحساب',
  accountCode: 'رقم الحساب',
  vault: 'الخزنة',
  vaultCode: 'رمز الخزنة',
  debit: 'مدين',
  credit: 'دائن',
  balance: 'الرصيد',
  note: 'ملاحظة',
  notes: 'ملاحظات',
  reason: 'السبب',
  description: 'الوصف',
  summary: 'الملخص',
  message: 'الرسالة',
  enabled: 'مفعّل',
  active: 'نشط',
  version: 'الإصدار',
  createdAt: 'تاريخ الإنشاء',
  updatedAt: 'تاريخ التحديث',
  createdBy: 'أنشئ بواسطة',
  approvedBy: 'اعتمد بواسطة',
  approvedAt: 'تاريخ الاعتماد',
  tiers: 'الشرائح',
  rules: 'القواعد',
  items: 'العناصر',
  lines: 'الأسطر',
  entries: 'القيود',
  fields: 'الحقول',
  mode: 'الوضع',
  currency: 'العملة',
  salary: 'الراتب',
  rent: 'الإيجار',
  fuel: 'الوقود',
  housing: 'السكن',
  deduction: 'الاستقطاع',
  bonus: 'المكافأة',
  commission: 'العمولة',
  penalty: 'الغرامة',
  source: 'المصدر',
  reference: 'المرجع',
  receiptRef: 'رقم الإيصال',
  iban: 'الآيبان',
  phone: 'الجوال',
};

export function labelKeyAr(key: string): string {
  if (!key) return '';
  if (KEY_LABELS_AR[key]) return KEY_LABELS_AR[key];
  const lower = key.charAt(0).toLowerCase() + key.slice(1);
  if (KEY_LABELS_AR[lower]) return KEY_LABELS_AR[lower];
  return key;
}

export function formatReadableValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'boolean') return value ? 'نعم' : 'لا';
  if (typeof value === 'number') return String(value);
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) {
    if (value.length === 0) return '—';
    return value.map((item) => formatReadableValue(item)).join('، ');
  }
  if (typeof value === 'object') {
    const parts = Object.entries(value as Record<string, unknown>)
      .filter(([, v]) => v !== null && v !== undefined && v !== '')
      .map(([k, v]) => `${labelKeyAr(k)}: ${formatReadableValue(v)}`);
    return parts.length > 0 ? parts.join(' · ') : '—';
  }
  return String(value);
}

export function formatRecordEntries(record: unknown): FormattedEntry[] {
  if (!record || typeof record !== 'object' || Array.isArray(record)) return [];
  return Object.entries(record as Record<string, unknown>).map(([key, val]) => ({
    key: labelKeyAr(key),
    label: labelKeyAr(key),
    value: formatReadableValue(val),
  }));
}
