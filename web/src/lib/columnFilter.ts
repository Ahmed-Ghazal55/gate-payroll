export type ColumnDataType = 'text' | 'number' | 'date' | 'list';
export type ColumnFilterType = ColumnDataType;

export type BadgeTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'accent' | string;

export interface ColumnFilterOption {
  value: string;
  label: string;
  count?: number;
  tone?: BadgeTone;
}

export type TextFilterOp = 'contains' | 'notContains' | 'beginsWith' | 'endsWith' | 'eq' | 'neq';
export type NumberFilterOp = 'eq' | 'neq' | 'gt' | 'gte' | 'lt' | 'lte' | 'between' | 'topN';
export type DateFilterOp = 'eq' | 'neq' | 'before' | 'after' | 'between' | 'thisMonth' | 'lastMonth';
export type FilterOperator = TextFilterOp | NumberFilterOp | DateFilterOp | 'in' | 'color';

export interface ColumnFilterValue {
  /** Condition operator (text, number, date) */
  op?: FilterOperator;
  /** Primary condition operand (text query, number, date YYYY-MM-DD, or N for topN) */
  value?: string;
  /** Second operand for 'between' */
  valueTo?: string;
  /** Checked raw values from the distinct value checkbox list. undefined = all values allowed. */
  values?: string[];
  /** Active color tone filter */
  colorTone?: BadgeTone;
}

export interface ServerFilterClause {
  col: string;
  op: FilterOperator;
  value?: string;
  valueTo?: string;
  values?: string[];
}

export interface ServerSortClause {
  col: string;
  dir: 'asc' | 'desc';
  tone?: string;
}

export const TEXT_OPERATORS: ReadonlyArray<{ op: TextFilterOp; label: string }> = [
  { op: 'contains', label: 'يحتوي على' },
  { op: 'notContains', label: 'لا يحتوي على' },
  { op: 'beginsWith', label: 'يبدأ بـ' },
  { op: 'endsWith', label: 'ينتهي بـ' },
  { op: 'eq', label: 'يساوي' },
  { op: 'neq', label: 'لا يساوي' },
];

export const NUMBER_OPERATORS: ReadonlyArray<{ op: NumberFilterOp; label: string }> = [
  { op: 'eq', label: 'يساوي (=)' },
  { op: 'neq', label: 'لا يساوي (≠)' },
  { op: 'gt', label: 'أكبر من (>)' },
  { op: 'gte', label: 'أكبر من أو يساوي (≥)' },
  { op: 'lt', label: 'أصغر من (<)' },
  { op: 'lte', label: 'أصغر من أو يساوي (≤)' },
  { op: 'between', label: 'بين قيمتين' },
  { op: 'topN', label: 'أعلى N قيم' },
];

export const DATE_OPERATORS: ReadonlyArray<{ op: DateFilterOp; label: string }> = [
  { op: 'eq', label: 'يساوي تاريخ' },
  { op: 'neq', label: 'لا يساوي تاريخ' },
  { op: 'before', label: 'قبل تاريخ' },
  { op: 'after', label: 'بعد تاريخ' },
  { op: 'between', label: 'بين تاريخين' },
  { op: 'thisMonth', label: 'هذا الشهر' },
  { op: 'lastMonth', label: 'الشهر الماضي' },
];

export const TONE_META: Record<
  string,
  { label: string; swatch: string; rank: number }
> = {
  success: { label: 'أخضر (نشط / مكتمل)', swatch: '#16a34a', rank: 1 },
  info: { label: 'أزرق (معلومات)', swatch: '#2563eb', rank: 2 },
  accent: { label: 'بنفسجي (مميز)', swatch: '#7c3aed', rank: 3 },
  warning: { label: 'أصفر (تنبيه / معلق)', swatch: '#d97706', rank: 4 },
  danger: { label: 'أحمر (خطر / مرفوض)', swatch: '#dc2626', rank: 5 },
  neutral: { label: 'رمادي (محايد)', swatch: '#64748b', rank: 6 },
};

/**
 * Normalizes digits (Arabic-Indic ٠-٩ and Persian ۰-۹) to ASCII 0-9.
 */
export function normalizeDigits(text: string | number | null | undefined): string {
  if (text === null || text === undefined) return '';
  return String(text)
    .replace(/[٠۰]/g, '0')
    .replace(/[١۱]/g, '1')
    .replace(/[٢۲]/g, '2')
    .replace(/[٣۳]/g, '3')
    .replace(/[٤۴]/g, '4')
    .replace(/[٥۵]/g, '5')
    .replace(/[٦۶]/g, '6')
    .replace(/[٧۷]/g, '7')
    .replace(/[٨۸]/g, '8')
    .replace(/[٩۹]/g, '9');
}

/**
 * Normalizes Arabic text and digits for consistent option & cell filtering.
 */
export function normalizeFilterText(text: string | null | undefined): string {
  if (text === null || text === undefined) return '';
  return normalizeDigits(text)
    .replace(/[\u064B-\u065F\u0670\u0640]/g, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .replace(/ؤ/g, 'و')
    .replace(/ئ/g, 'ي')
    .toLowerCase()
    .trim();
}

const SUCCESS_STATUS_SET = new Set(
  [
    'active',
    'approved',
    'posted',
    'paid',
    'completed',
    'ready',
    'salary_approved',
    'settled',
    'matched',
    'valid',
    'open',
    'enabled',
    'true',
    'yes',
    'نشط',
    'نشطة',
    'معتمد',
    'معتمدة',
    'مرحل',
    'مرحّل',
    'مسدد',
    'مكتمل',
    'جاهز',
    'ساري',
    'سارية',
    'مطابق',
    'مقبول',
    'متاح',
    'مدفوع',
    'نعم',
    'مفعل',
  ].map((s) => normalizeFilterText(s))
);

const WARNING_STATUS_SET = new Set(
  [
    'pending',
    'pending_approval',
    'draft',
    'review',
    'partial',
    'hold',
    'paused',
    'warning',
    'expiring',
    'under_review',
    'proposed',
    'processing',
    'معلق',
    'معلقة',
    'بانتظار الاعتماد',
    'مسودة',
    'قيد المراجعة',
    'جزئي',
    'موقوف مؤقتا',
    'موقوف',
    'موقوفة',
    'تنبيه',
    'قرب الانتهاء',
    'قيد الانتظار',
    'مقترح',
    'تحت المراجعة',
  ].map((s) => normalizeFilterText(s))
);

const DANGER_STATUS_SET = new Set(
  [
    'inactive',
    'rejected',
    'cancelled',
    'canceled',
    'reversed',
    'overdue',
    'expired',
    'failed',
    'error',
    'terminated',
    'fleeing',
    'absent',
    'blocked',
    'unmatched',
    'disputed',
    'missing',
    'disabled',
    'false',
    'no',
    'غير نشط',
    'غير نشطة',
    'مرفوض',
    'ملغي',
    'معكوس',
    'متأخر',
    'متعثر',
    'منتهي',
    'منتهية',
    'فصل',
    'هروب',
    'تغيب',
    'محظور',
    'خطأ',
    'مفقود',
    'غير مطابق',
    'معترض',
    'لا',
    'معطل',
    'آيبان مفقود للمكفول',
  ].map((s) => normalizeFilterText(s))
);

const INFO_STATUS_SET = new Set(
  [
    'info',
    'new',
    'transferred',
    'assigned',
    'external',
    'outsiderider',
    'جديد',
    'محول',
    'مسند',
    'خارجي',
    'أجير خارجي',
  ].map((s) => normalizeFilterText(s))
);

const ACCENT_STATUS_SET = new Set(
  [
    'sponsored',
    'sponsoredinternal',
    'special',
    'مكفول',
    'على الكفالة',
    'كفالة',
  ].map((s) => normalizeFilterText(s))
);

const NEUTRAL_STATUS_SET = new Set(
  ['archived', 'closed', 'none', 'مؤرشف', 'مغلق', 'عادي'].map((s) =>
    normalizeFilterText(s)
  )
);

/**
 * Infers a semantic badge/status color tone from a raw or displayed value.
 */
export function defaultStatusTone(value: unknown): BadgeTone | undefined {
  if (value === null || value === undefined) return undefined;
  const norm = normalizeFilterText(String(value));
  if (!norm) return undefined;
  if (DANGER_STATUS_SET.has(norm)) return 'danger';
  if (SUCCESS_STATUS_SET.has(norm)) return 'success';
  if (WARNING_STATUS_SET.has(norm)) return 'warning';
  if (INFO_STATUS_SET.has(norm)) return 'info';
  if (ACCENT_STATUS_SET.has(norm)) return 'accent';
  if (NEUTRAL_STATUS_SET.has(norm)) return 'neutral';
  if (norm.includes('غير نشط') || norm.includes('مرفوض') || norm.includes('مفقود') || norm.includes('منتهي') || norm.includes('معكوس')) {
    return 'danger';
  }
  if (norm.includes('بانتظار') || norm.includes('معلق') || norm.includes('مسوده') || norm.includes('قيد')) {
    return 'warning';
  }
  if (norm.includes('نشط') || norm.includes('معتمد') || norm.includes('مرحل') || norm.includes('مكتمل') || norm.includes('مسدد')) {
    return 'success';
  }
  return undefined;
}

const LIST_KEY_EXACT = new Set([
  'code',
  'status',
  'state',
  'city',
  'company',
  'companyid',
  'sponsor',
  'sponsorid',
  'sponsornamear',
  'platform',
  'platforms',
  'app',
  'apps',
  'vehicle',
  'vehicletype',
  'companyvehicletype',
  'provider',
  'providernamear',
  'cardname',
  'card',
  'type',
  'category',
  'categoryar',
  'kind',
  'role',
  'department',
  'nationality',
  'engagementtype',
  'worktype',
  'branch',
  'bank',
  'bankcode',
  'period',
  'month',
  'stage',
  'mode',
  'source',
  'fueltype',
  'direction',
  'priority',
  'audience',
  'housing',
]);

const LIST_KEY_SUFFIX =
  /(Code|Status|State|City|Company|CompanyId|Sponsor|SponsorId|Platform|App|Vehicle|VehicleType|Provider|Type|Category|Kind|Role|Department|Nationality|Branch|Bank|Period|Stage|Mode|Source)$/;

const TEXT_KEY_HINT =
  /(name|title|desc|note|reason|message|iqama|phone|mobile|iban|email|address|plate|vin|chassis|serial|number|num|id|date|from|to|at|amount|total|cost|salary|balance|liters|days|orders|hours|count|percent|vat|tax|odo|mileage)/i;

const DATE_KEY_HINT =
  /(^date$|Date$|At$|^from$|^to$|^dueDate$|^invoiceDate$|^receivedAt$|^createdAt$|^updatedAt$|^approvedAt$|^effectiveFrom$|^effectiveTo$|^startDate$|^endDate$|^expiryDate$|^issuedAt$)/;

const NUMBER_KEY_HINT =
  /(amount|total|cost|salary|balance|liters|days|orders|hours|count|percent|vat|tax|odo|mileage|debit|credit|installment|capacity|rooms|residents|vehicles|riders|accounts|net|deduction|price|rate|capital|remaining)/i;

const ID_LIKE_KEY_HINT =
  /(iqama|phone|mobile|iban|unified|commercialreg|receiptnumber|invoicenumber|cardnumber|serial|code|id|plate|vin)/i;

/**
 * Resolves whether a DataTable quick column filter renders as a type-in searchable dropdown ('list')
 * or a free-text input ('text'), or returns explicit 'number'/'date' when provided.
 */
export function inferColumnFilterType(
  columnKey: string,
  explicit?: ColumnFilterType
): ColumnFilterType {
  if (explicit === 'list' || explicit === 'text' || explicit === 'number' || explicit === 'date') {
    return explicit;
  }
  const trimmed = (columnKey || '').trim();
  if (!trimmed) return 'text';
  const low = trimmed.toLowerCase().replace(/[-_\s]/g, '');
  if (LIST_KEY_EXACT.has(low)) return 'list';
  if (LIST_KEY_SUFFIX.test(trimmed)) return 'list';
  if (TEXT_KEY_HINT.test(trimmed)) return 'text';
  return 'text';
}

/**
 * Resolves the full data type ('text' | 'number' | 'date' | 'list') of a column definition
 * for Excel-style AutoFilter sorting and operator selection.
 */
export function resolveColumnType(col: {
  key: string;
  type?: ColumnDataType;
  filterType?: ColumnDataType;
  numeric?: boolean;
}): ColumnDataType {
  const explicit = col.type ?? col.filterType;
  if (explicit === 'number' || explicit === 'date' || explicit === 'list' || explicit === 'text') {
    if (explicit === 'text' && col.numeric && !ID_LIKE_KEY_HINT.test(col.key)) {
      return 'number';
    }
    return explicit;
  }
  if (col.numeric && !ID_LIKE_KEY_HINT.test(col.key)) return 'number';
  if (DATE_KEY_HINT.test(col.key)) return 'date';
  if (inferColumnFilterType(col.key) === 'list') return 'list';
  if (NUMBER_KEY_HINT.test(col.key) && !ID_LIKE_KEY_HINT.test(col.key)) return 'number';
  return 'text';
}

export const AR_COLLATOR = new Intl.Collator('ar', { numeric: true, sensitivity: 'base' });

export function isBlankCellValue(raw: unknown): boolean {
  if (raw === null || raw === undefined) return true;
  const s = String(raw).trim();
  return s === '' || s === '—' || s === '-';
}

/**
 * Parses a numeric value from raw numbers or formatted strings (including Arabic digits and commas).
 * Blank/missing values return null (never 0).
 */
export function parseNumberValue(raw: unknown): number | null {
  if (isBlankCellValue(raw)) return null;
  if (typeof raw === 'number') return Number.isFinite(raw) ? raw : null;
  const cleaned = normalizeDigits(String(raw))
    .replace(/[,،\s]/g, '')
    .replace(/(ر\.س|ريال|SAR|%)/gi, '')
    .trim();
  if (!cleaned) return null;
  const num = Number(cleaned);
  return Number.isFinite(num) ? num : null;
}

export interface ParsedDateValue {
  year: number;
  month: number;
  day: number;
  dayEpoch: number;
}

/**
 * Parses a date or month value into a timezone-safe UTC day epoch.
 */
export function parseDateValue(raw: unknown): ParsedDateValue | null {
  if (isBlankCellValue(raw)) return null;
  if (raw instanceof Date) {
    if (isNaN(raw.getTime())) return null;
    const year = raw.getUTCFullYear();
    const month = raw.getUTCMonth() + 1;
    const day = raw.getUTCDate();
    return { year, month, day, dayEpoch: Date.UTC(year, month - 1, day) };
  }
  const s = normalizeDigits(String(raw)).trim();
  if (!s) return null;

  // Match YYYY-MM-DD or YYYY/MM/DD (with optional time)
  const isoMatch = s.match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})/);
  if (isoMatch) {
    const year = Number(isoMatch[1]);
    const month = Number(isoMatch[2]);
    const day = Number(isoMatch[3]);
    if (month >= 1 && month <= 12 && day >= 1 && day <= 31) {
      return { year, month, day, dayEpoch: Date.UTC(year, month - 1, day) };
    }
  }

  // Match YYYY-MM (period / month string)
  const ymMatch = s.match(/^(\d{4})[-/](\d{1,2})$/);
  if (ymMatch) {
    const year = Number(ymMatch[1]);
    const month = Number(ymMatch[2]);
    if (month >= 1 && month <= 12) {
      return { year, month, day: 1, dayEpoch: Date.UTC(year, month - 1, 1) };
    }
  }

  // Match DD/MM/YYYY or DD-MM-YYYY
  const dmyMatch = s.match(/^(\d{1,2})[-/](\d{1,2})[-/](\d{4})/);
  if (dmyMatch) {
    const day = Number(dmyMatch[1]);
    const month = Number(dmyMatch[2]);
    const year = Number(dmyMatch[3]);
    if (month >= 1 && month <= 12 && day >= 1 && day <= 31) {
      return { year, month, day, dayEpoch: Date.UTC(year, month - 1, day) };
    }
  }

  const parsed = new Date(s);
  if (!isNaN(parsed.getTime())) {
    const year = parsed.getUTCFullYear();
    const month = parsed.getUTCMonth() + 1;
    const day = parsed.getUTCDate();
    return { year, month, day, dayEpoch: Date.UTC(year, month - 1, day) };
  }
  return null;
}

/**
 * Computes the minimum value threshold for the Top N numeric values in a column.
 */
export function computeTopNThreshold(
  values: ReadonlyArray<unknown>,
  n: number
): number | null {
  if (!Number.isFinite(n) || n <= 0) return null;
  const nums: number[] = [];
  for (const v of values) {
    const num = parseNumberValue(v);
    if (num !== null) nums.push(num);
  }
  if (nums.length === 0) return null;
  nums.sort((a, b) => b - a);
  const idx = Math.min(Math.floor(n), nums.length) - 1;
  return nums[idx];
}

/**
 * Checks whether a ColumnFilterValue has any active constraint.
 */
export function isColumnFilterActive(
  filter: ColumnFilterValue | undefined | null
): boolean {
  if (!filter) return false;
  if (filter.values !== undefined) return true;
  if (filter.colorTone && String(filter.colorTone).trim() !== '') return true;
  if (filter.op === 'thisMonth' || filter.op === 'lastMonth') return true;
  if (filter.op && filter.value !== undefined && String(filter.value).trim() !== '') {
    return true;
  }
  if (
    filter.op === 'between' &&
    filter.valueTo !== undefined &&
    String(filter.valueTo).trim() !== ''
  ) {
    return true;
  }
  return false;
}

export interface EvaluateFilterOptions {
  type?: ColumnDataType;
  labelOf?: (val: any) => string;
  toneOf?: (val: any) => BadgeTone | null | undefined;
  topNThreshold?: number | null;
  referenceDate?: Date | string;
}

/**
 * Evaluates a complete Excel-style ColumnFilterValue (checkbox value set, color tone, and text/number/date operators)
 * against a cell's raw value, respecting labelOf(value) for displayed Arabic text and toneOf(value) for color.
 */
export function evaluateColumnFilter(
  rawValue: unknown,
  filter: ColumnFilterValue | undefined | null,
  options?: EvaluateFilterOptions
): boolean {
  if (!isColumnFilterActive(filter) || !filter) return true;

  const isBlank = isBlankCellValue(rawValue);
  const rawStr = isBlank ? '' : String(rawValue).trim();
  const displayStr = options?.labelOf
    ? String(options.labelOf(rawValue) ?? '')
    : rawStr;
  const normRaw = normalizeFilterText(rawStr);
  const normDisplay = normalizeFilterText(displayStr);

  // 1. Color filter
  if (filter.colorTone && String(filter.colorTone).trim() !== '') {
    const tone = options?.toneOf
      ? options.toneOf(rawValue)
      : defaultStatusTone(rawValue) ?? defaultStatusTone(displayStr);
    if (!tone || tone !== filter.colorTone) return false;
  }

  // 2. Checkbox value list
  if (filter.values !== undefined) {
    if (filter.values.length === 0) return false;
    let matched = false;
    for (const allowed of filter.values) {
      if (allowed === '' || allowed === '(بدون قيمة)' || allowed === '(فارغ)') {
        if (isBlank) {
          matched = true;
          break;
        }
        continue;
      }
      const normAllowed = normalizeFilterText(allowed);
      const allowedDisplay = options?.labelOf
        ? normalizeFilterText(options.labelOf(allowed))
        : normAllowed;
      if (
        normAllowed === normRaw ||
        normAllowed === normDisplay ||
        (allowedDisplay && (allowedDisplay === normDisplay || allowedDisplay === normRaw))
      ) {
        matched = true;
        break;
      }
    }
    if (!matched) return false;
  }

  // 3. Condition operator
  const op = filter.op;
  if (!op || op === 'in' || op === 'color') return true;

  const colType = options?.type ?? 'text';

  if (op === 'topN') {
    const nVal = parseNumberValue(filter.value);
    if (nVal === null || nVal <= 0) return true;
    const num = parseNumberValue(rawValue);
    if (num === null) return false;
    if (options?.topNThreshold === null || options?.topNThreshold === undefined) {
      return false;
    }
    return num >= options.topNThreshold;
  }

  if (op === 'thisMonth' || op === 'lastMonth') {
    const cellDate = parseDateValue(rawValue);
    if (!cellDate) return false;
    const refParsed = parseDateValue(options?.referenceDate ?? new Date());
    const now = new Date();
    const refYear = refParsed ? refParsed.year : now.getUTCFullYear();
    const refMonth = refParsed ? refParsed.month : now.getUTCMonth() + 1;
    if (op === 'thisMonth') {
      return cellDate.year === refYear && cellDate.month === refMonth;
    }
    const lastMonth = refMonth === 1 ? 12 : refMonth - 1;
    const lastYear = refMonth === 1 ? refYear - 1 : refYear;
    return cellDate.year === lastYear && cellDate.month === lastMonth;
  }

  const hasVal1 = filter.value !== undefined && String(filter.value).trim() !== '';
  const hasVal2 = filter.valueTo !== undefined && String(filter.valueTo).trim() !== '';
  if (!hasVal1 && !(op === 'between' && hasVal2)) return true;

  // Date operators or Date column
  if (colType === 'date' || op === 'before' || op === 'after') {
    const cellDate = parseDateValue(rawValue);
    if (op === 'between') {
      const d1 = parseDateValue(filter.value);
      const d2 = parseDateValue(filter.valueTo);
      if (!d1 && !d2) return true;
      if (!cellDate) return false;
      if (d1 && d2) {
        const lo = Math.min(d1.dayEpoch, d2.dayEpoch);
        const hi = Math.max(d1.dayEpoch, d2.dayEpoch);
        return cellDate.dayEpoch >= lo && cellDate.dayEpoch <= hi;
      }
      if (d1) return cellDate.dayEpoch >= d1.dayEpoch;
      return cellDate.dayEpoch <= d2!.dayEpoch;
    }
    const targetDate = parseDateValue(filter.value);
    if (!targetDate) return true;
    if (op === 'neq') return !cellDate || cellDate.dayEpoch !== targetDate.dayEpoch;
    if (!cellDate) return false;
    switch (op) {
      case 'eq':
        return cellDate.dayEpoch === targetDate.dayEpoch;
      case 'before':
      case 'lt':
        return cellDate.dayEpoch < targetDate.dayEpoch;
      case 'lte':
        return cellDate.dayEpoch <= targetDate.dayEpoch;
      case 'after':
      case 'gt':
        return cellDate.dayEpoch > targetDate.dayEpoch;
      case 'gte':
        return cellDate.dayEpoch >= targetDate.dayEpoch;
      default:
        break;
    }
  }

  // Number operators or Number column
  if (
    colType === 'number' ||
    op === 'gt' ||
    op === 'gte' ||
    op === 'lt' ||
    op === 'lte' ||
    (op === 'between' && colType !== 'date')
  ) {
    const cellNum = parseNumberValue(rawValue);
    if (op === 'between') {
      const n1 = parseNumberValue(filter.value);
      const n2 = parseNumberValue(filter.valueTo);
      if (n1 === null && n2 === null) return true;
      if (cellNum === null) return false;
      if (n1 !== null && n2 !== null) {
        const lo = Math.min(n1, n2);
        const hi = Math.max(n1, n2);
        return cellNum >= lo && cellNum <= hi;
      }
      if (n1 !== null) return cellNum >= n1;
      return cellNum <= n2!;
    }
    const targetNum = parseNumberValue(filter.value);
    if (targetNum === null) return true;
    if (op === 'neq') return cellNum === null || Math.abs(cellNum - targetNum) > 1e-9;
    if (cellNum === null) return false;
    switch (op) {
      case 'eq':
        return Math.abs(cellNum - targetNum) <= 1e-9;
      case 'gt':
        return cellNum > targetNum;
      case 'gte':
        return cellNum >= targetNum;
      case 'lt':
        return cellNum < targetNum;
      case 'lte':
        return cellNum <= targetNum;
      default:
        break;
    }
  }

  // Text operators (compare against displayed Arabic value as well as raw value)
  const queryNorm = normalizeFilterText(filter.value);
  if (!queryNorm) return true;
  switch (op) {
    case 'contains':
      return normDisplay.includes(queryNorm) || normRaw.includes(queryNorm);
    case 'notContains':
      return !normDisplay.includes(queryNorm) && !normRaw.includes(queryNorm);
    case 'beginsWith':
      return normDisplay.startsWith(queryNorm) || normRaw.startsWith(queryNorm);
    case 'endsWith':
      return normDisplay.endsWith(queryNorm) || normRaw.endsWith(queryNorm);
    case 'eq':
      return normDisplay === queryNorm || normRaw === queryNorm;
    case 'neq':
      return normDisplay !== queryNorm && normRaw !== queryNorm;
    default:
      return true;
  }
}

export interface CompareColumnOptions {
  type?: ColumnDataType;
  dir: 'asc' | 'desc';
  sortTone?: BadgeTone | null;
  labelOf?: (val: any) => string;
  toneOf?: (val: any) => BadgeTone | null | undefined;
}

/**
 * Compares two column cell values for Excel-style sorting:
 * - Optional color-tone priority (putting chosen tone at top)
 * - Blanks always placed at bottom regardless of asc/desc
 * - Numeric comparison for 'number', UTC dayEpoch for 'date', and Arabic Intl.Collator for 'text'/'list' (using labelOf).
 */
export function compareColumnValues(
  aRaw: unknown,
  bRaw: unknown,
  options: CompareColumnOptions
): number {
  const resolveTone = (val: unknown): BadgeTone | undefined => {
    if (options.toneOf) return options.toneOf(val) ?? undefined;
    const disp = options.labelOf ? options.labelOf(val) : val;
    return defaultStatusTone(val) ?? defaultStatusTone(disp);
  };

  if (options.sortTone) {
    const toneA = resolveTone(aRaw);
    const toneB = resolveTone(bRaw);
    const aMatch = toneA === options.sortTone;
    const bMatch = toneB === options.sortTone;
    if (aMatch !== bMatch) return aMatch ? -1 : 1;
    if (toneA !== toneB) {
      const rankA = toneA ? (TONE_META[toneA]?.rank ?? 99) : 99;
      const rankB = toneB ? (TONE_META[toneB]?.rank ?? 99) : 99;
      if (rankA !== rankB) return rankA - rankB;
    }
  }

  const aBlank = isBlankCellValue(aRaw);
  const bBlank = isBlankCellValue(bRaw);
  if (aBlank && bBlank) return 0;
  if (aBlank) return 1;
  if (bBlank) return -1;

  const factor = options.dir === 'asc' ? 1 : -1;
  const colType = options.type ?? 'text';

  if (colType === 'number') {
    const numA = parseNumberValue(aRaw);
    const numB = parseNumberValue(bRaw);
    if (numA === null && numB === null) return 0;
    if (numA === null) return 1;
    if (numB === null) return -1;
    return factor * (numA - numB);
  }

  if (colType === 'date') {
    const dateA = parseDateValue(aRaw);
    const dateB = parseDateValue(bRaw);
    if (!dateA && !dateB) return 0;
    if (!dateA) return 1;
    if (!dateB) return -1;
    return factor * (dateA.dayEpoch - dateB.dayEpoch);
  }

  const labelA = options.labelOf ? String(options.labelOf(aRaw) ?? '') : String(aRaw ?? '');
  const labelB = options.labelOf ? String(options.labelOf(bRaw) ?? '') : String(bRaw ?? '');
  return factor * AR_COLLATOR.compare(labelA, labelB);
}

export interface ExtractDistinctConfig {
  labelOf?: (val: any) => string;
  toneOf?: (val: any) => BadgeTone | null | undefined;
  presetOptions?: ReadonlyArray<ColumnFilterOption>;
  includeEmpty?: boolean;
  emptyCount?: number;
  counts?: Record<string, number>;
  type?: ColumnDataType;
}

/**
 * Extracts sorted unique options from raw column values, enriched with display labels (labelOf),
 * color tones (toneOf), and occurrence counts.
 */
export function extractDistinctOptions(
  values: ReadonlyArray<string | number | null | undefined>,
  config?: ExtractDistinctConfig
): ColumnFilterOption[] {
  const presetByNorm = new Map<string, ColumnFilterOption>();
  if (config?.presetOptions) {
    for (const po of config.presetOptions) {
      presetByNorm.set(normalizeFilterText(po.value), po);
    }
  }

  const buckets = new Map<
    string,
    { rawValue: string; label: string; count: number; tone?: BadgeTone }
  >();
  let emptyCount = config?.emptyCount ?? 0;

  // Seed preset options first so closed sets are always present
  if (config?.presetOptions) {
    for (const po of config.presetOptions) {
      const norm = normalizeFilterText(po.value);
      if (!norm) continue;
      const label = config.labelOf ? config.labelOf(po.value) : po.label || po.value;
      const tone =
        po.tone ??
        (config.toneOf ? (config.toneOf(po.value) ?? undefined) : undefined);
      buckets.set(norm, {
        rawValue: po.value,
        label,
        count: config.counts?.[po.value] ?? po.count ?? 0,
        tone,
      });
    }
  }

  for (const raw of values) {
    if (isBlankCellValue(raw)) {
      emptyCount += 1;
      continue;
    }
    const text = String(raw).trim();
    const norm = normalizeFilterText(text);
    if (!norm) {
      emptyCount += 1;
      continue;
    }
    const existing = buckets.get(norm);
    if (existing) {
      if (!config?.counts) {
        existing.count += 1;
      }
    } else {
      const preset = presetByNorm.get(norm);
      const rawValue = preset ? preset.value : text;
      const label = config?.labelOf
        ? String(config.labelOf(rawValue) ?? rawValue)
        : preset?.label ?? text;
      const tone =
        preset?.tone ??
        (config?.toneOf
          ? (config.toneOf(rawValue) ?? undefined)
          : undefined);
      const count = config?.counts?.[rawValue] ?? config?.counts?.[text] ?? 1;
      buckets.set(norm, { rawValue, label, count, tone });
    }
  }

  const nonEmpty = Array.from(buckets.values()).sort((a, b) => {
    if (config?.type === 'number') {
      const na = parseNumberValue(a.rawValue);
      const nb = parseNumberValue(b.rawValue);
      if (na !== null && nb !== null) return na - nb;
    }
    return AR_COLLATOR.compare(a.label, b.label);
  });

  const result: ColumnFilterOption[] = nonEmpty.map((item) => {
    const opt: ColumnFilterOption = {
      value: item.rawValue,
      label: item.label,
      count: item.count,
    };
    if (item.tone) opt.tone = item.tone;
    return opt;
  });

  if (config?.includeEmpty && emptyCount > 0) {
    result.push({
      value: '',
      label: '(بدون قيمة)',
      count: emptyCount,
    });
  }

  return result;
}

/**
 * Collects distinct color tones present in a column's options.
 */
export function extractDistinctTones(
  options: ReadonlyArray<ColumnFilterOption>,
  toneOf?: (val: any) => BadgeTone | null | undefined,
  inferFallback = false
): { tone: BadgeTone; label: string; swatch: string; count: number }[] {
  const counts = new Map<BadgeTone, number>();
  for (const opt of options) {
    if (opt.value === '') continue;
    const tone =
      opt.tone ??
      (toneOf
        ? (toneOf(opt.value) ?? undefined)
        : inferFallback
          ? (defaultStatusTone(opt.value) ?? defaultStatusTone(opt.label))
          : undefined);
    if (!tone) continue;
    counts.set(tone, (counts.get(tone) ?? 0) + (opt.count ?? 1));
  }
  return Array.from(counts.entries())
    .map(([tone, count]) => {
      const meta = TONE_META[tone] ?? {
        label: String(tone),
        swatch: '#64748b',
        rank: 50,
      };
      return { tone, label: meta.label, swatch: meta.swatch, count, rank: meta.rank };
    })
    .sort((a, b) => a.rank - b.rank)
    .map(({ tone, label, swatch, count }) => ({ tone, label, swatch, count }));
}

/**
 * Filters dropdown options by typed search query (multi-word Arabic/Latin normalized matching
 * against the displayed Arabic label and raw value).
 */
export function filterSelectOptions(
  options: ReadonlyArray<ColumnFilterOption>,
  query: string
): ColumnFilterOption[] {
  const words = normalizeFilterText(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return [...options];
  return options.filter((o) => {
    const label = normalizeFilterText(o.label);
    const val = normalizeFilterText(o.value);
    return words.every((w) => label.includes(w) || val.includes(w));
  });
}

/**
 * Handles "Select All" («تحديد الكل») and "Select All Search Results" («تحديد كل نتائج البحث»)
 * toggling for the Excel-style checkbox list.
 */
export function toggleSelectAllOptions(
  allOptions: ReadonlyArray<ColumnFilterOption>,
  searchQuery: string,
  currentValues: ReadonlyArray<string> | undefined,
  checked: boolean
): string[] | undefined {
  const trimmed = searchQuery.trim();
  if (!trimmed) {
    return checked ? undefined : [];
  }
  const matched = filterSelectOptions(allOptions, trimmed);
  const matchedValues = new Set(matched.map((o) => o.value));
  const nextSet = new Set(currentValues ?? allOptions.map((o) => o.value));
  for (const v of matchedValues) {
    if (checked) nextSet.add(v);
    else nextSet.delete(v);
  }
  if (nextSet.size >= allOptions.length && allOptions.every((o) => nextSet.has(o.value))) {
    return undefined;
  }
  return Array.from(nextSet);
}

/**
 * Excel behavior: typing in the value search box and pressing Enter (or picking search results)
 * immediately selects only the matching search results' underlying raw values.
 */
export function applySearchResultsSelection(
  allOptions: ReadonlyArray<ColumnFilterOption>,
  searchQuery: string,
  currentFilter?: ColumnFilterValue
): ColumnFilterValue {
  const trimmed = searchQuery.trim();
  if (!trimmed) {
    const next: ColumnFilterValue = { ...currentFilter };
    delete next.values;
    return next;
  }
  const matched = filterSelectOptions(allOptions, trimmed);
  return {
    ...currentFilter,
    values: matched.map((o) => o.value),
  };
}

/**
 * Serializes active column filters into the generic server query contract:
 * `filters=[{col,op,value,valueTo,values}]`
 */
export function serializeServerFilters(
  filters: Record<string, ColumnFilterValue | undefined>,
  columns?: ReadonlyArray<{
    key: string;
    filterColumn?: string;
    type?: ColumnDataType;
    filterType?: ColumnDataType;
    numeric?: boolean;
    labelOf?: (v: any) => string;
    toneOf?: (v: any) => BadgeTone | null | undefined;
    options?: ReadonlyArray<ColumnFilterOption>;
  }>
): ServerFilterClause[] {
  const clauses: ServerFilterClause[] = [];
  const colMap = new Map((columns ?? []).map((c) => [c.key, c]));

  for (const [colKey, spec] of Object.entries(filters)) {
    if (!spec || !isColumnFilterActive(spec)) continue;
    const colDef = colMap.get(colKey);
    const serverCol = colDef?.filterColumn || colKey;

    if (spec.values !== undefined) {
      clauses.push({
        col: serverCol,
        op: 'in',
        values: [...spec.values],
      });
    }

    if (spec.colorTone && String(spec.colorTone).trim() !== '') {
      const toneValues = (colDef?.options ?? [])
        .filter((o) => {
          const t =
            o.tone ??
            (colDef?.toneOf ? colDef.toneOf(o.value) : defaultStatusTone(o.value) ?? defaultStatusTone(o.label));
          return t === spec.colorTone;
        })
        .map((o) => o.value);
      clauses.push({
        col: serverCol,
        op: 'color',
        value: String(spec.colorTone),
        ...(toneValues.length > 0 ? { values: toneValues } : {}),
      });
    }

    if (spec.op && spec.op !== 'in' && spec.op !== 'color') {
      const isDatePreset = spec.op === 'thisMonth' || spec.op === 'lastMonth';
      const hasVal1 = spec.value !== undefined && String(spec.value).trim() !== '';
      const hasVal2 = spec.valueTo !== undefined && String(spec.valueTo).trim() !== '';
      if (isDatePreset || hasVal1 || (spec.op === 'between' && hasVal2)) {
        const clause: ServerFilterClause = {
          col: serverCol,
          op: spec.op,
        };
        if (hasVal1) clause.value = String(spec.value).trim();
        if (hasVal2) clause.valueTo = String(spec.valueTo).trim();
        clauses.push(clause);
      }
    }
  }

  return clauses;
}

export function serializeServerFiltersParam(
  filters: Record<string, ColumnFilterValue | undefined>,
  columns?: Parameters<typeof serializeServerFilters>[1]
): string | undefined {
  const clauses = serializeServerFilters(filters, columns);
  return clauses.length > 0 ? JSON.stringify(clauses) : undefined;
}

/**
 * Serializes active column sort into the generic server query contract:
 * `sort=[{col,dir,tone?}]`
 */
export function serializeServerSort(
  sortKey: string | null,
  sortDir: 'asc' | 'desc',
  sortTone?: BadgeTone | null,
  columns?: ReadonlyArray<{ key: string; filterColumn?: string }>
): ServerSortClause[] {
  if (!sortKey) return [];
  const colDef = (columns ?? []).find((c) => c.key === sortKey);
  const serverCol = colDef?.filterColumn || sortKey;
  const clause: ServerSortClause = { col: serverCol, dir: sortDir };
  if (sortTone) clause.tone = String(sortTone);
  return [clause];
}

export function serializeServerSortParam(
  sortKey: string | null,
  sortDir: 'asc' | 'desc',
  sortTone?: BadgeTone | null,
  columns?: ReadonlyArray<{ key: string; filterColumn?: string }>
): string | undefined {
  const clauses = serializeServerSort(sortKey, sortDir, sortTone, columns);
  return clauses.length > 0 ? JSON.stringify(clauses) : undefined;
}

/**
 * Matches a cell string against a simple column filter term (quick filter row).
 * List filters match exact normalized option equality (so 'نشطة' never matches 'غير نشطة').
 * Text filters match normalized substring (OR across comma/pipe separated terms).
 */
export function matchesColumnFilter(
  cellText: string,
  filterValue: string,
  filterType: ColumnFilterType
): boolean {
  if (!filterValue || !filterValue.trim()) return true;
  const cellNorm = normalizeFilterText(cellText);
  if (filterType === 'list') {
    const targetNorm = normalizeFilterText(filterValue);
    return targetNorm ? cellNorm === targetNorm : true;
  }
  const terms = filterValue
    .split(/[,|]/)
    .map((t) => normalizeFilterText(t))
    .filter(Boolean);
  if (terms.length === 0) return true;
  return terms.some((t) => cellNorm.includes(t));
}

export interface SearchableFilterState {
  value: string;
  query: string;
  open: boolean;
  activeIndex: number;
}

export function createSearchableFilterState(initialValue = ''): SearchableFilterState {
  return { value: initialValue, query: '', open: false, activeIndex: 0 };
}

export function searchableFilterReduce(
  state: SearchableFilterState,
  action:
    | { type: 'open' }
    | { type: 'close' }
    | { type: 'type'; query: string }
    | { type: 'select'; value: string }
    | { type: 'clear' }
    | { type: 'move'; delta: 1 | -1; count: number }
    | { type: 'commit'; shown: ReadonlyArray<ColumnFilterOption> }
): SearchableFilterState {
  switch (action.type) {
    case 'open':
      return { ...state, open: true, activeIndex: 0 };
    case 'close':
      return { ...state, open: false, query: '', activeIndex: 0 };
    case 'type':
      return { ...state, query: action.query, open: true, activeIndex: 0 };
    case 'select':
      return { value: action.value, query: '', open: false, activeIndex: 0 };
    case 'clear':
      return { value: '', query: '', open: false, activeIndex: 0 };
    case 'move': {
      const next =
        action.count <= 0
          ? 0
          : (state.activeIndex + action.delta + action.count) % action.count;
      return { ...state, open: true, activeIndex: next };
    }
    case 'commit': {
      if (!state.open || action.shown.length === 0) return state;
      const chosen = action.shown[state.activeIndex] ?? action.shown[0];
      if (!chosen) return state;
      return { value: chosen.value, query: '', open: false, activeIndex: 0 };
    }
  }
}

export interface OperatorDraftState {
  draftOp: FilterOperator | '';
  draftValue: string;
  draftValueTo: string;
}

export function operatorNeedsValueInput(op: FilterOperator | ''): boolean {
  return op !== '' && op !== 'thisMonth' && op !== 'lastMonth';
}

export function applyOperatorDraftChange(
  prev: OperatorDraftState,
  currentFilter: ColumnFilterValue | undefined,
  opStr: string
): { draft: OperatorDraftState; nextFilter: ColumnFilterValue | undefined } {
  const nextFilter: ColumnFilterValue = { ...currentFilter };
  if (!opStr) {
    delete nextFilter.op;
    delete nextFilter.value;
    delete nextFilter.valueTo;
    return {
      draft: { draftOp: '', draftValue: '', draftValueTo: '' },
      nextFilter: isColumnFilterActive(nextFilter) ? nextFilter : undefined,
    };
  }
  const nextOp = opStr as FilterOperator;
  let draftValue = prev.draftValue;
  let draftValueTo = prev.draftValueTo;
  nextFilter.op = nextOp;
  if (nextOp === 'thisMonth' || nextOp === 'lastMonth') {
    draftValue = '';
    draftValueTo = '';
    delete nextFilter.value;
    delete nextFilter.valueTo;
  } else {
    if (draftValue !== '') nextFilter.value = draftValue;
    else delete nextFilter.value;
    if (nextOp === 'between') {
      if (draftValueTo !== '') nextFilter.valueTo = draftValueTo;
      else delete nextFilter.valueTo;
    } else {
      draftValueTo = '';
      delete nextFilter.valueTo;
    }
  }
  const hasConditionValue =
    nextOp === 'thisMonth' ||
    nextOp === 'lastMonth' ||
    (nextFilter.value !== undefined && String(nextFilter.value).trim() !== '') ||
    (nextOp === 'between' &&
      nextFilter.valueTo !== undefined &&
      String(nextFilter.valueTo).trim() !== '');
  if (!hasConditionValue) {
    delete nextFilter.op;
    delete nextFilter.value;
    delete nextFilter.valueTo;
  }
  return {
    draft: { draftOp: nextOp, draftValue, draftValueTo },
    nextFilter: isColumnFilterActive(nextFilter) ? nextFilter : undefined,
  };
}

export function applyConditionValueDraftChange(
  prev: OperatorDraftState,
  currentFilter: ColumnFilterValue | undefined,
  columnType: ColumnDataType,
  val: string,
  isSecond = false
): { draft: OperatorDraftState; nextFilter: ColumnFilterValue | undefined } {
  const defaultOp: FilterOperator =
    columnType === 'number' || columnType === 'date' ? 'eq' : 'contains';
  const effectiveOp: FilterOperator =
    prev.draftOp && prev.draftOp !== 'in' && prev.draftOp !== 'color'
      ? prev.draftOp
      : currentFilter?.op && currentFilter.op !== 'in' && currentFilter.op !== 'color'
        ? currentFilter.op
        : defaultOp;
  const nextVal = isSecond ? prev.draftValue : val;
  const nextValTo = isSecond ? val : prev.draftValueTo;
  const nextFilter: ColumnFilterValue = {
    ...currentFilter,
    op: effectiveOp,
    value: nextVal,
    ...(effectiveOp === 'between' ? { valueTo: nextValTo } : {}),
  };
  const hasConditionValue =
    nextVal.trim() !== '' || (effectiveOp === 'between' && nextValTo.trim() !== '');
  if (!hasConditionValue) {
    delete nextFilter.op;
    delete nextFilter.value;
    delete nextFilter.valueTo;
  }
  return {
    draft: { draftOp: effectiveOp, draftValue: nextVal, draftValueTo: nextValTo },
    nextFilter: isColumnFilterActive(nextFilter) ? nextFilter : undefined,
  };
}

/** One active column filter as a row predicate (used for cascading value lists). */
export interface FacetFilterTest<T> {
  key: string;
  test: (row: T) => boolean;
}

/**
 * Excel-style cascading value lists. Splits rows into those passing every active filter
 * (`passAll`) and those failing exactly one (`failOnly[key]`). A column's value list is then
 * built from `facetRowsFor(result, key)`: rows that pass every OTHER filter.
 */
export function partitionRowsForFacets<T>(
  rows: ReadonlyArray<T>,
  tests: ReadonlyArray<FacetFilterTest<T>>,
  include: (row: T) => boolean = () => true
): { passAll: T[]; failOnly: Record<string, T[]> } {
  const passAll: T[] = [];
  const failOnly: Record<string, T[]> = {};
  for (const row of rows) {
    if (!include(row)) continue;
    let failedKey: string | null = null;
    let failures = 0;
    for (const t of tests) {
      if (!t.test(row)) {
        failures++;
        failedKey = t.key;
        if (failures > 1) break;
      }
    }
    if (failures === 0) passAll.push(row);
    else if (failures === 1 && failedKey !== null) (failOnly[failedKey] ??= []).push(row);
  }
  return { passAll, failOnly };
}

/** Rows a column's value list is built from: rows passing every filter except that column's own. */
export function facetRowsFor<T>(
  facets: { passAll: T[]; failOnly: Record<string, T[]> },
  key: string
): T[] {
  const own = facets.failOnly[key];
  return own && own.length > 0 ? facets.passAll.concat(own) : facets.passAll;
}
