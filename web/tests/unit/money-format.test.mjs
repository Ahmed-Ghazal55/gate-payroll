import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function formatSar(value) {
  if (value === null || value === undefined || value === '') return '—';
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n)) return '—';
  return `${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ر.س`;
}

test('formatSar uses two decimals and ر.س', () => {
  assert.equal(formatSar(1234.5), '1,234.50 ر.س');
  assert.equal(formatSar(0), '0.00 ر.س');
  assert.equal(formatSar('99'), '99.00 ر.س');
  assert.equal(formatSar(null), '—');
  assert.equal(formatSar(undefined), '—');
  assert.equal(formatSar(''), '—');
  assert.equal(formatSar(Number.NaN), '—');
  assert.equal(formatSar(-12.3), '-12.30 ر.س');
});

test('money.ts exports formatSar and formatMoney', () => {
  const src = readFileSync(path.join(root, 'src/lib/money.ts'), 'utf8');
  assert.match(src, /export function formatSar/);
  assert.match(src, /export const formatMoney = formatSar/);
  assert.match(src, /ر\.س/);
});

test('formatDataTableFooterTotal: money gets ر.س, count does not', async () => {
  const { formatDataTableFooterTotal } = await import('../../src/lib/money.ts');
  const money = formatDataTableFooterTotal(1250, { money: true });
  const count = formatDataTableFooterTotal(42, { money: false, numeric: true });
  assert.match(money, /ر\.س/);
  assert.doesNotMatch(count, /ر\.س/);
  assert.equal(count, '42');
});

test('DataTable footer uses money flag only (not every numeric)', () => {
  const table = readFileSync(path.join(root, 'src/components/ui/DataTable.tsx'), 'utf8');
  assert.match(table, /formatDataTableFooterTotal\(/);
  assert.doesNotMatch(table, /col\.money \|\| col\.numeric/);
});

test('debts and manual journals mark money columns for footer', () => {
  const debts = readFileSync(path.join(root, 'src/app/debts/page.tsx'), 'utf8');
  const journals = readFileSync(
    path.join(root, 'src/features/accounting/ManualJournalsPanel.tsx'),
    'utf8'
  );
  assert.match(debts, /key:\s*'originalAmount'[\s\S]*?money:\s*true/);
  assert.match(debts, /key:\s*'remainingAmount'[\s\S]*?money:\s*true/);
  assert.match(debts, /key:\s*'monthlyInstallment'[\s\S]*?money:\s*true/);
  assert.match(debts, /key:\s*'amount'[\s\S]*?money:\s*true/);
  assert.match(journals, /key:\s*'totalDebit'[\s\S]*?money:\s*true/);
});
