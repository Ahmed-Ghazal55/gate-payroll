import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  inferColumnFilterType,
  resolveColumnType,
  extractDistinctOptions,
  extractDistinctTones,
  filterSelectOptions,
  matchesColumnFilter,
  createSearchableFilterState,
  searchableFilterReduce,
  evaluateColumnFilter,
  compareColumnValues,
  computeTopNThreshold,
  toggleSelectAllOptions,
  applySearchResultsSelection,
  serializeServerFilters,
  serializeServerSort,
  defaultStatusTone,
  isColumnFilterActive,
} from '../../src/lib/columnFilter.ts';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

describe('DataTable & ColumnFilterMenu Excel AutoFilter', () => {
  it('defaults code/status/city/company/platform/vehicle columns to list and names to text', () => {
    for (const col of ['code', 'status', 'city', 'company', 'companyId', 'platform', 'vehicleType', 'provider', 'sponsorNameAr']) {
      assert.equal(inferColumnFilterType(col), 'list', `expected ${col} to default to list`);
    }
    for (const col of ['name', 'riderName', 'description', 'notes', 'iqama', 'totalAmount']) {
      assert.equal(inferColumnFilterType(col), 'text', `expected ${col} to default to text`);
    }
    assert.equal(inferColumnFilterType('name', 'list'), 'list');
    assert.equal(inferColumnFilterType('status', 'text'), 'text');
    assert.equal(resolveColumnType({ key: 'totalAmount', numeric: true }), 'number');
    assert.equal(resolveColumnType({ key: 'invoiceDate' }), 'date');
    assert.equal(resolveColumnType({ key: 'status' }), 'list');
    assert.equal(resolveColumnType({ key: 'iqama', numeric: true }), 'text');
  });

  it('extracts sorted unique non-empty distinct options with counts, labels, and tones', () => {
    const opts = extractDistinctOptions(['جدة', 'الرياض', 'جدة', '', null, undefined, '—', 'الدمام', 'الرياض ']);
    assert.deepEqual(
      opts.map((o) => o.value),
      ['الدمام', 'الرياض', 'جدة']
    );
    assert.deepEqual(
      opts.map((o) => o.label),
      ['الدمام', 'الرياض', 'جدة']
    );
    assert.deepEqual(
      opts.map((o) => o.count),
      [1, 2, 2]
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

  it('evaluates text filter operators: contains, notContains, beginsWith, endsWith, eq, neq', () => {
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'contains', value: 'بترو' }, { type: 'text' }), true);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'notContains', value: 'سيارة' }, { type: 'text' }), true);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'notContains', value: 'بترو' }, { type: 'text' }), false);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'beginsWith', value: 'شركه' }, { type: 'text' }), true);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'beginsWith', value: 'بترو' }, { type: 'text' }), false);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'endsWith', value: 'اب' }, { type: 'text' }), true);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'eq', value: 'شركه بترو اب' }, { type: 'text' }), true);
    assert.equal(evaluateColumnFilter('شركة بترو اب', { op: 'neq', value: 'شركة سيارة اب' }, { type: 'text' }), true);
  });

  it('evaluates number filter operators: eq, neq, gt, gte, lt, lte, between, topN', () => {
    const opts = { type: 'number' };
    assert.equal(evaluateColumnFilter(1500, { op: 'eq', value: '1500' }, opts), true);
    assert.equal(evaluateColumnFilter('1,500.00', { op: 'eq', value: '١٥٠٠' }, opts), true);
    assert.equal(evaluateColumnFilter(1500, { op: 'neq', value: '1200' }, opts), true);
    assert.equal(evaluateColumnFilter(1500, { op: 'gt', value: '1000' }, opts), true);
    assert.equal(evaluateColumnFilter(1000, { op: 'gt', value: '1000' }, opts), false);
    assert.equal(evaluateColumnFilter(1000, { op: 'gte', value: '1000' }, opts), true);
    assert.equal(evaluateColumnFilter(800, { op: 'lt', value: '1000' }, opts), true);
    assert.equal(evaluateColumnFilter(null, { op: 'lt', value: '1000' }, opts), false);
    assert.equal(evaluateColumnFilter(1000, { op: 'lte', value: '1000' }, opts), true);
    assert.equal(evaluateColumnFilter(1500, { op: 'between', value: '1000', valueTo: '2000' }, opts), true);
    assert.equal(evaluateColumnFilter(2500, { op: 'between', value: '1000', valueTo: '2000' }, opts), false);

    // Top N operator
    const sampleNums = [100, 500, 250, 900, 750, null];
    const threshold = computeTopNThreshold(sampleNums, 2);
    assert.equal(threshold, 750);
    assert.equal(
      evaluateColumnFilter(900, { op: 'topN', value: '2' }, { type: 'number', topNThreshold: threshold }),
      true
    );
    assert.equal(
      evaluateColumnFilter(750, { op: 'topN', value: '2' }, { type: 'number', topNThreshold: threshold }),
      true
    );
    assert.equal(
      evaluateColumnFilter(500, { op: 'topN', value: '2' }, { type: 'number', topNThreshold: threshold }),
      false
    );
  });

  it('evaluates date filter operators: eq, neq, before, after, between, thisMonth, lastMonth', () => {
    const refDate = '2026-11-19';
    const opts = { type: 'date', referenceDate: refDate };
    assert.equal(evaluateColumnFilter('2026-11-15', { op: 'eq', value: '2026-11-15' }, opts), true);
    assert.equal(evaluateColumnFilter('2026-11-15', { op: 'neq', value: '2026-11-16' }, opts), true);
    assert.equal(evaluateColumnFilter('2026-11-10', { op: 'before', value: '2026-11-15' }, opts), true);
    assert.equal(evaluateColumnFilter('2026-11-20', { op: 'after', value: '2026-11-15' }, opts), true);
    assert.equal(
      evaluateColumnFilter('2026-11-12', { op: 'between', value: '2026-11-01', valueTo: '2026-11-15' }, opts),
      true
    );
    assert.equal(
      evaluateColumnFilter('2026-11-25', { op: 'between', value: '2026-11-01', valueTo: '2026-11-15' }, opts),
      false
    );
    assert.equal(evaluateColumnFilter('2026-11-05', { op: 'thisMonth' }, opts), true);
    assert.equal(evaluateColumnFilter('2026-10-28', { op: 'thisMonth' }, opts), false);
    assert.equal(evaluateColumnFilter('2026-10-28', { op: 'lastMonth' }, opts), true);
    assert.equal(evaluateColumnFilter('2026-09-28', { op: 'lastMonth' }, opts), false);
  });

  it('sorts text (Arabic collation), numbers, dates, and sort-by-color with blanks always at bottom', () => {
    // Arabic collation A->Z and Z->A
    const cities = ['جدة', '', 'الدمام', 'أبها', 'الرياض'];
    const ascCities = [...cities].sort((a, b) => compareColumnValues(a, b, { type: 'text', dir: 'asc' }));
    assert.equal(ascCities[ascCities.length - 1], '');
    assert.equal(ascCities[0], 'أبها');
    const descCities = [...cities].sort((a, b) => compareColumnValues(a, b, { type: 'text', dir: 'desc' }));
    assert.equal(descCities[descCities.length - 1], '');

    // Numbers smallest->largest and largest->smallest
    const nums = ['1,200', null, '45', '300'];
    const ascNums = [...nums].sort((a, b) => compareColumnValues(a, b, { type: 'number', dir: 'asc' }));
    assert.deepEqual(ascNums, ['45', '300', '1,200', null]);
    const descNums = [...nums].sort((a, b) => compareColumnValues(a, b, { type: 'number', dir: 'desc' }));
    assert.deepEqual(descNums, ['1,200', '300', '45', null]);

    // Dates oldest->newest and newest->oldest with blanks at bottom
    const dates = ['2026-11-15', null, '2026-09-01', '2026-10-20'];
    const ascDates = [...dates].sort((a, b) => compareColumnValues(a, b, { type: 'date', dir: 'asc' }));
    assert.deepEqual(ascDates, ['2026-09-01', '2026-10-20', '2026-11-15', null]);
    const descDates = [...dates].sort((a, b) => compareColumnValues(a, b, { type: 'date', dir: 'desc' }));
    assert.deepEqual(descDates, ['2026-11-15', '2026-10-20', '2026-09-01', null]);

    // Sort by color (badge tone) puts selected tone at the top
    const statuses = ['active', 'pending_approval', 'rejected', 'active'];
    const toneOf = (v) =>
      v === 'active' ? 'success' : v === 'pending_approval' ? 'warning' : 'danger';
    const sortedByWarning = [...statuses].sort((a, b) =>
      compareColumnValues(a, b, { type: 'list', dir: 'asc', sortTone: 'warning', toneOf })
    );
    assert.equal(sortedByWarning[0], 'pending_approval');
  });

  it('supports Filter by Color and maps labelOf(value) <-> raw stored value accurately', () => {
    const STATUS_LABELS = {
      active: 'نشط',
      pending_approval: 'بانتظار الاعتماد',
      settled: 'مسدد بالكامل',
      cancelled: 'ملغي',
    };
    const labelOf = (v) => STATUS_LABELS[v] || String(v ?? '');
    const toneOf = (v) => defaultStatusTone(v);

    const rawValues = ['active', 'pending_approval', 'settled', 'cancelled', 'active'];
    const options = extractDistinctOptions(rawValues, { labelOf, toneOf });

    // Search uses displayed Arabic label ('بانتظار') and resolves to underlying stored value ('pending_approval')
    const matched = filterSelectOptions(options, 'بانتظار');
    assert.equal(matched.length, 1);
    assert.equal(matched[0].value, 'pending_approval');
    assert.equal(matched[0].label, 'بانتظار الاعتماد');

    // Pressing Enter in search selects all matching search results' raw values
    const filterAfterEnter = applySearchResultsSelection(options, 'نشط');
    assert.deepEqual(filterAfterEnter.values, ['active']);
    assert.equal(evaluateColumnFilter('active', filterAfterEnter, { type: 'list', labelOf, toneOf }), true);
    assert.equal(evaluateColumnFilter('cancelled', filterAfterEnter, { type: 'list', labelOf, toneOf }), false);

    // Filter by Color (danger -> matches 'cancelled')
    const tones = extractDistinctTones(options, toneOf);
    assert.ok(tones.some((t) => t.tone === 'success'));
    assert.ok(tones.some((t) => t.tone === 'warning'));
    assert.ok(tones.some((t) => t.tone === 'danger'));

    const dangerFilter = { colorTone: 'danger' };
    assert.equal(evaluateColumnFilter('cancelled', dangerFilter, { type: 'list', labelOf, toneOf }), true);
    assert.equal(evaluateColumnFilter('active', dangerFilter, { type: 'list', labelOf, toneOf }), false);
  });

  it('handles Select All and Select All Search Results in checkbox list', () => {
    const options = extractDistinctOptions(['الرياض', 'جدة', 'الدمام', 'مكة']);
    // Uncheck all when search is empty
    const none = toggleSelectAllOptions(options, '', undefined, false);
    assert.deepEqual(none, []);
    // Check all when search is empty -> undefined (no filter restriction)
    const all = toggleSelectAllOptions(options, '', none, true);
    assert.equal(all, undefined);
    // Toggle only search matches when search is active
    const onlyJeddah = toggleSelectAllOptions(options, 'جده', [], true);
    assert.deepEqual(onlyJeddah, ['جدة']);
  });

  it('combines filters across multiple columns and serializes to the generic server contract', () => {
    const rows = [
      { id: '1', city: 'الرياض', status: 'active', amount: 1500, date: '2026-11-10' },
      { id: '2', city: 'الرياض', status: 'cancelled', amount: 1800, date: '2026-11-12' },
      { id: '3', city: 'جدة', status: 'active', amount: 2200, date: '2026-11-14' },
      { id: '4', city: 'الرياض', status: 'active', amount: 900, date: '2026-10-05' },
    ];
    const filters = {
      city: { values: ['الرياض'] },
      status: { colorTone: 'success' },
      amount: { op: 'gte', value: '1200' },
      date: { op: 'thisMonth' },
    };

    const filtered = rows.filter(
      (r) =>
        evaluateColumnFilter(r.city, filters.city, { type: 'list' }) &&
        evaluateColumnFilter(r.status, filters.status, { type: 'list' }) &&
        evaluateColumnFilter(r.amount, filters.amount, { type: 'number' }) &&
        evaluateColumnFilter(r.date, filters.date, { type: 'date', referenceDate: '2026-11-19' })
    );
    assert.deepEqual(filtered.map((r) => r.id), ['1']);

    // Server contract serialization
    const clauses = serializeServerFilters(filters, [
      { key: 'city', filterColumn: 'city' },
      { key: 'status', filterColumn: 'status' },
      { key: 'amount', filterColumn: 'remainingBalance' },
      { key: 'date', filterColumn: 'createdAt' },
    ]);
    assert.equal(clauses.length, 4);
    assert.deepEqual(clauses[0], { col: 'city', op: 'in', values: ['الرياض'] });
    assert.deepEqual(clauses[1], { col: 'status', op: 'color', value: 'success' });
    assert.deepEqual(clauses[2], { col: 'remainingBalance', op: 'gte', value: '1200' });
    assert.deepEqual(clauses[3], { col: 'createdAt', op: 'thisMonth' });

    const sortClauses = serializeServerSort('amount', 'desc', null, [
      { key: 'amount', filterColumn: 'remainingBalance' },
    ]);
    assert.deepEqual(sortClauses, [{ col: 'remainingBalance', dir: 'desc' }]);

    // Clear filter
    assert.equal(isColumnFilterActive(undefined), false);
    assert.equal(isColumnFilterActive({}), false);
  });

  it('shared DataTable wires SearchableSelect, ColumnFilterMenu, TanStack Table, and distinctEndpoint fallback', () => {
    const dt = readFileSync(path.join(root, 'src/components/ui/DataTable.tsx'), 'utf8');
    const cfm = readFileSync(path.join(root, 'src/components/ui/ColumnFilterMenu.tsx'), 'utf8');
    const ss = readFileSync(path.join(root, 'src/components/ui/SearchableSelect.tsx'), 'utf8');
    assert.match(dt, /<SearchableSelect/);
    assert.match(dt, /<ColumnFilterMenu/);
    assert.match(dt, /useReactTable/);
    assert.match(dt, /distinctEndpoint\?:/);
    assert.match(dt, /filterType\?: ColumnFilterType/);
    assert.match(dt, /labelOf\?:/);
    assert.match(dt, /toneOf\?:/);
    assert.match(dt, /مسح كل الفلاتر/);
    assert.match(cfm, /ترتيب حسب اللون/);
    assert.match(cfm, /تصفية حسب اللون/);
    assert.match(cfm, /تحديد كل نتائج البحث/);
    assert.match(ss, /role="combobox"/);
    assert.match(ss, /aria-label="مسح التصفية"/);
    assert.match(ss, /role="listbox"/);
  });
});
