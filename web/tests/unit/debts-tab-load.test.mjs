import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  debtsLoadKindsForTab,
  DEBTS_PAGE_SIZE,
  debtCategoryOptionsFromCatalog,
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

  it('uses server page size 50 and builds category options from scoped catalog', () => {
    assert.equal(DEBTS_PAGE_SIZE, 50);
    const opts = debtCategoryOptionsFromCatalog([
      { code: 'InternetAdvance', nameAr: 'سلفة إنترنت' },
      { code: 'CashCustody', nameAr: 'عهدة نقدية' },
      { code: 'AdministrativeDeduction', nameAr: 'جزاء إداري' },
    ]);
    assert.ok(opts.some((o) => o.value === 'all'));
    assert.ok(opts.some((o) => o.value === 'advance'));
    assert.ok(opts.some((o) => o.value === 'InternetAdvance'));
    assert.ok(opts.some((o) => o.value === 'CashCustody'));
    assert.ok(opts.some((o) => o.value === 'AdministrativeDeduction'));
  });
});
