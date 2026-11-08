// R9 Keeta First Payroll Flow Verification (W1)
// Drives the real browser flow end-to-end:
// 1. User A uploads a real synthetic Keeta xlsx (with captain penalty + company adjustment row),
//    handles companyRows when requested by the API, confirms, and classifies deductions.
// 2. Asserts User A (maker) cannot approve their own invoice (maker ≠ checker).
// 3. User B (checker) approves the Keeta invoice in the browser.

import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { crc32 } from 'node:zlib';
import { randomBytes } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';

function syntheticKeetaInvoice() {
  const header = ['المعرف', 'الطلبات المُسلمة', 'الخصم', 'الإجمالي', 'إجمالي المبلغ المستحق'];
  const captainRow = ['998877', '210', '-150', '4200', '4830'];
  const companyAdjRow = ['', '0', '0', '-80', '-92'];

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
    'xl/workbook.xml': `<workbook xmlns="${ns}" xmlns:r="${rel}"><sheets><sheet name="KeetaSynthetic" r:id="r1"/></sheets></workbook>`,
    'xl/_rels/workbook.xml.rels':
      '<Relationships><Relationship Id="r1" Target="worksheets/sheet1.xml"/></Relationships>',
    'xl/worksheets/sheet1.xml': `<worksheet xmlns="${ns}"><sheetData>${row(header, 1)}${row(captainRow, 2)}${row(companyAdjRow, 3)}</sheetData></worksheet>`,
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

const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
const { chromium } = await import(pathToFileURL(pwPath));

console.log('--- Starting Gate Payroll R9 Keeta Flow Test Suite (W1) ---');

assert.ok(existsSync(outDir), 'web/out directory must exist. Run npm run build first.');
mkdirSync(wwwrootAppDir, { recursive: true });
cpSync(outDir, wwwrootAppDir, { recursive: true });
console.log('✓ Copied web/out to Payroll.Api/wwwroot/app');

const tempDir = mkdtempSync(path.join(tmpdir(), 'gate-r9-keeta-'));
const basePort = 19684;
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

  const db = new DatabaseSync(path.join(tempDir, 'payroll.db'));
  function put(kind, record) {
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      record.id,
      kind,
      JSON.stringify(record),
    );
  }

  const riderIqama = '2000000801';
  put('rider', {
    id: riderIqama,
    iqama: riderIqama,
    name: 'فهد القحطاني',
    sponsorId: companyId,
    status: 'active',
    sponsorshipStatus: 'internal',
    isDeliveryRider: true,
    payCategory: 'sponsored_private',
  });

  put('platform_account', {
    id: 'KT-ACC-001',
    platformCode: 'KEETA',
    externalAccountId: '998877',
    sourceDeleted: false,
    companyId: companyId,
    name: 'حساب كيتا الرياض 1',
    assignments: [
      {
        riderId: riderIqama,
        RawEffectiveFrom: '2026-10-01T00:00:00+03:00',
        RawEffectiveTo: '2026-10-31T23:59:59+03:00',
      },
    ],
  });

  db.close();
  console.log('✓ Seeded live-shaped KEETA platform_account into SQLite store');

  browser = await chromium.launch({ headless: true });

  const parseCookie = (raw) => {
    const [name, ...rest] = raw.split('=');
    return { name: name.trim(), value: rest.join('=').trim() };
  };

  const parsedCookieA = parseCookie(cookieA);
  const parsedCookieB = parseCookie(cookieB);

  // Step 1: User A uploads Keeta xlsx, handles companyRows, confirms, classifies
  {
    console.log('Driving User A: upload Keeta xlsx, preview, confirm, classify...');
    const contextA = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextA.addCookies([
      { name: parsedCookieA.name, value: parsedCookieA.value, domain: '127.0.0.1', path: '/' },
    ]);
    const pageA = await contextA.newPage();
    await pageA.goto(`${baseUrl}/app/payroll?tab=platforms&app=keeta&sub=settlements&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    await pageA.locator('input[type="file"]').first().setInputFiles({
      name: 'keeta-synthetic-2026-10.xlsx',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      buffer: syntheticKeetaInvoice(),
    });

    const nextStep = pageA.locator('#ninja-next-step');
    await nextStep.locator('button', { hasText: 'بدء المطابقة' }).click();

    // First preview asks for companyRows on row 3 -> UI offers and pre-populates companyRows=3
    const companyRowsInput = pageA.locator('input[aria-label="أسطر تعديل الشركة"]');
    await companyRowsInput.waitFor({ state: 'visible', timeout: 10000 });
    assert.equal(await companyRowsInput.inputValue(), '3', 'companyRows input must auto-populate row 3 from API error');
    console.log('✓ Conditional companyRows field offered and populated when API needed it');

    await nextStep.locator('button', { hasText: 'إعادة المطابقة' }).click();
    await nextStep.locator('button', { hasText: 'تأكيد وحفظ' }).click({ timeout: 20000 });

    const panel = pageA.locator('#ninja-classify-panel');
    await panel.waitFor({ state: 'visible', timeout: 15000 });
    assert.ok(
      (await pageA.locator('#ninja-approval-blockers').textContent()).includes('بلا تصنيف'),
      'Blockers must explain the unclassified Keeta deduction',
    );

    await panel.locator('[data-category="penalty"] button').click();
    await panel.locator('[data-category="penalty"]', { hasText: '0 بلا تصنيف' }).waitFor({ timeout: 8000 });

    const adjustmentRow = panel.locator('[data-category="supplier_adjustment"]');
    await adjustmentRow.locator('input[role="combobox"]').click();
    await pageA.locator('[role="option"]').first().waitFor({ timeout: 8000 });
    await adjustmentRow.locator('input[role="combobox"]').fill('5');
    await pageA.locator('[role="option"]').first().click();
    await adjustmentRow.locator('button').click();
    await adjustmentRow.filter({ hasText: '0 بلا تصنيف' }).waitFor({ timeout: 8000 });

    assert.equal(
      await pageA.locator('#btn-approve-ninja-invoice').count(),
      0,
      'User A (maker) must see 0 approve buttons on their own Keeta invoice',
    );
    assert.ok(
      (await pageA.locator('#ninja-approval-blockers').textContent()).includes('مستخدم آخر'),
      'Blockers must tell the maker a second user approves',
    );
    await pageA.screenshot({
      path: path.join(resultsDir, 'keeta-maker-classified.png'),
      fullPage: false,
    });
    console.log('✓ Maker uploaded, confirmed and classified Keeta invoice; 0 approve buttons shown');
    await contextA.close();
  }

  // Step 2: User B (Checker) approves the Keeta invoice in the browser
  {
    console.log('Driving User B (checker): approving pending Keeta invoice...');
    const contextB = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    await contextB.addCookies([
      { name: parsedCookieB.name, value: parsedCookieB.value, domain: '127.0.0.1', path: '/' },
    ]);
    const pageB = await contextB.newPage();
    await pageB.goto(`${baseUrl}/app/payroll?tab=platforms&app=keeta&sub=settlements&period=2026-10`, {
      waitUntil: 'networkidle',
    });

    const approveBtn = pageB.locator('#btn-approve-ninja-invoice');
    await approveBtn.waitFor({ state: 'visible', timeout: 8000 });
    await pageB.locator('#ninja-next-step button', { hasText: 'اعتماد الفاتورة' }).click();
    await pageB.waitForTimeout(800);
    await pageB.waitForLoadState('networkidle');

    await pageB.locator('text=معتمدة').first().waitFor({ state: 'visible', timeout: 5000 });
    await pageB.locator('#ninja-next-step a', { hasText: 'فتح مسودة المراجعة' }).waitFor({ timeout: 8000 });
    await pageB.screenshot({
      path: path.join(resultsDir, 'keeta-checker-approved.png'),
      fullPage: false,
    });
    console.log('✓ Keeta invoice approved by User B in browser');
    await contextB.close();
  }

  console.log('--- ALL R9 KEETA FLOW CHECKS PASSED ---');
} finally {
  if (browser) await browser.close().catch(() => {});
  proc.kill('SIGTERM');
  rmSync(tempDir, { recursive: true, force: true });
}
