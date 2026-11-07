// Import Center Playwright Test Suite (R6c - I3)
// Synthetic data only, temporary SQLite, real ASP.NET Core server and static export.

import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtempSync, rmSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import assert from 'node:assert/strict';
import { syntheticWorkbook } from '../../tests/financial-fixtures.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const rootDir = path.resolve(webDir, '..');
const resultsDir = path.resolve(rootDir, 'test-results/r6c-ui');
mkdirSync(resultsDir, { recursive: true });

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

const dataDir = mkdtempSync(path.join(tmpdir(), 'gate-import-center-test-'));
const voucherSecret = randomBytes(32).toString('hex');
let child, base, browser;

async function startServer() {
  const probe = createServer();
  await new Promise((ok, no) => {
    probe.once('error', no);
    probe.listen(0, '127.0.0.1', ok);
  });
  base = 'http://127.0.0.1:' + probe.address().port;
  await new Promise((ok) => probe.close(ok));

  child = spawn(
    'dotnet',
    [path.join(rootDir, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],
    {
      cwd: path.join(rootDir, 'Payroll.Api'),
      env: {
        ...process.env,
        ConnectionStrings__DefaultConnection: '',
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: base,
        PAYROLL_DATA_DIR: dataDir,
        Vouchers__Secret: voucherSecret,
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    }
  );
  child.stdout.on('data', d => {
    const s = d.toString();
    if (s.includes('fail:') || s.includes('error:')) console.log('SERVER OUT:', s.slice(0, 300));
  });
  child.stderr.on('data', d => console.log('SERVER ERR:', d.toString().slice(0, 300)));

  for (let n = 0; n < 150; n++) {
    if (child.exitCode !== null) throw new Error('API server exited prematurely');
    try {
      const res = await fetch(base + '/health');
      if (res.ok) return;
    } catch {}
    await new Promise((r) => setTimeout(r, 100));
  }
  throw new Error('API server startup timed out');
}

async function apiReq(route, body, cookie = '', status = 200) {
  const res = await fetch(base + route, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      ...(cookie ? { Cookie: cookie } : {}),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  assert.equal(res.status, status, `${route} returned ${res.status}: ${text.slice(0, 200)}`);
  const setCookie = res.headers.get('set-cookie');
  return {
    data: text ? JSON.parse(text) : null,
    cookie: setCookie ? setCookie.split(';')[0] : cookie,
    res,
  };
}

try {
  console.log('1. Starting API server with synthetic data dir...');
  await startServer();

  // Setup initial Admin
  const adminPassword = randomBytes(16).toString('hex');
  const adminSetup = await apiReq('/api/auth/setup', {
    username: 'admin-import-test',
    password: adminPassword,
  });
  const adminCookie = adminSetup.cookie;

  // Create FinancialAccountant user
  const finAccPassword = randomBytes(16).toString('hex');
  await apiReq(
    '/api/users',
    {
      username: 'fin-acc-user',
      password: finAccPassword,
      role: 'FinancialAccountant',
      displayNameAr: 'محاسب مالي تجريبي',
    },
    adminCookie
  );

  // Create Supervisor user (no import view or upload permissions)
  const supervisorPassword = randomBytes(16).toString('hex');
  await apiReq(
    '/api/users',
    {
      username: 'supervisor-user',
      password: supervisorPassword,
      role: 'PlatformsSupervisor',
      displayNameAr: 'مشرف تشغيلي تجريبي',
    },
    adminCookie
  );

  // Login as Supervisor to get their cookie
  const supLogin = await apiReq('/api/auth/login', {
    username: 'supervisor-user',
    password: supervisorPassword,
  });
  const supervisorCookie = supLogin.cookie;

  // Login as FinancialAccountant to get their cookie
  const finLogin = await apiReq('/api/auth/login', {
    username: 'fin-acc-user',
    password: finAccPassword,
  });
  const finCookie = finLogin.cookie;

  console.log('2. Verifying GET /api/imports/catalog response directly...');
  const catalogRes = await apiReq('/api/imports/catalog', undefined, adminCookie);
  const catalog = catalogRes.data;
  assert.ok(Array.isArray(catalog) && catalog.length === 12, 'Catalog must contain 12 steps');

  // Verify catalog order
  assert.equal(catalog[0].key, 'companies', 'First step must be companies');
  assert.equal(catalog[0].labelAr, 'الشركات (السجلات التجارية)');

  const opOrder = catalog
    .filter((s) => s.key.includes('_operations'))
    .map((s) => s.key);
  assert.deepEqual(
    opOrder,
    ['ninja_operations', 'keeta_operations', 'hungerstation_operations', 'chefz_operations'],
    'Operations order must be Ninja -> Keeta -> HungerStation -> Chefz'
  );

  console.log('3. Verifying every templateUrl returns 200 with an XLSX mime type...');
  for (const step of catalog) {
    if (step.templateUrl) {
      const url = base + step.templateUrl;
      const res = await fetch(url, {
        headers: { Cookie: adminCookie },
      });
      assert.equal(
        res.status,
        200,
        `Template download for ${step.key} (${step.templateUrl}) should return 200`
      );
      const ct = res.headers.get('content-type') || '';
      assert.ok(
        ct.includes('application/vnd.openxmlformats') || ct.includes('spreadsheetml'),
        `Template for ${step.key} must be xlsx mime, got: ${ct}`
      );
    }
  }
  console.log('✓ All catalog templates verified (status 200, XLSX mime)');

  console.log('4. Launching Playwright browser...');
  browser = await chromium.launch();

  // Test Admin session at 1440
  const ctxAdmin = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const [cookieName, cookieVal] = adminCookie.split('=');
  await ctxAdmin.addCookies([{ name: cookieName, value: cookieVal, url: base }]);

  const page = await ctxAdmin.newPage();
  page.on('console', m => console.log('PAGE LOG:', m.type(), m.text()));
  page.on('pageerror', err => console.error('PAGE ERROR:', err));
  page.on('response', r => {
    if (r.status() >= 400) console.log('RESP ERROR:', r.status(), r.url());
  });
  await page.goto(base + '/app/imports/', { waitUntil: 'networkidle' });

  // Verify page header
  await page.waitForSelector('text=مركز الاستيراد');
  // The free-form column-mapping importer stays available as a second tab.
  await page.click('[data-testid="imports-tab-free"]');
  await page.waitForSelector('text=استيراد بيانات التشغيل والمنصات');
  await page.click('[data-testid="imports-tab-center"]');
  await page.waitForSelector('text=الشركات (السجلات التجارية)');
  console.log('✓ Free-form import tab still available next to the catalog');

  // Check step cards rendering in order
  const stepHeadings = await page.locator('h3').allTextContents();
  assert.ok(
    stepHeadings[0].includes('الشركات'),
    `First card heading must be companies, got: ${stepHeadings[0]}`
  );

  // Check order of operation cards in DOM
  const pageContent = await page.content();
  const ninjaIdx = pageContent.indexOf('تشغيل نينجا للمناديب');
  const keetaIdx = pageContent.indexOf('تشغيل كيتا للمناديب');
  const hsIdx = pageContent.indexOf('تشغيل هنقرستيشن للمناديب');
  const chefzIdx = pageContent.indexOf('تشغيل شفز');

  assert.ok(ninjaIdx > 0 && keetaIdx > ninjaIdx, 'Ninja ops before Keeta ops');
  assert.ok(keetaIdx > 0 && hsIdx > keetaIdx, 'Keeta ops before HungerStation ops');
  assert.ok(hsIdx > 0 && chefzIdx > hsIdx, 'HungerStation ops before Chefz ops');

  // Take screenshot at 1440 Admin
  await page.screenshot({ path: path.join(resultsDir, 'import-center-1440-admin.png'), fullPage: true });
  console.log('✓ Saved import-center-1440-admin.png');

  // Test Columns Drawer opens and closes (Escape and outside click)
  console.log('5. Testing Columns Drawer (Escape and outside click)...');
  const firstColBtn = page.locator('#step-companies button:has-text("الأعمدة")');
  await firstColBtn.click();

  // Wait for drawer to appear
  const drawer = page.locator('[role="dialog"]');
  await drawer.waitFor({ state: 'visible' });
  const drawerTitle = await drawer.locator('h2').textContent();
  assert.ok(drawerTitle.includes('الشركات'), 'Drawer title matches companies step');

  // Test closing via Escape key
  await page.keyboard.press('Escape');
  await drawer.waitFor({ state: 'hidden' });
  console.log('✓ Drawer closed with Escape key');

  // Open drawer again and test closing via backdrop outside click
  await firstColBtn.click();
  await drawer.waitFor({ state: 'visible' });
  await page.locator('div.absolute.inset-0[aria-hidden="true"]').click({ force: true });
  await drawer.waitFor({ state: 'hidden' });
  console.log('✓ Drawer closed with outside backdrop click');

  // Test 360px Mobile Layout and zero horizontal scroll
  console.log('6. Testing 360px mobile responsiveness...');
  await page.setViewportSize({ width: 360, height: 800 });
  await page.waitForTimeout(300);

  const isWide = await page.evaluate(() => {
    const w = window.innerWidth;
    return document.documentElement.scrollWidth > w + 1;
  });
  assert.equal(isWide, false, 'No page-level horizontal scroll at 360px viewport');

  await page.screenshot({ path: path.join(resultsDir, 'import-center-360-admin.png'), fullPage: true });
  console.log('✓ Saved import-center-360-admin.png');

  // Open columns drawer on 360
  await firstColBtn.click();
  await drawer.waitFor({ state: 'visible' });
  await page.screenshot({ path: path.join(resultsDir, 'import-center-360-drawer.png') });
  console.log('✓ Saved import-center-360-drawer.png');
  await page.keyboard.press('Escape');
  await drawer.waitFor({ state: 'hidden' });

  // Test Supervisor Role (sees no upload buttons)
  console.log('7. Testing Supervisor role (no upload buttons)...');
  const ctxSupervisor = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const [sName, sVal] = supervisorCookie.split('=');
  await ctxSupervisor.addCookies([{ name: sName, value: sVal, url: base }]);

  const pageSup = await ctxSupervisor.newPage();
  await pageSup.goto(base + '/app/imports/', { waitUntil: 'networkidle' });

  // Ensure no upload buttons or file dropzones are present for supervisor
  const uploadInputs = await pageSup.locator('input[type="file"]').count();
  assert.equal(uploadInputs, 0, 'Supervisor must see 0 file upload inputs');

  const uploadLabels = await pageSup.locator('label[for^="file-input-"]').count();
  assert.equal(uploadLabels, 0, 'Supervisor must see 0 upload dropzone labels');

  await pageSup.screenshot({ path: path.join(resultsDir, 'import-center-1440-supervisor.png'), fullPage: true });
  console.log('✓ Saved import-center-1440-supervisor.png');

  // Test XLSX Preview flow with synthetic riders file
  console.log('8. Testing XLSX preview flow with synthetic riders file...');
  // Restore viewport
  await page.setViewportSize({ width: 1440, height: 900 });

  // Generate synthetic riders Excel
  const riderHeaders = [
    'رقم الإقامة',
    'الاسم الكامل',
    'الجوال',
    'المدينة',
    'معرف سياسة الحساب',
    'الجنسية',
    'نوع العلاقة',
    'الكفيل',
    'نوع المركبة',
    'الحالة',
  ];
  const riderRows = [
    riderHeaders,
    ['2000000888', 'مندوب تجريبي واحد', '0501112233', 'الرياض', '', 'سعودي', 'SponsoredInternal', 'BW', 'Car', 'Active'],
    ['2000000889', 'مندوب تجريبي اثنان', '0501112234', 'جدة', '', 'سعودي', 'SponsoredInternal', 'BW', 'Car', 'Active'],
  ];
  const ridersB64 = syntheticWorkbook([{ name: 'البيانات', rows: riderRows }]);
  const ridersBuffer = Buffer.from(ridersB64, 'base64');

  const ridersFileInput = page.locator('#file-input-riders');
  await ridersFileInput.setInputFiles({
    name: 'synthetic-riders-test.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: ridersBuffer,
  });

  // Verify preview results table appears
  await page.waitForSelector('text=معاينة الملف: synthetic-riders-test.xlsx');
  await page.waitForSelector('text=اعتماد البيانات واستيرادها');

  // Verify row counts in preview
  const previewText = await page.locator('#step-riders').textContent();
  assert.ok(previewText.includes('إجمالي الصفوف'), 'Preview shows total rows stat card');
  assert.ok(previewText.includes('مندوب تجريبي واحد'), 'Preview table shows sample row data');

  await page.locator('#step-riders').scrollIntoViewIfNeeded();
  await page.waitForTimeout(200);
  await page.screenshot({ path: path.join(resultsDir, 'import-center-preview-riders.png') });
  console.log('✓ Saved import-center-preview-riders.png');

  console.log('====================================================');
  console.log('PASS: Import Center Playwright Suite (R6c - I3) GREEN');
  console.log('====================================================');
} finally {
  if (browser) await browser.close();
  if (child && child.exitCode === null) {
    const ex = once(child, 'exit');
    child.kill();
    await ex;
  }
  rmSync(dataDir, { recursive: true, force: true });
}
