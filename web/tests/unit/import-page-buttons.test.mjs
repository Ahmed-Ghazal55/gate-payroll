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
  assert.match(dialog, /canDownloadImportTemplate/);
  assert.match(dialog, /allowedActions/);
  assert.match(dialog, /import-download-template/);
});

test('template download requires imports.view or endpoint permission via allowedActions', async () => {
  const { canDownloadImportTemplate, NINJA_OPS_IMPORT, OPENING_DEBTS_IMPORT } = await import(
    '../../src/features/imports/import-steps.ts'
  );
  assert.equal(NINJA_OPS_IMPORT.templatePermission, 'imports.view');
  assert.equal(OPENING_DEBTS_IMPORT.templatePermission, 'debts.view');
  const can = (code) => code === 'pay.manage';
  assert.equal(
    canDownloadImportTemplate(NINJA_OPS_IMPORT, { isAdmin: false, can, allowedActions: [] }),
    false,
    'pay.manage alone must not unlock template download'
  );
  assert.equal(
    canDownloadImportTemplate(NINJA_OPS_IMPORT, {
      isAdmin: false,
      can,
      allowedActions: ['download_template'],
    }),
    true
  );
  assert.equal(
    canDownloadImportTemplate(OPENING_DEBTS_IMPORT, {
      isAdmin: false,
      can: (c) => c === 'debts.view',
      allowedActions: null,
    }),
    true,
    'fallback uses templatePermission when allowedActions omitted'
  );
});

test('data pages expose import buttons gated by permission helpers', () => {
  const cases = [
    ['src/app/companies/page.tsx', "testId: 'import-companies'", 'COMPANIES_IMPORT', 'ImportUploadDialog'],
    ['src/app/debts/page.tsx', 'import-opening-debts', 'OPENING_DEBTS_IMPORT', 'ImportUploadDialog'],
    ['src/app/debts/page.tsx', 'import-bulk-collect', 'BULK_COLLECT_IMPORT', 'ImportUploadDialog'],
    ['src/app/accounting/page.tsx', 'import-treasury-opening', 'TREASURY_OPENING_IMPORT', 'ImportUploadDialog'],
    ['src/app/payroll/page.tsx', 'import-ninja-operations', 'NINJA_OPS_IMPORT', 'ImportUploadDialog'],
    ['src/app/payroll/page.tsx', 'import-keeta-operations', 'KEETA_OPS_IMPORT', 'KeetaOperationsImportDialog'],
    ['src/app/payroll/page.tsx', 'import-hs-operations', 'HS_OPS_IMPORT', 'ImportUploadDialog'],
  ];
  for (const [file, marker, step, dialog] of cases) {
    const text = read(file);
    if (marker.startsWith('testId:')) {
      assert.match(text, new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')), `${file} missing ${marker}`);
    } else {
      assert.match(text, new RegExp(`data-testid="${marker}"`), `${file} missing ${marker}`);
    }
    assert.match(text, new RegExp(`canShowImportStep\\(${step}`), `${file} missing gate for ${step}`);
    assert.match(text, new RegExp(dialog), `${file} missing ${dialog}`);
  }
});

test('Keeta ops dialog picks register before upload and styles dismissed rows', () => {
  const dialog = read('src/features/imports/KeetaOperationsImportDialog.tsx');
  assert.match(dialog, /اختر السجل قبل/);
  assert.match(dialog, /register/);
  assert.match(dialog, /invoiceId/);
  assert.match(dialog, /displayRows/);
  assert.match(dialog, /bg-red-600 text-white/);
  assert.match(dialog, /api-contract/);
  assert.match(dialog, /AutoFilterTh/);
  assert.match(dialog, /OpsDisplayRow/);
  assert.doesNotMatch(dialog, /as OpsDisplayRow/);
  assert.doesNotMatch(dialog, /Record<string, never>/);
  assert.doesNotMatch(dialog, /\[key: string\]: unknown/);
  assert.match(read('src/features/imports/import-steps.ts'), /الصلاحية متوقعة فقط/);
});

test('companies import-companies primary opens file ImportUploadDialog', () => {
  const page = read('src/app/companies/page.tsx');
  assert.match(page, /label:\s*'استيراد الشركات'/);
  assert.match(page, /testId:\s*'import-companies'/);
  // Primary action must open the Excel file dialog, not the built-in official plan.
  assert.match(
    page,
    /testId:\s*'import-companies'[\s\S]*?onClick:\s*\(\)\s*=>\s*\{\s*setFileImportOpen\(true\)/
  );
  assert.match(page, /moreActions[\s\S]*خطة شهادات الوزارة الرسمية/);
  assert.match(page, /setImportModalOpen\(true\)/);
});

test('workforce page has no riders-file upload (people data is live from ERP, Ghazal 2026-11-21)', () => {
  const page = read('src/app/workforce/page.tsx');
  assert.doesNotMatch(page, /import-riders/);
  assert.doesNotMatch(page, /RIDERS_IMPORT/);
  assert.doesNotMatch(page, /ImportUploadDialog/);
  assert.doesNotMatch(page, /ملف المناديب/);
  assert.doesNotMatch(read('src/features/imports/import-steps.ts'), /RIDERS_IMPORT|\/api\/excel\/riders/);
});

test('import step configs keep platform preview/commit endpoints', () => {
  const steps = read('src/features/imports/import-steps.ts');
  assert.match(steps, /\/api\/companies\/import\/preview/);
  assert.match(steps, /\/api\/finance\/opening\/preview/);
  assert.match(steps, /\/api\/debts\/import-opening\/preview/);
  assert.match(steps, /\/api\/debts\/bulk-collect\/preview-file/);
  assert.match(steps, /\/api\/pay\/apps\/ninja\/operations\/import\/preview/);
  assert.match(steps, /\/api\/pay\/apps\/keeta\/operations\/import\/preview/);
  assert.match(steps, /\/api\/pay\/apps\/hs\/operations\/import\/preview/);
});
