import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const sidebar = read('src/components/layout/Sidebar.tsx');
const tabs = read('src/components/ui/SectionTabs.tsx');
const accounting = read('src/app/accounting/page.tsx');
const admin = read('src/app/admin/page.tsx');
const operations = read('src/app/operations/page.tsx');

test('W3 sidebar keeps approvals, accounting tabs, and admin bypass/settings', () => {
  assert.match(sidebar, /\/approvals\//);
  assert.match(sidebar, /manual_journals/);
  assert.match(sidebar, /القيود اليدوية/);
  assert.match(sidebar, /tab=bypass/);
  assert.match(sidebar, /tab=settings/);
  assert.match(sidebar, /tab=coa/);
});

test('SectionTabs scrolls active tab into view and keeps role=tablist', () => {
  assert.match(tabs, /role="tablist"/);
  assert.match(tabs, /scrollIntoView/);
});

test('rule 3.0: accounting and operations have no duplicate top-level tablist', () => {
  assert.doesNotMatch(accounting, /role="tablist"/);
  assert.doesNotMatch(operations, /role="tablist"/);
});
