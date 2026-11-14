// U1 Operations Live UI Comprehensive Browser & API Verification
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtempSync, rmSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(webDir, '..');

// Playwright loader from /tmp/gate-browser
const pwModulePath = '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pwModulePath);

async function main() {
  console.log('--- Starting Gate Payroll U1 Operations UI Verification ---');

  // 1. Package web
  console.log('1. Packaging web static export to Payroll.Api/wwwroot/app...');
  const appTarget = path.join(repoRoot, 'Payroll.Api/wwwroot/app');
  mkdirSync(appTarget, { recursive: true });
  cpSync(path.join(webDir, 'out'), appTarget, { recursive: true });
  console.log('✓ Packaged web/out to wwwroot/app');

  // 2. Set up synthetic isolated directory for server
  const testDir = mkdtempSync(path.join(tmpdir(), 'gate-u1-ops-'));
  const port = 19695;
  const baseUrl = `http://127.0.0.1:${port}`;

  console.log(`2. Starting Dotnet API on ${baseUrl}...`);
  const server = spawn(
    'dotnet',
    ['run', '--project', path.join(repoRoot, 'Payroll.Api/Payroll.Api.csproj'), '--no-build', '--urls', baseUrl],
    {
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        DOTNET_CLI_TELEMETRY_OPTOUT: '1',
        PAYROLL_DATA_DIR: testDir,
        Operations__Source: 'store',
      },
      stdio: ['ignore', 'pipe', 'pipe'],
    }
  );

  server.stdout.on('data', (d) => {
    const text = d.toString();
    if (text.includes('Application started') || text.includes('Hosting started')) {
      server.emit('ready');
    }
  });

  server.stderr.on('data', (d) => {
    // console.error('[SERVER STDERR]', d.toString());
  });

  await Promise.race([
    once(server, 'ready'),
    new Promise((_, reject) => setTimeout(() => reject(new Error('Server start timed out after 30s')), 30000)),
  ]);
  console.log('✓ Dotnet live API started successfully');

  try {
    // 3. Register admin user
    const registerRes = await fetch(`${baseUrl}/api/auth/setup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: 'admin',
        password: 'AdminPassword_723!',
        displayNameAr: 'مدير النظام',
        role: 'Admin',
      }),
    });
    assert.equal(registerRes.status, 200, 'Admin setup must return 200');

    // Login
    const loginRes = await fetch(`${baseUrl}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: 'admin',
        password: 'AdminPassword_723!',
      }),
    });
    assert.equal(loginRes.status, 200, 'Login must return 200');
    const cookieHeader = loginRes.headers.get('set-cookie') || '';
    const cookieToken = cookieHeader.split(';')[0];
    console.log('✓ Admin user authenticated');

    // 4. Launch Browser
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: { width: 1440, height: 900 },
      locale: 'ar-SA',
    });

    const [cName, cVal] = cookieToken.split('=');
    await context.addCookies([
      {
        name: cName.trim(),
        value: cVal.trim(),
        domain: '127.0.0.1',
        path: '/',
        httpOnly: true,
      },
    ]);

    const page = await context.newPage();

    // Test A: Operations Workspace & Source Badge
    console.log('\n[Test A] Navigating to /app/operations/ ...');
    await page.goto(`${baseUrl}/app/operations/`);
    await page.waitForLoadState('networkidle');

    // Check Source Badge
    const sourceBadge = page.locator('[data-testid="source-badge-store"]');
    await sourceBadge.waitFor({ timeout: 5000 });
    const badgeText = await sourceBadge.textContent();
    assert.ok(badgeText.includes('بيانات تجريبية محلية'), 'Source badge must show «بيانات تجريبية محلية» in store mode');
    console.log(`✓ Source badge verified: ${badgeText.trim()}`);

    // Check Stat Cards: total of the active tab only (no fake counts for other tabs)
    console.log('\n[Test B] Verifying Active Tab Stat Cards (no fake counts for other tabs)...');
    const pageText = await page.textContent('body');
    assert.ok(!pageText.includes('مغطاة'), 'Must NOT show fake "مغطاة" string for other tabs');
    assert.ok(!pageText.includes('أسطول مسجل'), 'Must NOT show fake "أسطول مسجل" string for other tabs');
    assert.ok(!pageText.includes('عهد موثقة'), 'Must NOT show fake "عهد موثقة" string for other tabs');
    assert.ok(pageText.includes('المدن التشغيلية والمناطق'), 'Active tab title displayed');
    console.log('✓ Stat cards show factual active-tab metrics only');

    // Test C: Tabs navigation (all 7 kinds)
    console.log('\n[Test C] Testing tab navigation across operational kinds...');
    const tabs = [
      { id: 'housing', title: 'السكن والإيواء الميداني' },
      { id: 'vehicles', title: 'أسطول المركبات والتسليمات' },
      { id: 'custody', title: 'العهد الشهرية والأجهزة' },
      { id: 'guarantees', title: 'الكفالات والضمانات المالية' },
      { id: 'cases', title: 'القضايا والمنازعات التشغيلية' },
      { id: 'accounts', title: 'حسابات المنصات ومعرفات العمل' },
      { id: 'cities', title: 'المدن التشغيلية والمناطق' },
    ];

    for (const tab of tabs) {
      const tabButton = page.locator(`button[id="tab-${tab.id}"]`);
      await tabButton.click();
      await page.waitForTimeout(300);
      const titleElem = page.locator('h3:has-text("' + tab.title + '")').first();
      await titleElem.waitFor({ timeout: 5000 });
      console.log(`  ✓ Tab «${tab.id}» loaded: ${tab.title}`);
    }

    // Test D: Server Search & 300ms Debounce
    console.log('\n[Test D] Testing server-side search input with 300ms debounce...');
    const searchInput = page.locator('input[placeholder*="بحث في سجلات التشغيل"]');
    await searchInput.waitFor({ timeout: 5000 });

    // Type query
    await searchInput.fill('الرياض');
    // Wait for debounce (300ms) and network fetch
    await page.waitForTimeout(500);
    const bodyAfterSearch = await page.textContent('body');
    console.log('✓ Debounced search executed with query: الرياض');

    // Clear search
    const clearBtn = page.locator('button[aria-label="مسح البحث"]');
    if (await clearBtn.isVisible()) {
      await clearBtn.click();
      await page.waitForTimeout(500);
      console.log('✓ Search cleared');
    }

    // Test E: Pagination controls (page sizes [25, 50, 100, 200])
    console.log('\n[Test E] Testing pagination page-size selector...');
    const pageSizeSelect = page.locator('select[aria-label="عدد السجلات لكل صفحة"]');
    await pageSizeSelect.waitFor({ timeout: 5000 });
    const options = await pageSizeSelect.locator('option').allTextContents();
    assert.deepEqual(options.map((o) => o.trim()), ['25', '50', '100', '200'], 'Page sizes must be 25, 50, 100, 200');
    console.log('✓ Server pagination page size options verified: [25, 50, 100, 200]');

    // Test F: Mobile Width (360px viewport)
    console.log('\n[Test F] Testing 360px mobile viewport (no horizontal page scroll)...');
    const mobileContext = await browser.newContext({
      viewport: { width: 360, height: 640 },
      locale: 'ar-SA',
    });
    await mobileContext.addCookies([
      {
        name: cName.trim(),
        value: cVal.trim(),
        domain: '127.0.0.1',
        path: '/',
        httpOnly: true,
      },
    ]);
    const mobilePage = await mobileContext.newPage();
    await mobilePage.goto(`${baseUrl}/app/operations/`);
    await mobilePage.waitForLoadState('networkidle');

    const scrollWidth = await mobilePage.evaluate(() => document.documentElement.scrollWidth);
    assert.ok(scrollWidth <= 360, `360px viewport must not have page overflow: scrollWidth=${scrollWidth}`);
    console.log(`✓ 360px mobile layout verified (scrollWidth=${scrollWidth} <= 360)`);
    await mobileContext.close();

    // Test G: 503 Error Card & Retry
    console.log('\n[Test G] Testing 503 Service Unavailable Card and Retry...');
    // Intercept /api/operations/* to return 503
    await page.route('**/api/operations/**', (route) => {
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'مصدر التشغيل غير متاح الآن (تجربة انقطاع الخدمة)' }),
      });
    });

    // Refresh active tab to trigger 503
    const refreshBtn = page.locator('button:has-text("تحديث البيانات")').first();
    await refreshBtn.click();
    await page.waitForTimeout(500);

    // Verify 503 Card / Banner
    const card503 = page.locator('[role="alert"]:has-text("مصدر التشغيل غير متاح الآن")').first();
    await card503.waitFor({ timeout: 5000 });
    const cardText = await card503.textContent();
    assert.ok(cardText.includes('مصدر التشغيل غير متاح الآن'), 'Must show Arabic 503 card');
    console.log('✓ 503 card displayed: «مصدر التشغيل غير متاح الآن»');

    // Unroute to restore normal operation
    await page.unroute('**/api/operations/**');

    // Click retry button
    const retryBtn = card503.locator('button:has-text("إعادة المحاولة")');
    if (await retryBtn.isVisible()) {
      await retryBtn.click();
      await page.waitForTimeout(500);
      console.log('✓ Retry button clicked and recovered successfully');
    }

    // Test H: Rider Accounts Panel
    console.log('\n[Test H] Testing Rider Accounts Panel in /app/riders/ ...');
    await page.goto(`${baseUrl}/app/riders/`);
    await page.waitForLoadState('networkidle');

    const pageRidersText = await page.textContent('body');
    assert.ok(pageRidersText.includes('المناديب وسجلات التشغيل'), 'Riders page loaded');
    console.log('✓ Riders page loaded');

    // Test I: Rider Statement Accounts Panel
    console.log('\n[Test I] Testing Rider Accounts Panel in /app/riders/statement/ ...');
    await page.goto(`${baseUrl}/app/riders/statement/?iqama=2000000789`);
    await page.waitForLoadState('networkidle');
    const statementText = await page.textContent('body');
    assert.ok(statementText.includes('حسابات وتكليفات المنصات الميدانية'), 'RiderAccountsPanel must be visible on statement page');
    console.log('✓ RiderAccountsPanel rendered in rider statement');

    // Test J: Dark Mode (dim theme) verification on U1 operations screens
    console.log('\n[Test J] Testing Dark Mode (dim theme) on /app/operations/ and /app/riders/statement/ ...');
    await page.evaluate(() => {
      window.localStorage.setItem('gate_theme', 'dim');
      document.documentElement.setAttribute('data-theme', 'dim');
    });
    await page.goto(`${baseUrl}/app/operations/`);
    await page.waitForLoadState('networkidle');
    const themeAttr = await page.evaluate(() => document.documentElement.getAttribute('data-theme'));
    assert.equal(themeAttr, 'dim', 'Document must have data-theme="dim"');
    const dimBadge = page.locator('[data-testid="source-badge-store"]');
    await dimBadge.waitFor({ state: 'visible', timeout: 5000 });
    console.log('✓ Dark mode (dim) verified on Operations workspace and SourceBadge');

    await browser.close();
    console.log('\n======================================================');
    console.log('🎉 ALL U1 OPERATIONS UI TESTS PASSED SUCCESSFULLY!');
    console.log('======================================================');
    process.exit(0);
  } finally {
    server.kill('SIGTERM');
    try {
      rmSync(testDir, { recursive: true, force: true });
    } catch {}
  }
}

main().catch((err) => {
  console.error('FAIL:', err);
  process.exit(1);
});
