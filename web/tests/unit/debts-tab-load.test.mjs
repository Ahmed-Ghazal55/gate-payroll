import { describe, it } from 'node:test';
import assert from 'node:assert/strict';

// Mirrors web/src/features/debts/debts-tab-load.ts (keep in sync).
function debtsLoadKindsForTab(tab) {
  switch (tab) {
    case 'debts':
    case 'actions':
      return ['debts'];
    case 'approvals':
      return ['approvals'];
    case 'promissory':
      return ['promissory'];
    case 'advance_requests':
      return ['advances'];
    case 'statements':
      return ['none'];
    default:
      return ['none'];
  }
}

const DEBTS_CLIENT_PAGE_SIZE = 50;

function pageSlice(items, page, pageSize = DEBTS_CLIENT_PAGE_SIZE) {
  const p = Math.max(1, page);
  const start = (p - 1) * pageSize;
  return items.slice(start, start + pageSize);
}

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
    assert.deepEqual(pageSlice(items, 1), items.slice(0, 50));
    assert.deepEqual(pageSlice(items, 3), items.slice(100, 120));
  });
});
