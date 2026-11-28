import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const panel = readFileSync(path.join(root, 'src/features/treasury/OwnerExternalBoxPanel.tsx'), 'utf8');
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

test('owner box panel confirms entries and archive, sends companyId', () => {
  assert.match(panel, /confirmOwnerExternalBoxEntry/);
  assert.match(panel, /confirmOwnerExternalBoxArchive/);
  assert.match(panel, /companyId:\s*entry\.companyId/);
  assert.match(panel, /تجهيز القيد \(بانتظار شخص ثانٍ\)/);
  assert.doesNotMatch(panel, /قيد \(خارج الدفاتر\)/);
});

test('api exposes confirm and reverse owner-box routes', () => {
  assert.match(api, /confirmOwnerExternalBoxEntry/);
  assert.match(api, /confirmOwnerExternalBoxArchive/);
  assert.match(api, /reverseOwnerExternalBoxEntry/);
  assert.match(api, /confirm-archive/);
});
