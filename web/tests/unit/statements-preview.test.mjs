import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { shouldWarnUnapproved } from '../../src/features/rider-app/statement-preview.ts';

describe('statements preview behaviour', () => {
  it('warns only when status is not approved (ignores approvedOnly flag)', () => {
    // Same cases the phone preview card evaluates after getRiderStatementPreview returns.
    assert.equal(shouldWarnUnapproved('preview'), true, 'draft/preview status must warn');
    assert.equal(shouldWarnUnapproved('no_approved_cycle'), true, 'empty cycle must warn');
    assert.equal(shouldWarnUnapproved('approved'), false, 'approved status must not warn');
    assert.equal(shouldWarnUnapproved(undefined), true);
    assert.equal(shouldWarnUnapproved(null), true);
  });
});
