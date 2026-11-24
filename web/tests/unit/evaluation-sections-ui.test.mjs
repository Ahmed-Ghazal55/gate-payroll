import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  suggestedChipsFor,
  EVAL_IDENTITY_STORAGE_KEY,
  servesFilterCity,
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
    assert.ok(suggestedChipsFor('staff', 'ur').length >= 2);
    assert.ok(suggestedChipsFor('movement', 'ar').some((c) => c.includes('صيانة')));
    assert.ok(suggestedChipsFor('movement', 'ur').length >= 1);
    assert.ok(suggestedChipsFor('hr', 'ar').some((c) => c.includes('إقامة')));
    assert.ok(suggestedChipsFor('hr', 'en').some((c) => /iqama|papers|leave/i.test(c)));
    assert.ok(suggestedChipsFor('hr', 'ur').length >= 1);
    assert.ok(suggestedChipsFor('finance', 'ar').some((c) => c.includes('راتب')));
    assert.ok(suggestedChipsFor('finance', 'ur').length >= 1);
    assert.ok(suggestedChipsFor('support', 'ar').some((c) => c.includes('انتظار')));
    assert.ok(suggestedChipsFor('support', 'ur').length >= 1);
    assert.ok(suggestedChipsFor('app', 'ar').some((c) => c.includes('طلبات')));
    assert.ok(suggestedChipsFor('app', 'ur').length >= 1);
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
    assert.match(flow, /eval-section-\$\{current\.kind\}-step|eval-section-staff-step/);
    assert.match(flow, /eval-section-staff-reason/);
    assert.match(flow, /eval-finance-card/);
    assert.match(flow, /eval-section-review/);
  });

  it('EV6 R1 shows highlighted notice lines and complaint confidentiality reminder', () => {
    const flow = readWeb('src/features/forms/EvaluationSectionFlow.tsx');
    const api = readRepo('Payroll.Api/EvaluationSurvey.Api.cs');
    assert.match(flow, /eval-confidential-notice/);
    assert.match(flow, /noticeLines/);
    assert.match(flow, /eval-complaint-reminder/);
    assert.match(flow, /complaintConfidentialityReminder/);
    assert.match(api, /تقييمك وشكواك سرية تمامًا/);
    assert.match(api, /شكواك سرية ولن تُستخدم ضدك/);
  });

  it('EV6 R2 systems card follows finance in app forms with chips', () => {
    const flow = readWeb('src/features/forms/EvaluationSectionFlow.tsx');
    const helpers = readWeb('src/features/forms/evaluation-helpers.ts');
    assert.match(flow, /includeSystems/);
    assert.match(flow, /systemsAnswer/);
    assert.match(flow, /eval-systems-card/);
    assert.match(flow, /kind: 'systems'/);
    assert.match(helpers, /systems:/);
    assert.match(helpers, /النماذج سهلة وواضحة/);
  });

  it('EV6 R3/R4 staff editor has الإدارة department and appCities', () => {
    const tab = readWeb('src/features/forms/EvaluationsTab.tsx');
    const helpers = readWeb('src/features/forms/evaluation-helpers.ts');
    const sections = readRepo('Payroll.Api/EvaluationSurvey.Sections.cs');
    assert.match(helpers, /الإدارة/);
    assert.match(helpers, /STAFF_DEPARTMENT_OPTIONS/);
    assert.match(tab, /eval-staff-departments/);
    assert.match(tab, /eval-staff-app-cities/);
    assert.match(tab, /appCities/);
    assert.match(tab, /مدن هذا التطبيق/);
    assert.match(sections, /الإدارة والموارد البشرية/);
    assert.match(sections, /DeptManagement/);
    assert.match(sections, /ServesAppCity/);
  });

  it('server-hosted rider form redirects section tokens to the web client', () => {
    const rider = readRepo('RiderForm/src/rider-form.ts');
    assert.match(rider, /sectionKey/);
    assert.match(rider, /location\.replace/);
    assert.match(rider, /\/app\/evaluation\/\?/);
    assert.match(rider, /q\.set\(['"]evaluation['"]/);
    assert.doesNotMatch(rider, /eval-finance-card/);
    assert.doesNotMatch(rider, /eval-section-flow/);
  });
});

describe('EV6 city rule (same as server ServesSectionCity / ServesFilterCity)', () => {
  const mover = { cities: ['City A'], appCities: { Ninja: ['City B'] } };
  const remote = { cities: ['Office'], allCities: true };
  it('app section honours the per-app list', () => {
    assert.equal(servesFilterCity(mover, 'City B', 'app:Ninja'), true);
    assert.equal(servesFilterCity(mover, 'City A', 'app:Ninja'), false);
    assert.equal(servesFilterCity(mover, 'City A', 'app:Keeta'), true);
  });
  it('department section ignores the per-app list', () => {
    assert.equal(servesFilterCity(mover, 'City A', 'dept:movement', 'Ninja'), true);
    assert.equal(servesFilterCity(mover, 'City B', 'dept:movement'), false);
  });
  it('no section: an app filter uses the per-app list; otherwise card cities or all cities', () => {
    assert.equal(servesFilterCity(mover, 'City B', null, 'Ninja'), true);
    assert.equal(servesFilterCity(mover, 'City A', null, 'Ninja'), false);
    assert.equal(servesFilterCity(mover, 'City A'), true);
    assert.equal(servesFilterCity(remote, 'Any city'), true);
  });
});
