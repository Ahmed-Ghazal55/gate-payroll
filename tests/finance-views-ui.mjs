import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

const {chromium} = await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs'));
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const data = mkdtempSync(path.join(tmpdir(), 'gate-finance-views-ui-'));
const out = path.join(root, 'test-results');
mkdirSync(out, {recursive: true});

const base = 'http://127.0.0.1:19462';
const server = spawn((process.env.DOTNET_PATH || 'dotnet'), [path.join(root, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')], {
  cwd: path.join(root, 'Payroll.Api'),
  env: {...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: base, PAYROLL_DATA_DIR: data},
  windowsHide: true,
  stdio: 'ignore'
});

let spawnError;
server.once('error', e => { spawnError = e; });
let browser;

try {
  let ready = false;
  for (let n = 0; n < 150; n++) {
    if (spawnError) throw spawnError;
    if (server.exitCode !== null) throw Error('Server exited');
    try {
      if ((await fetch(base + '/health')).ok) {
        ready = true;
        break;
      }
    } catch {}
    await new Promise(r => setTimeout(r, 100));
  }
  assert.ok(ready, 'Server did not start');

  browser = await chromium.launch({headless: true});
  const context = await browser.newContext({viewport: {width: 1440, height: 1000}});
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));

  // 1. Initial setup
  const password = randomBytes(24).toString('hex');
  await page.goto(base);
  await page.locator('[name=username]').fill('finance-views-tester');
  await page.locator('[name=password]').fill(password);
  await page.locator('#auth-form button').click();
  await page.getByRole('heading', {name: 'كل مستحق في مكانه'}).waitFor();

  // Helper for API setup
  async function apiPost(route, body) {
    const res = await context.request.post(base + route, {data: body});
    const json = await res.json().catch(() => ({}));
    assert.ok(res.ok(), `${route} failed: ${JSON.stringify(json)}`);
    return json;
  }

  // Seed sample financial data for 2026-10
  const period = '2026-10';
  const body = '80' + '0'.repeat(15) + '123';
  let remainder = 0;
  for (const d of body + '281000') remainder = (remainder * 10 + Number(d)) % 97;
  const iban = 'SA' + String(98 - remainder).padStart(2, '0') + body;
  const iqama = '2000000888';

  const policy = await apiPost('/api/policies', {
    name: 'Test Commission Policy',
    kind: 'commission',
    platform: 'all',
    effectiveFrom: period,
    config: {companyFee: 500}
  });
  await apiPost(`/api/policies/${policy.id}/approve`, {});

  await apiPost('/api/riders', {
    iqama,
    name: 'Ahmed Test Rider',
    financeSponsorId: 'BW',
    policyId: policy.id,
    iban
  });

  const csv = `sourceRef,iqama,name,accountId,from,to,orders,platformRevenue,isValid\r\nsynthetic-work,${iqama},Ahmed Test Rider,0000000000000001,2026-10-01,2026-10-31,600,1500,true\r\n`;
  const upload = await apiPost('/api/imports/inspect', {
    filename: 'synthetic.csv',
    dataBase64: Buffer.from(csv).toString('base64')
  });

  const importSpec = {
    sheet: 'CSV',
    period,
    platform: 'Keeta',
    headerRow: 1,
    confirmActualRider: true,
    mapping: {
      sourceRef: 0,
      iqama: 1,
      name: 2,
      accountId: 3,
      from: 4,
      to: 5,
      orders: 6,
      platformRevenue: 7,
      isValid: 8
    }
  };
  await apiPost(`/api/imports/${upload.id}/preview`, importSpec);
  await apiPost(`/api/imports/${upload.id}/commit`, importSpec);

  const previewPay = await (await context.request.get(`${base}/api/payroll/preview?period=${period}`)).json();
  assert.equal(previewPay[0].payable, 1000);
  await apiPost('/api/payroll/approve', {
    sponsorId: 'BW',
    iqama,
    period,
    fingerprint: previewPay[0].fingerprint
  });

  // Navigate to Debts / Finance section
  await page.locator('aside.sidebar [data-view="debts"]').click();
  await page.locator('[data-subtab="debts-list"]').waitFor();

  // Verify all 4 new sub-tabs are present
  const coaTab = page.locator('[data-subtab="coa-target"]');
  const trialTab = page.locator('[data-subtab="trial-balance"]');
  const incomeTab = page.locator('[data-subtab="income-statement"]');
  const bankTab = page.locator('[data-subtab="bank-file"]');

  assert.equal(await coaTab.count(), 1, 'Target COA tab must exist');
  assert.equal(await trialTab.count(), 1, 'Trial Balance tab must exist');
  assert.equal(await incomeTab.count(), 1, 'Income Statement tab must exist');
  assert.equal(await bankTab.count(), 1, 'Bank File tab must exist');

  // ===============================================
  // Test 1: Chart of Accounts (coa-target)
  // ===============================================
  await coaTab.click();
  await page.locator('.table-wrap table').waitFor();

  // Verify stats cards
  const statsValues = await page.locator('.stats .stat strong').allInnerTexts();
  assert.equal(statsValues[0].trim(), '392', 'Total accounts must be 392');
  assert.equal(statsValues[1].trim(), '222', 'Posting accounts must be 222');
  assert.equal(statsValues[2].trim(), '170', 'Header accounts must be 170');
  assert.ok(statsValues[3].includes('317'), 'New accounts must be 317');
  assert.ok(statsValues[3].includes('75'), 'Mapping must be 75');

  // Verify badges: header/posting, nature, type, direction
  const rows = page.locator('tbody tr.coa-row');
  assert.ok((await rows.count()) > 10, 'Multiple accounts rendered');
  const firstRowText = await rows.first().innerText();
  assert.ok(firstRowText.includes('مدين') || firstRowText.includes('دائن'), 'Nature badge must exist');

  // Test search functionality
  const searchInput = page.locator('#coa-search-input');
  await searchInput.fill('الصندوق الرئيسي');
  await page.waitForTimeout(100);
  const searchRowCount = await page.locator('tbody tr.coa-row').count();
  assert.ok(searchRowCount >= 1 && searchRowCount < 392, 'Search filters accounts');
  const searchRowTexts = await page.locator('tbody tr.coa-row').allInnerTexts();
  assert.ok(searchRowTexts.some(t => t.includes('الصندوق الرئيسي')), 'Matching account is present');

  await searchInput.fill('');
  await page.waitForTimeout(100);

  // Test expand / collapse all
  await page.locator('#coa-collapse-all').click();
  await page.waitForTimeout(100);
  const collapsedCount = await page.locator('tbody tr.coa-row').count();
  await page.locator('#coa-expand-all').click();
  await page.waitForTimeout(100);
  const expandedCount = await page.locator('tbody tr.coa-row').count();
  assert.ok(expandedCount > collapsedCount, 'Expand all shows more rows than collapse all');

  // ===============================================
  // Test 2: Trial Balance (trial-balance)
  // ===============================================
  await trialTab.click();
  await page.locator('#tb-sponsor-select').waitFor();

  // Set dates to period 2026-10
  await page.locator('#tb-from-month').fill('2026-10');
  await page.locator('#tb-to-month').fill('2026-10');
  await page.locator('#tb-refresh-btn').click();
  await page.locator('.tb-table').first().waitFor();

  // Check balanced badge
  const tbBadge = await page.locator('.tag.good').allInnerTexts();
  assert.ok(tbBadge.some(t => t.includes('متوازن')), 'Trial balance must be balanced');

  // Verify totals row
  const tfootText = await page.locator('.tb-table').first().locator('tfoot').innerText();
  assert.ok(tfootText.includes('الإجمالي العام'), 'Totals row must exist');

  // Test toggle view: target vs current
  await page.locator('#tb-view-target').click();
  await page.locator('.tb-table').first().waitFor();
  assert.ok(await page.locator('#tb-view-target').evaluate(el => el.classList.contains('good')));

  await page.locator('#tb-view-current').click();
  await page.locator('.tb-table').first().waitFor();
  assert.ok(await page.locator('#tb-view-current').evaluate(el => el.classList.contains('good')));

  // ===============================================
  // Test 3: Income Statement (income-statement)
  // ===============================================
  await incomeTab.click();
  await page.locator('#inc-from-month').waitFor();

  await page.locator('#inc-from-month').fill('2026-10');
  await page.locator('#inc-to-month').fill('2026-10');
  await page.locator('#inc-refresh-btn').click();
  await page.locator('.stats').first().waitFor();

  // Check revenue, expense, and net cards
  const incStatLabels = await page.locator('.stats .stat .label').allInnerTexts();
  assert.ok(incStatLabels.some(l => l.includes('الإيرادات')));
  assert.ok(incStatLabels.some(l => l.includes('المصروفات')));
  assert.ok(incStatLabels.some(l => l.includes('صافي الدخل')));

  const incRevenueVals = await page.locator('.stats strong').allInnerTexts();
  assert.ok(incRevenueVals.some(v => v.includes('500.00')), 'Revenue must be 500.00 from seeded transaction');

  // Check expandable groups
  const incRows = page.locator('tbody tr.inc-row');
  assert.ok((await incRows.count()) > 0, 'Income groups rendered');

  // ===============================================
  // Test 4: Bank File (bank-file)
  // ===============================================
  await bankTab.click();
  await page.locator('#bank-file-sponsor').waitFor();

  await page.locator('#bank-file-period').fill('2026-10');
  await page.locator('#bank-file-preview-btn').click();
  await page.locator('.bank-row').waitFor();

  // Check cards: rows, total, errors
  const bankCards = await page.locator('.stats .stat strong').allInnerTexts();
  assert.equal(bankCards[0].trim(), '1', '1 row in bank preview');
  assert.ok(bankCards[1].includes('1,000.00'), 'Total amount 1,000.00');
  assert.equal(bankCards[2].trim(), '0', '0 errors initially');

  // Check read-only and editable fields
  const row = page.locator('.bank-row').first();
  const bankCell = await row.locator('td').nth(3).innerText();
  assert.equal(bankCell.trim(), 'RJHI', 'Bank code RJHI is read-only');

  const ibanCell = await row.locator('td').nth(4).innerText();
  assert.equal(ibanCell.trim(), iban, 'IBAN is read-only');

  const amountInput = row.locator('.bank-amount-input');
  assert.equal(await amountInput.inputValue(), '1000.00');

  const notesInput = row.locator('.bank-notes-input');
  await notesInput.fill('ملاحظة تجريبية لصرف الراتب');

  // Check that export button is enabled
  const exportBtn = page.locator('#bank-file-export-btn');
  assert.ok(!await exportBtn.isDisabled(), 'Export button must be enabled when no errors exist');

  // Test editing amount to 0 -> causes error and disables export button
  await amountInput.fill('0');
  await amountInput.dispatchEvent('input');
  await page.waitForTimeout(50);
  assert.ok(await exportBtn.isDisabled(), 'Export button must be disabled when amount <= 0');

  // Restore amount to valid value (950.00)
  await amountInput.fill('950.00');
  await amountInput.dispatchEvent('input');
  await page.waitForTimeout(50);
  assert.ok(!await exportBtn.isDisabled(), 'Export button re-enabled after fixing amount');

  // Verify CSV download
  const downloadPromise = page.waitForEvent('download');
  await exportBtn.click();
  const download = await downloadPromise;
  assert.match(download.suggestedFilename(), /_bulkPayroll\.csv/);

  // Read downloaded bytes and verify UTF-8 BOM and header
  const downloadStream = await download.createReadStream();
  const chunks = [];
  for await (const chunk of downloadStream) chunks.push(chunk);
  const downloadedBuffer = Buffer.concat(chunks);

  assert.equal(downloadedBuffer[0], 0xEF);
  assert.equal(downloadedBuffer[1], 0xBB);
  assert.equal(downloadedBuffer[2], 0xBF);
  const csvContent = downloadedBuffer.subarray(3).toString('utf-8');
  assert.ok(csvContent.startsWith('البنك,رقم الحساب,إجمالي الراتب,ملاحظات,اسم الموظف,رقم الهوية أو الإقامة,عنوان الموظف'));
  assert.ok(csvContent.includes('950.00'));
  assert.ok(csvContent.includes('ملاحظة تجريبية'));

  // Test Mobile view responsiveness (width: 375px)
  await page.setViewportSize({width: 375, height: 667});
  await page.waitForTimeout(100);
  assert.ok(await page.locator('.table-wrap').first().isVisible());

  // Confirm no uncaught JS errors
  assert.deepEqual(errors, [], `No page errors allowed: ${errors.join('; ')}`);

  console.log('PASS finance views UI: Target COA tree/search/stats, Trial Balance current/target/balanced, Income Statement groups, Bank File editable amounts/notes/validation and raw CSV download.');
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) {
    server.kill('SIGTERM');
    await new Promise(r => server.once('exit', r));
  }
  rmSync(data, {recursive: true, force: true});
}
