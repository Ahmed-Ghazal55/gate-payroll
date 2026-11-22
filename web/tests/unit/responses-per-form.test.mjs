import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const tab = readFileSync(path.join(root, 'src/features/forms/ResponsesTab.tsx'), 'utf8');
const types = readFileSync(path.join(root, 'src/lib/types.ts'), 'utf8');

describe('responses: one table per campaign or form (Ghazal 2026-11-22)', () => {
  it('loads the picker list and column definitions from GET /api/responses/forms', () => {
    assert.match(tab, /api\.getResponseForms\(\)/);
    assert.match(types, /paths\['\/api\/responses\/forms'\]/, 'type comes from the generated contract');
  });

  it('shows only the selected form: no "all campaigns" option', () => {
    assert.doesNotMatch(tab, /'كل الحملات'/);
    assert.match(tab, /s\.campaignId === selectedForm\.id/);
  });

  it('builds one column per question by looping over the selected form columns', () => {
    assert.match(tab, /\(selectedForm\?\.columns \?\? \[\]\)\.map\(/);
    assert.match(tab, /\.\.\.questionColumns/);
  });

  it('export uses the same questions as the screen, one file named after the form', () => {
    assert.match(tab, /\.\.\.questions\.map\(\(q\) => q\.labelAr\)/);
    assert.match(tab, /filename: `\$\{baseName\}\.xlsx`/);
  });

  it('keeps IBAN masked without riders.iban.view and review actions on campaigns only', () => {
    assert.match(tab, /canViewIban \? iban : `••••\$\{iban\.slice\(-4\)\}`/);
    assert.match(tab, /const canActOnRows = canReview && isCampaign/);
  });
});
