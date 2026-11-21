import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const css = readFileSync(path.join(root, 'src/app/globals.css'), 'utf8');

test('td.num and th.num keep display: table-cell', () => {
  assert.match(css, /td\.num\s*,\s*th\.num\s*\{[^}]*display:\s*table-cell/s);
});

test('.num still isolates LTR tabular digits for inline spans', () => {
  assert.match(css, /\.num\s*\{[^}]*direction:\s*ltr/s);
  assert.match(css, /\.num\s*\{[^}]*unicode-bidi:\s*isolate/s);
});
