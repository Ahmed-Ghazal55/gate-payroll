import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  debtsLoadKindsForTab,
  pageSlice,
  DEBTS_CLIENT_PAGE_SIZE,
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

  it('pages a list client-side until the API accepts page params', () => {
    const items = Array.from({ length: 120 }, (_, i) => i);
    assert.equal(DEBTS_CLIENT_PAGE_SIZE, 50);
    assert.deepEqual(pageSlice(items, 1), items.slice(0, 50));
    assert.deepEqual(pageSlice(items, 3), items.slice(100, 120));
  });
});
