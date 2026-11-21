import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** Strip // and /* *\/ comments so only JSX/string text is scanned. */
function stripComments(src) {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/.*$/gm, '$1');
}

const FILES = [
  'src/app/payroll/page.tsx',
  'src/features/payroll/RiderAssignmentsWorkspace.tsx',
  'src/features/payroll/PayrollReviewGrid.tsx',
  'src/features/payroll/PayrollReviewDrawer.tsx',
  'src/app/riders/statement/page.tsx',
  'src/features/admin/ConfigListsPanel.tsx',
  'src/features/payroll/ChefzReviewPanel.tsx',
  'src/features/payroll/NinjaInvoiceUploadScreen.tsx',
];

test('listed screens have no visible BR § or R4. in non-comment text', () => {
  const hits = [];
  for (const rel of FILES) {
    const text = stripComments(readFileSync(path.join(root, rel), 'utf8'));
    for (const pat of [/BR\s*§/g, /R4\./g]) {
      let m;
      while ((m = pat.exec(text))) {
        const line = text.slice(0, m.index).split('\n').length;
        hits.push(`${rel}:${line}:${m[0]}`);
      }
    }
  }
  assert.deepEqual(hits, [], `visible spec refs remain:\n${hits.join('\n')}`);
});
