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
  assert.match(labels, /pay\.cycle_created/);
  assert.match(labels, /platform\.ninja\.invoice_imported/);
  assert.match(labels, /jahez\.reconciliation_approved/);
  assert.match(labels, /excel\.import\./);
  assert.doesNotMatch(labels, /AUDIT_ACTION_LABELS_AR\[key\] \|\| AUDIT_ACTION_LABELS_AR\[last\]/);
});

test('auditActionLabelAr covers API keys and keeps generic fallback', async () => {
  const mod = await import('../../src/lib/audit-labels.ts');
  assert.equal(mod.auditActionLabelAr('pay.cycle_created'), 'إنشاء دورة رواتب');
  assert.equal(mod.auditActionLabelAr('admin.login'), 'تسجيل دخول إداري');
  assert.equal(mod.auditActionLabelAr('excel.import.riders'), 'استيراد ملف إكسل');
  assert.equal(mod.auditActionLabelAr('totally.unknown.action.xyz'), 'عملية إدارية مسجلة');
});

test('formatAuditActorDisplay adds @ only for real usernames', async () => {
  const mod = await import('../../src/lib/audit-labels.ts');
  assert.equal(mod.formatAuditActorDisplay('system'), 'النظام');
  assert.equal(mod.formatAuditActorDisplay('system-migration'), 'ترحيل النظام');
  assert.equal(mod.formatAuditActorDisplay('ghazal'), '@ghazal');
});

test('admin and dashboard use audit labels, raw action title, and bdi for actors', () => {
  assert.match(admin, /auditActionLabelAr/);
  assert.match(admin, /formatAuditAt/);
  assert.match(admin, /title=\{(?:String\()?log\.action/);
  assert.match(admin, /<bdi dir="ltr">/);
  assert.match(dash, /auditActionLabelAr/);
  assert.match(dash, /formatAuditAt/);
  assert.match(dash, /title=\{(?:String\()?row\.action/);
});

test('promissory status ActiveNafez maps to Arabic', () => {
  assert.match(debts, /ActiveNafez: 'نشط في نافذ'/);
  assert.match(debts, /promissoryStatusLabel/);
});

test('DataTable footer formats money via formatDataTableFooterTotal', () => {
  assert.match(table, /formatDataTableFooterTotal\(/);
  assert.doesNotMatch(table, /col\.money \|\| col\.numeric/);
});

test('Vouchers secret missing message is clean Arabic without config key', () => {
  assert.match(vouchers, /أكمل إعداد مفتاح توقيع السندات المالية قبل إنشاء مستند مالي/);
  assert.doesNotMatch(vouchers, /Vouchers:Secret/);
  assert.doesNotMatch(vouchers, /إعدادVouchers/);
});
