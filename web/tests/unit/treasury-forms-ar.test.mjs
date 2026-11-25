import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const jahez = readFileSync(path.join(root, 'src/features/treasury/JahezWorkspace.tsx'), 'utf8');
const bank = readFileSync(path.join(root, 'src/app/treasury/page.tsx'), 'utf8');

test('Jahez ops forms use Arabic labels not raw English keys as placeholders', () => {
  assert.match(jahez, /من خزنة/);
  assert.match(jahez, /إلى خزنة/);
  assert.match(jahez, /المبلغ/);
  assert.match(jahez, /مرجع المصدر/);
  assert.doesNotMatch(jahez, /placeholder=\{k\}/);
  assert.doesNotMatch(jahez, /placeholder="fromVaultCode"/);
});

test('Treasury extras panel is removed', () => {
  assert.equal(existsSync(path.join(root, 'src/features/treasury/TreasuryExtrasPanel.tsx')), false);
  assert.doesNotMatch(bank, /TreasuryExtrasPanel/);
  assert.doesNotMatch(bank, /tab=extras|activeTab === 'extras'/);
});

test('bank file without company is empty state not red error', () => {
  assert.match(bank, /اختر شركة كفيلة/);
  assert.doesNotMatch(bank, /setBankFileError\('اختر شركة كفيلة لمعاينة ملف البنك'\)/);
});
