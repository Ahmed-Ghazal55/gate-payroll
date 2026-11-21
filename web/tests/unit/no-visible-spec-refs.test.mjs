import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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

test('web UI has no visible English Maker ≠ Checker or Maker-Checker wording', () => {
  const hits = [];
  const roots = [path.join(root, 'src/app'), path.join(root, 'src/features'), path.join(root, 'src/components')];
  for (const start of roots) {
    const stack = [start];
    while (stack.length) {
      const dir = stack.pop();
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          stack.push(full);
          continue;
        }
        if (!/\.(tsx|ts)$/.test(entry.name)) continue;
        const text = stripComments(readFileSync(full, 'utf8'));
        for (const pat of [/Maker\s*[≠!=]\s*Checker/gi, /Maker-Checker/gi]) {
          let m;
          while ((m = pat.exec(text))) {
            const line = text.slice(0, m.index).split('\n').length;
            hits.push(`${path.relative(root, full)}:${line}:${m[0]}`);
          }
        }
      }
    }
  }
  assert.deepEqual(hits, [], `visible Maker/Checker English text remains:\n${hits.join('\n')}`);
});
