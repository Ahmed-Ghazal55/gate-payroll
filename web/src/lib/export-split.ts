// R1 / F-R2 (Ghazal 2026-11-19): Excel exports split merged data into separate columns.
// The screen keeps its stacked cells; only the exported sheet changes.
// - An IBAN column becomes three columns: IBAN, bank short code, bank name.
// - IDs, iqama, phones and IBANs stay text (never numbers), so leading digits survive.
// - A missing value is an empty cell, never 0.
import { bankNameOfIban, bankShortCodeOfIban, cleanIban } from './saudi-banks.ts';

export type ExportCell = string | number | null | undefined;

export interface ExportTable {
  headers: string[];
  rows: (string | number)[][];
  numericColumns: number[];
}

const IBAN_HEADER = /(آيبان|ايبان|الآيبان|iban)/i;
// Headers whose values are identifiers: always text cells.
const TEXT_HEADER = /(إقامة|اقامة|هوية|جوال|هاتف|رقم الحساب|آيبان|ايبان|iban|iqama|phone|id$|الرقم الموحد|سجل)/i;
const SAUDI_IBAN = /^SA\d{22}$/;

/** True when the column is an IBAN column: by its header, or every non-empty value is a Saudi IBAN. */
export function isIbanColumn(header: string, values: ExportCell[]): boolean {
  // Reject composite/alert headers (e.g. «المنصة / الآيبان», «تنبيه الآيبان») unless cells are pure IBANs.
  if (IBAN_HEADER.test(header)) {
    if (/[\/|]/.test(header) || /(تنبيه|حالة|ملاحظة)/.test(header)) {
      const filled = values.map((v) => cleanIban(v == null ? '' : String(v))).filter((v) => v && v !== '—');
      return filled.length > 0 && filled.every((v) => SAUDI_IBAN.test(v));
    }
    return true;
  }
  const filled = values.map((v) => cleanIban(v == null ? '' : String(v))).filter((v) => v && v !== '—');
  return filled.length > 0 && filled.every((v) => SAUDI_IBAN.test(v));
}

const text = (v: ExportCell): string => (v == null ? '' : String(v));

/**
 * Expands every IBAN column into IBAN / كود البنك / اسم البنك and drops number formatting
 * from identifier columns. Pure function: used by api.exportExcel for every export.
 */
export function splitForExport(
  headers: string[],
  rows: ExportCell[][],
  numericColumns: number[] = []
): ExportTable {
  const ibanCols = new Set<number>();
  headers.forEach((h, i) => {
    if (isIbanColumn(h, rows.map((r) => r[i]))) ibanCols.add(i);
  });
  const numeric = new Set(numericColumns.filter((i) => !ibanCols.has(i) && !TEXT_HEADER.test(headers[i] ?? '')));

  const outHeaders: string[] = [];
  const outNumeric: number[] = [];
  headers.forEach((h, i) => {
    if (ibanCols.has(i)) {
      outHeaders.push(h, 'كود البنك', 'اسم البنك');
      return;
    }
    if (numeric.has(i)) outNumeric.push(outHeaders.length);
    outHeaders.push(h);
  });

  const outRows = rows.map((row) => {
    const out: (string | number)[] = [];
    headers.forEach((_, i) => {
      const v = row[i];
      if (ibanCols.has(i)) {
        const raw = cleanIban(text(v));
        const iban = raw === '—' ? '' : raw;
        const isSaudi = SAUDI_IBAN.test(iban);
        out.push(iban, isSaudi ? bankShortCodeOfIban(iban) : '', isSaudi ? bankNameOfIban(iban) : '');
        return;
      }
      if (v == null || v === '') {
        out.push('');
        return;
      }
      out.push(numeric.has(i) && typeof v === 'number' ? v : text(v));
    });
    return out;
  });

  return { headers: outHeaders, rows: outRows, numericColumns: outNumeric };
}
