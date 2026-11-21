import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('jahez status and totals labels are Arabic', async () => {
  const {
    jahezReconciliationStatusLabelAr,
    jahezTotalsKeyLabelAr,
  } = await import('../../src/features/treasury/jahez-labels.ts');
  assert.equal(jahezReconciliationStatusLabelAr('draft'), 'مسودة');
  assert.equal(jahezReconciliationStatusLabelAr('pending_approval'), 'بانتظار الاعتماد');
  assert.equal(jahezReconciliationStatusLabelAr('approved'), 'معتمد');
  assert.equal(jahezTotalsKeyLabelAr('deliveryFees'), 'رسوم التوصيل');
  assert.equal(jahezTotalsKeyLabelAr('variance'), 'الفرق');
  assert.equal(jahezTotalsKeyLabelAr('unknownThing'), 'قيمة');
});

test('Jahez and treasury company options use code || id', () => {
  const jahez = readFileSync(path.join(root, 'src/features/treasury/JahezWorkspace.tsx'), 'utf8');
  assert.match(jahez, /value:\s*c\.code\s*\|\|\s*c\.id/);
  assert.match(jahez, /jahezReconciliationStatusLabelAr/);
  assert.match(jahez, /jahezTotalsKeyLabelAr/);
});
