import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');

test('sidebar items inherit group permission when item.permission is omitted', () => {
  assert.match(sidebar, /item\.permission \?\? group\.permission/);
  assert.match(sidebar, /An item with no permission inherits/);
});

test('bank file tab requires export and payroll view together', () => {
  assert.match(
    sidebar,
    /treasury-bankfile[\s\S]*permission:\s*\['finance\.bankfile\.export',\s*'payroll\.view'\][\s\S]*requireAll:\s*true/
  );
});

test('workforce statement and audit tabs use matching API permissions', () => {
  assert.match(sidebar, /wf-statement[\s\S]*permission:\s*'debts\.view'/);
  assert.match(sidebar, /admin-audit[\s\S]*permission:\s*'audit\.view'/);
});

test('accounting tabs carry per-tab API permissions', () => {
  assert.match(sidebar, /acc-coa[\s\S]*permission:\s*'treasury\.view'/);
  assert.match(sidebar, /acc-trial[\s\S]*permission:\s*'treasury\.journals'/);
  assert.match(sidebar, /acc-income[\s\S]*permission:\s*'treasury\.journals'/);
});
