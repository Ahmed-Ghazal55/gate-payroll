import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  partitionRowsForFacets,
  facetRowsFor,
  evaluateColumnFilter,
  extractDistinctOptions,
} from '../../src/lib/columnFilter.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

const rows = [
  { city: 'الرياض', platform: 'Ninja', status: 'active' },
  { city: 'الرياض', platform: 'Keeta', status: 'active' },
  { city: 'جدة', platform: 'Jahez', status: 'stopped' },
  { city: 'جدة', platform: 'Ninja', status: 'active' },
  { city: 'الدمام', platform: 'HungerStation', status: 'active' },
];
const listTest = (key, values) => ({
  key,
  test: (r) => evaluateColumnFilter(r[key], { values }, { type: 'list' }),
});
const valuesOf = (facets, key) =>
  extractDistinctOptions(facetRowsFor(facets, key).map((r) => r[key]), { type: 'list', includeEmpty: false })
    .map((o) => o.value)
    .sort();

describe('cascading column value lists (Excel-style)', () => {
  it('other columns list only values inside the active filter', () => {
    const facets = partitionRowsForFacets(rows, [listTest('city', ['الرياض'])]);
    assert.deepEqual(valuesOf(facets, 'platform'), ['Keeta', 'Ninja']);
    assert.deepEqual(valuesOf(facets, 'status'), ['active']);
  });

  it('the filtered column itself still lists every value so the user can widen it', () => {
    const facets = partitionRowsForFacets(rows, [listTest('city', ['الرياض'])]);
    assert.deepEqual(valuesOf(facets, 'city'), ['الدمام', 'الرياض', 'جدة']);
  });

  it('two filters: each column is narrowed by the other one only', () => {
    const facets = partitionRowsForFacets(rows, [listTest('city', ['جدة']), listTest('platform', ['Ninja'])]);
    assert.deepEqual(valuesOf(facets, 'city'), ['الرياض', 'جدة']);
    assert.deepEqual(valuesOf(facets, 'platform'), ['Jahez', 'Ninja']);
    assert.deepEqual(valuesOf(facets, 'status'), ['active']);
  });

  it('include() (global search) narrows every list', () => {
    const facets = partitionRowsForFacets(rows, [], (r) => r.status === 'stopped');
    assert.deepEqual(valuesOf(facets, 'city'), ['جدة']);
  });

  it('DataTable and useTableAutoFilter both build value lists from facet rows', () => {
    const dt = readFileSync(path.join(root, 'src/components/ui/DataTable.tsx'), 'utf8');
    const menu = readFileSync(path.join(root, 'src/components/ui/ColumnFilterMenu.tsx'), 'utf8');
    assert.match(dt, /facetRowsFor\(facetRows, col\.key\)/);
    assert.match(menu, /facetRowsFor\(facets, col\.key\)/);
  });
});
