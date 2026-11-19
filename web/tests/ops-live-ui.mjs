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

    // Test B (0.27.0, O1): cities is the default sub-page, KPI cards shown, no top tab bar repeating the sidebar
    console.log('\n[Test B] Verifying default cities sub-page and no repeated top tab bar (O1)...');
    const pageText = await page.textContent('body');
    assert.ok(pageText.includes('المدن والمناطق'), 'Cities title displayed');
    assert.ok(pageText.includes('عدد المدن النشطة'), 'Cities KPI card displayed');
    for (const id of ['cities', 'housing', 'vehicles', 'fuel', 'custody', 'guarantees', 'cases', 'accounts']) {
      assert.equal(await page.locator(`button[id="tab-${id}"]`).count(), 0, `No top tab button for «${id}»`);
    }
    console.log('✓ Cities sub-page loaded; no top tab bar');

    // Test C: the 4 sub-pages open from the URL (sidebar entries), and removed O7 tabs redirect
    console.log('\n[Test C] Testing sub-pages and O7 redirects...');
    for (const sub of [
      { id: 'housing', title: 'السكن والإسكان' },
      { id: 'vehicles', title: 'المركبات والتسليمات' },
      { id: 'fuel', title: 'الوقود' },
      { id: 'cities', title: 'المدن والمناطق' },
    ]) {
      await page.goto(`${baseUrl}/app/operations/?tab=${sub.id}`);
      await page.waitForLoadState('networkidle');
      await page.locator(`h1:has-text("${sub.title}"), h2:has-text("${sub.title}")`).first().waitFor({ timeout: 5000 });
      console.log(`  ✓ Sub-page «${sub.id}» loaded: ${sub.title}`);
    }
    await page.goto(`${baseUrl}/app/operations/?tab=cases`);
    await page.waitForURL(/\/app\/workforce\/\?tab=cases/, { timeout: 10000 });
    console.log('  ✓ ?tab=cases → /app/workforce/?tab=cases');
    await page.goto(`${baseUrl}/app/operations/?tab=custody`);
    await page.waitForURL(/\/app\/operations\/\?tab=vehicles/, { timeout: 10000 });
    console.log('  ✓ ?tab=custody → ?tab=vehicles');
    await page.goto(`${baseUrl}/app/operations/`);
    await page.waitForLoadState('networkidle');

    // Test D: search input
    console.log('\n[Test D] Testing search input...');
    const searchInput = page.locator('input[placeholder="بحث…"]');
    await searchInput.waitFor({ timeout: 5000 });
    await searchInput.fill('الرياض');
    await page.waitForTimeout(500);
    await searchInput.fill('');
    await page.waitForTimeout(500);
    console.log('✓ Search typed and cleared');

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

    // Refresh the sub-page to trigger 503
    const refreshBtn = page.locator('button:has-text("تحديث")').first();
    await refreshBtn.click();
    await page.waitForTimeout(500);

    // Verify the 503 banner
    const banner503 = page.locator('div:has-text("مصدر التشغيل غير متاح حاليًا")').last();
    await banner503.waitFor({ timeout: 5000 });
    console.log('✓ 503 banner displayed: «مصدر التشغيل غير متاح حاليًا»');

    // Unroute and refresh again: the banner goes away
    await page.unroute('**/api/operations/**');
    await refreshBtn.click();
    await page.waitForTimeout(500);
    assert.equal(await page.locator('text=مصدر التشغيل غير متاح حاليًا').count(), 0, '503 banner cleared after recovery');
    console.log('✓ Recovered after refresh');

    // Test H: the old riders page now redirects to the workforce directory (0.26.0 people section)
    console.log('\n[Test H] Testing /app/riders/ redirects to /app/workforce/ ...');
    await page.goto(`${baseUrl}/app/riders/`);
    await page.waitForURL(/\/app\/workforce\/?(\?|$)/, { timeout: 10000 });
    await page.waitForLoadState('networkidle');

    const pageRidersText = await page.textContent('body');
    assert.ok(pageRidersText.includes('دليل الأشخاص والتشغيل'), 'Workforce page loaded after riders redirect');
    console.log('✓ Riders link redirects to the workforce page');

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
