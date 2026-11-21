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

/** English glosses removed in UT1F — must not reappear inside Arabic user-facing strings. */
const FORBIDDEN_EN_UI = [
  /Invoice Stepper/i,
  /\(sheetName\)/,
  /\(companyRows\)/,
  /\(orders\)/,
  /\(days\)/,
  /\(gross\)/,
  /\(attendanceDays\)/,
  /Campaign ID/i,
  /Explain Lines/i,
  /\(Approve\)/,
  /\(Reject\)/,
  /Source Type/i,
  /Custom Form/i,
  /Form \/ Campaign ID/i,
  /\(Field ID\)/,
  /Choice Key/i,
  /\(Scheme\)/,
  /Dry-Run/i,
  /\(Metric\)/,
  /\(Operator\)/,
  /\(Threshold\)/,
  /\(Effect\)/,
  /\(Included\)/,
  /\(Sponsor\)/,
  /Platform Reference/i,
  /\(slabs\)/,
  /\(gte\)/,
  /\(lte\)/,
  /\(neq\)/,
  /\(zero\)/,
  /\(until\)/,
  /\(Supervision\)/,
  /\(Role\)/,
  /\(Accountant\)/,
  /\(Reviewer\)/,
  /\(Manager\)/,
  /\(Admin\)/,
  /\(Dept\)/,
  /Super Admin/i,
  /\(RBAC\)/,
  /\(IBAN\)/,
  /\(Ninja\)/,
  /\(Keeta\)/,
  /\(Hungerstation\)/i,
  /\(Jahez\)/,
  /\(Amazon\)/,
  /On demand/i,
];

const STR_LIT = /(['"`])((?:\\.|(?!\1)[\s\S])*?)\1/g;

function walkUiSources(cb) {
  const roots = [
    path.join(root, 'src/app'),
    path.join(root, 'src/features'),
    path.join(root, 'src/components'),
    path.join(root, 'src/lib'),
  ];
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
        if (entry.name === 'api-contract.ts' || entry.name === 'types.ts') continue;
        cb(full, stripComments(readFileSync(full, 'utf8')));
      }
    }
  }
}

test('UT1F: Arabic UI strings do not reintroduce fixed English glosses', () => {
  const hits = [];
  walkUiSources((full, text) => {
    let m;
    STR_LIT.lastIndex = 0;
    while ((m = STR_LIT.exec(text))) {
      const lit = m[2];
      // User-facing Arabic copy (or known English-only label mistakes).
      const looksUi =
        /[\u0600-\u06FF]/.test(lit) ||
        /Invoice Stepper|Super Admin|On demand|Dry-Run|Explain Lines|Campaign ID|Platform Reference|Custom Form|Source Type/i.test(
          lit
        );
      if (!looksUi) continue;
      for (const pat of FORBIDDEN_EN_UI) {
        const found = lit.match(pat);
        if (found) {
          const line = text.slice(0, m.index).split('\n').length;
          hits.push(`${path.relative(root, full)}:${line}:${found[0]}`);
        }
      }
    }
  });
  assert.deepEqual(hits, [], `forbidden English UI glosses remain:\n${hits.join('\n')}`);
});
