import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const read = (rel) => readFileSync(path.join(root, rel), 'utf8');

const payrollLabels = read('src/lib/payroll-labels.ts');
const admin = read('src/app/admin/page.tsx');
const payroll = read('src/app/payroll/page.tsx');
const header = read('src/components/layout/Header.tsx');
const columnFilterTs = read('src/lib/columnFilter.ts');
const columnFilterMenu = read('src/components/ui/ColumnFilterMenu.tsx');
const riderForm = read('../RiderForm/src/rider-form.ts');
const companies = read('src/app/companies/page.tsx');
const responses = read('src/features/forms/ResponsesTab.tsx');
const statement = read('src/app/riders/statement/page.tsx');
const accounting = read('src/app/accounting/page.tsx');
const workforce = read('src/app/workforce/page.tsx');
const manualJournals = read('src/features/accounting/ManualJournalsPanel.tsx');
const types = read('src/lib/types.ts');

test('payroll-labels maps blocker codes to Arabic', () => {
  assert.match(payrollLabels, /invoice_missing/);
  assert.match(payrollLabels, /export function payrollBlockerLabelAr/);
  assert.match(payrollLabels, /فاتورة المنصة غير مرفوعة/);
  assert.match(payrollLabels, /ValidateRiders/);
  assert.match(payrollLabels, /export function orgOperationLabelAr/);
});

test('admin routing UI avoids raw advance.review in user-facing strings', () => {
  assert.doesNotMatch(admin, /مراجعة السلف \(advance\.review\)/);
  assert.doesNotMatch(admin, /\(Routing\)/);
  assert.doesNotMatch(admin, /placeholder="e\.g\. advance/);
  assert.match(admin, /orgOperationLabelAr/);
  assert.match(admin, /تصدير إكسل/);
  assert.match(admin, /auditFilter\.rows/);
});

test('admin and rider statement export via api.exportExcel with Arabic headers', () => {
  assert.match(admin, /api\.exportExcel\(/);
  assert.match(admin, /اسم المستخدم/);
  assert.match(admin, /الدور/);
  assert.match(admin, /القسم/);
  assert.match(admin, /الحالة/);
  assert.match(admin, /المعرف/);
  assert.match(admin, /نوع العملية/);
  assert.match(admin, /المنفذ/);
  assert.match(admin, /التاريخ والوقت/);
  assert.match(admin, /auditActorLabelAr/);
  assert.doesNotMatch(admin, /\['username', 'role', 'department', 'status'\]/);
  assert.doesNotMatch(admin, /\['id', 'action', 'actor', 'at'\]/);
  assert.match(statement, /api\.exportExcel\(/);
  assert.match(statement, /تصدير إكسل/);
  assert.doesNotMatch(statement, /تصدير CSV/);
});

test('payroll uses blocker label helper and scopes ninja import CTA', () => {
  assert.match(payroll, /payrollBlockerLabelAr/);
  assert.match(payroll, /selectedPlatform === 'ninja'/);
  assert.doesNotMatch(payroll, /مسودة المراجعة المقترحة \(Review Draft\)/);
  assert.match(payroll, /bg-\[var\(--danger\)\] text-white/);
});

test('column filter empty value and Enter placeholder Arabic', () => {
  assert.match(columnFilterMenu, /اضغط إدخال للتطبيق/);
  assert.match(columnFilterTs, /\(بدون قيمة\)/);
  assert.match(columnFilterMenu, /اختر أو اكتب/);
});

test('header breadcrumbs and role Arabic', () => {
  assert.match(header, /التشغيل والأسطول/);
  assert.match(header, /القوى العاملة/);
  assert.match(header, /roleNameAr/);
  assert.match(header, /تطبيق المناديب والنماذج/);
});

test('rider form iqama placeholder spaced', () => {
  assert.match(riderForm, /رقم الإقامة من 10 أرقام/);
});

test('companies single import entry in page header actions', () => {
  const importPrimary = (companies.match(/primaryAction=\{[\s\S]*?label: 'استيراد/g) || []).length;
  assert.ok(importPrimary <= 1, 'duplicate primary import action');
  assert.match(companies, /testId: 'import-companies'/);
});

test('responses tab disables duplicate DataTable export', () => {
  assert.match(responses, /exportable=\{false\}/);
});

test('rider statement Excel label and timeline title Arabic', () => {
  assert.match(statement, /تصدير إكسل/);
  assert.doesNotMatch(statement, /Timeline Table/);
  assert.match(statement, /مدين: /);
  assert.match(statement, /دائن: /);
});

test('accounting dynamic coa count and Arabic excel labels', () => {
  assert.doesNotMatch(accounting, /\|\| 403/);
  assert.match(accounting, /إكسل 2024/);
  assert.match(accounting, /تصدير إكسل/);
  assert.match(manualJournals, /shortAuditId/);
});

test('workforce phone bdi and legal live connection notice', () => {
  assert.match(workforce, /<bdi dir="ltr">\{r\.phone/);
  assert.match(workforce, /يحتاج اتصال حي/);
});

test('UserProfile includes roleNameAr from ApiMe contract', () => {
  assert.match(types, /roleNameAr\?: ApiMe\['roleNameAr'\]/);
  assert.doesNotMatch(types, /roleNameAr\?: string;/);
});

test('admin mount loads org audit responsible only with matching permission', () => {
  assert.match(admin, /if \(canManageOrg\) \{[\s\S]*?void loadOrgData\(\)/);
  assert.match(admin, /if \(canViewAudit\) \{[\s\S]*?void loadAudit\(1\)/);
  assert.match(admin, /if \(canManageOrg\) \{[\s\S]*?void loadResponsible\(/);
  assert.match(admin, /const canViewAudit = can\('audit\.view'\) \|\| isAdmin/);
});
