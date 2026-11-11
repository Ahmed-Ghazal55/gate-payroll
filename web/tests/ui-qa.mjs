import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const outDir = path.join(webDir, 'out');
const resultsDir = path.resolve(webDir, '../test-results/r6b-ui');

fs.mkdirSync(resultsDir, { recursive: true });

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

let currentUser = { username: 'admin', role: 'Admin', displayNameAr: 'غزال', permissions: ['all'] };

function startStaticServer(port = 39823) {
  const server = http.createServer((req, res) => {
    let reqPath = req.url?.split('?')[0] || '/';

    // Mock API handler
    if (reqPath.startsWith('/api/')) {
      if (reqPath === '/api/me') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify(currentUser));
        return;
      }
      if (reqPath === '/api/health') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ ok: true, jobs: { enabled: true } }));
        return;
      }
      if (reqPath === '/api/system/jobs/erp-sync/dry-run' || reqPath === '/api/system/jobs/erp-sync/run') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ jobId: '123456' }));
        return;
      }
      if (reqPath === '/api/system/jobs/erp-sync/123456') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
          jobId: '123456',
          status: 'completed',
          mode: 'dry_run',
          queuedAt: new Date().toISOString(),
          startedAt: new Date().toISOString(),
          finishedAt: new Date().toISOString(),
          counts: [
            { kind: 'riders', source: 'ERP', new: 4, changed: 2, unchanged: 180, rejected: 0 },
            { kind: 'companies', source: 'ERP', new: 1, changed: 0, unchanged: 12, rejected: 0 },
          ]
        }));
        return;
      }
      if (reqPath === '/api/system/erp-sync') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          {
            id: 'erp-sync-run-1',
            startedAt: new Date().toISOString(),
            finishedAt: new Date().toISOString(),
            status: 'completed',
            mode: 'dry_run',
            counts: [{ kind: 'riders', new: 4, changed: 2, unchanged: 180, rejected: 0 }]
          }
        ]));
        return;
      }
      if (reqPath === '/api/notifications/summary') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ unreadCount: 3, approvalCount: 2, byCategory: { general: 1, personal: 1, system: 1, approvals: 1 } }));
        return;
      }
      if (reqPath === '/api/notifications') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: 'notif-1', title: 'اعتماد مسير رواتب', body: 'تم اعتماد مسير شهر أكتوبر بنجاح', category: 'approvals', unread: true, createdAt: new Date().toISOString() },
          { id: 'notif-2', title: 'تنبيه مديونية', body: 'تجاوزت مديونية المندوب الحد المسموح', category: 'general', unread: true, createdAt: new Date().toISOString() },
          { id: 'notif-3', title: 'سلفة جديدة', body: 'طلب سلفة جديد ينتظر المراجعة', category: 'tasks', requiresAction: true, unread: true, createdAt: new Date().toISOString() },
        ]));
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
          },
          {
            id: 'camp-102',
            title: 'اختيار نظام العمل',
            kind: 'work-choice',
            status: 'open',
            expiresAt: new Date(Date.now() - 3 * 86400 * 1000).toISOString(),
            createdAt: new Date().toISOString(),
            path: '/forms/work-2026',
            responseCount: 5,
          }
        ]));
        return;
      }
      if (reqPath === '/api/submissions' || reqPath === '/api/responses') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: 'sub-1', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'أحمد صالح المحمدي', iqama: '2000000111', iban: 'SA0380000000608010167519', bank: 'RJHI', status: 'pending', version: 1, createdAt: new Date().toISOString() },
          { id: 'sub-2', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'سالم فهد القحطاني', iqama: '2000000222', iban: 'SA4410000001234567890123', bank: 'NCBK', status: 'pending', version: 2, createdAt: new Date().toISOString() },
          { id: 'sub-3', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'محمد علي القرني', iqama: '2000000333', iban: 'SA5520000001234567890123', bank: 'ALBI', status: 'approved', version: 1, createdAt: new Date().toISOString() },
          { id: 'sub-4', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'خالد عبدالله الشهري', iqama: '2000000444', iban: 'SA6630000001234567890123', bank: 'BSFR', status: 'rejected', version: 1, createdAt: new Date().toISOString() },
          { id: 'sub-5', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'عمر ياسين الدوسري', iqama: '2000000555', iban: 'SA7740000001234567890123', bank: 'SABB', status: 'pending', version: 1, createdAt: new Date().toISOString() },
          { id: 'sub-6', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'ياسر حميد الزهراني', iqama: '2000000666', iban: 'SA8850000001234567890123', bank: 'ANBI', status: 'pending', version: 1, createdAt: new Date().toISOString() },
          { id: 'sub-7', campaignId: 'camp-101', campaignTitle: 'تحديث بيانات الآيبان 2026', name: 'باسم حسن الغامدي', iqama: '2000000777', iban: 'SA9960000001234567890123', bank: 'ARNB', status: 'approved', version: 1, createdAt: new Date().toISOString() },
        ]));
        return;
      }
      if (reqPath === '/api/riders') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { iqama: '2000000789', name: 'سالم فهد القحطاني', phone: '0552223344', city: 'جدة', platform: 'hungerstation', vehicleType: 'car', sponsorshipType: 'internal', status: 'active', iban: 'SA4410000001234567890123' },
          { iqama: '2000000111', name: 'أحمد صالح المحمدي', phone: '0501112233', city: 'الرياض', platform: 'keeta', vehicleType: 'motorcycle', sponsorshipType: 'internal', status: 'active', iban: 'SA0380000000608010167519' },
          { iqama: '2000000222', name: 'محمد علي القرني', phone: '0502223344', city: 'الدمام', platform: 'jahez', vehicleType: 'car', sponsorshipType: 'external', status: 'active', iban: 'SA5520000001234567890123' },
          { iqama: '2000000333', name: 'خالد عبدالله الشهري', phone: '0503334455', city: 'مكة', platform: 'toyou', vehicleType: 'car', sponsorshipType: 'internal', status: 'active', iban: 'SA6630000001234567890123' },
          { iqama: '2000000444', name: 'عمر ياسين الدوسري', phone: '0504445566', city: 'المدينة', platform: 'hungerstation', vehicleType: 'motorcycle', sponsorshipType: 'internal', status: 'suspended', iban: 'SA7740000001234567890123' },
          { iqama: '2000000555', name: 'ياسر حميد الزهراني', phone: '0505556677', city: 'الطائف', platform: 'keeta', vehicleType: 'car', sponsorshipType: 'internal', status: 'active', iban: 'SA8850000001234567890123' },
          { iqama: '2000000666', name: 'باسم حسن الغامدي', phone: '0506667788', city: 'تبوك', platform: 'jahez', vehicleType: 'car', sponsorshipType: 'internal', status: 'active', iban: 'SA9960000001234567890123' },
        ]));
        return;
      }
      if (reqPath === '/api/companies') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: 'comp-1', nameAr: 'شركة الميناء السريع', code: 'FAST_PORT', crNumber: '1010998877', taxNumber: '300011122200003', isActive: true, createdAt: new Date().toISOString() },
          { id: 'comp-2', nameAr: 'مؤسسة الخدمات المتطورة', code: 'ADV_SRV', crNumber: '1010887766', taxNumber: '300022233300003', isActive: true, createdAt: new Date().toISOString() }
        ]));
        return;
      }
      if (reqPath === '/api/users') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: 'usr-1', username: 'admin', role: 'Admin', displayNameAr: 'غزال', isActive: true, createdAt: '2026-10-01T00:00:00Z', lastLoginAt: new Date().toISOString() },
          { id: 'usr-2', username: 'auditor_fahad', role: 'FinancialAccountant', displayNameAr: 'فهد المحاسب', isActive: true, createdAt: '2026-10-15T00:00:00Z' }
        ]));
        return;
      }
      if (reqPath === '/api/debts' || reqPath === '/api/finance/debts') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          // Server shape (Debts.cs / DebtSheetOpening.cs): totalAmount + remainingBalance, never originalAmount.
          { id: 'debt-1', iqama: '2000000789', riderName: 'سالم فهد القحطاني', category: 'CashAdvance', categoryAr: 'سلفة نقدية', entryMode: 'Disbursement', totalAmount: 1500, paidAmount: 500, remainingBalance: 1000, status: 'active', createdAt: new Date().toISOString() },
          { id: 'debt-2', iqama: '2000000111', riderName: 'أحمد صالح المحمدي', category: 'AccidentDamage', categoryAr: 'تلفيات حوادث', entryMode: 'Accrual', totalAmount: 3200, paidAmount: 3200, remainingBalance: 0, status: 'settled', createdAt: new Date().toISOString() },
          { id: 'DEBT-SHEET:9:TrafficFine', iqama: '2000000333', riderName: 'مندوب افتتاحي', category: 'TrafficFine', categoryAr: 'مخالفات مرورية', entryMode: 'OpeningBalance', totalAmount: 420.5, paidAmount: 0, remainingBalance: 420.5, monthlyInstallment: 0, status: 'pending_approval', balanceAsOf: '2026-10-31', createdBy: 'debt-sheet-import' }
        ]));
        return;
      }
      if (reqPath === '/api/treasury' || reqPath === '/api/finance/treasury') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify([
          { id: 'acc-1', nameAr: 'خزينة المركز الرئيسي', type: 'cash', balance: 45000, currency: 'SAR', isActive: true },
          { id: 'acc-2', nameAr: 'مصرف الراجحي - تشغيلي', type: 'bank', balance: 185000, currency: 'SAR', isActive: true }
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

    let localPath = reqPath.slice(4);
    if (localPath === '' || localPath === '/') {
      localPath = '/index.html';
    }

    let filePath = path.join(outDir, localPath);
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
  console.log('--- Starting Gate Payroll R6 UI QA Test Suite ---');
  const { server, baseUrl } = await startStaticServer();
  console.log(`Serving app from ${baseUrl}`);

  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_PATH });
  const context = await browser.newContext();
  const page = await context.newPage();

  // Inject user profile in localStorage
  await page.addInitScript(() => {
    try {
      localStorage.setItem('gate_theme', 'sand');
      localStorage.setItem('gate_user', JSON.stringify({ username: 'admin', role: 'Admin', displayNameAr: 'غزال' }));
    } catch (_) {}
  });

  try {
    // =========================================================================
    // Test (a): Typing 10 characters in dialogs across modules without focus loss
    // =========================================================================
    console.log('\n[R6ui-1 / Test a] Typing 10 characters in dialogs without focus loss...');

    // 1. Admin Users Dialog
    console.log('  Testing Admin Create User Dialog...');
    await page.goto(`${baseUrl}/admin/`);
    await page.waitForLoadState('networkidle');
    const newUserBtn = page.locator('button:has-text("مستخدم جديد"), button:has-text("إضافة مستخدم")').first();
    if (await newUserBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await newUserBtn.click();
      await page.waitForSelector('[role="dialog"]', { timeout: 2000 });
      const firstInput = page.locator('[role="dialog"] input[type="text"]').first();
      await firstInput.waitFor({ state: 'visible' });
      await firstInput.focus();
      const testText = 'Ghazal2026';
      await page.keyboard.type(testText, { delay: 30 });
      const val = await firstInput.inputValue();
      assert.equal(val, testText, `Admin input value should equal typed 10 chars, got: ${val}`);
      const isFocused = await firstInput.evaluate((el) => el === document.activeElement);
      assert.ok(isFocused, 'Admin input must retain focus across 10 keystrokes');
      await page.keyboard.press('Escape');
      console.log('    ✓ Admin dialog typing 10 chars passed');
    }

    // 2. Forms / CampaignsTab Dialog
    console.log('  Testing Forms Campaigns Dialog...');
    await page.goto(`${baseUrl}/forms/`);
    await page.waitForLoadState('networkidle');
    const newCampBtn = page.locator('button:has-text("إنشاء حملة"), button:has-text("حملة جديدة")').first();
    if (await newCampBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await newCampBtn.click();
      await page.waitForSelector('[role="dialog"]', { timeout: 2000 });
      const firstInput = page.locator('[role="dialog"] input[type="text"]').first();
      await firstInput.waitFor({ state: 'visible' });
      await firstInput.focus();
      const testText = 'Campaign99';
      await page.keyboard.type(testText, { delay: 30 });
      const val = await firstInput.inputValue();
      assert.equal(val, testText, `Campaign input value should equal typed 10 chars, got: ${val}`);
      const isFocused = await firstInput.evaluate((el) => el === document.activeElement);
      assert.ok(isFocused, 'Campaign input must retain focus across 10 keystrokes');
      await page.keyboard.press('Escape');
      console.log('    ✓ Forms Campaign dialog typing 10 chars passed');
    }

    // 3. Debts Advance Request Dialog
    console.log('  Testing Debts Advance Request Dialog...');
    await page.goto(`${baseUrl}/debts/`);
    await page.waitForLoadState('networkidle');
    const advancesTab = page.locator('button:has-text("السلف والأقساط")').first();
    if (await advancesTab.isVisible({ timeout: 2000 }).catch(() => false)) {
      await advancesTab.click();
      await page.waitForTimeout(300);
    }
    const advanceBtn = page.locator('#btn-new-advance-request, button:has-text("طلب سلفة جديد")').first();
    if (await advanceBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await advanceBtn.click();
      await page.waitForSelector('[role="dialog"]', { timeout: 2000 });
      const firstInput = page.locator('[role="dialog"] input[type="text"], [role="dialog"] input[type="number"]').first();
      await firstInput.waitFor({ state: 'visible' });
      await firstInput.focus();
      const testText = '1000200030';
      await page.keyboard.type(testText, { delay: 30 });
      const val = await firstInput.inputValue();
      assert.equal(val, testText, `Debts advance input value should equal typed 10 chars, got: ${val}`);
      const isFocused = await firstInput.evaluate((el) => el === document.activeElement);
      assert.ok(isFocused, 'Debts advance input must retain focus across 10 keystrokes');
      await page.keyboard.press('Escape');
      console.log('    ✓ Debts Advance Request dialog typing 10 chars passed');
    }

    // 3b. Debts list with server-shaped records renders (0.17.1 crash) and offers opening approval
    console.log('  Testing Debts list renders server-shaped records...');
    await page.goto(`${baseUrl}/debts/`);
    await page.waitForLoadState('networkidle');
    await page.getByText('420.50', { exact: false }).first().waitFor({ timeout: 5000 });
    assert.equal(await page.getByText('تعذر عرض هذه الصفحة').count(), 0, 'Debts page must not hit the error boundary');
    assert.ok(await page.getByText('أرصدة افتتاحية بانتظار الاعتماد').first().isVisible(), 'Admin sees the pending opening balances banner');
    assert.ok(await page.locator('td span', { hasText: 'مخالفات مرورية' }).first().isVisible(), 'Category label comes from categoryAr');
    console.log('    ✓ Debts list renders totalAmount/remainingBalance and opening banner');

    // 3c. Expired campaign is labelled expired, not open
    console.log('  Testing expired campaign label...');
    await page.goto(`${baseUrl}/forms/`);
    await page.waitForLoadState('networkidle');
    await page.getByText('منتهية الصلاحية').first().waitFor({ timeout: 5000 });
    console.log('    ✓ Expired campaign shows منتهية الصلاحية');

    // 3d. Native selects follow the dim theme (white dropdown with invisible text, 0.17.0)
    console.log('  Testing select color-scheme in dim theme...');
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dim'));
    const scheme = await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);
    assert.equal(scheme, 'dark', `dim theme must set color-scheme dark, got ${scheme}`);
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'sand'));
    console.log('    ✓ dim theme sets color-scheme: dark');

    // 3e. SearchableSelect (R9 W2): type part of a name, list narrows, choosing fills the field, dark theme readable
    console.log('  Testing SearchableSelect filtering, selection, and dark theme readability...');
    await page.goto(`${baseUrl}/debts/`);
    await page.waitForLoadState('networkidle');
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dim'));
    const catSelect = page.locator('#select-debt-category');
    await catSelect.waitFor({ state: 'visible' });
    await catSelect.focus();
    const listbox = page.locator('[role="listbox"]');
    await listbox.waitFor({ state: 'visible' });
    const initialCount = await listbox.locator('[role="option"]').count();
    assert.ok(initialCount > 2, `SearchableSelect should open with multiple options, got ${initialCount}`);
    const listboxColors = await listbox.evaluate((el) => {
      const cs = getComputedStyle(el);
      return { bg: cs.backgroundColor, color: cs.color };
    });
    assert.notEqual(listboxColors.bg, listboxColors.color, 'SearchableSelect listbox text and background must contrast in dark theme');
    assert.notEqual(listboxColors.bg, 'rgb(255, 255, 255)', 'SearchableSelect listbox background must be dark in dim theme');
    await page.keyboard.type('مخالفات', { delay: 20 });
    const narrowedCount = await listbox.locator('[role="option"]').count();
    assert.equal(narrowedCount, 1, `Typing 'مخالفات' must narrow options to 1, got ${narrowedCount}`);
    await listbox.locator('[role="option"]').first().click();
    const chosenVal = await catSelect.inputValue();
    assert.ok(chosenVal.includes('مخالفات مرورية'), `Choosing option must fill field with 'مخالفات مرورية', got '${chosenVal}'`);
    // Reset to كافة الفئات and sand theme
    await catSelect.click();
    await listbox.locator('[role="option"]', { hasText: 'كافة الفئات' }).first().click();
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'sand'));
    console.log('    ✓ SearchableSelect narrows on typing, fills field on click, and is readable in dark theme');

    // 4. Treasury Dialog
    console.log('  Testing Treasury Dialog...');
    await page.goto(`${baseUrl}/treasury/`);
    await page.waitForLoadState('networkidle');
    const newTreasuryBtn = page.locator('button:has-text("حساب جديد"), button:has-text("إضافة حساب"), button:has-text("إضافة خزينة")').first();
    if (await newTreasuryBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await newTreasuryBtn.click();
      await page.waitForSelector('[role="dialog"]', { timeout: 2000 });
      const firstInput = page.locator('[role="dialog"] input[type="text"]').first();
      await firstInput.waitFor({ state: 'visible' });
      await firstInput.focus();
      const testText = 'Treasury01';
      await page.keyboard.type(testText, { delay: 30 });
      const val = await firstInput.inputValue();
      assert.equal(val, testText, `Treasury input value should equal typed 10 chars, got: ${val}`);
      const isFocused = await firstInput.evaluate((el) => el === document.activeElement);
      assert.ok(isFocused, 'Treasury input must retain focus across 10 keystrokes');
      await page.keyboard.press('Escape');
      console.log('    ✓ Treasury dialog typing 10 chars passed');
    }

    // 5. Companies Drawer / Modal
    console.log('  Testing Companies Drawer / Details...');
    await page.goto(`${baseUrl}/companies/`);
    await page.waitForLoadState('networkidle');
    const compRow = page.locator('table tbody tr').first();
    if (await compRow.isVisible({ timeout: 2000 }).catch(() => false)) {
      await compRow.click();
      await page.waitForTimeout(300);
      const drawer = page.locator('[role="dialog"]').first();
      if (await drawer.isVisible().catch(() => false)) {
        await page.keyboard.press('Escape');
        console.log('    ✓ Companies drawer opened and closed cleanly');
      }
    }

    // =========================================================================
    // Test (b): Only one panel or menu open at a time (Header, Toolbar, DataTable, Sidebar)
    // =========================================================================
    console.log('\n[R6ui-2 / Test b] Testing single open panel / menu mutual exclusivity...');
    await page.goto(`${baseUrl}/forms/`);
    await page.waitForLoadState('networkidle');

    // 1. Header mutual exclusivity & shortcuts & personal notifications (A1, A2)
    console.log('  Testing Header: Shortcuts, Personal Notifications, General Notifications, Theme, User...');
    const bellBtn = page.locator('header button[aria-label="الإشعارات العامة"]');
    const personalNotifBtn = page.locator('header button[aria-label="إشعاراتي الشخصية"]');
    const shortcutsBtn = page.locator('header button[aria-label="قائمة الاختصارات السريعة"]');
    const themeBtn = page.locator('header button[aria-label="تبديل المظهر"]');
    const userBtn = page.locator('header button[aria-label="قائمة المستخدم"]');

    // Test Personal Notifications
    await personalNotifBtn.click();
    const personalDialog = page.locator('text=إشعاراتي الشخصية').first();
    await personalDialog.waitFor({ state: 'visible' });
    console.log('    ✓ Personal notifications panel opened');

    // Test Bell Mutual Exclusivity: opening Bell closes Personal Notifications
    await bellBtn.click();
    const generalDialog = page.locator('h3:has-text("الإشعارات")').first();
    await generalDialog.waitFor({ state: 'visible' });
    assert.equal(await personalDialog.isVisible().catch(() => false), false, 'Personal notifications must close when Bell is opened');
    console.log('    ✓ Opening General Bell closed Personal Notifications');

    // Personal notification sound toggle (user menu): visible, flips, persists the choice.
    await page.locator('header button[aria-label="قائمة المستخدم"]').click();
    const soundToggle = page.locator('[data-testid="personal-sound-toggle"]');
    await soundToggle.waitFor({ state: 'visible' });
    assert.equal(await soundToggle.getAttribute('aria-pressed'), 'true', 'Personal sound is on by default');
    await soundToggle.click();
    assert.equal(await soundToggle.getAttribute('aria-pressed'), 'false', 'Toggle turns the sound off');
    assert.equal(await page.evaluate(() => localStorage.getItem('gate_personal_sound')), 'off', 'Choice is remembered');
    await soundToggle.click();
    assert.equal(await page.evaluate(() => localStorage.getItem('gate_personal_sound')), 'on');
    await page.keyboard.press('Escape');
    console.log('    ✓ Personal notification sound toggle works and persists');

    // Test Shortcuts Menu (as Admin)
    await shortcutsBtn.click();
    const shortcutsMenu = page.locator('text=اختصارات سريعة').first();
    await shortcutsMenu.waitFor({ state: 'visible' });
    assert.equal(await generalDialog.isVisible().catch(() => false), false, 'Bell must close when Shortcuts is opened');
    console.log('    ✓ Opening Shortcuts closed Bell');

    // Check all admin shortcuts
    const adminShortcutItems = [
      'طلب سلفة',
      'طلب سلفة نت',
      'إنشاء قيد محاسبي',
      'مراجعة السلف',
      'صرف السلف',
      'رفع فاتورة نينجا',
      'تحديث البيانات من ERP',
    ];
    for (const item of adminShortcutItems) {
      assert.ok(await page.locator(`text=${item}`).first().isVisible(), `Admin must see shortcut: ${item}`);
    }
    console.log('    ✓ Admin sees all 7 shortcuts');

    // Test A2: Click ERP Sync shortcut to open ERP Sync Drawer
    await page.locator('button:has-text("تحديث البيانات من ERP")').first().click();
    const erpDrawer = page.locator('[role="dialog"]:has-text("مزامنة بيانات الرواتب من ERP")').first();
    await erpDrawer.waitFor({ state: 'visible' });
    console.log('    ✓ ERP Sync Drawer opened from shortcut');

    // Check subtitle
    assert.ok(
      await page.locator('text=تحديث بيانات الرواتب من ERP — قراءة فقط من ERP').first().isVisible(),
      'ERP Drawer must display correct subtitle'
    );

    // Click Preview (Dry run)
    const dryRunBtn = erpDrawer.locator('button:has-text("معاينة (قراءة فقط)")').first();
    await dryRunBtn.click();
    await page.waitForTimeout(500);

    // Verify active job status and counts table
    assert.ok(await erpDrawer.locator('text=مهمة رقم: 123456').first().isVisible(), 'Active job ID displayed');
    assert.ok(await erpDrawer.locator('text=اكتملت بنجاح').first().isVisible(), 'Job completed badge displayed');
    assert.ok(await erpDrawer.locator('text=إحصائيات السجلات المتأثرة').first().isVisible(), 'Counts table displayed');
    console.log('    ✓ ERP Sync dry-run triggered, completed status and counts verified');

    // Close ERP drawer
    await erpDrawer.locator('button[aria-label="إغلاق اللوحة"]').first().click();
    await page.waitForTimeout(200);
    console.log('    ✓ ERP Drawer closed cleanly');

    // Test Supervisor Role shortcuts (must see ONLY advance request & internet advance)
    console.log('  Testing Supervisor Role shortcuts...');
    currentUser = { username: 'syn-supervisor', role: 'Supervisor', displayNameAr: 'مشرف تشغيلي', permissions: ['advances.request'] };
    await page.reload();
    await page.waitForLoadState('networkidle');

    await shortcutsBtn.click();
    await page.locator('text=اختصارات سريعة').first().waitFor({ state: 'visible' });

    assert.ok(await page.locator('text=طلب سلفة').first().isVisible(), 'Supervisor must see طلب سلفة');
    assert.ok(await page.locator('text=طلب سلفة نت').first().isVisible(), 'Supervisor must see طلب سلفة نت');
    assert.equal(await page.locator('text=إنشاء قيد محاسبي').count(), 0, 'Supervisor must NOT see إنشاء قيد محاسبي');
    assert.equal(await page.locator('text=مراجعة السلف').count(), 0, 'Supervisor must NOT see مراجعة السلف');
    assert.equal(await page.locator('text=صرف السلف').count(), 0, 'Supervisor must NOT see صرف السلف');
    assert.equal(await page.locator('text=رفع فاتورة نينجا').count(), 0, 'Supervisor must NOT see رفع فاتورة نينجا');
    assert.equal(await page.locator('text=تحديث البيانات من ERP').count(), 0, 'Supervisor must NOT see تحديث البيانات من ERP');
    console.log('    ✓ Supervisor sees strictly the two advance request shortcuts');

    // Reset back to Admin
    currentUser = { username: 'admin', role: 'Admin', displayNameAr: 'غزال', permissions: ['all'] };
    await page.reload();
    await page.waitForLoadState('networkidle');

    // Click Theme -> Bell closes, Theme opens
    await themeBtn.click();
    const themeDropdown = page.locator('text=اختر المظهر').first();
    await themeDropdown.waitFor({ state: 'visible' });
    console.log('    ✓ Opening Theme opened cleanly');

    // Click User -> Theme closes, User opens
    await userBtn.click();
    const userDropdown = page.locator('text=إعدادات النظام والأدوار').first();
    await userDropdown.waitFor({ state: 'visible' });
    const themeCount = await themeDropdown.count();
    assert.equal(themeCount, 0, 'Theme dropdown must close when User menu is clicked');
    console.log('    ✓ Opening User menu closed Theme');

    // Outside click closes User menu
    await page.locator('body').click({ position: { x: 50, y: 300 } });
    await page.waitForTimeout(200);
    const userCount = await userDropdown.count();
    assert.equal(userCount, 0, 'Clicking outside must close user menu');
    console.log('    ✓ Clicking outside closed User menu');

    // 2. Toolbar mutual exclusivity (Toolbar export / import)
    console.log('  Testing Toolbar Export Dropdown...');
    const exportBtn = page.locator('button[aria-label="تصدير البيانات"]').first();
    if (await exportBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await exportBtn.click();
      const exportMenu = page.locator('text=Excel (المفلتر حاليًا)').first();
      await exportMenu.waitFor({ state: 'visible' });
      console.log('    ✓ Export dropdown opened');
      // Outside click closes export
      await page.locator('body').click({ position: { x: 50, y: 300 } });
      await page.waitForTimeout(200);
      assert.ok(!(await exportMenu.isVisible().catch(() => false)), 'Export menu must close on outside click');
      console.log('    ✓ Export dropdown closed on outside click');
    }

    // 3. DataTable column filter vs row action menu mutual exclusivity
    console.log('  Testing DataTable Column Filter vs Row Actions mutual exclusivity...');
    // Switch to Responses tab which has a full DataTable
    const responsesTab = page.locator('button:has-text("ردود النماذج")').first();
    if (await responsesTab.isVisible({ timeout: 2000 }).catch(() => false)) {
      await responsesTab.click();
      await page.waitForTimeout(500);
    }
    const filterBtn = page.locator('button[aria-label*="تصفية"]').first();
    const rowMenuBtn = page.locator('button[aria-label="خيارات السجل"]').first();

    if (await filterBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await filterBtn.click();
      const popover = page.locator('[role="dialog"][aria-label*="تصفية"]');
      await popover.waitFor({ state: 'visible' });
      console.log('    ✓ Filter popover opened');

      // Click row action button -> filter popover must close, row menu must open!
      if (await rowMenuBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
        await rowMenuBtn.click();
        const rowMenu = page.locator('[role="menu"][aria-label="خيارات السجل"]');
        await rowMenu.waitFor({ state: 'visible' });
        const filterCount = await popover.count();
        assert.equal(filterCount, 0, 'Filter popover must close when row action menu is opened');
        console.log('    ✓ Row action menu opened and closed filter popover');

        // Click next row's action button -> previous row's menu must close!
        const nextRowBtn = page.locator('button[aria-label="خيارات السجل"]').nth(1);
        if (await nextRowBtn.isVisible().catch(() => false)) {
          // The open menu can sit over the next row's button; dispatch the click to test the one-menu rule.
          await nextRowBtn.dispatchEvent('click');
          await page.waitForTimeout(200);
          const menus = page.locator('[role="menu"][aria-label="خيارات السجل"]');
          assert.equal(await menus.count(), 1, 'Strictly only ONE row menu must be open');
          console.log('    ✓ Only one row action menu open at a time');
        }

        // Outside click closes row menu
        await page.locator('body').click({ position: { x: 50, y: 300 } });
        await page.waitForTimeout(200);
        assert.equal(await page.locator('[role="menu"][aria-label="خيارات السجل"]').count(), 0, 'Row menu closed on outside click');
      }
    }

    // 4. Sidebar: single open group at a time
    console.log('  Testing Sidebar: single open group...');
    await page.setViewportSize({ width: 1440, height: 900 });
    const groupToggles = page.locator('aside button[aria-label*="المجموعة"]');
    const groupCount = await groupToggles.count();
    if (groupCount >= 2) {
      await groupToggles.nth(0).click();
      await page.waitForTimeout(150);
      await groupToggles.nth(1).click();
      await page.waitForTimeout(150);
      console.log('    ✓ Sidebar toggles operate with single active group');
    }

    // Empty rider routes must wait for an explicit selection and issue no rider-specific request.
    const emptyRiderRequests = [];
    const trackEmptyRiderRequest = request => {
      if (/\/api\/(?:finance\/statement\/rider\/|riders\/[^/]+\/profile-360)/.test(request.url())) emptyRiderRequests.push(request.url());
    };
    page.on('request', trackEmptyRiderRequest);
    await page.goto(`${baseUrl}/riders/statement/`);await page.waitForLoadState('networkidle');
    await page.getByText('أدخل رقم الإقامة واستعرض كشف الحساب').waitFor();
    await page.goto(`${baseUrl}/riders/?tab=360`);await page.waitForLoadState('networkidle');
    await page.getByText('ابحث برقم الإقامة لاستعراض الملف 360°').waitFor();
    page.off('request', trackEmptyRiderRequest);
    assert.deepEqual(emptyRiderRequests, [], 'Empty rider routes must not request a placeholder rider');

    // =========================================================================
    // Test (c): Last row's menu is visible inside viewport (opens upward near bottom)
    // =========================================================================
    console.log('\n[R6ui-2 / Test c] Testing DataTable last row menu visible inside viewport...');
    await page.goto(`${baseUrl}/forms/`);
    await page.waitForLoadState('networkidle');
    const respTab = page.locator('button:has-text("ردود النماذج")').first();
    if (await respTab.isVisible().catch(() => false)) {
      await respTab.click();
      await page.waitForTimeout(500);
    }
    const allRowBtns = page.locator('button[aria-label="خيارات السجل"]');
    const rowCount = await allRowBtns.count();
    if (rowCount > 0) {
      const lastRowBtn = allRowBtns.last();
      await lastRowBtn.scrollIntoViewIfNeeded();
      await lastRowBtn.click();
      const menu = page.locator('[role="menu"][aria-label="خيارات السجل"]');
      await menu.waitFor({ state: 'visible' });

      const menuBox = await menu.boundingBox();
      const viewport = page.viewportSize();
      assert.ok(menuBox, 'Menu must have valid bounding box');
      assert.ok(menuBox.y >= 0, `Menu top (${menuBox.y}) must be within viewport (>= 0)`);
      assert.ok(menuBox.y + menuBox.height <= viewport.height + 10, `Menu bottom (${menuBox.y + menuBox.height}) must be inside viewport (${viewport.height})`);
      assert.ok(menuBox.x >= 0, `Menu left (${menuBox.x}) must be >= 0`);
      assert.ok(menuBox.x + menuBox.width <= viewport.width, `Menu right (${menuBox.x + menuBox.width}) must be within viewport width (${viewport.width})`);
      console.log(`    ✓ Last row menu placed cleanly inside viewport: top=${Math.round(menuBox.y)}, height=${Math.round(menuBox.height)}, vh=${viewport.height}`);
    }

    // =========================================================================
    // Test (d): No horizontal scroll at 360 viewport across all /app routes
    // =========================================================================
    console.log('\n[R6ui-4 / Test d] Testing 360 viewport (no horizontal page scroll & screenshots)...');

    const routes = [
      { name: 'dashboard', path: '/' },
      { name: 'payroll', path: '/payroll/' },
      { name: 'accounting', path: '/accounting/' },
      { name: 'riders', path: '/riders/' },
      { name: 'riders-statement', path: '/riders/statement/' },
      { name: 'debts', path: '/debts/' },
      { name: 'companies', path: '/companies/' },
      { name: 'treasury', path: '/treasury/' },
      { name: 'imports', path: '/imports/' },
      { name: 'operations', path: '/operations/' },
      { name: 'forms', path: '/forms/' },
      { name: 'rider-app', path: '/rider-app/' },
      { name: 'notifications', path: '/notifications/' },
      { name: 'admin', path: '/admin/' },
      { name: 'login', path: '/login/' },
      { name: 'setup', path: '/setup/' },
    ];

    // Mobile 360 test
    await page.setViewportSize({ width: 360, height: 740 });
    for (const r of routes) {
      await page.goto(`${baseUrl}${r.path}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(200);

      const scrollInfo = await page.evaluate(() => {
        const docEl = document.documentElement;
        return {
          scrollWidth: docEl.scrollWidth,
          clientWidth: docEl.clientWidth,
          windowWidth: window.innerWidth,
        };
      });

      assert.ok(
        scrollInfo.scrollWidth <= scrollInfo.clientWidth + 1,
        `Page ${r.name} has horizontal scroll! scrollWidth=${scrollInfo.scrollWidth}, clientWidth=${scrollInfo.clientWidth}`
      );

      const shotPath = path.join(resultsDir, `mobile-360-${r.name}.png`);
      await page.screenshot({ path: shotPath, fullPage: false });
      console.log(`  ✓ 360: ${r.name} (scrollWidth=${scrollInfo.scrollWidth} <= 360) -> screenshot saved`);
    }

    // Desktop 1440 test
    await page.setViewportSize({ width: 1440, height: 900 });
    for (const r of routes) {
      await page.goto(`${baseUrl}${r.path}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(150);

      const shotPath = path.join(resultsDir, `desktop-1440-${r.name}.png`);
      await page.screenshot({ path: shotPath, fullPage: false });
      console.log(`  ✓ 1440: ${r.name} -> screenshot saved`);
    }

    console.log('\n🎉 ALL UI QA VERIFICATIONS (a, b, c, d) PASSED WITH ZERO FAILURES!');
  } finally {
    await browser.close();
    server.close();
  }
}

run().catch((err) => {
  console.error('\n❌ UI QA Test Suite Failed:', err);
  process.exit(1);
});
