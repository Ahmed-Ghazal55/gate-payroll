import test from 'node:test';
import assert from 'node:assert/strict';
import { payAppHasEligibility, normalizePayAppSlug } from '../../src/lib/pay-app-flags.ts';

test('normalizePayAppSlug maps hunger/chefz aliases', () => {
  assert.equal(normalizePayAppSlug('HungerStation'), 'hs');
  assert.equal(normalizePayAppSlug('the-chefz'), 'chefz');
  assert.equal(normalizePayAppSlug('jahez'), 'jahez');
});

test('T11 payAppHasEligibility false for jahez/chefz/hs; true for keeta/ninja', () => {
  assert.equal(payAppHasEligibility('jahez'), false);
  assert.equal(payAppHasEligibility('chefz'), false);
  assert.equal(payAppHasEligibility('hs'), false);
  assert.equal(payAppHasEligibility('hungerstation'), false);
  assert.equal(payAppHasEligibility('keeta'), true);
  assert.equal(payAppHasEligibility('ninja'), true);
});
