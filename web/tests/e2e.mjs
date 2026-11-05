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
      if (reqPath.match(/\/api\/campaigns\/[^/]+\/delete-preview/)) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ id: 'camp-101', title: 'تحديث بيانات الآيبان 2026', responseCount: 24, fingerprint: 'mock-fp-101' }));
        return;
      }
      if (reqPath === '/api/submissions' || reqPath === '/api/responses') {
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
      if (reqPath === '/api/finance/coa/target') {
        const coaPath = path.resolve(webDir, '../Payroll.Api/Resources/target-coa.v1.json');
        if (fs.existsSync(coaPath)) {
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(fs.readFileSync(coaPath));
          return;
        }
      }
      if (reqPath === '/api/platforms/hs/posting') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ enabled: false }));
        return;
      }
      if (reqPath === '/api/promissory-notes') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ featureEnabled: true, items: [] }));
        return;
      }
      if (reqPath.startsWith('/api/finance/trial-balance')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ version: '1.0', view: 'target', companies: [] }));
        return;
      }
      if (reqPath.startsWith('/api/finance/income-statement')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ version: '1.0', view: 'target', companies: [] }));
        return;
      }
      if (reqPath.startsWith('/api/finance/bank-file/preview')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ period: '2026-10', totalAmount: 0, validRecordsCount: 0, excludedRecordsCount: 0, records: [] }));
        return;
      }
      if (reqPath === '/api/riders/data-quality') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          totalRiders: 294,
          withIban: 290,
          withPlatformAccount: 285,
          withCompleteDocs: 270,
          missingIbanCount: 4,
          duplicateIbanCount: 0,
          expiredDocsCount: 12
        }));
        return;
      }
      if (reqPath === '/api/riders/sync-baseline') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ matched: 294, newFromBaseline: 5, changed: 2, conflicts: 0 }));
        return;
      }
      if (reqPath.match(/\/api\/riders\/[^/]+\/profile-360/)) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          iqama: '2000000789',
          name: 'سالم فهد القحطاني',
          phone: '0552223344',
          city: 'جدة',
          platform: 'hungerstation',
          status: 'active',
          sponsorshipType: 'internal',
          vehicleType: 'car',
          iban: 'SA4410000001234567890123',
          documents: [
            { typeCode: 'national_id', titleAr: 'الهوية / الإقامة', isVerified: true, status: 'valid', documentNumber: '2000000789' }
          ],
          kpis: {
            totalOrders: 420,
            completionRate: 98,
            activeMonths: 14
          }
        }));
        return;
      }
      if (reqPath === '/api/riders') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            iqama: '2000000789',
            name: 'سالم فهد القحطاني',
            phone: '0552223344',
            city: 'جدة',
            platform: 'hungerstation',
            vehicleType: 'car',
            sponsorshipType: 'internal',
            status: 'active',
            iban: 'SA4410000001234567890123',
          },
          {
            iqama: '2000000111',
            name: 'أحمد صالح المحمدي',
            phone: '0501112233',
            city: 'الرياض',
            platform: 'keeta',
            vehicleType: 'motorcycle',
            sponsorshipType: 'internal',
            status: 'active',
            iban: 'SA0380000000608010167519',
          }
        ]));
        return;
      }
      if (reqPath.startsWith('/api/finance/statement/rider/')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          iqama: '2000000789',
          riderName: 'سالم فهد القحطاني',
          period: { from: '2026-01-01', to: '2026-10-31' },
          openingBalance: 0,
          closingBalance: 4250,
          entries: [
            {
              date: '2026-10-15',
              entryType: 'مكافأة أداء',
              description: 'مكافأة تحقيق الهدف الأسبوعي',
              reference: 'BON-101',
              debit: 300,
              credit: 0,
              balance: 2300
            },
            {
              date: '2026-10-31',
              entryType: 'راتب شهري',
              description: 'صافي مستحقات شهر أكتوبر 2026',
              reference: 'PAY-102',
              debit: 3950,
              credit: 0,
              balance: 4250
            }
          ]
        }));
        return;
      }
      if (reqPath === '/api/users') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'usr-1',
            username: 'admin',
            role: 'Admin',
            displayNameAr: 'غزال',
            isActive: true,
            createdAt: '2026-10-01T00:00:00Z',
            lastLoginAt: new Date().toISOString()
          },
          {
            id: 'usr-2',
            username: 'auditor_fahad',
            role: 'FinancialAccountant',
            displayNameAr: 'فهد المحاسب',
            isActive: true,
            createdAt: '2026-10-15T00:00:00Z'
          }
        ]));
        return;
      }
      if (reqPath === '/api/audit') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'aud-1',
            action: 'اعتماد مسير رواتب',
            actor: 'admin',
            target: 'دورة 2026-10',
            timestamp: new Date().toISOString(),
            details: 'تم الاعتماد المالي النهائي'
          },
          {
            id: 'aud-2',
            action: 'تعديل آيبان',
            actor: 'auditor_fahad',
            target: '2000000789',
            timestamp: new Date(Date.now() - 3600000).toISOString(),
            details: 'تحديث الحساب البنكي'
          }
        ]));
        return;
      }
      if (reqPath.startsWith('/api/rider-mobile/broadcast')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'bc-1',
            category: 'system',
            titleAr: 'تحديث الشروط والأحكام',
            bodyAr: 'يرجى مراجعة وتحديث وثائق التأمين',
            createdAt: new Date().toISOString()
          }
        ]));
        return;
      }
      if (reqPath.includes('firebase-config')) {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          projectId: 'gate-payroll-prod',
          clientEmail: 'service@gate-payroll.iam.gserviceaccount.com',
          privateKey: '-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQD...masked...\n-----END PRIVATE KEY-----'
        }));
        return;
      }
      if (reqPath === '/api/imports') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'imp-1',
            filename: 'operations_october.xlsx',
            uploadedBy: 'admin',
            status: 'committed',
            createdAt: new Date().toISOString(),
            rowsCount: 1500
          }
        ]));
        return;
      }
      if (reqPath.startsWith('/api/operations/')) {
        // Return 404 to trigger the graceful "غير متاح بعد — بانتظار اكتمال واجهات خادم التشغيل (Codex R4.6)" fallback
        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'Endpoint not implemented yet' }));
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
    // Test 4: Verified active Riders workspace (migrated in R4)
    // -------------------------------------------------------------
    console.log('\n[4/5] Testing Riders Workspace (active workspace, no under migration)...');
    {
      const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const page = await context.newPage();
      await page.goto(`${baseUrl}/riders/`);
      await page.waitForLoadState('networkidle');

      const text = await page.textContent('body');
      assert.ok(!text.includes('قيد النقل التدريجي'), 'Riders workspace must be active, not under migration');
      assert.ok(!text.includes('الشاشات القديمة (مؤقت)'), 'Riders workspace must not contain legacy link');
      assert.ok(text.includes('دليل المناديب'), 'Riders directory header missing');

      const shot = path.join(resultsDir, 'desktop-sand-migrated-riders.png');
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

    // -------------------------------------------------------------
    // Test 6: W1 Auth Pages (Login & Setup at 1440 and 360)
    // -------------------------------------------------------------
    console.log('\n[6/6] Testing W1 & W2 Pages (Login, Setup, Companies at 1440 & 360)...');
    const r2ResultsDir = path.join(webDir, '../test-results/r2-web');
    if (!fs.existsSync(r2ResultsDir)) {
      fs.mkdirSync(r2ResultsDir, { recursive: true });
    }

    for (const p of ['login', 'setup', 'companies']) {
      // 1440 desktop
      const deskContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const deskPage = await deskContext.newPage();
      await deskPage.goto(`${baseUrl}/${p}/`);
      await deskPage.waitForLoadState('networkidle');
      const deskShot = path.join(r2ResultsDir, `desktop-sand-${p}.png`);
      await deskPage.screenshot({ path: deskShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(deskShot)}`);
      await deskContext.close();

      // 360 mobile
      const mobContext = await browser.newContext({ viewport: { width: 360, height: 740 } });
      const mobPage = await mobContext.newPage();
      await mobPage.goto(`${baseUrl}/${p}/`);
      await mobPage.waitForLoadState('networkidle');
      const mobShot = path.join(r2ResultsDir, `mobile-sand-${p}.png`);
      await mobPage.screenshot({ path: mobShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(mobShot)}`);
      await mobContext.close();
    }

    // -------------------------------------------------------------
    // Test 7: R3 Workspaces (payroll, treasury, debts, accounting)
    // -------------------------------------------------------------
    console.log('\n[7/7] Testing R3 Workspaces (payroll, treasury, debts, accounting at 1440 & 360 across themes)...');
    const r3ResultsDir = path.join(webDir, '../test-results/r3-web');
    if (!fs.existsSync(r3ResultsDir)) {
      fs.mkdirSync(r3ResultsDir, { recursive: true });
    }

    const r3Pages = ['payroll', 'treasury', 'debts', 'accounting'];
    for (const p of r3Pages) {
      // 1440 desktop across themes
      for (const theme of themes) {
        const deskContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
        const deskPage = await deskContext.newPage();
        await deskPage.goto(`${baseUrl}/${p}/`);
        await deskPage.waitForLoadState('networkidle');
        await deskPage.evaluate((t) => document.documentElement.setAttribute('data-theme', t), theme);

        // Verify RTL
        const dir = await deskPage.evaluate(() => document.documentElement.getAttribute('dir') || document.body.getAttribute('dir'));
        assert.equal(dir, 'rtl', `${p} page must be RTL`);

        const deskShot = path.join(r3ResultsDir, `desktop-${theme}-${p}.png`);
        await deskPage.screenshot({ path: deskShot });
        console.log(`  ✓ Saved screenshot: ${path.basename(deskShot)}`);
        await deskContext.close();
      }

      // 360 mobile
      const mobContext = await browser.newContext({ viewport: { width: 360, height: 740 }, isMobile: true });
      const mobPage = await mobContext.newPage();
      await mobPage.goto(`${baseUrl}/${p}/`);
      await mobPage.waitForLoadState('networkidle');

      // Check horizontal scroll
      const hasHScroll = await mobPage.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      assert.ok(!hasHScroll, `${p} page has horizontal scroll at 360px`);

      const mobShot = path.join(r3ResultsDir, `mobile-sand-${p}.png`);
      await mobPage.screenshot({ path: mobShot });
      console.log(`  ✓ Saved screenshot: ${path.basename(mobShot)}`);
      await mobContext.close();
    }

    // -------------------------------------------------------------
    // Test 8: R4 Workspaces (riders, statement, operations, admin, imports, rider-app, payroll rules)
    // -------------------------------------------------------------
    console.log('\n[8/8] Testing R4 Workspaces at 1440 & 360 (RTL & no horizontal scroll)...');
    const r4ResultsDir = path.join(webDir, '../test-results/r4-web');
    if (!fs.existsSync(r4ResultsDir)) {
      fs.mkdirSync(r4ResultsDir, { recursive: true });
    }

    const r4Targets = [
      { name: 'riders', url: `${baseUrl}/riders/` },
      { name: 'riders-statement', url: `${baseUrl}/riders/statement/?iqama=2000000789` },
      { name: 'operations', url: `${baseUrl}/operations/` },
      { name: 'admin', url: `${baseUrl}/admin/` },
      { name: 'imports', url: `${baseUrl}/imports/` },
      { name: 'rider-app', url: `${baseUrl}/rider-app/` },
      { name: 'payroll-rules', url: `${baseUrl}/payroll/?tab=rules` },
      { name: 'payroll-cycle', url: `${baseUrl}/payroll/?tab=cycle` },
    ];

    for (const target of r4Targets) {
      // 1440 Desktop
      const deskContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      const deskPage = await deskContext.newPage();
      await deskPage.goto(target.url);
      await deskPage.waitForLoadState('networkidle');

      // Check RTL
      const dir = await deskPage.evaluate(() => document.documentElement.getAttribute('dir') || document.body.getAttribute('dir'));
      assert.equal(dir, 'rtl', `${target.name} page must be RTL`);

      const deskShot = path.join(r4ResultsDir, `desktop-sand-${target.name}.png`);
      await deskPage.screenshot({ path: deskShot });
      console.log(`  ✓ Saved R4 screenshot: ${path.basename(deskShot)}`);
      await deskContext.close();

      // 360 Mobile
      const mobContext = await browser.newContext({ viewport: { width: 360, height: 740 }, isMobile: true });
      const mobPage = await mobContext.newPage();
      await mobPage.goto(target.url);
      await mobPage.waitForLoadState('networkidle');

      // Check horizontal scroll
      const hasHScroll = await mobPage.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      assert.ok(!hasHScroll, `${target.name} page has horizontal scroll at 360px (scrollWidth: ${await mobPage.evaluate(() => document.documentElement.scrollWidth)})`);

      const mobShot = path.join(r4ResultsDir, `mobile-sand-${target.name}.png`);
      await mobPage.screenshot({ path: mobShot });
      console.log(`  ✓ Saved R4 screenshot: ${path.basename(mobShot)}`);
      await mobContext.close();
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
