import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const workspace = readFileSync(
  path.join(root, 'src/features/payments/RecurringPayeesWorkspace.tsx'),
  'utf8'
);
const api = readFileSync(path.join(root, 'src/lib/api.ts'), 'utf8');

test('Row2_executeKeepsVouchersAndWiresDualApproveAndPay', () => {
  assert.match(workspace, /vouchers/);
  assert.match(workspace, /decideApproval/);
  assert.match(api, /payPaymentVoucher|vouchers\/\$\{.*\}\/pay/);
  assert.match(workspace, /payPaymentVoucher|\/payments\/vouchers\//);
  assert.match(workspace, /approvalRequestId/);
});

test('Row4_editOmitsBankDetailsUnlessExplicitClear', () => {
  assert.match(workspace, /clearBankDetails/);
  assert.match(workspace, /bankDetailsTouched/);
  assert.match(workspace, /مسح تفاصيل التحويل/);
  assert.match(workspace, /if \(form\.bankDetailsTouched\) body\.bankDetails/);
});

test('Row7_runAndProfileExposeAmountFeeFxOneOffAndCostCentre', () => {
  assert.match(workspace, /costCentreId/);
  assert.match(workspace, /fxDifference/);
  assert.match(workspace, /transferFee/);
  assert.match(workspace, /إضافة بند/);
  assert.match(workspace, /oneOffLine/);
  assert.match(workspace, /patchLine\(r,\s*\{\s*amount:/);
});

test('Row8_oneOffSendsStableIdempotencyKey', () => {
  assert.match(workspace, /idempotencyKey/);
  assert.match(workspace, /crypto\.randomUUID|randomUUID/);
});
