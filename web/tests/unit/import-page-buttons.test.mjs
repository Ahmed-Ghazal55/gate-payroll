import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

test('shared ImportUploadDialog posts preview then commit URLs', () => {
  const dialog = read('src/features/imports/ImportUploadDialog.tsx');
  assert.match(dialog, /api\.postImportPreview\(step\.previewUrl/);
  assert.match(dialog, /api\.postImportCommit\(step\.commitUrl/);
  assert.match(dialog, /dataBase64/);
});

test('data pages expose import buttons gated by permission helpers', () => {
  const cases = [
    ['src/app/companies/page.tsx', 'import-companies', 'COMPANIES_IMPORT'],
    ['src/app/workforce/page.tsx', 'import-riders', 'RIDERS_IMPORT'],
    ['src/app/debts/page.tsx', 'import-opening-debts', 'OPENING_DEBTS_IMPORT'],
    ['src/app/debts/page.tsx', 'import-bulk-collect', 'BULK_COLLECT_IMPORT'],
    ['src/app/accounting/page.tsx', 'import-treasury-opening', 'TREASURY_OPENING_IMPORT'],
    ['src/app/payroll/page.tsx', 'import-ninja-operations', 'NINJA_OPS_IMPORT'],
    ['src/app/payroll/page.tsx', 'import-keeta-operations', 'KEETA_OPS_IMPORT'],
    ['src/app/payroll/page.tsx', 'import-hs-operations', 'HS_OPS_IMPORT'],
  ];
  for (const [file, testId, step] of cases) {
    const text = read(file);
    assert.match(text, new RegExp(`data-testid="${testId}"`), `${file} missing ${testId}`);
    assert.match(text, new RegExp(`canShowImportStep\\(${step}`), `${file} missing gate for ${step}`);
    assert.match(text, /ImportUploadDialog/, `${file} missing dialog`);
  }
});

test('import step configs keep platform preview/commit endpoints', () => {
  const steps = read('src/features/imports/import-steps.ts');
  assert.match(steps, /\/api\/companies\/import\/preview/);
  assert.match(steps, /\/api\/excel\/riders\/preview/);
  assert.match(steps, /\/api\/finance\/opening\/preview/);
  assert.match(steps, /\/api\/debts\/import-opening\/preview/);
  assert.match(steps, /\/api\/debts\/bulk-collect\/preview-file/);
  assert.match(steps, /\/api\/pay\/apps\/ninja\/operations\/import\/preview/);
  assert.match(steps, /\/api\/pay\/apps\/keeta\/operations\/import\/preview/);
  assert.match(steps, /\/api\/pay\/apps\/hs\/operations\/import\/preview/);
});
