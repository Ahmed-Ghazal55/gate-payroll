export type ColumnFilterType = 'list' | 'text';

export interface ColumnFilterOption {
  value: string;
  label: string;
}

/**
 * Normalizes Arabic text and digits for consistent option & cell filtering.
 */
export function normalizeFilterText(text: string | null | undefined): string {
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
    .replace(/[٩۹]/g, '9')
    .replace(/[\u064B-\u065F\u0670\u0640]/g, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .replace(/ؤ/g, 'و')
    .replace(/ئ/g, 'ي')
    .toLowerCase()
    .trim();
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

/**
 * Resolves whether a DataTable column filter renders as a type-in searchable dropdown ('list')
 * or a free-text input ('text'). Explicit declarations always win; otherwise code/status/city/
 * company/platform/vehicle/category/provider/type columns default to 'list'.
 */
export function inferColumnFilterType(
  columnKey: string,
  explicit?: ColumnFilterType
): ColumnFilterType {
  if (explicit === 'list' || explicit === 'text') return explicit;
  const trimmed = (columnKey || '').trim();
  if (!trimmed) return 'text';
  const low = trimmed.toLowerCase().replace(/[-_\s]/g, '');
  if (LIST_KEY_EXACT.has(low)) return 'list';
  if (LIST_KEY_SUFFIX.test(trimmed)) return 'list';
  if (TEXT_KEY_HINT.test(trimmed)) return 'text';
  return 'text';
}

const AR_COLLATOR = new Intl.Collator('ar', { numeric: true, sensitivity: 'base' });

/**
 * Extracts sorted unique non-empty options from raw column values.
 */
export function extractDistinctOptions(
  values: ReadonlyArray<string | number | null | undefined>
): ColumnFilterOption[] {
  const seen = new Map<string, string>();
  for (const raw of values) {
    if (raw === null || raw === undefined) continue;
    const text = String(raw).trim();
    if (!text || text === '—' || text === '-') continue;
    const norm = normalizeFilterText(text);
    if (!norm || seen.has(norm)) continue;
    seen.set(norm, text);
  }
  return Array.from(seen.values())
    .sort((a, b) => AR_COLLATOR.compare(a, b))
    .map((v) => ({ value: v, label: v }));
}

/**
 * Filters dropdown options by typed search query (multi-word Arabic/Latin normalized matching).
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
 * Matches a cell string against a column filter term.
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
      const next = action.count <= 0 ? 0 : (state.activeIndex + action.delta + action.count) % action.count;
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
