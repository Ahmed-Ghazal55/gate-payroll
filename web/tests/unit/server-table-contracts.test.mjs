import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  serializeServerFiltersParam,
  serializeServerSortParam,
  defaultStatusTone,
} from '../../src/lib/columnFilter.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const apiSrc = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

function walkTsx(dir, acc = []) {
  for (const name of readdirSync(dir)) {
    const full = path.join(dir, name);
    const st = statSync(full);
    if (st.isDirectory()) walkTsx(full, acc);
    else if (name.endsWith('.tsx')) acc.push(full);
  }
  return acc;
}

describe('Server-paged table generic filter/sort/distinct contract (FL2)', () => {
  it('debts table sends filters, sort, and /debts/distinct contract params on list and export', () => {
    const src = readFileSync(path.join(root, 'src/app/debts/page.tsx'), 'utf8');
    assert.match(src, /distinctEndpoint="\/debts\/distinct"/);
    assert.match(src, /distinctParams=\{\{[\s\S]{0,220}q:\s*debouncedSearch[\s\S]{0,220}filters:\s*debtsServerQuery\.filtersParam[\s\S]{0,40}\}\}/);
    assert.match(src, /onServerQueryChange=/);
    assert.match(src, /filters:\s*sq\.filtersParam/);
    assert.match(src, /sort:\s*sq\.sortParam/);
    assert.match(src, /filters:\s*debtsServerQuery\.filtersParam/);
    assert.match(src, /sort:\s*debtsServerQuery\.sortParam/);

    // Verify api.getDebts encodes filters & sort
    assert.match(apiSrc, /getDebts:[\s\S]{0,900}if\s*\(params\?\.filters\)\s*q\.set\('filters',\s*params\.filters\)/);
    assert.match(apiSrc, /getDebts:[\s\S]{0,900}if\s*\(params\?\.sort\)\s*q\.set\('sort',\s*params\.sort\)/);

    const fParam = serializeServerFiltersParam(
      { remainingAmount: { op: 'gte', value: '500' }, status: { colorTone: 'warning' } },
      [
        { key: 'remainingAmount', filterColumn: 'remainingBalance', type: 'number' },
        {
          key: 'status',
          filterColumn: 'status',
          type: 'list',
          toneOf: defaultStatusTone,
          options: [
            { value: 'active', label: 'نشط', tone: 'success' },
            { value: 'pending_approval', label: 'بانتظار الاعتماد', tone: 'warning' },
          ],
        },
      ]
    );
    const sParam = serializeServerSortParam('remainingAmount', 'desc', null, [
      { key: 'remainingAmount', filterColumn: 'remainingBalance' },
    ]);
    assert.deepEqual(JSON.parse(fParam), [
      { col: 'remainingBalance', op: 'gte', value: '500' },
      { col: 'status', op: 'color', value: 'warning', values: ['pending_approval'] },
    ]);
    assert.deepEqual(JSON.parse(sParam), [{ col: 'remainingBalance', dir: 'desc' }]);
  });

  it('workforce table sends filters, sort, and /workforce/distinct contract params on list and export', () => {
    const src = readFileSync(path.join(root, 'src/app/workforce/page.tsx'), 'utf8');
    assert.match(src, /distinctEndpoint="\/workforce\/distinct"/);
    assert.match(src, /onServerQueryChange=/);
    assert.match(src, /filters:\s*directoryServerQuery\.filtersParam/);
    assert.match(src, /sort:\s*directoryServerQuery\.sortParam/);

    // Verify api.getWorkforce & exportWorkforce encode filters & sort
    assert.match(apiSrc, /getWorkforce:[\s\S]{0,900}filters/);
    assert.match(apiSrc, /exportWorkforce:[\s\S]{0,900}filters/);

    const fParam = serializeServerFiltersParam(
      { city: { values: ['الرياض', 'جدة'] }, name: { op: 'beginsWith', value: 'محمد' } },
      [
        { key: 'city', filterColumn: 'city', type: 'list' },
        { key: 'name', filterColumn: 'name', type: 'text' },
      ]
    );
    const sParam = serializeServerSortParam('name', 'asc', null, [{ key: 'name' }]);
    assert.deepEqual(JSON.parse(fParam), [
      { col: 'city', op: 'in', values: ['الرياض', 'جدة'] },
      { col: 'name', op: 'beginsWith', value: 'محمد' },
    ]);
    assert.deepEqual(JSON.parse(sParam), [{ col: 'name', dir: 'asc' }]);
  });

  it('operations server-paged tables send filters, sort, and /operations/distinct contract params on all views and exports', () => {
    const src = readFileSync(path.join(root, 'src/app/operations/page.tsx'), 'utf8');
    assert.match(src, /distinctEndpoint="\/operations\/distinct"/);
    assert.match(src, /distinctParams=\{\{\s*tab,\s*view,\s*from,\s*to,\s*month,\s*mode,\s*q:\s*debouncedQ \|\| undefined,\s*filters:\s*serverQuery\.filtersParam\s*\}\}/);
    assert.match(src, /loading=\{loading\}/);
    assert.doesNotMatch(src, /\{loading \?\s*\(\s*<Skeleton/);
    assert.match(src, /exportOperationsFuelLog\(/);
    assert.match(src, /onServerQueryChange=/);
    assert.match(src, /exportOperationsCities\(\{\s*filters:\s*serverQuery\.filtersParam,\s*sort:\s*serverQuery\.sortParam\s*\}\)/);
    assert.match(src, /exportOperationsHousing\(\{[\s\S]{0,160}filters:\s*serverQuery\.filtersParam,\s*sort:\s*serverQuery\.sortParam/);
    assert.match(src, /exportOperationsPeriod\(\{[\s\S]{0,220}q:\s*debouncedQ[\s\S]{0,120}filters:\s*serverQuery\.filtersParam,\s*sort:\s*serverQuery\.sortParam/);
    assert.match(src, /exportOperationsFuel\(\{[\s\S]{0,180}q:\s*debouncedQ[\s\S]{0,120}filters:\s*serverQuery\.filtersParam,\s*sort:\s*serverQuery\.sortParam/);

    // Verify no rendered JSON.stringify in PaySettingsValidation
    const psv = readFileSync(path.join(root, 'src/features/payroll/PaySettingsValidation.ts'), 'utf8');
    assert.doesNotMatch(psv, /JSON\.stringify/);

    // Verify all operations api endpoints encode filters & sort
    for (const fn of [
      'getOperationsCities',
      'getOperationsHousing',
      'getOperationsPeriodReport',
      'getOperationsOilChangesPeriod',
      'getOperationsPurchaseInvoices',
      'getOperationsFuelCards',
      'getOperationsFuelAssignments',
      'exportOperationsCities',
      'exportOperationsHousing',
      'exportOperationsPeriod',
      'exportOperationsFuel',
    ]) {
      assert.match(apiSrc, new RegExp(`${fn}:[\\s\\S]{0,750}p\\.set\\('filters'`), `expected ${fn} to forward filters`);
      assert.match(apiSrc, new RegExp(`${fn}:[\\s\\S]{0,750}p\\.set\\('sort'`), `expected ${fn} to forward sort`);
    }

    const fParam = serializeServerFiltersParam(
      { total: { op: 'between', value: '500', valueTo: '2000' }, date: { op: 'thisMonth' } },
      [
        { key: 'total', filterColumn: 'totalAmount', type: 'number' },
        { key: 'date', filterColumn: 'invoiceDate', type: 'date' },
      ]
    );
    const sParam = serializeServerSortParam('status', 'asc', 'danger', [{ key: 'status' }]);
    assert.deepEqual(JSON.parse(fParam), [
      { col: 'totalAmount', op: 'between', value: '500', valueTo: '2000' },
      { col: 'invoiceDate', op: 'thisMonth' },
    ]);
    assert.deepEqual(JSON.parse(sParam), [{ col: 'status', dir: 'asc', tone: 'danger' }]);
  });

  it('rider-mobile broadcasts table sends filters, sort, and /rider-mobile/broadcasts/distinct params', () => {
    const src = readFileSync(path.join(root, 'src/features/rider-app/BroadcastsPanel.tsx'), 'utf8');
    assert.match(src, /serverSide/);
    assert.match(src, /distinctEndpoint="\/rider-mobile\/broadcasts\/distinct"/);
    assert.match(src, /onServerQueryChange=/);
    assert.match(src, /filters:\s*serverQuery\.filtersParam/);
    assert.match(src, /sort:\s*serverQuery\.sortParam/);
    assert.match(apiSrc, /getRiderBroadcastsPage:[\s\S]{0,500}q\.set\('filters'/);
    assert.match(apiSrc, /getRiderBroadcastsPage:[\s\S]{0,500}q\.set\('sort'/);
  });

  it('rider-mobile service-requests table sends filters, sort, and /rider-mobile/service-requests/distinct params', () => {
    const src = readFileSync(path.join(root, 'src/features/rider-app/RequestsPanel.tsx'), 'utf8');
    assert.match(src, /serverSide/);
    assert.match(src, /distinctEndpoint="\/rider-mobile\/service-requests\/distinct"/);
    assert.match(src, /onServerQueryChange=/);
    assert.match(src, /filters:\s*serverQuery\.filtersParam/);
    assert.match(src, /sort:\s*serverQuery\.sortParam/);
    assert.match(apiSrc, /getServiceRequestsPage:[\s\S]{0,800}q\.set\('filters'/);
    assert.match(apiSrc, /getServiceRequestsPage:[\s\S]{0,800}q\.set\('sort'/);
  });

  it('every raw <table> across web/src/app, web/src/features, and web/src/components uses ColumnFilterMenu AutoFilter', () => {
    const files = [
      ...walkTsx(path.join(root, 'src/app')),
      ...walkTsx(path.join(root, 'src/features')),
      ...walkTsx(path.join(root, 'src/components')),
    ].filter((f) => !f.endsWith('DataTable.tsx') && !f.endsWith('ColumnFilterMenu.tsx'));

    const missing = [];
    for (const file of files) {
      const content = readFileSync(file, 'utf8');
      const tableCount = (content.match(/<table\b/g) || []).length;
      if (tableCount === 0) continue;
      const hookCount = (content.match(/useTableAutoFilter\b/g) || []).length;
      const hasAutoTh = /<AutoFilterTh\b/.test(content);
      const hasAutoBar = /<AutoFilterBar\b/.test(content);
      // Minus 1 if `useTableAutoFilter` is imported on an import line
      const importMatches = (content.match(/import\s*\{[^}]*\buseTableAutoFilter\b[^}]*\}/g) || []).length;
      const callCount = hookCount - importMatches;
      if (callCount < tableCount || !hasAutoTh || !hasAutoBar) {
        missing.push(`${path.relative(root, file)} (tables=${tableCount}, hooks=${callCount}, AutoFilterTh=${hasAutoTh}, AutoFilterBar=${hasAutoBar})`);
      }
    }
    assert.deepEqual(missing, [], `Tables missing ColumnFilterMenu AutoFilter:\n${missing.join('\n')}`);
  });
});
