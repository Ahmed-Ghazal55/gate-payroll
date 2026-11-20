import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
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

test('imports centre page and /imports hrefs are gone', () => {
  assert.equal(existsSync(path.join(root, 'src/app/imports')), false);
  const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
  assert.doesNotMatch(sidebar, /\/imports\/?/);
  assert.doesNotMatch(sidebar, /استيراد الملفات/);

  const appDir = path.join(root, 'src');
  const stack = [appDir];
  const offenders = [];
  while (stack.length) {
    const dir = stack.pop();
    for (const name of readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, name.name);
      if (name.isDirectory()) {
        if (name.name === 'node_modules' || name.name === '.next') continue;
        stack.push(full);
        continue;
      }
      if (!/\.(tsx|ts|mjs|jsx|js)$/.test(name.name)) continue;
      const text = readFileSync(full, 'utf8');
      if (/['"`]\/(?:app\/)?imports\/?['"`]/.test(text) || /href:\s*['"]\/app\/imports/.test(text)) {
        offenders.push(path.relative(root, full));
      }
    }
  }
  assert.deepEqual(offenders, [], `dead /imports links: ${offenders.join(', ')}`);
});

test('payroll ops import blocker points at payroll platforms, not imports centre', () => {
  const payroll = readFileSync(path.join(root, 'src/app/payroll/page.tsx'), 'utf8');
  assert.match(payroll, /import_operations:.*href: '\/app\/payroll\?tab=platforms/);
  assert.doesNotMatch(payroll, /import_operations:.*\/app\/imports/);
});
