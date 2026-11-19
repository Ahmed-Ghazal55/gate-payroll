// Section 9 (R3): sidebar links point to real pages and real tab ids; old forms links redirect.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  RIDER_APP_TABS,
  resolveRiderAppTab,
  formsRedirectTarget,
} from '../../src/features/rider-app/tabs.ts';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(webRoot, 'src/components/layout/Sidebar.tsx'), 'utf8');
const hrefs = [...sidebar.matchAll(/(?:href|defaultHref):\s*'([^']+)'/g)].map((m) => m[1]);

describe('sidebar links (dead-link check like 0.25.2)', () => {
  it('every sidebar path is a page under web/src/app', () => {
    for (const href of hrefs) {
      const p = href.split('?')[0].replace(/^\/|\/$/g, '');
      const page = path.join(webRoot, 'src/app', p, 'page.tsx');
      assert.ok(existsSync(page), `missing page for ${href}`);
    }
  });

  it('the merged section has exactly the 8 entries and every ?tab= id is handled', () => {
    const appTabs = hrefs.filter((h) => h.startsWith('/rider-app/?tab=')).map((h) => new URLSearchParams(h.split('?')[1]).get('tab'));
    const entries = [...new Set(appTabs)];
    assert.deepEqual(entries.sort(), [...RIDER_APP_TABS].sort());
    for (const t of entries) assert.equal(resolveRiderAppTab(t).legacy, false, t);
  });

  it('no sidebar entry still points at the old forms page or old rider-app ids', () => {
    assert.ok(!hrefs.some((h) => h.startsWith('/forms/')), 'old /forms/ entry');
    assert.ok(!hrefs.some((h) => /statement-preview/.test(h)), 'old statement-preview id');
  });
});

describe('old links redirect', () => {
  it('maps /forms/ tabs to the merged section', () => {
    assert.equal(formsRedirectTarget(''), '/rider-app/?tab=campaigns');
    assert.equal(formsRedirectTarget('tab=responses'), '/rider-app/?tab=responses');
    assert.equal(formsRedirectTarget('tab=builder'), '/rider-app/?tab=builder');
    assert.equal(formsRedirectTarget('tab=analytics'), '/rider-app/?tab=campaigns&subtab=analytics');
    assert.equal(formsRedirectTarget('campaignId=c1&tab=campaigns'), '/rider-app/?campaignId=c1&tab=campaigns');
  });

  it('maps old rider-app ids and unknown ids', () => {
    assert.deepEqual(resolveRiderAppTab('statement-preview'), { tab: 'statements', legacy: true });
    assert.deepEqual(resolveRiderAppTab('bogus'), { tab: 'campaigns', legacy: true });
    assert.deepEqual(resolveRiderAppTab(null), { tab: 'campaigns', legacy: false });
  });
});
