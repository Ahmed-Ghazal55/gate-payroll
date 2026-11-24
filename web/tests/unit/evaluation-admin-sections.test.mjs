import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');

describe('EV5 S4 admin sections panel', () => {
  it('EvaluationsTab lists sections with copy/delete and staff edit dialog without requiring JSON', () => {
    const tab = readWeb('src/features/forms/EvaluationsTab.tsx');
    assert.match(tab, /eval-round-sections/);
    assert.match(tab, /eval-section-copy/);
    assert.match(tab, /eval-round-delete-btn/);
    assert.match(tab, /deleteEvaluationRound|عنوان الجولة/);
    assert.match(tab, /eval-staff-edit-dialog/);
    assert.match(tab, /putEvaluationStaff|allApps/);
    assert.match(tab, /addEvaluationSectionStaff|removeEvaluationSectionStaff/);
  });

  it('scorecards panel filters by section and shows bySection plus finance card', () => {
    const panel = readWeb('src/features/forms/EvaluationScorecardsPanel.tsx');
    const api = readWeb('src/lib/api.ts');
    assert.match(panel, /bySection/);
    assert.match(panel, /eval-finance-scorecard|getEvaluationFinanceScorecard/);
    assert.match(api, /section\?:/);
    assert.match(api, /getEvaluationFinanceScorecard/);
    assert.match(api, /deleteEvaluationRound/);
    assert.match(api, /putEvaluationStaff/);
  });
});
