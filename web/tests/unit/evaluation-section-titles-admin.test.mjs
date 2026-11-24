import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const repoRoot = path.resolve(webRoot, '..');
const readWeb = (rel) => readFileSync(path.join(webRoot, rel), 'utf8');
const readRepo = (rel) => readFileSync(path.join(repoRoot, rel), 'utf8');

describe('EV5F F3 section titles + F4 admin links', () => {
  it('server builds Arabic app titles with titleEn/titleUr (not raw Keeta English)', () => {
    const sections = readRepo('Payroll.Api/EvaluationSurvey.Sections.cs');
    assert.match(sections, /تطبيق كيتا|كيتا/);
    assert.match(sections, /titleEn/);
    assert.match(sections, /titleUr/);
    assert.doesNotMatch(sections, /"تطبيق " \+ platform/);
    const schema = readRepo('Payroll.Api/EvaluationSurvey.Api.cs');
    assert.match(schema, /titleEn/);
    assert.match(schema, /titleUr/);
    const contract = readRepo('Payroll.Api/ApiContracts.Evaluation.cs');
    assert.match(contract, /EvaluationSection\(\)[\s\S]*titleEn/);
  });

  it('admin shows section title never raw key and hides round-level copy/preview when sectioned', () => {
    const tab = readWeb('src/features/forms/EvaluationsTab.tsx');
    assert.match(tab, /eval-copy-link-btn/);
    assert.match(tab, /eval-preview-mobile-btn/);
    assert.match(tab, /roundSections\.length === 0/);
    assert.match(tab, /\{section\.title\}/);
    assert.doesNotMatch(tab, /font-mono\}\{section\.key\}/);
    assert.doesNotMatch(tab, /section\.key\}<\/div>/);
  });
});
