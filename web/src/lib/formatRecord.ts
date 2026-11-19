export interface FormattedEntry {
  key: string;
  value: string;
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
      .map(([k, v]) => `${k}: ${formatReadableValue(v)}`);
    return parts.length > 0 ? parts.join(' · ') : '—';
  }
  return String(value);
}

export function formatRecordEntries(record: unknown): FormattedEntry[] {
  if (!record || typeof record !== 'object' || Array.isArray(record)) return [];
  return Object.entries(record as Record<string, unknown>).map(([key, val]) => ({
    key,
    value: formatReadableValue(val),
  }));
}
