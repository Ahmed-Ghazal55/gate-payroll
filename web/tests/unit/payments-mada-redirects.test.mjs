import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {
  resolveTreasuryTab,
  resolveAccountingTab,
  buildTabRedirectUrl,
  REMOVED_TAB_TOASTS,
} from '../../src/lib/page-tabs.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
const workspace = readFileSync(
  path.join(root, 'src/features/payments/PaymentsWalletWorkspace.tsx'),
  'utf8'
);

test('removed extras and adjustments redirect to Payments / manual journals', () => {
  assert.equal(
    buildTabRedirectUrl('/treasury/', 'tab=extras', resolveTreasuryTab('extras')),
    '/payments/mada'
  );
  assert.equal(
    buildTabRedirectUrl('/accounting/', 'tab=adjustments', resolveAccountingTab('adjustments')),
    '/accounting/?tab=manual_journals'
  );
  assert.match(REMOVED_TAB_TOASTS.extras, /مدا/);
  assert.match(REMOVED_TAB_TOASTS.adjustments, /تسويات/);
});

test('payments screens and sidebar entries exist for mada fuel prepaid', () => {
  assert.ok(existsSync(path.join(root, 'src/app/payments/mada/page.tsx')));
  assert.ok(existsSync(path.join(root, 'src/app/payments/fuel/page.tsx')));
  assert.ok(existsSync(path.join(root, 'src/app/payments/prepaid-wallets/page.tsx')));
  assert.match(sidebar, /\/payments\/mada/);
  assert.match(sidebar, /\/payments\/fuel/);
  assert.match(sidebar, /\/payments\/prepaid-wallets/);
  assert.doesNotMatch(sidebar, /treasury-extras|acc-adjustments|tab=extras|tab=adjustments/);
  assert.match(workspace, /listMadaWallets|createMadaTopup|reconcileMada|issueMadaCard/);
  assert.match(workspace, /allowedActions/);
  assert.match(workspace, /formatSar/);
});
