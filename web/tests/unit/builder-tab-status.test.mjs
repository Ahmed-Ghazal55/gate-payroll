import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const tab = readFileSync(path.join(root, 'src/features/forms/BuilderTab.tsx'), 'utf8');

assert.ok(tab.includes("form.status === 'open'") || tab.includes("status === 'open'"), 'open status is treated as published');
assert.ok(tab.includes('titleAr'), 'renders titleAr from the API');
assert.ok(tab.includes('form.fields') || tab.includes('formFieldCount'), 'counts fields from the API payload');
assert.ok(tab.includes('closeCustomForm'), 'wires close action to the API');
assert.ok(!/\bform\.questions\.length\b/.test(tab), 'must not crash on missing questions');
console.log('PASS builder tab status/fields mapping');
