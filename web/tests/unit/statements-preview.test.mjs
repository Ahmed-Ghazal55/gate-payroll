import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const panel = readFileSync(path.join(root, 'src/features/rider-app/StatementsPanel.tsx'), 'utf8');
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

assert.ok(!panel.includes('approvedOnly === false'), 'must not warn solely because approvedOnly is false');
assert.ok(panel.includes("status !== 'approved'"), 'warn only when status is not approved');
assert.ok(panel.includes('shortLines') && panel.includes('deductions') && panel.includes('disclaimerAr'), 'preview renders short lines, deductions and disclaimer');
assert.ok(api.includes('approvedOnly=1'), 'preview requests approved-only statements');
console.log('PASS statements preview warning and fields');
