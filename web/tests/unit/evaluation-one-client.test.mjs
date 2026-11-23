import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = path.resolve(webRoot, '..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');
const readRepo = (rel) => readFileSync(path.join(repoRoot, rel), 'utf8');

describe('EV5F F1 one rider client for sectioned rounds', () => {
  it('rider-form redirects sectioned schemas to /app/evaluation/ and drops section UI', () => {
    const rider = readRepo('RiderForm/src/rider-form.ts');
    assert.match(rider, /sectionKey/);
    assert.match(rider, /\/app\/evaluation\/\?/);
    assert.match(rider, /location\.replace/);
    assert.doesNotMatch(rider, /includeFinanceCard\?/);
    assert.doesNotMatch(rider, /eval-finance-card/);
    assert.doesNotMatch(rider, /EVAL_CHIPS/);
    assert.doesNotMatch(rider, /evalChipHtml/);
  });

  it('admin section copy/open links use /app/evaluation/', () => {
    const tab = readWeb('src/features/forms/EvaluationsTab.tsx');
    const builder = tab.match(
      /const buildPublicEvaluationUrl = \([\s\S]*?return `\$\{origin\}([^`]+)`/
    );
    assert.ok(builder, 'buildPublicEvaluationUrl return found');
    assert.match(builder[1], /\/app\/evaluation\/\?evaluation=/);
    assert.doesNotMatch(builder[1], /rider-form\.html/);
  });

  it('Launch.cs issues section tokens to /app/evaluation/', () => {
    const launch = readRepo('Payroll.Api/EvaluationSurvey.Launch.cs');
    assert.match(launch, /\/app\/evaluation\/\?evaluation=/);
    assert.match(launch, /ActiveSection|activeSection|section/);
  });
});
