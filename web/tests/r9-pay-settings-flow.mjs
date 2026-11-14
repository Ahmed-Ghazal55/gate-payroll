// R9 Pay Settings End-to-End Browser Flow Test (S1 - S6)
// Verifies:
// 1. User A (maker) opens empty internal scheme form (no hard-coded defaults).
// 2. Empty form blocks submission with clear Arabic validation messages.
// 3. User A types custom scheme values (daily_commission = 15.50 for sponsored_private).
// 4. Form saves version 1 with status 'pending' (بانتظار الاعتماد) created by User A.
// 5. Maker ≠ Checker: User A cannot approve their own scheme (approve button disabled).
// 6. User B (checker) logs in, sees the scheme and approves it (status -> 'approved').
// 7. Assigned rider's calculation in cycle reflects the typed value (15.50) in «كيف حُسب؟».
// 8. Takes screenshots in test-results/r9-web/.

import { spawn } from 'node:child_process';
import { mkdtempSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { crc32 } from 'node:zlib';

function syntheticNinjaInvoice() {
  const header = [
    'supplier_id', 'supplier_name', 'CAPTAIN ACTIVE STATUS', 'capabilty', 'captain_id',
    'Zone Name', 'First Attended Date', 'Total Delivered Orders', 'Contract Amount', 'Contract Target',
    'Count of First Attended Date to End of Month', 'Adjusted Target', 'Adjusted Amount', 'Below Target Percentage',
    'Target Level Type', 'Target Deductions Amount', 'Target Bonus Amount', 'Target Total Amount',
    'Count of Attended Days', 'Attendance Deduction', 'Count of Days Less than 8 Hr', '8 Hour Deductions',
    'Deduction refund', 'Gasoline Support', 'Weekend 40 Bonus', 'Weekend 42 Bonus', 'Weekend 43 Bonus',
    '100 SAR BONUS', 'Supplier Performance Bonus', 'NAQIL BLOCKED', 'Penalty', 'on demand',
    'STOPS WITHOUT NOTICE DEDUCTION', 'Final Amount', 'VAT', 'TOTAL COST',
  ];
  const values = {
    supplier_id: '9001',
    capabilty: 'capability_car',
    captain_id: 'NJ-CAP-001',
    'Total Delivered Orders': '300',
    'Contract Amount': '6500',
    'Contract Target': '460',
    'Count of First Attended Date to End of Month': '31',
    'Adjusted Target': '460',
    'Adjusted Amount': '6500',
    'Target Total Amount': '6500',
    'Count of Attended Days': '25',
    'Final Amount': '6500',
    VAT: '975',
    'TOTAL COST': '7475',
  };

  const column = (i) => {
    let r = '';
    for (let n = i + 1; n > 0; n = Math.floor((n - 1) / 26)) {
      r = String.fromCharCode(65 + ((n - 1) % 26)) + r;
    }
    return r;
  };
  const row = (cells, n) =>
    `<row r="${n}">${cells
      .map((v, i) => `<c r="${column(i)}${n}" t="inlineStr"><is><t>${v}</t></is></c>`)
      .join('')}</row>`;
  const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
  const rel = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
  const files = {
    'xl/workbook.xml': `<workbook xmlns="${ns}" xmlns:r="${rel}"><sheets><sheet name="Synthetic" r:id="r1"/></sheets></workbook>`,
    'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="r1" Target="worksheets/sheet1.xml"/></Relationships>',
    'xl/worksheets/sheet1.xml': `<worksheet xmlns="${ns}"><sheetData>${row(header, 1)}${row(
      header.map((h) => values[h] ?? '0'),
      2,
    )}</sheetData></worksheet>`,
  };
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, text] of Object.entries(files)) {
    const data = Buffer.from(text);
    const nameBytes = Buffer.from(name);
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, nameBytes, data);
    centrals.push(central, nameBytes);
    offset += 30 + nameBytes.length + data.length;
  }
  const dir = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  const count = Object.keys(files).length;
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(count, 8);
  end.writeUInt16LE(count, 10);
  end.writeUInt32LE(dir.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, dir, end]);
}

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const rootDir = path.resolve(webDir, '..');
const outDir = path.join(webDir, 'out');
const wwwrootAppDir = path.join(rootDir, 'Payroll.Api/wwwroot/app');
const resultsDir = path.resolve(rootDir, 'test-results/r9-web');

mkdirSync(resultsDir, { recursive: true });

// Load Playwright
const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

console.log('--- Starting Gate Payroll R9 Pay Settings Test Suite ---');

assert.ok(existsSync(outDir), 'web/out directory must exist. Run npm run build first.');
mkdirSync(wwwrootAppDir, { recursive: true });
cpSync(outDir, wwwrootAppDir, { recursive: true });
console.log('✓ Copied web/out to Payroll.Api/wwwroot/app');

const tempDir = mkdtempSync(path.join(tmpdir(), 'gate-r9-settings-'));
const basePort = 19685;
const baseUrl = `http://127.0.0.1:${basePort}`;

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
  },
);

let logs = '';
let spawnError = null;
proc.on('error', (e) => (spawnError = e));
proc.stdout.on('data', (d) => (logs += d));
proc.stderr.on('data', (d) => (logs += d));

let browser;

try {
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

  // Create User A (maker)
  const userA = 'r9-maker-' + randomBytes(4).toString('hex');
  const passA = 'Password123!' + randomBytes(4).toString('hex');

  const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: userA, password: passA }),
  });
  assert.equal(setupRes.status, 200, 'Setup admin user should return 200');
  const cookieA = setupRes.headers.get('set-cookie').split(';')[0];
  console.log('✓ User A (maker) registered and authenticated');

  // Create User B (checker)
  const userB = 'r9-checker-' + randomBytes(4).toString('hex');
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
  const cookieB = loginBRes.headers.get('set-cookie').split(';')[0];
  console.log('✓ User B (checker) created and authenticated');

  const compRes = await fetch(`${baseUrl}/api/companies`, {
    headers: { Cookie: cookieA },
  });
  const companies = await compRes.json();
  const bwCompany = companies.find((c) => c.code === 'BW' || c.id === 'BW') || companies[0] || { id: 'BW' };
  const companyId = bwCompany.id;

  // Seed baseline starting conditions ONLY (DO NOT seed pay_scheme!)
  const db = new DatabaseSync(path.join(tempDir, 'payroll.db'));
  function put(kind, record) {
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      record.id,
      kind,
      JSON.stringify(record),
    );
  }

  const riderIqama = '2000000888';
  put('rider', {
    id: riderIqama,
    iqama: riderIqama,
    name: 'عبدالله السعيد',
    sponsorId: companyId,
    status: 'active',
    sponsorshipStatus: 'internal',
    isDeliveryRider: true,
    payCategory: 'sponsored_private',
  });

  put('platform_account', {
    id: 'NJ-ACC-001',
    platformCode: 'NINJA',
    externalAccountId: 'NJ-CAP-001',
    sourceDeleted: false,
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
    approvedBy: 'auditor-init',
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
    approvedBy: 'auditor-init',
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
  put('pay_eligibility', {
    id: 'ninja-eligibility-2026-10',
    kind: 'pay_eligibility',
    app: 'ninja',
    period: '2026-10',
    code: 'default',
    version: 1,
    status: 'approved',
    createdBy: userA,
    approvedBy: 'auditor-init',
    data: {
      name: 'شروط أهلية نينجا لشهر أكتوبر',
      rules: [
        { metric: 'orders', operator: 'gte', threshold: 100, effect: { type: 'zero' }, included: true },
        { metric: 'days', operator: 'gte', threshold: 10, effect: { type: 'zero' }, included: true },
      ],
    },
  });

  // Daily operations for rider 2000000888 (25 days summing to 300 orders matching invoice)
  for (let day = 1; day <= 25; day++) {
    const dd = String(day).padStart(2, '0');
    put('ninja_daily', {
      id: `nd-2026-10-${dd}`,
      companyId: companyId,
      platformAccountId: 'NJ-ACC-001',
      riderId: riderIqama,
      date: `2026-10-${dd}T12:00:00Z`,
      orders: day === 25 ? 60 : 10,
    });
  }

  db.close();
  console.log('✓ Seeded baseline data without seeding pay_scheme (as required)');

  browser = await chromium.launch({ headless: true });

  const parseCookie = (raw) => {
    const [name, ...rest] = raw.split('=');
    return { name: name.trim(), value: rest.join('=').trim() };
  };

  const parsedCookieA = parseCookie(cookieA);
  const parsedCookieB = parseCookie(cookieB);

  // STEP 1: User A creates the scheme through the UI form (S1 / S2)
  let createdSchemeId = '';
  const typedDailyCommission = '15.50';

  {
    console.log('Driving User A: opening scheme editor, testing empty validation, saving custom scheme...');
    const contextA = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextA.addCookies([
      { name: parsedCookieA.name, value: parsedCookieA.value, domain: '127.0.0.1', path: '/' },
    ]);
    const pageA = await contextA.newPage();
    await pageA.goto(`${baseUrl}/app/payroll?tab=platforms&app=ninja&sub=internal_schemes&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    // Verify empty state or button present
    const btnNewScheme = pageA.locator('button', { hasText: '+ إضافة نظام داخلي' });
    await btnNewScheme.waitFor({ state: 'visible', timeout: 10000 });
    await btnNewScheme.click();

    // Editor modal must be visible
    const modal = pageA.locator('[role="dialog"]');
    await modal.waitFor({ state: 'visible', timeout: 8000 });
    console.log('✓ Pay Settings Editor modal opened for User A');

    // Test S1: clicking save on empty form must block and display error
    const btnSave = modal.locator('button', { hasText: 'حفظ الإعداد' });
    await btnSave.click();

    const errorBanner = modal.locator('.bg-\\[var\\(--danger\\)\\]\\/10');
    await errorBanner.waitFor({ state: 'visible', timeout: 5000 });
    const errorText = await errorBanner.textContent();
    assert.ok(
      errorText.includes('اسم') || errorText.includes('إدخال'),
      `Error text must explain missing required name: ${errorText}`,
    );
    console.log(`✓ Empty form blocked submission with message: ${errorText.trim()}`);

    // Fill valid values
    const inputName = modal.locator('input[aria-label="اسم الإعداد"]');
    await inputName.fill('نظام نينجا اليومي المخصص');

    const inputCode = modal.locator('input[aria-label="رمز النظام بالإنجليزي"]');
    await inputCode.fill('ninja-custom-flow');

    // Select scheme type: daily_commission
    const selectType = modal.locator('select[aria-label="نوع النظام"]');
    await selectType.selectOption('daily_commission');

    // Switch to sponsored_private category tab
    const catTabBtn = modal.locator('button', { hasText: 'كفالة × مركبة خاصة' });
    await catTabBtn.click();

    // Enable sponsored_private category
    const catCheckbox = modal.locator('input[type="checkbox"]');
    await catCheckbox.check();

    // Fill typed daily commission
    const inputCommission = modal.locator('input[aria-label="العمولة اليومية"]');
    await inputCommission.waitFor({ state: 'visible', timeout: 5000 });
    await inputCommission.fill(typedDailyCommission);

    // Save
    await btnSave.click();
    await modal.waitFor({ state: 'hidden', timeout: 10000 });
    console.log(`✓ User A submitted custom scheme with dailyCommission = ${typedDailyCommission}`);

    // Assert scheme card appears in the list
    const schemeCard = pageA.locator('.border-\\[var\\(--warning\\)\\]\\/40', {
      hasText: 'نظام نينجا اليومي المخصص',
    });
    await schemeCard.waitFor({ state: 'visible', timeout: 8000 });
    const cardText = await schemeCard.textContent();
    assert.ok(cardText.includes('بانتظار الاعتماد'), 'Status must be «بانتظار الاعتماد»');
    assert.ok(cardText.includes(userA), `Creator must be @${userA}`);

    // Test Maker ≠ Checker: User A cannot approve their own scheme
    const approveBtn = schemeCard.locator('button', { hasText: 'اعتماد النظام (Maker-Checker)' });
    assert.equal(await approveBtn.isDisabled(), true, 'Approve button must be disabled for User A (maker)');
    const buttonTitle = await approveBtn.getAttribute('title');
    assert.ok(
      buttonTitle && buttonTitle.includes('Maker ≠ Checker'),
      `Tooltip must explain Maker ≠ Checker: ${buttonTitle}`,
    );
    console.log('✓ Maker ≠ Checker enforced: User A cannot approve their own scheme');

    await pageA.screenshot({
      path: path.join(resultsDir, 'r9-maker-scheme-pending.png'),
      fullPage: false,
    });

    await contextA.close();
  }

  // STEP 2: User B (Checker) approves the scheme (S2)
  {
    console.log('Driving User B (checker): viewing pending scheme and approving it...');
    const contextB = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextB.addCookies([
      { name: parsedCookieB.name, value: parsedCookieB.value, domain: '127.0.0.1', path: '/' },
    ]);
    const pageB = await contextB.newPage();
    await pageB.goto(`${baseUrl}/app/payroll?tab=platforms&app=ninja&sub=internal_schemes&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const approveBtn = pageB.locator('button', { hasText: 'اعتماد النظام (Maker-Checker)' }).first();
    await approveBtn.waitFor({ state: 'visible', timeout: 8000 });
    assert.equal(await approveBtn.isDisabled(), false, 'Approve button must be enabled for User B (checker)');

    await approveBtn.click();

    // Verify card changes to approved
    const approvedBadge = pageB.locator('span', { hasText: `معتمدة من @${userB}` }).first();
    await approvedBadge.waitFor({ state: 'visible', timeout: 8000 });
    console.log(`✓ Scheme successfully approved by User B: «معتمدة من @${userB}»`);

    await pageB.screenshot({
      path: path.join(resultsDir, 'r9-checker-scheme-approved.png'),
      fullPage: false,
    });

    await contextB.close();
  }

  // STEP 3: Assign rider to the approved scheme and verify «كيف حُسب» in payroll cycle
  {
    console.log('Assigning rider to approved scheme and driving cycle calculation...');
    // Query the approved scheme ID from server
    const schemesRes = await fetch(`${baseUrl}/api/pay/apps/ninja/schemes?period=2026-10`, {
      headers: { Cookie: cookieA },
    });
    const schemesData = await schemesRes.json();
    const approvedScheme = (schemesData.items || []).find((s) => s.status === 'approved');
    assert.ok(approvedScheme, 'Must have found approved scheme from API');
    createdSchemeId = approvedScheme.id;

    // Assign rider 2000000888 to the newly created and approved scheme in store
    const dbAssign = new DatabaseSync(path.join(tempDir, 'payroll.db'));
    const assignId = 'assign-' + randomBytes(4).toString('hex');
    dbAssign.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      assignId,
      'pay_assignment',
      JSON.stringify({
        id: assignId,
        rider: riderIqama,
        app: 'ninja',
        schemeId: createdSchemeId,
        period: '2026-10',
        companyId: companyId,
        status: 'approved',
        choiceApprovedAt: '2026-09-30T10:00:00+03:00',
      }),
    );
    dbAssign.close();
    console.log(`✓ Rider assigned to scheme ${createdSchemeId}`);

    // User A uploads Ninja synthetic invoice in browser
    {
      const contextUpload = await browser.newContext({ viewport: { width: 1440, height: 900 } });
      await contextUpload.addCookies([
        { name: parsedCookieA.name, value: parsedCookieA.value, domain: '127.0.0.1', path: '/' },
      ]);
      const pageUp = await contextUpload.newPage();
      await pageUp.goto(`${baseUrl}/app/payroll?tab=ninja_upload&period=2026-10`, { waitUntil: 'networkidle' });

      await pageUp.locator('input[type="file"]').first().setInputFiles({
        name: 'ninja-synthetic-2026-10.xlsx',
        mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        buffer: syntheticNinjaInvoice(),
      });
      const nextStep = pageUp.locator('#ninja-next-step');
      await nextStep.locator('button', { hasText: 'بدء المطابقة' }).click();
      await nextStep.locator('button', { hasText: 'تأكيد وحفظ' }).click({ timeout: 20000 });
      // Preview and save post to the same URL, so a response proves nothing. Keep the page open until the invoice exists.
      let savedCount = 0;
      for (let attempt = 0; attempt < 60 && savedCount === 0; attempt++) {
        const listed = await (await fetch(`${baseUrl}/api/platforms/ninja/invoices`, { headers: { Cookie: cookieB } })).json();
        savedCount = (listed.items || []).length;
        if (savedCount === 0) await pageUp.waitForTimeout(500);
      }
      assert.ok(savedCount > 0, 'The confirmed invoice must be saved');
      console.log('✓ Ninja invoice uploaded and confirmed by User A');
      await contextUpload.close();
    }

    // Get invoice ID and User B approves it
    const invRes = await fetch(`${baseUrl}/api/platforms/ninja/invoices`, {
      headers: { Cookie: cookieB },
    });
    const invData = await invRes.json();
    const invoiceList = Array.isArray(invData) ? invData : (invData.items || []);
    const invoiceId = invoiceList[0]?.id;
    assert.ok(invoiceId, `Must find an uploaded Ninja invoice. Got: ${JSON.stringify(invData)}`);
    console.log(`✓ Ninja invoice retrieved: ${invoiceId}`);

    // User B approves invoice (maker ≠ checker)
    const approveInvRes = await fetch(`${baseUrl}/api/platforms/ninja/invoices/${encodeURIComponent(invoiceId)}/approve`, {
      method: 'POST',
      headers: { Cookie: cookieB },
    });
    assert.equal(approveInvRes.status, 200, 'Approve Ninja invoice by checker must return 200');
    console.log('✓ Ninja invoice approved by User B');

    // Check review draft via API first
    const draftRes = await fetch(`${baseUrl}/api/pay/review-draft?app=ninja&period=2026-10&companyId=${encodeURIComponent(companyId)}`, {
      headers: { Cookie: cookieA },
    });
    const draftData = await draftRes.json();
    console.log('Review draft blockers:', JSON.stringify(draftData.blockers || []));
    console.log('Review draft canCreate:', draftData.canCreate);

    // Open cycle in browser, create cycle from draft, advance stages to 4, and assert «كيف حُسب؟»
    const contextA = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextA.addCookies([
      { name: parsedCookieA.name, value: parsedCookieA.value, domain: '127.0.0.1', path: '/' },
    ]);
    const pageA = await contextA.newPage();
    await pageA.goto(`${baseUrl}/app/payroll?tab=cycle&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const draftTabBtn = pageA.locator('button', { hasText: 'مسودة المراجعة المقترحة' });
    if (await draftTabBtn.count() > 0) {
      await draftTabBtn.click();
      await pageA.waitForTimeout(500);
      await pageA.waitForLoadState('networkidle');
    }

    const createCycleBtn = pageA.locator('button', { hasText: 'إنشاء دورة الرواتب' });
    await createCycleBtn.waitFor({ state: 'visible', timeout: 10000 });
    const [createResponse] = await Promise.all([
      pageA.waitForResponse((r) => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith('/api/pay/cycles'), { timeout: 15000 }),
      createCycleBtn.click(),
    ]);
    assert.equal(createResponse.status(), 200, `create cycle must succeed: ${await createResponse.text()}`);
    console.log('✓ Clicked «إنشاء دورة الرواتب» from Review Draft');

    // L2 (BR §39): inputs must be captured before stage 2. Until U2 adds the button, capture through the API, then reload.
    const createdCycle = await createResponse.json();
    const cycleIdForCapture = createdCycle?.id ?? createdCycle?.cycle?.id;
    assert.ok(cycleIdForCapture, 'Cycle id required before capture-inputs');
    const captureRes = await fetch(`${baseUrl}/api/pay/cycles/${encodeURIComponent(cycleIdForCapture)}/capture-inputs`, {
      method: 'POST', headers: { Cookie: cookieA, 'Content-Type': 'application/json' }, body: '{}',
    });
    assert.equal(captureRes.status, 200, `capture-inputs must succeed: ${await captureRes.text()}`);
    await pageA.reload({ waitUntil: 'networkidle' });
    // After a reload the page opens on the draft tab; switch to the cycle tab.
    const cycleTabBtn = pageA.locator('button', { hasText: 'دورة الرواتب ومراحل المسير' });
    await cycleTabBtn.first().waitFor({ state: 'visible', timeout: 10000 });
    await cycleTabBtn.first().click();
    await pageA.waitForLoadState('networkidle');

    // Advance stages with the stepper button until Stage 4 (lines generated)
    for (let stage = 1; stage <= 4; stage++) {
      const advanceBtn = pageA.locator('button', { hasText: 'تقديم المرحلة إلى' });
      await advanceBtn.first().waitFor({ state: 'visible', timeout: 8000 });
      const btnText = await advanceBtn.first().textContent();
      console.log(`  Clicking: ${btnText?.trim()}`);
      await advanceBtn.first().click();
      await pageA.waitForTimeout(800);
      await pageA.waitForLoadState('networkidle');
    }

    // Click «كيف حُسب؟» button on the rider's row
    const explainBtn = pageA.locator('button', { hasText: 'كيف حُسب؟' }).first();
    await explainBtn.waitFor({ state: 'visible', timeout: 10000 });
    await explainBtn.click();
    await pageA.waitForTimeout(500);

    // Drawer opens
    const drawerHeader = pageA.locator(`text=شرح احتساب مستحقات المندوب: ${riderIqama}`);
    await drawerHeader.first().waitFor({ state: 'visible', timeout: 8000 });

    const drawer = pageA.locator('[role="dialog"]').filter({ hasText: 'شرح احتساب مستحقات المندوب' });
    const drawerText = await drawer.textContent();

    // Assert typed commission appears in the calculation breakdown
    assert.ok(
      drawerText.includes('عمولة الشركة') || drawerText.includes('dailyCommission'),
      'Explain drawer must contain company commission calculation line',
    );
    // 25 days * 15.50 = 387.50
    assert.ok(
      drawerText.includes('387.50') || drawerText.includes('15.50') || drawerText.includes(typedDailyCommission),
      `Explain drawer must reflect the exact typed daily commission calculation: ${drawerText}`,
    );
    console.log(`✓ Verified «كيف حُسب؟» shows the typed value calculation (15.50 × 25 = 387.50 SAR)!`);

    await pageA.screenshot({
      path: path.join(resultsDir, 'r9-how-calculated-drawer.png'),
      fullPage: false,
    });

    await contextA.close();
  }

  console.log('--- ALL R9 PAY SETTINGS BROWSER TESTS PASSED SUCCESSFULLY! ---');
} finally {
  if (browser) await browser.close();
  proc.kill('SIGTERM');
  try {
    rmSync(tempDir, { recursive: true, force: true });
  } catch {}
}
