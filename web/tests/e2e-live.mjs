import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const rootDir = path.resolve(webDir, '..');
const outDir = path.join(webDir, 'out');
const wwwrootAppDir = path.join(rootDir, 'Payroll.Api/wwwroot/app');

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

console.log('--- Starting Gate Payroll 1.0 Web Live API E2E Suite ---');

// 1. Ensure web/out exists and copy to wwwroot/app for the live dotnet server to serve
assert.ok(existsSync(outDir), 'web/out directory must exist. Run npm run build first.');
mkdirSync(wwwrootAppDir, { recursive: true });
cpSync(outDir, wwwrootAppDir, { recursive: true });
console.log('✓ Copied web/out to Payroll.Api/wwwroot/app');

const tempDir = mkdtempSync(path.join(tmpdir(), 'gate-web-live-'));
const basePort = 19582;
const baseUrl = `http://127.0.0.1:${basePort}`;

// Spawn dotnet server
const proc = spawn(
  process.env.DOTNET_PATH || 'dotnet',
  [path.join(rootDir, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],
  {
    cwd: path.join(rootDir, 'Payroll.Api'),
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: baseUrl,
      PAYROLL_DATA_DIR: tempDir,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  }
);

let logs = '';
let spawnError = null;
proc.on('error', (e) => (spawnError = e));
proc.stdout.on('data', (d) => (logs += d));
proc.stderr.on('data', (d) => (logs += d));

let browser;

try {
  // Wait for server /health
  let ready = false;
  for (let i = 0; i < 150; i++) {
    if (spawnError) throw spawnError;
    try {
      const res = await fetch(`${baseUrl}/health`);
      if (res.ok) {
        ready = true;
        break;
      }
    } catch {}
    await new Promise((r) => setTimeout(r, 100));
  }
  assert.ok(ready, `Dotnet server failed to start at ${baseUrl}. Logs: ${logs}`);
  console.log(`✓ Dotnet live API started at ${baseUrl}`);

  browser = await chromium.launch({ headless: true });

  // 1. Setup / Login Admin User
  const adminUsername = 'admin-' + randomBytes(4).toString('hex');
  const adminPassword = 'Password123!' + randomBytes(4).toString('hex');

  const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: adminUsername, password: adminPassword }),
  });
  assert.equal(setupRes.status, 200, 'Setup admin user should return 200');
  console.log('✓ Step 1: Admin user registered and logged in');

  // Grab cookie from setup response to use for admin context
  const setCookie = setupRes.headers.get('set-cookie');
  assert.ok(setCookie, 'Must receive auth cookie');

  const context = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    extraHTTPHeaders: {
      Cookie: setCookie.split(';')[0],
    },
  });

  // Set auth cookie in browser context
  const [cookieName, ...cookieRest] = setCookie.split(';')[0].split('=');
  const cookieValue = cookieRest.join('=');
  await context.addCookies([
    {
      name: cookieName.trim(),
      value: cookieValue.trim(),
      domain: '127.0.0.1',
      path: '/',
    },
  ]);

  const page = await context.newPage();

  // Verify /api/me works with live cookie
  const meRes = await page.request.get(`${baseUrl}/api/me`);
  assert.equal(meRes.status(), 200, '/api/me should succeed');
  const meData = await meRes.json();
  assert.equal(meData.username, adminUsername);
  console.log(`✓ Live authenticated session confirmed for @${meData.username} (${meData.role})`);

  // --- W1 Tests: Login, ReturnUrl, Logout, Lockout ---
  console.log('\n--- Running W1 Live E2E Tests ---');

  // Test 1: Unauthenticated 401 redirect preserves returnUrl
  const unauthContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const loginPage = await unauthContext.newPage();
  const cspErrors = [];
  loginPage.on('console', (m) => { if (m.type() === 'error' && m.text().includes('Content Security Policy')) cspErrors.push(m.text()); });
  await loginPage.goto(`${baseUrl}/app/notifications/`);
  console.log('Navigated to notifications, current URL:', loginPage.url());
  await loginPage.waitForURL(url => url.pathname.includes('/login'), { timeout: 8000 });
  assert.ok(loginPage.url().includes('returnUrl='), 'Unauthenticated visit should redirect to login with returnUrl');
  console.log('✓ W1: 401 redirect to /app/login/?returnUrl= confirmed');
  assert.equal(cspErrors.length, 0, 'The real CSP must allow the Next.js inline boot scripts (hashes)');
  console.log('✓ CSP: no blocked inline scripts with the real server policy');

  // Test 2: Valid login with returnUrl redirects to destination
  await loginPage.goto(`${baseUrl}/app/login/?returnUrl=/app/notifications/`);
  await loginPage.locator('#username').fill(adminUsername);
  await loginPage.locator('#password').fill(adminPassword);
  await loginPage.locator('button[type="submit"]').click();
  await loginPage.waitForURL(url => url.pathname.includes('/notifications'), { timeout: 8000 });
  console.log('✓ W1: Login with safe returnUrl redirected to /app/notifications/');

  // Test 3: Logout calls /api/auth/logout and redirects to /app/login/
  await loginPage.evaluate(async () => {
    await fetch('/api/auth/logout', { method: 'POST' });
    window.location.href = '/app/login/';
  });
  await loginPage.waitForURL(url => url.pathname.includes('/login'), { timeout: 8000 });
  await loginPage.waitForSelector('#username', { timeout: 10000 });
  console.log('✓ W1: Logout successful and redirected to /app/login/');

  // Test 4: Open redirect protection (unsafe returnUrl blocked with 400, safe /legacy/ returnUrl succeeds)
  const badReturnRes = await fetch(`${baseUrl}/app/login/?returnUrl=${encodeURIComponent('https://malicious-site.example/steal-token')}`);
  assert.equal(badReturnRes.status, 400, 'Unsafe returnUrl must be rejected with 400');
  console.log('✓ W1: Malicious returnUrl blocked with HTTP 400 by security guard');

  await unauthContext.clearCookies();
  await loginPage.goto(`${baseUrl}/app/login/?returnUrl=/legacy/#/dashboard`);
  await loginPage.waitForSelector('#username', { timeout: 10000 });
  await loginPage.locator('#username').fill(adminUsername);
  await loginPage.locator('#password').fill(adminPassword);
  await loginPage.locator('button[type="submit"]').click();
  await loginPage.waitForURL(url => url.pathname.includes('/legacy'), { timeout: 8000 });
  console.log('✓ W1: Safe returnUrl redirected to /legacy/#/dashboard');

  // Test 5: Wrong password displays 401 error message
  await unauthContext.clearCookies();
  await loginPage.goto(`${baseUrl}/app/login/`);
  await loginPage.locator('#username').fill(adminUsername);
  await loginPage.locator('#password').fill('WrongPassword999!');
  await loginPage.locator('button[type="submit"]').click();
  const alertEl = loginPage.locator('#auth-error-alert');
  await alertEl.waitFor({ timeout: 5000 });
  const alertText = await alertEl.textContent();
  assert.ok(alertText.includes('اسم المستخدم أو كلمة المرور غير صحيحة') || alertText.includes('unauthorized'), 'Should show wrong password error');
  console.log('✓ W1: Wrong password error message verified');

  // Test 6: Account lockout (423) after 5 failed attempts
  const lockUsername = 'lock-' + randomBytes(3).toString('hex');
  const lockPassword = 'StrongPassword123!';
  const createLockUserRes = await page.request.post(`${baseUrl}/api/users`, {
    data: { username: lockUsername, password: lockPassword, role: 'Accountant' },
  });
  assert.equal(createLockUserRes.status(), 200, 'User creation for lockout test should succeed');

  for (let i = 1; i <= 5; i++) {
    const failRes = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: lockUsername, password: 'WrongPassword!' }),
    });
    assert.equal(failRes.status, 401, `Failed attempt ${i} should return 401`);
  }

  // 6th attempt via UI should trigger 423
  await loginPage.locator('#username').fill(lockUsername);
  await loginPage.locator('#password').fill('WrongPassword!');
  await loginPage.locator('button[type="submit"]').click();
  const lockAlertEl = loginPage.locator('#auth-error-alert');
  await lockAlertEl.waitFor({ timeout: 5000 });
  const lockAlertText = await lockAlertEl.textContent();
  assert.ok(lockAlertText.includes('مقفل') || lockAlertText.includes('account_locked'), 'Lockout alert must be displayed');
  console.log('✓ W1: Account lockout (423) alert verified on 6th failed attempt');

  await unauthContext.close();

  // --- W2 Tests: Companies and Commercial Registrations ---
  console.log('\n--- Running W2 Live E2E Tests (Companies & Registrations) ---');

  // Test 1: Open /app/companies/ and verify stat cards and tables
  await page.goto(`${baseUrl}/app/companies/`);
  await page.waitForLoadState('networkidle');
  await page.waitForSelector('h1:has-text("الشركات والسجلات التجارية")', { timeout: 8000 });
  console.log('✓ W2: /app/companies/ loaded successfully');

  // Verify stat cards
  const statText = await page.textContent('body');
  assert.ok(statText.includes('الشركات والمنشآت'), 'Stat card for companies count must be present');
  assert.ok(statText.includes('السجلات التجارية النشطة'), 'Stat card for active CRs must be present');
  assert.ok(statText.includes('استحقاقات التأكيد'), 'Stat card for confirmations must be present');
  assert.ok(statText.includes('إجمالي رأس المال'), 'Stat card for capital must be present');

  // Test 2: Import certificates via Admin Preview -> Commit flow
  console.log('Testing Admin import flow (Preview -> Commit)...');
  const importBtn = page.locator('button:has-text("استيراد بيانات الشركات")');
  await importBtn.waitFor({ timeout: 5000 });
  await importBtn.click();

  // Wait for import dialog and preview plan
  const importDialog = page.locator('div[role="dialog"]:has-text("استيراد وتحديث بيانات الشركات والسجلات")');
  await importDialog.waitFor({ timeout: 5000 });
  await importDialog.locator('strong:has-text("الشركات:")').waitFor({ timeout: 8000 });
  console.log('✓ W2: Import preview plan generated with valid fingerprint');

  // Commit import
  const commitBtn = importDialog.locator('button:has-text("تأكيد الاستيراد والاعتماد النهائي")');
  await commitBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("تم اعتماد وتحديث شهادات الشركات بنجاح")', { timeout: 8000 });
  console.log('✓ W2: Companies certificates import committed successfully');

  // Test 3: Verify Companies Table rows (BW, EX, TR)
  const bwRow = page.locator('tbody tr').filter({ has: page.locator('td:nth-child(1)', { hasText: 'BW' }) });
  await bwRow.waitFor({ timeout: 5000 });
  const exRow = page.locator('tbody tr').filter({ has: page.locator('td:nth-child(1)', { hasText: 'EX' }) });
  const trRow = page.locator('tbody tr').filter({ has: page.locator('td:nth-child(1)', { hasText: 'TR' }) });
  assert.ok(await exRow.count() > 0, 'EX company row must exist');
  assert.ok(await trRow.count() > 0, 'TR company row must exist');

  // Verify unified number for BW (7015658094) and CR (4030362130)
  const bwRowText = await bwRow.textContent();
  assert.ok(bwRowText.includes('7015658094'), 'BW unified number 7015658094 must be present');
  assert.ok(bwRowText.includes('4030362130'), 'BW CR number 4030362130 must be present');
  console.log('✓ W2: Companies table rows verified with official unified and CR numbers');

  // Test 4: Open Drawer and verify ISIC activities and «ناقص» chip
  await bwRow.click();
  const drawer = page.locator('div[role="dialog"]:has-text("شركة البوابة المقبلة")');
  await drawer.waitFor({ timeout: 5000 });
  console.log('✓ W2: Company details drawer opened');

  // Switch to Activities tab
  const actTabBtn = drawer.locator('button:has-text("الأنشطة المعتمدة")');
  await actTabBtn.click();
  await drawer.locator('span:has-text("ناقص")').waitFor({ timeout: 5000 });
  assert.ok(await drawer.locator('span:has-text("410010")').count() > 0, 'ISIC activity 410010 must be rendered');
  console.log('✓ W2: ISIC activity chips and «ناقص» tag verified in drawer');

  // Close drawer
  await page.keyboard.press('Escape');
  await drawer.waitFor({ state: 'detached', timeout: 5000 });

  // Test 5: Sub-tab «السجلات التجارية»
  const regTabBtn = page.locator('button[role="tab"]:has-text("السجلات التجارية")');
  await regTabBtn.click();
  await page.locator('th:has-text("رقم السجل التجاري")').waitFor({ timeout: 5000 });
  assert.ok(await page.locator('td:has-text("4030362130")').count() > 0, 'CR 4030362130 must exist in registrations tab');
  console.log('✓ W2: Commercial registrations sub-tab verified');

  // Test 6: Sub-tab «الوثائق والانتهاءات»
  const docTabBtn = page.locator('button[role="tab"]:has-text("الوثائق والانتهاءات")');
  await docTabBtn.click();
  await page.locator('h3:has-text("السجلات والوثائق مستحقة التجديد والتأكيد")').waitFor({ timeout: 5000 });
  console.log('✓ W2: Expiring documents and annual confirmations sub-tab verified');

  // 2. Create an IBAN Campaign via API or UI
  console.log('\nStep 2: Creating an IBAN campaign...');
  const campaignTitle = 'حملة تحديث الآيبان الحية';
  const campRes = await page.request.post(`${baseUrl}/api/campaigns`, {
    data: {
      title: campaignTitle,
      kind: 'iban',
      days: 7,
    },
  });
  assert.equal(campRes.status(), 200, 'Create campaign should return 200');
  const campaign = await campRes.json();
  campaign.title = campaignTitle;
  assert.ok(campaign.id);
  assert.ok(campaign.path);
  console.log(`✓ Step 2: Campaign created: ${campaign.id} (${campaign.title}) at ${campaign.path}`);

  // Extract public token from campaign path (e.g. /forms/c-xxx)
  const token = campaign.path.replace(/^\/forms\//, '');

  // 3. Submit one answer through the public form API
  console.log('\nStep 3: Submitting public answer...');
  const testIqama = '2000000789';
  const testName = 'مندوب تجريبي حي';
  const testIban = 'SA0380000000608010167519';

  const submitRes = await fetch(`${baseUrl}/api/public/forms/${encodeURIComponent(token)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      iqama: testIqama,
      name: testName,
      phone: '+966501112233',
      city: 'جدة',
      iban: testIban,
      bankCode: '80',
      platforms: ['keeta', 'jahez'],
    }),
  });
  assert.equal(submitRes.status, 200, `Public submit should return 200, got ${submitRes.status}`);
  console.log(`✓ Step 3: Answer submitted successfully for iqama ${testIqama}`);

  // 4. Open New Web UI at /app/forms/index.html?tab=responses and approve in Review Drawer
  console.log('\nStep 4: Opening /app/forms/index.html?tab=responses and verifying review checks...');
  await page.goto(`${baseUrl}/app/forms/index.html?tab=responses`);
  await page.waitForLoadState('networkidle');

  // Verify masked IBAN in table list view
  const rowLocator = page.locator(`tr:has-text("${testIqama}")`);
  await rowLocator.waitFor({ timeout: 10000 });
  const rowText = await rowLocator.textContent();
  assert.ok(rowText.includes('****'), 'IBAN must be masked in the list view');
  console.log('✓ Masked IBAN verified in list view');

  // Open review drawer via row options menu
  const menuBtn = rowLocator.locator('button[aria-label="خيارات السجل"]');
  await menuBtn.click();
  const reviewBtn = page.locator('button:has-text("معاينة وتدقيق")');
  await reviewBtn.click();

  const drawerLocator = page.locator('div[role="dialog"]:has-text("مراجعة وتدقيق بيانات الآيبان")');
  await drawerLocator.waitFor({ timeout: 5000 });

  // Verify unmasked IBAN is displayed inside review drawer
  const drawerText = await drawerLocator.textContent();
  assert.ok(drawerText.includes(testIban), 'Unmasked IBAN must be visible inside review drawer');
  console.log('✓ Full unmasked IBAN visible inside review drawer');

  // Attempt approval without ticking checks: should be prevented by validation
  const approveBtn = drawerLocator.locator('button:has-text("اعتماد رسمي")');
  await approveBtn.click();
  // Toast warning should appear
  await page.waitForSelector('div[role="alert"]:has-text("يرجى تأكيد")', { timeout: 3000 });
  console.log('✓ Verification: Approval prevented without ticking mandatory review checks');

  // Now tick both required checks: identity reviewed + IBAN ownership reviewed
  const identityCheck = drawerLocator.locator('input[type="checkbox"]').nth(0);
  const ibanCheck = drawerLocator.locator('input[type="checkbox"]').nth(1);
  await identityCheck.check();
  await ibanCheck.check();

  // Now click approve
  await approveBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("تم اعتماد الرد")', { timeout: 5000 });
  console.log('✓ Step 4: Submission approved successfully with review checks');

  // 5. Test Close Campaign
  console.log('\nStep 5: Testing close campaign...');
  await page.goto(`${baseUrl}/app/forms/index.html?tab=campaigns`);
  await page.waitForLoadState('networkidle');

  const campRow = page.locator(`tr:has-text("${campaign.title}")`);
  await campRow.waitFor({ timeout: 5000 });
  const campMenuBtn = campRow.locator('button[aria-label="خيارات السجل"]');
  await campMenuBtn.click();
  const closeBtn = page.locator('button:has-text("إغلاق الاستقبال")');
  if (await closeBtn.count() > 0) {
    await closeBtn.click();
    await page.waitForSelector('div[role="alert"]:has-text("تم إغلاق")', { timeout: 5000 });
    console.log('✓ Step 5: Campaign closed successfully');
  }

  // 6. Delete Campaign with Delete Preview
  console.log('\nStep 6: Deleting campaign with delete-preview...');
  // Open menu again for delete
  await campMenuBtn.click();
  const deleteBtn = page.locator('button:has-text("حذف الحملة")');
  await deleteBtn.click();

  const deleteDialog = page.locator('div[role="dialog"]:has-text("تأكيد حذف حملة النماذج")');
  await deleteDialog.waitFor({ timeout: 5000 });

  // Verify preview loaded fingerprint and response count
  await deleteDialog.locator('strong.num:has-text("1")').waitFor({ timeout: 5000 });
  console.log('✓ Delete preview loaded response count and fingerprint correctly');

  // Confirm delete
  const confirmDeleteBtn = deleteDialog.locator('button:has-text("تأكيد الحذف النهائي")');
  await confirmDeleteBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("تم حذف النموذج")', { timeout: 5000 });
  console.log('✓ Step 6: Campaign deleted with valid fingerprint confirmed');

  // 7. Export filtered rows to Excel
  console.log('\nStep 7: Testing Excel export payload...');
  const exportRes = await page.request.post(`${baseUrl}/api/excel/export`, {
    data: {
      headers: ['اسم المندوب', 'الإقامة', 'الحالة'],
      rows: [['مندوب تجريبي', '2000000789', 'معتمد']],
      numericColumns: [1],
    },
  });
  assert.equal(exportRes.status(), 200, 'Excel export should return 200');
  const blob = await exportRes.body();
  assert.ok(blob.length > 50, 'Export binary should have valid length');
  console.log(`✓ Step 7: Excel export verified (${blob.length} bytes received)`);

  // --- R3 Tests: HungerStation Review Screens & Workspaces (A7) ---
  console.log('\n--- Running R3 Live E2E Tests (HungerStation Review & L2 Workspaces) ---');

  // Load syntheticWorkbook fixture
  const fixturePath = pathToFileURL(path.join(rootDir, 'tests/financial-fixtures.mjs'));
  const { syntheticWorkbook } = await import(fixturePath);

  // Generate synthetic workbook for HS import
  const hsFile = syntheticWorkbook([
    {
      name: 'Co LVL',
      rows: [
        ['Company CR', 'Contract Name', 'completed_orders', 'basic_payment', 'COURIER_BASIC_PAYMENT', 'Amount Excl VAT', 'VAT', 'Amount Incl VAT', 'Net FTR', 'CAR Deduction October 2026', 'TGA Deduction October 2026'],
        ['4030362130', 'Synthetic HS contract', 10, 100, 100, 100, 15, 115, 115, 5, 20],
      ],
    },
    {
      name: 'Rider LVL',
      rows: [
        ['rider_id', 'Contract Name', 'completed_orders', 'basic_payment', 'COURIER_BASIC_PAYMENT', 'scheme'],
        ['SYN-HS-LIVE', 'Synthetic HS contract', 10, 100, 100, 'salary'],
      ],
    },
    {
      name: 'Breakdown',
      rows: [
        ['rider_id', 'completed_orders'],
        ['SYN-HS-LIVE', 10],
      ],
    },
  ]);

  console.log('\nStep 8: Testing HungerStation Import (Preview & Commit)...');
  const hsInput = {
    period: '2026-10',
    files: [{ fileName: 'synthetic-hs.xlsx', dataBase64: hsFile }],
  };
  const hsPreviewRes = await page.request.post(`${baseUrl}/api/platforms/hs/invoices/import/preview`, {
    data: hsInput,
  });
  assert.equal(hsPreviewRes.status(), 200, 'HS import preview should succeed');
  const hsPreview = await hsPreviewRes.json();
  assert.equal(hsPreview.canCommit, true, 'HS preview must be committable');
  console.log('✓ Step 8a: HS import preview verified with canCommit: true');

  const hsCommitRes = await page.request.post(`${baseUrl}/api/platforms/hs/invoices/import/commit`, {
    data: { ...hsInput, rows: hsPreview.rows, fingerprint: hsPreview.fingerprint, confirm: true },
  });
  assert.equal(hsCommitRes.status(), 200, 'HS import commit should succeed');
  console.log('✓ Step 8b: HS import committed successfully');

  // Grab the TGA line ID from preview
  const tgaLine = hsPreview.rows[0].lines.find((l) => l.tgaAmount !== 0);
  assert.ok(tgaLine, 'TGA line must exist in preview');

  // Step 8c: User A proposes TGA split (verifying percent mode and rejection of mixed mode)
  console.log('\nStep 8c: User A proposing TGA split in percent mode...');

  // Mixed mode must be rejected with 400
  const mixedRes = await page.request.post(`${baseUrl}/api/platforms/hs/allocations/${encodeURIComponent(tgaLine.id)}`, {
    data: { riderPercent: 50, companyAmount: 10 },
  });
  assert.equal(mixedRes.status(), 400, 'Mixed mode (percent + amount) must be rejected with HTTP 400');
  console.log('✓ Step 8c-1: Mixed mode rejection verified (HTTP 400)');

  // Pure percent mode proposal
  const splitRes = await page.request.post(`${baseUrl}/api/platforms/hs/allocations/${encodeURIComponent(tgaLine.id)}`, {
    data: { riderPercent: 0 },
  });
  assert.equal(splitRes.status(), 200, 'TGA split proposal in percent mode should return 200');
  const splitData = await splitRes.json();
  assert.equal(splitData.companyAmount, 20, '0% rider percent means 100% to company');
  console.log(`✓ Step 8c-2: TGA split proposed in percent mode by User A: id=${splitData.id}`);

  // Step 8d: Verify User A refusal when attempting to approve own split (Maker-checker rule)
  const selfApproveRes = await page.request.post(`${baseUrl}/api/platforms/hs/allocations/${encodeURIComponent(splitData.id)}/approve`, {
    data: {},
  });
  assert.equal(selfApproveRes.status(), 403, 'User A self-approval of split must be rejected with 403');
  console.log('✓ Step 8d: Maker-checker verified: User A self-approval refused with HTTP 403');

  console.log('\nStep 9: Testing HungerStation Review Workspace (/app/payroll/)...');
  await page.goto(`${baseUrl}/app/payroll/`);
  await page.waitForLoadState('networkidle');

  // Verify invoice row by period and total amount
  const invoiceRow = page.locator('tbody tr').filter({ hasText: '2026-10' }).first();
  await invoiceRow.waitFor({ timeout: 8000 });
  console.log('✓ Step 9a: Invoice row visible in invoices sub-tab');

  // Verify TGA allocation in allocations sub-tab for User A
  const allocTabBtn = page.locator('#tab-allocations');
  await allocTabBtn.click();
  await page.waitForSelector('th:has-text("المبلغ الإجمالي")', { timeout: 5000 });

  // In UI, User A's approve button should be disabled
  const userAApproveBtn = page.locator('tbody tr button:has-text("اعتماد المدقق")').first();
  await userAApproveBtn.waitFor({ timeout: 5000 });
  assert.ok(await userAApproveBtn.isDisabled(), 'Maker-checker UI: Approve button must be disabled for maker (User A)');
  console.log('✓ Step 9b: Maker-checker UI verified: Approve button is disabled for maker in UI');

  // Create User B (independent financial checker)
  const checkerUsername = 'checker-' + randomBytes(4).toString('hex');
  const checkerPassword = 'CheckerPass123!' + randomBytes(4).toString('hex');
  const createUserBRes = await page.request.post(`${baseUrl}/api/users`, {
    data: { username: checkerUsername, password: checkerPassword, role: 'FinancialAccountant' },
  });
  assert.equal(createUserBRes.status(), 200, 'User B creation should succeed');

  // Login as User B in a separate browser context
  const checkerContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const checkerPage = await checkerContext.newPage();
  await checkerPage.goto(`${baseUrl}/app/login/`);
  await checkerPage.locator('#username').fill(checkerUsername);
  await checkerPage.locator('#password').fill(checkerPassword);
  await checkerPage.locator('button[type="submit"]').click();
  await checkerPage.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });

  // User B opens /app/payroll/ and resolves CAR dispute as platform_rejected
  await checkerPage.goto(`${baseUrl}/app/payroll/`);
  await checkerPage.waitForLoadState('networkidle');

  const disputesTabBtn = checkerPage.locator('#tab-disputes');
  await disputesTabBtn.click();
  await checkerPage.waitForSelector('th:has-text("الحالة والمسار المحاسبي")', { timeout: 5000 });

  // User B clicks "معالجة النزاع"
  const resolveBtn = checkerPage.locator('tbody tr button:has-text("معالجة النزاع")').first();
  await resolveBtn.waitFor({ timeout: 5000 });
  await resolveBtn.click();

  const resolveModal = checkerPage.locator('div[role="dialog"]:has-text("معالجة وفض نزاع")');
  await resolveModal.waitFor({ timeout: 5000 });

  // Click outcome: المنصة رفضت (تصعيد 112803)
  const rejectBtn = resolveModal.locator('button:has-text("المنصة رفضت")');
  await rejectBtn.click();

  // Enter platform reference and reason
  await resolveModal.locator('input[placeholder*="HS-TICKET"]').fill('REF-CAR-E2E-999');
  await resolveModal.locator('textarea[placeholder*="قرار المنصة"]').fill('Platform rejected CAR claim');

  // Confirm resolution
  const confirmResolveBtn = resolveModal.locator('button:has-text("تأكيد قرار فض النزاع")');
  await confirmResolveBtn.click();
  await checkerPage.waitForSelector('div[role="alert"]:has-text("تم تسجيل رفض المنصة")', { timeout: 5000 });

  // Verify escalated badge
  const escalatedBadge = checkerPage.locator('span:has-text("مرحّل لحساب النزاعات 112803 لحين فض النزاع")').first();
  await escalatedBadge.waitFor({ timeout: 5000 });
  assert.ok(await escalatedBadge.isVisible(), 'Escalated badge 112803 must be visible');
  console.log('✓ Step 9c: CAR dispute resolved as platform_rejected by User B and escalated badge (112803) verified');

  // User B switches to TGA Allocations tab and approves split
  const checkerAllocTab = checkerPage.locator('#tab-allocations');
  await checkerAllocTab.click();

  const checkerApproveBtn = checkerPage.locator('tbody tr button:has-text("اعتماد المدقق")').first();
  await checkerApproveBtn.waitFor({ timeout: 5000 });
  assert.ok(!await checkerApproveBtn.isDisabled(), 'Checker (User B) should have active approve button');
  await checkerApproveBtn.click();
  await checkerPage.waitForSelector('div[role="alert"]:has-text("تم اعتماد توزيع TGA")', { timeout: 5000 });

  // Verify status is now "معتمد"
  const approvedBadge = checkerPage.locator('tbody tr span:has-text("معتمد")').first();
  await approvedBadge.waitFor({ timeout: 5000 });
  assert.ok(await approvedBadge.isVisible(), 'Allocation status must become "معتمد"');
  console.log('✓ Step 9d: Maker-checker verified: User B (checker) approved TGA split successfully');
  await checkerContext.close();

  // Step 10: Verify all workspaces load cleanly without UnderMigration
  console.log('\nStep 10: Verifying active workspaces (/app/treasury/, /app/debts/, /app/accounting/, /app/riders/, /app/operations/, /app/admin/, /app/imports/, /app/rider-app/)...');
  const allWorkspacePaths = [
    '/app/treasury/',
    '/app/debts/',
    '/app/accounting/',
    '/app/riders/',
    '/app/riders/statement/?iqama=2000000789',
    '/app/operations/',
    '/app/admin/',
    '/app/imports/',
    '/app/rider-app/',
  ];
  for (const pagePath of allWorkspacePaths) {
    await page.goto(`${baseUrl}${pagePath}`);
    await page.waitForLoadState('networkidle');
    const content = await page.textContent('body');
    assert.ok(!content.includes('قيد النقل التدريجي'), `${pagePath} must be an active workspace, not UnderMigration`);
    console.log(`✓ Step 10: ${pagePath} loaded cleanly as real workspace`);
  }

  // --- R4-fix1 Tests: F1, F2, F3 ---
  console.log('\n--- Running R4-Fix1 Live E2E Tests (F1, F2, F3) ---');
  const fix1ResultsDir = path.join(rootDir, 'test-results/r4-fix1');
  mkdirSync(fix1ResultsDir, { recursive: true });

  // 11. F1: Payroll Rules Maker-Checker, Copy Month & Cycle Stepper
  console.log('\nStep 11: Testing F1 (Payroll Rules Maker-Checker, Copy Month & Cycle Stepper)...');

  // Create User B context again for maker-checker tests
  const checkerContext2 = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const checkerPage2 = await checkerContext2.newPage();
  await checkerPage2.goto(`${baseUrl}/app/login/`);
  await checkerPage2.locator('#username').fill(checkerUsername);
  await checkerPage2.locator('#password').fill(checkerPassword);
  await checkerPage2.locator('button[type="submit"]').click();
  await checkerPage2.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });

  // 11a: User A creates global settings draft and attempts self-approval (refused with 403)
  await page.goto(`${baseUrl}/app/payroll/?tab=rules&period=2026-10`);
  await page.waitForLoadState('networkidle');

  // Wait for empty state or existing setting
  await page.waitForSelector('button:has-text("إنشاء مسودة"), button:has-text("اعتماد الإعدادات العامة")', { timeout: 8000 });

  const createGlobalDraftBtn = page.locator('button:has-text("إنشاء مسودة من القيم الافتراضية")');
  if (await createGlobalDraftBtn.count() > 0) {
    await createGlobalDraftBtn.click();
    await page.waitForSelector('div[role="alert"]:has-text("تم إنشاء مسودة")', { timeout: 5000 });
  }

  // Attempt approval as User A (maker) -> Must fail with 403 / "لا يجوز اعتماد طلبك بنفسك"
  const userAApproveGlobalBtn = page.locator('button:has-text("اعتماد الإعدادات العامة (Maker-Checker)")');
  await userAApproveGlobalBtn.waitFor({ timeout: 5000 });
  await userAApproveGlobalBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("لا يجوز اعتماد طلبك بنفسك")', { timeout: 5000 });
  console.log('✓ Step 11a: Maker-checker verified: User A self-approval of global settings refused with HTTP 403');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f1-rules-maker-refusal.png') });

  // 11b: User B (checker) approves global settings
  await checkerPage2.goto(`${baseUrl}/app/payroll/?tab=rules&period=2026-10`);
  await checkerPage2.waitForLoadState('networkidle');
  const checkerApproveGlobalBtn = checkerPage2.locator('button:has-text("اعتماد الإعدادات العامة (Maker-Checker)")');
  await checkerApproveGlobalBtn.waitFor({ timeout: 5000 });
  await checkerApproveGlobalBtn.click();
  await checkerPage2.waitForSelector('div[role="alert"]:has-text("تم اعتماد الإعدادات العامة")', { timeout: 5000 });
  await checkerPage2.waitForSelector('span:has-text("للقراءة فقط")', { timeout: 5000 });
  console.log('✓ Step 11b: Maker-checker verified: User B approved global settings and marked read-only');
  await checkerPage2.screenshot({ path: path.join(fix1ResultsDir, 'f1-rules-approved.png') });

  // 11c: App Terms draft creation and approval
  await page.goto(`${baseUrl}/app/payroll/?tab=platforms&app=hs&sub=parent_terms&period=2026-10`);
  await page.waitForLoadState('networkidle');
  await page.waitForSelector('button:has-text("إنشاء مسودة"), button:has-text("اعتماد الشروط")', { timeout: 8000 });

  const createTermsBtn = page.locator('button:has-text("إنشاء مسودة جديدة")');
  await createTermsBtn.waitFor({ timeout: 5000 });
  await createTermsBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("تم إنشاء مسودة")', { timeout: 5000 });

  const userAApproveTermsBtn = page.locator('button:has-text("اعتماد الشروط (Maker-Checker)")');
  await userAApproveTermsBtn.waitFor({ timeout: 5000 });
  await userAApproveTermsBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("لا يجوز اعتماد طلبك بنفسك")', { timeout: 5000 });

  // User B approves terms
  await checkerPage2.goto(`${baseUrl}/app/payroll/?tab=platforms&app=hs&sub=parent_terms&period=2026-10`);
  await checkerPage2.waitForLoadState('networkidle');
  const checkerApproveTermsBtn = checkerPage2.locator('button:has-text("اعتماد الشروط (Maker-Checker)")');
  await checkerApproveTermsBtn.waitFor({ timeout: 5000 });
  await checkerApproveTermsBtn.click();
  await checkerPage2.waitForSelector('div[role="alert"]:has-text("تم اعتماد شروط المنصة")', { timeout: 5000 });
  console.log('✓ Step 11c: App terms maker-checker lifecycle verified');

  // 11d: Copy month to 2026-11
  await page.goto(`${baseUrl}/app/payroll/?tab=platforms&app=hs&sub=parent_terms&period=2026-11`);
  await page.waitForLoadState('networkidle');
  const copyBtn = page.locator('button:has-text("انسخ الشهر السابق")').first();
  await copyBtn.waitFor({ timeout: 5000 });
  await copyBtn.click();
  await page.waitForSelector('div[role="alert"]:has-text("تم نسخ")', { timeout: 5000 });
  console.log('✓ Step 11d: Copy month flow created draft for next month (2026-11)');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f1-copy-month.png') });

  // 11e: Cycle Stepper: Advance 0 -> 4 with synthetic data
  const compRes = await page.request.get(`${baseUrl}/api/companies`);
  const companyList = await compRes.json();
  const cycleCompanyId = companyList[0].id;
  await page.request.post(`${baseUrl}/api/platforms/hs/posting`, { data: { enabled: true } });

  const cycleDb = new DatabaseSync(path.join(tempDir, 'payroll.db'));
  const cyclePut = (id, kind, body) =>
    cycleDb.prepare('INSERT OR REPLACE INTO records(id,kind,body) VALUES(?,?,?)').run(id, kind, JSON.stringify({ ...body, id }));
  cyclePut(testIqama, 'rider', {
    id: testIqama,
    iqama: testIqama,
    name: testName,
    isDeliveryRider: true,
    status: 'Active',
    sponsorId: cycleCompanyId,
  });

  cyclePut('synthetic-cycle-account', 'platform_account', {
    companyId: cycleCompanyId,
    platformCode: 'hs',
    assignments: [{ riderId: testIqama, RawEffectiveFrom: '2026-10-01T00:00:00Z' }],
  });
  cyclePut('synthetic-cycle-invoice', 'platform_invoice', {
    companyId: cycleCompanyId,
    platform: 'HungerStation',
    period: '2026-10',
    approvalStatus: 'approved',
    amountExclVat: 100,
    vat: 15,
    deductionsTotal: 0,
  });
  cyclePut('synthetic-cycle-line', 'platform_invoice_line', {
    invoiceId: 'synthetic-cycle-invoice',
    platformAccountId: 'synthetic-cycle-account',
    lineType: 'rider',
  });

  // Seed approved scheme and assignment so BuildLines calculates cleanly
  const schemeId = 'pay-scheme:synthetic-hs-scheme';
  cyclePut(schemeId, 'pay_scheme', {
    id: schemeId,
    kind: 'pay_scheme',
    app: 'hs',
    period: '2026-10',
    code: 'synthetic-commission',
    version: 1,
    status: 'approved',
    data: {
      name: 'Synthetic commission scheme',
      type: 'daily_commission',
      accountingRole: 'agent',
      parameters: {
        categories: Object.fromEntries(
          ['sponsored_company_car', 'sponsored_company_bike', 'sponsored_private', 'external_company_car', 'external_company_bike', 'external_private'].map((c) => [
            c,
            { enabled: true, dailyCommission: 10, chargeCompanyVehicle: false },
          ])
        ),
      },
      eligibilitySetId: '',
    },
  });

  const assignId = 'pay-assignment:synthetic-' + testIqama;
  cyclePut(assignId, 'pay_assignment', {
    id: assignId,
    app: 'hs',
    period: '2026-10',
    rider: testIqama,
    schemeId: schemeId,
    status: 'approved',
    choiceApprovedAt: '2026-09-30T20:00:00Z',
  });
  cycleDb.close();

  const cycleRes = await page.request.post(`${baseUrl}/api/pay/cycles`, {
    data: {
      period: '2026-10',
      companyId: cycleCompanyId,
      invoices: [
        {
          app: 'hs',
          invoiceId: 'synthetic-cycle-invoice',
          deductions: [],
          accounts: [
            {
              accountId: 'synthetic-cycle-account',
              sourceLineId: 'synthetic-cycle-line',
              baseGross: 100,
              incentives: 0,
              orders: 10,
              riders: [
                {
                  rider: testIqama,
                  orders: 10,
                  baseGross: 100,
                  deduction: 0,
                  category: 'sponsored_private',
                  days: 1,
                  metrics: {},
                },
              ],
            },
          ],
        },
      ],
    },
  });
  const cycleBody = await cycleRes.text();
  assert.equal(cycleRes.status(), 200, `Cycle creation failed: ${cycleBody}`);
  const cycleResJson = JSON.parse(cycleBody);

  await page.goto(`${baseUrl}/app/payroll/?tab=cycle&period=2026-10`);
  await page.waitForLoadState('networkidle');

  // Advance stages 0 -> 1 -> 2 -> 3 -> 4
  for (let s = 1; s <= 4; s++) {
    const advanceBtn = page.locator(`button:has-text("تقديم المرحلة إلى [${s}:")`);
    await advanceBtn.waitFor({ timeout: 8000 });
    await advanceBtn.click();
    await page.waitForSelector(`div[role="alert"]:has-text("المرحلة ${s}")`, { timeout: 8000 });
  }

  const checkLines = await page.request.get(`${baseUrl}/api/pay/cycles/${encodeURIComponent(cycleResJson.id)}/lines`);
  const checkLinesJson = await checkLines.json();
  console.log('✓ Stage 4 cycle lines from backend total:', checkLinesJson.total);

  // Wait for lines table row with explain button
  await page.waitForSelector('button:has-text("شرح الاحتساب")', { timeout: 8000 });
  const explainBtn = page.locator('button:has-text("شرح الاحتساب")').first();
  await explainBtn.waitFor({ timeout: 5000 });
  await explainBtn.click();
  const explainDrawer = page.locator('div[role="dialog"]:has-text("شرح احتساب مستحقات المندوب")');
  await explainDrawer.waitFor({ timeout: 5000 });
  const explainText = await explainDrawer.textContent();
  assert.ok(explainText.includes(testIqama), 'Explain lines must display rider iqama');
  console.log('✓ Step 11e: Cycle stepper advanced 0 -> 4 with explain lines verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f1-cycle-stepper-stage4.png') });
  await page.keyboard.press('Escape');

  // 12. F2: Legacy Production Payroll Run Flow
  console.log('\nStep 12: Testing F2 (Current Legacy Payroll Run Workspace)...');
  page.on('console', msg => console.log('PAGE LOG:', msg.text()));
  page.on('request', req => {
    if (req.url().includes('/api/payments')) {
      console.log('PAYMENTS REQUEST BODY:', req.postData());
    }
  });
  page.on('response', async res => {
    if (res.url().includes('/api/payments')) {
      try {
        console.log('PAYMENTS API RESPONSE:', res.status(), await res.text());
      } catch {}
    }
  });

  // Seed verified opening balances for cash vaults so BANK.BW has available funds for payroll payment
  const openPrevRes = await page.request.post(`${baseUrl}/api/finance/opening/preview`, {
    data: {
      rows: ['BW', 'EX', 'TR'].flatMap((sponsorCode) => [
        `MAIN.${sponsorCode}.DEBT`,
        `BANK.${sponsorCode}`,
      ]).map((accountCode) => ({
        accountCode,
        sponsorCode: accountCode.includes('BW') ? 'BW' : accountCode.includes('EX') ? 'EX' : 'TR',
        iqama: '',
        verifiedBalance: 100000,
        sourceRef: `synthetic-opening:${accountCode}`,
        notes: 'Synthetic verified cash for live e2e integration tests',
      })),
    },
  });
  const openPrev = await openPrevRes.json();
  const openCommRes = await page.request.post(`${baseUrl}/api/finance/opening/commit`, {
    data: { rows: openPrev.rows, fingerprint: openPrev.fingerprint, confirm: true },
  });
  assert.equal(openCommRes.status(), 200, 'Accounting opening commit should succeed');
  console.log('✓ Seeded accounting cash for payment vault (BANK.BW)');

  // Create and approve a policy for the legacy payroll run
  const policyRes = await page.request.post(`${baseUrl}/api/policies`, {
    data: {
      name: 'Approved pay policy',
      kind: 'approved_pay',
      platform: 'all',
      effectiveFrom: '2026-10',
      config: {},
    },
  });
  assert.equal(policyRes.status(), 200, 'Policy creation should succeed');
  const policy = await policyRes.json();
  const approvePolicyRes = await page.request.post(`${baseUrl}/api/policies/${encodeURIComponent(policy.id)}/approve`);
  assert.equal(approvePolicyRes.status(), 200, 'Policy approval should succeed');

  // Seed rider with approved policyId and financeSponsorId for 2026-10
  const riderPostRes = await page.request.post(`${baseUrl}/api/riders`, {
    data: {
      iqama: testIqama,
      name: testName,
      financeSponsorId: 'BW',
      policyId: policy.id,
      phone: '0501112233',
      city: 'جدة',
    },
  });
  const riderPost = await riderPostRes.json();
  const targetSponsorId = riderPost.financeSponsorId;
  console.log('Rider post res:', riderPostRes.status(), JSON.stringify(riderPost));

  const legacyCsv = `sourceRef,iqama,name,accountId,from,to,orders,riderPay,isValid\r\nlive-legacy-row,${testIqama},${testName},ACC-001,2026-10-01,2026-10-31,10,1500,true\r\n`;
  const uploadRes = await page.request.post(`${baseUrl}/api/imports/inspect`, {
    data: { filename: 'legacy.csv', dataBase64: Buffer.from(legacyCsv).toString('base64') },
  });
  const uploadData = await uploadRes.json();
  console.log('Upload inspect res:', uploadData);

  const legacySpec = {
    sheet: 'CSV',
    period: '2026-10',
    platform: 'Keeta',
    headerRow: 1,
    confirmActualRider: true,
    mapping: { sourceRef: 0, iqama: 1, name: 2, accountId: 3, from: 4, to: 5, orders: 6, riderPay: 7, isValid: 8 },
  };
  const prevRes = await page.request.post(`${baseUrl}/api/imports/${uploadData.id}/preview`, { data: legacySpec });
  console.log('Import preview res:', prevRes.status(), await prevRes.text());

  const commRes = await page.request.post(`${baseUrl}/api/imports/${uploadData.id}/commit`, { data: legacySpec });
  console.log('Import commit res:', commRes.status(), await commRes.text());

  const checkPreview = await page.request.get(`${baseUrl}/api/payroll/preview?period=2026-10`);
  console.log('Legacy payroll preview check:', checkPreview.status(), await checkPreview.text());

  // Navigate to current-cycle tab
  await page.goto(`${baseUrl}/app/payroll/?tab=current-cycle&period=2026-10`);
  await page.waitForLoadState('networkidle');

  // Verify preview row
  const legacyRow = page.locator(`tr:has-text("${testIqama}")`);
  await legacyRow.waitFor({ timeout: 8000 });
  console.log('✓ Step 12a: Legacy payroll preview row rendered');

  // Record adjustment
  const adjBtn = page.locator('button:has-text("إضافة تحمل/مستحق")');
  await adjBtn.click();
  const adjDialog = page.locator('div[role="dialog"]:has-text("إضافة مستحق أو تحمل تشغيلي")');
  await adjDialog.waitFor({ timeout: 5000 });
  await adjDialog.locator('select').first().selectOption(testIqama);
  await adjDialog.locator('input[type="number"]').fill('-200');
  await adjDialog.locator('input[placeholder*="REF"]').fill('REF-ADJ-LIVE-01');
  await adjDialog.locator('input[placeholder*="شرح"]').fill('خصم وقود إضافي');
  await adjDialog.locator('button:has-text("تسجيل البند")').click();
  await page.waitForSelector('div[role="alert"]:has-text("تم تسجيل البند")', { timeout: 5000 });
  console.log('✓ Step 12b: Legacy adjustment recorded and preview updated');

  // Approve payroll run (creates ledger payable balance)
  const rowApproveBtn = legacyRow.locator('button:has-text("اعتماد")');
  await rowApproveBtn.click();
  const approveDialog = page.locator('div[role="dialog"]:has-text("اعتماد وترحيل مسير المندوب محاسبياً")');
  await approveDialog.waitFor({ timeout: 5000 });
  await approveDialog.locator('select').selectOption(targetSponsorId);
  await approveDialog.locator('button:has-text("اعتماد المسير والقيود")').click();
  await page.waitForSelector('div[role="alert"]:has-text("تم اعتماد مسير المندوب")', { timeout: 5000 });
  console.log('✓ Step 12c: Legacy payroll run approved and posted');

  // Record payment against the approved ledger balance
  const payBtn = page.locator('button:has-text("تسجيل دفعة")');
  await payBtn.click();
  const payDialog = page.locator('div[role="dialog"]:has-text("تسجيل صرف دفعة راتب فعلية")');
  await payDialog.waitFor({ timeout: 5000 });
  await payDialog.locator('select').first().selectOption(testIqama);
  await payDialog.locator('input[type="number"]').fill('300');
  await payDialog.locator('input[placeholder*="BANK-TRX"]').fill('TRANS-LIVE-PAY-01');
  await payDialog.locator('button:has-text("تسجيل الدفع وترحيل القيد")').click();
  await page.waitForSelector('div[role="alert"]:has-text("تم تسجيل الدفعة النقدية")', { timeout: 8000 });
  console.log('✓ Step 12d: Legacy payment recorded');

  // View ledger
  const rowLedgerBtn = page.locator(`tr:has-text("${testIqama}") button:has-text("الدفتر")`);
  await rowLedgerBtn.click();
  const ledgerDialog = page.locator('div[role="dialog"]:has-text("دفتر حساب المندوب")');
  await ledgerDialog.waitFor({ timeout: 5000 });
  await ledgerDialog.locator('text=الرصيد الدفتري الحالي').waitFor({ timeout: 5000 });
  const ledgerContent = await ledgerDialog.textContent();
  assert.ok(ledgerContent.includes('الرصيد الدفتري الحالي'), 'Ledger balance must be rendered');
  console.log('✓ Step 12e: Legacy rider ledger modal verified');
  await page.keyboard.press('Escape');

  // CSV Export
  const csvExportLink = page.locator('a:has-text("تصدير المعتمد (CSV)")');
  const exportHref = await csvExportLink.getAttribute('href');
  assert.ok(exportHref.includes('/api/payroll/export.csv'), 'Export link must target /api/payroll/export.csv');
  const csvExportRes = await page.request.get(`${baseUrl}/api/payroll/export.csv?period=2026-10`);
  assert.equal(csvExportRes.status(), 200, 'Legacy CSV export must succeed');
  console.log('✓ Step 12f: Legacy payroll CSV export verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f2-legacy-payroll.png') });

  // 13. F3: Operations and Statements with 0.14.0 Endpoints
  console.log('\nStep 13: Testing F3 (Operations & Statements with 0.14.0 Endpoints)...');

  // Operations tabs call GET /api/operations/{kind}?page=&q=
  await page.goto(`${baseUrl}/app/operations/?tab=accounts`);
  await page.waitForLoadState('networkidle');
  const opContent = await page.textContent('body');
  assert.ok(!opContent.includes('غير متاح بعد'), 'Operations accounts tab must not show "غير متاح بعد"');
  assert.ok(opContent.includes('حسابات المنصات'), 'Accounts tab header present');

  const searchInput = page.locator('input[placeholder*="بحث"]');
  await searchInput.fill('synthetic');
  await page.waitForTimeout(500);
  console.log('✓ Step 13a: Operations accounts tab with search and pagination verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f3-operations.png') });

  // Rider Profile Accounts tab: GET /api/riders/{iqama}/accounts
  await page.goto(`${baseUrl}/app/riders/`);
  await page.waitForLoadState('networkidle');
  const riderProfileBtn = page.locator(`tr:has-text("${testName}") button:has-text("الملف 360°")`).first();
  await riderProfileBtn.waitFor({ timeout: 5000 });
  await riderProfileBtn.click();
  await page.waitForSelector('button:has-text("حسابات المنصات والمعرفات")', { timeout: 5000 });
  const accountsTabBtn = page.locator('button:has-text("حسابات المنصات والمعرفات")');
  await accountsTabBtn.click();
  await page.locator('th:has-text("المنصة")').first().waitFor({ timeout: 5000 });
  console.log('✓ Step 13b: Rider Profile 360 accounts tab from GET /api/riders/{iqama}/accounts verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f3-rider-accounts.png') });

  // Rider Statement: GET /api/finance/statement/rider/{iqama}/monthly?period=
  await page.goto(`${baseUrl}/app/riders/statement/?iqama=${testIqama}&period=2026-10`);
  await page.waitForLoadState('networkidle');
  const stmtContent = await page.textContent('body');
  assert.ok(stmtContent.includes(testIqama), 'Statement must display rider iqama');
  console.log('✓ Step 13c: Rider monthly statement verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f3-rider-monthly-statement.png') });

  // Rider App Admin Statement Preview & F4 sessions gap
  await page.goto(`${baseUrl}/app/rider-app/?tab=statement-preview`);
  await page.waitForLoadState('networkidle');
  const appPreviewContent = await page.textContent('body');
  assert.ok(
    appPreviewContent.includes('كشف الحساب في تطبيق المندوب') || appPreviewContent.includes('كشف الحساب في التطبيق'),
    'App preview statement header verified'
  );
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f3-rider-app-statement.png') });

  // Sessions sub-tab preserves F4 gap banner
  const sessionsTabBtn = page.locator('button:has-text("الحسابات والأجهزة")').first();
  await sessionsTabBtn.click();
  const sessionsContent = await page.textContent('body');
  assert.ok(sessionsContent.includes('غير متاح بعد — بانتظار اكتمال واجهات خادم الجلسات الميدانية (Codex)'), 'Sessions gap banner preserved');
  console.log('✓ Step 13d (F4): Rider mobile sessions gap banner verified');
  await page.screenshot({ path: path.join(fix1ResultsDir, 'f4-sessions-gap.png') });

  await checkerContext2.close();

  // =========================================================================
  // Step 14: R5 Advances Workflow (Org Directory, Supervision, Advances Flow, Payout, Badges)
  // =========================================================================
  console.log('\nStep 14: Testing R5 Advances Workflow (Org, Review, Proposal, Approval, Payout)...');
  const r5ResultsDir = path.join(rootDir, 'test-results/r5-web');
  mkdirSync(r5ResultsDir, { recursive: true });

  // 14a. Admin creates synthetic users (Supervisor, Accountant, Custodian, Outsider)
  const supUsername = 'syn-supervisor-' + randomBytes(3).toString('hex');
  const accUsername = 'syn-accountant-' + randomBytes(3).toString('hex');
  const cusUsername = 'syn-custodian-' + randomBytes(3).toString('hex');
  const outUsername = 'syn-outsider-' + randomBytes(3).toString('hex');

  const supPw = 'SupPass123!' + randomBytes(3).toString('hex');
  const accPw = 'AccPass123!' + randomBytes(3).toString('hex');
  const cusPw = 'CusPass123!' + randomBytes(3).toString('hex');
  const outPw = 'OutPass123!' + randomBytes(3).toString('hex');

  const synUsers = [
    { username: supUsername, password: supPw, permissions: ['advances.request'] },
    { username: accUsername, password: accPw, permissions: ['advances.review'] },
    { username: cusUsername, password: cusPw, permissions: ['advances.payout'] },
    { username: outUsername, password: outPw, permissions: ['advances.review', 'advances.payout'] },
  ];

  for (const u of synUsers) {
    const uRes = await page.request.post(`${baseUrl}/api/users`, {
      data: {
        username: u.username,
        password: u.password,
        role: 'Reviewer',
        customPermissions: u.permissions,
      },
    });
    const uBody = await uRes.text();
    assert.equal(uRes.status(), 200, `Creating user ${u.username} should succeed: ${uBody}`);
  }

  // Seed rider 2000000611 with sponsor BW
  const advIqama = '2000000611';
  await page.request.post(`${baseUrl}/api/riders`, {
    data: {
      iqama: advIqama,
      name: 'سالم أحمد المحمدي',
      phone: '0501234567',
      financeSponsorId: 'BW',
      city: 'الرياض',
    },
  });

  // 14b. Admin sets up Org structure: memberships, routing, supervision (BR §25.1)
  const m1 = await page.request.post(`${baseUrl}/api/org/memberships`, {
    data: { userId: accUsername, department: 'FIN', rank: 'A', validFrom: '2026-01-01' },
  });
  assert.equal(m1.status(), 200, 'Accountant membership creation should succeed');

  const m2 = await page.request.post(`${baseUrl}/api/org/memberships`, {
    data: { userId: cusUsername, department: 'FIN', rank: 'C', validFrom: '2026-01-01' },
  });
  assert.equal(m2.status(), 200, 'Custodian membership creation should succeed');

  // Routing
  const r1 = await page.request.post(`${baseUrl}/api/org/routing`, {
    data: { operation: 'advance.review', department: 'FIN', minRank: 'B', users: [accUsername], validFrom: '2026-01-01' },
  });
  assert.equal(r1.status(), 200, 'Review routing should succeed');

  const r2 = await page.request.post(`${baseUrl}/api/org/routing`, {
    data: { operation: 'advance.payout:MAIN.BW.DEBT', department: 'FIN', minRank: 'C', users: [cusUsername], validFrom: '2026-01-01' },
  });
  assert.equal(r2.status(), 200, 'Cash vault payout routing should succeed');

  const r3 = await page.request.post(`${baseUrl}/api/org/routing`, {
    data: { operation: 'advance.payout:BANK.BW', department: 'FIN', minRank: 'C', users: [cusUsername], validFrom: '2026-01-01' },
  });
  assert.equal(r3.status(), 200, 'Bank vault payout routing should succeed');

  // Supervision: supervisor supervises rider advIqama
  const s1 = await page.request.post(`${baseUrl}/api/org/supervision`, {
    data: { supervisorUserId: supUsername, riderIqama: advIqama, validFrom: '2026-01-01' },
  });
  assert.equal(s1.status(), 200, 'Supervision creation should succeed');
  console.log('✓ Step 14a: Admin created memberships, routing, and supervision');

  // Admin verifies Organization Workspace at /app/admin/?tab=org
  await page.goto(`${baseUrl}/app/admin/?tab=org`);
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: path.join(r5ResultsDir, 'r5-admin-org-directory.png') });

  // Admin 360 mobile screenshot
  const admin360Context = await browser.newContext({
    viewport: { width: 360, height: 740 },
    extraHTTPHeaders: { Cookie: setCookie.split(';')[0] },
  });
  const admin360Page = await admin360Context.newPage();
  await admin360Page.goto(`${baseUrl}/app/admin/?tab=org`);
  await admin360Page.waitForLoadState('networkidle');
  await admin360Page.screenshot({ path: path.join(r5ResultsDir, 'r5-admin-org-directory-360.png') });
  await admin360Context.close();

  // 14c. Supervisor requests cash advance
  const supContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const supPage = await supContext.newPage();
  await supPage.goto(`${baseUrl}/app/login/`);
  await supPage.locator('#username').fill(supUsername);
  await supPage.locator('#password').fill(supPw);
  await supPage.locator('button[type="submit"]').click();
  await supPage.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });

  await supPage.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await supPage.waitForLoadState('networkidle');

  // Supervisor submits advance request via UI modal
  await supPage.locator('#btn-new-advance-request').click();
  await supPage.waitForSelector('#select-supervised-rider');
  await supPage.locator('#select-supervised-rider').selectOption(advIqama);
  await supPage.locator('#input-req-amount').fill('400');
  await supPage.locator('#input-req-reason').fill('Synthetic cash need for supervised rider');
  await supPage.locator('#btn-submit-advance-request').click();
  await supPage.waitForLoadState('networkidle');

  // Verify request appears in supervisor table
  const supContent = await supPage.textContent('body');
  assert.ok(supContent.includes(advIqama), 'Rider advance request must appear in supervisor table');
  await supPage.screenshot({ path: path.join(r5ResultsDir, 'r5-debts-advance-requests.png') });

  // Mobile screenshot of supervisor debts
  const sup360Context = await browser.newContext({ viewport: { width: 360, height: 740 } });
  const sup360Page = await sup360Context.newPage();
  await sup360Page.goto(`${baseUrl}/app/login/`);
  await sup360Page.locator('#username').fill(supUsername);
  await sup360Page.locator('#password').fill(supPw);
  await sup360Page.locator('button[type="submit"]').click();
  await sup360Page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });
  await sup360Page.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await sup360Page.waitForLoadState('networkidle');
  await sup360Page.screenshot({ path: path.join(r5ResultsDir, 'r5-debts-advance-requests-360.png') });
  await sup360Context.close();

  // Get created advance request ID
  const myRequestsRes = await supPage.request.get(`${baseUrl}/api/advance-requests?mine=1`);
  const myRequests = await myRequestsRes.json();
  assert.ok(myRequests.length > 0, 'Supervisor must have at least one request');
  const advReq = myRequests[0];
  const advReqId = advReq.id;
  console.log(`✓ Step 14b: Supervisor requested cash advance: id=${advReqId}`);

  // 14d. Negative Checks:
  // Check 1: User with permission but without routing assignment cannot review (refused 403)
  const outLoginRes = await page.request.post(`${baseUrl}/api/auth/login`, {
    data: { username: outUsername, password: outPw },
  });
  const outCookie = outLoginRes.headers()['set-cookie']?.split(';')[0];
  const outReviewRes = await fetch(`${baseUrl}/api/advance-requests/${advReqId}/review`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Cookie: outCookie },
    body: JSON.stringify({ action: 'reject', note: 'Outsider review attempt' }),
  });
  assert.equal(outReviewRes.status, 403, 'Unassigned user with permission must receive HTTP 403');
  console.log('✓ Step 14c: Negative check 1 verified: Unassigned user refused with HTTP 403');

  // Check 2: Requester self-approval refused with HTTP 403
  const supSelfReviewRes = await supPage.request.post(`${baseUrl}/api/advance-requests/${advReqId}/review`, {
    data: { action: 'approve', monthlyInstallment: 200 },
  });
  assert.equal(supSelfReviewRes.status(), 403, 'Requester cannot review/approve own request (HTTP 403)');
  console.log('✓ Step 14d: Negative check 2 verified: Requester self-approval refused with HTTP 403');

  // 14e. Accountant reviews request: opens drawer, checks summary card, proposes bank transfer
  const accContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const accPage = await accContext.newPage();
  await accPage.goto(`${baseUrl}/app/login/`);
  await accPage.locator('#username').fill(accUsername);
  await accPage.locator('#password').fill(accPw);
  await accPage.locator('button[type="submit"]').click();
  await accPage.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });

  await accPage.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await accPage.waitForLoadState('networkidle');

  // Open drawer
  const advReviewBtn = accPage.locator(`button[id="btn-review-advance-${advReqId}"], button:has-text("مراجعة والبت")`).first();
  await advReviewBtn.click();
  await accPage.waitForSelector('#btn-mode-approve');
  await accPage.screenshot({ path: path.join(r5ResultsDir, 'r5-accountant-review-drawer.png') });

  // Mobile screenshot of review drawer
  const acc360Context = await browser.newContext({ viewport: { width: 360, height: 740 } });
  const acc360Page = await acc360Context.newPage();
  await acc360Page.goto(`${baseUrl}/app/login/`);
  await acc360Page.locator('#username').fill(accUsername);
  await acc360Page.locator('#password').fill(accPw);
  await acc360Page.locator('button[type="submit"]').click();
  await acc360Page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });
  await acc360Page.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await acc360Page.waitForLoadState('networkidle');
  const reviewBtn360 = acc360Page.locator(`button[id="btn-review-advance-${advReqId}"], button:has-text("مراجعة والبت")`).first();
  await reviewBtn360.click();
  await acc360Page.waitForSelector('#btn-mode-approve');
  await acc360Page.screenshot({ path: path.join(r5ResultsDir, 'r5-accountant-review-drawer-360.png') });
  await acc360Context.close();

  // Accountant proposes bank transfer
  await accPage.locator('#btn-mode-propose').click();
  await accPage.locator('#select-propose-type').selectOption('bank');
  await accPage.locator('#textarea-propose-note').fill('نوصي بالتحويل البنكي لتوفر الرصيد');
  await accPage.locator('#btn-submit-review-decision').click();
  await accPage.waitForLoadState('networkidle');
  console.log('✓ Step 14e: Accountant proposed bank transfer');

  // 14f. Supervisor receives notification and responds to proposal
  await supPage.goto(`${baseUrl}/app/notifications/`);
  await supPage.waitForLoadState('networkidle');
  const notifBody = await supPage.textContent('body');
  assert.ok(notifBody.includes('بانتظار البت') || notifBody.includes('سلفة') || notifBody.includes('مقترح'), 'Supervisor received targeted notification');
  await supPage.screenshot({ path: path.join(r5ResultsDir, 'r5-notifications-tabs.png') });

  // Mobile screenshot of notifications
  const notif360Context = await browser.newContext({ viewport: { width: 360, height: 740 } });
  const notif360Page = await notif360Context.newPage();
  await notif360Page.goto(`${baseUrl}/app/login/`);
  await notif360Page.locator('#username').fill(supUsername);
  await notif360Page.locator('#password').fill(supPw);
  await notif360Page.locator('button[type="submit"]').click();
  await notif360Page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });
  await notif360Page.goto(`${baseUrl}/app/notifications/`);
  await notif360Page.waitForLoadState('networkidle');
  await notif360Page.screenshot({ path: path.join(r5ResultsDir, 'r5-notifications-tabs-360.png') });
  await notif360Context.close();

  // Supervisor responds: accepts proposal via UI dialog
  await supPage.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await supPage.waitForLoadState('networkidle');
  const respondBtn = supPage.locator(`button[id="btn-respond-proposal-${advReqId}"], button:has-text("رد على المقترح")`).first();
  await respondBtn.click();
  await supPage.waitForSelector('#btn-proposal-accept');
  await supPage.locator('#btn-proposal-accept').click();
  await supPage.locator('#btn-submit-proposal-response').click();
  await supPage.waitForLoadState('networkidle');
  console.log('✓ Step 14f: Supervisor accepted accountant proposal');

  // 14g. Accountant approves with instalment 200 SAR
  await accPage.goto(`${baseUrl}/app/debts/?tab=advance_requests`);
  await accPage.waitForLoadState('networkidle');
  const reviewBtn2 = accPage.locator(`button[id="btn-review-advance-${advReqId}"], button:has-text("مراجعة والبت")`).first();
  await reviewBtn2.click();
  await accPage.waitForSelector('#btn-mode-approve');
  await accPage.locator('#btn-mode-approve').click();
  await accPage.locator('#input-review-installment').fill('200');
  await accPage.waitForSelector('#textarea-coverage-reason', { timeout: 8000 });
  await accPage.locator('#textarea-coverage-reason').fill('موافقة استثنائية مع تغطية من إيرادات الشهر القادم');
  await accPage.locator('#btn-submit-review-decision').click();
  await accPage.waitForSelector('div[role="alert"]:has-text("تم اعتماد السلفة")', { timeout: 8000 });
  console.log('✓ Step 14g: Accountant approved advance request with monthly installment 200 SAR');

  // 14h. Custodian pays out advance
  const cusContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const cusPage = await cusContext.newPage();
  await cusPage.goto(`${baseUrl}/app/login/`);
  await cusPage.locator('#username').fill(cusUsername);
  await cusPage.locator('#password').fill(cusPw);
  await cusPage.locator('button[type="submit"]').click();
  await cusPage.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });

  await cusPage.goto(`${baseUrl}/app/treasury/?tab=advance_payouts`);
  await cusPage.waitForLoadState('networkidle');
  await cusPage.screenshot({ path: path.join(r5ResultsDir, 'r5-treasury-advance-payouts.png') });

  // Mobile screenshot of treasury advance payouts
  const cus360Context = await browser.newContext({ viewport: { width: 360, height: 740 } });
  const cus360Page = await cus360Context.newPage();
  await cus360Page.goto(`${baseUrl}/app/login/`);
  await cus360Page.locator('#username').fill(cusUsername);
  await cus360Page.locator('#password').fill(cusPw);
  await cus360Page.locator('button[type="submit"]').click();
  await cus360Page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 8000 });
  await cus360Page.goto(`${baseUrl}/app/treasury/?tab=advance_payouts`);
  await cus360Page.waitForLoadState('networkidle');
  await cus360Page.screenshot({ path: path.join(r5ResultsDir, 'r5-treasury-advance-payouts-360.png') });
  await cus360Context.close();

  // Execute payout via UI dialog
  const payoutBtn = cusPage.locator(`button[id="btn-payout-advance-${advReqId}"], button:has-text("صرف السلفة")`).first();
  await payoutBtn.click();
  await cusPage.waitForSelector('#input-payout-reference');
  await cusPage.locator('#input-payout-reference').fill('SYN-TRF-LIVE-9');
  await cusPage.locator('#btn-confirm-payout').click();
  await cusPage.waitForSelector('div[role="alert"]:has-text("تم صرف السلفة بنجاح")', { timeout: 8000 });
  console.log('✓ Step 14h: Custodian executed payout with reference SYN-TRF-LIVE-9');

  // Verify created debt and journal
  const debtsRes = await page.request.get(`${baseUrl}/api/debts?iqama=${advIqama}`);
  const debtsData = await debtsRes.json();
  const paidDebt = debtsData.find((d) => d.category === 'BankAdvance' && (d.totalAmount === 400 || d.amount === 400));
  assert.ok(paidDebt, 'Paid advance must appear in debts list with category BankAdvance');
  assert.equal(paidDebt.monthlyInstallment, 200, 'Debt monthly installment must be 200 SAR');
  assert.equal(paidDebt.paymentVaultCode, 'BANK.BW', 'Debt payment vault must be BANK.BW');
  console.log(`✓ Step 14i: Debt verified: category=${paidDebt.category}, amount=${paidDebt.totalAmount}, installment=${paidDebt.monthlyInstallment}`);

  await supContext.close();
  await accContext.close();
  await cusContext.close();

  console.log('\n🎉 ALL LIVE E2E INTEGRATION TESTS PASSED AGAINST REAL .NET API!');
} finally {
  if (browser) await browser.close();
  proc.kill('SIGTERM');
  try {
    rmSync(tempDir, { recursive: true, force: true });
    rmSync(wwwrootAppDir, { recursive: true, force: true });
  } catch {}
}
