import {seedAccountingCash} from './financial-fixtures.mjs';
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

const {chromium} = await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs'));
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const data = mkdtempSync(path.join(tmpdir(), 'gate-rider-statement-ui-'));
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
  await page.locator('[name=username]').fill('simple-statement-tester');
  await page.locator('[name=password]').fill(password);
  await page.locator('#auth-form button').click();
  await page.getByRole('heading', {name: 'كل مستحق في مكانه'}).waitFor();

  // Helper for API setup
  let cookie = '';
  async function apiPost(route, body) {
    const res = await context.request.post(base + route, {data: body});
    assert.ok(res.ok(), `${route} failed: ${res.status()}`);
    return await res.json().catch(() => ({}));
  }

  // Seed accounting cash and a rider
  const iqama = '2000000999';
  const period = new Date().toISOString().slice(0, 7);
  // Seed via direct HTTP
  const setupReq = async (route, body) => {
    const res = await fetch(base + route, {
      method: body === undefined ? 'GET' : 'POST',
      headers: {'Content-Type': 'application/json'},
      body: body === undefined ? undefined : JSON.stringify(body)
    });
    return await res.json().catch(() => ({}));
  };

  // Create rider
  await apiPost('/api/riders', {iqama, name: 'سالم الكعبي', sponsor: 'شركة البوابا', city: 'الرياض', status: 'Active'});

  // Create a debt and collection
  const debt = await apiPost('/api/debts', {
    iqama,
    category: 'WorkPermit',
    entryMode: 'Accrual',
    totalAmount: 600,
    monthlyInstallment: 100,
    creditorSponsorId: '019c18d5-62e1-7000-8000-000000000041',
    sourceRef: 'test-debt-permit',
    agreementReviewed: true
  });

  await apiPost(`/api/debts/${debt.id}/collect`, {
    collectedAmount: 200,
    targetVaultCode: 'MAIN.BW.DEBT',
    receiptRef: 'test-receipt-collect',
    period
  });

  // 2. Test "كشف مبسّط" button in riders view
  await page.locator('nav [data-view="riders"]').click();
  await page.locator(`[data-enterprise="open-statement-simple"][data-iqama="${iqama}"]`).waitFor();
  await page.locator(`[data-enterprise="open-statement-simple"][data-iqama="${iqama}"]`).click();

  // Should navigate to debts view with simple statement subtab active
  await page.locator('.simple-statement-stats').waitFor();
  assert.equal(await page.locator('.simple-statement-header h2 strong').innerText(), 'سالم الكعبي');

  // Verify the 6 stat cards
  const statLabels = await page.locator('.simple-statement-stats .stat .label').allInnerTexts();
  assert.deepEqual(statLabels, ['اتستحقله', 'اتخصم', 'استلم', 'اتحوّل', 'مديونية', 'الصافي']);

  // Verify that cards add up to the net: 0 + 0 + 0 + 0 + (-400) = -400
  const statValues = (await page.locator('.simple-statement-stats .stat strong').allInnerTexts())
    .map(t => Number(t.replace(/,/g, '').trim()));
  const movementSum = statValues.slice(0, 5).reduce((s, v) => s + v, 0);
  assert.equal(movementSum, statValues[5], `Cards sum (${movementSum}) must equal net (${statValues[5]})`);

  // Net should be -400 (money-negative / red)
  const netCard = page.locator('.simple-statement-stats .stat').nth(5);
  assert.match(await netCard.locator('strong').innerText(), /400/);
  assert.ok(await netCard.locator('strong').evaluate(el => el.classList.contains('money-negative')));

  // Verify Net Split cards (Salary, Jahez Cash, Other Debts)
  const splitLabels = await page.locator('.net-split-cards .stat .label').allInnerTexts();
  assert.equal(splitLabels.length, 3);
  assert.ok(splitLabels[0].includes('الراتب'));
  assert.ok(splitLabels[1].includes('جاهز'));
  assert.ok(splitLabels[2].includes('الأخرى'));
  const splitValues = (await page.locator('.net-split-cards .stat strong').allInnerTexts())
    .map(t => Number(t.replace(/,/g, '').trim()));
  const splitSum = splitValues.reduce((s, v) => s + v, 0);
  assert.equal(splitSum, statValues[5], `Split sum (${splitSum}) must equal net (${statValues[5]})`);

  // Verify scopeLimited=false shows normal phrase
  const normalPhrase = await netCard.locator('.hint').innerText();
  assert.ok(normalPhrase.includes('مستحق عليك للشركة'));

  // 3. Test scopeLimited=true: warning banner appears, "مستحق لك/عليك" phrase is hidden
  await page.route('**/api/finance/statement/rider/*/simple*', async route => {
    const res = await route.fetch();
    const json = await res.json();
    json.scopeLimited = true;
    json.excludedLineCount = 3;
    json.closingNetIsComplete = false;
    json.closingLabelAr = 'صافي جزئي ضمن نطاقك — لا يمثل الرصيد الكامل';
    await route.fulfill({json});
  });

  await page.locator('[data-subtab="debts-list"]').click();
  await page.locator('[data-subtab="statement-simple"]').click();
  await page.locator('.scope-limited-banner').waitFor();

  const bannerText = await page.locator('.scope-limited-banner').innerText();
  assert.ok(bannerText.includes('تنبيه نطاق الصلاحيات'));
  assert.ok(bannerText.includes('3 سطر مالي'));

  const scopedNetText = await page.locator('.simple-statement-stats .stat').nth(5).locator('.hint').innerText();
  assert.ok(scopedNetText.includes('صافي جزئي ضمن نطاقك'));
  assert.ok(!scopedNetText.includes('مستحق لك'));
  assert.ok(!scopedNetText.includes('مستحق عليك'));

  // Verify entire screen has no "مستحق لك / مستحق عليك" phrase when scopeLimited
  const scopedPageText = await page.locator('#content').innerText();
  assert.ok(!scopedPageText.includes('مستحق لك لدى الشركة'));
  assert.ok(!scopedPageText.includes('مستحق عليك للشركة'));

  await page.unroute('**/api/finance/statement/rider/*/simple*');

  // Verify Month table
  const monthTable = page.locator('.simple-statement-month-table');
  await monthTable.waitFor();
  assert.equal(await monthTable.locator('tbody tr.simple-month-row').count(), 1);

  // Verify no debit / credit columns in the entire screen
  const pageText = await page.locator('#content').innerText();
  assert.ok(!pageText.includes('مدين ('));
  assert.ok(!pageText.includes('دائن ('));
  assert.ok(!pageText.includes('Debit'));
  assert.ok(!pageText.includes('Credit'));

  // Test expanding the month row
  const detailsRow = page.locator(`.simple-month-details-row[data-details-for="${period}"]`);
  assert.ok(await detailsRow.evaluate(el => el.classList.contains('hide')));

  // Click on the month row
  await page.locator(`.simple-month-row[data-month-period="${period}"]`).click();
  assert.ok(!(await detailsRow.evaluate(el => el.classList.contains('hide'))));

  // Check detail rows inside
  const detailCount = await detailsRow.locator('tbody tr').count();
  assert.ok(detailCount >= 2, `Expected at least 2 detail rows, got ${detailCount}`);

  // Test collapse
  await page.locator(`.simple-month-row[data-month-period="${period}"]`).click();
  assert.ok(await detailsRow.evaluate(el => el.classList.contains('hide')));

  // Test Print Button exists
  assert.ok(await page.locator('[data-action="print-simple-statement"]').isVisible());

  // 3. Test sub-tab navigation
  await page.locator('[data-subtab="debts-list"]').click();
  await page.locator('[data-subtab="statement-simple"]').waitFor();
  await page.locator('[data-subtab="statement-simple"]').click();
  await page.locator('.simple-statement-stats').waitFor();

  // 4. Test phone width (390x844)
  await page.setViewportSize({width: 390, height: 844});
  await page.waitForTimeout(300);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Mobile width overflow');
  await page.screenshot({path: path.join(out, 'rider-statement-mobile.png'), fullPage: true});

  assert.deepEqual(errors, []);
  console.log('PASS rider simple statement UI: navigation, row button, 6 stat cards, red/green net, month details expansion, no debit/credit, print button, mobile width.');
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null && !spawnError) {
    const stopped = new Promise(r => server.once('exit', r));
    server.kill();
    await Promise.race([stopped, new Promise(r => setTimeout(r, 5000))]);
  }
  rmSync(data, {recursive: true, force: true});
}
