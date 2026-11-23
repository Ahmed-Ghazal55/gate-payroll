import { spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';

const { chromium } = await import(
  pathToFileURL(
    process.env.PLAYWRIGHT_MODULE || '/tmp/gate-browser/node_modules/playwright/index.mjs'
  )
);

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const data = mkdtempSync(path.join(tmpdir(), 'gate-browser-ev1-'));
const outDir = path.join(root, 'test-results');
mkdirSync(outDir, { recursive: true });

// Ensure packaged Next.js static export and rider-form assets are present in wwwroot for the API process.
const webOut = path.join(root, 'web/out');
for (const targetRoot of [
  path.join(root, 'Payroll.Api/wwwroot'),
  path.join(root, 'Payroll.Api/bin/Debug/net10.0/wwwroot'),
]) {
  mkdirSync(targetRoot, { recursive: true });
  if (existsSync(webOut)) {
    const appDest = path.join(targetRoot, 'app');
    rmSync(appDest, { recursive: true, force: true });
    cpSync(webOut, appDest, { recursive: true });
  }
  for (const asset of ['rider-form.js', 'rider-form.html', 'app.css', 'favicon.svg']) {
    const src = path.join(root, 'Payroll.Api/wwwroot', asset);
    const dst = path.join(targetRoot, asset);
    if (existsSync(src) && src !== dst) cpSync(src, dst);
  }
}

async function freePort() {
  const probe = createServer();
  await new Promise((ok, no) => {
    probe.once('error', no);
    probe.listen(0, '127.0.0.1', ok);
  });
  const port = probe.address().port;
  await new Promise((ok) => probe.close(ok));
  return port;
}

const port = await freePort();
const base = `http://127.0.0.1:${port}`;
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
      ConnectionStrings__DefaultConnection: '',
      Vouchers__Secret: 'synthetic-test-only-voucher-key-never-use-on-a-server',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  }
);

let logs = '';
let spawnError;
server.on('error', (e) => {
  spawnError = e;
});
server.stdout.on('data', (b) => {
  logs = (logs + b).slice(-6000);
});
server.stderr.on('data', (b) => {
  logs = (logs + b).slice(-6000);
});

let browser;
try {
  let ready = false;
  for (let n = 0; n < 150; n++) {
    if (spawnError) throw spawnError;
    if (server.exitCode !== null) throw new Error(logs);
    try {
      if ((await fetch(`${base}/health`)).ok) {
        ready = true;
        break;
      }
    } catch {}
    await new Promise((r) => setTimeout(r, 100));
  }
  assert.ok(ready, `API server did not start: ${logs}`);

  const fallbackExe = [
    process.env.PLAYWRIGHT_EXECUTABLE_PATH,
    process.env.PLAYWRIGHT_CHROMIUM_PATH,
    '/home/ghazal/.cache/ms-playwright/chromium-1148/chrome-linux/chrome',
    '/usr/bin/google-chrome',
  ].find((p) => p && existsSync(p));
  browser = await chromium.launch({
    headless: true,
    ...(fallbackExe ? { executablePath: fallbackExe } : {}),
  });
  const errors = [];
  const cspErrors = [];

  const attachWatchers = (p) => {
    p.on('pageerror', (e) => errors.push(e.message));
    p.on('console', (m) => {
      if (/Content Security Policy/i.test(m.text())) cspErrors.push(m.text());
    });
  };

  // 1. Setup admin & synthetic rider for mobile session test
  const adminSetupPage = await browser.newPage();
  const password = randomBytes(20).toString('hex');
  assert.equal(
    (
      await adminSetupPage.request.post(`${base}/api/auth/setup`, {
        data: { username: 'syn-ev1-admin', password },
      })
    ).status(),
    200
  );
  assert.equal(
    (
      await adminSetupPage.request.post(`${base}/api/riders`, {
        data: {
          iqama: '2000000812',
          name: 'Synthetic App Evaluation Rider',
          phone: '0501238812',
          financeSponsorId: 'BW',
          status: 'Active',
        },
      })
    ).status(),
    200
  );
  const mobileGrant = await (
    await adminSetupPage.request.post(`${base}/api/inquiry-access`, {
      data: { iqama: '2000000812', scope: 'mobile', identityReviewed: true },
    })
  ).json();
  const mobileLogin = await (
    await adminSetupPage.request.post(`${base}/api/rider-mobile/login`, {
      data: {
        iqamaNo: '2000000812',
        phoneLast4: '8812',
        code: mobileGrant.code,
        language: 'ar',
        devicePlatform: 'Synthetic-Test',
      },
    })
  ).json();
  assert.ok(mobileLogin.accessToken, 'rider mobile bearer token issued');

  const cookies = await adminSetupPage.request.storageState();
  await adminSetupPage.close();

  // 2. Admin UI: import synthetic staff cards, create evaluation round, and open it
  const adminCtx = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    storageState: cookies,
  });
  const adminPage = await adminCtx.newPage();
  attachWatchers(adminPage);

  await adminPage.goto(`${base}/app/rider-app/?tab=campaigns&subtab=evaluations`, {
    waitUntil: 'networkidle',
  });
  await adminPage.locator('[data-testid="evaluations-admin-page"]').waitFor();

  // Import synthetic staff cards (1 normal, 1 pendingRoleApproval, 1 excluded)
  await adminPage.click('[data-testid="eval-import-staff-btn"]');
  const syntheticStaffPayload = {
    staff: [
      {
        name: 'Synthetic Field Supervisor Alpha',
        cities: ['Synthetic City One'],
        departments: ['Operations'],
        roles: ['Field Operations Supervisor'],
        apps: ['Ninja'],
        active: true,
      },
      {
        name: 'Synthetic Team Lead Beta',
        cities: ['Synthetic City One'],
        departments: ['Platform Follow-up'],
        roles: ['Original Role Text Replaced By Placeholder'],
        apps: ['Keeta'],
        active: true,
        pendingRoleApproval: true,
      },
      {
        name: 'Synthetic Excluded Executive',
        cities: ['Synthetic City One'],
        departments: ['Management'],
        roles: ['Executive'],
        apps: ['Ninja'],
        active: true,
        excluded: true,
      },
    ],
  };
  await adminPage.fill(
    '[data-testid="eval-import-json-textarea"]',
    JSON.stringify(syntheticStaffPayload)
  );
  await adminPage.click('[data-testid="eval-confirm-import-staff-btn"]');
  await adminPage.locator('[data-testid="eval-staff-directory-section"]').waitFor();

  // Create evaluation round via UI
  await adminPage.click('[data-testid="eval-create-round-btn"]');
  await adminPage.fill('[data-testid="eval-round-title-input"]', 'جولة تقييم تجريبية شاملة');
  await adminPage.click('[data-testid="eval-confirm-create-round-btn"]');

  // Open the round using the allowedActions button
  await adminPage.locator('[data-testid="eval-round-open-btn"]').waitFor();
  await adminPage.click('[data-testid="eval-round-open-btn"]');
  await adminPage.locator('[data-testid="eval-round-pause-btn"]').waitFor();
  await adminPage.locator('[data-testid="eval-round-sections"]').waitFor();
  await adminPage.locator('[data-testid="eval-round-delete-btn"]').waitFor();
  await adminPage.locator('[data-testid="eval-section-copy"]').first().waitFor();
  await adminPage.locator('[data-testid="eval-finance-scorecard"]').waitFor();

  const rounds = await (
    await adminPage.request.get(`${base}/api/forms/evaluations/rounds`)
  ).json();
  assert.equal(rounds.length, 1);
  const round = rounds[0];
  assert.equal(round.status, 'open');
  assert.equal(round.staff.length, 2, 'excluded staff not in round cards');
  assert.ok(Array.isArray(round.sections) && round.sections.length >= 5, 'sectioned round exposes sections');
  const ninjaSection = round.sections.find((s) => s.key === 'app:Ninja');
  assert.ok(ninjaSection?.sharingToken, 'app:Ninja section token');

  // 3. Rider 1: Public Web Form (/rider-form.html?evaluation=<token>) in 3 languages on 360px phone
  // Round-level token keeps the legacy combined two-page path.
  const phoneCtx = await browser.newContext({
    viewport: { width: 360, height: 780 },
    hasTouch: true,
    isMobile: true,
  });
  const publicPage = await phoneCtx.newPage();
  attachWatchers(publicPage);

  await publicPage.goto(
    `${base}/rider-form.html?evaluation=${encodeURIComponent(round.sharingToken)}`,
    { waitUntil: 'networkidle' }
  );
  await publicPage.locator('#rider-evaluation').waitFor();
  assert.match(
    await publicPage.locator('#eval-confidential-notice').textContent(),
    /سرية/
  );

  // Switch languages AR -> EN -> UR -> AR and check layout fits 360px
  await publicPage.click('[data-lang="en"]');
  assert.match(
    await publicPage.locator('#eval-confidential-notice').textContent(),
    /confidential/i
  );
  await publicPage.click('[data-lang="ur"]');
  assert.match(
    await publicPage.locator('#eval-confidential-notice').textContent(),
    /خفیہ/
  );
  await publicPage.click('[data-lang="ar"]');

  const mobileOverflow = await publicPage.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  assert.ok(mobileOverflow <= 1, `Public evaluation form overflowed 360px by ${mobileOverflow}px`);

  // Fill identity + nationality + city + workApps
  await publicPage.fill('#eval-iqama', '٢٠٠٠٠٠٠٨١١');
  await publicPage.fill('#eval-name', 'Synthetic Public Rider');
  await publicPage.selectOption('#eval-city', 'Synthetic City One');
  await publicPage.fill('#eval-nationality', 'Synthetic Nationality One');
  await publicPage.check('[name="workApps"][value="Ninja"]');

  // Verify 2 cards shown, placeholder «بانتظار موافقة غزال» visible, excluded person absent
  assert.equal(await publicPage.locator('[data-staff-card]').count(), 2);
  const cardsText = await publicPage.locator('#eval-staff-cards').textContent();
  assert.match(cardsText, /بانتظار موافقة غزال/);
  assert.doesNotMatch(cardsText, /Synthetic Excluded Executive/);

  const staff1Id = round.staff[0].id;
  const staff2Id = round.staff[1].id;

  // Rate card 1 (all 5 dimensions = 9) + confidential complaint
  for (const dim of ['professional', 'treatment', 'support', 'responseSpeed', 'problemSolving']) {
    await publicPage.click(
      `[data-staff-rate="${staff1Id}"][data-dim="${dim}"][data-score="9"]`
    );
  }
  await publicPage.click(`[data-staff-complaint-btn="${staff1Id}"]`);
  await publicPage.fill(
    `[data-staff-complaint-text="${staff1Id}"]`,
    'Synthetic confidential complaint from public form'
  );

  // Skip card 2 with reason not_dealt
  await publicPage.selectOption(`[data-staff-reason="${staff2Id}"]`, 'not_dealt');

  // Next -> Page 2 (platforms)
  await publicPage.click('#eval-next-btn');
  await publicPage.locator('#eval-page-2').waitFor();

  // Rate Ninja 5 stars + optional factor stars, mark other platforms not_worked
  await publicPage.click('[data-app-star="Ninja"][data-stars="5"]');
  await publicPage.click('[data-app-factor-star="Ninja"][data-factor-id="orderVolume"][data-stars="4"]');
  await publicPage.click('[data-app-factor-star="Ninja"][data-factor-id="fairPay"][data-stars="3"]');
  for (const app of round.apps.filter((a) => a !== 'Ninja')) {
    await publicPage.selectOption(`[data-app-reason="${app}"]`, 'not_worked');
  }

  await publicPage.click('#eval-submit-btn');
  await publicPage.locator('.success-symbol').waitFor();
  assert.match(await publicPage.locator('.edit-credentials').textContent(), /^[0-9a-f]{64}$/);

  // 3b. Section token form shows finance card on page 2 (evaluation-section-v1)
  const sectionPage = await phoneCtx.newPage();
  attachWatchers(sectionPage);
  await sectionPage.goto(
    `${base}/rider-form.html?evaluation=${encodeURIComponent(ninjaSection.sharingToken)}`,
    { waitUntil: 'networkidle' }
  );
  await sectionPage.locator('#rider-evaluation').waitFor();
  await sectionPage.fill('#eval-iqama', '٢٠٠٠٠٠٠٨١٣');
  await sectionPage.fill('#eval-name', 'Synthetic Section Rider');
  await sectionPage.selectOption('#eval-city', 'Synthetic City One');
  await sectionPage.fill('#eval-nationality', 'Synthetic Nationality Section');
  await sectionPage.check('[name="workApps"][value="Ninja"]');
  for (const staff of ninjaSection.staff || []) {
    for (const dim of ['professional', 'treatment', 'support', 'responseSpeed', 'problemSolving']) {
      const rate = sectionPage.locator(
        `[data-staff-rate="${staff.id}"][data-dim="${dim}"][data-score="6"]`
      );
      if ((await rate.count()) > 0) await rate.click();
    }
  }
  await sectionPage.click('#eval-next-btn');
  await sectionPage.locator('#eval-page-2').waitFor();
  await sectionPage.locator('#eval-finance-card').waitFor();
  await sectionPage.click('[data-app-star="Ninja"][data-stars="3"]');
  await sectionPage.click('[data-finance-star="4"]');
  await sectionPage.click('#eval-submit-btn');
  await sectionPage.locator('.success-symbol').waitFor();

  // 4. Rider 2: Signed-in Rider App session on /app/evaluation/?token=<token>&mode=app
  const riderAppPage = await phoneCtx.newPage();
  attachWatchers(riderAppPage);
  await riderAppPage.addInitScript((sess) => {
    sessionStorage.setItem('gate_rider_mob', JSON.stringify(sess));
  }, {
    accessToken: mobileLogin.accessToken,
    iqama: '2000000812',
    name: 'Synthetic App Evaluation Rider',
  });

  await riderAppPage.goto(
    `${base}/app/evaluation/?token=${encodeURIComponent(round.sharingToken)}&mode=app`,
    { waitUntil: 'networkidle' }
  );
  await riderAppPage.locator('[data-testid="evaluation-public-form"]').waitFor();
  assert.equal(await riderAppPage.locator('input[name="iqama"]').inputValue(), '2000000812');

  await riderAppPage.selectOption('select[name="city"]', 'Synthetic City One');
  await riderAppPage.fill('input[name="nationality"]', 'Synthetic Nationality Two');
  await riderAppPage.check('input[name="workApps"][value="Keeta"]');

  // Rate card 2 (all 5 dimensions = 7), skip card 1
  await riderAppPage.selectOption(`[data-staff-reason="${staff1Id}"]`, 'unknown_person');
  for (const dim of ['professional', 'treatment', 'support', 'responseSpeed', 'problemSolving']) {
    await riderAppPage.click(
      `[data-staff-rate="${staff2Id}"][data-dim="${dim}"][data-score="7"]`
    );
  }
  await riderAppPage.click('#eval-next-btn');
  await riderAppPage.locator('[data-testid="evaluation-page-2"]').waitFor();

  await riderAppPage.click('[data-app-star="Keeta"][data-stars="4"]');
  await riderAppPage.click('[data-app-factor-star="Keeta"][data-factor-id="appProblems"][data-stars="2"]');
  for (const app of round.apps.filter((a) => a !== 'Keeta')) {
    await riderAppPage.selectOption(`[data-app-reason="${app}"]`, 'not_worked');
  }

  await riderAppPage.click('#eval-submit-btn');
  await riderAppPage.locator('[data-testid="evaluation-receipt"]').waitFor();

  // 5. Admin Results Page: verify statistics, 5 charts, filters, table, drawer, and export
  await adminPage.reload({ waitUntil: 'networkidle' });
  await adminPage.locator('[data-testid="eval-chart-by-city"]').waitFor();

  const byCityText = await adminPage.locator('[data-testid="eval-chart-by-city"]').textContent();
  assert.match(byCityText, /Synthetic City One/);
  assert.match(byCityText, /3 رد/);

  const byNatText = await adminPage
    .locator('[data-testid="eval-chart-by-nationality"]')
    .textContent();
  assert.match(byNatText, /Synthetic Nationality One/);
  assert.match(byNatText, /Synthetic Nationality Two/);

  const byAppText = await adminPage.locator('[data-testid="eval-chart-by-app"]').textContent();
  assert.match(byAppText, /نينجا/);
  assert.match(byAppText, /كيتا/);

  const factorsText = await adminPage
    .locator('[data-testid="eval-chart-factor-averages"]')
    .textContent();
  assert.match(factorsText, /عدد الطلبات/);
  assert.match(factorsText, /عدالة احتساب الأجر/);
  assert.match(factorsText, /مشكلات التطبيق/);

  const supText = await adminPage
    .locator('[data-testid="eval-chart-by-supervisor"]')
    .textContent();
  assert.match(supText, /Synthetic Field Supervisor Alpha/);
  assert.match(supText, /Synthetic Team Lead Beta/);
  assert.match(supText, /بانتظار موافقة غزال/);
  assert.match(supText, /1 شكوى سرية/);

  // Test dimension drill-down button
  await adminPage.click('[data-dimension="nationality"]');
  assert.match(
    await adminPage.locator('[data-testid="eval-chart-by-nationality"]').textContent(),
    /Synthetic Nationality One/
  );
  await adminPage.click('[data-dimension="all"]');

  // Verify Excel export download
  const [download] = await Promise.all([
    adminPage.waitForEvent('download'),
    adminPage.click('[data-testid="eval-export-excel-btn"]'),
  ]);
  assert.match(download.suggestedFilename(), /\.xlsx$/);

  await adminPage.screenshot({
    path: path.join(outDir, 'evaluations-admin-results.png'),
    fullPage: true,
  });

  assert.deepEqual(errors, [], `Unexpected page errors: ${errors.join('; ')}`);
  assert.deepEqual(cspErrors, [], `Unexpected CSP errors: ${cspErrors.join('; ')}`);
  console.log(
    'PASS browser-evaluations: admin sections panel + delete, legacy + section public forms (finance card), rider app session, charts, Excel export.'
  );
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null && !spawnError) {
    const stopped = new Promise((r) => server.once('exit', r));
    server.kill();
    await Promise.race([stopped, new Promise((r) => setTimeout(r, 5000))]);
  }
  rmSync(data, { recursive: true, force: true });
}
