import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const sidebar = read('src/components/layout/Sidebar.tsx');
const tabs = read('src/components/ui/SectionTabs.tsx');
const payroll = read('src/app/payroll/page.tsx');
const debts = read('src/app/debts/page.tsx');
const treasury = read('src/app/treasury/page.tsx');
const accounting = read('src/app/accounting/page.tsx');
const admin = read('src/app/admin/page.tsx');

test('sidebar includes every section tab including current-cycle and former top-bar tabs', () => {
  assert.match(sidebar, /ninja_upload/);
  assert.match(sidebar, /رفع فاتورة نينجا/);
  assert.match(sidebar, /current-cycle/);
  assert.match(sidebar, /tab=actions/);
  assert.match(sidebar, /تحصيل وإنشاء/);
  assert.match(sidebar, /manual_journals/);
  assert.match(sidebar, /القيود اليدوية/);
  assert.match(sidebar, /tab=lists/);
  assert.match(sidebar, /tab=bypass/);
});

test('SectionTabs scrolls active tab into view and keeps role=tablist', () => {
  assert.match(tabs, /role="tablist"/);
  assert.match(tabs, /scrollIntoView/);
  assert.match(tabs, /scrollbar-width:thin/);
});

test('rule 3.0: sidebar-split pages render no duplicate top-level tab bars', () => {
  assert.doesNotMatch(payroll, /<SectionTabs\b/);
  assert.doesNotMatch(payroll, /sharedSectionTabs/);
  assert.doesNotMatch(debts, /id=\{`tab-\$\{tab\.id\}`\}/);
  assert.doesNotMatch(treasury, /id=\{`tab-\$\{tab\.id\}`\}/);
  assert.doesNotMatch(accounting, /role="tablist"/);
  assert.doesNotMatch(admin, /<SectionTabs\b/);
});
