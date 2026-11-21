import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sidebar = readFileSync(path.join(root, 'src/components/layout/Sidebar.tsx'), 'utf8');
const appRoot = path.join(root, 'src/app');

function appRoutes() {
  const routes = new Set();
  function walk(dir, base = '') {
    for (const name of readdirSync(dir)) {
      const full = path.join(dir, name);
      if (statSync(full).isDirectory()) walk(full, `${base}/${name}`);
      else if (name === 'page.tsx' || name === 'page.ts' || name === 'page.jsx') {
        const route = (base || '/') + '/';
        routes.add(route.replace(/\/+/g, '/'));
      }
    }
  }
  walk(appRoot);
  return routes;
}

function hrefsFromSidebar(text) {
  const hrefs = [];
  const re = /href:\s*'([^']+)'/g;
  let m;
  while ((m = re.exec(text))) hrefs.push(m[1]);
  return hrefs;
}

function tabIdsInPage(pageText) {
  const ids = new Set();
  // MAIN_TABS / TABS arrays and SectionTabs items
  for (const m of pageText.matchAll(/['"]([a-z0-9_]+)['"]/g)) ids.add(m[1]);
  for (const m of pageText.matchAll(/id:\s*'([^']+)'/g)) ids.add(m[1]);
  for (const m of pageText.matchAll(/rawTab === '([^']+)'/g)) ids.add(m[1]);
  for (const m of pageText.matchAll(/MAIN_TABS = new Set\(\[([^\]]+)\]\)/g)) {
    for (const part of m[1].split(',')) {
      const id = part.trim().replace(/['"]/g, '');
      if (id) ids.add(id);
    }
  }
  return ids;
}

test('every Sidebar href resolves to an app route', () => {
  const routes = appRoutes();
  const hrefs = hrefsFromSidebar(sidebar);
  assert.ok(hrefs.length > 10, 'sidebar should list hrefs');
  for (const href of hrefs) {
    const pathOnly = href.split('?')[0];
    const normalized = pathOnly.endsWith('/') ? pathOnly : pathOnly + '/';
    const ok =
      routes.has(normalized) ||
      routes.has(pathOnly) ||
      existsSync(path.join(appRoot, pathOnly.replace(/^\//, ''), 'page.tsx'));
    assert.ok(ok, `missing route for sidebar href ${href} (path ${normalized})`);
  }
});

test('operations sidebar tabs and views exist on the page', () => {
  const page = readFileSync(path.join(appRoot, 'operations/page.tsx'), 'utf8');
  const ids = tabIdsInPage(page);
  for (const href of hrefsFromSidebar(sidebar).filter((h) => h.startsWith('/operations'))) {
    const u = new URL(href, 'https://example.local');
    const tab = u.searchParams.get('tab');
    const view = u.searchParams.get('view');
    if (tab) assert.ok(ids.has(tab), `operations page missing tab id ${tab} from ${href}`);
    if (view) assert.ok(ids.has(view), `operations page missing view id ${view} from ${href}`);
  }
  assert.ok(ids.has('cities') && ids.has('housing') && ids.has('vehicles') && ids.has('fuel'));
});

test('removed O7 tabs are absent from Sidebar operations group', () => {
  assert.doesNotMatch(sidebar, /ops-custody|ops-guarantees|ops-cases|ops-accounts/);
  assert.match(sidebar, /ops-fuel/);
});
