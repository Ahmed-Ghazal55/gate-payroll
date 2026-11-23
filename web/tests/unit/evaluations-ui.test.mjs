import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  normalizeEvalDigits,
  localizedLabel,
  platformDisplayName,
  STAFF_DIMENSION_LABELS_AR,
  STAFF_SKIP_REASON_LABELS_AR,
  APP_SKIP_REASON_LABELS_AR,
  FACTOR_LABELS_AR,
} from '../../src/features/forms/evaluation-helpers.ts';
import {
  resolveRiderAppTab,
  formsRedirectTarget,
} from '../../src/features/rider-app/tabs.ts';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = path.resolve(webRoot, '..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');
const readRepo = (rel) => readFileSync(path.join(repoRoot, rel), 'utf8');

describe('EV1 evaluation survey and admin results screens', () => {
  it('derives all evaluation types in web/src/lib/api.ts strictly from web/src/lib/api-contract.ts', () => {
    const apiTs = readWeb('src/lib/api.ts');
    for (const route of [
      '/api/forms/evaluations/staff',
      '/api/forms/evaluations/staff/import',
      '/api/forms/evaluations/rounds',
      '/api/forms/evaluations/rounds/{id}/responses',
      '/api/forms/evaluations/rounds/{id}/statistics',
      '/api/forms/evaluations/rounds/{id}/statistics/{dimension}',
      '/api/forms/evaluations/rounds/{id}/scorecards/supervisors',
      '/api/forms/evaluations/rounds/{id}/scorecards/apps',
      '/api/forms/evaluations/rounds/{id}/scorecards/heatmap',
      '/api/forms/evaluations/rounds/{id}/scorecards/city-app',
      '/api/public/evaluations/{token}',
      '/api/rider-mobile/evaluations',
      '/api/rider-mobile/evaluations/{token}',
    ]) {
      assert.ok(
        apiTs.includes(`ApiResp<'${route}'`),
        `expected api.ts to derive type from contract route ${route}`
      );
    }
  });

  it('renders KPI1 scorecards with Arabic labels, drill-down, confidence note and Excel export hooks', () => {
    const panel = readWeb('src/features/forms/EvaluationScorecardsPanel.tsx');
    const adminTab = readWeb('src/features/forms/EvaluationsTab.tsx');
    const apiTs = readWeb('src/lib/api.ts');
    assert.match(adminTab, /EvaluationScorecardsPanel/);
    assert.match(adminTab, /handleScorecardDrillDown/);
    assert.match(panel, /eval-scorecards-panel/);
    assert.match(panel, /eval-scorecard-confidence-note/);
    assert.match(panel, /بطاقة أداء المشرف \/ الموظف/);
    assert.match(panel, /بطاقة أداء المنصة/);
    assert.match(panel, /خريطة حرارية: مشرف × بُعد/);
    assert.match(panel, /مصفوفة المدينة × المنصة/);
    assert.match(panel, /عينة صغيرة/);
    assert.match(panel, /exportEvaluationScorecard/);
    assert.match(panel, /eval-export-scorecard-supervisors/);
    assert.match(panel, /eval-export-scorecard-apps/);
    assert.match(panel, /eval-export-scorecard-heatmap/);
    assert.match(panel, /eval-export-scorecard-city-app/);
    assert.match(panel, /eval-heatmap-table/);
    assert.match(panel, /eval-city-app-matrix/);
    assert.match(apiTs, /getEvaluationSupervisorScorecard/);
    assert.match(apiTs, /exportEvaluationScorecard/);
    assert.match(panel, /identityVisible === false/);
    assert.match(panel, /disabled:cursor-default/);
    assert.match(adminTab, /supervisorId: supervisorFilter/);
    assert.match(adminTab, /department: departmentFilter/);
    assert.match(apiTs, /supervisorId/);
    assert.match(apiTs, /department/);
  });

  it('supports 3 languages (ar/en/ur), nationality, 5 staff dimensions (1-10), 1-5 app stars, factors, complaints, and pending role placeholder across web, RiderForm, and mobile', () => {
    const webForm = readWeb('src/features/forms/EvaluationPublicForm.tsx');
    const riderForm = readRepo('RiderForm/src/rider-form.ts');
    const mobileEval = readRepo('mobile/src/screens/evaluation.ts');

    for (const src of [webForm, riderForm, mobileEval]) {
      assert.match(src, /بانتظار موافقة غزال/);
      assert.doesNotMatch(src, /reportingLine/);
      assert.match(src, /nationality/);
      assert.match(src, /complaintDetails/);
      assert.match(src, /staffAnswers/);
      assert.match(src, /appAnswers/);
    }

    assert.equal(normalizeEvalDigits('٢٠٠٠٠٠٠٠٩١'), '2000000091');
    assert.equal(normalizeEvalDigits('۲۰۰۰۰۰۰۰۹۱'), '2000000091');
    assert.equal(
      localizedLabel(
        { labelAr: 'المستوى الفني', labelEn: 'Professional level', labelUr: 'پیشہ ورانہ سطح' },
        'ur'
      ),
      'پیشہ ورانہ سطح'
    );
    assert.equal(platformDisplayName('Ninja', 'ar'), 'نينجا');
    assert.equal(platformDisplayName('Keeta', 'ur'), 'کیٹا');
    assert.equal(platformDisplayName('HungerStation', 'en'), 'HungerStation');
  });

  it('renders admin results dashboard with filters, statistics, 5 charts, DataTable with ColumnFilterMenu, and allowedActions', () => {
    const adminTab = readWeb('src/features/forms/EvaluationsTab.tsx');
    assert.match(adminTab, /forms\.evaluations\.view/);
    assert.match(adminTab, /forms\.evaluations\.export/);
    assert.match(adminTab, /isSupervisorRole/);
    assert.match(adminTab, /allowedRoundActions\.includes\('open'\)/);
    assert.match(adminTab, /allowedRoundActions\.includes\('pause'\)/);
    assert.match(adminTab, /allowedRoundActions\.includes\('close'\)/);
    assert.match(adminTab, /eval-chart-by-city/);
    assert.match(adminTab, /eval-chart-by-nationality/);
    assert.match(adminTab, /eval-chart-by-app/);
    assert.match(adminTab, /eval-chart-factor-averages/);
    assert.match(adminTab, /eval-chart-by-supervisor/);
    assert.match(adminTab, /<DataTable\b/);
    assert.match(adminTab, /بانتظار موافقة غزال/);

    assert.equal(Object.keys(STAFF_DIMENSION_LABELS_AR).length, 5);
    assert.equal(Object.keys(STAFF_SKIP_REASON_LABELS_AR).length, 3);
    assert.equal(Object.keys(APP_SKIP_REASON_LABELS_AR).length, 2);
    assert.equal(Object.keys(FACTOR_LABELS_AR).length, 8);
  });

  it('routes /rider-app/?tab=evaluations and /forms/?tab=evaluations cleanly to campaigns evaluations subtab', () => {
    assert.deepEqual(resolveRiderAppTab('evaluations'), {
      tab: 'campaigns',
      subtab: 'evaluations',
      legacy: true,
    });
    assert.equal(
      formsRedirectTarget('tab=evaluations'),
      '/rider-app/?tab=campaigns&subtab=evaluations'
    );
  });

  it('keeps hosted evaluation survey session in gate_eval_survey eves_ only and reads launch from the URL fragment', () => {
    const riderForm = readRepo('RiderForm/src/rider-form.ts');
    const publicForm = readRepo('web/src/features/forms/EvaluationPublicForm.tsx');
    assert.match(riderForm, /gate_eval_survey/);
    assert.match(riderForm, /startsWith\('eves_'\)/);
    assert.match(riderForm, /hashParams\.get\('launch'\)/);
    assert.doesNotMatch(riderForm, /sessionStorage\.setItem\('gate_rider_mob'/);
    assert.match(publicForm, /gate_eval_survey/);
    assert.match(publicForm, /hashParams\.get\('launch'\)/);
    assert.match(publicForm, /startsWith\('eves_'\)/);
    assert.match(publicForm, /pathname\.startsWith\('\/app\/'\)/);
    assert.doesNotMatch(publicForm, /sessionStorage\.setItem\(\s*['"]gate_rider_mob['"]/);
  });
});
