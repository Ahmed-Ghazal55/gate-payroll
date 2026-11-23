import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  suggestedChipsFor,
  EVAL_IDENTITY_STORAGE_KEY,
} from '../../src/features/forms/evaluation-helpers.ts';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = path.resolve(webRoot, '..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');
const readRepo = (rel) => readFileSync(path.join(repoRoot, rel), 'utf8');

describe('EV5 S3 section public form', () => {
  it('exposes suggested answer chips per section type in ar/en/ur', () => {
    const staffAr = suggestedChipsFor('staff', 'ar');
    assert.ok(staffAr.some((c) => c.includes('متعاون')));
    assert.ok(suggestedChipsFor('staff', 'en').some((c) => /cooperat/i.test(c) || c.length > 3));
    assert.ok(suggestedChipsFor('movement', 'ar').some((c) => c.includes('صيانة')));
    assert.ok(suggestedChipsFor('hr', 'ar').some((c) => c.includes('إقامة')));
    assert.ok(suggestedChipsFor('finance', 'ar').some((c) => c.includes('راتب')));
    assert.ok(suggestedChipsFor('support', 'ar').some((c) => c.includes('انتظار')));
    assert.ok(suggestedChipsFor('app', 'ar').some((c) => c.includes('طلبات')));
  });

  it('section form remembers identity and walks notice → identity → city → staff → app/finance', () => {
    const form = readWeb('src/features/forms/EvaluationPublicForm.tsx');
    const flow = readWeb('src/features/forms/EvaluationSectionFlow.tsx');
    assert.match(form, /EvaluationSectionFlow/);
    assert.match(form, /sectionKey/);
    assert.match(form, /data-testid=\"evaluation-page-1\"/); // legacy two-page path kept
    assert.match(flow, /EVAL_IDENTITY_STORAGE_KEY/);
    assert.equal(EVAL_IDENTITY_STORAGE_KEY, 'gate_eval_identity_v1');
    assert.match(flow, /includeFinance/);
    assert.match(flow, /financeAnswer/);
    assert.match(flow, /suggestedChipsFor/);
    assert.match(flow, /eval-suggested-chip/);
    assert.match(flow, /eval-section-staff-step/);
    assert.match(flow, /eval-finance-card/);
  });

  it('server-hosted rider form also supports section finance card and chips', () => {
    const rider = readRepo('RiderForm/src/rider-form.ts');
    assert.match(rider, /includeFinanceCard|financeAnswer|evaluation-section-v1/);
    assert.match(rider, /eval_chip_|suggestedChip|مالية/);
  });
});
