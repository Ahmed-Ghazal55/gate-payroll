import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('payroll and operations use Link for in-app money/ops targets', () => {
  const payroll = readFileSync(path.join(root, 'src/app/payroll/page.tsx'), 'utf8');
  const operations = readFileSync(path.join(root, 'src/app/operations/page.tsx'), 'utf8');
  assert.doesNotMatch(payroll, /<a href="\/treasury\/\?tab=jahez"/);
  assert.match(payroll, /<Link href="\/treasury\/\?tab=jahez"/);
  assert.doesNotMatch(operations, /<a className="text-\[var\(--accent\)\]" href=\{\`\/workforce\//);
  assert.match(operations, /<Link className="text-\[var\(--accent\)\]" href=\{\`\/workforce\//);
});

test('imports hungerstation and chefz links match payroll URL params', () => {
  const imports = readFileSync(path.join(root, 'src/app/imports/page.tsx'), 'utf8');
  assert.match(imports, /\/payroll\/\?tab=platforms&app=hungerstation&sub=settlements/);
  assert.match(imports, /\/payroll\/\?tab=platforms&app=thechefz&sub=settlements/);
});
