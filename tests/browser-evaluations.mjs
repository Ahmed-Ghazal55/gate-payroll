import { spawn } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { createServer } from 'node:net';
import { DatabaseSync } from 'node:sqlite';

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

  // Import synthetic staff cards (ops/app + HR + excluded). Departments use Arabic names for section membership.
  await adminPage.click('[data-testid="eval-import-staff-btn"]');
  const syntheticStaffPayload = {
    staff: [
      {
        name: 'Synthetic Field Supervisor Alpha',
        cities: ['Synthetic City One'],
        departments: ['العمليات'],
        roles: ['Field Operations Supervisor'],
        apps: ['Ninja'],
        active: true,
      },
      {
        name: 'Synthetic Team Lead Beta',
        cities: ['Synthetic City One'],
        departments: ['العمليات'],
        roles: ['Original Role Text Replaced By Placeholder'],
        apps: ['Keeta'],
        active: true,
        pendingRoleApproval: true,
      },
      {
        name: 'Synthetic HR Officer One',
        cities: ['Synthetic City One'],
        departments: ['الموارد البشرية'],
        roles: ['HR'],
        apps: [],
        active: true,
      },
      {
        name: 'Synthetic HR Officer Two',
        cities: ['Synthetic City One'],
        departments: ['الموارد البشرية'],
        roles: ['HR Assistant'],
        apps: [],
        active: true,
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
  assert.equal(round.staff.length, 4, 'excluded staff not in round cards');
  assert.ok(Array.isArray(round.sections) && round.sections.length >= 5, 'sectioned round exposes sections');
  assert.equal(round.sharingToken, undefined, 'sectioned RoundView omits round sharingToken');
  const ninjaSection = round.sections.find((s) => s.key === 'app:Ninja');
  const hrSection = round.sections.find((s) => s.key === 'dept:hr');
  assert.ok(ninjaSection?.sharingToken, 'app:Ninja section token');
  assert.ok(hrSection?.sharingToken, 'dept:hr section token');
  assert.ok((hrSection.staff || []).length >= 2, 'HR section has at least two staff cards');
  assert.equal(hrSection.title, 'الموارد البشرية');
  assert.equal(ninjaSection.title, 'تطبيق نينجا');
  assert.equal(await adminPage.locator('[data-testid="eval-copy-link-btn"]').count(), 0);
  assert.equal(await adminPage.locator('[data-testid="eval-preview-mobile-btn"]').count(), 0);

  // Legacy combined two-page path needs a round without sections (live legacy rounds).
  function makeLegacyRound(roundId) {
    const db = new DatabaseSync(path.join(data, 'payroll.db'));
    const row = db.prepare('SELECT body FROM records WHERE id=?').get(roundId);
    const body = JSON.parse(row.body);
    delete body.sections;
    db.prepare('UPDATE records SET body=? WHERE id=?').run(JSON.stringify(body), roundId);
    db.close();
    return body.sharingToken;
  }
  const legacyDraft = await (
    await adminPage.request.post(`${base}/api/forms/evaluations/rounds`, {
      data: { title: 'جولة تقييم تراثية مجمّعة' },
    })
  ).json();
  const legacyToken = makeLegacyRound(legacyDraft.id);
  const legacyRound = await (
    await adminPage.request.post(
      `${base}/api/forms/evaluations/rounds/${encodeURIComponent(legacyDraft.id)}/open`,
      { data: {} }
    )
  ).json();
  assert.ok(legacyRound.sharingToken);
  assert.equal(legacyRound.sharingToken, legacyToken);
  assert.ok(!legacyRound.sections);

  // 3. Rider 1: Public Web Form (/rider-form.html?evaluation=<token>) in 3 languages on 360px phone
  const phoneCtx = await browser.newContext({
    viewport: { width: 360, height: 780 },
    hasTouch: true,
    isMobile: true,
  });
  const publicPage = await phoneCtx.newPage();
  attachWatchers(publicPage);

  await publicPage.goto(
    `${base}/rider-form.html?evaluation=${encodeURIComponent(legacyRound.sharingToken)}`,
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

  // Verify staff cards shown, placeholder «بانتظار موافقة غزال» visible, excluded person absent
  assert.equal(await publicPage.locator('[data-staff-card]').count(), 4);
  const cardsText = await publicPage.locator('#eval-staff-cards').textContent();
  assert.match(cardsText, /بانتظار موافقة غزال/);
  assert.doesNotMatch(cardsText, /Synthetic Excluded Executive/);

  const staff1Id = legacyRound.staff.find((s) => s.name.includes('Alpha')).id;
  const staff2Id = legacyRound.staff.find((s) => s.name.includes('Beta')).id;
  const staffHr1Id = legacyRound.staff.find((s) => s.name.includes('HR Officer One')).id;
  const staffHr2Id = legacyRound.staff.find((s) => s.name.includes('HR Officer Two')).id;

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

  // Skip card 2 and both HR cards with reason not_dealt
  await publicPage.selectOption(`[data-staff-reason="${staff2Id}"]`, 'not_dealt');
  await publicPage.selectOption(`[data-staff-reason="${staffHr1Id}"]`, 'not_dealt');
  await publicPage.selectOption(`[data-staff-reason="${staffHr2Id}"]`, 'not_dealt');

  // Next -> Page 2 (platforms)
  await publicPage.click('#eval-next-btn');
  await publicPage.locator('#eval-page-2').waitFor();

  // Rate Ninja 5 stars + optional factor stars, mark other platforms not_worked
  await publicPage.click('[data-app-star="Ninja"][data-stars="5"]');
  await publicPage.click('[data-app-factor-star="Ninja"][data-factor-id="orderVolume"][data-stars="4"]');
  await publicPage.click('[data-app-factor-star="Ninja"][data-factor-id="fairPay"][data-stars="3"]');
  for (const app of legacyRound.apps.filter((a) => a !== 'Ninja')) {
    await publicPage.selectOption(`[data-app-reason="${app}"]`, 'not_worked');
  }

  await publicPage.click('#eval-submit-btn');
  await publicPage.locator('.success-symbol').waitFor();
  assert.match(await publicPage.locator('.edit-credentials').textContent(), /^[0-9a-f]{64}$/);

  async function fillSectionIdentity(page, { iqama, name, nationality, app }) {
    await page.locator('[data-testid="eval-section-flow"]').waitFor();
    await page.getByRole('button', { name: /متابعة|Continue|جاری/ }).first().click();
    await page.locator('[data-testid="eval-section-city"]').waitFor();
    await page.locator('[data-testid="eval-section-city"]').selectOption('Synthetic City One');
    await page.locator('[data-testid="eval-section-iqama"]').fill(iqama);
    await page.locator('[data-testid="eval-section-name"]').fill(name);
    await page.locator('[data-testid="eval-section-nationality"]').fill(nationality);
    const appLabel = app === 'Ninja' ? 'نينجا' : app === 'Keeta' ? 'كيتا' : app;
    await page.locator(`label:has-text("${appLabel}") input[type="checkbox"]`).check();
    await page.getByRole('button', { name: /^التالي$|^Next$|^اگلا$/ }).click();
  }

  async function rateStaffCard(page, score = 6) {
    await page.locator('[data-testid="eval-section-staff-step"]').waitFor();
    const dims = ['professional', 'treatment', 'support', 'responseSpeed', 'problemSolving'];
    for (const dim of dims) {
      await page.locator(`[data-dim="${dim}"][data-score="${score}"]`).first().click();
    }
  }

  // 3b. HR section link → identity/city → tap ratings + one reason card + one complaint → send
  const hrPage = await phoneCtx.newPage();
  attachWatchers(hrPage);
  await hrPage.goto(
    `${base}/app/evaluation/?evaluation=${encodeURIComponent(hrSection.sharingToken)}`,
    { waitUntil: 'networkidle' }
  );
  await fillSectionIdentity(hrPage, {
    iqama: '2000000813',
    name: 'Synthetic HR Section Rider',
    nationality: 'Synthetic Nationality HR',
    app: 'Ninja',
  });
  // Card 1: tap ratings + confidential complaint
  await rateStaffCard(hrPage, 7);
  await hrPage.getByRole('button', { name: /شكوى|complaint|شکایت/i }).click();
  await hrPage.locator('textarea').last().fill('Synthetic HR confidential complaint details');
  await hrPage.getByRole('button', { name: /التالي|Next|اگلا/ }).click();
  // Card 2: skip with reason
  await hrPage.locator('[data-testid="eval-section-staff-step"]').waitFor();
  await hrPage.locator('[data-testid="eval-section-staff-reason"]').selectOption('not_dealt');
  await hrPage.getByRole('button', { name: /التالي|Next|اگلا/ }).click();
  await hrPage.locator('[data-testid="eval-section-review"]').waitFor();
  await hrPage.getByRole('button', { name: /تأكيد|Confirm|تصدیق|إرسال/i }).click();
  await hrPage.locator('[data-testid="eval-section-done"]').waitFor();

  // 3c. App section link → staff → platform → finance → send
  const sectionPage = await phoneCtx.newPage();
  attachWatchers(sectionPage);
  await sectionPage.goto(
    `${base}/app/evaluation/?evaluation=${encodeURIComponent(ninjaSection.sharingToken)}`,
    { waitUntil: 'networkidle' }
  );
  await fillSectionIdentity(sectionPage, {
    iqama: '2000000814',
    name: 'Synthetic App Section Rider',
    nationality: 'Synthetic Nationality AppSec',
    app: 'Ninja',
  });
  // Staff cards for Ninja section (ops with Ninja + any allApps) — rate then next until app card
  for (;;) {
    if (await sectionPage.locator('[data-testid="eval-section-app-step"]').count()) break;
    if (await sectionPage.locator('[data-testid="eval-section-staff-step"]').count()) {
      await rateStaffCard(sectionPage, 6);
      await sectionPage.getByRole('button', { name: /التالي|Next|اگلا/ }).click();
      continue;
    }
    break;
  }
  await sectionPage.locator('[data-testid="eval-section-app-step"]').waitFor();
  await sectionPage.locator('[data-app-star="Ninja"][data-stars="3"]').click();
  await sectionPage.getByRole('button', { name: /التالي|Next|اگلا/ }).click();
  await sectionPage.locator('[data-testid="eval-finance-card"]').waitFor();
  await sectionPage.locator('[data-finance-star="4"]').click();
  await sectionPage.getByRole('button', { name: /التالي|Next|اگلا/ }).click();
  await sectionPage.locator('[data-testid="eval-section-review"]').waitFor();
  await sectionPage.getByRole('button', { name: /تأكيد|Confirm|تصدیق|إرسال/i }).click();
  await sectionPage.locator('[data-testid="eval-section-done"]').waitFor();

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
    `${base}/app/evaluation/?token=${encodeURIComponent(legacyRound.sharingToken)}&mode=app`,
    { waitUntil: 'networkidle' }
  );
  await riderAppPage.locator('[data-testid="evaluation-public-form"]').waitFor();
  assert.equal(await riderAppPage.locator('input[name="iqama"]').inputValue(), '2000000812');

  await riderAppPage.selectOption('select[name="city"]', 'Synthetic City One');
  await riderAppPage.fill('input[name="nationality"]', 'Synthetic Nationality Two');
  await riderAppPage.check('input[name="workApps"][value="Keeta"]');

  // Rate card 2 (all 5 dimensions = 7), skip card 1 and both HR cards
  await riderAppPage.selectOption(`[data-staff-reason="${staff1Id}"]`, 'unknown_person');
  await riderAppPage.selectOption(`[data-staff-reason="${staffHr1Id}"]`, 'not_dealt');
  await riderAppPage.selectOption(`[data-staff-reason="${staffHr2Id}"]`, 'not_dealt');
  for (const dim of ['professional', 'treatment', 'support', 'responseSpeed', 'problemSolving']) {
    await riderAppPage.click(
      `[data-staff-rate="${staff2Id}"][data-dim="${dim}"][data-score="7"]`
    );
  }
  await riderAppPage.click('#eval-next-btn');
  await riderAppPage.locator('[data-testid="evaluation-page-2"]').waitFor();

  await riderAppPage.click('[data-app-star="Keeta"][data-stars="4"]');
  await riderAppPage.click('[data-app-factor-star="Keeta"][data-factor-id="appProblems"][data-stars="2"]');
  for (const app of legacyRound.apps.filter((a) => a !== 'Keeta')) {
    await riderAppPage.selectOption(`[data-app-reason="${app}"]`, 'not_worked');
  }

  await riderAppPage.click('#eval-submit-btn');
  await riderAppPage.locator('[data-testid="evaluation-receipt"]').waitFor();

  // 5. Admin Results Page: section filter sees HR + app replies; charts, Excel export
  await adminPage.reload({ waitUntil: 'networkidle' });
  await adminPage.locator('[data-testid="eval-chart-by-city"]').waitFor();

  async function pickSectionFilter(label) {
    await adminPage.locator('#eval-filter-section').click();
    await adminPage.getByRole('option', { name: label, exact: true }).click();
    await adminPage.waitForTimeout(500);
  }
  await pickSectionFilter('الموارد البشرية');
  const hrRows = await (
    await adminPage.request.get(
      `${base}/api/forms/evaluations/rounds/${encodeURIComponent(round.id)}/responses?page=1&pageSize=20&section=${encodeURIComponent('dept:hr')}`
    )
  ).json();
  assert.ok(
    (hrRows.items || []).some((r) => r.iqama === '2000000813'),
    'admin section filter dept:hr includes HR section reply'
  );
  await pickSectionFilter('تطبيق نينجا');
  const ninjaRows = await (
    await adminPage.request.get(
      `${base}/api/forms/evaluations/rounds/${encodeURIComponent(round.id)}/responses?page=1&pageSize=20&section=${encodeURIComponent('app:Ninja')}`
    )
  ).json();
  assert.ok(
    (ninjaRows.items || []).some((r) => r.iqama === '2000000814'),
    'admin section filter app:Ninja includes app section reply'
  );
  await pickSectionFilter('كل أقسام التقييم');

  // Sectioned round only: HR + app section replies (legacy replies live on the helper round).
  const byCityText = await adminPage.locator('[data-testid="eval-chart-by-city"]').textContent();
  assert.match(byCityText, /Synthetic City One/);
  assert.match(byCityText, /2 رد/);

  const byNatText = await adminPage
    .locator('[data-testid="eval-chart-by-nationality"]')
    .textContent();
  assert.match(byNatText, /Synthetic Nationality HR/);
  assert.match(byNatText, /Synthetic Nationality AppSec/);

  const byAppText = await adminPage.locator('[data-testid="eval-chart-by-app"]').textContent();
  assert.match(byAppText, /نينجا/);

  const factorsText = await adminPage
    .locator('[data-testid="eval-chart-factor-averages"]')
    .textContent();
  assert.match(factorsText, /عدد الطلبات/);

  const supText = await adminPage
    .locator('[data-testid="eval-chart-by-supervisor"]')
    .textContent();
  assert.match(supText, /Synthetic HR Officer/);
  assert.match(supText, /1 شكوى سرية/);

  // Test dimension drill-down button
  await adminPage.click('[data-dimension="nationality"]');
  assert.match(
    await adminPage.locator('[data-testid="eval-chart-by-nationality"]').textContent(),
    /Synthetic Nationality HR/
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
    'PASS browser-evaluations: legacy rider-form + HR/app /app/evaluation section flows, admin section filter, rider app session, charts, Excel export.'
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
