import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';

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

  // Step 8c: User A proposes TGA split
  console.log('\nStep 8c: User A proposing TGA split...');
  const splitRes = await page.request.post(`${baseUrl}/api/platforms/hs/allocations/${encodeURIComponent(tgaLine.id)}`, {
    data: { companyAmount: 20, riderAmount: 0 },
  });
  assert.equal(splitRes.status(), 200, 'TGA split proposal should return 200');
  const splitData = await splitRes.json();
  console.log(`✓ Step 8c: TGA split proposed by User A: id=${splitData.id}`);

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

  // Step 10: Verify moved workspaces load cleanly without UnderMigration
  console.log('\nStep 10: Verifying moved workspaces (/app/treasury/, /app/debts/, /app/accounting/)...');
  for (const pagePath of ['/app/treasury/', '/app/debts/', '/app/accounting/']) {
    await page.goto(`${baseUrl}${pagePath}`);
    await page.waitForLoadState('networkidle');
    const content = await page.textContent('body');
    assert.ok(!content.includes('قيد النقل التدريجي'), `${pagePath} must be an active workspace, not UnderMigration`);
    console.log(`✓ Step 10: ${pagePath} loaded cleanly as real workspace`);
  }

  console.log('\n🎉 ALL LIVE E2E INTEGRATION TESTS PASSED AGAINST REAL .NET API!');
} finally {
  if (browser) await browser.close();
  proc.kill('SIGTERM');
  try {
    rmSync(tempDir, { recursive: true, force: true });
    rmSync(wwwrootAppDir, { recursive: true, force: true });
  } catch {}
}
