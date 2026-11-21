// Single URL-backed period (`?period=YYYY-MM`) shared across the global header picker,
// every page/panel period picker, and in-app navigation links.

export const PERIOD_PARAM = 'period';
const PERIOD_RE = /^\d{4}-(0[1-9]|1[0-2])$/;

export function isValidPeriod(value: string | null | undefined): value is string {
  return typeof value === 'string' && PERIOD_RE.test(value.trim());
}

export function currentYearMonth(now: Date = new Date()): string {
  return now.toISOString().slice(0, 7);
}

function toSearchParams(search: string | URLSearchParams | null | undefined): URLSearchParams {
  if (!search) return new URLSearchParams();
  if (typeof search === 'string') return new URLSearchParams(search);
  return new URLSearchParams(search.toString());
}

/**
 * Reads the canonical `?period=YYYY-MM` from a query string (falling back to legacy `?month=`),
 * or returns `fallback` / current UTC year-month if absent or invalid.
 */
export function resolvePeriod(
  search: string | URLSearchParams | null | undefined,
  fallback?: string,
  now: Date = new Date()
): string {
  const params = toSearchParams(search);
  const rawPeriod = params.get(PERIOD_PARAM)?.trim();
  if (isValidPeriod(rawPeriod)) return rawPeriod;
  const legacyMonth = params.get('month')?.trim();
  if (isValidPeriod(legacyMonth)) return legacyMonth;
  if (isValidPeriod(fallback)) return fallback.trim();
  return currentYearMonth(now);
}

/**
 * Returns the explicit valid period present in the URL query string, or null if none is set.
 */
export function readUrlPeriod(search: string | URLSearchParams | null | undefined): string | null {
  const params = toSearchParams(search);
  const rawPeriod = params.get(PERIOD_PARAM)?.trim();
  if (isValidPeriod(rawPeriod)) return rawPeriod;
  const legacyMonth = params.get('month')?.trim();
  if (isValidPeriod(legacyMonth)) return legacyMonth;
  return null;
}

/**
 * Produces a URL string with `?period=YYYY-MM` set while keeping all other query parameters
 * and dropping any legacy `?month=` parameter.
 */
export function withPeriodInUrl(
  pathname: string,
  search: string | URLSearchParams | null | undefined,
  nextPeriod: string,
  extraPatch?: Record<string, string | null>
): string {
  const params = toSearchParams(search);
  const clean = nextPeriod.trim();
  if (isValidPeriod(clean)) {
    params.set(PERIOD_PARAM, clean);
    params.delete('month');
  }
  if (extraPatch) {
    for (const [key, val] of Object.entries(extraPatch)) {
      if (val === null || val === '') params.delete(key);
      else params.set(key, val);
    }
  }
  const qs = params.toString();
  return qs ? `${pathname}?${qs}` : pathname;
}

/**
 * Preserves the active `?period=YYYY-MM` on internal app navigation links.
 * Leaves external URLs, hash-only links, and links that already carry a valid `period` unchanged.
 */
export function appendPeriodToHref(href: string, period: string | null | undefined): string {
  if (!href || href.startsWith('#') || !isValidPeriod(period)) return href;
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(href) || href.startsWith('//')) return href;
  const url = new URL(href, 'https://gate.local');
  if (!isValidPeriod(url.searchParams.get(PERIOD_PARAM))) {
    url.searchParams.set(PERIOD_PARAM, period.trim());
  }
  return `${url.pathname}${url.search}${url.hash}`;
}

/**
 * Computes UTC calendar start and end dates (`YYYY-MM-01` .. `YYYY-MM-<last>`) for a `YYYY-MM` period.
 */
export function periodDateRange(period: string, now: Date = new Date()): { from: string; to: string } {
  const valid = isValidPeriod(period) ? period.trim() : currentYearMonth(now);
  const [yearStr, monthStr] = valid.split('-');
  const year = Number(yearStr);
  const month = Number(monthStr);
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return {
    from: `${yearStr}-${monthStr}-01`,
    to: `${yearStr}-${monthStr}-${String(lastDay).padStart(2, '0')}`,
  };
}

/**
 * Generates descending `YYYY-MM` options for the global header period selector,
 * always including `selectedPeriod` even if outside the rolling window.
 */
export function buildPeriodOptions(
  selectedPeriod: string,
  monthsBack = 18,
  now: Date = new Date()
): Array<{ value: string; label: string }> {
  const values = new Set<string>();
  const baseYear = now.getUTCFullYear();
  const baseMonth = now.getUTCMonth(); // 0-indexed
  // Include next month (+1) down to -monthsBack
  for (let offset = 1; offset >= -monthsBack; offset--) {
    const d = new Date(Date.UTC(baseYear, baseMonth + offset, 1));
    values.add(d.toISOString().slice(0, 7));
  }
  if (isValidPeriod(selectedPeriod)) {
    values.add(selectedPeriod.trim());
  }
  return Array.from(values)
    .sort((a, b) => b.localeCompare(a))
    .map((v) => ({ value: v, label: v }));
}
