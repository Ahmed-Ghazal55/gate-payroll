import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const labels = readFileSync(path.join(root, 'src/lib/audit-labels.ts'), 'utf8');
const admin = readFileSync(path.join(root, 'src/app/admin/page.tsx'), 'utf8');
const dash = readFileSync(path.join(root, 'src/app/page.tsx'), 'utf8');
const debts = readFileSync(path.join(root, 'src/app/debts/page.tsx'), 'utf8');
const table = readFileSync(path.join(root, 'src/components/ui/DataTable.tsx'), 'utf8');
const vouchers = readFileSync(
  path.join(root, '../Payroll.Api/Vouchers.cs'),
  'utf8'
);

test('audit-labels maps known actions and formats dates', () => {
  assert.match(labels, /operations\.rider_accounts_read/);
  assert.match(labels, /export function auditActionLabelAr/);
  assert.match(labels, /export function formatAuditAt/);
  assert.match(labels, /calendar: 'gregory'/);
});

test('admin and dashboard use audit labels and bdi for actors', () => {
  assert.match(admin, /auditActionLabelAr/);
  assert.match(admin, /formatAuditAt/);
  assert.match(admin, /<bdi dir="ltr">/);
  assert.match(dash, /auditActionLabelAr/);
  assert.match(dash, /formatAuditAt/);
});

test('promissory status ActiveNafez maps to Arabic', () => {
  assert.match(debts, /ActiveNafez: 'نشط في نافذ'/);
  assert.match(debts, /promissoryStatusLabel/);
});

test('DataTable numeric footer uses formatSar', () => {
  assert.match(table, /col\.money \|\| col\.numeric/);
  assert.match(table, /formatSar\(value\)/);
});

test('Vouchers secret missing message is clean Arabic without config key', () => {
  assert.match(vouchers, /أكمل إعداد مفتاح توقيع السندات المالية قبل إنشاء مستند مالي/);
  assert.doesNotMatch(vouchers, /Vouchers:Secret/);
  assert.doesNotMatch(vouchers, /إعدادVouchers/);
});
