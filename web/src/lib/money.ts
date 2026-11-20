/** Shared SAR money display: always two decimals and «ر.س» (INT1). */
export function formatSar(value: number | string | null | undefined): string {
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) return '—';
  return `${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ر.س`;
}

/** Alias used by some screens; same output as formatSar. */
export const formatMoney = formatSar;
