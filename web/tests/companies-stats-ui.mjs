// Browser smoke test for /companies/?tab=stats and drill-down to /workforce/
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtempSync, rmSync, cpSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { createServer } from 'node:net';
import { randomBytes } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import assert from 'node:assert/strict';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const webDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(webDir, '..');

const pwPath = process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs';
if (!existsSync(pwPath)) {
  console.log(`SKIP: Playwright module not found at ${pwPath}`);
  process.exit(0);
}
const { chromium } = await import(pathToFileURL(pwPath));

async function getFreePort() {
  const srv = createServer();
  await new Promise((r) => srv.listen(0, '127.0.0.1', r));
  const { port } = srv.address();
  await new Promise((r) => srv.close(r));
  return port;
}

async function main() {
  const outDir = path.join(webDir, 'out');
  assert.ok(existsSync(outDir), 'Run npm --prefix web run build before browser smoke test');

  const appTarget = path.join(repoRoot, 'Payroll.Api/wwwroot/app');
  mkdirSync(appTarget, { recursive: true });
  cpSync(outDir, appTarget, { recursive: true });

  const dataDir = mkdtempSync(path.join(tmpdir(), 'gate-co-stats-ui-'));
  const port = await getFreePort();
  const baseUrl = `http://127.0.0.1:${port}`;
  const server = spawn(
    'dotnet',
    [path.join(repoRoot, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],
    {
      cwd: path.join(repoRoot, 'Payroll.Api'),
      env: {
        ...process.env,
        ConnectionStrings__DefaultConnection: '',
        PAYROLL_DATA_DIR: dataDir,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: baseUrl,
        Operations__Source: 'store',
        Jobs__Enabled: 'false',
      },
      stdio: 'ignore',
    }
  );

  let browser;
  try {
    let ready = false;
    for (let i = 0; i < 150; i++) {
      if (server.exitCode !== null) throw new Error('Synthetic API exited early');
      try {
        if ((await fetch(`${baseUrl}/health`)).ok) {
          ready = true;
          break;
        }
      } catch {}
      await new Promise((r) => setTimeout(r, 100));
    }
    assert.ok(ready, 'API did not become healthy');

    const password = randomBytes(20).toString('hex');
    const setupRes = await fetch(`${baseUrl}/api/auth/setup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: 'syn-co-ui-admin', password }),
    });
    assert.equal(setupRes.status, 200);
    const cookieHeader = (setupRes.headers.get('set-cookie') || '').split(';')[0];

    const companiesRes = await fetch(`${baseUrl}/api/companies`, {
      headers: { Cookie: cookieHeader },
    });
    const companies = await companiesRes.json();
    assert.ok(companies.length >= 2);
    const first = companies[0];

    // Seed synthetic rider + platform account for first company
    const db = new DatabaseSync(path.join(dataDir, 'payroll.db'));
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      '2000000901',
      'rider',
      JSON.stringify({
        id: '2000000901',
        iqama: '2000000901',
        name: 'مندوب تجريبي',
        status: 'Active',
        engagementType: 'SponsoredInternal',
        isEmployee: false,
        sponsorId: first.id,
        financeSponsorId: first.id,
        sponsorUnified: first.unifiedNumber || '',
        platform: 'JAHEZ',
      })
    );
    db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(
      'syn-ui-pa-1',
      'platform_account',
      JSON.stringify({
        id: 'syn-ui-pa-1',
        companyId: first.id,
        platformCode: 'JAHEZ',
        status: 'Assigned',
        assignments: [{ riderId: '2000000901', EffectiveTo: '' }],
      })
    );
    db.close();

    browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: { width: 1280, height: 860 },
      locale: 'ar-SA',
    });
    const [cName, cVal] = cookieHeader.split('=');
    await context.addCookies([
      { name: cName.trim(), value: cVal.trim(), domain: '127.0.0.1', path: '/', httpOnly: true },
    ]);

    const page = await context.newPage();
    const pageErrors = [];
    page.on('pageerror', (err) => pageErrors.push(err.message));

    await page.goto(`${baseUrl}/app/companies/?tab=stats`, { waitUntil: 'networkidle' });
    await page.locator('#tabpanel-stats').waitFor({ state: 'visible', timeout: 15000 });
    await page.locator('[data-testid="company-balances-placeholder"]').waitFor({ state: 'visible' });
    const balancesText = await page.locator('[data-testid="company-balances-placeholder"]').innerText();
    assert.ok(balancesText.includes('الأرصدة بعد ربط المحاسبة'));

    // Drill-down link from company card to /workforce/?company=...
    const card = page.locator(`[data-testid="company-stats-card-${first.code || first.id}"]`);
    await card.waitFor({ state: 'visible' });
    const drillLink = card.locator('a', { hasText: 'عرض في القوى العاملة' });
    await drillLink.click();
    await page.waitForURL(/\/workforce\/.*company=/, { timeout: 15000 });
    assert.equal(pageErrors.length, 0, `Page errors: ${pageErrors.join('; ')}`);

    console.log('Companies stats UI smoke test: PASSED');
  } finally {
    if (browser) await browser.close();
    server.kill('SIGTERM');
    await once(server, 'exit');
    rmSync(dataDir, { recursive: true, force: true });
  }
}

await main();
