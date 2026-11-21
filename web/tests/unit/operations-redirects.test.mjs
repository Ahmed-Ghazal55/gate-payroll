import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const page = readFileSync(path.join(root, 'src/app/operations/page.tsx'), 'utf8');

test('O7 redirects: cases → workforce cases', () => {
  assert.match(page, /rawTab === 'cases'/);
  assert.match(page, /router\.replace\('\/workforce\/\?tab=cases'\)/);
});

test('O7 redirects: accounts → workforce', () => {
  assert.match(page, /rawTab === 'accounts'/);
  assert.match(page, /router\.replace\('\/workforce\/'\)/);
});

test('O7 redirects: custody/guarantees → vehicles', () => {
  assert.match(page, /rawTab === 'custody' \|\| rawTab === 'guarantees'/);
  assert.match(page, /router\.replace\('\/operations\/\?tab=vehicles'\)/);
});

test('O7 redirects: unknown tab → cities', () => {
  assert.match(page, /MAIN_TABS\.has\(rawTab\)/);
  assert.match(page, /router\.replace\('\/operations\/\?tab=cities'\)/);
});

test('O1: no horizontal main tab bar for the four sidebar entries', () => {
  assert.doesNotMatch(page, /label: 'العهد العينية'/);
  assert.doesNotMatch(page, /label: 'الكفالات'/);
  assert.doesNotMatch(page, /label: 'حسابات المنصات'/);
  assert.match(page, /MAIN_TABS = new Set\(\['cities', 'housing', 'vehicles', 'fuel'\]\)/);
});
