import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  inferColumnFilterType,
  extractDistinctOptions,
  filterSelectOptions,
  matchesColumnFilter,
  createSearchableFilterState,
  searchableFilterReduce,
} from '../../src/lib/columnFilter.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

describe('DataTable & SearchableSelect column filter', () => {
  it('defaults code/status/city/company/platform/vehicle columns to list and names to text', () => {
    for (const col of ['code', 'status', 'city', 'company', 'companyId', 'platform', 'vehicleType', 'provider', 'sponsorNameAr']) {
      assert.equal(inferColumnFilterType(col), 'list', `expected ${col} to default to list`);
    }
    for (const col of ['name', 'riderName', 'description', 'notes', 'iqama', 'totalAmount']) {
      assert.equal(inferColumnFilterType(col), 'text', `expected ${col} to default to text`);
    }
    assert.equal(inferColumnFilterType('name', 'list'), 'list');
    assert.equal(inferColumnFilterType('status', 'text'), 'text');
  });

  it('extracts sorted unique non-empty distinct options', () => {
    const opts = extractDistinctOptions(['جدة', 'الرياض', 'جدة', '', null, undefined, '—', 'الدمام', 'الرياض ']);
    assert.deepEqual(
      opts.map((o) => o.value),
      ['الدمام', 'الرياض', 'جدة']
    );
    assert.deepEqual(
      opts.map((o) => o.label),
      ['الدمام', 'الرياض', 'جدة']
    );
  });

  it('typing filters options with Arabic and digit normalization', () => {
    const options = extractDistinctOptions([
      'شركة بترو اب',
      'شركة سيارة اب',
      'أبها 102',
      'الرياض',
    ]);
    // Normalized alef & taa marbuta
    const petro = filterSelectOptions(options, 'شركه بترو');
    assert.deepEqual(petro.map((o) => o.value), ['شركة بترو اب']);

    // Arabic-Indic digits match ASCII digits
    const abha = filterSelectOptions(options, 'ابها ١٠٢');
    assert.deepEqual(abha.map((o) => o.value), ['أبها 102']);

    // Empty query shows all options
    assert.equal(filterSelectOptions(options, '').length, 4);

    // Non-matching query returns empty list
    assert.deepEqual(filterSelectOptions(options, 'غير موجود'), []);
  });

  it('handles open, typing, selection, keyboard navigation, and clear transitions', () => {
    const options = extractDistinctOptions(['نشطة', 'غير نشطة', 'موقوفة']);
    let state = createSearchableFilterState();

    // Open
    state = searchableFilterReduce(state, { type: 'open' });
    assert.equal(state.open, true);
    assert.equal(state.activeIndex, 0);

    // Type narrows options
    state = searchableFilterReduce(state, { type: 'type', query: 'نشطه' });
    const filtered = filterSelectOptions(options, state.query);
    assert.equal(filtered.length, 2);

    // Arrow down moves highlight and Enter commits selection
    state = searchableFilterReduce(state, { type: 'move', delta: 1, count: filtered.length });
    assert.equal(state.activeIndex, 1);
    state = searchableFilterReduce(state, { type: 'commit', shown: filtered });
    assert.equal(state.open, false);
    assert.equal(state.query, '');
    assert.equal(state.value, filtered[1].value);

    // Direct selection
    state = searchableFilterReduce(state, { type: 'select', value: 'موقوفة' });
    assert.equal(state.value, 'موقوفة');
    assert.equal(state.open, false);

    // Clear resets value and query
    state = searchableFilterReduce(state, { type: 'clear' });
    assert.equal(state.value, '');
    assert.equal(state.query, '');
    assert.equal(state.open, false);
  });

  it('uses exact normalized match for list columns so active never matches inactive', () => {
    assert.equal(matchesColumnFilter('نشطة', 'نشطة', 'list'), true);
    assert.equal(matchesColumnFilter('غير نشطة', 'نشطة', 'list'), false);
    assert.equal(matchesColumnFilter('غير نشطة', 'غير نشطة', 'list'), true);

    // Text columns still allow partial / free-text search and OR via comma
    assert.equal(matchesColumnFilter('أحمد محمد', 'محمد', 'text'), true);
    assert.equal(matchesColumnFilter('أحمد محمد', 'خالد, احمد', 'text'), true);
    assert.equal(matchesColumnFilter('أحمد محمد', 'خالد', 'text'), false);
  });

  it('shared DataTable wires SearchableSelect for list columns and distinctEndpoint fallback', () => {
    const dt = readFileSync(path.join(root, 'src/components/ui/DataTable.tsx'), 'utf8');
    const ss = readFileSync(path.join(root, 'src/components/ui/SearchableSelect.tsx'), 'utf8');
    assert.match(dt, /<SearchableSelect/);
    assert.match(dt, /distinctEndpoint\?:/);
    assert.match(dt, /filterType\?: ColumnFilterType/);
    assert.match(ss, /role="combobox"/);
    assert.match(ss, /aria-label="مسح التصفية"/);
    assert.match(ss, /role="listbox"/);
  });
});
