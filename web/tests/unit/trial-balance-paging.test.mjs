import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('paginateFilteredRows pages after filter and keeps full export set', async () => {
  const { paginateFilteredRows } = await import(
    '../../src/features/accounting/trial-balance-paging.ts'
  );
  const filtered = Array.from({ length: 100 }, (_, i) => ({ id: i + 1 }));
  const page1 = paginateFilteredRows(filtered, 1, 75);
  assert.equal(page1.totalPages, 2);
  assert.equal(page1.safePage, 1);
  assert.equal(page1.pageRows.length, 75);
  assert.equal(page1.pageRows[0].id, 1);
  assert.equal(page1.exportRows.length, 100);

  const page2 = paginateFilteredRows(filtered, 2, 75);
  assert.equal(page2.safePage, 2);
  assert.equal(page2.pageRows.length, 25);
  assert.equal(page2.pageRows[0].id, 76);
  assert.equal(page2.exportRows, filtered);

  const clamped = paginateFilteredRows(filtered, 99, 75);
  assert.equal(clamped.safePage, 2);
});

test('TrialBalanceTable filters baseRows then paginates tf.rows', () => {
  const src = readFileSync(path.join(root, 'src/app/accounting/page.tsx'), 'utf8');
  assert.match(src, /useTableAutoFilter<TrialBalanceCompanyRow>\(baseRows/);
  assert.doesNotMatch(src, /useTableAutoFilter<TrialBalanceCompanyRow>\(pageRows/);
  assert.match(src, /paginateFilteredRows\(tf\.rows/);
  assert.match(src, /onExport\(tf\.rows\)|onExport\(paging\.exportRows\)/);
  assert.match(src, /setPage\(1\).*filters|tf\.filters[\s\S]*setPage\(1\)/);
});
