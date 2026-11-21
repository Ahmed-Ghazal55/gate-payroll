import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** Mirror of formatMudadSalary in src/lib/money.ts (node unit test, no TS loader). */
function formatSar(value) {
  if (value === null || value === undefined || value === '') return '—';
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) return '—';
  return `${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ر.س`;
}

function formatMudadSalary(value) {
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

test('formatMudadSalary formats finite numbers as SAR', () => {
  assert.equal(formatMudadSalary(4500), '4,500.00 ر.س');
  assert.equal(formatMudadSalary('3200'), '3,200.00 ر.س');
  assert.equal(formatMudadSalary('99.5'), '99.50 ر.س');
});

test('formatMudadSalary keeps non-numeric backend text', () => {
  assert.equal(formatMudadSalary('لا يوجد مصدر بعد'), 'لا يوجد مصدر بعد');
  assert.equal(formatMudadSalary('masked note'), 'masked note');
});

test('formatMudadSalary empty or masked blank becomes dash', () => {
  assert.equal(formatMudadSalary(''), '—');
  assert.equal(formatMudadSalary('   '), '—');
  assert.equal(formatMudadSalary(null), '—');
  assert.equal(formatMudadSalary(undefined), '—');
  assert.equal(formatMudadSalary(Number.NaN), '—');
});

test('money.ts exports formatMudadSalary', () => {
  const src = readFileSync(path.join(root, 'src/lib/money.ts'), 'utf8');
  assert.match(src, /export function formatMudadSalary/);
});
