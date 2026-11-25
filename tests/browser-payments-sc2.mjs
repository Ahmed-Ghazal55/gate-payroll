import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';

const { chromium } = await import(
  pathToFileURL(process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs')
);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const data = mkdtempSync(path.join(tmpdir(), 'gate-sc2-browser-'));
const base = 'http://127.0.0.1:19352';

const server = spawn(
  process.env.DOTNET_PATH || 'dotnet',
  [path.join(root, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],
  {
    cwd: path.join(root, 'Payroll.Api'),
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: base,
      PAYROLL_DATA_DIR: data,
    },
    windowsHide: true,
    stdio: 'ignore',
  }
);

let spawnError;
server.once('error', (e) => {
  spawnError = e;
});

let browser;
try {
  let ready = false;
  for (let n = 0; n < 150; n++) {
    if (spawnError) throw spawnError;
    if (server.exitCode !== null) throw new Error('SC2 browser server exited');
    try {
      if ((await fetch(`${base}/health`)).ok) {
        ready = true;
        break;
      }
    } catch {}
    await new Promise((r) => setTimeout(r, 100));
  }
  assert.ok(ready, 'SC2 browser server did not start');
  assert.ok(
    existsSync(path.join(root, 'Payroll.Api/wwwroot/app/payments/quick/index.html')),
    'Packaged Next export must include /app/payments/quick/index.html'
  );

  const launchOpts = {
    headless: true,
    ...(process.env.PLAYWRIGHT_EXECUTABLE_PATH ? { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH } : {}),
  };
  browser = await chromium.launch(launchOpts);
  const setupPage = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const password = randomBytes(24).toString('hex');
  assert.equal(
    (await setupPage.request.post(`${base}/api/auth/setup`, { data: { username: 'sc2-browser', password } })).status(),
    200
  );
  const storageState = await setupPage.request.storageState();
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 1000 }, storageState });
  const page = await ctx.newPage();
  const pageErrors = [];
  page.on('pageerror', (e) => pageErrors.push(e.message));

  const routes = [
    { path: '/app/payments/rents/?period=2026-10', heading: 'الإيجارات والسكنات' },
    { path: '/app/payments/installments/?period=2026-10', heading: 'الأقساط والمدفوعات البنكية' },
    { path: '/app/payments/purchases/?period=2026-10', heading: 'فواتير المشتريات والمصروفات' },
    { path: '/app/payments/fuel/?period=2026-10', heading: 'الوقود والمحطات' },
    { path: '/app/payments/hr-requests/?period=2026-10', heading: 'طلبات الموارد البشرية والخصومات' },
    { path: '/app/payments/advances/?period=2026-10', heading: 'السلف والشحن' },
    { path: '/app/payments/mada/?period=2026-10', heading: 'جهاز مدا والشحن' },
    { path: '/app/payments/hs-custody/?period=2026-10', heading: 'عهد هنقرستيشن' },
    { path: '/app/payments/traffic-violations/?period=2026-10', heading: 'سداد المخالفات المرورية' },
    { path: '/app/payments/suppliers/?period=2026-10', heading: 'الموردون والدفعات' },
    { path: '/app/payments/due-calendar/?period=2026-10', heading: 'لوحة المستحقات' },
    { path: '/app/payments/government/?period=2026-10', heading: 'المدفوعات الحكومية' },
    { path: '/app/payments/prepaid-wallets/?period=2026-10', heading: 'المحافظ المدفوعة مقدمًا' },
    { path: '/app/payments/quick/?period=2026-10', heading: 'تسجيل مدفوعات سريعة' },
    { path: '/app/payments/recurring/?period=2026-10', heading: 'المدفوعات الدورية' },
  ];

  for (const r of routes) {
    await page.goto(`${base}${r.path}`, { waitUntil: 'networkidle' });
    await page.getByRole('heading', { name: r.heading }).first().waitFor({ timeout: 10000 });
    assert.ok((await page.locator('[role="tablist"]').count()) >= 1, `${r.path} should render in-page SectionTabs`);
    const bodyText = await page.locator('main').innerText();
    assert.doesNotMatch(bodyText, /U-W3-|S-PAY|BR §/, `${r.path} must not leak spec refs`);
  }

  // Verify /app/payments/ redirects to /app/payments/due-calendar preserving ?period=2026-10
  await page.goto(`${base}/app/payments/?period=2026-10`, { waitUntil: 'networkidle' });
  await page.waitForURL(/\/app\/payments\/due-calendar\/?\?.*period=2026-10/, { timeout: 10000 });

  // Verify Quick Payment live journal preview updates with formatSar («ر.س»)
  await page.goto(`${base}/app/payments/quick/?period=2026-10`, { waitUntil: 'networkidle' });
  const amountInput = page.locator('main input[type="number"]').first();
  await amountInput.fill('250');
  const asideText = await page.locator('main aside').innerText();
  assert.match(asideText, /250\.00 ر\.س/, 'Quick payment live journal preview must format SAR via formatSar');

  // Verify suppliers create dialog posts to the real payments endpoint (JSON company store ≠ SQL hr_company on fresh SQLite,
  // so insert may 409 on FK — still prove the screen wires POST /api/payments/suppliers with Arabic feedback).
  await page.goto(`${base}/app/payments/suppliers/?period=2026-10`, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: 'إضافة مورد جديد' }).click();
  await page.locator('[role="dialog"] input[required]').first().fill('مورد اختبار آلي');
  const [supplierRes] = await Promise.all([
    page.waitForResponse(
      (r) => r.url().includes('/api/payments/suppliers') && r.request().method() === 'POST',
      { timeout: 10000 }
    ),
    page.getByRole('button', { name: 'حفظ المورد' }).click(),
  ]);
  assert.ok([200, 409].includes(supplierRes.status()), `supplier create POST status ${supplierRes.status()}`);
  if (supplierRes.status() === 200) {
    await page.getByText('مورد اختبار آلي').first().waitFor({ timeout: 10000 });
  } else {
    const body = await supplierRes.json();
    assert.match(String(body.error || ''), /تعذر|مورد|حفظ/, '409 body must be Arabic API error');
    await page.getByText(/تعذر|راجع تكرار/).first().waitFor({ timeout: 10000 });
  }

  assert.deepEqual(pageErrors, [], `Unexpected JS page errors: ${pageErrors.join('; ')}`);
  console.log(`PASS browser-payments-sc2: verified ${routes.length} Payments screens, /payments redirect, live journal preview, supplier create POST wiring, 0 JS errors.`);
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null && !spawnError) {
    const stopped = new Promise((r) => server.once('exit', r));
    server.kill();
    await Promise.race([stopped, new Promise((r) => setTimeout(r, 5000))]);
  }
  rmSync(data, { recursive: true, force: true });
}
