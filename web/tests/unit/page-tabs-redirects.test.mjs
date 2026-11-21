import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import {
  PAYROLL_TABS,
  DEBTS_TABS,
  TREASURY_TABS,
  ACCOUNTING_TABS,
  ADMIN_TABS,
  OPERATIONS_TABS,
  resolvePayrollTab,
  resolveDebtsTab,
  resolveTreasuryTab,
  resolveAccountingTab,
  resolveAdminTab,
  resolveOperationsTab,
  buildTabRedirectUrl,
  getBreadcrumbTabLabel,
} from '../../src/lib/page-tabs.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');

test('every Sidebar ?tab= href maps to a canonical non-legacy tab', () => {
  const resolvers = {
    '/payroll/': { tabs: PAYROLL_TABS, resolve: resolvePayrollTab },
    '/debts/': { tabs: DEBTS_TABS, resolve: resolveDebtsTab },
    '/treasury/': { tabs: TREASURY_TABS, resolve: resolveTreasuryTab },
    '/accounting/': { tabs: ACCOUNTING_TABS, resolve: resolveAccountingTab },
    '/admin/': { tabs: ADMIN_TABS, resolve: resolveAdminTab },
    '/operations/': { tabs: OPERATIONS_TABS, resolve: resolveOperationsTab },
  };
  const hrefs = [...sidebar.matchAll(/(?:href|defaultHref):\s*'([^']+)'/g)].map((m) => m[1]);
  for (const [prefix, { tabs, resolve }] of Object.entries(resolvers)) {
    const matched = hrefs.filter((h) => h.startsWith(prefix) && /[?&]tab=/.test(h));
    // W3 path-based sections (payroll/debts) may have no ?tab= links.
    if (matched.length === 0) continue;
    for (const href of matched) {
      const u = new URL(href, 'https://gate.local');
      const tab = u.searchParams.get('tab');
      assert.ok(tab, `sidebar link ${href} must include canonical ?tab=`);
      const res = resolve(tab);
      assert.equal(res.legacy, false, `${href} should be canonical, got legacy`);
      assert.equal(res.tab, tab);
      assert.ok(tabs.includes(tab), `${href} tab=${tab} missing from tabs for ${prefix}`);
    }
  }
});

test('payroll legacy ?tab= values map and preserve query params', () => {
  assert.deepEqual(resolvePayrollTab(null), { tab: 'platforms', legacy: false });
  assert.deepEqual(resolvePayrollTab('current_cycle'), { tab: 'current-cycle', extraParams: undefined, legacy: true });
  assert.deepEqual(resolvePayrollTab('apps'), { tab: 'platforms', extraParams: undefined, legacy: true });
  assert.deepEqual(resolvePayrollTab('keeta_upload'), {
    tab: 'platforms',
    extraParams: { app: 'keeta', sub: 'settlements' },
    legacy: true,
  });
  assert.equal(
    buildTabRedirectUrl('/payroll/', 'tab=keeta_upload&period=2026-10', resolvePayrollTab('keeta_upload')),
    '/payroll/?tab=platforms&period=2026-10&app=keeta&sub=settlements'
  );
  assert.equal(
    buildTabRedirectUrl('/payroll/', 'tab=unknown&period=2026-09', resolvePayrollTab('unknown')),
    '/payroll/?tab=platforms&period=2026-09'
  );
});

test('debts, treasury, accounting, admin legacy ?tab= redirects preserve period and deep-link params', () => {
  assert.equal(
    buildTabRedirectUrl('/debts/', 'tab=statements&period=2026-10', resolveDebtsTab('statements')),
    '/debts/?tab=statement&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/debts/', 'tab=advances&action=new&period=2026-10', resolveDebtsTab('advances')),
    '/debts/?tab=advance_requests&action=new&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/treasury/', 'tab=bank&period=2026-10', resolveTreasuryTab('bank')),
    '/treasury/?tab=bank_file&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/treasury/', 'tab=payouts', resolveTreasuryTab('payouts')),
    '/treasury/?tab=advance_payouts'
  );
  assert.equal(
    buildTabRedirectUrl('/accounting/', 'tab=trial&period=2026-10', resolveAccountingTab('trial')),
    '/accounting/?tab=trial_balance&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/accounting/', 'tab=income&period=2026-10', resolveAccountingTab('income')),
    '/accounting/?tab=income_statement&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/admin/', 'tab=audit_log&period=2026-10', resolveAdminTab('audit_log')),
    '/admin/?tab=audit&period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/admin/', 'tab=roles', resolveAdminTab('roles')),
    '/admin/?tab=users'
  );
});

test('operations legacy ?tab= redirects keep cross-page destinations and preserve period', () => {
  assert.equal(
    buildTabRedirectUrl('/operations/', 'tab=cases&period=2026-10', resolveOperationsTab('cases')),
    '/workforce/?period=2026-10&tab=cases'
  );
  assert.equal(
    buildTabRedirectUrl('/operations/', 'tab=accounts&period=2026-10', resolveOperationsTab('accounts')),
    '/workforce/?period=2026-10'
  );
  assert.equal(
    buildTabRedirectUrl('/operations/', 'tab=custody&period=2026-10', resolveOperationsTab('custody')),
    '/operations/?tab=vehicles&period=2026-10'
  );
});

test('breadcrumbs return Arabic labels for canonical and legacy tabs across sections', () => {
  assert.equal(getBreadcrumbTabLabel('accounting', 'trial_balance'), 'ميزان المراجعة');
  assert.equal(getBreadcrumbTabLabel('accounting', 'trial'), 'ميزان المراجعة');
  assert.equal(getBreadcrumbTabLabel('payroll', 'current-cycle'), 'الدورة الحالية (النظام القائم)');
  assert.equal(getBreadcrumbTabLabel('debts', 'statements'), 'كشف حساب المندوب');
  assert.equal(getBreadcrumbTabLabel('treasury', 'bank_file'), 'ملف صرف الرواتب البنكي');
  assert.equal(getBreadcrumbTabLabel('admin', 'bypass'), 'تجاوز الاعتماد');
});
