import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const panel = readFileSync(path.join(root, 'src/features/treasury/OwnerExternalBoxPanel.tsx'), 'utf8');
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

test('owner box panel cancels pending entries (JC1f #2)', () => {
  assert.match(api, /cancelOwnerExternalBoxEntry/);
  assert.match(panel, /cancelOwnerExternalBoxEntry/);
  assert.match(panel, /إلغاء التجهيز/);
  assert.match(panel, /cancel_entry/);
});
