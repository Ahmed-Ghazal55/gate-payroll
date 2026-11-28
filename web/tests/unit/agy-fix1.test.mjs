import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

test('AGYFIX1: DataTable separates text-end and text-start on numeric cells', () => {
  const dt = read('src/components/ui/DataTable.tsx');
  assert.match(dt, /isNumeric \? 'text-end' : 'text-start'/);
  assert.match(dt, /isNumeric \? 'text-end num' : 'text-start'/);
});

test('AGYFIX1: workforce legal tab renders empty state for no-source message instead of ErrorState', () => {
  const wf = read('src/app/workforce/page.tsx');
  assert.match(wf, /setLegalMessage/);
  assert.match(wf, /!loadingCases && legalItems\.length === 0/);
  assert.match(wf, /<EmptyState/);
});

test('AGYFIX1: debts non-default tabs do not keep loading=true stuck', () => {
  const debts = read('src/app/debts/page.tsx');
  assert.match(debts, /useState<boolean>\(false\)/);
  assert.match(debts, /if \(!kinds\.some\(/);
});

test('AGYFIX1: sidebar pr-mudad routes to ?tab=mudad and chefz pill id matches ?app=chefz', () => {
  const sidebar = read('src/components/layout/Sidebar.tsx');
  const payroll = read('src/app/payroll/page.tsx');
  const tabs = read('src/lib/page-tabs.ts');
  assert.match(sidebar, /id: 'pr-mudad', href: '\/payroll\/\?tab=mudad'/);
  assert.match(tabs, /'mudad'/);
  assert.match(payroll, /mainTab === 'mudad'/);
  assert.match(payroll, /id: 'chefz'/);
});

test('AGYFIX1: DebtActionsPanel and VaultCountsPanel use Arabic labels instead of raw field keys', () => {
  const debtActions = read('src/features/debts/DebtActionsPanel.tsx');
  const vaultCounts = read('src/features/treasury/VaultCountsPanel.tsx');
  assert.doesNotMatch(debtActions, /placeholder=\{k\}/);
  assert.doesNotMatch(debtActions, /placeholder="debtId"/);
  assert.match(debtActions, /رقم الإقامة/);
  assert.match(debtActions, /فئة المديونية/);
  assert.doesNotMatch(vaultCounts, /placeholder=\{k\}/);
  assert.match(vaultCounts, /المبلغ المعدود فعليًا/);
});

test('AGYFIX1: accounting 5 extended tabs and amazon route no longer render ScreenShell stubs', () => {
  const acc = read('src/app/accounting/page.tsx');
  const amazon = read('src/app/payroll/amazon/page.tsx');
  assert.doesNotMatch(acc, /ScreenShell/);
  assert.match(acc, /GeneralLedgerPanel/);
  assert.match(acc, /BalanceSheetPanel/);
  assert.match(acc, /CostCentresPanel/);
  assert.match(acc, /AppProfitabilityPanel/);
  assert.match(acc, /AccountingReportsPanel/);
  assert.match(amazon, /redirect\('\/payroll\/\?tab=platforms&app=amazon'\)/);
});
