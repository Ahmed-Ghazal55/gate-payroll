import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  suggestedChipsFor,
  sectionChipKindFromKey,
} from '../../src/features/forms/evaluation-helpers.ts';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');

describe('EV5F F2 complete EvaluationSectionFlow', () => {
  it('chips cover every spec list per section kind in ar/en/ur', () => {
    for (const lang of ['ar', 'en', 'ur']) {
      assert.equal(suggestedChipsFor('staff', lang).length, 8);
      assert.ok(suggestedChipsFor('movement', lang).length >= 11);
      assert.ok(suggestedChipsFor('hr', lang).length >= 10);
      assert.ok(suggestedChipsFor('support', lang).length >= 10);
      assert.equal(suggestedChipsFor('finance', lang).length, 5);
      assert.equal(suggestedChipsFor('app', lang).length, 5);
    }
    assert.equal(sectionChipKindFromKey('dept:movement'), 'movement');
    assert.equal(sectionChipKindFromKey('app:Keeta'), 'app');
  });

  it('section flow uses tap rating buttons, card progress, uploads, review, and edit', () => {
    const flow = readWeb('src/features/forms/EvaluationSectionFlow.tsx');
    assert.match(flow, /data-staff-rate/);
    assert.match(flow, /data-app-star/);
    assert.match(flow, /data-app-factor-star/);
    assert.match(flow, /data-finance-star/);
    assert.match(flow, /data-finance-factor/);
    assert.match(flow, /بطاقة|cardProgress|card of/i);
    assert.match(flow, /shrinkImageForEvaluation/);
    assert.match(flow, /voiceFileForEvaluation/);
    assert.match(flow, /eval-section-review|step === 'review'/);
    assert.match(flow, /editPublicEvaluation|editRiderMobileEvaluation/);
    assert.match(flow, /rolesLabel|roles\.join/);
    assert.match(flow, /eval-section-back|prevBtn|السابق/);
    assert.doesNotMatch(flow, /type=\"number\"/);
  });
});
