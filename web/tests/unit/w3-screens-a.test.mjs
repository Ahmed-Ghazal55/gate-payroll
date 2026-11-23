import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
const header = readFileSync(path.join(root, 'src/components/layout/Header.tsx'), 'utf8');
const approvals = readFileSync(path.join(root, 'src/app/approvals/page.tsx'), 'utf8');
const admin = readFileSync(path.join(root, 'src/app/admin/page.tsx'), 'utf8');
const coa = readFileSync(path.join(root, 'src/features/accounting/CoaManagePanel.tsx'), 'utf8');
const sim = readFileSync(path.join(root, 'src/components/payroll/SimulationBanner.tsx'), 'utf8');
const platformPay = readFileSync(path.join(root, 'src/features/payroll/PlatformPayrollPage.tsx'), 'utf8');
const payrollPage = readFileSync(path.join(root, 'src/app/payroll/page.tsx'), 'utf8');
const keetaPage = readFileSync(path.join(root, 'src/app/payroll/keeta/page.tsx'), 'utf8');
const money = readFileSync(path.join(root, 'src/lib/money.ts'), 'utf8');
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');
const payReview = readFileSync(path.join(root, '../Payroll.Api/PayReviewDraft.cs'), 'utf8');
const browser = readFileSync(path.join(root, '../tests/browser.mjs'), 'utf8');
const riderApp = readFileSync(path.join(root, 'src/app/rider-app/page.tsx'), 'utf8');
const salSims = readFileSync(path.join(root, '../Payroll.Api/SalSimulations.cs'), 'utf8');

function appRoutes() {
  const routes = new Set();
  const appRoot = path.join(root, 'src/app');
  function walk(dir, base = '') {
    for (const name of readdirSync(dir)) {
      const full = path.join(dir, name);
      if (statSync(full).isDirectory()) walk(full, `${base}/${name}`);
      else if (name === 'page.tsx' || name === 'page.ts') {
        routes.add(((base || '/') + '/').replace(/\/+/g, '/'));
      }
    }
  }
  walk(appRoot);
  return routes;
}

function hrefsFromSidebar(text) {
  const hrefs = [];
  const re = /href:\s*'([^']+)'/g;
  let m;
  while ((m = re.exec(text))) hrefs.push(m[1]);
  return hrefs;
}

test('U-W3-01 sidebar has no import/sync entries and includes approvals/accounting tabs', () => {
  assert.doesNotMatch(sidebar, /مركز الاستيراد|ErpSync|\/imports\/|المزامنة/);
  assert.match(sidebar, /\/approvals\//);
  assert.match(sidebar, /\/workforce\//);
  assert.match(sidebar, /\/accounting\/\?tab=coa/);
  assert.match(sidebar, /\/accounting\/\?tab=balance_sheet/);
  assert.match(sidebar, /\/admin\/\?tab=bypass/);
  assert.match(sidebar, /\/admin\/\?tab=settings/);
  assert.match(header, /href="\/approvals\/"/);
  assert.doesNotMatch(header, /مركز الاستيراد|تحديث البيانات من ERP/);
});

test('U-W3-01 every Sidebar href resolves to an app route', () => {
  const routes = appRoutes();
  for (const href of hrefsFromSidebar(sidebar)) {
    if (href.includes('amazon')) continue; // disabled later badge
    const pathOnly = href.split('?')[0];
    const normalized = pathOnly.endsWith('/') ? pathOnly : pathOnly + '/';
    const ok =
      routes.has(normalized) ||
      routes.has(pathOnly) ||
      existsSync(path.join(root, 'src/app', pathOnly.replace(/^\//, ''), 'page.tsx'));
    assert.ok(ok, `missing route for sidebar href ${href}`);
  }
});

test('U-W3-12 approvals buttons call decide endpoint and honor allowedActions', () => {
  assert.match(approvals, /api\.decideApproval/);
  assert.match(approvals, /allowedActions/);
  assert.match(approvals, /approve|reject|suspend/);
  assert.match(approvals, /شرط الشخصين/);
  assert.match(approvals, /MAKER_CHECKER_AR/);
  assert.match(approvals, /EmptyState/);
  assert.match(approvals, /formatSar/);
  assert.match(api, /\/approvals\/\$\{/);
});

test('U-W3-18/24 admin settings save and read summary use real endpoints', () => {
  assert.match(admin, /AdminSettingsPanel/);
  assert.match(admin, /AdminReadSummaryPanel/);
  assert.match(api, /\/admin\/settings/);
  assert.match(api, /\/admin\/read-summary/);
  assert.match(api, /\/admin\/audit\/self-approvals/);
});

test('U-W3-14 CoA buttons call create/edit/deactivate/reactivate', () => {
  assert.match(coa, /createFinanceCoa/);
  assert.match(coa, /updateFinanceCoa/);
  assert.match(coa, /deactivateFinanceCoa/);
  assert.match(coa, /reactivateFinanceCoa/);
  assert.match(coa, /allowedActions/);
  assert.match(coa, /حساب جديد/);
});

test('U-W3-21 simulation banner discard/promote use allowedActions and Arabic copy', () => {
  assert.match(sim, /discard_simulation/);
  assert.match(sim, /promote_simulation/);
  assert.match(sim, /discardSimulation|promoteSimulation/);
  assert.match(sim, /وضع المحاكاة \(بيانات حية — بدون ترحيل\)/);
  assert.match(sim, /غير متاح في وضع المحاكاة/);
  assert.match(api, /createSimulation/);
  assert.match(api, /\/sal\/simulations['"`]/);
  assert.match(api, /\/sal\/simulations\//);
  assert.match(salSims, /MapPost\("\/sal\/simulations"/);
  assert.match(payrollPage, /api\.createSimulation/);
  assert.match(payrollPage, /SimulationBanner/);
  assert.doesNotMatch(payrollPage, /sim-\$\{|sim-\$\{app\}/);
  assert.doesNotMatch(platformPay, /مسار الشاشة الجانبي جاهز|sim-\$\{/);
  assert.match(keetaPage, /redirect\(/);
  assert.doesNotMatch(sidebar, /href:\s*'\/payroll\/keeta'/);
  assert.match(sidebar, /\/payroll\/\?tab=platforms&app=keeta/);
});

test('money formatter and follow-ups', () => {
  assert.match(money, /formatSar/);
  assert.match(money, /ر\.س/);
  assert.match(payReview, /شروط الأجر/);
  assert.doesNotMatch(payReview, /غير معتمد: "\+kind/);
  assert.match(browser, /package_web\.py/);
  assert.match(browser, /assert\.ok\(packaged/);
  const periodSrc = readFileSync(path.join(root, 'src/lib/period.ts'), 'utf8');
  assert.match(periodSrc, /currentYearMonth|new Date/);
  assert.match(riderApp, /StatementsPanel/);
});
