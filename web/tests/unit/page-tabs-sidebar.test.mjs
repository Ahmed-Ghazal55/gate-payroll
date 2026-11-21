import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
const tabs = readFileSync(path.join(root, 'src/components/ui/SectionTabs.tsx'), 'utf8');
const admin = readFileSync(path.join(root, 'src/app/admin/page.tsx'), 'utf8');
const accounting = readFileSync(path.join(root, 'src/app/accounting/page.tsx'), 'utf8');

test('sidebar lists accounting and admin sub-pages instead of duplicate top bars', () => {
  assert.match(sidebar, /manual_journals/);
  assert.match(sidebar, /القيود اليدوية/);
  assert.match(sidebar, /tab=lists/);
  assert.match(sidebar, /tab=bypass/);
  assert.match(sidebar, /\/payroll\/ninja/);
  assert.match(sidebar, /\/approvals\//);
});

test('SectionTabs keeps role=tablist for in-page tabs', () => {
  assert.match(tabs, /role="tablist"/);
  assert.match(tabs, /aria-selected/);
});

test('admin page does not duplicate sidebar tabs as a top SectionTabs bar', () => {
  assert.doesNotMatch(admin, /SectionTabs tabs=\{sectionTabs\}/);
  assert.match(admin, /page-tabs-rule/);
});

test('accounting coa tab uses CoaManagePanel', () => {
  assert.match(accounting, /CoaManagePanel/);
  assert.match(accounting, /balance_sheet|general_ledger/);
});
