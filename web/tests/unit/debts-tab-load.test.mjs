import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  debtsLoadKindsForTab,
  DEBTS_PAGE_SIZE,
  DEBT_CATEGORY_OPTIONS,
} from '../../src/features/debts/debts-tab-load.ts';

describe('debts tab load (F-D2)', () => {
  it('loads only debts for the debts tab', () => {
    assert.deepEqual(debtsLoadKindsForTab('debts'), ['debts']);
  });

  it('does not prefetch approvals or promissory with debts', () => {
    const kinds = debtsLoadKindsForTab('debts');
    assert.equal(kinds.includes('approvals'), false);
    assert.equal(kinds.includes('promissory'), false);
  });

  it('loads only approvals for the approvals tab', () => {
    assert.deepEqual(debtsLoadKindsForTab('approvals'), ['approvals']);
  });

  it('loads only promissory for the promissory tab', () => {
    assert.deepEqual(debtsLoadKindsForTab('promissory'), ['promissory']);
  });

  it('uses server page size 50 and category options for filters', () => {
    assert.equal(DEBTS_PAGE_SIZE, 50);
    assert.ok(DEBT_CATEGORY_OPTIONS.some((o) => o.value === 'all'));
    assert.ok(DEBT_CATEGORY_OPTIONS.some((o) => o.value === 'advance'));
  });
});
