import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');

test('sidebar items inherit group permission when item.permission is omitted', () => {
  assert.match(sidebar, /item\.permission \?\? group\.permission/);
});

test('approvals and rider statement carry view permissions', () => {
  assert.match(sidebar, /approvals-main[\s\S]*\/approvals\//);
  assert.match(sidebar, /rider-statement[\s\S]*permission:\s*\['pay\.view',\s*'payroll\.view',\s*'debts\.view'\]/);
});

test('admin audit tab uses audit view permission', () => {
  assert.match(sidebar, /ad-audit[\s\S]*permission:\s*'admin\.audit\.view'/);
});

test('accounting group is permission-gated', () => {
  assert.match(sidebar, /id: 'accounting'[\s\S]*permission:\s*'accounting\.chart\.view'/);
  assert.match(sidebar, /acc-coa[\s\S]*\/accounting\/\?tab=coa/);
  assert.match(sidebar, /acc-tb[\s\S]*\/accounting\/\?tab=trial_balance/);
});
