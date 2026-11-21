import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

test('workforce legal tab visibility uses permission not only directory allowedActions', () => {
  const page = read('src/app/workforce/page.tsx');
  assert.match(page, /riders\.legal_cases\.view/);
  assert.match(page, /القضايا/);
});

test('DataTable scroll area has bottom padding so last row is not clipped', () => {
  const table = read('src/components/ui/DataTable.tsx');
  assert.match(table, /max-h-\[68vh\][^\n]*pb-|pb-\d+[^\n]*max-h-\[68vh\]/);
});

test('SearchableSelect shows placeholder when value is empty string', async () => {
  const src = read('src/components/ui/SearchableSelect.tsx');
  // Empty string must not pick the blank option label as the closed display.
  assert.match(src, /value === ''[\s\S]{0,120}undefined|!value[\s\S]{0,80}placeholder/);
  assert.doesNotMatch(
    src,
    /const selected = options\.find\(\(o\) => o\.value === value\);/
  );
});
