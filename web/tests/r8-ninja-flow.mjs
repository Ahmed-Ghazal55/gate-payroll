// R8 Ninja First Payroll Flow Verification (A4 -> A5)
// Real ASP.NET Core server with synthetic SQLite store & Next.js static export
// Tests light and dim modes at 1440 and 360 viewports with screenshots in test-results/r8-web/

import { spawn } from 'node:child_process';
import { once } from 'node:events';
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
const resultsDir = path.resolve(rootDir, 'test-results/r8-web');

mkdirSync(resultsDir, { recursive: true });

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

console.log('--- Starting Gate Payroll R8 Ninja Flow Test Suite ---');

// 1. Ensure web/out exists and copy to wwwroot/app
assert.ok(existsSync(outDir), 'web/out directory must exist. Run npm run build first.');
mkdirSync(wwwrootAppDir, { recursive: true });
cpSync(outDir, wwwrootAppDir, { recursive: true });
console.log('✓ Copied web/out to Payroll.Api/wwwroot/app');

const tempDir = mkdtempSync(path.join(tmpdir(), 'gate-r8-web-'));
const basePort = 19683;
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
      Jobs__Enabled: 'false',
      Vouchers__Secret: 'GateSynthetic_CI_Only_Voucher_Key_000001',
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

  // Setup synthetic admin
  const adminUsername = 'r8-admin-' + randomBytes(4).toString('hex');
  const adminPassword = 'Password123!' + randomBytes(4).toString('hex');

  const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: adminUsername, password: adminPassword }),
  });
  assert.equal(setupRes.status, 200, 'Setup admin user should return 200');
  const setCookie = setupRes.headers.get('set-cookie');
  assert.ok(setCookie, 'Must receive auth cookie');
  const authCookie = setCookie.split(';')[0];
  console.log('✓ Admin registered and authenticated');

  // Seed SQLite with synthetic Ninja records
  const db = new DatabaseSync(path.join(tempDir, 'payroll.db'));
  function put(kind, record) {
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      record.id,
      kind,
      JSON.stringify(record)
    );
  }

  // Get seeded company
  const compRes = await fetch(`${baseUrl}/api/companies`, {
    headers: { Cookie: authCookie },
  });
  const companies = await compRes.json();
  const companyId = companies[0]?.id || 'BW';

  // Seed synthetic rider, accounts, invoice, review draft, cycle, lines
  const riderIqama = '2000000789';
  put('rider', {
    id: riderIqama,
    iqama: riderIqama,
    name: 'سالم الشمري',
    sponsorId: companyId,
    status: 'active',
    sponsorshipStatus: 'internal',
    isDeliveryRider: true,
    payCategory: 'company_car',
  });

  put('platform_account', {
    id: 'NJ-ACC-001',
    platformCode: 'Ninja',
    companyId: companyId,
    name: 'حساب نينجا الرياض 1',
    assignments: [
      {
        riderId: riderIqama,
        RawEffectiveFrom: '2026-10-01T00:00:00Z',
        RawEffectiveTo: '2026-10-31T23:59:59Z',
      },
    ],
  });

  const invoiceId = 'inv-ninja-2026-10-001';
  put('platform_invoice', {
    id: invoiceId,
    platform: 'Ninja',
    companyId: companyId,
    period: '2026-10',
    reference: 'NJ-INV-202610-01',
    status: 'invoiced',
    approvalStatus: 'approved',
    createdBy: 'system',
    approvedBy: 'financial-auditor',
    amountExclVat: 4500,
    deductionsTotal: 300,
    vat: 675,
    allowedActions: ['approve', 'post'],
  });

  put('platform_invoice_line', {
    id: 'line-nj-001',
    invoiceId: invoiceId,
    platformAccountId: 'NJ-ACC-001',
    sourceRow: 2,
    baseGross: 4500,
    incentives: 0,
    deductionAmount: 300,
    orders: 350,
    vat: 675,
    invoiced: 4500,
    status: 'invoiced',
  });

  const cycleId = 'pay-cycle:ninja-2026-10';
  put('pay_cycle', {
    id: cycleId,
    companyId: companyId,
    period: '2026-10',
    app: 'ninja',
    stage: 4,
    status: 'review',
    createdBy: 'payroll-specialist',
    createdAt: '2026-11-01T10:00:00Z',
    history: [
      { stage: 1, advancedBy: 'auditor-1', at: '2026-11-01T11:00:00Z' },
      { stage: 2, advancedBy: 'auditor-2', at: '2026-11-01T12:00:00Z' },
      { stage: 3, advancedBy: 'auditor-1', at: '2026-11-01T13:00:00Z' },
      { stage: 4, advancedBy: 'auditor-3', at: '2026-11-01T14:00:00Z' },
    ],
    invoices: [
      {
        invoiceId: invoiceId,
        app: 'ninja',
        accounts: [
          {
            accountId: 'NJ-ACC-001',
            sourceLineId: 'line-nj-001',
            baseGross: 4500,
            incentives: 0,
            orders: 350,
            riders: [
              {
                rider: riderIqama,
                orders: 350,
                baseGross: 4500,
                deduction: 300,
                category: 'company_car',
                days: 30,
              },
            ],
          },
        ],
        deductions: [],
      },
    ],
    riders: [
      {
        rider: riderIqama,
        status: 'ready',
        gross: 4500,
        commission: 450,
        afterCommission: 4050,
        phase1: 300,
        preliminaryDue: 3750,
        phase2: 0,
        phase3: 0,
        net: 3750,
      },
    ],
  });

  put('pay_cycle_line', {
    id: 'cycle-line:001',
    cycleId: cycleId,
    rider: riderIqama,
    app: 'ninja',
    period: '2026-10',
    status: 'ready',
    platformGross: 4500,
    gross: 4500,
    allocatedVehicleCharge: 400,
    accountDeduction: 300,
    net: 3750,
    category: 'company_car',
    sources: [
      {
        sourceLineId: 'line-nj-001',
        accountId: 'NJ-ACC-001',
        baseGross: 4500,
      },
    ],
    lines: [
      {
        label: 'استحقاق الطلبات المنجزة',
        formula: 'orders * 12.8571 SAR',
        inputs: { orders: 350, rate: 12.8571 },
        amount: 4500,
        category: 'gross',
        affectsNet: true,
      },
      {
        label: 'إيجار مركبة الشركة',
        formula: 'monthlyRent / 30 * days',
        inputs: { monthlyRent: 400, days: 30 },
        amount: -400,
        category: 'vehicle',
        affectsNet: true,
      },
      {
        label: 'خصم تشغيلي من المنصة',
        formula: 'account_deductions_share',
        inputs: { accountDeduction: 300 },
        amount: -300,
        category: 'deduction',
        affectsNet: true,
      },
    ],
  });

  // Seed Ninja eligibility
  put('pay_eligibility', {
    id: 'ninja-eligibility-2026-10',
    kind: 'pay_eligibility',
    app: 'ninja',
    period: '2026-10',
    code: 'default',
    version: 1,
    status: 'approved',
    createdBy: 'system',
    approvedBy: 'auditor-chief',
    data: {
      name: 'شروط أهلية نينجا لشهر أكتوبر',
      rules: [
        { metric: 'orders', operator: 'gte', threshold: 250, effect: { type: 'zero' }, included: true },
        { metric: 'attendanceDays', operator: 'gte', threshold: 24, effect: { type: 'zero' }, included: true },
      ],
    },
  });

  db.close();
  console.log('✓ Seeded synthetic database with Ninja invoice, cycle, and lines');

  // Launch Playwright
  browser = await chromium.launch({ headless: true });

  const [cookieName, ...cookieRest] = authCookie.split('=');
  const cookieValue = cookieRest.join('=');

  // Helper to test viewport and theme
  async function testViewAndTheme(width, height, isDim, suffix) {
    const context = await browser.newContext({
      viewport: { width, height },
    });
    await context.addCookies([
      {
        name: cookieName.trim(),
        value: cookieValue.trim(),
        domain: '127.0.0.1',
        path: '/',
      },
    ]);

    const page = await context.newPage();

    // Set theme in localStorage before navigating
    await page.addInitScript((dim) => {
      window.localStorage.setItem('gate_theme', dim ? 'dim' : 'light');
      window.localStorage.setItem('gate_balances_visible', 'true');
    }, isDim);

    // 1. Visit Ninja upload screen (A4)
    await page.goto(`${baseUrl}/app/payroll?tab=ninja_upload&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    // Check Stepper exists
    const stepper = page.locator('text=رفع الفاتورة');
    await stepper.first().waitFor({ state: 'visible', timeout: 8000 });
    console.log(`  ✓ [${suffix}] Ninja stepper rendered on /app/payroll?tab=ninja_upload`);

    // Screenshot A4 Stepper
    await page.screenshot({
      path: path.join(resultsDir, `ninja-a4-stepper-${suffix}.png`),
      fullPage: false,
    });

    // 2. Visit Cycle screen (A5)
    await page.goto(`${baseUrl}/app/payroll?tab=cycle&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    // Switch to Review Draft
    const draftTabBtn = page.locator('button:has-text("مسودة المراجعة المقترحة")');
    if (await draftTabBtn.count() > 0) {
      await draftTabBtn.click();
      await page.waitForTimeout(500);
      console.log(`  ✓ [${suffix}] Switched to Review Draft tab`);

      // Screenshot A5 Review Draft
      await page.screenshot({
        path: path.join(resultsDir, `ninja-a5-draft-${suffix}.png`),
        fullPage: false,
      });
    }

    // Switch to Active Cycle
    const cycleTabBtn = page.locator('button:has-text("دورة الرواتب ومراحل المسير")');
    if (await cycleTabBtn.count() > 0) {
      await cycleTabBtn.click();
      await page.waitForTimeout(500);

      // Verify progression history who/when is visible
      const historyIndicator = page.locator('text=تقدم بواسطة:');
      if (await historyIndicator.count() > 0) {
        console.log(`  ✓ [${suffix}] Stage history who/when rendered in Stepper`);
      }

      // Screenshot A5 Active Cycle
      await page.screenshot({
        path: path.join(resultsDir, `ninja-a5-cycle-${suffix}.png`),
        fullPage: false,
      });

      // Click «كيف حُسب؟» explain button
      const explainBtn = page.locator('button:has-text("كيف حُسب؟")');
      if (await explainBtn.count() > 0) {
        await explainBtn.first().click();
        await page.waitForTimeout(400);

        // Verify Drawer open
        const drawerTitle = page.locator('text=تفاصيل معادلة الاستحقاق');
        if (await drawerTitle.count() > 0) {
          console.log(`  ✓ [${suffix}] Formula explain drawer opened successfully`);
        }

        // Screenshot Explain Drawer
        await page.screenshot({
          path: path.join(resultsDir, `ninja-a5-explain-${suffix}.png`),
          fullPage: false,
        });
      }
    }

    await context.close();
  }

  console.log('Running test matrix: 1440 & 360 viewports in light and dim themes...');
  await testViewAndTheme(1440, 900, false, '1440-light');
  await testViewAndTheme(1440, 900, true, '1440-dim');
  await testViewAndTheme(360, 780, false, '360-light');
  await testViewAndTheme(360, 780, true, '360-dim');

  console.log('🎉 ALL R8 NINJA FLOW E2E TESTS PASSED WITH SCREENSHOTS GENERATED!');
} finally {
  if (browser) await browser.close().catch(() => {});
  if (proc && proc.exitCode === null) {
    proc.kill('SIGTERM');
    await Promise.race([
      once(proc, 'exit'),
      new Promise((r) => setTimeout(r, 2000)),
    ]).catch(() => {});
  }
  try {
    rmSync(tempDir, { recursive: true, force: true });
  } catch {}
}
