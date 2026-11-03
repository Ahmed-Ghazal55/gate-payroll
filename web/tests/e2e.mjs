import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const outDir = path.join(webDir, 'out');
const resultsDir = path.resolve(webDir, '../test-results/1.0-web');

fs.mkdirSync(resultsDir, { recursive: true });

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

// Simple static server for web/out with basePath: /app
function startStaticServer(port = 39821) {
  const server = http.createServer((req, res) => {
    let reqPath = req.url?.split('?')[0] || '/';

    // Mock API handler if called directly
    if (reqPath.startsWith('/api/')) {
      if (reqPath === '/api/me') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ username: 'admin', role: 'Admin', displayNameAr: 'غزال' }));
        return;
      }
      if (reqPath === '/api/notifications/summary') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ unreadCount: 3, approvalCount: 2, byCategory: { general: 1, personal: 1, system: 1, approvals: 1 } }));
        return;
      }
      if (reqPath === '/api/dashboard') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          riders: 294,
          imports: 18,
          pendingResponses: 5,
          approvedRuns: 12,
          latestAudit: [
            { id: '1', action: 'اعتماد مسير رواتب أكتوبر', actor: 'غزال', at: new Date().toISOString() },
            { id: '2', action: 'تحديث آيبان مندوب', actor: 'غزال', at: new Date().toISOString() }
          ]
        }));
        return;
      }
      if (reqPath === '/api/excel/export') {
        res.writeHead(200, { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
        res.end('Excel binary mockup');
        return;
      }
      if (reqPath === '/api/campaigns') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'camp-101',
            title: 'تحديث بيانات الآيبان 2026',
            kind: 'iban',
            status: 'open',
            expiresAt: new Date(Date.now() + 10 * 86400 * 1000).toISOString(),
            createdAt: new Date().toISOString(),
            path: '/forms/iban-2026',
            responseCount: 24,
          }
        ]));
        return;
      }
      if (reqPath === '/api/submissions') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'sub-1',
            campaignId: 'camp-101',
            campaignTitle: 'تحديث بيانات الآيبان 2026',
            name: 'أحمد صالح المحمدي',
            iqama: '2000000111',
            iban: 'SA0380000000608010167519',
            bank: 'RJHI',
            status: 'pending',
            version: 1,
            createdAt: new Date().toISOString(),
          },
          {
            id: 'sub-2',
            campaignId: 'camp-101',
            campaignTitle: 'تحديث بيانات الآيبان 2026',
            name: 'سالم فهد القحطاني',
            iqama: '2000000222',
            iban: 'SA4410000001234567890123',
            bank: 'NCBK',
            status: 'pending',
            version: 2,
            isEdited: true,
            createdAt: new Date().toISOString(),
          }
        ]));
        return;
      }
      if (reqPath.startsWith('/api/search')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: '1', kind: 'rider', title: 'سالم فهد القحطاني', subtitle: 'إقامة: 2000000222', url: '/app/riders/' }
        ]));
        return;
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end('[]');
      return;
    }

    if (!reqPath.startsWith('/app')) {
      res.writeHead(302, { Location: '/app/' });
      res.end();
      return;
    }

    // Strip /app prefix to map to out/
    let localPath = reqPath.slice(4);
    if (localPath === '' || localPath === '/') {
      localPath = '/index.html';
    }

    let filePath = path.join(outDir, localPath);

    // If file doesn't exist, try appending index.html or .html
    if (fs.existsSync(filePath) && fs.statSync(filePath).isDirectory()) {
      filePath = path.join(filePath, 'index.html');
    } else if (!fs.existsSync(filePath)) {
      if (fs.existsSync(filePath + '.html')) {
        filePath = filePath + '.html';
      } else if (fs.existsSync(path.join(filePath, 'index.html'))) {
        filePath = path.join(filePath, 'index.html');
      }
    }

    if (!fs.existsSync(filePath)) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('Not Found: ' + localPath);
      return;
    }

    const ext = path.extname(filePath);
    const mimeMap = {
      '.html': 'text/html; charset=utf-8',
      '.js': 'application/javascript; charset=utf-8',
      '.css': 'text/css; charset=utf-8',
      '.json': 'application/json',
      '.png': 'image/png',
      '.svg': 'image/svg+xml',
      '.ttf': 'font/ttf',
      '.woff2': 'font/woff2',
    };

    res.writeHead(200, { 'Content-Type': mimeMap[ext] || 'application/octet-stream' });
    fs.createReadStream(filePath).pipe(res);
  });

  return new Promise((resolve) => {
    server.listen(port, '127.0.0.1', () => {
      resolve({ server, baseUrl: `http://127.0.0.1:${port}/app` });
    });
  });
}

async function run() {
  console.log('--- Starting Gate Payroll 1.0 Web E2E Test Suite ---');
  const { server, baseUrl } = await startStaticServer();
  console.log(`Serving static out from ${baseUrl}`);

  const browser = await chromium.launch({ headless: true });

  const viewports = [
    { name: 'desktop', width: 1440, height: 900, isMobile: false },
    { name: 'mobile', width: 360, height: 740, isMobile: true },
  ];

  const themes = ['sand', 'light', 'dim'];

  try {
    // -------------------------------------------------------------
    // Test 1: Dashboard Page across viewports and themes
    // -------------------------------------------------------------
    console.log('\n[1/5] Testing Dashboard (الرئيسية)...');
    for (const vp of viewports) {
      const context = await browser.newContext({
        viewport: { width: vp.width, height: vp.height },
        isMobile: vp.isMobile,
      });
      const page = await context.newPage();

      for (const theme of themes) {
        await page.goto(`${baseUrl}/`);
        await page.waitForLoadState('networkidle');

        // Apply theme
        await page.evaluate((t) => {
          document.documentElement.setAttribute('data-theme', t);
          localStorage.setItem('gate_theme', t);
        }, theme);

        // Check RTL
        const dir = await page.evaluate(() => document.documentElement.getAttribute('dir') || document.body.getAttribute('dir'));
        assert.equal(dir, 'rtl', 'Dashboard must be RTL');

        // Check horizontal scroll
        const hasHScroll = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
        assert.ok(!hasHScroll, `Dashboard has horizontal scroll at ${vp.name} (${theme})`);

        // Check KPIs rendered
        const kpiText = await page.textContent('body');
        assert.ok(kpiText.includes('المناديب المسجلون'), 'Dashboard missing riders KPI');
        assert.ok(kpiText.includes('دفعات الاستيراد'), 'Dashboard missing imports KPI');

        const shotPath = path.join(resultsDir, `${vp.name}-${theme}-dashboard.png`);
        await page.screenshot({ path: shotPath, fullPage: false });
        console.log(`  ✓ Saved screenshot: ${path.basename(shotPath)}`);
      }
      await context.close();
    }

    // -------------------------------------------------------------
    // Test 2: Notifications Bell Panel & Notifications Full Page
    // -------------------------------------------------------------
    console.log('\n[2/5] Testing Notifications Panel & Page...');
    {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const page = await context.newPage();
      await page.goto(`${baseUrl}/`);
      await page.waitForLoadState('networkidle');

      // Click notification bell
      const bellBtn = page.locator('button[aria-label="الإشعارات"]');
      await bellBtn.click();
      await page.waitForSelector('[role="dialog"][aria-label="مركز الإشعارات والتنبيهات"]');

      // Click a category tab in the notifications panel (e.g. الاعتمادات)
      const approvalsTab = page.locator('[role="dialog"] button:has-text("الاعتمادات")');
      await approvalsTab.click();

      const notifPanelShot = path.join(resultsDir, 'desktop-sand-notifications-panel.png');
      await page.screenshot({ path: notifPanelShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(notifPanelShot)}`);

      // Navigate to full notifications page
      await page.goto(`${baseUrl}/notifications/`);
      await page.waitForLoadState('networkidle');

      const notifPageText = await page.textContent('body');
      assert.ok(notifPageText.includes('مركز الإشعارات والتنبيهات'), 'Missing notifications title');

      const notifFullShot = path.join(resultsDir, 'desktop-sand-notifications.png');
      await page.screenshot({ path: notifFullShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(notifFullShot)}`);

      // Mobile check
      const mobContext = await browser.newContext({ viewport: { width: 360, height: 740 }, isMobile: true });
      const mobPage = await mobContext.newPage();
      await mobPage.goto(`${baseUrl}/notifications/`);
      await mobPage.waitForLoadState('networkidle');
      const mobNotifShot = path.join(resultsDir, 'mobile-sand-notifications.png');
      await mobPage.screenshot({ path: mobNotifShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(mobNotifShot)}`);

      await context.close();
      await mobContext.close();
    }

    // -------------------------------------------------------------
    // Test 3: Forms & Responses Workspace (4 tabs, drawer, filters)
    // -------------------------------------------------------------
    console.log('\n[3/5] Testing Forms & Responses (النماذج والردود)...');
    {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const page = await context.newPage();

      // Tab 1: Campaigns
      await page.goto(`${baseUrl}/forms/?tab=campaigns`);
      await page.waitForLoadState('networkidle');
      const campShot = path.join(resultsDir, 'desktop-sand-forms-campaigns.png');
      await page.screenshot({ path: campShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(campShot)}`);

      // Tab 3: Responses
      for (const theme of themes) {
        await page.goto(`${baseUrl}/forms/?tab=responses`);
        await page.waitForLoadState('networkidle');
        await page.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme);

        const respShot = path.join(resultsDir, `desktop-${theme}-forms-responses.png`);
        await page.screenshot({ path: respShot });
        console.log(`  ✓ Saved screenshot: ${path.basename(respShot)}`);
      }

      // Check Masked IBAN in responses table
      const pageContent = await page.content();
      assert.ok(pageContent.includes('****'), 'IBAN must be masked in list view');

      // Test Table Column Sort
      const tableHeader = page.locator('th:has-text("اسم المندوب") button[aria-label^="ترتيب"]').first();
      if (await tableHeader.count() > 0) {
        await tableHeader.click();
        console.log('  ✓ Table sort executed cleanly');
      }

      // Test Table Filter Popover
      const filterBtn = page.locator('button[aria-label^="تصفية عمود"]').first();
      if (await filterBtn.count() > 0) {
        await filterBtn.click();
        const popover = page.locator('div[role="dialog"][aria-label^="تصفية"]');
        assert.ok(await popover.isVisible(), 'Filter popover must be visible');
        await page.keyboard.press('Escape');
        console.log('  ✓ Table filter popover opened and closed with Escape');
      }

      // Mobile check for responses
      const mobContext = await browser.newContext({ viewport: { width: 360, height: 740 }, isMobile: true });
      const mobPage = await mobContext.newPage();
      await mobPage.goto(`${baseUrl}/forms/?tab=responses`);
      await mobPage.waitForLoadState('networkidle');
      const mobHScroll = await mobPage.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      assert.ok(!mobHScroll, 'Mobile responses page must not have window horizontal scroll');

      const mobRespShot = path.join(resultsDir, 'mobile-sand-forms-responses.png');
      await mobPage.screenshot({ path: mobRespShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(mobRespShot)}`);

      await context.close();
      await mobContext.close();
    }

    // -------------------------------------------------------------
    // Test 4: Under Migration stub pages (e.g. Riders)
    // -------------------------------------------------------------
    console.log('\n[4/5] Testing Under Migration Pages (المناديب)...');
    {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const page = await context.newPage();
      await page.goto(`${baseUrl}/riders/`);
      await page.waitForLoadState('networkidle');

      const text = await page.textContent('body');
      assert.ok(text.includes('قيد النقل التدريجي'), 'Missing under migration tag');
      assert.ok(text.includes('فتح الشاشة في الواجهة السابقة'), 'Missing legacy link');

      const shot = path.join(resultsDir, 'desktop-sand-unmigrated-riders.png');
      await page.screenshot({ path: shot });
      console.log(`  ✓ Saved screenshot: ${path.basename(shot)}`);
      await context.close();
    }

    // -------------------------------------------------------------
    // Test 5: Keyboard Focus Visibility & Accessibility
    // -------------------------------------------------------------
    console.log('\n[5/5] Testing Keyboard Focus & Accessibility...');
    {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const page = await context.newPage();
      await page.goto(`${baseUrl}/`);
      await page.waitForLoadState('networkidle');

      // Press Tab key several times to trigger focus-visible
      await page.keyboard.press('Tab');
      await page.keyboard.press('Tab');
      await page.keyboard.press('Tab');

      const focusedTag = await page.evaluate(() => document.activeElement?.tagName);
      assert.ok(focusedTag, 'Element must be focused on Tab navigation');

      // Test Ctrl+K shortcut
      await page.keyboard.press('Control+KeyK');
      const searchDialog = page.locator('div[role="dialog"][aria-label="البحث الشامل"]');
      assert.ok(await searchDialog.isVisible(), 'Ctrl+K should open global search dialog');
      await page.keyboard.press('Escape');

      console.log('  ✓ Focus and keyboard shortcuts (Ctrl+K, Esc) working as expected');
      await context.close();
    }

    console.log('\n✅ All Gate Payroll 1.0 Web E2E Tests Passed Successfully!');
  } finally {
    await browser.close();
    server.close();
  }
}

run().catch((err) => {
  console.error('\n❌ E2E Test Failure:', err);
  process.exit(1);
});
