import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {
  isValidPeriod,
  resolvePeriod,
  readUrlPeriod,
  withPeriodInUrl,
  appendPeriodToHref,
  periodDateRange,
  buildPeriodOptions,
} from '../../src/lib/period.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

test('isValidPeriod validates strict YYYY-MM format', () => {
  assert.equal(isValidPeriod('2026-10'), true);
  assert.equal(isValidPeriod('2026-01'), true);
  assert.equal(isValidPeriod('2026-12'), true);
  assert.equal(isValidPeriod('2026-00'), false);
  assert.equal(isValidPeriod('2026-13'), false);
  assert.equal(isValidPeriod('2026-1'), false);
  assert.equal(isValidPeriod(''), false);
  assert.equal(isValidPeriod(null), false);
});

test('resolvePeriod reads ?period= first, falls back to legacy ?month=, then fallback/now', () => {
  const fixedNow = new Date('2026-11-21T09:00:00Z');
  assert.equal(resolvePeriod('tab=cycle&period=2026-10', undefined, fixedNow), '2026-10');
  assert.equal(resolvePeriod('tab=fuel&month=2026-09', undefined, fixedNow), '2026-09');
  assert.equal(resolvePeriod('period=2026-10&month=2026-08', undefined, fixedNow), '2026-10');
  assert.equal(resolvePeriod('period=bad-val', '2026-07', fixedNow), '2026-07');
  assert.equal(resolvePeriod('', undefined, fixedNow), '2026-11');
  assert.equal(readUrlPeriod('tab=cycle'), null);
  assert.equal(readUrlPeriod('tab=cycle&period=2026-10'), '2026-10');
});

test('withPeriodInUrl updates ?period=, preserves other params, and drops legacy ?month=', () => {
  assert.equal(
    withPeriodInUrl('/payroll/', 'tab=platforms&app=ninja&sub=settlements', '2026-10'),
    '/payroll/?tab=platforms&app=ninja&sub=settlements&period=2026-10'
  );
  assert.equal(
    withPeriodInUrl('/operations/', 'tab=fuel&month=2026-08&page=3', '2026-10', { page: '1' }),
    '/operations/?tab=fuel&page=1&period=2026-10'
  );
  // Invalid period does not corrupt URL
  assert.equal(
    withPeriodInUrl('/accounting/', 'tab=trial_balance&period=2026-10', 'not-a-month'),
    '/accounting/?tab=trial_balance&period=2026-10'
  );
});

test('appendPeriodToHref keeps active period across navigation without overwriting explicit period', () => {
  assert.equal(appendPeriodToHref('/payroll/?tab=cycle', '2026-10'), '/payroll/?tab=cycle&period=2026-10');
  assert.equal(appendPeriodToHref('/debts/', '2026-10'), '/debts/?period=2026-10');
  assert.equal(
    appendPeriodToHref('/payroll/?tab=cycle&period=2026-09', '2026-10'),
    '/payroll/?tab=cycle&period=2026-09'
  );
  assert.equal(appendPeriodToHref('#section', '2026-10'), '#section');
  assert.equal(appendPeriodToHref('https://example.com/a', '2026-10'), 'https://example.com/a');
  assert.equal(appendPeriodToHref('/debts/', null), '/debts/');
});

test('periodDateRange handles 28, 29 (leap), 30, and 31 day months', () => {
  assert.deepEqual(periodDateRange('2026-10'), { from: '2026-10-01', to: '2026-10-31' });
  assert.deepEqual(periodDateRange('2026-11'), { from: '2026-11-01', to: '2026-11-30' });
  assert.deepEqual(periodDateRange('2026-02'), { from: '2026-02-01', to: '2026-02-28' });
  assert.deepEqual(periodDateRange('2024-02'), { from: '2024-02-01', to: '2024-02-29' });
});

test('buildPeriodOptions always includes selectedPeriod and sorts descending', () => {
  const opts = buildPeriodOptions('2024-05', 6, new Date('2026-11-21T00:00:00Z'));
  assert.ok(opts.some((o) => o.value === '2024-05'));
  assert.ok(opts.some((o) => o.value === '2026-11'));
  assert.ok(opts.some((o) => o.value === '2026-10'));
  for (let i = 1; i < opts.length; i++) {
    assert.ok(opts[i - 1].value > opts[i].value, 'options should be strictly descending');
  }
});

test('AppShell, Header, Sidebar, and period pages share URL-backed usePeriod with no duplicated local period state', () => {
  const appShell = read('src/components/layout/AppShell.tsx');
  const header = read('src/components/layout/Header.tsx');
  const sidebar = read('src/components/layout/Sidebar.tsx');
  const payroll = read('src/app/payroll/page.tsx');
  const treasury = read('src/app/treasury/page.tsx');
  const accounting = read('src/app/accounting/page.tsx');
  const operations = read('src/app/operations/page.tsx');

  assert.doesNotMatch(appShell, /useState\(['"]2026-10['"]\)/);
  assert.match(header, /usePeriod\(/);
  assert.match(sidebar, /appendPeriodToHref/);
  assert.match(payroll, /usePeriod\(/);
  assert.doesNotMatch(payroll, /const \[rulesMonth/);
  assert.doesNotMatch(payroll, /const \[legacyPeriod/);
  assert.match(treasury, /usePeriod\(/);
  assert.match(accounting, /usePeriod\(/);
  assert.match(operations, /usePeriod\(/);
});
