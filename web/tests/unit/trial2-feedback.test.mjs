import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const sidebar = read('src/components/layout/Sidebar.tsx');
const sectionTabs = read('src/components/ui/SectionTabs.tsx');
const coa = read('src/features/accounting/CoaManagePanel.tsx');
const configLists = read('src/features/admin/ConfigListsPanel.tsx');
const adminPage = read('src/app/admin/page.tsx');
const madaWallet = read('src/features/payments/PaymentsWalletWorkspace.tsx');

function collectMatches(text, re) {
  const out = [];
  let m;
  const copy = new RegExp(re.source, re.flags.includes('g') ? re.flags : `${re.flags}g`);
  while ((m = copy.exec(text))) out.push(m[1]);
  return out;
}

test('T1: sidebar payments group appears once; group labels and hrefs are unique', () => {
  const paymentsLabels = (sidebar.match(/label:\s*'المدفوعات والمشتريات'/g) || []).length;
  assert.equal(paymentsLabels, 1, 'duplicate payments sidebar group after union merge');

  const paymentsIds = (sidebar.match(/id:\s*'payments'/g) || []).length;
  assert.equal(paymentsIds, 1, 'payments group id must appear once in buildNavGroups');

  // Group ids inside buildNavGroups return [ ... ]
  const buildStart = sidebar.indexOf('export function buildNavGroups');
  const buildBody = sidebar.slice(buildStart, sidebar.indexOf('export function Sidebar'));
  const groupIds = collectMatches(buildBody, /^\s*id:\s*'([^']+)'/gm).filter((id, idx, arr) => {
    // top-level groups are followed soon by label: — keep first id of each `{ id, label` block by
    // taking ids that have a following `label:` before the next `items:`
    return true;
  });
  // Prefer explicit group labels: each `label:` immediately after a group `id:` at indent of object root
  const groupLabels = [];
  const groupIdList = [];
  const groupBlockRe = /\{\s*\n\s*id:\s*'([^']+)',\s*\n\s*label:\s*'([^']+)',/g;
  let gm;
  while ((gm = groupBlockRe.exec(buildBody))) {
    groupIdList.push(gm[1]);
    groupLabels.push(gm[2]);
  }
  assert.ok(groupIdList.length >= 5, 'expected several nav groups');
  assert.equal(new Set(groupIdList).size, groupIdList.length, `duplicate group ids: ${groupIdList.join(',')}`);
  assert.equal(new Set(groupLabels).size, groupLabels.length, `duplicate group labels: ${groupLabels.join(',')}`);

  const hrefs = collectMatches(buildBody, /href:\s*'([^']+)'/g);
  assert.ok(hrefs.length > 10, 'expected many sidebar hrefs');
  assert.equal(new Set(hrefs).size, hrefs.length, `duplicate sidebar hrefs: ${hrefs.filter((h, i) => hrefs.indexOf(h) !== i).join(',')}`);
});

test('T2: CoA / SectionTabs tab bar has no vertical scrollbar (overflow-y hidden, no-scrollbar)', () => {
  assert.match(sectionTabs, /overflow-y-hidden/, 'SectionTabs must hide vertical overflow');
  assert.match(sectionTabs, /no-scrollbar/, 'SectionTabs must not show scrollbar arrows');
  assert.match(coa, /SectionTabs/, 'CoA manage panel uses SectionTabs');
  assert.match(
    coa,
    /className=\{?["'`][^"'`]*flex-wrap|SectionTabs[^>]*className=["'`][^"'`]*flex-wrap|className=["'`][^"'`]*overflow-hidden/,
    'CoA tab bar wraps or hides overflow so three tabs need no scrollbar'
  );
});

test('T3: Mada recharge lists leave General lists and live under Mada settings', () => {
  assert.match(configLists, /includeGroups|excludeGroups|MADA_RECHARGE|recharge_networks/, 'ConfigListsPanel must support group filtering');
  assert.match(adminPage, /ConfigListsPanel/, 'admin still hosts general lists');
  assert.match(adminPage, /excludeGroups=\{MADA_RECHARGE_LIST_GROUPS\}/, 'admin General lists exclude Mada recharge groups');
  assert.match(madaWallet, /ConfigListsPanel/, 'Mada settings hosts list management');
  assert.match(madaWallet, /includeGroups=\{MADA_RECHARGE_LIST_GROUPS\}/, 'Mada settings includes recharge groups');
  assert.match(configLists, /MADA_RECHARGE_LIST_GROUPS\s*=\s*\[[^\]]*recharge_networks[^\]]*recharge_packages/, 'shared Mada group ids');
  // Same endpoints / permissions for list edits
  assert.match(configLists, /pay\.manage/);
  assert.match(configLists, /createConfigListItem|approveConfigListItem/);
});

const configListsCs = read('../Payroll.Api/ConfigLists.cs');
const adminSettings = read('src/features/admin/AdminSettingsPanel.tsx');

test('T4: operating_systems seed is 2 parents + 6 leaves; old codes resolve', () => {
  for (const code of [
    'kafala.own_vehicle',
    'kafala.company_car',
    'kafala.company_bike',
    'external.own_vehicle',
    'external.company_car',
    'external.company_bike',
  ]) {
    assert.match(configListsCs, new RegExp(code.replace('.', '\\.')), `missing leaf ${code}`);
  }
  assert.match(configListsCs, /"external"|'external'/, 'external parent');
  assert.match(configListsCs, /ResolveOperating|CanonicalOperating|operatingSystemAlias|mapsTo|own_car/, 'old codes resolve via alias/canonical');
  assert.match(configListsCs, /own_car/, 'own_car kept as alias');
  assert.match(configListsCs, /bike/, 'bike kept as alias');
  assert.match(configLists, /operating_systems/, 'ConfigListsPanel handles operating_systems');
  assert.match(configLists, /kind === 'parent'|values\?\.kind === 'parent'/, 'UI groups by parent rider type');
  assert.match(configLists, /└|ps-6|grouped|شجرة/, 'UI shows tree indentation for leaves');
});

test('T5: staff reply deadline label moved to notifications settings', () => {
  assert.match(adminSettings, /مهلة رد الموظفين على المهام والطلبات \(أيام عمل\)/);
  assert.match(adminSettings, /لا تخص المناديب ولا الرواتب/);
  assert.match(adminSettings, /إعدادات الإشعارات/);
  assert.match(adminSettings, /بيانات الشركة(?! والعمل)/);
  assert.doesNotMatch(adminSettings, /أيام العمل لمهلة التصعيد/);
  assert.match(adminSettings, /reply_working_days/, 'same setting key');
});

const coaApi = read('../Payroll.Api/ChartOfAccountsApi.cs');
const coaTests = read('../tests/Engine.Tests/ChartOfAccountsApiTests.cs');
const archivePanel = read('src/features/admin/ArchivePanel.tsx');
const apiTs = read('src/lib/api.ts');

test('T6: CoA List disposes reader before Load; SQL Server HTTP list test exists', () => {
  assert.match(coaApi, /Materialize codes and dispose the reader before Load/);
  const listFn = coaApi.slice(coaApi.indexOf('public static JsonObject List'));
  const usingBlock = listFn.match(/using\s*\(\s*var\s+\w+\s*=\s*\w+\.ExecuteReader\(\)\s*\)[\s\S]*?while[\s\S]*?codes\.Add/);
  assert.ok(usingBlock, 'codes reader must be inside a using block that ends before foreach Load');
  assert.match(coaTests, /ListActiveAndInactiveThroughHttp_WithAtLeastTwoAccounts/);
  assert.match(coaTests, /inactive=true/);
});

test('T7: archive admin tab uses ArchivePanel with real archive endpoints', () => {
  assert.match(adminPage, /activeTab === 'archive' && <ArchivePanel \/>/);
  assert.doesNotMatch(adminPage, /activeTab === 'archive' && \(\s*<ScreenShell/);
  assert.match(archivePanel, /getArchivePolicies|requestArchiveRestore/);
  assert.match(archivePanel, /سياسات الاحتفاظ/);
  assert.match(archivePanel, /طلب استرجاع/);
  assert.match(apiTs, /getArchivePolicies/);
  assert.match(apiTs, /\/admin\/archive\/policies/);
  assert.match(apiTs, /\/admin\/archive\/restore-request/);
});

const debtsPage = read('src/app/debts/page.tsx');
const navActive = read('src/lib/nav-active.ts');

test('T8: debts sidebar children use canonical ?tab=; KPI cards show 0 not em dash', () => {
  assert.match(sidebar, /\/debts\/\?tab=debts/);
  assert.match(sidebar, /\/debts\/\?tab=promissory/);
  assert.match(debtsPage, /formatSar\(debtsSummary \? totalRemainingDebts : 0\)/);
  assert.match(debtsPage, /formatSar\(debtsSummary \? totalAdvances : 0\)/);
  assert.match(debtsPage, /String\(advancesLoaded \? pendingAdvancesCount : 0\)/);
  assert.match(debtsPage, /String\(approvalsLoaded \? pendingApprovalsCount : 0\)/);
});

test('T9: sidebar active matching uses isNavHrefActive with tab and app query', () => {
  assert.match(sidebar, /isNavHrefActive/);
  assert.match(navActive, /export function isNavHrefActive/);
  assert.match(navActive, /app/);
  assert.match(navActive, /tab/);
});

const payAppFlags = read('src/lib/pay-app-flags.ts');
const payrollPage = read('src/app/payroll/page.tsx');
const payPlatforms = read('../Payroll.Api/PayPlatforms.cs');
const hsParse = read('../Payroll.Api/HungerStationInvoices.Parse.cs');
const commissionQuery = read('../Payroll.Sync/PlatformCommissionLiveQuery.cs');
const paySourcesApi = read('../Payroll.Api/PaySourcesLiveApi.cs');

test('T10: PENDING Ghazal — parent terms keep/hide decision; no silent change required', () => {
  // Documented wait: do not remove parent_terms until Ghazal answers.
  assert.match(payrollPage, /parent_terms/);
});

test('T11: eligibility hidden for jahez/chefz/hs via per-app flag', () => {
  assert.match(payAppFlags, /export function payAppHasEligibility/);
  assert.match(payAppFlags, /jahez/);
  assert.match(payAppFlags, /chefz/);
  assert.match(payAppFlags, /hs/);
  assert.match(payPlatforms, /HasEligibility/);
  assert.match(payPlatforms, /false\),\s*\n\s*new\("keeta"/);
  assert.match(payrollPage, /payAppHasEligibility\(appSlug\)/);
  assert.match(payrollPage, /الأهلية والضوابط/);
});

test('T11b: HS invoice promotes segment/rating as invoice facts', () => {
  assert.match(hsParse, /riderCategory/);
  assert.match(hsParse, /riderRating/);
  assert.match(hsParse, /invoiceFacts/);
  assert.match(hsParse, /hs_invoice/);
  assert.match(payrollPage, /riderCategory|فئة المندوب|تقييم/);
});

test('T12/T13: Jahez/Chefz internal schemes read Omar PlatformDailyCommissionRates', () => {
  assert.match(commissionQuery, /PlatformDailyCommissionRates/);
  assert.match(commissionQuery, /ProgramJahez/);
  assert.match(commissionQuery, /ProgramChefzFullTime/);
  assert.match(commissionQuery, /غير متوفر في نظام عمر/);
  assert.match(paySourcesApi, /omar-commission-rates/);
  assert.match(apiTs, /getOmarCommissionRates/);
  assert.match(payrollPage, /getOmarCommissionRates|omarCommission/);
});
