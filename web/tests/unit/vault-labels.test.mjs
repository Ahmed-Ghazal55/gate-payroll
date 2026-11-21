import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const src = readFileSync(path.join(root, 'src/lib/vault-labels.ts'), 'utf8');
const page = readFileSync(path.join(root, 'src/app/treasury/page.tsx'), 'utf8');

test('vault-labels maps 111x to cashbox and 113x to bank', () => {
  assert.match(src, /isBankVaultCode/);
  assert.match(src, /isCashVaultCode/);
  assert.match(src, /vaultKindLabelAr/);
  assert.match(src, /تجميعي \(غير ترحيلي\)/);
  assert.match(src, /حساب بنكي رسمي/);
  assert.match(src, /صندوق نقدية تشغيلي/);
});

test('treasury vaults use vault-labels instead of inverted startsWith(111)=bank', () => {
  assert.match(page, /from ['"].*vault-labels['"]/);
  assert.match(page, /vaultGroupLabelAr|isBankVaultCode/);
  assert.doesNotMatch(page, /startsWith\('111'\) \? 'حساب بنكي/);
  assert.doesNotMatch(page, /حسابات بنكية \(1110\)/);
});

test('treasury drawer never shows raw English None for account type', () => {
  assert.match(page, /vaultKindLabelAr\(/);
  assert.doesNotMatch(page, /\{selectedAccount\.vaultKind \|\| selectedAccount\.accountType\}/);
});
