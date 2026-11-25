import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { isNavHrefActive } = await import('../../src/lib/nav-active.ts');

const payrollSiblings = [
  '/payroll/?tab=platforms&app=keeta',
  '/payroll/?tab=platforms&app=hungerstation',
  '/payroll/?tab=platforms&app=ninja',
  '/payroll/?tab=platforms&app=chefz',
  '/payroll/?tab=platforms&app=jahez',
  '/payroll/?tab=platforms',
  '/payroll/?tab=bank',
  '/payroll/?tab=deductions',
  '/payroll/?tab=rules',
];

const debtsSiblings = ['/debts/?tab=debts', '/debts/?tab=promissory'];

test('T9: only the selected payroll app is active; other apps stay inactive', () => {
  const opts = { siblingHrefs: payrollSiblings };
  assert.equal(
    isNavHrefActive('/payroll/?tab=platforms&app=ninja', '/payroll/', 'tab=platforms&app=ninja', opts),
    true
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=platforms&app=keeta', '/payroll/', 'tab=platforms&app=ninja', opts),
    false
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=platforms', '/payroll/', 'tab=platforms&app=ninja', opts),
    false,
    'mudad/platforms without app must not highlight when an app is selected'
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=bank', '/payroll/', 'tab=platforms&app=ninja', opts),
    false
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=bank', '/payroll/', 'tab=bank', opts),
    true
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=deductions', '/payroll/', 'tab=deductions', opts),
    true
  );
  assert.equal(
    isNavHrefActive('/payroll/?tab=rules', '/payroll/', 'tab=rules', opts),
    true
  );
});

test('T8: debts register vs promissory archive tabs are exclusive', () => {
  const opts = { siblingHrefs: debtsSiblings };
  assert.equal(isNavHrefActive('/debts/?tab=debts', '/debts/', 'tab=debts', opts), true);
  assert.equal(isNavHrefActive('/debts/?tab=promissory', '/debts/', 'tab=debts', opts), false);
  assert.equal(isNavHrefActive('/debts/?tab=debts', '/debts/', 'tab=promissory', opts), false);
  assert.equal(isNavHrefActive('/debts/?tab=promissory', '/debts/', 'tab=promissory', opts), true);
});

test('sidebar wires isNavHrefActive and debts use ?tab= keys', () => {
  const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
  assert.match(sidebar, /isNavHrefActive/);
  assert.match(sidebar, /\/debts\/\?tab=debts/);
  assert.match(sidebar, /\/debts\/\?tab=promissory/);
});
