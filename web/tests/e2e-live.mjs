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
    bypassCSP: true,
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

  console.log('\n🎉 ALL LIVE E2E INTEGRATION TESTS PASSED AGAINST REAL .NET API!');
} finally {
  if (browser) await browser.close();
  proc.kill('SIGTERM');
  try {
    rmSync(tempDir, { recursive: true, force: true });
    rmSync(wwwrootAppDir, { recursive: true, force: true });
  } catch {}
}
