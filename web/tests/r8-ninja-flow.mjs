// R8 Ninja First Payroll Flow Verification (WF-4)
// Drives the real browser flow end-to-end:
// 1. Pending invoice created by user A (maker) -> asserts maker cannot approve (maker ≠ checker).
// 2. User B (checker) approves invoice in browser.
// 3. Review draft has 0 blockers and per-account totals match invoice.
// 4. Creates payroll cycle and advances stages with real buttons until lines exist.
// 5. Opens Explain Drawer («كيف حُسب؟») and asserts net shown matches GET /api/pay/cycles/{id}/lines.
// Tests light and dim modes at 1440 and 360 viewports with screenshots in test-results/r8-web/.

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

console.log('--- Starting Gate Payroll R8 Ninja Flow Test Suite (WF-4) ---');

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

  // Setup synthetic admin User A (Maker)
  const userA = 'r8-maker-' + randomBytes(4).toString('hex');
  const passA = 'Password123!' + randomBytes(4).toString('hex');

  const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: userA, password: passA }),
  });
  assert.equal(setupRes.status, 200, 'Setup admin user should return 200');
  const setCookieA = setupRes.headers.get('set-cookie');
  assert.ok(setCookieA, 'Must receive auth cookie for User A');
  const cookieA = setCookieA.split(';')[0];
  console.log('✓ User A (maker) registered and authenticated');

  // Create User B (Checker) via API
  const userB = 'r8-checker-' + randomBytes(4).toString('hex');
  const passB = 'Password123!' + randomBytes(4).toString('hex');

  const createBRes = await fetch(`${baseUrl}/api/users`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Cookie: cookieA },
    body: JSON.stringify({ username: userB, password: passB, role: 'Admin' }),
  });
  assert.equal(createBRes.status, 200, 'Create User B must succeed');

  const loginBRes = await fetch(`${baseUrl}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: userB, password: passB }),
  });
  assert.equal(loginBRes.status, 200, 'Login User B must succeed');
  const setCookieB = loginBRes.headers.get('set-cookie');
  assert.ok(setCookieB, 'Must receive auth cookie for User B');
  const cookieB = setCookieB.split(';')[0];
  console.log('✓ User B (checker) created and authenticated');

  // Get seeded company (BW)
  const compRes = await fetch(`${baseUrl}/api/companies`, {
    headers: { Cookie: cookieA },
  });
  const companies = await compRes.json();
  const bwCompany = companies.find((c) => c.code === 'BW' || c.id === 'BW') || companies[0] || { id: 'BW' };
  const companyId = bwCompany.id;

  // Seed baseline starting conditions only (no pre-seeded cycle or lines)
  const db = new DatabaseSync(path.join(tempDir, 'payroll.db'));
  function put(kind, record) {
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      record.id,
      kind,
      JSON.stringify(record)
    );
  }

  const riderIqama = '2000000789';
  put('rider', {
    id: riderIqama,
    iqama: riderIqama,
    name: 'سالم الشمري',
    sponsorId: companyId,
    status: 'active',
    sponsorshipStatus: 'internal',
    isDeliveryRider: true,
    payCategory: 'sponsored_private',
  });

  put('platform_account', {
    id: 'NJ-ACC-001',
    platformCode: 'ninja',
    companyId: companyId,
    name: 'حساب نينجا الرياض 1',
    assignments: [
      {
        riderId: riderIqama,
        RawEffectiveFrom: '2026-10-01T00:00:00+03:00',
        RawEffectiveTo: '2026-10-31T23:59:59+03:00',
      },
    ],
  });

  put('ninja_account', {
    id: 'ninja-captain-001',
    platformAccountId: 'NJ-ACC-001',
    CaptainId: 'NJ-CAP-001',
  });

  // Approved global settings for 2026-10-01
  put('pay_setting', {
    id: 'global-settings-2026-10',
    kind: 'pay_setting',
    app: 'global',
    code: 'default',
    version: 1,
    status: 'approved',
    effectiveFrom: '2026-10-01',
    createdBy: userA,
    approvedBy: 'financial-auditor',
    data: {
      name: 'إعدادات الرواتب العامة',
      values: {
        standardDays: 30,
        februaryDays: 28,
        billingCutoff: '14:00',
        timeZone: 'Asia/Riyadh',
        roundingDigits: 2,
        roundingMode: 'AwayFromZero',
        vehicleRent: { company_car: 1800, company_bike: 800, private: 0 },
        accounts: {
          salaryExpense: '512101',
          vehicleIncomeCar: '421101',
          vehicleIncomeBike: '421102',
          outputVat: '213201',
        },
      },
    },
  });

  // Approved Ninja terms for 2026-10
  put('pay_terms', {
    id: 'ninja-terms-2026-10',
    kind: 'pay_terms',
    app: 'ninja',
    period: '2026-10',
    code: 'default',
    version: 1,
    status: 'approved',
    createdBy: userA,
    approvedBy: 'financial-auditor',
    data: {
      name: 'شروط نينجا الأصلية',
      billingModel: 'per_order',
      parameters: {
        pricePerOrder: 12.8571,
        companyCostAccount: '526402',
      },
    },
  });

  // Approved Ninja eligibility for 2026-10
  const rulesId = 'ninja-eligibility-2026-10';
  put('pay_eligibility', {
    id: rulesId,
    kind: 'pay_eligibility',
    app: 'ninja',
    period: '2026-10',
    code: 'default',
    version: 1,
    status: 'approved',
    createdBy: userA,
    approvedBy: 'auditor-chief',
    data: {
      name: 'شروط أهلية نينجا لشهر أكتوبر',
      rules: [
        { metric: 'orders', operator: 'gte', threshold: 250, effect: { type: 'zero' }, included: true },
        { metric: 'days', operator: 'gte', threshold: 20, effect: { type: 'zero' }, included: true },
      ],
    },
  });

  // Approved internal Ninja scheme for 2026-10
  const schemeId = 'ninja-scheme-2026-10';
  put('pay_scheme', {
    id: schemeId,
    kind: 'pay_scheme',
    app: 'ninja',
    period: '2026-10',
    code: 'default',
    version: 1,
    status: 'approved',
    createdBy: userA,
    approvedBy: 'auditor-chief',
    data: {
      name: 'نظام نينجا الداخلي',
      type: 'percent_of_gross',
      accountingRole: 'principal',
      eligibilitySetId: rulesId,
      parameters: {
        categories: {
          sponsored_company_car: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
          sponsored_company_bike: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
          sponsored_private: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
          external_company_car: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
          external_company_bike: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
          external_private: { enabled: true, companyPercent: 10, chargeCompanyVehicle: false },
        },
      },
    },
  });

  // Approved scheme assignment for rider
  put('pay_assignment', {
    id: 'pay-assign-ninja-001',
    rider: riderIqama,
    app: 'ninja',
    period: '2026-10',
    status: 'approved',
    schemeId: schemeId,
    choiceApprovedAt: '2026-09-25T10:00:00Z',
  });

  // Seed daily operational records (25 days summing to exactly 350 orders)
  for (let day = 1; day <= 25; day++) {
    const dd = String(day).padStart(2, '0');
    put('ninja_daily', {
      id: `nd-2026-10-${dd}`,
      companyId: companyId,
      platformAccountId: 'NJ-ACC-001',
      riderId: riderIqama,
      date: `2026-10-${dd}T12:00:00Z`,
      orders: day === 25 ? 110 : 10,
    });
  }

  // Pending invoice created by User A (maker)
  const invoiceId = 'inv-ninja-2026-10-001';
  put('platform_invoice', {
    id: invoiceId,
    platform: 'Ninja',
    companyId: companyId,
    period: '2026-10',
    reference: 'NJ-INV-202610-01',
    status: 'invoiced',
    approvalStatus: 'pending',
    createdBy: userA,
    amountExclVat: 4500,
    deductionsTotal: 300,
    vat: 675,
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
    deductions: [
      { category: 'penalty', amount: 300, level: 'account_level', classifiedBy: userA },
    ],
  });

  db.close();
  console.log('✓ Seeded baseline records into SQLite store');

  // Launch Playwright
  browser = await chromium.launch({ headless: true });

  const parseCookie = (raw) => {
    const [name, ...rest] = raw.split('=');
    return { name: name.trim(), value: rest.join('=').trim() };
  };

  const parsedCookieA = parseCookie(cookieA);
  const parsedCookieB = parseCookie(cookieB);

  // Step 6: Verify User A (Maker) sees NO approve button (maker ≠ checker)
  {
    console.log('Testing maker ≠ checker: verifying User A cannot approve own invoice...');
    const contextA = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextA.addCookies([
      {
        name: parsedCookieA.name,
        value: parsedCookieA.value,
        domain: '127.0.0.1',
        path: '/',
      },
    ]);
    const pageA = await contextA.newPage();
    await pageA.goto(`${baseUrl}/app/payroll?tab=ninja_upload&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    await pageA.waitForSelector('text=رفع الفاتورة', { timeout: 8000 });
    const approveBtnCountA = await pageA.locator('#btn-approve-ninja-invoice').count();
    assert.equal(
      approveBtnCountA,
      0,
      'User A (maker) must see 0 approve buttons on their own invoice'
    );
    console.log('✓ Maker cannot approve own invoice (asserted 0 approve buttons for User A)');
    await contextA.close();
  }

  // Step 1: User B (Checker) approves the invoice in the browser
  {
    console.log('Driving User B in browser: approving pending Ninja invoice...');
    const contextB = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextB.addCookies([
      {
        name: parsedCookieB.name,
        value: parsedCookieB.value,
        domain: '127.0.0.1',
        path: '/',
      },
    ]);
    const pageB = await contextB.newPage();
    await pageB.goto(`${baseUrl}/app/payroll?tab=ninja_upload&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const approveBtn = pageB.locator('#btn-approve-ninja-invoice');
    await approveBtn.waitFor({ state: 'visible', timeout: 8000 });
    console.log('✓ Approve button is visible for User B (checker)');
    await approveBtn.click();
    await pageB.waitForTimeout(1000);
    await pageB.waitForLoadState('networkidle');

    // Verify invoice approved in UI
    const approvedBadge = pageB.locator('text=معتمدة');
    await approvedBadge.first().waitFor({ state: 'visible', timeout: 5000 });
    console.log('✓ Invoice approved successfully by User B in browser');
    await contextB.close();
  }

  // Helper to open context for User B with viewport and theme
  async function makeUserBPage(width, height, isDim) {
    const context = await browser.newContext({ viewport: { width, height } });
    await context.addCookies([
      {
        name: parsedCookieB.name,
        value: parsedCookieB.value,
        domain: '127.0.0.1',
        path: '/',
      },
    ]);
    const page = await context.newPage();
    await page.addInitScript((dim) => {
      window.localStorage.setItem('gate_theme', dim ? 'dim' : 'light');
      window.localStorage.setItem('gate_balances_visible', 'true');
    }, isDim);
    return { context, page };
  }

  // Matrix: Capture A4 Stepper and A5 Draft across all 4 viewport/theme configs
  const matrix = [
    { width: 1440, height: 900, isDim: false, suffix: '1440-light' },
    { width: 1440, height: 900, isDim: true, suffix: '1440-dim' },
    { width: 360, height: 780, isDim: false, suffix: '360-light' },
    { width: 360, height: 780, isDim: true, suffix: '360-dim' },
  ];

  console.log('Capturing A4 Stepper and A5 Draft across viewports & themes...');
  for (const { width, height, isDim, suffix } of matrix) {
    const { context, page } = await makeUserBPage(width, height, isDim);

    // 1. Visit Ninja upload screen (A4)
    await page.goto(`${baseUrl}/app/payroll?tab=ninja_upload&period=2026-10`, {
      waitUntil: 'networkidle',
    });
    const stepper = page.locator('text=رفع الفاتورة');
    await stepper.first().waitFor({ state: 'visible', timeout: 8000 });
    await page.screenshot({
      path: path.join(resultsDir, `ninja-a4-stepper-${suffix}.png`),
      fullPage: false,
    });
    console.log(`  ✓ [${suffix}] Captured ninja-a4-stepper-${suffix}.png`);

    // 2. Visit Cycle screen (A5) Draft Tab
    await page.goto(`${baseUrl}/app/payroll?tab=cycle&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const draftTabBtn = page.locator('button:has-text("مسودة المراجعة المقترحة")');
    if (await draftTabBtn.count() > 0) {
      await draftTabBtn.click();
      await page.waitForTimeout(500);
      await page.waitForLoadState('networkidle');
    }

    // Step 2 Assertions: Assert 0 blockers and per-account total equals invoice line total
    const zeroBlockersEl = page.locator('text=0 Blockers');
    await zeroBlockersEl.first().waitFor({ state: 'visible', timeout: 8000 });

    const totalEl = page.locator('text=4,500.00');
    await totalEl.first().waitFor({ state: 'visible', timeout: 5000 });
    console.log(`  ✓ [${suffix}] Asserted 0 blockers and per-account total (4,500.00 SAR)`);

    await page.screenshot({
      path: path.join(resultsDir, `ninja-a5-draft-${suffix}.png`),
      fullPage: false,
    });
    console.log(`  ✓ [${suffix}] Captured ninja-a5-draft-${suffix}.png`);

    await context.close();
  }

  // Step 3 & 4: User B creates cycle and advances stages 1 -> 4
  console.log('Driving User B: creating cycle and advancing stages 1..4...');
  let cycleId = null;
  {
    const { context, page } = await makeUserBPage(1440, 900, false);
    await page.goto(`${baseUrl}/app/payroll?tab=cycle&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const draftTabBtn = page.locator('button:has-text("مسودة المراجعة المقترحة")');
    if (await draftTabBtn.count() > 0) {
      await draftTabBtn.click();
      await page.waitForTimeout(500);
      await page.waitForLoadState('networkidle');
    }

    const createCycleBtn = page.locator('button:has-text("إنشاء دورة الرواتب")');
    await createCycleBtn.waitFor({ state: 'visible', timeout: 8000 });
    await createCycleBtn.click();
    console.log('✓ Clicked «إنشاء دورة الرواتب»');

    // Wait for cycle view to appear
    await page.waitForTimeout(1000);
    await page.waitForLoadState('networkidle');

    // Advance stages with the stepper button until Stage 4
    for (let stage = 1; stage <= 4; stage++) {
      const advanceBtn = page.locator('button:has-text("تقديم المرحلة إلى")');
      await advanceBtn.first().waitFor({ state: 'visible', timeout: 8000 });
      const btnText = await advanceBtn.first().textContent();
      console.log(`  Clicking: ${btnText?.trim()}`);
      await advanceBtn.first().click();
      await page.waitForTimeout(800);
      await page.waitForLoadState('networkidle');
    }

    // Verify stage 4 reached and lines exist
    const explainBtn = page.locator('button:has-text("كيف حُسب؟")');
    await explainBtn.first().waitFor({ state: 'visible', timeout: 8000 });
    console.log('✓ Stage 4 reached: payroll cycle lines generated and visible in UI');

    // Get cycle id from API
    const cyclesRes = await fetch(`${baseUrl}/api/pay/cycles?period=2026-10`, {
      headers: { Cookie: cookieB },
    });
    const cyclesData = await cyclesRes.json();
    cycleId = (cyclesData.items || cyclesData)[0]?.id;
    assert.ok(cycleId, 'Cycle must exist in API');
    console.log('✓ Active cycle id verified from API:', cycleId);

    await context.close();
  }

  // Fetch line from API to verify «كيف حُسب؟» net entitlement
  const linesApiRes = await fetch(`${baseUrl}/api/pay/cycles/${cycleId}/lines`, {
    headers: { Cookie: cookieB },
  });
  assert.equal(linesApiRes.status, 200, 'GET /api/pay/cycles/{id}/lines must succeed');
  const linesData = await linesApiRes.json();
  const riderLine = (linesData.items || linesData).find((l) => l.rider === riderIqama);
  assert.ok(riderLine, 'Must find line for rider ' + riderIqama);
  const expectedNet = riderLine.entitlement ?? riderLine.gross ?? 0;
  const expectedNetFormatted = expectedNet.toLocaleString('en-US', { minimumFractionDigits: 2 });
  console.log(`✓ API calculated entitlement for rider ${riderIqama}: ${expectedNetFormatted} SAR`);

  // Matrix: Capture A5 Cycle and A5 Explain Drawer across all 4 viewport/theme configs
  console.log('Capturing A5 Cycle and A5 Explain Drawer across viewports & themes...');
  for (const { width, height, isDim, suffix } of matrix) {
    const { context, page } = await makeUserBPage(width, height, isDim);

    await page.goto(`${baseUrl}/app/payroll?tab=cycle&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    // Make sure active cycle view is selected
    const cycleTabBtn = page.locator('button:has-text("دورة الرواتب ومراحل المسير")');
    if (await cycleTabBtn.count() > 0) {
      await cycleTabBtn.click();
      await page.waitForTimeout(400);
    }

    // Verify Stepper Stage 4
    await page.screenshot({
      path: path.join(resultsDir, `ninja-a5-cycle-${suffix}.png`),
      fullPage: false,
    });
    console.log(`  ✓ [${suffix}] Captured ninja-a5-cycle-${suffix}.png`);

    // Step 5: Click «كيف حُسب؟» and assert net value matches API
    const explainBtn = page.locator('button:has-text("كيف حُسب؟")');
    await explainBtn.first().waitFor({ state: 'visible', timeout: 5000 });
    await explainBtn.first().click();
    await page.waitForTimeout(400);

    // Verify Explain Drawer opened
    const drawerHeader = page.locator(`text=شرح احتساب مستحقات المندوب: ${riderIqama}`);
    await drawerHeader.first().waitFor({ state: 'visible', timeout: 5000 });

    // Assert net shown in drawer equals API value
    const drawerText = await page.locator('.fixed').first().textContent();
    assert.ok(
      drawerText.includes(expectedNetFormatted),
      `Explain drawer must display API calculated net: ${expectedNetFormatted}`
    );
    console.log(`  ✓ [${suffix}] Asserted Explain Drawer net displayed matches API (${expectedNetFormatted} SAR)`);

    await page.screenshot({
      path: path.join(resultsDir, `ninja-a5-explain-${suffix}.png`),
      fullPage: false,
    });
    console.log(`  ✓ [${suffix}] Captured ninja-a5-explain-${suffix}.png`);

    await context.close();
  }

  console.log('🎉 ALL R8 NINJA FLOW E2E TESTS PASSED (WF-4 COMPLETE WITH ALL 16 SCREENSHOTS)!');
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
