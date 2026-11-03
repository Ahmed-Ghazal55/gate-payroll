import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';

const { chromium } = await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs'));
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const data = mkdtempSync(path.join(tmpdir(), 'gate-theme-shots-'));
const base = 'http://127.0.0.1:19399';
const outDir = path.join(root, 'test-results/1.0-theme');
mkdirSync(outDir, { recursive: true });

const proc = spawn(process.env.DOTNET_PATH || 'dotnet', [path.join(root, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')], {
  cwd: path.join(root, 'Payroll.Api'),
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: base,
    PAYROLL_DATA_DIR: data
  },
  stdio: ['ignore', 'pipe', 'pipe']
});

let logs = '', spawnError;
proc.on('error', e => spawnError = e);
proc.stdout.on('data', x => logs += x);
proc.stderr.on('data', x => logs += x);

try {
  let ready = false;
  for (let n = 0; n < 150; n++) {
    if (spawnError) throw spawnError;
    try {
      if ((await fetch(base + '/health')).ok) { ready = true; break; }
    } catch {}
    await new Promise(r => setTimeout(r, 100));
  }
  assert.ok(ready, logs);

  const browser = await chromium.launch({ headless: true });
  const adminContext = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const setupPage = await adminContext.newPage();
  assert.equal((await setupPage.request.post(base + '/api/auth/setup', {data: {username: 'theme-tester', password: randomBytes(24).toString('hex')}})).status(), 200);
  await setupPage.goto(base + '/legacy/');
  await setupPage.getByRole('heading', { name: 'كل مستحق في مكانه' }).waitFor();

  // Create sample rider & debts data for rich dashboard & debts screens
  await adminContext.request.post(base + '/api/riders', {
    data: { iqama: '2000000001', name: 'سالم أحمد الشهري', nationality: 'Saudi Arabia', status: 'Active', city: 'الرياض' }
  });

  const campaign = await (await adminContext.request.post(base + '/api/campaigns', {
    data: { title: 'تحديث بيانات الآيبان للمناديب', days: 7, kind: 'iban' }
  })).json();

  const storageState = await adminContext.storageState();
  await adminContext.close();

  async function captureView(viewName, url, isPublic, setupAction) {
    for (const theme of ['sand', 'dim']) {
      for (const [width, height, isMobile] of [[1440, 900, false], [360, 740, true]]) {
        const ctx = await browser.newContext({
          viewport: { width, height },
          isMobile,
          hasTouch: isMobile,
          storageState: isPublic ? undefined : storageState
        });
        const page = await ctx.newPage();
        await page.addInitScript((th) => {
          localStorage.setItem('gate-theme', th);
        }, theme);

        await page.goto(url);
        if (setupAction) {
          await setupAction(page, theme);
        }
        await page.waitForTimeout(500);
        const filename = `${viewName}-${width}-${theme}.png`;
        await page.screenshot({ path: path.join(outDir, filename), fullPage: false });
        console.log(`Saved screenshot: ${filename}`);
        await ctx.close();
      }
    }
  }

  // 1. Dashboard
  await captureView('dashboard', base + '/legacy/#/dashboard', false, async (page, theme) => {
    await page.locator('.stats, .panel').first().waitFor();
  });

  // 2. Debts
  await captureView('debts', base + '/legacy/#/debts', false, async (page, theme) => {
    await page.locator('[data-view="debts"]').first().click().catch(() => {});
    await page.waitForTimeout(400);
  });

  // 3. IBAN Form
  await captureView('iban', base + campaign.path, true, async (page, theme) => {
    await page.locator('#rider-choice').waitFor();
  });

  await browser.close();
  console.log('ALL 12 screenshots captured successfully!');
} finally {
  proc.kill();
  try { rmSync(data, { recursive: true, force: true }); } catch {}
}
