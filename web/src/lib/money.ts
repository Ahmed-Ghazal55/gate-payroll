/** Shared SAR money display: always two decimals and «ر.س» (INT1). */
export function formatSar(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) return '—';
  return `${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ر.س`;
}

/**
 * Directory «راتب مدد»: format finite amounts as SAR; keep non-numeric backend text
 * (e.g. «لا يوجد مصدر بعد»); empty / masked blank → «—».
 */
export function formatMudadSalary(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'number') {
    return Number.isFinite(value) ? formatSar(value) : '—';
  }
  const text = String(value).trim();
  if (text === '') return '—';
  if (/^-?\d+(?:\.\d+)?$/.test(text)) {
    const n = Number(text);
    if (Number.isFinite(n)) return formatSar(n);
  }
  return text;
}

/** Alias used by some screens; same output as formatSar. */
export const formatMoney = formatSar;

/**
 * DataTable footer total cell: «ر.س» only when the column is money.
 * Count / plain numeric columns stay without currency.
 */
export function formatDataTableFooterTotal(
  value: number,
  col: { money?: boolean; numeric?: boolean }
): string {
  if (col.money) return formatSar(value);
  return value.toLocaleString('en-US', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  });
}
