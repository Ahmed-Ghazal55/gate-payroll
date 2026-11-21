import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
const tabs = readFileSync(path.join(root, 'src/components/ui/SectionTabs.tsx'), 'utf8');
const accounting = readFileSync(path.join(root, 'src/app/accounting/page.tsx'), 'utf8');

test('sidebar includes tabs that were top-bar-only', () => {
  assert.match(sidebar, /ninja_upload/);
  assert.match(sidebar, /رفع فاتورة نينجا/);
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

test('accounting tab buttons sit inside role=tablist', () => {
  assert.match(accounting, /role="tablist"/);
});
