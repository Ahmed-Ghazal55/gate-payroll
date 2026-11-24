import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '../..');

test('SCR1 false-positive: workforce تحديث has onClick reload', () => {
  const src = readFileSync(join(root, 'src/app/workforce/page.tsx'), 'utf8');
  assert.match(src, /label:\s*'تحديث'/);
  assert.match(src, /onClick:\s*\(\)\s*=>\s*\{[\s\S]*loadDirectory/);
});

test('SCR1 false-positive: riders statement تصدير إكسل calls exportExcel', () => {
  const src = readFileSync(join(root, 'src/app/riders/statement/page.tsx'), 'utf8');
  assert.match(src, /handleExportExcel/);
  assert.match(src, /exportExcel/);
  assert.match(src, /تصدير إكسل/);
});

test('TRACE U-W1-24: manual journal UI cancels drafts (cancel action)', () => {
  const src = readFileSync(join(root, 'src/features/accounting/ManualJournalsPanel.tsx'), 'utf8');
  assert.match(src, /allowedActions\?\.includes\('cancel'\)/);
  assert.match(src, /إلغاء المسودة/);
});

test('FLOW1 audit prefixes cover sal/treasury/payment', () => {
  const src = readFileSync(join(root, 'src/lib/audit-labels.ts'), 'utf8');
  assert.match(src, /prefix:\s*'sal\.'/);
  assert.match(src, /prefix:\s*'treasury\.'/);
  assert.match(src, /finance\.manual_cancelled/);
  assert.match(src, /treasury\.bank_adjust_rejected/);
});
